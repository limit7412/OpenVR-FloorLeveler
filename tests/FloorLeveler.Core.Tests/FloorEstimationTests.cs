using System.Numerics;
using FloorLeveler.Core;

namespace FloorLeveler.Core.Tests;

public class FloorEstimationTests
{
    [Fact]
    public void Estimate_TooFewPoints_ReturnsNoPlane()
    {
        var estimate = FloorEstimation.Estimate([new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f)]);

        Assert.Null(estimate.Plane);
        Assert.False(estimate.CanCorrect);
    }

    [Fact]
    public void Estimate_CollinearPoints_ReturnsNoPlaneInsteadOfThrowing()
    {
        // サンプリング途中に一直線上の点だけが集まるのは正常な過渡状態。
        var points = Enumerable.Range(0, 5)
            .Select(i => new Vector3(i * 0.2f, 0f, 0f))
            .ToArray();

        var estimate = FloorEstimation.Estimate(points);

        Assert.Null(estimate.Plane);
        Assert.False(estimate.CanCorrect);
        Assert.False(estimate.Quality.HasEnoughSpread);
    }

    [Fact]
    public void Estimate_GoodPointCloud_CanCorrect()
    {
        var points = new List<Vector3>();
        for (var ix = -1; ix <= 1; ix++)
        {
            for (var iz = -1; iz <= 1; iz++)
            {
                points.Add(new Vector3(ix * 0.3f, 0.01f, iz * 0.3f));
            }
        }

        var estimate = FloorEstimation.Estimate(points);

        Assert.NotNull(estimate.Plane);
        Assert.True(estimate.CanCorrect);
        Assert.Equal(0, estimate.OutlierCount);
    }

    [Fact]
    public void Estimate_WithRansac_ReportsOutliers()
    {
        var points = new List<Vector3>();
        for (var ix = -1; ix <= 1; ix++)
        {
            for (var iz = -1; iz <= 1; iz++)
            {
                points.Add(new Vector3(ix * 0.3f, 0f, iz * 0.3f));
            }
        }

        points.Add(new Vector3(0.1f, 0.5f, 0.1f)); // 外れ値

        var estimate = FloorEstimation.Estimate(points, useRansac: true);

        Assert.NotNull(estimate.Plane);
        Assert.Equal(1, estimate.OutlierCount);
    }
}
