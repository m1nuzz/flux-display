using System.Net;
using System.Security.Cryptography;
using Xunit;
using FluxDisplay.Core.Updates;

namespace FluxDisplay.Core.Tests;

// Streaming download: progress, timeouts, cancellation, cleanup. No network.
public sealed class UpdateDownloaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "FluxUpdTests-" + Guid.NewGuid().ToString("N"));

    private static readonly byte[] Payload = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
    private static readonly string PayloadHex = Convert.ToHexString(SHA256.HashData(Payload)).ToLowerInvariant();

    private static UpdateInfo Info(string url = "https://example.com/FluxDisplay-1.2.0-setup.exe") =>
        new(new Version(1, 2, 0, 0), new Uri(url), new Uri("https://example.com/r"), string.Empty, "FluxDisplay-1.2.0-setup.exe", null);

    private UpdateDownloader Downloader(
        FakeHandler handler,
        TimeSpan? header = null,
        TimeSpan? budget = null,
        TimeSpan? stall = null) =>
        new(new HttpClient(handler), _dir, null,
            headerTimeout: header ?? TimeSpan.FromSeconds(5),
            budget: budget ?? TimeSpan.FromMinutes(1),
            noProgressTimeout: stall ?? TimeSpan.FromSeconds(5));

    // Yields chunks with delays between them.
    private sealed class SlowStream : MemoryStream
    {
        private readonly int _chunk;
        private readonly TimeSpan _gap;

        public SlowStream(byte[] data, int chunk, TimeSpan gap)
            : base(data, writable: false)
        {
            _chunk = chunk;
            _gap = gap;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Delay(_gap, ct).ConfigureAwait(false);
            return await base.ReadAsync(buffer.Slice(0, Math.Min(buffer.Length, _chunk)), ct).ConfigureAwait(false);
        }
    }

    // Never yields data; honors cancellation only.
    private sealed class StallStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return 0;
        }
    }

    private sealed class DropStream : MemoryStream
    {
        private readonly long _failAfter;
        public DropStream(byte[] data, long failAfter)
            : base(data, writable: false)
        {
            _failAfter = failAfter;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (Position >= _failAfter)
            {
                throw new IOException("connection reset");
            }

            return base.ReadAsync(buffer, ct);
        }
    }

    private static async Task SpinUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var start = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - start > TimeSpan.FromMilliseconds(timeoutMs))
            {
                throw new TimeoutException("Timed out waiting for progress reports.");
            }

            await Task.Delay(20).ConfigureAwait(false);
        }
    }

    private static HttpResponseMessage Streamed(Stream body, long? length)
    {
        var content = new StreamContent(body);
        if (length.HasValue)
        {
            content.Headers.ContentLength = length.Value;
        }
        else
        {
            content.Headers.ContentLength = null;
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    [Fact]
    public async Task Full_download_hash_matches_fixture()
    {
        var handler = new FakeHandler(Streamed(new MemoryStream(Payload, writable: false), Payload.Length));
        var seen = new List<UpdateDownloadProgress>();
        var progress = new Progress<UpdateDownloadProgress>(p => seen.Add(p));
        var downloaded = await Downloader(handler).DownloadAsync(Info(), progress, CancellationToken.None);
        try
        {
            Assert.Equal(Payload.Length, downloaded.BytesReceived);
            Assert.Equal(PayloadHex, downloaded.Sha256Hex);
            Assert.Equal(Payload, await File.ReadAllBytesAsync(downloaded.Path));
            await SpinUntilAsync(() => seen.Count > 0);
            Assert.All(seen, p => Assert.Equal(Payload.Length, p.TotalBytes));
            Assert.Equal(100.0, seen[^1].Percentage);
        }
        finally
        {
            File.Delete(downloaded.Path);
        }
    }

    [Fact]
    public async Task Missing_length_reports_indeterminate_progress()
    {
        var handler = new FakeHandler(Streamed(new SlowStream(Payload, 64, TimeSpan.FromMilliseconds(10)), null));
        var seen = new List<UpdateDownloadProgress>();
        var downloaded = await Downloader(handler)
            .DownloadAsync(Info(), new Progress<UpdateDownloadProgress>(p => seen.Add(p)), CancellationToken.None);
        try
        {
            Assert.Equal(PayloadHex, downloaded.Sha256Hex);
            await SpinUntilAsync(() => seen.Count > 0);
            Assert.All(seen, p =>
            {
                Assert.Null(p.TotalBytes);
                Assert.Null(p.Percentage);
            });
            Assert.All(seen, p =>
            {
                Assert.Null(p.TotalBytes);
                Assert.Null(p.Percentage);
            });
        }
        finally
        {
            File.Delete(downloaded.Path);
        }
    }

    [Fact]
    public async Task Stalled_download_fails_and_cleans_up()
    {
        var handler = new FakeHandler(Streamed(new StallStream(), 1024));
        var before = Directory.Exists(_dir) ? Directory.GetFiles(_dir) : Array.Empty<string>();
        var ex = await Assert.ThrowsAsync<UpdateDownloadException>(() =>
            Downloader(handler, stall: TimeSpan.FromMilliseconds(200)).DownloadAsync(Info(), null, CancellationToken.None));
        Assert.Contains("stalled", ex.Message);
        var after = Directory.Exists(_dir) ? Directory.GetFiles(_dir) : Array.Empty<string>();
        Assert.Equal(before.Length, after.Length);
    }

    [Fact]
    public async Task Dropped_connection_fails_and_cleans_up()
    {
        var handler = new FakeHandler(Streamed(new DropStream(Payload, 64), Payload.Length));
        await Assert.ThrowsAsync<UpdateDownloadException>(() =>
            Downloader(handler).DownloadAsync(Info(), null, CancellationToken.None));
        Assert.Empty(Directory.Exists(_dir) ? Directory.GetFiles(_dir) : Array.Empty<string>());
    }

    [Fact]
    public async Task User_cancellation_propagates_and_cleans_up()
    {
        var handler = new FakeHandler(Streamed(new SlowStream(new byte[4096], 64, TimeSpan.FromMilliseconds(50)), 4096));
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(120));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Downloader(handler).DownloadAsync(Info(), null, cts.Token));
        Assert.Empty(Directory.Exists(_dir) ? Directory.GetFiles(_dir) : Array.Empty<string>());
    }

    [Fact]
    public async Task Http_error_is_controlled_failure_with_cleanup()
    {
        var handler = new FakeHandler(HttpStatusCode.ServiceUnavailable);
        await Assert.ThrowsAsync<UpdateDownloadException>(() =>
            Downloader(handler).DownloadAsync(Info(), null, CancellationToken.None));
        Assert.False(Directory.Exists(_dir) && Directory.GetFiles(_dir).Length > 0);
    }

    [Fact]
    public void Digest_compare_is_correct()
    {
        Assert.True(UpdateDownloader.DigestsEqual(PayloadHex, PayloadHex.ToUpperInvariant()));
        Assert.False(UpdateDownloader.DigestsEqual(PayloadHex, new string('0', 64)));
        Assert.False(UpdateDownloader.DigestsEqual("zz", PayloadHex));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch
        {
        }
    }
}
