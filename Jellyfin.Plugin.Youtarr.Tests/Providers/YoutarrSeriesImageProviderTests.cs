using System;
using System.IO;
using System.Linq;
using System.Text;
using Jellyfin.Plugin.Youtarr;
using Jellyfin.Plugin.Youtarr.Providers;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Youtarr.Tests.Providers;

/// <summary>
/// Unit tests for <see cref="YoutarrSeriesImageProvider"/> — the only custom artwork code in
/// Phase 3. Verifies ART-02 (poster.jpg surfaced as <see cref="ImageType.Backdrop"/> for Series),
/// the Primary-boundary (this provider must never emit <see cref="ImageType.Primary"/> — the
/// built-in LocalImageProvider owns it), ART-04 (missing poster.jpg yields an empty enumerable and
/// never throws), the Series-only support gate, and the provider identity.
/// </summary>
public class YoutarrSeriesImageProviderTests
{
    private readonly Mock<ILogger<YoutarrSeriesImageProvider>> _logger = new();
    private readonly Mock<IDirectoryService> _directoryService = new();

    private YoutarrSeriesImageProvider CreateProvider() => new(_logger.Object);

    [Fact]
    public void GetImages_YoutarrChannelWithPoster_ReturnsSingleBackdrop()
    {
        // ART-02: in a genuine Youtarr channel folder, poster.jpg is surfaced as a Backdrop image.
        var dir = CreateTempDir();
        try
        {
            WriteYoutarrNfo(Path.Combine(dir, "video.nfo"));
            var posterPath = Path.Combine(dir, "poster.jpg");
            File.WriteAllBytes(posterPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

            var provider = CreateProvider();
            var series = new Series { Path = dir };

            var images = provider.GetImages(series, _directoryService.Object).ToList();

            var image = Assert.Single(images);
            Assert.Equal(ImageType.Backdrop, image.Type);
            Assert.NotNull(image.FileInfo);
            Assert.EndsWith("poster.jpg", image.FileInfo!.FullName);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GetImages_YoutarrChannelWithPoster_NeverEmitsPrimary()
    {
        // ART-02 boundary: the built-in LocalImageProvider owns Series Primary. This provider must
        // not compete — no emitted image may carry ImageType.Primary.
        var dir = CreateTempDir();
        try
        {
            WriteYoutarrNfo(Path.Combine(dir, "video.nfo"));
            File.WriteAllBytes(Path.Combine(dir, "poster.jpg"), new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

            var provider = CreateProvider();
            var series = new Series { Path = dir };

            var images = provider.GetImages(series, _directoryService.Object).ToList();

            Assert.DoesNotContain(images, i => i.Type == ImageType.Primary);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GetImages_YoutarrChannelWithoutPoster_ReturnsEmpty_NoThrow()
    {
        // ART-04: a Youtarr channel folder with no poster.jpg yields an empty enumerable, no throw.
        var dir = CreateTempDir();
        try
        {
            WriteYoutarrNfo(Path.Combine(dir, "video.nfo"));

            var provider = CreateProvider();
            var series = new Series { Path = dir };

            var images = provider.GetImages(series, _directoryService.Object).ToList();

            Assert.Empty(images);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GetImages_NonYoutarrSeriesWithPoster_ReturnsEmpty_DoesNotForceBackdrop()
    {
        // BUG FIX: a regular library's Series folder that has a poster.jpg but is NOT a Youtarr
        // channel must yield NO images — the provider must not force its poster to a Backdrop.
        var dir = CreateTempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "poster.jpg"), new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

            var provider = CreateProvider();
            var series = new Series { Path = dir };

            var images = provider.GetImages(series, _directoryService.Object).ToList();

            Assert.Empty(images);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Supports_SeriesTrue_NonSeriesFalse()
    {
        var provider = CreateProvider();

        Assert.True(provider.Supports(new Series()));
        Assert.False(provider.Supports(new Movie()));
        Assert.False(provider.Supports(new Episode()));
    }

    [Fact]
    public void Name_ReturnsConstantsProviderName()
    {
        Assert.Equal(Constants.ProviderName, CreateProvider().Name);
        Assert.Equal("Youtarr", CreateProvider().Name);
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
        var dir = Path.Combine(Path.GetTempPath(), "youtarr-img-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
