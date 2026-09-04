using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Youtarr;
using Jellyfin.Plugin.Youtarr.Providers;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Youtarr.Tests.Providers;

/// <summary>
/// Unit tests for <see cref="YoutarrSeriesNfoProvider"/> — Series-name derivation,
/// HasMetadata authority (SER-02), and studio NFO fallback ordering (SER-01).
/// </summary>
public class YoutarrSeriesNfoProviderTests
{
    private readonly Mock<ILogger<YoutarrSeriesNfoProvider>> _logger = new();
    private readonly Mock<IDirectoryService> _directoryService = new();

    private YoutarrSeriesNfoProvider CreateProvider() => new(_logger.Object);

    private static ItemInfo ItemInfoForPath(string path)
    {
        // ItemInfo has no parameterless ctor in 10.11.11; construct from a BaseItem then set Path.
        return new ItemInfo(new Series()) { Path = path };
    }

    [Fact]
    public void Name_ReturnsConstantsProviderName()
    {
        Assert.Equal(Constants.ProviderName, CreateProvider().Name);
        Assert.Equal("Youtarr", CreateProvider().Name);
    }

    [Fact]
    public async Task GetMetadata_ValidChannelPath_SetsNameAndHasMetadata()
    {
        var dir = CreateTempDir();
        try
        {
            // A genuine Youtarr channel folder: a <movie> NFO with a youtube uniqueid present.
            WriteYoutarrNfo(Path.Combine(dir, "video.nfo"));

            var provider = CreateProvider();
            var info = ItemInfoForPath(dir);

            var result = await provider.GetMetadata(info, _directoryService.Object, CancellationToken.None);

            Assert.True(result.HasMetadata);
            Assert.NotNull(result.Item);
            Assert.Equal(Path.GetFileName(dir), result.Item!.Name);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task GetMetadata_TrailingSeparator_YieldsFolderName()
    {
        var dir = CreateTempDir();
        try
        {
            WriteYoutarrNfo(Path.Combine(dir, "video.nfo"));

            var provider = CreateProvider();
            var info = ItemInfoForPath(dir + Path.DirectorySeparatorChar);

            var result = await provider.GetMetadata(info, _directoryService.Object, CancellationToken.None);

            Assert.True(result.HasMetadata);
            Assert.Equal(Path.GetFileName(dir), result.Item!.Name);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task GetMetadata_StudioNfoPresent_FolderNameStillWins()
    {
        // SER-01 ordering: even when a video NFO carries a <studio> that differs from
        // the folder name, the folder name remains the authoritative Series name.
        var dir = CreateTempDir();
        try
        {
            // Youtarr channel: <movie> NFO with a youtube uniqueid AND a differing studio.
            File.WriteAllText(
                Path.Combine(dir, "video.nfo"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><movie><uniqueid type=\"youtube\">dQw4w9WgXcQ</uniqueid><studio>A Different Studio Name</studio></movie>",
                Encoding.UTF8);

            var provider = CreateProvider();
            var info = ItemInfoForPath(dir);

            var result = await provider.GetMetadata(info, _directoryService.Object, CancellationToken.None);

            Assert.True(result.HasMetadata);
            Assert.Equal(Path.GetFileName(dir), result.Item!.Name);
            Assert.NotEqual("A Different Studio Name", result.Item!.Name);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task GetMetadata_MalformedNfo_DoesNotThrow_FolderNameWins()
    {
        // T-01-03: a malformed NFO must not crash the scan. The folder is still a genuine Youtarr
        // channel (a valid Youtarr NFO is present alongside the broken one).
        var dir = CreateTempDir();
        try
        {
            WriteYoutarrNfo(Path.Combine(dir, "video.nfo"));
            File.WriteAllText(Path.Combine(dir, "broken.nfo"), "<not valid xml", Encoding.UTF8);

            var provider = CreateProvider();
            var info = ItemInfoForPath(dir);

            var result = await provider.GetMetadata(info, _directoryService.Object, CancellationToken.None);

            Assert.True(result.HasMetadata);
            Assert.Equal(Path.GetFileName(dir), result.Item!.Name);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task GetMetadata_NonYoutarrSeriesFolder_HasMetadataFalse_DoesNotClaim()
    {
        // BUG FIX: a regular TV/anime Series folder that is NOT a Youtarr channel must be left
        // untouched. Even with a non-Youtarr NFO present, the provider must not claim the Series.
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(
                Path.Combine(dir, "tvshow.nfo"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><tvshow><title>Some Anime</title></tvshow>",
                Encoding.UTF8);

            var provider = CreateProvider();
            var info = ItemInfoForPath(dir);

            var result = await provider.GetMetadata(info, _directoryService.Object, CancellationToken.None);

            Assert.False(result.HasMetadata);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task GetMetadata_EmptyPath_HasMetadataFalse_NoThrow()
    {
        var provider = CreateProvider();
        var info = ItemInfoForPath(string.Empty);

        var result = await provider.GetMetadata(info, _directoryService.Object, CancellationToken.None);

        Assert.False(result.HasMetadata);
    }

    [Fact]
    public async Task GetMetadata_WhitespacePath_HasMetadataFalse_NoThrow()
    {
        var provider = CreateProvider();
        var info = ItemInfoForPath("   ");

        var result = await provider.GetMetadata(info, _directoryService.Object, CancellationToken.None);

        Assert.False(result.HasMetadata);
    }

    /// <summary>
    /// Writes a minimal Youtarr <c>&lt;movie&gt;</c> NFO with a youtube uniqueid — the evidence that
    /// makes the enclosing folder a genuine Youtarr channel.
    /// </summary>
    private static void WriteYoutarrNfo(string path)
    {
        File.WriteAllText(
            path,
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><movie><title>A Video</title><uniqueid type=\"youtube\">dQw4w9WgXcQ</uniqueid></movie>",
            Encoding.UTF8);
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "youtarr-prov-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
