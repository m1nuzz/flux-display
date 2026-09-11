using FluxDisplay.Core.Updates;

namespace FluxDisplay.App.Models;

public sealed record UpdateModeOption(UpdateMode Mode, string Label)
{
    public static readonly IReadOnlyList<UpdateModeOption> All =
    [
        new(UpdateMode.Automatic, "Download and install automatically"),
        new(UpdateMode.NotifyOnly, "Notify me, ask before updating"),
        new(UpdateMode.Disabled, "Off"),
    ];

    public static UpdateModeOption FromMode(UpdateMode mode) =>
        All.FirstOrDefault(x => x.Mode == mode) ?? All[0];

    public override string ToString() => Label;
}
