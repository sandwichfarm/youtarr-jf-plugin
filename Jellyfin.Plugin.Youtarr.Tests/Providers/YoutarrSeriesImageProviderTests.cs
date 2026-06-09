using System;
using System.IO;
using System.Linq;
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
    public void GetImages_SeriesWithPoster_ReturnsSingleBackdrop()
    {
        // ART-02: a real poster.jpg in the Series folder is surfaced as a Backdrop image.
        var dir = CreateTempDir();
        try
        {
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
    public void GetImages_SeriesWithPoster_NeverEmitsPrimary()
    {
        // ART-02 boundary: the built-in LocalImageProvider owns Series Primary. This provider must
        // not compete — no emitted image may carry ImageType.Primary.
        var dir = CreateTempDir();
        try
        {
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
    public void GetImages_SeriesWithoutPoster_ReturnsEmpty_NoThrow()
    {
        // ART-04: a Series folder with no poster.jpg yields an empty enumerable and never throws.
        var dir = CreateTempDir();
        try
        {
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

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "youtarr-img-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
