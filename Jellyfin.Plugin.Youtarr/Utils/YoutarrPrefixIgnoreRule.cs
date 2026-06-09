using System;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Model.IO;

namespace Jellyfin.Plugin.Youtarr.Utils;

/// <summary>
/// Tells Jellyfin's library scanner to skip any directory whose name starts with
/// two underscores (Youtarr's grouping prefix convention: __kids, __music, __news).
/// Without this rule, SeriesResolver classifies these prefix folders as Series,
/// producing phantom entries (T-01-05). The rule is intentionally limited to
/// directories so files like <c>__weird.nfo</c> are never affected.
/// </summary>
public class YoutarrPrefixIgnoreRule : IResolverIgnoreRule
{
    /// <inheritdoc />
    public bool ShouldIgnore(FileSystemMetadata fileInfo, BaseItem? parent)
    {
        // Only apply to directories (prefix folders are directories, not files).
        if (fileInfo is null || !fileInfo.IsDirectory)
        {
            return false;
        }

        // Assumption A1: fileInfo.Name is the bare directory name (not the full path).
        // If 01-03's live test shows A1 is false, switch to Path.GetFileName(fileInfo.FullName).
        return fileInfo.Name.StartsWith("__", StringComparison.Ordinal);
    }
}
