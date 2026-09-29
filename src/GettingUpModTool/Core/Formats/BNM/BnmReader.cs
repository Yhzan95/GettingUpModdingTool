using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Text;

namespace GettingUpModTool.Core.Formats.BNM;

public static class BnmReader
{
    private const int NameFieldSize = 32;
    private const int FrameCountOffset = 0x24;
    private const int UnknownByteOffset = 0x26;
    private const int FpsOffset = 0x27;
    private const int TrackCountOffset = 0x28;
    private const int TrackExtraWordOffset = 0x2A;
    private const int TrackTablePointerOffset = 0x2C;
    
    
    private const int RootBoundsBeforeTrackTable = 0x34;
    private const int BoneBoundsBeforeTrackTable = 0x1C;
    
    private const int SerializedTrackMinimumSize = 0x1C;

    public static BnmAnimation Read(string path)
        => Read(File.ReadAllBytes(path), Path.GetFileName(path));

    public static BnmAnimation Read(byte[] data, string fileName = "<memory>")
    {
        if (data.Length < 0x180)
            throw new InvalidDataException("Fichier BNM trop petit pour la structure Getting Up PC.");

        string name = ReadAsciiZ(data, 0, NameFieldSize);
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException("Nom BNM introuvable à l'offset 0x00.");

        int baseOffset = FindAnimationBlock(data, name);
        if (baseOffset < 0)
            throw new InvalidDataException("Bloc animation BNM Getting Up non reconnu.");

        int frameCount = ReadU16(data, baseOffset + FrameCountOffset);
        byte unknown = data[baseOffset + UnknownByteOffset];
        int fps = data[baseOffset + FpsOffset];
        int trackCount = ReadU16(data, baseOffset + TrackCountOffset);
        ushort extraWord = ReadU16(data, baseOffset + TrackExtraWordOffset);
        int tableRelativeOffset = ReadTrackTableRelativeOffset(data, baseOffset);
        int tableOffset = checked(baseOffset + tableRelativeOffset);
        int rootBoundsOffset = checked(tableOffset - RootBoundsBeforeTrackTable);
        int boneBoundsOffset = checked(tableOffset - BoneBoundsBeforeTrackTable);

        var result = new BnmAnimation
        {
            FileName = fileName,
            Name = name,
            FileSize = data.Length,
            DataBaseOffset = baseOffset,
            FrameCount = frameCount,
            UnknownHeaderByte = unknown,
            FramesPerSecond = fps,
            TrackCount = trackCount,
            TrackHeaderExtraWord = extraWord,
            OffsetTableOffset = tableOffset,
            TranslationBoundsRoot = ReadBounds(data, rootBoundsOffset),
            TranslationBoundsBones = ReadBounds(data, boneBoundsOffset),
            RawData = data
        };

        var relativeOffsets = new int[trackCount];
        for (int i = 0; i < trackCount; i++)
            relativeOffsets[i] = checked((int)ReadU32(data, tableOffset + i * 4));

        for (int i = 0; i < trackCount; i++)
        {
            int absolute = checked(baseOffset + relativeOffsets[i]);
            int end = i + 1 < trackCount
                ? checked(baseOffset + relativeOffsets[i + 1])
                : data.Length;

            if (absolute < 4 || end < absolute || end > data.Length || end - absolute < SerializedTrackMinimumSize)
                throw new InvalidDataException($"Track BNM #{i} hors limites (0x{absolute:X}..0x{end:X}).");

            int runtimeBase = absolute - 4;
            int rotationCount = ReadU16(data, absolute + 0x00);
            int translationCount = ReadU16(data, absolute + 0x02);
            uint pointerLike = ReadU32(data, absolute + 0x04);
            byte[] rotationDescriptor = data.AsSpan(absolute + 0x08, 6).ToArray();
            byte codecByte = data[absolute + 0x0E];
            byte flagsByte = data[absolute + 0x0F];
            int rotationCodec = codecByte & 0x0F;
            int translationCodec = codecByte >> 4;
            int rotationStreamRelative = checked((int)ReadU32(data, absolute + 0x10));
            int translationStreamRelative = checked((int)ReadU32(data, absolute + 0x14));
            ushort trackId = ReadU16(data, absolute + 0x18);
            string headerTail = Hex(data, absolute + 0x1A, 2);

            int rotationStreamAbsolute = checked(runtimeBase + rotationStreamRelative);
            int translationStreamAbsolute = checked(runtimeBase + translationStreamRelative);
            int? rotationEntryBytes = BnmCodec.RotationEntrySize(rotationCodec);
            int? translationEntryBytes = BnmCodec.TranslationEntrySize(translationCodec);

            int rotationStreamSize = rotationCount > 0 && rotationEntryBytes is int rs
                ? checked(rotationCount * rs)
                : Math.Max(0, translationStreamAbsolute - rotationStreamAbsolute);
            int translationStreamSize = translationCount > 0 && translationEntryBytes is int ts
                ? checked(translationCount * ts)
                : Math.Max(0, end - translationStreamAbsolute);

            if (rotationCount > 0 && (rotationStreamAbsolute < 0 || rotationStreamAbsolute + rotationStreamSize > data.Length))
                throw new InvalidDataException($"Stream rotation du track #{i} hors limites.");
            if (translationCount > 0 && (translationStreamAbsolute < 0 || translationStreamAbsolute + translationStreamSize > data.Length))
                throw new InvalidDataException($"Stream translation du track #{i} hors limites.");

            var track = new BnmTrack
            {
                Index = i,
                RelativeOffset = relativeOffsets[i],
                AbsoluteOffset = absolute,
                RuntimeBaseOffset = runtimeBase,
                Size = end - absolute,
                CountA = rotationCount,
                CountB = translationCount,
                PointerLikeValue = pointerLike,
                RotationDescriptor = rotationDescriptor,
                CodecDescriptorHex = Convert.ToHexString(rotationDescriptor),
                CodecByte = codecByte,
                RotationCodec = rotationCodec,
                TranslationCodec = translationCodec,
                FlagsByte = flagsByte,
                StreamARelativeOffset = rotationStreamRelative,
                StreamBRelativeOffset = translationStreamRelative,
                RotationStreamAbsoluteOffset = rotationStreamAbsolute,
                TranslationStreamAbsoluteOffset = translationStreamAbsolute,
                TrackId = trackId,
                HeaderTailHex = headerTail,
                StreamASize = rotationStreamSize,
                StreamBSize = translationStreamSize,
                StreamAEntryBytes = rotationEntryBytes,
                StreamBBytesPerEntryApprox = translationEntryBytes,
                StreamBFooterBytes = Math.Max(0, end - (translationStreamAbsolute + translationStreamSize))
            };

            result.Tracks.Add(track);
        }

        
        foreach (BnmTrack track in result.Tracks)
            BnmCodec.DecodeTrack(result, track);

        return result;
    }

    public static List<BnmStructuralDiff> Compare(string leftPath, string rightPath)
    {
        BnmAnimation left = Read(leftPath);
        BnmAnimation right = Read(rightPath);
        var rows = new List<BnmStructuralDiff>();

        Add(rows, "Name", left.Name, right.Name);
        Add(rows, "FrameCount", left.FrameCount.ToString(), right.FrameCount.ToString());
        Add(rows, "FPS", left.FramesPerSecond.ToString(), right.FramesPerSecond.ToString());
        Add(rows, "Duration", left.DurationSeconds.ToString("0.###"), right.DurationSeconds.ToString("0.###"));
        Add(rows, "TrackCount", left.TrackCount.ToString(), right.TrackCount.ToString());
        Add(rows, "Root translation bounds", left.TranslationBoundsRoot.ToString(), right.TranslationBoundsRoot.ToString());
        Add(rows, "Bone translation bounds", left.TranslationBoundsBones.ToString(), right.TranslationBoundsBones.ToString());

        int n = Math.Max(left.TrackCount, right.TrackCount);
        for (int i = 0; i < n; i++)
        {
            if (i >= left.TrackCount)
            {
                Add(rows, $"Track {i}", "<absent>", DescribeTrack(right.Tracks[i]));
                continue;
            }
            if (i >= right.TrackCount)
            {
                Add(rows, $"Track {i}", DescribeTrack(left.Tracks[i]), "<absent>");
                continue;
            }

            BnmTrack a = left.Tracks[i];
            BnmTrack b = right.Tracks[i];
            Add(rows, $"T{i} TrackId", $"0x{a.TrackId:X4}", $"0x{b.TrackId:X4}");
            Add(rows, $"T{i} Rot keys", a.RotationKeyCount.ToString(), b.RotationKeyCount.ToString());
            Add(rows, $"T{i} Trans keys", a.TranslationKeyCount.ToString(), b.TranslationKeyCount.ToString());
            Add(rows, $"T{i} Codecs", $"R{a.RotationCodec}/T{a.TranslationCodec}", $"R{b.RotationCodec}/T{b.TranslationCodec}");
            Add(rows, $"T{i} Rot descriptor", a.CodecDescriptorHex, b.CodecDescriptorHex);
            Add(rows, $"T{i} Size", a.Size.ToString(), b.Size.ToString());
        }

        return rows;
    }

    private static BnmVectorBounds ReadBounds(byte[] data, int offset)
    {
        Vector3 min = new(ReadF32(data, offset), ReadF32(data, offset + 4), ReadF32(data, offset + 8));
        Vector3 max = new(ReadF32(data, offset + 12), ReadF32(data, offset + 16), ReadF32(data, offset + 20));
        return new BnmVectorBounds(min, max);
    }

    private static int FindAnimationBlock(byte[] data, string name)
    {
        byte[] needle = Encoding.ASCII.GetBytes(name);
        for (int offset = NameFieldSize; offset + 0xA0 < data.Length; offset++)
        {
            if (!data.AsSpan(offset, needle.Length).SequenceEqual(needle))
                continue;
            if (ValidateBlock(data, offset))
                return offset;
        }

        const int observed = 0x12C;
        return observed + 0xA0 < data.Length && ValidateBlock(data, observed) ? observed : -1;
    }

    private static bool ValidateBlock(byte[] data, int baseOffset)
    {
        try
        {
            if (baseOffset < 0 || baseOffset + TrackTablePointerOffset + 4 > data.Length)
                return false;

            int frameCount = ReadU16(data, baseOffset + FrameCountOffset);
            int fps = data[baseOffset + FpsOffset];
            int trackCount = ReadU16(data, baseOffset + TrackCountOffset);
            if (frameCount < 1 || frameCount > 100_000 || fps < 1 || fps > 240 || trackCount < 1 || trackCount > 512)
                return false;

            int tableRelative = ReadTrackTableRelativeOffset(data, baseOffset);
            int table = checked(baseOffset + tableRelative);
            if (tableRelative < 0x70 || tableRelative > 0x400 || table + trackCount * 4 > data.Length)
                return false;

            int previous = -1;
            for (int i = 0; i < trackCount; i++)
            {
                uint relRaw = ReadU32(data, table + i * 4);
                if (relRaw > int.MaxValue)
                    return false;
                int rel = (int)relRaw;
                int abs = baseOffset + rel;
                if (rel < 0x80 || abs < 4 || abs + SerializedTrackMinimumSize > data.Length)
                    return false;
                if (previous >= 0 && rel <= previous)
                    return false;
                previous = rel;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int ReadTrackTableRelativeOffset(byte[] data, int baseOffset)
    {
        uint runtimePointer = ReadU32(data, baseOffset + TrackTablePointerOffset);
        if (runtimePointer < 4 || runtimePointer > 0x10000)
            throw new InvalidDataException($"Offset de table BNM invalide : 0x{runtimePointer:X}.");
        return checked((int)runtimePointer - 4);
    }

    private static string DescribeTrack(BnmTrack t)
        => $"id=0x{t.TrackId:X4}, R={t.RotationKeyCount}/c{t.RotationCodec}, T={t.TranslationKeyCount}/c{t.TranslationCodec}";

    private static void Add(List<BnmStructuralDiff> rows, string field, string left, string right)
        => rows.Add(new BnmStructuralDiff { Field = field, Left = left, Right = right, Same = left == right });

    private static string ReadAsciiZ(byte[] data, int offset, int maxLength)
    {
        int max = Math.Min(data.Length, offset + maxLength);
        int end = offset;
        while (end < max && data[end] != 0)
        {
            byte b = data[end];
            if (b < 32 || b > 126)
                return string.Empty;
            end++;
        }
        return Encoding.ASCII.GetString(data, offset, end - offset);
    }

    private static string Hex(byte[] data, int offset, int count)
        => Convert.ToHexString(data.AsSpan(offset, count));

    private static ushort ReadU16(byte[] data, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));

    private static uint ReadU32(byte[] data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));

    private static float ReadF32(byte[] data, int offset)
        => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4)));
}
