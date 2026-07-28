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

/// <summary>
/// OpenVR 呼び出しの失敗種別。表示する文言は言語に依存するため、この層では
/// 種別と (翻訳できない) 詳細だけを持ち、文章の組み立ては UI 側に委ねる。
/// </summary>
public enum OpenVrFailure
{
    /// <summary>VR_Init に失敗した。詳細は OpenVR のエラー記述 (英語)。</summary>
    InitializationFailed,

    /// <summary>ランタイムが必要なインターフェイスバージョンを提供していない。詳細はその名前。</summary>
    InterfaceVersionUnsupported,

    /// <summary>インターフェイス (FnTable) を取得できなかった。詳細は名前とエラー記述。</summary>
    InterfaceUnavailable,

    /// <summary>Chaperone の working copy を読み取れなかった。詳細は呼び出した API 名。</summary>
    ChaperoneReadFailed,
}

/// <summary>OpenVR API の呼び出しに失敗した際の例外。</summary>
/// <param name="reason">失敗の種別。UI はこれを見て言語に応じた文言を選ぶ。</param>
/// <param name="detail">
/// 翻訳できない詳細 (OpenVR のエラー記述や API 名)。どの言語でもそのまま見せる。
/// </param>
public sealed class OpenVrException(OpenVrFailure reason, string detail)
    : Exception($"{reason}: {detail}")
{
    /// <summary>失敗の種別。</summary>
    public OpenVrFailure Reason { get; } = reason;

    /// <summary>翻訳できない詳細。</summary>
    public string Detail { get; } = detail;
}
