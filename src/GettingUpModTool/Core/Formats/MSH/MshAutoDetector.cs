namespace GettingUpModTool.Core.Formats.MSH;

public static class MshAutoDetector
{
    public static MshAutoDetectionResult? Detect(byte[] data)
    {
        MshStructureAnalysis analysis = MshGenericStructureAnalyzer.Analyze(data);
        if (analysis.Sections.Count == 0)
            return null;

        MshSectionInfo section = analysis.Sections
            .OrderByDescending(x => x.VertexCount)
            .ThenByDescending(x => x.TotalTriangleCount)
            .First();

        var layout = new MshLayout
        {
            VertexOffset = section.VertexOffset,
            VertexCount = section.VertexCount,
            VertexStride = section.VertexStride,
            PositionOffset = 0,
            NormalOffset = section.NormalOffset,
            NormalEncoding = NormalEncoding.PackedUnorm8,
            UVOffset = section.UvOffset,
            BoneWeightsOffset = section.IsSkinned ? section.WeightOffset : null,
            BoneIndicesOffset = section.IsSkinned ? section.BoneIndicesOffset : null,
            BoneIndexDivisor = section.IsSkinned ? 3 : 1,
            IndexOffset = section.IndexOffset,
            IndexCount = section.IndexCount,
            IndexSize = IndexElementSize.UInt16,
            PrimitiveMode = section.PrimitiveMode
        };

        double confidence = Math.Clamp(section.VertexScore * 0.85 + 0.15, 0, 1);
        string kind = section.IsSkinned ? "skinné" : "statique";
        string notes = analysis.Sections.Count == 1
            ? $"Section {kind} unique; palette={section.PaletteCount}; marker={section.FooterMarker}."
            : $"{analysis.Sections.Count} sections {kind} / {analysis.TotalDrawBatches} lots de rendu détectés ({analysis.TotalVertices:N0} vertices au total). " +
              "L'éditeur manuel affiche la plus grande section; le mode normal assemble toutes les sections.";

        return new MshAutoDetectionResult
        {
            Layout = layout,
            Confidence = confidence,
            Notes = notes
        };
    }
}
