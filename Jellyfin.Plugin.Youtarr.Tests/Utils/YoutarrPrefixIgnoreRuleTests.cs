using Jellyfin.Plugin.Youtarr.Utils;
using MediaBrowser.Model.IO;
using Xunit;

namespace Jellyfin.Plugin.Youtarr.Tests.Utils;

/// <summary>
/// Unit tests for <see cref="YoutarrPrefixIgnoreRule"/> — the resolver ignore rule that
/// suppresses Youtarr <c>__prefix</c> grouping directories (T-01-05).
/// </summary>
public class YoutarrPrefixIgnoreRuleTests
{
    private readonly YoutarrPrefixIgnoreRule _rule = new();

    [Fact]
    public void ShouldIgnore_DoubleUnderscoreDirectory_Kids_ReturnsTrue()
    {
        var info = Dir("__kids");
        Assert.True(_rule.ShouldIgnore(info, parent: null));
    }

    [Fact]
    public void ShouldIgnore_DoubleUnderscoreDirectory_Music_ReturnsTrue()
    {
        var info = Dir("__music");
        Assert.True(_rule.ShouldIgnore(info, parent: null));
    }

    [Fact]
    public void ShouldIgnore_NormalChannelDirectory_ReturnsFalse()
    {
        var info = Dir("MyChannel");
        Assert.False(_rule.ShouldIgnore(info, parent: null));
    }

    [Fact]
    public void ShouldIgnore_DoubleUnderscoreFile_ReturnsFalse()
    {
        // Rule applies to directories only — a file named __weird.nfo must NOT be ignored.
        var info = File("__weird.nfo");
        Assert.False(_rule.ShouldIgnore(info, parent: null));
    }

    [Fact]
    public void ShouldIgnore_SingleUnderscoreDirectory_ReturnsFalse()
    {
        // Requires exactly the "__" (two underscore) prefix.
        var info = Dir("_single_underscore");
        Assert.False(_rule.ShouldIgnore(info, parent: null));
    }

    private static FileSystemMetadata Dir(string name) => new()
    {
        Name = name,
        FullName = "/media/" + name,
        IsDirectory = true,
        Exists = true,
    };

    private static FileSystemMetadata File(string name) => new()
    {
        Name = name,
        FullName = "/media/MyChannel/" + name,
        IsDirectory = false,
        Exists = true,
    };
}
