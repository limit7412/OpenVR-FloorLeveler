using System.Reflection;

namespace FloorLeveler.App.Services;

/// <summary>
/// 実行中アセンブリのバージョン表示 (仕様 §8.4: タグ駆動バージョンを exe に埋め込み、
/// <c>--version</c> で確認できるようにする)。
/// </summary>
public static class AppVersion
{
    /// <summary>このアセンブリの表示用バージョン。</summary>
    public static string Current => Format(typeof(AppVersion).Assembly);

    /// <summary>アセンブリから表示用バージョンを取り出す。</summary>
    public static string Format(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return Format(
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            assembly.GetName().Version);
    }

    /// <summary>
    /// 表示用バージョンを組み立てる (純粋部分)。プレリリース識別子 (1.2.3-rc.1) や
    /// ビルドメタデータ (1.2.3+build.1) はアセンブリバージョンでは失われるため、
    /// InformationalVersion を優先して<em>そのまま</em>返す。埋め込まれた値を加工すると
    /// exe のファイルプロパティやタグと表示が食い違うため、除去は行わない
    /// (コミットハッシュの自動付与は csproj の IncludeSourceRevisionInInformationalVersion
    /// で抑止済み)。取れない場合はアセンブリバージョン、それも無ければ "0.0.0"。
    /// </summary>
    /// <param name="informationalVersion">AssemblyInformationalVersion の値。</param>
    /// <param name="assemblyVersion">アセンブリバージョン (フォールバック)。</param>
    public static string Format(string? informationalVersion, Version? assemblyVersion)
    {
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion.Trim();
        }

        return assemblyVersion?.ToString() ?? "0.0.0";
    }
}
