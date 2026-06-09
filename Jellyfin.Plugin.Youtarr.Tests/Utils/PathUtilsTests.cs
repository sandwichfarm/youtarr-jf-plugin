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

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "youtarr-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
