using System.Numerics;
using FloorLeveler.Core;

namespace FloorLeveler.Core.Tests;

public class StillnessSamplerTests
{
    private static readonly DeviceContactProfile Zero = new("test", Vector3.Zero);

    private static RigidTransform At(Vector3 p) => RigidTransform.CreateTranslation(p);

    [Fact]
    public void Feed_StillOverWindow_EmitsOnce()
    {
        var sampler = new StillnessSampler(Zero);
        var pos = new Vector3(1f, 0f, 1f);

        var emissions = new List<Vector3>();
        for (var i = 0; i <= 8; i++)
        {
            var e = sampler.Feed(TimeSpan.FromSeconds(i * 0.1), At(pos + new Vector3(0.001f, 0f, 0f) * (i % 2)));
            if (e is { } v)
            {
                emissions.Add(v);
            }
        }

        // 静止が続く間に記録されるのは 1 点だけ。
        Assert.Single(emissions);
        Assert.True((emissions[0] - pos).Length() < 0.01f);
    }

    [Fact]
    public void Feed_NotStillEnoughWindow_DoesNotEmit()
    {
        var sampler = new StillnessSampler(Zero);

        // 窓 (500ms) を覆う前に止めても記録されない。
        var e1 = sampler.Feed(TimeSpan.FromSeconds(0.0), At(new Vector3(1f, 0f, 1f)));
        var e2 = sampler.Feed(TimeSpan.FromSeconds(0.1), At(new Vector3(1f, 0f, 1f)));

        Assert.Null(e1);
        Assert.Null(e2);
    }

    [Fact]
    public void Feed_MovingDevice_DoesNotEmit()
    {
        var sampler = new StillnessSampler(Zero);

        Vector3? last = null;
        for (var i = 0; i <= 8; i++)
        {
            last = sampler.Feed(TimeSpan.FromSeconds(i * 0.1), At(new Vector3(i * 0.05f, 0f, 0f)));
        }

        Assert.Null(last);
    }

    [Fact]
    public void Feed_RecordsAgainAfterMovingAway()
    {
        var sampler = new StillnessSampler(Zero);
        var emissions = new List<Vector3>();
        var t = 0.0;

        void HoldStill(Vector3 pos)
        {
            for (var i = 0; i < 8; i++)
            {
                var e = sampler.Feed(TimeSpan.FromSeconds(t), At(pos));
                if (e is { } v)
                {
                    emissions.Add(v);
                }

                t += 0.1;
            }
        }

        HoldStill(new Vector3(0f, 0f, 0f));       // 1 点目
        HoldStill(new Vector3(0.5f, 0f, 0.5f));   // 5cm 以上離れた別位置 → 2 点目

        Assert.Equal(2, emissions.Count);
        Assert.True((emissions[0] - new Vector3(0f, 0f, 0f)).Length() < 0.01f);
        Assert.True((emissions[1] - new Vector3(0.5f, 0f, 0.5f)).Length() < 0.01f);
    }

    [Fact]
    public void Feed_StayingStill_DoesNotReRecordWithoutMoving()
    {
        var sampler = new StillnessSampler(Zero);
        var emissions = 0;

        for (var i = 0; i < 20; i++)
        {
            if (sampler.Feed(TimeSpan.FromSeconds(i * 0.1), At(new Vector3(1f, 0f, 1f))) is not null)
            {
                emissions++;
            }
        }

        Assert.Equal(1, emissions);
    }
}

public class ContinuousSamplerTests
{
    private static readonly DeviceContactProfile Zero = new("test", Vector3.Zero);

    private static RigidTransform At(Vector3 p) => RigidTransform.CreateTranslation(p);

    [Fact]
    public void Feed_SlowDrag_RecordsAtSpacing()
    {
        var sampler = new ContinuousSampler(Zero, minSpacingMeters: 0.1f);
        var emissions = new List<Vector3>();

        // 0.3 m/s で引きずり、0.1s ごと (=3cm) にフィード。10cm 間隔で記録される。
        for (var i = 0; i <= 20; i++)
        {
            var e = sampler.Feed(TimeSpan.FromSeconds(i * 0.1), At(new Vector3(i * 0.03f, 0f, 0f)));
            if (e is { } v)
            {
                emissions.Add(v);
            }
        }

        Assert.True(emissions.Count >= 5, $"emitted {emissions.Count}");
        // 記録点は最小間隔以上離れている。
        for (var i = 1; i < emissions.Count; i++)
        {
            Assert.True((emissions[i] - emissions[i - 1]).Length() >= 0.1f - 1e-4f);
        }
    }

    [Fact]
    public void Feed_TooClose_DoesNotRecord()
    {
        var sampler = new ContinuousSampler(Zero, minSpacingMeters: 0.1f);

        var first = sampler.Feed(TimeSpan.FromSeconds(0.0), At(new Vector3(0f, 0f, 0f)));
        var second = sampler.Feed(TimeSpan.FromSeconds(0.1), At(new Vector3(0.02f, 0f, 0f))); // 2cm

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void Feed_FastMotion_IsRejected()
    {
        var sampler = new ContinuousSampler(Zero, maxSpeedMetersPerSecond: 1.0f, minSpacingMeters: 0.01f);

        sampler.Feed(TimeSpan.FromSeconds(0.0), At(new Vector3(0f, 0f, 0f)));
        // 0.1s で 0.5m 移動 = 5 m/s → 棄却。
        var lifted = sampler.Feed(TimeSpan.FromSeconds(0.1), At(new Vector3(0.5f, 0.5f, 0f)));

        Assert.Null(lifted);
    }

    [Fact]
    public void Feed_ResumesRecordingAfterFastMotion()
    {
        var sampler = new ContinuousSampler(Zero, maxSpeedMetersPerSecond: 1.0f, minSpacingMeters: 0.01f);

        sampler.Feed(TimeSpan.FromSeconds(0.0), At(new Vector3(0f, 0f, 0f)));
        sampler.Feed(TimeSpan.FromSeconds(0.1), At(new Vector3(0.5f, 0.5f, 0f))); // 棄却
        // 再び床でゆっくり動かす (0.05m / 0.1s = 0.5 m/s)。
        var resumed = sampler.Feed(TimeSpan.FromSeconds(0.2), At(new Vector3(0.55f, 0.5f, 0f)));

        Assert.NotNull(resumed);
    }
}
