using System.IO;

namespace GettingUpModTool.Core.Game;

public static class GameAssetScanner
{
    private static readonly Dictionary<string, GameAssetKind> KnownKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        [".msh"] = GameAssetKind.Mesh,
        [".bnm"] = GameAssetKind.Animation,
        [".st"] = GameAssetKind.Texture,
        [".mtm"] = GameAssetKind.Material,
        [".gat"] = GameAssetKind.Attachment,
        [".ban"] = GameAssetKind.Ban,
        [".gin"] = GameAssetKind.Gin,
        [".cin"] = GameAssetKind.Cinematic,
        [".plr"] = GameAssetKind.PlayerData,
        [".cap"] = GameAssetKind.Capture,
        [".smf"] = GameAssetKind.Smf,
        [".fts"] = GameAssetKind.Fts
    };

    public static IReadOnlyList<GameAsset> Scan(string gameRoot, bool includeOtherFiles = false, IProgress<int>? progress = null)
    {
        const int reportEvery = 250;
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
            throw new DirectoryNotFoundException($"Dossier du jeu introuvable : {gameRoot}");

        string root = Path.GetFullPath(gameRoot);
        var assets = new List<GameAsset>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string directory = pending.Pop();

            try
            {
                foreach (string subDirectory in Directory.EnumerateDirectories(directory))
                    pending.Push(subDirectory);
            }
            catch (UnauthorizedAccessException)
            {
                
            }
            catch (IOException)
            {
                
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory).ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (string file in files)
            {
                string extension = Path.GetExtension(file);
                bool known = KnownKinds.TryGetValue(extension, out GameAssetKind kind);
                if (!known && !includeOtherFiles)
                    continue;

                FileInfo info;
                try
                {
                    info = new FileInfo(file);
                }
                catch
                {
                    continue;
                }

                string relative = Path.GetRelativePath(root, file);
                string? relativeDirectory = Path.GetDirectoryName(relative);

                assets.Add(new GameAsset
                {
                    Name = Path.GetFileName(file),
                    FullPath = file,
                    RelativePath = relative,
                    DirectoryRelativePath = string.IsNullOrEmpty(relativeDirectory) ? "." : relativeDirectory,
                    Extension = extension.ToLowerInvariant(),
                    Kind = known ? kind : GameAssetKind.Other,
                    Size = info.Exists ? info.Length : 0
                });

                if (assets.Count % reportEvery == 0)
                    progress?.Report(assets.Count);
            }
        }

        progress?.Report(assets.Count);

        return assets
            .OrderBy(a => a.DirectoryRelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
