using System.Numerics;
using GettingUpModTool.Core.Formats.BNM;
using GettingUpModTool.Core.Models;

namespace GettingUpModTool.Core.Animation;

public sealed class AnimationPose
{
    public float Frame { get; init; }
    public List<Matrix4x4> BoneWorldMatrices { get; } = [];
    public MeshData? SkinnedMesh { get; init; }
    public Matrix4x4 EntityRootMatrix { get; init; } = Matrix4x4.Identity;
}
public static class AnimationPoseEvaluator
{
    public static AnimationPose Evaluate(
        MeshData mesh,
        SkeletonData skeleton,
        BnmAnimation animation,
        float frame,
        bool skinMesh,
        bool applyEntityRoot,
        AnimationSkeletonBinding? binding = null)
    {
        int boneCount = skeleton.Bones.Count;
        var worlds = new Matrix4x4[boneCount];
        var evaluated = new bool[boneCount];
        var visiting = new bool[boneCount];
        var locals = BuildBindLocalMatrices(skeleton);

        Matrix4x4 entityRoot = Matrix4x4.Identity;
        int entityRootTrackIndex = binding?.EntityRootTrackIndex ?? 0;
        if (applyEntityRoot && entityRootTrackIndex >= 0 && entityRootTrackIndex < animation.Tracks.Count)
            entityRoot = BuildEntityRoot(animation.Tracks[entityRootTrackIndex], frame, binding?.UseStandardQuaternionMatrix == true);

        for (int i = 0; i < boneCount; i++)
            EvaluateBone(i, skeleton, animation, frame, locals, worlds, evaluated, entityRoot, visiting, binding);

        var pose = new AnimationPose
        {
            Frame = frame,
            EntityRootMatrix = entityRoot,
            SkinnedMesh = skinMesh && mesh.HasSkinning
                ? SkinMesh(mesh, skeleton, worlds)
                : null
        };
        pose.BoneWorldMatrices.AddRange(worlds);
        return pose;
    }
    private static Matrix4x4 EvaluateBone(
        int index,
        SkeletonData skeleton,
        BnmAnimation animation,
        float frame,
        IReadOnlyList<Matrix4x4> bindLocals,
        Matrix4x4[] worlds,
        bool[] evaluated,
        Matrix4x4 entityRoot,
        bool[] visiting,
        AnimationSkeletonBinding? binding)
    {
        if (evaluated[index])
            return worlds[index];
        if (visiting[index])
            return skeleton.Bones[index].BindWorldMatrix;
        visiting[index] = true;

        BoneData bone = skeleton.Bones[index];
        Matrix4x4 bindLocal = bindLocals[index];
        int trackIndex = binding?.TrackIndexForBone(index) ?? (index + 1);
        bool boneAllowed = binding is null || TraneBoneGroups.AllowsBone(binding.BoneMaskName, bone.Name);
        BnmTrack? track = boneAllowed && trackIndex >= 0 && trackIndex < animation.Tracks.Count ? animation.Tracks[trackIndex] : null;

        Quaternion? sampledRotation = track is null ? null : BnmSampler.SampleRotation(track, frame);
        Vector3? sampledTranslation = track is null ? null : BnmSampler.SampleTranslation(track, frame);

        Matrix4x4 bindRotation = bindLocal;
        bindRotation.M41 = bindRotation.M42 = bindRotation.M43 = 0f;
        bindRotation.M14 = bindRotation.M24 = bindRotation.M34 = 0f;
        bindRotation.M44 = 1f;

        Matrix4x4 local;
        Quaternion? referenceRotation = binding?.UseReferencePoseRetarget == true &&
            binding.ReferenceBoneRotations is { } referenceRotations &&
            index >= 0 && index < referenceRotations.Length
                ? referenceRotations[index]
                : null;

        if (sampledRotation is Quaternion q && referenceRotation is Quaternion referenceQ)
        {   
            Matrix4x4 currentAnim = CreateAnimationRotation(q, binding?.UseStandardQuaternionMatrix == true);
            Matrix4x4 referenceAnim = CreateAnimationRotation(referenceQ, binding?.UseStandardQuaternionMatrix == true);
            if (Matrix4x4.Invert(referenceAnim, out Matrix4x4 inverseReference))
                local = bindRotation * inverseReference * currentAnim;
            else
                local = bindRotation;
        }
        else if (sampledRotation is Quaternion qDirect)
        {
            local = CreateAnimationRotation(qDirect, binding?.UseStandardQuaternionMatrix == true);
        }
        else
        {
            local = bindRotation;
        }

        Vector3 restOffset = new(bindLocal.M41, bindLocal.M42, bindLocal.M43);
        Vector3? referenceTranslation = binding?.UseReferencePoseRetarget == true &&
            binding.ReferenceBoneTranslations is { } referenceTranslations &&
            index >= 0 && index < referenceTranslations.Length
                ? referenceTranslations[index]
                : null;

        Vector3 translation;
        if (binding?.UseReferencePoseRetarget == true && sampledTranslation is Vector3 currentT && referenceTranslation is Vector3 referenceT)
        {
            translation = restOffset + (currentT - referenceT);
        }
        else if (index == 0)
        {
            translation = sampledTranslation ?? restOffset;
        }
        else
        {
            translation = restOffset + (sampledTranslation ?? Vector3.Zero);
        }
        local.M41 = translation.X;
        local.M42 = translation.Y;
        local.M43 = translation.Z;

        if (bone.ParentIndex >= 0 && bone.ParentIndex < skeleton.Bones.Count)
        {
            Matrix4x4 parent = EvaluateBone(bone.ParentIndex, skeleton, animation, frame, bindLocals, worlds, evaluated, entityRoot, visiting, binding);
            worlds[index] = local * parent;
        }
        else
        {
            worlds[index] = local * entityRoot;
        }

        visiting[index] = false;
        evaluated[index] = true;
        return worlds[index];
    }

    private static Matrix4x4[] BuildBindLocalMatrices(SkeletonData skeleton)
    {
        var locals = new Matrix4x4[skeleton.Bones.Count];
        for (int i = 0; i < skeleton.Bones.Count; i++)
        {
            BoneData bone = skeleton.Bones[i];
            if (bone.ParentIndex < 0 || bone.ParentIndex >= skeleton.Bones.Count)
            {
                locals[i] = bone.BindWorldMatrix;
                continue;
            }
            if (Matrix4x4.Invert(skeleton.Bones[bone.ParentIndex].BindWorldMatrix, out Matrix4x4 parentInverse))
                locals[i] = bone.BindWorldMatrix * parentInverse;
            else
                locals[i] = bone.BindWorldMatrix;
        }
        return locals;
    }

    private static Matrix4x4 BuildEntityRoot(BnmTrack track, float frame, bool useStandardQuaternionMatrix)
    {
        Quaternion q = BnmSampler.SampleRotation(track, frame) ?? Quaternion.Identity;
        Vector3 t = BnmSampler.SampleTranslation(track, frame) ?? Vector3.Zero;
        Matrix4x4 result = CreateAnimationRotation(q, useStandardQuaternionMatrix);
        result.M41 = t.X;
        result.M42 = t.Y;
        result.M43 = t.Z;
        return result;
    }
    private static Matrix4x4 CreateAnimationRotation(Quaternion q, bool useStandardQuaternionMatrix)
    {
        if (!useStandardQuaternionMatrix)
            return CreateEngineRotation(q);

        if (q.LengthSquared() < 0.0000001f)
            q = Quaternion.Identity;
        else
            q = Quaternion.Normalize(q);

        Matrix4x4 m = Matrix4x4.CreateFromQuaternion(q);
        m.M14 = m.M24 = m.M34 = 0f;
        m.M41 = m.M42 = m.M43 = 0f;
        m.M44 = 1f;
        return m;
    }
    public static Matrix4x4 CreateEngineRotation(Quaternion q)
    {
        if (q.LengthSquared() < 0.0000001f)
            q = Quaternion.Identity;
        else
            q = Quaternion.Normalize(q);

        Matrix4x4 m = Matrix4x4.Transpose(Matrix4x4.CreateFromQuaternion(q));
        m.M14 = m.M24 = m.M34 = 0f;
        m.M41 = m.M42 = m.M43 = 0f;
        m.M44 = 1f;
        return m;
    }
    private static MeshData SkinMesh(MeshData source, SkeletonData skeleton, IReadOnlyList<Matrix4x4> worlds)
    {
        var output = new MeshData();
        output.Indices.AddRange(source.Indices);
        output.UVs.AddRange(source.UVs);
        output.Skinning.AddRange(source.Skinning);
        foreach (MeshPart part in source.Parts)
        {
            output.Parts.Add(new MeshPart
            {
                SectionIndex = part.SectionIndex,
                IndexStart = part.IndexStart,
                IndexCount = part.IndexCount,
                RenderGroupName = part.RenderGroupName,
                MaterialName = part.MaterialName,
                TextureReference = part.TextureReference,
                IsVisible = part.IsVisible
            });
        }
        var skinMatrices = new Matrix4x4[skeleton.Bones.Count];
        for (int i = 0; i < skeleton.Bones.Count; i++)
            skinMatrices[i] = skeleton.Bones[i].InverseBindMatrix * worlds[i];

        for (int i = 0; i < source.Positions.Count; i++)
        {
            Vector3 p = source.Positions[i];
            VertexSkin skin = source.Skinning[i];
            Vector3 result = Vector3.Zero;
            float total = 0f;

            foreach ((int joint, float weight) in skin.Influences())
            {
                if (joint < 0 || joint >= skinMatrices.Length || weight <= 0f)
                    continue;
                result += Vector3.Transform(p, skinMatrices[joint]) * weight;
                total += weight;
            }
            output.Positions.Add(total > 0.000001f ? result / total : p);

            if (source.Normals.Count == source.Positions.Count)
            {
                Vector3 n = source.Normals[i];
                Vector3 nr = Vector3.Zero;
                float nt = 0f;
                foreach ((int joint, float weight) in skin.Influences())
                {
                    if (joint < 0 || joint >= skinMatrices.Length || weight <= 0f)
                        continue;
                    nr += Vector3.TransformNormal(n, skinMatrices[joint]) * weight;
                    nt += weight;
                }
                if (nt > 0.000001f) nr /= nt;
                if (nr.LengthSquared() > 0.000001f) nr = Vector3.Normalize(nr);
                output.Normals.Add(nr.LengthSquared() > 0.000001f ? nr : n);
            }
        }

        return output;
    }
}
