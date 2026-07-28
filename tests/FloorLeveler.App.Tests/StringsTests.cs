using System.Reflection;
using FloorLeveler.App.Localization;

namespace FloorLeveler.App.Tests;

public class StringsTests
{
    /// <summary>
    /// <see cref="Strings"/> がリソースから引く全キー。メンバー名 = キー名という
    /// 規約なので、公開メンバーの名前がそのままキーの一覧になる。
    /// </summary>
    private static IReadOnlyList<string> MemberKeys() =>
    [
        .. typeof(Strings)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Where(IsStringAccessor)
            .Select(m => m.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal),
    ];

    private static bool IsStringAccessor(MemberInfo member) => member switch
    {
        PropertyInfo p => p.PropertyType == typeof(string),
        // プロパティのアクセサ (get_Xxx) や ToString などは除く。
        MethodInfo m => m.ReturnType == typeof(string) && !m.IsSpecialName && m.DeclaringType == typeof(Strings),
        _ => false,
    };

    public static TheoryData<AppLanguage> Languages => [AppLanguage.Japanese, AppLanguage.English];

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryMemberHasAResourceEntry(AppLanguage language)
    {
        // キーが無いとメンバー名がそのまま画面に出てしまう。追加漏れをここで落とす。
        var keys = Strings.KeysOf(language);
        var missing = MemberKeys().Where(k => !keys.Contains(k, StringComparer.Ordinal)).ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void BothLanguagesHaveTheSameKeys()
    {
        // 片方だけに増えたキーは「訳し忘れ」か「消し忘れ」。どちらも実害があるので落とす。
        Assert.Equal(Strings.KeysOf(AppLanguage.Japanese), Strings.KeysOf(AppLanguage.English));
    }

    [Fact]
    public void NoResourceKeyIsUnused()
    {
        // 使われないキーは訳文だけが残って腐る。
        var members = MemberKeys();
        var unused = Strings.KeysOf(AppLanguage.Japanese)
            .Where(k => !members.Contains(k, StringComparer.Ordinal))
            .ToArray();

        Assert.Empty(unused);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryPlainStringIsNonEmptyAndNotJustTheKey(AppLanguage language)
    {
        var strings = Strings.For(language);

        foreach (var property in typeof(Strings).GetProperties()
                     .Where(p => p.PropertyType == typeof(string)))
        {
            var value = (string?)property.GetValue(strings);

            Assert.False(string.IsNullOrWhiteSpace(value), $"{language}/{property.Name} が空です");
            // 欠落時はキー名がそのまま返る仕様なので、それを検出する。
            Assert.NotEqual(property.Name, value);
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void FormattedMessagesEmbedTheirArguments(AppLanguage language)
    {
        var s = Strings.For(language);

        Assert.Contains("42", s.StatusSampleRecorded(42), StringComparison.Ordinal);
        Assert.Contains("42", s.StatusAutoRecorded(42), StringComparison.Ordinal);
        Assert.Contains("42", s.PointCountLabel(42), StringComparison.Ordinal);
        Assert.Contains("boom", s.StatusConnectFailed("boom"), StringComparison.Ordinal);
        Assert.Contains("boom", s.StatusApplyFailed("boom"), StringComparison.Ordinal);
        Assert.Contains("a.json", s.StatusBackupSaved("a.json"), StringComparison.Ordinal);
        Assert.Contains("12:00", s.StatusRestored("12:00"), StringComparison.Ordinal);

        var withSkips = s.StatusRestoredWithSkips("12:00", 3);
        Assert.Contains("12:00", withSkips, StringComparison.Ordinal);
        Assert.Contains("3", withSkips, StringComparison.Ordinal);

        var backup = s.BackupDisplayName("2026-07-27 03:53:19", s.BackupKindPreApply, 12);
        Assert.Contains("2026-07-27 03:53:19", backup, StringComparison.Ordinal);
        Assert.Contains(s.BackupKindPreApply, backup, StringComparison.Ordinal);
        Assert.Contains("12", backup, StringComparison.Ordinal);
    }

    [Fact]
    public void JapaneseAndEnglishDiffer()
    {
        // 英語リソースが日本語のコピーのままだと「英語化した」ことにならない。
        // 言語名 (日本語 / English) は各言語での自称表記なので一致してよい。
        var ja = Strings.For(AppLanguage.Japanese);
        var en = Strings.For(AppLanguage.English);

        Assert.NotEqual(ja.ApplyButton, en.ApplyButton);
        Assert.NotEqual(ja.StatusNotConnected, en.StatusNotConnected);
        Assert.NotEqual(ja.BackupKindPreApply, en.BackupKindPreApply);
        Assert.Equal(ja.LanguageEnglish, en.LanguageEnglish);
    }

    [Fact]
    public void For_ReturnsTheSameInstancePerLanguage()
    {
        // 束縛のたびに新しいインスタンスを返すと、変更通知が無くても
        // 参照が変わってしまい無駄な再評価が起きる。
        Assert.Same(Strings.For(AppLanguage.Japanese), Strings.For(AppLanguage.Japanese));
        Assert.NotSame(Strings.For(AppLanguage.Japanese), Strings.For(AppLanguage.English));
    }
}
