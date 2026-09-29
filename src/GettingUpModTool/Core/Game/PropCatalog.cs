namespace GettingUpModTool.Core.Game;

public sealed class PropCategoryDefinition
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Icon { get; init; }
    public required string Description { get; init; }
}

public static class PropCatalog
{
    public static IReadOnlyList<PropCategoryDefinition> Categories { get; } =
    [
        new() { Key = "all", Name = "Tous les objets", Icon = "📦", Description = "Tous les MSH dans Meshes\\Props et Meshes\\Pickups" },
        new() { Key = "weapons", Name = "Armes / objets utilisables", Icon = "🧰", Description = "Armes, objets de combat et objets ramassables du dossier Weapons" },
        new() { Key = "paint", Name = "Peinture / graffiti", Icon = "🎨", Description = "Bombes de peinture, rouleaux, marqueurs et outils de graffiti" },
        new() { Key = "electronics", Name = "Téléphones / électronique", Icon = "📱", Description = "Téléphones, radios, écrans, caméras et petits appareils" },
        new() { Key = "crates", Name = "Caisses / boîtes", Icon = "📦", Description = "Caisses, cartons, palettes, poubelles et conteneurs" },
        new() { Key = "pickups", Name = "Pickups", Icon = "➕", Description = "Santé, bonus et objets à récupérer" },
        new() { Key = "vehicles", Name = "Véhicules", Icon = "🚗", Description = "Voitures, camions, hélicoptères et éléments de véhicules" },
        new() { Key = "furniture", Name = "Mobilier", Icon = "🪑", Description = "Chaises, tables, bancs, casiers, lits et mobilier divers" },
        new() { Key = "cinematic", Name = "Props cinématiques", Icon = "🎬", Description = "Objets utilisés dans les cinématiques" },
        new() { Key = "static", Name = "Décor statique", Icon = "🏙", Description = "Objets de rue, bâtiments et éléments de décor statiques" },
        new() { Key = "level", Name = "Décors de niveaux", Icon = "🧱", Description = "Meshes propres aux missions et environnements" }
    ];

    public static bool IsPropMesh(GameAsset asset)
    {
        if (asset.Kind != GameAssetKind.Mesh)
            return false;

        string path = Normalize(asset.RelativePath);
        return path.Contains("/meshes/props/", StringComparison.Ordinal) ||
               path.Contains("/meshes/pickups/", StringComparison.Ordinal) ||
               path.StartsWith("meshes/props/", StringComparison.Ordinal) ||
               path.StartsWith("meshes/pickups/", StringComparison.Ordinal);
    }

    public static string GetCategoryKey(GameAsset asset)
    {
        string path = Normalize(asset.RelativePath);
        string name = asset.Name.ToLowerInvariant();
        string haystack = path + "/" + name;

        if (path.Contains("/meshes/pickups/", StringComparison.Ordinal) || path.StartsWith("meshes/pickups/", StringComparison.Ordinal))
            return "pickups";

        if (ContainsAny(haystack,
            "paintcan", "paint_can", "paintroller", "paint_roller", "wheatpaste", "wheat_paste",
            "spray", "graff", "marker", "inkbottle", "paintbrush", "paint_brush", "booster can", "boostercan"))
            return "paint";

        if (ContainsAny(haystack,
            "celphone", "cellphone", "cell_phone", "nokia", "telephone", "phone01", "phone02", "phone03",
            "radio", "walkie", "camera", "videocamera", "microphone", "monitor", "television", "/tv/", "computer", "laptop"))
            return "electronics";

        if (ContainsAny(haystack,
            "crate", "box", "carton", "pallet", "palette", "trashcan", "trash_can", "trashbasket", "dumpster", "container"))
            return "crates";

        if (path.Contains("/props/weapons/", StringComparison.Ordinal) || path.StartsWith("props/weapons/", StringComparison.Ordinal))
            return "weapons";

        if (ContainsAny(path, "/vehicles/", "/vehicle/", "cars/", "trucks/", "helicopter", "taxi", "sedan", "coupe", "suv", "van/"))
            return "vehicles";

        if (ContainsAny(haystack,
            "chair", "table", "bench", "sofa", "couch", "bed", "locker", "cabinet", "desk", "stool", "shelf", "bookcase", "wardrobe"))
            return "furniture";

        if (path.Contains("/props/cinematicprops/", StringComparison.Ordinal) || path.StartsWith("props/cinematicprops/", StringComparison.Ordinal))
            return "cinematic";

        if (path.Contains("/props/static/", StringComparison.Ordinal) || path.StartsWith("props/static/", StringComparison.Ordinal))
            return "static";

        return "level";
    }

    public static string CategoryName(string key)
        => Categories.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Name ?? key;

    private static string Normalize(string value)
        => value.Replace('\\', '/').ToLowerInvariant();

    private static bool ContainsAny(string value, params string[] tokens)
        => tokens.Any(token => value.Contains(token, StringComparison.Ordinal));
}
