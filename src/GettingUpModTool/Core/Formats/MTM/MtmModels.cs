namespace GettingUpModTool.Core.Formats.MTM;

public sealed record MtmStringEntry(int Offset, string Value);
public sealed record MtmNumericEntry(int Offset, uint UInt32, int Int32, float Float32, string Hex);
public sealed record MtmDifference(int Offset, byte Left, byte Right);

public sealed class MtmAnalysis
{
    public required string FileName { get; init; }
    public required int Size { get; init; }
    public List<MtmStringEntry> Strings { get; init; } = [];
    public List<MtmNumericEntry> NumericValues { get; init; } = [];
}
