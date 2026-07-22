using System.Runtime.InteropServices;

namespace FloorLeveler.OpenVr;

// OpenVR SDK 2.12 (openvr_capi.h) の必要最小限の型定義。
// 列挙値・構造体レイアウトは公式ヘッダーに一致させること。

public enum EVRApplicationType
{
    Other = 0,
    Scene = 1,
    Overlay = 2,
    Background = 3,
    /// <summary>HMD レンダリングを行わないユーティリティアプリ (本ツールはこれで初期化する)。</summary>
    Utility = 4,
}

public enum ETrackingUniverseOrigin
{
    Seated = 0,
    Standing = 1,
    RawAndUncalibrated = 2,
}

public enum ETrackedDeviceClass
{
    Invalid = 0,
    Hmd = 1,
    Controller = 2,
    GenericTracker = 3,
    TrackingReference = 4,
    DisplayRedirect = 5,
}

public enum ETrackingResult
{
    Uninitialized = 1,
    CalibratingInProgress = 100,
    CalibratingOutOfRange = 101,
    RunningOk = 200,
    RunningOutOfRange = 201,
    FallbackRotationOnly = 300,
}

public enum EChaperoneConfigFile
{
    Live = 1,
    Temp = 2,
}

public static class TrackedDeviceProperty
{
    public const int ModelNumberString = 1001;
    public const int SerialNumberString = 1002;
}

public static class OpenVrConstants
{
    public const uint MaxTrackedDeviceCount = 64;

    public const string SystemVersion = "IVRSystem_023";
    public const string ChaperoneSetupVersion = "IVRChaperoneSetup_006";
}

/// <summary>行優先 3x4 行列 (回転 3x3 + 第 4 列に並進)。openvr.h の HmdMatrix34_t と同一レイアウト。</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct HmdMatrix34
{
    public float M00, M01, M02, M03;
    public float M10, M11, M12, M13;
    public float M20, M21, M22, M23;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct HmdVector3
{
    public float X, Y, Z;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct HmdVector2
{
    public float X, Y;
}

/// <summary>Chaperone 境界の 1 枚の壁 (4 頂点)。openvr.h の HmdQuad_t と同一レイアウト。</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct HmdQuad
{
    public HmdVector3 Corner0;
    public HmdVector3 Corner1;
    public HmdVector3 Corner2;
    public HmdVector3 Corner3;
}

/// <summary>openvr.h の TrackedDevicePose_t と同一レイアウト。</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct TrackedDevicePose
{
    public HmdMatrix34 DeviceToAbsoluteTracking;
    public HmdVector3 Velocity;
    public HmdVector3 AngularVelocity;
    public ETrackingResult TrackingResult;
    [MarshalAs(UnmanagedType.U1)] public bool PoseIsValid;
    [MarshalAs(UnmanagedType.U1)] public bool DeviceIsConnected;
}
