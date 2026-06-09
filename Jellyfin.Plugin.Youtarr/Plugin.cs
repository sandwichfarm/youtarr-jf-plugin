using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Youtarr.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Youtarr;

/// <summary>
/// Plugin entry point.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Permanent plugin identity. MUST match build.yaml `guid` exactly and never
    /// change between releases (GUID drift orphans config at config/plugins/&lt;GUID&gt;/config.xml).
    /// </summary>
    public static readonly Guid StaticId = new Guid("80302d7f-7fc3-4b1c-9a3f-fd85b98b9a69");

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Application paths, injected by Jellyfin DI.</param>
    /// <param name="xmlSerializer">XML serializer, injected by Jellyfin DI.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>Gets the current plugin instance (set during construction).</summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override Guid Id => StaticId;

    /// <inheritdoc />
    public override string Name => "YoutarrMetadata";

    /// <inheritdoc />
    public override string Description => "Organizes Youtarr downloads as Series/Season/Episode in Jellyfin.";

    // TODO(plan 01-02): DI registration in Jellyfin 10.10.x is NOT a BasePlugin override.
    // BasePlugin<T> exposes no RegisterServices method to override. Instead, implement a
    // separate `IPluginServiceRegistrator` (MediaBrowser.Controller.Plugins) class that
    // Jellyfin auto-discovers, e.g.:
    //
    //   public class PluginServiceRegistrator : IPluginServiceRegistrator
    //   {
    //       public void RegisterServices(IServiceCollection services, IServerApplicationHost host)
    //           => services.AddSingleton<IResolverIgnoreRule, YoutarrPrefixIgnoreRule>();
    //   }
    //
    // YoutarrPrefixIgnoreRule does not exist until plan 01-02, which owns this wiring.
    // IResolverIgnoreRule implementations are also auto-discovered by Jellyfin's DI, so
    // explicit registration may be unnecessary — 01-02 verifies which is needed.

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "YoutarrMetadata",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
            }
        };
    }
}
