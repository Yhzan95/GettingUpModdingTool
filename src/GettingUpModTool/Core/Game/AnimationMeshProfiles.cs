using System.IO;

namespace GettingUpModTool.Core.Game;

public sealed class AnimationMeshProfile
{
    public required string Family { get; init; }
    public IReadOnlyList<string> DirectoryHints { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FilePrefixes { get; init; } = Array.Empty<string>();
    public bool IsGenericFamily { get; init; }
}

public static class AnimationMeshProfiles
{
    private static readonly IReadOnlyDictionary<string, AnimationMeshProfile> Profiles =
        new Dictionary<string, AnimationMeshProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["Belt"] = Profile("Belt", new[] { "dip01" }, new[] { "belt" }),
            ["Bum"] = Profile("Bum", new[] { "bum01", "bum02" }, new[] { "bum" }),
            ["CCK"] = Profile("CCK", new[] { "cckhvys01", "cckhvyslvg01", "ccklts01", "cckltslvg01" }, new[] { "cckhvy", "ccklt" }),
            ["CWorker"] = Profile("CWorker", new[] { "cnstwrkrv01", "constl01" }, new[] { "cnstwrkr", "constl" }),
            ["Cope2"] = Profile("Cope2", new[] { "cope201" }, new[] { "cope2" }),
            ["Dip"] = Profile("Dip", new[] { "dip01" }, new[] { "dip" }),
            ["Dog"] = Profile("Dog", new[] { "dog01" }, new[] { "dog" }),
            ["Gabe"] = Profile("Gabe", new[] { "gabe01", "gabe02" }, new[] { "gabe" }),
            ["Janitor"] = Profile("Janitor", new[] { "jan01" }, new[] { "jan" }),
            ["Kry"] = Profile("Kry", new[] { "kry01" }, new[] { "kry" }),
            ["MeatWorker"] = Profile("MeatWorker", new[] { "mw", "mwl01", "mws01" }, new[] { "mwl", "mws" }),
            ["Pigeon"] = Profile("Pigeon", new[] { "pigeon01" }, new[] { "pigeon" }),
            ["PoliceChief"] = Profile("PoliceChief", new[] { "chiefwmhunt01" }, new[] { "chiefwmhunt" }),
            ["PvtArmySoldier"] = Profile("PvtArmySoldier", new[] { "mpa" }, new[] { "mpa" }),
            ["Rat"] = Profile("Rat", new[] { "rat01" }, new[] { "rat" }),
            ["Seagull"] = Profile("Seagull", new[] { "seagull01" }, new[] { "seagull" }),
            ["SecurityGuard"] = Profile("SecurityGuard", new[] { "securityguard", "securityguard01" }, new[] { "securityguard" }),
            ["ShannaRay"] = Profile("ShannaRay", new[] { "shannaray01" }, new[] { "shannaray" }),
            ["Spleen"] = Profile("Spleen", new[] { "spleen01" }, new[] { "spleen" }),
            ["Trane"] = Profile("Trane", new[] { "trane" }, new[] { "trane" }),
            ["VanR"] = Profile("VanR", new[] { "vanrl", "vanrs" }, new[] { "vanrl", "vanrs", "vanrminiboss" }),
            ["VanSquad"] = Profile("VanSquad", new[] { "vansq01" }, Array.Empty<string>()),
            ["VanSquadBoss"] = Profile("VanSquadBoss", new[] { "vansqboss01" }, new[] { "vansqboss" }),
            ["WWA"] = Profile("WWA", new[] { "wwal01", "wwas01" }, new[] { "wwal", "wwas" }),
            ["Welder"] = Profile("Welder", new[] { "welder01" }, new[] { "welder" }),
            ["WhiteMike"] = Profile("WhiteMike", new[] { "whitemike01" }, new[] { "whitemike" }),
            ["WorkBum"] = Profile("WorkBum", new[] { "workbumv01" }, new[] { "workbum" }),

            
            ["E3Poses"] = Generic("E3Poses"),
            ["Grapples"] = Generic("Grapples"),
            ["Layers"] = Generic("Layers"),
            ["LifeStyle"] = Generic("LifeStyle"),
            ["MidGround"] = Generic("MidGround"),
            ["NPC"] = Generic("NPC"),
            ["PEDS"] = Generic("PEDS"),
            ["Reactions"] = Generic("Reactions"),
            ["Crew"] = Generic("Crew"),
            ["Buddy"] = Generic("Buddy"),
            ["BlimpPipe"] = Generic("BlimpPipe")
        };

    public static AnimationMeshProfile? Get(string family)
        => Profiles.TryGetValue(family, out AnimationMeshProfile? profile) ? profile : null;

    public static bool Matches(AnimationMeshProfile profile, GameAsset meshAsset)
    {
        string fileBase = Normalize(Path.GetFileNameWithoutExtension(meshAsset.Name));
        string[] segments = meshAsset.RelativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize)
            .Where(x => x.Length > 0)
            .ToArray();

        if (profile.DirectoryHints.Any(hint => segments.Contains(Normalize(hint), StringComparer.OrdinalIgnoreCase)))
            return true;

        return profile.FilePrefixes.Any(prefix => fileBase.StartsWith(Normalize(prefix), StringComparison.OrdinalIgnoreCase));
    }

    private static AnimationMeshProfile Profile(string family, IReadOnlyList<string> directories, IReadOnlyList<string> prefixes)
        => new() { Family = family, DirectoryHints = directories, FilePrefixes = prefixes };

    private static AnimationMeshProfile Generic(string family)
        => new() { Family = family, IsGenericFamily = true };

    private static string Normalize(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
