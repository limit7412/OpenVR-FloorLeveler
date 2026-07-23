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

        sampler.Feed(TimeSpan.FromSeconds(0.0), At(new Vector3(0f, 0f, 0f)));       // ウォームアップ(基準確立)
        var first = sampler.Feed(TimeSpan.FromSeconds(0.1), At(new Vector3(0.08f, 0f, 0f)));  // 8cm 移動 → 記録
        var second = sampler.Feed(TimeSpan.FromSeconds(0.2), At(new Vector3(0.10f, 0f, 0f))); // +2cm → 間隔不足

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void Feed_FirstPose_IsWarmupAndNotRecorded()
    {
        var sampler = new ContinuousSampler(Zero, minSpacingMeters: 0.05f);

        // 切り替え直後の初回ポーズ (手に持った高さなど) は基準確立のみで記録しない。
        var first = sampler.Feed(TimeSpan.FromSeconds(0.0), At(new Vector3(0.5f, 0f, 0.5f)));
        Assert.Null(first);

        // 2 フレーム目以降、ゆっくり引きずると記録される。
        var dragged = sampler.Feed(TimeSpan.FromSeconds(0.1), At(new Vector3(0.56f, 0f, 0.5f)));
        Assert.NotNull(dragged);
    }

    [Fact]
    public void Feed_HeldStillInAirAfterLift_DoesNotRecord()
    {
        var sampler = new ContinuousSampler(Zero, maxSpeedMetersPerSecond: 1.0f, minSpacingMeters: 0.05f);

        // 床でドラッグして 1 点記録。
        sampler.Feed(TimeSpan.FromSeconds(0.0), At(new Vector3(0f, 0f, 0f)));       // 基準
        var recorded = sampler.Feed(TimeSpan.FromSeconds(0.1), At(new Vector3(0.06f, 0f, 0f)));
        Assert.NotNull(recorded);

        // 素早く空中へ持ち上げる → 速度超過で棄却。
        var lifted = sampler.Feed(TimeSpan.FromSeconds(0.2), At(new Vector3(0.06f, 0.8f, 0f)));
        Assert.Null(lifted);

        // 空中でしばらく静止するだけ (記録点から 5cm 以上離れているが速度 0)。
        // 棄却後は基準を取り直し、静止は記録しないため接地点にならない。
        for (var i = 0; i < 10; i++)
        {
            var still = sampler.Feed(TimeSpan.FromSeconds(0.3 + i * 0.1), At(new Vector3(0.06f, 0.8f, 0f)));
            Assert.Null(still);
        }
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
    public void BreakContinuity_ThenFastMove_DoesNotRecordAcrossGap()
    {
        var sampler = new ContinuousSampler(Zero, maxSpeedMetersPerSecond: 1.0f, minSpacingMeters: 0.01f);

        sampler.Feed(TimeSpan.FromSeconds(0.0), At(new Vector3(0f, 0f, 0f)));
        // トラッキングロストで連続性が切れる。
        sampler.BreakContinuity();

        // 復帰後、欠落をまたいで遠くへ移動 (時間差が大きく速度は小さく見える)。
        var warm = sampler.Feed(TimeSpan.FromSeconds(5.0), At(new Vector3(2f, 0f, 0f)));
        // ウォームアップフレームは記録しない (欠落中の高速移動を外れ点にしない)。
        Assert.Null(warm);

        // 次のフレームからは通常の速度判定に戻る。0.1s で 2cm = 0.2 m/s → 記録。
        var next = sampler.Feed(TimeSpan.FromSeconds(5.1), At(new Vector3(2.02f, 0f, 0f)));
        Assert.NotNull(next);
    }

    [Fact]
    public void Feed_ResumesRecordingAfterFastMotion()
    {
        var sampler = new ContinuousSampler(Zero, maxSpeedMetersPerSecond: 1.0f, minSpacingMeters: 0.01f);

        sampler.Feed(TimeSpan.FromSeconds(0.0), At(new Vector3(0f, 0f, 0f)));       // 基準
        sampler.Feed(TimeSpan.FromSeconds(0.1), At(new Vector3(0.5f, 0.5f, 0f)));   // 高速 → 棄却、連続性を切る
        // 棄却直後のフレームは基準の取り直しのみで記録しない (棄却ポーズを基準にしない)。
        var rebasis = sampler.Feed(TimeSpan.FromSeconds(0.2), At(new Vector3(0.55f, 0.5f, 0f)));
        Assert.Null(rebasis);

        // 再び床でゆっくり動かす (0.05m / 0.1s = 0.5 m/s) → 記録が再開する。
        var resumed = sampler.Feed(TimeSpan.FromSeconds(0.3), At(new Vector3(0.60f, 0.5f, 0f)));
        Assert.NotNull(resumed);
    }
}
