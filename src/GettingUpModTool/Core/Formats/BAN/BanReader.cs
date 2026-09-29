using System.Buffers.Binary;
using System.IO;

namespace GettingUpModTool.Core.Formats.BAN;

public static class BanReader
{
    private const int OffsetTableOffset = 0x18;

    public static BanDocument Read(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.Length < 0x20)
            throw new InvalidDataException("BAN trop petit.");

        ushort sectionCount = ReadU16(data, 0x08);
        ushort marker = ReadU16(data, 0x0A);
        if (sectionCount < 1 || sectionCount > 4096)
            throw new InvalidDataException($"Nombre de sections BAN non plausible : {sectionCount}.");
        if (OffsetTableOffset + sectionCount * 4 > data.Length)
            throw new InvalidDataException("Table d'offsets BAN hors limites.");

        var offsets = new int[sectionCount];
        int previous = -1;
        for (int i = 0; i < sectionCount; i++)
        {
            uint raw = ReadU32(data, OffsetTableOffset + i * 4);
            if (raw > int.MaxValue)
                throw new InvalidDataException("Offset BAN trop grand.");
            int off = (int)raw;
            if (off < OffsetTableOffset + sectionCount * 4 || off >= data.Length)
                throw new InvalidDataException($"Section BAN #{i} hors limites : 0x{off:X}.");
            if (previous >= 0 && off <= previous)
                throw new InvalidDataException("Offsets BAN non croissants.");
            offsets[i] = off;
            previous = off;
        }

        var result = new BanDocument
        {
            FileName = Path.GetFileName(path),
            ActualFileSize = data.Length,
            DeclaredFileSize = ReadU32(data, 0x04),
            SectionCount = sectionCount,
            Marker = marker,
            HeaderWord0 = ReadU32(data, 0x00),
            HeaderWord3 = ReadU32(data, 0x0C),
            HeaderWord4 = ReadU32(data, 0x10),
            OffsetTableOffset = OffsetTableOffset
        };

        for (int i = 0; i < sectionCount; i++)
        {
            int start = offsets[i];
            int end = i + 1 < sectionCount ? offsets[i + 1] : data.Length;
            int size = end - start;
            int preview = Math.Min(24, size);
            result.Sections.Add(new BanSection
            {
                Index = i,
                Offset = start,
                Size = size,
                PreviewHex = Convert.ToHexString(data.AsSpan(start, preview))
            });
        }

        return result;
    }
    private static ushort ReadU16(byte[] data, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));

    private static uint ReadU32(byte[] data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
}
