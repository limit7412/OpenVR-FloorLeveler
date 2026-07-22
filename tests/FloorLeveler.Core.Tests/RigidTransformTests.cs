using System.Numerics;
using FloorLeveler.Core;

namespace FloorLeveler.Core.Tests;

public class RigidTransformTests
{
    private const float Tolerance = 1e-5f;

    private static void AssertVectorEqual(Vector3 expected, Vector3 actual, float tolerance = Tolerance)
        => Assert.True(
            (expected - actual).Length() <= tolerance,
            $"expected {expected} but got {actual}");

    [Fact]
    public void Identity_DoesNotMovePoints()
    {
        var p = new Vector3(1.2f, -3.4f, 5.6f);
        AssertVectorEqual(p, RigidTransform.Identity.TransformPoint(p));
    }

    [Fact]
    public void TransformPoint_AppliesRotationThenTranslation()
    {
        // Y 軸 90° 回転は +X を -Z に写す (右手系・列ベクトル規約)。
        var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        var t = new RigidTransform(rot, new Vector3(0f, 1f, 0f));

        AssertVectorEqual(new Vector3(0f, 1f, -1f), t.TransformPoint(Vector3.UnitX));
    }

    [Fact]
    public void Inverse_UndoesTransform()
    {
        var t = new RigidTransform(
            Quaternion.CreateFromYawPitchRoll(0.4f, -0.2f, 0.7f),
            new Vector3(1f, 2f, 3f));

        var p = new Vector3(-0.3f, 0.9f, 2.5f);
        AssertVectorEqual(p, t.Inverse().TransformPoint(t.TransformPoint(p)));
    }

    [Fact]
    public void Compose_AppliesInnerFirst()
    {
        var inner = RigidTransform.CreateTranslation(new Vector3(1f, 0f, 0f));
        var outer = new RigidTransform(
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f),
            Vector3.Zero);

        var composed = RigidTransform.Compose(outer, inner);

        // 原点 → inner → (1,0,0) → outer (Y軸90°) → (0,0,-1)
        AssertVectorEqual(new Vector3(0f, 0f, -1f), composed.TransformPoint(Vector3.Zero));
    }

    [Fact]
    public void RotationAboutPoint_KeepsCenterFixed()
    {
        var center = new Vector3(2f, 0.5f, -1f);
        var t = RigidTransform.CreateRotationAboutPoint(
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.3f), center);

        AssertVectorEqual(center, t.TransformPoint(center));
    }

    [Fact]
    public void RowMajor3x4_RoundTrips()
    {
        var original = new RigidTransform(
            Quaternion.CreateFromYawPitchRoll(0.31f, 0.17f, -0.53f),
            new Vector3(0.1f, -0.2f, 0.3f));

        var restored = RigidTransform.FromRowMajor3x4(original.ToRowMajor3x4());

        var p = new Vector3(1f, 2f, 3f);
        AssertVectorEqual(original.TransformPoint(p), restored.TransformPoint(p));
    }

    [Fact]
    public void RowMajor3x4_UsesColumnVectorConvention()
    {
        // Y 軸 90° 回転の列ベクトル規約行列: R * (1,0,0) = (0,0,-1)。
        // 行優先 3x4 では m[2,0] = -1 となるはず。
        var t = new RigidTransform(
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f),
            Vector3.Zero);

        var m = t.ToRowMajor3x4();

        Assert.Equal(0f, m[0, 0], 5);
        Assert.Equal(-1f, m[2, 0], 5);
        Assert.Equal(1f, m[0, 2], 5);
    }

    [Theory]
    [InlineData(0f, 1f, 0f)]
    [InlineData(1f, 0f, 0f)]
    [InlineData(0.5f, 0.8f, -0.3f)]
    public void FromToRotation_MapsFromOntoTo(float x, float y, float z)
    {
        var from = Vector3.Normalize(new Vector3(x, y, z));
        var to = Vector3.UnitY;

        var q = RigidTransform.FromToRotation(from, to);

        AssertVectorEqual(to, Vector3.Transform(from, q));
    }

    [Fact]
    public void FromToRotation_HandlesAntiparallelVectors()
    {
        var q = RigidTransform.FromToRotation(-Vector3.UnitY, Vector3.UnitY);
        AssertVectorEqual(Vector3.UnitY, Vector3.Transform(-Vector3.UnitY, q));
    }
}
