using System.Diagnostics;
using System.IO;
using System.Text;

namespace GettingUpModTool.Core.Game;

public static class ExportLog
{
    private static readonly object Sync = new();

    public static string LogPath
    {
        get
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GettingUpModTool");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "export.log");
        }
    }

    public static void WriteTextureWarnings(string format, IEnumerable<string> warnings)
    {
        string[] items = warnings.Where(w => !string.IsNullOrWhiteSpace(w)).ToArray();
        if (items.Length == 0)
            return;

        try
        {
            var sb = new StringBuilder()
                .AppendLine("============================================================")
                .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {format} texture warnings ({items.Length})");
            foreach (string warning in items)
                sb.AppendLine("- " + warning);
            sb.AppendLine();

            lock (Sync)
                File.AppendAllText(LogPath, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[GettingUpModTool] Unable to write export.log: {ex}");
        }
    }
}
