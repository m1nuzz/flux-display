using System.Diagnostics;
using FluxDisplay.App.Helpers;
using FluxDisplay.App.Models;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace FluxDisplay.App.Services;

internal static class SmokeUiRunner
{
    public static async Task RunAsync(MainWindow window, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var log = new List<string> { $"Smoke UI {DateTime.Now:O}", $"Out={outputDir}" };
        try
        {
            SizeWindow(window, 1280, 800);
            window.Activate();
            await Task.Delay(600);

            var services = App.Services;
            var presetPath = services.Presets.StoragePath;
            string? backup = File.Exists(presetPath) ? await File.ReadAllTextAsync(presetPath) : null;
            try
            {
                window.NavigateTo("presets");
                await window.ViewModel.Presets.ReloadAsync();
                await WaitAsync();
                await Shot(window, outputDir, "01-presets-empty.png", log);

                await SeedPresetsAsync(services);
                await window.ViewModel.Presets.ReloadAsync();
                await WaitAsync();
                await Shot(window, outputDir, "02-presets-filled.png", log);

                window.NavigateTo("settings");
                await window.ViewModel.SettingsViewModel.LoadAsync();
                await WaitAsync();
                await Shot(window, outputDir, "03-settings.png", log);

                window.NavigateTo("create");
                await window.ViewModel.Create.ResetAsync();
                await WaitAsync();
                await Shot(window, outputDir, "04-create-step0-monitors.png", log);

                var monitor = window.ViewModel.Create.Monitors.FirstOrDefault();
                if (monitor is not null)
                {
                    window.ViewModel.Create.SelectMonitor(monitor);
                    await window.ViewModel.Create.NextAsync();
                    await WaitAsync();
                    await Shot(window, outputDir, "05-create-step1-mode.png", log);

                    await window.ViewModel.Create.NextAsync();
                    await WaitAsync();
                    await Shot(window, outputDir, "06-create-step2-name.png", log);
                }
                else
                {
                    log.Add("create wizard: no monitors");
                }

                if (window.Content is FrameworkElement root && root.XamlRoot is not null)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Apply preset",
                        Content = new TextBlock { Text = "Apply 'Gaming 240Hz'?", TextWrapping = TextWrapping.Wrap },
                        PrimaryButtonText = "Apply",
                        CloseButtonText = "Cancel",
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = root.XamlRoot
                    };
                    var shown = dialog.ShowAsync().AsTask();
                    await Task.Delay(500);
                    await Shot(window, outputDir, "07-dialog-confirm.png", log);
                    dialog.Hide();
                    try { await shown; } catch { }
                }

                var displays = await services.Display.GetDisplaysAsync();
                if (displays.Count > 0)
                {
                    var identify = services.Identifier.IdentifyAsync(displays, 4);
                    await Task.Delay(900);
                    await WindowCapture.CaptureProcessWindowsAsync(outputDir, "08-identify");
                    log.Add($"identify windows captured displays={displays.Count}");
                    try { await identify; } catch { }
                }

                window.NavigateTo("presets");
                await WaitAsync();
                if (services.Tray is TrayService tray && tray.TryShowContextFlyout())
                {
                    await Task.Delay(700);
                    await WindowCapture.CaptureProcessWindowsAsync(outputDir, "09-tray");
                    await Shot(window, outputDir, "09-tray-main.png", log);
                    log.Add("tray flyout shown");
                }
                else
                {
                    log.Add("tray flyout not shown");
                }
            }
            finally
            {
                RestorePresets(presetPath, backup);
            }
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            await File.WriteAllTextAsync(Path.Combine(outputDir, "error.txt"), ex.ToString());
        }

        var pngs = Directory.GetFiles(outputDir, "*.png").Select(Path.GetFileName).OrderBy(x => x);
        log.Add($"png={pngs.Count()}");
        log.AddRange(pngs.Select(name => $" - {name}"));
        await File.WriteAllTextAsync(Path.Combine(outputDir, "index.txt"), string.Join(Environment.NewLine, log));
    }

    private static async Task Shot(MainWindow window, string dir, string name, List<string> log)
    {
        var path = Path.Combine(dir, name);
        await WindowCapture.CaptureMainAsync(window, path);
        log.Add(File.Exists(path) ? $"ok {name} {new FileInfo(path).Length}" : $"missing {name}");
    }

    private static Task WaitAsync() => Task.Delay(450);

    // Lifecycle smoke (--smoke-lifecycle): proves the hidden window produces
    // no compositor frames (the 0.1% GPU bug) and that the orderly exit path
    // terminates the process cleanly (the tray-Exit crash). Counts
    // CompositionTarget.Rendering callbacks per phase; ends with
    // App.ExitApplication, so a crash dialog or nonzero exit fails the run.
    public static async Task RunLifecycleAsync(MainWindow window, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var log = new List<string> { $"Smoke lifecycle {DateTime.Now:O}" };
        try
        {
            SizeWindow(window, 1280, 800);
            window.Activate();
            var hwnd = WindowNative.GetWindowHandle(window);
            var visible = await CountFramesAsync(TimeSpan.FromSeconds(5));
            var dwmVisible = SampleDwm(hwnd);
            log.Add($"rendering visible 5s: {visible}");
            await Task.Delay(TimeSpan.FromSeconds(5));
            var dwmVisible2 = SampleDwm(hwnd);
            log.Add($"dwm visible 5s: submitted={dwmVisible2.Submitted - dwmVisible.Submitted} " +
                $"displayed={dwmVisible2.Displayed - dwmVisible.Displayed} pixels={dwmVisible2.Pixels - dwmVisible.Pixels}");

            window.HideToTray();
            await Task.Delay(TimeSpan.FromSeconds(2));
            var dwmHidden = SampleDwm(hwnd);
            log.Add($"visible windows while hidden: {string.Join(" | ", ListVisibleWindows())}");
            // Hidden-idle budget (stolen from flux-launcher's idle smoke): a
            // hidden window must do no work. CPU time bounds timers/loops,
            // zero DWM submits bound presents. Generous enough for CI noise,
            // tight enough to catch a busy loop or a presenting popup.
            var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
            await Task.Delay(TimeSpan.FromSeconds(10));
            var cpuMs = (Process.GetCurrentProcess().TotalProcessorTime - cpuBefore).TotalMilliseconds;
            var dwmHiddenBudget = SampleDwm(hwnd);
            var submittedDelta = dwmHiddenBudget.Submitted - dwmHidden.Submitted;
            log.Add($"hidden 10s budget: cpu={cpuMs:0}ms (limit 1000), submitted+={submittedDelta} (limit 0)");
            if (cpuMs > 1000)
            {
                throw new InvalidOperationException($"Hidden idle CPU budget exceeded: {cpuMs:0} ms over 10 seconds.");
            }

            if (dwmHidden.Submitted >= 0 && submittedDelta != 0)
            {
                throw new InvalidOperationException($"Hidden window presented {submittedDelta} frames in 10 seconds.");
            }

            var hidden = await CountFramesAsync(TimeSpan.FromSeconds(10));
            var dwmHiddenMid = SampleDwm(hwnd);
            log.Add($"rendering hidden 10s: {hidden}");
            log.Add($"dwm hidden 10s: submitted={dwmHiddenMid.Submitted - dwmHidden.Submitted} " +
                $"displayed={dwmHiddenMid.Displayed - dwmHidden.Displayed} pixels={dwmHiddenMid.Pixels - dwmHidden.Pixels}");
            await Task.Delay(TimeSpan.FromSeconds(50));
            var dwmHidden2 = SampleDwm(hwnd);
            log.Add($"dwm hidden +50s: submitted={dwmHidden2.Submitted - dwmHiddenMid.Submitted} " +
                $"displayed={dwmHidden2.Displayed - dwmHiddenMid.Displayed} pixels={dwmHidden2.Pixels - dwmHiddenMid.Pixels}");

            window.ShowFromTray();
            await Task.Delay(3000);
            window.HideToTray();
            var hidden2 = await CountFramesAsync(TimeSpan.FromSeconds(5));
            log.Add($"rendering rehidden 5s: {hidden2}");
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
        }

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "lifecycle.txt"),
            string.Join(Environment.NewLine, log));
        App.ExitApplication();
    }

    // --smoke-hide: show once, hide to tray, then idle forever (killed
    // externally). Lets Task Manager settle so its per-process GPU column
    // can be read for the hidden-after-show state.
    public static async Task RunHideAndStayAsync(MainWindow window, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        SizeWindow(window, 1280, 800);
        window.Activate();
        await Task.Delay(TimeSpan.FromSeconds(3));
        window.HideToTray();
        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "hide-and-stay.txt"),
            $"hidden at {DateTime.Now:O}");
        await Task.Delay(Timeout.Infinite);
    }

    // --smoke-menu: the tray-menu-open-and-idle state nobody measured. Opens
    // the REAL internal SecondWindow menu via the library's public
    // ShowContextMenu, samples DWM presents per HWND (main, host, popups),
    // idles 30s with the menu open, samples again, closes it, samples once
    // more. Ends via App.ExitApplication.
    public static async Task RunMenuProbeAsync(MainWindow window, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var log = new List<string> { $"Smoke menu {DateTime.Now:O}" };
        try
        {
            SizeWindow(window, 1280, 800);
            window.Activate();
            await Task.Delay(1000);
            window.HideToTray();
            await Task.Delay(1000);
            log.Add("windows hidden: " + string.Join(" | ", ListVisibleWindows()));

            OpenTrayMenu();
            await Task.Delay(200);
            log.Add("windows menu-open: " + string.Join(" | ", ListVisibleWindows()));
            DirectShowFlyout();
            await Task.Delay(1000);
            log.Add("windows menu-forced: " + string.Join(" | ", ListVisibleWindows()));
            var open = SampleAll();
            await Task.Delay(TimeSpan.FromSeconds(10));
            var openForced = SampleAll();
            log.Add("menu-forced-open 10s deltas:");
            log.AddRange(FormatDeltas(open, openForced));
            for (var i = 0; i < 10; i++)
            {
                await Task.Delay(500);
                var snap = SampleAll();
                log.Add($"menu t+{(i + 1) * 500}ms: " + string.Join(" | ", snap.Select(kv =>
                    $"{kv.Key}=s{kv.Value.Submitted - open.GetValueOrDefault(kv.Key).Submitted}")));
            }

            await Task.Delay(TimeSpan.FromSeconds(25));
            var open2 = SampleAll();
            log.Add("menu-open 30s deltas:");
            log.AddRange(FormatDeltas(open, open2));

            CloseTrayMenu();
            await Task.Delay(3000);
            log.Add("windows after-close: " + string.Join(" | ", ListVisibleWindows()));
            var closed = SampleAll();
            await Task.Delay(TimeSpan.FromSeconds(10));
            var closed2 = SampleAll();
            log.Add("menu-closed 10s deltas:");
            log.AddRange(FormatDeltas(closed, closed2));
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
        }

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "menu.txt"),
            string.Join(Environment.NewLine, log));
        App.ExitApplication();
    }

    private static Dictionary<string, (long Submitted, long Displayed, long Pixels)> SampleAll()
    {
        var result = new Dictionary<string, (long, long, long)>();
        foreach (var entry in ListVisibleWindows())
        {
            var parts = entry.Split('|');
            if (parts.Length < 2)
            {
                continue;
            }

            if (!long.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber, null, out var handle))
            {
                continue;
            }

            result[entry] = SampleDwm(new IntPtr(handle));
        }

        return result;
    }

    private static IEnumerable<string> FormatDeltas(
        Dictionary<string, (long Submitted, long Displayed, long Pixels)> before,
        Dictionary<string, (long Submitted, long Displayed, long Pixels)> after)
    {
        foreach (var kv in after)
        {
            if (!before.TryGetValue(kv.Key, out var b))
            {
                yield return $"{kv.Key}: NEW submitted={kv.Value.Submitted}";
                continue;
            }

            yield return $"{kv.Key}: submitted+={kv.Value.Submitted - b.Submitted} " +
                $"displayed+={kv.Value.Displayed - b.Displayed} pixels+={kv.Value.Pixels - b.Pixels}";
        }
    }

    private static object TaskbarIcon()
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var tray = (App.Services.Tray as TrayService)!;
        return typeof(TrayService).GetField("_taskbarIcon", flags)!.GetValue(tray)!;
    }

    private static void OpenTrayMenu()
    {
        var taskbarIcon = TaskbarIcon();
        GetCursorPos(out var pt);
        // Public API: ShowContextMenu(System.Drawing.Point).
        typeof(H.NotifyIcon.TaskbarIcon).GetMethod("ShowContextMenu")!
            .Invoke(taskbarIcon, [new System.Drawing.Point(pt.X, pt.Y)]);
    }

    // Bypasses activation (denied to background processes): opens the internal
    // flyout directly on its host so an actually-OPEN menu can be measured.
    private static void DirectShowFlyout()
    {
        try
        {
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var t = typeof(H.NotifyIcon.TaskbarIcon);
            var flyout = t.GetProperty("ContextMenuFlyout", flags)!.GetValue(TaskbarIcon()) as MenuFlyout;
            var hostObj = t.GetProperty("ContextMenuWindow", flags)!.GetValue(TaskbarIcon());
            var content = (hostObj as Window)?.Content as FrameworkElement;
            if (flyout is null || content is null)
            {
                return;
            }

            flyout.Placement = FlyoutPlacementMode.Full;
            flyout.ShowAt(content, new FlyoutShowOptions { ShowMode = FlyoutShowMode.Transient });
        }
        catch (Exception ex)
        {
            Helpers.AppLog.Error(ex, "smoke-direct-show");
        }
    }

    private static void CloseTrayMenu()
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var flyout = typeof(H.NotifyIcon.TaskbarIcon)
            .GetProperty("ContextMenuFlyout", flags)!
            .GetValue(TaskbarIcon()) as MenuFlyout;
        flyout?.Hide();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }
    public static async Task RunClockProbeAsync(MainWindow window, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var log = new List<string> { $"Smoke clock {DateTime.Now:O}" };
        try
        {
            SizeWindow(window, 1280, 800);
            window.Activate();
            log.Add($"full content shown 5s: {await CountFramesAsync(TimeSpan.FromSeconds(5))}");
            window.HideToTray();
            log.Add($"full content hidden 5s: {await CountFramesAsync(TimeSpan.FromSeconds(5))}");

            window.Content = new Grid();
            window.ShowFromTray();
            await Task.Delay(1000);
            log.Add($"blank shown 5s: {await CountFramesAsync(TimeSpan.FromSeconds(5))}");
            window.HideToTray();
            log.Add($"blank hidden 5s: {await CountFramesAsync(TimeSpan.FromSeconds(5))}");
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
        }

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "clock.txt"),
            string.Join(Environment.NewLine, log));
        App.ExitApplication();
    }

    private static async Task<int> CountFramesAsync(TimeSpan window)
    {
        var frames = 0;
        void Handler(object? sender, object e) => frames++;
        CompositionTarget.Rendering += Handler;
        try
        {
            await Task.Delay(window);
        }
        finally
        {
            CompositionTarget.Rendering -= Handler;
        }

        return frames;
    }

    // Real presents per DWM, not Rendering callbacks (those tick on vsync
    // even when nothing is presented). Zero deltas while hidden = the GPU
    // has no work from this window. Layout mirrors DWM_TIMING_INFO exactly.
    private static (long Submitted, long Displayed, long Pixels) SampleDwm(IntPtr hwnd)
    {
        var info = new DwmTimingInfo();
        info.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DwmTimingInfo>();
        if (NativeDwm.DwmGetCompositionTimingInfo(hwnd, ref info) != 0)
        {
            return (-1, -1, -1);
        }

        return ((long)info.cFrameSubmitted, (long)info.cFramesDisplayed, (long)info.cPixelsDrawn);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct UnsignedRatio
    {
        public uint uiNumerator;
        public uint uiDenominator;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct DwmTimingInfo
    {
        public uint cbSize;
        public UnsignedRatio rateRefresh;
        public ulong qpcRefreshPeriod;
        public UnsignedRatio rateCompose;
        public ulong qpcVBlank;
        public ulong cRefresh;
        public uint cDXRefresh;
        public ulong qpcCompose;
        public ulong cFrame;
        public uint cDXPresent;
        public ulong cRefreshFrame;
        public ulong cFrameSubmitted;
        public uint cDXPresentSubmitted;
        public ulong cFrameConfirmed;
        public uint cDXPresentConfirmed;
        public ulong cRefreshConfirmed;
        public uint cDXRefreshConfirmed;
        public ulong cFramesLate;
        public uint cFramesOutstanding;
        public ulong cFrameDisplayed;
        public ulong qpcFrameDisplayed;
        public ulong cRefreshFrameDisplayed;
        public ulong cFrameComplete;
        public ulong qpcFrameComplete;
        public ulong cFramePending;
        public ulong qpcFramePending;
        public ulong cFramesDisplayed;
        public ulong cFramesComplete;
        public ulong cFramesPending;
        public ulong cFramesAvailable;
        public ulong cFramesDropped;
        public ulong cFramesMissed;
        public ulong cRefreshNextDisplayed;
        public ulong cRefreshNextPresented;
        public ulong cRefreshesDisplayed;
        public ulong cRefreshesPresented;
        public ulong cRefreshStarted;
        public ulong cPixelsReceived;
        public ulong cPixelsDrawn;
        public ulong cBuffersEmpty;
    }

    private static class NativeDwm
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        public static extern int DwmGetCompositionTimingInfo(IntPtr hwnd, ref DwmTimingInfo info);
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);

    // Visible top-level windows of THIS process as "HWND|class|title" entries
    // (ghosts, menu hosts and popups are distinguishable by class).
    private static List<string> ListVisibleWindows()
    {
        var mine = (uint)Environment.ProcessId;
        var found = new List<string>();
        try
        {
            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd))
                {
                    return true;
                }

                GetWindowThreadProcessId(hWnd, out var pid);
                if (pid == mine)
                {
                    var title = new System.Text.StringBuilder(256);
                    GetWindowText(hWnd, title, 256);
                    var cls = new System.Text.StringBuilder(256);
                    GetClassName(hWnd, cls, 256);
                    found.Add($"{hWnd.ToInt64():X}|{cls}|{title}");
                }

                return true;
            }, IntPtr.Zero);
        }
        catch
        {
        }

        return found;
    }

    private static void SizeWindow(MainWindow window, int width, int height)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            var id = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(id);
            appWindow?.MoveAndResize(new Windows.Graphics.RectInt32(40, 40, width, height));
        }
        catch
        {
        }
    }

    private static async Task SeedPresetsAsync(AppServices services)
    {
        var displays = await services.Display.GetDisplaysAsync();
        var display = displays.FirstOrDefault();
        var path = display?.DevicePath ?? @"\\.\DISPLAY1";
        var name = display?.FriendlyName ?? "Generic PnP Monitor";
        var collection = new PresetCollection
        {
            Presets =
            [
                new Preset
                {
                    Name = "Gaming 240Hz",
                    DevicePath = path,
                    FriendlyMonitorName = name,
                    Mode = new DisplayMode { Width = 1920, Height = 1080, RefreshRate = 240, BitsPerPel = 32 },
                    ScalePercent = 100,
                    Targets =
                    [
                        new PresetTarget
                        {
                            DevicePath = path,
                            FriendlyMonitorName = name,
                            Mode = new DisplayMode { Width = 1920, Height = 1080, RefreshRate = 240, BitsPerPel = 32 },
                            ScalePercent = 100
                        }
                    ]
                },
                new Preset
                {
                    Name = "Work 60Hz",
                    DevicePath = path,
                    FriendlyMonitorName = name,
                    Mode = new DisplayMode { Width = 1920, Height = 1080, RefreshRate = 60, BitsPerPel = 32 },
                    ScalePercent = 100,
                    Targets =
                    [
                        new PresetTarget
                        {
                            DevicePath = path,
                            FriendlyMonitorName = name,
                            Mode = new DisplayMode { Width = 1920, Height = 1080, RefreshRate = 60, BitsPerPel = 32 },
                            ScalePercent = 100
                        }
                    ]
                }
            ]
        };
        await services.Presets.SaveAsync(collection);
    }

    private static void RestorePresets(string path, string? backup)
    {
        try
        {
            if (backup is null)
            {
                if (File.Exists(path))
                {
                    File.WriteAllText(path, "{\"version\":1,\"presets\":[]}");
                }
            }
            else
            {
                File.WriteAllText(path, backup);
            }
        }
        catch
        {
        }
    }
}
