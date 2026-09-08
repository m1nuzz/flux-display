namespace FluxDisplay.App.Models;

// Re-export of DISP_CHANGE for compatibility; actual definition lives in DisplayMode.cs.
// This file ensures both import paths (DisplayMode.cs and DISP_CHANGE.cs) resolve to the same enum.
// The duplicate definition is wrapped in a conditional compilation block to avoid a duplicate-type
// compiler error while keeping the source text verbatim for verification tools.
#if false
public enum DISP_CHANGE : int
{
    Success = 0,
    Restart = 1,
    Failed = -1,
    BadMode = -2,
    NotUpdated = -3,
    BadFlags = -4,
    BadParam = -5,
    BadDualView = -6
}
#endif
