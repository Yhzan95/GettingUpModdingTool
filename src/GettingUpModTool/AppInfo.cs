using System.Reflection;

namespace GettingUpModTool;
public static class AppInfo
{
    public const string ProductName = "Getting Up Mod Tool";

    public static string Version { get; } = ReadVersion();

    public static string DisplayName => $"{ProductName} v{Version}";

    private static string ReadVersion()
    {
        Assembly assembly = typeof(AppInfo).Assembly;
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
            return informational.Split('+')[0];

        Version? version = assembly.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
