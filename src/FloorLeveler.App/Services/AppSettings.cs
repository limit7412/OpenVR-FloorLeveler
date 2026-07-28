using System.Text.Json;
using System.Text.Json.Serialization;
using FloorLeveler.App.Localization;

namespace FloorLeveler.App.Services;

/// <summary>永続化する設定 (仕様 F-7)。破損・不在時は既定値で起動する。</summary>
public sealed record AppSettings
{
    public int RecordDelaySeconds { get; init; } = 3;

    public int ContinuousIntervalMs { get; init; } = 100;

    public bool UseRansac { get; init; }

    public float RansacThresholdMeters { get; init; } = 0.003f;

    public double WindowWidth { get; init; } = 720;

    public double WindowHeight { get; init; } = 900;

    private readonly AppLanguage _language = AppLanguage.Japanese;

    /// <summary>
    /// UI の表示言語 (仕様 §6)。既定は日本語。
    /// 未定義の値は既定へ戻す。<c>JsonStringEnumConverter</c> は既定で整数値も受け付けるため、
    /// 設定ファイルが <c>"Language": 999</c> のように壊れていても例外にならず、そのまま
    /// 未定義の列挙値として入ってくる。その状態では文字列こそ日本語にフォールバックする
    /// ものの、言語の選択状態がどちらでもなくなり、終了時に不正値が再保存される
    /// (仕様 F-7 の「破損時は既定値で起動する」を満たさない)。
    /// </summary>
    public AppLanguage Language
    {
        get => _language;
        init => _language = Enum.IsDefined(value) ? value : AppLanguage.Japanese;
    }

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(AppPaths.SettingsFile);
            return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 破損・読み取り不可なら既定値で起動する (仕様 F-7)。
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            var json = JsonSerializer.Serialize(this, SettingsJsonContext.Default.AppSettings);
            File.WriteAllText(AppPaths.SettingsFile, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 保存失敗は致命的ではないため無視する。
        }
    }
}

// trim 安全な JSON シリアライズ (仕様 §8.2)。
[JsonSourceGenerationOptions(WriteIndented = true, Converters = [typeof(JsonStringEnumConverter<AppLanguage>)])]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext;
