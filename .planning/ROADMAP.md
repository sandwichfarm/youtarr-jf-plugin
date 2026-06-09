# Roadmap: Youtarr Jellyfin Plugin

## Overview

Four coarse phases take the plugin from a bare scaffold that loads in Jellyfin to a fully-packaged, installable release. Phase 1 proves the foundational architecture (plugin loads, channel folders are recognized as Series). Phase 2 is the highest-risk core: mapping Youtarr's flat video files to Episodes with year-seasons, validated against a live Jellyfin 10.10.x instance before the full pipeline is built. Phase 3 completes the visual experience with artwork and a polished configuration page. Phase 4 produces the installable artifact and a publishable manifest.

## Phases

**Phase Numbering:**

- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [ ] **Phase 1: Scaffold + Series Proof** - Plugin loads as Active; channel folders appear as named Series in a Shows library
- [ ] **Phase 2: Episodes + Year-Seasons** - Videos become Episodes grouped into year-seasons with full per-video metadata (highest-risk phase)
- [ ] **Phase 3: Artwork + Configuration Page** - Channel and episode artwork surfaced; configuration page fully functional in Jellyfin Dashboard
- [ ] **Phase 4: Packaging + Distribution** - Installable versioned ZIP and publishable manifest.json produced

## Phase Details

### Phase 1: Scaffold + Series Proof

**Goal**: Plugin loads cleanly into Jellyfin 10.10.x and channel folders in a Shows library are resolved as correctly-named Series
**Mode:** mvp
**Depends on**: Nothing (first phase)
**Requirements**: PLUG-01, LIB-01, SER-01, SER-02, CMP-03
**Success Criteria** (what must be TRUE):

  1. Plugin appears as "Active" in the Jellyfin Dashboard Plugins list with no load errors in the Jellyfin log
  2. In a Shows library pointed at the Youtarr download folder, each channel subfolder appears as a Series item with the channel name as the Series title
  3. A channel folder with a Youtarr `__prefix` grouping (e.g. `__kids/ChannelName`) resolves as a Series without errors and does not produce duplicate entries
  4. Series metadata is synthesized from the channel folder name even though no `tvshow.nfo` is present on disk

**Plans**: 3 plans

  - [x] 01-01-PLAN.md — Scaffold .NET 8 plugin project + entry point + build.yaml; DLL builds and is publish-clean (PLUG-01)
  - [x] 01-02-PLAN.md — Series provider + __prefix ignore rule + unit tests (LIB-01, SER-01, SER-02, CMP-03)
  - [ ] 01-03-PLAN.md — End-to-end Docker load test against real Jellyfin 10.10.7 + live verification checkpoint

### Phase 2: Episodes + Year-Seasons

**Goal**: Every downloaded video file is presented as an Episode under its channel's Series, grouped into seasons by upload year, with all per-video metadata populated from the `<movie>` NFO
**Mode:** mvp
**Depends on**: Phase 1
**Requirements**: LIB-02, LIB-03, LIB-04, EPI-01, EPI-02, EPI-03, EPI-04, EPI-05, EPI-06, EPI-07, CMP-01, CMP-02
**Success Criteria** (what must be TRUE):

  1. VALIDATE FIRST on a live Jellyfin 10.10.x instance: a minimal stub Episode provider that sets `ParentIndexNumber = 2024` causes a "Season 2024" container to appear in the UI for a flat-layout channel folder — this must be confirmed before building the full pipeline
  2. Each video file in a flat channel folder (default Youtarr layout, `YOUTARR_SKIP_VIDEO_FOLDER=true`) appears as an Episode with title, plot/description, premiere date, and runtime populated from the `<movie>` NFO
  3. Each video file in a per-video subfolder layout appears as an Episode in the same way
  4. Episodes are grouped under "Season YYYY" containers reflecting the video's upload year by default; disabling year-seasons in configuration causes all episodes to appear under a single season
  5. Episode numbers are derived from the upload date (selectable: default sequential vs. YYYYMMDD scheme); each episode receives a stable, unique number
  6. Genre, tags, and content rating (`<mpaa>`) from the NFO are visible on the Episode detail page
  7. The YouTube video ID is recorded on the Episode and visible as a provider ID in the metadata editor
  8. A video with a missing or invalid upload date does not crash the library scan and is placed in a fallback season

**Plans**: 4 plans

  - [ ] 02-01-PLAN.md — Wave 0 live virtual-season probe: stub provider + episode/year fixtures + human-verify checkpoint (LIB-02, LIB-03)
  - [ ] 02-02-PLAN.md — YoutarrNfoParser + YoutarrVideoData DTO + config fields (numbering scheme, max length); TDD (EPI-01..07, LIB-04)
  - [ ] 02-03-PLAN.md — YoutarrEpisodeNfoProvider NFO→Episode mapping + FindNfoForVideo; TDD (LIB-02/03/04, EPI-01..07, CMP-01/02)
  - [ ] 02-04-PLAN.md — Replace stub with real provider; full-pipeline live verification (LIB-02/03/04, EPI-01, EPI-07, CMP-01/02)

### Phase 3: Artwork + Configuration Page

**Goal**: Channel artwork is shown as the Series poster and backdrop; per-video thumbnails appear as Episode images; the plugin configuration page in the Jellyfin Dashboard exposes all user-facing settings
**Mode:** mvp
**Depends on**: Phase 2
**Requirements**: ART-01, ART-02, ART-03, ART-04, PLUG-03
**Success Criteria** (what must be TRUE):

  1. The channel `poster.jpg` is displayed as the Series primary (poster) image in the library grid
  2. The channel image is also shown as the Series backdrop on the Series detail page
  3. The per-video thumbnail (e.g. `video-title.jpg` sidecar) is shown as the Episode primary image
  4. A channel folder with no `poster.jpg` and an Episode with no thumbnail image both display without errors and fall back gracefully to no image
  5. The Jellyfin Dashboard configuration page for the plugin shows controls for: year-seasons toggle, episode numbering scheme (Default vs. YYYYMMDD), and maximum description length; saving changes persists them across server restarts

**Plans**: 3 plans

  - [ ] 03-01-PLAN.md — YoutarrSeriesImageProvider (poster.jpg → Series Backdrop) + DI + unit tests; TDD (ART-02, ART-04)
  - [ ] 03-02-PLAN.md — Config page: 3 controls wired to ApiClient + PluginConfiguration fields (PLUG-03)
  - [ ] 03-03-PLAN.md — Live artwork + config verification: image fixtures + Docker harness + human-verify checkpoint (ART-01, ART-02, ART-03, ART-04, PLUG-03)

**UI hint**: yes

### Phase 4: Packaging + Distribution

**Goal**: The plugin can be installed by dropping a downloaded ZIP into Jellyfin's plugins directory, and a manifest.json is ready for submission to a plugin repository
**Mode:** mvp
**Depends on**: Phase 3
**Requirements**: PLUG-02, PKG-01, PKG-02
**Success Criteria** (what must be TRUE):

  1. Running the build produces a versioned ZIP file (e.g. `youtarr-jf-plugin_1.0.0.0.zip`) that installs correctly when dropped into the Jellyfin plugins directory and the server is restarted
  2. A `manifest.json` is generated alongside the ZIP with correct plugin name, GUID, version, targetAbi, and changelog fields in the format required by Jellyfin's plugin repository specification

**Plans**: TBD

## Progress

**Execution Order:**
Phases execute in numeric order: 1 → 2 → 3 → 4

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Scaffold + Series Proof | 2/3 | In Progress|  |
| 2. Episodes + Year-Seasons | 0/4 | Not started | - |
| 3. Artwork + Configuration Page | 0/3 | Not started | - |
| 4. Packaging + Distribution | 0/TBD | Not started | - |
