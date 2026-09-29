namespace GettingUpModTool.Core.Formats.BAN;

public sealed class BanDocument
{
    public string FileName { get; init; } = string.Empty;
    public int ActualFileSize { get; init; }
    public uint DeclaredFileSize { get; init; }
    public ushort SectionCount { get; init; }
    public ushort Marker { get; init; }
    public uint HeaderWord0 { get; init; }
    public uint HeaderWord3 { get; init; }
    public uint HeaderWord4 { get; init; }
    public uint OffsetTableOffset { get; init; }
    public List<BanSection> Sections { get; } = [];
}

public sealed class BanSection
{
    public int Index { get; init; }
    public int Offset { get; init; }
    public int Size { get; init; }
    public string PreviewHex { get; init; } = string.Empty;
}
