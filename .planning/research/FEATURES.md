# Feature Research

**Domain:** Jellyfin metadata plugin — YouTube-archive library organizer (file-only)
**Researched:** 2026-06-09
**Confidence:** HIGH — NFO generator confirmed from Youtarr source (`server/modules/nfoGenerator.js`), image filenames confirmed from `videoDownloadPostProcessFiles.js` and `YOUTARR_DOWNLOADS_FOLDER_STRUCTURE.md`, tubearchivist-jf-plugin features confirmed from source code.

---

## Youtarr On-Disk Output (Confirmed from Source)

### Confirmed NFO Fields (`<movie>` root — Kodi/Jellyfin/Emby compatible)

All fields confirmed from `server/modules/nfoGenerator.js`:

| NFO Element | Source Data | Notes |
|-------------|-------------|-------|
| `<title>` | `jsonData.fulltitle \|\| jsonData.title` | Channel-prefixed title |
| `<plot>` | `jsonData.description` | Full YouTube description — can be very long, contains URLs |
| `<uniqueid type="youtube" default="true">` | `jsonData.id` | YouTube video ID |
| `<youtubeid>` | `jsonData.id` | Duplicate ID field for broader compatibility |
| `<premiered>` | `jsonData.upload_date` (YYYYMMDD → YYYY-MM-DD) | Upload date |
| `<dateadded>` | Current UTC timestamp | Download time |
| `<studio>` | `jsonData.uploader \|\| jsonData.channel` | Channel name |
| `<credits>` | `jsonData.uploader` | Channel name again |
| `<genre>` | `jsonData.categories[]` | One element per YouTube category |
| `<tag>` | `jsonData.tags[]` | One element per tag |
| `<mpaa>` | `jsonData.normalized_rating` | Normalized content rating if available |
| `<ratings><rating name="mpaa">` | Numeric mapping of MPAA rating | 1–4 scale |
| `<runtime>` | `Math.ceil(duration / 60)` | Duration in minutes |
| `<fileinfo><streamdetails><video><durationinseconds>` | `jsonData.duration` | Seconds |
| `<trailer>` | `plugin://plugin.video.youtube/?action=play_video&videoid=ID` | Kodi plugin URL |

**No `<plot>` truncation** — Youtarr writes the full YouTube description verbatim.

### Confirmed Image Files (from source)

| Filename | Location | What It Is |
|----------|----------|------------|
| `poster.jpg` | `<channel folder>/poster.jpg` | Channel artwork (thumbnail from YouTube) |
| `<VideoFilename>.jpg` | Same dir as video (nested or flat) | Per-video thumbnail |

**No banner.jpg, no fanart.jpg, no clearlogo, no channel-level NFO.** Only `poster.jpg` at channel level. The channel thumbnail is fetched separately from YouTube via yt-dlp and stored at `configModule.getImagePath()/channelthumb-<channelId>.jpg` before being copied as `poster.jpg` into the channel folder.

### Confirmed Folder Layouts

**Default (nested video subfolders):**
```
<YOUTUBE_OUTPUT_DIR>/
└── Channel Name/
    ├── poster.jpg
    └── Channel - Video Title [youtubeId]/
        ├── Channel - Video Title [youtubeId].mp4
        ├── Channel - Video Title [youtubeId].nfo
        ├── Channel - Video Title [youtubeId].[lang].srt
        └── Channel - Video Title [youtubeId].jpg
```

**With subfolders (__ prefix):**
```
<YOUTUBE_OUTPUT_DIR>/
└── __Kids/
    └── Channel Name/
        ├── poster.jpg
        └── Channel - Video Title [youtubeId]/
            └── [video files]
```

**Flat mode (per-channel option):**
```
<YOUTUBE_OUTPUT_DIR>/
└── Channel Name/
    ├── poster.jpg
    ├── Channel - Video Title [youtubeId].mp4
    ├── Channel - Video Title [youtubeId].nfo
    └── Channel - Video Title [youtubeId].jpg
```

### NFO Field → Jellyfin Episode/Series Mapping

| NFO Field | Maps To (Episode) | Maps To (Series) | Notes |
|-----------|-------------------|------------------|-------|
| `<title>` | `Episode.Name` | — | Strip channel prefix for episode display |
| `<plot>` | `Episode.Overview` | `Series.Overview` | Needs truncation — YouTube descriptions contain URLs and are multi-KB |
| `<premiered>` | `Episode.PremiereDate`, `Episode.AiredSeasonNumber` year | — | Year drives season grouping |
| `<studio>` | `Episode.Studios[]` | `Series.Studios[]` | Channel name |
| `<credits>` | — (redundant with studio) | — | Not separately useful |
| `<genre>` | `Episode.Genres[]` | — | Or pass through to Series |
| `<tag>` | `Episode.Tags[]` | — | |
| `<runtime>` | `Episode.RunTimeTicks` | — | Convert minutes → ticks (10,000,000 × seconds) |
| `<durationinseconds>` | `Episode.RunTimeTicks` | — | More precise than `<runtime>` |
| `<uniqueid type="youtube">` | `Episode.ProviderIds["YouTube"]` | `Series.ProviderIds["YouTube"]` | For linking |
| `<youtubeid>` | Fallback if uniqueid absent | — | |
| `<mpaa>` | `Episode.OfficialRating` | `Series.OfficialRating` | G/PG/PG-13/R/NC-17/TV-Y/TV-PG/TV-14/TV-MA |
| `<trailer>` | Ignored for Episodes | — | Kodi plugin URL, not useful in Jellyfin |
| `<dateadded>` | — | — | Download timestamp; Jellyfin manages its own DateAdded |

**Series-level metadata source:** No `tvshow.nfo` or channel-level NFO exists on disk. Series name comes from the channel folder name. Series poster comes from `poster.jpg` in the channel folder. Series description would need to be synthesized or left empty at v1.

---

## tubearchivist-jf-plugin Feature Audit

Features confirmed from source code (`Providers/`, `Tasks/`, `Configuration/`):

### API-Required Features (all sync features require TubeArchivist running instance)
- Bidirectional watched-status sync (TA→JF, JF→TA) via scheduled tasks
- Bidirectional playlist sync (TA→JF, JF→TA) via scheduled tasks
- Optional playlist deletion on sync
- Series metadata from API (`SeriesMetadataProvider` calls `taApi.GetChannel()`)
- Episode metadata from API (`EpisodeMetadataProvider` calls `taApi.GetVideo()`)
- Channel artwork served via API proxy (Primary, Art/tvart, Banner, Backdrop all from TubeArchivist HTTP endpoints)
- Library startup sync

### Configuration Fields (confirmed from `PluginConfiguration.cs`)
- `CollectionTitle` — display name for the collection
- `TubeArchivistUrl` + `TubeArchivistApiKey` — instance connection
- `MaxDescriptionLength` (default 500) — truncates both series and episode overviews
- `EpisodeNumberingScheme` — `Default` (null index) or `YYYYMMDD` (integer like 20250804)
- `TAJFProgressSync`, `JFTAProgressSync` — directional progress sync toggles
- `JFTAPlaylistsSync`, `JFTAPlaylistsDelete`, `TAJFPlaylistsSync`, `TAJFPlaylistsDelete` — playlist sync
- `JFUsernameFrom`, `JFUsernamesTo` — user assignment for sync
- `TAJFProgressTaskInterval`, `JFTAPlaylistsSyncTaskInterval`, `TAJFPlaylistsSyncTaskInterval` — intervals in seconds (min 60)

### Key Implementation Pattern from tubearchivist-jf-plugin
- Season is driven by `Published.Year` set as `ParentIndexNumber` on Episode
- `YYYYMMDD` numbering: `(year × 10000) + (month × 100) + day` as integer `IndexNumber`
- `Default` numbering: `IndexNumber = null` (Jellyfin auto-sequences)
- `FormatDescription()` truncates at `MaxDescriptionLength` chars, converts `\n` → `<br>`
- Series image provider only provides `Primary` (thumb), `Art` (tvart), `Banner` — all fetched via API

---

## Feature Landscape

### Table Stakes (Users Expect These)

Features without which the plugin is pointless.

| Feature | Why Expected | File-Only? | Complexity | Notes |
|---------|--------------|------------|------------|-------|
| Channel folder → Jellyfin Series | Core value prop; without this it's just a flat movie library | YES | MEDIUM | Requires custom resolver or library type convention; central technical challenge |
| Video → Episode under its channel's Series | Core value prop | YES | MEDIUM | Depends on Series grouping working |
| Season grouping by upload year | Explicitly required; `<premiered>` year → `ParentIndexNumber` | YES | LOW | Year parsed from NFO `<premiered>`; tubearchivist-jf-plugin proves the pattern |
| Per-episode metadata from NFO | Users expect title, description, air date, runtime — Jellyfin reads NFO but maps to Movie without this plugin | YES (NFO on disk) | LOW | Remap `<movie>` NFO fields to Episode; re-use Jellyfin's NFO reader or parse directly |
| Channel poster as Series artwork | Without it the library looks like placeholder squares | YES (`poster.jpg` confirmed) | LOW | `poster.jpg` is always present if Youtarr's "Copy channel poster.jpg" setting is on |
| Video thumbnail as Episode image | Standard expectation for any video library | YES (`<VideoFilename>.jpg` confirmed) | LOW | Same filename as video with `.jpg` extension |
| Config page (year-seasons toggle, episode numbering) | Users need to control the two main behavioural options | N/A | LOW | Standard Jellyfin plugin config page pattern |
| Manual DLL install path | Without this nobody can use the plugin | N/A | LOW | Standard Jellyfin plugin packaging |

### Differentiators (Competitive Advantage)

Features that add value beyond the baseline.

| Feature | Value Proposition | File-Only? | Complexity | Notes |
|---------|-------------------|------------|------------|-------|
| YYYYMMDD episode numbering | Unique to YouTube archives — lets users see exact upload date as episode number; makes browsing by date natural | YES | LOW | Integer `IndexNumber = (year×10000)+(month×100)+day`; confirmed approach from tubearchivist-jf-plugin |
| Plot/description truncation with configurable max length | Youtarr writes full YouTube descriptions (often 500–5000 chars with URLs, hashtags, sponsor text); raw plot looks terrible in Jellyfin UI | YES | LOW | Port `FormatDescription()` from tubearchivist-jf-plugin: truncate at N chars, `\n` → `<br>`; expose max length in config |
| Season-flatten option (single Season 1) | Some channels have irregular upload schedules where year-splitting is unhelpful; or users just want simple browsing | YES | LOW | Config toggle: when disabled, all episodes go to `ParentIndexNumber = 1` |
| Content rating surfacing | Youtarr writes `<mpaa>` with MPAA/TV-PG ratings (G/PG/PG-13/R/NC-17/TV-Y/TV-PG/TV-14/TV-MA); surfaces in Jellyfin parental controls | YES (from NFO) | LOW | Read `<mpaa>` from NFO, set on Episode; potentially propagate to Series |
| Multi-library support via `__prefix` subfolders | Youtarr's `__Kids`, `__Music` etc. allow separate Jellyfin libraries per channel group; each subfolder path becomes its own library | YES (folder layout) | LOW | No plugin work needed — user creates separate Jellyfin libraries pointing to `__subfolder` paths; document this |
| YouTube ID as ProviderID | Enables deep-linking to YouTube and future extensibility | YES | LOW | Write `ProviderIds["YouTube"] = youtubeId` on Episode and Series |
| Genre and tag passthrough | Maps YouTube categories → genres, YouTube tags → tags; enables Jellyfin filtering | YES | LOW | Multiple `<genre>` and `<tag>` elements already in NFO |
| Publishable plugin manifest / repo | Personal-use plugin with clean manifest, versioning, CI — ready to submit to Jellyfin plugin catalogue | N/A | LOW | Affects packaging/distribution, not functionality |
| Both nested and flat folder layout support | Youtarr supports per-channel flat mode; plugin must resolve videos whether they are in video subfolders or directly in the channel folder | YES | MEDIUM | ID extraction from filename `[youtubeId]` suffix must work in both layouts |

### Anti-Features (Deliberately Not Built)

| Feature | Why Requested | Why Avoid | Better Alternative |
|---------|---------------|-----------|-------------------|
| Watched-status sync to Youtarr | Users want Jellyfin watch state reflected in Youtarr | Requires a running Youtarr instance + API key; breaks file-only constraint; major additional complexity; Youtarr's API may change | Defer to v2; if ever built, make it opt-in with explicit API key config |
| Playlist sync (Jellyfin ↔ Youtarr) | Users have YouTube playlists and want them as JF playlists | Same as above — needs API; playlist ordering requires API round-trips; very complex | File-only: user creates Jellyfin collections manually |
| Downloading YouTube content | Users might want one-click download from JF | Completely out of scope — that's Youtarr's job | Use Youtarr directly |
| Re-implementing NFO parsing from scratch | Might seem like cleaner architecture | Jellyfin already has a robust NFO reader; duplicating it means maintaining XML parsing logic and staying in sync with Jellyfin's NFO schema changes | Leverage Jellyfin's existing NFO infrastructure where possible; only override what's needed for Series/Episode structure |
| Scraping YouTube metadata at runtime | Some plugins fetch metadata directly from YouTube | Breaks file-only; adds YouTube API dependency; YouTube rate-limits and changes; Youtarr already fetches all needed metadata | Read from NFO — it's already there |
| Channel-level NFO writing | Plugin could generate `tvshow.nfo` for each channel | Modifying the user's filesystem is risky; Youtarr doesn't write one; the plugin is a read-only consumer | Synthesize Series metadata from folder name + `poster.jpg` in memory; do not write files |
| Automatic scheduled library refresh | Pull-style "check Youtarr for new videos" task | Needs API; Jellyfin already has "real-time monitoring" for file system changes | Rely on Jellyfin's built-in real-time monitoring (inotify/FSW); document the setting |
| Configurable filename template awareness | Youtarr has a `videoFilenamePrefix` config that changes filename patterns | Over-engineering for v1; the `[youtubeId]` suffix in brackets is always present regardless of prefix | Parse YouTube ID from `[youtubeId]` bracket pattern in filename — robust across all templates |

---

## Feature Dependencies

```
Channel → Series grouping
    └──requires──> Video → Episode mapping
                       └──requires──> Year-season extraction from <premiered>

Per-episode metadata from NFO
    └──requires──> Video → Episode mapping (must be an Episode before metadata applies)

Channel poster as Series artwork
    └──requires──> Channel → Series grouping

Video thumbnail as Episode image
    └──requires──> Video → Episode mapping

YYYYMMDD episode numbering
    └──requires──> Video → Episode mapping
    └──requires──> <premiered> date in NFO

Plot truncation config
    └──requires──> Per-episode metadata from NFO

Content rating surfacing
    └──requires──> Per-episode metadata from NFO
    └──requires──> <mpaa> field present in NFO (only present when Youtarr assigns a rating)

Multi-library via __ subfolders
    └──requires──> Channel → Series grouping (must resolve channel even with subfolder prefix)

Season-flatten option
    └──conflicts──> YYYYMMDD episode numbering (when flat, year season is gone; numbering still works)
```

### Dependency Notes

- **Series grouping requires Episode mapping:** A Series with no episodes is useless; both must work together.
- **All metadata features require the channel→Series / video→Episode resolver:** The resolver is the foundation everything else builds on.
- **Content rating is conditional:** `<mpaa>` is only written when Youtarr determines a rating (MPAA priority → TVPG → yt rating → age_limit heuristic → omitted). The plugin must handle absent `<mpaa>` gracefully.
- **Flat folder layout requires robust ID extraction:** In flat mode there is no video subfolder; the YouTube ID `[youtubeId]` is in the filename stem. The plugin must handle both `Channel - Title [id]/Channel - Title [id].mp4` (nested) and `Channel - Title [id].mp4` (flat) layouts.
- **Multi-library (`__prefix`) is transparent:** The `__Kids/Channel/` path just means Jellyfin is pointed at `__Kids/` as the library root; the channel folders inside are identical. No special plugin logic needed — document for users.

---

## MVP Definition

### Launch With (v1)

Minimum viable product — what validates the core concept.

- [ ] Channel folder → Jellyfin Series — why essential: the entire value prop
- [ ] Video → Episode under its Series — why essential: the entire value prop
- [ ] Season grouping by upload year (default ON, config toggle to flatten) — why essential: explicitly stated requirement; without it episodes pile into a single unsorted list
- [ ] Per-episode metadata from NFO (title, plot, premiered, runtime, genre, tags, studio, YouTube ID) — why essential: a library full of untitled episodes with no descriptions is useless
- [ ] Channel poster (`poster.jpg`) as Series primary image — why essential: the library looks broken without artwork
- [ ] Video thumbnail (`.jpg` next to video) as Episode image — why essential: standard visual affordance
- [ ] Config page with: year-seasons toggle, episode numbering scheme (Default vs YYYYMMDD), max description length — why essential: users need control over the two key behaviors
- [ ] Plot truncation (configurable, default 500 chars) — why essential: raw YouTube descriptions make the episode detail view unreadable
- [ ] Manual DLL install (zip package) — why essential: no install = no users

### Add After Validation (v1.x)

- [ ] Content rating surfacing from `<mpaa>` — trigger: users report parental control needs; low effort once v1 ships
- [ ] Genre and tag passthrough — trigger: users want filtering; trivial to add
- [ ] YouTube ID as ProviderID on Series and Episode — trigger: clean data; enables future features
- [ ] Publishable plugin manifest + repo listing — trigger: want to share with community
- [ ] CI build + automated packaging — trigger: before publishing

### Future Consideration (v2+)

- [ ] Watched-status sync to/from Youtarr — why defer: requires API; breaks file-only constraint; complex; validate that file-only v1 satisfies users first
- [ ] Playlist sync — why defer: same as above
- [ ] Channel description as Series Overview — why defer: no channel-level NFO exists; would require either a Youtarr API call or writing a `tvshow.nfo` file; complexity not warranted until file-only proves insufficient

---

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| Channel → Series grouping | HIGH | MEDIUM | P1 |
| Video → Episode mapping | HIGH | MEDIUM | P1 |
| Year-season grouping | HIGH | LOW | P1 |
| Per-episode NFO metadata | HIGH | LOW | P1 |
| Channel poster as Series art | HIGH | LOW | P1 |
| Video thumbnail as Episode art | MEDIUM | LOW | P1 |
| Config page | HIGH | LOW | P1 |
| Plot truncation | MEDIUM | LOW | P1 |
| Manual DLL install packaging | HIGH | LOW | P1 |
| YYYYMMDD episode numbering | MEDIUM | LOW | P2 |
| Season-flatten toggle | MEDIUM | LOW | P2 |
| Content rating surfacing | MEDIUM | LOW | P2 |
| Genre/tag passthrough | LOW | LOW | P2 |
| YouTube ID as ProviderID | LOW | LOW | P2 |
| Plugin manifest / repo listing | MEDIUM | LOW | P2 |
| CI build pipeline | LOW | LOW | P3 |
| Watched-status sync | MEDIUM | HIGH | P3 |
| Playlist sync | LOW | HIGH | P3 |

**Priority key:** P1 = must have for launch, P2 = add when core works, P3 = future

---

## Competitor Feature Analysis

| Feature | tubearchivist-jf-plugin | youtarr-jf-plugin (this) |
|---------|------------------------|--------------------------|
| Channel → Series | YES (via API) | YES (via folder structure) |
| Video → Episode | YES (via API) | YES (via NFO + folder) |
| Year-season grouping | YES | YES |
| Metadata source | TubeArchivist REST API | NFO files on disk |
| Series description | YES (API: `channel_description`) | v1: empty; v2: consider API |
| Channel artwork (Primary) | YES (API: `channel_thumb_url`) | YES (`poster.jpg`) |
| Channel artwork (Backdrop) | YES (API: `channel_tvart_url`) | NO (not written by Youtarr) |
| Channel artwork (Banner) | YES (API: `channel_banner_url`) | NO (not written by Youtarr) |
| Episode thumbnail | YES (API: `vid_thumb_url`) | YES (`.jpg` alongside video) |
| YYYYMMDD numbering | YES | YES |
| Plot truncation (configurable) | YES (default 500) | YES (port same approach) |
| Content rating | NO | YES (from `<mpaa>` NFO field) |
| Watched-status sync | YES (bidirectional) | NO (v1; deferred) |
| Playlist sync | YES (bidirectional) | NO (deferred) |
| Scheduled tasks | YES (4 sync tasks) | NO (Jellyfin FSW sufficient) |
| API key required | YES (hard requirement) | NO (file-only) |
| Flat folder support | N/A | YES (both layouts) |
| __ subfolder multi-library | N/A | YES (transparent — document only) |
| Publishable manifest | YES | Target: YES (v1.x) |

---

## Sources

- Youtarr NFO generator: `server/modules/nfoGenerator.js` in `DialmasterOrg/Youtarr` (confirmed fields, exact XML structure)
- Youtarr image generation: `server/modules/videoDownloadPostProcessFiles.js` (confirmed `poster.jpg` at channel level, `<filename>.jpg` at video level; no banner/fanart)
- Youtarr folder structure: `docs/YOUTARR_DOWNLOADS_FOLDER_STRUCTURE.md` (confirmed nested, flat, and `__prefix` layouts)
- Youtarr media server docs: `docs/MEDIA_SERVERS.md`, `docs/media-servers/jellyfin.md`
- Youtarr rating system: `server/modules/ratingMapper.js` (MPAA + TV-PG ratings, numeric mapping)
- tubearchivist-jf-plugin configuration: `Configuration/PluginConfiguration.cs` (all config fields confirmed)
- tubearchivist-jf-plugin numbering: `Configuration/NumberingScheme.cs` (Default + YYYYMMDD enum)
- tubearchivist-jf-plugin episode mapping: `TubeArchivist/Video/Video.cs` (`ToEpisode()` method — season, IndexNumber logic)
- tubearchivist-jf-plugin series mapping: `TubeArchivist/Channel/Channel.cs` (`ToSeries()` method)
- tubearchivist-jf-plugin artwork: `Providers/SeriesImageProvider.cs` (Primary, Art, Banner, Backdrop image types)
- tubearchivist-jf-plugin description truncation: `Utils/Utils.cs` (`FormatDescription()` — 500 char default, `\n`→`<br>`)
- Jellyfin shows naming/structure: https://jellyfin.org/docs/general/server/media/shows/
- Jellyfin NFO metadata support: https://jellyfin.org/docs/general/server/metadata/nfo/

---
*Feature research for: Jellyfin plugin for Youtarr download folder organization*
*Researched: 2026-06-09*
