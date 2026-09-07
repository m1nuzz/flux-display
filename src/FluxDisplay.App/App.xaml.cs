using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace FluxDisplay.App;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow ??= new Window
        {
            Content = new MainPage(),
            SystemBackdrop = new MicaBackdrop(),
            ExtendsContentIntoTitleBar = true
        };
        MainWindow.Activate();
    }
}
