using System.Reflection;
using System.Runtime.InteropServices;

namespace FloorLeveler.OpenVr;

/// <summary>
/// <c>openvr_api.dll</c> の解決 (仕様 §8.2)。exe には内包せず、
/// <list type="number">
///   <item>exe と同じディレクトリ / OS の検索パス</item>
///   <item>SteamVR に付属するもの (<see cref="OpenVrRuntimeLocator"/>)</item>
/// </list>
/// の順で探す。どちらでも見つからない場合は既定の失敗処理
/// (<see cref="DllNotFoundException"/>) に委ねる。
/// </summary>
public static class OpenVrNativeLibrary
{
    /// <summary>P/Invoke で使うライブラリ名 (<see cref="NativeMethods"/> と一致させる)。</summary>
    internal const string LibraryName = "openvr_api";

    /// <summary>SteamVR 側を探すときのファイル名。</summary>
    internal const string FileName = "openvr_api.dll";

    private static readonly Assembly Self = typeof(OpenVrNativeLibrary).Assembly;

    // 解決子の登録は 1 回だけ (2 回目の SetDllImportResolver は例外)。かつ登録が
    // 完了するまで他スレッドを通さない必要があるため Lazy で直列化する。フラグを
    // 先に立てる方式では、登録前に別スレッドが P/Invoke へ進み得る。
    private static readonly Lazy<bool> Registration = new(
        () =>
        {
            NativeLibrary.SetDllImportResolver(Self, Resolve);
            return true;
        },
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// DllImport の解決子を登録する。最初の P/Invoke より前に呼ぶこと。
    /// 複数回・複数スレッドから呼んでも登録は 1 回だけで、登録が完了するまで
    /// 呼び出し側は戻らない (登録前に P/Invoke へ進ませないため)。
    /// </summary>
    public static void Register() => _ = Registration.Value;

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
        {
            return IntPtr.Zero; // 対象外は既定の解決に任せる
        }

        // 1) 既定の探索を優先する。exe と同じディレクトリに置かれた dll や、
        //    パスの通った dll をそのまま使うため。任意のバージョンに差し替えたい
        //    場合もここで効く。この API は解決子を再入しないので再帰しない。
        if (NativeLibrary.TryLoad(libraryName, assembly, searchPath, out var handle))
        {
            return handle;
        }

        // 2) SteamVR に付属するものを探す。exe の隣に dll を置かなくても、
        //    SteamVR が入っていれば単体で動くようにするため。
        foreach (var candidate in OpenVrRuntimeLocator.EnumerateCandidates(FileName))
        {
            if (NativeLibrary.TryLoad(candidate, out var runtimeHandle))
            {
                return runtimeHandle;
            }
        }

        return IntPtr.Zero; // 既定の失敗処理 (DllNotFoundException) に委ねる
    }
}
