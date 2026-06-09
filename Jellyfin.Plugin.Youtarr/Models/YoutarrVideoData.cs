using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Youtarr.Models;

/// <summary>
/// Plain data-transfer object holding every field parsed from a Youtarr
/// <c>&lt;movie&gt;</c> NFO. Deliberately free of any Jellyfin (<c>MediaBrowser.*</c>)
/// types so NFO parsing can be unit-tested in isolation; the Episode provider
/// (Phase 2 plan 02-03) maps this DTO onto a Jellyfin <c>Episode</c>.
/// </summary>
public class YoutarrVideoData
{
    /// <summary>Gets the video title (<c>&lt;title&gt;</c>), trimmed.</summary>
    public string? Title { get; init; }

    /// <summary>Gets the description/plot (<c>&lt;plot&gt;</c>), trimmed and entity-decoded.</summary>
    public string? Plot { get; init; }

    /// <summary>
    /// Gets the upload date (<c>&lt;premiered&gt;</c>). Only set when the parsed value is a
    /// valid YouTube-era date (year &gt;= 2005 and &lt;= now+2); otherwise <see langword="null"/>.
    /// </summary>
    public DateTime? PremiereDate { get; init; }

    /// <summary>
    /// Gets the download timestamp (<c>&lt;dateadded&gt;</c>). Captured without a YouTube-date
    /// guard so the provider can use it as an EPI-07 fallback. Not the upload date.
    /// </summary>
    public DateTime? DateAdded { get; init; }

    /// <summary>Gets the runtime in minutes (<c>&lt;runtime&gt;</c>).</summary>
    public int? RuntimeMinutes { get; init; }

    /// <summary>
    /// Gets the precise duration in seconds
    /// (<c>&lt;fileinfo&gt;&lt;streamdetails&gt;&lt;video&gt;&lt;durationinseconds&gt;</c>).
    /// Preferred over <see cref="RuntimeMinutes"/> by the provider.
    /// </summary>
    public int? DurationInSeconds { get; init; }

    /// <summary>Gets the channel name (<c>&lt;studio&gt;</c>), trimmed.</summary>
    public string? Studio { get; init; }

    /// <summary>
    /// Gets the YouTube video ID, from <c>&lt;uniqueid type="youtube"&gt;</c> (preferred)
    /// or <c>&lt;youtubeid&gt;</c> (fallback).
    /// </summary>
    public string? YouTubeId { get; init; }

    /// <summary>Gets the content rating (<c>&lt;mpaa&gt;</c>), trimmed.</summary>
    public string? MpaaRating { get; init; }

    /// <summary>Gets all non-empty <c>&lt;genre&gt;</c> values.</summary>
    public IReadOnlyList<string> Genres { get; init; } = new List<string>();

    /// <summary>Gets all non-empty <c>&lt;tag&gt;</c> values.</summary>
    public IReadOnlyList<string> Tags { get; init; } = new List<string>();
}
