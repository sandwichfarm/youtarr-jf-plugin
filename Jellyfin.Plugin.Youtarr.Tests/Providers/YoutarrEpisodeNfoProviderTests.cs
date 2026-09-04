using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Youtarr;
using Jellyfin.Plugin.Youtarr.Configuration;
using Jellyfin.Plugin.Youtarr.Models;
using Jellyfin.Plugin.Youtarr.Providers;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Youtarr.Tests.Providers;

/// <summary>
/// Unit tests for <see cref="YoutarrEpisodeNfoProvider"/>. The config-dependent mapping
/// logic (season assignment, numbering, runtime, EPI-07 fallback, plot formatting) is tested
/// directly against the internal static <c>MapToEpisode</c>/<c>FormatDescription</c> methods so
/// no running Jellyfin server / Plugin.Instance is required. File-discovery and HasMetadata
/// behaviour is tested through <c>GetMetadata</c> with on-disk temp NFOs.
/// </summary>
public class YoutarrEpisodeNfoProviderTests
{
    private readonly Mock<ILogger<YoutarrEpisodeNfoProvider>> _logger = new();
    private readonly Mock<IDirectoryService> _directoryService = new();

    private YoutarrEpisodeNfoProvider CreateProvider() => new(_logger.Object);

    private static ItemInfo ItemInfoForPath(string path)
    {
        // ItemInfo has no parameterless ctor in 10.11.11; construct from a BaseItem then set Path.
        return new ItemInfo(new Episode()) { Path = path };
    }

    private static PluginConfiguration Config(
        bool yearSeasons = true,
        EpisodeNumberingScheme scheme = EpisodeNumberingScheme.Default,
        int maxLength = 500)
    {
        return new PluginConfiguration
        {
            YearSeasons = yearSeasons,
            EpisodeNumberingScheme = scheme,
            MaxDescriptionLength = maxLength,
        };
    }

    private static YoutarrVideoData Data(
        string? title = "A Title",
        string? plot = "A plot.",
        DateTime? premiere = null,
        DateTime? dateAdded = null,
        int? durationSeconds = null,
        int? runtimeMinutes = null,
        string? studio = null,
        string? youTubeId = null,
        string? mpaa = null,
        string[]? genres = null,
        string[]? tags = null)
    {
        return new YoutarrVideoData
        {
            Title = title,
            Plot = plot,
            PremiereDate = premiere,
            DateAdded = dateAdded,
            DurationInSeconds = durationSeconds,
            RuntimeMinutes = runtimeMinutes,
            Studio = studio,
            YouTubeId = youTubeId,
            MpaaRating = mpaa,
            Genres = genres ?? Array.Empty<string>(),
            Tags = tags ?? Array.Empty<string>(),
        };
    }

    // ---- GetMetadata: discovery / HasMetadata (T-02-08) ----

    [Fact]
    public void Name_ReturnsConstantsProviderName()
    {
        Assert.Equal(Constants.ProviderName, CreateProvider().Name);
    }

    [Fact]
    public async Task GetMetadata_NoNfoFile_HasMetadataFalse()
    {
        var dir = CreateTempDir();
        try
        {
            var video = Path.Combine(dir, "video.mp4");
            File.WriteAllText(video, string.Empty);

            var result = await CreateProvider().GetMetadata(
                ItemInfoForPath(video), _directoryService.Object, CancellationToken.None);

            Assert.False(result.HasMetadata);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task GetMetadata_MalformedNfo_HasMetadataFalse_NoThrow()
    {
        var dir = CreateTempDir();
        try
        {
            var video = Path.Combine(dir, "video.mp4");
            File.WriteAllText(video, string.Empty);
            File.WriteAllText(Path.Combine(dir, "video.nfo"), "<not valid xml", Encoding.UTF8);

            var result = await CreateProvider().GetMetadata(
                ItemInfoForPath(video), _directoryService.Object, CancellationToken.None);

            Assert.False(result.HasMetadata);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task GetMetadata_NonMovieRootNfo_HasMetadataFalse()
    {
        var dir = CreateTempDir();
        try
        {
            var video = Path.Combine(dir, "video.mp4");
            File.WriteAllText(video, string.Empty);
            File.WriteAllText(
                Path.Combine(dir, "video.nfo"),
                "<?xml version=\"1.0\"?><episodedetails><title>x</title></episodedetails>",
                Encoding.UTF8);

            var result = await CreateProvider().GetMetadata(
                ItemInfoForPath(video), _directoryService.Object, CancellationToken.None);

            Assert.False(result.HasMetadata);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task GetMetadata_ValidNfo_HasMetadataTrue()
    {
        var dir = CreateTempDir();
        try
        {
            var video = Path.Combine(dir, "video.mp4");
            File.WriteAllText(video, string.Empty);
            File.WriteAllText(
                Path.Combine(dir, "video.nfo"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><movie><title>My Episode</title><premiered>2024-03-15</premiered></movie>",
                Encoding.UTF8);

            var result = await CreateProvider().GetMetadata(
                ItemInfoForPath(video), _directoryService.Object, CancellationToken.None);

            Assert.True(result.HasMetadata);
            Assert.NotNull(result.Item);
            Assert.Equal("My Episode", result.Item!.Name);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- MapToEpisode: title / plot (EPI-01, EPI-04) ----

    [Fact]
    public void MapToEpisode_Title_MappedToName()
    {
        var ep = YoutarrEpisodeNfoProvider.MapToEpisode(Data(title: "Hello World"), Config());
        Assert.Equal("Hello World", ep.Name);
    }

    [Fact]
    public void FormatDescription_LongPlot_TruncatedToMaxLength()
    {
        var plot = new string('x', 1000);
        var formatted = YoutarrEpisodeNfoProvider.FormatDescription(plot, 500);
        Assert.NotNull(formatted);
        Assert.Equal(500, formatted!.Length);
    }

    [Fact]
    public void MapToEpisode_Plot_TruncatedToMaxLength()
    {
        var plot = new string('x', 1000);
        var ep = YoutarrEpisodeNfoProvider.MapToEpisode(Data(plot: plot), Config(maxLength: 500));
        Assert.NotNull(ep.Overview);
        Assert.Equal(500, ep.Overview!.Length);
    }

    [Fact]
    public void FormatDescription_Newlines_ConvertedToBr()
    {
        var formatted = YoutarrEpisodeNfoProvider.FormatDescription("line1\nline2", 500);
        Assert.Equal("line1<br>line2", formatted);
    }

    [Fact]
    public void MapToEpisode_Plot_NewlinesConverted()
    {
        var ep = YoutarrEpisodeNfoProvider.MapToEpisode(Data(plot: "a\nb"), Config());
        Assert.Contains("<br>", ep.Overview, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatDescription_NullOrEmpty_Passthrough()
    {
        Assert.Null(YoutarrEpisodeNfoProvider.FormatDescription(null, 500));
        Assert.Equal(string.Empty, YoutarrEpisodeNfoProvider.FormatDescription(string.Empty, 500));
    }

    // ---- Season assignment (LIB-03 / LIB-04) ----

    [Fact]
    public void MapToEpisode_ValidYear_ParentIndexNumberIsYear()
    {
        var ep = YoutarrEpisodeNfoProvider.MapToEpisode(
            Data(premiere: new DateTime(2024, 3, 15)), Config(yearSeasons: true));
        Assert.Equal(2024, ep.ParentIndexNumber);
        Assert.Equal("Season 2024", ep.SeasonName);
        Assert.Equal(2024, ep.ProductionYear);
        Assert.Equal(new DateTime(2024, 3, 15), ep.PremiereDate);
    }

    [Fact]
    public void MapToEpisode_YearSeasonsOff_ParentIndexNumber1()
    {
        var ep = YoutarrEpisodeNfoProvider.MapToEpisode(
            Data(premiere: new DateTime(2024, 3, 15)), Config(yearSeasons: false));
        Assert.Equal(1, ep.ParentIndexNumber);
        Assert.Equal("Season 1", ep.SeasonName);
    }

    // ---- EPI-07 fallback ----

    [Fact]
    public void MapToEpisode_MissingDate_ParentIndexNumber0()
    {
        var ep = YoutarrEpisodeNfoProvider.MapToEpisode(Data(premiere: null, dateAdded: null), Config());
        Assert.Equal(0, ep.ParentIndexNumber);
        Assert.Equal("Specials", ep.SeasonName);
    }

    [Fact]
    public void MapToEpisode_MissingDate_IndexNumberNull_PremiereNull()
    {
        var ep = YoutarrEpisodeNfoProvider.MapToEpisode(Data(premiere: null, dateAdded: null), Config());
        Assert.Null(ep.IndexNumber);
        Assert.Null(ep.PremiereDate);
    }

    [Fact]
    public void MapToEpisode_PremiereMissing_DateAddedValid_Season0_PremiereFromDateAdded()
    {
        // EPI-07 chain: premiered missing but dateadded present -> Season 0,
        // IndexNumber null, but PremiereDate set from DateAdded so the episode still has a date.
        var added = new DateTime(2026, 1, 2);
        var ep = YoutarrEpisodeNfoProvider.MapToEpisode(
            Data(premiere: null, dateAdded: added), Config());
        Assert.Equal(0, ep.ParentIndexNumber);
        Assert.Null(ep.IndexNumber);
        Assert.Equal(added, ep.PremiereDate);
    }

    // ---- Numbering (EPI-05) ----

    [Fact]
    public void MapToEpisode_YYYYMMDD_CorrectIndexNumber()
    {
        var ep = YoutarrEpisodeNfoProvider.MapToEpisode(
            Data(premiere: new DateTime(2024, 3, 15)),
            Config(scheme: EpisodeNumberingScheme.YYYYMMDD));
        Assert.Equal(20240315, ep.IndexNumber);
    }

    [Fact]
    public void MapToEpisode_Default_IndexNumberNull()
    {
        var ep = YoutarrEpisodeNfoProvider.MapToEpisode(
            Data(premiere: new DateTime(2024, 3, 15)),
            Config(scheme: EpisodeNumberingScheme.Default));
        Assert.Null(ep.IndexNumber);
    }

    // ---- Runtime (EPI-01, Pitfall 4) ----

    [Fact]
    public void MapToEpisode_DurationSeconds_RunTimeTicks()
    {
        var ep = YoutarrEpisodeNfoProvider.MapToEpisode(Data(durationSeconds: 300), Config());
        Assert.Equal(TimeSpan.FromSeconds(300).Ticks, ep.RunTimeTicks);
        Assert.Equal(3_000_000_000L, ep.RunTimeTicks);
    }

    [Fact]
    public void MapToEpisode_RuntimeMinutesOnly_RunTimeTicks()
    {
        var ep = YoutarrEpisodeNfoProvider.MapToEpisode(Data(durationSeconds: null, runtimeMinutes: 5), Config());
        Assert.Equal(TimeSpan.FromMinutes(5).Ticks, ep.RunTimeTicks);
        Assert.Equal(3_000_000_000L, ep.RunTimeTicks);
    }

    [Fact]
    public void MapToEpisode_DurationPreferredOverRuntime()
    {
        var ep = YoutarrEpisodeNfoProvider.MapToEpisode(
            Data(durationSeconds: 300, runtimeMinutes: 99), Config());
        Assert.Equal(TimeSpan.FromSeconds(300).Ticks, ep.RunTimeTicks);
    }

    // ---- Provider ids / rating / studio / collections (EPI-02, EPI-03, EPI-06) ----

    [Fact]
    public void MapToEpisode_YouTubeId_InProviderIds()
    {
        var ep = YoutarrEpisodeNfoProvider.MapToEpisode(Data(youTubeId: "dQw4w9WgXcQ"), Config());
        Assert.Equal("dQw4w9WgXcQ", ep.ProviderIds[Constants.YouTubeProviderId]);
    }

    [Fact]
    public void MapToEpisode_Mpaa_OfficialRating()
    {
        var ep = YoutarrEpisodeNfoProvider.MapToEpisode(Data(mpaa: "PG-13"), Config());
        Assert.Equal("PG-13", ep.OfficialRating);
    }

    [Fact]
    public void MapToEpisode_NoMpaa_OfficialRatingNull()
    {
        var ep = YoutarrEpisodeNfoProvider.MapToEpisode(Data(mpaa: null), Config());
        Assert.Null(ep.OfficialRating);
    }

    [Fact]
    public void MapToEpisode_Genres_PopulatedFromNfo()
    {
        var ep = YoutarrEpisodeNfoProvider.MapToEpisode(
            Data(genres: new[] { "Music", "Comedy" }), Config());
        Assert.Equal(new[] { "Music", "Comedy" }, ep.Genres);
    }

    [Fact]
    public void MapToEpisode_Tags_PopulatedFromNfo()
    {
        var ep = YoutarrEpisodeNfoProvider.MapToEpisode(
            Data(tags: new[] { "youtube", "vlog" }), Config());
        Assert.Equal(new[] { "youtube", "vlog" }, ep.Tags);
    }

    [Fact]
    public void MapToEpisode_Studio_PopulatedFromNfo()
    {
        var ep = YoutarrEpisodeNfoProvider.MapToEpisode(Data(studio: "My Channel"), Config());
        Assert.Equal(new[] { "My Channel" }, ep.Studios);
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "youtarr-ep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
