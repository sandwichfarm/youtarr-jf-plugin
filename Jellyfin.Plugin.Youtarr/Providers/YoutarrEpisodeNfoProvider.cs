using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Youtarr.Configuration;
using Jellyfin.Plugin.Youtarr.Models;
using Jellyfin.Plugin.Youtarr.Parsers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Youtarr.Providers;

/// <summary>
/// Provides Episode metadata for Youtarr-downloaded videos by reading the co-located
/// <c>&lt;movie&gt;</c> NFO sidecar and mapping it onto a Jellyfin <see cref="Episode"/>.
/// Maps title, plot (truncated, EPI-04), premiere date, runtime, genres, tags, the YouTube
/// provider id (EPI-03), and content rating (EPI-06); assigns the season by upload year with the
/// LIB-04 flatten toggle; applies the EPI-05 numbering scheme; and routes undated videos to
/// Season 0 via the EPI-07 fallback chain.
///
/// <para>The config-dependent mapping lives in the <c>internal static</c>
/// <see cref="MapToEpisode"/> and <see cref="FormatDescription"/> methods (taking an explicit
/// <see cref="PluginConfiguration"/>) so they are unit-testable without constructing
/// <c>Plugin.Instance</c>; <see cref="GetMetadata"/> is a thin wrapper that reads live config and
/// delegates to them.</para>
/// </summary>
public class YoutarrEpisodeNfoProvider : ILocalMetadataProvider<Episode>, IHasItemChangeMonitor
{
    private readonly ILogger<YoutarrEpisodeNfoProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="YoutarrEpisodeNfoProvider"/> class.
    /// </summary>
    /// <param name="logger">Injected logger.</param>
    public YoutarrEpisodeNfoProvider(ILogger<YoutarrEpisodeNfoProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => Constants.ProviderName;

    /// <inheritdoc />
    public Task<MetadataResult<Episode>> GetMetadata(
        ItemInfo info,
        IDirectoryService directoryService,
        CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Episode>();

        var videoPath = info.Path;
        if (string.IsNullOrWhiteSpace(videoPath))
        {
            return Task.FromResult(result); // HasMetadata stays false
        }

        // Same-basename sidecar resolves identically for flat (CMP-01) and nested (CMP-02) layouts.
        var nfoPath = Path.ChangeExtension(videoPath, ".nfo");
        if (!File.Exists(nfoPath))
        {
            return Task.FromResult(result); // No NFO → HasMetadata false.
        }

        YoutarrVideoData? data;
        try
        {
            // T-02-08: a single malformed/huge NFO must never crash the library scan.
            data = YoutarrNfoParser.Parse(nfoPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Youtarr] Failed to parse episode NFO '{Nfo}'; skipping.", nfoPath);
            return Task.FromResult(result); // HasMetadata false.
        }

        if (data is null)
        {
            // Not a Youtarr <movie> NFO (e.g. <episodedetails>); leave to other providers.
            return Task.FromResult(result);
        }

        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();

        if (data.PremiereDate is null && data.DateAdded is null)
        {
            _logger.LogWarning("[Youtarr] No usable date for '{Video}'; placing in Season 0.", videoPath);
        }
        else if (data.PremiereDate is null)
        {
            _logger.LogWarning(
                "[Youtarr] Missing/invalid <premiered> in '{Nfo}'; using <dateadded> for date, Season 0.",
                nfoPath);
        }

        result.Item = MapToEpisode(data, config);
        result.HasMetadata = true;
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public bool HasChanged(BaseItem item, IDirectoryService directoryService)
    {
        if (item?.Path is null)
        {
            return false;
        }

        var nfoPath = Path.ChangeExtension(item.Path, ".nfo");
        if (!File.Exists(nfoPath))
        {
            return false;
        }

        // Re-read only when the sidecar has been modified since Jellyfin last saved the item.
        return File.GetLastWriteTimeUtc(nfoPath) > item.DateLastSaved;
    }

    /// <summary>
    /// Maps a parsed <see cref="YoutarrVideoData"/> onto a Jellyfin <see cref="Episode"/> using
    /// the supplied configuration. Internal + static so it can be unit-tested directly without a
    /// running server. Implements LIB-03/LIB-04 season assignment, EPI-05 numbering, the EPI-07
    /// date fallback chain (premiered → dateadded → Season 0), and the full field map.
    /// </summary>
    /// <param name="data">The parsed NFO data.</param>
    /// <param name="config">The active plugin configuration.</param>
    /// <returns>A populated <see cref="Episode"/>.</returns>
    internal static Episode MapToEpisode(YoutarrVideoData data, PluginConfiguration config)
    {
        var episode = new Episode
        {
            // EPI-01: title + plot (EPI-04 truncation / newline normalisation).
            Name = data.Title,
            Overview = FormatDescription(data.Plot, config.MaxDescriptionLength),
        };

        if (data.PremiereDate.HasValue)
        {
            // EPI-01: valid upload date → real season/number.
            var date = data.PremiereDate.Value;
            episode.PremiereDate = date;
            episode.ProductionYear = date.Year;

            // LIB-03 / LIB-04: year-seasons toggle. Off → flatten into Season 1.
            episode.ParentIndexNumber = config.YearSeasons ? date.Year : 1;

            // EPI-05: numbering scheme. YYYYMMDD → upload-date integer; Default → auto-sequence.
            episode.IndexNumber = config.EpisodeNumberingScheme == EpisodeNumberingScheme.YYYYMMDD
                ? (date.Year * 10000) + (date.Month * 100) + date.Day
                : null;
        }
        else
        {
            // EPI-07 fallback chain. No valid <premiered>.
            // Always Season 0 (Specials), no episode number — never trust <dateadded> for the year.
            episode.ParentIndexNumber = 0;
            episode.IndexNumber = null;

            // …but if <dateadded> is present, still give the episode a date so it sorts sensibly.
            episode.PremiereDate = data.DateAdded; // null when both are missing.
        }

        // EPI-01: runtime — prefer the precise durationinseconds, else runtime minutes.
        // Pitfall 4: use TimeSpan (long arithmetic) — never a manual ticks multiply (T-02-11).
        if (data.DurationInSeconds.HasValue)
        {
            episode.RunTimeTicks = TimeSpan.FromSeconds(data.DurationInSeconds.Value).Ticks;
        }
        else if (data.RuntimeMinutes.HasValue)
        {
            episode.RunTimeTicks = TimeSpan.FromMinutes(data.RuntimeMinutes.Value).Ticks;
        }

        // EPI-02: genres / tags.
        if (data.Genres.Count > 0)
        {
            episode.Genres = data.Genres.ToArray();
        }

        if (data.Tags.Count > 0)
        {
            episode.Tags = data.Tags.ToArray();
        }

        // EPI-03: YouTube provider id.
        if (!string.IsNullOrWhiteSpace(data.YouTubeId))
        {
            episode.ProviderIds[Constants.YouTubeProviderId] = data.YouTubeId;
        }

        // EPI-06: content rating.
        if (!string.IsNullOrWhiteSpace(data.MpaaRating))
        {
            episode.OfficialRating = data.MpaaRating;
        }

        // Studio (channel name).
        if (!string.IsNullOrWhiteSpace(data.Studio))
        {
            episode.Studios = new[] { data.Studio };
        }

        return episode;
    }

    /// <summary>
    /// EPI-04: truncates a description to <paramref name="maxLength"/> characters and converts
    /// literal newlines to <c>&lt;br&gt;</c> for Jellyfin display. Null/empty input is returned
    /// unchanged. Internal + static for direct unit testing.
    /// </summary>
    /// <param name="description">The raw plot text.</param>
    /// <param name="maxLength">Maximum length in characters (T-02-09 bound).</param>
    /// <returns>The formatted description, or the original null/empty value.</returns>
    internal static string? FormatDescription(string? description, int maxLength)
    {
        if (string.IsNullOrEmpty(description))
        {
            return description;
        }

        if (description.Length > maxLength)
        {
            description = description[..maxLength];
        }

        return description.Replace("\n", "<br>", StringComparison.Ordinal);
    }
}
