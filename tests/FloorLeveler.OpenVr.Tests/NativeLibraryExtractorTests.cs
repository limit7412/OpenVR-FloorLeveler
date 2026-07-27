using System.Text;
using FloorLeveler.OpenVr;

namespace FloorLeveler.OpenVr.Tests;

public class NativeLibraryExtractorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "floorleveler-native-" + Guid.NewGuid().ToString("N"));

    private static Func<Stream> Payload(string content)
        => () => new MemoryStream(Encoding.UTF8.GetBytes(content));

    [Fact]
    public void EnsureExtracted_WritesPayloadUnderRoot()
    {
        var path = NativeLibraryExtractor.EnsureExtracted(Payload("fake-dll"), _root, "openvr_api.dll");

        Assert.True(File.Exists(path));
        Assert.Equal("fake-dll", File.ReadAllText(path));
        Assert.StartsWith(_root, path, StringComparison.Ordinal);
        Assert.Equal("openvr_api.dll", Path.GetFileName(path));
    }

    [Fact]
    public void EnsureExtracted_IsIdempotent_DoesNotRewrite()
    {
        // 同じ内容なら書き直さない (実行中の dll を上書きしないため)。
        var first = NativeLibraryExtractor.EnsureExtracted(Payload("fake-dll"), _root, "openvr_api.dll");
        var writtenAt = File.GetLastWriteTimeUtc(first);

        var second = NativeLibraryExtractor.EnsureExtracted(Payload("fake-dll"), _root, "openvr_api.dll");

        Assert.Equal(first, second);
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(second));
    }

    [Fact]
    public void EnsureExtracted_DifferentContent_UsesDifferentDirectory()
    {
        // 内容が変われば展開先も変わるので、古い dll を掴み続けない。
        var a = NativeLibraryExtractor.EnsureExtracted(Payload("version-a"), _root, "openvr_api.dll");
        var b = NativeLibraryExtractor.EnsureExtracted(Payload("version-b"), _root, "openvr_api.dll");

        Assert.NotEqual(a, b);
        Assert.NotEqual(Path.GetDirectoryName(a), Path.GetDirectoryName(b));
        Assert.Equal("version-a", File.ReadAllText(a));
        Assert.Equal("version-b", File.ReadAllText(b));
    }

    [Fact]
    public void EnsureExtracted_TruncatedExistingFile_IsRewritten()
    {
        // 書き出し途中で終わった残骸 (長さ不一致) は展開し直す。
        var path = NativeLibraryExtractor.EnsureExtracted(Payload("fake-dll"), _root, "openvr_api.dll");
        File.WriteAllText(path, "x");

        var again = NativeLibraryExtractor.EnsureExtracted(Payload("fake-dll"), _root, "openvr_api.dll");

        Assert.Equal(path, again);
        Assert.Equal("fake-dll", File.ReadAllText(again));
    }

    [Fact]
    public void EnsureExtracted_NonSeekablePayload_IsSupported()
    {
        // 埋め込みリソース以外 (シーク不可のストリーム) でも扱えること。
        var path = NativeLibraryExtractor.EnsureExtracted(
            () => new NonSeekableStream(Encoding.UTF8.GetBytes("streamed")), _root, "openvr_api.dll");

        Assert.Equal("streamed", File.ReadAllText(path));
    }

    [Fact]
    public void EnsureExtracted_RejectsEmptyArguments()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NativeLibraryExtractor.EnsureExtracted(null!, _root, "openvr_api.dll"));
        Assert.Throws<ArgumentException>(() =>
            NativeLibraryExtractor.EnsureExtracted(Payload("x"), " ", "openvr_api.dll"));
        Assert.Throws<ArgumentException>(() =>
            NativeLibraryExtractor.EnsureExtracted(Payload("x"), _root, " "));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>シーク不可のストリーム (埋め込みリソース以外の経路を再現する)。</summary>
    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

public class OpenVrNativeLibraryTests
{
    [Fact]
    public void Register_IsIdempotent()
    {
        // 何度呼んでも解決子の登録は 1 回だけ (2 回目以降で例外にならないこと)。
        OpenVrNativeLibrary.Register();
        OpenVrNativeLibrary.Register();
    }

    [Fact]
    public void EmbeddedLibrary_WhenPresent_ExtractsToLoadableFile()
    {
        // 内包の有無はビルド時に native/win-x64/openvr_api.dll があるかで決まるため、
        // このテストは「内包しているビルドなら展開まで通ること」を確認する
        // (内包しない通常のビルドでは検証対象が無いので何もしない)。
        if (!OpenVrNativeLibrary.HasEmbeddedLibrary)
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "floorleveler-embedded-" + Guid.NewGuid().ToString("N"));
        try
        {
            var payload = OpenVrNativeLibrary.OpenEmbeddedPayload;
            var path = NativeLibraryExtractor.EnsureExtracted(payload, root, "openvr_api.dll");

            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 0);

            // PE ファイル (MZ ヘッダ) が展開されていること。
            using var file = File.OpenRead(path);
            Assert.Equal((byte)'M', (byte)file.ReadByte());
            Assert.Equal((byte)'Z', (byte)file.ReadByte());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ExtractionRoot_IsUnderLocalApplicationData()
    {
        // 書き込みは %LOCALAPPDATA% 配下のみ (仕様 NF-3)。
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(localAppData, OpenVrNativeLibrary.ExtractionRoot, StringComparison.Ordinal);
        Assert.Contains("FloorLeveler", OpenVrNativeLibrary.ExtractionRoot, StringComparison.Ordinal);
    }
}
