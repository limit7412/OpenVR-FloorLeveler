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
        service.Save(Sample(0.01f), new DateTime(2026, 7, 23, 1, 0, 0));
        service.Save(Sample(0.02f), new DateTime(2026, 7, 23, 2, 0, 0));

        var list = service.List();

        Assert.Equal(2, list.Count);
        Assert.Equal("20260723-020000", list[0].Timestamp);
        Assert.Equal("20260723-010000", list[1].Timestamp);
        Assert.Equal(list[0].Path, service.Latest()!.Path);
    }

    [Fact]
    public void List_EmptyDirectory_ReturnsEmpty()
    {
        var service = new BackupService(_dir);
        Assert.Empty(service.List());
        Assert.Null(service.Latest());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
