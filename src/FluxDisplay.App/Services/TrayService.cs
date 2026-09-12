#pragma warning disable CS0618, CS0219
using System.Reflection;
using FluxDisplay.App.Controls;
using FluxDisplay.App.Helpers;
using FluxDisplay.App.Models;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FluxDisplay.App.Services;

// Manages the system tray icon via H.NotifyIcon.WinUI TaskbarIcon.
// XAML declaration is stubbed in MainWindow (TaskbarIcon declared in code-behind for now).
// Behavior: TrayLeftMouseUp shows/activates MainWindow, context menu contains disabled title,
// dynamic presets, separators, Open, Settings, Exit. Icon /Assets/tray.ico fallback StoreLogo.png.
public sealed class TrayService : ITrayService
{
    private TaskbarIcon? _taskbarIcon;
    private IReadOnlyList<Preset> _presets = Array.Empty<Preset>();
    private Guid? _activePresetId;
    private Dictionary<string, int> _displayNumbers = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler? OpenRequested;
    public event EventHandler<Guid>? PresetApplyRequested;

    public void Initialize()
    {
        if (_taskbarIcon is not null)
        {
            return;
        }

        // H.NotifyIcon converts IconSource via BitmapImage.UriSource -> loose file
        // read. ms-appx:/// never resolves unpackaged (no package identity) and
        // a stream-loaded BitmapImage has UriSource == null, so both render
        // transparent. Only a file:// URI to a real .ico works.
        var iconSource = TryCreateLooseFileIcon()
            ?? TryCreateTempFileIcon()
            ?? new BitmapImage(TryGetTrayIconUri());

        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "flux-display",
            IconSource = iconSource,
            // SecondWindow renders XAML MenuFlyout (themeable dark) instead of Win32 white PopupMenu
            ContextMenuMode = ContextMenuMode.SecondWindow
        };

        // Use LeftClickCommand to handle TrayLeftMouseUp per spec (WinRT events disabled by default).
        var leftCmd = new Microsoft.UI.Xaml.Input.XamlUICommand();
        leftCmd.ExecuteRequested += (_, _) => OnTrayLeftMouseUp();
        _taskbarIcon.LeftClickCommand = leftCmd;

        // Also handle double-click similarly.
        var dblCmd = new Microsoft.UI.Xaml.Input.XamlUICommand();
        dblCmd.ExecuteRequested += (_, _) => OnTrayLeftMouseUp();
        _taskbarIcon.DoubleClickCommand = dblCmd;

        // Runs synchronously on the tray-callback (UI) thread right BEFORE the
        // library shows the SecondWindow menu on right-click. The library
        // stomps Height/Padding on our items on every open (template no longer
        // depends on item Padding) and its cached host handle/AppWindow can go
        // stale (first click lost with 0x80070578). Resync them here so the
        // very first open already measures and positions correctly.
        var rightCmd = new Microsoft.UI.Xaml.Input.XamlUICommand();
        rightCmd.ExecuteRequested += (_, _) => OnTrayRightMousePreShow();
        _taskbarIcon.RightClickCommand = rightCmd;

        RebuildMenu();
        _taskbarIcon.ForceCreate();
        LogMenuHostLiveness("init");
        StartMenuHostWatchdog();
    }

    private bool? _lastMenuHostAlive;

    // DIAGNOSTIC (temporary): the library's hidden SecondWindow host sometimes
    // dies (Invalid window handle on right-click). Poll its HWND liveness via
    // reflection + IsWindow and log transitions, so the log shows WHEN it dies.
    private void LogMenuHostLiveness(string why)
    {
        try
        {
            nint handle = 0;
            // NOTE: auto-property in 2.3.0, not a field — GetField silently misses it.
            var prop = typeof(TaskbarIcon).GetProperty(
                "ContextMenuWindowHandle", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_taskbarIcon is not null && prop?.GetValue(_taskbarIcon) is nint h)
            {
                handle = h;
            }

            var alive = handle != 0 && IsWindow(handle);
            if (_lastMenuHostAlive is null || _lastMenuHostAlive != alive)
            {
                Helpers.AppLog.Info($"menu-host {why} hwnd=0x{handle:X} alive={alive}");
                _lastMenuHostAlive = alive;
            }
        }
        catch (Exception ex)
        {
            Helpers.AppLog.Error(ex, "menu-host probe");
        }
    }

    private void StartMenuHostWatchdog()
    {
        AsyncHelper.FireAndForget(async () =>
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
                while (await timer.WaitForNextTickAsync().ConfigureAwait(false))
                {
                    if (_taskbarIcon is null)
                    {
                        return;
                    }

                    LogMenuHostLiveness("poll");
                }
            }
            catch (Exception ex)
            {
                Helpers.AppLog.Error(ex, "menu-host watchdog");
            }
        });
    }

    public void UpdatePresets(IReadOnlyList<Preset> presets, Guid? activePresetId)
    {
        _presets = presets ?? Array.Empty<Preset>();
        _activePresetId = activePresetId;
        if (_taskbarIcon is null)
        {
            return;
        }

        AsyncHelper.FireAndForget(async () =>
        {
            await RefreshDisplayNumbersAsync().ConfigureAwait(false);
            await AsyncHelper.EnqueueAsync(RebuildMenu).ConfigureAwait(false);
        });
    }

    // One-time warm-up for the library's internal menu flyout. On high-DPI
    // displays the first tray open measures with RasterizationScale fallback
    // (XamlRoot is null until the flyout is shown once), producing an
    // undersized host window with scroll buttons; later opens are fine.
    // We ShowAt the flyout far off-screen and hide it right away: no visible
    // flash, but the flyout gets an XamlRoot and caches fonts, so the first
    // real open measures correctly. Best-effort: any failure just keeps the
    // old (self-healing on second open) behavior.
    public bool WarmUpMenuHost()
    {
        try
        {
            // Immediate attempt (usually skipped pre-Activate: no XamlRoot yet).
            WarmUpNow();
            // Real attempt after the first frame has rendered.
            App.MainWindow?.DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => _ = DelayedWarmUpAsync());
            return true;
        }
        catch (Exception ex)
        {
            Helpers.AppLog.Error(ex, "menu-warmup.schedule");
            return false;
        }
    }

    private async Task DelayedWarmUpAsync()
    {
        try
        {
            await Task.Delay(800).ConfigureAwait(true);
            WarmUpNow();
            await FireMenuHostLoadedAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Helpers.AppLog.Error(ex, "menu-warmup.delayed");
        }
    }

    // 2.3.0 fires frame.Loaded only on the host window's FIRST real ShowWindow
    // — i.e. on the user's first right-click, where its ShowAt+Hide races the
    // click's own Activated->ShowAt and the menu ends up closed (invisible
    // first open, fine afterwards). Burn Loaded down here instead: show the
    // (fully transparent) host once, let Loaded restyle + settle, hide again.
    private async Task FireMenuHostLoadedAsync()
    {
        try
        {
            var handle = GetMenuHostHandle();
            if (handle == 0 || !IsWindow(handle))
            {
                return;
            }

            _ = ShowWindow(handle, 1); // SW_SHOWNORMAL, no activation steal
            await Task.Delay(400).ConfigureAwait(true);
            if (IsWindow(handle))
            {
                _ = ShowWindow(handle, 0); // SW_HIDE
            }

            try
            {
                const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var flyout = typeof(TaskbarIcon).GetProperty("ContextMenuFlyout", flags)
                    ?.GetValue(_taskbarIcon) as MenuFlyout;
                flyout?.Hide();
            }
            catch
            {
            }

            Helpers.AppLog.Info("menu-warmup host-loaded fired");
        }
        catch (Exception ex)
        {
            Helpers.AppLog.Error(ex, "menu-warmup.host-loaded");
        }
    }

    private nint GetMenuHostHandle()
    {
        try
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            if (typeof(TaskbarIcon).GetProperty("ContextMenuWindowHandle", flags)?.GetValue(_taskbarIcon) is nint h)
            {
                return h;
            }
        }
        catch
        {
        }

        return 0;
    }

    private void WarmUpNow()
    {
        try
        {
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var flyoutProp = typeof(TaskbarIcon).GetProperty("ContextMenuFlyout", flags);
            var flyout = _taskbarIcon is not null
                ? flyoutProp?.GetValue(_taskbarIcon) as MenuFlyout
                : null;
            if (flyout is null)
            {
                Helpers.AppLog.Info("menu-warmup skipped: no internal flyout");
                return;
            }

            // The flyout is already associated with the hidden host window's
            // tree (showing it against the main window throws "already
            // associated with a XamlRoot"). Warm it up in place: ShowAt+Hide
            // in the same UI tick forces template realization and measurement
            // without ever painting (mirrors the library's own frame.Loaded).
            var windowProp = typeof(TaskbarIcon).GetProperty("ContextMenuWindow", flags);
            var hostContent = (_taskbarIcon is not null
                ? windowProp?.GetValue(_taskbarIcon) as Window
                : null)?.Content as FrameworkElement;
            if (hostContent is null)
            {
                Helpers.AppLog.Info("menu-warmup skipped: no host content");
                return;
            }

            flyout.ShowAt(hostContent);
            // Same-tick ShowAt+Hide never runs layout, so the first real open
            // still measures cold (narrow host -> scrollbar). Force a layout
            // pass and run the library's own measure while hidden: host window
            // stays invisible, but templates/fonts/DesiredSize get realized.
            try
            {
                hostContent.UpdateLayout();
            }
            catch
            {
            }

            WarmMeasureInternalFlyout();
            flyout.Hide();
            HookInternalFlyoutEvents();
            Helpers.AppLog.Info("menu-warmup done");
        }
        catch (Exception ex)
        {
            Helpers.AppLog.Error(ex, "menu-warmup");
        }
    }

    // Pre-show hook for right-click: refresh items in place (the library
    // syncs these same objects into its internal flyout) and resync the
    // hidden SecondWindow host if its cached handle/AppWindow went stale.
    // Must never throw: an exception here would cancel the menu open.
    private void OnTrayRightMousePreShow()
    {
        try
        {
            var queue = App.MainWindow?.DispatcherQueue;
            if (queue is not null && !queue.HasThreadAccess)
            {
                Helpers.AppLog.Info("menu-host right-click: off-UI-thread, resync skipped");
                return;
            }

            try
            {
                RefreshMenuItems();
            }
            catch
            {
            }

            ResyncMenuHost("right-click");
            PreShowHostWindow();
            WarmMeasureInternalFlyout();
            LogPreShowState();
            EnqueuePostShowState();
        }
        catch (Exception ex)
        {
            Helpers.AppLog.Error(ex, "tray-rightclick-preshow");
        }
    }

    // 2.3.0 drops the show when the host goes hidden->shown+foreground in one
    // stack (Activated->ShowAt never fires: invisible first open), but
    // visible->foreground always works (that's why the second click is fine:
    // the first one left the host shown). Pre-show the host without activation
    // so the library's own ShowWindow+SetForegroundWindow takes the warm path.
    // The host is fully transparent: nothing flashes.
    private void PreShowHostWindow()
    {
        try
        {
            var h = GetMenuHostHandle();
            if (h != 0 && IsWindow(h) && !IsWindowVisible(h))
            {
                _ = ShowWindow(h, 4); // SW_SHOWNA: show, no activation
            }
        }
        catch
        {
        }
    }

    private void ResyncMenuHost(string why)
    {
        try
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var t = typeof(TaskbarIcon);
            var window = t.GetProperty("ContextMenuWindow", flags)?.GetValue(_taskbarIcon) as Window;
            if (window is null)
            {
                Helpers.AppLog.Info($"menu-host {why}: no ContextMenuWindow");
                return;
            }

            var liveHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            if (liveHandle == 0 || !IsWindow(liveHandle))
            {
                // Window object survived but its HWND is gone: Activate
                // recreates it, then hide right away (repair path only).
                try
                {
                    window.Activate();
                    liveHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
                    if (liveHandle != 0 && IsWindow(liveHandle))
                    {
                        _ = ShowWindow(liveHandle, 0);
                    }
                }
                catch
                {
                }
            }

            var liveAlive = liveHandle != 0 && IsWindow(liveHandle);
            var cached = t.GetProperty("ContextMenuWindowHandle", flags)?.GetValue(_taskbarIcon) as nint?;
            if (liveAlive && cached != liveHandle)
            {
                t.GetProperty("ContextMenuWindowHandle", flags)?.SetValue(_taskbarIcon, liveHandle);
                try
                {
                    var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(liveHandle);
                    var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id);
                    t.GetProperty("ContextMenuAppWindow", flags)?.SetValue(_taskbarIcon, appWindow);
                }
                catch (Exception ex)
                {
                    Helpers.AppLog.Error(ex, "menu-host appwindow-resync");
                }

                Helpers.AppLog.Info($"menu-host {why} resynced hwnd=0x{liveHandle:X} (was 0x{cached:X})");
            }
            else if (_lastMenuHostAlive != liveAlive)
            {
                Helpers.AppLog.Info($"menu-host {why} hwnd=0x{liveHandle:X} alive={liveAlive}");
                _lastMenuHostAlive = liveAlive;
            }
        }
        catch (Exception ex)
        {
            Helpers.AppLog.Error(ex, "menu-host resync");
        }
    }

    // Runs the library's own private MeasureFlyout over its internal flyout:
    // same Prepare(stomp)+Measure the show path does, but while hidden. Pure
    // (no visual effect), best-effort, pinned to H.NotifyIcon 2.3.0.
    private void WarmMeasureInternalFlyout()
    {
        try
        {
            const BindingFlags inst =
                BindingFlags.NonPublic | BindingFlags.Instance;
            const BindingFlags stat =
                BindingFlags.NonPublic | BindingFlags.Static;
            var t = typeof(TaskbarIcon);
            var flyout = t.GetProperty("ContextMenuFlyout", inst)?.GetValue(_taskbarIcon) as MenuFlyout;
            if (flyout is null)
            {
                return;
            }

            t.GetMethod("MeasureFlyout", stat)?.Invoke(
                null, new object[] { flyout, new Windows.Foundation.Size(10000.0, 10000.0) });
        }
        catch (Exception ex)
        {
            Helpers.AppLog.Error(ex, "menu-warm-measure");
        }
    }

    private MenuFlyout? _hookedFlyout;

    // Hook the library's internal flyout once: exact Opened/Closed timeline
    // per click. Shows whether click 1 never opens or opens-then-closes.
    private void HookInternalFlyoutEvents()
    {
        try
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var flyout = typeof(TaskbarIcon).GetProperty("ContextMenuFlyout", flags)?.GetValue(_taskbarIcon) as MenuFlyout;
            if (flyout is null || ReferenceEquals(flyout, _hookedFlyout))
            {
                return;
            }

            _hookedFlyout = flyout;
            flyout.Opened += (_, _) => Helpers.AppLog.Info("menu-flyout opened");
            flyout.Closed += (_, _) => Helpers.AppLog.Info("menu-flyout closed");

            try
            {
                const BindingFlags flags2 = BindingFlags.NonPublic | BindingFlags.Instance;
                var host = typeof(TaskbarIcon).GetProperty("ContextMenuWindow", flags2)?.GetValue(_taskbarIcon) as Window;
                if (host is not null)
                {
                    host.Activated += (_, args) => Helpers.AppLog.Info($"menu-host activated state={args.WindowActivationState}");
                }
            }
            catch (Exception ex)
            {
                Helpers.AppLog.Error(ex, "menu-host-hook");
            }
        }
        catch (Exception ex)
        {
            Helpers.AppLog.Error(ex, "menu-flyout-hook");
        }
    }

    // One mandatory line per right-click: internal flyout XamlRoot/scale,
    // loaded flag, host handle. Tells cold first-open apart from warm ones.
    private void LogPreShowState()
    {
        try
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var t = typeof(TaskbarIcon);
            var flyout = t.GetProperty("ContextMenuFlyout", flags)?.GetValue(_taskbarIcon) as MenuFlyout;
            object? loaded = t.GetProperty("IsSecondWindowContextMenuLoaded", flags)?.GetValue(_taskbarIcon);
            loaded ??= t.GetField("IsSecondWindowContextMenuLoaded", flags)?.GetValue(_taskbarIcon);
            var cached = t.GetProperty("ContextMenuWindowHandle", flags)?.GetValue(_taskbarIcon) as nint?;
            var scale = flyout?.XamlRoot?.RasterizationScale;
            var open = flyout?.IsOpen;
            Helpers.AppLog.Info(
                $"menu-show click xamlroot={(scale is null ? "null" : scale.ToString())} loaded={loaded} isopen={open} hwnd=0x{cached:X}");
        }
        catch (Exception ex)
        {
            Helpers.AppLog.Error(ex, "menu-show-state");
        }
    }

    // Sampled ~600ms after the click: did the flyout actually open, is the
    // host visible, and where is it. Discriminates "ShowAt never ran" from
    // "opened then closed" from "opened off-screen".
    private void EnqueuePostShowState()
    {
        try
        {
            App.MainWindow?.DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await Task.Delay(600).ConfigureAwait(true);
                    const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                    var t = typeof(TaskbarIcon);
                    var flyout = t.GetProperty("ContextMenuFlyout", flags)?.GetValue(_taskbarIcon) as MenuFlyout;
                    var visible = t.GetProperty("IsContextMenuVisible", flags)?.GetValue(_taskbarIcon);
                    var handle = GetMenuHostHandle();
                    var shown = handle != 0 && IsWindowVisible(handle);
                    string rect = "n/a";
                    if (handle != 0)
                    {
                        try
                        {
                            if (GetWindowRect(handle, out var r))
                            {
                                rect = $"{r.left},{r.top},{r.right},{r.bottom}";
                            }
                        }
                        catch
                        {
                        }
                    }

                    Helpers.AppLog.Info(
                        $"menu-show +600ms isopen={flyout?.IsOpen} visibleflag={visible} winvisible={shown} rect={rect}");
                }
                catch (Exception ex)
                {
                    Helpers.AppLog.Error(ex, "menu-postshow-state");
                }
            });
        }
        catch
        {
        }
    }

    internal bool TryShowContextFlyout()
    {
        if (_taskbarIcon?.ContextFlyout is not MenuFlyout flyout)
        {
            return false;
        }

        var window = App.MainWindow;
        if (window?.Content is not FrameworkElement root || root.XamlRoot is null)
        {
            return false;
        }

        flyout.ShowAt(root, new FlyoutShowOptions
        {
            Placement = FlyoutPlacementMode.Right,
            ShowMode = FlyoutShowMode.Standard
        });
        return true;
    }

    public void ShowNotification(string title, string message)
    {
        if (_taskbarIcon is null)
        {
            return;
        }

        try
        {
            _taskbarIcon.ShowNotification(title, message);
        }
        catch
        {
        }
    }

    private void OnTrayLeftMouseUp()
    {
        Helpers.AppLog.Info("TrayService.OnTrayLeftMouseUp");
        LogMenuHostLiveness("left-click");
        try
        {
            var window = App.MainWindow;
            if (window is not null)
            {
                window.DispatcherQueue.TryEnqueue(() =>
                {
                    window.Activate();
                    try
                    {
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                        _ = ShowWindow(hwnd, 5);
                        _ = SetForegroundWindow(hwnd);
                    }
                    catch
                    {
                    }
                });
            }
        }
        catch
        {
        }

        OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    // CRASH FIX (H.NotifyIcon #229): every assignment to ContextFlyout makes the
    // library create a new hidden SecondWindow + SetWindowSubclass with a new
    // delegate that overwrites a single static slot. Old windows leak, old
    // delegates get GC'd, and the next message to an orphaned window kills the
    // process with FailFast("callback on a garbage collected SUBCLASSPROC").
    // So the flyout is built ONCE and presets are mutated in place afterwards.
    // H.NotifyIcon copies our items into its internal flyout at Prepare time,
    // but they are the SAME objects, so in-place Text/Visibility/Tag edits
    // stay visible. Never re-assign ContextFlyout, never touch Items after.
    private const int MaxMenuPresets = 10;
    private const double MenuItemMinWidth = 250.0;
    private readonly List<TrayPresetMenuItem> _presetSlots = new();
    private MenuFlyoutItem? _emptyItem;

    private void RebuildMenu()
    {
        if (_taskbarIcon is null)
        {
            return;
        }

        if (_taskbarIcon.ContextFlyout is null)
        {
            BuildMenuOnce();
        }

        RefreshMenuItems();
    }

    private void BuildMenuOnce()
    {
        if (_taskbarIcon is null)
        {
            return;
        }

        var flyout = new MenuFlyout();

        // Force dark theme for tray menu to match reference (navy #081827, rounded).
        // NOTE: keep presenter style minimal (theme + background only) — replacing the
        // full default style breaks layout. Width is enforced per-item below (flyout
        // sizes to its widest item), which reliably fixes "Displa"/"No pr" clipping.
        try
        {
            var presenterStyle = new Style(typeof(MenuFlyoutPresenter));
            presenterStyle.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, ElementTheme.Dark));
            presenterStyle.Setters.Add(new Setter(Control.BackgroundProperty, new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x08, 0x18, 0x27))));
            presenterStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(12)));
            presenterStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1E, 0x3A, 0x5F))));
            presenterStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            presenterStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 10, 12, 10)));
            flyout.MenuFlyoutPresenterStyle = presenterStyle;
        }
        catch
        {
        }

        var header = new MenuFlyoutItem
        {
            Text = "Display Presets",
            IsEnabled = false,
            MinWidth = MenuItemMinWidth,
            Icon = new FontIcon
            {
                Glyph = "\uE7F4",
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x3B, 0x82, 0xF6))
            }
        };
        flyout.Items.Add(header);
        flyout.Items.Add(new MenuFlyoutSeparator());

        for (var i = 0; i < MaxMenuPresets; i++)
        {
            var slot = new TrayPresetMenuItem
            {
                MinWidth = MenuItemMinWidth,
                // Base MenuFlyoutItem clamps rows to 32px; two-line content
                // (~6+19+4+16+6=51) would overflow into neighbours. MinHeight
                // wins in layout and fits the content exactly, no dead air.
                MinHeight = 52,
                Visibility = Visibility.Collapsed,
                Icon = new FontIcon
                {
                    Glyph = "\uE7F4",
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xE6, 0xE6, 0xE6))
                }
            };
            slot.Click += (_, _) =>
            {
                if (slot.Tag is Guid id)
                {
                    PresetApplyRequested?.Invoke(this, id);
                }
            };
            _presetSlots.Add(slot);
            flyout.Items.Add(slot);
        }

        _emptyItem = new MenuFlyoutItem { Text = "No presets", IsEnabled = false, MinWidth = MenuItemMinWidth };
        flyout.Items.Add(_emptyItem);
        flyout.Items.Add(new MenuFlyoutSeparator());

        var open = new MenuFlyoutItem
        {
            Text = "Open Display Presets",
            MinWidth = MenuItemMinWidth,
            Icon = new FontIcon
            {
                Glyph = "\uE713",
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xCC, 0xCC, 0xCC))
            }
        };
        open.Click += (_, _) =>
        {
            OpenRequested?.Invoke(this, EventArgs.Empty);
            try
            {
                var window = App.MainWindow;
                window?.DispatcherQueue.TryEnqueue(() => window.Activate());
            }
            catch
            {
            }
        };
        flyout.Items.Add(open);

        var exit = new MenuFlyoutItem
        {
            Text = "Exit",
            MinWidth = MenuItemMinWidth,
            Icon = new FontIcon
            {
                Glyph = "\uE7E8",
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xCC, 0xCC, 0xCC))
            }
        };
        exit.Click += (_, _) => App.ExitApplication();
        flyout.Items.Add(exit);

        // Assigned exactly once per process. See crash note above.
        _taskbarIcon.ContextFlyout = flyout;
        Helpers.AppLog.Info("TrayService menu built once");
    }

    private void RefreshMenuItems()
    {
        var presets = _presets.Take(MaxMenuPresets).ToList();
        for (var i = 0; i < _presetSlots.Count; i++)
        {
            var slot = _presetSlots[i];
            if (i < presets.Count)
            {
                var preset = presets[i];
                slot.Text = preset.Name;
                slot.Subtitle = FormatMenuSubtitle(preset);
                slot.Tag = preset.Id;
                slot.Visibility = Visibility.Visible;
            }
            else
            {
                slot.Visibility = Visibility.Collapsed;
            }
        }

        if (_emptyItem is not null)
        {
            _emptyItem.Visibility = presets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        Helpers.AppLog.Info($"TrayService menu refreshed presets={presets.Count}");
    }

    private string FormatMenuSubtitle(Preset preset)
    {
        var targets = preset.GetTargets();
        var numbers = targets.Select(ResolveMonitorNumber).ToList();
        var rates = targets.Select(t => t.Mode.RefreshRate).ToList();
        if (targets.Count <= 1)
        {
            var number = numbers.FirstOrDefault(1);
            var rate = rates.FirstOrDefault();
            return $"Monitor {number} \u00B7 {rate} Hz";
        }

        return $"Monitors {string.Join("+", numbers)} \u00B7 {string.Join("/", rates)} Hz";
    }

    private int ResolveMonitorNumber(PresetTarget target)
    {
        if (_displayNumbers.TryGetValue(target.DevicePath, out var number))
        {
            return number;
        }

        return 1;
    }

    private async Task RefreshDisplayNumbersAsync()
    {
        try
        {
            var displays = await App.Services.Display.GetDisplaysAsync().ConfigureAwait(false);
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var number = 1;
            foreach (var display in displays)
            {
                if (map.TryAdd(display.DevicePath, number))
                {
                    number++;
                }
            }

            _displayNumbers = map;
        }
        catch
        {
        }
    }

    private Microsoft.UI.Xaml.Media.ImageSource? TryCreateLooseFileIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "tray.ico");
            if (!File.Exists(path))
            {
                LogTray("loose tray.ico missing: " + path);
                return null;
            }

            LogTray("using loose file: " + path);
            return new BitmapImage(new Uri(path));
        }
        catch (Exception ex)
        {
            LogTray("loose file failed: " + ex.Message);
            return null;
        }
    }

    private Microsoft.UI.Xaml.Media.ImageSource? TryCreateTempFileIcon()
    {
        try
        {
            var asm = typeof(TrayService).Assembly;
            var match = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("tray.png", StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                LogTray("tray.png not embedded");
                return null;
            }

            var dir = Path.Combine(Path.GetTempPath(), "FluxDisplay");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "tray.png");
            if (!File.Exists(path))
            {
                using var src = asm.GetManifestResourceStream(match);
                if (src is null)
                {
                    LogTray("open stream failed");
                    return null;
                }

                using var dst = File.Create(path);
                src.CopyTo(dst);
            }

            LogTray("using temp file: " + path);
            return new BitmapImage(new Uri(path));
        }
        catch (Exception ex)
        {
            LogTray("temp file failed: " + ex.Message);
            return null;
        }
    }

    private static void LogTray(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "flux-tray.log"),
                $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static Uri TryGetTrayIconUri()
    {
        // NOTE: H.NotifyIcon renders .ico reliably; PNG via BitmapImage came out
        // blank in the overflow. tray.ico now has thick borders (16/32/48 drawn
        // for small sizes), so the monitor reads at 16px.
        try
        {
            return new Uri("ms-appx:///Assets/tray.ico");
        }
        catch
        {
            return new Uri("ms-appx:///Assets/StoreLogo.png");
        }
    }

    // Orderly teardown before process exit (tray Exit, update install).
    // Native message/subclass windows must be destroyed on the UI thread
    // while the CLR is alive: if they outlive managed delegates into process
    // teardown, the next message to them ends in a FailFast crash dialog
    // (stack buffer overrun). Safe to call twice; never throws.
    public void Shutdown()
    {
        try
        {
            try
            {
                _taskbarIcon?.ContextFlyout?.Hide();
            }
            catch
            {
            }

            try
            {
                var handle = GetMenuHostHandle();
                if (handle != 0 && IsWindow(handle))
                {
                    _ = ShowWindow(handle, 0);
                }
            }
            catch
            {
            }

            LogMenuHostLiveness("shutdown");
        }
        catch (Exception ex)
        {
            Helpers.AppLog.Error(ex, "tray-shutdown");
        }
        finally
        {
            Dispose();
        }
    }

    public void ParkMenuHost()
    {
        try
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var flyout = typeof(TaskbarIcon).GetProperty("ContextMenuFlyout", flags)?.GetValue(_taskbarIcon) as MenuFlyout;
            if (flyout?.IsOpen == true)
            {
                return;
            }

            var handle = GetMenuHostHandle();
            if (handle != 0 && IsWindow(handle) && IsWindowVisible(handle))
            {
                _ = ShowWindow(handle, 0);
                Helpers.AppLog.Info("menu-host parked hidden");
            }
        }
        catch (Exception ex)
        {
            Helpers.AppLog.Error(ex, "menu-host park");
        }
    }

    public void Dispose()
    {
        if (_taskbarIcon is not null)
        {
            try
            {
                _taskbarIcon.Dispose();
            }
            catch
            {
            }
            _taskbarIcon = null;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out WinRect lpRect);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct WinRect
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
