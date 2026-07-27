using FloorLeveler.App.Services;

namespace FloorLeveler.App.Tests;

public class AppVersionTests
{
    [Fact]
    public void Format_PrefersInformationalVersion()
    {
        // タグ由来の InformationalVersion をそのまま使う (アセンブリバージョンは 4 桁に
        // 丸められるため、表示にはこちらを優先する)。
        Assert.Equal("1.2.3", AppVersion.Format("1.2.3", new Version(1, 2, 3, 0)));
    }

    [Fact]
    public void Format_KeepsPrereleaseIdentifier()
    {
        // プレリリース識別子はアセンブリバージョンでは失われるため保持する。
        Assert.Equal("1.2.3-rc.1", AppVersion.Format("1.2.3-rc.1", new Version(1, 2, 3, 0)));
    }

    [Fact]
    public void Format_DropsBuildMetadata()
    {
        // SourceLink 等が付ける "+コミットハッシュ" は表示しない
        // (exe のファイルプロパティやタグと突き合わせられるようにするため)。
        Assert.Equal("1.2.3", AppVersion.Format("1.2.3+abc1234", new Version(1, 2, 3, 0)));
        Assert.Equal("1.2.3-rc.1", AppVersion.Format("1.2.3-rc.1+abc1234", new Version(1, 2, 3, 0)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+abc1234")] // メタデータのみでバージョン本体が無い
    public void Format_FallsBackToAssemblyVersion(string? informational)
    {
        Assert.Equal("4.5.6.0", AppVersion.Format(informational, new Version(4, 5, 6, 0)));
    }

    [Fact]
    public void Format_WithoutAnyVersion_ReturnsZero()
    {
        Assert.Equal("0.0.0", AppVersion.Format(null, null));
    }

    [Fact]
    public void Current_ReturnsNonEmptyVersion()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppVersion.Current));
    }
}
