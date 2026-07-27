using System.Runtime.InteropServices;
using System.Text.Json;

namespace FloorLeveler.OpenVr;

/// <summary>
/// SteamVR に付属する <c>openvr_api.dll</c> の探索 (仕様 §8.2)。
/// OpenVR ランタイム自身と同じ規約で場所を決める。
/// <list type="number">
///   <item><c>VR_OVERRIDE</c> — ランタイムのルートを直接指定する環境変数</item>
///   <item><c>openvrpaths.vrpath</c> の <c>runtime</c> — SteamVR が書き出す設定ファイル。
///     場所は <c>VR_PATHREG_OVERRIDE</c>、既定では <c>%LOCALAPPDATA%\openvr\</c></item>
/// </list>
/// 探索の中心は純粋関数で、環境変数とファイル読み取りだけを薄い Shell に置く。
/// </summary>
internal static class OpenVrRuntimeLocator
{
    /// <summary>ランタイムのルートを直接指定する環境変数 (OpenVR 共通)。</summary>
    internal const string RuntimeOverrideVariable = "VR_OVERRIDE";

    /// <summary>openvrpaths.vrpath の場所を上書きする環境変数 (OpenVR 共通)。</summary>
    internal const string PathRegistryOverrideVariable = "VR_PATHREG_OVERRIDE";

    internal const string PathRegistryFileName = "openvrpaths.vrpath";

    /// <summary>ランタイムのルートからの相対位置 (アーキテクチャ部分は可変)。</summary>
    private const string BinDirectory = "bin";

    /// <summary>
    /// 実環境から候補パスを列挙する。存在確認まではせず、順序だけを決める
    /// (実際にロードできるかは呼び出し側が試す)。
    /// </summary>
    public static IEnumerable<string> EnumerateCandidates(string fileName)
        => BuildCandidates(
            Environment.GetEnvironmentVariable(RuntimeOverrideVariable),
            ReadPathRegistry(),
            fileName,
            ArchitectureDirectory);

    /// <summary>
    /// 候補パスを組み立てる (純粋)。空・重複は取り除き、指定順を保つ。
    /// </summary>
    /// <param name="runtimeOverride"><c>VR_OVERRIDE</c> の値 (未設定なら null)。</param>
    /// <param name="pathRegistryJson">openvrpaths.vrpath の内容 (読めなければ null)。</param>
    /// <param name="fileName">ライブラリのファイル名。</param>
    /// <param name="architectureDirectory"><c>bin</c> 配下のアーキテクチャ別ディレクトリ名。</param>
    public static IReadOnlyList<string> BuildCandidates(
        string? runtimeOverride,
        string? pathRegistryJson,
        string fileName,
        string architectureDirectory)
    {
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(runtimeOverride))
        {
            roots.Add(runtimeOverride);
        }

        roots.AddRange(ParseRuntimePaths(pathRegistryJson));

        var candidates = new List<string>(roots.Count);
        foreach (var root in roots)
        {
            var path = ToLibraryPath(root.Trim(), fileName, architectureDirectory);
            if (!candidates.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(path);
            }
        }

        return candidates;
    }

    /// <summary>ランタイムのルート → ライブラリのフルパス (純粋)。</summary>
    public static string ToLibraryPath(string runtimeRoot, string fileName, string architectureDirectory)
        => Path.Combine(runtimeRoot, BinDirectory, architectureDirectory, fileName);

    /// <summary>
    /// openvrpaths.vrpath から <c>runtime</c> の配列を取り出す (純粋)。
    /// 壊れた JSON や想定外の形は「候補なし」として扱う。この探索は最後の手段であり、
    /// ここで例外にすると既定の探索で見つかる環境まで巻き添えになるため。
    /// </summary>
    public static IReadOnlyList<string> ParseRuntimePaths(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("runtime", out var runtime)
                || runtime.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return runtime.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>openvrpaths.vrpath の既定の場所 (プロセスのユーザー基準)。</summary>
    internal static string DefaultPathRegistryFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "openvr",
        PathRegistryFileName);

    /// <summary>
    /// <c>bin</c> 配下のアーキテクチャ別ディレクトリ名。SteamVR は 64bit を
    /// <c>win64</c>、32bit を <c>win32</c> に置く。
    /// </summary>
    internal static string ArchitectureDirectory
        => RuntimeInformation.ProcessArchitecture == Architecture.X86 ? "win32" : "win64";

    /// <summary>openvrpaths.vrpath を読む。無い・読めない場合は null。</summary>
    private static string? ReadPathRegistry()
    {
        var path = Environment.GetEnvironmentVariable(PathRegistryOverrideVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            path = DefaultPathRegistryFile;
        }

        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
