namespace FluxDisplay.App.Models;

public sealed record NavigationItem(string Tag, string Title, string Glyph)
{
    public static readonly IReadOnlyList<NavigationItem> All =
    [
        new("presets", "Presets", "\uE7F8"),
        new("create", "Create", "\uE710"),
        new("settings", "Settings", "\uE713"),
        new("about", "About", "\uE946"),
    ];
}
