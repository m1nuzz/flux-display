namespace FluxDisplay.Core.Updates;

// Strict release-tag versions. Only the stable `vX.Y.Z` / `X.Y.Z` shape is
// accepted; prerelease or non-numeric suffixes are rejected until a SemVer
// prerelease policy exists. Comparison is done on four normalized components,
// so `1.2.3` equals assembly `1.2.3.0`.
public static class UpdateVersion
{
    public static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text.Substring(1);
        }

        var parts = text.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        var numbers = new int[4];
        for (var i = 0; i < 3; i++)
        {
            var part = parts[i];
            if (part.Length == 0 || part.Length > 5 || !part.All(char.IsDigit))
            {
                return false;
            }

            numbers[i] = int.Parse(part);
        }

        version = new Version(numbers[0], numbers[1], numbers[2], 0);
        return true;
    }

    // Normalizes any assembly-style version to four components for comparison.
    public static Version Normalize(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new Version(
            Math.Max(0, version.Major),
            Math.Max(0, version.Minor),
            Math.Max(0, version.Build),
            Math.Max(0, version.Revision));
    }

    // <0 latest is older, 0 equal, >0 latest is newer.
    public static int Compare(Version current, Version latest) =>
        Normalize(current).CompareTo(Normalize(latest));

    // Display form: "1.2.0", never "1.2.0.0".
    public static string FormatShort(Version version)
    {
        var n = Normalize(version);
        return n.Revision == 0 ? n.ToString(3) : n.ToString();
    }
}
