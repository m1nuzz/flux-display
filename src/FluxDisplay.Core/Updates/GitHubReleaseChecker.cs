using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluxDisplay.Core.Updates;

// Queries the stable /latest release and vets exactly one setup asset.
// Pure policy, no UI: every outcome is a ReleaseQueryResult, never an
// exception (except cancellation). Asset rule is strict on purpose:
// `FluxDisplay-<X.Y.Z>-setup.exe` with X.Y.Z equal to the tag, exactly one
// match — 0 or 2+ is a controlled failure, never "the first .exe".
public sealed partial class GitHubReleaseChecker : IUpdateSource
{
    public const string DefaultLatestReleaseUrl = "https://api.github.com/repos/m1nuzz/flux-display/releases/latest";

    // Test hook for fake end-to-end runs: point to a local stub serving the
    // /latest shape. Never set in production; the URL is logged on use.
    public static string LatestReleaseUrl =>
        Environment.GetEnvironmentVariable("FLUXDISPLAY_UPDATE_API_URL") is { Length: > 0 } custom
            ? custom
            : DefaultLatestReleaseUrl;

    public static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(30);

    // Strict asset name; the captured version must equal the release tag.
    [GeneratedRegex(@"^FluxDisplay-(\d+\.\d+\.\d+)-setup\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex SetupAssetNameRegex();

    private readonly HttpClient _http;
    private readonly Action<string>? _log;
    private readonly TimeSpan _headerTimeout;

    public GitHubReleaseChecker(HttpClient http, Action<string>? log = null, TimeSpan? headerTimeout = null)
    {
        _http = http;
        _log = log;
        _headerTimeout = headerTimeout ?? HeaderTimeout;
    }

    public async Task<ReleaseQueryResult> QueryAsync(Version current, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(current);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_headerTimeout);

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fail("Update check timed out before headers.");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // No published stable release. Normal, not an error.
                return new ReleaseQueryResult(ReleaseQueryStatus.NoPublishedRelease, null, "No published stable release.");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return RateLimitOrForbidden(response);
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return Fail($"Update check HTTP {(int)response.StatusCode}.");
            }

            try
            {
                using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                return ParseRelease(doc.RootElement, current);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Fail($"Update check could not parse the release: {ex.GetType().Name}.");
            }
        }
    }

    private ReleaseQueryResult RateLimitOrForbidden(HttpResponseMessage response)
    {
        var remaining = Header(response, "X-RateLimit-Remaining");
        var reset = Header(response, "X-RateLimit-Reset");
        var retryAfter = Header(response, "Retry-After");
        _log?.Invoke($"Update check HTTP 403 rate-limit headers remaining={remaining} reset={reset} retry-after={retryAfter}.");
        if (remaining == "0")
        {
            return new ReleaseQueryResult(ReleaseQueryStatus.RateLimited, null, "GitHub rate limit exhausted.");
        }

        return Fail("Update check HTTP 403.");
    }

    private static string? Header(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values))
        {
            return string.Join(",", values);
        }

        if (response.Content.Headers.TryGetValues(name, out var contentValues))
        {
            return string.Join(",", contentValues);
        }

        return null;
    }

    private ReleaseQueryResult ParseRelease(JsonElement root, Version current)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Fail("Release JSON is not an object.");
        }

        var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
        if (!UpdateVersion.TryParseTag(tag, out var latest))
        {
            return Fail($"Release tag '{tag}' is not a stable vX.Y.Z tag.");
        }

        if (UpdateVersion.Compare(current, latest) >= 0)
        {
            if (UpdateVersion.Compare(current, latest) > 0)
            {
                _log?.Invoke($"Installed {UpdateVersion.Normalize(current)} is newer than latest {latest}: downgrade protection, no install.");
            }
            else
            {
                _log?.Invoke($"Up to date: current {UpdateVersion.Normalize(current)}, latest {latest}.");
            }

            return new ReleaseQueryResult(ReleaseQueryStatus.UpToDate, null, null);
        }

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return Fail("Release has no assets array.");
        }

        var matches = new List<(string Name, Uri Url, string? Digest)>();
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var m = SetupAssetNameRegex().Match(name);
            if (!m.Success)
            {
                continue; // portable ZIP, foreign exe, debug file: never selected.
            }

            if (!UpdateVersion.TryParseTag(m.Groups[1].Value, out var assetVersion) ||
                UpdateVersion.Compare(assetVersion, latest) != 0)
            {
                return Fail($"Setup asset '{name}' version does not match release tag '{tag}'.");
            }

            var urlText = asset.TryGetProperty("browser_download_url", out var urlEl) ? urlEl.GetString() : null;
            if (!Uri.TryCreate(urlText, UriKind.Absolute, out var url))
            {
                return Fail($"Setup asset '{name}' has no usable download URL.");
            }

            var (digestOk, digest) = ParseDigest(asset);
            if (!digestOk)
            {
                return Fail($"Setup asset '{name}' has a malformed digest.");
            }

            matches.Add((name, url, digest));
        }

        if (matches.Count == 0)
        {
            return Fail($"Release {tag} has no vetted setup asset.");
        }

        if (matches.Count > 1)
        {
            return Fail($"Release {tag} has {matches.Count} setup assets, expected exactly one.");
        }

        var match = matches[0];
        var releaseUrl = root.TryGetProperty("html_url", out var htmlEl) &&
            Uri.TryCreate(htmlEl.GetString(), UriKind.Absolute, out var parsed)
            ? parsed
            : new Uri("https://github.com/m1nuzz/flux-display/releases");
        var notes = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? string.Empty : string.Empty;
        _log?.Invoke($"Update available: current {UpdateVersion.Normalize(current)}, latest {latest}, asset {match.Name}.");
        return new ReleaseQueryResult(
            ReleaseQueryStatus.Available,
            new UpdateInfo(latest, match.Url, releaseUrl, notes, match.Name, match.Digest),
            null);
    }

    // GitHub may return assets[].digest as "sha256:<hex>". Anything else is
    // rejected; absence is allowed here and enforced by install policy later.
    // Returns (ok, normalizedHexOrNull).
    private static (bool Ok, string? Digest) ParseDigest(JsonElement asset)
    {
        if (!asset.TryGetProperty("digest", out var digestEl))
        {
            return (true, null);
        }

        var text = digestEl.ValueKind == JsonValueKind.String ? digestEl.GetString() : null;
        if (string.IsNullOrEmpty(text))
        {
            return (true, null);
        }

        const string prefix = "sha256:";
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || text.Length != prefix.Length + 64)
        {
            return (false, null);
        }

        var hex = text.Substring(prefix.Length);
        if (!hex.All(Uri.IsHexDigit))
        {
            return (false, null);
        }

        return (true, hex.ToLowerInvariant());
    }

    private static ReleaseQueryResult Fail(string reason) =>
        new(ReleaseQueryStatus.Failed, null, reason);
}
