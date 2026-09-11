using System.Diagnostics;
using FluxDisplay.App.Helpers;
using FluxDisplay.Core.Updates;
using Microsoft.UI.Xaml;

namespace FluxDisplay.App.Services;

// Launches the verified setup and hands the process over. Inno (per-user,
// CloseApplications=yes) replaces the files without prompts; its postinstall
// Run entry starts the new version exactly once, so the app only has to exit
// cleanly and unlock its files.
public sealed class UpdateInstaller : IUpdateInstaller
{
    public Task InstallAsync(DownloadedUpdate downloaded, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(downloaded);

        // Order matters: the app must never exit BEFORE the installer process
        // exists. On start failure we return (throw) with the app still alive.
        // Per-user install => no UAC prompt is expected; if elevation were
        // ever required, its cancel path also lands here with no exit.
        try
        {
            var logPath = Path.Combine(Path.GetTempPath(), "FluxDisplay-setup.log");
            var psi = new ProcessStartInfo(downloaded.Path,
                $"/VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART /CLOSEAPPLICATIONS /LOG=\"{logPath}\"")
            {
                UseShellExecute = true
            };
            _ = Process.Start(psi);
            AppLog.Info($"Update installer started for {downloaded.BytesReceived} bytes.");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Update.Install");
            throw new UpdateDownloadException("Update installer could not be started.", ex);
        }

        // Exit on the UI thread; the installer (CloseApplications) also closes
        // us if we are still alive when it reaches our files. Either way the
        // process is gone, so there is nothing to await afterwards.
        // Watchdog: Application.Exit runs window teardown, which this app's
        // hide-to-tray handlers must not veto into a hang — force-quit if we
        // are still here after 5 seconds so a silent update never stalls.
        try
        {
            _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ =>
            {
                try
                {
                    Environment.Exit(0);
                }
                catch
                {
                }
            });

            var queue = App.MainWindow?.DispatcherQueue;
            if (queue is not null && !queue.HasThreadAccess)
            {
                queue.TryEnqueue(Exit);
            }
            else
            {
                Exit();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Update.Exit");
        }

        return Task.CompletedTask;
    }

    private static void Exit()
    {
        try
        {
            Application.Current.Exit();
        }
        catch
        {
            Environment.Exit(0);
        }
    }
}
