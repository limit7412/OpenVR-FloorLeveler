using FloorLeveler.OpenVr;

namespace FloorLeveler.OpenVr.Tests;

public class OpenVrRuntimeLocatorTests
{
    private const string Dll = "openvr_api.dll";
    private const string Arch = "win64";

    private static string Registry(params string[] runtimes)
        => $$"""
        {
          "config" : [ "C:\\Steam\\config" ],
          "external_drivers" : null,
          "jsonid" : "vrpathreg",
          "log" : [ "C:\\Steam\\logs" ],
          "runtime" : [ {{string.Join(", ", runtimes.Select(r => $"\"{r.Replace("\\", "\\\\")}\""))}} ],
          "version" : 1
        }
        """;

    [Fact]
    public void ParseRuntimePaths_ReadsRuntimeArray()
    {
        var paths = OpenVrRuntimeLocator.ParseRuntimePaths(
            Registry(@"C:\Steam\steamapps\common\SteamVR", @"D:\SteamVR"));

        Assert.Equal([@"C:\Steam\steamapps\common\SteamVR", @"D:\SteamVR"], paths);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]                       // 壊れたファイル
    [InlineData("[1, 2, 3]")]                        // オブジェクトではない
    [InlineData("""{ "runtime": null }""")]          // SteamVR 未インストール時に入り得る
    [InlineData("""{ "runtime": "C:\\SteamVR" }""")] // 配列ではない
    [InlineData("""{ "version": 1 }""")]             // runtime が無い
    public void ParseRuntimePaths_MalformedInput_YieldsNoCandidates(string? json)
    {
        // 最後の手段の探索なので、壊れていても例外にせず「候補なし」にする。
        // ここで throw すると、既定の探索で見つかる環境まで道連れになる。
        Assert.Empty(OpenVrRuntimeLocator.ParseRuntimePaths(json));
    }

    [Fact]
    public void ParseRuntimePaths_SkipsNonStringAndBlankEntries()
    {
        var paths = OpenVrRuntimeLocator.ParseRuntimePaths(
            """{ "runtime": ["C:\\SteamVR", 42, null, "  ", "D:\\SteamVR"] }""");

        Assert.Equal([@"C:\SteamVR", @"D:\SteamVR"], paths);
    }

    [Fact]
    public void ToLibraryPath_UsesBinArchitectureLayout()
    {
        // SteamVR は <ランタイム>\bin\win64\openvr_api.dll に置く。
        Assert.Equal(
            Path.Combine(@"C:\SteamVR", "bin", "win64", Dll),
            OpenVrRuntimeLocator.ToLibraryPath(@"C:\SteamVR", Dll, Arch));
    }

    [Fact]
    public void BuildCandidates_PrefersRuntimeOverrideOverPathRegistry()
    {
        // VR_OVERRIDE は明示指定なので、設定ファイルより先に試す。
        var candidates = OpenVrRuntimeLocator.BuildCandidates(
            @"C:\Override", Registry(@"C:\SteamVR"), Dll, Arch);

        Assert.Equal(
            [
                Path.Combine(@"C:\Override", "bin", "win64", Dll),
                Path.Combine(@"C:\SteamVR", "bin", "win64", Dll),
            ],
            candidates);
    }

    [Fact]
    public void BuildCandidates_WithoutOverride_UsesPathRegistryOnly()
    {
        var candidates = OpenVrRuntimeLocator.BuildCandidates(
            null, Registry(@"C:\SteamVR"), Dll, Arch);

        Assert.Equal([Path.Combine(@"C:\SteamVR", "bin", "win64", Dll)], candidates);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildCandidates_BlankOverride_IsIgnored(string? runtimeOverride)
    {
        Assert.Empty(OpenVrRuntimeLocator.BuildCandidates(runtimeOverride, null, Dll, Arch));
    }

    [Fact]
    public void BuildCandidates_Deduplicates_IgnoringCase()
    {
        // 同じランタイムが VR_OVERRIDE と設定ファイルの両方に出るのは普通の状態。
        // 同じ dll を 2 回ロードしにいかないよう 1 つにまとめる。
        var candidates = OpenVrRuntimeLocator.BuildCandidates(
            @"C:\SteamVR", Registry(@"c:\steamvr", @"D:\SteamVR"), Dll, Arch);

        Assert.Equal(
            [
                Path.Combine(@"C:\SteamVR", "bin", "win64", Dll),
                Path.Combine(@"D:\SteamVR", "bin", "win64", Dll),
            ],
            candidates);
    }

    [Fact]
    public void BuildCandidates_TrimsSurroundingWhitespace()
    {
        var candidates = OpenVrRuntimeLocator.BuildCandidates(
            null, """{ "runtime": ["  C:\\SteamVR  "] }""", Dll, Arch);

        Assert.Equal([Path.Combine(@"C:\SteamVR", "bin", "win64", Dll)], candidates);
    }

    [Fact]
    public void BuildCandidates_NoSources_YieldsNothing()
    {
        // SteamVR 未インストール。既定の探索だけに任せる。
        Assert.Empty(OpenVrRuntimeLocator.BuildCandidates(null, null, Dll, Arch));
    }

    [Fact]
    public void ArchitectureDirectory_IsWin64OnX64()
    {
        // テストは x64 で走る前提。win32 は 32bit プロセス用。
        Assert.Equal("win64", OpenVrRuntimeLocator.ArchitectureDirectory);
    }

    [Fact]
    public void DefaultPathRegistryFile_IsUnderLocalApplicationData()
    {
        // OpenVR ランタイムと同じ場所を見ないと SteamVR の設定を拾えない。
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.Equal(
            Path.Combine(root, "openvr", "openvrpaths.vrpath"),
            OpenVrRuntimeLocator.DefaultPathRegistryFile);
    }

    [Fact]
    public void EnumerateCandidates_UsesRuntimeOverrideFromEnvironment()
    {
        var previous = Environment.GetEnvironmentVariable(OpenVrRuntimeLocator.RuntimeOverrideVariable);
        try
        {
            Environment.SetEnvironmentVariable(OpenVrRuntimeLocator.RuntimeOverrideVariable, @"C:\Override");

            Assert.Contains(
                Path.Combine(@"C:\Override", "bin", OpenVrRuntimeLocator.ArchitectureDirectory, Dll),
                OpenVrRuntimeLocator.EnumerateCandidates(Dll));
        }
        finally
        {
            Environment.SetEnvironmentVariable(OpenVrRuntimeLocator.RuntimeOverrideVariable, previous);
        }
    }

    [Fact]
    public void EnumerateCandidates_ReadsPathRegistryPointedByEnvironment()
    {
        var file = Path.Combine(Path.GetTempPath(), $"vrpath-{Guid.NewGuid():N}.vrpath");
        var previous = Environment.GetEnvironmentVariable(OpenVrRuntimeLocator.PathRegistryOverrideVariable);
        try
        {
            File.WriteAllText(file, Registry(@"C:\SteamVR"));
            Environment.SetEnvironmentVariable(OpenVrRuntimeLocator.PathRegistryOverrideVariable, file);

            Assert.Contains(
                Path.Combine(@"C:\SteamVR", "bin", OpenVrRuntimeLocator.ArchitectureDirectory, Dll),
                OpenVrRuntimeLocator.EnumerateCandidates(Dll));
        }
        finally
        {
            Environment.SetEnvironmentVariable(OpenVrRuntimeLocator.PathRegistryOverrideVariable, previous);
            File.Delete(file);
        }
    }

    [Fact]
    public void EnumerateCandidates_MissingPathRegistry_YieldsNothing()
    {
        // SteamVR が入っていない環境。例外にせず候補ゼロで返し、
        // 既定の探索の結果 (DllNotFoundException) をそのまま見せる。
        var previousRuntime = Environment.GetEnvironmentVariable(OpenVrRuntimeLocator.RuntimeOverrideVariable);
        var previousRegistry = Environment.GetEnvironmentVariable(OpenVrRuntimeLocator.PathRegistryOverrideVariable);
        try
        {
            Environment.SetEnvironmentVariable(OpenVrRuntimeLocator.RuntimeOverrideVariable, null);
            Environment.SetEnvironmentVariable(
                OpenVrRuntimeLocator.PathRegistryOverrideVariable,
                Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.vrpath"));

            Assert.Empty(OpenVrRuntimeLocator.EnumerateCandidates(Dll));
        }
        finally
        {
            Environment.SetEnvironmentVariable(OpenVrRuntimeLocator.RuntimeOverrideVariable, previousRuntime);
            Environment.SetEnvironmentVariable(OpenVrRuntimeLocator.PathRegistryOverrideVariable, previousRegistry);
        }
    }
}

public class OpenVrNativeLibraryTests
{
    [Fact]
    public void Register_IsIdempotent()
    {
        // 2 回目の SetDllImportResolver は例外になるため、複数回呼んでも
        // 登録は 1 回だけであること。
        OpenVrNativeLibrary.Register();
        OpenVrNativeLibrary.Register();
    }

    [Fact]
    public void Register_FromMultipleThreads_RegistersOnce()
    {
        // 直列化できていないと 2 回目の登録で InvalidOperationException になる。
        // かつ戻った時点で登録は完了している (登録前に P/Invoke へ進ませない)。
        Parallel.For(0, 32, _ => OpenVrNativeLibrary.Register());
    }
}
