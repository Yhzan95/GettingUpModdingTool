namespace GettingUpModTool.Core.Formats.ST;

public sealed class StTexture
{
    public string FileName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public int DataOffset { get; init; }
    public int CompressedSize { get; init; }
    public string Codec { get; init; } = string.Empty;
    public byte[] Bgra32 { get; init; } = [];
}
