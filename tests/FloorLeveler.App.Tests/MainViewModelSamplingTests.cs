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

    [Fact]
    public void Previewing_SuspendsAutoSampling()
    {
        // 2° 傾いた standing にして補正を適用可能にし、プレビュー中に自動記録が
        // 走らない (プレビューが破棄されない) ことを確認する。
        var gateway = new FakeSessionGateway(
            new RigidTransform(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 2f * MathF.PI / 180f), new Vector3(0f, 1f, 0f)));
        var vm = Connected(gateway);
        vm.SamplingMode = SamplingMode.Stillness;
        gateway.PoseForAnyDevice = RigidTransform.CreateTranslation(new Vector3(1f, 0f, 1f));

        vm.PreviewCommand.Execute(null);
        Assert.True(vm.IsPreviewing);

        for (var i = 0; i < 30; i++)
        {
            vm.PollSample();
        }

        Assert.True(vm.IsPreviewing);      // プレビューが維持されている
        Assert.True(gateway.PreviewVisible);
        Assert.Equal(0, vm.PointCount);    // 自動記録されていない
    }

    [Fact]
    public void Apply_ResetsSampler()
    {
        // モード A で 2° 傾き。静止で 1 点自動記録 → 適用 → 座標系が変わりサンプラーが
        // リセットされるため、同じ静止姿勢で再び記録できる。
        var gateway = new FakeSessionGateway(
            new RigidTransform(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 2f * MathF.PI / 180f), new Vector3(0f, 1f, 0f)));
        var vm = Connected(gateway);
        vm.SamplingMode = SamplingMode.Stillness;
        gateway.PoseForAnyDevice = RigidTransform.CreateTranslation(new Vector3(1f, 0f, 1f));

        for (var i = 0; i < 20; i++)
        {
            vm.PollSample();
        }

        Assert.Equal(1, vm.PointCount);

        vm.ApplyCommand.Execute(null); // モード A は点群不要で適用可能

        // 適用後もサンプル点は保持される (新座標へ写される) が、サンプラーは
        // リセットされているので、同じ静止姿勢で追加記録できる。
        var before = vm.PointCount;
        for (var i = 0; i < 20; i++)
        {
            vm.PollSample();
        }

        Assert.True(vm.PointCount > before);
    }

    [Fact]
    public void LostTracking_BreaksContinuity()
    {
        // 連続方式で、トラッキングロスト (null ポーズ) を挟んで遠くへワープしても、
        // 復帰直後のフレームは記録されない (ウォームアップ)。
        var gateway = new FakeSessionGateway(RigidTransform.Identity);
        var vm = Connected(gateway);
        vm.SamplingMode = SamplingMode.Continuous;

        gateway.PoseForAnyDevice = RigidTransform.CreateTranslation(new Vector3(0f, 0f, 0f));
        vm.PollSample(); // 基準確立 (初回ポーズは記録しない)
        gateway.PoseForAnyDevice = RigidTransform.CreateTranslation(new Vector3(0.03f, 0f, 0f));
        vm.PollSample(); // ゆっくりドラッグで 1 点目を記録
        var afterFirst = vm.PointCount;
        Assert.True(afterFirst >= 1);

        gateway.PoseForAnyDevice = null; // トラッキングロスト
        vm.PollSample();

        // 復帰後、遠くへワープ (欠落をまたぐ) → 基準の取り直しのみで記録されない。
        gateway.PoseForAnyDevice = RigidTransform.CreateTranslation(new Vector3(3f, 0f, 3f));
        vm.PollSample();

        Assert.Equal(afterFirst, vm.PointCount);
    }

    [Fact]
    public void RecordingPoints_UpdatesVisualizationPlots()
    {
        var gateway = new FakeSessionGateway(RigidTransform.Identity);
        var vm = Connected(gateway); // 手動方式・トラッカー選択

        // 初期状態は空。
        Assert.True(vm.TopPlot.IsEmpty);
        Assert.True(vm.SidePlot.IsEmpty);

        // 平らな床に広がった 4 点を手動記録する。
        var floor = new[]
        {
            new Vector3(0.4f, 0f, 0f),
            new Vector3(-0.4f, 0f, 0.1f),
            new Vector3(0f, 0f, 0.4f),
            new Vector3(0.1f, 0f, -0.4f),
        };
        foreach (var p in floor)
        {
            gateway.PoseForAnyDevice = RigidTransform.CreateTranslation(p);
            vm.RecordPointCommand.Execute(null);
        }

        Assert.Equal(4, vm.PointCount);
        Assert.Equal(4, vm.TopPlot.Points.Count);
        Assert.Equal(4, vm.SidePlot.Points.Count);
        // 3 点以上で平面が推定され、側面ビューに床線が現れる。
        Assert.NotNull(vm.SidePlot.FloorLine);

        // クリアするとプロットも空に戻る。
        vm.ClearPointsCommand.Execute(null);
        Assert.True(vm.TopPlot.IsEmpty);
        Assert.True(vm.SidePlot.IsEmpty);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
