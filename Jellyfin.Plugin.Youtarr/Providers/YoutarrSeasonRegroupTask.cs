using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Youtarr.Configuration;
using Jellyfin.Plugin.Youtarr.Models;
using Jellyfin.Plugin.Youtarr.Parsers;
using Jellyfin.Plugin.Youtarr.Utils;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Youtarr.Providers;

/// <summary>
/// Regroups Youtarr channel episodes into deterministic Jellyfin seasons derived from each
/// episode's same-basename Youtarr <c>&lt;movie&gt;</c> NFO. This runs after scans so nested
/// Youtarr layouts cannot leave behind one phantom season per video.
/// </summary>
public partial class YoutarrSeasonRegroupTask : ILibraryPostScanTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IItemRepository _itemRepository;
    private readonly ILogger<YoutarrSeasonRegroupTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="YoutarrSeasonRegroupTask"/> class.
    /// </summary>
    public YoutarrSeasonRegroupTask(
        ILibraryManager libraryManager,
        IItemRepository itemRepository,
        ILogger<YoutarrSeasonRegroupTask> logger)
    {
        _libraryManager = libraryManager;
        _itemRepository = itemRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "One bad series must not abort the full library scan.")]
    public async Task Run(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        LogSeasonRegroupStarting(_logger);

        var allSeries = _libraryManager
            .GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Series },
                Recursive = true,
            })
            .OfType<Series>()
            .ToList();

        if (allSeries.Count == 0)
        {
            progress.Report(1.0);
            return;
        }

        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var processed = 0;

        foreach (var series in allSeries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!PathUtils.IsYoutarrChannelFolder(series.Path))
            {
                processed++;
                progress.Report((double)processed / allSeries.Count);
                continue;
            }

            try
            {
                await RegroupSeriesAsync(series, config, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogSeriesRegroupFailed(_logger, ex, series.Name);
            }

            processed++;
            progress.Report((double)processed / allSeries.Count);
        }

        LogSeasonRegroupFinished(_logger);
        progress.Report(1.0);
    }

    private async Task RegroupSeriesAsync(
        Series series,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        var episodes = series
            .GetRecursiveChildren(item => item is Episode)
            .OfType<Episode>()
            .ToList();

        if (episodes.Count == 0)
        {
            return;
        }

        var populatedSeasonIds = new HashSet<Guid>();
        var protectedSeasonIds = new HashSet<Guid>();

        foreach (var episode in episodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var parsed = TryParseEpisodeNfo(episode, out var nfoPath);
            if (parsed is null)
            {
                if (!string.IsNullOrWhiteSpace(nfoPath))
                {
                    LogEpisodeSkippedForNonYoutarrNfo(_logger, episode.Name, nfoPath);
                }

                ProtectExistingSeasonLink(episode, protectedSeasonIds);
                continue;
            }

            var seasonTarget = ResolveSeasonTarget(config, parsed.PremiereDate);
            var targetSeason = await GetOrCreateSeasonAsync(
                series,
                seasonTarget.Index,
                seasonTarget.Name,
                cancellationToken).ConfigureAwait(false);

            populatedSeasonIds.Add(targetSeason.Id);

            var linkage = BuildEpisodeSeasonLinkage(
                series.Id,
                series.Name,
                targetSeason.Id,
                seasonTarget.Index,
                seasonTarget.Name,
                parsed.PremiereDate ?? parsed.DateAdded,
                parsed.PremiereDate?.Year);

            if (!ApplyEpisodeSeasonLinkage(episode, targetSeason, linkage))
            {
                continue;
            }

            await episode
                .UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken)
                .ConfigureAwait(false);
        }

        DeletePhantomSeasons(series, populatedSeasonIds, protectedSeasonIds, cancellationToken);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "One unreadable NFO must not abort regrouping the rest of its series.")]
    private YoutarrVideoData? TryParseEpisodeNfo(Episode episode, out string? nfoPath)
    {
        nfoPath = PathUtils.FindNfoForVideo(episode.Path);
        if (string.IsNullOrWhiteSpace(nfoPath))
        {
            LogEpisodeSkippedForMissingNfo(_logger, episode.Name);
            return null;
        }

        try
        {
            return YoutarrNfoParser.Parse(nfoPath);
        }
        catch (Exception ex)
        {
            LogEpisodeNfoParseFailed(_logger, ex, nfoPath, episode.Name);
            return null;
        }
    }

    private async Task<Season> GetOrCreateSeasonAsync(
        Series series,
        int seasonIndex,
        string seasonName,
        CancellationToken cancellationToken)
    {
        var season = series
            .GetRecursiveChildren(item => item is Season)
            .OfType<Season>()
            .FirstOrDefault(item => item.IndexNumber == seasonIndex && string.IsNullOrEmpty(item.Path));

        if (season is null)
        {
            season = new Season
            {
                Name = seasonName,
                IndexNumber = seasonIndex,
                Id = _libraryManager.GetNewItemId(
                    series.Id.ToString("N", CultureInfo.InvariantCulture)
                    + seasonIndex.ToString(CultureInfo.InvariantCulture)
                    + seasonName,
                    typeof(Season)),
                IsVirtualItem = false,
                SeriesId = series.Id,
                SeriesName = series.Name,
                SeriesPresentationUniqueKey = series.GetPresentationUniqueKey(),
                ProductionYear = seasonIndex >= 2005 ? seasonIndex : null,
            };

            series.AddChild(season);

            await season
                .UpdateToRepositoryAsync(ItemUpdateType.MetadataImport, cancellationToken)
                .ConfigureAwait(false);

            return season;
        }

        var changed = false;

        if (!string.Equals(season.Name, seasonName, StringComparison.Ordinal))
        {
            season.Name = seasonName;
            changed = true;
        }

        if (season.IndexNumber != seasonIndex)
        {
            season.IndexNumber = seasonIndex;
            changed = true;
        }

        if (season.SeriesId != series.Id)
        {
            season.SeriesId = series.Id;
            changed = true;
        }

        if (!string.Equals(season.SeriesName, series.Name, StringComparison.Ordinal))
        {
            season.SeriesName = series.Name;
            changed = true;
        }

        var presentationKey = series.GetPresentationUniqueKey();
        if (!string.Equals(season.SeriesPresentationUniqueKey, presentationKey, StringComparison.Ordinal))
        {
            season.SeriesPresentationUniqueKey = presentationKey;
            changed = true;
        }

        int? productionYear = seasonIndex >= 2005 ? seasonIndex : null;
        if (season.ProductionYear != productionYear)
        {
            season.ProductionYear = productionYear;
            changed = true;
        }

        if (changed)
        {
            await season
                .UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken)
                .ConfigureAwait(false);
        }

        return season;
    }

    private void DeletePhantomSeasons(
        Series series,
        HashSet<Guid> populatedSeasonIds,
        HashSet<Guid> protectedSeasonIds,
        CancellationToken cancellationToken)
    {
        var seasons = series
            .GetRecursiveChildren(item => item is Season)
            .OfType<Season>()
            .ToList();

        foreach (var season in seasons)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (populatedSeasonIds.Contains(season.Id) || protectedSeasonIds.Contains(season.Id))
            {
                continue;
            }

            // LibraryManager.DeleteItem recursively enumerates the physical directory and would
            // delete its episode records even after they were reparented. Delete only the now-
            // childless season row; IItemRepository follows persisted ParentId links, not paths.
            season.ParentId = Guid.Empty;
            season.SeriesId = Guid.Empty;
            season.SeriesName = null;
            _itemRepository.DeleteItem(season.Id);
            LogPhantomSeasonDeleted(_logger, season.Name, season.IndexNumber);
        }
    }

    private static void ProtectExistingSeasonLink(Episode episode, HashSet<Guid> protectedSeasonIds)
    {
        if (episode.ParentId != Guid.Empty)
        {
            protectedSeasonIds.Add(episode.ParentId);
        }

        if (episode.SeasonId != Guid.Empty)
        {
            protectedSeasonIds.Add(episode.SeasonId);
        }
    }

    internal static YoutarrSeasonTarget ResolveSeasonTarget(
        PluginConfiguration config,
        DateTime? premiereDate)
    {
        if (premiereDate.HasValue)
        {
            var seasonIndex = config.YearSeasons ? premiereDate.Value.Year : 1;
            return new YoutarrSeasonTarget(seasonIndex, FormatSeasonName(seasonIndex));
        }

        return new YoutarrSeasonTarget(0, FormatSeasonName(0));
    }

    internal static YoutarrEpisodeSeasonLinkage BuildEpisodeSeasonLinkage(
        Guid seriesId,
        string? seriesName,
        Guid seasonId,
        int seasonIndex,
        string seasonName,
        DateTime? premiereDate,
        int? productionYear)
    {
        return new YoutarrEpisodeSeasonLinkage(
            ParentIndexNumber: seasonIndex,
            SeasonId: seasonId,
            SeasonName: seasonName,
            SeriesId: seriesId,
            SeriesName: seriesName,
            PremiereDate: premiereDate,
            ProductionYear: productionYear);
    }

    internal static bool ApplyEpisodeSeasonLinkage(
        Episode episode,
        Season targetSeason,
        YoutarrEpisodeSeasonLinkage linkage)
    {
        var changed = false;

        if (episode.ParentId != targetSeason.Id)
        {
            episode.SetParent(targetSeason);
            changed = true;
        }

        if (episode.SeasonId != linkage.SeasonId)
        {
            episode.SeasonId = linkage.SeasonId;
            changed = true;
        }

        if (!string.Equals(episode.SeasonName, linkage.SeasonName, StringComparison.Ordinal))
        {
            episode.SeasonName = linkage.SeasonName;
            changed = true;
        }

        if (episode.ParentIndexNumber != linkage.ParentIndexNumber)
        {
            episode.ParentIndexNumber = linkage.ParentIndexNumber;
            changed = true;
        }

        if (episode.SeriesId != linkage.SeriesId)
        {
            episode.SeriesId = linkage.SeriesId;
            changed = true;
        }

        if (!string.Equals(episode.SeriesName, linkage.SeriesName, StringComparison.Ordinal))
        {
            episode.SeriesName = linkage.SeriesName;
            changed = true;
        }

        if (episode.PremiereDate != linkage.PremiereDate)
        {
            episode.PremiereDate = linkage.PremiereDate;
            changed = true;
        }

        if (episode.ProductionYear != linkage.ProductionYear)
        {
            episode.ProductionYear = linkage.ProductionYear;
            changed = true;
        }

        return changed;
    }

    private static string FormatSeasonName(int seasonIndex)
    {
        return seasonIndex == 0
            ? "Specials"
            : string.Format(CultureInfo.InvariantCulture, "Season {0}", seasonIndex);
    }

    [LoggerMessage(LogLevel.Information, "[Youtarr] Season regroup starting.")]
    private static partial void LogSeasonRegroupStarting(ILogger logger);

    [LoggerMessage(LogLevel.Warning, "[Youtarr] Failed to regroup series '{SeriesName}'.")]
    private static partial void LogSeriesRegroupFailed(ILogger logger, Exception exception, string? seriesName);

    [LoggerMessage(LogLevel.Information, "[Youtarr] Season regroup finished.")]
    private static partial void LogSeasonRegroupFinished(ILogger logger);

    [LoggerMessage(LogLevel.Debug, "[Youtarr] Skipping episode '{EpisodeName}' because '{NfoPath}' was not a Youtarr movie NFO.")]
    private static partial void LogEpisodeSkippedForNonYoutarrNfo(ILogger logger, string? episodeName, string nfoPath);

    [LoggerMessage(LogLevel.Debug, "[Youtarr] Skipping episode '{EpisodeName}' because no same-basename NFO was found.")]
    private static partial void LogEpisodeSkippedForMissingNfo(ILogger logger, string? episodeName);

    [LoggerMessage(LogLevel.Warning, "[Youtarr] Failed to parse episode NFO '{NfoPath}' for '{EpisodeName}'.")]
    private static partial void LogEpisodeNfoParseFailed(ILogger logger, Exception exception, string nfoPath, string? episodeName);

    [LoggerMessage(LogLevel.Information, "[Youtarr] Removed empty phantom season '{SeasonName}' (index {SeasonIndex}).")]
    private static partial void LogPhantomSeasonDeleted(ILogger logger, string? seasonName, int? seasonIndex);
}

internal readonly record struct YoutarrSeasonTarget(int Index, string Name);

internal readonly record struct YoutarrEpisodeSeasonLinkage(
    int ParentIndexNumber,
    Guid SeasonId,
    string SeasonName,
    Guid SeriesId,
    string? SeriesName,
    DateTime? PremiereDate,
    int? ProductionYear);
