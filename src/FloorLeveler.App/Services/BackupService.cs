using System.Text.Json;
using System.Text.Json.Serialization;
using FloorLeveler.Core;

namespace FloorLeveler.App.Services;

/// <summary>バックアップの種別。</summary>
public enum BackupKind
{
    /// <summary>接続時の自動退避 (起動時の状態)。復元候補には含めない。</summary>
    Auto,

    /// <summary>補正を適用する直前の退避 (復旧に最も重要)。</summary>
    PreApply,

    /// <summary>ユーザーによる手動退避。</summary>
    Manual,
}

/// <summary>バックアップファイルのメタデータ。</summary>
/// <param name="Path">ファイルの絶対パス。</param>
/// <param name="Timestamp">ファイル名から復元したタイムスタンプ表示 (yyyyMMdd-HHmmss)。</param>
/// <param name="Kind">バックアップ種別。</param>
public sealed record BackupEntry(string Path, string Timestamp, BackupKind Kind);

/// <summary>
/// Chaperone スナップショットのファイル保存・読み込み (仕様 F-6)。
/// 保存先は %LOCALAPPDATA%\FloorLeveler\backups\{timestamp}-{kind}.json (NF-3)。
/// 種別を区別し、復元対象 (最新) からは接続時の自動退避を除外する。
/// </summary>
public sealed class BackupService
{
    private readonly string _directory;

    // 保存順を表す単調増加カウンタ。ファイル名で種別より前に置くことで、
    // 同一秒に種別違いを保存しても「保存順」で新しい方が最新になる
    // (種別の文字列順で並んでしまう問題の回避)。既存ファイルの最大連番から
    // 継続することで、再起動をまたいでも順序が保たれる。
    private long _sequence;

    public BackupService(string? directory = null)
    {
        _directory = directory ?? AppPaths.BackupsDirectory;
        _sequence = ReadMaxSequence();
    }

    /// <summary>
    /// スナップショットを保存し、そのパスを返す。同一秒に複数回保存された場合も
    /// 既存ファイルを上書きせず、保存順を表す連番付きで別ファイルとして残す。
    /// </summary>
    public string Save(ChaperoneSnapshot snapshot, DateTime timestamp, BackupKind kind = BackupKind.Manual)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Directory.CreateDirectory(_directory);
        var json = JsonSerializer.Serialize(snapshot, BackupJsonContext.Default.ChaperoneSnapshot);
        var kindTag = KindTag(kind);
        var stamp = timestamp.ToString("yyyyMMdd-HHmmss");

        while (true)
        {
            // プロセス内は単調増加カウンタで seq が一意 (再起動後も ReadMaxSequence で
            // 継続)。別プロセスとの競合で同 seq・別種別が生じ得るが、その場合の
            // 並び順は List() 側で書き込み時刻により tie-break するため、種別の
            // 文字列順に依存しない (単一ユーザーのデスクトップ用途では別プロセスの
            // 同秒保存自体がまず起きないため、ロックファイル等の原子的確保は行わない)。
            var seq = Interlocked.Increment(ref _sequence);
            var path = Path.Combine(_directory, $"{stamp}-{seq:D9}-{kindTag}.json");

            FileStream stream;
            try
            {
                // CreateNew: 既存ファイルがあれば例外 → 次の連番で衝突を回避する。
                // ここで捕捉するのは「名前衝突」のみ (書き込み失敗とは区別する)。
                stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            }
            catch (IOException) when (File.Exists(path))
            {
                continue;
            }

            // ファイル生成後の書き込み失敗 (ディスクフル等) は衝突ではないため
            // リトライせず、部分ファイルを削除して例外を伝播する。
            try
            {
                using (stream)
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(json);
                }

                return path;
            }
            catch
            {
                TryDelete(path);
                throw;
            }
        }
    }

    public ChaperoneSnapshot Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize(json, BackupJsonContext.Default.ChaperoneSnapshot)
            ?? throw new InvalidDataException($"スナップショットの読み込みに失敗しました: {path}");
    }

    /// <summary>
    /// 保存済みバックアップを新しい順に列挙する。
    /// ディレクトリのアクセス失敗時は空リストを返す (CanExecute 経路から呼ばれても
    /// 未処理例外にしないため)。
    /// </summary>
    public IReadOnlyList<BackupEntry> List()
    {
        try
        {
            if (!Directory.Exists(_directory))
            {
                return [];
            }

            // 本サービスが保存した新形式 ({stamp}-{seq}-{kind}) のみを対象とする。
            // 同じディレクトリを共有する M0 PoC の旧形式 (yyyyMMdd-HHmmss.json) や
            // 無関係なファイルを最新扱いしないため。
            // 並び順は種別を除いた seq で決める。別プロセス競合で同 seq が万一
            // 発生した場合の tie-break は実際の書き込み時刻で行う。
            return Directory.EnumerateFiles(_directory, "*.json")
                .Where(IsRecognizedFormat)
                .OrderByDescending(OrderKey, StringComparer.Ordinal)
                .ThenByDescending(SafeLastWriteUtc)
                .Select(ToEntry)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// 復元候補を新しい順に返す。接続時の自動退避 (<see cref="BackupKind.Auto"/>) は
    /// 除外する (悪い補正後の再接続で自動退避が最新になり、正常な適用前状態へ戻れなくなるのを防ぐ)。
    /// 呼び出し側は先頭から順に試し、破損した候補を飛ばして次の有効な候補へ進める。
    /// </summary>
    public IReadOnlyList<BackupEntry> RestorableCandidates()
        => List().Where(e => e.Kind != BackupKind.Auto).ToArray();

    /// <summary>復元対象となる最新のバックアップ。無ければ null。</summary>
    public BackupEntry? LatestRestorable()
        => RestorableCandidates().FirstOrDefault();

    /// <summary>既存ファイルの最大連番を読み取る。IO 失敗時や不在時は 0。</summary>
    private long ReadMaxSequence()
    {
        try
        {
            if (!Directory.Exists(_directory))
            {
                return 0;
            }

            long max = 0;
            foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
            {
                var parts = Path.GetFileNameWithoutExtension(file).Split('-');
                // 形式: {yyyyMMdd}-{HHmmss}-{seq}-{kind}
                if (parts.Length >= 3 && long.TryParse(parts[2], out var seq))
                {
                    max = Math.Max(max, seq);
                }
            }

            return max;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 後始末の失敗は握りつぶす。
        }
    }

    /// <summary>
    /// 本サービスが保存した新形式か: {yyyyMMdd}-{HHmmss}-{seq(9桁)}-{kind}。
    /// seq が数字・kind が既知の種別であることを確認する。
    /// </summary>
    private static bool IsRecognizedFormat(string path)
    {
        var parts = Path.GetFileNameWithoutExtension(path).Split('-');
        return parts.Length == 4
            && parts[2].Length > 0
            && parts[2].All(char.IsDigit)
            && parts[3] is "auto" or "preapply" or "manual";
    }

    /// <summary>
    /// 並び順キー: 保存順を表す連番 seq (固定幅ゼロ埋め)。壁時計 (timestamp) は
    /// DST のフォールバックや手動時刻修正で逆行し得るため順序の基準にしない。
    /// seq は保存横断で単調増加 (再起動後も ReadMaxSequence で継続) する。
    /// </summary>
    private static string OrderKey(string path)
    {
        var parts = Path.GetFileNameWithoutExtension(path).Split('-');
        return parts.Length >= 3 ? parts[2] : Path.GetFileName(path);
    }

    private static DateTime SafeLastWriteUtc(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    private static BackupEntry ToEntry(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var parts = name.Split('-');
        // 形式: {yyyyMMdd}-{HHmmss}-{seq}-{kind}
        var timestamp = parts.Length >= 2 ? $"{parts[0]}-{parts[1]}" : name;
        var kind = parts.Length >= 4 ? ParseKind(parts[3]) : BackupKind.Manual;
        return new BackupEntry(path, timestamp, kind);
    }

    private static string KindTag(BackupKind kind) => kind switch
    {
        BackupKind.Auto => "auto",
        BackupKind.PreApply => "preapply",
        _ => "manual",
    };

    private static BackupKind ParseKind(string tag) => tag switch
    {
        "auto" => BackupKind.Auto,
        "preapply" => BackupKind.PreApply,
        _ => BackupKind.Manual,
    };
}

// trim 安全な JSON シリアライズ (仕様 §8.2)。
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ChaperoneSnapshot))]
internal partial class BackupJsonContext : JsonSerializerContext;
