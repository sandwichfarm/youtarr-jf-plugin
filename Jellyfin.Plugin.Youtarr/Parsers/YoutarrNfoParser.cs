using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;
using Jellyfin.Plugin.Youtarr.Models;

namespace Jellyfin.Plugin.Youtarr.Parsers;

/// <summary>
/// Parses a Youtarr <c>&lt;movie&gt;</c>-rooted NFO into a strongly-typed
/// <see cref="YoutarrVideoData"/>. Pure logic (input: a file path; output: a DTO) with no
/// Jellyfin (<c>MediaBrowser.*</c>) dependency, so every field mapping and date edge case is
/// unit-testable without a running server. EPI-07 date robustness is enforced here at the
/// parse boundary.
/// </summary>
public static class YoutarrNfoParser
{
    /// <summary>
    /// Parses a Youtarr <c>&lt;movie&gt;</c>-rooted NFO file.
    /// </summary>
    /// <param name="nfoPath">Path to the NFO file.</param>
    /// <returns>
    /// A populated <see cref="YoutarrVideoData"/>, or <see langword="null"/> when the root
    /// element is not <c>&lt;movie&gt;</c> (e.g. an <c>&lt;episodedetails&gt;</c> NFO).
    /// </returns>
    /// <exception cref="System.Xml.XmlException">
    /// Thrown when the file contains malformed XML. The caller (the Episode provider in plan
    /// 02-03) wraps this in a try/catch so a single bad NFO cannot crash the library scan.
    /// </exception>
    public static YoutarrVideoData? Parse(string nfoPath)
    {
        // Pitfall 3: always use the StreamReader overload with explicit UTF-8 so BOM-less
        // UTF-8 (and emoji) decode correctly, regardless of the XML declaration's encoding.
        using var reader = new StreamReader(nfoPath, Encoding.UTF8);
        var doc = XDocument.Load(reader);

        var root = doc.Root;

        // Only Youtarr <movie> NFOs are ours; <episodedetails>/<tvshow>/etc. are not.
        if (root is null || !string.Equals(root.Name.LocalName, "movie", StringComparison.Ordinal))
        {
            return null;
        }

        return new YoutarrVideoData
        {
            Title = Trimmed(root.Element("title")?.Value),
            Plot = Trimmed(root.Element("plot")?.Value),
            PremiereDate = ParsePremiereDate(root.Element("premiered")?.Value),
            DateAdded = ParseDate(root.Element("dateadded")?.Value),
            RuntimeMinutes = ParseInt(root.Element("runtime")?.Value),
            DurationInSeconds = ParseInt(
                root.Element("fileinfo")
                    ?.Element("streamdetails")
                    ?.Element("video")
                    ?.Element("durationinseconds")
                    ?.Value),
            Studio = Trimmed(root.Element("studio")?.Value),
            YouTubeId = ParseYouTubeId(root),
            MpaaRating = Trimmed(root.Element("mpaa")?.Value),
            Genres = CollectNonEmpty(root, "genre"),
            Tags = CollectNonEmpty(root, "tag"),
        };
    }

    private static string? Trimmed(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static int? ParseInt(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static DateTime? ParseDate(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && DateTime.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static DateTime? ParsePremiereDate(string? value)
    {
        var parsed = ParseDate(value);
        if (parsed.HasValue && IsValidYouTubeDate(parsed.Value))
        {
            return parsed;
        }

        return null;
    }

    private static string? ParseYouTubeId(XElement root)
    {
        // <uniqueid type="youtube"> takes priority (case-insensitive attribute compare).
        foreach (var uid in root.Elements("uniqueid"))
        {
            if (string.Equals(uid.Attribute("type")?.Value, "youtube", StringComparison.OrdinalIgnoreCase))
            {
                var value = Trimmed(uid.Value);
                if (value is not null)
                {
                    return value;
                }
            }
        }

        // Fall back to <youtubeid>.
        return Trimmed(root.Element("youtubeid")?.Value);
    }

    private static IReadOnlyList<string> CollectNonEmpty(XElement root, string elementName)
    {
        var values = new List<string>();
        foreach (var element in root.Elements(elementName))
        {
            var value = Trimmed(element.Value);
            if (value is not null)
            {
                values.Add(value);
            }
        }

        return values;
    }

    /// <summary>
    /// EPI-07 date guard. YouTube launched 2005-04-23, so a year before 2005 (this also
    /// rejects <see cref="DateTime.MinValue"/> at 0001 and the Unix epoch at 1970) or more
    /// than two years in the future is treated as a parsing error rather than a real date.
    /// </summary>
    private static bool IsValidYouTubeDate(DateTime dt)
    {
        return dt.Year >= 2005 && dt.Year <= DateTime.UtcNow.Year + 2;
    }
}
