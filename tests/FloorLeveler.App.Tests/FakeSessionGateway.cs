using System.Numerics;
using FloorLeveler.App.Services;
using FloorLeveler.Core;

namespace FloorLeveler.App.Tests;

/// <summary>テスト用の <see cref="ISessionGateway"/>。SteamVR なしで VM を検証する。</summary>
internal sealed class FakeSessionGateway : ISessionGateway
{
    private readonly Queue<Vector3?> _nextPositions = new();

    public FakeSessionGateway(RigidTransform standingZeroPose)
        => StandingZeroPose = standingZeroPose;

    public RigidTransform StandingZeroPose { get; set; }

    public List<GatewayDevice> DeviceList { get; } =
        [new GatewayDevice(3, "GenericTracker", "Vive Tracker 3.0", "LHR-1234")];

    public bool CommitResult { get; set; } = true;

    public int ApplyCount { get; private set; }

    public int CommitCount { get; private set; }

    public int RevertCount { get; private set; }

    public bool PreviewVisible { get; private set; }

    public CorrectionResult? LastCorrection { get; private set; }

    public void EnqueuePosition(Vector3? position) => _nextPositions.Enqueue(position);

    public IReadOnlyList<GatewayDevice> ListDevices() => DeviceList;

    public Vector3? GetDevicePosition(uint deviceIndex)
        => _nextPositions.Count > 0 ? _nextPositions.Dequeue() : null;

    public RigidTransform GetStandingZeroPose() => StandingZeroPose;

    public AppliedCorrectionInfo ApplyCorrection(CorrectionResult correction)
    {
        ApplyCount++;
        LastCorrection = correction;
        var newStanding = correction.ApplyTo(StandingZeroPose);
        return new AppliedCorrectionInfo(StandingZeroPose, newStanding, 4);
    }

    public bool Commit()
    {
        CommitCount++;
        return CommitResult;
    }

    public void Revert() => RevertCount++;

    public void ShowPreview() => PreviewVisible = true;

    public void HidePreview() => PreviewVisible = false;

    public void Dispose()
    {
    }
}
