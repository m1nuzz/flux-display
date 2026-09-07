namespace FluxDisplay.Core.Models;

public enum AppTheme
{
    System,
    Light,
    Dark
}

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; } = true;
    public int IdentifyOverlaySeconds { get; set; } = 3;
    public bool ConfirmBeforeApply { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.System;
}
