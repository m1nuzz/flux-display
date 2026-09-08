using System.ComponentModel;
using FluxDisplay.App.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FluxDisplay.App.Controls;

public sealed partial class MonitorPickerCard : UserControl
{
    private static readonly SolidColorBrush SelectedBorderBrush = new(Microsoft.UI.Colors.DodgerBlue);
    private static readonly SolidColorBrush SelectedBackgroundBrush = new(Windows.UI.Color.FromArgb(0x22, 0x3B, 0x82, 0xF6));
    private static readonly Thickness SelectedThickness = new(2);
    private static readonly Thickness NormalThickness = new(1);

    private MonitorOption? _boundOption;

    public MonitorPickerCard()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => UpdateVisual();
        Unloaded += (_, _) =>
        {
            if (_boundOption != null)
            {
                _boundOption.PropertyChanged -= OnOptionPropertyChanged;
                _boundOption = null;
            }
        };
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (_boundOption != null)
        {
            _boundOption.PropertyChanged -= OnOptionPropertyChanged;
            _boundOption = null;
        }
        if (args.NewValue is MonitorOption opt)
        {
            _boundOption = opt;
            opt.PropertyChanged += OnOptionPropertyChanged;
            UpdateVisual();
        }
        else
        {
            UpdateVisual();
        }
    }

    private void OnOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MonitorOption.IsSelected))
        {
            DispatcherQueue.TryEnqueue(UpdateVisual);
        }
    }

    private void UpdateVisual()
    {
        Brush GetBrush(string key, Windows.UI.Color fallback)
        {
            try
            {
                if (Application.Current.Resources.TryGetValue(key, out var v) && v is Brush b) return b;
            }
            catch { }
            return new SolidColorBrush(fallback);
        }

        if (DataContext is not MonitorOption opt)
        {
            RootBorder.BorderBrush = GetBrush("CardStrokeColorDefaultBrush", Windows.UI.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
            RootBorder.BorderThickness = NormalThickness;
            RootBorder.Background = GetBrush("CardBackgroundFillColorDefaultBrush", Windows.UI.Color.FromArgb(0xFF, 0x16, 0x2A, 0x3A));
            return;
        }

        if (opt.IsSelected)
        {
            RootBorder.BorderBrush = SelectedBorderBrush;
            RootBorder.BorderThickness = SelectedThickness;
            RootBorder.Background = SelectedBackgroundBrush;
            if (IconGlyph != null) IconGlyph.Foreground = SelectedBorderBrush;
        }
        else
        {
            RootBorder.BorderBrush = GetBrush("CardStrokeColorDefaultBrush", Windows.UI.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
            RootBorder.BorderThickness = NormalThickness;
            RootBorder.Background = GetBrush("CardBackgroundFillColorDefaultBrush", Windows.UI.Color.FromArgb(0xFF, 0x16, 0x2A, 0x3A));
            if (IconGlyph != null) IconGlyph.Foreground = GetBrush("TextFillColorSecondaryBrush", Windows.UI.Color.FromArgb(0xFF, 0xAA, 0xAA, 0xAA));
        }
    }
}
