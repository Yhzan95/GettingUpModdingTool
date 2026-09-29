namespace GettingUpModTool.Core.Game;

public enum GameAssetKind
{
    Mesh,
    Animation,
    Texture,
    Material,
    Attachment,
    Ban,
    Gin,
    Cinematic,
    PlayerData,
    Capture,
    Smf,
    Fts,
    Other
}

public sealed class GameAsset
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required string RelativePath { get; init; }
    public required string Extension { get; init; }
    public required string DirectoryRelativePath { get; init; }
    public required GameAssetKind Kind { get; init; }
    public long Size { get; init; }

    public string KindLabel => Kind switch
    {
        GameAssetKind.Mesh => "MSH / Mesh",
        GameAssetKind.Animation => "BNM / Animation",
        GameAssetKind.Texture => "ST / Texture",
        GameAssetKind.Material => "MTM / Material",
        GameAssetKind.Attachment => "GAT / Attachment",
        GameAssetKind.Ban => "BAN",
        GameAssetKind.Gin => "GIN",
        GameAssetKind.Cinematic => "CIN / Cinematic",
        GameAssetKind.PlayerData => "PLR",
        GameAssetKind.Capture => "CAP",
        GameAssetKind.Smf => "SMF",
        GameAssetKind.Fts => "FTS",
        _ => "Autre"
    };

    public string SizeLabel => Size < 1024
        ? $"{Size} o"
        : Size < 1024 * 1024
            ? $"{Size / 1024.0:0.#} Ko"
            : $"{Size / (1024.0 * 1024.0):0.##} Mo";
}
