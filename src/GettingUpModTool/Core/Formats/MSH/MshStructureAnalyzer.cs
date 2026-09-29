using System.Buffers.Binary;
using System.Numerics;
using GettingUpModTool.Core.Models;

namespace GettingUpModTool.Core.Formats.MSH;

public enum MshDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed class MshDiagnosticIssue
{
    public MshDiagnosticSeverity Severity { get; init; }
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
    public string SeverityLabel => Severity switch
    {
        MshDiagnosticSeverity.Error => "ERREUR",
        MshDiagnosticSeverity.Warning => "ATTENTION",
        _ => "INFO"
    };
}

public sealed class MshDrawBatchInfo
{
    public int BatchIndex { get; init; }
    public int HeaderOffset { get; init; }
    public int PaletteCount { get; init; }
    public ushort[] BonePalette { get; init; } = Array.Empty<ushort>();
    public int IndexCount { get; init; }
    public int IndexOffset { get; init; }
    public PrimitiveMode PrimitiveMode { get; init; }
    public int TriangleCount { get; init; }
    public int UniqueIndexedVertices { get; init; }
    public int MinIndex { get; init; }
    public int MaxIndex { get; init; }
    public bool LegacyContinuationLayout { get; init; }

    public string IndexOffsetHex => $"0x{IndexOffset:X}";
    public string PrimitiveLabel => PrimitiveMode.ToString();
}

public sealed class MshSectionInfo
{
    public int Index { get; init; }
    public bool IsSkinned { get; init; } = true;
    public int CountOffset { get; init; }
    public int VertexOffset { get; init; }
    public int VertexCount { get; init; }
    public int VertexStride { get; init; }
    public int NormalOffset { get; init; } = 12;
    public int UvOffset { get; init; } = 16;
    public int WeightOffset { get; init; }
    public int BoneIndicesOffset { get; init; }
    public int FooterOffset { get; init; }
    public int FooterMarker { get; init; }
    public int FooterGroupValue { get; init; }
    public int FooterBoneReferenceValue { get; init; }
    public int PaletteCount { get; init; }
    public ushort[] BonePalette { get; init; } = Array.Empty<ushort>();
    public int IndexCount { get; init; }
    public int IndexOffset { get; init; }
    public PrimitiveMode PrimitiveMode { get; init; }
    public int TriangleCount { get; init; }
    public int UniqueIndexedVertices { get; init; }
    public int MaxIndex { get; init; }
    public int MaxLocalBoneSlot { get; init; }
    public double VertexScore { get; init; }
    public double IndexCoverage { get; init; }
    public Vector3 BoundsMin { get; init; }
    public Vector3 BoundsMax { get; init; }
    public List<MshDrawBatchInfo> DrawBatches { get; } = [];
    public int CombinedUniqueIndexedVertices { get; set; }
    public double CombinedIndexCoverage { get; set; }
    public bool DrawBatchParseComplete { get; set; } = true;

    public int DrawBatchCount => Math.Max(1, DrawBatches.Count);
    public int TotalIndexCount => DrawBatches.Count == 0 ? IndexCount : DrawBatches.Sum(x => x.IndexCount);
    public int TotalTriangleCount => DrawBatches.Count == 0 ? TriangleCount : DrawBatches.Sum(x => x.TriangleCount);

    public string VertexOffsetHex => $"0x{VertexOffset:X}";
    public string FooterOffsetHex => $"0x{FooterOffset:X}";
    public string IndexOffsetHex => $"0x{IndexOffset:X}";
    public string FormatLabel => $"stride {VertexStride} / marker {FooterMarker}";
    public string PrimitiveLabel => PrimitiveMode.ToString();
    public string CoverageLabel => $"{IndexCoverage:P0}";
    public string CombinedCoverageLabel => $"{CombinedIndexCoverage:P0}";
    public string PaletteLabel => BonePalette.Length == 0
        ? "-"
        : string.Join(",", BonePalette.Take(24)) + (BonePalette.Length > 24 ? ",…" : "");
}

public sealed class MshStructureAnalysis
{
    public List<MshSectionInfo> Sections { get; } = [];
    public List<MshDiagnosticIssue> Issues { get; } = [];
    public int FileSize { get; init; }
    public int SkeletonBoneCount { get; init; }

    public int TotalVertices => Sections.Sum(x => x.VertexCount);
    public int TotalIndices => Sections.Sum(x => x.TotalIndexCount);
    public int TotalTriangles => Sections.Sum(x => x.TotalTriangleCount);
    public int TotalDrawBatches => Sections.Sum(x => x.DrawBatchCount);
    public int ErrorCount => Issues.Count(x => x.Severity == MshDiagnosticSeverity.Error);
    public int WarningCount => Issues.Count(x => x.Severity == MshDiagnosticSeverity.Warning);
    public bool CanAssemble => Sections.Count > 0 && ErrorCount == 0;
}

public static class MshStructureAnalyzer
{
    private static readonly int[] SupportedStrides = [36, 44];

    public static MshStructureAnalysis Analyze(byte[] data, SkeletonData? skeleton = null)
    {
        var analysis = new MshStructureAnalysis
        {
            FileSize = data.Length,
            SkeletonBoneCount = skeleton?.Bones.Count ?? 0
        };

        var candidates = new List<MshSectionInfo>();
        int maxCountOffset = Math.Max(0, data.Length - 4);

        for (int countOffset = 0; countOffset < maxCountOffset; countOffset++)
        {
            uint rawCount = ReadU32(data, countOffset);
            if (rawCount < 3 || rawCount > 50_000)
                continue;

            int vertexCount = (int)rawCount;
            int vertexOffset = countOffset + 4;

            foreach (int stride in SupportedStrides)
            {
                MshSectionInfo? section = TryParseSection(data, countOffset, vertexOffset, vertexCount, stride, skeleton);
                if (section is not null)
                    candidates.Add(section);
            }
        }

        
        
        var deduped = candidates
            .GroupBy(x => x.IndexOffset)
            .Select(g => g
                .OrderByDescending(x => x.VertexScore)
                .ThenByDescending(x => x.VertexCount)
                .First())
            .OrderBy(x => x.VertexOffset)
            .ToList();

        for (int i = 0; i < deduped.Count; i++)
        {
            MshSectionInfo s = deduped[i];
            var finalSection = new MshSectionInfo
            {
                Index = i,
                CountOffset = s.CountOffset,
                VertexOffset = s.VertexOffset,
                VertexCount = s.VertexCount,
                VertexStride = s.VertexStride,
                NormalOffset = s.NormalOffset,
                UvOffset = s.UvOffset,
                WeightOffset = s.WeightOffset,
                BoneIndicesOffset = s.BoneIndicesOffset,
                FooterOffset = s.FooterOffset,
                FooterMarker = s.FooterMarker,
                FooterGroupValue = s.FooterGroupValue,
                FooterBoneReferenceValue = s.FooterBoneReferenceValue,
                PaletteCount = s.PaletteCount,
                BonePalette = s.BonePalette,
                IndexCount = s.IndexCount,
                IndexOffset = s.IndexOffset,
                PrimitiveMode = s.PrimitiveMode,
                TriangleCount = s.TriangleCount,
                UniqueIndexedVertices = s.UniqueIndexedVertices,
                MaxIndex = s.MaxIndex,
                MaxLocalBoneSlot = s.MaxLocalBoneSlot,
                VertexScore = s.VertexScore,
                IndexCoverage = s.IndexCoverage,
                BoundsMin = s.BoundsMin,
                BoundsMax = s.BoundsMax
            };
            PopulateDrawBatches(data, finalSection);
            analysis.Sections.Add(finalSection);
        }

        BuildIssues(analysis, skeleton);
        return analysis;
    }

    private static MshSectionInfo? TryParseSection(
        byte[] data,
        int countOffset,
        int vertexOffset,
        int vertexCount,
        int stride,
        SkeletonData? skeleton)
    {
        long footer64 = (long)vertexOffset + (long)vertexCount * stride;
        if (footer64 < 0 || footer64 + 24 > data.Length)
            return null;
        int footer = (int)footer64;

        uint marker = ReadU32(data, footer + 0);
        uint groupValue = ReadU32(data, footer + 4);
        uint boneReferenceValue = ReadU32(data, footer + 8);
        uint rawPaletteCount = ReadU32(data, footer + 12);

        
        
        if (marker < 1 || marker > 16 || groupValue < 1 || groupValue > 16 ||
            boneReferenceValue < 1 || boneReferenceValue > 512 || rawPaletteCount < 1 || rawPaletteCount > 256)
            return null;

        int paletteCount = (int)rawPaletteCount;
        int paletteOffset = footer + 16;
        long indexHeader64 = (long)paletteOffset + paletteCount * 2L;
        if (indexHeader64 + 8 > data.Length)
            return null;
        int indexHeader = (int)indexHeader64;

        uint indexCountA = ReadU32(data, indexHeader);
        uint indexCountB = ReadU32(data, indexHeader + 4);
        if (indexCountA != indexCountB || indexCountA < 3 || indexCountA > 500_000)
            return null;

        int indexCount = (int)indexCountA;
        int indexOffset = indexHeader + 8;
        if ((long)indexOffset + indexCount * 2L > data.Length)
            return null;

        ushort[] palette = new ushort[paletteCount];
        for (int i = 0; i < paletteCount; i++)
            palette[i] = ReadU16(data, paletteOffset + i * 2);

        int validIndices = 0;
        int maxIndex = -1;
        int adjacentDuplicates = 0;
        int previous = -1;
        var used = new HashSet<int>();
        var rawIndices = new int[indexCount];
        for (int i = 0; i < indexCount; i++)
        {
            int idx = ReadU16(data, indexOffset + i * 2);
            rawIndices[i] = idx;
            if (idx >= 0 && idx < vertexCount)
            {
                validIndices++;
                used.Add(idx);
            }
            maxIndex = Math.Max(maxIndex, idx);
            if (i > 0 && idx == previous)
                adjacentDuplicates++;
            previous = idx;
        }
        if ((double)validIndices / indexCount < 0.999)
            return null;

        int weightOffset = stride == 44 ? 32 : 24;
        int bonesOffset = weightOffset + 4;
        int samples = Math.Min(vertexCount, 96);
        int positionOk = 0, uvOk = 0, normalOk = 0, weightOk = 0, bonesOk = 0;
        int maxLocalBoneSlot = -1;
        Vector3 min = new(float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity);

        for (int s = 0; s < samples; s++)
        {
            int vi = samples == vertexCount ? s : (int)((long)s * (vertexCount - 1) / Math.Max(1, samples - 1));
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
            if (float.IsFinite(u) && float.IsFinite(v) && u >= -10f && u <= 10f && v >= -10f && v <= 10f)
                uvOk++;

            int weightSum = data[p + weightOffset] + data[p + weightOffset + 1] +
                            data[p + weightOffset + 2] + data[p + weightOffset + 3];
            if (weightSum is >= 254 and <= 256)
                weightOk++;

            bool localBonesValid = true;
            for (int j = 0; j < 4; j++)
            {
                byte weight = data[p + weightOffset + j];
                byte encoded = data[p + bonesOffset + j];
                if (weight == 0)
                    continue;
                if (encoded % 3 != 0)
                {
                    localBonesValid = false;
                    break;
                }
                int localSlot = encoded / 3;
                maxLocalBoneSlot = Math.Max(maxLocalBoneSlot, localSlot);
                if (localSlot < 0 || localSlot >= paletteCount)
                {
                    localBonesValid = false;
                    break;
                }
                _ = palette[localSlot]; 
            }
            if (localBonesValid)
                bonesOk++;
        }

        double n = Math.Max(1, samples);
        double posRatio = positionOk / n;
        double uvRatio = uvOk / n;
        double normalRatio = normalOk / n;
        double weightRatio = weightOk / n;
        double boneRatio = bonesOk / n;

        if (posRatio < 0.98 || uvRatio < 0.80 || weightRatio < 0.90 || boneRatio < 0.90)
            return null;

        double vertexScore = posRatio * 0.25 + uvRatio * 0.15 + normalRatio * 0.10 + weightRatio * 0.25 + boneRatio * 0.25;
        double adjacentRate = indexCount <= 1 ? 0 : (double)adjacentDuplicates / (indexCount - 1);
        PrimitiveMode primitive = adjacentRate >= 0.03 || indexCount % 3 != 0
            ? PrimitiveMode.TriangleStrip
            : PrimitiveMode.TriangleList;
        int triangleCount = primitive == PrimitiveMode.TriangleStrip
            ? CountStripTriangles(rawIndices)
            : indexCount / 3;

        return new MshSectionInfo
        {
            CountOffset = countOffset,
            VertexOffset = vertexOffset,
            VertexCount = vertexCount,
            VertexStride = stride,
            WeightOffset = weightOffset,
            BoneIndicesOffset = bonesOffset,
            FooterOffset = footer,
            FooterMarker = (int)marker,
            FooterGroupValue = (int)groupValue,
            FooterBoneReferenceValue = (int)boneReferenceValue,
            PaletteCount = paletteCount,
            BonePalette = palette,
            IndexCount = indexCount,
            IndexOffset = indexOffset,
            PrimitiveMode = primitive,
            TriangleCount = triangleCount,
            UniqueIndexedVertices = used.Count,
            MaxIndex = maxIndex,
            MaxLocalBoneSlot = maxLocalBoneSlot,
            VertexScore = vertexScore,
            IndexCoverage = vertexCount == 0 ? 0 : (double)used.Count / vertexCount,
            BoundsMin = min,
            BoundsMax = max
        };
    }

    private static void PopulateDrawBatches(byte[] data, MshSectionInfo section)
    {
        section.DrawBatches.Clear();
        section.DrawBatches.Add(new MshDrawBatchInfo
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
        });

        int expected = Math.Max(1, section.FooterGroupValue);
        int cursor = section.IndexOffset + section.IndexCount * 2;
        for (int batchIndex = 1; batchIndex < expected; batchIndex++)
        {
            if (!TryParseContinuationBatch(data, cursor, section.VertexCount, batchIndex, out MshDrawBatchInfo? batch, out int nextOffset) || batch is null)
            {
                section.DrawBatchParseComplete = false;
                break;
            }

            section.DrawBatches.Add(batch);
            cursor = nextOffset;
        }

        var used = new HashSet<int>();
        foreach (MshDrawBatchInfo batch in section.DrawBatches)
        {
            for (int i = 0; i < batch.IndexCount; i++)
                used.Add(ReadU16(data, batch.IndexOffset + i * 2));
        }
        section.CombinedUniqueIndexedVertices = used.Count;
        section.CombinedIndexCoverage = section.VertexCount == 0 ? 0 : (double)used.Count / section.VertexCount;
    }

    private static bool TryParseContinuationBatch(
        byte[] data,
        int offset,
        int vertexCount,
        int batchIndex,
        out MshDrawBatchInfo? batch,
        out int nextOffset)
    {
        batch = null;
        nextOffset = offset;
        if (offset < 0 || offset + 24 > data.Length || ReadU32(data, offset) != 1 || ReadU32(data, offset + 4) != 1)
            return false;

        
        
        uint standardPaletteCount = ReadU32(data, offset + 20);
        if (standardPaletteCount is >= 1 and <= 256 &&
            TryBuildContinuationBatch(data, offset, offset + 24, (int)standardPaletteCount, vertexCount, batchIndex, false, out batch, out nextOffset))
            return true;

        
        
        int legacyPaletteCount = ReadU16(data, offset + 18);
        if (legacyPaletteCount is >= 1 and <= 256 && ReadU16(data, offset + 20) == 0 &&
            TryBuildContinuationBatch(data, offset, offset + 22, legacyPaletteCount, vertexCount, batchIndex, true, out batch, out nextOffset))
            return true;

        return false;
    }

    private static bool TryBuildContinuationBatch(
        byte[] data,
        int headerOffset,
        int paletteOffset,
        int paletteCount,
        int vertexCount,
        int batchIndex,
        bool legacy,
        out MshDrawBatchInfo? batch,
        out int nextOffset)
    {
        batch = null;
        nextOffset = headerOffset;
        long indexHeader64 = (long)paletteOffset + paletteCount * 2L;
        if (indexHeader64 + 8 > data.Length)
            return false;
        int indexHeader = (int)indexHeader64;

        uint countA = ReadU32(data, indexHeader);
        uint countB = ReadU32(data, indexHeader + 4);
        if (countA != countB || countA < 3 || countA > 500_000)
            return false;

        int indexCount = (int)countA;
        int indexOffset = indexHeader + 8;
        if ((long)indexOffset + indexCount * 2L > data.Length)
            return false;

        var indices = new int[indexCount];
        var used = new HashSet<int>();
        int minIndex = int.MaxValue;
        int maxIndex = -1;
        int adjacentDuplicates = 0;
        int previous = -1;
        for (int i = 0; i < indexCount; i++)
        {
            int idx = ReadU16(data, indexOffset + i * 2);
            if (idx < 0 || idx >= vertexCount)
                return false;
            indices[i] = idx;
            used.Add(idx);
            minIndex = Math.Min(minIndex, idx);
            maxIndex = Math.Max(maxIndex, idx);
            if (i > 0 && idx == previous)
                adjacentDuplicates++;
            previous = idx;
        }

        ushort[] palette = new ushort[paletteCount];
        for (int i = 0; i < paletteCount; i++)
            palette[i] = ReadU16(data, paletteOffset + i * 2);

        double adjacentRate = indexCount <= 1 ? 0 : (double)adjacentDuplicates / (indexCount - 1);
        PrimitiveMode primitive = adjacentRate >= 0.03 || indexCount % 3 != 0
            ? PrimitiveMode.TriangleStrip
            : PrimitiveMode.TriangleList;

        batch = new MshDrawBatchInfo
        {
            BatchIndex = batchIndex,
            HeaderOffset = headerOffset,
            PaletteCount = paletteCount,
            BonePalette = palette,
            IndexCount = indexCount,
            IndexOffset = indexOffset,
            PrimitiveMode = primitive,
            TriangleCount = primitive == PrimitiveMode.TriangleStrip ? CountStripTriangles(indices) : indexCount / 3,
            UniqueIndexedVertices = used.Count,
            MinIndex = minIndex == int.MaxValue ? 0 : minIndex,
            MaxIndex = maxIndex,
            LegacyContinuationLayout = legacy
        };
        nextOffset = indexOffset + indexCount * 2;
        return true;
    }

    private static void BuildIssues(MshStructureAnalysis analysis, SkeletonData? skeleton)
    {
        if (analysis.Sections.Count == 0)
        {
            analysis.Issues.Add(new MshDiagnosticIssue
            {
                Severity = MshDiagnosticSeverity.Error,
                Code = "NO_SKINNED_SECTION",
                Message = "Aucune section skinnée reconnue (stride 36/44 + footer/palette/index). Le fichier peut être un accessoire, un mesh rigide ou une variante non encore supportée."
            });
            return;
        }

        if (analysis.Sections.Count > 1)
        {
            analysis.Issues.Add(new MshDiagnosticIssue
            {
                Severity = MshDiagnosticSeverity.Info,
                Code = "MULTI_SECTION",
                Message = $"Le MSH contient {analysis.Sections.Count} sections géométriques. Les anciennes versions n'en affichaient qu'une : cela explique beaucoup de personnages incomplets."
            });
        }

        if (analysis.Sections.Any(s => s.DrawBatchCount > 1))
        {
            int multi = analysis.Sections.Count(s => s.DrawBatchCount > 1);
            analysis.Issues.Add(new MshDiagnosticIssue
            {
                Severity = MshDiagnosticSeverity.Info,
                Code = "MULTI_DRAW_BATCH",
                Message = $"{multi} section(s) utilisent plusieurs lots d'indices/palettes ({analysis.TotalDrawBatches} lots au total). Ils sont maintenant tous assemblés ; ignorer ces lots provoquait notamment le torse manquant de Trane Act1."
            });
        }

        if (analysis.Sections.Any(s => s.VertexStride == 44))
        {
            analysis.Issues.Add(new MshDiagnosticIssue
            {
                Severity = MshDiagnosticSeverity.Info,
                Code = "STRIDE_44",
                Message = "Layout stride 44 détecté : weights à +32 et joints à +36. Ce format est notamment utilisé par ShannaRay, Tina, Stake, MPA et certains placeholders."
            });
        }

        if (analysis.Sections.Any(s => s.FooterMarker != 4))
        {
            string markers = string.Join(", ", analysis.Sections.Select(s => s.FooterMarker).Distinct().OrderBy(x => x));
            analysis.Issues.Add(new MshDiagnosticIssue
            {
                Severity = MshDiagnosticSeverity.Info,
                Code = "FOOTER_VARIANT",
                Message = $"Variante de section détectée (marker {markers}). Le loader actuel l'accepte au lieu de forcer marker 4."
            });
        }

        bool paletteRemapNeeded = analysis.Sections.Any(s =>
            (s.DrawBatches.Count == 0 ? Enumerable.Repeat(s.BonePalette, 1) : s.DrawBatches.Select(x => x.BonePalette))
                .Any(p => p.Select((value, slot) => (value, slot)).Any(x => x.value != x.slot)));
        if (paletteRemapNeeded)
        {
            analysis.Issues.Add(new MshDiagnosticIssue
            {
                Severity = MshDiagnosticSeverity.Info,
                Code = "BONE_PALETTE_REMAP",
                Message = "Les joint indices des vertices sont locaux à chaque section. Le loader actuel les remappe via la palette d'os du footer ; l'ancien mapping direct pouvait tordre les personnages pendant l'animation."
            });
        }

        foreach (MshSectionInfo s in analysis.Sections)
        {
            if (s.CombinedIndexCoverage < 0.25)
            {
                analysis.Issues.Add(new MshDiagnosticIssue
                {
                    Severity = MshDiagnosticSeverity.Warning,
                    Code = "LOW_INDEX_COVERAGE",
                    Message = $"Section {s.Index}: seulement {s.CombinedIndexCoverage:P0} des vertices sont référencés par l'ensemble des lots d'indices. Possible LOD/données supplémentaires ou variante atypique."
                });
            }

            if (!s.DrawBatchParseComplete)
            {
                analysis.Issues.Add(new MshDiagnosticIssue
                {
                    Severity = MshDiagnosticSeverity.Warning,
                    Code = "DRAW_BATCH_INCOMPLETE",
                    Message = $"Section {s.Index}: {s.DrawBatches.Count}/{Math.Max(1, s.FooterGroupValue)} lots de rendu ont été décodés. La géométrie peut rester incomplète."
                });
            }

            if (s.PaletteCount <= s.MaxLocalBoneSlot)
            {
                analysis.Issues.Add(new MshDiagnosticIssue
                {
                    Severity = MshDiagnosticSeverity.Error,
                    Code = "LOCAL_BONE_OUT_OF_RANGE",
                    Message = $"Section {s.Index}: slot d'os local {s.MaxLocalBoneSlot} hors palette ({s.PaletteCount})."
                });
            }

            IEnumerable<ushort> allPaletteBones = s.DrawBatches.Count == 0
                ? s.BonePalette
                : s.DrawBatches.SelectMany(x => x.BonePalette);
            if (skeleton is not null && allPaletteBones.Any(b => b >= skeleton.Bones.Count))
            {
                analysis.Issues.Add(new MshDiagnosticIssue
                {
                    Severity = MshDiagnosticSeverity.Error,
                    Code = "GLOBAL_BONE_OUT_OF_RANGE",
                    Message = $"Section {s.Index}: une palette de lot référence un os >= {skeleton.Bones.Count}."
                });
            }

            Vector3 size = s.BoundsMax - s.BoundsMin;
            float extent = Math.Max(size.X, Math.Max(size.Y, size.Z));
            if (!float.IsFinite(extent) || extent < 0.001f || extent > 100_000f)
            {
                analysis.Issues.Add(new MshDiagnosticIssue
                {
                    Severity = MshDiagnosticSeverity.Warning,
                    Code = "SUSPICIOUS_BOUNDS",
                    Message = $"Section {s.Index}: bounds suspects ({size.X:0.##}, {size.Y:0.##}, {size.Z:0.##})."
                });
            }
        }

        if (skeleton is null)
        {
            analysis.Issues.Add(new MshDiagnosticIssue
            {
                Severity = MshDiagnosticSeverity.Warning,
                Code = "NO_SKELETON",
                Message = "Sections skinnées trouvées mais squelette non détecté : l'animation ne pourra pas être appliquée correctement."
            });
        }

        if (analysis.Sections.Count > 1)
        {
            analysis.Issues.Add(new MshDiagnosticIssue
            {
                Severity = MshDiagnosticSeverity.Warning,
                Code = "MULTI_MATERIAL_TEXTURE",
                Message = "Plusieurs sections utilisent potentiellement des matériaux/textures différents. Le viewer Smart Materials applique désormais les textures par section ; le diagnostic permet de vérifier les associations."
            });
        }
    }

    private static int CountStripTriangles(IReadOnlyList<int> strip)
    {
        int count = 0;
        for (int i = 2; i < strip.Count; i++)
        {
            int a = strip[i - 2], b = strip[i - 1], c = strip[i];
            if (a != b && b != c && a != c)
                count++;
        }
        return count;
    }

    private static bool IsPlausiblePosition(float value)
        => float.IsFinite(value) && Math.Abs(value) < 1_000_000f;

    private static uint ReadU32(byte[] data, int offset)
    {
        if (offset < 0 || offset + 4 > data.Length)
            return uint.MaxValue;
        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
    }

    private static ushort ReadU16(byte[] data, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));

    private static float ReadF32(byte[] data, int offset)
        => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4)));
}
