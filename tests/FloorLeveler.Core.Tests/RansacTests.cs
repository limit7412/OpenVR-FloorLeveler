using System.Numerics;
using FloorLeveler.Core;

namespace FloorLeveler.Core.Tests;

public class RansacTests
{
    [Fact]
    public void Run_ExcludesOutliers()
    {
        var points = new List<Vector3>();
        for (var ix = -2; ix <= 2; ix++)
        {
            for (var iz = -2; iz <= 2; iz++)
            {
                points.Add(new Vector3(ix * 0.3f, 0f, iz * 0.3f));
            }
        }

        // 床から大きく外れた点 (持ち上げた瞬間の誤記録などを想定) を混入させる。
        var outlierIndices = new[] { points.Count, points.Count + 1, points.Count + 2 };
        points.Add(new Vector3(0.1f, 0.5f, 0.1f));
        points.Add(new Vector3(-0.2f, 0.3f, 0.4f));
        points.Add(new Vector3(0.3f, -0.4f, -0.2f));

        var result = Ransac.Run(points);

        Assert.Equal(3, result.OutlierCount);
        Assert.All(outlierIndices, i => Assert.DoesNotContain(i, result.InlierIndices));
        Assert.True(result.Fit.TiltAngleDegrees < 0.1f);
        Assert.True(result.Fit.RmsResidual < 1e-4f);
    }

    [Fact]
    public void Run_CleanData_KeepsAllPoints()
    {
        var points = new List<Vector3>();
        for (var ix = -2; ix <= 2; ix++)
        {
            for (var iz = -2; iz <= 2; iz++)
            {
                points.Add(new Vector3(ix * 0.3f, 0.01f, iz * 0.3f));
            }
        }

        var result = Ransac.Run(points);

        Assert.Equal(0, result.OutlierCount);
        Assert.Equal(points.Count, result.InlierIndices.Count);
    }

    [Fact]
    public void Run_IsDeterministicForSameSeed()
    {
        var random = new Random(42);
        var points = Enumerable.Range(0, 30)
            .Select(_ => new Vector3(
                (float)(random.NextDouble() - 0.5),
                (float)(random.NextDouble() * 0.004),
                (float)(random.NextDouble() - 0.5)))
            .ToList();

        var a = Ransac.Run(points, seed: 7);
        var b = Ransac.Run(points, seed: 7);

        Assert.Equal(a.InlierIndices, b.InlierIndices);
        Assert.Equal(a.Fit.Normal, b.Fit.Normal);
    }

    [Fact]
    public void Run_TooFewPoints_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => Ransac.Run(new List<Vector3> { Vector3.Zero, Vector3.UnitX }));
    }
}
