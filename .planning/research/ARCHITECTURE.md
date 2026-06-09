# Architecture Research

**Domain:** Jellyfin plugin — YouTube channel downloads as Shows/Series library
**Researched:** 2026-06-09
**Confidence:** HIGH (core classification mechanism verified against Jellyfin source and two reference plugins)

---

## The Core Question Answered: How Items Get Classified

The classification mechanism is a **combination of (a) library content type and (d) metadata providers**. No custom resolver is needed. Here is exactly how it works:

### Step 1: Library content type drives folder classification

When the user creates a Jellyfin library with content type **Shows** (`CollectionType.tvshows`) and points it at the Youtarr root folder, Jellyfin's built-in `SeriesResolver` (`Emby.Server.Implementations/Library/Resolvers/TV/SeriesResolver.cs`) is triggered. Its first resolution path is:

```
if (collectionType == CollectionType.tvshows)
    → resolve this directory as a Series
```

This fires for every immediate subdirectory of the library root. Every channel folder (e.g., `LinusTechTips/`, `Veritasium/`) becomes a `Series` automatically, with no plugin intervention.

### Step 2: Episode classification follows the parent

Jellyfin's built-in `EpisodeResolver` classifies video files as `Episode` items when either the collection type is tvshows OR the file is under a Series ancestor. Because every channel folder is already a Series (Step 1), every `.mp4` / `.mkv` inside a channel folder resolves to an `Episode`.

### Step 3: NFO parsing — where the plugin's local metadata providers are essential

Jellyfin's built-in `EpisodeNfoProvider` calls `EpisodeNfoParser`, which searches for `</episodedetails>` blocks. Youtarr writes `<movie>` root NFOs. These two are **incompatible** — Jellyfin's built-in provider will not read Youtarr's episode NFOs.

The plugin must implement a custom `ILocalMetadataProvider<Episode>` that:
- Opens the sidecar `.nfo` file (same base name as the video)
- Parses the `<movie>` XML tree
- Maps fields to `Episode` properties (see data flow below)
- Sets `ParentIndexNumber = uploadYear` (the year from `<premiered>`)
- Sets `HasMetadata = true` to win over any built-in provider

Similarly, a `tvshow.nfo` is not present in Youtarr channel folders, so the built-in `SeriesNfoProvider` finds nothing. The plugin must also implement `ILocalMetadataProvider<Series>` to provide channel-level Series metadata.

### Step 4: Year-seasons are created by Jellyfin, not the plugin

When an Episode has `ParentIndexNumber = 2024` and no `Season 2024` folder exists on disk, Jellyfin's `SeriesMetadataService.CreateSeasonsAsync()` automatically creates a virtual `Season` entry with `IndexNumber = 2024`. This is the standard Jellyfin mechanism — the plugin does not create Season entities itself. It only sets `ParentIndexNumber` correctly on each Episode.

**Known issue (Jellyfin 10.10.0 - 10.10.6):** A regression broke virtual season creation from `SeasonName`; the fix landed in a `release-10.10.z` point release. Using `ParentIndexNumber` (not `SeasonName`) is the correct mechanism and works reliably in 10.10.x current.

---

## How tubearchivist-jf-plugin Solves the Same Mapping

TubeArchivist stores no metadata on disk. Every metadata field must be fetched from the TA HTTP API. The mapping is:

| tubearchivist-jf mechanism | Youtarr-jf equivalent |
|---|----|
| `IRemoteMetadataProvider<Series, SeriesInfo>` — calls TA API `GET /api/channel/{id}` | `ILocalMetadataProvider<Series, ItemInfo>` — reads from channel-level data (can be extracted from any video NFO's `<studio>` / `<credits>`) |
| `IRemoteMetadataProvider<Episode, EpisodeInfo>` — calls TA API `GET /api/video/{id}` | `ILocalMetadataProvider<Episode, ItemInfo>` — parses the per-video `<movie>` NFO |
| `IRemoteImageProvider` for Series — downloads channel art from TA server URL | `ILocalImageProvider` for Series — reads `poster.jpg` / `fanart.jpg` already present in the channel folder |
| `IRemoteImageProvider` for Episode — downloads video thumbnail from TA server URL | `ILocalImageProvider` for Episode — reads the per-video thumbnail already on disk |
| Channel ID extracted from file path (`Utils.GetChannelNameFromPath` → last path segment) | Same approach: channel name = parent folder name |
| `video.ToEpisode()` sets `ParentIndexNumber = Published.Year`, `SeriesName = Channel.Name` | Same fields, same approach, from the `<movie>` NFO |
| No custom resolver — purely metadata providers on a "Shows" library | Same — no custom resolver needed |

TubeArchivist-jf has no Season-level metadata provider because there is no TA API endpoint for seasons. It relies entirely on `ParentIndexNumber` to trigger virtual season creation, exactly as this plugin should.

---

## System Overview

```
┌──────────────────────────────────────────────────────────────────┐
│                   Youtarr Downloads Root Folder                   │
│  ┌───────────────┐  ┌───────────────┐  ┌───────────────────────┐ │
│  │ ChannelName/  │  │ ChannelName2/ │  │ __kids/ChannelName3/  │ │
│  │  video.mp4    │  │  video.mp4    │  │  video.mp4            │ │
│  │  video.nfo    │  │  video.nfo    │  │  video.nfo            │ │
│  │  video.jpg    │  │  video.jpg    │  │  video.jpg            │ │
│  │  poster.jpg   │  │  poster.jpg   │  │  poster.jpg           │ │
│  └───────────────┘  └───────────────┘  └───────────────────────┘ │
└──────────────────────────────────────────────────────────────────┘
         │                        │
         │ Jellyfin "Shows" library pointed here
         ▼
┌──────────────────────────────────────────────────────────────────┐
│                      Jellyfin Library Scanner                     │
│                                                                   │
│  SeriesResolver    ──── channel folder ──→  Series item           │
│  EpisodeResolver   ──── video file    ──→  Episode item           │
│                                                                   │
│  (built-in resolvers, no plugin resolver needed)                  │
└────────────────────────┬─────────────────────────────────────────┘
                         │ metadata refresh dispatched
                         ▼
┌──────────────────────────────────────────────────────────────────┐
│                    ProviderManager (Jellyfin core)                │
│                                                                   │
│  For Series items:                                                │
│    1. YoutarrSeriesNfoProvider (ILocalMetadataProvider<Series>)   │
│       → reads any video NFO in channel folder, extracts channel   │
│         metadata from <studio>, <credits>, tags                   │
│       → populates Series.Name, Overview, Studios, Tags            │
│    2. YoutarrSeriesImageProvider (ILocalImageProvider)            │
│       → returns poster.jpg / fanart.jpg from channel folder       │
│                                                                   │
│  For Episode items:                                               │
│    1. YoutarrEpisodeNfoProvider (ILocalMetadataProvider<Episode>) │
│       → opens video.nfo, parses <movie> XML                       │
│       → maps to Episode fields (see data flow)                    │
│       → sets ParentIndexNumber = premiered.Year                   │
│    2. YoutarrEpisodeImageProvider (ILocalImageProvider)           │
│       → returns video thumbnail (video.jpg or -thumb.jpg)         │
│                                                                   │
│  For Season items (virtual, created by Jellyfin automatically):   │
│    Optional: YoutarrSeasonMetadataProvider                        │
│    → sets Season.Name = "year" string, IndexNumber                │
│    → can be deferred (Jellyfin creates season automatically)      │
└────────────────────────┬─────────────────────────────────────────┘
                         │
                         ▼
┌──────────────────────────────────────────────────────────────────┐
│                 SeriesMetadataService (Jellyfin core)             │
│                                                                   │
│  CreateSeasonsAsync() sees Episode.ParentIndexNumber = 2024       │
│  → no Season with IndexNumber=2024 exists                         │
│  → creates virtual Season { IndexNumber=2024, Name="Season 2024"} │
│  → Season is not virtual if it has at least one episode           │
└──────────────────────────────────────────────────────────────────┘
         │
         ▼
┌──────────────────────────────────────────────────────────────────┐
│             Jellyfin Database (BaseItem persistence)              │
│  Series, Season (virtual), Episode records all stored             │
└──────────────────────────────────────────────────────────────────┘
         │
         ▼
┌──────────────────────────────────────────────────────────────────┐
│             Jellyfin Web UI / Client                              │
│  Library → Channel as Series → 2024 / 2023 / ... → Episodes      │
└──────────────────────────────────────────────────────────────────┘
```

---

## Component Boundaries

| Component | Interface | Responsibility | Talks To |
|-----------|-----------|----------------|----------|
| `Plugin.cs` | `BasePlugin<PluginConfiguration>` + `IHasWebPages` | Entry point, DI registration, config, config page HTML | All other components via DI |
| `PluginConfiguration.cs` | POCO | Year-season toggle, episode numbering scheme, future options | Read by all providers |
| `YoutarrEpisodeNfoProvider` | `ILocalMetadataProvider<Episode>` | Reads `<movie>` NFO, maps to Episode, sets ParentIndexNumber | Jellyfin ProviderManager, NFO parser helper |
| `YoutarrEpisodeImageProvider` | `ILocalImageProvider` | Returns per-video thumbnail from disk | Jellyfin ItemImageProvider |
| `YoutarrSeriesNfoProvider` | `ILocalMetadataProvider<Series>` | Derives Series metadata (channel name, description) from NFO files in channel folder | Jellyfin ProviderManager, NFO parser helper |
| `YoutarrSeriesImageProvider` | `ILocalImageProvider` | Returns `poster.jpg` / `fanart.jpg` from channel folder as Series primary/backdrop | Jellyfin ItemImageProvider |
| `YoutarrSeasonMetadataProvider` | `ILocalMetadataProvider<Season>` (optional phase 2) | Sets Season name to year string; handles "flatten to one season" config toggle | Jellyfin ProviderManager |
| `NfoParser` (internal helper) | — | Reads Youtarr `<movie>` XML, extracts all fields into a plain DTO | Used by Episode and Series providers |

### What the plugin does NOT need

- No `IItemResolver` / `IMultiItemResolver` — classification is fully driven by the user's "Shows" library type plus Jellyfin's built-in resolvers.
- No `IScheduledTask` for initial MVP — the EpisodeIndexer pattern (as in jellyfin-youtube-metadata-plugin) can be added later for renumbering but is not required for core functionality.
- No HTTP client or network calls — file-only by design.

---

## Data Flow: Disk to Displayed Item

### Library scan trigger

```
User adds "Shows" library → Jellyfin scans directory tree
  → Sees ChannelName/ folder
    → SeriesResolver: collectionType == tvshows → creates Series item
    → Sees video.mp4 inside
      → EpisodeResolver: parent has Series ancestor → creates Episode item
```

### Episode metadata refresh (per Episode item)

```
Jellyfin dispatches EpisodeMetadataService.RefreshAsync(episode)
  → ILocalMetadataProvider<Episode> pipeline:
      1. YoutarrEpisodeNfoProvider.GetLocalMetadata(ItemInfo{Path=video.mp4})
         → opens video.nfo (same dir, same base name, .nfo extension)
         → parses <movie> XML:
             <title>       → Episode.Name
             <plot>        → Episode.Overview
             <premiered>   → Episode.PremiereDate; Year → Episode.ParentIndexNumber
             <studio>      → Episode.SeriesName (channel name)
             <runtime>     → Episode.RunTimeTicks
             <tag>         → Episode.Tags[]
             <genre>       → Episode.Genres[]
             <uniqueid type="youtube"> → Episode.ProviderIds["YouTube"]
             <mpaa>        → Episode.OfficialRating
         → if config.YearSeasons == false: ParentIndexNumber = 1
         → IndexNumber = date-derived (YYYYMMDD or sequential per config)
         → HasMetadata = true
         → returns MetadataResult<Episode>
      2. Built-in EpisodeNfoProvider finds no <episodedetails> → HasMetadata = false → skipped
  → ILocalImageProvider pipeline:
      YoutarrEpisodeImageProvider.GetImages(episode)
        → looks for: video-thumb.jpg OR video.jpg in same folder
        → returns RemoteImageInfo (local path) as Primary image
```

### Series metadata refresh (per Series item)

```
Jellyfin dispatches SeriesMetadataService.RefreshAsync(series)
  → ILocalMetadataProvider<Series> pipeline:
      1. YoutarrSeriesNfoProvider.GetLocalMetadata(ItemInfo{Path=ChannelName/})
         → scans channel folder for any *.nfo file
         → reads first available NFO, extracts:
             <studio> or <credits> → Series.Name (channel name)
             [channel description not in per-video NFO]
         → Alternative: channel folder name as fallback for Series.Name
         → HasMetadata = true
  → ILocalImageProvider pipeline:
      YoutarrSeriesImageProvider.GetImages(series)
        → looks for poster.jpg in channel folder → Primary image
        → looks for fanart.jpg / backdrop.jpg → Backdrop image
        → looks for banner.jpg → Banner image
```

### Season creation (automatic, no plugin involvement)

```
SeriesMetadataService.CreateSeasonsAsync(series)
  → queries all Episode children of this Series
  → collects distinct ParentIndexNumber values: {2023, 2024, 2025}
  → for each year Y:
      if no Season with IndexNumber == Y exists:
        → creates virtual Season {IndexNumber=Y, Name="Season Y", IsVirtualItem=true}
        → triggers Season metadata refresh
  → once Season has ≥1 episode: IsVirtualItem cleared
```

---

## Folder Structure Assumption (User Library Configuration)

**Assumption:** The user points a single Jellyfin "Shows" library at the Youtarr downloads root. This is the only supported configuration.

```
/path/to/youtarr/downloads/        ← Jellyfin library root (Shows type)
├── ChannelName/                   ← becomes Series
│   ├── video_title [YTID].mp4
│   ├── video_title [YTID].nfo    ← <movie> XML, parsed by YoutarrEpisodeNfoProvider
│   ├── video_title [YTID].jpg    ← episode thumbnail, used as Episode.Primary image
│   ├── another_video [YTID].mp4
│   ├── another_video [YTID].nfo
│   ├── another_video [YTID].jpg
│   └── poster.jpg                 ← used as Series.Primary image
├── AnotherChannel/
│   ├── ...
│   └── poster.jpg
└── __kids/                        ← Youtarr subfolder prefix (separate library)
    └── KidsChannel/
        └── ...
```

Youtarr's `YOUTARR_SKIP_VIDEO_FOLDER=true` (flat mode, default) keeps all video files directly in the channel folder. This is the layout the plugin targets. The nested per-video subfolder mode is a secondary concern.

**Channel art:** Youtarr writes `poster.jpg` to the channel folder. Jellyfin recognizes `poster.jpg` as the Series Primary image automatically via its built-in `LocalImageProvider`. The plugin's `YoutarrSeriesImageProvider` supplements this by explicitly returning it and looking for `fanart.jpg` as backdrop — ensuring the images surface even if Jellyfin's default scan doesn't pick them up first.

**No `tvshow.nfo`:** Youtarr does not write a `tvshow.nfo` at the channel level. Jellyfin's `SeriesNfoProvider` will find nothing. The plugin's `YoutarrSeriesNfoProvider` compensates by deriving Series metadata from the video-level `<movie>` NFOs already present.

**Youtarr subfolders (`__kids`, `__music`, `__news`):** Each subfolder is intended by Youtarr to be a separate Jellyfin library root. Users should configure a separate Jellyfin "Shows" library per Youtarr subfolder. The plugin does not need to handle multi-level nesting specially.

---

## Recommended Project Structure

```
Jellyfin.Plugin.Youtarr/
├── Jellyfin.Plugin.Youtarr.csproj
├── Plugin.cs                          # BasePlugin<YoutarrConfig> + IHasWebPages
├── Constants.cs                       # provider name, YouTube provider ID key
├── Configuration/
│   ├── PluginConfiguration.cs         # YearSeasons toggle, episode numbering, etc.
│   └── configPage.html                # Dashboard config UI (embedded resource)
├── Parsers/
│   └── YoutarrNfoParser.cs            # Reads <movie> XML, returns YoutarrVideoData DTO
├── Models/
│   └── YoutarrVideoData.cs            # Plain DTO: title, plot, premiered, studio, etc.
├── Providers/
│   ├── YoutarrEpisodeNfoProvider.cs   # ILocalMetadataProvider<Episode>
│   ├── YoutarrEpisodeImageProvider.cs # ILocalImageProvider (episode thumbnails)
│   ├── YoutarrSeriesNfoProvider.cs    # ILocalMetadataProvider<Series>
│   ├── YoutarrSeriesImageProvider.cs  # ILocalImageProvider (channel art)
│   └── YoutarrSeasonProvider.cs       # ILocalMetadataProvider<Season> (Phase 2)
└── Utils/
    └── PathUtils.cs                   # Extract channel name from path, find NFO files
```

---

## Architectural Patterns

### Pattern 1: Local-Only Metadata Providers (the right model for Youtarr)

**What:** Implement `ILocalMetadataProvider<T>` instead of `IRemoteMetadataProvider<T>`. Reads files from disk rather than network calls.

**When to use:** When the metadata source already exists on disk (Youtarr's case). The local provider fires before remote providers. If `HasMetadata = true` is returned, remote providers are skipped for that item (unless explicitly configured to also run).

**Trade-off:** Simpler, faster, works offline. The provider loses out only if Jellyfin changes its priority model (low risk).

**Example signature:**
```csharp
public class YoutarrEpisodeNfoProvider : ILocalMetadataProvider<Episode>, IHasItemChangeMonitor
{
    public string Name => Constants.ProviderName;
    public Task<MetadataResult<Episode>> GetLocalMetadata(ItemInfo info, IDirectoryService dirService, CancellationToken ct);
    public bool HasChanged(BaseItem item, IDirectoryService dirService);
}
```

### Pattern 2: ParentIndexNumber for Year-Season Grouping

**What:** Set `episode.ParentIndexNumber = uploadYear` (e.g., 2024) in the Episode provider. Jellyfin's `SeriesMetadataService.CreateSeasonsAsync()` automatically creates a virtual `Season { IndexNumber = 2024 }` container.

**When to use:** Any time you want season grouping without physical season folders on disk. Verified as the mechanism used by tubearchivist-jf-plugin.

**Trade-off:** Relies on Jellyfin's internal season-creation logic. Has had regressions (10.10.0 bug now fixed). When `YearSeasons == false` config is set, return `ParentIndexNumber = 1` instead to put all videos in one season.

**Critical:** `ParentIndexNumber` must be set to the year integer (e.g., `2024`), NOT the year as a string or as a `SeasonName`. `SeasonName` on Episode was broken in 10.10 and should not be relied on.

### Pattern 3: Series Name From Path, Not From NFO

**What:** The channel folder name IS the Series name. Use `Path.GetFileName(seriesPath)` as the authoritative Series.Name fallback.

**When to use:** `YoutarrSeriesNfoProvider` when no per-channel NFO exists. This is how tubearchivist-jf-plugin's `Utils.GetChannelNameFromPath()` works — just splits on the path separator and takes the last segment.

**Trade-off:** Channel folder names may differ from the actual channel display name. Acceptable for MVP; can be enriched later if Youtarr adds a channel-level metadata file.

### Pattern 4: Episode IndexNumber from Date

**What:** Derive `IndexNumber` from the upload date as `(year * 10000) + (month * 100) + day` for YYYYMMDD-style numbering, or use a sequential count as a fallback.

**When to use:** When there is no "episode number" concept (YouTube uploads). Date-derived numbering ensures stable, sortable episode numbers. Matches tubearchivist-jf-plugin's `EpisodeNumberingScheme.YYYYMMDD` approach.

**Trade-off:** Episode numbers like `20240315` display oddly in the UI (S2024E20240315). Some users prefer a sequential counter. Expose both as a config option.

---

## Integration Points

### Jellyfin Core Interfaces

| Interface | Where Used | Notes |
|-----------|------------|-------|
| `BasePlugin<TConfig>` | `Plugin.cs` | Core plugin base class |
| `IHasWebPages` | `Plugin.cs` | Returns embedded config page HTML |
| `ILocalMetadataProvider<T>` | Episode, Series, Season providers | Local-file metadata read |
| `ILocalImageProvider` | Episode and Series image providers | Returns on-disk image paths |
| `IHasItemChangeMonitor` | All providers | `HasChanged()` triggers re-read on rescan |

### Jellyfin's Built-in Components That Do Work For Us

| Component | What it does for us |
|-----------|---------------------|
| `SeriesResolver` | Classifies channel folders as Series (tvshows CollectionType) |
| `EpisodeResolver` | Classifies video files as Episodes (Series ancestor) |
| `SeriesMetadataService.CreateSeasonsAsync()` | Creates virtual year-Season containers from ParentIndexNumber |
| `LocalImageProvider` (core) | Detects `poster.jpg` in channel folder as Series Primary |

### Jellyfin's Built-in Components We Override/Suppress

| Component | Why suppress |
|-----------|-------------|
| `EpisodeNfoProvider` (core) | Looks for `<episodedetails>`, finds nothing in Youtarr NFOs |
| `SeriesNfoProvider` (core) | Looks for `tvshow.nfo`, not present in Youtarr |
| Remote providers (TVDB, TMDB) | User should disable these in library settings; they will not match YouTube videos |

---

## Suggested Build Order

Build in this order to de-risk the most uncertain pieces first:

### Phase 1: Minimal Plugin Loads
**Goal:** DLL loads into Jellyfin without crashing.
- `Plugin.cs` + `PluginConfiguration.cs` (empty config)
- `.csproj` targeting `net8.0` (Jellyfin 10.10.x target framework)
- Stub providers that return `HasMetadata = false` (registered but no-op)
- Verify: plugin appears in Dashboard → Plugins

### Phase 2: Series Classification and NFO Parsing
**Goal:** Channel folders show up as Series with correct names.
- `YoutarrNfoParser.cs` — parse `<movie>` XML into DTO
- `YoutarrSeriesNfoProvider` — derive Series.Name from NFO `<studio>` field
- **Critical proof point:** After library scan, ChannelName/ appears as a Series item (this already works from the Shows library type; the provider just enriches it)

### Phase 3: Episode Metadata from NFO
**Goal:** Each video appears as an Episode with correct title, date, description, tags.
- `YoutarrEpisodeNfoProvider` — map all `<movie>` fields to Episode
- Set `ParentIndexNumber = premiered.Year`
- Verify: after scan, Episodes show correct metadata; Jellyfin auto-creates year-Season containers

### Phase 4: Channel and Episode Art
**Goal:** Channel poster appears as Series image; video thumbnail as Episode image.
- `YoutarrSeriesImageProvider` — return `poster.jpg` / `fanart.jpg`
- `YoutarrEpisodeImageProvider` — return per-video thumbnail

### Phase 5: Configuration Page
**Goal:** User can toggle year-seasons and episode numbering scheme.
- `configPage.html` embedded resource
- Config bindings in providers (YearSeasons toggle, numbering scheme)

### Phase 6: Season Metadata Provider (optional enrichment)
**Goal:** Season items have "2024" as display name, not "Season 2024".
- `YoutarrSeasonProvider` — sets Season.Name from IndexNumber
- Handles flatten-to-one-season config path

### Phase 7: Packaging and Distribution
**Goal:** Installable via Jellyfin plugin repository.
- `manifest.json`, `build.yaml`
- GitHub Actions CI pipeline
- Version bumping process

---

## Anti-Patterns

### Anti-Pattern 1: Implementing a Custom IItemResolver

**What people do:** Write a custom resolver to classify folders as Series.

**Why it's wrong:** Not needed. The "Shows" library content type already drives `SeriesResolver` to classify every subdirectory as Series. Custom resolvers in Jellyfin plugins are experimental, unsupported in some versions (see github.com/jellyfin/jellyfin/issues/2187), and require path-based heuristics to avoid firing in the wrong libraries. This adds complexity with no benefit.

**Do this instead:** Point the library at the Youtarr root with content type "Shows". Classification is handled by Jellyfin's built-in resolvers automatically.

### Anti-Pattern 2: Using SeasonName Instead of ParentIndexNumber

**What people do:** Set `episode.SeasonName = "2024"` expecting Jellyfin to create a "Season 2024" container.

**Why it's wrong:** `SeasonName` was broken in Jellyfin 10.10.0 and produced "Season Unknown" entries. Even after the fix, `ParentIndexNumber` is the authoritative field that `SeriesMetadataService.CreateSeasonsAsync()` uses to group episodes and create Season containers.

**Do this instead:** Set `episode.ParentIndexNumber = uploadDate.Year`. Jellyfin creates the virtual Season automatically.

### Anti-Pattern 3: Re-implementing NFO Parsing from Scratch

**What people do:** Write a complete XML parser for Jellyfin's episode/movie NFO format.

**Why it's wrong:** Jellyfin already has `BaseNfoParser<T>` with a complete switch-based field map for all common fields. You can instantiate it or extend it.

**Do this instead:** Subclass `BaseNfoParser<Episode>` and override `FetchDataFromXmlNode` to handle `<movie>`-specific elements that differ from `<episodedetails>`. Or write a thin dedicated parser for only the fields Youtarr writes. The Youtarr NFO schema is small (15-20 fields) — a direct `XmlReader` implementation is also fine at this scale.

### Anti-Pattern 4: Calling Jellyfin's Library Manager to Create Season Items

**What people do:** Call `LibraryManager.CreateItem()` to explicitly create Season entities.

**Why it's wrong:** `SeriesMetadataService.CreateSeasonsAsync()` handles this automatically based on `ParentIndexNumber`. Manual Season creation bypasses this and can produce duplicate or conflicting entries.

**Do this instead:** Let Jellyfin create Seasons. The plugin only needs to ensure `ParentIndexNumber` is set correctly on Episode items.

---

## Scaling Considerations

This plugin operates at personal/homelab scale. There are no scaling tiers in the traditional sense. However:

| Concern | Approach |
|---------|----------|
| Large libraries (1000+ videos) | `HasChanged()` in `IHasItemChangeMonitor` prevents unnecessary re-reads; return `false` if NFO file modification time has not changed since last scan |
| Frequent library rescans | Keep providers stateless; avoid caching in-memory state |
| Multi-library setups | Provider logic should be collection-type agnostic but gracefully no-op on non-tvshows libraries (check `args.GetCollectionType()` if needed) |

---

## Sources

- [tubearchivist/tubearchivist-jf-plugin source](https://github.com/tubearchivist/tubearchivist-jf-plugin) — Providers/, TubeArchivist/Video/Video.cs (`ToEpisode()`), Utils/Utils.cs — **HIGH confidence, verified against source**
- [Jellyfin SeriesResolver.cs](https://github.com/jellyfin/jellyfin/blob/master/Emby.Server.Implementations/Library/Resolvers/TV/SeriesResolver.cs) — CollectionType.tvshows → Series classification — **HIGH confidence**
- [Jellyfin EpisodeResolver.cs](https://github.com/jellyfin/jellyfin/blob/master/Emby.Server.Implementations/Library/Resolvers/TV/EpisodeResolver.cs) — Episode classification, Season 1 default — **HIGH confidence**
- [Jellyfin EpisodeNfoParser.cs](https://github.com/jellyfin/jellyfin/blob/master/MediaBrowser.XbmcMetadata/Parsers/EpisodeNfoParser.cs) — looks for `</episodedetails>`, NOT `<movie>` — **HIGH confidence, directly verified**
- [Jellyfin SeriesMetadataService.cs](https://github.com/jellyfin/jellyfin/blob/master/MediaBrowser.Providers/TV/SeriesMetadataService.cs) — `CreateSeasonsAsync()` from `ParentIndexNumber` — **HIGH confidence**
- [Jellyfin issue #13358](https://github.com/jellyfin/jellyfin/issues/13358) — SeasonName property broken in 10.10; ParentIndexNumber is correct mechanism — **HIGH confidence**
- [Jellyfin issue #14080](https://github.com/jellyfin/jellyfin/issues/14080) — ParentIndexNumber override limitations — **MEDIUM confidence** (workarounds exist)
- [ankenyr/jellyfin-youtube-metadata-plugin](https://github.com/ankenyr/jellyfin-youtube-metadata-plugin) — ILocalMetadataProvider pattern for YouTube content — **HIGH confidence, verified against source**
- [DialmasterOrg/Youtarr nfoGenerator.js](https://github.com/DialmasterOrg/Youtarr/blob/main/server/modules/nfoGenerator.js) — `<movie>` root element, exact field names — **HIGH confidence**
- [Jellyfin DeepWiki metadata management](https://deepwiki.com/jellyfin/jellyfin/2.2-metadata-management) — provider pipeline, MergeData, refresh order — **MEDIUM confidence** (community doc, cross-referenced with source)

---

*Architecture research for: Jellyfin 10.10.x plugin for Youtarr YouTube channel library*
*Researched: 2026-06-09*
