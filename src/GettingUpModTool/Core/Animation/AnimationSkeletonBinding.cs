using System.IO;
using System.Numerics;
using System.Collections.Concurrent;
using GettingUpModTool.Core.Formats.BNM;
using GettingUpModTool.Core.Game;
using GettingUpModTool.Core.Models;

namespace GettingUpModTool.Core.Animation;

public sealed class AnimationSkeletonBinding
{
    public int EntityRootTrackIndex { get; init; } = -1;
    public int[] BoneTrackIndices { get; init; } = Array.Empty<int>();
    public int MappedBoneCount { get; init; }
    public int SourceTrackCount { get; init; }
    public int BoneCount => BoneTrackIndices.Length;
    public double SelectedTrackCoverage { get; init; }
    public string Mode { get; init; } = "Incompatible";
    public string ReferenceAnimationName { get; init; } = "";
    public string Reason { get; init; } = "";
    
    
    public bool UseStandardQuaternionMatrix { get; init; }
    public bool UseReferencePoseRetarget { get; init; }
    public Quaternion?[]? ReferenceBoneRotations { get; init; }
    public Vector3?[]? ReferenceBoneTranslations { get; init; }
    public string BoneMaskName { get; init; } = "EntireBody";

    public bool IsUsable => MappedBoneCount > 0 && BoneTrackIndices.Length > 0;
    public bool IsComplete => IsUsable && MappedBoneCount == BoneTrackIndices.Length;
    public bool IsPartial => IsUsable && !IsComplete;

    public int TrackIndexForBone(int boneIndex)
        => boneIndex >= 0 && boneIndex < BoneTrackIndices.Length ? BoneTrackIndices[boneIndex] : -1;

    public int BoneIndexForTrack(int trackIndex)
    {
        for (int i = 0; i < BoneTrackIndices.Length; i++)
        {
            if (BoneTrackIndices[i] == trackIndex)
                return i;
        }
        return -1;
    }
}
public static class AnimationSkeletonBindingResolver
{
    private sealed record BnmSignature(string Name, int TrackCount, ushort[] TrackIds);

    private static readonly ConcurrentDictionary<string, BnmSignature> SignatureCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> InvalidSignatureCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, AnimationSkeletonBinding> BindingCache = new(StringComparer.OrdinalIgnoreCase);

    public static AnimationSkeletonBinding Resolve(
        BnmAnimation animation,
        SkeletonData skeleton,
        string? animationPath,
        IEnumerable<GameAsset> allAssets,
        CancellationToken cancellationToken = default)
    {
        int boneCount = skeleton.Bones.Count;
        if (boneCount <= 0)
            return Incompatible(animation, boneCount, "Squelette vide.");

        string? family = ResolveFamily(animationPath, animation, allAssets);

        
        if (string.Equals(family, "Trane", StringComparison.OrdinalIgnoreCase))
        {
            AnimationSkeletonBinding? trane = TryResolveTrane(animation, skeleton, animationPath, allAssets);
            if (trane is not null)
                return trane;       
            
            return Incompatible(animation, boneCount,
                "Signature Trane spécialisée/non décodée : preview squelettique neutralisée pour éviter un faux remapping.");
        }
        if (animation.TrackCount == boneCount + 1)
        {
            return new AnimationSkeletonBinding
            {
                EntityRootTrackIndex = 0,
                BoneTrackIndices = Enumerable.Range(1, boneCount).ToArray(),
                MappedBoneCount = boneCount,
                SourceTrackCount = animation.TrackCount,
                SelectedTrackCoverage = 1.0,
                Mode = "Exact",
                Reason = $"{animation.TrackCount} tracks = EntityRoot + {boneCount} os."
            };
        }
        if (animation.TrackCount == boneCount)
        {
            return new AnimationSkeletonBinding
            {
                EntityRootTrackIndex = -1,
                BoneTrackIndices = Enumerable.Range(0, boneCount).ToArray(),
                MappedBoneCount = boneCount,
                SourceTrackCount = animation.TrackCount,
                SelectedTrackCoverage = 1.0,
                Mode = "Exact sans EntityRoot",
                Reason = $"{animation.TrackCount} tracks = {boneCount} os."
            };
        }

        if (string.IsNullOrWhiteSpace(family))
            return Incompatible(animation, boneCount, "Famille d'animation inconnue : impossible de rechercher un clip de référence.");

        string skeletonSignature = BuildSkeletonSignature(skeleton);
        string cacheKey = string.Join("|", family, boneCount, skeletonSignature, animationPath ?? animation.FileName,
            string.Join(",", animation.Tracks.Select(t => t.TrackId.ToString("X4"))));
        if (BindingCache.TryGetValue(cacheKey, out AnimationSkeletonBinding? cached))
            return cached;

        List<GameAsset> familyAssets = allAssets
            .Where(a => a.Kind == GameAssetKind.Animation)
            .Where(a => string.Equals(FullGameDatabase.GetAnimationFamily(a.RelativePath), family, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => IsLikelyFullBodyClip(a.Name))
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        HashSet<ushort> selectedIds = animation.Tracks.Select(t => t.TrackId).ToHashSet();
        AnimationSkeletonBinding? best = null;
        double bestScore = double.NegativeInfinity;
        int examinedReferences = 0;
        int scannedCandidates = 0;

        foreach (GameAsset candidate in familyAssets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++scannedCandidates > 260)
                break;
            if (!string.IsNullOrWhiteSpace(animationPath) &&
                string.Equals(Path.GetFullPath(candidate.FullPath), Path.GetFullPath(animationPath), StringComparison.OrdinalIgnoreCase))
                continue;

            BnmSignature? signature = ReadSignature(candidate.FullPath);
            if (signature is null)
                continue;

            bool hasEntityRoot;
            if (signature.TrackCount == boneCount + 1)
                hasEntityRoot = true;
            else if (signature.TrackCount == boneCount)
                hasEntityRoot = false;
            else
                continue;

            examinedReferences++;
            Dictionary<ushort, int> referenceById = BuildUniqueIndex(signature.TrackIds);
            if (referenceById.Count == 0)
                continue;

            int[] map = Enumerable.Repeat(-1, boneCount).ToArray();
            int entityRootTrack = -1;
            int mapped = 0;
            int selectedMatched = 0;

            for (int selectedTrackIndex = 0; selectedTrackIndex < animation.Tracks.Count; selectedTrackIndex++)
            {
                ushort id = animation.Tracks[selectedTrackIndex].TrackId;
                if (!referenceById.TryGetValue(id, out int referenceTrackIndex))
                    continue;

                selectedMatched++;
                if (hasEntityRoot && referenceTrackIndex == 0)
                {
                    entityRootTrack = selectedTrackIndex;
                    continue;
                }

                int boneIndex = hasEntityRoot ? referenceTrackIndex - 1 : referenceTrackIndex;
                if (boneIndex < 0 || boneIndex >= boneCount || map[boneIndex] >= 0)
                    continue;

                map[boneIndex] = selectedTrackIndex;
                mapped++;
            }
 
            int nonRootTracks = animation.TrackCount - (entityRootTrack >= 0 ? 1 : 0);
            double selectedCoverage = nonRootTracks > 0 ? mapped / (double)nonRootTracks : 0.0;
            double boneCoverage = mapped / (double)boneCount;
            double idOverlap = selectedIds.Count > 0
                ? selectedIds.Count(id => referenceById.ContainsKey(id)) / (double)selectedIds.Count
                : 0.0;

            double score = selectedCoverage * 60.0 + boneCoverage * 30.0 + idOverlap * 10.0;
            if (hasEntityRoot && entityRootTrack >= 0)
                score += 5.0;

            bool reliable = mapped >= Math.Min(6, boneCount) && selectedCoverage >= 0.72 && idOverlap >= 0.72;
            if (!reliable || score <= bestScore)
                continue;

            bestScore = score;
            best = new AnimationSkeletonBinding
            {
                EntityRootTrackIndex = entityRootTrack,
                BoneTrackIndices = map,
                MappedBoneCount = mapped,
                SourceTrackCount = animation.TrackCount,
                SelectedTrackCoverage = selectedCoverage,
                Mode = mapped == boneCount ? "Remappée" : "Partielle remappée",
                ReferenceAnimationName = signature.Name,
                Reason = $"{mapped}/{boneCount} os mappés par TrackId ({selectedCoverage:P0} des tracks du clip), référence {signature.Name}."
            };

            
            if (selectedCoverage >= 0.98 && mapped >= Math.Min(animation.TrackCount - 1, boneCount))
                break;

            
            
            if (examinedReferences >= 48 && best is not null)
                break;
        }

        AnimationSkeletonBinding resolved = best ?? Incompatible(
            animation,
            boneCount,
            examinedReferences == 0
                ? $"Aucun clip complet de référence ({boneCount}/{boneCount + 1} tracks) trouvé dans {family}."
                : $"Les TrackId ne correspondent pas suffisamment aux {examinedReferences} clip(s) complet(s) de référence testés dans {family}.");

        BindingCache[cacheKey] = resolved;
        return resolved;
    }
    private static readonly IReadOnlyDictionary<ushort, string> TraneTrackToBone =
        new Dictionary<ushort, string>
        {
            [0x001B] = "root",
            [0x001C] = "Pelvis",
            [0x001D] = "Spine",
            [0x001E] = "Spine1",
            [0x001F] = "Spine2",
            [0x0020] = "Neck",
            [0x0021] = "Head",
            [0x0582] = "Jaw",
            [0x0022] = "LipLowerRt",
            [0x0023] = "LipLowerLft",
            [0x0024] = "Tongue",
            [0x0025] = "LipCornerLft",
            [0x0026] = "LipCornerRt",
            [0x0027] = "LipUpperLft",
            [0x0028] = "LipUpperRt",
            [0x0029] = "Cheeks",
            [0x002A] = "Eyes",
            [0x002B] = "EyeLids",
            [0x005F] = "Brow",
            [0x002C] = "L Clavicle",
            [0x002D] = "L UpperArm",
            [0x002E] = "L Forearm",
            [0x002F] = "L Hand",
            [0x0030] = "L Finger0",
            [0x0031] = "L Finger01",
            [0x0032] = "L Finger1",
            [0x0033] = "L Finger11",
            [0x0034] = "L Finger2",
            [0x0035] = "L Finger21",
            [0x01E0] = "R Clavicle",
            [0x05A1] = "R UpperArm",
            [0x05A4] = "R Forearm",
            [0x05A0] = "R Hand",
            [0x05A2] = "R Finger0",
            [0x0067] = "R Finger01",
            [0x0567] = "R Finger1",
            [0x0036] = "R Finger11",
            [0x0037] = "R Finger2",
            [0x0038] = "R Finger21",
            [0x0039] = "L Thigh",
            [0x005D] = "L Calf",
            [0x003A] = "L Foot",
            [0x003B] = "L Toe0",
            [0x003C] = "R Thigh",
            [0x003D] = "R Calf",
            [0x0065] = "R Foot",
            [0x0063] = "R Toe0"
        };
    private static AnimationSkeletonBinding? TryResolveTrane(BnmAnimation animation, SkeletonData skeleton, string? animationPath, IEnumerable<GameAsset> allAssets)
    {   
        if (animation.Name.Contains("MeshAnim_", StringComparison.OrdinalIgnoreCase) ||
            animation.FileName.Contains("MeshAnim_", StringComparison.OrdinalIgnoreCase))
        {
            return Incompatible(animation, skeleton.Bones.Count,
                "Animation faciale/mesh Trane : les cibles non squelettiques ne sont pas encore décodées.");
        }

        var boneByName = skeleton.Bones
            .Select((bone, index) => (bone.Name, index))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().index, StringComparer.OrdinalIgnoreCase);

        int[] map = Enumerable.Repeat(-1, skeleton.Bones.Count).ToArray();
        int entityRoot = -1;
        int mapped = 0;
        int knownSelected = 0;

        for (int trackIndex = 0; trackIndex < animation.Tracks.Count; trackIndex++)
        {
            ushort trackId = animation.Tracks[trackIndex].TrackId;
            if (trackId == 0x001A)
            {
                entityRoot = trackIndex;
                continue;
            }

            if (!TraneTrackToBone.TryGetValue(trackId, out string? boneName))
                continue;
            knownSelected++;

            if (!boneByName.TryGetValue(boneName, out int boneIndex) || map[boneIndex] >= 0)
                continue;

            map[boneIndex] = trackIndex;
            mapped++;
        }

        int nonRootTracks = animation.TrackCount - (entityRoot >= 0 ? 1 : 0);

        double coverage = nonRootTracks > 0 ? mapped / (double)nonRootTracks : 0.0;
        
        int facialSuppressed = TraneBoneGroups.ApplyFacialGuard(map, skeleton);
        if (facialSuppressed > 0)
            mapped -= facialSuppressed;

        string boneMaskName = TraneBoneGroups.DetectBodyMask(map, skeleton);
        int maskedBones = TraneBoneGroups.ApplyMask(map, skeleton, boneMaskName);
        if (maskedBones > 0)
            mapped -= maskedBones;

        
        int prunedDescendants = PruneOrphanedTraneBranches(map, skeleton);
        if (prunedDescendants > 0)
            mapped -= prunedDescendants;
    
        double coreCoverage = TraneBoneGroups.ComputeCoreCoverage(map, skeleton, boneMaskName);
        if (entityRoot < 0 || mapped < 12 || coreCoverage < 0.80)
            return null;

        Quaternion?[]? referenceRotations = null;
        Vector3?[]? referenceTranslations = null;
        string referenceName = "Profil TrackId Trane";
        if (TryLoadTraneReferencePose(animationPath, allAssets, skeleton, out Quaternion?[] rotations, out Vector3?[] translations, out string? loadedReferenceName))
        {
            referenceRotations = rotations;
            referenceTranslations = translations;
            referenceName = loadedReferenceName ?? "TR_Idle";
        }
        
        if (referenceRotations is null)
        {
            return Incompatible(animation, skeleton.Bones.Count,
                "Profil Trane reconnu, mais TR_Idle.bnm est introuvable. Place TR_Idle à côté du clip ou scanne la bibliothèque du jeu avant la preview.");
        }

        int unmappedSkeletonBones = skeleton.Bones.Count - mapped;
        string mode = unmappedSkeletonBones <= 1 ? "Trane exact" : "Trane partiel";
        return new AnimationSkeletonBinding
        {
            EntityRootTrackIndex = entityRoot,
            BoneTrackIndices = map,
            MappedBoneCount = mapped,
            SourceTrackCount = animation.TrackCount,
            SelectedTrackCoverage = coverage,
            Mode = mode,
            ReferenceAnimationName = referenceName,
            Reason = referenceRotations is not null
                ? $"Profil Trane : {mapped}/{skeleton.Bones.Count} os appliqués · masque {boneMaskName} · structure {coreCoverage:P0}. Rotations retargetées par rapport à {referenceName}; visage neutralisé ({facialSuppressed} os) et zones exclues laissées en bind pose" +
                  (prunedDescendants > 0 ? $" ({prunedDescendants} descendant(s) supplémentaire(s) gelé(s))." : ".")
                : $"Profil Trane : EntityRoot TrackId 0x001A, {mapped}/{skeleton.Bones.Count} os appliqués par TrackId + nom ({coverage:P0} des tracks reconnus, structure {coreCoverage:P0}) · masque {boneMaskName}; visage neutralisé ({facialSuppressed} os)" +
                  (prunedDescendants > 0 ? $"; {prunedDescendants} descendant(s) gelé(s) car leur chaîne parentale est partielle." : "."),
            
            UseStandardQuaternionMatrix = false,
            UseReferencePoseRetarget = referenceRotations is not null,
            ReferenceBoneRotations = referenceRotations,
            ReferenceBoneTranslations = referenceTranslations,
            BoneMaskName = boneMaskName
        };
    }
    private static int PruneOrphanedTraneBranches(int[] map, SkeletonData skeleton)
    {
        int pruned = 0;
        for (int boneIndex = 0; boneIndex < skeleton.Bones.Count; boneIndex++)
        {
            if (map[boneIndex] < 0)
                continue;

            int parent = skeleton.Bones[boneIndex].ParentIndex;
            bool orphaned = false;
            int guard = 0;
            while (parent >= 0 && parent < skeleton.Bones.Count && guard++ < skeleton.Bones.Count)
            {
                
                
                if (map[parent] < 0)
                {
                    orphaned = true;
                    break;
                }
                parent = skeleton.Bones[parent].ParentIndex;
            }

            if (!orphaned)
                continue;

            map[boneIndex] = -1;
            pruned++;
        }
        return pruned;
    }
    private static bool TryLoadTraneReferencePose(
        string? animationPath,
        IEnumerable<GameAsset> allAssets,
        SkeletonData skeleton,
        out Quaternion?[] rotations,
        out Vector3?[] translations,
        out string? referenceName)
    {
        rotations = new Quaternion?[skeleton.Bones.Count];
        translations = new Vector3?[skeleton.Bones.Count];
        referenceName = null;

        string? referencePath = null;
        if (!string.IsNullOrWhiteSpace(animationPath))
        {
            string? directory = Path.GetDirectoryName(animationPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                string sibling = Path.Combine(directory, "TR_Idle.bnm");
                if (File.Exists(sibling))
                    referencePath = sibling;
            }
        }

        if (referencePath is null)
        {
            GameAsset? idle = allAssets
                .Where(a => a.Kind == GameAssetKind.Animation)
                .FirstOrDefault(a =>
                    string.Equals(FullGameDatabase.GetAnimationFamily(a.RelativePath), "Trane", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Path.GetFileName(a.FullPath), "TR_Idle.bnm", StringComparison.OrdinalIgnoreCase));
            referencePath = idle?.FullPath;
        }

        if (string.IsNullOrWhiteSpace(referencePath) || !File.Exists(referencePath))
            return false;

        try
        {
            BnmAnimation reference = BnmReader.Read(referencePath);
            var boneByName = skeleton.Bones
                .Select((bone, index) => (bone.Name, index))
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().index, StringComparer.OrdinalIgnoreCase);

            int found = 0;
            foreach (BnmTrack track in reference.Tracks)
            {
                if (track.TrackId == 0x001A || !TraneTrackToBone.TryGetValue(track.TrackId, out string? boneName))
                    continue;
                if (!boneByName.TryGetValue(boneName, out int boneIndex))
                    continue;

                Quaternion? q = BnmSampler.SampleRotation(track, 0f);
                Vector3? t = BnmSampler.SampleTranslation(track, 0f);
                if (q is not null)
                    rotations[boneIndex] = q;
                if (t is not null)
                    translations[boneIndex] = t;
                if (q is not null || t is not null)
                    found++;
            }

            if (found < 12)
                return false;

            referenceName = reference.Name;
            return true;
        }
        catch
        {
            return false;
        }
    }
    private static string? ResolveFamily(string? animationPath, BnmAnimation animation, IEnumerable<GameAsset> assets)
    {
        if (!string.IsNullOrWhiteSpace(animationPath))
        {
            try
            {
                string full = Path.GetFullPath(animationPath);
                GameAsset? asset = assets.FirstOrDefault(a => a.Kind == GameAssetKind.Animation &&
                    string.Equals(Path.GetFullPath(a.FullPath), full, StringComparison.OrdinalIgnoreCase));
                if (asset is not null)
                {
                    string? scannedFamily = FullGameDatabase.GetAnimationFamily(asset.RelativePath);
                    if (!string.IsNullOrWhiteSpace(scannedFamily))
                        return scannedFamily;
                }
            }
            catch
            {
            }
        }
        
        string fileName = !string.IsNullOrWhiteSpace(animationPath)
            ? Path.GetFileName(animationPath)
            : animation.FileName;
        string animationName = animation.Name ?? string.Empty;
        if (LooksLikeTraneAnimationName(fileName) || LooksLikeTraneAnimationName(animationName))
            return "Trane";

        if (!string.IsNullOrWhiteSpace(animationPath))
        {
            string normalized = animationPath.Replace('/', '\\');
            if (normalized.Contains("\\Animation\\Trane\\", StringComparison.OrdinalIgnoreCase))
                return "Trane";
        }

        return null;
    }

    private static bool LooksLikeTraneAnimationName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        string file = Path.GetFileNameWithoutExtension(name);
        return file.StartsWith("TR_", StringComparison.OrdinalIgnoreCase) ||
               file.StartsWith("Trane_", StringComparison.OrdinalIgnoreCase) ||
               file.StartsWith("L_TR_", StringComparison.OrdinalIgnoreCase);
    }
    private static string BuildSkeletonSignature(SkeletonData skeleton)
    {
        
        unchecked
        {
            uint hash = 2166136261;
            foreach (BoneData bone in skeleton.Bones)
            {
                foreach (char c in bone.Name ?? string.Empty)
                {
                    hash ^= char.ToUpperInvariant(c);
                    hash *= 16777619;
                }

                hash ^= (uint)(bone.ParentIndex + 2);
                hash *= 16777619;
            }
            return hash.ToString("X8");
        }
    }

    private static BnmSignature? ReadSignature(string path)
    {
        if (SignatureCache.TryGetValue(path, out BnmSignature? cached))
            return cached;
        if (InvalidSignatureCache.ContainsKey(path))
            return null;

        try
        {
            BnmAnimation clip = BnmReader.Read(path);
            var signature = new BnmSignature(clip.Name, clip.TrackCount, clip.Tracks.Select(t => t.TrackId).ToArray());
            SignatureCache[path] = signature;
            return signature;
        }
        catch
        {
            InvalidSignatureCache[path] = 1;
            return null;
        }
    }
    private static Dictionary<ushort, int> BuildUniqueIndex(IReadOnlyList<ushort> ids)
    {
        var counts = new Dictionary<ushort, int>();
        foreach (ushort id in ids)
            counts[id] = counts.TryGetValue(id, out int n) ? n + 1 : 1;

        var result = new Dictionary<ushort, int>();
        for (int i = 0; i < ids.Count; i++)
        {
            if (counts[ids[i]] == 1)
                result[ids[i]] = i;
        }
        return result;
    }

    private static bool IsLikelyFullBodyClip(string name)
    {
        string n = name.ToLowerInvariant();
        return n.Contains("idle") || n.Contains("walk") || n.Contains("run") || n.Contains("combat") ||
               n.Contains("stalk") || n.Contains("stand") || n.Contains("locom") || n.Contains("ready");
    }

    private static AnimationSkeletonBinding Incompatible(BnmAnimation animation, int boneCount, string reason)
        => new()
        {
            EntityRootTrackIndex = -1,
            BoneTrackIndices = Enumerable.Repeat(-1, Math.Max(0, boneCount)).ToArray(),
            MappedBoneCount = 0,
            SourceTrackCount = animation.TrackCount,
            SelectedTrackCoverage = 0,
            Mode = "Incompatible",
            Reason = reason
        };
}
