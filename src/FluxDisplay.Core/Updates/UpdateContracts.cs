namespace FluxDisplay.Core.Updates;

// Release metadata source (GitHub in production, fake in tests).
public interface IUpdateSource
{
    Task<ReleaseQueryResult> QueryAsync(Version current, CancellationToken ct);
}

// Streaming setup download.
public interface IUpdateDownloader
{
    Task<DownloadedUpdate> DownloadAsync(
        UpdateInfo update,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken ct);
}

// Launches the setup and hands the process over (app exits).
public interface IUpdateInstaller
{
    Task InstallAsync(DownloadedUpdate downloaded, CancellationToken ct);
}

// User confirmation. Implemented with a real dialog in the app.
public interface IUpdatePrompter
{
    Task<bool> ConfirmInstallAsync(UpdateInfo update, bool isInstalledCopy, CancellationToken ct);
}

// Remembers which version was last installed, across restarts. Loop
// protection: if a check offers the same version again (broken version
// plumbing would otherwise reinstall forever), the run is skipped.
public interface IInstallHistory
{
    Task<string?> GetLastInstalledAsync(CancellationToken ct);
    // Records the just-installed version; null clears a stale record.
    Task RecordInstalledAsync(string? version, CancellationToken ct);
}

public enum UpdateTrigger
{
    Startup,
    Manual,
    Periodic
}
