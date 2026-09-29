namespace GettingUpModTool.Core.Formats.MSH;

public sealed record MshLayoutCandidate(
    int VertexOffset,
    int VertexStride,
    int SuggestedVertexCount,
    double Score,
    string Notes);
