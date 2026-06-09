# Youtarr Jellyfin Plugin

## What This Is

A Jellyfin plugin (C#/.NET, targeting Jellyfin 10.10.x) that turns a [Youtarr](https://github.com/DialmasterOrg/Youtarr) download folder into a clean, well-organized Jellyfin library. Instead of a flat wall of videos, each YouTube channel becomes a **Show (Series)**, each video becomes an **Episode**, and episodes are grouped into **seasons by upload year**. Channel artwork is surfaced as the Series poster/backdrop. It works entirely from the NFO files and images Youtarr writes to disk — no API key required.

It is modeled on [`tubearchivist-jf-plugin`](https://github.com/tubearchivist/tubearchivist-jf-plugin) but solves a different problem: TubeArchivist keeps metadata in its own database and writes nothing to disk, so its plugin *must* call an API. Youtarr already writes per-video `<movie>` NFOs, poster images, and embedded MP4 metadata, so this plugin can be file-only. The value it adds over Jellyfin's built-in NFO import is **structure** — grouping the flat collection into channels-as-shows with year seasons — not basic metadata reading.

## Core Value

A Youtarr download folder, pointed at by a Jellyfin library, shows up as channels-grouped Shows with year seasons and correct per-video metadata — not a flat undifferentiated wall of videos. If everything else fails, the channel→Show / video→Episode grouping must work.

## Requirements

### Validated

<!-- Shipped and confirmed valuable. -->

(None yet — ship to validate)

### Active

<!-- Current scope. Building toward these. Hypotheses until shipped. -->

- [ ] Each Youtarr channel folder is presented as a Jellyfin Show/Series
- [ ] Each downloaded video is presented as an Episode under its channel's Series
- [ ] Episodes are grouped into seasons by upload year (default ON; setting to flatten to a single season)
- [ ] Per-video metadata (title, plot/description, premiered/air date, runtime, genre, tags, studio/channel, YouTube ID) is read from Youtarr's NFO files
- [ ] Channel artwork (poster/banner/folder image) is shown as the Series poster and backdrop
- [ ] Plugin works file-only — no Youtarr API key or instance address required
- [ ] Plugin installs into Jellyfin 10.10.x via the standard plugin mechanism (manual DLL drop at minimum)
- [ ] Configuration page exposes the key options (e.g. year-seasons toggle, episode numbering scheme)

### Out of Scope

<!-- Explicit boundaries. Includes reasoning. -->

- Two-way watched-status / playlist sync back to Youtarr — deferred; needs an API and adds major complexity. Revisit only if file-only proves insufficient.
- Requiring a Youtarr instance address or API key — explicitly avoided; Youtarr writes everything needed to disk. An *optional* API connection may be reconsidered later only if it clearly improves the experience.
- Downloading or managing YouTube content — that is Youtarr's job, not the plugin's.
- Re-implementing Jellyfin's generic NFO reader — we layer structure on top of existing on-disk metadata, not replace metadata parsing wholesale.

## Context

- **Reference plugin:** `tubearchivist-jf-plugin` — C#/.NET 9, official Jellyfin plugin template, acts as a metadata provider; organizes channels as Shows and videos as episodes seasoned by year; requires a TubeArchivist address + API key because TubeArchivist exposes metadata only via API.
- **Youtarr:** self-hosted, Docker-based YouTube downloader. Organizes downloads by channel, supports custom subfolder grouping via prefixes (`__kids`, `__music`, `__news`) to create separate libraries. Generates NFO files, poster images, embedded MP4 metadata, and normalized content ratings (G/PG/PG-13/R/NC-17/TV-*). Supports Plex, Kodi, Jellyfin, Emby.
- **Sample Youtarr NFO** (the user's reference) is a Kodi-style `<movie>` document containing: `title`, `plot`, `uniqueid type="youtube"` + `youtubeid`, `premiered` date, `studio` (channel name), `credits`, `genre`, multiple `tag`s, `runtime`, `fileinfo/streamdetails/video/durationinseconds`, and a `trailer` backlink to YouTube in Kodi plugin format. Note: the video-level NFO uses the `<movie>` root, which Jellyfin maps to a Movie — turning these into Series/Season/Episode is the plugin's central technical challenge.
- **Jellyfin native behavior:** already reads `.nfo` + local images, but for a folder of channel subfolders it produces a flat Movies-style library; it does not natively model "channel = Show, year = Season" from Youtarr's layout. That gap is what the plugin fills.
- **Key open technical question for research:** how a Jellyfin 10.10 plugin restructures a channel/flat-video folder layout into Series/Season/Episode — whether via metadata/image providers, a custom library resolver, library content-type conventions, or a combination — and how `tubearchivist-jf-plugin` achieves the same mapping.

## Constraints

- **Tech stack**: C# / .NET (the Jellyfin plugin SDK is .NET-only) — non-negotiable for a Jellyfin plugin. Target the .NET version matching Jellyfin 10.10.x.
- **Compatibility**: Target Jellyfin 10.10.x (current stable); note a minimum supported version. Plugin must load via Jellyfin's plugin API for that release line.
- **Integration model**: File-only by default — read NFO/images/embedded metadata from the media folders. No network dependency on a running Youtarr instance.
- **Data source shape**: Bound by what Youtarr actually writes to disk (NFO schema, image filenames, folder layout). The plugin must map *Youtarr's* on-disk conventions, which differ from TubeArchivist's.
- **Distribution**: Personal use first, but architected cleanly so it can be published to a Jellyfin plugin repository later (proper versioning, plugin manifest, build/packaging, ideally CI).

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Channels-as-Shows, videos-as-Episodes content model | Solves the core "flat wall of videos" pain; matches `tubearchivist-jf-plugin`'s proven model and how people browse channels | — Pending |
| Season split by upload year, default ON (toggle to flatten) | Natural grouping for long-running channels; user explicitly wants it on by default | — Pending |
| File-only, no API key | Youtarr writes NFO + images + embedded metadata to disk; avoids coupling to a running Youtarr instance | — Pending |
| Channel art as Series poster/backdrop is in-scope (important) | Big part of making the library feel polished rather than utilitarian | — Pending |
| Target Jellyfin 10.10.x | Current stable; modern plugin API; right baseline for a new plugin | — Pending |
| Build personal-first but publishable | User wants both eventually; cleaner to bake in manifest/versioning early than retrofit | — Pending |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd-transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd:complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-06-09 after initialization*
