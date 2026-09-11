using FluxDisplay.Core.Updates;

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

    // Update subsystem: metadata source, streaming downloader and the
    // orchestrator owning the mode matrix and single-flight. Dialogs reach it
    // only via the prompter callback.
    public UpdateService UpdateSource { get; }
    public UpdateOrchestrator Updates { get; }

    // Last known update mode (settings.json). Set at startup and on every
    // settings change; the orchestrator reads it through this cache because
    // settings load is async and the provider must stay synchronous.
    public UpdateMode CachedUpdateMode { get; set; } = UpdateMode.Automatic;

    public event Action<UpdateStateInfo>? UpdateStateChanged;

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

        var http = UpdateService.CreateSharedHttpClient(
            typeof(AppServices).Assembly.GetName().Version ?? new Version(1, 0, 0, 0));
        UpdateSource = new UpdateService(http);
        var downloader = new UpdateDownloader(http);
        Updates = new UpdateOrchestrator(
            UpdateSource,
            downloader,
            new UpdateInstaller(),
            new DialogUpdatePrompter(Dialog),
            () => CachedUpdateMode,
            () => UpdateSource.IsInstalledCopy,
            () => UpdateSource.CurrentVersion,
            s => UpdateStateChanged?.Invoke(s),
            m => Helpers.AppLog.Info(m));
    }

    public static AppServices Initialize()
    {
        Current = new AppServices();
        return Current;
    }

    private sealed class DialogUpdatePrompter : IUpdatePrompter
    {
        private readonly IDialogService _dialog;

        public DialogUpdatePrompter(IDialogService dialog)
        {
            _dialog = dialog;
        }

        public Task<bool> ConfirmInstallAsync(UpdateInfo update, bool isInstalledCopy, CancellationToken ct)
        {
            var target = isInstalledCopy
                ? "Download and install it now?"
                : "This is a portable copy, so the installer will create/update the installed copy. Continue?";
            // Compat path without a digest: informed consent requires telling
            // the user the file cannot be verified — a bare "Update?" is not
            // enough.
            if (update.Digest is null)
            {
                target += " Warning: this release has no checksum, so the download cannot be verified.";
            }

            return _dialog.ShowConfirmAsync(
                "Update available",
                $"FluxDisplay {UpdateVersion.FormatShort(update.Version)} is available. {target}",
                "Update",
                "Later");
        }
    }
}
