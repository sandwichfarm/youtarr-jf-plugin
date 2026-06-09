using Jellyfin.Plugin.Youtarr.Configuration;
using Xunit;

namespace Jellyfin.Plugin.Youtarr.Tests.Configuration;

/// <summary>
/// Unit tests for <see cref="PluginConfiguration"/> Phase 2 settings: ensures the
/// defaults match research (year-seasons on, Default numbering, 500-char descriptions).
/// </summary>
public class PluginConfigurationTests
{
    [Fact]
    public void Defaults_YearSeasons_IsTrue()
    {
        var config = new PluginConfiguration();
        Assert.True(config.YearSeasons);
    }

    [Fact]
    public void Defaults_EpisodeNumberingScheme_IsDefault()
    {
        var config = new PluginConfiguration();
        Assert.Equal(EpisodeNumberingScheme.Default, config.EpisodeNumberingScheme);
    }

    [Fact]
    public void Defaults_MaxDescriptionLength_Is500()
    {
        var config = new PluginConfiguration();
        Assert.Equal(500, config.MaxDescriptionLength);
    }

    [Fact]
    public void EpisodeNumberingScheme_EnumValues_AreStable()
    {
        Assert.Equal(0, (int)EpisodeNumberingScheme.Default);
        Assert.Equal(1, (int)EpisodeNumberingScheme.YYYYMMDD);
    }
}
