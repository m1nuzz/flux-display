using Xunit;
using FluxDisplay.Core.Updates;

namespace FluxDisplay.Core.Tests;

public sealed class UpdatePathsTests
{
    [Fact]
    public void Exact_install_dir_matches()
    {
        var dir = Path.Combine("C:", "Users", "u", "AppData", "Local", "Programs", "FluxDisplay");
        Assert.True(UpdatePaths.IsInstalledCopy(dir, dir));
    }

    [Fact]
    public void Subdirectory_matches()
    {
        var install = Path.Combine("C:", "App", "FluxDisplay");
        Assert.True(UpdatePaths.IsInstalledCopy(Path.Combine(install, "sub"), install));
    }

    [Fact]
    public void Backup_sibling_does_not_match()
    {
        var install = Path.Combine("C:", "App", "FluxDisplay");
        Assert.False(UpdatePaths.IsInstalledCopy(install + "Backup", install));
    }

    [Fact]
    public void Other_directory_does_not_match()
    {
        var install = Path.Combine("C:", "App", "FluxDisplay");
        Assert.False(UpdatePaths.IsInstalledCopy(Path.Combine("D:", "portable"), install));
    }

    [Fact]
    public void Format_short_drops_zero_revision()
    {
        Assert.Equal("1.2.0", UpdateVersion.FormatShort(new Version(1, 2, 0, 0)));
        Assert.Equal("1.2.3", UpdateVersion.FormatShort(new Version(1, 2, 3)));
    }
}
