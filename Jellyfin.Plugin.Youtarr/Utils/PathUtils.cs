using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Jellyfin.Plugin.Youtarr.Utils;

/// <summary>
/// Pure path/string/XML helpers for deriving channel metadata from a Youtarr
/// download folder. Deliberately free of any Jellyfin (<c>MediaBrowser.*</c>) types
/// so the logic can be unit-tested in isolation without a running server.
/// </summary>
public static class PathUtils
{
    /// <summary>
    /// Derives the channel (Series) name from a channel folder path: the final path
    /// segment, tolerant of a trailing directory separator. Returns <see langword="null"/>
    /// for null/empty/whitespace input rather than throwing.
    /// </summary>
    /// <param name="path">The channel folder path.</param>
    /// <returns>The folder name, or <see langword="null"/> if it cannot be derived.</returns>
    public static string? GetChannelNameFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>
    /// Returns the first <c>*.nfo</c> file (top directory only) in the given folder,
    /// or <see langword="null"/> when none exist or the folder is missing/unreadable.
    /// </summary>
    /// <param name="folderPath">The folder to scan.</param>
    /// <returns>The full path of the first NFO, or <see langword="null"/>.</returns>
    public static string? FindFirstNfoInFolder(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return null;
        }

        try
        {
            return Directory
                .EnumerateFiles(folderPath, "*.nfo", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves the co-located <c>.nfo</c> sidecar for a video file: the same-basename
    /// <c>.nfo</c> in the same directory. This works identically for both Youtarr layouts —
    /// flat (<c>Channel/video.mp4</c> + <c>Channel/video.nfo</c>, CMP-01) and nested
    /// (<c>Channel/Title/Title.mp4</c> + <c>Channel/Title/Title.nfo</c>, CMP-02) — because both
    /// place the NFO next to the video with a matching base name. Returns <see langword="null"/>
    /// for null/empty/whitespace input or when no sidecar exists. Pure: the only side effect is
    /// the <see cref="File.Exists(string)"/> probe.
    /// </summary>
    /// <param name="videoPath">Full path to the video file.</param>
    /// <returns>The sibling <c>.nfo</c> path when present, otherwise <see langword="null"/>.</returns>
    public static string? FindNfoForVideo(string? videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
        {
            return null;
        }

        var nfoPath = Path.ChangeExtension(videoPath, ".nfo");
        return File.Exists(nfoPath) ? nfoPath : null;
    }

    /// <summary>
    /// Reads the <c>&lt;studio&gt;</c> element from a Youtarr <c>&lt;movie&gt;</c>-rooted NFO,
    /// returning its trimmed text. Any error (missing file, malformed XML, missing element)
    /// yields <see langword="null"/> — never throws, so a single bad NFO cannot crash a scan.
    /// </summary>
    /// <param name="nfoPath">Path to the NFO file.</param>
    /// <returns>The trimmed studio value, or <see langword="null"/>.</returns>
    public static string? ReadStudioFromMovieNfo(string? nfoPath)
    {
        if (string.IsNullOrWhiteSpace(nfoPath))
        {
            return null;
        }

        try
        {
            using var reader = new StreamReader(nfoPath, Encoding.UTF8);
            var doc = XDocument.Load(reader);
            var studio = doc.Root?.Element("studio")?.Value?.Trim();
            return string.IsNullOrWhiteSpace(studio) ? null : studio;
        }
        catch
        {
            // Attacker-influenceable input (T-01-03): swallow all parse/IO errors to null.
            return null;
        }
    }
}
