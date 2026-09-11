using System.Net;
using Xunit;
using FluxDisplay.Core.Updates;

namespace FluxDisplay.Core.Tests;

// Strict asset selection and HTTP outcome mapping. All transport is faked.
public sealed class GitHubReleaseCheckerTests
{
    private const string Digest = "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private static string ReleaseJson(string tag, string assets, string body = "notes") =>
        "{\"tag_name\":\"" + tag + "\"," +
        "\"html_url\":\"https://github.com/m1nuzz/flux-display/releases/tag/" + tag + "\"," +
        "\"body\":\"" + body + "\"," +
        "\"assets\":[" + assets + "]}";

    private static string Asset(string name, string digest = Digest) =>
        "{\"name\":\"" + name + "\"," +
        "\"browser_download_url\":\"https://github.com/m1nuzz/flux-display/releases/download/v9.9.9/" + name + "\"," +
        "\"digest\":\"" + digest + "\"}";

    private static GitHubReleaseChecker Checker(FakeHandler handler) =>
        new(new HttpClient(handler), headerTimeout: TimeSpan.FromSeconds(5));

    [Fact]
    public async Task Single_matching_setup_is_selected()
    {
        var json = ReleaseJson("v1.2.0", Asset("FluxDisplay-1.2.0-setup.exe"));
        var result = await Checker(new FakeHandler(HttpStatusCode.OK, json))
            .QueryAsync(new Version(1, 1, 0, 0), CancellationToken.None);
        Assert.Equal(ReleaseQueryStatus.Available, result.Status);
        Assert.NotNull(result.Update);
        Assert.Equal(new Version(1, 2, 0, 0), UpdateVersion.Normalize(result.Update!.Version));
        Assert.Equal("FluxDisplay-1.2.0-setup.exe", result.Update.AssetName);
        Assert.Equal(Digest.Substring("sha256:".Length), result.Update.Digest);
        Assert.StartsWith("https://github.com/", result.Update.SetupUrl.ToString());
    }

    [Fact]
    public async Task Portable_zip_and_foreign_exe_are_ignored()
    {
        var json = ReleaseJson("v1.2.0",
            "{\"name\":\"FluxDisplay-win-x64-portable.zip\",\"browser_download_url\":\"https://example.com/p.zip\"}," +
            "{\"name\":\"helper.exe\",\"browser_download_url\":\"https://example.com/h.exe\"}," +
            Asset("FluxDisplay-1.2.0-setup.exe"));
        var result = await Checker(new FakeHandler(HttpStatusCode.OK, json))
            .QueryAsync(new Version(1, 0, 0, 0), CancellationToken.None);
        Assert.Equal(ReleaseQueryStatus.Available, result.Status);
        Assert.Equal("FluxDisplay-1.2.0-setup.exe", result.Update!.AssetName);
    }

    [Fact]
    public async Task Zero_matching_assets_is_controlled_failure()
    {
        var json = ReleaseJson("v1.2.0",
            "{\"name\":\"FluxDisplay-win-x64-portable.zip\",\"browser_download_url\":\"https://example.com/p.zip\"}");
        var result = await Checker(new FakeHandler(HttpStatusCode.OK, json))
            .QueryAsync(new Version(1, 0, 0, 0), CancellationToken.None);
        Assert.Equal(ReleaseQueryStatus.Failed, result.Status);
        Assert.Null(result.Update);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public async Task Two_matching_assets_is_controlled_failure()
    {
        var json = ReleaseJson("v1.2.0",
            Asset("FluxDisplay-1.2.0-setup.exe") + "," + Asset("fluxdisplay-1.2.0-setup.exe"));
        var result = await Checker(new FakeHandler(HttpStatusCode.OK, json))
            .QueryAsync(new Version(1, 0, 0, 0), CancellationToken.None);
        Assert.Equal(ReleaseQueryStatus.Failed, result.Status);
        Assert.Null(result.Update);
    }

    [Fact]
    public async Task Asset_version_mismatch_is_controlled_failure()
    {
        var json = ReleaseJson("v1.2.0", Asset("FluxDisplay-1.1.0-setup.exe"));
        var result = await Checker(new FakeHandler(HttpStatusCode.OK, json))
            .QueryAsync(new Version(1, 0, 0, 0), CancellationToken.None);
        Assert.Equal(ReleaseQueryStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Equal_versions_mean_no_update()
    {
        var json = ReleaseJson("v1.2.0", Asset("FluxDisplay-1.2.0-setup.exe"));
        var result = await Checker(new FakeHandler(HttpStatusCode.OK, json))
            .QueryAsync(new Version(1, 2, 0, 0), CancellationToken.None);
        Assert.Equal(ReleaseQueryStatus.UpToDate, result.Status);
        Assert.Null(result.Update);
    }

    [Fact]
    public async Task Newer_installed_version_is_downgrade_protection()
    {
        var json = ReleaseJson("v1.2.0", Asset("FluxDisplay-1.2.0-setup.exe"));
        var result = await Checker(new FakeHandler(HttpStatusCode.OK, json))
            .QueryAsync(new Version(2, 0, 0, 0), CancellationToken.None);
        Assert.Equal(ReleaseQueryStatus.UpToDate, result.Status);
        Assert.Null(result.Update);
    }

    [Fact]
    public async Task Missing_release_is_neutral_not_error()
    {
        var handler = new FakeHandler(HttpStatusCode.NotFound);
        var result = await Checker(handler).QueryAsync(new Version(1, 0, 0, 0), CancellationToken.None);
        Assert.Equal(ReleaseQueryStatus.NoPublishedRelease, result.Status);
        Assert.Null(result.Update);
    }

    [Fact]
    public async Task Exhausted_rate_limit_is_distinct_status()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.Add("X-RateLimit-Remaining", "0");
        response.Headers.Add("X-RateLimit-Reset", "9999999999");
        var result = await Checker(new FakeHandler(response))
            .QueryAsync(new Version(1, 0, 0, 0), CancellationToken.None);
        Assert.Equal(ReleaseQueryStatus.RateLimited, result.Status);
    }

    [Fact]
    public async Task Other_forbidden_is_plain_failure()
    {
        var result = await Checker(new FakeHandler(HttpStatusCode.Forbidden))
            .QueryAsync(new Version(1, 0, 0, 0), CancellationToken.None);
        Assert.Equal(ReleaseQueryStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Header_timeout_is_controlled_failure()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var checker = new GitHubReleaseChecker(new HttpClient(handler), headerTimeout: TimeSpan.FromMilliseconds(100));
        var result = await checker.QueryAsync(new Version(1, 0, 0, 0), CancellationToken.None);
        Assert.Equal(ReleaseQueryStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Malformed_digest_is_controlled_failure()
    {
        var json = ReleaseJson("v1.2.0", Asset("FluxDisplay-1.2.0-setup.exe", "md5:abc"));
        var result = await Checker(new FakeHandler(HttpStatusCode.OK, json))
            .QueryAsync(new Version(1, 0, 0, 0), CancellationToken.None);
        Assert.Equal(ReleaseQueryStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Missing_digest_is_allowed_with_null()
    {
        var json = ReleaseJson("v1.2.0",
            "{\"name\":\"FluxDisplay-1.2.0-setup.exe\",\"browser_download_url\":\"https://example.com/s.exe\"}");
        var result = await Checker(new FakeHandler(HttpStatusCode.OK, json))
            .QueryAsync(new Version(1, 0, 0, 0), CancellationToken.None);
        Assert.Equal(ReleaseQueryStatus.Available, result.Status);
        Assert.Null(result.Update!.Digest);
    }

    [Fact]
    public async Task Old_asset_name_with_equal_version_reports_no_update_without_scan()
    {
        // v1.0.0 shipped FluxDisplay-win-x64-setup.exe. A new client must call
        // it up to date on version equality alone, never failing on the name.
        var json = ReleaseJson("v1.0.0",
            "{\"name\":\"FluxDisplay-win-x64-setup.exe\",\"browser_download_url\":\"https://example.com/s.exe\"}");
        var result = await Checker(new FakeHandler(HttpStatusCode.OK, json))
            .QueryAsync(new Version(1, 0, 0, 0), CancellationToken.None);
        Assert.Equal(ReleaseQueryStatus.UpToDate, result.Status);
        Assert.Null(result.Update);
    }

    [Fact]
    public async Task User_cancellation_propagates()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Checker(handler).QueryAsync(new Version(1, 0, 0, 0), cts.Token));
    }
}
