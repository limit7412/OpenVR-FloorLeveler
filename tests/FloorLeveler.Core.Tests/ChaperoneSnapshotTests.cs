using System.Numerics;
using FloorLeveler.Core;

namespace FloorLeveler.Core.Tests;

public class ChaperoneSnapshotTests
{
    [Fact]
    public void Create_RoundTripsTransforms()
    {
        var standing = new RigidTransform(
            Quaternion.CreateFromYawPitchRoll(0.3f, -0.1f, 0.2f),
            new Vector3(0.1f, 1.2f, -0.3f));
        var seated = new RigidTransform(Quaternion.Identity, new Vector3(0f, 0.6f, 0f));

        var snapshot = ChaperoneSnapshot.Create(standing, seated, [], (2f, 3f));

        var p = new Vector3(1f, 2f, 3f);
        Assert.True((standing.TransformPoint(p) - snapshot.Standing().TransformPoint(p)).Length() < 1e-5f);
        Assert.True((seated.TransformPoint(p) - snapshot.Seated().TransformPoint(p)).Length() < 1e-5f);
    }

    [Fact]
    public void Create_PreservesBoundsAndPlayArea()
    {
        var quad = new[]
        {
            new Vector3(1f, 0f, 1f),
            new Vector3(1f, 2.4f, 1f),
            new Vector3(-1f, 2.4f, 1f),
            new Vector3(-1f, 0f, 1f),
        };

        var snapshot = ChaperoneSnapshot.Create(RigidTransform.Identity, RigidTransform.Identity, [quad], (2f, 3f));

        var restored = snapshot.BoundsAsVectors();
        Assert.Single(restored);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(quad[i], restored[0][i]);
        }

        Assert.NotNull(snapshot.PlayAreaSize);
        Assert.Equal([2f, 3f], snapshot.PlayAreaSize!);
    }

    [Fact]
    public void Create_NoPlayArea_StoresNull()
    {
        var snapshot = ChaperoneSnapshot.Create(RigidTransform.Identity, RigidTransform.Identity, [], null);
        Assert.Null(snapshot.PlayAreaSize);
    }

    [Fact]
    public void FromRows_InvalidShape_Throws()
    {
        Assert.Throws<ArgumentException>(() => ChaperoneSnapshot.FromRows([[1f, 2f, 3f]]));
    }
}
