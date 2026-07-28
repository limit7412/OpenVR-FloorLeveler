using FloorLeveler.App.Localization;

namespace FloorLeveler.App.Tests;

/// <summary>
/// テストからメッセージを参照するためのヘルパー。文言を直書きせずリソース経由で
/// 突き合わせることで、訳文を直しただけでテストが落ちないようにする。
/// </summary>
internal static class Localized
{
    /// <summary>既定言語 (日本語) の文字列。</summary>
    public static readonly Strings Ja = Strings.For(AppLanguage.Japanese);

    /// <summary>英語の文字列。</summary>
    public static readonly Strings En = Strings.For(AppLanguage.English);

    /// <summary>
    /// 書式付きメッセージに埋める番人。実行時にしか決まらない値 (ファイル名や
    /// 例外メッセージ) を含むメッセージでも、固定部分だけを突き合わせられる。
    /// </summary>
    public const string Arg = "￿";

    /// <summary>
    /// <see cref="Arg"/> を埋めたメッセージから可変部を除いた固定部分を返す。
    /// 例: <c>Fixed(s =&gt; s.StatusBackupSaved(Arg))</c> → "バックアップを保存しました: "
    /// </summary>
    public static string Fixed(Func<Strings, string> format, Strings? strings = null)
        => format(strings ?? Ja).Replace(Arg, string.Empty, StringComparison.Ordinal).Trim();
}
