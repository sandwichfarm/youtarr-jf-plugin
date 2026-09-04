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

    // DI registration: in Jellyfin 10.11.x BasePlugin<T> has no RegisterServices
    // override. YoutarrPrefixIgnoreRule is registered via the separate IPluginServiceRegistrator
    // implementation in PluginServiceRegistrator.cs, which Jellyfin auto-discovers at startup
    // (serviceCollection.AddSingleton<IResolverIgnoreRule, YoutarrPrefixIgnoreRule>()).

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
