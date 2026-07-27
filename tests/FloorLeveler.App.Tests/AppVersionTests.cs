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
    public void Format_KeepsBuildMetadataVerbatim()
    {
        // 埋め込まれた値を加工しない。除去すると exe のファイルプロパティやタグと
        // 表示が食い違い、スモークテストやリリース表示がずれるため。
        Assert.Equal("1.2.3+build.1", AppVersion.Format("1.2.3+build.1", new Version(1, 2, 3, 0)));
        Assert.Equal("1.2.3-rc.1+abc1234", AppVersion.Format("1.2.3-rc.1+abc1234", new Version(1, 2, 3, 0)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
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
