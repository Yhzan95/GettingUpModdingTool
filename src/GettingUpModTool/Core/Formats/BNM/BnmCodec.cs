using System.Numerics;

namespace GettingUpModTool.Core.Formats.BNM;

public static class BnmCodec
{
    private static readonly Dictionary<int, int> RotationEntrySizes = new()
    {
        [2] = 2, [3] = 3, [4] = 4, [6] = 6, [7] = 7, [8] = 8,
        [10] = 10, [11] = 11, [13] = 9, [14] = 5, [15] = 18
    };

    private static readonly Dictionary<int, int> TranslationEntrySizes = new()
    {
        [2] = 2, [3] = 3, [5] = 5, [8] = 8, [9] = 9, [15] = 14
    };

    
    private static readonly HashSet<int> SupportedRotation = [3, 4, 6, 7, 8, 10];
    private static readonly HashSet<int> SupportedTranslation = [2, 3, 5, 8];

    public static int? RotationEntrySize(int codec)
        => RotationEntrySizes.TryGetValue(codec, out int size) ? size : null;

    public static int? TranslationEntrySize(int codec)
        => TranslationEntrySizes.TryGetValue(codec, out int size) ? size : null;

    public static bool CanDecodeRotation(int codec) => SupportedRotation.Contains(codec);
    public static bool CanDecodeTranslation(int codec) => SupportedTranslation.Contains(codec);

    public static void DecodeTrack(BnmAnimation animation, BnmTrack track)
    {
        DecodeRotations(animation, track);
        DecodeTranslations(animation, track);
    }

    private static void DecodeRotations(BnmAnimation animation, BnmTrack track)
    {
        int? sizeMaybe = RotationEntrySize(track.RotationCodec);
        if (track.RotationKeyCount <= 0 || sizeMaybe is null || !CanDecodeRotation(track.RotationCodec))
            return;

        int size = sizeMaybe.Value;
        int start = track.RotationStreamAbsoluteOffset;
        int required = checked(track.RotationKeyCount * size);
        if (start < 0 || start + required > animation.RawData.Length)
            return;

        for (int i = 0; i < track.RotationKeyCount; i++)
        {
            int p = start + i * size;
            ReadOnlySpan<byte> e = animation.RawData.AsSpan(p, size);
            (int frame, Vector3 raw) = DecodeRotationRaw(e, track.RotationCodec);
            Quaternion q = RemapRotation(raw, track.RotationDescriptor);
            string hex = Convert.ToHexString(e);
            track.RotationKeys.Add(new BnmRotationKey(frame, q, p, hex));
            track.CandidateAKeys.Add(new BnmCandidateKey
            {
                TrackIndex = track.Index,
                Stream = "Rotation",
                EntryIndex = i,
                EntryBytes = size,
                Frame = frame,
                TimeSeconds = animation.FramesPerSecond > 0 ? frame / (double)animation.FramesPerSecond : 0,
                AbsoluteOffset = p,
                RawHex = hex,
                TimingRule = $"codec {track.RotationCodec} — 10-bit frame exact"
            });
        }

        track.RotationDecoded = track.RotationKeys.Count == track.RotationKeyCount;
    }

    private static void DecodeTranslations(BnmAnimation animation, BnmTrack track)
    {
        int? sizeMaybe = TranslationEntrySize(track.TranslationCodec);
        if (track.TranslationKeyCount <= 0 || sizeMaybe is null || !CanDecodeTranslation(track.TranslationCodec))
            return;

        int size = sizeMaybe.Value;
        int start = track.TranslationStreamAbsoluteOffset;
        int required = checked(track.TranslationKeyCount * size);
        if (start < 0 || start + required > animation.RawData.Length)
            return;

        BnmVectorBounds bounds = track.Index <= 1
            ? animation.TranslationBoundsRoot
            : animation.TranslationBoundsBones;

        for (int i = 0; i < track.TranslationKeyCount; i++)
        {
            int p = start + i * size;
            ReadOnlySpan<byte> e = animation.RawData.AsSpan(p, size);
            (int frame, Vector3 t01) = DecodeTranslationRaw(e, track.TranslationCodec);
            Vector3 value = new(
                bounds.Min.X + (bounds.Max.X - bounds.Min.X) * t01.X,
                bounds.Min.Y + (bounds.Max.Y - bounds.Min.Y) * t01.Y,
                bounds.Min.Z + (bounds.Max.Z - bounds.Min.Z) * t01.Z);
            string hex = Convert.ToHexString(e);
            track.TranslationKeys.Add(new BnmTranslationKey(frame, value, p, hex));
            track.CandidateBKeys.Add(new BnmCandidateKey
            {
                TrackIndex = track.Index,
                Stream = "Translation",
                EntryIndex = i,
                EntryBytes = size,
                Frame = frame,
                TimeSeconds = animation.FramesPerSecond > 0 ? frame / (double)animation.FramesPerSecond : 0,
                AbsoluteOffset = p,
                RawHex = hex,
                TimingRule = $"codec {track.TranslationCodec} — 10-bit frame exact"
            });
        }

        track.TranslationDecoded = track.TranslationKeys.Count == track.TranslationKeyCount;
    }

    private static (int Frame, Vector3 Raw) DecodeRotationRaw(ReadOnlySpan<byte> b, int codec)
    {
        return codec switch
        {
            3 => DecodeRot3(b),
            4 => DecodeRot4(b),
            6 => DecodeRot6(b),
            7 => DecodeRot7(b),
            8 => DecodeRot8(b),
            10 => DecodeRot10(b),
            _ => throw new NotSupportedException($"Codec rotation BNM {codec} non supporté.")
        };
    }

    private static (int Frame, Vector3 T01) DecodeTranslationRaw(ReadOnlySpan<byte> b, int codec)
    {
        return codec switch
        {
            2 => DecodeTrans2(b),
            3 => DecodeTrans3(b),
            5 => DecodeTrans5(b),
            8 => DecodeTrans8(b),
            _ => throw new NotSupportedException($"Codec translation BNM {codec} non supporté.")
        };
    }

    private static (int, Vector3) DecodeRot3(ReadOnlySpan<byte> b)
    {
        int x = b[0] >> 4;
        int y = ((b[0] & 0x0F) << 1) | (b[1] >> 7);
        int z = (b[1] >> 2) & 0x1F;
        return (Frame10(b[1], b[2]), new Vector3(ToSigned(x, 15), ToSigned(y, 31), ToSigned(z, 31)));
    }

    private static (int, Vector3) DecodeRot4(ReadOnlySpan<byte> b)
    {
        int x = b[0] >> 1;
        int y = ((b[0] & 1) << 6) | (b[1] >> 2);
        int z = ((b[1] & 3) << 6) | (b[2] >> 2);
        return (Frame10(b[2], b[3]), new Vector3(ToSigned(x, 127), ToSigned(y, 127), ToSigned(z, 255)));
    }

    private static (int, Vector3) DecodeRot6(ReadOnlySpan<byte> b)
    {
        int x = (b[0] << 4) | (b[1] >> 4);
        int y = ((((b[1] & 15) << 8) | b[2]) << 1) | (b[3] >> 7);
        int z = ((b[3] & 127) << 6) | (b[4] >> 2);
        return (Frame10(b[4], b[5]), new Vector3(ToSigned(x, 4095), ToSigned(y, 8191), ToSigned(z, 8191)));
    }

    private static (int, Vector3) DecodeRot7(ReadOnlySpan<byte> b)
    {
        int x = (b[0] << 7) | (b[1] >> 1);
        int y = ((((b[1] & 1) << 8) | b[2]) << 6) | (b[3] >> 2);
        int z = ((((b[3] & 3) << 8) | b[4]) << 6) | (b[5] >> 2);
        return (Frame10(b[5], b[6]), new Vector3(ToSigned(x, 32767), ToSigned(y, 32767), ToSigned(z, 65535)));
    }

    private static (int, Vector3) DecodeRot8(ReadOnlySpan<byte> b)
    {
        int x = (((b[0] << 8) | b[1]) << 2) | (b[2] >> 6);
        int y = ((((b[2] & 63) << 8) | b[3]) << 4) | (b[4] >> 4);
        int z = ((((b[4] & 15) << 8) | b[5]) << 6) | (b[6] >> 2);
        return (Frame10(b[6], b[7]), new Vector3(ToSigned(x, 262143), ToSigned(y, 262143), ToSigned(z, 262143)));
    }

    private static (int, Vector3) DecodeRot10(ReadOnlySpan<byte> b)
    {
        int x = (((b[0] << 8) | b[1]) << 7) | (b[2] >> 1);
        int y = (((((b[2] & 1) << 8) | b[3]) << 8 | b[4]) << 6) | (b[5] >> 2);
        int z = (((((b[5] & 3) << 8) | b[6]) << 8 | b[7]) << 6) | (b[8] >> 2);
        return (Frame10(b[8], b[9]), new Vector3(ToSigned(x, 8_388_607), ToSigned(y, 8_388_607), ToSigned(z, 16_777_215)));
    }

    private static (int, Vector3) DecodeTrans2(ReadOnlySpan<byte> b)
    {
        int x = b[0] >> 6;
        int y = (b[0] >> 4) & 3;
        int z = (b[0] >> 2) & 3;
        return (Frame10(b[0], b[1]), new Vector3(x / 3f, y / 3f, z / 3f));
    }

    private static (int, Vector3) DecodeTrans3(ReadOnlySpan<byte> b)
    {
        int x = b[0] >> 4;
        int y = ((b[0] & 15) << 1) | (b[1] >> 7);
        int z = (b[1] >> 2) & 31;
        return (Frame10(b[1], b[2]), new Vector3(x / 15f, y / 31f, z / 31f));
    }

    private static (int, Vector3) DecodeTrans5(ReadOnlySpan<byte> b)
    {
        int x = (b[0] << 2) | (b[1] >> 6);
        int y = ((b[1] & 63) << 4) | (b[2] >> 4);
        int z = ((b[2] & 15) << 6) | (b[3] >> 2);
        return (Frame10(b[3], b[4]), new Vector3(x / 1023f, y / 1023f, z / 1023f));
    }

    private static (int, Vector3) DecodeTrans8(ReadOnlySpan<byte> b)
    {
        int x = (((b[0] << 8) | b[1]) << 2) | (b[2] >> 6);
        int y = ((((b[2] & 63) << 8) | b[3]) << 4) | (b[4] >> 4);
        int z = ((((b[4] & 15) << 8) | b[5]) << 6) | (b[6] >> 2);
        return (Frame10(b[6], b[7]), new Vector3(x / 262143f, y / 262143f, z / 262143f));
    }

    private static Quaternion RemapRotation(Vector3 raw, ReadOnlySpan<byte> descriptor)
    {
        if (descriptor.Length < 6)
            return Quaternion.Identity;

        float x = RemapAxis(raw.X, descriptor[0], descriptor[1]);
        float y = RemapAxis(raw.Y, descriptor[2], descriptor[3]);
        float z = RemapAxis(raw.Z, descriptor[4], descriptor[5]);
        float w2 = MathF.Max(0f, 1f - x * x - y * y - z * z);
        return new Quaternion(x, y, z, MathF.Sqrt(w2));
    }

    private static float RemapAxis(float signedValue, byte minByte, byte rangeByte)
    {
        float min = minByte * (2f / 255f) - 1f;
        float range = rangeByte * (2f / 255f);
        return min + ((signedValue + 1f) * 0.5f) * range;
    }

    private static float ToSigned(int value, int max)
        => value * (2f / max) - 1f;

    private static int Frame10(byte penultimate, byte last)
        => ((penultimate & 0x03) << 8) | last;
}
