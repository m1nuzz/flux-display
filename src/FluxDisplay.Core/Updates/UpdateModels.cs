namespace FluxDisplay.Core.Updates;

// A newer stable release with exactly one vetted setup asset.
public sealed record UpdateInfo(
    Version Version,
    Uri SetupUrl,
    Uri ReleaseUrl,
    string Notes,
    string AssetName,
    string? Digest);

public enum ReleaseQueryStatus
{
    // A newer vetted release exists.
    Available,
    // Current version is equal or newer (newer = downgrade protection, no install).
    UpToDate,
    // No published stable release (HTTP 404 on /latest).
    NoPublishedRelease,
    // HTTP 403 with exhausted rate limit.
    RateLimited,
    // Anything else: network, protocol, parse or policy failure. Never thrown to UI.
    Failed
}

public sealed record ReleaseQueryResult(ReleaseQueryStatus Status, UpdateInfo? Update, string? Reason);

// Streaming download progress. TotalBytes is null when Content-Length is
// missing: show indeterminate progress, never a bogus percentage.
public readonly record struct UpdateDownloadProgress(long BytesReceived, long? TotalBytes)
{
    public double? Percentage => TotalBytes is > 0
        ? Math.Clamp(100.0 * BytesReceived / TotalBytes.Value, 0.0, 100.0)
        : null;
}

// A fully downloaded, closed and (when required) verified setup file.
public sealed record DownloadedUpdate(string Path, long BytesReceived, string Sha256Hex);
