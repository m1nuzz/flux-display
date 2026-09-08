using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxDisplay.App.Helpers;
using FluxDisplay.App.Models;
using FluxDisplay.App.Services;

namespace FluxDisplay.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;

    public MainViewModel(AppServices services)
    {
        _services = services;
        Presets = new PresetsPageViewModel(services);
        Create = new CreatePresetViewModel(services);
        SettingsViewModel = new SettingsViewModel(services);
        SelectedTag = "presets";
    }

    public PresetsPageViewModel Presets { get; }
    public CreatePresetViewModel Create { get; }
    public SettingsViewModel SettingsViewModel { get; }

    [ObservableProperty]
    private string _selectedTag = "presets";

    [ObservableProperty]
    private AppSettings _currentSettings = new();

    public async Task InitializeAsync()
    {
        CurrentSettings = await _services.SettingsStore.LoadAsync().ConfigureAwait(true);
        CurrentSettings.ApplyTheme();
        _services.Startup.SetEnabled(CurrentSettings.StartWithWindows);
        await Presets.ReloadAsync().ConfigureAwait(true);
        _services.Tray.Initialize();
        await SyncTrayAsync().ConfigureAwait(true);

        _services.Tray.OpenRequested += (_, _) => RequestShowWindow?.Invoke(this, EventArgs.Empty);
        _services.Tray.PresetApplyRequested += async (_, id) => await Presets.ApplyByIdAsync(id).ConfigureAwait(true);
        Presets.PresetsChanged += async (_, _) => await SyncTrayAsync().ConfigureAwait(true);
        Create.PresetCreated += async (_, preset) =>
        {
            await Presets.AddPresetAsync(preset).ConfigureAwait(true);
            Navigate("presets");
        };
    }

    public event EventHandler? RequestShowWindow;
    public event EventHandler<string>? NavigationRequested;

    [RelayCommand]
    public void Navigate(string tag)
    {
        SelectedTag = tag;
        NavigationRequested?.Invoke(this, tag);
        if (tag == "create")
        {
            _ = Create.ResetAsync();
        }
    }

    private async Task SyncTrayAsync()
    {
        var active = Presets.Items.FirstOrDefault(x => x.IsActive)?.Id;
        _services.Tray.UpdatePresets(Presets.Items.Select(x => x.Preset).ToList(), active);
        await Task.CompletedTask;
    }
}
