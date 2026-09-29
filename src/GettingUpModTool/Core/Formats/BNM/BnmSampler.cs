using System.Numerics;

namespace GettingUpModTool.Core.Formats.BNM;

public static class BnmSampler
{
    public static Quaternion? SampleRotation(BnmTrack track, float frame)
    {
        IReadOnlyList<BnmRotationKey> keys = track.RotationKeys;
        if (keys.Count == 0) return null;
        if (keys.Count == 1 || frame <= keys[0].Frame)
            return NormalizeSafe(keys[0].Rotation);
        if (frame >= keys[^1].Frame)
            return NormalizeSafe(keys[^1].Rotation);

        int hi = FindUpperRotation(keys, frame);
        int lo = Math.Max(0, hi - 1);
        var a = keys[lo];
        var b = keys[hi];
        float span = Math.Max(1, b.Frame - a.Frame);
        float t = Math.Clamp((frame - a.Frame) / span, 0f, 1f);
        Quaternion qa = NormalizeSafe(a.Rotation);
        Quaternion qb = NormalizeSafe(b.Rotation);
        if (Quaternion.Dot(qa, qb) < 0f)
            qb = new Quaternion(-qb.X, -qb.Y, -qb.Z, -qb.W);
        return NormalizeSafe(Quaternion.Slerp(qa, qb, t));
    }

    public static Vector3? SampleTranslation(BnmTrack track, float frame)
    {
        IReadOnlyList<BnmTranslationKey> keys = track.TranslationKeys;
        if (keys.Count == 0) return null;
        if (keys.Count == 1 || frame <= keys[0].Frame)
            return keys[0].Translation;
        if (frame >= keys[^1].Frame)
            return keys[^1].Translation;

        int hi = FindUpperTranslation(keys, frame);
        int lo = Math.Max(0, hi - 1);
        var a = keys[lo];
        var b = keys[hi];
        float span = Math.Max(1, b.Frame - a.Frame);
        float t = Math.Clamp((frame - a.Frame) / span, 0f, 1f);
        return Vector3.Lerp(a.Translation, b.Translation, t);
    }

    private static int FindUpperRotation(IReadOnlyList<BnmRotationKey> keys, float frame)
    {
        int lo = 1, hi = keys.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (keys[mid].Frame < frame) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private static int FindUpperTranslation(IReadOnlyList<BnmTranslationKey> keys, float frame)
    {
        int lo = 1, hi = keys.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (keys[mid].Frame < frame) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private static Quaternion NormalizeSafe(Quaternion q)
        => q.LengthSquared() > 0.0000001f ? Quaternion.Normalize(q) : Quaternion.Identity;
}
