using System.Text.Json;
using FloorLeveler.App.Localization;
using FloorLeveler.App.Services;

namespace FloorLeveler.App.Tests;

/// <summary>設定 (仕様 F-7) の読み書き。</summary>
public class AppSettingsTests
{
    /// <summary>
    /// 0.1.0 までの版が書いた設定ファイルには、その後削除した RecordDelaySeconds /
    /// ContinuousIntervalMs が残っている。既定値で起動し直させないよう、未知のキーが
    /// あっても他の項目はそのまま読めること。
    /// </summary>
    [Fact]
    public void Load_KeepsTheOtherValuesWhenTheFileHasSettingsThatNoLongerExist()
    {
        const string json = """
            {
              "RecordDelaySeconds": 5,
              "ContinuousIntervalMs": 250,
              "UseRansac": true,
              "RansacThresholdMeters": 0.005,
              "WindowWidth": 800,
              "WindowHeight": 1000,
              "Language": "English"
            }
            """;

        var settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);

        Assert.NotNull(settings);
        Assert.True(settings.UseRansac);
        Assert.Equal(0.005f, settings.RansacThresholdMeters);
        Assert.Equal(800, settings.WindowWidth);
        Assert.Equal(1000, settings.WindowHeight);
        Assert.Equal(AppLanguage.English, settings.Language);
    }

    /// <summary>
    /// 保存した設定ファイルに、変更しても何も起きない項目が現れないこと。
    /// </summary>
    [Fact]
    public void Save_WritesOnlyTheSettingsThatAreActuallyUsed()
    {
        var json = JsonSerializer.Serialize(new AppSettings(), SettingsJsonContext.Default.AppSettings);

        var keys = JsonDocument.Parse(json).RootElement.EnumerateObject()
            .Select(p => p.Name)
            .ToArray();

        Assert.Equal(
            ["UseRansac", "RansacThresholdMeters", "WindowWidth", "WindowHeight", "Language"],
            keys);
    }
}
