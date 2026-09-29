using System.Text;

namespace GettingUpModTool.Core.IO;

public static class HexDumper
{
    public static string Dump(byte[] data, int maxBytes = 256 * 1024)
    {
        int length = Math.Min(data.Length, maxBytes);
        var sb = new StringBuilder(length * 4);

        for (int offset = 0; offset < length; offset += 16)
        {
            sb.Append($"{offset:X8}  ");
            for (int i = 0; i < 16; i++)
            {
                if (offset + i < length) sb.Append($"{data[offset + i]:X2} ");
                else sb.Append("   ");
            }

            sb.Append(' ');
            for (int i = 0; i < 16 && offset + i < length; i++)
            {
                byte b = data[offset + i];
                sb.Append(b is >= 32 and <= 126 ? (char)b : '.');
            }
            sb.AppendLine();
        }

        if (data.Length > maxBytes)
            sb.AppendLine($"\n--- Aperçu limité à {maxBytes:N0} octets sur {data.Length:N0}. ---");
        return sb.ToString();
    }
}
