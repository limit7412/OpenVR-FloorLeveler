using FloorLeveler.App.Localization;
using FloorLeveler.App.Services;

namespace FloorLeveler.App.ViewModels;

/// <summary>
/// バックアップ一覧の 1 行 (仕様 F-6)。表示名だけを言語に依存させ、
/// <see cref="BackupEntry"/> 自体は訳文を持たない。言語を切り替えたときは
/// 一覧を作り直す。
/// </summary>
/// <param name="Entry">元のバックアップ。</param>
/// <param name="DisplayName">一覧に出すラベル (例: "2026-07-27 03:53:19  適用前  #12")。</param>
public sealed record BackupListItem(BackupEntry Entry, string DisplayName)
{
    /// <summary>指定言語のラベルを付けて 1 行を作る。</summary>
    public static BackupListItem Create(BackupEntry entry, Strings strings)
        => new(entry, strings.BackupDisplayName(
            entry.FormattedTimestamp, KindLabel(entry.Kind, strings), entry.Sequence));

    /// <summary>種別の表示名。</summary>
    public static string KindLabel(BackupKind kind, Strings strings) => kind switch
    {
        BackupKind.Auto => strings.BackupKindAuto,
        BackupKind.PreApply => strings.BackupKindPreApply,
        _ => strings.BackupKindManual,
    };
}
