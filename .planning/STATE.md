---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: executing
stopped_at: "Completed 03-02-PLAN.md (PLUG-03 config page: configPage.html fleshed out with three controls — YearSeasons checkbox, EpisodeNumberingScheme select Default/YYYYMMDD, MaxDescriptionLength number — wired to ApiClient.get/updatePluginConfiguration via GUID; enum bound by name, parseInt for int; Task 1 PluginConfiguration fields already present, idempotent no-op; Release build + 85/85 tests green). Next: 03-03 (artwork + config live verify, Docker) which proves PLUG-03/ART-02/ART-04 end-to-end; plus still-queued 01-03 / 02-01 / 02-04 Docker checkpoints (operator must start docker via sudo)."
last_updated: "2026-06-09T22:14:40.452Z"
last_activity: 2026-06-09
progress:
  total_phases: 4
  completed_phases: 1
  total_plans: 12
  completed_plans: 7
  percent: 58
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-06-09)

**Core value:** A Youtarr download folder shows up in Jellyfin as channels-grouped Shows with year seasons and correct per-video metadata — not a flat undifferentiated wall of videos.
**Current focus:** Phase 3 — Artwork + Configuration Page (custom artwork code done; config page + live verify remaining)

## Current Position

Phase: 3 of 4 (Artwork + Configuration Page)
Plan: 2 of 3 complete in current phase (03-01, 03-02); 03-03 (live verify, Docker) remaining. Phase 1/2 live Docker checkpoints (01-03, 02-01, 02-04) also still queued.
Status: Ready to execute
Last activity: 2026-06-09

Progress: [██████░░░░] 58%

> Note: 02-02 was built ahead of the Wave-0 live probe (02-01) per the orchestrator's
> sequencing note — the parser/DTO/config are pure logic fully covered by unit tests; only
> the virtual-season CONTAINER mechanism needs the live probe, which is independent of the
> ParentIndexNumber value this contract layer enables. Docker checkpoints (01-03, 02-01,
> 02-04) run together once the daemon is up.

## Performance Metrics

**Velocity:**

- Total plans completed: 5
- Average duration: ~8 min
- Total execution time: ~0.7 hours

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 01 | 2 of 3 | 16 min | 8 min |
| 02 | 2 of 4 | ~22 min | ~11 min |
| 03 | 2 of 3 | ~9 min | ~5 min |

**Recent Trend:**

- Last 5 plans: —
- Trend: —

*Updated after each plan completion*
| Phase 03 P02 | ~4 min | 2 tasks | 1 files |

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work:

- Init: Use "Shows" library type + built-in SeriesResolver; no custom IItemResolver (officially unsupported in 10.10.x)
- Init: Target Youtarr flat layout (YOUTARR_SKIP_VIDEO_FOLDER=true) as primary; nested layout as secondary
- Init: Virtual seasons via Episode.ParentIndexNumber — MUST validate against live 10.10.x instance at Phase 2 start before building full pipeline
- 01-01: Permanent plugin GUID is **80302d7f-7fc3-4b1c-9a3f-fd85b98b9a69** (in Plugin.cs StaticId + build.yaml). Reused by 01-02, 01-03, Phase 4. Never change it.
- 01-01: DI registration in Jellyfin 10.10.x uses a separate IPluginServiceRegistrator class, NOT a BasePlugin.RegisterServices override (the override does not exist). Wiring deferred to 01-02.
- 01-02: __prefix ignore rule wired via PluginServiceRegistrator : IPluginServiceRegistrator (AddSingleton<IResolverIgnoreRule, YoutarrPrefixIgnoreRule>).
- 01-02: IResolverIgnoreRule lives in MediaBrowser.Controller.Resolvers (reflection-verified), not MediaBrowser.Controller.Library as the RESEARCH skeleton's using stated.
- 01-02: Test project references Jellyfin.Controller/Model WITHOUT ExcludeAssets so MediaBrowser runtime assemblies load at test time (plugin keeps ExcludeAssets; publish stays clean).
- 01-02: No custom provider Order set — confirm in 01-03 that YoutarrSeriesNfoProvider wins over built-in SeriesNfoProvider; add Order=0 if it doesn't.
- 01-02 (open for 01-03): Assumption A1 (FileSystemMetadata.Name == bare dir name) still needs live confirmation; one-line fallback to Path.GetFileName(FullName) if wrong.
- 02-02: NFO parsing isolated from Jellyfin types (same pattern as PathUtils) — YoutarrNfoParser.Parse is pure (file path in, YoutarrVideoData out, no Plugin.Instance), so 02-03's MapToEpisode/FormatDescription can be tested as static methods taking an explicit PluginConfiguration.
- 02-02: Parser surfaces XmlException on malformed XML (does NOT swallow) — 02-03's YoutarrEpisodeNfoProvider must wrap Parse in try/catch and set HasMetadata=false on failure. Non-<movie> roots return null.
- 02-02: EPI-07 date guard (IsValidYouTubeDate >=2005, <=now+2) applied to <premiered> only; <dateadded> captured raw on the DTO so 02-03 can implement premiered -> dateadded -> Season 0 fallback.
- 02-02: DTO Genres/Tags typed IReadOnlyList<string> (init-only) to stay CA2227-clean; 02-03 converts to arrays via .ToArray() as the research MapToEpisode skeleton already does.
- 02-02: EPI-01..07 / LIB-04 NOT yet marked complete in REQUIREMENTS — the parser/config is the contract layer; these requirements are satisfied end-to-end only once 02-03 maps the DTO onto Episode and 02-04 verifies live.
- 02-03: MapToEpisode/FormatDescription are internal static taking an explicit PluginConfiguration ([assembly: InternalsVisibleTo] in new Properties/AssemblyInfo.cs); GetMetadata is a thin wrapper over Plugin.Instance?.Configuration — mapping logic unit-tested with no running server.
- 02-03: EPI-07 chain realized as premiered→dateadded→Season 0 — when <premiered> invalid/missing but DateAdded valid, PremiereDate=DateAdded (episode keeps a date) while ParentIndexNumber=0/IndexNumber=null route the SEASON to Season 0. Download date never sets the year (T-02-10).
- 02-03: No explicit DI registration for YoutarrEpisodeNfoProvider — auto-discovery (Pitfall 7), same as YoutarrSeriesNfoProvider. 02-04 live probe decides whether explicit registration / provider Order is needed.
- 02-03: EPI/LIB/CMP requirements STILL marked Pending in REQUIREMENTS — unit-proven here but checked off only after 02-04 live verify confirms ParentIndexNumber/virtual-season behaviour end-to-end (continuing the 02-02 deferral decision).
- 03-01: ART-01 (Series Primary) and ART-03 (Episode thumbnail) need NO custom code — the built-in LocalImageProvider / EpisodeLocalImageProvider already cover them. The ONLY custom artwork class is YoutarrSeriesImageProvider for ART-02 (Series Backdrop), since Youtarr writes no fanart/backdrop file.
- 03-01: YoutarrSeriesImageProvider returns poster.jpg as ImageType.Backdrop ONLY — never Primary (built-in owns Primary at Order=0; re-emitting risks rescan flicker, Pitfall 1). Asserted by GetImages_SeriesWithPoster_NeverEmitsPrimary.
- 03-01: GetImages yield-breaks (empty enumerable, LogDebug, no throw) when poster.jpg absent (ART-04). LocalImageInfo.FileInfo = new FileSystemMetadata { FullName = posterPath } (FullName is all the pipeline needs, mirrors built-in EpisodeLocalImageProvider).
- 03-01: Explicit AddSingleton<ILocalImageProvider, YoutarrSeriesImageProvider>() added as a conservative DI safety net (mirrors IResolverIgnoreRule precedent); no custom Order. 03-03 live verify decides whether auto-discovery makes it redundant / whether an Order is needed.
- 03-01: ART-02/ART-04 left Pending in REQUIREMENTS — unit-proven here, checked off only after 03-03 live artwork verify (continuing the 02-02/02-03 deferral convention). Portrait poster as 16:9 backdrop is an accepted, documented visual tradeoff.
- 03-02: Dashboard config page (PLUG-03) code-complete — configPage.html exposes YearSeasons checkbox, EpisodeNumberingScheme select (Default/YYYYMMDD), MaxDescriptionLength number input, wired to ApiClient.getPluginConfiguration/updatePluginConfiguration with GUID 80302d7f-7fc3-4b1c-9a3f-fd85b98b9a69. No Plugin.cs/csproj change (GetPages + EmbeddedResource already in place); only the HTML body replaced. Release build + 85/85 tests green.
- 03-02: EpisodeNumberingScheme bound by enum NAME (option values "Default"/"YYYYMMDD"), not integer — the enum serializes to config XML by name; integer option values would fail deserialization and silently reset the setting (Pitfall 4). MaxDescriptionLength wrapped in parseInt(value, 10) on save so the server receives an int, not a string (Pitfall 5 / threat T-03-01). Added emby-select to data-require.
- 03-02: Task 1 (PluginConfiguration fields) was an idempotent no-op — YearSeasons/EpisodeNumberingScheme/MaxDescriptionLength already existed on disk from prior Phase 2 work with the exact required names/defaults, so they were left untouched (no commit for Task 1). PLUG-03 REQUIREMENTS checkbox left Pending until live persistence-across-restart is proven in 03-03 (Docker), matching the 03-01/02-02 deferral convention.

### Pending Todos

None yet.

### Blockers/Concerns

- **CRITICAL (Phase 2):** Whether Jellyfin 10.10.x CreateSeasonsAsync() reliably creates virtual seasons from ParentIndexNumber in a flat folder layout is unconfirmed. Must validate with a minimal stub (one provider returning ParentIndexNumber=2024) on a live instance BEFORE building the full Episode pipeline. If broken, fallback is a post-scan task — but do not implement until confirmed broken.
- **Setup note:** "Save metadata to media folders" must be disabled on Youtarr libraries or Jellyfin will overwrite NFO files (issue #12197).

## Deferred Items

| Category | Item | Status | Deferred At |
|----------|------|--------|-------------|
| *(none)* | | | |

## Session Continuity

Last session: 2026-06-09T22:14:35.142Z
Stopped at: Completed 03-02-PLAN.md (PLUG-03 config page: configPage.html fleshed out with three controls — YearSeasons checkbox, EpisodeNumberingScheme select Default/YYYYMMDD, MaxDescriptionLength number — wired to ApiClient.get/updatePluginConfiguration via GUID; enum bound by name, parseInt for int; Task 1 PluginConfiguration fields already present, idempotent no-op; Release build + 85/85 tests green). Next: 03-03 (artwork + config live verify, Docker) which proves PLUG-03/ART-02/ART-04 end-to-end; plus still-queued 01-03 / 02-01 / 02-04 Docker checkpoints (operator must start docker via sudo).
Resume file: None
