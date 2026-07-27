using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace FloorLeveler.OpenVr;

/// <summary>
/// <c>openvr_api.dll</c> の解決 (仕様 §8.2)。既定の探索 (exe と同じディレクトリ /
/// OS の検索パス) を優先し、見つからない場合のみ exe に内包したコピーを展開して
/// ロードする。内包が無いビルドでは従来どおり既定の探索だけで動く。
/// </summary>
public static class OpenVrNativeLibrary
{
    /// <summary>P/Invoke で使うライブラリ名 (<see cref="NativeMethods"/> と一致させる)。</summary>
    internal const string LibraryName = "openvr_api";

    /// <summary>内包した dll の埋め込みリソース名 (csproj の LogicalName と一致させる)。</summary>
    internal const string ResourceName = "FloorLeveler.OpenVr.native.win-x64.openvr_api.dll";

    private const string FileName = "openvr_api.dll";

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

    /// <summary>openvr_api.dll を内包したビルドかどうか。</summary>
    public static bool HasEmbeddedLibrary
        => Array.IndexOf(Self.GetManifestResourceNames(), ResourceName) >= 0;

    /// <summary>
    /// DllImport の解決子を登録する。最初の P/Invoke より前に呼ぶこと。
    /// 複数回・複数スレッドから呼んでも登録は 1 回だけで、登録が完了するまで
    /// 呼び出し側は戻らない (登録前に P/Invoke へ進ませないため)。
    /// </summary>
    public static void Register() => _ = Registration.Value;

    /// <summary>展開先のルート。書き込みは %LOCALAPPDATA% 配下のみ (仕様 NF-3)。</summary>
    internal static string ExtractionRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FloorLeveler",
        "native");

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
        {
            return IntPtr.Zero; // 対象外は既定の解決に任せる
        }

        // 1) 既定の探索を優先する。exe と同じディレクトリに置かれた dll や、
        //    SteamVR のパスが通っている環境をそのまま活かすため。
        //    この API は解決子を再入しないので無限再帰にはならない。
        if (NativeLibrary.TryLoad(libraryName, assembly, searchPath, out var handle))
        {
            return handle;
        }

        // 2) 内包していれば展開してロードする (単一 exe 配布、仕様 NF-1)。
        if (!HasEmbeddedLibrary)
        {
            return IntPtr.Zero; // 既定の失敗処理 (DllNotFoundException) に委ねる
        }

        var path = NativeLibraryExtractor.EnsureExtracted(OpenEmbeddedPayload, ExtractionRoot, FileName);
        return NativeLibrary.Load(path);
    }

    /// <summary>内包した dll のストリームを開く。内包していない場合は例外。</summary>
    internal static Stream OpenEmbeddedPayload()
        => Self.GetManifestResourceStream(ResourceName)
            ?? throw new OpenVrException($"内包された {FileName} を読み出せませんでした。");
}

/// <summary>
/// 埋め込みネイティブライブラリの展開 (仕様 §8.2)。single-file ホストの自己展開に
/// 依存せず、任意のディレクトリへ展開してからロードできるようにする。
/// </summary>
internal static class NativeLibraryExtractor
{
    /// <summary>
    /// ペイロードを内容ハッシュ名のサブディレクトリへ展開し、そのパスを返す。
    /// 同じ内容が展開済みなら書き直さない (実行中の dll を上書きせず、プロセス間・
    /// 起動間で再利用するため)。内容が変わればディレクトリが変わるので、
    /// 古い dll を掴み続けることもない。
    /// </summary>
    /// <param name="openPayload">ペイロードを開く関数 (ハッシュ計算と書き出しで 2 回呼ぶ)。</param>
    /// <param name="rootDirectory">展開先のルート。</param>
    /// <param name="fileName">展開後のファイル名。</param>
    public static string EnsureExtracted(Func<Stream> openPayload, string rootDirectory, string fileName)
    {
        ArgumentNullException.ThrowIfNull(openPayload);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var (hash, length) = Fingerprint(openPayload);
        var directory = Path.Combine(rootDirectory, hash[..DirectoryNameLength]);
        var target = Path.Combine(directory, fileName);

        // 再利用前に内容ハッシュまで照合する。ディレクトリ名はあくまで期待値であり、
        // 中身が破損・差し替えされていないことは保証しないため (長さ一致でも中身が
        // 違えば壊れた dll を毎回ロードし続けることになる)。
        if (IsUpToDate(target, length, hash))
        {
            return target;
        }

        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $"{fileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var source = openPayload())
            using (var destination = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            {
                source.CopyTo(destination);
            }

            // 一時ファイルへ書き切ってから差し替える (部分ファイルをロードしないため)。
            File.Move(temp, target, overwrite: true);
        }
        catch (IOException) when (IsUpToDate(target, length, hash))
        {
            // 別プロセスが先に展開済み (実行中でロックされている場合を含む)。
            TryDelete(temp);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        return target;
    }

    /// <summary>展開先ディレクトリ名に使うハッシュの文字数。</summary>
    private const int DirectoryNameLength = 32;

    /// <summary>
    /// 展開済みファイルが期待どおりか (長さと内容ハッシュの両方が一致するか)。
    /// 長さを先に見て、違えばハッシュ計算を省く。
    /// </summary>
    private static bool IsUpToDate(string path, long expectedLength, string expectedHash)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length != expectedLength)
            {
                return false;
            }

            using var stream = File.OpenRead(path);
            return string.Equals(ToHex(SHA256.HashData(stream)), expectedHash, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>ペイロードの内容ハッシュ (16 進) と長さ。</summary>
    private static (string Hash, long Length) Fingerprint(Func<Stream> openPayload)
    {
        using var stream = openPayload();

        if (stream.CanSeek)
        {
            var length = stream.Length;
            return (ToHex(SHA256.HashData(stream)), length);
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;
        return (ToHex(SHA256.HashData(buffer)), buffer.Length);
    }

    private static string ToHex(byte[] hash) => Convert.ToHexString(hash).ToLowerInvariant();

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 後始末の失敗は握りつぶす。
        }
    }
}
