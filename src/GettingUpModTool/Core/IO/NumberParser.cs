using System.Globalization;

namespace GettingUpModTool.Core.IO;

public static class NumberParser
{
    public static int ParseInt(string? text, int fallback = 0)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex))
            return hex;
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value : fallback;
    }

    public static int? ParseNullableInt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return ParseInt(text, int.MinValue) is var v && v != int.MinValue ? v : null;
    }
}
