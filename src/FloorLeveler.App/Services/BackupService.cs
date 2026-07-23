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
public sealed class BackupService(string? directory = null)
{
    private readonly string _directory = directory ?? AppPaths.BackupsDirectory;

    // 保存順を表す単調増加カウンタ。ファイル名で種別より前に置くことで、
    // 同一秒に種別違いを保存しても「保存順」で新しい方が最新になる
    // (種別の文字列順で並んでしまう問題の回避)。
    private long _sequence;

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
            var seq = Interlocked.Increment(ref _sequence);
            var name = $"{stamp}-{seq:D9}-{kindTag}.json";
            var path = Path.Combine(_directory, name);
            try
            {
                // CreateNew: 既存ファイルがあれば例外 → 次の連番で衝突を回避する。
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                using var writer = new StreamWriter(stream);
                writer.Write(json);
                return path;
            }
            catch (IOException) when (File.Exists(path))
            {
                // 同名が既にある場合 (別セッションの同秒同連番など) は次の連番へ。
            }
        }
    }

    public ChaperoneSnapshot Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize(json, BackupJsonContext.Default.ChaperoneSnapshot)
            ?? throw new InvalidDataException($"スナップショットの読み込みに失敗しました: {path}");
    }

    /// <summary>保存済みバックアップを新しい順に列挙する。</summary>
    public IReadOnlyList<BackupEntry> List()
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(_directory, "*.json")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Select(ToEntry)
            .ToArray();
    }

    /// <summary>
    /// 復元対象となる最新のバックアップ。接続時の自動退避 (<see cref="BackupKind.Auto"/>) は
    /// 除外する (悪い補正後の再接続で自動退避が最新になり、正常な適用前状態へ戻れなくなるのを防ぐ)。
    /// </summary>
    public BackupEntry? LatestRestorable()
        => List().FirstOrDefault(e => e.Kind != BackupKind.Auto);

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
