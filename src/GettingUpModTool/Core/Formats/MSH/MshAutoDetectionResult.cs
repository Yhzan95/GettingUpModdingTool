namespace GettingUpModTool.Core.Formats.MSH;

public sealed class MshAutoDetectionResult
{
    public required MshLayout Layout { get; init; }
    public double Confidence { get; init; }
    public string Notes { get; init; } = string.Empty;
}
