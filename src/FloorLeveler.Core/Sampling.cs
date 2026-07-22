using System.Numerics;

namespace FloorLeveler.Core;

/// <summary>時刻付きの位置サンプル。</summary>
public readonly record struct TimedSample(TimeSpan Timestamp, Vector3 Position);

/// <summary>サンプルの採用・棄却判定と点群の品質評価 (仕様 F-1)。</summary>
public static class Sampling
{
    /// <summary>静止判定の観測窓 (既定 500 ms)。</summary>
    public static readonly TimeSpan DefaultStillnessWindow = TimeSpan.FromMilliseconds(500);

    /// <summary>静止判定の移動量閾値 (既定 5 mm)。</summary>
    public const float DefaultStillnessToleranceMeters = 0.005f;

    /// <summary>連続方式でサンプルを棄却する速度閾値 (既定 1.0 m/s)。持ち上げた瞬間などを弾く。</summary>
    public const float DefaultMaxSpeedMetersPerSecond = 1.0f;

    /// <summary>フィットに必要な最小点数。</summary>
    public const int MinimumPoints = 3;

    /// <summary>点群に要求する水平方向の最小の広がり (既定 30 cm)。</summary>
    public const float DefaultMinimumSpreadMeters = 0.30f;

    /// <summary>
    /// 静止判定 (仕様 F-1 静置方式): 最新サンプル時刻から window 遡った区間の全サンプルが
    /// 半径 tolerance 内に収まっているかを調べる。区間全体を覆う観測が無い場合は false。
    /// 窓の開始時刻以前の直近サンプルも移動量の評価に含めるため、窓内の観測が
    /// 疎 (欠落) な場合はその間の移動が保守的に検出される。
    /// </summary>
    public static bool IsStill(
        IReadOnlyList<TimedSample> history,
        TimeSpan? window = null,
        float toleranceMeters = DefaultStillnessToleranceMeters)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (history.Count < 2)
        {
            return false;
        }

        var w = window ?? DefaultStillnessWindow;
        var newest = history[^1].Timestamp;
        var windowStart = newest - w;

        var min = history[^1].Position;
        var max = min;
        var coversWindow = false;

        for (var i = history.Count - 2; i >= 0; i--)
        {
            var sample = history[i];
            min = Vector3.Min(min, sample.Position);
            max = Vector3.Max(max, sample.Position);
            if (sample.Timestamp <= windowStart)
            {
                coversWindow = true; // 窓の外側まで観測が続いている
                break;
            }
        }

        if (!coversWindow)
        {
            return false;
        }

        return (max - min).Length() <= toleranceMeters;
    }

    /// <summary>
    /// 連続方式の速度棄却 (仕様 F-1): 直前サンプルとの間の平均速度が閾値を超えていれば true。
    /// 時刻が逆行・同時刻の場合も棄却対象とする。
    /// </summary>
    public static bool ExceedsSpeed(
        TimedSample previous,
        TimedSample current,
        float maxSpeedMetersPerSecond = DefaultMaxSpeedMetersPerSecond)
    {
        var dt = (current.Timestamp - previous.Timestamp).TotalSeconds;
        if (dt <= 0)
        {
            return true;
        }

        var speed = (current.Position - previous.Position).Length() / dt;
        return speed > maxSpeedMetersPerSecond;
    }

    /// <summary>
    /// 点群の水平方向の広がり (仕様 F-1): XZ 平面へ射影した点群の凸包に対する
    /// 回転キャリパー幅 (最小外接矩形の短辺に相当) をメートルで返す。
    /// 点が 2 点未満、または全点がほぼ一致する場合は 0。
    /// </summary>
    public static float HorizontalSpread(IReadOnlyList<Vector3> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 2)
        {
            return 0f;
        }

        var projected = points
            .Select(p => new Vector2(p.X, p.Z))
            .Distinct()
            .ToArray();
        if (projected.Length < 2)
        {
            return 0f;
        }

        var hull = ConvexHull(projected);
        if (hull.Count < 2)
        {
            return 0f;
        }

        if (hull.Count == 2)
        {
            return 0f; // 一直線上: 幅は 0
        }

        // 各辺に沿った方向を基準に、垂直方向の広がりの最小値を取る (回転キャリパー幅)。
        var width = float.MaxValue;
        for (var i = 0; i < hull.Count; i++)
        {
            var edge = hull[(i + 1) % hull.Count] - hull[i];
            var len = edge.Length();
            if (len < 1e-9f)
            {
                continue;
            }

            var normal = new Vector2(-edge.Y, edge.X) / len;
            float min = float.MaxValue, max = float.MinValue;
            foreach (var p in hull)
            {
                var d = Vector2.Dot(p, normal);
                min = Math.Min(min, d);
                max = Math.Max(max, d);
            }

            width = Math.Min(width, max - min);
        }

        return width == float.MaxValue ? 0f : width;
    }

    /// <summary>点群がフィットに十分か (点数と広がり) を評価する (仕様 F-1)。</summary>
    public static SampleQuality Evaluate(
        IReadOnlyList<Vector3> points,
        int minimumPoints = MinimumPoints,
        float minimumSpreadMeters = DefaultMinimumSpreadMeters)
    {
        ArgumentNullException.ThrowIfNull(points);
        var spread = HorizontalSpread(points);
        return new SampleQuality(
            points.Count,
            spread,
            points.Count >= minimumPoints,
            spread >= minimumSpreadMeters);
    }

    /// <summary>単調鎖 (Andrew's monotone chain) による 2D 凸包。反時計回りで返す。</summary>
    private static List<Vector2> ConvexHull(Vector2[] points)
    {
        var sorted = points
            .OrderBy(p => p.X)
            .ThenBy(p => p.Y)
            .ToArray();

        if (sorted.Length <= 2)
        {
            return sorted.ToList();
        }

        var hull = new List<Vector2>(sorted.Length * 2);

        foreach (var p in sorted)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], p) <= 0)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(p);
        }

        var lowerCount = hull.Count + 1;
        for (var i = sorted.Length - 2; i >= 0; i--)
        {
            var p = sorted[i];
            while (hull.Count >= lowerCount && Cross(hull[^2], hull[^1], p) <= 0)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(p);
        }

        hull.RemoveAt(hull.Count - 1);
        return hull;
    }

    private static float Cross(Vector2 o, Vector2 a, Vector2 b)
        => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
}

/// <summary>点群の品質評価結果。</summary>
/// <param name="PointCount">有効サンプル数。</param>
/// <param name="SpreadMeters">水平方向の広がり (メートル)。</param>
/// <param name="HasEnoughPoints">点数が要求を満たすか。</param>
/// <param name="HasEnoughSpread">広がりが要求を満たすか。</param>
public sealed record SampleQuality(
    int PointCount,
    float SpreadMeters,
    bool HasEnoughPoints,
    bool HasEnoughSpread)
{
    /// <summary>フィットに十分な品質か。</summary>
    public bool IsSufficient => HasEnoughPoints && HasEnoughSpread;
}
