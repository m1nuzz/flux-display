using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace FluxDisplay.App.Views;

public sealed partial class MonitorIdentifyWindow : Window
{
    public MonitorIdentifyWindow(int number)
    {
        InitializeComponent();
        NumberText.Text = number.ToString();
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            appWindow.IsShownInSwitchers = false;
        }
        catch
        {
        }
    }
}
