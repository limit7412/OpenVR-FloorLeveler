using System.Runtime.InteropServices;

namespace FloorLeveler.OpenVr;

// FnTable (VR_GetGenericInterface("FnTable:...") で得られる関数ポインタ表) の
// レイアウト定義。メンバーの順序は openvr_capi.h (SDK 2.12) の
// VR_IVRSystem_FnTable / VR_IVRChaperoneSetup_FnTable と厳密に一致させること。
// 順序を誤ると別の関数が呼ばれ、実行時に致命的な誤動作となる。
// 呼び出し規約は Windows では __stdcall。

#pragma warning disable CS0649 // フィールドはネイティブ側から Marshal で書き込まれる

[StructLayout(LayoutKind.Sequential)]
internal struct VRSystemFnTable
{
    public IntPtr GetRecommendedRenderTargetSize;
    public IntPtr GetProjectionMatrix;
    public IntPtr GetProjectionRaw;
    public IntPtr ComputeDistortion;
    public IntPtr GetEyeToHeadTransform;
    public IntPtr GetTimeSinceLastVsync;
    public IntPtr GetD3D9AdapterIndex;
    public IntPtr GetDXGIOutputInfo;
    public IntPtr GetOutputDevice;
    public IntPtr IsDisplayOnDesktop;
    public IntPtr SetDisplayVisibility;
    public IntPtr GetDeviceToAbsoluteTrackingPose;
    public IntPtr GetSeatedZeroPoseToStandingAbsoluteTrackingPose;
    public IntPtr GetRawZeroPoseToStandingAbsoluteTrackingPose;
    public IntPtr GetSortedTrackedDeviceIndicesOfClass;
    public IntPtr GetTrackedDeviceActivityLevel;
    public IntPtr ApplyTransform;
    public IntPtr GetTrackedDeviceIndexForControllerRole;
    public IntPtr GetControllerRoleForTrackedDeviceIndex;
    public IntPtr GetTrackedDeviceClass;
    public IntPtr IsTrackedDeviceConnected;
    public IntPtr GetBoolTrackedDeviceProperty;
    public IntPtr GetFloatTrackedDeviceProperty;
    public IntPtr GetInt32TrackedDeviceProperty;
    public IntPtr GetUint64TrackedDeviceProperty;
    public IntPtr GetMatrix34TrackedDeviceProperty;
    public IntPtr GetArrayTrackedDeviceProperty;
    public IntPtr GetStringTrackedDeviceProperty;
    public IntPtr GetPropErrorNameFromEnum;
    public IntPtr PollNextEvent;
    public IntPtr PollNextEventWithPose;
    public IntPtr PollNextEventWithPoseAndOverlays;
    public IntPtr GetEventTypeNameFromEnum;
    public IntPtr GetHiddenAreaMesh;
    public IntPtr GetControllerState;
    public IntPtr GetControllerStateWithPose;
    public IntPtr TriggerHapticPulse;
    public IntPtr GetButtonIdNameFromEnum;
    public IntPtr GetControllerAxisTypeNameFromEnum;
    public IntPtr IsInputAvailable;
    public IntPtr IsSteamVRDrawingControllers;
    public IntPtr ShouldApplicationPause;
    public IntPtr ShouldApplicationReduceRenderingWork;
    public IntPtr PerformFirmwareUpdate;
    public IntPtr AcknowledgeQuit_Exiting;
    public IntPtr GetAppContainerFilePaths;
    public IntPtr GetRuntimeVersion;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VRChaperoneSetupFnTable
{
    public IntPtr CommitWorkingCopy;
    public IntPtr RevertWorkingCopy;
    public IntPtr GetWorkingPlayAreaSize;
    public IntPtr GetWorkingPlayAreaRect;
    public IntPtr GetWorkingCollisionBoundsInfo;
    public IntPtr GetLiveCollisionBoundsInfo;
    public IntPtr GetWorkingSeatedZeroPoseToRawTrackingPose;
    public IntPtr GetWorkingStandingZeroPoseToRawTrackingPose;
    public IntPtr SetWorkingPlayAreaSize;
    public IntPtr SetWorkingCollisionBoundsInfo;
    public IntPtr SetWorkingPerimeter;
    public IntPtr SetWorkingSeatedZeroPoseToRawTrackingPose;
    public IntPtr SetWorkingStandingZeroPoseToRawTrackingPose;
    public IntPtr ReloadFromDisk;
    public IntPtr GetLiveSeatedZeroPoseToRawTrackingPose;
    public IntPtr ExportLiveToBuffer;
    public IntPtr ImportFromBufferToWorking;
    public IntPtr ShowWorkingSetPreview;
    public IntPtr HideWorkingSetPreview;
    public IntPtr RoomSetupStarting;
}

#pragma warning restore CS0649

// 使用する関数のみデリゲート型を定義する (stdcall)。

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void GetDeviceToAbsoluteTrackingPoseDelegate(
    ETrackingUniverseOrigin origin,
    float predictedSecondsToPhotonsFromNow,
    [In, Out] TrackedDevicePose[] poseArray,
    uint poseArrayCount);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate ETrackedDeviceClass GetTrackedDeviceClassDelegate(uint deviceIndex);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
[return: MarshalAs(UnmanagedType.U1)]
internal delegate bool IsTrackedDeviceConnectedDelegate(uint deviceIndex);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate uint GetStringTrackedDevicePropertyDelegate(
    uint deviceIndex,
    int property,
    [Out] byte[]? value,
    uint bufferSize,
    ref int error);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
[return: MarshalAs(UnmanagedType.U1)]
internal delegate bool CommitWorkingCopyDelegate(EChaperoneConfigFile configFile);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void NoArgsDelegate();

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
[return: MarshalAs(UnmanagedType.U1)]
internal delegate bool GetPlayAreaSizeDelegate(ref float sizeX, ref float sizeZ);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
[return: MarshalAs(UnmanagedType.U1)]
internal delegate bool GetMatrix34Delegate(ref HmdMatrix34 matrix);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void SetMatrix34Delegate(ref HmdMatrix34 matrix);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
[return: MarshalAs(UnmanagedType.U1)]
internal delegate bool GetCollisionBoundsCountDelegate(IntPtr quads, ref uint quadCount);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
[return: MarshalAs(UnmanagedType.U1)]
internal delegate bool GetCollisionBoundsDelegate([In, Out] HmdQuad[] quads, ref uint quadCount);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void SetCollisionBoundsDelegate([In] HmdQuad[] quads, uint quadCount);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void ReloadFromDiskDelegate(EChaperoneConfigFile configFile);
