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
        if (ViewModel.SettingsViewModel.StartMinimized)
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

    private void HideToTray()
    {
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

    private void Navigate(string tag)
    {
        ViewModel.SelectedTag = tag;
        ContentFrame.Content = tag switch
        {
            "settings" => new SettingsPage { DataContext = ViewModel.SettingsViewModel },
            "create" => new CreatePresetPage { DataContext = ViewModel.Create },
            _ => new PresetsPage { DataContext = ViewModel.Presets }
        };

        foreach (var menu in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            menu.IsSelected = string.Equals(menu.Tag as string, tag is "create" ? "presets" : tag, StringComparison.Ordinal);
        }

        if (tag == "settings")
        {
            _ = ViewModel.SettingsViewModel.LoadAsync();
        }
    }

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
        args.Handled = true;
        HideToTray();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);
}
