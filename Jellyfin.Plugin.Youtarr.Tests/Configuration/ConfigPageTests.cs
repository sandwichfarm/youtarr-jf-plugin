using System;
using System.IO;
using Jellyfin.Plugin.Youtarr;
using Xunit;

namespace Jellyfin.Plugin.Youtarr.Tests.Configuration;

public class ConfigPageTests
{
    [Fact]
    public void EmbeddedScript_IsInsidePluginPageRoot()
    {
        var assembly = typeof(Plugin).Assembly;
        var resourceName = typeof(Plugin).Namespace + ".Configuration.configPage.html";
        using var stream = assembly.GetManifestResourceStream(resourceName);

        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var html = reader.ReadToEnd();
        var scriptIndex = html.IndexOf("<script", StringComparison.Ordinal);
        var rootClosingDivIndex = html.LastIndexOf("</div>", StringComparison.Ordinal);

        Assert.True(scriptIndex >= 0, "The embedded configuration page must contain a script.");
        Assert.True(
            scriptIndex < rootClosingDivIndex,
            "Jellyfin only injects the plugin page root, so its script must be nested inside that root div.");
    }
}
