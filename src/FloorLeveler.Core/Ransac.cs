using System.Numerics;

namespace FloorLeveler.Core;

/// <summary>RANSAC による外れ値除去の結果。</summary>
/// <param name="Fit">インライアのみで再フィットした平面。</param>
/// <param name="InlierIndices">入力点群におけるインライアのインデックス (昇順)。</param>
/// <param name="OutlierCount">除外された点の数。</param>
public sealed record RansacResult(PlaneFitResult Fit, IReadOnlyList<int> InlierIndices, int OutlierCount);

/// <summary>
/// RANSAC による外れ値に頑健な平面推定 (仕様 F-2 オプション。既定: 閾値 3 mm、反復 200 回)。
/// </summary>
public static class Ransac
{
    public const float DefaultInlierThresholdMeters = 0.003f;
    public const int DefaultIterations = 200;

    /// <summary>
    /// 3 点サンプリングで平面仮説を生成し、インライア数が最大の仮説を選んで
    /// インライアのみで最小二乗フィットし直す。seed 固定で決定的に動作する。
    /// </summary>
    public static RansacResult Run(
        IReadOnlyList<Vector3> points,
        float inlierThresholdMeters = DefaultInlierThresholdMeters,
        int iterations = DefaultIterations,
        int seed = 20260722)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < PlaneFit.MinimumPoints)
        {
            throw new ArgumentException($"RANSAC には {PlaneFit.MinimumPoints} 点以上が必要です。", nameof(points));
        }

        var random = new Random(seed);
        List<int>? bestInliers = null;
        var bestResidualSq = double.PositiveInfinity;

        for (var i = 0; i < iterations; i++)
        {
            var (i0, i1, i2) = PickDistinctIndices(random, points.Count);
            var p0 = points[i0];
            var normal = Vector3.Cross(points[i1] - p0, points[i2] - p0);
            if (normal.LengthSquared() < 1e-12f)
            {
                continue; // 3 点がほぼ一直線 (退化した仮説)
            }

            normal = Vector3.Normalize(normal);

            var inliers = new List<int>(points.Count);
            double residualSq = 0;
            for (var j = 0; j < points.Count; j++)
            {
                var distance = Math.Abs(Vector3.Dot(points[j] - p0, normal));
                residualSq += (double)distance * distance;
                if (distance <= inlierThresholdMeters)
                {
                    inliers.Add(j);
                }
            }

            if (inliers.Count < PlaneFit.MinimumPoints)
            {
                continue;
            }

            // インライア数が最大の仮説を選ぶ。同数の場合は全点の残差二乗和が
            // 小さい方を採用する (外れ値を含む仮説が先着順で残るのを防ぐ)。
            if (bestInliers is null
                || inliers.Count > bestInliers.Count
                || (inliers.Count == bestInliers.Count && residualSq < bestResidualSq))
            {
                bestInliers = inliers;
                bestResidualSq = residualSq;
            }
        }

        if (bestInliers is null)
        {
            // 有効な仮説が得られなければ全点でフィットした結果を返す。
            return new RansacResult(
                PlaneFit.Fit(points),
                Enumerable.Range(0, points.Count).ToArray(),
                0);
        }

        var inlierPoints = bestInliers.Select(j => points[j]).ToArray();
        var fit = PlaneFit.Fit(inlierPoints);
        return new RansacResult(fit, bestInliers, points.Count - bestInliers.Count);
    }

    private static (int, int, int) PickDistinctIndices(Random random, int count)
    {
        var a = random.Next(count);
        int b;
        do
        {
            b = random.Next(count);
        } while (b == a);

        int c;
        do
        {
            c = random.Next(count);
        } while (c == a || c == b);

        return (a, b, c);
    }
}
