using System.Text.Json;
using System.Text.Json.Serialization;
using FloorLeveler.Core;

namespace FloorLeveler.App.Services;

/// <summary>バックアップファイルのメタデータ。</summary>
/// <param name="Path">ファイルの絶対パス。</param>
/// <param name="Timestamp">ファイル名から復元したタイムスタンプ表示。</param>
public sealed record BackupEntry(string Path, string Timestamp);

/// <summary>
/// Chaperone スナップショットのファイル保存・読み込み (仕様 F-6)。
/// 保存先は %LOCALAPPDATA%\FloorLeveler\backups\{timestamp}.json (NF-3)。
/// </summary>
public sealed class BackupService(string? directory = null)
{
    private readonly string _directory = directory ?? AppPaths.BackupsDirectory;

    /// <summary>スナップショットを新規ファイルに保存し、そのパスを返す。</summary>
    public string Save(ChaperoneSnapshot snapshot, DateTime timestamp)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{timestamp:yyyyMMdd-HHmmss}.json");
        var json = JsonSerializer.Serialize(snapshot, BackupJsonContext.Default.ChaperoneSnapshot);
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>指定パスのスナップショットを読み込む。</summary>
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
            .OrderByDescending(p => p, StringComparer.Ordinal)
            .Select(p => new BackupEntry(p, Path.GetFileNameWithoutExtension(p)))
            .ToArray();
    }

    /// <summary>最新のバックアップを返す。存在しなければ null。</summary>
    public BackupEntry? Latest() => List().FirstOrDefault();
}

// trim 安全な JSON シリアライズ (仕様 §8.2)。
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ChaperoneSnapshot))]
internal partial class BackupJsonContext : JsonSerializerContext;
