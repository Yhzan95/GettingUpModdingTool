using System.Buffers.Binary;
using System.Numerics;

namespace GettingUpModTool.Core.Formats.MSH;

public static class MshHeuristicAnalyzer
{
    private static readonly int[] Strides = [12, 16, 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64];

    public static List<MshLayoutCandidate> FindCandidates(byte[] data, int maxResults = 25)
    {
        var results = new List<MshLayoutCandidate>();
        int maxStart = Math.Min(Math.Max(0, data.Length - 12), 64 * 1024);

        foreach (int stride in Strides)
        {
            for (int start = 0; start <= maxStart; start += 4)
            {
                int available = (data.Length - start) / stride;
                if (available < 6)
                    continue;

                int sampleCount = Math.Min(available, 128);
                int plausible = 0;
                int finite = 0;
                Vector3 min = new(float.PositiveInfinity);
                Vector3 max = new(float.NegativeInfinity);

                for (int i = 0; i < sampleCount; i++)
                {
                    int p = start + i * stride;
                    if (!TryReadVector3(data, p, out var v))
                        continue;

                    finite++;
                    if (IsPlausiblePosition(v))
                    {
                        plausible++;
                        min = Vector3.Min(min, v);
                        max = Vector3.Max(max, v);
                    }
                }

                if (finite == 0 || plausible < Math.Max(6, sampleCount / 2))
                    continue;

                var extent = max - min;
                float spread = MathF.Abs(extent.X) + MathF.Abs(extent.Y) + MathF.Abs(extent.Z);
                if (!float.IsFinite(spread) || spread < 0.0001f)
                    continue;

                double ratio = plausible / (double)sampleCount;
                double diversityBonus = Math.Min(1.0, Math.Log10(1 + spread) / 4.0);
                double score = ratio * 0.8 + diversityBonus * 0.2;

                if (score >= 0.72)
                {
                    results.Add(new MshLayoutCandidate(
                        start,
                        stride,
                        Math.Min(available, 50000),
                        score,
                        $"{plausible}/{sampleCount} positions plausibles; étendue {extent.X:F2}, {extent.Y:F2}, {extent.Z:F2}"));
                }
            }
        }

        return results
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.VertexOffset)
            .Take(maxResults)
            .ToList();
    }

    private static bool TryReadVector3(byte[] data, int offset, out Vector3 value)
    {
        value = default;
        if (offset < 0 || offset + 12 > data.Length)
            return false;

        float x = ReadFloat(data, offset);
        float y = ReadFloat(data, offset + 4);
        float z = ReadFloat(data, offset + 8);
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
            return false;

        value = new Vector3(x, y, z);
        return true;
    }

    private static float ReadFloat(byte[] data, int offset)
    {
        int raw = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
        return BitConverter.Int32BitsToSingle(raw);
    }

    private static bool IsPlausiblePosition(Vector3 v)
    {
        const float limit = 100000f;
        return MathF.Abs(v.X) <= limit && MathF.Abs(v.Y) <= limit && MathF.Abs(v.Z) <= limit;
    }
}
