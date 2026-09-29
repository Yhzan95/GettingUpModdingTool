using System.IO;
using System.Buffers.Binary;
using System.Numerics;
using GettingUpModTool.Core.Models;

namespace GettingUpModTool.Core.Formats.MSH;

public static class MshReader
{
    public static MeshData Read(string path, MshLayout layout)
        => Read(File.ReadAllBytes(path), layout);

    public static MeshData Read(byte[] data, MshLayout layout)
    {
        Validate(data, layout);
        var mesh = new MeshData();

        int maxVertices = Math.Min(layout.VertexCount,
            (data.Length - layout.VertexOffset) / layout.VertexStride);

        for (int i = 0; i < maxVertices; i++)
        {
            int baseOffset = layout.VertexOffset + i * layout.VertexStride;
            int p = baseOffset + layout.PositionOffset;
            mesh.Positions.Add(new Vector3(
                ReadFloat(data, p),
                ReadFloat(data, p + 4),
                ReadFloat(data, p + 8)));

            if (layout.NormalOffset is int nOff)
            {
                int n = baseOffset + nOff;
                if (layout.NormalEncoding == NormalEncoding.PackedUnorm8 && n + 4 <= data.Length)
                {
                    Vector3 normal = new(
                        DecodeUnormNormal(data[n]),
                        DecodeUnormNormal(data[n + 1]),
                        DecodeUnormNormal(data[n + 2]));
                    if (normal.LengthSquared() > 0.000001f)
                        normal = Vector3.Normalize(normal);
                    mesh.Normals.Add(normal);
                }
                else if (layout.NormalEncoding == NormalEncoding.Float3 && n + 12 <= data.Length)
                {
                    mesh.Normals.Add(new Vector3(
                        ReadFloat(data, n),
                        ReadFloat(data, n + 4),
                        ReadFloat(data, n + 8)));
                }
            }

            if (layout.UVOffset is int uvOff && baseOffset + uvOff + 8 <= data.Length)
            {
                int uv = baseOffset + uvOff;
                mesh.UVs.Add(new Vector2(ReadFloat(data, uv), ReadFloat(data, uv + 4)));
            }

            if (layout.BoneWeightsOffset is int wOff &&
                layout.BoneIndicesOffset is int bOff &&
                baseOffset + wOff + 4 <= data.Length &&
                baseOffset + bOff + 4 <= data.Length)
            {
                int w = baseOffset + wOff;
                int b = baseOffset + bOff;
                int divisor = Math.Max(1, layout.BoneIndexDivisor);

                float w0 = data[w] / 255f;
                float w1 = data[w + 1] / 255f;
                float w2 = data[w + 2] / 255f;
                float w3 = data[w + 3] / 255f;
                float sum = w0 + w1 + w2 + w3;
                if (sum > 0.000001f)
                {
                    w0 /= sum; w1 /= sum; w2 /= sum; w3 /= sum;
                }

                mesh.Skinning.Add(new VertexSkin(
                    data[b] / divisor,
                    data[b + 1] / divisor,
                    data[b + 2] / divisor,
                    data[b + 3] / divisor,
                    w0, w1, w2, w3));
            }
        }

        if (layout.IndexOffset is int indexOffset && layout.IndexCount > 0)
        {
            var raw = ReadIndices(data, indexOffset, layout.IndexCount, layout.IndexSize);
            if (layout.PrimitiveMode == PrimitiveMode.TriangleStrip)
                mesh.Indices.AddRange(StripToTriangles(raw));
            else
                mesh.Indices.AddRange(raw.Take(raw.Count - raw.Count % 3));
        }

        return mesh;
    }

    private static void Validate(byte[] data, MshLayout layout)
    {
        if (layout.VertexOffset < 0 || layout.VertexOffset >= data.Length)
            throw new ArgumentOutOfRangeException(nameof(layout.VertexOffset));
        if (layout.VertexCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(layout.VertexCount));
        if (layout.VertexStride < 12)
            throw new ArgumentOutOfRangeException(nameof(layout.VertexStride), "Stride must be >= 12.");
        if (layout.PositionOffset < 0 || layout.PositionOffset + 12 > layout.VertexStride)
            throw new ArgumentOutOfRangeException(nameof(layout.PositionOffset));

        if (layout.NormalOffset is int normalOffset)
        {
            int size = layout.NormalEncoding == NormalEncoding.PackedUnorm8 ? 4 : 12;
            if (normalOffset < 0 || normalOffset + size > layout.VertexStride)
                throw new ArgumentOutOfRangeException(nameof(layout.NormalOffset));
        }

        if (layout.UVOffset is int uvOffset && (uvOffset < 0 || uvOffset + 8 > layout.VertexStride))
            throw new ArgumentOutOfRangeException(nameof(layout.UVOffset));
        if (layout.BoneWeightsOffset is int weightsOffset && (weightsOffset < 0 || weightsOffset + 4 > layout.VertexStride))
            throw new ArgumentOutOfRangeException(nameof(layout.BoneWeightsOffset));
        if (layout.BoneIndicesOffset is int bonesOffset && (bonesOffset < 0 || bonesOffset + 4 > layout.VertexStride))
            throw new ArgumentOutOfRangeException(nameof(layout.BoneIndicesOffset));
    }

    private static float ReadFloat(byte[] data, int offset)
    {
        if (offset < 0 || offset + 4 > data.Length)
            return float.NaN;
        int raw = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
        return BitConverter.Int32BitsToSingle(raw);
    }

    private static float DecodeUnormNormal(byte value)
        => value / 255f * 2f - 1f;

    private static List<int> ReadIndices(byte[] data, int offset, int count, IndexElementSize size)
    {
        if (offset < 0 || offset >= data.Length)
            return [];

        var result = new List<int>(count);
        int elementSize = (int)size;
        int max = Math.Min(count, (data.Length - offset) / elementSize);

        for (int i = 0; i < max; i++)
        {
            int p = offset + i * elementSize;
            result.Add(size == IndexElementSize.UInt16
                ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p, 2))
                : checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p, 4))));
        }

        return result;
    }

    public static IEnumerable<int> StripToTriangles(IReadOnlyList<int> strip)
    {
        for (int i = 2; i < strip.Count; i++)
        {
            int a = strip[i - 2];
            int b = strip[i - 1];
            int c = strip[i];
            if (a == b || b == c || a == c)
                continue;

            if ((i & 1) == 0)
            {
                yield return a; yield return b; yield return c;
            }
            else
            {
                yield return b; yield return a; yield return c;
            }
        }
    }
}
