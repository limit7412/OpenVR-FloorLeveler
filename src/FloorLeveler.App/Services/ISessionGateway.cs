using FloorLeveler.Core;

namespace FloorLeveler.App.Services;

/// <summary>
/// ViewModel が必要とする OpenVR セッションの操作を抽象化する境界。
/// これにより UI ロジックを SteamVR 実機なしで単体テストできる。
/// </summary>
public interface ISessionGateway : IDisposable
{
    /// <summary>接続中のデバイス一覧。</summary>
    IReadOnlyList<GatewayDevice> ListDevices();

    /// <summary>指定デバイスの standing 座標系での現在ポーズ。無効なら null。</summary>
    RigidTransform? GetDevicePose(uint deviceIndex);

    /// <summary>現在の standing → raw 変換 (S→R 行列)。</summary>
    RigidTransform GetStandingZeroPose();

    /// <summary>補正を working copy に適用する (commit はしない)。</summary>
    AppliedCorrectionInfo ApplyCorrection(CorrectionResult correction);

    /// <summary>現在の Chaperone 設定のスナップショットを取得する (仕様 F-6)。</summary>
    ChaperoneSnapshot CaptureSnapshot();

    /// <summary>スナップショットを working copy へ書き戻す (commit はしない)。</summary>
    void RestoreSnapshot(ChaperoneSnapshot snapshot);

    /// <summary>working copy を Live へ反映する。</summary>
    bool Commit();

    /// <summary>working copy の変更を破棄する。</summary>
    void Revert();

    void ShowPreview();

    void HidePreview();
}

/// <summary>ゲートウェイ経由で得たデバイス情報。</summary>
public sealed record GatewayDevice(uint Index, string Kind, string ModelNumber, string SerialNumber)
{
    /// <summary>UI 表示用のラベル。</summary>
    public string DisplayName => $"#{Index} {Kind} — {ModelNumber} ({SerialNumber})";
}

/// <summary>適用した補正の要約 (アンドゥ・表示用)。</summary>
public sealed record AppliedCorrectionInfo(
    RigidTransform OldStandingToRaw,
    RigidTransform NewStandingToRaw,
    int TransformedBoundsQuadCount);

/// <summary>セッション接続に失敗した理由。UI 文言の選択に使う (訳文は持たせない)。</summary>
public enum SessionFailure
{
    /// <summary>SteamVR ランタイムからのエラー。<see cref="Exception.Message"/> をそのまま見せる。</summary>
    Runtime,

    /// <summary>openvr_api.dll を解決できなかった (SteamVR 未インストールなど)。</summary>
    NativeLibraryMissing,
}

/// <summary>セッション接続の失敗を表す例外。</summary>
public sealed class SessionUnavailableException(
    SessionFailure reason, string message, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>失敗の種類。表示する文言は呼び出し側 (ViewModel) が言語に応じて決める。</summary>
    public SessionFailure Reason { get; } = reason;
}
