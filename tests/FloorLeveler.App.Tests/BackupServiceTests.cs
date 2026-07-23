using System.Numerics;
using FloorLeveler.App.Services;
using FloorLeveler.Core;

namespace FloorLeveler.App.Tests;

public class BackupServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "floorleveler-test-" + Guid.NewGuid().ToString("N"));

    private static ChaperoneSnapshot Sample(float roll)
        => ChaperoneSnapshot.Create(
            new RigidTransform(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, roll), new Vector3(0f, 1f, 0f)),
            RigidTransform.Identity,
            [],
            (2f, 2f));

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var service = new BackupService(_dir);
        var snapshot = Sample(0.05f);

        var path = service.Save(snapshot, new DateTime(2026, 7, 23, 1, 2, 3));
        var loaded = service.Load(path);

        var p = new Vector3(1f, 0.5f, -0.7f);
        Assert.True(
            (snapshot.Standing().TransformPoint(p) - loaded.Standing().TransformPoint(p)).Length() < 1e-5f);
    }

    [Fact]
    public void List_ReturnsNewestFirst()
    {
        var service = new BackupService(_dir);
        service.Save(Sample(0.01f), new DateTime(2026, 7, 23, 1, 0, 0), BackupKind.Manual);
        service.Save(Sample(0.02f), new DateTime(2026, 7, 23, 2, 0, 0), BackupKind.Manual);

        var list = service.List();

        Assert.Equal(2, list.Count);
        Assert.Equal("20260723-020000", list[0].Timestamp);
        Assert.Equal("20260723-010000", list[1].Timestamp);
        Assert.Equal(list[0].Path, service.LatestRestorable()!.Path);
    }

    [Fact]
    public void List_EmptyDirectory_ReturnsEmpty()
    {
        var service = new BackupService(_dir);
        Assert.Empty(service.List());
        Assert.Null(service.LatestRestorable());
    }

    [Fact]
    public void Save_SameSecond_DoesNotOverwrite()
    {
        var service = new BackupService(_dir);
        var timestamp = new DateTime(2026, 7, 23, 1, 0, 0);

        var p1 = service.Save(Sample(0.01f), timestamp, BackupKind.PreApply);
        var p2 = service.Save(Sample(0.02f), timestamp, BackupKind.PreApply);

        Assert.NotEqual(p1, p2);
        Assert.True(File.Exists(p1));
        Assert.True(File.Exists(p2));
        Assert.Equal(2, service.List().Count);
    }

    [Fact]
    public void LatestRestorable_SameSecondDifferentKind_UsesSaveOrder()
    {
        // 同一秒に preapply → manual と保存した場合、種別の文字列順ではなく
        // 保存順で最新 (manual) が復元候補になる。
        var service = new BackupService(_dir);
        var timestamp = new DateTime(2026, 7, 23, 1, 0, 0);
        service.Save(Sample(0.01f), timestamp, BackupKind.PreApply);
        service.Save(Sample(0.02f), timestamp, BackupKind.Manual);

        Assert.Equal(BackupKind.Manual, service.LatestRestorable()!.Kind);
    }

    [Fact]
    public void LatestRestorable_ClockWentBackwards_UsesSaveOrder()
    {
        // DST フォールバックや手動時刻修正で 2 回目の保存が早い時刻になっても、
        // 保存順 (seq) が一次キーのため後から保存した方が最新になる。
        var service = new BackupService(_dir);
        service.Save(Sample(0.01f), new DateTime(2026, 7, 23, 2, 0, 0), BackupKind.Manual);
        service.Save(Sample(0.02f), new DateTime(2026, 7, 23, 1, 0, 0), BackupKind.Manual);

        var latest = service.LatestRestorable();

        // 後から保存した (時刻は早い 01:00:00 の) ファイルが最新。
        Assert.Equal("20260723-010000", latest!.Timestamp);
    }

    [Fact]
    public void Save_TwoInstances_SameSecond_KeepSaveOrder()
    {
        // 2 インスタンスが同じディレクトリを共有し同一秒に別種別を保存しても、
        // seq が種別非依存で一意になり、後から保存した方が最新になる。
        var timestamp = new DateTime(2026, 7, 23, 1, 0, 0);
        var a = new BackupService(_dir);
        var b = new BackupService(_dir);

        a.Save(Sample(0.01f), timestamp, BackupKind.PreApply);
        b.Save(Sample(0.02f), timestamp, BackupKind.Manual);

        Assert.Equal(BackupKind.Manual, b.LatestRestorable()!.Kind);
        Assert.Equal(2, b.List().Count);
    }

    [Fact]
    public void Save_NewInstance_ContinuesSequenceAcrossRestart()
    {
        // 再起動を模して別インスタンスで同一秒に保存しても、既存の最大連番から
        // 継続するため保存順が保たれる (preapply より後の manual が最新)。
        var timestamp = new DateTime(2026, 7, 23, 1, 0, 0);
        new BackupService(_dir).Save(Sample(0.01f), timestamp, BackupKind.PreApply);

        var restarted = new BackupService(_dir);
        restarted.Save(Sample(0.02f), timestamp, BackupKind.Manual);

        Assert.Equal(BackupKind.Manual, restarted.LatestRestorable()!.Kind);
        Assert.Equal(2, restarted.List().Count);
    }

    [Fact]
    public void LatestRestorable_ExcludesAutoBackups()
    {
        var service = new BackupService(_dir);
        // 手動退避のあとに (より新しい) 自動退避があっても、復元候補は手動退避のまま。
        service.Save(Sample(0.01f), new DateTime(2026, 7, 23, 1, 0, 0), BackupKind.Manual);
        service.Save(Sample(0.02f), new DateTime(2026, 7, 23, 2, 0, 0), BackupKind.Auto);

        var restorable = service.LatestRestorable();

        Assert.NotNull(restorable);
        Assert.Equal(BackupKind.Manual, restorable!.Kind);
        // List 自体には Auto も含まれる。
        Assert.Equal(2, service.List().Count);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
