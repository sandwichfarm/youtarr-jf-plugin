---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: executing
stopped_at: "02-02 complete: YoutarrNfoParser + YoutarrVideoData DTO + PluginConfiguration fields (EpisodeNumberingScheme, MaxDescriptionLength) built TDD; full suite 49/49 green in Release. Built ahead of the live Docker checkpoints (01-03 live, 02-01 probe, 02-04 verify) which remain queued for when the operator starts the Docker daemon (sudo)."
last_updated: "2026-06-09T23:45:00.000Z"
last_activity: 2026-06-09 — 02-02 NFO parser/DTO + config contract layer complete (49/49 tests green); Docker checkpoints still pending daemon start
progress:
  total_phases: 4
  completed_phases: 0
  total_plans: 3
  completed_plans: 2
  percent: 67
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-06-09)

**Core value:** A Youtarr download folder shows up in Jellyfin as channels-grouped Shows with year seasons and correct per-video metadata — not a flat undifferentiated wall of videos.
**Current focus:** Phase 2 — Episodes + Year-Seasons (autonomous unit-testable layer; live Docker checkpoints queued)

## Current Position

Phase: 2 of 4 (Episodes + Year-Seasons)
Plan: 1 of 4 complete in current phase (02-02); 02-01/02-03/02-04 remaining
Status: Executing
Last activity: 2026-06-09 — Completed 02-02 (YoutarrNfoParser + YoutarrVideoData DTO + config fields; 49 tests green)

Progress: [███████░░░] 67%

> Note: 02-02 was built ahead of the Wave-0 live probe (02-01) per the orchestrator's
> sequencing note — the parser/DTO/config are pure logic fully covered by unit tests; only
> the virtual-season CONTAINER mechanism needs the live probe, which is independent of the
> ParentIndexNumber value this contract layer enables. Docker checkpoints (01-03, 02-01,
> 02-04) run together once the daemon is up.

## Performance Metrics

**Velocity:**

- Total plans completed: 3
- Average duration: ~9 min
- Total execution time: ~0.5 hours

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 01 | 2 of 3 | 16 min | 8 min |
| 02 | 1 of 4 | ~12 min | ~12 min |

**Recent Trend:**

- Last 5 plans: —
- Trend: —

*Updated after each plan completion*

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

Last session: 2026-06-09
Stopped at: Completed 02-02-PLAN.md (YoutarrNfoParser + YoutarrVideoData DTO + PluginConfiguration EpisodeNumberingScheme/MaxDescriptionLength; TDD; full suite 49/49 green in Release). Next: 02-03 (YoutarrEpisodeNfoProvider NFO→Episode mapping, consumes this DTO) — also pure/unit-testable, can proceed before Docker is up. Live Docker checkpoints (01-03, 02-01, 02-04) remain queued for the operator to run once the daemon is started (sudo).
Resume file: None
