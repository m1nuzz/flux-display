using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxDisplay.App.Models;
using FluxDisplay.App.Services;
using FluxDisplay.Core.Services;

namespace FluxDisplay.App.ViewModels;

public sealed partial class CreatePresetViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly PresetValidator _validator = new();
    private IReadOnlyList<DisplayMode> _allModes = [];

    public CreatePresetViewModel(AppServices services)
    {
        _services = services;
        Monitors = new ObservableCollection<MonitorOption>();
        Resolutions = new ObservableCollection<string>();
        RefreshRates = new ObservableCollection<int>();
    }

    public ObservableCollection<MonitorOption> Monitors { get; }
    public ObservableCollection<string> Resolutions { get; }
    public ObservableCollection<int> RefreshRates { get; }

    [ObservableProperty] private int _currentStep;
    [ObservableProperty] private MonitorOption? _selectedMonitor;
    [ObservableProperty] private string? _selectedResolution;
    [ObservableProperty] private int _selectedRefreshRate;
    [ObservableProperty] private int _currentDpi;
    [ObservableProperty] private int _scalePercent = 100;
    [ObservableProperty] private string _presetName = string.Empty;
    [ObservableProperty] private bool _canGoNext;
    [ObservableProperty] private bool _canCreate;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public IReadOnlyList<StepInfo> Steps =>
    [
        new(0, "Choose display", "Pick the monitor", CurrentStep > 0, CurrentStep == 0),
        new(1, "Display settings", "Resolution and refresh", CurrentStep > 1, CurrentStep == 1),
        new(2, "Preset name", "Name and confirm", CurrentStep > 2, CurrentStep == 2),
    ];

    public event EventHandler<Preset>? PresetCreated;
    public event EventHandler? Cancelled;

    partial void OnSelectedMonitorChanged(MonitorOption? value) => UpdateCanGoNext();
    partial void OnSelectedResolutionChanged(string? value)
    {
        RebuildRefreshRates();
        UpdateCanGoNext();
        UpdateSummary();
    }
    partial void OnSelectedRefreshRateChanged(int value)
    {
        UpdateCanGoNext();
        UpdateSummary();
    }
    partial void OnPresetNameChanged(string value)
    {
        CanCreate = !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= PresetValidator.MaxNameLength;
        UpdateSummary();
    }
    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(Steps));
        UpdateCanGoNext();
    }

    public async Task ResetAsync()
    {
        using var _ = Helpers.AppLog.Scope("CreatePreset.ResetAsync");
        CurrentStep = 0;
        PresetName = string.Empty;
        SelectedMonitor = null;
        SelectedResolution = null;
        SelectedRefreshRate = 0;
        await LoadMonitorsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task LoadMonitorsAsync()
    {
        using var _ = Helpers.AppLog.Scope("CreatePreset.LoadMonitorsAsync");
        IsBusy = true;
        try
        {
            Monitors.Clear();
            var displays = await _services.Display.GetDisplaysAsync().ConfigureAwait(true);
            Helpers.AppLog.Info($"LoadMonitorsAsync got {displays.Count} displays");
            foreach (var display in displays)
            {
                Monitors.Add(MonitorOption.FromDisplayInfo(display));
                Helpers.AppLog.Info($"LoadMonitorsAsync added {display.DisplayName}");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task IdentifyAsync()
    {
        using var _ = Helpers.AppLog.Scope("CreatePreset.IdentifyAsync");
        var settings = await _services.SettingsStore.LoadAsync().ConfigureAwait(true);
        var displays = await _services.Display.GetDisplaysAsync().ConfigureAwait(true);
        await _services.Identifier.IdentifyAsync(displays, settings.IdentifyOverlaySeconds).ConfigureAwait(true);
    }

    [RelayCommand]
    public void SelectMonitor(MonitorOption? option)
    {
        Helpers.AppLog.Info($"CreatePreset.SelectMonitor {option?.DevicePath}");
        foreach (var item in Monitors)
        {
            item.IsSelected = option is not null && item.DevicePath == option.DevicePath;
        }

        SelectedMonitor = option;
    }

    [RelayCommand]
    public async Task NextAsync()
    {
        Helpers.AppLog.Info($"CreatePreset.NextAsync step={CurrentStep} mon={SelectedMonitor?.DevicePath}");
        if (CurrentStep == 0 && SelectedMonitor is not null)
        {
            await LoadModesAsync().ConfigureAwait(true);
            CurrentStep = 1;
            return;
        }

        if (CurrentStep == 1 && SelectedResolution is not null && SelectedRefreshRate > 0)
        {
            if (string.IsNullOrWhiteSpace(PresetName) && SelectedMonitor is not null)
            {
                PresetName = $"{SelectedMonitor.FriendlyName} {SelectedResolution} {SelectedRefreshRate}Hz";
            }

            CurrentStep = 2;
            UpdateSummary();
        }
    }

    [RelayCommand]
    public void Back()
    {
        if (CurrentStep > 0)
        {
            CurrentStep--;
        }
    }

    [RelayCommand]
    public void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public async Task CreateAsync()
    {
        using var _ = Helpers.AppLog.Scope("CreatePreset.CreateAsync",
            $"mon={SelectedMonitor?.DevicePath} res={SelectedResolution} hz={SelectedRefreshRate}");
        if (SelectedMonitor is null || SelectedResolution is null || SelectedRefreshRate <= 0)
        {
            return;
        }

        var parts = SelectedResolution.Split('×', 'x');
        if (parts.Length != 2 || !int.TryParse(parts[0].Trim(), out var width) || !int.TryParse(parts[1].Trim(), out var height))
        {
            await _services.Dialog.ShowErrorAsync("Invalid resolution", "Choose a real resolution.").ConfigureAwait(true);
            return;
        }

        var preset = new Preset
        {
            Name = PresetName.Trim(),
            DevicePath = SelectedMonitor.DevicePath,
            FriendlyMonitorName = SelectedMonitor.FriendlyName,
            Mode = new DisplayMode
            {
                Width = width,
                Height = height,
                RefreshRate = SelectedRefreshRate,
                BitsPerPel = 32
            },
            ScalePercent = ScalePercent
        };

        var errors = _validator.Validate(ToCore(preset));
        if (errors.Count > 0)
        {
            await _services.Dialog.ShowErrorAsync("Invalid preset", string.Join(Environment.NewLine, errors)).ConfigureAwait(true);
            return;
        }

        PresetCreated?.Invoke(this, preset);
    }

    private async Task LoadModesAsync()
    {
        using var _ = Helpers.AppLog.Scope("CreatePreset.LoadModesAsync", SelectedMonitor?.DisplayName);
        if (SelectedMonitor is null)
        {
            return;
        }

        CurrentDpi = SelectedMonitor.CurrentDpi;
        ScalePercent = SelectedMonitor.ScalePercent;
        _allModes = await _services.Display.GetSupportedModesAsync(SelectedMonitor.DisplayName).ConfigureAwait(true);
        Resolutions.Clear();
        foreach (var resolution in _allModes.Select(m => $"{m.Width} × {m.Height}").Distinct())
        {
            Resolutions.Add(resolution);
        }

        var current = $"{SelectedMonitor.CurrentMode.Width} × {SelectedMonitor.CurrentMode.Height}";
        SelectedResolution = Resolutions.Contains(current) ? current : Resolutions.FirstOrDefault();
        RebuildRefreshRates();
        if (RefreshRates.Contains(SelectedMonitor.CurrentMode.RefreshRate))
        {
            SelectedRefreshRate = SelectedMonitor.CurrentMode.RefreshRate;
        }
    }

    private void RebuildRefreshRates()
    {
        RefreshRates.Clear();
        if (SelectedResolution is null)
        {
            return;
        }

        var parts = SelectedResolution.Split('×', 'x');
        if (parts.Length != 2 || !int.TryParse(parts[0].Trim(), out var width) || !int.TryParse(parts[1].Trim(), out var height))
        {
            return;
        }

        foreach (var rate in _allModes.Where(m => m.Width == width && m.Height == height).Select(m => m.RefreshRate).Distinct().OrderByDescending(x => x))
        {
            RefreshRates.Add(rate);
        }

        if (!RefreshRates.Contains(SelectedRefreshRate))
        {
            SelectedRefreshRate = RefreshRates.FirstOrDefault();
        }
    }

    private void UpdateCanGoNext()
    {
        CanGoNext = CurrentStep switch
        {
            0 => SelectedMonitor is not null,
            1 => SelectedResolution is not null && SelectedRefreshRate > 0,
            _ => false
        };
    }

    private void UpdateSummary()
    {
        if (SelectedMonitor is null)
        {
            Summary = string.Empty;
            return;
        }

        Summary = $"{PresetName}\n{SelectedMonitor.FriendlyName}\n{SelectedResolution} @ {SelectedRefreshRate} Hz\nScale {ScalePercent}% · {CurrentDpi} DPI";
    }

    private static FluxDisplay.Core.Models.Preset ToCore(Preset preset) => new()
    {
        Id = preset.Id,
        Name = preset.Name,
        DevicePath = preset.DevicePath,
        FriendlyMonitorName = preset.FriendlyMonitorName,
        Mode = new FluxDisplay.Core.Models.DisplayMode
        {
            Width = preset.Mode.Width,
            Height = preset.Mode.Height,
            RefreshRate = preset.Mode.RefreshRate,
            BitsPerPel = preset.Mode.BitsPerPel,
            IsInterlaced = preset.Mode.IsInterlaced
        },
        ScalePercent = preset.ScalePercent,
        CreatedAt = preset.CreatedAt,
        LastAppliedAt = preset.LastAppliedAt
    };
}
