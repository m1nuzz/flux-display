using FluxDisplay.App.ViewModels;
using FluxDisplay.App.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace FluxDisplay.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();
        TryApplyMinSize();
        ViewModel = new MainViewModel(App.Services);
        Activated += (_, _) => SetTitleBar(AppTitleBar);
        Closed += OnClosed;
        TryHookClosing();
        ViewModel.NavigationRequested += (_, tag) => Navigate(tag);
        ViewModel.RequestShowWindow += (_, _) => ShowFromTray();
        ViewModel.Presets.AddRequested += (_, _) => Navigate("create");
        ViewModel.Create.Cancelled += (_, _) => Navigate("presets");
    }

    public async Task InitializeAsync()
    {
        await ViewModel.InitializeAsync().ConfigureAwait(true);
        Navigate("presets");
        if (ViewModel.CurrentSettings.StartMinimized)
        {
            HideToTray();
        }
        else
        {
            Activate();
        }
    }

    public void ShowFromTray()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                // Restore the backdrop dropped on hide (see HideToTray).
                if (SystemBackdrop is null)
                {
                    SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
                }
            }
            catch
            {
            }

            try
            {
                var hwnd = WindowNative.GetWindowHandle(this);
                ShowWindow(hwnd, 9);
                SetForegroundWindow(hwnd);
            }
            catch
            {
            }

            Activate();
        });
    }

    // Internal for the lifecycle smoke test (same assembly).
    internal void HideToTray()
    {
        try
        {
            // A hidden window must cost nothing: drop Mica so DWM keeps no
            // live backdrop surface for us while we sit in the tray. Without
            // this the process holds ~0.1% GPU forever after the first show.
            SystemBackdrop = null;
        }
        catch
        {
        }

        try
        {
            // Close menu popups before hiding: a popup open across hide/show
            // leaves WS_VISIBLE ghost PopupHost windows behind (they composite
            // every vsync and eat input). Content dialogs are left alone.
            if (Content is FrameworkElement root && root.XamlRoot is not null)
            {
                foreach (var popup in Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot))
                {
                    try
                    {
                        if (popup.Child is Microsoft.UI.Xaml.Controls.MenuFlyoutPresenter)
                        {
                            popup.IsOpen = false;
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch
        {
        }

        try
        {
            App.Services.Tray.ParkMenuHost();
        }
        catch
        {
        }

        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            ShowWindow(hwnd, 0);
        }
        catch
        {
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            Navigate(tag);
        }
    }

    internal void NavigateTo(string tag) => Navigate(tag);

    private void Navigate(string tag)
    {
        Helpers.AppLog.Info($"MainWindow.Navigate tag={tag}");
        ViewModel.SelectedTag = tag;
        if (tag == "create")
        {
            _ = ViewModel.Create.ResetAsync();
        }
        ContentFrame.Content = tag switch
        {
            "settings" => new SettingsPage { DataContext = ViewModel.SettingsViewModel },
            "create" => new CreatePresetPage { DataContext = ViewModel.Create },
            "edit" => new CreatePresetPage { DataContext = ViewModel.Create },
            _ => new PresetsPage { DataContext = ViewModel.Presets }
        };
        Helpers.AppLog.Info($"MainWindow.Navigate content set tag={tag}");

        foreach (var menu in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            menu.IsSelected = string.Equals(menu.Tag as string, tag is "create" or "edit" ? "presets" : tag, StringComparison.Ordinal);
        }

        if (tag == "settings")
        {
            _ = ViewModel.SettingsViewModel.LoadAsync();
        }
    }

    // Win32 subclass state for the min-size clamp (see TryApplyMinSize).
    // Kept in fields so the delegate is never GC-collected.
    private SubclassProc? _minSizeSubclass;
    private int _minTrackWidth;
    private int _minTrackHeight;

    private void TryApplyMinSize()
    {
        try
        {
            // Project targets Windows App SDK 1.6, which has no
            // OverlappedPresenter.PreferredMinimum* (added in 1.7), so clamp
            // via WM_GETMINMAXINFO. 640x480 effective px also matches
            // NavigationView's Auto breakpoint (LeftMinimal below 640).
            var hwnd = WindowNative.GetWindowHandle(this);
            uint dpi = GetDpiForWindow(hwnd);
            double scale = dpi / 96.0;
            _minTrackWidth = (int)(640 * scale);
            _minTrackHeight = (int)(480 * scale);
            _minSizeSubclass = MinSizeSubclassProc;
            SetWindowSubclass(hwnd, _minSizeSubclass, 0, 0);
        }
        catch
        {
        }
    }

    private int MinSizeSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam, nint uIdSubclass, nint dwRefData)
    {
        const uint WM_GETMINMAXINFO = 0x0024;
        if (uMsg == WM_GETMINMAXINFO)
        {
            try
            {
                var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MinMaxInfo>(lParam);
                mmi.ptMinTrackSize.x = _minTrackWidth;
                mmi.ptMinTrackSize.y = _minTrackHeight;
                System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, false);
            }
            catch
            {
            }

            return 0;
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Point
    {
        public int x;
        public int y;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point ptReserved;
        public Point ptMaxSize;
        public Point ptMaxPosition;
        public Point ptMinTrackSize;
        public Point ptMaxTrackSize;
    }

    private delegate int SubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam, nint uIdSubclass, nint dwRefData);

    [System.Runtime.InteropServices.DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nint uIdSubclass, nint dwRefData);

    [System.Runtime.InteropServices.DllImport("comctl32.dll")]
    private static extern int DefSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hWnd);

    private void TryHookClosing()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow is not null)
            {
                appWindow.Closing += (_, e) =>
                {
                    // Same as OnClosed: never veto a real process exit.
                    if (App.IsExiting)
                    {
                        return;
                    }

                    e.Cancel = true;
                    HideToTray();
                };
            }
        }
        catch
        {
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        // During real process exit the close must go through (hiding would
        // strand the shutdown behind a vetoed close).
        if (App.IsExiting)
        {
            return;
        }

        args.Handled = true;
        HideToTray();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);
}
