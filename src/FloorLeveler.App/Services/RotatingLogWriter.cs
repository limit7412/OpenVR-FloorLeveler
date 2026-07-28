using System.Globalization;

namespace FloorLeveler.App.Services;

/// <summary>
/// ローテーション付きテキストログ (仕様 NF-4)。
/// 現行ファイルが上限サイズを超えたら連番付きにローテートし、古いものを削除する。
/// 適用操作は変更前後の行列値を含めて記録する (呼び出し側の責務)。
/// スレッドセーフ。ファイル IO の失敗はログ機能の性質上握りつぶす。
/// </summary>
public sealed class RotatingLogWriter
{
    private readonly string _directory;
    private readonly string _baseName;
    private readonly long _maxBytes;
    private readonly int _maxFiles;
    private readonly Func<DateTime> _clock;
    private readonly object _lock = new();

    public RotatingLogWriter(
        string? directory = null,
        string baseName = "floorleveler.log",
        long maxBytes = 1024 * 1024,
        int maxFiles = 5,
        Func<DateTime>? clock = null)
    {
        _directory = directory ?? AppPaths.LogsDirectory;
        _baseName = baseName;
        _maxBytes = maxBytes;
        _maxFiles = Math.Max(1, maxFiles);
        _clock = clock ?? (() => DateTime.Now);
    }

    private string CurrentPath => Path.Combine(_directory, _baseName);

    public void Log(string message)
    {
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                RotateIfNeeded();
                // 不変カルチャで整形する。カスタム書式の ':' は時刻区切りの
                // プレースホルダで、ロケールによっては別の文字に置き換わるため。
                var line = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{_clock():yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
                File.AppendAllText(CurrentPath, line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // ログ出力の失敗はアプリの動作を妨げないため無視する。
            }
        }
    }

    private void RotateIfNeeded()
    {
        var current = new FileInfo(CurrentPath);
        if (!current.Exists || current.Length < _maxBytes)
        {
            return;
        }

        // 末尾の世代を削除し、古いものから番号を繰り上げる。
        var oldest = RotatedPath(_maxFiles - 1);
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var i = _maxFiles - 2; i >= 1; i--)
        {
            var from = RotatedPath(i);
            if (File.Exists(from))
            {
                File.Move(from, RotatedPath(i + 1), overwrite: true);
            }
        }

        File.Move(CurrentPath, RotatedPath(1), overwrite: true);
    }

    private string RotatedPath(int index) => Path.Combine(_directory, $"{_baseName}.{index}");
}
