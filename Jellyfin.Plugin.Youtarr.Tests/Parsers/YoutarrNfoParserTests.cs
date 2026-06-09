using System;
using System.IO;
using System.Text;
using System.Xml;
using Jellyfin.Plugin.Youtarr.Models;
using Jellyfin.Plugin.Youtarr.Parsers;
using Xunit;

namespace Jellyfin.Plugin.Youtarr.Tests.Parsers;

/// <summary>
/// Unit tests for <see cref="YoutarrNfoParser"/> — pure NFO-to-DTO mapping with EPI-07
/// date validation enforced at the parse boundary. Each test writes a fixture NFO to a
/// temp file (UTF-8) and asserts the resulting <see cref="YoutarrVideoData"/>.
/// </summary>
public class YoutarrNfoParserTests
{
    [Fact]
    public void Parse_FullNfo_AllFieldsMapped()
    {
        var year = DateTime.UtcNow.Year;
        var nfo = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<movie>
  <title>  My Great Video  </title>
  <plot>A full description.</plot>
  <premiered>2024-03-15</premiered>
  <dateadded>2024-03-16 10:20:30</dateadded>
  <runtime>12</runtime>
  <studio>My Channel</studio>
  <mpaa>TV-14</mpaa>
  <uniqueid type=""youtube"">dQw4w9WgXcQ</uniqueid>
  <genre>Music</genre>
  <genre>Comedy</genre>
  <tag>tutorial</tag>
  <fileinfo>
    <streamdetails>
      <video>
        <durationinseconds>742</durationinseconds>
      </video>
    </streamdetails>
  </fileinfo>
</movie>";
        var data = ParseFixture(nfo);

        Assert.NotNull(data);
        Assert.Equal("My Great Video", data!.Title);
        Assert.Equal("A full description.", data.Plot);
        Assert.Equal(new DateTime(2024, 3, 15), data.PremiereDate);
        Assert.NotNull(data.DateAdded);
        Assert.Equal(new DateTime(2024, 3, 16, 10, 20, 30), data.DateAdded);
        Assert.Equal(12, data.RuntimeMinutes);
        Assert.Equal(742, data.DurationInSeconds);
        Assert.Equal("My Channel", data.Studio);
        Assert.Equal("TV-14", data.MpaaRating);
        Assert.Equal("dQw4w9WgXcQ", data.YouTubeId);
        Assert.Equal(2, data.Genres.Count);
        Assert.Contains("Music", data.Genres);
        Assert.Contains("Comedy", data.Genres);
        Assert.Single(data.Tags);
        Assert.Contains("tutorial", data.Tags);
        _ = year;
    }

    [Fact]
    public void Parse_NotMovieRoot_ReturnsNull()
    {
        var nfo = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<episodedetails><title>X</title></episodedetails>";
        Assert.Null(ParseFixture(nfo));
    }

    [Fact]
    public void Parse_MissingPremiered_DateNull()
    {
        var nfo = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<movie><title>X</title><premiered></premiered></movie>";
        var data = ParseFixture(nfo);
        Assert.NotNull(data);
        Assert.Null(data!.PremiereDate);
    }

    [Fact]
    public void Parse_NoPremieredElement_DateNull()
    {
        var nfo = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<movie><title>X</title></movie>";
        var data = ParseFixture(nfo);
        Assert.NotNull(data);
        Assert.Null(data!.PremiereDate);
    }

    [Fact]
    public void Parse_InvalidPremiered_DateNull()
    {
        var nfo = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<movie><title>X</title><premiered>not-a-date</premiered></movie>";
        var data = ParseFixture(nfo);
        Assert.NotNull(data);
        Assert.Null(data!.PremiereDate);
    }

    [Fact]
    public void Parse_BeforeYouTube_DateNull()
    {
        var nfo = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<movie><title>X</title><premiered>2003-05-01</premiered></movie>";
        var data = ParseFixture(nfo);
        Assert.NotNull(data);
        Assert.Null(data!.PremiereDate);
    }

    [Fact]
    public void Parse_MinValueDate_DateNull()
    {
        var nfo = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<movie><title>X</title><premiered>0001-01-01</premiered></movie>";
        var data = ParseFixture(nfo);
        Assert.NotNull(data);
        Assert.Null(data!.PremiereDate);
    }

    [Fact]
    public void Parse_UnixEpochDate_DateNull()
    {
        var nfo = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<movie><title>X</title><premiered>1970-01-01</premiered></movie>";
        var data = ParseFixture(nfo);
        Assert.NotNull(data);
        Assert.Null(data!.PremiereDate);
    }

    [Fact]
    public void Parse_FutureDate_DateNull()
    {
        var future = DateTime.UtcNow.Year + 5;
        var nfo = $"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<movie><title>X</title><premiered>{future}-01-01</premiered></movie>";
        var data = ParseFixture(nfo);
        Assert.NotNull(data);
        Assert.Null(data!.PremiereDate);
    }

    [Fact]
    public void Parse_ValidPremiered_DateParsed()
    {
        var nfo = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<movie><title>X</title><premiered>2024-03-15</premiered></movie>";
        var data = ParseFixture(nfo);
        Assert.NotNull(data);
        Assert.Equal(new DateTime(2024, 3, 15), data!.PremiereDate);
    }

    [Fact]
    public void Parse_DateAdded_CapturedWithoutYouTubeGuard()
    {
        // dateadded is a download timestamp, not an upload date — no >=2005 guard applied.
        var nfo = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<movie><title>X</title><dateadded>2026-06-09 08:00:00</dateadded></movie>";
        var data = ParseFixture(nfo);
        Assert.NotNull(data);
        Assert.Equal(new DateTime(2026, 6, 9, 8, 0, 0), data!.DateAdded);
    }

    [Fact]
    public void Parse_UniqueIdYouTubeType_ExtractsId()
    {
        var nfo = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<movie><title>X</title>" +
                  "<uniqueid type=\"imdb\">tt123</uniqueid>" +
                  "<uniqueid type=\"YouTube\">abc123XYZ</uniqueid>" +
                  "<youtubeid>shouldNotWin</youtubeid></movie>";
        var data = ParseFixture(nfo);
        Assert.NotNull(data);
        Assert.Equal("abc123XYZ", data!.YouTubeId);
    }

    [Fact]
    public void Parse_YouTubeIdFallback_UsedWhenNoUniqueId()
    {
        var nfo = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<movie><title>X</title><youtubeid>fallbackId</youtubeid></movie>";
        var data = ParseFixture(nfo);
        Assert.NotNull(data);
        Assert.Equal("fallbackId", data!.YouTubeId);
    }

    [Fact]
    public void Parse_DurationInSeconds_PreferredOverRuntime()
    {
        var nfo = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<movie><title>X</title><runtime>10</runtime>" +
                  "<fileinfo><streamdetails><video><durationinseconds>635</durationinseconds></video></streamdetails></fileinfo></movie>";
        var data = ParseFixture(nfo);
        Assert.NotNull(data);
        Assert.Equal(10, data!.RuntimeMinutes);
        Assert.Equal(635, data.DurationInSeconds);
    }

    [Fact]
    public void Parse_MultipleGenres_AllCaptured()
    {
        var nfo = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<movie><title>X</title>" +
                  "<genre>A</genre><genre>B</genre><genre>C</genre></movie>";
        var data = ParseFixture(nfo);
        Assert.NotNull(data);
        Assert.Equal(3, data!.Genres.Count);
    }

    [Fact]
    public void Parse_MultipleTags_AllCaptured()
    {
        var nfo = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<movie><title>X</title>" +
                  "<tag>one</tag><tag>two</tag><tag>three</tag></movie>";
        var data = ParseFixture(nfo);
        Assert.NotNull(data);
        Assert.Equal(3, data!.Tags.Count);
    }

    [Fact]
    public void Parse_EmptyGenreAndTag_Skipped()
    {
        var nfo = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<movie><title>X</title>" +
                  "<genre>Real</genre><genre>  </genre><tag></tag><tag>kept</tag></movie>";
        var data = ParseFixture(nfo);
        Assert.NotNull(data);
        Assert.Single(data!.Genres);
        Assert.Single(data.Tags);
    }

    [Fact]
    public void Parse_EmojiInPlot_DeserializesCorrectly()
    {
        var nfo = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<movie><title>X</title><plot>Hello \U0001F600 world \U0001F680</plot></movie>";
        var data = ParseFixture(nfo);
        Assert.NotNull(data);
        Assert.Equal("Hello \U0001F600 world \U0001F680", data!.Plot);
    }

    [Fact]
    public void Parse_XmlEntitiesInPlot_DecodedCorrectly()
    {
        var nfo = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<movie><title>X</title><plot>Tom &amp; Jerry &lt;3 &gt; all</plot></movie>";
        var data = ParseFixture(nfo);
        Assert.NotNull(data);
        Assert.Equal("Tom & Jerry <3 > all", data!.Plot);
    }

    [Fact]
    public void Parse_VeryLongPlot_NotTruncatedAtParseBoundary()
    {
        var longText = new string('a', 6000);
        var nfo = $"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<movie><title>X</title><plot>{longText}</plot></movie>";
        var data = ParseFixture(nfo);
        Assert.NotNull(data);
        Assert.Equal(6000, data!.Plot!.Length);
    }

    [Fact]
    public void Parse_MalformedXml_Throws()
    {
        Assert.Throws<XmlException>(() =>
            ParseFixture("<?xml version=\"1.0\"?>\n<movie><title>unclosed"));
    }

    private static YoutarrVideoData? ParseFixture(string nfoContent)
    {
        var dir = Path.Combine(Path.GetTempPath(), "youtarr-parser-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var nfo = Path.Combine(dir, "video.nfo");
            File.WriteAllText(nfo, nfoContent, new UTF8Encoding(false));
            return YoutarrNfoParser.Parse(nfo);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
