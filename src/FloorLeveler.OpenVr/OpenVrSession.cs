using System.Runtime.InteropServices;

namespace FloorLeveler.OpenVr;

/// <summary>
/// OpenVR セッションの管理 (init / shutdown)。Utility アプリケーションとして初期化し、
/// <see cref="VrSystem"/> と <see cref="ChaperoneTuner"/> を提供する。
/// </summary>
public sealed class OpenVrSession : IDisposable
{
    private bool _disposed;

    public VrSystem System { get; }
    public ChaperoneTuner ChaperoneSetup { get; }

    private OpenVrSession(VrSystem system, ChaperoneTuner chaperoneSetup)
    {
        System = system;
        ChaperoneSetup = chaperoneSetup;
    }

    /// <summary>
    /// SteamVR に Utility アプリケーションとして接続する。
    /// SteamVR 未起動などで失敗した場合は <see cref="OpenVrException"/> を送出する。
    /// </summary>
    public static OpenVrSession Connect()
    {
        // openvr_api.dll の解決子を登録する (仕様 §8.2)。
        // 最初の P/Invoke より前に呼ぶ必要がある。
        OpenVrNativeLibrary.Register();

        var error = 0;
        NativeMethods.VR_InitInternal2(ref error, EVRApplicationType.Utility, null);
        if (error != 0)
        {
            throw new OpenVrException($"OpenVR の初期化に失敗しました: {NativeMethods.InitErrorDescription(error)}");
        }

        try
        {
            // FnTable レイアウトはインターフェイスバージョンに依存するため、
            // 想定バージョンをランタイムが提供していることを必ず確認する。
            EnsureInterfaceVersion(OpenVrConstants.SystemVersion);
            EnsureInterfaceVersion(OpenVrConstants.ChaperoneSetupVersion);

            var system = new VrSystem(GetFnTable(OpenVrConstants.SystemVersion));
            var chaperone = new ChaperoneTuner(GetFnTable(OpenVrConstants.ChaperoneSetupVersion));
            return new OpenVrSession(system, chaperone);
        }
        catch
        {
            NativeMethods.VR_ShutdownInternal();
            throw;
        }
    }

    public static bool IsRuntimeInstalled()
    {
        OpenVrNativeLibrary.Register();
        return NativeMethods.VR_IsRuntimeInstalled();
    }

    public static bool IsHmdPresent()
    {
        OpenVrNativeLibrary.Register();
        return NativeMethods.VR_IsHmdPresent();
    }

    private static void EnsureInterfaceVersion(string version)
    {
        if (!NativeMethods.VR_IsInterfaceVersionValid(version))
        {
            throw new OpenVrException(
                $"SteamVR ランタイムがインターフェイス {version} を提供していません。" +
                "SteamVR のバージョンと本ツールの対応状況を確認してください。");
        }
    }

    private static IntPtr GetFnTable(string version)
    {
        var error = 0;
        var ptr = NativeMethods.VR_GetGenericInterface("FnTable:" + version, ref error);
        if (ptr == IntPtr.Zero || error != 0)
        {
            throw new OpenVrException(
                $"FnTable:{version} の取得に失敗しました: {NativeMethods.InitErrorDescription(error)}");
        }

        return ptr;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        NativeMethods.VR_ShutdownInternal();
    }
}

/// <summary>IVRSystem のうち本ツールが使う機能のラッパー。</summary>
public sealed class VrSystem
{
    private readonly GetDeviceToAbsoluteTrackingPoseDelegate _getPoses;
    private readonly GetTrackedDeviceClassDelegate _getDeviceClass;
    private readonly IsTrackedDeviceConnectedDelegate _isConnected;
    private readonly GetStringTrackedDevicePropertyDelegate _getStringProperty;

    internal VrSystem(IntPtr fnTablePtr)
    {
        var table = Marshal.PtrToStructure<VRSystemFnTable>(fnTablePtr);
        _getPoses = Marshal.GetDelegateForFunctionPointer<GetDeviceToAbsoluteTrackingPoseDelegate>(table.GetDeviceToAbsoluteTrackingPose);
        _getDeviceClass = Marshal.GetDelegateForFunctionPointer<GetTrackedDeviceClassDelegate>(table.GetTrackedDeviceClass);
        _isConnected = Marshal.GetDelegateForFunctionPointer<IsTrackedDeviceConnectedDelegate>(table.IsTrackedDeviceConnected);
        _getStringProperty = Marshal.GetDelegateForFunctionPointer<GetStringTrackedDevicePropertyDelegate>(table.GetStringTrackedDeviceProperty);
    }

    /// <summary>全デバイススロットの現在ポーズを指定座標系で取得する。</summary>
    public TrackedDevicePose[] GetPoses(ETrackingUniverseOrigin origin)
    {
        var poses = new TrackedDevicePose[OpenVrConstants.MaxTrackedDeviceCount];
        _getPoses(origin, 0f, poses, (uint)poses.Length);
        return poses;
    }

    public ETrackedDeviceClass GetDeviceClass(uint deviceIndex) => _getDeviceClass(deviceIndex);

    public bool IsConnected(uint deviceIndex) => _isConnected(deviceIndex);

    public string GetStringProperty(uint deviceIndex, int property)
    {
        var error = 0;
        var required = _getStringProperty(deviceIndex, property, null, 0, ref error);
        if (required == 0)
        {
            return string.Empty;
        }

        var buffer = new byte[required];
        error = 0;
        _getStringProperty(deviceIndex, property, buffer, required, ref error);
        if (error != 0)
        {
            return string.Empty;
        }

        var end = Array.IndexOf(buffer, (byte)0);
        return System.Text.Encoding.UTF8.GetString(buffer, 0, end < 0 ? buffer.Length : end);
    }

    /// <summary>接続中のデバイス一覧 (仕様 F-1)。</summary>
    public IReadOnlyList<TrackedDeviceInfo> ListConnectedDevices()
    {
        var devices = new List<TrackedDeviceInfo>();
        for (var i = 0u; i < OpenVrConstants.MaxTrackedDeviceCount; i++)
        {
            if (!IsConnected(i))
            {
                continue;
            }

            devices.Add(new TrackedDeviceInfo(
                i,
                GetDeviceClass(i),
                GetStringProperty(i, TrackedDeviceProperty.ModelNumberString),
                GetStringProperty(i, TrackedDeviceProperty.SerialNumberString)));
        }

        return devices;
    }
}

/// <summary>接続中のトラッキングデバイスの情報。</summary>
public sealed record TrackedDeviceInfo(
    uint Index,
    ETrackedDeviceClass DeviceClass,
    string ModelNumber,
    string SerialNumber);
