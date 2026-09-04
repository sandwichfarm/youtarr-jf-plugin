using Jellyfin.Plugin.Youtarr.Providers;
using Jellyfin.Plugin.Youtarr.Utils;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Resolvers;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Youtarr;

/// <summary>
/// Registers the plugin's DI services. In Jellyfin 10.11.x, <c>BasePlugin&lt;T&gt;</c> has no
/// <c>RegisterServices</c> override; DI registration is performed by a separate class implementing
/// <see cref="IPluginServiceRegistrator"/>, which Jellyfin auto-discovers at startup.
/// </summary>
/// <remarks>
/// Acts as a safety net for <see cref="YoutarrPrefixIgnoreRule"/> and
/// <see cref="YoutarrSeriesImageProvider"/> so the plugin still works if Jellyfin's automatic
/// discovery differs across deployments. <c>ILocalMetadataProvider&lt;Series&gt;</c> implementations
/// are auto-discovered and need no explicit registration here. The explicit
/// <see cref="ILibraryPostScanTask"/> registration guarantees the production season-regroup task
/// runs after scans to collapse Youtarr's phantom per-video seasons into deterministic year or
/// flat seasons.
/// </remarks>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IResolverIgnoreRule, YoutarrPrefixIgnoreRule>();
        serviceCollection.AddSingleton<ILocalImageProvider, YoutarrSeriesImageProvider>();

        // Explicit registration mirrors the safety-net precedent above so season regroup always
        // runs as an ILibraryPostScanTask after each scan.
        serviceCollection.AddSingleton<ILibraryPostScanTask, YoutarrSeasonRegroupTask>();
    }
}
