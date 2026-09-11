using System.Security.Cryptography;

namespace FluxDisplay.Core.Updates;

// Streams a setup asset to a unique temp file with progress, timeouts and
// cleanup. SHA-256 is computed on the fly while writing; the file is never
// read twice. Any failure (cancel, HTTP, stall, disk, hash policy handled by
// the caller) deletes the partial file. Cancellation is normal: it propagates
// as OperationCanceledException after cleanup.
public sealed class UpdateDownloader : IUpdateDownloader
{
    // Explicit, testable budgets. The user CancellationToken is not a timeout.
    public static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan WholeDownloadBudget = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan NoProgressTimeout = TimeSpan.FromSeconds(60);
    private const int BufferSize = 81920;

    private readonly HttpClient _http;
    private readonly string _downloadDirectory;
    private readonly Action<string>? _log;
    private readonly TimeSpan _headerTimeout;
    private readonly TimeSpan _budget;
    private readonly TimeSpan _noProgressTimeout;

    public UpdateDownloader(
        HttpClient http,
        string? downloadDirectory = null,
        Action<string>? log = null,
        TimeSpan? headerTimeout = null,
        TimeSpan? budget = null,
        TimeSpan? noProgressTimeout = null)
    {
        _http = http;
        _downloadDirectory = downloadDirectory ?? Path.GetTempPath();
        _log = log;
        _headerTimeout = headerTimeout ?? HeaderTimeout;
        _budget = budget ?? WholeDownloadBudget;
        _noProgressTimeout = noProgressTimeout ?? NoProgressTimeout;
    }

    public async Task<DownloadedUpdate> DownloadAsync(
        UpdateInfo update,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(update);
        using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budgetCts.CancelAfter(_budget);
        var budgetToken = budgetCts.Token;

        var path = Path.Combine(_downloadDirectory, $"FluxDisplay-setup-{Guid.NewGuid():N}.exe");
        try
        {
            using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(budgetToken);
            headerCts.CancelAfter(_headerTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, update.SetupUrl);
            using var response = await SendForHeadersAsync(request, headerCts.Token, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateDownloadException($"Setup download HTTP {(int)response.StatusCode}.");
            }

            var total = response.Content.Headers.ContentLength;
            using var web = await response.Content.ReadAsStreamAsync(budgetToken).ConfigureAwait(false)
                ?? throw new UpdateDownloadException("Setup download has no content stream.");

            Directory.CreateDirectory(_downloadDirectory);
            long received = 0;
            byte[] hash;
            var buffer = new byte[BufferSize];
            // A hung read must not wait out the whole budget: every chunk
            // re-arms the no-progress watchdog, which shares the budget/user
            // cancellation underneath.
            using var stallCts = CancellationTokenSource.CreateLinkedTokenSource(budgetToken);
            await using (var file = File.Create(path))
            {
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                while (true)
                {
                    budgetToken.ThrowIfCancellationRequested();
                    stallCts.CancelAfter(_noProgressTimeout);
                    int read;
                    try
                    {
                        read = await web.ReadAsync(buffer.AsMemory(), stallCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested && !budgetToken.IsCancellationRequested)
                    {
                        throw new UpdateDownloadException("Setup download stalled with no progress.");
                    }
                    catch (Exception ex) when (ex is IOException || ex is HttpRequestException)
                    {
                        throw new UpdateDownloadException("Setup download interrupted.", ex);
                    }

                    if (read == 0)
                    {
                        break;
                    }

                    try
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read), budgetToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex) when (ex is IOException)
                    {
                        throw new UpdateDownloadException("Setup download could not be written to disk.", ex);
                    }

                    sha.AppendData(buffer, 0, read);
                    received += read;
                    progress?.Report(new UpdateDownloadProgress(received, total));
                }

                hash = sha.GetCurrentHash();
            }

            var hex = Convert.ToHexString(hash).ToLowerInvariant();
            _log?.Invoke($"Setup downloaded: {received} bytes.");
            return new DownloadedUpdate(path, received, hex);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // An internal budget fired, not the user: controlled failure.
            DeleteQuietly(path);
            throw new UpdateDownloadException("Setup download timed out.");
        }
        catch
        {
            DeleteQuietly(path);
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendForHeadersAsync(
        HttpRequestMessage request, CancellationToken headerToken, CancellationToken userToken)
    {
        try
        {
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!userToken.IsCancellationRequested)
        {
            throw new UpdateDownloadException("Setup download timed out waiting for headers.");
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    public static bool DigestsEqual(string expectedHex, string actualHex)
    {
        try
        {
            var expected = Convert.FromHexString(expectedHex);
            var actual = Convert.FromHexString(actualHex);
            return expected.Length == actual.Length &&
                CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch
        {
            return false;
        }
    }
}

// A download that failed for a non-cancellation reason (HTTP, stall, budget,
// disk). User cancellation propagates as OperationCanceledException instead.
public sealed class UpdateDownloadException : Exception
{
    public UpdateDownloadException(string message)
        : base(message)
    {
    }

    public UpdateDownloadException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
