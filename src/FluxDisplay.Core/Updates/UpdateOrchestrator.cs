namespace FluxDisplay.Core.Updates;

// Owns the update matrix and lifecycle: startup/manual/periodic triggers,
// single-flight across all of them, mode matrix, verify policy, install
// handoff. UI reaches it only via CheckAsync + state reports; networking,
// download, dialogs and setup launch are injected seams.
//
// Behavior matrix (UpdateMode x copy):
//   Disabled            -> nothing, no requests.
//   Automatic+installed -> silent check/download/verify/install, no confirm.
//                          Silent requires a valid digest; without one the
//                          run fails instead of installing blind.
//   Automatic+portable  -> confirm dialog, then download/install on approval.
//   NotifyOnly          -> confirm dialog, then download/install on approval.
// A missing digest on a user-confirmed run is allowed (compat): the user saw
// and approved that exact version. The digest still only proves the file
// matches GitHub metadata — it is not a publisher signature.
public sealed class UpdateOrchestrator
{
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan PeriodicInterval = TimeSpan.FromHours(6);
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(24);

    private readonly IUpdateSource _source;
    private readonly IUpdateDownloader _downloader;
    private readonly IUpdateInstaller _installer;
    private readonly IUpdatePrompter _prompter;
    private readonly IInstallHistory _history;
    private readonly Func<UpdateMode> _mode;
    private readonly Func<bool> _isInstalledCopy;
    private readonly Func<Version> _currentVersion;
    private readonly Action<UpdateStateInfo>? _onState;
    private readonly Action<string>? _log;

    private readonly object _startLock = new();
    private readonly SemaphoreSlim _shutdownGate = new(1, 1);
    private readonly CancellationTokenSource _shutdownCts = new();
    private Task<UpdateStateInfo>? _inflight;
    private int _installStarted;
    private int _consecutiveFailures;
    private DateTime _backoffUntilUtc;
    private Task? _periodicTask;
    private int _periodicStarted;
    private readonly HashSet<string> _promptedVersions = new(StringComparer.OrdinalIgnoreCase);

    public UpdateOrchestrator(
        IUpdateSource source,
        IUpdateDownloader downloader,
        IUpdateInstaller installer,
        IUpdatePrompter prompter,
        IInstallHistory history,
        Func<UpdateMode> mode,
        Func<bool> isInstalledCopy,
        Func<Version> currentVersion,
        Action<UpdateStateInfo>? onState = null,
        Action<string>? log = null)
    {
        _source = source;
        _downloader = downloader;
        _installer = installer;
        _prompter = prompter;
        _history = history;
        _mode = mode;
        _isInstalledCopy = isInstalledCopy;
        _currentVersion = currentVersion;
        _onState = onState;
        _log = log;
    }

    // Starts the run unless one is already in flight (joiners share it).
    // Cancellation ownership: a joiner's token is NOT linked into the shared
    // run — cancelling a join only abandons that waiter, the operation keeps
    // going for the others. Only the starter's token and Shutdown() drive
    // real cancellation. Returned tasks never fault: every outcome is a state.
    public Task<UpdateStateInfo> CheckAsync(UpdateTrigger trigger, CancellationToken ct = default)
    {
        lock (_startLock)
        {
            if (_inflight is { IsCompleted: false } running)
            {
                return running;
            }

            // A manual check never bypasses an active server backoff; periodic
            // checks skip earlier in their loop.
            if (DateTime.UtcNow < _backoffUntilUtc)
            {
                _log?.Invoke("Update check skipped: server backoff is active.");
                return Task.FromResult(Report(UpdateState.Failed, "Update check failed"));
            }

            _inflight = RunAsync(trigger, ct);
            return _inflight;
        }
    }

    public Task<UpdateStateInfo> RunStartupCheckAsync(CancellationToken ct = default) =>
        RunDelayedAsync(StartupDelay, ct);

    public void StartPeriodicChecks(TimeSpan? interval = null)
    {
        if (Interlocked.Exchange(ref _periodicStarted, 1) == 1)
        {
            return;
        }

        var period = interval ?? PeriodicInterval;
        _periodicTask = PeriodicLoopAsync(period);
    }

    public void Shutdown()
    {
        try
        {
            _shutdownCts.Cancel();
        }
        catch
        {
        }
    }

    private async Task<UpdateStateInfo> RunDelayedAsync(TimeSpan delay, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);
        try
        {
            await Task.Delay(delay, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Report(UpdateState.Cancelled, "Update cancelled");
        }

        return await CheckAsync(UpdateTrigger.Startup, ct).ConfigureAwait(false);
    }

    private async Task PeriodicLoopAsync(TimeSpan interval)
    {
        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(_shutdownCts.Token).ConfigureAwait(false))
            {
                if (DateTime.UtcNow < _backoffUntilUtc)
                {
                    continue;
                }

                try
                {
                    await CheckAsync(UpdateTrigger.Periodic, _shutdownCts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // CheckAsync never faults by contract; belt and suspenders.
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<UpdateStateInfo> RunAsync(UpdateTrigger trigger, CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);
        var token = linkedCts.Token;
        try
        {
            if (Volatile.Read(ref _installStarted) == 1)
            {
                return Report(UpdateState.Installing, "Installing update…");
            }

            Report(UpdateState.Checking, "Checking for updates…");
            var mode = SafeMode();
            if (mode == UpdateMode.Disabled)
            {
                return Report(UpdateState.Idle, null);
            }

            var current = SafeCurrentVersion();
            var query = await _source.QueryAsync(current, token).ConfigureAwait(false);
            switch (query.Status)
            {
                case ReleaseQueryStatus.UpToDate:
                case ReleaseQueryStatus.NoPublishedRelease:
                    NoteSuccess();
                    return Report(UpdateState.NoUpdate, "No updates available");
                case ReleaseQueryStatus.RateLimited:
                    NoteFailure(TimeSpan.FromHours(1));
                    _log?.Invoke("Update rate limited by GitHub.");
                    return Report(UpdateState.Failed, "Update check failed");
                case ReleaseQueryStatus.Failed:
                    NoteFailure(TimeSpan.FromMinutes(15));
                    _log?.Invoke($"Update check failed: {query.Reason}");
                    return Report(UpdateState.Failed, "Update check failed");
            }

            var update = query.Update!;
            NoteSuccess();
            if (await WasAlreadyInstalledAsync(update.Version, token).ConfigureAwait(false))
            {
                _log?.Invoke($"Update {UpdateVersion.FormatShort(update.Version)} already installed, skipping.");
                return Report(UpdateState.NoUpdate, $"Update {UpdateVersion.FormatShort(update.Version)} already installed");
            }

            Report(UpdateState.UpdateAvailable, "Update available");
            var installed = SafeIsInstalledCopy();
            if (mode == UpdateMode.Automatic && installed)
            {
                // Silent runs never install blind: a missing digest is a
                // publish error, not a reason to ask (there is nobody to ask).
                // No download happened yet, so the message must say that.
                if (update.Digest is null)
                {
                    _log?.Invoke("Silent update refused: setup asset has no digest.");
                    return Report(UpdateState.Failed, "Release has no checksum. Automatic install is disabled.");
                }
            }
            else
            {
                var offered = UpdateVersion.FormatShort(update.Version);
                lock (_promptedVersions)
                {
                    if (_promptedVersions.Contains(offered))
                    {
                        return Report(UpdateState.Cancelled, "Update cancelled");
                    }
                }

                bool confirmed;
                try
                {
                    confirmed = await _prompter.ConfirmInstallAsync(update, installed, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"Update confirm failed: {ex.GetType().Name}.");
                    return Report(UpdateState.Failed, "Update check failed");
                }

                if (!confirmed)
                {
                    lock (_promptedVersions)
                    {
                        _promptedVersions.Add(offered);
                    }

                    return Report(UpdateState.Cancelled, "Update cancelled");
                }
            }

            Report(UpdateState.Downloading, "Downloading update…");
            DownloadedUpdate downloaded;
            try
            {
                downloaded = await _downloader.DownloadAsync(update, new Progress<UpdateDownloadProgress>(p =>
                {
                    var text = p.Percentage is double pct
                        ? $"Downloading update… {pct:0}%"
                        : "Downloading update…";
                    _onState?.Invoke(new UpdateStateInfo(UpdateState.Downloading, text, p.Percentage));
                }), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (UpdateDownloadException ex)
            {
                _log?.Invoke($"Update download failed: {ex.Message}");
                return Report(UpdateState.Failed, "Update download failed");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Update download failed: {ex.GetType().Name}.");
                return Report(UpdateState.Failed, "Update download failed");
            }

            Report(UpdateState.Verifying, "Verifying update…");
            if (update.Digest is not null &&
                !UpdateDownloader.DigestsEqual(update.Digest, downloaded.Sha256Hex))
            {
                DeleteQuietly(downloaded.Path);
                _log?.Invoke("Downloaded update digest mismatch; file deleted, setup not started.");
                return Report(UpdateState.Failed, "Downloaded update could not be verified");
            }

            if (Interlocked.Exchange(ref _installStarted, 1) == 1)
            {
                return Report(UpdateState.Installing, "Installing update…");
            }

            Report(UpdateState.Installing, "Installing update…");
            // Record BEFORE the handoff: a successful install exits the
            // process, so nothing after InstallAsync runs. On launch failure
            // the record is cleared again below.
            await RecordInstalledAsync(UpdateVersion.FormatShort(update.Version)).ConfigureAwait(false);
            try
            {
                await _installer.InstallAsync(downloaded, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await RecordInstalledAsync(null).ConfigureAwait(false);
                _log?.Invoke($"Update install failed: {ex.GetType().Name}.");
                return Report(UpdateState.Failed, "Update install failed");
            }

            return new UpdateStateInfo(UpdateState.Installing, "Installing update…", null);
        }
        catch (OperationCanceledException)
        {
            return Report(UpdateState.Cancelled, "Update cancelled");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Update run failed: {ex.GetType().Name}.");
            return Report(UpdateState.Failed, "Update check failed");
        }
    }

    private UpdateMode SafeMode()
    {
        try
        {
            return _mode();
        }
        catch
        {
            return UpdateMode.Automatic;
        }
    }

    private Version SafeCurrentVersion()
    {
        try
        {
            return _currentVersion() ?? new Version(1, 0, 0, 0);
        }
        catch
        {
            return new Version(1, 0, 0, 0);
        }
    }

    private bool SafeIsInstalledCopy()
    {
        try
        {
            return _isInstalledCopy();
        }
        catch
        {
            return false;
        }
    }

    // Loop protection: skip an offered version this updater already
    // installed (broken version plumbing would otherwise reinstall forever).
    // A stale record (manual downgrade, fixed plumbing) is forgotten so a
    // genuinely newer-or-equal offer flows normally. History errors are
    // ignored: worst case is one redundant install attempt, never a skip.
    private async Task<bool> WasAlreadyInstalledAsync(Version offered, CancellationToken ct)
    {
        try
        {
            var last = await _history.GetLastInstalledAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(last))
            {
                return false;
            }

            if (!UpdateVersion.TryParseTag(last.Trim(), out var recorded))
            {
                return false;
            }

            var current = SafeCurrentVersion();
            if (UpdateVersion.Compare(recorded, current) <= 0)
            {
                try
                {
                    await _history.RecordInstalledAsync(null, ct).ConfigureAwait(false);
                }
                catch
                {
                }

                return false;
            }

            return UpdateVersion.Compare(recorded, offered) == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task RecordInstalledAsync(string? version)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _history.RecordInstalledAsync(version, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Install history record failed: {ex.GetType().Name}.");
        }
    }

    private void NoteSuccess()
    {
        _consecutiveFailures = 0;
        _backoffUntilUtc = default;
    }

    private void NoteFailure(TimeSpan backoff)
    {
        _consecutiveFailures++;
        var doubled = TimeSpan.FromTicks(Math.Min(
            MaxBackoff.Ticks,
            backoff.Ticks * (long)Math.Pow(2, Math.Min(_consecutiveFailures - 1, 6))));
        _backoffUntilUtc = DateTime.UtcNow + doubled;
    }

    private UpdateStateInfo Report(UpdateState state, string? message, double? progress = null)
    {
        var info = new UpdateStateInfo(state, message, progress);
        try
        {
            _onState?.Invoke(info);
        }
        catch
        {
        }

        return info;
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
}
