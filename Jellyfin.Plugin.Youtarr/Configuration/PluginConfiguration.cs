using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Youtarr.Configuration;

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
}
