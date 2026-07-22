using System.Numerics;

namespace FloorLeveler.Core;

/// <summary>
/// 収集したサンプル点群から床平面を推定し、補正を算出する高レベル API (仕様 F-2, F-3)。
/// 純粋関数のみで構成し、Shell (UI/OpenVR) から呼び出す。
/// </summary>
public static class FloorEstimation
{
    /// <summary>
    /// 点群から床平面を推定する。<paramref name="useRansac"/> が真なら RANSAC で
    /// 外れ値を除去してからフィットする。
    /// </summary>
    public static FloorEstimate Estimate(
        IReadOnlyList<Vector3> points,
        bool useRansac = false,
        float ransacThresholdMeters = Ransac.DefaultInlierThresholdMeters)
    {
        ArgumentNullException.ThrowIfNull(points);

        var quality = Sampling.Evaluate(points);
        if (points.Count < PlaneFit.MinimumPoints)
        {
            return new FloorEstimate(null, quality, 0);
        }

        try
        {
            if (useRansac)
            {
                var result = Ransac.Run(points, ransacThresholdMeters);
                return new FloorEstimate(result.Fit, quality, result.OutlierCount);
            }

            return new FloorEstimate(PlaneFit.Fit(points), quality, 0);
        }
        catch (ArgumentException)
        {
            // 点群が退化 (一直線上など) して平面が定まらない場合は「推定不能」として返す。
            // サンプリング途中に一時的に起こり得る正常な状態であり、例外にはしない。
            return new FloorEstimate(null, quality, 0);
        }
    }
}

/// <summary>床推定の結果 (UI 表示用にまとめたもの)。</summary>
/// <param name="Plane">推定平面。点数不足などで推定できない場合は null。</param>
/// <param name="Quality">点群の品質評価。</param>
/// <param name="OutlierCount">RANSAC で除外された点数 (未使用時は 0)。</param>
public sealed record FloorEstimate(PlaneFitResult? Plane, SampleQuality Quality, int OutlierCount)
{
    public bool CanCorrect => Plane is not null && Quality.IsSufficient;
}
