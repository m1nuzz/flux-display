namespace FluxDisplay.App.Models;

public sealed record ScaleOption(int Percent, string Label)
{
    public static readonly IReadOnlyList<ScaleOption> All =
    [
        new(100, "100%"),
        new(125, "125%"),
        new(150, "150%"),
        new(175, "175%"),
        new(200, "200%"),
        new(225, "225%"),
        new(250, "250%"),
        new(300, "300%"),
    ];

    public static ScaleOption FromPercent(int percent) =>
        All.FirstOrDefault(x => x.Percent == percent) ?? new ScaleOption(percent, $"{percent}%");

    public override string ToString() => Label;
}
