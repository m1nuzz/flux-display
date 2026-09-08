using FluxDisplay.App.Models;
using Microsoft.UI.Xaml;

namespace FluxDisplay.App.Helpers;

public static class ThemeHelper
{
    public static void ApplyTheme(this AppSettings settings)
    {
        var root = App.MainWindow?.Content as FrameworkElement;
        if (root is null)
        {
            return;
        }

        root.RequestedTheme = settings.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }
}
