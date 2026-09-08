namespace FluxDisplay.App.Services;

public sealed class AppServices
{
    public static AppServices Current { get; private set; } = null!;

    public IDisplayService Display { get; }
    public IPresetRepository Presets { get; }
    public IDialogService Dialog { get; }
    public ITrayService Tray { get; }
    public IPresetApplier Applier { get; }
    public IStartupManager Startup { get; }
    public IMonitorIdentifier Identifier { get; }
    public SettingsRepository SettingsStore { get; }

    private AppServices()
    {
        Display = new DisplayService();
        Presets = new PresetRepository();
        Dialog = new DialogService();
        Tray = new TrayService();
        Applier = new PresetApplier(Display, Presets, Dialog, Tray);
        Startup = new StartupManager();
        Identifier = new MonitorIdentifier();
        SettingsStore = new SettingsRepository();
    }

    public static AppServices Initialize()
    {
        Current = new AppServices();
        return Current;
    }
}
