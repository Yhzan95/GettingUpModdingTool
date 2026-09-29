using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace GettingUpModTool.Core.Formats.ST;

public static class StReader
{
    public static StTexture Read(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.Length < 0x80 || data[0] != (byte)'S' || data[1] != (byte)'T')
            throw new InvalidDataException("Texture ST non reconnue.");

        int formatCode = checked((int)ReadU32(data, 0x08));
        int width = checked((int)ReadU32(data, 0x0C));
        int height = checked((int)ReadU32(data, 0x10));
        if (width < 1 || height < 1 || width > 16384 || height > 16384)
            throw new InvalidDataException($"Dimensions ST non plausibles : {width}x{height}.");

        int baseLevelSize = GetBaseLevelSize(formatCode, width, height);
        int dataOffset = FindPayloadOffset(data, baseLevelSize);
        if (dataOffset < 0 || (long)dataOffset + baseLevelSize > data.Length)
            throw new InvalidDataException("Payload ST principal tronqué ou non reconnu.");

        string name = ReadAsciiZ(data, 0x40, 96);
        byte[] bgra;
        string codec;

        switch (formatCode)
        {
            case 0:
                codec = "BGRA32";
                bgra = data.AsSpan(dataOffset, baseLevelSize).ToArray();
                break;

            case 3:
                codec = "DXT1";
                bgra = DecodeDxt1(data.AsSpan(dataOffset, baseLevelSize), width, height);
                break;

            case 4:
                codec = "DXT5";
                bgra = DecodeDxt5(data.AsSpan(dataOffset, baseLevelSize), width, height);
                break;

            default:
                throw new InvalidDataException($"Format ST non pris en charge (code {formatCode}).");
        }

        return new StTexture
        {
            FileName = Path.GetFileName(path),
            Name = name,
            Width = width,
            Height = height,
            DataOffset = dataOffset,
            CompressedSize = baseLevelSize,
            Codec = codec,
            Bgra32 = bgra
        };
    }

    private static int GetBaseLevelSize(int formatCode, int width, int height)
    {
        return formatCode switch
        {
            0 => checked(width * height * 4),
            3 => checked(((width + 3) / 4) * ((height + 3) / 4) * 8),
            4 => checked(((width + 3) / 4) * ((height + 3) / 4) * 16),
            _ => throw new InvalidDataException($"Format ST non pris en charge (code {formatCode}).")
        };
    }

    private static int FindPayloadOffset(byte[] data, int baseLevelSize)
    {
        
        
        
        
        if (data.Length >= 0x3C)
        {
            uint storedSize = ReadU32(data, 0x34);
            uint storageHeader = ReadU32(data, 0x38);
            if (storedSize >= storageHeader)
            {
                long payloadSpan = (long)storedSize - storageHeader;
                long offset = data.Length - payloadSpan;
                if (offset >= 0x40 && offset <= data.Length && offset + baseLevelSize <= data.Length)
                    return (int)offset;
            }
        }

        
        int tailOffset = data.Length - baseLevelSize;
        if (tailOffset >= 0x40)
            return tailOffset;

        throw new InvalidDataException("Offset du payload ST introuvable.");
    }

    private static byte[] DecodeDxt1(ReadOnlySpan<byte> src, int width, int height)
    {
        byte[] dst = new byte[checked(width * height * 4)];
        int source = 0;
        int blocksX = (width + 3) / 4;
        int blocksY = (height + 3) / 4;
        Span<byte> colors = stackalloc byte[16]; 

        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                if (source + 8 > src.Length)
                    throw new InvalidDataException("Bloc DXT1 tronqué.");

                ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(source + 0, 2));
                ushort c1 = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(source + 2, 2));
                uint colorBits = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(source + 4, 4));

                Put565(colors, 0, c0);
                Put565(colors, 4, c1);

                if (c0 > c1)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        colors[8 + c] = (byte)((2 * colors[c] + colors[4 + c]) / 3);
                        colors[12 + c] = (byte)((colors[c] + 2 * colors[4 + c]) / 3);
                    }
                    colors[11] = 255;
                    colors[15] = 255;
                }
                else
                {
                    for (int c = 0; c < 3; c++)
                        colors[8 + c] = (byte)((colors[c] + colors[4 + c]) / 2);
                    colors[11] = 255;
                    colors[12] = 0;
                    colors[13] = 0;
                    colors[14] = 0;
                    colors[15] = 0;
                }

                for (int py = 0; py < 4; py++)
                {
                    for (int px = 0; px < 4; px++)
                    {
                        int pixel = py * 4 + px;
                        int colorIndex = (int)((colorBits >> (2 * pixel)) & 0x3);
                        int x = bx * 4 + px;
                        int y = by * 4 + py;
                        if (x >= width || y >= height)
                            continue;

                        int d = (y * width + x) * 4;
                        int c = colorIndex * 4;
                        dst[d + 0] = colors[c + 0];
                        dst[d + 1] = colors[c + 1];
                        dst[d + 2] = colors[c + 2];
                        dst[d + 3] = colors[c + 3];
                    }
                }

                source += 8;
            }
        }

        return dst;
    }

    private static byte[] DecodeDxt5(ReadOnlySpan<byte> src, int width, int height)
    {
        byte[] dst = new byte[checked(width * height * 4)];
        int source = 0;
        int blocksX = (width + 3) / 4;
        int blocksY = (height + 3) / 4;
        Span<byte> alphas = stackalloc byte[8];
        Span<byte> colors = stackalloc byte[16]; 

        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                if (source + 16 > src.Length)
                    throw new InvalidDataException("Bloc DXT5 tronqué.");

                byte a0 = src[source + 0];
                byte a1 = src[source + 1];
                ulong alphaBits = 0;
                for (int i = 0; i < 6; i++)
                    alphaBits |= (ulong)src[source + 2 + i] << (8 * i);

                alphas[0] = a0;
                alphas[1] = a1;
                if (a0 > a1)
                {
                    for (int i = 1; i <= 6; i++)
                        alphas[i + 1] = (byte)(((7 - i) * a0 + i * a1) / 7);
                }
                else
                {
                    for (int i = 1; i <= 4; i++)
                        alphas[i + 1] = (byte)(((5 - i) * a0 + i * a1) / 5);
                    alphas[6] = 0;
                    alphas[7] = 255;
                }

                ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(source + 8, 2));
                ushort c1 = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(source + 10, 2));
                uint colorBits = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(source + 12, 4));

                Put565(colors, 0, c0);
                Put565(colors, 4, c1);
                for (int c = 0; c < 3; c++)
                {
                    colors[8 + c] = (byte)((2 * colors[c] + colors[4 + c]) / 3);
                    colors[12 + c] = (byte)((colors[c] + 2 * colors[4 + c]) / 3);
                }
                colors[11] = 255;
                colors[15] = 255;

                for (int py = 0; py < 4; py++)
                {
                    for (int px = 0; px < 4; px++)
                    {
                        int pixel = py * 4 + px;
                        int alphaIndex = (int)((alphaBits >> (3 * pixel)) & 0x7);
                        int colorIndex = (int)((colorBits >> (2 * pixel)) & 0x3);
                        int x = bx * 4 + px;
                        int y = by * 4 + py;
                        if (x >= width || y >= height)
                            continue;

                        int d = (y * width + x) * 4;
                        int c = colorIndex * 4;
                        dst[d + 0] = colors[c + 0];
                        dst[d + 1] = colors[c + 1];
                        dst[d + 2] = colors[c + 2];
                        dst[d + 3] = alphas[alphaIndex];
                    }
                }

                source += 16;
            }
        }

        return dst;
    }

    private static void Put565(Span<byte> colors, int offset, ushort value)
    {
        int r = (value >> 11) & 31;
        int g = (value >> 5) & 63;
        int b = value & 31;
        colors[offset + 0] = (byte)(b * 255 / 31);
        colors[offset + 1] = (byte)(g * 255 / 63);
        colors[offset + 2] = (byte)(r * 255 / 31);
        colors[offset + 3] = 255;
    }

    private static string ReadAsciiZ(byte[] data, int offset, int maxLength)
    {
        int end = offset;
        int max = Math.Min(data.Length, offset + maxLength);
        while (end < max && data[end] != 0)
            end++;
        return Encoding.ASCII.GetString(data, offset, Math.Max(0, end - offset));
    }

    private static uint ReadU32(byte[] data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
}
