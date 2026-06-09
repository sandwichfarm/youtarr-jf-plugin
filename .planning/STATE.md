# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-06-09)

**Core value:** A Youtarr download folder shows up in Jellyfin as channels-grouped Shows with year seasons and correct per-video metadata — not a flat undifferentiated wall of videos.
**Current focus:** Phase 1 — Scaffold + Series Proof

## Current Position

Phase: 1 of 4 (Scaffold + Series Proof)
Plan: 0 of TBD in current phase
Status: Ready to plan
Last activity: 2026-06-09 — Roadmap created; requirements mapped; ready for phase 1 planning

Progress: [░░░░░░░░░░] 0%

## Performance Metrics

**Velocity:**
- Total plans completed: 0
- Average duration: —
- Total execution time: 0 hours

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| - | - | - | - |

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
Stopped at: Roadmap written; REQUIREMENTS.md traceability updated; ready to run /gsd:plan-phase 1
Resume file: None
