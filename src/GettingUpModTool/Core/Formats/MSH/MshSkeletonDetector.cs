using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using GettingUpModTool.Core.Models;

namespace GettingUpModTool.Core.Formats.MSH;
public static class MshSkeletonDetector
{
    public const int BoneRecordSize = 82;
    private const int MatrixOffset = 32;
    private const int TranslationOffset = 68;
    private const int ParentOffset = 80;

    public static SkeletonData? Detect(byte[] data, int searchStart = 0)
    {
        SkeletonData? best = null;

        int end = data.Length - 4 - BoneRecordSize * 2;
        for (int countOffset = Math.Max(0, searchStart); countOffset <= end; countOffset++)
        {
            uint rawCount = ReadU32(data, countOffset);
            if (rawCount < 2 || rawCount > 256)
                continue;

            int count = (int)rawCount;
            int recordsOffset = countOffset + 4;
            long recordsEnd = (long)recordsOffset + (long)count * BoneRecordSize;
            if (recordsEnd > data.Length)
                continue;

            var parsed = TryParse(data, countOffset, recordsOffset, count);
            if (parsed is null)
                continue;

            if (best is null || parsed.Confidence > best.Confidence ||
                (Math.Abs(parsed.Confidence - best.Confidence) < 0.0001 && parsed.Bones.Count > best.Bones.Count))
            {
                best = parsed;
            }
        }

        return best;
    }

    private static SkeletonData? TryParse(byte[] data, int countOffset, int recordsOffset, int count)
    {
        var bones = new List<BoneData>(count);
        int validNames = 0;
        int validParents = 0;
        int invertibleMatrices = 0;
        int plausibleMatrices = 0;
        int orderedParents = 0;
        int rootCount = 0;

        for (int i = 0; i < count; i++)
        {
            int p = recordsOffset + i * BoneRecordSize;
            string? name = TryReadName(data, p, 24);
            if (name is null)
                return null;
            validNames++;

            Matrix4x4 inverseBind = ReadAffine3x4(data, p + MatrixOffset, p + TranslationOffset);
            if (!IsFinite(inverseBind))
                return null;

            if (MatrixLooksPlausible(inverseBind))
                plausibleMatrices++;

            Matrix4x4 world;
            if (!Matrix4x4.Invert(inverseBind, out world) || !IsFinite(world))
                world = Matrix4x4.Identity;
            else
                invertibleMatrices++;

            ushort rawParent = ReadU16(data, p + ParentOffset);
            int parent = rawParent == ushort.MaxValue ? -1 : rawParent;
            if (parent == -1)
            {
                rootCount++;
                validParents++;
            }
            else if (parent >= 0 && parent < count && parent != i)
            {
                validParents++;
                if (parent < i)
                    orderedParents++;
            }
            else
            {
                return null;
            }

            bones.Add(new BoneData
            {
                Index = i,
                Name = name,
                ParentIndex = parent,
                InverseBindMatrix = inverseBind,
                BindWorldMatrix = world
            });
        }

        if (rootCount < 1 || rootCount > Math.Max(2, count / 3))
            return null;

        double n = count;
        double confidence =
            (validNames / n) * 0.20 +
            (validParents / n) * 0.20 +
            (invertibleMatrices / n) * 0.25 +
            (plausibleMatrices / n) * 0.20 +
            (orderedParents / Math.Max(1.0, n - rootCount)) * 0.15;

        if (confidence < 0.80)
            return null;

        var result = new SkeletonData
        {
            CountOffset = countOffset,
            RecordsOffset = recordsOffset,
            RecordSize = BoneRecordSize,
            Confidence = Math.Clamp(confidence, 0.0, 1.0)
        };
        result.Bones.AddRange(bones);
        return result;
    }

    private static string? TryReadName(byte[] data, int offset, int maxLength)
    {
        int max = Math.Min(data.Length, offset + maxLength);
        int end = offset;
        while (end < max && data[end] != 0)
        {
            byte b = data[end];
            if (b < 32 || b > 126)
                return null;
            end++;
        }

        int len = end - offset;
        if (len < 1 || len > 20 || end >= max || data[end] != 0)
            return null;

        return Encoding.ASCII.GetString(data, offset, len);
    }

    private static Matrix4x4 ReadAffine3x4(byte[] data, int matrixOffset, int translationOffset)
    {
        float m11 = ReadF32(data, matrixOffset + 0);
        float m12 = ReadF32(data, matrixOffset + 4);
        float m13 = ReadF32(data, matrixOffset + 8);
        float m21 = ReadF32(data, matrixOffset + 12);
        float m22 = ReadF32(data, matrixOffset + 16);
        float m23 = ReadF32(data, matrixOffset + 20);
        float m31 = ReadF32(data, matrixOffset + 24);
        float m32 = ReadF32(data, matrixOffset + 28);
        float m33 = ReadF32(data, matrixOffset + 32);
        float tx = ReadF32(data, translationOffset + 0);
        float ty = ReadF32(data, translationOffset + 4);
        float tz = ReadF32(data, translationOffset + 8);

        return new Matrix4x4(
            m11, m12, m13, 0,
            m21, m22, m23, 0,
            m31, m32, m33, 0,
            tx,  ty,  tz,  1);
    }

    private static bool MatrixLooksPlausible(Matrix4x4 m)
    {
        var x = new Vector3(m.M11, m.M12, m.M13);
        var y = new Vector3(m.M21, m.M22, m.M23);
        var z = new Vector3(m.M31, m.M32, m.M33);
        float lx = x.Length();
        float ly = y.Length();
        float lz = z.Length();
        if (!float.IsFinite(lx) || !float.IsFinite(ly) || !float.IsFinite(lz))
            return false;
        if (lx < 0.05f || ly < 0.05f || lz < 0.05f || lx > 20 || ly > 20 || lz > 20)
            return false;

        float tx = m.M41, ty = m.M42, tz = m.M43;
        return Math.Abs(tx) < 1_000_000 && Math.Abs(ty) < 1_000_000 && Math.Abs(tz) < 1_000_000;
    }

    private static bool IsFinite(Matrix4x4 m)
    {
        return float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M13) && float.IsFinite(m.M14) &&
               float.IsFinite(m.M21) && float.IsFinite(m.M22) && float.IsFinite(m.M23) && float.IsFinite(m.M24) &&
               float.IsFinite(m.M31) && float.IsFinite(m.M32) && float.IsFinite(m.M33) && float.IsFinite(m.M34) &&
               float.IsFinite(m.M41) && float.IsFinite(m.M42) && float.IsFinite(m.M43) && float.IsFinite(m.M44);
    }

    private static uint ReadU32(byte[] data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));

    private static ushort ReadU16(byte[] data, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));

    private static float ReadF32(byte[] data, int offset)
        => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4)));
}
