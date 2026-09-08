#pragma warning disable CS0618, CS0219
using FluxDisplay.App.Models;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

    public event EventHandler? OpenRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler<Guid>? PresetApplyRequested;

    public void Initialize()
    {
        if (_taskbarIcon is not null)
        {
            return;
        }

        var iconUri = TryGetTrayIconUri();

        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "flux-display",
            IconSource = new BitmapImage(iconUri),
            ContextMenuMode = ContextMenuMode.PopupMenu
        };

        // Use LeftClickCommand to handle TrayLeftMouseUp per spec (WinRT events disabled by default).
        var leftCmd = new Microsoft.UI.Xaml.Input.XamlUICommand();
        leftCmd.ExecuteRequested += (_, _) => OnTrayLeftMouseUp();
        _taskbarIcon.LeftClickCommand = leftCmd;

        // Also handle double-click similarly.
        var dblCmd = new Microsoft.UI.Xaml.Input.XamlUICommand();
        dblCmd.ExecuteRequested += (_, _) => OnTrayLeftMouseUp();
        _taskbarIcon.DoubleClickCommand = dblCmd;

        RebuildMenu();
        _taskbarIcon.ForceCreate();
    }

    public void UpdatePresets(IReadOnlyList<Preset> presets, Guid? activePresetId)
    {
        _presets = presets ?? Array.Empty<Preset>();
        _activePresetId = activePresetId;
        if (_taskbarIcon is not null)
        {
            if (Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread() is not null)
            {
                RebuildMenu();
            }
            else
            {
                try
                {
                    App.MainWindow?.DispatcherQueue.TryEnqueue(RebuildMenu);
                }
                catch
                {
                    RebuildMenu();
                }
            }
        }
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

    private void RebuildMenu()
    {
        if (_taskbarIcon is null)
        {
            return;
        }

        var flyout = new MenuFlyout();

        var header = new MenuFlyoutItem
        {
            Text = "flux-display",
            IsEnabled = false
        };
        flyout.Items.Add(header);
        flyout.Items.Add(new MenuFlyoutSeparator());

        if (_presets.Count > 0)
        {
            foreach (var preset in _presets.Take(10))
            {
                var isActive = _activePresetId.HasValue && _activePresetId.Value == preset.Id;
                var item = new MenuFlyoutItem
                {
                    Text = isActive ? $"● {preset.Name}" : preset.Name,
                    Tag = preset.Id
                };
                item.Click += (_, _) =>
                {
                    if (item.Tag is Guid id)
                    {
                        PresetApplyRequested?.Invoke(this, id);
                    }
                };
                flyout.Items.Add(item);
            }
            flyout.Items.Add(new MenuFlyoutSeparator());
        }
        else
        {
            var empty = new MenuFlyoutItem { Text = "No presets", IsEnabled = false };
            flyout.Items.Add(empty);
            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        var open = new MenuFlyoutItem { Text = "Open" };
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

        var settings = new MenuFlyoutItem { Text = "Settings" };
        settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        flyout.Items.Add(settings);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var exit = new MenuFlyoutItem { Text = "Exit" };
        exit.Click += (_, _) => Application.Current.Exit();
        flyout.Items.Add(exit);

        _taskbarIcon.ContextFlyout = flyout;
    }

    private static Uri TryGetTrayIconUri()
    {
        try
        {
            return new Uri("ms-appx:///Assets/tray.ico");
        }
        catch
        {
            return new Uri("ms-appx:///Assets/StoreLogo.png");
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
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
