using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Youtarr.Configuration;

/// <summary>
/// Determines how the plugin assigns <c>Episode.IndexNumber</c> for downloaded videos.
/// </summary>
public enum EpisodeNumberingScheme
{
    /// <summary>
    /// Let Jellyfin auto-sequence episode numbers. The provider sets
    /// <c>IndexNumber = null</c> and Jellyfin assigns numbers in scan order.
    /// </summary>
    Default = 0,

    /// <summary>
    /// Use the upload date as the episode number: <c>(year * 10000) + (month * 100) + day</c>.
    /// For example, 2024-03-15 becomes <c>20240315</c>. Note: videos uploaded on the same
    /// day share an <c>IndexNumber</c> (same-day collision, accepted per the EPI-05 decision —
    /// users with many same-day uploads should prefer <see cref="Default"/>).
    /// </summary>
    YYYYMMDD = 1,
}

/// <summary>
/// Plugin configuration. Fields added per phase; placeholder properties prevent
/// deserialization errors when config XML from a later version is read by an earlier one.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether to group episodes by upload year into seasons.
    /// Default: true. Set false to put all episodes in one season.
    /// </summary>
    public bool YearSeasons { get; set; } = true;

    /// <summary>
    /// Gets or sets the episode numbering scheme.
    /// Default: auto-sequenced by Jellyfin.
    /// YYYYMMDD: upload date as integer episode number (e.g. 20240315).
    /// </summary>
    public EpisodeNumberingScheme EpisodeNumberingScheme { get; set; } = EpisodeNumberingScheme.Default;

    /// <summary>
    /// Gets or sets the maximum description length in characters.
    /// YouTube descriptions are often 500-5000 chars; raw text looks bad in the Jellyfin UI.
    /// Default: 500 (matching tubearchivist-jf-plugin default).
    /// </summary>
    public int MaxDescriptionLength { get; set; } = 500;
}
