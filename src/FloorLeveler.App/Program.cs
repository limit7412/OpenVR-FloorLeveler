using Avalonia;

namespace FloorLeveler.App;

internal static class Program
{
    // 初期化コードは AppMain より前 (Avalonia の初期化完了前) に何も参照しないこと。
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--version"))
        {
            // 単一 exe 発行のスモークテスト用 (仕様 §8.4)。
            Console.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0");
            return 0;
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
