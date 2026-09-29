using GettingUpModTool;
using GettingUpModTool.Core.Animation;
using System.IO;
using GettingUpModTool.Core.Formats.BNM;
using GettingUpModTool.Core.Formats.MSH;

namespace GettingUpModTool.Core.Game;

public sealed class AnimationFamilyInfo
{
    public required string Name { get; init; }
    public required string RelativeDirectory { get; init; }
    public required IReadOnlyList<GameAsset> Animations { get; init; }
    public int AnimationCount => Animations.Count;
}

public sealed class CompatibleMeshInfo
{
    public required GameAsset Asset { get; init; }
    public int BoneCount { get; init; }
    public double SkeletonConfidence { get; init; }
    public int CompatibilityScore { get; init; }
    public int SectionCount { get; init; }
    public string MeshFormats { get; init; } = "";
    public int MeshWarnings { get; init; }
    public bool IsRecommended { get; init; }
    public bool IsSkeletonCompatible { get; init; }
    public bool IsExactSkeletonMatch { get; init; }
    public bool IsToleratedExtraBones { get; init; }
    public bool IsTrackMappedPartial { get; init; }
    public int MappedBoneCount { get; init; }
    public double TrackMappingCoverage { get; init; }
    public string TrackMappingReason { get; init; } = "";
    public int AnimationTrackCount { get; init; }
    public int BoneTrackDelta { get; init; }
    public string AssociationLabel { get; init; } = "";
    public string SkeletonStatusLabel
    {
        get
        {
            if (IsExactSkeletonMatch)
                return BoneCount == AnimationTrackCount
                    ? LocalizationService.T("Exact")
                    : LocalizationService.T("EntityRoot");
            if (IsTrackMappedPartial)
                return string.Format(LocalizationService.T("Partielle ({0}/{1} os)"), MappedBoneCount, BoneCount);
            if (IsToleratedExtraBones)
                return string.Format(LocalizationService.T("Probable (+{0} os)"), BoneTrackDelta);
            return LocalizationService.T("Non confirmé");
        }
    }
    public string MatchGroupLabel => IsRecommended ? LocalizationService.T("Meshes associés") : LocalizationService.T("Autres compatibles");
    public required string Reason { get; init; }

    public string Name => Asset.Name;
    public string RelativePath => Asset.RelativePath;
    public string BoneCountLabel => BoneCount.ToString();
    public string TrackCountLabel => AnimationTrackCount.ToString();
    public string BoneTrackDeltaLabel => BoneTrackDelta > 0 ? $"+{BoneTrackDelta}" : BoneTrackDelta.ToString();
    public string ConfidenceLabel => $"{SkeletonConfidence:P0}";
    public string ScoreLabel => CompatibilityScore.ToString();
    public string SectionsLabel => SectionCount.ToString();
    public string WarningsLabel => MeshWarnings.ToString();
    public string MappingLabel => IsTrackMappedPartial
        ? $"{MappedBoneCount}/{BoneCount} ({TrackMappingCoverage:P0})"
        : IsExactSkeletonMatch ? LocalizationService.T("Complet") : "-";
}

public sealed class BatchValidationRow
{
    public required string Kind { get; init; }
    public required string Name { get; init; }
    public required string RelativePath { get; init; }
    public bool Success { get; init; }
    public string Result { get; init; } = "";
}

public static class FullGameDatabase
{
    public static IReadOnlyList<AnimationFamilyInfo> BuildAnimationFamilies(IEnumerable<GameAsset> assets)
    {
        return assets
            .Where(a => a.Kind == GameAssetKind.Animation)
            .Select(a => new { Asset = a, Family = GetAnimationFamily(a.RelativePath) })
            .Where(x => !string.IsNullOrWhiteSpace(x.Family))
            .GroupBy(x => x.Family!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new AnimationFamilyInfo
            {
                Name = g.Key,
                RelativeDirectory = FindFamilyDirectory(g.Select(x => x.Asset)),
                Animations = g.Select(x => x.Asset)
                    .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .OrderByDescending(f => f.AnimationCount)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string? GetAnimationFamily(string relativePath)
    {
        string[] parts = SplitPath(relativePath);
        for (int i = 0; i + 1 < parts.Length; i++)
        {
            if (parts[i].Equals("Animation", StringComparison.OrdinalIgnoreCase))
                return parts[i + 1];
        }
        return null;
    }

    public static bool IsCharacterMesh(GameAsset asset)
    {
        if (asset.Kind != GameAssetKind.Mesh)
            return false;

        string[] parts = SplitPath(asset.RelativePath);
        for (int i = 0; i + 1 < parts.Length; i++)
        {
            if (parts[i].Equals("Meshes", StringComparison.OrdinalIgnoreCase) &&
                parts[i + 1].Equals("Chars", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static IReadOnlyList<CompatibleMeshInfo> FindCompatibleMeshes(
        GameAsset animationAsset,
        IEnumerable<GameAsset> allAssets,
        CancellationToken cancellationToken = default)
    {
        if (animationAsset.Kind != GameAssetKind.Animation)
            throw new ArgumentException("L'asset doit être un BNM.", nameof(animationAsset));

        List<GameAsset> allAssetList = allAssets.ToList();
        BnmAnimation animation = BnmReader.Read(animationAsset.FullPath);
        int expectedBonesWithoutRoot = Math.Max(0, animation.TrackCount - 1);
        int expectedBonesDirect = animation.TrackCount;
        string family = GetAnimationFamily(animationAsset.RelativePath) ?? "";
        AnimationMeshProfile? familyProfile = AnimationMeshProfiles.Get(family);

        List<GameAsset> characterMeshes = allAssetList.Where(IsCharacterMesh).ToList();
        if (characterMeshes.Count == 0)
            characterMeshes = allAssetList.Where(a => a.Kind == GameAssetKind.Mesh).ToList();

        var result = new List<CompatibleMeshInfo>();
        foreach (GameAsset meshAsset in characterMeshes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                byte[] data = File.ReadAllBytes(meshAsset.FullPath);
                var skeleton = MshSkeletonDetector.Detect(data);
                if (skeleton is null)
                    continue;

                bool directTrackMatch = skeleton.Bones.Count == expectedBonesDirect;
                bool minusRootMatch = skeleton.Bones.Count == expectedBonesWithoutRoot;

                string haystack = (meshAsset.Name + " " + meshAsset.RelativePath).ToLowerInvariant();
                bool profileMatch = familyProfile is not null && !familyProfile.IsGenericFamily &&
                    AnimationMeshProfiles.Matches(familyProfile, meshAsset);
                bool tokenFamilyMatch = familyProfile is null && NameTokens(family)
                    .Any(token => haystack.Contains(token, StringComparison.OrdinalIgnoreCase));
                bool familyMatch = profileMatch || tokenFamilyMatch;

                
                
                AnimationSkeletonBinding? trackBinding = null;
                bool forceTrackIdProfile = familyMatch && string.Equals(family, "Trane", StringComparison.OrdinalIgnoreCase);
                if (familyMatch && (forceTrackIdProfile || (!directTrackMatch && !minusRootMatch)))
                {
                    trackBinding = AnimationSkeletonBindingResolver.Resolve(
                        animation,
                        skeleton,
                        animationAsset.FullPath,
                        allAssetList,
                        cancellationToken);
                }
                bool mappedPartial = trackBinding?.IsPartial == true;
                bool mappedUsable = trackBinding?.IsUsable == true;

                
                
                int closestExpected = Math.Abs(skeleton.Bones.Count - expectedBonesDirect) <= Math.Abs(skeleton.Bones.Count - expectedBonesWithoutRoot)
                    ? expectedBonesDirect
                    : expectedBonesWithoutRoot;
                int boneTrackDelta = skeleton.Bones.Count - closestExpected;
                bool toleratedExtraBones = familyMatch && !mappedPartial && !directTrackMatch && !minusRootMatch &&
                    boneTrackDelta > 0 && boneTrackDelta <= 8;
                bool skeletonCompatible = forceTrackIdProfile
                    ? mappedUsable
                    : directTrackMatch || minusRootMatch || mappedPartial || toleratedExtraBones;

                
                
                
                if (!skeletonCompatible && !familyMatch)
                    continue;

                MshStructureAnalysis meshAnalysis = MshStructureAnalyzer.Analyze(data, skeleton);
                if (meshAnalysis.Sections.Count == 0)
                    continue;

                int score = skeletonCompatible ? 50 : 5;
                var reasons = new List<string>
                {
                    forceTrackIdProfile && mappedUsable
                        ? trackBinding!.Reason
                        : directTrackMatch
                        ? $"{skeleton.Bones.Count} os = {animation.TrackCount} tracks (exact)"
                        : minusRootMatch
                            ? $"{skeleton.Bones.Count} os = {animation.TrackCount} tracks - EntityRoot"
                            : mappedPartial
                                ? $"clip partiel remappé : {trackBinding!.MappedBoneCount}/{skeleton.Bones.Count} os via TrackId"
                                : toleratedExtraBones
                                    ? $"{skeleton.Bones.Count} os / {animation.TrackCount} tracks : +{boneTrackDelta} os supplémentaires tolérés pour la famille {family}"
                                    : $"association famille confirmée, mais {skeleton.Bones.Count} os pour {animation.TrackCount} tracks",
                    $"{meshAnalysis.Sections.Count} section(s) mesh"
                };

                if (forceTrackIdProfile && mappedUsable)
                {
                    score += 30;
                    reasons.Add($"profil TrackId Trane : {trackBinding!.MappedBoneCount}/{skeleton.Bones.Count} os");
                }
                else if (directTrackMatch)
                {
                    score += 10;
                    reasons.Add("nombre d'os identique au nombre de tracks");
                }
                else if (mappedPartial)
                {
                    score += 24;
                    reasons.Add($"mapping TrackId : {trackBinding!.MappedBoneCount}/{skeleton.Bones.Count} os ({trackBinding.SelectedTrackCoverage:P0} des tracks du clip)");
                }
                else if (toleratedExtraBones)
                {
                    score += 6;
                    reasons.Add($"compatibilité probable : {boneTrackDelta} os supplémentaires");
                }
                else if (!skeletonCompatible)
                {
                    score -= 25;
                    reasons.Add("squelette à vérifier avant chargement");
                }

                if (profileMatch)
                {
                    score += 180;
                    reasons.Add($"profil jeu : mesh associé à la famille « {family} »");
                }
                else if (tokenFamilyMatch)
                {
                    score += 100;
                    reasons.Add($"nom/dossier correspond à la famille « {family} »");
                }

                string animationStem = Path.GetFileNameWithoutExtension(animationAsset.Name);
                string prefix = animationStem.Split('_', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                if (prefix.Length >= 2 && haystack.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    score += 8;
                    reasons.Add($"préfixe {prefix}");
                }

                score += (int)Math.Round(skeleton.Confidence * 15.0);

                result.Add(new CompatibleMeshInfo
                {
                    Asset = meshAsset,
                    BoneCount = skeleton.Bones.Count,
                    SkeletonConfidence = skeleton.Confidence,
                    CompatibilityScore = score,
                    SectionCount = meshAnalysis.Sections.Count,
                    MeshFormats = string.Join(", ", meshAnalysis.Sections
                        .GroupBy(x => x.FormatLabel)
                        .Select(g => g.Count() == 1 ? g.Key : $"{g.Key}×{g.Count()}")),
                    MeshWarnings = meshAnalysis.WarningCount + meshAnalysis.ErrorCount,
                    IsRecommended = familyMatch,
                    IsSkeletonCompatible = skeletonCompatible,
                    IsExactSkeletonMatch = forceTrackIdProfile
                        ? mappedUsable && trackBinding!.MappedBoneCount >= skeleton.Bones.Count - 1
                        : directTrackMatch || minusRootMatch,
                    IsToleratedExtraBones = toleratedExtraBones,
                    IsTrackMappedPartial = mappedPartial,
                    MappedBoneCount = trackBinding?.MappedBoneCount ?? 0,
                    TrackMappingCoverage = trackBinding?.SelectedTrackCoverage ?? 0,
                    TrackMappingReason = trackBinding?.Reason ?? "",
                    AnimationTrackCount = animation.TrackCount,
                    BoneTrackDelta = skeleton.Bones.Count - animation.TrackCount,
                    AssociationLabel = profileMatch
                        ? (skeletonCompatible ? LocalizationService.T("Associé") : LocalizationService.T("Associé · à vérifier"))
                        : tokenFamilyMatch
                            ? LocalizationService.T("Nom correspondant")
                            : LocalizationService.T("Compatible squelette"),
                    Reason = LocalizationService.T(string.Join("; ", reasons))
                });
            }
            catch
            {
                
            }
        }

        return result
            .OrderByDescending(x => x.IsRecommended)
            .ThenByDescending(x => x.IsSkeletonCompatible)
            .ThenByDescending(x => x.CompatibilityScore)
            .ThenByDescending(x => x.SkeletonConfidence)
            .ThenBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<BatchValidationRow> ValidateBnms(
        IEnumerable<GameAsset> assets,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<BatchValidationRow>();
        foreach (GameAsset asset in assets.Where(a => a.Kind == GameAssetKind.Animation))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                BnmAnimation clip = BnmReader.Read(asset.FullPath);
                int rotOk = clip.Tracks.Count(t => t.RotationDecoded || t.RotationKeyCount == 0);
                int transOk = clip.Tracks.Count(t => t.TranslationDecoded || t.TranslationKeyCount == 0);
                rows.Add(new BatchValidationRow
                {
                    Kind = "BNM",
                    Name = asset.Name,
                    RelativePath = asset.RelativePath,
                    Success = true,
                    Result = $"{clip.FrameCount}f @ {clip.FramesPerSecond} FPS; {clip.TrackCount} tracks; R {rotOk}/{clip.TrackCount}; T {transOk}/{clip.TrackCount}"
                });
            }
            catch (Exception ex)
            {
                rows.Add(new BatchValidationRow
                {
                    Kind = "BNM",
                    Name = asset.Name,
                    RelativePath = asset.RelativePath,
                    Success = false,
                    Result = ex.Message
                });
            }
        }
        return rows;
    }

    public static IReadOnlyList<BatchValidationRow> ValidateCharacterMeshes(
        IEnumerable<GameAsset> assets,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<BatchValidationRow>();
        foreach (GameAsset asset in assets.Where(IsCharacterMesh))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                byte[] data = File.ReadAllBytes(asset.FullPath);
                var skeleton = MshSkeletonDetector.Detect(data);
                MshStructureAnalysis analysis = MshStructureAnalyzer.Analyze(data, skeleton);
                bool ok = skeleton is not null && analysis.Sections.Count > 0 && analysis.ErrorCount == 0;
                string formats = analysis.Sections.Count == 0
                    ? "aucun"
                    : string.Join(",", analysis.Sections.Select(x => x.VertexStride).Distinct().OrderBy(x => x));
                rows.Add(new BatchValidationRow
                {
                    Kind = "MSH",
                    Name = asset.Name,
                    RelativePath = asset.RelativePath,
                    Success = ok,
                    Result = $"squelette={(skeleton is null ? "non" : skeleton.Bones.Count + " os")}; sections={analysis.Sections.Count}; stride={formats}; " +
                             $"verts={analysis.TotalVertices}; tris={analysis.TotalTriangles}; diag={analysis.ErrorCount}E/{analysis.WarningCount}W"
                });
            }
            catch (Exception ex)
            {
                rows.Add(new BatchValidationRow
                {
                    Kind = "MSH",
                    Name = asset.Name,
                    RelativePath = asset.RelativePath,
                    Success = false,
                    Result = ex.Message
                });
            }
        }
        return rows;
    }

    public static IReadOnlyList<BatchValidationRow> ValidateCharacterMaterials(
        IEnumerable<GameAsset> assets,
        CancellationToken cancellationToken = default)
    {
        List<GameAsset> all = assets.ToList();
        var rows = new List<BatchValidationRow>();
        foreach (GameAsset asset in all.Where(IsCharacterMesh))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                byte[] data = File.ReadAllBytes(asset.FullPath);
                var skeleton = MshSkeletonDetector.Detect(data);
                MshStructureAnalysis mesh = MshStructureAnalyzer.Analyze(data, skeleton);
                MshMaterialAnalysis materials = MshMaterialAnalyzer.Analyze(data, mesh.Sections, asset.FullPath, all);
                int resolved = materials.Bindings.Count(x => x.ResolvedTexturePath is not null);
                bool complete = mesh.Sections.Count > 0 &&
                                materials.Bindings.Count == mesh.Sections.Count &&
                                resolved == materials.Bindings.Count;
                rows.Add(new BatchValidationRow
                {
                    Kind = "MAT",
                    Name = asset.Name,
                    RelativePath = asset.RelativePath,
                    Success = complete,
                    Result = $"sections={mesh.Sections.Count}; matériaux={materials.Bindings.Count}; textures={resolved}/{materials.Bindings.Count}; " +
                             $"variantes masquées={materials.HiddenCount}; sans mapping={materials.UnmappedSectionCount}"
                });
            }
            catch (Exception ex)
            {
                rows.Add(new BatchValidationRow
                {
                    Kind = "MAT",
                    Name = asset.Name,
                    RelativePath = asset.RelativePath,
                    Success = false,
                    Result = ex.Message
                });
            }
        }
        return rows;
    }

    private static string FindFamilyDirectory(IEnumerable<GameAsset> assets)
    {
        GameAsset? first = assets.FirstOrDefault();
        return first?.DirectoryRelativePath ?? "";
    }

    private static string[] SplitPath(string path)
        => path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IEnumerable<string> NameTokens(string family)
    {
        if (string.IsNullOrWhiteSpace(family))
            yield break;

        string normalized = new string(family.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        if (normalized.Length >= 2)
            yield return normalized;

        
        if (family.Equals("Dog", StringComparison.OrdinalIgnoreCase)) yield return "dog";
        if (family.Equals("Rat", StringComparison.OrdinalIgnoreCase)) yield return "rat";
        if (family.Equals("Trane", StringComparison.OrdinalIgnoreCase)) yield return "trane";
        if (family.Equals("ShannaRay", StringComparison.OrdinalIgnoreCase)) yield return "shanna";
        if (family.Equals("WhiteMike", StringComparison.OrdinalIgnoreCase)) yield return "whitemike";
        if (family.Equals("MeatWorker", StringComparison.OrdinalIgnoreCase)) yield return "meat";
        if (family.Equals("WorkBum", StringComparison.OrdinalIgnoreCase)) yield return "workbum";
        if (family.Equals("SecurityGuard", StringComparison.OrdinalIgnoreCase)) yield return "security";
        if (family.Equals("PoliceChief", StringComparison.OrdinalIgnoreCase)) yield return "police";
    }
}
