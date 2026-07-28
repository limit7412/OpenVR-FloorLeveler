using System.Globalization;
using FloorLeveler.App.Services;

namespace FloorLeveler.App.Tests;

public class RotatingLogWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "floorleveler-log-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Log_WritesTimestampedLine()
    {
        var writer = new RotatingLogWriter(_dir, clock: () => new DateTime(2026, 7, 23, 1, 2, 3));
        writer.Log("hello");

        var content = File.ReadAllText(Path.Combine(_dir, "floorleveler.log"));
        Assert.Contains("2026-07-23 01:02:03 hello", content);
    }

    [Fact]
    public void Log_TimestampDoesNotDependOnTheCurrentCulture()
    {
        // ログは環境をまたいで読まれる診断出力なので、実行環境のロケールで
        // 書式が変わらないこと。カスタム書式の ':' は時刻区切りのプレースホルダで、
        // 不変カルチャを指定しないとロケール依存の文字に置き換わる。
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.TimeSeparator = "@";
        culture.DateTimeFormat.DateSeparator = "#";

        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            var writer = new RotatingLogWriter(_dir, clock: () => new DateTime(2026, 7, 23, 1, 2, 3));
            writer.Log("hello");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        var content = File.ReadAllText(Path.Combine(_dir, "floorleveler.log"));
        Assert.Contains("2026-07-23 01:02:03 hello", content);
    }

    [Fact]
    public void Log_RotatesWhenExceedingMaxBytes()
    {
        var writer = new RotatingLogWriter(_dir, maxBytes: 64, maxFiles: 3);

        for (var i = 0; i < 20; i++)
        {
            writer.Log($"line {i} with some padding to exceed the tiny size budget");
        }

        // 現行ファイルとローテート済みファイルが存在し、世代数は上限内。
        Assert.True(File.Exists(Path.Combine(_dir, "floorleveler.log")));
        Assert.True(File.Exists(Path.Combine(_dir, "floorleveler.log.1")));
        Assert.False(File.Exists(Path.Combine(_dir, "floorleveler.log.3")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
