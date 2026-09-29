using System.Numerics;

namespace GettingUpModTool.Core.Formats.BNM;

public sealed class BnmAnimation
{
    public string FileName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int FileSize { get; init; }
    public int DataBaseOffset { get; init; }
    public int FrameCount { get; init; }
    public byte UnknownHeaderByte { get; init; }
    public int FramesPerSecond { get; init; }
    public int TrackCount { get; init; }
    public ushort TrackHeaderExtraWord { get; init; }
    public int OffsetTableOffset { get; init; }
    public BnmVectorBounds TranslationBoundsRoot { get; init; }
    public BnmVectorBounds TranslationBoundsBones { get; init; }
    public byte[] RawData { get; init; } = [];
    public List<BnmTrack> Tracks { get; } = [];

    public double DurationSeconds
        => FramesPerSecond > 0 && FrameCount > 0
            ? Math.Max(0, FrameCount - 1) / (double)FramesPerSecond
            : 0.0;
}

public readonly record struct BnmVectorBounds(Vector3 Min, Vector3 Max)
{
    public override string ToString()
        => $"({Min.X:0.###}, {Min.Y:0.###}, {Min.Z:0.###}) → ({Max.X:0.###}, {Max.Y:0.###}, {Max.Z:0.###})";
}

public sealed class BnmTrack
{
    public int Index { get; init; }
    public int RelativeOffset { get; init; }

    
    public int AbsoluteOffset { get; init; }

    
    public int RuntimeBaseOffset { get; init; }
    public int Size { get; init; }

    
    public int CountA { get; init; }
    public int CountB { get; init; }
    public int RotationKeyCount => CountA;
    public int TranslationKeyCount => CountB;

    public uint PointerLikeValue { get; init; }
    public byte[] RotationDescriptor { get; init; } = [];
    public string CodecDescriptorHex { get; init; } = string.Empty;
    public byte CodecByte { get; init; }
    public int RotationCodec { get; init; }
    public int TranslationCodec { get; init; }
    public byte FlagsByte { get; init; }

    public int StreamARelativeOffset { get; init; }
    public int StreamBRelativeOffset { get; init; }
    public int RotationStreamAbsoluteOffset { get; init; }
    public int TranslationStreamAbsoluteOffset { get; init; }
    public ushort TrackId { get; init; }
    public string HeaderTailHex { get; init; } = string.Empty;

    public int StreamASize { get; init; }
    public int StreamBSize { get; init; }
    public int? StreamAEntryBytes { get; init; }
    public int? StreamBBytesPerEntryApprox { get; init; }
    public int StreamBFooterBytes { get; init; }

    public bool RotationDecoded { get; set; }
    public bool TranslationDecoded { get; set; }
    public List<BnmRotationKey> RotationKeys { get; } = [];
    public List<BnmTranslationKey> TranslationKeys { get; } = [];

    
    public List<BnmCandidateKey> CandidateAKeys { get; } = [];
    public List<BnmCandidateKey> CandidateBKeys { get; } = [];
}

public readonly record struct BnmRotationKey(
    int Frame,
    Quaternion Rotation,
    int AbsoluteOffset,
    string RawHex);

public readonly record struct BnmTranslationKey(
    int Frame,
    Vector3 Translation,
    int AbsoluteOffset,
    string RawHex);

public sealed class BnmCandidateKey
{
    public int TrackIndex { get; init; }
    public string Stream { get; init; } = string.Empty;
    public int EntryIndex { get; init; }
    public int EntryBytes { get; init; }
    public int Frame { get; init; }
    public double TimeSeconds { get; init; }
    public int AbsoluteOffset { get; init; }
    public string RawHex { get; init; } = string.Empty;
    public string TimingRule { get; init; } = string.Empty;
}

public sealed class BnmStructuralDiff
{
    public string Field { get; init; } = string.Empty;
    public string Left { get; init; } = string.Empty;
    public string Right { get; init; } = string.Empty;
    public bool Same { get; init; }
}
