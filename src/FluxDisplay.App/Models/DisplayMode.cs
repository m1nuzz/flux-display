namespace FluxDisplay.App.Models;

// Win32-compatible result of ChangeDisplaySettingsEx. Values match the native DISP_CHANGE constants.
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

// Represents a single display mode entry with resolution, refresh and color depth.
public sealed record DisplayMode
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int RefreshRate { get; init; }
    public required int BitsPerPel { get; init; }
    public bool IsInterlaced { get; init; }

    public override string ToString() => $"{Width} × {Height} @ {RefreshRate} Hz";
}
