using System.Numerics;
using FloorLeveler.Core;

namespace FloorLeveler.Core.Tests;

public class SamplingTests
{
    private static TimedSample At(double seconds, float x, float y, float z)
        => new(TimeSpan.FromSeconds(seconds), new Vector3(x, y, z));

    [Fact]
    public void IsStill_StationaryDevice_ReturnsTrue()
    {
        var history = new[]
        {
            At(0.0, 1f, 0f, 1f),
            At(0.2, 1.001f, 0f, 1f),
            At(0.4, 1f, 0.001f, 1f),
            At(0.6, 1.001f, 0.001f, 1f),
        };

        Assert.True(Sampling.IsStill(history));
    }

    [Fact]
    public void IsStill_MovingDevice_ReturnsFalse()
    {
        var history = new[]
        {
            At(0.0, 1f, 0f, 1f),
            At(0.2, 1.01f, 0f, 1f),
            At(0.4, 1.02f, 0f, 1f),
            At(0.6, 1.03f, 0f, 1f),
        };

        Assert.False(Sampling.IsStill(history));
    }

    [Fact]
    public void IsStill_InsufficientHistory_ReturnsFalse()
    {
        // 窓 (500 ms) を覆う観測が無い場合は静止と判定しない。
        var history = new[]
        {
            At(0.0, 1f, 0f, 1f),
            At(0.1, 1f, 0f, 1f),
        };

        Assert.False(Sampling.IsStill(history));
    }

    [Fact]
    public void IsStill_MovementOutsideWindow_IsIgnored()
    {
        // 0.6 秒前の大きな移動は窓の外なので無視される。
        var history = new[]
        {
            At(0.0, 5f, 0f, 5f),
            At(0.4, 1f, 0f, 1f),
            At(0.7, 1.001f, 0f, 1f),
            At(1.0, 1f, 0f, 1f),
        };

        Assert.True(Sampling.IsStill(history));
    }

    [Fact]
    public void ExceedsSpeed_FastMovement_ReturnsTrue()
    {
        var prev = At(0.0, 0f, 0f, 0f);
        var next = At(0.1, 0.2f, 0f, 0f); // 2 m/s

        Assert.True(Sampling.ExceedsSpeed(prev, next));
    }

    [Fact]
    public void ExceedsSpeed_SlowDrag_ReturnsFalse()
    {
        var prev = At(0.0, 0f, 0f, 0f);
        var next = At(0.1, 0.03f, 0f, 0f); // 0.3 m/s (床を引きずる想定)

        Assert.False(Sampling.ExceedsSpeed(prev, next));
    }

    [Fact]
    public void ExceedsSpeed_NonPositiveTimeDelta_ReturnsTrue()
    {
        var prev = At(1.0, 0f, 0f, 0f);
        var same = At(1.0, 0f, 0f, 0f);

        Assert.True(Sampling.ExceedsSpeed(prev, same));
    }

    [Fact]
    public void HorizontalSpread_SquareLayout_ReturnsSideLength()
    {
        var points = new[]
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(0.5f, 0f, 0f),
            new Vector3(0f, 0f, 0.5f),
            new Vector3(0.5f, 0f, 0.5f),
        };

        Assert.Equal(0.5f, Sampling.HorizontalSpread(points), 4);
    }

    [Fact]
    public void HorizontalSpread_CollinearPoints_ReturnsZero()
    {
        var points = Enumerable.Range(0, 5)
            .Select(i => new Vector3(i * 0.2f, 0f, 0f))
            .ToArray();

        Assert.Equal(0f, Sampling.HorizontalSpread(points), 4);
    }

    [Fact]
    public void HorizontalSpread_HeightDoesNotCount()
    {
        // Y 方向の広がりは水平の広がりに寄与しない。
        var points = new[]
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(0.1f, 1f, 0f),
            new Vector3(0.05f, 2f, 0.02f),
        };

        Assert.True(Sampling.HorizontalSpread(points) < 0.1f);
    }

    [Fact]
    public void Evaluate_GoodPointCloud_IsSufficient()
    {
        var points = new[]
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(0.6f, 0f, 0f),
            new Vector3(0f, 0f, 0.6f),
            new Vector3(0.6f, 0f, 0.6f),
        };

        var quality = Sampling.Evaluate(points);

        Assert.True(quality.IsSufficient);
        Assert.Equal(4, quality.PointCount);
    }

    [Fact]
    public void Evaluate_NarrowSpread_IsInsufficient()
    {
        // 30 cm 未満の広がりでは不足 (仕様 F-1)。
        var points = new[]
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(0.2f, 0f, 0f),
            new Vector3(0f, 0f, 0.2f),
            new Vector3(0.2f, 0f, 0.2f),
        };

        var quality = Sampling.Evaluate(points);

        Assert.False(quality.IsSufficient);
        Assert.True(quality.HasEnoughPoints);
        Assert.False(quality.HasEnoughSpread);
    }

    [Fact]
    public void Evaluate_TooFewPoints_IsInsufficient()
    {
        var points = new[]
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(0.6f, 0f, 0.6f),
        };

        var quality = Sampling.Evaluate(points);

        Assert.False(quality.IsSufficient);
        Assert.False(quality.HasEnoughPoints);
    }

    [Fact]
    public void DeviceProfile_ContactPointAppliesLocalOffset()
    {
        var profile = new DeviceContactProfile("test", new Vector3(0f, -0.01f, 0f));
        var pose = RigidTransform.CreateTranslation(new Vector3(1f, 0.05f, 2f));

        var contact = profile.ContactPoint(pose);

        Assert.Equal(new Vector3(1f, 0.04f, 2f), contact);
    }
}
