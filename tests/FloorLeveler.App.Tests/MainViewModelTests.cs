using System.Numerics;
using FloorLeveler.App.ViewModels;
using FloorLeveler.Core;

namespace FloorLeveler.App.Tests;

public class MainViewModelTests
{
    private static RigidTransform TiltedStanding(float rollDegrees)
        => new(
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, rollDegrees * MathF.PI / 180f),
            new Vector3(0f, 1.0f, 0f));

    private static MainViewModel Connected(FakeSessionGateway gateway)
    {
        var vm = new MainViewModel(() => gateway);
        vm.ConnectCommand.Execute(null);
        return vm;
    }

    [Fact]
    public void Connect_PopulatesDevicesAndSelectsFirst()
    {
        var gateway = new FakeSessionGateway(RigidTransform.Identity);
        var vm = Connected(gateway);

        Assert.True(vm.IsConnected);
        Assert.Single(vm.Devices);
        Assert.NotNull(vm.SelectedDevice);
    }

    [Fact]
    public void Connect_Failure_SetsStatusAndStaysDisconnected()
    {
        var vm = new MainViewModel(() => throw new Services.SessionUnavailableException("no steamvr"));

        vm.ConnectCommand.Execute(null);

        Assert.False(vm.IsConnected);
        Assert.Contains("no steamvr", vm.StatusMessage);
    }

    [Fact]
    public void GravityAlign_TiltedStanding_CanApplyWithoutSampling()
    {
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway);

        // 既定はモード A。サンプリングなしで補正可能。
        Assert.False(vm.UseMeasuredFloorMode);
        Assert.False(vm.IsSamplingRequired);
        Assert.True(vm.CanApply);
    }

    [Fact]
    public void GravityAlign_LevelStanding_IsNegligible()
    {
        var gateway = new FakeSessionGateway(TiltedStanding(0f));
        var vm = Connected(gateway);

        Assert.False(vm.CanApply);
        Assert.Contains("補正不要", vm.CorrectionSummary);
    }

    [Fact]
    public void MeasuredFloorMode_RequiresSampling()
    {
        var gateway = new FakeSessionGateway(RigidTransform.Identity);
        var vm = Connected(gateway);

        vm.UseMeasuredFloorMode = true;

        Assert.True(vm.IsSamplingRequired);
        Assert.False(vm.CanApply); // まだサンプルなし
    }

    [Fact]
    public void RecordPoint_InvalidSample_DoesNotAddPoint()
    {
        var gateway = new FakeSessionGateway(RigidTransform.Identity);
        var vm = Connected(gateway);
        gateway.EnqueuePosition(null); // トラッキングロスト

        vm.RecordPointCommand.Execute(null);

        Assert.Equal(0, vm.PointCount);
        Assert.Contains("取得できませんでした", vm.StatusMessage);
    }

    [Fact]
    public void MeasuredFloorMode_AfterSamplingTiltedFloor_CanApply()
    {
        var gateway = new FakeSessionGateway(RigidTransform.Identity);
        var vm = Connected(gateway);
        vm.UseMeasuredFloorMode = true;

        // 3° 傾いた床の点群を記録する。
        var tilt = new RigidTransform(
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 3f * MathF.PI / 180f),
            Vector3.Zero);
        foreach (var (ix, iz) in GridIndices())
        {
            gateway.EnqueuePosition(tilt.TransformPoint(new Vector3(ix * 0.5f, 0f, iz * 0.5f)));
            vm.RecordPointCommand.Execute(null);
        }

        Assert.True(vm.PointCount >= 9);
        Assert.True(vm.CanApply);
        Assert.Contains("回転", vm.CorrectionSummary);
    }

    [Fact]
    public void Apply_CommitsAndEnablesUndo()
    {
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway);

        Assert.False(vm.UndoCommand.CanExecute(null));
        vm.ApplyCommand.Execute(null);

        Assert.True(gateway.CommitCount >= 1);
        Assert.Contains("適用しました", vm.StatusMessage);
        Assert.True(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void Apply_CommitFailure_Reverts()
    {
        var gateway = new FakeSessionGateway(TiltedStanding(2f)) { CommitResult = false };
        var vm = Connected(gateway);

        vm.ApplyCommand.Execute(null);

        Assert.True(gateway.RevertCount >= 1);
        Assert.Contains("失敗", vm.StatusMessage);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void Undo_RestoresOriginalStandingPose()
    {
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway);

        vm.ApplyCommand.Execute(null);
        var applied = gateway.LastCorrection!.ApplyTo(TiltedStanding(2f));

        vm.UndoCommand.Execute(null);

        // アンドゥ補正を適用後の S→R に合成すると元の S→R に戻る。
        var undoMap = gateway.LastCorrection!;
        var restored = undoMap.ApplyTo(applied);
        var original = TiltedStanding(2f);
        var p = new Vector3(1f, 0.5f, -0.7f);
        Assert.True((original.TransformPoint(p) - restored.TransformPoint(p)).Length() < 1e-4f);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void Preview_ShowsPreviewWithoutCommit()
    {
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway);

        vm.PreviewCommand.Execute(null);

        Assert.True(gateway.PreviewVisible);
        Assert.Equal(0, gateway.CommitCount);
        Assert.True(vm.IsPreviewing);
    }

    private static IEnumerable<(int, int)> GridIndices()
    {
        for (var ix = -1; ix <= 1; ix++)
        {
            for (var iz = -1; iz <= 1; iz++)
            {
                yield return (ix, iz);
            }
        }
    }
}
