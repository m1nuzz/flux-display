using FluxDisplay.Core.Updates;

namespace FluxDisplay.App.Models;

// Theme preference for the application.
public enum AppTheme
{
    System,
    Light,
    Dark
}

// Global application preferences persisted alongside presets.
// UpdateMode lives in Core so the JSON compat policy is unit-testable.
public sealed class AppSettings
{
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; } = true;
    public int IdentifyOverlaySeconds { get; set; } = 3;
    public bool ConfirmBeforeApply { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.System;
    public UpdateMode UpdateMode { get; set; } = UpdateMode.Automatic;
}
