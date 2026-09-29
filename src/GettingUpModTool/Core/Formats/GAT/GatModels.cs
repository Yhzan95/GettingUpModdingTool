using System.Numerics;

namespace GettingUpModTool.Core.Formats.GAT;

public sealed class GatDocument
{
    public string FileName { get; init; } = string.Empty;
    public List<GatAttachment> Attachments { get; } = [];
}

public sealed class GatAttachment
{
    public string Name { get; init; } = string.Empty;
    public string BoneName { get; init; } = string.Empty;
    public Vector3 Translation { get; init; }
    public Quaternion Rotation { get; init; }
}
