# Requirements: Youtarr Jellyfin Plugin

**Defined:** 2026-06-09
**Core Value:** A Youtarr download folder shows up in Jellyfin as channels-grouped Shows with year seasons and correct per-video metadata — not a flat undifferentiated wall of videos.

## v1 Requirements

Requirements for the initial release. Each maps to a roadmap phase.

### Plugin & Install (PLUG)

- [x] **PLUG-01**: Plugin loads as "Active" in a Jellyfin 10.10.x dashboard with no load errors
- [ ] **PLUG-02**: User can install the plugin by dropping a downloadable ZIP/DLL into the Jellyfin plugins directory
- [ ] **PLUG-03**: User can open a plugin configuration page in the Jellyfin Dashboard that surfaces all plugin settings

### Library Structure (LIB)

- [ ] **LIB-01**: Each Youtarr channel folder appears as a Series in a Jellyfin "Shows" library
- [ ] **LIB-02**: Each downloaded video appears as an Episode under its channel's Series
- [ ] **LIB-03**: Episodes are grouped into seasons by upload year by default (e.g. "Season 2025")
- [ ] **LIB-04**: User can turn year-seasons off to collapse a channel's episodes into a single season

### Episode Metadata (EPI)

- [ ] **EPI-01**: Episode title, plot/description, premiered (air) date, and runtime are read from Youtarr's `<movie>` NFO
- [ ] **EPI-02**: Genres and tags from the NFO are carried onto the Episode
- [ ] **EPI-03**: The YouTube video ID is recorded as a provider ID on the Episode
- [ ] **EPI-04**: Episode descriptions are truncated to a configurable maximum length (default 500 chars)
- [ ] **EPI-05**: Each Episode receives a stable episode number, with a selectable scheme (date-ordered default vs `YYYYMMDD`)
- [ ] **EPI-06**: When the NFO contains a content rating (`<mpaa>`), it is applied to the Episode
- [ ] **EPI-07**: Videos with missing or invalid upload dates are handled gracefully (no crash; sensible season/number fallback)

### Series Metadata (SER)

- [ ] **SER-01**: Series name is derived from the channel (folder name, falling back to the NFO `<studio>` field)
- [ ] **SER-02**: Series metadata is synthesized even though Youtarr writes no `tvshow.nfo` (built-in Series NFO provider finds nothing)

### Artwork (ART)

- [ ] **ART-01**: The channel `poster.jpg` is shown as the Series primary image (poster)
- [ ] **ART-02**: The channel image is also surfaced as the Series backdrop
- [ ] **ART-03**: The per-video thumbnail (`<videofilename>.jpg`) is shown as the Episode primary image
- [ ] **ART-04**: Missing artwork degrades gracefully (no errors when `poster.jpg` or a thumbnail is absent)

### Compatibility (CMP)

- [ ] **CMP-01**: Plugin works with Youtarr's default flat per-channel folder layout
- [ ] **CMP-02**: Plugin works with Youtarr's nested per-video subfolder layout
- [ ] **CMP-03**: Youtarr `__prefix` grouping subfolders do not break Series resolution

### Packaging (PKG)

- [ ] **PKG-01**: Build produces a versioned plugin ZIP suitable for manual install
- [ ] **PKG-02**: A plugin-repository `manifest.json` is generated so the plugin can be published later

## v2 Requirements

Deferred to a future release. Tracked but not in the current roadmap.

### Youtarr Sync (SYNC)

- **SYNC-01**: Two-way watched-status sync between Jellyfin and Youtarr (requires Youtarr API)
- **SYNC-02**: Playlist sync between Jellyfin and Youtarr (requires Youtarr API)

### Enriched Metadata (ENR)

- **ENR-01**: Channel description shown as the Series overview (needs a channel-level source not currently on disk)

### Distribution (DIST)

- **DIST-01**: CI pipeline auto-builds and publishes a release + manifest on tagged push

## Out of Scope

Explicitly excluded. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| Requiring a Youtarr API key or instance address for core function | File-only by design — Youtarr writes NFO + images + embedded metadata to disk. An optional connection may be reconsidered only for v2 sync features. |
| Downloading or managing YouTube content | That is Youtarr's responsibility, not the plugin's. |
| Custom `IItemResolver` to classify items | Officially unsupported in Jellyfin 10.10; causes duplicate entries and breaks on updates. Use a "Shows" library + built-in resolvers instead. |
| Re-implementing Jellyfin's generic NFO/movie parsing wholesale | The plugin layers a `<movie>`→Episode mapper on top of existing on-disk metadata; it does not replace Jellyfin's parsing stack. |
| Renaming or moving the user's media files on disk | The plugin only reads files; it must never mutate the user's Youtarr library. |

## Traceability

Which phases cover which requirements. Populated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| PLUG-01 | Phase 1 | Complete |
| PLUG-02 | Phase 4 | Pending |
| PLUG-03 | Phase 3 | Pending |
| LIB-01 | Phase 1 | Pending |
| LIB-02 | Phase 2 | Pending |
| LIB-03 | Phase 2 | Pending |
| LIB-04 | Phase 2 | Pending |
| EPI-01 | Phase 2 | Pending |
| EPI-02 | Phase 2 | Pending |
| EPI-03 | Phase 2 | Pending |
| EPI-04 | Phase 2 | Pending |
| EPI-05 | Phase 2 | Pending |
| EPI-06 | Phase 2 | Pending |
| EPI-07 | Phase 2 | Pending |
| SER-01 | Phase 1 | Pending |
| SER-02 | Phase 1 | Pending |
| ART-01 | Phase 3 | Pending |
| ART-02 | Phase 3 | Pending |
| ART-03 | Phase 3 | Pending |
| ART-04 | Phase 3 | Pending |
| CMP-01 | Phase 2 | Pending |
| CMP-02 | Phase 2 | Pending |
| CMP-03 | Phase 1 | Pending |
| PKG-01 | Phase 4 | Pending |
| PKG-02 | Phase 4 | Pending |

**Coverage:**

- v1 requirements: 25 total
- Mapped to phases: 25 (roadmap complete)
- Unmapped: 0 ✓

---
*Requirements defined: 2026-06-09*
*Last updated: 2026-06-09 after roadmap creation — all 25 v1 requirements mapped*
