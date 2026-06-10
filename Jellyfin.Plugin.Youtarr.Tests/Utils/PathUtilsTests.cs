using System;
using System.IO;
using System.Text;
using Jellyfin.Plugin.Youtarr.Utils;
using Xunit;

namespace Jellyfin.Plugin.Youtarr.Tests.Utils;

/// <summary>
/// Unit tests for <see cref="PathUtils"/> — pure string/path/XML logic with no Jellyfin types.
/// </summary>
public class PathUtilsTests
{
    // ---- GetChannelNameFromPath ----

    [Fact]
    public void GetChannelNameFromPath_SimplePath_ReturnsFolderName()
    {
        var path = Path.Combine("/media", "MyChannel");
        Assert.Equal("MyChannel", PathUtils.GetChannelNameFromPath(path));
    }

    [Fact]
    public void GetChannelNameFromPath_TrailingSeparator_ReturnsFolderName()
    {
        var path = Path.Combine("/media", "MyChannel") + Path.DirectorySeparatorChar;
        Assert.Equal("MyChannel", PathUtils.GetChannelNameFromPath(path));
    }

    [Fact]
    public void GetChannelNameFromPath_EmptyString_ReturnsNullOrEmpty()
    {
        var result = PathUtils.GetChannelNameFromPath(string.Empty);
        Assert.True(string.IsNullOrEmpty(result));
    }

    [Fact]
    public void GetChannelNameFromPath_WhitespaceOnly_ReturnsNullOrEmpty()
    {
        var result = PathUtils.GetChannelNameFromPath("   ");
        Assert.True(string.IsNullOrWhiteSpace(result));
    }

    [Fact]
    public void GetChannelNameFromPath_Null_DoesNotThrow_ReturnsNullOrEmpty()
    {
        var result = PathUtils.GetChannelNameFromPath(null!);
        Assert.True(string.IsNullOrEmpty(result));
    }

    // ---- ReadStudioFromMovieNfo ----

    [Fact]
    public void ReadStudioFromMovieNfo_ValidStudio_ReturnsTrimmedValue()
    {
        var dir = CreateTempDir();
        try
        {
            var nfo = Path.Combine(dir, "video.nfo");
            File.WriteAllText(
                nfo,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<movie>\n  <title>T</title>\n  <studio>  My YouTube Channel  </studio>\n</movie>",
                Encoding.UTF8);

            Assert.Equal("My YouTube Channel", PathUtils.ReadStudioFromMovieNfo(nfo));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ReadStudioFromMovieNfo_NoStudioElement_ReturnsNull()
    {
        var dir = CreateTempDir();
        try
        {
            var nfo = Path.Combine(dir, "video.nfo");
            File.WriteAllText(
                nfo,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<movie>\n  <title>No studio here</title>\n</movie>",
                Encoding.UTF8);

            Assert.Null(PathUtils.ReadStudioFromMovieNfo(nfo));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ReadStudioFromMovieNfo_MalformedXml_ReturnsNull_NoThrow()
    {
        var dir = CreateTempDir();
        try
        {
            var nfo = Path.Combine(dir, "broken.nfo");
            File.WriteAllText(nfo, "this is <not> valid <xml", Encoding.UTF8);

            Assert.Null(PathUtils.ReadStudioFromMovieNfo(nfo));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ReadStudioFromMovieNfo_NonExistentFile_ReturnsNull_NoThrow()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".nfo");
        Assert.Null(PathUtils.ReadStudioFromMovieNfo(missing));
    }

    // ---- FindFirstNfoInFolder ----

    [Fact]
    public void FindFirstNfoInFolder_WithNfo_ReturnsNfoPath()
    {
        var dir = CreateTempDir();
        try
        {
            var nfo = Path.Combine(dir, "video.nfo");
            File.WriteAllText(nfo, "<movie></movie>", Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "video.mp4"), string.Empty);

            var found = PathUtils.FindFirstNfoInFolder(dir);
            Assert.NotNull(found);
            Assert.Equal(".nfo", Path.GetExtension(found));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FindFirstNfoInFolder_NoNfo_ReturnsNull()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "video.mp4"), string.Empty);
            Assert.Null(PathUtils.FindFirstNfoInFolder(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FindFirstNfoInFolder_NonExistentFolder_ReturnsNull_NoThrow()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.Null(PathUtils.FindFirstNfoInFolder(missing));
    }

    // ---- FindNfoForVideo (CMP-01 flat + CMP-02 nested) ----

    [Fact]
    public void FindNfoForVideo_NfoExists_ReturnsSiblingPath()
    {
        // CMP-01 flat layout: Channel/video.mp4 + Channel/video.nfo.
        var dir = CreateTempDir();
        try
        {
            var video = Path.Combine(dir, "video.mp4");
            var nfo = Path.Combine(dir, "video.nfo");
            File.WriteAllText(video, string.Empty);
            File.WriteAllText(nfo, "<movie></movie>", Encoding.UTF8);

            var found = PathUtils.FindNfoForVideo(video);
            Assert.Equal(nfo, found);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FindNfoForVideo_NfoMissing_ReturnsNull()
    {
        var dir = CreateTempDir();
        try
        {
            var video = Path.Combine(dir, "video.mp4");
            File.WriteAllText(video, string.Empty);

            Assert.Null(PathUtils.FindNfoForVideo(video));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FindNfoForVideo_NestedLayout_ResolvesCorrectly()
    {
        // CMP-02 nested layout: Channel/Title/Title.mp4 + Channel/Title/Title.nfo.
        var root = CreateTempDir();
        try
        {
            var nested = Path.Combine(root, "MyChannel", "My Video Title");
            Directory.CreateDirectory(nested);
            var video = Path.Combine(nested, "My Video Title.mp4");
            var nfo = Path.Combine(nested, "My Video Title.nfo");
            File.WriteAllText(video, string.Empty);
            File.WriteAllText(nfo, "<movie></movie>", Encoding.UTF8);

            var found = PathUtils.FindNfoForVideo(video);
            Assert.Equal(nfo, found);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindNfoForVideo_NullInput_ReturnsNull()
    {
        Assert.Null(PathUtils.FindNfoForVideo(null));
        Assert.Null(PathUtils.FindNfoForVideo(string.Empty));
        Assert.Null(PathUtils.FindNfoForVideo("   "));
    }

    // ---- IsYoutarrChannelFolder ----

    [Fact]
    public void IsYoutarrChannelFolder_FlatLayoutWithYoutarrNfo_ReturnsTrue()
    {
        // CMP-01 flat layout: a Youtarr <movie> NFO (with a youtube uniqueid) lives directly
        // in the channel folder.
        var dir = CreateTempDir();
        try
        {
            WriteYoutarrNfo(Path.Combine(dir, "video.nfo"), "dQw4w9WgXcQ");
            Assert.True(PathUtils.IsYoutarrChannelFolder(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void IsYoutarrChannelFolder_NestedLayoutWithYoutarrNfo_ReturnsTrue()
    {
        // CMP-02 nested layout: the Youtarr NFO sits one level down in a per-video subdirectory.
        var dir = CreateTempDir();
        try
        {
            var nested = Path.Combine(dir, "My Video Title");
            Directory.CreateDirectory(nested);
            WriteYoutarrNfo(Path.Combine(nested, "My Video Title.nfo"), "abc123XYZ_-");
            Assert.True(PathUtils.IsYoutarrChannelFolder(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void IsYoutarrChannelFolder_EpisodedetailsNfo_ReturnsFalse()
    {
        // A non-Youtarr NFO (<episodedetails>) parses to null → not a Youtarr channel.
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(
                Path.Combine(dir, "episode.nfo"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><episodedetails><title>Some TV Show</title></episodedetails>",
                Encoding.UTF8);
            Assert.False(PathUtils.IsYoutarrChannelFolder(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void IsYoutarrChannelFolder_MovieNfoWithoutYouTubeId_ReturnsFalse()
    {
        // A plain <movie> NFO from a movie library has no youtube id → not a Youtarr channel.
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(
                Path.Combine(dir, "movie.nfo"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><movie><title>The Matrix</title><studio>Warner Bros</studio></movie>",
                Encoding.UTF8);
            Assert.False(PathUtils.IsYoutarrChannelFolder(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void IsYoutarrChannelFolder_EmptyFolder_ReturnsFalse()
    {
        var dir = CreateTempDir();
        try
        {
            Assert.False(PathUtils.IsYoutarrChannelFolder(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void IsYoutarrChannelFolder_NullOrNonExistentPath_ReturnsFalse_NoThrow()
    {
        Assert.False(PathUtils.IsYoutarrChannelFolder(null));
        Assert.False(PathUtils.IsYoutarrChannelFolder(string.Empty));
        Assert.False(PathUtils.IsYoutarrChannelFolder("   "));

        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.False(PathUtils.IsYoutarrChannelFolder(missing));
    }

    /// <summary>
    /// Writes a minimal Youtarr <c>&lt;movie&gt;</c> NFO carrying a <c>&lt;uniqueid type="youtube"&gt;</c>,
    /// the on-disk evidence that marks a folder as a Youtarr channel.
    /// </summary>
    private static void WriteYoutarrNfo(string path, string youTubeId)
    {
        File.WriteAllText(
            path,
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<movie>\n  <title>A Video</title>\n  <uniqueid type=\"youtube\">"
                + youTubeId
                + "</uniqueid>\n</movie>",
            Encoding.UTF8);
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "youtarr-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
