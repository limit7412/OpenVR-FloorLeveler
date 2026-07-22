using System.Runtime.InteropServices;

namespace FloorLeveler.OpenVr;

/// <summary>
/// openvr_api.dll のエクスポート関数 (C API、cdecl)。
/// DLL は実行ファイルと同じディレクトリ、または OS の標準検索パスに配置すること。
/// </summary>
internal static partial class NativeMethods
{
    private const string Dll = "openvr_api";

    [LibraryImport(Dll, EntryPoint = "VR_InitInternal2", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial uint VR_InitInternal2(ref int error, EVRApplicationType applicationType, string? startupInfo);

    [LibraryImport(Dll, EntryPoint = "VR_ShutdownInternal")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial void VR_ShutdownInternal();

    [LibraryImport(Dll, EntryPoint = "VR_IsHmdPresent")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool VR_IsHmdPresent();

    [LibraryImport(Dll, EntryPoint = "VR_IsRuntimeInstalled")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool VR_IsRuntimeInstalled();

    [LibraryImport(Dll, EntryPoint = "VR_GetGenericInterface", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial IntPtr VR_GetGenericInterface(string interfaceVersion, ref int error);

    [LibraryImport(Dll, EntryPoint = "VR_IsInterfaceVersionValid", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool VR_IsInterfaceVersionValid(string interfaceVersion);

    [LibraryImport(Dll, EntryPoint = "VR_GetVRInitErrorAsSymbol")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial IntPtr VR_GetVRInitErrorAsSymbol(int error);

    [LibraryImport(Dll, EntryPoint = "VR_GetVRInitErrorAsEnglishDescription")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    internal static partial IntPtr VR_GetVRInitErrorAsEnglishDescription(int error);

    internal static string InitErrorDescription(int error)
    {
        var symbol = Marshal.PtrToStringUTF8(VR_GetVRInitErrorAsSymbol(error)) ?? "?";
        var description = Marshal.PtrToStringUTF8(VR_GetVRInitErrorAsEnglishDescription(error)) ?? "?";
        return $"{symbol} ({description})";
    }
}

/// <summary>OpenVR API の呼び出しに失敗した際の例外。</summary>
public sealed class OpenVrException(string message) : Exception(message);
