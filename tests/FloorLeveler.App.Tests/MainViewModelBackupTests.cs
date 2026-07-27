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
    public void Connect_CreatesAutoBackup_NotRestorableUntilChange()
    {
        // 接続時は自動退避 (Auto) のみ。Auto は復元対象外のため、まだ復元できない。
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway, out var backup);

        Assert.Single(backup.List());
        Assert.Equal(BackupKind.Auto, backup.List()[0].Kind);
        Assert.Null(backup.LatestRestorable());
        Assert.False(vm.RestoreLatestCommand.CanExecute(null));
    }

    [Fact]
    public void Backup_SavesManualRestorableEntry()
    {
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway, out var backup);

        vm.BackupCommand.Execute(null);

        Assert.Equal(BackupKind.Manual, backup.LatestRestorable()!.Kind);
        Assert.True(vm.RestoreLatestCommand.CanExecute(null));
        Assert.Contains("バックアップを保存", vm.StatusMessage);
    }

    [Fact]
    public void Apply_SavesPreApplyBackup()
    {
        // 適用前の状態が PreApply として復元候補に残る。
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway, out var backup);

        vm.ApplyCommand.Execute(null);

        Assert.Equal(BackupKind.PreApply, backup.LatestRestorable()!.Kind);
    }

    [Fact]
    public void RestoreLatest_RestoresPreApplyState()
    {
        // 2° 傾いた状態を接続 → 補正を適用 (適用前に PreApply 退避) → 復元で 2° に戻る。
        var original = TiltedStanding(2f);
        var gateway = new FakeSessionGateway(original);
        var vm = Connected(gateway, out _);

        vm.ApplyCommand.Execute(null); // 水平化を commit (適用前状態=2° を退避)
        Assert.NotEqual(original, gateway.CommittedStanding);

        vm.RestoreLatestCommand.Execute(null);

        var p = new Vector3(1f, 0.5f, -0.7f);
        Assert.True(
            (original.TransformPoint(p) - gateway.CommittedStanding.TransformPoint(p)).Length() < 1e-4f);
        Assert.Contains("復元しました", vm.StatusMessage);
    }

    [Fact]
    public void RestoreLatest_ExcludesAutoBackup()
    {
        // Auto しか無い状態では復元は行われない (自動退避は復元対象外)。
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway, out _);

        vm.RestoreLatestCommand.Execute(null);

        Assert.Equal(0, gateway.CommitCount);
    }

    [Fact]
    public void RestoreLatest_ClearsRecordedSamples()
    {
        // 復元は standing 座標系を不連続に変えるため、旧座標系の点群は破棄される。
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway, out _);
        vm.BackupCommand.Execute(null); // 復元候補を作る
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
    public void Apply_AbortsWhenPreApplyBackupFails()
    {
        // バックアップ先を「ファイル」にして Directory.CreateDirectory を失敗させ、
        // 適用前バックアップが作れない状況を再現する。
        Directory.CreateDirectory(_dir);
        var filePath = Path.Combine(_dir, "not-a-directory");
        File.WriteAllText(filePath, "x");
        var backup = new BackupService(filePath);

        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = new MainViewModel(() => gateway, backupService: backup, clock: () => new DateTime(2026, 7, 23, 3, 0, 0));
        vm.ConnectCommand.Execute(null);

        vm.ApplyCommand.Execute(null);

        Assert.Equal(0, gateway.CommitCount);
        Assert.Contains("適用を中止", vm.StatusMessage);
    }

    [Fact]
    public void RestoreLatest_SkipsCorruptBackup()
    {
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway, out var backup);
        vm.BackupCommand.Execute(null); // 有効な手動退避 (3:00:00)

        // より新しい「壊れた」候補を直接書き込む。
        File.WriteAllText(
            Path.Combine(_dir, "20260723-040000-999999999-manual.json"),
            "this is not valid json {");

        vm.RestoreLatestCommand.Execute(null);

        // 破損した最新をスキップして有効な手動退避へ復元できる。
        Assert.True(gateway.CommitCount >= 1);
        Assert.Contains("スキップ", vm.StatusMessage);
    }

    [Fact]
    public void RestoreLatest_SkipsStructurallyInvalidBackup()
    {
        // JSON としては読めるが StandingToRaw が 3x4 でない (行が 3 要素) ファイルは
        // RestoreSnapshot で例外になる。これをスキップして有効な候補へ復元する。
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway, out _);
        vm.BackupCommand.Execute(null); // 有効な手動退避

        File.WriteAllText(
            Path.Combine(_dir, "20260723-060000-999999999-manual.json"),
            "{\"StandingToRaw\":[[1,0,0],[0,1,0],[0,0,1]]," +
            "\"SeatedToRaw\":[[1,0,0,0],[0,1,0,0],[0,0,1,0]]," +
            "\"BoundsQuads\":[],\"PlayAreaSize\":null}");

        vm.RestoreLatestCommand.Execute(null);

        Assert.True(gateway.CommitCount >= 1);
        Assert.Contains("スキップ", vm.StatusMessage);
    }

    [Fact]
    public void RestoreLatest_SkipsInvalidPlayAreaSize()
    {
        // JSON も行列も有効だが PlayAreaSize が壊れている ([1]) ファイルは
        // Validate で例外になり、スキップして有効な候補へ復元する。
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway, out _);
        vm.BackupCommand.Execute(null); // 有効な手動退避

        File.WriteAllText(
            Path.Combine(_dir, "20260723-070000-999999999-manual.json"),
            "{\"StandingToRaw\":[[1,0,0,0],[0,1,0,0],[0,0,1,0]]," +
            "\"SeatedToRaw\":[[1,0,0,0],[0,1,0,0],[0,0,1,0]]," +
            "\"BoundsQuads\":[],\"PlayAreaSize\":[1]}");

        vm.RestoreLatestCommand.Execute(null);

        Assert.True(gateway.CommitCount >= 1);
        Assert.Contains("スキップ", vm.StatusMessage);
    }

    [Fact]
    public void RestoreLatest_SkipsCandidateWhoseCommitFails()
    {
        // 最新候補の commit が OpenVR に拒否された場合も、次の有効な候補へ進む。
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway, out _);
        vm.BackupCommand.Execute(null); // 手動退避 #1
        vm.BackupCommand.Execute(null); // 手動退避 #2 (より新しい)
        gateway.CommitFailCount = 1; // 最新候補の commit のみ失敗させる

        vm.RestoreLatestCommand.Execute(null);

        // 2 回 commit を試み (1 回目失敗・2 回目成功)、復元に成功する。
        Assert.Equal(2, gateway.CommitCount);
        Assert.Contains("復元しました", vm.StatusMessage);
        Assert.Contains("スキップ", vm.StatusMessage);
    }

    [Fact]
    public void RestoreLatest_AllCandidatesCorrupt_ReportsFailure()
    {
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway, out _);
        File.WriteAllText(
            Path.Combine(_dir, "20260723-050000-999999999-manual.json"),
            "broken {");

        vm.RestoreLatestCommand.Execute(null);

        Assert.Equal(0, gateway.CommitCount);
        Assert.Contains("すべて復元に失敗", vm.StatusMessage);
    }

    [Fact]
    public void RestoreLatest_CommitFailure_Reverts()
    {
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway, out _);
        vm.BackupCommand.Execute(null); // 復元候補を作る
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

    [Fact]
    public void Backups_ListsAllKindsNewestFirst()
    {
        // 一覧は自動退避も含めて全件を新しい順に並べる (任意時点を選べるようにするため)。
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway, out _); // 接続で Auto が 1 件
        vm.BackupCommand.Execute(null);     // 手動で 1 件

        Assert.Equal(2, vm.Backups.Count);
        Assert.Equal(BackupKind.Manual, vm.Backups[0].Kind); // 新しい順
        Assert.Equal(BackupKind.Auto, vm.Backups[1].Kind);
        Assert.Equal("手動", vm.Backups[0].KindLabel);
        Assert.Contains("2026-07-23 03:00:00", vm.Backups[0].DisplayName);
    }

    [Fact]
    public void RestoreSelected_RequiresSelection()
    {
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway, out _);
        vm.BackupCommand.Execute(null);

        Assert.Null(vm.SelectedBackup);
        Assert.False(vm.RestoreSelectedCommand.CanExecute(null));

        vm.SelectedBackup = vm.Backups[0];
        Assert.True(vm.RestoreSelectedCommand.CanExecute(null));
    }

    [Fact]
    public void RestoreSelected_RestoresChosenSnapshotNotLatest()
    {
        // 2° の状態を退避 → 補正を適用して別状態にする → 一覧から「最初に退避した 2°」を
        // 明示的に選んで復元すると、最新ではなくその時点の S→R に戻る。
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway, out _);
        vm.BackupCommand.Execute(null);
        var chosen = vm.Backups[0];              // 2° 時点の手動退避
        var expected = gateway.CommittedStanding;

        vm.ApplyCommand.Execute(null);           // 適用で状態が変わる (PreApply も増える)
        Assert.NotEqual(expected, gateway.CommittedStanding);
        Assert.NotEqual(chosen.Path, vm.Backups[0].Path); // 最新はもう別の退避

        vm.SelectedBackup = vm.Backups.First(e => e.Path == chosen.Path);
        vm.RestoreSelectedCommand.Execute(null);

        Assert.Equal(expected, gateway.CommittedStanding);
        Assert.Contains("復元しました", vm.StatusMessage);
    }

    [Fact]
    public void RestoreSelected_CanRestoreAutoBackup()
    {
        // 自動退避は「最新の復元」からは除外されるが、明示的に選べば復元できる。
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway, out _);
        var auto = vm.Backups.Single(e => e.Kind == BackupKind.Auto);
        var expected = gateway.CommittedStanding;

        vm.ApplyCommand.Execute(null);
        Assert.NotEqual(expected, gateway.CommittedStanding);

        vm.SelectedBackup = vm.Backups.First(e => e.Path == auto.Path);
        vm.RestoreSelectedCommand.Execute(null);

        Assert.Equal(expected, gateway.CommittedStanding);
    }

    [Fact]
    public void RestoreSelected_CorruptEntry_FailsWithoutFallingBack()
    {
        // 選択した 1 件が壊れていても、他の候補へ勝手に復元しない
        // (ユーザーが指定した時点と違う状態を黙って書き込まないため)。
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway, out _);
        vm.BackupCommand.Execute(null); // 有効な手動退避

        var corrupt = Path.Combine(_dir, "20260723-050000-999999999-manual.json");
        File.WriteAllText(corrupt, "this is not valid json {");
        vm.RefreshBackupsCommand.Execute(null);

        var before = gateway.CommitCount;
        vm.SelectedBackup = vm.Backups.First(e => e.Path == corrupt);
        vm.RestoreSelectedCommand.Execute(null);

        Assert.Equal(before, gateway.CommitCount); // commit していない
        Assert.Contains("復元できませんでした", vm.StatusMessage);
    }

    [Fact]
    public void Backups_SameSecondSameKind_AreDistinguishableInList()
    {
        // 同一秒に同種を連続保存しても一覧表示が同一にならない (保存順の連番で区別)。
        // 表示が同じだとユーザーが意図しない時点を復元し得るため。
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway, out _); // クロックは 3:00:00 固定
        vm.BackupCommand.Execute(null);
        vm.BackupCommand.Execute(null);

        var manual = vm.Backups.Where(e => e.Kind == BackupKind.Manual).ToArray();
        Assert.Equal(2, manual.Length);
        Assert.Equal(manual[0].Timestamp, manual[1].Timestamp); // 秒までは同一
        Assert.NotEqual(manual[0].Sequence, manual[1].Sequence);
        Assert.NotEqual(manual[0].DisplayName, manual[1].DisplayName);
    }

    [Fact]
    public void RestoreSelected_CommitThrows_RevertsWithoutPropagating()
    {
        // commit が例外を投げても UI へ伝播させず、revert して失敗として扱う (NF-5)。
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway, out _);
        vm.BackupCommand.Execute(null);
        vm.SelectedBackup = vm.Backups[0];

        gateway.ThrowOnCommit = true;
        var revertsBefore = gateway.RevertCount;

        vm.RestoreSelectedCommand.Execute(null);

        Assert.True(gateway.RevertCount > revertsBefore, "revert されていない");
        Assert.Contains("復元できませんでした", vm.StatusMessage);
    }

    [Fact]
    public void RefreshBackups_KeepsSelectionWhenEntryStillExists()
    {
        var gateway = new FakeSessionGateway(TiltedStanding(2f));
        var vm = Connected(gateway, out _);
        vm.BackupCommand.Execute(null);
        vm.SelectedBackup = vm.Backups[0];
        var selectedPath = vm.SelectedBackup!.Path;

        vm.RefreshBackupsCommand.Execute(null);

        Assert.Equal(selectedPath, vm.SelectedBackup?.Path);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
