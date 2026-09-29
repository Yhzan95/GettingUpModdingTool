using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using GettingUpModTool.Core.Models;

namespace GettingUpModTool.Core.Formats.MSH;
public static class MshMultiSectionReader
{
    public static MeshData Read(
        byte[] data,
        IReadOnlyList<MshSectionInfo> sections,
        IReadOnlySet<int>? flipVSections = null,
        IReadOnlySet<int>? wrapVSections = null)
    {
        if (sections.Count == 0)
            throw new InvalidDataException("Aucune section MSH à assembler.");

        var mesh = new MeshData();

        foreach (MshSectionInfo section in sections.OrderBy(x => x.VertexOffset))
        {
            int indexStart = mesh.Indices.Count;
            IReadOnlyList<MshDrawBatchInfo> batches = section.DrawBatches.Count > 0
                ? section.DrawBatches
                : [CreateFallbackBatch(section)];

            foreach (MshDrawBatchInfo batch in batches)
                ReadBatch(
                    data,
                    section,
                    batch,
                    mesh,
                    flipVSections?.Contains(section.Index) == true,
                    wrapVSections?.Contains(section.Index) == true);

            mesh.Parts.Add(new MeshPart
            {
                SectionIndex = section.Index,
                IndexStart = indexStart,
                IndexCount = mesh.Indices.Count - indexStart,
                RenderGroupName = "Base",
                MaterialName = $"Section {section.Index}",
                IsVisible = true
            });
        }

        return mesh;
    }

    private static MshDrawBatchInfo CreateFallbackBatch(MshSectionInfo section)
        => new()
        {
            BatchIndex = 0,
            HeaderOffset = section.FooterOffset,
            PaletteCount = section.PaletteCount,
            BonePalette = section.BonePalette,
            IndexCount = section.IndexCount,
            IndexOffset = section.IndexOffset,
            PrimitiveMode = section.PrimitiveMode,
            TriangleCount = section.TriangleCount,
            UniqueIndexedVertices = section.UniqueIndexedVertices,
            MinIndex = 0,
            MaxIndex = section.MaxIndex
        };

    private static void ReadBatch(
        byte[] data,
        MshSectionInfo section,
        MshDrawBatchInfo batch,
        MeshData mesh,
        bool flipV,
        bool wrapV)
    {
        var rawIndices = new List<int>(batch.IndexCount);
        for (int i = 0; i < batch.IndexCount; i++)
            rawIndices.Add(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(batch.IndexOffset + i * 2, 2)));

        
        
        var vertexMap = new Dictionary<int, int>();
        var remapped = new List<int>(rawIndices.Count);
        foreach (int sourceVertex in rawIndices)
        {
            if (!vertexMap.TryGetValue(sourceVertex, out int destinationVertex))
            {
                destinationVertex = mesh.Positions.Count;
                vertexMap[sourceVertex] = destinationVertex;
                if (section.IsSkinned)
                    ReadVertex(data, section, batch.BonePalette, sourceVertex, mesh, flipV, wrapV);
                else
                    ReadStaticVertex(data, section, sourceVertex, mesh, flipV, wrapV);
            }
            remapped.Add(destinationVertex);
        }

        IEnumerable<int> triangles = batch.PrimitiveMode == PrimitiveMode.TriangleStrip
            ? MshReader.StripToTriangles(remapped)
            : remapped.Take(remapped.Count - remapped.Count % 3);

        mesh.Indices.AddRange(triangles);
    }

    private static void ReadVertex(
        byte[] data,
        MshSectionInfo section,
        IReadOnlyList<ushort> palette,
        int sourceVertex,
        MeshData mesh,
        bool flipV,
        bool wrapV)
    {
        if (sourceVertex < 0 || sourceVertex >= section.VertexCount)
            throw new InvalidDataException($"Vertex {sourceVertex} hors section {section.Index} ({section.VertexCount}).");

        int p = section.VertexOffset + sourceVertex * section.VertexStride;
        mesh.Positions.Add(new Vector3(
            ReadF32(data, p),
            ReadF32(data, p + 4),
            ReadF32(data, p + 8)));

        Vector3 normal = new(
            DecodeUnormNormal(data[p + section.NormalOffset]),
            DecodeUnormNormal(data[p + section.NormalOffset + 1]),
            DecodeUnormNormal(data[p + section.NormalOffset + 2]));
        if (normal.LengthSquared() > 0.000001f)
            normal = Vector3.Normalize(normal);
        mesh.Normals.Add(normal);

        float u = ReadF32(data, p + section.UvOffset);
        float v = ReadF32(data, p + section.UvOffset + 4);
        v = TransformV(v, flipV, wrapV);
        mesh.UVs.Add(new Vector2(u, v));

        int w = p + section.WeightOffset;
        int b = p + section.BoneIndicesOffset;
        float w0 = data[w] / 255f;
        float w1 = data[w + 1] / 255f;
        float w2 = data[w + 2] / 255f;
        float w3 = data[w + 3] / 255f;
        float sum = w0 + w1 + w2 + w3;
        if (sum > 0.000001f)
        {
            w0 /= sum;
            w1 /= sum;
            w2 /= sum;
            w3 /= sum;
        }

        int j0 = RemapJoint(data[b], palette);
        int j1 = RemapJoint(data[b + 1], palette);
        int j2 = RemapJoint(data[b + 2], palette);
        int j3 = RemapJoint(data[b + 3], palette);
        mesh.Skinning.Add(new VertexSkin(j0, j1, j2, j3, w0, w1, w2, w3));
    }

    private static void ReadStaticVertex(
        byte[] data,
        MshSectionInfo section,
        int sourceVertex,
        MeshData mesh,
        bool flipV,
        bool wrapV)
    {
        if (sourceVertex < 0 || sourceVertex >= section.VertexCount)
            throw new InvalidDataException($"Vertex {sourceVertex} hors section statique {section.Index} ({section.VertexCount}).");

        int p = section.VertexOffset + sourceVertex * section.VertexStride;
        mesh.Positions.Add(new Vector3(
            ReadF32(data, p),
            ReadF32(data, p + 4),
            ReadF32(data, p + 8)));

        Vector3 normal = new(
            DecodeUnormNormal(data[p + section.NormalOffset]),
            DecodeUnormNormal(data[p + section.NormalOffset + 1]),
            DecodeUnormNormal(data[p + section.NormalOffset + 2]));
        if (normal.LengthSquared() > 0.000001f)
            normal = Vector3.Normalize(normal);
        mesh.Normals.Add(normal);

        float u = ReadF32(data, p + section.UvOffset);
        float v = ReadF32(data, p + section.UvOffset + 4);
        v = TransformV(v, flipV, wrapV);
        mesh.UVs.Add(new Vector2(u, v));
    }

    private static float TransformV(float v, bool flipV, bool wrapV)
    {
        
        
        
        
        if (wrapV && float.IsFinite(v))
            v -= MathF.Floor(v);
        if (flipV)
            v = 1f - v;
        return v;
    }

    private static int RemapJoint(byte encoded, IReadOnlyList<ushort> palette)
    {
        if (encoded % 3 != 0)
            return -1;
        int localSlot = encoded / 3;
        if (localSlot < 0 || localSlot >= palette.Count)
            return -1;
        return palette[localSlot];
    }

    private static float DecodeUnormNormal(byte value)
        => value / 255f * 2f - 1f;

    private static float ReadF32(byte[] data, int offset)
        => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4)));
}
