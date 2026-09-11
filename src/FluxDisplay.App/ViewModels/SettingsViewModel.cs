using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxDisplay.App.Helpers;
using FluxDisplay.App.Models;
using FluxDisplay.App.Services;
using FluxDisplay.Core.Updates;

namespace FluxDisplay.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        Themes = new ObservableCollection<ThemeOption>(ThemeOption.All);
        SelectedTheme = Themes[0];
        UpdateModes = new ObservableCollection<UpdateModeOption>(UpdateModeOption.All);
        SelectedUpdateMode = UpdateModes[0];
        RefreshVersionLabel();
        _services.UpdateStateChanged += OnUpdateState;
    }

    public ObservableCollection<ThemeOption> Themes { get; }
    public ObservableCollection<UpdateModeOption> UpdateModes { get; }

    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _startMinimized = true;
    [ObservableProperty] private bool _confirmBeforeApply;
    [ObservableProperty] private int _identifyOverlaySeconds = 3;
    [ObservableProperty] private ThemeOption _selectedTheme = ThemeOption.All[0];
    [ObservableProperty] private UpdateModeOption _selectedUpdateMode = UpdateModeOption.All[0];
    [ObservableProperty] private string _version = "1.0.0";
    [ObservableProperty] private string _updateStatus = string.Empty;
    [ObservableProperty] private bool _isUpdateBusy;
    public Uri GitHubUri { get; } = new("https://github.com/m1nuzz/flux-display");
    [ObservableProperty] private string _githubUrl = "https://github.com/m1nuzz/flux-display";
    [ObservableProperty] private bool _isLoaded;

    public async Task LoadAsync()
    {
        using var _ = Helpers.AppLog.Scope("Settings.LoadAsync");
        var settings = await _services.SettingsStore.LoadAsync().ConfigureAwait(true);
        IsLoaded = false;
        StartWithWindows = settings.StartWithWindows;
        StartMinimized = settings.StartMinimized;
        ConfirmBeforeApply = settings.ConfirmBeforeApply;
        IdentifyOverlaySeconds = Math.Clamp(settings.IdentifyOverlaySeconds, 1, 10);
        SelectedTheme = ThemeOption.FromTheme(settings.Theme);
        SelectedUpdateMode = UpdateModeOption.FromMode(settings.UpdateMode);
        _services.CachedUpdateMode = settings.UpdateMode;
        StartWithWindows = _services.Startup.IsEnabled || StartWithWindows;
        IsLoaded = true;
        settings.ApplyTheme();
    }

    partial void OnStartWithWindowsChanged(bool value) => _ = PersistAsync();
    partial void OnStartMinimizedChanged(bool value) => _ = PersistAsync();
    partial void OnConfirmBeforeApplyChanged(bool value) => _ = PersistAsync();
    partial void OnIdentifyOverlaySecondsChanged(int value) => _ = PersistAsync();
    partial void OnSelectedThemeChanged(ThemeOption value) => _ = PersistAsync();
    partial void OnSelectedUpdateModeChanged(UpdateModeOption value) => _ = PersistAsync();

    private void RefreshVersionLabel()
    {
        try
        {
            Version = UpdateVersion.FormatShort(_services.UpdateSource.CurrentVersion);
        }
        catch
        {
            Version = "1.0.0";
        }
    }

    // Orchestrator state fan-in. Reports can arrive on background threads
    // (download progress), so marshal to the UI thread.
    private void OnUpdateState(UpdateStateInfo info)
    {
        try
        {
            var queue = App.MainWindow?.DispatcherQueue;
            if (queue is not null && !queue.HasThreadAccess)
            {
                queue.TryEnqueue(() => ApplyUpdateState(info));
            }
            else
            {
                ApplyUpdateState(info);
            }
        }
        catch
        {
        }
    }

    private void ApplyUpdateState(UpdateStateInfo info)
    {
        if (!string.IsNullOrEmpty(info.Message))
        {
            UpdateStatus = info.State == UpdateState.NoUpdate
                ? $"{info.Message} ({Version})."
                : info.Message;
        }

        IsUpdateBusy = info.State is UpdateState.Checking or UpdateState.Downloading
            or UpdateState.Verifying or UpdateState.Installing;
        CheckUpdatesCommand.NotifyCanExecuteChanged();
    }

    private bool CanCheckUpdates() => IsLoaded && !IsUpdateBusy;

    [RelayCommand(CanExecute = nameof(CanCheckUpdates))]
    private async Task CheckUpdatesAsync()
    {
        if (!IsLoaded)
        {
            return;
        }

        try
        {
            await _services.Updates.CheckAsync(UpdateTrigger.Manual).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Helpers.AppLog.Error(ex, "Settings.CheckUpdates");
            UpdateStatus = "Update check failed";
        }
    }

    [RelayCommand]
    private async Task PersistAsync()
    {
        if (!IsLoaded)
        {
            return;
        }

        var settings = new AppSettings
        {
            StartWithWindows = StartWithWindows,
            StartMinimized = StartMinimized,
            ConfirmBeforeApply = ConfirmBeforeApply,
            IdentifyOverlaySeconds = Math.Clamp(IdentifyOverlaySeconds, 1, 10),
            Theme = SelectedTheme.Theme,
            UpdateMode = SelectedUpdateMode.Mode
        };

        await _services.SettingsStore.SaveAsync(settings).ConfigureAwait(true);
        _services.Startup.SetEnabled(settings.StartWithWindows);
        _services.CachedUpdateMode = settings.UpdateMode;
        settings.ApplyTheme();
    }
}
