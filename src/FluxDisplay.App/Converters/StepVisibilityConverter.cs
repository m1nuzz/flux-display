using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace FluxDisplay.App.Converters;

public sealed class StepVisibilityConverter : IValueConverter
{
    public int Step { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var current = value is int i ? i : 0;
        return current == Step ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
