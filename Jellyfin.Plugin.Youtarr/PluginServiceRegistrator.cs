using Jellyfin.Plugin.Youtarr.Providers;
using Jellyfin.Plugin.Youtarr.Utils;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Resolvers;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Youtarr;

/// <summary>
/// Registers the plugin's DI services. In Jellyfin 10.10.x, <c>BasePlugin&lt;T&gt;</c> has no
/// <c>RegisterServices</c> override; DI registration is performed by a separate class implementing
/// <see cref="IPluginServiceRegistrator"/>, which Jellyfin auto-discovers at startup.
/// </summary>
/// <remarks>
/// Acts as a safety net for <see cref="YoutarrPrefixIgnoreRule"/> (RESEARCH Open Question #1).
/// <c>IResolverIgnoreRule</c> implementations are also auto-discovered by Jellyfin's DI, so this
/// explicit registration may be redundant; plan 01-03 verifies which mechanism is required.
/// The same conservative safety-net applies to <see cref="YoutarrSeriesImageProvider"/>
/// (<c>ILocalImageProvider</c>, RESEARCH Open Question #2) — registered explicitly here pending
/// live confirmation of auto-discovery in plan 03-03.
/// <c>ILocalMetadataProvider&lt;Series&gt;</c> implementations are always auto-discovered and need
/// no explicit registration here.
/// </remarks>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IResolverIgnoreRule, YoutarrPrefixIgnoreRule>();
        serviceCollection.AddSingleton<ILocalImageProvider, YoutarrSeriesImageProvider>();
    }
}
