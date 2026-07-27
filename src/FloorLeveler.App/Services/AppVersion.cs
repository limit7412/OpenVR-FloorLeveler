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
    /// 表示用バージョンを組み立てる (純粋部分)。プレリリース識別子 (1.2.3-rc.1) を
    /// 保持するため InformationalVersion を優先し、SourceLink 等が付ける
    /// "+{コミットハッシュ}" のビルドメタデータは落とす。取れない場合はアセンブリ
    /// バージョン、それも無ければ "0.0.0"。
    /// </summary>
    /// <param name="informationalVersion">AssemblyInformationalVersion の値。</param>
    /// <param name="assemblyVersion">アセンブリバージョン (フォールバック)。</param>
    public static string Format(string? informationalVersion, Version? assemblyVersion)
    {
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var plus = informationalVersion.IndexOf('+', StringComparison.Ordinal);
            var trimmed = (plus >= 0 ? informationalVersion[..plus] : informationalVersion).Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return assemblyVersion?.ToString() ?? "0.0.0";
    }
}
