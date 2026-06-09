---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: executing
stopped_at: "01-03 harness built (docker-compose, fixtures, deploy-plugin.sh, README) and DLL staged. PENDING: Task 3 live human-verify — operator must start the Docker daemon (sudo) and run docker compose up to confirm plugin Active + MyChannel Series + __kids suppression."
last_updated: "2026-06-09T23:20:00.000Z"
last_activity: 2026-06-09 — 01-03 load-test harness ready (live in-Jellyfin verify pending Docker daemon start)
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
**Current focus:** Phase 1 — Scaffold + Series Proof

## Current Position

Phase: 1 of 4 (Scaffold + Series Proof)
Plan: 2 of 3 complete in current phase
Status: Executing
Last activity: 2026-06-09 — Completed 01-02 (path utils, __prefix ignore rule, Series provider; 24 tests green)

Progress: [███████░░░] 67%

## Performance Metrics

**Velocity:**

- Total plans completed: 2
- Average duration: ~8 min
- Total execution time: ~0.3 hours

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 01 | 2 of 3 | 16 min | 8 min |

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
Stopped at: Completed 01-02-PLAN.md (PathUtils + __prefix ignore rule + Series provider; 24 tests green). Next: 01-03 (live Jellyfin load verification).
Resume file: None
