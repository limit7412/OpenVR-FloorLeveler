using System.Numerics;

namespace FloorLeveler.Core;

/// <summary>
/// 2D プロット用に投影した点群と推定床面の断面線 (仕様 F-4)。座標はメートル。
/// 描画 (ピクセルへの写像) は Shell 側に委ね、本型は純粋な幾何のみを持つ。
/// </summary>
/// <param name="Points">投影後の 2D 点群。</param>
/// <param name="FloorLine">推定床面の断面線 (両端点)。平面が無い場合は null。</param>
/// <param name="Min">点群と床線を含む最小座標 (描画範囲決定用)。空なら原点。</param>
/// <param name="Max">同上、最大座標。空なら原点。</param>
/// <param name="HorizontalLabel">横軸ラベル。</param>
/// <param name="VerticalLabel">縦軸ラベル。</param>
public sealed record FloorPlot(
    IReadOnlyList<Vector2> Points,
    (Vector2 Start, Vector2 End)? FloorLine,
    Vector2 Min,
    Vector2 Max,
    string HorizontalLabel,
    string VerticalLabel)
{
    public bool IsEmpty => Points.Count == 0 && FloorLine is null;
}

/// <summary>
/// 点群と推定平面を俯瞰 (真上) / 側面 (傾き方向の断面) の 2D ビューへ投影する
/// 純粋関数 (仕様 F-4)。座標系は standing 空間 (Y が上)。
/// </summary>
public static class FloorProjection
{
    /// <summary>
    /// 俯瞰ビュー: XZ 平面へ投影する (横=X, 縦=Z)。真上から見た点群の広がりを表す。
    /// </summary>
    public static FloorPlot TopDown(IReadOnlyList<Vector3> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var projected = new Vector2[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            projected[i] = new Vector2(points[i].X, points[i].Z);
        }

        var (min, max) = Bounds(projected, null);
        return new FloorPlot(projected, null, min, max, "X (m)", "Z (m)");
    }

    /// <summary>
    /// 側面ビュー: 傾きの下り方位方向を横軸 u、高さ Y を縦軸に投影する。床の傾きが
    /// 直線の傾斜として現れる。平面が与えられた場合はその断面線も返す。
    /// </summary>
    /// <param name="points">standing 空間の点群。</param>
    /// <param name="plane">推定平面。null (点数不足など) の場合は床線を返さない。</param>
    public static FloorPlot Side(IReadOnlyList<Vector3> points, PlaneFitResult? plane)
    {
        ArgumentNullException.ThrowIfNull(points);

        // 横軸 u は傾きの下り方位方向 (水平単位ベクトル d)。原点は平面の重心
        // (点群のみのときは点群重心)。これにより床線が u=0 で重心高さを通る。
        var (dx, dz) = SlopeAxis(plane);
        var origin = plane?.Centroid ?? Centroid(points);

        var projected = new Vector2[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var u = (p.X - origin.X) * dx + (p.Z - origin.Z) * dz;
            projected[i] = new Vector2(u, p.Y);
        }

        (Vector2 Start, Vector2 End)? line = null;
        if (plane is { } pl)
        {
            // 断面線は u=0 (重心) で y=重心高さを通り、傾き -tan(tilt) の直線。
            // 導出: 平面上の点は u·h + (y-cy)·Ny = 0 を満たし、h=sqrt(Nx^2+Nz^2)、
            // tan(tilt)=h/Ny なので y = cy - tan(tilt)·u。
            var slope = -MathF.Tan(pl.TiltAngleDegrees * (MathF.PI / 180f));
            var (uMin, uMax) = HorizontalSpan(projected);
            line = (
                new Vector2(uMin, pl.Centroid.Y + slope * uMin),
                new Vector2(uMax, pl.Centroid.Y + slope * uMax));
        }

        var (min, max) = Bounds(projected, line);
        return new FloorPlot(projected, line, min, max, "傾き方向 u (m)", "高さ Y (m)");
    }

    /// <summary>下り方位方向の水平単位ベクトル。傾きが無い/平面が無い場合は +Z。</summary>
    private static (float Dx, float Dz) SlopeAxis(PlaneFitResult? plane)
    {
        if (plane is { } p)
        {
            var h = MathF.Sqrt((p.Normal.X * p.Normal.X) + (p.Normal.Z * p.Normal.Z));
            if (h > 1e-6f)
            {
                return (p.Normal.X / h, p.Normal.Z / h);
            }
        }

        return (0f, 1f);
    }

    /// <summary>断面線を描く u 範囲。点群の範囲、狭ければ ±0.25 m を最低幅とする。</summary>
    private static (float Min, float Max) HorizontalSpan(IReadOnlyList<Vector2> projected)
    {
        if (projected.Count == 0)
        {
            return (-0.5f, 0.5f);
        }

        var min = float.MaxValue;
        var max = float.MinValue;
        foreach (var v in projected)
        {
            min = MathF.Min(min, v.X);
            max = MathF.Max(max, v.X);
        }

        if (max - min < 0.01f)
        {
            var center = (min + max) * 0.5f;
            min = center - 0.25f;
            max = center + 0.25f;
        }

        return (min, max);
    }

    private static Vector3 Centroid(IReadOnlyList<Vector3> points)
    {
        if (points.Count == 0)
        {
            return Vector3.Zero;
        }

        var sum = Vector3.Zero;
        foreach (var p in points)
        {
            sum += p;
        }

        return sum / points.Count;
    }

    private static (Vector2 Min, Vector2 Max) Bounds(
        IReadOnlyList<Vector2> pts, (Vector2 Start, Vector2 End)? line)
    {
        var any = false;
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);

        void Accumulate(Vector2 v)
        {
            min = Vector2.Min(min, v);
            max = Vector2.Max(max, v);
            any = true;
        }

        foreach (var v in pts)
        {
            Accumulate(v);
        }

        if (line is { } l)
        {
            Accumulate(l.Start);
            Accumulate(l.End);
        }

        return any ? (min, max) : (Vector2.Zero, Vector2.Zero);
    }
}
