using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Youtarr.Utils;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Youtarr.Providers;

/// <summary>
/// Provides Series metadata for Youtarr channel folders.
/// Jellyfin's built-in SeriesNfoProvider looks for <c>tvshow.nfo</c>, which Youtarr does not write.
/// This provider synthesizes Series metadata from the channel folder name (SER-01/SER-02) and,
/// as a best-effort confirmation, reads the <c>&lt;studio&gt;</c> field of the first video NFO in
/// the folder. The folder name always wins as the Series name.
/// </summary>
public class YoutarrSeriesNfoProvider : ILocalMetadataProvider<Series>, IHasItemChangeMonitor
{
    private readonly ILogger<YoutarrSeriesNfoProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="YoutarrSeriesNfoProvider"/> class.
    /// </summary>
    /// <param name="logger">Injected logger.</param>
    public YoutarrSeriesNfoProvider(ILogger<YoutarrSeriesNfoProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => Constants.ProviderName;

    /// <inheritdoc />
    public Task<MetadataResult<Series>> GetMetadata(
        ItemInfo info,
        IDirectoryService directoryService,
        CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Series>();
        var channelPath = info.Path;

        // Primary, authoritative source: the channel folder name. No file I/O required.
        var channelName = PathUtils.GetChannelNameFromPath(channelPath);
        if (string.IsNullOrWhiteSpace(channelName))
        {
            _logger.LogWarning("[Youtarr] Could not derive Series name from path: {Path}", channelPath);
            return Task.FromResult(result); // HasMetadata stays false
        }

        result.Item = new Series { Name = channelName };

        // Best-effort enrichment/confirmation: read <studio> from the first NFO. Folder name
        // still wins per SER-01. Wrapped so a malformed/unreadable NFO can never crash the scan
        // (T-01-03) — PathUtils helpers already swallow their own errors to null.
        try
        {
            var nfoPath = PathUtils.FindFirstNfoInFolder(channelPath);
            if (nfoPath is not null)
            {
                var studioName = PathUtils.ReadStudioFromMovieNfo(nfoPath);
                if (!string.IsNullOrWhiteSpace(studioName) &&
                    !string.Equals(studioName, channelName, StringComparison.Ordinal))
                {
                    _logger.LogDebug(
                        "[Youtarr] Series '{FolderName}': NFO studio field is '{Studio}'. Using folder name.",
                        channelName,
                        studioName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Youtarr] Error scanning NFOs in '{Path}'; using folder name only.", channelPath);
        }

        // CRITICAL (Pitfall 5 / SER-02): without HasMetadata = true Jellyfin discards this result
        // and the Series name goes blank. Setting it true makes the synthesized metadata authoritative.
        result.HasMetadata = true;
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public bool HasChanged(BaseItem item, IDirectoryService directoryService)
    {
        // Phase 1: do not force re-scans. Phase 2+ may track NFO mtimes.
        return false;
    }
}
