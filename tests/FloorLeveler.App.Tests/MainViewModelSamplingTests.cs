using System.Numerics;
using FloorLeveler.App.Services;
using FloorLeveler.App.ViewModels;
using FloorLeveler.Core;

namespace FloorLeveler.App.Tests;

public class MainViewModelSamplingTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "floorleveler-sampling-" + Guid.NewGuid().ToString("N"));

    private long _ticks;

    private MainViewModel Connected(FakeSessionGateway gateway)
    {
        // 各ポールで時刻を 50ms 進める決定的なクロック。
        var vm = new MainViewModel(
            () => gateway,
            backupService: new BackupService(_dir),
            clock: () => new DateTime(_ticks += TimeSpan.FromMilliseconds(50).Ticks));
        vm.ConnectCommand.Execute(null);
        return vm;
    }

    [Fact]
    public void ManualMode_PollDoesNothing()
    {
        var gateway = new FakeSessionGateway(RigidTransform.Identity);
        var vm = Connected(gateway);
        gateway.PoseForAnyDevice = RigidTransform.CreateTranslation(new Vector3(1f, 0f, 1f));

        for (var i = 0; i < 20; i++)
        {
            vm.PollSample();
        }

        Assert.Equal(0, vm.PointCount);
    }

    [Fact]
    public void StillnessMode_HeldStill_AutoRecordsOnePoint()
    {
        var gateway = new FakeSessionGateway(RigidTransform.Identity);
        var vm = Connected(gateway);
        vm.SamplingMode = SamplingMode.Stillness;
        gateway.PoseForAnyDevice = RigidTransform.CreateTranslation(new Vector3(1f, 0f, 1f));

        // 静止を保ったまま十分な回数ポールする (窓 500ms を 50ms 刻みで超える)。
        for (var i = 0; i < 20; i++)
        {
            vm.PollSample();
        }

        Assert.Equal(1, vm.PointCount);
        Assert.False(vm.RecordPointCommand.CanExecute(null)); // 自動方式では手動記録は無効
    }

    [Fact]
    public void ContinuousMode_SlowDrag_AutoRecordsMultiplePoints()
    {
        var gateway = new FakeSessionGateway(RigidTransform.Identity);
        var vm = Connected(gateway);
        vm.SamplingMode = SamplingMode.Continuous;

        // 50ms ごとに 3cm 進む (=0.6 m/s)。最小間隔 5cm ごとに記録される。
        for (var i = 0; i < 30; i++)
        {
            gateway.PoseForAnyDevice = RigidTransform.CreateTranslation(new Vector3(i * 0.03f, 0f, 0f));
            vm.PollSample();
        }

        Assert.True(vm.PointCount >= 3, $"recorded {vm.PointCount}");
    }

    [Fact]
    public void SwitchingToManual_DisablesAutoPolling()
    {
        var gateway = new FakeSessionGateway(RigidTransform.Identity);
        var vm = Connected(gateway);
        vm.SamplingMode = SamplingMode.Stillness;
        vm.SamplingMode = SamplingMode.Manual;
        gateway.PoseForAnyDevice = RigidTransform.CreateTranslation(new Vector3(1f, 0f, 1f));

        for (var i = 0; i < 20; i++)
        {
            vm.PollSample();
        }

        Assert.Equal(0, vm.PointCount);
        Assert.True(vm.IsManualSampling);
    }

    [Fact]
    public void ClearPoints_ResetsSampler()
    {
        var gateway = new FakeSessionGateway(RigidTransform.Identity);
        var vm = Connected(gateway);
        vm.SamplingMode = SamplingMode.Stillness;
        gateway.PoseForAnyDevice = RigidTransform.CreateTranslation(new Vector3(1f, 0f, 1f));

        for (var i = 0; i < 20; i++)
        {
            vm.PollSample();
        }

        Assert.Equal(1, vm.PointCount);

        vm.ClearPointsCommand.Execute(null);
        Assert.Equal(0, vm.PointCount);

        // クリア後、同じ位置で静止を続けると再び記録される (サンプラーがリセットされている)。
        for (var i = 0; i < 20; i++)
        {
            vm.PollSample();
        }

        Assert.Equal(1, vm.PointCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
