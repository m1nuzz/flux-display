using Xunit;
using FluxDisplay.Core.Updates;

namespace FluxDisplay.Core.Tests;

// Mode matrix, single-flight and install guards with faked seams.
public sealed class UpdateOrchestratorTests
{
    private const string Digest = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private static UpdateInfo Info(string? digest = Digest) => new(
        new Version(1, 2, 0, 0),
        new Uri("https://example.com/FluxDisplay-1.2.0-setup.exe"),
        new Uri("https://example.com/r"),
        string.Empty,
        "FluxDisplay-1.2.0-setup.exe",
        digest);

    private sealed class Source : IUpdateSource
    {
        public ReleaseQueryResult Result = new(ReleaseQueryStatus.UpToDate, null, null);
        public int Calls;
        public Task<ReleaseQueryResult> QueryAsync(Version current, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class Downloader : IUpdateDownloader
    {
        public int Calls;
        public Func<UpdateInfo, CancellationToken, Task<DownloadedUpdate>>? Impl;
        public async Task<DownloadedUpdate> DownloadAsync(UpdateInfo u, IProgress<UpdateDownloadProgress>? p, CancellationToken ct)
        {
            Calls++;
            if (Impl is not null)
            {
                return await Impl(u, ct).ConfigureAwait(false);
            }

            return new DownloadedUpdate(Path.GetTempFileName(), 1, Digest);
        }
    }

    private sealed class Installer : IUpdateInstaller
    {
        public int Calls;
        public Task InstallAsync(DownloadedUpdate d, CancellationToken ct)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class Prompter : IUpdatePrompter
    {
        public int Calls;
        public bool Answer = true;
        public Task<bool> ConfirmInstallAsync(UpdateInfo u, bool installed, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Answer);
        }
    }

    private static UpdateOrchestrator Orch(
        Source source,
        Downloader downloader,
        Installer installer,
        Prompter prompter,
        UpdateMode mode = UpdateMode.Automatic,
        bool installed = true,
        List<UpdateStateInfo>? states = null) =>
        new(source, downloader, installer, prompter,
            () => mode, () => installed, () => new Version(1, 0, 0, 0),
            states is null ? null : (Action<UpdateStateInfo>)(s => states.Add(s)));

    private static Source AvailableSource() => new()
    {
        Result = new ReleaseQueryResult(ReleaseQueryStatus.Available, Info(), null)
    };

    [Fact]
    public async Task Disabled_mode_makes_no_request()
    {
        var source = AvailableSource();
        var orch = Orch(source, new Downloader(), new Installer(), new Prompter(), UpdateMode.Disabled);
        var result = await orch.CheckAsync(UpdateTrigger.Startup);
        Assert.Equal(UpdateState.Idle, result.State);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task Installed_automatic_runs_silent_without_confirm()
    {
        var source = AvailableSource();
        var downloader = new Downloader();
        var installer = new Installer();
        var prompter = new Prompter();
        var orch = Orch(source, downloader, installer, prompter, UpdateMode.Automatic, installed: true);
        var result = await orch.CheckAsync(UpdateTrigger.Startup);
        Assert.Equal(UpdateState.Installing, result.State);
        Assert.Equal(0, prompter.Calls);
        Assert.Equal(1, downloader.Calls);
        Assert.Equal(1, installer.Calls);
    }

    [Fact]
    public async Task Portable_automatic_asks_first()
    {
        var source = AvailableSource();
        var prompter = new Prompter { Answer = false };
        var installer = new Installer();
        var orch = Orch(source, new Downloader(), installer, prompter, UpdateMode.Automatic, installed: false);
        var result = await orch.CheckAsync(UpdateTrigger.Startup);
        Assert.Equal(UpdateState.Cancelled, result.State);
        Assert.Equal(1, prompter.Calls);
        Assert.Equal(0, installer.Calls);
    }

    [Fact]
    public async Task Notify_only_asks_first()
    {
        var source = AvailableSource();
        var prompter = new Prompter { Answer = true };
        var installer = new Installer();
        var orch = Orch(source, new Downloader(), installer, prompter, UpdateMode.NotifyOnly, installed: true);
        var result = await orch.CheckAsync(UpdateTrigger.Startup);
        Assert.Equal(UpdateState.Installing, result.State);
        Assert.Equal(1, prompter.Calls);
    }

    [Fact]
    public async Task Missing_digest_blocks_silent_but_allows_confirmed()
    {
        var source = new Source
        {
            Result = new ReleaseQueryResult(ReleaseQueryStatus.Available, Info(digest: null), null)
        };

        var silent = Orch(source, new Downloader(), new Installer(), new Prompter(), UpdateMode.Automatic, installed: true);
        var silentResult = await silent.CheckAsync(UpdateTrigger.Startup);
        Assert.Equal(UpdateState.Failed, silentResult.State);
        Assert.Equal("Release has no checksum. Automatic install is disabled.", silentResult.Message);

        var installer = new Installer();
        var confirmed = Orch(source, new Downloader(), installer, new Prompter(), UpdateMode.NotifyOnly, installed: true);
        var confirmedResult = await confirmed.CheckAsync(UpdateTrigger.Manual);
        Assert.Equal(UpdateState.Installing, confirmedResult.State);
        Assert.Equal(1, installer.Calls);
    }

    [Fact]
    public async Task Digest_mismatch_deletes_and_never_installs()
    {
        var source = new Source
        {
            Result = new ReleaseQueryResult(ReleaseQueryStatus.Available, Info("00" + Digest.Substring(2)), null)
        };
        var installer = new Installer();
        var orch = Orch(source, new Downloader(), installer, new Prompter(), UpdateMode.Automatic, installed: true);
        var result = await orch.CheckAsync(UpdateTrigger.Startup);
        Assert.Equal(UpdateState.Failed, result.State);
        Assert.Equal("Downloaded update could not be verified", result.Message);
        Assert.Equal(0, installer.Calls);
    }

    [Fact]
    public async Task Concurrent_triggers_share_one_run()
    {
        var gate = new TaskCompletionSource();
        var source = new Source
        {
            Result = new ReleaseQueryResult(ReleaseQueryStatus.Available, Info(), null)
        };
        var downloader = new Downloader
        {
            Impl = async (_, ct) =>
            {
                await gate.Task.WaitAsync(ct).ConfigureAwait(false);
                return new DownloadedUpdate(Path.GetTempFileName(), 1, Digest);
            }
        };
        var orch = Orch(source, downloader, new Installer(), new Prompter());
        var first = orch.CheckAsync(UpdateTrigger.Startup);
        var second = orch.CheckAsync(UpdateTrigger.Manual);
        await Task.Delay(100);
        Assert.Equal(1, source.Calls);
        gate.SetResult();
        Assert.Same(await first, await second);
        Assert.Equal(1, downloader.Calls);
    }

    [Fact]
    public async Task Install_starts_only_once()
    {
        var source = AvailableSource();
        var installer = new Installer();
        var orch = Orch(source, new Downloader(), installer, new Prompter());
        await orch.CheckAsync(UpdateTrigger.Startup);
        var again = await orch.CheckAsync(UpdateTrigger.Manual);
        Assert.Equal(UpdateState.Installing, again.State);
        Assert.Equal(1, installer.Calls);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task Manual_check_does_not_bypass_active_backoff()
    {
        var source = new Source { Result = new ReleaseQueryResult(ReleaseQueryStatus.Failed, null, "boom") };
        var orch = Orch(source, new Downloader(), new Installer(), new Prompter());
        var first = await orch.CheckAsync(UpdateTrigger.Startup);
        Assert.Equal(UpdateState.Failed, first.State);
        Assert.Equal(1, source.Calls);
        var second = await orch.CheckAsync(UpdateTrigger.Manual);
        Assert.Equal(UpdateState.Failed, second.State);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task Cancelling_a_joiner_does_not_cancel_the_shared_run()
    {
        var gate = new TaskCompletionSource();
        var source = new Source
        {
            Result = new ReleaseQueryResult(ReleaseQueryStatus.Available, Info(), null)
        };
        var downloader = new Downloader
        {
            Impl = async (_, ct) =>
            {
                await gate.Task.WaitAsync(ct).ConfigureAwait(false);
                return new DownloadedUpdate(Path.GetTempFileName(), 1, Digest);
            }
        };
        var installer = new Installer();
        var orch = Orch(source, downloader, installer, new Prompter());
        var first = orch.CheckAsync(UpdateTrigger.Startup);
        using var joinerCts = new CancellationTokenSource();
        var second = orch.CheckAsync(UpdateTrigger.Manual, joinerCts.Token);
        joinerCts.Cancel();
        gate.SetResult();
        // The joiner's token is not linked into the shared run: cancelling it
        // neither faults nor cancels the operation for everyone else.
        var joined = await second;
        var result = await first;
        Assert.Same(result, joined);
        Assert.Equal(UpdateState.Installing, joined.State);
        Assert.Equal(1, downloader.Calls);
        Assert.Equal(1, installer.Calls);
    }

    [Fact]
    public async Task Up_to_date_reports_neutral_state()
    {
        var states = new List<UpdateStateInfo>();
        var orch = Orch(new Source(), new Downloader(), new Installer(), new Prompter(), states: states);
        var result = await orch.CheckAsync(UpdateTrigger.Manual);
        Assert.Equal(UpdateState.NoUpdate, result.State);
        Assert.Equal("No updates available", result.Message);
        Assert.Contains(states, s => s.State == UpdateState.Checking);
    }
}
