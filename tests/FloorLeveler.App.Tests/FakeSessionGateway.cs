using System.Numerics;
using FloorLeveler.App.Services;
using FloorLeveler.Core;

namespace FloorLeveler.App.Tests;

/// <summary>
/// テスト用の <see cref="ISessionGateway"/>。SteamVR なしで VM を検証する。
/// 実際の IVRChaperoneSetup と同様に working copy / commit 済み状態を持ち、
/// 二重適用や revert 漏れを検出できるようにしている。
/// </summary>
internal sealed class FakeSessionGateway : ISessionGateway
{
    private readonly Queue<RigidTransform?> _nextPoses = new();

    public FakeSessionGateway(RigidTransform standingZeroPose)
    {
        CommittedStanding = standingZeroPose;
        WorkingStanding = standingZeroPose;
    }

    /// <summary>Live (commit 済み) の S→R。</summary>
    public RigidTransform CommittedStanding { get; private set; }

    /// <summary>working copy の S→R。</summary>
    public RigidTransform WorkingStanding { get; private set; }

    public List<GatewayDevice> DeviceList { get; } =
    [
        new GatewayDevice(0, "Hmd", "Index HMD", "LHR-HMD"),
        new GatewayDevice(1, "TrackingReference", "Base Station", "LHB-1"),
        new GatewayDevice(3, "GenericTracker", "Vive Tracker 3.0", "LHR-1234"),
    ];

    public bool CommitResult { get; set; } = true;

    /// <summary>最初の N 回の Commit を失敗させる (候補ごとの復元失敗の再現用)。</summary>
    public int CommitFailCount { get; set; }

    /// <summary>true にすると S→R 取得時に例外を投げる (接続時エラーの再現用)。</summary>
    public bool ThrowOnGetStandingZeroPose { get; set; }

    /// <summary>true にすると Commit が例外を投げる (セッション断の再現用)。</summary>
    public bool ThrowOnCommit { get; set; }

    public int ApplyCount { get; private set; }

    public int CommitCount { get; private set; }

    public int RevertCount { get; private set; }

    public bool PreviewVisible { get; private set; }

    public CorrectionResult? LastCorrection { get; private set; }

    /// <summary>設定すると GetDevicePose が常にこのポーズを返す (自動サンプリングのポール用)。</summary>
    public RigidTransform? PoseForAnyDevice { get; set; }

    public void EnqueuePose(RigidTransform? pose) => _nextPoses.Enqueue(pose);

    public void EnqueuePosition(Vector3? position)
        => _nextPoses.Enqueue(position is { } p ? RigidTransform.CreateTranslation(p) : null);

    public IReadOnlyList<GatewayDevice> ListDevices() => DeviceList;

    public RigidTransform? GetDevicePose(uint deviceIndex)
    {
        if (PoseForAnyDevice is { } fixedPose)
        {
            return fixedPose;
        }

        return _nextPoses.Count > 0 ? _nextPoses.Dequeue() : null;
    }

    public RigidTransform GetStandingZeroPose()
        => ThrowOnGetStandingZeroPose
            ? throw new InvalidOperationException("chaperone unavailable")
            : WorkingStanding;

    /// <summary>設定すると ApplyCorrection がこの例外を投げる (適用失敗の再現用)。</summary>
    public Exception? ApplyException { get; set; }

    public AppliedCorrectionInfo ApplyCorrection(CorrectionResult correction)
    {
        if (ApplyException is { } ex)
        {
            throw ex;
        }

        ApplyCount++;
        LastCorrection = correction;
        var old = WorkingStanding;
        WorkingStanding = correction.ApplyTo(WorkingStanding);
        return new AppliedCorrectionInfo(old, WorkingStanding, 4);
    }

    public ChaperoneSnapshot CaptureSnapshot()
        => ChaperoneSnapshot.Create(WorkingStanding, RigidTransform.Identity, [], (2f, 2f));

    public void RestoreSnapshot(ChaperoneSnapshot snapshot)
        => WorkingStanding = snapshot.Standing();

    public bool Commit()
    {
        CommitCount++;
        if (ThrowOnCommit)
        {
            throw new InvalidOperationException("commit failed");
        }

        if (CommitFailCount > 0)
        {
            CommitFailCount--;
            return false;
        }

        if (!CommitResult)
        {
            return false;
        }

        CommittedStanding = WorkingStanding;
        return true;
    }

    public void Revert()
    {
        RevertCount++;
        WorkingStanding = CommittedStanding;
    }

    public void ShowPreview() => PreviewVisible = true;

    public void HidePreview() => PreviewVisible = false;

    public void Dispose()
    {
    }
}
