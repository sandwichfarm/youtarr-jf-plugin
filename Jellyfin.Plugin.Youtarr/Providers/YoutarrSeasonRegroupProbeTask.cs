using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Youtarr.Utils;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Youtarr.Providers;

// THROWAWAY PROBE — quick task 260615-jb9 only.
// This class is a temporary instrument to test ONE hypothesis: can an ILibraryPostScanTask
// reparent Episodes into year-seasons and survive a second scan without orphaning or
// duplicating episodes? If the answer is NO (scan #2 re-creates phantom seasons), the
// entire post-scan-task approach is a dead end and will be abandoned.
//
// Explicitly OUT OF SCOPE for this probe:
//   - Config toggle to enable/disable regrouping (production feature)
//   - Season-0 fallback for undated videos (skipped with a log line)
//   - Artwork / poster / backdrop handling on regrouped seasons
//   - Generalising beyond self-gated Youtarr channel folders
//   - Auto-asserting the regroup result in CI
//
// The probe's sole output is [Youtarr] log lines readable via "docker logs" across two scans.

/// <summary>
/// THROWAWAY PROBE (quick task 260615-jb9) — not a production feature.
/// <para>
/// Tests whether a <see cref="ILibraryPostScanTask"/> can reparent each <see cref="Episode"/>
/// under its <see cref="Series"/> into a "Season {upload-year}" container, delete the phantom
/// per-video seasons Jellyfin's folder resolver creates from Youtarr's NESTED layout, and
/// have that regroup SURVIVE A SECOND SCAN without orphaning or duplicating episodes.
/// </para>
/// <para>
/// Self-gates on <see cref="PathUtils.IsYoutarrChannelFolder"/> so it never mutates
/// libraries other than Youtarr channel folders. All activity is logged with an
/// <c>[Youtarr] Probe:</c> prefix for easy grep across two consecutive scans.
/// </para>
/// </summary>
public class YoutarrSeasonRegroupProbeTask : ILibraryPostScanTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<YoutarrSeasonRegroupProbeTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="YoutarrSeasonRegroupProbeTask"/> class.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager (injected by DI).</param>
    /// <param name="logger">The logger (injected by DI).</param>
    public YoutarrSeasonRegroupProbeTask(
        ILibraryManager libraryManager,
        ILogger<YoutarrSeasonRegroupProbeTask> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task Run(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Youtarr] Probe: YoutarrSeasonRegroupProbeTask starting");

        // Step 1: Query all Series in the library.
        var allSeries = _libraryManager
            .GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Series },
                Recursive = true,
            })
            .OfType<Series>()
            .ToList();

        _logger.LogInformation("[Youtarr] Probe: found {Count} total Series", allSeries.Count);

        if (allSeries.Count == 0)
        {
            progress.Report(1.0);
            return;
        }

        int processed = 0;
        foreach (var series in allSeries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Step 2: SELF-GATE — only process Youtarr channel folders.
            if (!PathUtils.IsYoutarrChannelFolder(series.Path))
            {
                _logger.LogDebug(
                    "[Youtarr] Probe: skipping non-Youtarr Series {Name} at {Path}",
                    series.Name,
                    series.Path);
                processed++;
                progress.Report((double)processed / allSeries.Count);
                continue;
            }

            // Step 3: Log the gated Series.
            _logger.LogInformation(
                "[Youtarr] Probe: Series found {Name} at {Path}",
                series.Name,
                series.Path);

            try
            {
                await RegroupSeriesAsync(series, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Step 9: One bad Series must not abort the scan.
                _logger.LogWarning(
                    ex,
                    "[Youtarr] Probe: error regrouping Series {Name}",
                    series.Name);
            }

            processed++;
            progress.Report((double)processed / allSeries.Count);
        }

        _logger.LogInformation("[Youtarr] Probe: YoutarrSeasonRegroupProbeTask finished");
        progress.Report(1.0);
    }

    private async Task RegroupSeriesAsync(Series series, CancellationToken cancellationToken)
    {
        // Step 4: Enumerate this Series' Episodes.
        var episodes = series
            .GetRecursiveChildren(i => i is Episode)
            .OfType<Episode>()
            .ToList();

        _logger.LogInformation(
            "[Youtarr] Probe: Series {Name} has {Count} episodes",
            series.Name,
            episodes.Count);

        // Track which year-seasons we created/reused so we can identify phantoms afterward.
        var populatedSeasonIds = new HashSet<Guid>();

        foreach (var episode in episodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Step 5: Derive target year.
            int? year = episode.ProductionYear ?? episode.PremiereDate?.Year;
            if (year is null)
            {
                // Season-0 handling is OUT OF SCOPE for this probe.
                _logger.LogInformation(
                    "[Youtarr] Probe: skipping {Title} (no year)",
                    episode.Name);
                continue;
            }

            // Step 6: Get-or-create the target year Season.
            var targetSeason = GetOrCreateYearSeason(series, year.Value, out bool created);
            populatedSeasonIds.Add(targetSeason.Id);

            _logger.LogInformation(
                "[Youtarr] Probe: get-or-create Season {Year} (created={Created})",
                year,
                created);

            // Step 7: Reparent the episode — or log a no-op on scan #2.
            if (episode.ParentId == targetSeason.Id)
            {
                _logger.LogInformation(
                    "[Youtarr] Probe: {Title} already under Season {Year} (no-op)",
                    episode.Name,
                    year);
                continue;
            }

            episode.SetParent(targetSeason);
            await episode
                .UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "[Youtarr] Probe: moved {Title} -> Season {Year}",
                episode.Name,
                year);
        }

        // Step 8: Delete phantom seasons (empty, not in the year-seasons we populated).
        var childSeasons = series
            .GetRecursiveChildren(i => i is Season)
            .OfType<Season>()
            .ToList();

        foreach (var season in childSeasons)
        {
            if (populatedSeasonIds.Contains(season.Id))
            {
                // This is one of our year seasons — keep it.
                continue;
            }

            var seasonEpisodes = season
                .GetRecursiveChildren(i => i is Episode)
                .ToList();

            if (seasonEpisodes.Count > 0)
            {
                // Still has episodes (shouldn't happen, but be safe) — leave it.
                _logger.LogDebug(
                    "[Youtarr] Probe: leaving non-empty non-year Season {Name} (index={Index}, episodes={Count})",
                    season.Name,
                    season.IndexNumber,
                    seasonEpisodes.Count);
                continue;
            }

            // Empty phantom season — delete it. DeleteFileLocation=false: NEVER delete media.
            _logger.LogInformation(
                "[Youtarr] Probe: deleted phantom Season {Name} (index={Index})",
                season.Name,
                season.IndexNumber);

            _libraryManager.DeleteItem(
                season,
                new DeleteOptions { DeleteFileLocation = false });
        }
    }

    /// <summary>
    /// Returns an existing child <see cref="Season"/> with <see cref="BaseItem.IndexNumber"/> ==
    /// <paramref name="year"/>, or creates and links a new one using the canonical pattern from
    /// <c>SeriesMetadataService.CreateSeasonAsync</c> (jellyfin v10.10.7).
    /// </summary>
    private Season GetOrCreateYearSeason(Series series, int year, out bool created)
    {
        var existing = series
            .GetRecursiveChildren(i => i is Season s && s.IndexNumber == year)
            .OfType<Season>()
            .FirstOrDefault();

        if (existing is not null)
        {
            created = false;
            return existing;
        }

        var seasonName = string.Format(CultureInfo.InvariantCulture, "Season {0}", year);

        // Canonical season-creation pattern from SeriesMetadataService.CreateSeasonAsync
        // (jellyfin v10.10.7) — replicated verbatim as specified in the plan interfaces block.
        var season = new Season
        {
            Name = seasonName,
            IndexNumber = year,
            Id = _libraryManager.GetNewItemId(
                series.Id.ToString("N", CultureInfo.InvariantCulture)
                + year.ToString(CultureInfo.InvariantCulture)
                + seasonName,
                typeof(Season)),
            IsVirtualItem = false,
            SeriesId = series.Id,
            SeriesName = series.Name,
            SeriesPresentationUniqueKey = series.GetPresentationUniqueKey(),
        };

        series.AddChild(season);

        created = true;
        return season;
    }
}
