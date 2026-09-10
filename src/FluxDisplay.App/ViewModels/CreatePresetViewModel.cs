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

    public CreatePresetViewModel(AppServices services)
    {
        _services = services;
        Monitors = new ObservableCollection<MonitorOption>();
        TargetDrafts = new ObservableCollection<MonitorSettingsDraft>();
    }

    public ObservableCollection<MonitorOption> Monitors { get; }
    public ObservableCollection<MonitorSettingsDraft> TargetDrafts { get; }
    public ObservableCollection<int> ScaleOptions { get; } = [100, 125, 150, 175, 200, 225, 250];

    [ObservableProperty] private int _currentStep;
    [ObservableProperty] private MonitorOption? _selectedMonitor;
    [ObservableProperty] private string _presetName = string.Empty;
    [ObservableProperty] private bool _canGoNext;
    [ObservableProperty] private bool _canCreate;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public IReadOnlyList<StepInfo> Steps =>
    [
        new(0, "Choose displays", "Pick one or more monitors", CurrentStep > 0, CurrentStep == 0),
        new(1, "Display settings", "Resolution and refresh", CurrentStep > 1, CurrentStep == 1),
        new(2, "Preset name", "Name and confirm", CurrentStep > 2, CurrentStep == 2),
    ];

    public event EventHandler<Preset>? PresetCreated;
    public event EventHandler? Cancelled;

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
        TargetDrafts.Clear();
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
            var number = 1;
            foreach (var display in displays)
            {
                var option = MonitorOption.FromDisplayInfo(display);
                option.DisplayNumber = number++;
                Monitors.Add(option);
                Helpers.AppLog.Info($"LoadMonitorsAsync added #{option.DisplayNumber} {display.DisplayName}");
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
        if (option is null)
            return;

        Helpers.AppLog.Info($"CreatePreset.SelectMonitor {option.DevicePath} -> {!option.IsSelected}");
        option.IsSelected = !option.IsSelected;
        SelectedMonitor = Monitors.FirstOrDefault(item => item.IsSelected);
        UpdateCanGoNext();
    }

    [RelayCommand]
    public async Task NextAsync()
    {
        var selected = SelectedMonitors().ToList();
        Helpers.AppLog.Info($"CreatePreset.NextAsync step={CurrentStep} count={selected.Count}");
        if (CurrentStep == 0 && selected.Count > 0)
        {
            await LoadModesAsync(selected).ConfigureAwait(true);
            CurrentStep = 1;
            return;
        }

        if (CurrentStep == 1 && TargetDrafts.All(d => d.IsComplete) && TargetDrafts.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(PresetName))
                PresetName = BuildDefaultName();
            CurrentStep = 2;
            UpdateSummary();
        }
    }

    [RelayCommand]
    public void Back()
    {
        if (CurrentStep > 0)
            CurrentStep--;
    }

    [RelayCommand]
    public void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public async Task CreateAsync()
    {
        using var _ = Helpers.AppLog.Scope("CreatePreset.CreateAsync", $"targets={TargetDrafts.Count}");
        var targets = new List<PresetTarget>();
        foreach (var draft in TargetDrafts)
        {
            var mode = draft.TryBuildMode();
            if (mode is null)
            {
                await _services.Dialog.ShowErrorAsync("Invalid resolution", "Choose a real resolution for every display.").ConfigureAwait(true);
                return;
            }

            targets.Add(new PresetTarget
            {
                DevicePath = draft.Monitor.DevicePath,
                FriendlyMonitorName = draft.Monitor.FriendlyName,
                Mode = mode,
                ScalePercent = draft.ScalePercent
            });
        }

        if (targets.Count == 0)
            return;

        var primary = targets[0];
        var preset = new Preset
        {
            Name = PresetName.Trim(),
            DevicePath = primary.DevicePath,
            FriendlyMonitorName = primary.FriendlyMonitorName,
            Mode = primary.Mode,
            ScalePercent = primary.ScalePercent,
            Targets = targets
        };

        var errors = _validator.Validate(ToCore(preset));
        if (errors.Count > 0)
        {
            await _services.Dialog.ShowErrorAsync("Invalid preset", string.Join(Environment.NewLine, errors)).ConfigureAwait(true);
            return;
        }

        PresetCreated?.Invoke(this, preset);
    }

    private IEnumerable<MonitorOption> SelectedMonitors() => Monitors.Where(item => item.IsSelected);

    private async Task LoadModesAsync(IReadOnlyList<MonitorOption> selected)
    {
        using var _ = Helpers.AppLog.Scope("CreatePreset.LoadModesAsync", $"count={selected.Count}");
        TargetDrafts.Clear();
        foreach (var monitor in selected)
        {
            var draft = new MonitorSettingsDraft(monitor, ScaleOptions);
            draft.PropertyChanged += (_, _) =>
            {
                UpdateCanGoNext();
                UpdateSummary();
            };
            var modes = await _services.Display.GetSupportedModesAsync(monitor.DisplayName).ConfigureAwait(true);
            draft.ApplyModes(modes);
            TargetDrafts.Add(draft);
            Helpers.AppLog.Info($"LoadModesAsync {monitor.DisplayName} modes={modes.Count} res={draft.SelectedResolution} hz={draft.SelectedRefreshRate}");
        }

        UpdateCanGoNext();
    }

    private string BuildDefaultName()
    {
        if (TargetDrafts.Count == 1)
        {
            var draft = TargetDrafts[0];
            return $"{draft.Monitor.FriendlyName} {draft.SelectedResolution} {draft.SelectedRefreshRate}Hz";
        }

        var names = string.Join(" + ", TargetDrafts.Select(d => d.Monitor.FriendlyName));
        return names.Length <= PresetValidator.MaxNameLength ? names : $"{TargetDrafts.Count} displays";
    }

    private void UpdateCanGoNext()
    {
        CanGoNext = CurrentStep switch
        {
            0 => Monitors.Any(item => item.IsSelected),
            1 => TargetDrafts.Count > 0 && TargetDrafts.All(d => d.IsComplete),
            _ => false
        };
    }

    private void UpdateSummary()
    {
        if (TargetDrafts.Count == 0)
        {
            Summary = string.Empty;
            return;
        }

        var lines = new List<string> { PresetName };
        foreach (var draft in TargetDrafts)
            lines.Add($"{draft.Monitor.FriendlyName}: {draft.SelectedResolution} @ {draft.SelectedRefreshRate} Hz · {draft.ScalePercent}%");
        Summary = string.Join('\n', lines);
    }

    private static FluxDisplay.Core.Models.Preset ToCore(Preset preset)
    {
        preset.EnsureTargets();
        return new FluxDisplay.Core.Models.Preset
        {
            Id = preset.Id,
            Name = preset.Name,
            DevicePath = preset.DevicePath,
            FriendlyMonitorName = preset.FriendlyMonitorName,
            Mode = ToCoreMode(preset.Mode),
            ScalePercent = preset.ScalePercent,
            Targets = preset.GetTargets().Select(ToCoreTarget).ToList(),
            CreatedAt = preset.CreatedAt,
            LastAppliedAt = preset.LastAppliedAt
        };
    }

    private static FluxDisplay.Core.Models.PresetTarget ToCoreTarget(PresetTarget target) => new()
    {
        DevicePath = target.DevicePath,
        FriendlyMonitorName = target.FriendlyMonitorName,
        Mode = ToCoreMode(target.Mode),
        ScalePercent = target.ScalePercent
    };

    private static FluxDisplay.Core.Models.DisplayMode ToCoreMode(DisplayMode mode) => new()
    {
        Width = mode.Width,
        Height = mode.Height,
        RefreshRate = mode.RefreshRate,
        BitsPerPel = mode.BitsPerPel,
        IsInterlaced = mode.IsInterlaced
    };
}
