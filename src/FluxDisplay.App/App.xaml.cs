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
        var instance = AppInstance.FindOrRegisterForKey("flux-display-main");
        if (!instance.IsCurrent)
        {
            instance.RedirectActivationToAsync(AppInstance.GetActivatedEventArgs()).AsTask().GetAwaiter().GetResult();
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
}
