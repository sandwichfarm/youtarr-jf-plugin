using System.Collections.Generic;
using System.IO;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Youtarr.Providers;

/// <summary>
/// Supplements Jellyfin's built-in <c>LocalImageProvider</c> for Series items.
/// The built-in already picks up <c>poster.jpg</c> as the Series Primary image (ART-01),
/// so this provider's ONLY responsibility is ART-02: returning that same <c>poster.jpg</c>
/// as <see cref="ImageType.Backdrop"/>, since Youtarr writes no separate fanart/backdrop file.
/// It deliberately never emits a Primary image — competing with the built-in
/// for Primary risks image flicker on rescans (RESEARCH Pitfall 1). When <c>poster.jpg</c> is
/// absent it returns an empty enumerable and never throws (ART-04).
/// </summary>
public class YoutarrSeriesImageProvider : ILocalImageProvider
{
    private readonly ILogger<YoutarrSeriesImageProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="YoutarrSeriesImageProvider"/> class.
    /// </summary>
    /// <param name="logger">Injected logger.</param>
    public YoutarrSeriesImageProvider(ILogger<YoutarrSeriesImageProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => Constants.ProviderName;

    /// <inheritdoc />
    public bool Supports(BaseItem item) => item is Series;

    /// <inheritdoc />
    public IEnumerable<LocalImageInfo> GetImages(BaseItem item, IDirectoryService directoryService)
    {
        var posterPath = Path.Combine(item.Path, "poster.jpg");
        if (!File.Exists(posterPath))
        {
            // ART-04: graceful degradation — no poster, no backdrop, no exception.
            _logger.LogDebug("[Youtarr] No poster.jpg found for Series at {Path}; skipping backdrop.", item.Path);
            yield break;
        }

        // ART-02: surface poster.jpg as Backdrop ONLY. The built-in LocalImageProvider already
        // returns poster.jpg as Primary (Order=0); we must not re-emit Primary (RESEARCH Pitfall 1).
        // FullName is the only field the image pipeline needs (mirrors built-in EpisodeLocalImageProvider).
        yield return new LocalImageInfo
        {
            FileInfo = new FileSystemMetadata { FullName = posterPath },
            Type = ImageType.Backdrop,
        };
    }
}
