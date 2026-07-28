using System.Numerics;
using FloorLeveler.App.Services;
using FloorLeveler.App.ViewModels;
using FloorLeveler.Core;

namespace FloorLeveler.App.Tests;

public class MainViewModelTests : IDisposable
{
    // 単体テストが実バックアップディレクトリ (%LOCALAPPDATA%) を汚染しないよう、
    // すべての MainViewModel に一時ディレクトリの BackupService を注入する。
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "floorleveler-vmtest-" + Guid.NewGuid().ToString("N"));

    private static RigidTransform TiltedStanding(float rollDegrees)
        => new(
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, rollDegrees * MathF.PI / 180f),
            new Vector3(0f, 1.0f, 0f));

    private MainViewModel Create(Func<ISessionGateway> factory, AppSettings? settings = null)
        => new(factory, settings, new BackupService(_dir));

    private MainViewModel Connected(FakeSessionGateway gateway)
    {
        var vm = Create(() => gateway);
        vm.ConnectCommand.Execute(null);
        return vm;
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Connect_ListsOnlySamplingDevices()
    {
        // HMD やベースステーションは床に置けないため一覧から除外される (F-1)。
        var gateway = new FakeSessionGateway(RigidTransform.Identity);
        var vm = Connected(gateway);

        Assert.True(vm.IsConnected);
        Assert.Single(vm.Devices);
        Assert.Equal("GenericTracker", vm.Devices[0].Kind);
        Assert.NotNull(vm.SelectedDevice);
    }

    [Fact]
    public void Connect_ChaperoneFailureAfterInit_ReturnsToDisconnected()
    {
        // OpenVR 初期化は成功しても ChaperoneSetup の読み取りで失敗する環境では
        // 未接続状態に戻り、未処理例外にならない。
        var gateway = new FakeSessionGateway(RigidTransform.Identity)
        {
            ThrowOnGetStandingZeroPose = true,
        };
        var vm = Create(() => gateway);

        vm.ConnectCommand.Execute(null);

        Assert.False(vm.IsConnected);
        Assert.StartsWith(Localized.Fixed(s => s.StatusConnectFailed(Localized.Arg)), vm.StatusMessage);
    }

    [Fact]
    public void Connect_Failure_SetsStatusAndStaysDisconnected()
    {
        var vm = Create(() => throw new SessionUnavailableException(SessionFailure.Runtime, "no steamvr"));

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
        Assert.Equal(Localized.Ja.StatusCorrectionNegligible, vm.CorrectionSummary);
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
        Assert.Equal(Localized.Ja.StatusNoValidPose, vm.StatusMessage);
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
        Assert.StartsWith(Localized.Fixed(s => s.CorrectionSummaryValue(0f, 0f)).Split(' ')[0], vm.CorrectionSummary);
    }

    [Fact]
    public void Apply_CommitsAndEnablesUndo()
    {
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway);

        Assert.False(vm.UndoCommand.CanExecute(null));
        vm.ApplyCommand.Execute(null);

        Assert.True(gateway.CommitCount >= 1);
        Assert.Equal(Localized.Ja.StatusApplied, vm.StatusMessage);
        Assert.True(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void Apply_CommitFailure_Reverts()
    {
        var gateway = new FakeSessionGateway(TiltedStanding(2f)) { CommitResult = false };
        var vm = Connected(gateway);

        vm.ApplyCommand.Execute(null);

        Assert.True(gateway.RevertCount >= 1);
        Assert.Equal(Localized.Ja.StatusApplyFailedReverted, vm.StatusMessage);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void Undo_RestoresOriginalStandingPose()
    {
        var original = TiltedStanding(2f);
        var gateway = new FakeSessionGateway(original);
        var vm = Connected(gateway);

        vm.ApplyCommand.Execute(null);
        vm.UndoCommand.Execute(null);

        var p = new Vector3(1f, 0.5f, -0.7f);
        Assert.True(
            (original.TransformPoint(p) - gateway.CommittedStanding.TransformPoint(p)).Length() < 1e-4f);
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

    [Fact]
    public void PreviewThenApply_AppliesCorrectionExactlyOnce()
    {
        // プレビューで working copy に適用済みの状態から「適用」しても
        // 補正が二重に合成されないこと。
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway);

        vm.PreviewCommand.Execute(null);
        vm.ApplyCommand.Execute(null);

        // 二重適用なら up 軸が逆側に 2° 傾く。1 回だけなら重力方向と一致する。
        var up = gateway.CommittedStanding.TransformVector(Vector3.UnitY);
        Assert.True((up - Vector3.UnitY).Length() < 1e-4f, $"standing up = {up}");
        Assert.False(vm.IsPreviewing);
        Assert.False(gateway.PreviewVisible);
    }

    [Fact]
    public void CancelPreview_RevertsWorkingCopy()
    {
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway);

        vm.PreviewCommand.Execute(null);
        Assert.True(vm.CancelPreviewCommand.CanExecute(null));

        vm.CancelPreviewCommand.Execute(null);

        Assert.False(vm.IsPreviewing);
        Assert.False(gateway.PreviewVisible);
        Assert.Equal(0, gateway.CommitCount);
        var p = new Vector3(1f, 0.5f, -0.7f);
        Assert.True(
            (gateway.WorkingStanding.TransformPoint(p) - gateway.CommittedStanding.TransformPoint(p)).Length() < 1e-5f);
    }

    [Fact]
    public void LargeCorrection_RequiresAcknowledgementBeforeApply()
    {
        // 12° 傾いた standing → モード A でも 10° 超の確認が必要。
        var gateway = new FakeSessionGateway(TiltedStanding(12f));
        var vm = Connected(gateway);

        Assert.True(vm.NeedsConfirmation);
        Assert.False(vm.CanApply);
        Assert.False(vm.ApplyCommand.CanExecute(null));

        vm.LargeCorrectionAcknowledged = true;

        Assert.True(vm.CanApply);
        Assert.True(vm.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public void GravityAlign_PoorQualityFloorSamples_DoesNotChangeHeight()
    {
        // 品質不足 (狭い 3 点、机上の誤記録を想定) の床サンプルは
        // モード A の高さ合わせに使わない。
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway);

        gateway.EnqueuePosition(new Vector3(0.00f, 0.75f, 0.00f));
        gateway.EnqueuePosition(new Vector3(0.05f, 0.75f, 0.00f));
        gateway.EnqueuePosition(new Vector3(0.00f, 0.75f, 0.05f));
        for (var i = 0; i < 3; i++)
        {
            vm.RecordPointCommand.Execute(null);
        }

        vm.ApplyCommand.Execute(null);

        // 高さ合わせが行われていなければ standing 原点の高さは変わらない。
        Assert.Equal(TiltedStanding(2f).Translation.Y, gateway.CommittedStanding.Translation.Y, 3);
    }

    [Fact]
    public void Apply_Twice_DoesNotReapplyStaleCorrection()
    {
        // 適用後は保留補正が新姿勢基準で再計算されるため、二度押しで
        // 同じ補正が再合成されない。
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway);

        vm.ApplyCommand.Execute(null);
        var committedAfterFirst = gateway.CommittedStanding;

        Assert.False(vm.CanApply); // 水平化済みなので補正不要
        vm.ApplyCommand.Execute(null);

        Assert.Equal(committedAfterFirst, gateway.CommittedStanding);
        Assert.Equal(1, gateway.CommitCount);
    }

    [Fact]
    public void RecomputeDuringPreview_DiscardsPreviewAndKeepsLiveBaseline()
    {
        // プレビュー中に RANSAC を切り替えても、補正済み working copy を基準に
        // 再計算せず、プレビューを破棄して Live 基準で計算し直す。
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway);

        vm.PreviewCommand.Execute(null);
        vm.UseRansac = true; // Recompute が走る

        Assert.False(vm.IsPreviewing);
        Assert.False(gateway.PreviewVisible);
        Assert.True(vm.CanApply); // Live 基準なら 2° の補正は引き続き有効
    }

    [Fact]
    public void Dispose_DuringPreview_RevertsWorkingCopy()
    {
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway);

        vm.PreviewCommand.Execute(null);
        vm.Dispose();

        Assert.False(gateway.PreviewVisible);
        var p = new Vector3(1f, 0.5f, -0.7f);
        Assert.True(
            (gateway.WorkingStanding.TransformPoint(p) - gateway.CommittedStanding.TransformPoint(p)).Length() < 1e-5f);
    }

    [Fact]
    public void Settings_RansacThresholdIsAppliedToEstimation()
    {
        // 閾値を 20mm に緩めた設定では、6mm 浮いた点もインライアとして扱われる。
        var gateway = new FakeSessionGateway(RigidTransform.Identity);
        var settings = new AppSettings
        {
            UseRansac = true,
            RansacThresholdMeters = 0.02f,
        };
        var vm = Create(() => gateway, settings);
        vm.ConnectCommand.Execute(null);
        vm.UseMeasuredFloorMode = true;

        foreach (var (ix, iz) in GridIndices())
        {
            var y = (ix + iz) % 2 == 0 ? 0f : 0.006f; // 既定閾値 3mm では外れ値になる散らばり
            gateway.EnqueuePosition(new Vector3(ix * 0.5f, y, iz * 0.5f));
            vm.RecordPointCommand.Execute(null);
        }

        // 全点がインライア扱いなら十分な広がりが維持され補正可能になる。
        Assert.True(vm.CanApply);
    }

    [Fact]
    public void Settings_AreLoadedAndSnapshotted()
    {
        var gateway = new FakeSessionGateway(RigidTransform.Identity);
        var settings = new AppSettings { UseRansac = true };
        var vm = Create(() => gateway, settings);

        Assert.True(vm.UseRansac);

        vm.UseRansac = false;
        var snapshot = vm.SnapshotSettings(800, 600);

        Assert.False(snapshot.UseRansac);
        Assert.Equal(800, snapshot.WindowWidth);
        Assert.Equal(600, snapshot.WindowHeight);
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
