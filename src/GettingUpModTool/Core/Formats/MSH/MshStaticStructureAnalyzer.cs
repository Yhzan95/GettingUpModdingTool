using System.Buffers.Binary;
using System.Numerics;
using GettingUpModTool.Core.Models;

namespace GettingUpModTool.Core.Formats.MSH;

public static class MshStaticStructureAnalyzer
{
    private static readonly HashSet<int> SupportedStrides = [28, 36];

    private sealed class Candidate
    {
        public int CountOffset { get; init; }
        public int VertexOffset { get; init; }
        public int VertexCount { get; init; }
        public int VertexStride { get; init; }
        public int FooterOffset { get; init; }
        public int FooterMarker { get; init; }
        public int FooterGroupValue { get; init; }
        public int IndexCount { get; init; }
        public int IndexOffset { get; init; }
        public PrimitiveMode PrimitiveMode { get; init; }
        public int TriangleCount { get; init; }
        public int UniqueIndexedVertices { get; init; }
        public int MaxIndex { get; init; }
        public double VertexScore { get; init; }
        public double IndexCoverage { get; init; }
        public Vector3 BoundsMin { get; init; }
        public Vector3 BoundsMax { get; init; }
    }

    public static MshStructureAnalysis Analyze(byte[] data)
    {
        var analysis = new MshStructureAnalysis
        {
            FileSize = data.Length,
            SkeletonBoneCount = 0
        };

        var candidates = new List<Candidate>();

        
        
        for (int descriptorOffset = 0; descriptorOffset <= data.Length - 6; descriptorOffset++)
        {
            int descriptorKind = data[descriptorOffset];
            int stride = data[descriptorOffset + 1];
            if (descriptorKind < 1 || descriptorKind > 16 || !SupportedStrides.Contains(stride))
                continue;

            int countOffset = descriptorOffset + 2;
            uint rawVertexCount = ReadU32(data, countOffset);
            if (rawVertexCount < 3 || rawVertexCount > 65_535)
                continue;

            int vertexCount = (int)rawVertexCount;
            int vertexOffset = countOffset + 4;
            long footer64 = (long)vertexOffset + (long)vertexCount * stride;
            if (footer64 < 0 || footer64 + 24 > data.Length)
                continue;
            int footer = (int)footer64;

            uint marker = ReadU32(data, footer + 0);
            uint group = ReadU32(data, footer + 4);
            uint staticA = ReadU32(data, footer + 8);
            uint staticB = ReadU32(data, footer + 12);
            if (marker < 1 || marker > 16 || group < 1 || group > 16 || staticA != 0 || staticB != 0)
                continue;

            uint indexCountA = ReadU32(data, footer + 16);
            uint indexCountB = ReadU32(data, footer + 20);
            if (indexCountA != indexCountB || indexCountA < 3 || indexCountA > 1_000_000)
                continue;

            int indexCount = (int)indexCountA;
            int indexOffset = footer + 24;
            if ((long)indexOffset + indexCount * 2L > data.Length)
                continue;

            var indices = new int[indexCount];
            int maxIndex = -1;
            int adjacentDuplicates = 0;
            int previous = -1;
            var used = new HashSet<int>();
            bool indexOk = true;
            for (int i = 0; i < indexCount; i++)
            {
                int idx = ReadU16(data, indexOffset + i * 2);
                indices[i] = idx;
                if (idx < 0 || idx >= vertexCount)
                {
                    indexOk = false;
                    break;
                }
                used.Add(idx);
                maxIndex = Math.Max(maxIndex, idx);
                if (i > 0 && idx == previous)
                    adjacentDuplicates++;
                previous = idx;
            }
            if (!indexOk)
                continue;

            int samples = Math.Min(vertexCount, 96);
            int positionOk = 0;
            int uvOk = 0;
            int normalOk = 0;
            Vector3 min = new(float.PositiveInfinity);
            Vector3 max = new(float.NegativeInfinity);

            for (int s = 0; s < samples; s++)
            {
                int vi = samples == vertexCount
                    ? s
                    : (int)((long)s * (vertexCount - 1) / Math.Max(1, samples - 1));
                int p = vertexOffset + vi * stride;

                float x = ReadF32(data, p + 0);
                float y = ReadF32(data, p + 4);
                float z = ReadF32(data, p + 8);
                if (IsPlausiblePosition(x) && IsPlausiblePosition(y) && IsPlausiblePosition(z))
                {
                    positionOk++;
                    min = Vector3.Min(min, new Vector3(x, y, z));
                    max = Vector3.Max(max, new Vector3(x, y, z));
                }

                
                if (data[p + 15] == 0)
                    normalOk++;

                float u = ReadF32(data, p + 16);
                float v = ReadF32(data, p + 20);
                if (float.IsFinite(u) && float.IsFinite(v) && u >= -100f && u <= 100f && v >= -100f && v <= 100f)
                    uvOk++;
            }

            double n = Math.Max(1, samples);
            double posRatio = positionOk / n;
            double uvRatio = uvOk / n;
            double normalRatio = normalOk / n;
            if (posRatio < 0.98 || uvRatio < 0.80)
                continue;

            double vertexScore = posRatio * 0.55 + uvRatio * 0.30 + normalRatio * 0.15;
            double adjacentRate = indexCount <= 1 ? 0 : (double)adjacentDuplicates / (indexCount - 1);
            PrimitiveMode primitive = adjacentRate >= 0.03 || indexCount % 3 != 0
                ? PrimitiveMode.TriangleStrip
                : PrimitiveMode.TriangleList;
            int triangleCount = primitive == PrimitiveMode.TriangleStrip
                ? CountStripTriangles(indices)
                : indexCount / 3;

            candidates.Add(new Candidate
            {
                CountOffset = countOffset,
                VertexOffset = vertexOffset,
                VertexCount = vertexCount,
                VertexStride = stride,
                FooterOffset = footer,
                FooterMarker = (int)marker,
                FooterGroupValue = (int)group,
                IndexCount = indexCount,
                IndexOffset = indexOffset,
                PrimitiveMode = primitive,
                TriangleCount = triangleCount,
                UniqueIndexedVertices = used.Count,
                MaxIndex = maxIndex,
                VertexScore = vertexScore,
                IndexCoverage = vertexCount == 0 ? 0 : used.Count / (double)vertexCount,
                BoundsMin = min,
                BoundsMax = max
            });
        }

        
        
        List<Candidate> deduped = candidates
            .GroupBy(x => x.IndexOffset)
            .Select(g => g
                .OrderByDescending(x => x.VertexScore)
                .ThenByDescending(x => x.VertexCount)
                .First())
            .OrderBy(x => x.VertexOffset)
            .ToList();

        for (int i = 0; i < deduped.Count; i++)
        {
            Candidate c = deduped[i];
            var section = new MshSectionInfo
            {
                Index = i,
                IsSkinned = false,
                CountOffset = c.CountOffset,
                VertexOffset = c.VertexOffset,
                VertexCount = c.VertexCount,
                VertexStride = c.VertexStride,
                NormalOffset = 12,
                UvOffset = 16,
                WeightOffset = -1,
                BoneIndicesOffset = -1,
                FooterOffset = c.FooterOffset,
                FooterMarker = c.FooterMarker,
                FooterGroupValue = c.FooterGroupValue,
                FooterBoneReferenceValue = 0,
                PaletteCount = 0,
                BonePalette = Array.Empty<ushort>(),
                IndexCount = c.IndexCount,
                IndexOffset = c.IndexOffset,
                PrimitiveMode = c.PrimitiveMode,
                TriangleCount = c.TriangleCount,
                UniqueIndexedVertices = c.UniqueIndexedVertices,
                MaxIndex = c.MaxIndex,
                MaxLocalBoneSlot = -1,
                VertexScore = c.VertexScore,
                IndexCoverage = c.IndexCoverage,
                BoundsMin = c.BoundsMin,
                BoundsMax = c.BoundsMax,
                CombinedUniqueIndexedVertices = c.UniqueIndexedVertices,
                CombinedIndexCoverage = c.IndexCoverage,
                DrawBatchParseComplete = true
            };
            section.DrawBatches.Add(new MshDrawBatchInfo
            {
                BatchIndex = 0,
                HeaderOffset = c.FooterOffset,
                PaletteCount = 0,
                BonePalette = Array.Empty<ushort>(),
                IndexCount = c.IndexCount,
                IndexOffset = c.IndexOffset,
                PrimitiveMode = c.PrimitiveMode,
                TriangleCount = c.TriangleCount,
                UniqueIndexedVertices = c.UniqueIndexedVertices,
                MinIndex = 0,
                MaxIndex = c.MaxIndex,
                LegacyContinuationLayout = false
            });
            analysis.Sections.Add(section);
        }

        if (analysis.Sections.Count > 0)
        {
            analysis.Issues.Add(new MshDiagnosticIssue
            {
                Severity = MshDiagnosticSeverity.Info,
                Code = "STATIC_PROP_LAYOUT",
                Message = $"Mesh statique détecté : {analysis.Sections.Count} section(s), stride(s) {string.Join(", ", analysis.Sections.Select(x => x.VertexStride).Distinct().OrderBy(x => x))}."
            });
        }

        return analysis;
    }

    private static int CountStripTriangles(IReadOnlyList<int> strip)
    {
        int count = 0;
        for (int i = 2; i < strip.Count; i++)
        {
            int a = strip[i - 2];
            int b = strip[i - 1];
            int c = strip[i];
            if (a != b && b != c && a != c)
                count++;
        }
        return count;
    }

    private static bool IsPlausiblePosition(float value)
        => float.IsFinite(value) && MathF.Abs(value) <= 100_000f;

    private static uint ReadU32(byte[] data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));

    private static ushort ReadU16(byte[] data, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));

    private static float ReadF32(byte[] data, int offset)
        => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4)));
}
