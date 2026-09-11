namespace FluxDisplay.Core.Updates;

// How the built-in updater behaves. Stored in settings.json as camelCase.
public enum UpdateMode
{
    // Check on startup; download and install silently (installed copies only).
    Automatic,
    // Check on startup; ask before downloading.
    NotifyOnly,
    // Never check.
    Disabled
}
