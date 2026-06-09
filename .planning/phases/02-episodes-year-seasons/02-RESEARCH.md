# Phase 2: Episodes + Year-Seasons — Research

**Researched:** 2026-06-09
**Domain:** Jellyfin 10.10.x `ILocalMetadataProvider<Episode>`, `<movie>` NFO field mapping, `ParentIndexNumber`-based virtual season creation, episode numbering, date fallbacks
**Confidence:** HIGH (core mechanism verified against Jellyfin source and tubearchivist-jf-plugin; one MEDIUM caveat on virtual-season live validation)

---

## Summary

Phase 2 is the highest-risk phase of the project. Its job is to make every downloaded video appear as an `Episode` under its channel's `Series`, grouped into year-named `Season` containers, with all per-video metadata populated from Youtarr's `<movie>` NFO files.

Three facts are locked from prior research and the built codebase:

1. The Jellyfin library scanner already classifies video files as `Episode` items (because the library is a "Shows" type and each parent folder is a Series). The plugin does NOT need to reclassify anything.
2. Jellyfin's built-in `EpisodeNfoProvider` looks for `<episodedetails>` XML root and finds nothing in Youtarr's `<movie>` NFOs — it silently returns `HasMetadata = false`. The plugin MUST supply its own `ILocalMetadataProvider<Episode>` that parses the `<movie>` root.
3. Setting `Episode.ParentIndexNumber = uploadYear` triggers `SeriesMetadataService.CreateSeasonsAsync()` to auto-create virtual Season containers. This is the ONLY mechanism the plugin needs for year-seasons. No physical `Season YYYY` folders required. The mechanism is verified against `SeriesMetadataService.cs` source: it iterates Episode items, reads `ParentIndexNumber`, and creates virtual seasons for any value that has no matching physical folder. [CITED: github.com/jellyfin/jellyfin SeriesMetadataService.cs]

The critical unresolved question is whether this virtual-season creation works reliably in Jellyfin **10.10.7** for a flat-layout library (no year subdirectories on disk). ARCHITECTURE.md and PITFALLS.md appear contradictory on this; the reconciliation (documented in SUMMARY.md) is that the two issues cited (#11916 folder-naming regression, #13197 NFO-season-tag ignored) apply to *physical* season folders, not virtual ones. SUMMARY.md recommends: front-load a live validation with a single stub provider before building the full pipeline. That remains the recommendation — Phase 2 Wave 0 MUST be a one-episode live probe.

If the virtual-season probe passes (expected), proceed with the full provider. If it fails, the fallback is a post-scan `ILibraryPostScanTask` that patches season assignments — but do not build this until the primary path is confirmed broken on the actual 10.10.7 server.

**Primary recommendation:** Implement `YoutarrEpisodeNfoProvider : ILocalMetadataProvider<Episode>` (following the exact codebase patterns from Phase 1), wire the `LIB-04` flatten toggle through the already-stubbed `PluginConfiguration.YearSeasons`, add two more config properties (`EpisodeNumberingScheme`, `MaxDescriptionLength`), and front-load the live probe before any other task.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Episode item classification (video file → Episode) | Jellyfin built-in (EpisodeResolver) | — | Already works from "Shows" library type; plugin does nothing here |
| Episode metadata (title, plot, dates, genres, etc.) | Plugin (`YoutarrEpisodeNfoProvider`) | — | Built-in EpisodeNfoProvider cannot read `<movie>` NFO root |
| Year-season container creation | Jellyfin built-in (`SeriesMetadataService.CreateSeasonsAsync`) | — | Plugin sets `ParentIndexNumber`; Jellyfin creates the Season entity |
| Season-flatten toggle | Plugin (`YoutarrEpisodeNfoProvider` reads config) | — | When `YearSeasons = false`, provider returns `ParentIndexNumber = 1` for all episodes |
| Episode numbering (IndexNumber) | Plugin (`YoutarrEpisodeNfoProvider`) | — | No SxxExx pattern in filenames; plugin must supply this or episodes all get IndexNumber=0 |
| NFO sidecar discovery (flat vs nested layout) | Plugin (`PathUtils` extension) | — | Same-basename .nfo next to .mp4 in both layouts |
| Date fallback chain (missing `<premiered>`) | Plugin (`YoutarrEpisodeNfoProvider`) | — | EPI-07: must not crash; must assign fallback season/number |
| Description truncation | Plugin (`YoutarrEpisodeNfoProvider`) | — | EPI-04: configurable max length, default 500 chars |
| Configuration surface (3 new fields) | Plugin (`PluginConfiguration`) | — | `EpisodeNumberingScheme`, `MaxDescriptionLength` added Phase 2 |

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| LIB-02 | Each downloaded video appears as an Episode under its channel's Series | EpisodeResolver classifies video files automatically; plugin provides metadata via ILocalMetadataProvider<Episode> |
| LIB-03 | Episodes grouped into seasons by upload year by default | `ParentIndexNumber = premiered.Year`; `SeriesMetadataService.CreateSeasonsAsync` creates virtual seasons |
| LIB-04 | User can turn year-seasons off to collapse to a single season | `PluginConfiguration.YearSeasons` already stubbed; when false, return `ParentIndexNumber = 1` |
| EPI-01 | Title, plot/description, premiered date, runtime from `<movie>` NFO | Direct XDocument field mapping: `<title>`, `<plot>`, `<premiered>`, `<runtime>`/`<durationinseconds>` |
| EPI-02 | Genres and tags from NFO | `<genre>` elements → `Episode.Genres[]`; `<tag>` elements → `Episode.Tags[]` |
| EPI-03 | YouTube video ID as provider ID | `<uniqueid type="youtube">` or `<youtubeid>` → `Episode.ProviderIds["YouTube"]` |
| EPI-04 | Description truncated to configurable max (default 500) | Port `FormatDescription()` from tubearchivist-jf-plugin; `\n` → `<br>` |
| EPI-05 | Stable episode number; selectable scheme (date-ordered default vs YYYYMMDD) | `IndexNumber = (year*10000)+(month*100)+day` for YYYYMMDD; `null` for default (Jellyfin auto-sequences) |
| EPI-06 | Content rating from `<mpaa>` when present | `<mpaa>` → `Episode.OfficialRating` |
| EPI-07 | Missing/invalid upload date handled gracefully | Date fallback chain: `<premiered>` → `<dateadded>` → filename → `ParentIndexNumber = 0` (Season 0) |
| CMP-01 | Works with Youtarr flat per-channel layout | NFO sidecar = same dir as .mp4, same base name, `.nfo` extension |
| CMP-02 | Works with Youtarr nested per-video subfolder layout | NFO sidecar in subfolder next to .mp4; path resolution identical |
</phase_requirements>

---

## Project Constraints (from CLAUDE.md)

1. **Tech stack non-negotiable:** C# / net8.0; `Jellyfin.Controller 10.10.7` + `Jellyfin.Model 10.10.7` with `<ExcludeAssets>runtime</ExcludeAssets>`.
2. **File-only:** No HTTP calls, no Youtarr API key, no network dependencies anywhere in Phase 2.
3. **No custom `IItemResolver`:** Classification is done by Jellyfin's built-in resolvers; plugin only provides metadata.
4. **Read-only:** Plugin must never mutate, rename, move, or write to any user media file.
5. **No sudo from agents:** Docker commands requiring sudo must be copy-paste blocks for the operator.
6. **GSD workflow:** All code changes via `/gsd:execute-phase`.

---

## Standard Stack

### Core (no new packages required for Phase 2)

Phase 2 requires no new NuGet packages beyond what Phase 1 already uses.

| Library | Version | Purpose | Status |
|---------|---------|---------|--------|
| `Jellyfin.Controller` | `10.10.7` | `ILocalMetadataProvider<Episode>`, `Episode` type | Already in csproj [VERIFIED: NuGet registry, Phase 1] |
| `Jellyfin.Model` | `10.10.7` | `MetadataResult<Episode>`, `BaseItem` fields | Already in csproj [VERIFIED: NuGet registry, Phase 1] |
| `System.Xml.Linq` | BCL / net8.0 | XDocument parsing of `<movie>` NFO | Already used in `PathUtils.cs` [VERIFIED: Phase 1 builds clean] |
| `xunit` | `2.9.3` | Unit tests | Already in test project [VERIFIED: Phase 1] |
| `Moq` | `4.20.72` | Mocking `IDirectoryService`, `ILogger<T>` | Already in test project [VERIFIED: Phase 1] |

No `npm install`, `pip install`, or additional `dotnet add package` needed for Phase 2.

### Existing Codebase Assets to Reuse

| Asset | File | What Phase 2 Reuses |
|-------|------|---------------------|
| `PathUtils.GetChannelNameFromPath` | `Utils/PathUtils.cs` | Used in EpisodeNfoProvider for logging context |
| `PathUtils.FindFirstNfoInFolder` | `Utils/PathUtils.cs` | Extend to handle nested layout; add `FindNfoForVideo` |
| `PathUtils.ReadStudioFromMovieNfo` | `Utils/PathUtils.cs` | The XDocument/UTF-8 parsing pattern; episode parser uses same approach |
| `Constants.ProviderName` | `Constants.cs` | `Name` property on the new provider |
| `Constants.YouTubeProviderId` | `Constants.cs` | Key for `Episode.ProviderIds["YouTube"]` |
| `PluginConfiguration.YearSeasons` | `Configuration/PluginConfiguration.cs` | Already stubbed; provider reads this |
| `Plugin.Instance?.Configuration` | `Plugin.cs` | Singleton access pattern for config — same as tubearchivist-jf-plugin |
| `PluginServiceRegistrator.RegisterServices` | `PluginServiceRegistrator.cs` | Add `AddSingleton` for EpisodeNfoProvider if needed (auto-discovered) |
| `IPluginServiceRegistrator` pattern | `PluginServiceRegistrator.cs` | DI registration mechanism confirmed in Phase 1 (not `Plugin.RegisterServices`) |
| Load-test harness | `test/jellyfin-load-test/` | Extend with episode fixtures; reuse exact deploy loop |

---

## Architecture Patterns

### System Architecture Diagram

```
Youtarr flat layout on disk:
  /media/MyChannel/
    ├── Video Title [YTID].mp4          ← Episode item (classified by EpisodeResolver)
    ├── Video Title [YTID].nfo          ← <movie> NFO — ONLY source of metadata
    ├── Video Title [YTID].jpg          ← thumbnail (Phase 3 — not Phase 2)
    └── poster.jpg

Youtarr nested layout on disk:
  /media/MyChannel/
    ├── Video Title [YTID]/
    │   ├── Video Title [YTID].mp4      ← Episode item
    │   ├── Video Title [YTID].nfo
    │   └── Video Title [YTID].jpg
    └── poster.jpg

         │
         │  ItemInfo.Path = /media/MyChannel/Video Title [YTID].mp4
         ▼

  YoutarrEpisodeNfoProvider.GetMetadata(ItemInfo, IDirectoryService, CancellationToken)
         │
         ├─ NFO discovery:
         │    nfoPath = Path.ChangeExtension(videoPath, ".nfo")   ← same dir, same base, .nfo
         │    (works for both flat and nested layouts — video and NFO are co-located)
         │
         ├─ XDocument.Load(nfoPath, UTF-8)
         │
         ├─ Field mapping:
         │    <title>          → episode.Name
         │    <plot>           → episode.Overview (truncated via FormatDescription)
         │    <premiered>      → episode.PremiereDate; year → episode.ParentIndexNumber
         │    <runtime>        → episode.RunTimeTicks (minutes × 60 × TimeSpan.TicksPerSecond)
         │    <durationinseconds> → episode.RunTimeTicks (more precise, preferred)
         │    <genre>*         → episode.Genres[]
         │    <tag>*           → episode.Tags[]
         │    <uniqueid type="youtube"> or <youtubeid> → episode.ProviderIds["YouTube"]
         │    <mpaa>           → episode.OfficialRating
         │    <studio>         → episode.Studios[]
         │
         ├─ Season assignment:
         │    if (config.YearSeasons && uploadYear.HasValue)
         │        episode.ParentIndexNumber = uploadYear
         │    else
         │        episode.ParentIndexNumber = 1          ← flatten toggle
         │
         ├─ Episode numbering:
         │    if (scheme == YYYYMMDD && uploadDate.HasValue)
         │        episode.IndexNumber = (year*10000) + (month*100) + day
         │    else
         │        episode.IndexNumber = null             ← Jellyfin auto-sequences
         │
         ├─ EPI-07 date fallback chain:
         │    1. Parse <premiered> as DateTime
         │    2. If invalid/missing: parse <dateadded>
         │    3. If still invalid: log warning, ParentIndexNumber = 0, IndexNumber = null
         │
         └─ result.HasMetadata = true
                  │
                  ▼

  Jellyfin stores Episode{ParentIndexNumber=2024, IndexNumber=20240315, ...}

         │
         ▼

  SeriesMetadataService.CreateSeasonsAsync(series)
    → finds Episode.ParentIndexNumber = 2024
    → no physical Season 2024 folder on disk
    → creates virtual Season{IndexNumber=2024, Name="Season 2024"}
    → [LIVE VALIDATION REQUIRED — see Wave 0]

         │
         ▼

  Jellyfin UI: MyChannel → Season 2024 → "Video Title" (E20240315)
```

### Recommended Project Structure After Phase 2

```
Jellyfin.Plugin.Youtarr/
├── Configuration/
│   └── PluginConfiguration.cs     # +EpisodeNumberingScheme, +MaxDescriptionLength
├── Constants.cs                   # Unchanged
├── Parsers/
│   └── YoutarrNfoParser.cs        # NEW: YoutarrVideoData DTO + parse logic (extracted from EpisodeNfoProvider)
├── Models/
│   └── YoutarrVideoData.cs        # NEW: plain DTO holding all NFO fields
├── Providers/
│   ├── YoutarrSeriesNfoProvider.cs  # Unchanged from Phase 1
│   └── YoutarrEpisodeNfoProvider.cs # NEW: ILocalMetadataProvider<Episode>
└── Utils/
    ├── PathUtils.cs               # +FindNfoForVideo (sidecar discovery, flat + nested)
    └── YoutarrPrefixIgnoreRule.cs # Unchanged from Phase 1

Jellyfin.Plugin.Youtarr.Tests/
├── Parsers/
│   └── YoutarrNfoParserTests.cs   # NEW: all 15+ NFO parsing cases
├── Providers/
│   ├── YoutarrSeriesNfoProviderTests.cs  # Unchanged
│   └── YoutarrEpisodeNfoProviderTests.cs # NEW: metadata mapping, date fallbacks, numbering
└── Utils/
    ├── PathUtilsTests.cs          # +FindNfoForVideo tests
    └── YoutarrPrefixIgnoreRuleTests.cs   # Unchanged

test/jellyfin-load-test/media/
├── MyChannel/                     # Existing (Phase 1 fixture)
│   ├── test_video.mp4
│   └── test_video.nfo
├── ChannelA/                      # NEW: 2 videos in different years
│   ├── Video 2023 [ytid1].mp4
│   ├── Video 2023 [ytid1].nfo     # <premiered>2023-06-15</premiered>
│   ├── Video 2024 [ytid2].mp4
│   └── Video 2024 [ytid2].nfo     # <premiered>2024-03-20</premiered>
├── ChannelB/                      # NEW: 1 video with missing date (EPI-07)
│   ├── Missing Date [ytid3].mp4
│   └── Missing Date [ytid3].nfo   # <premiered></premiered> — intentionally empty
└── __kids/                        # Existing (Phase 1 fixture)
```

---

## Core Pattern: ILocalMetadataProvider<Episode>

### Class Skeleton (exact interface matching Phase 1 patterns)

```csharp
// Source: interface verified from Jellyfin.Controller 10.10.7 (Phase 1 live build)
// Namespace for ILocalMetadataProvider<T>: MediaBrowser.Controller.Providers
// Namespace for IHasItemChangeMonitor: MediaBrowser.Controller.Providers
// Namespace for Episode: MediaBrowser.Controller.Entities.TV
// Namespace for MetadataResult<T>: MediaBrowser.Controller.Providers
// Namespace for ItemInfo: MediaBrowser.Controller.Providers
// Namespace for IDirectoryService: MediaBrowser.Controller.Providers

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Jellyfin.Plugin.Youtarr.Configuration;
using Jellyfin.Plugin.Youtarr.Models;
using Jellyfin.Plugin.Youtarr.Parsers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Youtarr.Providers;

public class YoutarrEpisodeNfoProvider : ILocalMetadataProvider<Episode>, IHasItemChangeMonitor
{
    private readonly ILogger<YoutarrEpisodeNfoProvider> _logger;

    public YoutarrEpisodeNfoProvider(ILogger<YoutarrEpisodeNfoProvider> logger)
    {
        _logger = logger;
    }

    // MUST match Constants.ProviderName — same as YoutarrSeriesNfoProvider
    public string Name => Constants.ProviderName;

    public Task<MetadataResult<Episode>> GetMetadata(
        ItemInfo info,
        IDirectoryService directoryService,
        CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Episode>();

        // Sidecar discovery: video.mp4 → video.nfo (same directory, same basename)
        var nfoPath = Path.ChangeExtension(info.Path, ".nfo");
        if (!File.Exists(nfoPath))
        {
            _logger.LogDebug("[Youtarr] No NFO sidecar for {VideoPath}", info.Path);
            return Task.FromResult(result); // HasMetadata stays false
        }

        YoutarrVideoData? data;
        try
        {
            data = YoutarrNfoParser.Parse(nfoPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Youtarr] Failed to parse NFO: {NfoPath}", nfoPath);
            return Task.FromResult(result);
        }

        if (data is null)
        {
            return Task.FromResult(result);
        }

        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var episode = MapToEpisode(data, config);
        result.Item = episode;
        result.HasMetadata = true; // CRITICAL — without this Jellyfin discards the result
        return Task.FromResult(result);
    }

    public bool HasChanged(BaseItem item, IDirectoryService directoryService)
    {
        // Return true if the NFO file has been modified since last scan
        // to trigger re-reads when Youtarr updates metadata.
        var nfoPath = Path.ChangeExtension(item.Path, ".nfo");
        if (!File.Exists(nfoPath))
        {
            return false;
        }
        var nfoLastWrite = File.GetLastWriteTimeUtc(nfoPath);
        return nfoLastWrite > item.DateLastSaved;
    }

    private static Episode MapToEpisode(YoutarrVideoData data, PluginConfiguration config)
    {
        var episode = new Episode();

        // EPI-01: Title
        episode.Name = data.Title;

        // EPI-01: Plot (with EPI-04 truncation)
        episode.Overview = FormatDescription(data.Plot, config.MaxDescriptionLength);

        // EPI-01: PremiereDate + season assignment
        if (data.PremiereDate.HasValue)
        {
            episode.PremiereDate = data.PremiereDate.Value;
            episode.ProductionYear = data.PremiereDate.Value.Year;

            // LIB-03 / LIB-04: year-seasons toggle
            episode.ParentIndexNumber = config.YearSeasons
                ? data.PremiereDate.Value.Year
                : 1;

            // EPI-05: episode numbering scheme
            episode.IndexNumber = config.EpisodeNumberingScheme == EpisodeNumberingScheme.YYYYMMDD
                ? (data.PremiereDate.Value.Year * 10000)
                    + (data.PremiereDate.Value.Month * 100)
                    + data.PremiereDate.Value.Day
                : (int?)null; // Default: Jellyfin auto-sequences
        }
        else
        {
            // EPI-07: missing/invalid date — Season 0 (Specials), no episode number
            episode.ParentIndexNumber = 0;
            episode.IndexNumber = null;
        }

        // EPI-01: Runtime — prefer durationinseconds (more precise), fall back to runtime (minutes)
        if (data.DurationInSeconds.HasValue)
        {
            episode.RunTimeTicks = TimeSpan.FromSeconds(data.DurationInSeconds.Value).Ticks;
        }
        else if (data.RuntimeMinutes.HasValue)
        {
            episode.RunTimeTicks = TimeSpan.FromMinutes(data.RuntimeMinutes.Value).Ticks;
        }

        // EPI-02: Genres and Tags
        if (data.Genres.Count > 0)
        {
            episode.Genres = data.Genres.ToArray();
        }
        if (data.Tags.Count > 0)
        {
            episode.Tags = data.Tags.ToArray();
        }

        // EPI-03: YouTube provider ID
        if (!string.IsNullOrWhiteSpace(data.YouTubeId))
        {
            episode.ProviderIds[Constants.YouTubeProviderId] = data.YouTubeId;
        }

        // EPI-06: Content rating from <mpaa>
        if (!string.IsNullOrWhiteSpace(data.MpaaRating))
        {
            episode.OfficialRating = data.MpaaRating;
        }

        // Studio (channel name) — from <studio> in NFO
        if (!string.IsNullOrWhiteSpace(data.Studio))
        {
            episode.Studios = new[] { data.Studio };
        }

        return episode;
    }

    // EPI-04: truncate and normalize description (port of tubearchivist-jf FormatDescription)
    private static string? FormatDescription(string? description, int maxLength)
    {
        if (string.IsNullOrEmpty(description))
        {
            return description;
        }
        if (description.Length > maxLength)
        {
            description = description[..maxLength];
        }
        // Convert literal newlines to HTML line breaks for Jellyfin display
        return description.Replace("\n", "<br>", StringComparison.Ordinal);
    }
}
```

### NFO Parser and DTO

The parser is extracted as a separate class so it is independently testable (same pattern as `PathUtils`).

```csharp
// Models/YoutarrVideoData.cs
namespace Jellyfin.Plugin.Youtarr.Models;

public class YoutarrVideoData
{
    public string? Title { get; init; }
    public string? Plot { get; init; }
    public DateTime? PremiereDate { get; init; }
    public DateTime? DateAdded { get; init; }       // fallback when PremiereDate missing
    public int? RuntimeMinutes { get; init; }        // from <runtime> (minutes, ceiled)
    public int? DurationInSeconds { get; init; }     // from <fileinfo><streamdetails><video><durationinseconds>
    public string? Studio { get; init; }             // from <studio>
    public string? YouTubeId { get; init; }          // from <uniqueid type="youtube"> or <youtubeid>
    public string? MpaaRating { get; init; }         // from <mpaa>
    public List<string> Genres { get; init; } = new();
    public List<string> Tags { get; init; } = new();
}
```

```csharp
// Parsers/YoutarrNfoParser.cs
// Source: XDocument pattern from PathUtils.ReadStudioFromMovieNfo (Phase 1)
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;
using Jellyfin.Plugin.Youtarr.Models;

namespace Jellyfin.Plugin.Youtarr.Parsers;

public static class YoutarrNfoParser
{
    /// <summary>
    /// Parses a Youtarr <movie>-rooted NFO file. Returns null if the file does not
    /// exist or the root element is not <movie>. Throws on malformed UTF-8 or XML
    /// so callers can log and skip the video without crashing the scan.
    /// </summary>
    public static YoutarrVideoData? Parse(string nfoPath)
    {
        using var reader = new StreamReader(nfoPath, Encoding.UTF8);
        var doc = XDocument.Load(reader);

        // Verify this is a Youtarr <movie> NFO, not an <episodedetails> or <tvshow>
        if (doc.Root?.Name.LocalName != "movie")
        {
            return null;
        }

        var root = doc.Root;

        // <premiered>YYYY-MM-DD</premiered>
        DateTime? premiereDate = null;
        var premieredStr = root.Element("premiered")?.Value?.Trim();
        if (!string.IsNullOrEmpty(premieredStr)
            && DateTime.TryParse(premieredStr, out var pd)
            && IsValidYouTubeDate(pd))
        {
            premiereDate = pd;
        }

        // <dateadded> — fallback date source (download timestamp, not upload date)
        DateTime? dateAdded = null;
        var dateAddedStr = root.Element("dateadded")?.Value?.Trim();
        if (!string.IsNullOrEmpty(dateAddedStr)
            && DateTime.TryParse(dateAddedStr, out var da))
        {
            dateAdded = da;
        }

        // <uniqueid type="youtube"> takes priority over <youtubeid>
        string? youtubeId = null;
        foreach (var uid in root.Elements("uniqueid"))
        {
            if (string.Equals(uid.Attribute("type")?.Value, "youtube",
                    StringComparison.OrdinalIgnoreCase))
            {
                youtubeId = uid.Value?.Trim();
                break;
            }
        }
        if (string.IsNullOrWhiteSpace(youtubeId))
        {
            youtubeId = root.Element("youtubeid")?.Value?.Trim();
        }

        // <runtime> is in minutes (Youtarr: Math.ceil(duration / 60))
        int? runtimeMinutes = null;
        if (int.TryParse(root.Element("runtime")?.Value?.Trim(), out var rm))
        {
            runtimeMinutes = rm;
        }

        // <fileinfo><streamdetails><video><durationinseconds>
        int? durationInSeconds = null;
        var durStr = root
            .Element("fileinfo")
            ?.Element("streamdetails")
            ?.Element("video")
            ?.Element("durationinseconds")
            ?.Value?.Trim();
        if (!string.IsNullOrEmpty(durStr) && int.TryParse(durStr, out var dur))
        {
            durationInSeconds = dur;
        }

        // Multiple <genre> elements
        var genres = new List<string>();
        foreach (var g in root.Elements("genre"))
        {
            var v = g.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(v))
            {
                genres.Add(v);
            }
        }

        // Multiple <tag> elements
        var tags = new List<string>();
        foreach (var t in root.Elements("tag"))
        {
            var v = t.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(v))
            {
                tags.Add(v);
            }
        }

        return new YoutarrVideoData
        {
            Title         = root.Element("title")?.Value?.Trim(),
            Plot          = root.Element("plot")?.Value?.Trim(),
            PremiereDate  = premiereDate,
            DateAdded     = dateAdded,
            RuntimeMinutes    = runtimeMinutes,
            DurationInSeconds = durationInSeconds,
            Studio        = root.Element("studio")?.Value?.Trim(),
            YouTubeId     = youtubeId,
            MpaaRating    = root.Element("mpaa")?.Value?.Trim(),
            Genres        = genres,
            Tags          = tags,
        };
    }

    // YouTube launched 2005-04-23; dates before 2005 or after current year+2 are bogus
    private static bool IsValidYouTubeDate(DateTime dt)
    {
        return dt.Year >= 2005 && dt.Year <= DateTime.UtcNow.Year + 2;
    }
}
```

### PluginConfiguration Additions

```csharp
// Configuration/PluginConfiguration.cs — additions for Phase 2
// (YearSeasons already present from Phase 1 stub)

public enum EpisodeNumberingScheme
{
    /// <summary>Let Jellyfin auto-sequence episode numbers. IndexNumber = null.</summary>
    Default = 0,

    /// <summary>
    /// Use upload date as episode number: (year * 10000) + (month * 100) + day.
    /// E.g. 2024-03-15 → IndexNumber = 20240315.
    /// Note: same-day uploads will share an IndexNumber (see pitfall below).
    /// </summary>
    YYYYMMDD = 1,
}

// In PluginConfiguration class, add:
/// <summary>
/// Gets or sets the episode numbering scheme.
/// Default: auto-sequenced by Jellyfin.
/// YYYYMMDD: upload date as integer episode number (e.g. 20240315).
/// </summary>
public EpisodeNumberingScheme EpisodeNumberingScheme { get; set; } = EpisodeNumberingScheme.Default;

/// <summary>
/// Gets or sets the maximum description length in characters.
/// YouTube descriptions are often 500–5000 chars; raw text looks bad in the Jellyfin UI.
/// Default: 500 (matching tubearchivist-jf-plugin default).
/// </summary>
public int MaxDescriptionLength { get; set; } = 500;
```

---

## The Critical Unknown: Virtual Season Creation

### What `SeriesMetadataService.CreateSeasonsAsync` Actually Does

From direct source inspection [CITED: github.com/jellyfin/jellyfin SeriesMetadataService.cs]:

1. After all Episode metadata is refreshed for a Series, `CreateSeasonsAsync` is called.
2. It queries all `Episode` children and collects distinct `ParentIndexNumber` values.
3. For each distinct `ParentIndexNumber` value, it checks whether a physical Season folder or a Season DB entry with that `IndexNumber` already exists.
4. If not found: creates a virtual `Season { IndexNumber = value, Name = "Season {value}" }`.
5. Virtual seasons are created whenever `episode.ParentIndexNumber.HasValue` is true.

The key point: this mechanism does NOT require physical `Season YYYY` folders. It works purely from the `ParentIndexNumber` values on Episode items.

### Why PITFALLS Issues #11916 and #13197 Do Not Apply Here

- **#11916 (Season folder naming regression, 10.9.4):** This affects `SeasonResolver` classifying *physical folders* on disk. Youtarr's flat layout has no physical season folders, so `SeasonResolver` never runs for year-seasons. This issue is irrelevant.
- **#13197 (NFO season tag ignored, 10.10.3+):** This affects reading a `<season>` element from an *episode* NFO file. The plugin does not write or rely on `<season>` in the NFO — it sets `ParentIndexNumber` programmatically in the provider. This issue is irrelevant.

The remaining concern is **issue #14080** ("Plugin cannot change Episode ParentIndexNumber"), which documents a specific scenario where the filename parser's season/episode detection runs *after* the metadata provider and overwrites provider-set values. For Youtarr's filenames (no `SxxExx` pattern, no `1x01` pattern), the filename parser produces no season/episode values, so there is nothing to overwrite. The expected behavior is:

```
EpisodeResolver classifies .mp4 as Episode
  → YoutarrEpisodeNfoProvider.GetMetadata() called
      → sets ParentIndexNumber = 2024, IndexNumber = 20240315
  → filename parser runs: "Video Title [ytid].mp4" → extracts NO season/episode
      → ParentIndexNumber unchanged: 2024
  → SeriesMetadataService.CreateSeasonsAsync() creates virtual Season 2024
```

However, this is the key assumption that must be confirmed on a live 10.10.7 instance before the full pipeline is built. The Wave 0 live probe validates exactly this.

### Wave 0: Minimal Live Probe (MUST RUN FIRST)

Before building the full provider, implement a minimal stub that returns a hardcoded `ParentIndexNumber` and verify virtual season creation on the real Docker server.

**Probe procedure:**

1. Add a one-file stub provider to the existing plugin (does not need to be the final implementation):

```csharp
// Stub EpisodeNfoProvider that returns ParentIndexNumber=2024 for ALL episodes
// without reading any NFO file. Used only to validate virtual season creation.
public class YoutarrEpisodeNfoProbeStub : ILocalMetadataProvider<Episode>
{
    public string Name => Constants.ProviderName;

    public Task<MetadataResult<Episode>> GetMetadata(
        ItemInfo info, IDirectoryService _, CancellationToken ct)
    {
        var result = new MetadataResult<Episode>
        {
            Item = new Episode
            {
                Name = Path.GetFileNameWithoutExtension(info.Path),
                ParentIndexNumber = 2024,   // PROBE: hardcoded year
                IndexNumber = 1,             // PROBE: hardcoded episode number
            },
            HasMetadata = true,
        };
        return Task.FromResult(result);
    }
}
```

2. Add a second fixture file to the load-test harness that maps to year 2023:

```xml
<!-- test/jellyfin-load-test/media/ChannelA/video_2023.nfo -->
<?xml version="1.0" encoding="utf-8"?>
<movie>
  <title>2023 Test Video</title>
  <studio>Channel A</studio>
  <premiered>2023-09-10</premiered>
</movie>
```
```
touch test/jellyfin-load-test/media/ChannelA/video_2023.mp4
touch test/jellyfin-load-test/media/ChannelA/video_2024.mp4
# (stub provider hardcodes 2024 for all; probe is about virtual season creation mechanism)
```

3. Deploy, restart, scan, verify:
   - Expected: Library shows `ChannelA → Season 2024 → "video_2023", "video_2024"`
   - Success: virtual season container appeared with no physical `Season 2024/` folder
   - Failure: episodes appear in "Season 1" or "Unknown Season" → `ParentIndexNumber` is being discarded

4. Once the probe passes, replace the stub with the full `YoutarrEpisodeNfoProvider` implementation.

---

## Field Mapping Table

Complete field-by-field map from Youtarr NFO to Jellyfin Episode property.

| NFO Element | `YoutarrVideoData` Field | `Episode` Property | Notes |
|-------------|--------------------------|---------------------|-------|
| `<title>` | `Title` | `Name` | Direct; strip leading/trailing whitespace |
| `<plot>` | `Plot` | `Overview` | Truncated to `MaxDescriptionLength` chars; `\n` → `<br>` |
| `<premiered>` | `PremiereDate` | `PremiereDate`, `ProductionYear`, `ParentIndexNumber` | Validate year >= 2005; drive year-season assignment |
| `<dateadded>` | `DateAdded` | (used only in EPI-07 fallback chain, not mapped to Episode) | Download timestamp, not upload date |
| `<runtime>` | `RuntimeMinutes` | `RunTimeTicks` | Minutes × 60 × 10,000,000 ticks/sec; fallback if no `<durationinseconds>` |
| `<fileinfo><streamdetails><video><durationinseconds>` | `DurationInSeconds` | `RunTimeTicks` | Seconds × 10,000,000; more precise — preferred over `<runtime>` |
| `<studio>` | `Studio` | `Studios[]` | Channel name |
| `<uniqueid type="youtube">` | `YouTubeId` | `ProviderIds["YouTube"]` | Priority over `<youtubeid>`; strip whitespace |
| `<youtubeid>` | `YouTubeId` (fallback) | `ProviderIds["YouTube"]` | Only used if `<uniqueid type="youtube">` absent |
| `<genre>` (multiple) | `Genres` | `Genres[]` | All elements collected into list |
| `<tag>` (multiple) | `Tags` | `Tags[]` | All elements collected into list |
| `<mpaa>` | `MpaaRating` | `OfficialRating` | Pass through as-is; Youtarr outputs G/PG/PG-13/R/NC-17/TV-Y/TV-PG/TV-14/TV-MA |
| `<credits>` | (not captured) | — | Redundant with `<studio>`; not separately useful |
| `<trailer>` | (not captured) | — | Kodi plugin URL; not useful in Jellyfin Episode |
| `<ratings><rating name="mpaa">` | (not captured) | — | Numeric rating scale; `<mpaa>` text value is sufficient |

### RunTimeTicks Conversion

```csharp
// TimeSpan.TicksPerSecond = 10_000_000 (100-nanosecond intervals)
// Jellyfin expects RunTimeTicks in this unit.

// Preferred (more precise):
episode.RunTimeTicks = TimeSpan.FromSeconds(data.DurationInSeconds.Value).Ticks;

// Fallback (Youtarr writes Math.ceil(duration/60) minutes):
episode.RunTimeTicks = TimeSpan.FromMinutes(data.RuntimeMinutes.Value).Ticks;
```

---

## EPI-07: Date Fallback Chain

Youtarr's `<premiered>` field can be empty (video had no extractable upload date), `0001-01-01` (DateTime.MinValue from yt-dlp failure), or `1970-01-01` (Unix epoch fallback). The provider must handle all cases without crashing.

```
1. Parse <premiered> from NFO
   └─ valid YouTube date (year 2005–present)? → use it, set ParentIndexNumber = year
   └─ invalid or missing?
      ↓
2. Log warning: "[Youtarr] Missing/invalid <premiered> in {nfoPath}; checking <dateadded>"
   Parse <dateadded> from NFO
   └─ valid date? → use it for PremiereDate ONLY (this is download date, not upload date)
      → still assign ParentIndexNumber = 0 (Season 0 / Specials), IndexNumber = null
   └─ invalid or missing?
      ↓
3. Log warning: "[Youtarr] No usable date for {videoPath}; placing in Season 0"
   episode.ParentIndexNumber = 0   (Season 0 = Specials in Jellyfin convention)
   episode.IndexNumber = null
   episode.PremiereDate = null
```

**Why Season 0?** Jellyfin uses Season 0 ("Specials") as the conventional holding area for episodes without a valid season assignment. `ParentIndexNumber = 0` is the correct signal.

**Why not Season 1 fallback?** Season 1 would mix "undated" videos with "year-2001 videos" (hypothetically) in a confusing way. Season 0 is visually distinct and signals "something is wrong here."

**Validation for `IsValidYouTubeDate`:** Reject `year < 2005` (YouTube launched April 2005), reject `year > DateTime.UtcNow.Year + 2` (future date likely a parsing error), reject `DateTime.MinValue` (0001-01-01) and `UnixEpoch` (1970-01-01).

---

## EPI-05: Episode Numbering

### YYYYMMDD Scheme

```csharp
// EpisodeNumberingScheme.YYYYMMDD
episode.IndexNumber = (uploadDate.Year * 10000)
                    + (uploadDate.Month * 100)
                    + uploadDate.Day;
// Example: 2024-03-15 → 20240315
```

**Same-day collision problem:** Two videos uploaded on the same day (e.g., `20240315`) will have identical `IndexNumber` values. Jellyfin does NOT deduplicate by `IndexNumber` — both will appear in the library, but their sort order within the day is arbitrary.

**Decision (EPI-05):** Accept same-day collisions in YYYYMMDD mode for Phase 2. The `Default` scheme (null IndexNumber) avoids this entirely. A `YYYYMMDDNN` compound scheme (appending a within-day sequence counter) requires the provider to scan all videos in the channel at metadata time — this is a significant complexity increase and is deferred to a later phase if users report it as a real problem.

**Documentation note:** Record in PLAN.md that YYYYMMDD has same-day collision behavior; users who have many same-day uploads should use Default scheme.

### Default Scheme

```csharp
// EpisodeNumberingScheme.Default
episode.IndexNumber = null; // Jellyfin assigns sequential numbers automatically
```

When `IndexNumber = null`, Jellyfin assigns episode numbers in the order items are added to the library. This is stable once assigned but does not encode the upload date. Sorting in the UI will be by title unless the user sets sort by PremiereDate.

---

## CMP-01 vs CMP-02: Flat vs Nested Layout NFO Discovery

Youtarr writes the NFO sidecar next to the video file in both layouts:

```
Flat:   /Channel/Video Title [YTID].mp4
        /Channel/Video Title [YTID].nfo     ← same dir, same basename
        
Nested: /Channel/Video Title [YTID]/Video Title [YTID].mp4
        /Channel/Video Title [YTID]/Video Title [YTID].nfo  ← same dir, same basename
```

In both cases: `Path.ChangeExtension(videoPath, ".nfo")` correctly resolves the NFO path. No path traversal or directory scanning needed.

```csharp
// Works for both flat and nested layouts:
var nfoPath = Path.ChangeExtension(info.Path, ".nfo");
// info.Path = "/Channel/Title [id].mp4"  → nfoPath = "/Channel/Title [id].nfo"
// info.Path = "/Channel/Title [id]/Title [id].mp4" → nfoPath = "/Channel/Title [id]/Title [id].nfo"
```

`PathUtils.FindNfoForVideo(string videoPath)` — the new extension to `PathUtils`:

```csharp
/// <summary>
/// Returns the NFO sidecar path for a video file: same directory, same basename, .nfo extension.
/// Returns null if the resolved path does not exist (no NFO present for this video).
/// Pure function — no filesystem side effects beyond existence check.
/// </summary>
public static string? FindNfoForVideo(string? videoPath)
{
    if (string.IsNullOrWhiteSpace(videoPath))
    {
        return null;
    }
    var nfoPath = Path.ChangeExtension(videoPath, ".nfo");
    return File.Exists(nfoPath) ? nfoPath : null;
}
```

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Season entity creation | `LibraryManager.CreateItem()` for Season items | `ParentIndexNumber` on Episode → `SeriesMetadataService.CreateSeasonsAsync()` | Manual creation produces duplicates and bypasses the standard lifecycle |
| NFO root element detection | Regex or string search | `doc.Root?.Name.LocalName != "movie"` check in XDocument | XDocument parses the namespace-qualified root name correctly and handles XML declarations |
| Episode numbering from date | Custom sort/index routine | `(year*10000) + (month*100) + day` integer | This is the exact formula from tubearchivist-jf-plugin; no reinvention needed |
| Description truncation | `Substring` with length check | `description[..maxLength]` (range operator) + `Replace("\n", "<br>")` | Port directly from tubearchivist-jf-plugin's `FormatDescription()` |
| RunTimeTicks conversion | Manual ticks arithmetic | `TimeSpan.FromSeconds(n).Ticks` | BCL handles the 100ns-tick precision; no magic numbers |
| Sidecar file discovery | Directory scan + matching | `Path.ChangeExtension(videoPath, ".nfo")` | Youtarr always co-locates NFO with video using same basename |
| XML encoding detection | BOM-detection code | `new StreamReader(path, Encoding.UTF8)` | Always specify UTF-8 explicitly; XDocument.Load with a TextReader uses the reader's encoding |

---

## Common Pitfalls

### Pitfall 1: HasMetadata = false Causes Jellyfin to Silently Discard All Episode Metadata

**What goes wrong:** `GetMetadata` returns a populated `MetadataResult<Episode>` but `HasMetadata` is not set to `true`. Jellyfin treats this as "no metadata found" and falls through to other providers or leaves the Episode with a blank title.

**Why it happens:** Same pattern as `YoutarrSeriesNfoProvider` in Phase 1 — `HasMetadata = false` is the default and is what no-op providers return.

**How to avoid:** Set `result.HasMetadata = true` before returning from every code path that successfully populates the Episode.

**Warning signs:** Episodes appear with raw filenames as titles; no metadata in the episode detail view; per-video descriptions blank.

---

### Pitfall 2: `ParentIndexNumber` Discarded by Filename Parser (Issue #14080)

**What goes wrong:** The provider sets `ParentIndexNumber = 2024` but the Episode appears in Season 1 or "Unknown Season" after a scan. The filename parser ran after the provider and overwrote `ParentIndexNumber` with a value derived from the filename (`SxxExx` patterns).

**Why it happens:** Jellyfin's `EpisodeResolver` runs filename-pattern extraction (regex for `S01E01`, `1x01`, etc.) as part of metadata merging. If the pattern matches something in the filename, that takes priority.

**Why it probably does NOT apply here:** Youtarr filenames use the pattern `Video Title [YTID].mp4`. The `[YTID]` suffix (square-bracketed YouTube ID like `[dQw4w9WgXcQ]`) does not match any Jellyfin season/episode regex. The filename parser extracts nothing, so the provider's `ParentIndexNumber` wins.

**How to validate:** The Wave 0 probe (a single hardcoded stub provider) confirms this assumption before the full provider is built.

**Warning signs during probe:** All episodes in "Season 1" or "Unknown Season" despite provider setting `ParentIndexNumber = 2024`; `IndexNumber = 0` despite provider setting a value.

---

### Pitfall 3: XDocument.Load Without Explicit UTF-8 Misparses Emoji and Special Characters

**What goes wrong:** `XDocument.Load(nfoPath)` (the file-path overload) uses the XML declaration's encoding, which may differ from the file's actual bytes if Youtarr wrote BOM-less UTF-8. Emoji in `<plot>` appear as `?` or throw an exception.

**How to avoid:** Always use the `StreamReader` overload with explicit `Encoding.UTF8`, matching the pattern already established in `PathUtils.ReadStudioFromMovieNfo`:

```csharp
using var reader = new StreamReader(nfoPath, Encoding.UTF8);
var doc = XDocument.Load(reader);
```

**Warning signs:** Plot descriptions contain `?` glyphs where emoji were; `XmlException` thrown on NFO files with non-ASCII characters.

---

### Pitfall 4: Integer Overflow When Computing RunTimeTicks

**What goes wrong:** `durationInSeconds * 10_000_000` overflows a 32-bit integer for long videos (> 214 seconds overflow int; > 3.5 hours overflows long if cast wrong). The `RunTimeTicks` property is `long?`, but the multiplication must be done in `long` arithmetic.

**How to avoid:** Use `TimeSpan.FromSeconds(data.DurationInSeconds.Value).Ticks` — `Ticks` returns `long` and `TimeSpan` handles the arithmetic correctly. This is cleaner than manual multiplication.

---

### Pitfall 5: Jellyfin Overwrites NFO Files (Issue #12197)

**What goes wrong:** After a scan, Jellyfin's metadata saver writes baseline NFO files to the media folders, overwriting Youtarr's on-disk NFOs with sparse versions that contain only filename-derived titles and blank plots.

**How to avoid:** The media is mounted read-only in the load-test harness (`./media:/media:ro`), which blocks writes physically. For production deployments: document clearly that users must disable "Save metadata to media folders" in library settings.

**The plugin itself is read-only** — it never writes to the user's files. This pitfall is a user-configuration issue, not a plugin bug.

---

### Pitfall 6: `<premiered>` Date Is the Upload Date — `<dateadded>` Is the Download Date

**What goes wrong:** Using `<dateadded>` instead of `<premiered>` for `PremiereDate` and `ParentIndexNumber` year. `<dateadded>` is the download timestamp (when Youtarr fetched the video), not when YouTube published it. A video uploaded in 2015 but downloaded in 2025 would appear in "Season 2025."

**How to avoid:** `PremiereDate` and year-season assignment MUST use `<premiered>`. `<dateadded>` is only used in the EPI-07 fallback chain (and only for `PremiereDate`, not for `ParentIndexNumber`).

---

### Pitfall 7: Provider Registration — Auto-Discovery vs Explicit

**What goes wrong:** `YoutarrEpisodeNfoProvider` is not registered in Jellyfin's DI container. Jellyfin's `ProviderManager` never discovers or calls it. Episode metadata stays blank.

**Why it may not happen:** Jellyfin uses reflection to discover all `ILocalMetadataProvider<T>` implementations in loaded plugin assemblies. Phase 1 confirmed that `YoutarrSeriesNfoProvider` was auto-discovered without explicit registration in `PluginServiceRegistrator`. The same auto-discovery should apply to `YoutarrEpisodeNfoProvider`.

**Safety net:** If auto-discovery does not work (observable as zero `[Youtarr]` Episode log lines after a scan), add to `PluginServiceRegistrator.RegisterServices`:

```csharp
serviceCollection.AddSingleton<ILocalMetadataProvider<Episode>, YoutarrEpisodeNfoProvider>();
```

---

## Live Validation Procedure (Phase 2 Wave 0 + Wave 1)

### Wave 0: Virtual Season Probe

This validates the single most critical assumption before building the full pipeline.

**Fixtures to add before Wave 0:**

```
test/jellyfin-load-test/media/ChannelA/
  ├── Video 2023 [ytid1].mp4     (empty file)
  ├── Video 2023 [ytid1].nfo
  ├── Video 2024 [ytid2].mp4     (empty file)
  └── Video 2024 [ytid2].nfo

test/jellyfin-load-test/media/ChannelB/
  ├── Missing Date [ytid3].mp4   (empty file)
  └── Missing Date [ytid3].nfo   (<premiered></premiered> — empty)
```

NFO content for `Video 2023 [ytid1].nfo`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<movie>
  <title>2023 Video</title>
  <studio>Channel A</studio>
  <premiered>2023-06-15</premiered>
  <plot>A 2023 video for year-season testing.</plot>
  <uniqueid type="youtube">ytid1</uniqueid>
  <runtime>5</runtime>
</movie>
```

NFO content for `Video 2024 [ytid2].nfo`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<movie>
  <title>2024 Video</title>
  <studio>Channel A</studio>
  <premiered>2024-03-20</premiered>
  <plot>A 2024 video for year-season testing.</plot>
  <uniqueid type="youtube">ytid2</uniqueid>
  <runtime>7</runtime>
</movie>
```

NFO content for `Missing Date [ytid3].nfo`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<movie>
  <title>Missing Date Video</title>
  <studio>Channel B</studio>
  <premiered></premiered>
  <plot>This video has no upload date.</plot>
  <uniqueid type="youtube">ytid3</uniqueid>
</movie>
```

**Verification commands (copy-paste for operator):**

```bash
API_KEY="<paste-admin-api-key>"

# Trigger full library scan
curl -X POST "http://localhost:8096/Library/Refresh" \
  -H "Authorization: MediaBrowser Token=\"${API_KEY}\""

# Wait ~30s, then list all Series:
curl -s "http://localhost:8096/Shows" \
  -H "Authorization: MediaBrowser Token=\"${API_KEY}\"" \
  | python3 -m json.tool | grep -A5 '"Name"'

# List seasons for ChannelA (replace SERIES_ID with actual ID from above):
SERIES_ID="<paste-ChannelA-series-id>"
curl -s "http://localhost:8096/Shows/${SERIES_ID}/Seasons" \
  -H "Authorization: MediaBrowser Token=\"${API_KEY}\"" \
  | python3 -m json.tool | grep -E '"Name"|"IndexNumber"'

# Check Youtarr-specific log lines:
docker exec jellyfin-plugin-test grep -r "\[Youtarr\]" /config/log/ 2>/dev/null
```

**Pass criteria:**

| Check | Expected | What it confirms |
|-------|----------|-----------------|
| ChannelA appears as Series | Yes | LIB-01 preserved from Phase 1 |
| ChannelA has Season 2023 (virtual, no folder) | Yes | LIB-03: `ParentIndexNumber` virtual season creation works |
| ChannelA has Season 2024 (virtual, no folder) | Yes | LIB-03: second year |
| Video 2023 is under Season 2023 | Yes | LIB-02: episode under correct season |
| Video 2024 is under Season 2024 | Yes | LIB-02 + LIB-03 |
| Missing Date Video is under Season 0 | Yes | EPI-07: graceful fallback |
| ChannelB does NOT crash the scan | Yes | EPI-07: no exception |
| Episode titles match NFO `<title>` | Yes | EPI-01 basic sanity |

**Failure modes and responses:**

| Observed behavior | Diagnosis | Response |
|-------------------|-----------|----------|
| Episodes in "Season 1" instead of "Season 2023/2024" | `ParentIndexNumber` being discarded (issue #14080) | Investigate Jellyfin scan logs for evidence of filename parser overwriting; check if `DateLastSaved` comparison in `HasChanged` causes provider to be skipped |
| Episodes in "Unknown Season" | `ParentIndexNumber = 0` being treated as no season | Adjust fallback season from 0 to 1, or check if Season 0 requires special Jellyfin config |
| Provider never called (no `[Youtarr]` lines for episodes) | DI registration failure | Add explicit `AddSingleton<ILocalMetadataProvider<Episode>, YoutarrEpisodeNfoProvider>` to `PluginServiceRegistrator` |
| Exception during scan | Parser or mapping bug | Read full exception from log; fix specific line |

---

## Unit Test Plan

All tests follow Phase 1's RED→GREEN TDD pattern. Tests must be added before implementation (`test(02-XX)` commit before `feat(02-XX)` commit).

### `YoutarrNfoParserTests.cs`

| Test | Input | Expected |
|------|-------|----------|
| `Parse_FullNfo_AllFieldsMapped` | NFO with all fields | All properties populated correctly |
| `Parse_MissingTitle_ReturnsNull` | `<movie>` with no `<title>` | `data.Title == null` (not throws) |
| `Parse_MissingPremiered_DateNull` | `<premiered></premiered>` | `data.PremiereDate == null` |
| `Parse_InvalidPremiered_DateNull` | `<premiered>not-a-date</premiered>` | `data.PremiereDate == null` |
| `Parse_BeforeYouTube_DateNull` | `<premiered>2003-01-01</premiered>` | `data.PremiereDate == null` (year < 2005) |
| `Parse_MinValueDate_DateNull` | `<premiered>0001-01-01</premiered>` | `data.PremiereDate == null` |
| `Parse_UniqueIdYouTubeType_ExtractsId` | `<uniqueid type="youtube">abc123</uniqueid>` | `data.YouTubeId == "abc123"` |
| `Parse_YouTubeIdFallback_UsedWhenNoUniqueId` | `<youtubeid>abc123</youtubeid>` | `data.YouTubeId == "abc123"` |
| `Parse_DurationInSeconds_PreferredOverRuntime` | Both `<durationinseconds>` and `<runtime>` present | `DurationInSeconds` populated, `RuntimeMinutes` populated |
| `Parse_MultipleGenres_AllCaptured` | Three `<genre>` elements | `data.Genres.Count == 3` |
| `Parse_MultipleTags_AllCaptured` | Three `<tag>` elements | `data.Tags.Count == 3` |
| `Parse_EmojiInPlot_DeserializesCorrectly` | `<plot>Hello 🎬</plot>` | `data.Plot == "Hello 🎬"` |
| `Parse_XmlEntitiesInPlot_DecodedCorrectly` | `<plot>&amp; &lt;</plot>` | `data.Plot == "& <"` |
| `Parse_NotMovieRoot_ReturnsNull` | `<episodedetails>...</episodedetails>` | Returns `null` |
| `Parse_MalformedXml_Throws` | `<movie><title>unclosed` | Throws `XmlException` (caller catches) |

### `YoutarrEpisodeNfoProviderTests.cs`

| Test | Scenario | Expected |
|------|----------|----------|
| `GetMetadata_NoNfoFile_HasMetadataFalse` | Video file with no `.nfo` sidecar | `result.HasMetadata == false` |
| `GetMetadata_ValidNfo_HasMetadataTrue` | Valid NFO present | `result.HasMetadata == true` |
| `GetMetadata_Title_MappedToName` | NFO with `<title>` | `episode.Name == nfoTitle` |
| `GetMetadata_Plot_TruncatedToMaxLength` | Plot longer than MaxDescriptionLength | `episode.Overview.Length == config.MaxDescriptionLength` |
| `GetMetadata_Plot_NewlinesConverted` | Plot with `\n` | `episode.Overview` contains `<br>` |
| `GetMetadata_ValidYear_ParentIndexNumberIsYear` | `<premiered>2024-03-15</premiered>` | `episode.ParentIndexNumber == 2024` |
| `GetMetadata_YearSeasonsOff_ParentIndexNumber1` | Valid date + `config.YearSeasons = false` | `episode.ParentIndexNumber == 1` |
| `GetMetadata_MissingDate_ParentIndexNumber0` | Empty `<premiered>` | `episode.ParentIndexNumber == 0` |
| `GetMetadata_MissingDate_IndexNumberNull` | Empty `<premiered>` | `episode.IndexNumber == null` |
| `GetMetadata_YYYYMMDD_CorrectIndexNumber` | `2024-03-15` + YYYYMMDD scheme | `episode.IndexNumber == 20240315` |
| `GetMetadata_Default_IndexNumberNull` | Valid date + Default scheme | `episode.IndexNumber == null` |
| `GetMetadata_DurationSeconds_RunTimeTicks` | `<durationinseconds>300</durationinseconds>` | `episode.RunTimeTicks == 3_000_000_000` |
| `GetMetadata_RuntimeMinutes_RunTimeTicks` | `<runtime>5</runtime>` (no durationinseconds) | `episode.RunTimeTicks == 3_000_000_000` |
| `GetMetadata_YouTubeId_InProviderIds` | `<uniqueid type="youtube">abc</uniqueid>` | `episode.ProviderIds["YouTube"] == "abc"` |
| `GetMetadata_Mpaa_OfficialRating` | `<mpaa>PG-13</mpaa>` | `episode.OfficialRating == "PG-13"` |
| `GetMetadata_NoMpaa_OfficialRatingNull` | No `<mpaa>` element | `episode.OfficialRating == null` |

### `PathUtilsTests.cs` additions

| Test | Input | Expected |
|------|-------|----------|
| `FindNfoForVideo_NfoExists_ReturnsSiblingPath` | video.mp4 with video.nfo present | Returns path ending in `.nfo` |
| `FindNfoForVideo_NfoMissing_ReturnsNull` | video.mp4, no video.nfo | Returns `null` |
| `FindNfoForVideo_NestedLayout_ResolvesCorrectly` | `Channel/Title/Title.mp4` with `Channel/Title/Title.nfo` | Returns correct sidecar path |
| `FindNfoForVideo_NullInput_ReturnsNull` | `null` | Returns `null` |

---

## Phase 2 is NOT Responsible For (Deferred)

| Capability | Deferred To | Reason |
|------------|------------|--------|
| Episode thumbnail (`.jpg` next to video) | Phase 3 (Artwork) | `ILocalImageProvider` is a separate provider type; metadata and images are separate concerns |
| Channel `poster.jpg` as Series image | Phase 3 (Artwork) | Same — image providers |
| Season metadata enrichment (`YoutarrSeasonProvider`) | Optional Phase 6 | Virtual season names ("Season 2024") are auto-generated by Jellyfin; sufficient for MVP |
| Configuration page HTML | Phase 3 (Polish) | Config values are read by providers; the UI surface can be added later |
| Same-day collision resolution (YYYYMMDDNN) | Post-Phase 2 if needed | Added complexity; accept collision behavior, document it |
| `ILibraryPostScanTask` for re-numbering | Fallback only | Build only if virtual season probe fails |

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK 8.x | Plugin build | Yes | 8.0.422 | — |
| Docker | Load-test harness | Yes (daemon needs `sudo start`) | 29.5.2 | — |
| Jellyfin 10.10.7 Docker image | Live probe | Not pulled by agent | — | `docker pull jellyfin/jellyfin:10.10.7` (operator runs) |
| Existing 10.10.7 server state | Phase 2 validation | Must wipe config/ and cache/ between probe runs | — | `rm -rf test/jellyfin-load-test/config test/jellyfin-load-test/cache` |

**Missing dependencies with no fallback:** None — all Phase 2 tools are present.

**Docker note:** Agent cannot run `sudo`. Commands requiring sudo (daemon start) must be run by the operator per project policy.

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 (already in test project) |
| Config file | `Jellyfin.Plugin.Youtarr.Tests/Jellyfin.Plugin.Youtarr.Tests.csproj` |
| Quick run command | `dotnet test Jellyfin.Plugin.Youtarr.Tests -c Release` |
| Full suite command | `dotnet test Jellyfin.Plugin.Youtarr.Tests -c Release --no-build` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Command | File |
|--------|----------|-----------|---------|------|
| LIB-02 | Video appears as Episode | Live (Docker probe) | see Wave 0 procedure | test/jellyfin-load-test |
| LIB-03 | Episodes in year-seasons | Live (Docker probe) | see Wave 0 procedure | test/jellyfin-load-test |
| LIB-04 | YearSeasons=false → Season 1 | Unit | `dotnet test --filter GetMetadata_YearSeasonsOff` | `YoutarrEpisodeNfoProviderTests.cs` |
| EPI-01 | Title, plot, date, runtime from NFO | Unit | `dotnet test --filter GetMetadata_ValidNfo` | `YoutarrEpisodeNfoProviderTests.cs` |
| EPI-02 | Genres and tags | Unit | `dotnet test --filter GetMetadata_*Genres*` | `YoutarrNfoParserTests.cs` |
| EPI-03 | YouTube provider ID | Unit | `dotnet test --filter GetMetadata_YouTubeId` | `YoutarrEpisodeNfoProviderTests.cs` |
| EPI-04 | Description truncation | Unit | `dotnet test --filter GetMetadata_Plot_Truncated` | `YoutarrEpisodeNfoProviderTests.cs` |
| EPI-05 | Episode numbering schemes | Unit | `dotnet test --filter GetMetadata_YYYYMMDD` | `YoutarrEpisodeNfoProviderTests.cs` |
| EPI-06 | OfficialRating from mpaa | Unit | `dotnet test --filter GetMetadata_Mpaa` | `YoutarrEpisodeNfoProviderTests.cs` |
| EPI-07 | Missing date fallback | Unit + Live | `dotnet test --filter GetMetadata_Missing` | `YoutarrEpisodeNfoProviderTests.cs` |
| CMP-01 | Flat layout NFO discovery | Unit | `dotnet test --filter FindNfoForVideo` | `PathUtilsTests.cs` |
| CMP-02 | Nested layout NFO discovery | Unit | `dotnet test --filter FindNfoForVideo_Nested` | `PathUtilsTests.cs` |

### Wave 0 Gaps (new test files and fixtures needed)

- [ ] `test/jellyfin-load-test/media/ChannelA/` — 2 video fixtures in different years
- [ ] `test/jellyfin-load-test/media/ChannelB/` — 1 video fixture with missing date
- [ ] `Jellyfin.Plugin.Youtarr.Tests/Parsers/YoutarrNfoParserTests.cs` — covers all parser cases
- [ ] `Jellyfin.Plugin.Youtarr.Tests/Providers/YoutarrEpisodeNfoProviderTests.cs` — covers mapping and fallbacks
- [ ] `Jellyfin.Plugin.Youtarr/Parsers/YoutarrNfoParser.cs` — the parser under test
- [ ] `Jellyfin.Plugin.Youtarr/Models/YoutarrVideoData.cs` — the DTO
- [ ] `PluginConfiguration.cs` additions: `EpisodeNumberingScheme`, `MaxDescriptionLength`

---

## Security Domain

No network calls, no user-supplied inputs beyond file paths that Jellyfin's scanner has already validated. The attack surface is limited to file read operations on NFO files.

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | No | — |
| V3 Session Management | No | — |
| V4 Access Control | No | — |
| V5 Input Validation | Yes (NFO content) | Wrap `XDocument.Load` in try/catch; swallow parse errors to null; validate date ranges |
| V6 Cryptography | No | — |

### Relevant Threat Patterns

| Pattern | Notes | Mitigation |
|---------|-------|------------|
| Malformed XML in NFO crashing plugin | Youtarr writes NFOs but corrupt files are possible | `try/catch (Exception)` around `YoutarrNfoParser.Parse`; return `HasMetadata = false`, log warning |
| Extremely long `<plot>` (100KB+) | YouTube descriptions can be very long | `MaxDescriptionLength` truncation before storing; never stores unbounded strings in Episode |
| Path traversal in NFO content | Plot or title containing `../../../` | Plugin never uses NFO content as a file path; XDocument string values are pure data |
| Emoji/BOM in NFO crashing UTF-8 reader | Common in YouTube metadata | `StreamReader(path, Encoding.UTF8)` + `XDocument.Load(reader)` handles BOM correctly |

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `[YTID]` bracket pattern in Youtarr filenames does not match Jellyfin's season/episode filename regex | Pitfall 2, Wave 0 | If it does match, `ParentIndexNumber` is overwritten; fix: investigate `EpisodeResolver` regex patterns and rename test fixture files to confirm |
| A2 | `YoutarrEpisodeNfoProvider` is auto-discovered by Jellyfin's DI without explicit `AddSingleton` registration | Pitfall 7 | If not, episodes never get metadata; fix: add explicit registration to `PluginServiceRegistrator` |
| A3 | `SeriesMetadataService.CreateSeasonsAsync` runs automatically after episode metadata refresh in 10.10.7 and creates virtual seasons from `ParentIndexNumber` when no physical season folders exist | Virtual Season section | HIGH risk if wrong — whole year-season feature fails; fix: implement `ILibraryPostScanTask` as fallback |
| A4 | `Path.ChangeExtension(videoPath, ".nfo")` correctly resolves to the NFO sidecar in both flat and nested layouts (Youtarr always uses same-basename co-location) | CMP-01/CMP-02 section | Low risk — confirmed from FEATURES.md Youtarr source analysis; if Youtarr config changes the NFO filename, discovery fails silently |

**A3 is the only HIGH-risk assumption.** It is the explicit target of the Wave 0 live probe.

---

## Open Questions

1. **Virtual season creation in 10.10.7 (CRITICAL)**
   - What we know: `SeriesMetadataService.CreateSeasonsAsync` creates virtual seasons from `ParentIndexNumber` per source code inspection
   - What's unclear: Whether this works reliably in the specific 10.10.7 release for flat-layout libraries with no physical season folders; whether a regression exists between the researched source and the shipped binary
   - Recommendation: Wave 0 live probe. Do not proceed to full provider implementation until probe passes.

2. **Provider ordering vs. built-in EpisodeNfoProvider**
   - What we know: Jellyfin runs `ILocalMetadataProvider<Episode>` implementations; first with `HasMetadata = true` wins. Phase 1 confirmed `YoutarrSeriesNfoProvider` wins over `SeriesNfoProvider` without explicit `Order` setting.
   - What's unclear: Whether `EpisodeNfoProvider` (built-in) returns `HasMetadata = true` for Youtarr's `<movie>` NFOs (it should return false since it expects `<episodedetails>`) or whether it could shadow the plugin provider.
   - Recommendation: If the Wave 0 probe shows episode metadata not populated despite provider being called, add `public int Order => 0;` to `YoutarrEpisodeNfoProvider` to ensure it runs before the built-in provider.

3. **`MaxDescriptionLength` in the FormatDescription truncation**
   - What we know: tubearchivist-jf-plugin uses `MaxDescriptionLength` from config (default 500), applies it in `FormatDescription()`
   - What's unclear: Whether truncating at exactly 500 chars on a UTF-16 string boundary (C# `string` is UTF-16) causes visual corruption on multibyte characters. `string[..500]` in C# truncates on code units, not grapheme clusters — emoji that span multiple code units could be cut in half.
   - Recommendation: For Phase 2, accept this behavior; document it. If users report garbled descriptions, add a grapheme-cluster-aware truncation in a later phase.

---

## Sources

### Primary (HIGH confidence)
- Phase 1 codebase: `Plugin.cs`, `PluginServiceRegistrator.cs`, `PathUtils.cs`, `YoutarrSeriesNfoProvider.cs`, `Constants.cs`, `PluginConfiguration.cs` — verified live-built against Jellyfin 10.10.7
- `Jellyfin.Controller 10.10.7` NuGet: `ILocalMetadataProvider<T>`, `IHasItemChangeMonitor` signatures — verified from Phase 1 build
- Phase 1 SUMMARY.md (01-02-SUMMARY.md): DI registration pattern, IPluginServiceRegistrator, IResolverIgnoreRule namespace corrections — all confirmed live
- Phase 1 RESEARCH.md (01-RESEARCH.md): provider ordering, HasMetadata semantics, ExcludeAssets, test project pattern — confirmed live
- ARCHITECTURE.md: `SeriesMetadataService.CreateSeasonsAsync` mechanism, `ParentIndexNumber` field, `EpisodeNfoProvider` incompatibility — HIGH confidence from Jellyfin source
- FEATURES.md: Youtarr NFO field list confirmed from `nfoGenerator.js` source; `FormatDescription` pattern from tubearchivist-jf-plugin `Utils.cs`
- PITFALLS.md: pitfalls #1–#13 with issue numbers; `<movie>` NFO trap, NFO overwrite, UTF-8 encoding — HIGH confidence
- SUMMARY.md: season grouping contradiction reconciliation — HIGH confidence analysis
- `github.com/jellyfin/jellyfin MediaBrowser.Providers/TV/SeriesMetadataService.cs`: `CreateSeasonsAsync` reads `ParentIndexNumber`, creates virtual seasons — VERIFIED via WebFetch [CITED]
- `github.com/jellyfin/jellyfin MediaBrowser.Controller/Entities/BaseItem.cs`: property types (`ParentIndexNumber: int?`, `IndexNumber: int?`, `RunTimeTicks: long?`, etc.) — VERIFIED via WebFetch [CITED]
- `github.com/tubearchivist/tubearchivist-jf-plugin TubeArchivist/Video/Video.cs`: `ToEpisode()` — `ParentIndexNumber = Published.Year`, YYYYMMDD formula, `FormatDescription` call — VERIFIED via WebFetch [CITED]
- `github.com/tubearchivist/tubearchivist-jf-plugin Utils/Utils.cs`: `FormatDescription` — truncate at `MaxDescriptionLength`, `\n` → `<br>` — VERIFIED via WebFetch [CITED]
- `github.com/jellyfin/jellyfin MediaBrowser.Controller/Entities/TV/Episode.cs`: `AiredSeasonNumber` computed alias, `SeasonName` with `[JsonIgnore]` — VERIFIED via WebFetch [CITED]

### Secondary (MEDIUM confidence)
- Jellyfin GitHub issue #14080: `ParentIndexNumber` override behavior — MEDIUM (not directly reproduced on 10.10.7; addressed by Wave 0 probe)
- Jellyfin GitHub issue #13358: `SeasonName` broken in 10.10, `ParentIndexNumber` is correct — MEDIUM (issue documented, fix confirmed for 10.10.x)
- `github.com/ankenyr/jellyfin-youtube-metadata-plugin Providers/LocalMetadata/YoutubeLocalEpisodeProvider.cs`: structure confirms `ILocalMetadataProvider<Episode>` pattern for YouTube content

### Tertiary (LOW confidence)
- None in Phase 2 scope; all foundational claims are HIGH or MEDIUM with official source backing

---

## Metadata

**Confidence breakdown:**
- Episode metadata mapping (field table): HIGH — all fields verified from Youtarr NFO source + Jellyfin BaseItem.cs property types
- Virtual season creation mechanism: HIGH for mechanism; MEDIUM for 10.10.7 specific reliability (requires live validation)
- Provider registration/discovery pattern: HIGH — confirmed live in Phase 1 for Series provider; same pattern applies to Episode provider
- Pitfalls: HIGH — sourced from specific verified Jellyfin GitHub issues and Phase 1 live experience
- Episode numbering formula: HIGH — exact formula from tubearchivist-jf-plugin source, verified

**Research date:** 2026-06-09
**Valid until:** 2026-07-09 (Jellyfin 10.10.x is stable; interfaces unlikely to change in 30 days)
