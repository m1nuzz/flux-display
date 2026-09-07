namespace FluxDisplay.Core.Models;

public sealed record DisplayMode
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int RefreshRate { get; init; }
    public required int BitsPerPel { get; init; }
    public bool IsInterlaced { get; init; }

    public override string ToString() => $"{Width} × {Height} @ {RefreshRate} Hz";
}
