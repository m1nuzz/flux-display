using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace FluxDisplay.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
    }
}
