using Xunit;
using FluxDisplay.Core.Updates;

namespace FluxDisplay.Core.Tests;

// Strict tag parsing and four-component comparison policy.
public sealed class UpdateVersionTests
{
    [Theory]
    [InlineData("v1.2.3", 1, 2, 3, 0)]
    [InlineData("1.2.3", 1, 2, 3, 0)]
    [InlineData("V10.0.1", 10, 0, 1, 0)]
    [InlineData("  v2.0.0  ", 2, 0, 0, 0)]
    public void Valid_tags_parse(string tag, int major, int minor, int build, int revision)
    {
        Assert.True(UpdateVersion.TryParseTag(tag, out var version));
        Assert.Equal(new Version(major, minor, build, revision), UpdateVersion.Normalize(version));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("v1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("v1.2.3-beta")]
    [InlineData("1.2.3+build")]
    [InlineData("vv1.2.3")]
    [InlineData("v")]
    [InlineData("latest")]
    [InlineData("v1.2.x")]
    [InlineData("v-1.2.3")]
    public void Invalid_tags_are_rejected(string? tag)
    {
        Assert.False(UpdateVersion.TryParseTag(tag, out _));
    }

    [Fact]
    public void Three_part_tag_equals_four_part_assembly()
    {
        Assert.True(UpdateVersion.TryParseTag("1.2.3", out var tag));
        Assert.Equal(0, UpdateVersion.Compare(new Version(1, 2, 3, 0), tag));
    }

    [Fact]
    public void Newer_latest_compares_positive()
    {
        Assert.True(UpdateVersion.TryParseTag("v1.2.4", out var latest));
        Assert.True(UpdateVersion.Compare(new Version(1, 2, 3, 0), latest) < 0);
    }

    [Fact]
    public void Installed_newer_than_latest_is_downgrade_case()
    {
        Assert.True(UpdateVersion.TryParseTag("v1.2.3", out var latest));
        Assert.True(UpdateVersion.Compare(new Version(2, 0, 0, 0), latest) > 0);
    }

    [Fact]
    public void Older_latest_compares_positive()
    {
        Assert.True(UpdateVersion.TryParseTag("v1.2.2", out var latest));
        Assert.True(UpdateVersion.Compare(new Version(1, 2, 3, 0), latest) > 0);
    }
}
