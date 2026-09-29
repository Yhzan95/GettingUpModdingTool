using System.Numerics;

namespace GettingUpModTool.Core.Models;

public sealed class BoneData
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public int ParentIndex { get; init; } = -1;
    public Matrix4x4 InverseBindMatrix { get; init; } = Matrix4x4.Identity;
    public Matrix4x4 BindWorldMatrix { get; init; } = Matrix4x4.Identity;

    public Vector3 BindPosition => new(BindWorldMatrix.M41, BindWorldMatrix.M42, BindWorldMatrix.M43);
}

public sealed class SkeletonData
{
    public int CountOffset { get; init; }
    public int RecordsOffset { get; init; }
    public int RecordSize { get; init; }
    public double Confidence { get; init; }
    public List<BoneData> Bones { get; } = [];

    public string ParentName(BoneData bone)
        => bone.ParentIndex >= 0 && bone.ParentIndex < Bones.Count
            ? Bones[bone.ParentIndex].Name
            : "<root>";
}
