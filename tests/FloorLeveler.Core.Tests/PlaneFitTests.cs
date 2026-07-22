using System.Numerics;
using FloorLeveler.Core;

namespace FloorLeveler.Core.Tests;

public class PlaneFitTests
{
    /// <summary>グリッド状の床点群を生成し、指定の変換で傾ける。</summary>
    private static List<Vector3> MakeFloorPoints(RigidTransform transform, float noiseAmplitude = 0f, int seed = 1)
    {
        var random = new Random(seed);
        var points = new List<Vector3>();
        for (var ix = -2; ix <= 2; ix++)
        {
            for (var iz = -2; iz <= 2; iz++)
            {
                var noise = noiseAmplitude == 0f
                    ? 0f
                    : (float)(random.NextDouble() * 2 - 1) * noiseAmplitude;
                points.Add(transform.TransformPoint(new Vector3(ix * 0.5f, noise, iz * 0.5f)));
            }
        }

        return points;
    }

    [Fact]
    public void Fit_FlatFloor_ReturnsUpNormalAndZeroTilt()
    {
        var points = MakeFloorPoints(RigidTransform.CreateTranslation(new Vector3(0f, 0.02f, 0f)));

        var fit = PlaneFit.Fit(points);

        Assert.True((fit.Normal - Vector3.UnitY).Length() < 1e-4f);
        Assert.True(fit.TiltAngleDegrees < 0.01f);
        Assert.Equal(0.02f, fit.Centroid.Y, 4);
        Assert.True(fit.RmsResidual < 1e-5f);
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(3.0f)]
    [InlineData(7.5f)]
    public void Fit_TiltedFloor_RecoversTiltAngle(float tiltDegrees)
    {
        var tilt = new RigidTransform(
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, tiltDegrees * MathF.PI / 180f),
            Vector3.Zero);
        var points = MakeFloorPoints(tilt);

        var fit = PlaneFit.Fit(points);

        Assert.Equal(tiltDegrees, fit.TiltAngleDegrees, 2);
    }

    [Fact]
    public void Fit_NormalSignIsAlwaysUp()
    {
        // 180° 近く傾けても法線の Y は正に正規化される。
        var tilt = new RigidTransform(
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, 5f * MathF.PI / 180f),
            Vector3.Zero);
        var fit = PlaneFit.Fit(MakeFloorPoints(tilt));

        Assert.True(fit.Normal.Y > 0f);
    }

    [Fact]
    public void Fit_NoisyFloor_ReportsResiduals()
    {
        var points = MakeFloorPoints(RigidTransform.Identity, noiseAmplitude: 0.002f);

        var fit = PlaneFit.Fit(points);

        Assert.True(fit.RmsResidual > 0f);
        Assert.True(fit.RmsResidual < 0.002f);
        Assert.True(fit.MaxResidual >= fit.RmsResidual);
        Assert.True(fit.TiltAngleDegrees < 1f);
    }

    [Fact]
    public void Fit_TiltAzimuth_PointsDownhill()
    {
        // Z 軸まわり +3° の回転で床は +X 側が持ち上がる → 下りは -X 方向 (azimuth = -90°)。
        var tilt = new RigidTransform(
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 3f * MathF.PI / 180f),
            Vector3.Zero);
        var fit = PlaneFit.Fit(MakeFloorPoints(tilt));

        Assert.Equal(-90f, fit.TiltAzimuthDegrees, 1);
    }

    [Fact]
    public void Fit_TooFewPoints_Throws()
    {
        var points = new List<Vector3> { Vector3.Zero, Vector3.UnitX };
        Assert.Throws<ArgumentException>(() => PlaneFit.Fit(points));
    }

    [Fact]
    public void Fit_CollinearPoints_Throws()
    {
        var points = Enumerable.Range(0, 10)
            .Select(i => new Vector3(i * 0.1f, 0f, 0f))
            .ToList();
        Assert.Throws<ArgumentException>(() => PlaneFit.Fit(points));
    }
}
