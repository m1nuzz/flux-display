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

    public App()
    {
        InitializeComponent();
        RequestedTheme = ApplicationTheme.Dark;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var cmd = Environment.GetCommandLineArgs();
        if (cmd.Any(a => a == "--smoke-dump" || a == "--smoke"))
        {
            HandleSmokeDumpAndExit(cmd);
            return;
        }

        var instance = AppInstance.FindOrRegisterForKey("flux-display-main");
        if (!instance.IsCurrent)
        {
            var redirectArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            instance.RedirectActivationToAsync(redirectArgs).AsTask().GetAwaiter().GetResult();
            Current.Exit();
            return;
        }

        instance.Activated += OnRedirectedActivation;
        Services = AppServices.Initialize();
        var window = new MainWindow();
        MainWindow = window;
        window.SystemBackdrop = new MicaBackdrop();
        window.ExtendsContentIntoTitleBar = true;
        window.Activate();
        AsyncHelper.FireAndForget(async () =>
        {
            await window.InitializeAsync().ConfigureAwait(true);
            TrimWorkingSet();
        });
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
        try
        {
            _ = SetProcessWorkingSetSize(GetCurrentProcess(), nint.MaxValue, nint.MaxValue);
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
