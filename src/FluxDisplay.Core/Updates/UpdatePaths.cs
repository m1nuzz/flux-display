namespace FluxDisplay.Core.Updates;

// Pure path policy for install detection. Guards against false prefixes:
// "<...>\FluxDisplayBackup" must not match "<...>\FluxDisplay".
public static class UpdatePaths
{
    public static bool IsInstalledCopy(string baseDirectory, string installDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory) || string.IsNullOrWhiteSpace(installDirectory))
        {
            return false;
        }

        var baseDir = baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var installDir = installDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return baseDir.Equals(installDir, StringComparison.OrdinalIgnoreCase) ||
            baseDir.StartsWith(installDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
