using FluxDisplay.Core.Models;

namespace FluxDisplay.App.Models;

public sealed record ThemeOption(AppTheme Theme, string Label)
{
    public static readonly IReadOnlyList<ThemeOption> All =
    [
        new(AppTheme.System, "System"),
        new(AppTheme.Light, "Light"),
        new(AppTheme.Dark, "Dark"),
    ];

    public static ThemeOption FromTheme(AppTheme theme) =>
        All.FirstOrDefault(x => x.Theme == theme) ?? All[0];
}
