using System.Numerics;
using FloorLeveler.Core;

namespace FloorLeveler.Core.Tests;

public class CorrectionTests
{
    private const float Tolerance = 1e-4f;

    private static void AssertVectorEqual(Vector3 expected, Vector3 actual, float tolerance = Tolerance)
        => Assert.True(
            (expected - actual).Length() <= tolerance,
            $"expected {expected} but got {actual}");

    /// <summary>傾いた床の点群 (standing 座標) を生成する。</summary>
    private static List<Vector3> MakeTiltedFloor(float tiltDegrees, float height, Vector3 axis)
    {
        var tilt = RigidTransform.Compose(
            RigidTransform.CreateTranslation(new Vector3(0f, height, 0f)),
            new RigidTransform(
                Quaternion.CreateFromAxisAngle(axis, tiltDegrees * MathF.PI / 180f),
                Vector3.Zero));

        var points = new List<Vector3>();
        for (var ix = -2; ix <= 2; ix++)
        {
            for (var iz = -2; iz <= 2; iz++)
            {
                points.Add(tilt.TransformPoint(new Vector3(ix * 0.4f, 0f, iz * 0.4f)));
            }
        }

        return points;
    }

    [Fact]
    public void FloorAlign_FlattensSampledFloorToZeroHeight()
    {
        var points = MakeTiltedFloor(3f, 0.05f, Vector3.UnitZ);
        var fit = PlaneFit.Fit(points);

        var correction = Correction.ComputeFloorAlign(fit);

        // 補正後の standing 座標では床点群はすべて Y=0 に載る。
        foreach (var p in points)
        {
            var corrected = correction.StandingSpaceMap.TransformPoint(p);
            Assert.True(Math.Abs(corrected.Y) < 1e-4f, $"floor point {p} → {corrected}");
        }

        Assert.Equal(3f, correction.RotationAngleDegrees, 2);
        Assert.False(correction.IsNegligible);
        Assert.False(correction.RequiresConfirmation);
    }

    [Fact]
    public void FloorAlign_NewStandingToRaw_KeepsPhysicalPointsFixed()
    {
        var points = MakeTiltedFloor(2f, -0.03f, Vector3.UnitX);
        var fit = PlaneFit.Fit(points);
        var correction = Correction.ComputeFloorAlign(fit);

        var oldS2R = new RigidTransform(
            Quaternion.CreateFromYawPitchRoll(0.5f, 0.02f, -0.01f),
            new Vector3(0.3f, 1.1f, -0.7f));
        var newS2R = correction.ApplyTo(oldS2R);

        // 不変条件: 旧 standing 座標 p の物理点 (raw 座標) は、
        // 新 standing 座標 C(p) からも同じ raw 座標に写る。
        foreach (var p in points.Take(5))
        {
            var raw = oldS2R.TransformPoint(p);
            var rawViaNew = newS2R.TransformPoint(correction.StandingSpaceMap.TransformPoint(p));
            AssertVectorEqual(raw, rawViaNew);
        }

        // 床サンプル (raw 固定) は新 standing 座標で Y=0 になる。
        foreach (var p in points.Take(5))
        {
            var raw = oldS2R.TransformPoint(p);
            var newStanding = newS2R.Inverse().TransformPoint(raw);
            Assert.True(Math.Abs(newStanding.Y) < 1e-4f);
        }
    }

    [Fact]
    public void FloorAlign_FlatFloorAtZero_IsNegligible()
    {
        var points = MakeTiltedFloor(0f, 0f, Vector3.UnitZ);
        var correction = Correction.ComputeFloorAlign(PlaneFit.Fit(points));

        Assert.True(correction.IsNegligible);
    }

    [Fact]
    public void FloorAlign_LargeTilt_RequiresConfirmation()
    {
        var points = MakeTiltedFloor(12f, 0f, Vector3.UnitZ);
        var correction = Correction.ComputeFloorAlign(PlaneFit.Fit(points));

        Assert.True(correction.RequiresConfirmation);
    }

    [Fact]
    public void GravityAlign_MakesStandingUpMatchRawUp()
    {
        // ルームセットアップ誤差で standing が roll 方向に 2° 傾いている状況。
        var oldS2R = new RigidTransform(
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 2f * MathF.PI / 180f),
            new Vector3(0.2f, 1.0f, 0.4f));

        var correction = Correction.ComputeGravityAlign(oldS2R);
        var newS2R = correction.ApplyTo(oldS2R);

        // 新しい standing の up 軸は raw の up 軸と一致する。
        AssertVectorEqual(Vector3.UnitY, newS2R.TransformVector(Vector3.UnitY));
        Assert.Equal(2f, correction.RotationAngleDegrees, 2);
    }

    [Fact]
    public void GravityAlign_AlreadyLevel_IsNegligible()
    {
        var oldS2R = new RigidTransform(
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.8f), // yaw のみ (水平)
            new Vector3(0f, 1.2f, 0f));

        var correction = Correction.ComputeGravityAlign(oldS2R);

        Assert.True(correction.IsNegligible);
    }

    [Fact]
    public void GravityAlign_WithFloorSamples_AlsoLevelsHeight()
    {
        var oldS2R = new RigidTransform(
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, 1.5f * MathF.PI / 180f),
            new Vector3(0f, 1.0f, 0f));

        // 床サンプルは standing 座標で少し浮いている。
        var floorPoints = MakeTiltedFloor(0f, 0.04f, Vector3.UnitZ);
        var fit = PlaneFit.Fit(floorPoints);

        var correction = Correction.ComputeGravityAlign(oldS2R, fit);

        // 床重心は補正後 Y=0 になる。
        var correctedCentroid = correction.StandingSpaceMap.TransformPoint(fit.Centroid);
        Assert.True(Math.Abs(correctedCentroid.Y) < 1e-4f);
        Assert.NotEqual(0f, correction.HeightChangeMeters);
    }

    [Fact]
    public void BoundaryTransform_KeepsWallsFixedInRawSpace()
    {
        var points = MakeTiltedFloor(2.5f, 0.02f, Vector3.UnitZ);
        var correction = Correction.ComputeFloorAlign(PlaneFit.Fit(points));

        var oldS2R = new RigidTransform(
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f),
            new Vector3(0.1f, 1.0f, -0.2f));
        var newS2R = correction.ApplyTo(oldS2R);

        var walls = new[]
        {
            new Vector3(1.5f, 0f, 1.5f),
            new Vector3(1.5f, 2.4f, 1.5f),
            new Vector3(-1.5f, 0f, 1.5f),
            new Vector3(-1.5f, 2.4f, -1.5f),
        };

        var transformed = BoundaryTransform.Transform(correction, walls);

        // 変換後の頂点を新 S→R で raw に写すと、元の頂点を旧 S→R で写した位置と一致する
        // (物理的な壁位置が維持される)。
        for (var i = 0; i < walls.Length; i++)
        {
            AssertVectorEqual(
                oldS2R.TransformPoint(walls[i]),
                newS2R.TransformPoint(transformed[i]));
        }
    }
}
