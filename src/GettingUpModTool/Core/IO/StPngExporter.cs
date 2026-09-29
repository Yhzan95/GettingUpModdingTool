using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GettingUpModTool.Core.Formats.ST;

namespace GettingUpModTool.Core.IO;

public static class StPngExporter
{
    public static string Export(string stPath, string pngPath, bool forceOpaque = false)
    {
        StTexture texture = StReader.Read(stPath);
        byte[] pixels = texture.Bgra32;

        if (forceOpaque)
        {
            pixels = (byte[])pixels.Clone();
            for (int i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;
        }

        BitmapSource bitmap = BitmapSource.Create(
            texture.Width,
            texture.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            texture.Width * 4);
        bitmap.Freeze();

        string? directory = Path.GetDirectoryName(pngPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using FileStream stream = File.Create(pngPath);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
        return pngPath;
    }

    public static IReadOnlyList<string> ExportMany(IEnumerable<string> stPaths, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var results = new List<string>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string source in stPaths
                     .Where(File.Exists)
                     .Select(Path.GetFullPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string stem = SanitizeFileName(Path.GetFileNameWithoutExtension(source));
            string fileName = stem + ".png";
            int suffix = 2;
            while (!usedNames.Add(fileName))
                fileName = $"{stem}_{suffix++}.png";

            string target = Path.Combine(outputDirectory, fileName);
            Export(source, target);
            results.Add(target);
        }

        return results;
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "texture" : cleaned;
    }
}
