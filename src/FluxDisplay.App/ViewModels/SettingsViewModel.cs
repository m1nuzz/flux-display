using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxDisplay.App.Helpers;
using FluxDisplay.App.Models;
using FluxDisplay.App.Services;

namespace FluxDisplay.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        Themes = new ObservableCollection<ThemeOption>(ThemeOption.All);
        SelectedTheme = Themes[0];
    }

    public ObservableCollection<ThemeOption> Themes { get; }

    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _startMinimized = true;
    [ObservableProperty] private bool _confirmBeforeApply;
    [ObservableProperty] private int _identifyOverlaySeconds = 3;
    [ObservableProperty] private ThemeOption _selectedTheme = ThemeOption.All[0];
    [ObservableProperty] private string _version = typeof(App).Assembly.GetName().Version?.ToString() ?? "1.0.0";
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
        StartWithWindows = _services.Startup.IsEnabled || StartWithWindows;
        IsLoaded = true;
        settings.ApplyTheme();
    }

    partial void OnStartWithWindowsChanged(bool value) => _ = PersistAsync();
    partial void OnStartMinimizedChanged(bool value) => _ = PersistAsync();
    partial void OnConfirmBeforeApplyChanged(bool value) => _ = PersistAsync();
    partial void OnIdentifyOverlaySecondsChanged(int value) => _ = PersistAsync();
    partial void OnSelectedThemeChanged(ThemeOption value) => _ = PersistAsync();

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
            Theme = SelectedTheme.Theme
        };

        await _services.SettingsStore.SaveAsync(settings).ConfigureAwait(true);
        _services.Startup.SetEnabled(settings.StartWithWindows);
        settings.ApplyTheme();
    }
}
