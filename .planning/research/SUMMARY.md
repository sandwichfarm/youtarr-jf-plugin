# Project Research Summary

**Project:** Youtarr Jellyfin Plugin
**Domain:** Jellyfin 10.10.x metadata provider plugin (C#/.NET) — YouTube archive organizer
**Researched:** 2026-06-09
**Confidence:** HIGH

## Executive Summary

This is a Jellyfin metadata provider plugin, not a downloader or sync tool. The core challenge is turning Youtarr's flat channel-folder/video layout into a Series/Season/Episode hierarchy inside Jellyfin's library system. Research confirms the approach is well-understood: use a "Shows" library content type (which causes Jellyfin's built-in `SeriesResolver` to classify every channel subfolder as a Series automatically), then supply metadata via `ILocalMetadataProvider<Series>`, `ILocalMetadataProvider<Episode>`, and `ILocalImageProvider` implementations that read what Youtarr has already written to disk. No custom resolver is needed; no API key is required. The `tubearchivist-jf-plugin` proves this exact pattern works for YouTube archive content.

The recommended approach builds in seven short phases: scaffold the plugin and prove it loads, wire up Series classification and NFO parsing, implement Episode metadata (the hardest piece technically), add artwork providers, expose the configuration page, add optional Season enrichment, then package for distribution. Phase 3 (Episode metadata and year-season assignment) is the highest-risk phase and must be validated against a live Jellyfin 10.10.x instance early, because `ParentIndexNumber`-based virtual season creation has had regressions and there is a direct contradiction between the architecture ideal and a known pitfall (see the Season Grouping section below).

Key risks in priority order: (1) the season grouping mechanism — physical `Season YYYY` folders vs. virtual seasons via `ParentIndexNumber` — is the single most dangerous unresolved question and must be tested on a live instance before committing to the architecture; (2) Jellyfin's built-in `EpisodeNfoProvider` is incompatible with Youtarr's `<movie>`-rooted NFOs, so the plugin must provide its own Episode parser; (3) the library content type must be "Shows" — a "Movies" or "Mixed" library makes all metadata providers irrelevant. All other risks are manageable with standard defensive coding.

---

## Critical Research Contradiction: Season Grouping Mechanism

**This must be resolved by testing on a live Jellyfin 10.10.x instance before Phase 3 implementation.**

ARCHITECTURE.md and PITFALLS.md make contradictory claims about how year-seasons are created:

**ARCHITECTURE.md claims:** Setting `Episode.ParentIndexNumber = uploadYear` causes Jellyfin's `SeriesMetadataService.CreateSeasonsAsync()` to automatically create a virtual Season container with `IndexNumber = uploadYear`. No physical season folders needed. This is the mechanism used by `tubearchivist-jf-plugin` and is documented in Jellyfin's `SeriesMetadataService.cs` source.

**PITFALLS.md claims (issues #11916, #13197):** Since Jellyfin 10.9.4, the `SeasonResolver` only treats a folder as a distinct Season if it is named `Season ##` with a numeric suffix. Raw year folders (`2023/`, `YouTube-2023/`) collapse into a single unnamed season. Furthermore, the NFO `<season>` tag is ignored — season assignment is driven by physical folder structure or the `SxxExx` filename pattern, not by metadata provider values.

**Reconciliation:** Both claims can be simultaneously true under different conditions:

- **Flat layout (no year subfolders on disk):** Youtarr's default mode — `YOUTARR_SKIP_VIDEO_FOLDER=true` — puts video files directly in the channel folder with no year subdirectories. In this layout, there are no physical season folders for `SeasonResolver` to classify. `SeriesMetadataService.CreateSeasonsAsync()` relies entirely on `ParentIndexNumber` to create virtual Season containers. This is where ARCHITECTURE.md's claim applies. Whether this reliably works in Jellyfin 10.10.x — or whether the #11916/#13197 regressions broke this path too — is the open question.

- **Nested layout (year subfolders on disk):** If the disk has `ChannelName/2023/video.mp4` (year-named subdirectories), `SeasonResolver` runs on those folders. Per PITFALLS.md, folders named `2023` (raw year) are not reliably classified as `Season 2023` since 10.9.4. Renaming them to `Season 2023` fixes the resolver but Youtarr does not produce this naming by default and the plugin cannot rename user files.

**Recommended approach:** Target Youtarr's default flat layout (no year subfolders). Rely on `ParentIndexNumber` for virtual season creation. This is the approach proven by `tubearchivist-jf-plugin`. Validate this against a live 10.10.x instance as the first concrete proof point of Phase 3. If `ParentIndexNumber` alone fails to create virtual seasons reliably, the fallback is a `ILibraryPostScanTask` that groups episodes into seasons after the main scan — but do not implement this until the virtual season path is confirmed broken.

**Residual risk (must validate early):** It is unknown whether Jellyfin 10.10.x's `CreateSeasonsAsync()` correctly creates virtual seasons from `ParentIndexNumber` when there are no physical season folders. The architecture research cross-references `SeriesMetadataService.cs` source (HIGH confidence), but the pitfall research cross-references two unresolved GitHub issues (#11916, #13197) that predate 10.10.x. Neither confirms behavior specifically for flat-layout virtual season creation in 10.10.7.

**Flag for Phase 3:** Before building the full Episode metadata pipeline, create a minimal test — one provider stub returning `ParentIndexNumber = 2024` for one episode in a flat channel folder — and verify that "Season 2024" appears in the UI. If it does, proceed with the virtual-season architecture. If not, design around physical `Season YYYY` folder requirements or a post-scan task.

---

## Key Findings

### Recommended Stack

The stack is non-negotiable in its essentials: C# targeting `net8.0` with `Jellyfin.Controller 10.10.7` and `Jellyfin.Model 10.10.7`. Both NuGet packages must use `<ExcludeAssets>runtime</ExcludeAssets>` or the plugin will fail to load due to assembly duplication. Current stable Jellyfin is 10.11.11, but the user's server is on 10.10.x; building for 10.10.x now and upgrading later requires only bumping the TFM, package versions, and `targetAbi` — the interfaces are stable.

NFO parsing uses `System.Xml.Linq` (XDocument) from the BCL — no additional NuGet package needed. The Youtarr NFO schema is small (15-20 fields) and well-defined. Packaging uses `jprm` (Python) to produce the versioned ZIP and `manifest.json`. GitHub Actions CI is the target for automated release builds.

**Core technologies:**
- `.NET 8 / C#` (`net8.0`): plugin runtime — required by Jellyfin 10.10.x, non-negotiable
- `Jellyfin.Controller 10.10.7`: plugin SDK providing all interfaces — must match server version exactly
- `Jellyfin.Model 10.10.7`: entity types (Series, Season, Episode) — must be identical version to Controller
- `System.Xml.Linq` (BCL): NFO parsing via XDocument — no extra package, sufficient for `<movie>` NFOs
- `jprm` (Python): plugin packaging and manifest generation — standard across Jellyfin plugin ecosystem
- `xUnit` + `Moq`: testing — for NFO parser logic at minimum

### Expected Features

Youtarr writes `<movie>`-rooted NFOs (not `<episodedetails>`), `poster.jpg` at channel level, and `<VideoFilename>.jpg` per video. No `tvshow.nfo`, no `banner.jpg`, no `fanart.jpg`. The plugin must synthesize Series metadata from per-video NFO `<studio>` fields and the channel folder name. Series description (from channel description) is not available in v1 — no channel-level NFO exists on disk.

**Must have (table stakes):**
- Channel folder resolved as Jellyfin Series — the entire value proposition
- Video file resolved as Episode under its channel's Series
- Year-season grouping via `ParentIndexNumber = uploadYear` (default ON, toggle to flatten)
- Per-episode metadata from `<movie>` NFO (title, plot/description, premiered, runtime, genre, tags, studio, YouTube ID)
- Channel `poster.jpg` as Series primary artwork
- Per-video `.jpg` thumbnail as Episode primary image
- Configuration page: year-seasons toggle, episode numbering scheme (Default vs YYYYMMDD), max description length
- Plot truncation (default 500 chars — YouTube descriptions are often multi-KB with hashtags and URLs)
- Manual DLL install via ZIP package

**Should have (competitive differentiators):**
- YYYYMMDD episode numbering: `(year * 10000) + (month * 100) + day` as `IndexNumber` — stable, sortable, date-readable; proven approach from `tubearchivist-jf-plugin`
- Season-flatten toggle: when disabled, all episodes go to `ParentIndexNumber = 1`
- Content rating surfacing from `<mpaa>` NFO field (G/PG/PG-13/R/NC-17/TV-Y/TV-PG/TV-14/TV-MA)
- YouTube ID as `ProviderIds["YouTube"]` on Episode and Series
- Genre and tag passthrough from NFO `<genre>` and `<tag>` elements
- Both flat and nested folder layout support (Youtarr supports per-channel flat mode)
- Publishable plugin manifest (`manifest.json`, `build.yaml`)

**Defer (v2+):**
- Watched-status sync to/from Youtarr — requires API key, breaks file-only constraint; validate file-only v1 first
- Playlist sync — same reasons
- Channel description as Series Overview — no channel-level NFO exists; would require Youtarr API or writing files to disk
- CI build pipeline — useful but not blocking for personal use

### Architecture Approach

The architecture is purely provider-based: no custom `IItemResolver`, no `IScheduledTask` for MVP. Classification relies entirely on Jellyfin's built-in `SeriesResolver` and `EpisodeResolver`, which fire automatically when the library content type is "Shows". The plugin's job is to supply correct metadata on top of already-classified items. A central `YoutarrNfoParser` helper parses the `<movie>` XML into a plain DTO; providers consume that DTO. Season entities are created automatically by `SeriesMetadataService.CreateSeasonsAsync()` based on `Episode.ParentIndexNumber` — the plugin never directly creates Season items.

**Major components:**
1. `Plugin.cs` + `PluginConfiguration.cs` — entry point, DI, config (year-seasons toggle, numbering scheme, max description length)
2. `YoutarrNfoParser.cs` — reads `<movie>` XML with explicit UTF-8, returns `YoutarrVideoData` DTO; handles missing fields, emoji, unescaped entities
3. `YoutarrEpisodeNfoProvider` (`ILocalMetadataProvider<Episode>`) — maps DTO to Episode fields, sets `ParentIndexNumber`, derives `IndexNumber` from upload date
4. `YoutarrSeriesNfoProvider` (`ILocalMetadataProvider<Series>`) — derives Series name from channel folder name or `<studio>` field in any per-video NFO
5. `YoutarrEpisodeImageProvider` + `YoutarrSeriesImageProvider` (`ILocalImageProvider`) — return per-video thumbnail and channel `poster.jpg` respectively
6. `YoutarrSeasonProvider` (`ILocalMetadataProvider<Season>`) — optional Phase 6; enriches virtual season display name
7. `configPage.html` (embedded resource) — Jellyfin Dashboard configuration UI

### Critical Pitfalls

1. **Wrong library type:** The Youtarr folder must be configured as a "Shows/TV Shows" library — not "Movies" or "Mixed". If wrong, Jellyfin classifies videos as Movie items and all `ILocalMetadataProvider<Episode>` implementations are never called. User-configuration requirement; must be prominently documented.

2. **`<movie>` NFO root incompatibility:** Jellyfin's built-in `EpisodeNfoProvider` looks for `<episodedetails>` root and finds nothing in Youtarr NFOs — it silently returns `HasMetadata = false`. The plugin's Episode provider must parse the `<movie>` root explicitly and map fields to `MetadataResult<Episode>`.

3. **Season grouping mechanism (CRITICAL — validate early):** Detailed in the section above. Must be tested against a live Jellyfin 10.10.x instance before building out the full metadata pipeline.

4. **`ParentIndexNumber` silently discarded (issue #14080):** Jellyfin's filename parser runs before metadata providers. For flat layouts with no `SxxExx` patterns, the parser produces no value so the provider's value should win — but confirm during Phase 3 testing.

5. **`targetAbi` mismatch causes silent load failure:** Set `targetAbi` in `meta.json` to `10.10.7.0` to match the NuGet version. Mismatches produce `NotSupported` status with no prominent UI error; `ReflectionTypeLoadException` in logs is the diagnostic signal.

6. **Jellyfin overwrites NFOs (10.9.0+ regression, issue #12197):** "Save metadata to media folders" must be disabled for Youtarr libraries. Document prominently.

7. **Plugin GUID is permanent:** Generate once, commit, never regenerate. A GUID change orphans all user configuration.

---

## Implications for Roadmap

Based on combined research, a seven-phase structure is recommended. Phases are ordered to front-load risk validation and ensure each phase delivers a usable, testable artifact.

### Phase 1: Project Scaffold and Plugin Loads
**Rationale:** All other work is blocked until the DLL loads into Jellyfin without error. `targetAbi`, `ExcludeAssets`, and GUID must be set correctly from the start — mistakes here cause silent failures in all later phases.
**Delivers:** Plugin appears as "Active" in Dashboard with a stub configuration page. No functional behavior yet.
**Addresses:** Plugin install requirement; configuration page skeleton
**Avoids:** `targetAbi` mismatch (Pitfall 4), GUID drift (Pitfall 13), `ExcludeAssets` omission

### Phase 2: Series Classification Proof Point
**Rationale:** Confirm that a "Shows" library pointed at the Youtarr root causes channel folders to appear as correctly-named Series. Also implement `YoutarrNfoParser` and `YoutarrSeriesNfoProvider`. Validates the foundational architecture assumption before any Episode work.
**Delivers:** Channel folders visible as correctly-named Series in Jellyfin. No episodes or seasons yet.
**Uses:** `ILocalMetadataProvider<Series>`, `YoutarrNfoParser` (first use), `System.Xml.Linq`
**Avoids:** Wrong library type (Pitfall 1), custom resolver trap (Pitfall 5)

### Phase 3: Episode Metadata and Year-Season Assignment
**Rationale:** Highest-risk phase. The virtual season creation mechanism (ARCHITECTURE vs. PITFALLS contradiction) must be validated first with a minimal stub before the full pipeline is built. This phase maps `<movie>` NFO fields to `MetadataResult<Episode>`, derives episode numbering from upload date, and sets `ParentIndexNumber` to trigger year-season creation.
**Delivers:** Videos appear as Episodes under their Series, grouped into year-seasons. All per-video metadata populated. YYYYMMDD episode numbering and season-flatten toggle implemented.
**Uses:** `ILocalMetadataProvider<Episode>`, `YoutarrNfoParser` (extended), date parsing, episode numbering logic
**Avoids:** `<movie>` NFO incompatibility (Pitfall 2), episode number missing (Pitfall 7), same-day collisions (Pitfall 8), missing premiere dates (Pitfall 9), NFO overwrite (Pitfall 6)
**Research flag:** MUST validate `ParentIndexNumber` virtual season creation on live 10.10.x before building full pipeline. Test with one stub episode first.

### Phase 4: Artwork Providers
**Rationale:** Once Series and Episodes are structured, artwork is the biggest remaining visual gap. Both image sources are confirmed present on disk. Low risk; depends on Phase 2 (Series) and Phase 3 (Episodes) completing first.
**Delivers:** Channel `poster.jpg` as Series primary image; per-video thumbnails as Episode images.
**Uses:** `ILocalImageProvider` for both Series and Episode
**Avoids:** Stale image cache pitfall (document "Replace all images" workaround)

### Phase 5: Configuration Page and Polish
**Rationale:** Config toggles are already wired into providers from Phase 3; this phase exposes them in the Dashboard UI. Plot truncation (ported from `tubearchivist-jf-plugin`'s `FormatDescription()`) finalized here.
**Delivers:** Functional configuration page. Year-seasons toggle, YYYYMMDD vs default numbering, max description length.
**Uses:** `IHasWebPages`, embedded `configPage.html`, `PluginConfiguration.cs`

### Phase 6: Season Metadata Enrichment (Optional)
**Rationale:** Jellyfin auto-creates virtual seasons with acceptable "Season YYYY" names from `ParentIndexNumber`. This phase adds explicit control over Season display names and handles edge cases (Season 0 for undated videos, flatten path). Deferred because auto-created names are sufficient for most users.
**Delivers:** Controlled Season display names; Season 0 handling for undated videos.
**Uses:** `ILocalMetadataProvider<Season>`

### Phase 7: Packaging and Distribution
**Rationale:** Personal use works with a manual DLL drop. Clean packaging enables community sharing and validates the distribution mechanism.
**Delivers:** Versioned ZIP artifact, `manifest.json` for plugin repository, CI build on tag push.
**Uses:** `jprm`, GitHub Actions, `dotnet publish`

### Phase Ordering Rationale

- Phases 1-2 are hard prerequisites: nothing can be tested without a loading plugin and a correctly-configured library.
- Phase 3 is the critical path: highest technical risk (virtual season mechanism), most complex implementation (NFO parsing, date logic, numbering), and gates all subsequent phases.
- Phase 4 (artwork) is isolated from Phase 3 complexity but requires Series and Episodes to exist — ordered after Phase 3 to keep phases small and testable.
- Phase 5 (config page) is decoupled from media logic and could theoretically precede Phase 4, but placed here so all provider behavior is complete before the UI is finalized.
- Phase 6 is explicitly optional; auto-created season names are functional, not placeholder.
- Phase 7 is last because packaging requires a stable artifact.

### Research Flags

Phases needing live-instance validation or deeper research during planning:

- **Phase 3 (virtual season creation):** CRITICAL. Validate `ParentIndexNumber` virtual season creation on Jellyfin 10.10.7 with a flat folder layout using a minimal stub before committing to the full architecture. This reconciles the ARCHITECTURE.md vs. PITFALLS.md contradiction.
- **Phase 3 (ParentIndexNumber override priority):** Confirm the Episode provider's `ParentIndexNumber` is not discarded by Jellyfin's filename parser for flat-layout files (no `SxxExx` pattern). Issue #14080 documents a related but not identical scenario.
- **Phase 4 (image provider necessity):** Verify whether Jellyfin's built-in `LocalImageProvider` picks up `poster.jpg` automatically from channel folders, or whether a custom `ILocalImageProvider` is required. If the built-in handles it, Phase 4 scope shrinks.

Phases with standard, well-documented patterns (skip research-phase):

- **Phase 1 (scaffold):** Official plugin template, `build.yaml` format, `ExcludeAssets` pattern — all verified and documented.
- **Phase 2 (Series classification):** "Shows" library type + built-in `SeriesResolver` behavior verified against Jellyfin source.
- **Phase 5 (config page):** `IHasWebPages` + embedded HTML is standard; `tubearchivist-jf-plugin` is a working reference.
- **Phase 7 (packaging):** `jprm` + GitHub Actions pattern fully documented with a working reference.

---

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | NuGet versions verified directly on NuGet Gallery; `ExcludeAssets` requirement verified from plugin template and reference plugins; .NET 8 / 10.10.x pairing confirmed |
| Features | HIGH | Youtarr NFO schema confirmed from `nfoGenerator.js` source; image filenames confirmed from `videoDownloadPostProcessFiles.js`; folder layouts confirmed from `YOUTARR_DOWNLOADS_FOLDER_STRUCTURE.md` |
| Architecture | HIGH (with one MEDIUM caveat) | `SeriesResolver`/`EpisodeResolver` behavior verified against Jellyfin source. `ILocalMetadataProvider` pattern verified from two reference plugins. MEDIUM caveat: virtual season creation from `ParentIndexNumber` for flat layouts requires live-instance validation |
| Pitfalls | HIGH | Sourced from specific verified Jellyfin GitHub issues with numbers; `tubearchivist-jf-plugin` issue #78 verified from source |

**Overall confidence:** HIGH, with one critical validation requirement (virtual season mechanism) before Phase 3 architecture is finalized.

### Gaps to Address

- **Virtual season creation in flat-layout (CRITICAL):** Whether `SeriesMetadataService.CreateSeasonsAsync()` reliably creates virtual seasons from `Episode.ParentIndexNumber` in Jellyfin 10.10.7 when no physical season folders exist. Resolve with a minimal live-instance test at the start of Phase 3 implementation. Do not skip this.

- **Youtarr flat vs. nested layout detection:** Youtarr's `YOUTARR_SKIP_VIDEO_FOLDER` setting controls whether video files are in per-video subfolders or directly in the channel folder. The plugin's NFO sidecar discovery logic must handle both layouts. Confirm path resolution logic in Phase 3.

- **`poster.jpg` copy behavior default:** FEATURES.md notes `poster.jpg` is written to the channel folder only "if Youtarr's 'Copy channel poster.jpg' setting is on." Whether this defaults ON or OFF is unconfirmed. If OFF by default, artwork will be missing for many users. Document required Youtarr configuration; implement Series image provider defensively with graceful fallback.

- **`ParentIndexNumber` override priority (MEDIUM):** Issue #14080 documents that the provider's `ParentIndexNumber` can be silently discarded. For flat layouts with non-`SxxExx` filenames, the filename parser should produce no season/episode values — verify during Phase 3 testing.

- **YYYYMMDD same-day collision strategy:** Two videos uploaded the same day get identical `IndexNumber` values. Use `YYYYMMDDNN` compound scheme (appending within-day sequence counter) or sort-based sequential index. Decide in Phase 3 design; sequential approach requires channel-wide episode list at metadata time — benchmark for channels with 1000+ videos.

---

## Sources

### Primary (HIGH confidence)
- `Jellyfin.Controller 10.10.7` / `Jellyfin.Model 10.10.7` on NuGet — version, TFM, dependency graph
- `jellyfin/jellyfin` source: `SeriesResolver.cs`, `EpisodeResolver.cs`, `SeriesMetadataService.cs`, `EpisodeNfoParser.cs`, `LocalImageProvider.cs` — resolver behavior, season creation, NFO parser incompatibility
- `tubearchivist/tubearchivist-jf-plugin` source — `Providers/`, `TubeArchivist/Video/Video.cs`, `Utils/Utils.cs`, `Configuration/PluginConfiguration.cs` — reference implementation patterns
- `DialmasterOrg/Youtarr` source: `nfoGenerator.js`, `videoDownloadPostProcessFiles.js`, `YOUTARR_DOWNLOADS_FOLDER_STRUCTURE.md` — confirmed NFO schema, image filenames, folder layouts
- `ankenyr/jellyfin-youtube-metadata-plugin` source — `ILocalMetadataProvider` pattern for YouTube content
- `endrl/jellyfin-plugin-edl` `build.yaml` — confirmed `targetAbi: "10.10.0.0"`, `framework: "net8.0"`

### Secondary (MEDIUM confidence)
- Jellyfin GitHub issue #14080 — `ParentIndexNumber` override behavior in provider pipeline
- Jellyfin GitHub issue #11916 — Season folder naming regression (10.9.4+)
- Jellyfin GitHub issue #13197 — NFO `<season>` tag ignored (10.10.3+)
- Jellyfin GitHub issue #13358 — `SeasonName` broken in 10.10, `ParentIndexNumber` is correct mechanism
- Jellyfin DeepWiki metadata management — provider pipeline, MergeData (community doc, cross-referenced with source)

### Tertiary (LOW confidence)
- Jellyfin GitHub issue #12197, #13655 — Jellyfin overwriting custom NFO metadata (10.9.0+, 10.10.0+)
- Jellyfin GitHub issue #15804 — Duplicate season entries with title-prefixed folder names (10.11.4+) — not directly applicable to 10.10.x target

---
*Research completed: 2026-06-09*
*Ready for roadmap: yes*
