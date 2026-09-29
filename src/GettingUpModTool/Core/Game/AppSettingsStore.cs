using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace GettingUpModTool.Core.Game;

public sealed class AppSettingsData
{
    public string? GameRoot { get; set; }
    public bool IncludeOtherFiles { get; set; }
    public string Language { get; set; } = "en";

    public int LastScanFileCount { get; set; }
}

public static class AppSettingsStore
{
    public static string? LastWarning { get; private set; }
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GettingUpModTool");

    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettingsData Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettingsData();

            return JsonSerializer.Deserialize<AppSettingsData>(File.ReadAllText(SettingsPath))
                   ?? new AppSettingsData();
        }
        catch (Exception ex)
        {
            RecordWarning("Lecture des paramètres impossible", ex);
            return new AppSettingsData();
        }
    }

    public static void Save(AppSettingsData settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
        catch (Exception ex)
        {
            RecordWarning("Écriture des paramètres impossible", ex);
        }
    }
    private static void RecordWarning(string message, Exception ex)
    {
        LastWarning = $"{message}: {ex.Message}";
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.AppendAllText(Path.Combine(SettingsDirectory, "settings-errors.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {LastWarning}{Environment.NewLine}");
        }
        catch (Exception logException)
        {
            Trace.WriteLine($"[GettingUpModTool] Impossible d'écrire settings-errors.log: {logException}");
        }
    }

}
