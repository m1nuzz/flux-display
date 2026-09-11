namespace FluxDisplay.Core.Updates;

// Explicit update lifecycle. Exactly one operation per process at a time.
public enum UpdateState
{
    Idle,
    Checking,
    UpdateAvailable,
    Downloading,
    Verifying,
    ReadyToInstall,
    Installing,
    NoUpdate,
    Cancelled,
    Failed
}

// State snapshot pushed to the UI.
public sealed record UpdateStateInfo(UpdateState State, string? Message, double? ProgressPercent);
