using System;
using Jellyfin.Plugin.Youtarr.Configuration;
using Jellyfin.Plugin.Youtarr.Providers;
using MediaBrowser.Controller.Entities.TV;
using Xunit;

namespace Jellyfin.Plugin.Youtarr.Tests.Providers;

public class YoutarrSeasonRegroupTaskTests
{
    [Fact]
    public void ResolveSeasonTarget_YearSeasonsOn_UsesPremiereYear()
    {
        var config = new PluginConfiguration { YearSeasons = true };

        var target = YoutarrSeasonRegroupTask.ResolveSeasonTarget(
            config,
            new DateTime(2024, 3, 15));

        Assert.Equal(2024, target.Index);
        Assert.Equal("Season 2024", target.Name);
    }

    [Fact]
    public void ResolveSeasonTarget_YearSeasonsOff_UsesSeasonOne()
    {
        var config = new PluginConfiguration { YearSeasons = false };

        var target = YoutarrSeasonRegroupTask.ResolveSeasonTarget(
            config,
            new DateTime(2024, 3, 15));

        Assert.Equal(1, target.Index);
        Assert.Equal("Season 1", target.Name);
    }

    [Fact]
    public void ResolveSeasonTarget_Undated_UsesSpecials()
    {
        var config = new PluginConfiguration { YearSeasons = true };

        var target = YoutarrSeasonRegroupTask.ResolveSeasonTarget(config, null);

        Assert.Equal(0, target.Index);
        Assert.Equal("Specials", target.Name);
    }

    [Fact]
    public void BuildEpisodeSeasonLinkage_ContainsFullIdentityTuple()
    {
        var seriesId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var premiereDate = new DateTime(2022, 8, 1);

        var linkage = YoutarrSeasonRegroupTask.BuildEpisodeSeasonLinkage(
            seriesId,
            "Channel Name",
            seasonId,
            2022,
            "Season 2022",
            premiereDate,
            premiereDate.Year);

        Assert.Equal(2022, linkage.ParentIndexNumber);
        Assert.Equal(seasonId, linkage.SeasonId);
        Assert.Equal("Season 2022", linkage.SeasonName);
        Assert.Equal(seriesId, linkage.SeriesId);
        Assert.Equal("Channel Name", linkage.SeriesName);
        Assert.Equal(premiereDate, linkage.PremiereDate);
        Assert.Equal(2022, linkage.ProductionYear);
    }

    [Fact]
    public void BuildEpisodeSeasonLinkage_UndatedVideo_KeepsDateAddedWithoutInventingProductionYear()
    {
        var dateAdded = new DateTime(2026, 9, 4);

        var linkage = YoutarrSeasonRegroupTask.BuildEpisodeSeasonLinkage(
            Guid.NewGuid(),
            "Channel Name",
            Guid.NewGuid(),
            0,
            "Specials",
            dateAdded,
            productionYear: null);

        Assert.Equal(dateAdded, linkage.PremiereDate);
        Assert.Null(linkage.ProductionYear);
    }

    [Fact]
    public void ApplyEpisodeSeasonLinkage_ReconcilesEpisodeIdentityTuple()
    {
        var seriesId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var season = new Season { Id = seasonId };
        var episode = new Episode();
        var premiereDate = new DateTime(2021, 6, 7);
        var linkage = YoutarrSeasonRegroupTask.BuildEpisodeSeasonLinkage(
            seriesId,
            "Series",
            seasonId,
            2021,
            "Season 2021",
            premiereDate,
            premiereDate.Year);

        var changed = YoutarrSeasonRegroupTask.ApplyEpisodeSeasonLinkage(episode, season, linkage);

        Assert.True(changed);
        Assert.Equal(seasonId, episode.ParentId);
        Assert.Equal(seasonId, episode.SeasonId);
        Assert.Equal(2021, episode.ParentIndexNumber);
        Assert.Equal("Season 2021", episode.SeasonName);
        Assert.Equal(seriesId, episode.SeriesId);
        Assert.Equal("Series", episode.SeriesName);
        Assert.Equal(premiereDate, episode.PremiereDate);
        Assert.Equal(2021, episode.ProductionYear);
        Assert.False(YoutarrSeasonRegroupTask.ApplyEpisodeSeasonLinkage(episode, season, linkage));
    }
}
