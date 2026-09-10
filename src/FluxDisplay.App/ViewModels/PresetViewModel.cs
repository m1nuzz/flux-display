using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxDisplay.App.Models;

namespace FluxDisplay.App.ViewModels;

public sealed partial class PresetViewModel : ObservableObject
{
    public PresetViewModel(Preset preset)
    {
        Preset = preset;
        Preset.EnsureTargets();
    }

    public Preset Preset { get; }

    public Guid Id => Preset.Id;
    public string Name => Preset.Name;
    public string MonitorName => string.Join(" · ", Preset.GetTargets().Select(t => t.FriendlyMonitorName));
    public string ModeText => string.Join(" · ", Preset.GetTargets().Select(t => t.Mode.ToString()));
    public string ScaleText => string.Join(" · ", Preset.GetTargets().Select(t => $"{t.ScalePercent}%"));

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isApplying;

    public IAsyncRelayCommand? ApplyCommand { get; set; }
    public IAsyncRelayCommand? DeleteCommand { get; set; }
    public IRelayCommand? EditCommand { get; set; }

    public PresetCardModel ToCard() => new()
    {
        Preset = Preset,
        IsActive = IsActive,
        IsFavorite = false
    };
}
