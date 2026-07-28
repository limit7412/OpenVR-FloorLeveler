using FloorLeveler.App.Services;
using FloorLeveler.OpenVr;

namespace FloorLeveler.App.Localization;

/// <summary>
/// 例外を表示用の文言にする。分類できる失敗は選択中の言語で組み立て、
/// 分類できないものは原文をそのまま見せる。
/// </summary>
/// <remarks>
/// OpenVR / interop 層は訳文を持たず「失敗の種別」と「翻訳できない詳細」
/// (OpenVR のエラー記述や API 名) だけを運ぶ。文章にするのはここ 1 箇所。
/// </remarks>
internal static class ErrorText
{
    public static string Describe(Exception exception, Strings strings) => exception switch
    {
        SessionUnavailableException e => Describe(e.Reason, e.Detail, strings),
        OpenVrException e => Describe(e.Reason, e.Detail, strings),

        // 想定外の例外は原文のまま。訳せない代わりに情報を落とさない。
        _ => exception.Message,
    };

    private static string Describe(SessionFailure reason, string detail, Strings strings) => reason switch
    {
        SessionFailure.NativeLibraryMissing => strings.StatusNativeLibraryMissing,
        SessionFailure.InitializationFailed => strings.ErrorInitializationFailed(detail),
        SessionFailure.InterfaceVersionUnsupported => strings.ErrorInterfaceVersionUnsupported(detail),
        SessionFailure.InterfaceUnavailable => strings.ErrorInterfaceUnavailable(detail),
        _ => detail,
    };

    private static string Describe(OpenVrFailure reason, string detail, Strings strings) => reason switch
    {
        OpenVrFailure.InitializationFailed => strings.ErrorInitializationFailed(detail),
        OpenVrFailure.InterfaceVersionUnsupported => strings.ErrorInterfaceVersionUnsupported(detail),
        OpenVrFailure.InterfaceUnavailable => strings.ErrorInterfaceUnavailable(detail),
        OpenVrFailure.ChaperoneReadFailed => strings.ErrorChaperoneReadFailed(detail),
        _ => detail,
    };
}
