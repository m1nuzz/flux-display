using System.Runtime.InteropServices;
using FluxDisplay.App.Helpers;
using FluxDisplay.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.AppLifecycle;

namespace FluxDisplay.App;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }
    public static AppServices Services { get; private set; } = null!;

    // True once ExitApplication starts. MainWindow.OnClosed must not veto the
    // close (hide-to-tray) while the process is really going away.
    internal static bool IsExiting { get; private set; }

    // The only graceful exit path (tray Exit, update install). Tears the tray
    // icon down on the UI thread first — see TrayService.Shutdown — because
    // tearing native windows down during CLR shutdown crash-dialogs the exit.
    // Never throws; falls back to Environment.Exit.
    public static void ExitApplication()
    {
        try
        {
            var queue = MainWindow?.DispatcherQueue;
            if (queue is not null && !queue.HasThreadAccess)
            {
                queue.TryEnqueue(ExitApplication);
                return;
            }

            IsExiting = true;
            try
            {
                Services.Tray.Shutdown();
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "App.ExitShutdown");
            }

            Helpers.AppLog.Info("App.ExitApplication exiting");
            Current.Exit();
        }
        catch (Exception ex)
        {
            try
            {
                AppLog.Error(ex, "App.Exit");
            }
            catch
            {
            }

            Environment.Exit(0);
        }
    }

    public App()
    {
        AppLog.RunHeader(Environment.GetCommandLineArgs());
        AppLog.Info("App ctor");
        AppLog.LogDumpHint();
        AttachCrashLogging();
        InitializeComponent();
        RequestedTheme = ApplicationTheme.Dark;
    }

    // Diagnosis for native "Unknown Hard Error" crashes: managed handlers will
    // not fire for a true native fault, but the log still shows the last
    // managed scope before death. FirstChanceException is filtered to our own
    // frames to avoid flooding the log with handled framework exceptions.
    // True only for the known tray-menu host death: Win32 ERROR_INVALID_WINDOW_HANDLE
    // surfacing from H.NotifyIcon's ShowContextMenuInSecondWindowMode. Anything
    // else still crashes (loudly, by design).
    private static bool IsDeadMenuHostFailure(Exception ex)
    {
        try
        {
            if (ex is not System.Runtime.InteropServices.COMException com)
            {
                return false;
            }

            if (com.HResult != unchecked((int)0x80070578))
            {
                return false;
            }

            var stack = ex.StackTrace ?? string.Empty;
            return stack.Contains("ShowContextMenuInSecondWindowMode", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private void AttachCrashLogging()
    {
        try
        {
            UnhandledException += (_, e) =>
            {
                try
                {
                    AppLog.Error(e.Exception, "App.UnhandledException");
                    // TEMPORARY safety net (tray-menu Invalid-handle crash): swallow
                    // ONLY this exact failure — a dead SecondWindow host handle in
                    // H.NotifyIcon's own right-click path. The click is lost but the
                    // process survives; the liveness probe in TrayService records it.
                    // Remove once the host-window lifetime is understood/fixed.
                    if (IsDeadMenuHostFailure(e.Exception))
                    {
                        AppLog.Error("Swallowed dead menu-host MoveAndResize (temporary net)");
                        e.Handled = true;
                    }
                }
                catch
                {
                }
            };
        }
        catch
        {
        }

        try
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                try
                {
                    if (e.ExceptionObject is Exception ex)
                    {
                        AppLog.Error(ex, "AppDomain.UnhandledException");
                    }
                    else
                    {
                        AppLog.Error($"AppDomain.UnhandledException non-Exception: {e.ExceptionObject}");
                    }
                }
                catch
                {
                }
            };
        }
        catch
        {
        }

        try
        {
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                try
                {
                    AppLog.Error(e.Exception, "TaskScheduler.UnobservedTaskException");
                    e.SetObserved();
                }
                catch
                {
                }
            };
        }
        catch
        {
        }

        try
        {
            AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
            {
                try
                {
                    var stack = e.Exception.StackTrace ?? string.Empty;
                    if (stack.Contains("FluxDisplay", StringComparison.Ordinal))
                    {
                        AppLog.Error(e.Exception, "FirstChance");
                    }
                }
                catch
                {
                }
            };
        }
        catch
        {
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var cmd = Environment.GetCommandLineArgs();
        if (cmd.Any(a => a == "--smoke-dump" || a == "--smoke"))
        {
            HandleSmokeDumpAndExit(cmd);
            return;
        }

        var smokeUi = cmd.Any(a => a == "--smoke-ui");
        var smokeLifecycle = cmd.Any(a => a == "--smoke-lifecycle");
        var smokeHide = cmd.Any(a => a == "--smoke-hide");
        var smokeClock = cmd.Any(a => a == "--smoke-clock");
        var smokeMenu = cmd.Any(a => a == "--smoke-menu");
        if (!smokeUi && !smokeLifecycle && !smokeHide && !smokeClock && !smokeMenu)
        {
            var instance = AppInstance.FindOrRegisterForKey("flux-display-main");
            if (!instance.IsCurrent)
            {
                var redirectArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
                instance.RedirectActivationToAsync(redirectArgs).AsTask().GetAwaiter().GetResult();
                Current.Exit();
                return;
            }

            instance.Activated += OnRedirectedActivation;
        }

        Services = AppServices.Initialize();
        var window = new MainWindow();
        MainWindow = window;
        window.SystemBackdrop = new MicaBackdrop();
        window.ExtendsContentIntoTitleBar = true;
        // Initialize the tray early so its menu host exists, then warm the
        // menu up while everything is still invisible (see WarmUpMenuHost).
        // MainViewModel.InitializeAsync calls Tray.Initialize() again later,
        // which early-returns as already initialized.
        Services.Tray.Initialize();
        Services.Tray.WarmUpMenuHost();
        window.Activate();
        AsyncHelper.FireAndForget(async () =>
        {
            await window.InitializeAsync().ConfigureAwait(true);
            if (smokeUi)
            {
                var outDir = cmd.SkipWhile(a => a != "--smoke-ui").Skip(1).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(outDir) || outDir.StartsWith('-'))
                {
                    outDir = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "ui-smoke");
                }

                await SmokeUiRunner.RunAsync(window, outDir).ConfigureAwait(true);
                Current.Exit();
                return;
            }

            if (smokeLifecycle)
            {
                var lifecycleDir = cmd.SkipWhile(a => a != "--smoke-lifecycle").Skip(1).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(lifecycleDir) || lifecycleDir.StartsWith('-'))
                {
                    lifecycleDir = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "ui-smoke");
                }

                // Ends the process itself via App.ExitApplication.
                await SmokeUiRunner.RunLifecycleAsync(window, lifecycleDir).ConfigureAwait(true);
                return;
            }

            if (smokeHide)
            {
                var hideDir = cmd.SkipWhile(a => a != "--smoke-hide").Skip(1).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(hideDir) || hideDir.StartsWith('-'))
                {
                    hideDir = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "ui-smoke");
                }

                // Never returns (killed externally); no update checks here.
                await SmokeUiRunner.RunHideAndStayAsync(window, hideDir).ConfigureAwait(true);
                return;
            }

            if (smokeClock)
            {
                var clockDir = cmd.SkipWhile(a => a != "--smoke-clock").Skip(1).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(clockDir) || clockDir.StartsWith('-'))
                {
                    clockDir = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "ui-smoke");
                }

                // Ends the process itself via App.ExitApplication.
                await SmokeUiRunner.RunClockProbeAsync(window, clockDir).ConfigureAwait(true);
                return;
            }

            if (smokeMenu)
            {
                var menuDir = cmd.SkipWhile(a => a != "--smoke-menu").Skip(1).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(menuDir) || menuDir.StartsWith('-'))
                {
                    menuDir = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "ui-smoke");
                }

                // Ends the process itself via App.ExitApplication.
                await SmokeUiRunner.RunMenuProbeAsync(window, menuDir).ConfigureAwait(true);
                return;
            }

            TrimWorkingSet();
            StartUpdateChecks(window);
        });
    }

    // Update wiring (skipped in smoke modes): seed the mode cache, run the
    // delayed startup check plus periodic checks, cancel everything when the
    // main window closes. The matrix itself lives in UpdateOrchestrator.
    private static void StartUpdateChecks(Window window)
    {
        try
        {
            // This app hides to tray instead of closing (MainWindow.OnClosed
            // vetoes the close), so window.Closed alone is not a reliable
            // shutdown signal — ProcessExit covers Application.Exit,
            // Environment.Exit and external kills. Cancel() itself is sync.
            System.AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try
                {
                    Services.Updates.Shutdown();
                }
                catch
                {
                }
            };
            window.Closed += (_, _) =>
            {
                try
                {
                    Services.Updates.Shutdown();
                }
                catch
                {
                }
            };
            AsyncHelper.FireAndForget(async () =>
            {
                try
                {
                    var settings = await Services.SettingsStore.LoadAsync().ConfigureAwait(false);
                    Services.CachedUpdateMode = settings.UpdateMode;
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, "Update.ModeCache");
                }

                Services.Updates.StartPeriodicChecks();
                await Services.Updates.RunStartupCheckAsync().ConfigureAwait(false);
            });
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Update.Startup");
        }
    }

    private void HandleSmokeDumpAndExit(string[] args)
    {
        try
        {
            var logPath = args.SkipWhile(a => a != "--smoke-dump").Skip(1).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(logPath) || logPath.StartsWith("-"))
                logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "flux-smoke.log");
            // Also write to artifacts for CI
            var artifactsLog = System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "artifacts", "smoke.log");
            try { System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(artifactsLog)!); } catch {}

            Services = AppServices.Initialize();
            var svc = Services.Display;
            // Raw P/Invoke check for comparison (independent of DisplayService)
            var rawAdapters = new System.Collections.Generic.List<string>();
            try
            {
                uint raIdx = 0;
                while (true)
                {
                    var dd = new FluxDisplay.App.Native.DISPLAY_DEVICEW(); dd.cb = (uint)System.Runtime.InteropServices.Marshal.SizeOf<FluxDisplay.App.Native.DISPLAY_DEVICEW>();
                    bool ok = RawEnumDisplayDevicesW(null, raIdx, ref dd, 0);
                    if (!ok) break;
                    rawAdapters.Add($"{dd.DeviceName} Flags={dd.StateFlags}");
                    raIdx++; if (raIdx > 20) break;
                }
            } catch (Exception ex) { rawAdapters.Add("raw error: " + ex.Message); }

            // Support both Task and IAsyncOperation signatures (with/without CancellationToken)
            var displaysTask = svc.GetDisplaysAsync();
            displaysTask.Wait(5000);
            var displays = displaysTask.Result;
            var lines = new System.Collections.Generic.List<string>
            {
                $"Smoke dump {DateTime.Now:O}",
                $"Args: {string.Join(" ", args)}",
                $"Raw adapters ({rawAdapters.Count}): {string.Join(", ", rawAdapters)}",
                $"Count={displays.Count}"
            };
            foreach (var d in displays)
            {
                lines.Add($"DisplayName={d.DisplayName} DevicePath={d.DevicePath} Friendly={d.FriendlyName} Adapter={d.AdapterName} Bounds={d.Bounds.X},{d.Bounds.Y},{d.Bounds.Width},{d.Bounds.Height} Mode={d.CurrentMode.Width}x{d.CurrentMode.Height}@{d.CurrentMode.RefreshRate} Primary={d.IsPrimary} Dpi={d.CurrentDpi}");
                try
                {
                    var modesTask = svc.GetSupportedModesAsync(d.DisplayName);
                    modesTask.Wait(5000);
                    var modes = modesTask.Result;
                    lines.Add($"  Modes={modes.Count} first={(modes.Count>0?modes[0].ToString():"")}");
                }
                catch (Exception ex) { lines.Add($"  Modes error: {ex.Message}"); }
            }

            // Check fallback detection
            if (displays.Count == 0)
                lines.Add("ERROR: No displays enumerated — fallback should have provided at least 1");

            var text = string.Join(Environment.NewLine, lines);
            System.IO.File.WriteAllText(logPath, text);
            try { System.IO.File.WriteAllText(artifactsLog, text); } catch {}
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "flux-smoke.log"), text + Environment.NewLine);
        }
        catch (Exception ex)
        {
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "flux-smoke-error.log"), ex.ToString()); } catch {}
        }
        finally
        {
            Current.Exit();
        }
    }

    private static void OnRedirectedActivation(object? sender, AppActivationArguments e)
    {
        if (MainWindow is MainWindow window)
        {
            window.DispatcherQueue.TryEnqueue(window.ShowFromTray);
        }
    }

    private static void TrimWorkingSet()
    {
        // (SIZE_T)-1 for both min and max asks the OS to trim the working set
        // (EmptyWorkingSet equivalent). nint.MaxValue here is NOT -1: it requests
        // an 8 EB working set, which causes "Unknown Hard Error" on minimize.
        try
        {
            _ = SetProcessWorkingSetSize(GetCurrentProcess(), new nint(-1), new nint(-1));
        }
        catch
        {
        }
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(nint hProcess, nint dwMinimumWorkingSetSize, nint dwMaximumWorkingSetSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplayDevicesW")]
    private static extern bool RawEnumDisplayDevicesW(string? lpDevice, uint iDevNum, ref FluxDisplay.App.Native.DISPLAY_DEVICEW lpDisplayDevice, uint dwFlags);
}
