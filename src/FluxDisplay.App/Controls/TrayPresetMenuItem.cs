using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluxDisplay.App.Controls;

public sealed class TrayPresetMenuItem : MenuFlyoutItem
{
    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle),
        typeof(string),
        typeof(TrayPresetMenuItem),
        new PropertyMetadata(string.Empty));

    public TrayPresetMenuItem()
    {
        DefaultStyleKey = typeof(TrayPresetMenuItem);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }
}
