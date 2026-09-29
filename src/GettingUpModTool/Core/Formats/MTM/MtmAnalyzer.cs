using System.IO;
using System.Buffers.Binary;
using System.Text;

namespace GettingUpModTool.Core.Formats.MTM;

public static class MtmAnalyzer
{
    public static MtmAnalysis Analyze(string path, int maxNumericRows = 4096)
    {
        byte[] data = File.ReadAllBytes(path);
        return new MtmAnalysis
        {
            FileName = Path.GetFileName(path),
            Size = data.Length,
            Strings = ExtractAsciiStrings(data, 4),
            NumericValues = ExtractNumericValues(data, maxNumericRows)
        };
    }

    public static List<MtmStringEntry> ExtractAsciiStrings(byte[] data, int minimumLength = 4)
    {
        var result = new List<MtmStringEntry>();
        int start = -1;

        for (int i = 0; i <= data.Length; i++)
        {
            bool printable = i < data.Length && data[i] >= 32 && data[i] <= 126;
            if (printable)
            {
                if (start == -1) start = i;
            }
            else if (start != -1)
            {
                int length = i - start;
                if (length >= minimumLength)
                    result.Add(new MtmStringEntry(start, Encoding.ASCII.GetString(data, start, length)));
                start = -1;
            }
        }

        return result;
    }

    public static List<MtmNumericEntry> ExtractNumericValues(byte[] data, int maxRows = 4096)
    {
        var result = new List<MtmNumericEntry>();
        int rows = Math.Min(data.Length / 4, maxRows);

        for (int i = 0; i < rows; i++)
        {
            int offset = i * 4;
            uint u = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
            int s = unchecked((int)u);
            float f = BitConverter.Int32BitsToSingle(s);
            result.Add(new MtmNumericEntry(offset, u, s, f, $"0x{u:X8}"));
        }

        return result;
    }

    public static List<MtmDifference> Compare(string leftPath, string rightPath, int maxDifferences = 10000)
    {
        byte[] a = File.ReadAllBytes(leftPath);
        byte[] b = File.ReadAllBytes(rightPath);
        int length = Math.Max(a.Length, b.Length);
        var differences = new List<MtmDifference>();

        for (int i = 0; i < length && differences.Count < maxDifferences; i++)
        {
            byte av = i < a.Length ? a[i] : (byte)0;
            byte bv = i < b.Length ? b[i] : (byte)0;
            if (i >= a.Length || i >= b.Length || av != bv)
                differences.Add(new MtmDifference(i, av, bv));
        }

        return differences;
    }
}
