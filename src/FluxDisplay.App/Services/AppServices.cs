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
            new SettingsInstallHistory(SettingsStore),
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

    private sealed class SettingsInstallHistory : IInstallHistory
    {
        private readonly SettingsRepository _store;

        public SettingsInstallHistory(SettingsRepository store)
        {
            _store = store;
        }

        public async Task<string?> GetLastInstalledAsync(CancellationToken ct)
        {
            var settings = await _store.LoadAsync(ct).ConfigureAwait(false);
            return settings.LastInstalledUpdateVersion;
        }

        public async Task RecordInstalledAsync(string? version, CancellationToken ct)
        {
            var settings = await _store.LoadAsync(ct).ConfigureAwait(false);
            settings.LastInstalledUpdateVersion = version;
            await _store.SaveAsync(settings, ct).ConfigureAwait(false);
        }
    }

    private sealed class DialogUpdatePrompter : IUpdatePrompter
    {
        private readonly IDialogService _dialog;

        public DialogUpdatePrompter(IDialogService dialog)
        {
            _dialog = dialog;
        }

        public async Task<bool> ConfirmInstallAsync(UpdateInfo update, bool isInstalledCopy, CancellationToken ct)
        {
            // A modal dialog on a hidden window is invisible and unclickable
            // (and an open-but-invisible popup keeps composition work going).
            // Bring the main window out first.
            try
            {
                if (App.MainWindow is MainWindow main)
                {
                    main.ShowFromTray();
                    await Task.Delay(600, ct).ConfigureAwait(false);
                }
            }
            catch
            {
            }

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

            return await _dialog.ShowConfirmAsync(
                "Update available",
                $"FluxDisplay {UpdateVersion.FormatShort(update.Version)} is available. {target}",
                "Update",
                "Later").ConfigureAwait(false);
        }
    }
}
