using System.Numerics;
using FloorLeveler.App.Services;
using FloorLeveler.App.ViewModels;
using FloorLeveler.Core;

namespace FloorLeveler.App.Tests;

public class MainViewModelBackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "floorleveler-vmbackup-" + Guid.NewGuid().ToString("N"));

    private static RigidTransform TiltedStanding(float rollDegrees)
        => new(
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, rollDegrees * MathF.PI / 180f),
            new Vector3(0f, 1.0f, 0f));

    private MainViewModel Connected(FakeSessionGateway gateway, out BackupService backup)
    {
        backup = new BackupService(_dir);
        var vm = new MainViewModel(() => gateway, backupService: backup, clock: () => new DateTime(2026, 7, 23, 3, 0, 0));
        vm.ConnectCommand.Execute(null);
        return vm;
    }

    [Fact]
    public void Connect_AutomaticallyCreatesBackup()
    {
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway, out var backup);

        Assert.NotNull(backup.Latest());
        Assert.True(vm.RestoreLatestCommand.CanExecute(null));
    }

    [Fact]
    public void Backup_SavesCurrentState()
    {
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway, out var backup);

        var before = backup.List().Count;
        // clock は固定のため、別名になるよう 1 秒進めた VM で保存する。
        var vm2 = new MainViewModel(() => gateway, backupService: backup, clock: () => new DateTime(2026, 7, 23, 3, 0, 5));
        vm2.ConnectCommand.Execute(null);
        vm2.BackupCommand.Execute(null);

        Assert.True(backup.List().Count > before);
        Assert.Contains("バックアップを保存", vm2.StatusMessage);
    }

    [Fact]
    public void RestoreLatest_RestoresCommittedState()
    {
        // 2° 傾いた状態を接続時に自動退避 → 補正を適用 → 復元で元の 2° に戻る。
        var original = TiltedStanding(2f);
        var gateway = new FakeSessionGateway(original);
        var vm = Connected(gateway, out _);

        vm.ApplyCommand.Execute(null); // 水平化を commit
        Assert.NotEqual(original, gateway.CommittedStanding);

        vm.RestoreLatestCommand.Execute(null);

        var p = new Vector3(1f, 0.5f, -0.7f);
        Assert.True(
            (original.TransformPoint(p) - gateway.CommittedStanding.TransformPoint(p)).Length() < 1e-4f);
        Assert.Contains("復元しました", vm.StatusMessage);
    }

    [Fact]
    public void RestoreLatest_ClearsRecordedSamples()
    {
        // 復元は standing 座標系を不連続に変えるため、旧座標系の点群は破棄される。
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway, out _);
        vm.UseMeasuredFloorMode = true;

        for (var i = 0; i < 4; i++)
        {
            gateway.EnqueuePosition(new Vector3(i * 0.3f, 0f, i * 0.3f));
            vm.RecordPointCommand.Execute(null);
        }

        Assert.True(vm.PointCount > 0);

        vm.RestoreLatestCommand.Execute(null);

        Assert.Equal(0, vm.PointCount);
    }

    [Fact]
    public void RestoreLatest_CommitFailure_Reverts()
    {
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway, out _);
        gateway.CommitResult = false;

        vm.RestoreLatestCommand.Execute(null);

        Assert.True(gateway.RevertCount >= 1);
        Assert.Contains("失敗", vm.StatusMessage);
    }

    [Fact]
    public void BackupCommand_DisabledWhilePreviewing()
    {
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway, out _);

        vm.PreviewCommand.Execute(null);

        Assert.False(vm.BackupCommand.CanExecute(null));
        Assert.False(vm.RestoreLatestCommand.CanExecute(null));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
