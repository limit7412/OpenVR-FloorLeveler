namespace FloorLeveler.App.Services;

/// <summary>アプリのデータ保存先 (仕様 F-6, F-7, NF-3: %LOCALAPPDATA% 配下のみ)。</summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FloorLeveler");

    public static string BackupsDirectory => Path.Combine(Root, "backups");

    public static string LogsDirectory => Path.Combine(Root, "logs");

    public static string SettingsFile => Path.Combine(Root, "settings.json");
}
