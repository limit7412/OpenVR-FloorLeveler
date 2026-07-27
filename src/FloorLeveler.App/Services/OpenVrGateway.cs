using System.Numerics;
using FloorLeveler.Core;
using FloorLeveler.OpenVr;

namespace FloorLeveler.App.Services;

/// <summary><see cref="OpenVrSession"/> を用いた <see cref="ISessionGateway"/> の実装。</summary>
public sealed class OpenVrGateway : ISessionGateway
{
    private readonly OpenVrSession _session;

    private OpenVrGateway(OpenVrSession session) => _session = session;

    /// <summary>SteamVR に接続する。失敗時は <see cref="SessionUnavailableException"/>。</summary>
    public static OpenVrGateway Connect()
    {
        try
        {
            return new OpenVrGateway(OpenVrSession.Connect());
        }
        catch (OpenVrException ex)
        {
            throw new SessionUnavailableException(ex.Message, ex);
        }
        catch (DllNotFoundException ex)
        {
            throw new SessionUnavailableException(
                "openvr_api.dll が見つかりません。SteamVR をインストールするか、" +
                "FloorLeveler.exe と同じディレクトリに openvr_api.dll を置いてください。", ex);
        }
    }

    public IReadOnlyList<GatewayDevice> ListDevices()
        => _session.System.ListConnectedDevices()
            .Select(d => new GatewayDevice(d.Index, d.DeviceClass.ToString(), d.ModelNumber, d.SerialNumber))
            .ToArray();

    public RigidTransform? GetDevicePose(uint deviceIndex)
    {
        var poses = _session.System.GetPoses(ETrackingUniverseOrigin.Standing);
        if (deviceIndex >= poses.Length)
        {
            return null;
        }

        var pose = poses[deviceIndex];
        if (!pose.PoseIsValid || pose.TrackingResult != ETrackingResult.RunningOk)
        {
            return null;
        }

        return pose.DeviceToAbsoluteTracking.ToRigidTransform();
    }

    public RigidTransform GetStandingZeroPose()
        => _session.ChaperoneSetup.GetWorkingStandingZeroPose();

    public AppliedCorrectionInfo ApplyCorrection(CorrectionResult correction)
    {
        var applied = _session.ChaperoneSetup.ApplyCorrection(correction);
        return new AppliedCorrectionInfo(
            applied.OldStandingToRaw,
            applied.NewStandingToRaw,
            applied.TransformedBoundsQuadCount);
    }

    public ChaperoneSnapshot CaptureSnapshot()
        => ChaperoneSnapshotIo.Capture(_session.ChaperoneSetup);

    public void RestoreSnapshot(ChaperoneSnapshot snapshot)
        => ChaperoneSnapshotIo.Restore(_session.ChaperoneSetup, snapshot);

    public bool Commit() => _session.ChaperoneSetup.Commit();

    public void Revert() => _session.ChaperoneSetup.Revert();

    public void ShowPreview() => _session.ChaperoneSetup.ShowWorkingSetPreview();

    public void HidePreview() => _session.ChaperoneSetup.HideWorkingSetPreview();

    public void Dispose() => _session.Dispose();
}
