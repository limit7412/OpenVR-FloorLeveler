using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FloorLeveler.App.Services;
using FloorLeveler.App.ViewModels;
using FloorLeveler.App.Views;

namespace FloorLeveler.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 設定の読み込みと終了時の保存 (仕様 F-7)。
            // バックアップ (F-6) とローテーションログ (NF-4) は本番でも生成する。
            var settings = AppSettings.Load();
            var viewModel = new MainViewModel(
                OpenVrGateway.Connect,
                settings,
                new BackupService(),
                new RotatingLogWriter());
            var window = new MainWindow
            {
                DataContext = viewModel,
                Width = settings.WindowWidth,
                Height = settings.WindowHeight,
            };

            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) =>
            {
                viewModel.SnapshotSettings(window.Width, window.Height).Save();
                viewModel.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
