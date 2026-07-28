using System.Globalization;
using System.Numerics;
using FloorLeveler.App.Localization;
using FloorLeveler.App.Services;
using FloorLeveler.App.ViewModels;
using FloorLeveler.Core;

namespace FloorLeveler.App.Tests;

/// <summary>
/// ログ (仕様 NF-4) の内容。ログは UI ではなく診断出力なので、UI の言語設定にも
/// 実行環境のロケールにも左右されず、同じ内容が出ること。
/// </summary>
public class MainViewModelLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"fl-log-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>接続 → 適用 → 元に戻す、まで通してログを書かせる。</summary>
    private string RunSession(AppLanguage language, CultureInfo? culture = null)
    {
        var logDirectory = Path.Combine(_dir, $"{language}-{culture?.Name ?? "current"}");
        var log = new RotatingLogWriter(
            logDirectory, clock: () => new DateTime(2026, 7, 23, 3, 0, 0, DateTimeKind.Local));

        // 傾いた S→R にして、適用できる補正がある状態にする。
        var gateway = new FakeSessionGateway(new RigidTransform(
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 2f * MathF.PI / 180f), Vector3.Zero));

        var previous = CultureInfo.CurrentCulture;
        try
        {
            if (culture is not null)
            {
                CultureInfo.CurrentCulture = culture;
            }

            using var vm = new MainViewModel(
                () => gateway,
                new AppSettings { Language = language },
                new BackupService(Path.Combine(logDirectory, "backups")),
                log,
                () => new DateTime(2026, 7, 23, 3, 0, 0, DateTimeKind.Local));

            vm.ConnectCommand.Execute(null);
            vm.BackupCommand.Execute(null);
            vm.ApplyCommand.Execute(null);
            vm.UndoCommand.Execute(null);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // 実行ごとに異なるのは一時ディレクトリのパスだけなので、比較のために伏せる
        // (バックアップの保存先がログに出るため)。
        return File.ReadAllText(Path.Combine(logDirectory, "floorleveler.log"))
            .Replace(logDirectory, "<dir>", StringComparison.Ordinal);
    }

    [Fact]
    public void Log_IsWrittenInEnglishRegardlessOfTheUiLanguage()
    {
        // 問題報告のログを受け取る側が UI の言語設定に左右されないよう、
        // ログは英語固定にしている。
        var japanese = RunSession(AppLanguage.Japanese);
        var english = RunSession(AppLanguage.English);

        Assert.Equal(japanese, english);
        Assert.Contains("Connected.", japanese, StringComparison.Ordinal);
        Assert.Contains("Applied correction", japanese, StringComparison.Ordinal);
        Assert.Contains("Undid the last correction.", japanese, StringComparison.Ordinal);
        Assert.Contains("Backup saved:", japanese, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_ContainsNoJapanese()
    {
        var content = RunSession(AppLanguage.Japanese);

        Assert.DoesNotContain(content, c => c is (>= '぀' and <= 'ヿ') or (>= '一' and <= '鿿'));
    }

    [Fact]
    public void Log_RecordsTheMatricesAroundApply()
    {
        // 仕様 NF-4: 適用操作は変更前後の行列値を必ず記録する。
        var content = RunSession(AppLanguage.Japanese);

        var applyLine = content.Split(Environment.NewLine)
            .Single(l => l.Contains("Applied correction", StringComparison.Ordinal));

        Assert.Contains("standing->raw [", applyLine, StringComparison.Ordinal);
        Assert.Contains("] -> [", applyLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_NumberFormatDoesNotDependOnTheCurrentCulture()
    {
        // 小数点が ',' になるロケールでもログの数値表記が変わらないこと
        // (受け取ったログを機械的に読めるようにするため)。
        var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";
        comma.NumberFormat.NumberGroupSeparator = ".";

        Assert.Equal(RunSession(AppLanguage.Japanese), RunSession(AppLanguage.Japanese, comma));
    }
}
