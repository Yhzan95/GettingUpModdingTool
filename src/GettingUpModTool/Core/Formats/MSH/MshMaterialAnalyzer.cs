using System.Buffers.Binary;
using System.IO;
using System.Text;
using GettingUpModTool.Core.Game;

namespace GettingUpModTool.Core.Formats.MSH;

public sealed class MshMaterialSetInfo
{
    public int Index { get; init; }
    public int TextureTableOffset { get; init; }
    public int MaterialTableOffset { get; init; }
    public int EndOffset { get; init; }
    public string RenderGroupName { get; set; } = "Base";
    public string? NextRenderGroupName { get; set; }
    public List<string> TextureReferences { get; } = [];
    public List<string> DiffuseTextureReferences { get; } = [];
    public List<string> AuxiliaryTextureReferences { get; } = [];
    public List<string> MaterialNames { get; } = [];
}

public sealed class MshSectionMaterialBinding
{
    public int SectionIndex { get; init; }
    public string RenderGroupName { get; set; } = "Base";
    public string MaterialName { get; set; } = "<inconnu>";
    public string TextureReference { get; set; } = "";
    public string? ResolvedTexturePath { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool TextureDecoded { get; set; }
    public bool AutoHidden { get; set; }
    public string AutoRule { get; set; } = "";

    public string TextureName => string.IsNullOrWhiteSpace(TextureReference) ? "-" : TextureReference;
    public string TextureFileName => ResolvedTexturePath is null ? "-" : Path.GetFileName(ResolvedTexturePath);
    public string StatusLabel => ResolvedTexturePath is null
        ? "Introuvable"
        : TextureDecoded ? "OK" : "ST non décodé";
    public string VisibilityLabel => IsVisible ? "Visible" : "Masqué";
}

public sealed class MshMaterialAnalysis
{
    public List<MshMaterialSetInfo> Sets { get; } = [];
    public List<MshSectionMaterialBinding> Bindings { get; } = [];
    public List<string> Notes { get; } = [];

    public int ResolvedTextureCount => Bindings.Count(x => x.ResolvedTexturePath is not null);
    public int HiddenCount => Bindings.Count(x => !x.IsVisible);
    public int UnmappedSectionCount { get; set; }
}

public static class MshMaterialAnalyzer
{
    private sealed record StringTable(int Offset, int EndOffset, IReadOnlyList<string> Strings);

    public static MshMaterialAnalysis Analyze(
        byte[] data,
        IReadOnlyList<MshSectionInfo> sections,
        string mshPath,
        IEnumerable<GameAsset>? allAssets = null)
    {
        var result = new MshMaterialAnalysis();
        if (sections.Count == 0)
        {
            result.Notes.Add("Aucune section MSH reconnue : association matériau impossible.");
            return result;
        }
        
        List<MshMaterialSetInfo> standardSets = FindMaterialSets(data);
        List<MshMaterialSetInfo> legacySets = FindLegacyMaterialSets(data, mshPath, allAssets);
        var sets = standardSets
            .Concat(legacySets)
            .OrderBy(s => s.TextureTableOffset)
            .ToList();

        result.Sets.AddRange(sets);
        if (sets.Count == 0)
        {
            result.UnmappedSectionCount = sections.Count;
            result.Notes.Add("Aucune table matériau embarquée reconnue. Le viewer utilise un rendu neutre pour les sections non résolues.");
            return result;
        }

        if (standardSets.Count > 0 && legacySets.Count > 0)
            result.Notes.Add($"Layout matériau hybride reconnu : {standardSets.Count} table(s) marquée(s) 7 + {legacySets.Count} table(s) legacy.");
        else if (legacySets.Count > 0)
            result.Notes.Add("Table matériau legacy reconnue (sans marqueur 7) : textures de section récupérées automatiquement.");

        
        
        for (int i = 0; i < sets.Count; i++)
        {
            int searchEnd = i + 1 < sets.Count
                ? sets[i + 1].TextureTableOffset
                : Math.Min(data.Length, sets[i].EndOffset + 4096);
            sets[i].NextRenderGroupName = FindRenderGroupName(data, sets[i].EndOffset, searchEnd, sets[i]);
            if (i + 1 < sets.Count && !string.IsNullOrWhiteSpace(sets[i].NextRenderGroupName))
                sets[i + 1].RenderGroupName = sets[i].NextRenderGroupName!;
        }

        var orderedSections = sections.OrderBy(s => s.VertexOffset).ToList();
        var assigned = new HashSet<int>();

        foreach (MshMaterialSetInfo set in sets.OrderBy(s => s.TextureTableOffset))
        {
            int count = Math.Min(set.MaterialNames.Count, set.DiffuseTextureReferences.Count);
            if (count <= 0)
                continue;

            
            
            List<MshSectionInfo> candidates = orderedSections
                .Where(s => !assigned.Contains(s.Index) && SectionEnd(s) <= set.TextureTableOffset)
                .ToList();

            if (candidates.Count < count)
                continue;

            List<MshSectionInfo> target = candidates.TakeLast(count).ToList();
            for (int i = 0; i < count; i++)
            {
                MshSectionInfo section = target[i];
                string textureRef = set.DiffuseTextureReferences[i];
                var binding = new MshSectionMaterialBinding
                {
                    SectionIndex = section.Index,
                    RenderGroupName = set.RenderGroupName,
                    MaterialName = set.MaterialNames[i],
                    TextureReference = textureRef,
                    ResolvedTexturePath = ResolveTextureReference(textureRef, mshPath, allAssets),
                    IsVisible = true
                };
                result.Bindings.Add(binding);
                assigned.Add(section.Index);
            }
        }

        result.UnmappedSectionCount = sections.Count - assigned.Count;
        ApplyDefaultVisibility(result.Bindings);

        if (result.UnmappedSectionCount > 0)
            result.Notes.Add($"{result.UnmappedSectionCount} section(s) sans matériau attribué : rendu neutre utilisé pour éviter une mauvaise texture.");
        if (result.HiddenCount > 0)
            result.Notes.Add($"{result.HiddenCount} variante(s) alternative(s) masquée(s) automatiquement pour éviter les meshes superposés.");
        if (result.ResolvedTextureCount < result.Bindings.Count)
            result.Notes.Add($"{result.Bindings.Count - result.ResolvedTextureCount} texture(s) référencée(s) non résolue(s) sur disque.");

        return result;
    }

    public static void ApplyDefaultVisibility(IList<MshSectionMaterialBinding> bindings)
    {
        foreach (MshSectionMaterialBinding b in bindings)
        {
            b.IsVisible = true;
            b.AutoHidden = false;
            b.AutoRule = "";
        }

        bool hasHoodDown = bindings.Any(b => ContainsAny(b.RenderGroupName, "hooddwn", "hooddown"));
        bool hasHairS01 = bindings.Any(b => NormalizeToken(b.MaterialName).Contains("hairs01", StringComparison.Ordinal));

        
        
        
        var headVariantGroups = bindings
            .Select(b => new { Binding = b, Variant = TryGetHeadVariant(b.TextureReference) })
            .Where(x => x.Variant is not null)
            .GroupBy(x => x.Variant!.Value.Family, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToDictionary(
                g => g.Key,
                g => g.Min(x => x.Variant!.Value.Number),
                StringComparer.OrdinalIgnoreCase);

        foreach (MshSectionMaterialBinding b in bindings)
        {
            string group = NormalizeToken(b.RenderGroupName);
            string material = NormalizeToken(b.MaterialName);

            var headVariant = TryGetHeadVariant(b.TextureReference);
            if (headVariant is not null &&
                headVariantGroups.TryGetValue(headVariant.Value.Family, out int defaultHeadVariant) &&
                headVariant.Value.Number != defaultHeadVariant)
            {
                Hide(b, $"tête alternative {headVariant.Value.Number:00} (variante {defaultHeadVariant:00} affichée par défaut)");
                continue;
            }

            if (group.Contains("shadow", StringComparison.Ordinal) || material.Contains("shadow", StringComparison.Ordinal))
            {
                Hide(b, "ombre technique");
                continue;
            }

            if (hasHoodDown && group.Contains("hoodup", StringComparison.Ordinal))
            {
                Hide(b, "variante HoodUp (HoodDwn affichée par défaut)");
                continue;
            }

            if (hasHairS01 && material.Contains("hairs02", StringComparison.Ordinal))
            {
                Hide(b, "variante hair_S02 (hair_S01 affichée par défaut)");
                continue;
            }
        }
    }

    private static (string Family, int Number)? TryGetHeadVariant(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        string normalizedPath = reference.Replace('\\', '/');
        string stem = Path.GetFileNameWithoutExtension(normalizedPath);
        if (string.IsNullOrWhiteSpace(stem) ||
            !ContainsAny(stem, "head", "face"))
            return null;

        int end = stem.Length - 1;
        while (end >= 0 && char.IsDigit(stem[end]))
            end--;
        if (end == stem.Length - 1)
            return null;

        string digits = stem[(end + 1)..];
        if (!int.TryParse(digits, out int number))
            return null;

        
        
        string family = stem[..(end + 1)].TrimEnd('_', '-');
        if (family.EndsWith("S", StringComparison.OrdinalIgnoreCase) && family.Length > 1)
            family = family[..^1].TrimEnd('_', '-');
        if (family.Length < 3)
            return null;

        return (NormalizeToken(family), number);
    }

    private static void Hide(MshSectionMaterialBinding b, string rule)
    {
        b.IsVisible = false;
        b.AutoHidden = true;
        b.AutoRule = rule;
    }

    private static int SectionEnd(MshSectionInfo section)
        => section.DrawBatches.Count == 0
            ? section.IndexOffset + section.IndexCount * 2
            : section.DrawBatches.Max(x => x.IndexOffset + x.IndexCount * 2);

    private static List<MshMaterialSetInfo> FindLegacyMaterialSets(
        byte[] data,
        string mshPath,
        IEnumerable<GameAsset>? allAssets)
    {
        var tables = new List<StringTable>();
        for (int offset = 0; offset <= data.Length - 24; offset++)
        {
            if (TryReadStringTable(data, offset, out StringTable? table) && table is not null)
                tables.Add(table);
        }

        var sets = new List<MshMaterialSetInfo>();
        var usedTextureOffsets = new HashSet<int>();

        for (int i = 0; i < tables.Count; i++)
        {
            StringTable textureTable = tables[i];
            if (usedTextureOffsets.Contains(textureTable.Offset))
                continue;

            
            
            if (textureTable.Offset >= 4 && ReadU32(data, textureTable.Offset - 4) == 7)
                continue;

            List<string> auxiliary = textureTable.Strings.Where(IsAuxiliaryTexture).ToList();
            List<string> diffuse = textureTable.Strings.Where(s => !IsAuxiliaryTexture(s)).ToList();
            if (diffuse.Count == 0 || diffuse.Count > 32)
                continue;

            
            
            
            var resolved = diffuse
                .Select(reference => ResolveTextureReference(reference, mshPath, allAssets))
                .ToArray();
            if (resolved.Any(path => string.IsNullOrWhiteSpace(path) || !File.Exists(path)))
                continue;

            int searchEnd = Math.Min(data.Length, textureTable.EndOffset + 128);
            StringTable? materialTable = null;
            for (int j = i + 1; j < tables.Count; j++)
            {
                StringTable candidate = tables[j];
                if (candidate.Offset < textureTable.EndOffset)
                    continue;
                if (candidate.Offset > searchEnd)
                    break;
                if (candidate.Strings.Count != diffuse.Count || candidate.Strings.Count == 0 || candidate.Strings.Count > 32)
                    continue;

                materialTable = candidate;
                break;
            }

            if (materialTable is null)
                continue;

            var set = new MshMaterialSetInfo
            {
                Index = sets.Count,
                TextureTableOffset = textureTable.Offset,
                MaterialTableOffset = materialTable.Offset,
                EndOffset = materialTable.EndOffset,
                RenderGroupName = "Base"
            };
            set.TextureReferences.AddRange(textureTable.Strings);
            set.DiffuseTextureReferences.AddRange(diffuse);
            set.AuxiliaryTextureReferences.AddRange(auxiliary);
            set.MaterialNames.AddRange(materialTable.Strings);
            sets.Add(set);
            usedTextureOffsets.Add(textureTable.Offset);
        }

        return sets;
    }

    private static List<MshMaterialSetInfo> FindMaterialSets(byte[] data)
    {
        var sets = new List<MshMaterialSetInfo>();
        for (int offset = 0; offset <= data.Length - 28; offset++)
        {
            if (ReadU32(data, offset) != 7)
                continue;

            if (!TryReadStringTable(data, offset + 4, out StringTable? textureTable) || textureTable is null)
                continue;

            int materialSearchEnd = Math.Min(data.Length - 24, textureTable.EndOffset + 128);
            for (int probe = textureTable.EndOffset; probe <= materialSearchEnd; probe++)
            {
                if (!TryReadStringTable(data, probe, out StringTable? materialTable) || materialTable is null)
                    continue;

                List<string> auxiliary = textureTable.Strings.Where(IsAuxiliaryTexture).ToList();
                List<string> diffuse = textureTable.Strings.Where(s => !IsAuxiliaryTexture(s)).ToList();

                
                if (materialTable.Strings.Count == 0 || materialTable.Strings.Count > 32 ||
                    diffuse.Count != materialTable.Strings.Count)
                    continue;

                var set = new MshMaterialSetInfo
                {
                    Index = sets.Count,
                    TextureTableOffset = offset,
                    MaterialTableOffset = probe,
                    EndOffset = materialTable.EndOffset
                };
                set.TextureReferences.AddRange(textureTable.Strings);
                set.DiffuseTextureReferences.AddRange(diffuse);
                set.AuxiliaryTextureReferences.AddRange(auxiliary);
                set.MaterialNames.AddRange(materialTable.Strings);
                sets.Add(set);
                offset = Math.Max(offset, materialTable.EndOffset - 1);
                break;
            }
        }
        return sets;
    }

    private static bool TryReadStringTable(byte[] data, int offset, out StringTable? table)
    {
        table = null;
        if (offset < 0 || offset + 24 > data.Length)
            return false;

        uint totalBytesRaw = ReadU32(data, offset + 0);
        uint zeroA = ReadU32(data, offset + 4);
        uint totalBytesRaw2 = ReadU32(data, offset + 8);
        uint four = ReadU32(data, offset + 12);
        uint countRaw = ReadU32(data, offset + 16);
        uint zeroB = ReadU32(data, offset + 20);

        if (zeroA != 0 || zeroB != 0 || totalBytesRaw != totalBytesRaw2 || four != 4 ||
            totalBytesRaw == 0 || totalBytesRaw > 16_384 || countRaw == 0 || countRaw > 64)
            return false;

        int totalBytes = (int)totalBytesRaw;
        int count = (int)countRaw;
        int cumulativeOffset = offset + 24;
        int stringOffset = cumulativeOffset + Math.Max(0, count - 1) * 4;
        if ((long)stringOffset + totalBytes > data.Length)
            return false;

        uint previous = 0;
        for (int i = 0; i < count - 1; i++)
        {
            uint value = ReadU32(data, cumulativeOffset + i * 4);
            if (value < previous || value > totalBytesRaw)
                return false;
            previous = value;
        }

        int end = stringOffset + totalBytes;
        int cursor = stringOffset;
        var strings = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            int nul = Array.IndexOf(data, (byte)0, cursor, end - cursor);
            if (nul < 0 || nul == cursor || nul - cursor > 256)
                return false;
            for (int p = cursor; p < nul; p++)
                if (data[p] < 32 || data[p] > 126)
                    return false;

            strings.Add(Encoding.ASCII.GetString(data, cursor, nul - cursor));
            cursor = nul + 1;
        }

        for (int p = cursor; p < end; p++)
            if (data[p] != 0)
                return false;

        table = new StringTable(offset, end, strings);
        return true;
    }

    private static string? FindRenderGroupName(byte[] data, int start, int end, MshMaterialSetInfo currentSet)
    {
        if (start < 0 || end <= start || start >= data.Length)
            return null;
        end = Math.Min(end, data.Length);

        HashSet<string> excluded = currentSet.TextureReferences
            .Concat(currentSet.MaterialNames)
            .Select(NormalizeToken)
            .ToHashSet(StringComparer.Ordinal);

        string? fallback = null;
        int cursor = start;
        while (cursor < end)
        {
            if (data[cursor] < 32 || data[cursor] > 126)
            {
                cursor++;
                continue;
            }

            int p = cursor;
            while (p < end && data[p] >= 32 && data[p] <= 126 && p - cursor < 96)
                p++;
            if (p < end && data[p] == 0 && p - cursor >= 3)
            {
                string value = Encoding.ASCII.GetString(data, cursor, p - cursor).Trim();
                string normalized = NormalizeToken(value);
                if (!excluded.Contains(normalized) && LooksLikeRenderGroup(value))
                {
                    if (ContainsAny(value, "hood", "hair", "head", "scope", "shadow", "body", "roller"))
                        return value;
                    fallback ??= value;
                }
            }
            cursor = Math.Max(cursor + 1, p + 1);
        }
        return fallback;
    }

    private static bool LooksLikeRenderGroup(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 3 || value.Length > 64)
            return false;
        if (value.Contains('.') || value.Contains('\\') || value.Contains('/'))
            return false;
        if (value.Any(char.IsWhiteSpace))
            return false;
        int alpha = value.Count(char.IsLetter);
        if (alpha < 3)
            return false;
        return value.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '#');
    }

    private static bool IsAuxiliaryTexture(string reference)
    {
        string s = reference.Replace('/', '\\').ToLowerInvariant();
        return s.Contains("specular") ||
               s.Contains("normalmap") ||
               s.Contains("bumpmap") ||
               s.Contains("reflectmap") ||
               s.Contains("reflectionmap");
    }

    private static string? ResolveTextureReference(string reference, string mshPath, IEnumerable<GameAsset>? allAssets)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        string? directory = Path.GetDirectoryName(mshPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            string relative = reference.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            string relativeWithExt = Path.HasExtension(relative) ? relative : relative + ".st";
            try
            {
                string candidate = Path.GetFullPath(Path.Combine(directory, relativeWithExt));
                string? match = FindFileCaseInsensitive(candidate);
                if (match is not null)
                    return match;
            }
            catch
            {
                
            }

            string stem = Path.GetFileNameWithoutExtension(reference);
            if (!string.IsNullOrWhiteSpace(stem) && Directory.Exists(directory))
            {
                string? sameDir = Directory.EnumerateFiles(directory, "*.st", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), stem, StringComparison.OrdinalIgnoreCase));
                if (sameDir is not null)
                    return sameDir;
            }
        }

        if (allAssets is not null)
        {
            string stem = Path.GetFileNameWithoutExtension(reference);
            string sourceDirectory = Path.GetDirectoryName(mshPath) ?? string.Empty;
            return allAssets
                .Where(a => string.Equals(a.Extension, ".st", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(Path.GetFileNameWithoutExtension(a.Name), stem, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => CommonPathPrefixLength(sourceDirectory, Path.GetDirectoryName(a.FullPath) ?? string.Empty))
                .Select(a => a.FullPath)
                .FirstOrDefault();
        }

        return null;
    }

    private static string? FindFileCaseInsensitive(string candidate)
    {
        if (File.Exists(candidate))
            return candidate;
        string? dir = Path.GetDirectoryName(candidate);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return null;
        string fileName = Path.GetFileName(candidate);
        return Directory.EnumerateFiles(dir)
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase));
    }

    private static int CommonPathPrefixLength(string left, string right)
    {
        int length = Math.Min(left.Length, right.Length);
        int i = 0;
        while (i < length && char.ToUpperInvariant(left[i]) == char.ToUpperInvariant(right[i]))
            i++;
        return i;
    }

    private static bool ContainsAny(string value, params string[] terms)
        => terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeToken(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static uint ReadU32(byte[] data, int offset)
        => offset >= 0 && offset + 4 <= data.Length
            ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4))
            : uint.MaxValue;
}
