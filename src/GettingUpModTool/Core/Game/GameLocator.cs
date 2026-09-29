using Microsoft.Win32;
using System.IO;
using System.Text.RegularExpressions;

namespace GettingUpModTool.Core.Game;

public static class GameLocator
{
    private static readonly string[] InstallFolderNames =
    {
        "Marc Ecko's Getting Up 2",
        "Marc Ecko's Getting Up",
        "Marc Ecko's Getting Up Contents Under Pressure"
    };

    public static string? DetectGameRoot()
    {
        foreach (string candidate in EnumerateCandidates())
        {
            if (LooksLikeGameRoot(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }

    public static bool LooksLikeGameRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        try
        {
            return Directory.Exists(Path.Combine(path, "engine")) ||
                   Directory.EnumerateFiles(path, "*.exe", SearchOption.TopDirectoryOnly)
                       .Any(f => Path.GetFileName(f).Contains("GettingUp", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    public static string? ResolveGameRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return null;

        DirectoryInfo? directory = new DirectoryInfo(Path.GetFullPath(path));
        for (int i = 0; directory is not null && i < 10; i++, directory = directory.Parent)
        {
            if (LooksLikeGameRoot(directory.FullName))
                return directory.FullName;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string steamRoot in EnumerateSteamRoots())
        {
            string common = Path.Combine(steamRoot, "steamapps", "common");
            foreach (string folder in InstallFolderNames)
            {
                string candidate = Path.Combine(common, folder);
                if (seen.Add(candidate))
                    yield return candidate;
            }

            if (Directory.Exists(common))
            {
                IEnumerable<string> matchingDirectories;
                try
                {
                    matchingDirectories = Directory.EnumerateDirectories(common, "*Getting Up*", SearchOption.TopDirectoryOnly).ToArray();
                }
                catch
                {
                    matchingDirectories = Array.Empty<string>();
                }

                foreach (string candidate in matchingDirectories)
                {
                    if (seen.Add(candidate))
                        yield return candidate;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateSteamRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            string normalized = value.Replace('/', Path.DirectorySeparatorChar).Trim();
            if (Directory.Exists(normalized)) roots.Add(normalized);
        }

        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            Add(key?.GetValue("SteamPath") as string);
            Add(key?.GetValue("InstallPath") as string);
        }
        catch
        {
            
        }

        var expanded = new HashSet<string>(roots, StringComparer.OrdinalIgnoreCase);
        foreach (string steamRoot in roots.ToArray())
        {
            string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;

            try
            {
                string text = File.ReadAllText(vdf);
                foreach (Match match in Regex.Matches(text, "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase))
                {
                    string path = match.Groups[1].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(path)) expanded.Add(path);
                }
            }
            catch
            {
                
            }
        }

        return expanded;
    }
}
