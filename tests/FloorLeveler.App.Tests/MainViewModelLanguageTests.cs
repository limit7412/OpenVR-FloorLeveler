using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FloorLeveler.App.Localization;
using FloorLeveler.App.Services;
using FloorLeveler.App.ViewModels;
using FloorLeveler.Core;

namespace FloorLeveler.App.Tests;

/// <summary>言語切替 (仕様 §6 / M3) の ViewModel 側の挙動。</summary>
public class MainViewModelLanguageTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), $"fl-lang-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private MainViewModel Create(AppSettings? settings = null, FakeSessionGateway? gateway = null)
        => new(
            () => gateway ?? new FakeSessionGateway(RigidTransform.Identity),
            settings ?? new AppSettings(),
            new BackupService(_dir),
            log: null,
            clock: () => new DateTime(2026, 7, 23, 3, 0, 0, DateTimeKind.Local));

    [Fact]
    public void DefaultLanguage_IsJapanese()
    {
        // 仕様 §6:「言語は日本語 UI を基本とし」。
        var vm = Create();

        Assert.Equal(AppLanguage.Japanese, vm.Language);
        Assert.True(vm.IsJapanese);
        Assert.False(vm.IsEnglish);
        Assert.Equal(Localized.Ja.StatusNotConnected, vm.StatusMessage);
    }

    [Fact]
    public void Language_IsRestoredFromSettings()
    {
        var vm = Create(new AppSettings { Language = AppLanguage.English });

        Assert.Equal(AppLanguage.English, vm.Language);
        Assert.Equal(Localized.En.StatusNotConnected, vm.StatusMessage);
    }

    [Fact]
    public void SwitchingLanguage_RewritesTheCurrentStatusMessage()
    {
        // 文言を「文字列」ではなく「作る関数」で持っているので、表示中の
        // メッセージも切り替わる (再表示のための操作をユーザーに要求しない)。
        var vm = Create();
        Assert.Equal(Localized.Ja.StatusNotConnected, vm.StatusMessage);

        vm.Language = AppLanguage.English;

        Assert.Equal(Localized.En.StatusNotConnected, vm.StatusMessage);
    }

    [Fact]
    public void SwitchingLanguage_RewritesFormattedStatusMessage()
    {
        // 引数付きのメッセージも、切替後の言語の書式で作り直される。
        var gateway = new FakeSessionGateway(RigidTransform.Identity)
        {
            PoseForAnyDevice = RigidTransform.Identity,
        };
        var vm = Create(gateway: gateway);
        vm.ConnectCommand.Execute(null);
        vm.RecordPointCommand.Execute(null);
        Assert.Equal(Localized.Ja.StatusSampleRecorded(1), vm.StatusMessage);

        vm.Language = AppLanguage.English;

        Assert.Equal(Localized.En.StatusSampleRecorded(1), vm.StatusMessage);
    }

    [Fact]
    public void SwitchingLanguage_RewritesBackupLabels()
    {
        // 一覧のラベルは生成済みの文字列なので、作り直さないと旧言語のまま残る。
        var vm = Create();
        vm.ConnectCommand.Execute(null); // 接続で自動退避が 1 件

        Assert.NotEmpty(vm.Backups);
        Assert.Contains(Localized.Ja.BackupKindAuto, vm.Backups[0].DisplayName);

        vm.Language = AppLanguage.English;

        Assert.Contains(Localized.En.BackupKindAuto, vm.Backups[0].DisplayName);
    }

    [Fact]
    public void SwitchingLanguage_KeepsTheSelectedBackup()
    {
        // 一覧を作り直すため、選択が飛ぶと「選んだ時点」を失う。
        var vm = Create();
        vm.ConnectCommand.Execute(null);
        vm.SelectedBackup = vm.Backups[0];
        var selected = vm.SelectedBackup!.Entry.Path;

        vm.Language = AppLanguage.English;

        Assert.Equal(selected, vm.SelectedBackup?.Entry.Path);
    }

    [Fact]
    public void SwitchingLanguage_RaisesPropertyChangedForBoundText()
    {
        // XAML は L 経由で束縛するため、L の変更通知が出ないと画面が旧言語のまま残る。
        var vm = Create();
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

        vm.Language = AppLanguage.English;

        Assert.Contains(nameof(MainViewModel.L), changed);
        Assert.Contains(nameof(MainViewModel.StatusMessage), changed);
        Assert.Contains(nameof(MainViewModel.CorrectionSummary), changed);
        Assert.Contains(nameof(MainViewModel.TiltText), changed);
        Assert.Contains(nameof(MainViewModel.ResidualText), changed);
        Assert.Contains(nameof(MainViewModel.PointCountText), changed);
        Assert.Contains(nameof(MainViewModel.SpreadLabelText), changed);
    }

    [Fact]
    public void SwitchingLanguage_ChangesTheStringsInstance()
    {
        var vm = Create();
        Assert.Same(Strings.For(AppLanguage.Japanese), vm.L);

        vm.Language = AppLanguage.English;

        Assert.Same(Strings.For(AppLanguage.English), vm.L);
    }

    [Fact]
    public void IsEnglish_SwitchesLanguage()
    {
        // ラジオボタン用の個別バインディング。false 代入では切り替えない
        // (もう一方が true になったときだけ切り替わる)。
        var vm = Create();

        vm.IsEnglish = true;
        Assert.Equal(AppLanguage.English, vm.Language);

        vm.IsEnglish = false;
        Assert.Equal(AppLanguage.English, vm.Language);

        vm.IsJapanese = true;
        Assert.Equal(AppLanguage.Japanese, vm.Language);
    }

    [Fact]
    public void SnapshotSettings_PersistsTheLanguage()
    {
        // 次回起動でも同じ言語で開くこと (仕様 F-7)。
        var vm = Create();
        vm.Language = AppLanguage.English;

        Assert.Equal(AppLanguage.English, vm.SnapshotSettings(800, 600).Language);
    }

    [Fact]
    public void ConnectFailure_WithMissingNativeLibrary_ExplainsHowToFixIt()
    {
        // dll が無いケースは OpenVR のメッセージより具体的な案内を出す。
        var vm = new MainViewModel(
            () => throw new SessionUnavailableException(
                SessionFailure.NativeLibraryMissing, "Unable to load openvr_api"),
            new AppSettings(),
            new BackupService(_dir));

        vm.ConnectCommand.Execute(null);

        Assert.Equal(Localized.Ja.StatusNativeLibraryMissing, vm.StatusMessage);
    }

    [Fact]
    public void ConnectFailure_FromRuntime_ShowsTheUnderlyingMessage()
    {
        // SteamVR 由来のエラーは原文をそのまま見せる (こちらで訳せないため)。
        var vm = new MainViewModel(
            () => throw new SessionUnavailableException(SessionFailure.Runtime, "hmd not found"),
            new AppSettings(),
            new BackupService(_dir));

        vm.ConnectCommand.Execute(null);

        Assert.Equal(Localized.Ja.StatusConnectFailed("hmd not found"), vm.StatusMessage);
    }

    [Fact]
    public void RestoreFailureMessage_IsFullyRewrittenOnLanguageChange()
    {
        // 表示名 (種別名を含む) も作り直すこと。生成済みの文字列を掴んでいると、
        // 外側の文だけ英語になり「適用前」などが旧言語で残る。
        var gateway = new FakeSessionGateway(RigidTransform.Identity) { CommitResult = false };
        var vm = Create(gateway: gateway);
        vm.ConnectCommand.Execute(null); // 接続で自動退避が 1 件
        vm.SelectedBackup = vm.Backups[0];

        vm.RestoreSelectedCommand.Execute(null);
        Assert.Contains(Localized.Ja.BackupKindAuto, vm.StatusMessage);

        vm.Language = AppLanguage.English;

        Assert.Contains(Localized.En.BackupKindAuto, vm.StatusMessage);
        Assert.DoesNotContain(Localized.Ja.BackupKindAuto, vm.StatusMessage);
    }

    [Fact]
    public void PlotAxisLabels_FollowTheSelectedLanguage()
    {
        // 軸ラベルは投影時に確定する文字列なので、切替時に作り直さないと旧言語で残る。
        var vm = Create();
        Assert.Equal(Localized.Ja.PlotAxisTiltDirection, vm.SidePlot.HorizontalLabel);
        Assert.Equal(Localized.Ja.PlotAxisHeight, vm.SidePlot.VerticalLabel);
        Assert.Equal(Localized.Ja.PlotAxisX, vm.TopPlot.HorizontalLabel);

        vm.Language = AppLanguage.English;

        Assert.Equal(Localized.En.PlotAxisTiltDirection, vm.SidePlot.HorizontalLabel);
        Assert.Equal(Localized.En.PlotAxisHeight, vm.SidePlot.VerticalLabel);
        Assert.Equal(Localized.En.PlotAxisX, vm.TopPlot.HorizontalLabel);
    }

    [Theory]
    [InlineData(SessionFailure.InitializationFailed)]
    [InlineData(SessionFailure.InterfaceVersionUnsupported)]
    [InlineData(SessionFailure.InterfaceUnavailable)]
    public void ConnectFailure_FromOpenVr_IsDescribedInTheSelectedLanguage(SessionFailure reason)
    {
        // interop 層は種別と翻訳できない詳細だけを運び、文章は UI が組み立てる。
        // 以前は日本語の文が例外メッセージに埋まっており、英語 UI で混在していた。
        var vm = new MainViewModel(
            () => throw new SessionUnavailableException(reason, "DETAIL"),
            new AppSettings { Language = AppLanguage.English },
            new BackupService(_dir));

        vm.ConnectCommand.Execute(null);

        Assert.Contains("DETAIL", vm.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(reason.ToString(), vm.StatusMessage, StringComparison.Ordinal);

        var english = vm.StatusMessage;
        vm.Language = AppLanguage.Japanese;
        Assert.NotEqual(english, vm.StatusMessage);
        Assert.Contains("DETAIL", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyFailure_FromChaperoneRead_IsDescribedInTheSelectedLanguage()
    {
        // Chaperone の読み取り失敗も同様に、選択中の言語で組み立てる。
        // 傾いた S→R にして、モード A で適用できる補正がある状態にする。
        var gateway = new FakeSessionGateway(new RigidTransform(
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 2f * MathF.PI / 180f), Vector3.Zero));
        var vm = Create(gateway: gateway);
        vm.ConnectCommand.Execute(null);
        vm.UseMeasuredFloorMode = false;
        Assert.True(vm.CanApply);

        gateway.ApplyException = new OpenVr.OpenVrException(
            OpenVr.OpenVrFailure.ChaperoneReadFailed, "GetWorkingCollisionBoundsInfo");
        vm.ApplyCommand.Execute(null);

        Assert.Equal(
            Localized.Ja.StatusApplyFailed(
                Localized.Ja.ErrorChaperoneReadFailed("GetWorkingCollisionBoundsInfo")),
            vm.StatusMessage);

        vm.Language = AppLanguage.English;

        Assert.Equal(
            Localized.En.StatusApplyFailed(
                Localized.En.ErrorChaperoneReadFailed("GetWorkingCollisionBoundsInfo")),
            vm.StatusMessage);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(999)]
    [InlineData(-1)]
    public void UndefinedLanguageValue_FallsBackToJapanese(int raw)
    {
        // 設定ファイルが壊れて未定義の列挙値が入っても既定値で起動する (仕様 F-7)。
        // 放置すると、どちらの言語も選択されていない状態になり、終了時に不正値が
        // 再保存されてしまう。
        var settings = new AppSettings { Language = (AppLanguage)raw };

        Assert.Equal(AppLanguage.Japanese, settings.Language);

        var vm = Create(settings);
        Assert.True(vm.IsJapanese);
        Assert.False(vm.IsEnglish);
        Assert.Equal(AppLanguage.Japanese, vm.SnapshotSettings(800, 600).Language);
    }

    [Fact]
    public void UndefinedLanguageValue_FromJson_FallsBackToJapanese()
    {
        // JsonStringEnumConverter は既定で整数値も受け付けるため、例外にはならず
        // 未定義値として入ってくる経路も塞いでおく。
        var settings = JsonSerializer.Deserialize<AppSettings>(
            """{"Language": 999}""",
            new JsonSerializerOptions { Converters = { new JsonStringEnumConverter<AppLanguage>() } });

        Assert.NotNull(settings);
        Assert.Equal(AppLanguage.Japanese, settings.Language);
    }

    [Fact]
    public void TiltAndResidual_AreFormattedInTheSelectedLanguage()
    {
        var gateway = new FakeSessionGateway(RigidTransform.Identity);
        var vm = Create(gateway: gateway);
        vm.ConnectCommand.Execute(null);

        // 傾いた床を 3 点で作る (方位の語が言語で変わることを見たいだけなので粗くてよい)。
        foreach (var p in new[]
                 {
                     new Vector3(0f, 0f, 0f), new Vector3(1f, 0.05f, 0f), new Vector3(0f, 0f, 1f),
                 })
        {
            gateway.PoseForAnyDevice = new RigidTransform(Quaternion.Identity, p);
            vm.RecordPointCommand.Execute(null);
        }

        var japanese = vm.TiltText;
        vm.Language = AppLanguage.English;

        Assert.NotEqual("-", japanese);
        Assert.NotEqual(japanese, vm.TiltText);
    }
}
