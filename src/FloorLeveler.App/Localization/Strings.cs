using System.Globalization;
using System.Resources;

namespace FloorLeveler.App.Localization;

/// <summary>
/// UI 文字列 (仕様 §6: 日本語を基本とし、文字列リソースを分離して英語化に備える)。
/// 実体は <c>StringsJa.resx</c> / <c>StringsEn.resx</c> にあり、本クラスは
/// 「キー名 = メンバー名」の型付きアクセサを提供する。XAML からは
/// <c>{Binding L.ApplyButton}</c> のように束縛するため、コンパイル済みバインディングで
/// キーの綴り誤りがビルド時に落ちる。
/// </summary>
/// <remarks>
/// 言語ごとに別リソース (カルチャ接尾辞なし) にして明示的に選ぶ。
/// サテライトアセンブリと <see cref="CultureInfo.CurrentUICulture"/> に頼ると、
/// 単一 exe 発行や OS のロケールに挙動が左右されるため。
/// </remarks>
public sealed class Strings
{
    private static readonly ResourceManager JapaneseResources =
        new("FloorLeveler.App.Localization.StringsJa", typeof(Strings).Assembly);

    private static readonly ResourceManager EnglishResources =
        new("FloorLeveler.App.Localization.StringsEn", typeof(Strings).Assembly);

    private static readonly Strings JapaneseInstance = new(AppLanguage.Japanese);
    private static readonly Strings EnglishInstance = new(AppLanguage.English);

    private readonly ResourceManager _resources;

    private Strings(AppLanguage language)
    {
        Language = language;
        _resources = language == AppLanguage.English ? EnglishResources : JapaneseResources;
    }

    /// <summary>この インスタンスが表す言語。</summary>
    public AppLanguage Language { get; }

    /// <summary>言語に対応するインスタンスを返す (不変なので使い回す)。</summary>
    public static Strings For(AppLanguage language)
        => language == AppLanguage.English ? EnglishInstance : JapaneseInstance;

    // --- ウィンドウ / 共通 ---

    public string WindowTitle => Get(nameof(WindowTitle));

    public string LanguageLabel => Get(nameof(LanguageLabel));

    public string LanguageJapanese => Get(nameof(LanguageJapanese));

    public string LanguageEnglish => Get(nameof(LanguageEnglish));

    public string ConnectButton => Get(nameof(ConnectButton));

    // --- サンプリング ---

    public string SamplingHeader => Get(nameof(SamplingHeader));

    public string SamplingModeManual => Get(nameof(SamplingModeManual));

    public string SamplingModeStillness => Get(nameof(SamplingModeStillness));

    public string SamplingModeContinuous => Get(nameof(SamplingModeContinuous));

    public string RecordButton => Get(nameof(RecordButton));

    public string ClearButton => Get(nameof(ClearButton));

    public string SamplingHint => Get(nameof(SamplingHint));

    public string PointCountLabel(int count) => Format(nameof(PointCountLabel), count);

    public string SpreadLabel(string spread) => Format(nameof(SpreadLabel), spread);

    // --- 推定結果 ---

    public string EstimateHeader => Get(nameof(EstimateHeader));

    public string TiltLabel => Get(nameof(TiltLabel));

    public string ResidualLabel => Get(nameof(ResidualLabel));

    public string UseRansacLabel => Get(nameof(UseRansacLabel));

    public string TiltValue(float angleDegrees, float azimuthDegrees)
        => Format(nameof(TiltValue), angleDegrees, azimuthDegrees);

    public string ResidualValue(float rmsMillimeters, float maxMillimeters)
        => Format(nameof(ResidualValue), rmsMillimeters, maxMillimeters);

    // --- 可視化 ---

    public string PlotHeader => Get(nameof(PlotHeader));

    public string TopViewLabel => Get(nameof(TopViewLabel));

    public string SideViewLabel => Get(nameof(SideViewLabel));

    public string PlotHint => Get(nameof(PlotHint));

    // --- 補正 ---

    public string CorrectionHeader => Get(nameof(CorrectionHeader));

    public string ModeGravityAlign => Get(nameof(ModeGravityAlign));

    public string ModeMeasuredFloor => Get(nameof(ModeMeasuredFloor));

    public string LargeCorrectionAcknowledge => Get(nameof(LargeCorrectionAcknowledge));

    public string PreviewButton => Get(nameof(PreviewButton));

    public string CancelPreviewButton => Get(nameof(CancelPreviewButton));

    public string ApplyButton => Get(nameof(ApplyButton));

    public string UndoButton => Get(nameof(UndoButton));

    // --- バックアップ ---

    public string BackupHeader => Get(nameof(BackupHeader));

    public string BackupButton => Get(nameof(BackupButton));

    public string RestoreLatestButton => Get(nameof(RestoreLatestButton));

    public string RestoreAnyLabel => Get(nameof(RestoreAnyLabel));

    public string RestoreSelectedButton => Get(nameof(RestoreSelectedButton));

    public string RefreshBackupsButton => Get(nameof(RefreshBackupsButton));

    public string BackupHint => Get(nameof(BackupHint));

    public string BackupKindAuto => Get(nameof(BackupKindAuto));

    public string BackupKindPreApply => Get(nameof(BackupKindPreApply));

    public string BackupKindManual => Get(nameof(BackupKindManual));

    public string BackupDisplayName(string timestamp, string kind, long sequence)
        => Format(nameof(BackupDisplayName), timestamp, kind, sequence);

    // --- ステータスメッセージ ---

    public string StatusNotConnected => Get(nameof(StatusNotConnected));

    public string StatusConnected => Get(nameof(StatusConnected));

    public string StatusNativeLibraryMissing => Get(nameof(StatusNativeLibraryMissing));

    public string StatusManualMode => Get(nameof(StatusManualMode));

    public string StatusStillnessMode => Get(nameof(StatusStillnessMode));

    public string StatusContinuousMode => Get(nameof(StatusContinuousMode));

    public string StatusNoValidPose => Get(nameof(StatusNoValidPose));

    public string StatusPointsCleared => Get(nameof(StatusPointsCleared));

    public string StatusPreviewing => Get(nameof(StatusPreviewing));

    public string StatusPreviewDiscarded => Get(nameof(StatusPreviewDiscarded));

    public string StatusBackupBeforeApplyFailed => Get(nameof(StatusBackupBeforeApplyFailed));

    public string StatusApplyFailedReverted => Get(nameof(StatusApplyFailedReverted));

    public string StatusApplied => Get(nameof(StatusApplied));

    public string StatusUndone => Get(nameof(StatusUndone));

    public string StatusUndoFailed => Get(nameof(StatusUndoFailed));

    public string StatusNoRestorableBackup => Get(nameof(StatusNoRestorableBackup));

    public string StatusAllRestoresFailed => Get(nameof(StatusAllRestoresFailed));

    public string StatusCorrectionUnavailable => Get(nameof(StatusCorrectionUnavailable));

    public string StatusCorrectionNegligible => Get(nameof(StatusCorrectionNegligible));

    public string CorrectionNeedsConfirmationSuffix => Get(nameof(CorrectionNeedsConfirmationSuffix));

    public string StatusConnectFailed(string detail) => Format(nameof(StatusConnectFailed), detail);

    public string StatusSampleRecorded(int count) => Format(nameof(StatusSampleRecorded), count);

    public string StatusAutoRecorded(int count) => Format(nameof(StatusAutoRecorded), count);

    public string StatusPreviewFailed(string detail) => Format(nameof(StatusPreviewFailed), detail);

    public string StatusApplyFailed(string detail) => Format(nameof(StatusApplyFailed), detail);

    public string StatusUndoFailedWithReason(string detail)
        => Format(nameof(StatusUndoFailedWithReason), detail);

    public string StatusBackupSaved(string fileName) => Format(nameof(StatusBackupSaved), fileName);

    public string StatusBackupFailed(string detail) => Format(nameof(StatusBackupFailed), detail);

    public string StatusRestoreFailed(string displayName)
        => Format(nameof(StatusRestoreFailed), displayName);

    public string StatusRestored(string timestamp) => Format(nameof(StatusRestored), timestamp);

    public string StatusRestoredWithSkips(string timestamp, int skipped)
        => Format(nameof(StatusRestoredWithSkips), timestamp, skipped);

    public string StatusCorrectionComputeFailed(string detail)
        => Format(nameof(StatusCorrectionComputeFailed), detail);

    public string CorrectionSummaryValue(float rotationDegrees, float heightChangeMillimeters)
        => Format(nameof(CorrectionSummaryValue), rotationDegrees, heightChangeMillimeters);

    /// <summary>
    /// キーに対応する文字列を返す。欠落時はキー名をそのまま返す
    /// (画面が空欄になるより、どのキーが無いか分かる方が直せる)。
    /// </summary>
    internal string Get(string key) => _resources.GetString(key, CultureInfo.InvariantCulture) ?? key;

    /// <summary>数値・日時は UI の表示なので実行環境のカルチャで整形する。</summary>
    private string Format(string key, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>リソースに定義されている全キー (テストでの日英の突き合わせ用)。</summary>
    internal static IReadOnlyList<string> KeysOf(AppLanguage language)
    {
        var resources = language == AppLanguage.English ? EnglishResources : JapaneseResources;
        var set = resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true)
            ?? throw new MissingManifestResourceException($"{language} のリソースを読み込めませんでした。");

        return set.Cast<System.Collections.DictionaryEntry>()
            .Select(e => (string)e.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
    }
}
