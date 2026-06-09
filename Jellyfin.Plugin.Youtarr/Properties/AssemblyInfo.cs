using System.Runtime.CompilerServices;

// Exposes internal static MapToEpisode / FormatDescription (YoutarrEpisodeNfoProvider) to the
// test project so the config-dependent mapping logic can be unit-tested directly, without
// constructing Plugin.Instance or a running Jellyfin server.
[assembly: InternalsVisibleTo("Jellyfin.Plugin.Youtarr.Tests")]
