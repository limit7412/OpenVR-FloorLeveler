using System.Numerics;
using FloorLeveler.Core;

namespace FloorLeveler.Core.Tests;

public class FloorProjectionTests
{
    [Fact]
    public void TopDown_ProjectsToXz_IgnoringHeight()
    {
        var points = new[] { new Vector3(1f, 9f, 2f), new Vector3(3f, -5f, 4f) };

        var plot = FloorProjection.TopDown(points);

        Assert.Equal(new Vector2(1f, 2f), plot.Points[0]);
        Assert.Equal(new Vector2(3f, 4f), plot.Points[1]);
        Assert.Null(plot.FloorLine);
        Assert.Equal(new Vector2(1f, 2f), plot.Min);
        Assert.Equal(new Vector2(3f, 4f), plot.Max);
        Assert.Equal("X (m)", plot.HorizontalLabel);
        Assert.Equal("Z (m)", plot.VerticalLabel);
    }

    [Fact]
    public void TopDown_Empty_IsEmptyWithOriginBounds()
    {
        var plot = FloorProjection.TopDown([]);

        Assert.True(plot.IsEmpty);
        Assert.Empty(plot.Points);
        Assert.Equal(Vector2.Zero, plot.Min);
        Assert.Equal(Vector2.Zero, plot.Max);
    }

    [Fact]
    public void Side_FlatFloor_ProducesHorizontalFloorLine()
    {
        // y=0.5 の水平床にある格子点。法線は +Y、傾き 0。
        var points = Grid((x, z) => new Vector3(x, 0.5f, z));
        var plane = PlaneFit.Fit(points);

        var plot = FloorProjection.Side(points, plane);

        Assert.NotNull(plot.FloorLine);
        var (start, end) = plot.FloorLine!.Value;
        // 水平床なので断面線は水平 (両端の高さが等しく重心高さ)。
        Assert.Equal(0.5f, start.Y, 3);
        Assert.Equal(0.5f, end.Y, 3);
        Assert.Equal("傾き方向 u (m)", plot.HorizontalLabel);
        Assert.Equal("高さ Y (m)", plot.VerticalLabel);
    }

    [Fact]
    public void Side_TiltedFloor_FloorLineDescendsAlongSlope()
    {
        // +X 方向に 5° 下る床: y = -tan(5°)·x。法線 (sin5°, cos5°, 0)、方位 90°。
        var theta = 5f * MathF.PI / 180f;
        var slope = MathF.Tan(theta);
        var points = Grid((x, z) => new Vector3(x, -slope * x, z));
        var plane = PlaneFit.Fit(points);

        Assert.Equal(5f, plane.TiltAngleDegrees, 2);

        var plot = FloorProjection.Side(points, plane);
        Assert.NotNull(plot.FloorLine);
        var (start, end) = plot.FloorLine!.Value;

        // 横軸 u は下り方向 (+X)。u が増えると高さは下がる。
        Assert.True(end.X > start.X);
        Assert.True(end.Y < start.Y, $"start.Y={start.Y}, end.Y={end.Y}");

        // 断面線 y = cy - tan(tilt)·u を満たす (重心は原点付近)。
        Assert.Equal(-slope * start.X, start.Y, 3);
        Assert.Equal(-slope * end.X, end.Y, 3);

        // 各投影点は断面線上に乗る (残差ほぼ 0 の合成床)。
        foreach (var p in plot.Points)
        {
            Assert.Equal(-slope * p.X, p.Y, 3);
        }
    }

    [Fact]
    public void Side_NoPlane_ProjectsPointsButNoFloorLine()
    {
        // 平面推定前 (2 点) は床線を返さない。
        var points = new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 1f) };

        var plot = FloorProjection.Side(points, plane: null);

        Assert.Null(plot.FloorLine);
        Assert.Equal(2, plot.Points.Count);
        // 傾き不明なので横軸は +Z 方向 (重心基準)。u = z - 0.5。
        Assert.Equal(-0.5f, plot.Points[0].X, 4);
        Assert.Equal(0.5f, plot.Points[1].X, 4);
    }

    private static Vector3[] Grid(Func<float, float, Vector3> f)
    {
        var pts = new List<Vector3>();
        foreach (var x in new[] { -0.5f, 0f, 0.5f })
        {
            foreach (var z in new[] { -0.5f, 0f, 0.5f })
            {
                pts.Add(f(x, z));
            }
        }

        return [.. pts];
    }
}
