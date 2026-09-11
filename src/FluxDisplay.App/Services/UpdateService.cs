using System.Net.Sockets;
using FluxDisplay.App.Helpers;
using FluxDisplay.Core.Updates;

namespace FluxDisplay.App.Services;

// App-side release metadata: current assembly version, installed-copy
// detection and the shared HTTP transport. Parsing, policy and comparison
// live in Core (GitHubReleaseChecker) and are unit-tested there.
public sealed class UpdateService : IUpdateSource
{
    private readonly HttpClient _http;
    private readonly GitHubReleaseChecker _checker;

    public UpdateService(HttpClient http, Version? currentVersionOverride = null)
    {
        _http = http;
        CurrentVersion = currentVersionOverride
            ?? typeof(UpdateService).Assembly.GetName().Version
            ?? new Version(1, 0, 0, 0);
        _checker = new GitHubReleaseChecker(
            _http,
            m => AppLog.Info(m),
            headerTimeout: TimeSpan.FromSeconds(30));
    }

    public Version CurrentVersion { get; }

    // Installed copies live under %LocalAppData%\Programs\FluxDisplay (the Inno
    // default). Portable copies run from anywhere else and cannot be replaced
    // in place, so they only ever get the notify flow.
    public bool IsInstalledCopy
    {
        get
        {
            try
            {
                var installDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs",
                    "FluxDisplay");
                return UpdatePaths.IsInstalledCopy(AppContext.BaseDirectory, installDir);
            }
            catch
            {
                return false;
            }
        }
    }

    public Task<ReleaseQueryResult> QueryAsync(Version current, CancellationToken ct) =>
        _checker.QueryAsync(current, ct);

    public static HttpClient CreateSharedHttpClient(Version current)
    {
        var handler = new SocketsHttpHandler
        {
            // Long-lived process: don't pin stale DNS forever.
            PooledConnectionLifetime = TimeSpan.FromMinutes(15)
        };
        var http = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan // timeouts are per-operation, not global
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"FluxDisplay-Updater/{UpdateVersion.FormatShort(current)}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }
}
