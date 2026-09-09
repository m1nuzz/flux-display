using Microsoft.UI.Xaml.Controls;

namespace FluxDisplay.App.Views;

public sealed partial class CreatePresetPage : Page
{
    public CreatePresetPage()
    {
        Helpers.AppLog.Info("CreatePresetPage.ctor start");
        InitializeComponent();
        Helpers.AppLog.Info("CreatePresetPage.ctor done");
        Unloaded += (_, _) => Helpers.AppLog.Info("CreatePresetPage.Unloaded");
    }
}
