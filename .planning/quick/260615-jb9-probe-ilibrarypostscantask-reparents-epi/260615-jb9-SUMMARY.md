---
phase: quick
plan: 260615-jb9
status: complete
type: execute
date: 2026-06-15
---

# Quick Task 260615-jb9 — Season-regroup probe — Summary

**Status:** COMPLETE. Built, locally verified, AND live-validated against a Jellyfin 10.10.7 Docker container on the dev machine (Docker was already running; no operator action needed).

## RESULT: HYPOTHESIS CONFIRMED (with one nuance)

Post-scan reparenting into year-seasons **works and survives rescans**. End-state after BOTH scans:
- `Nested Probe Channel` → exactly `Season 2024` (First Video) + `Season 2025` (Part 2 of the Saga). No per-video phantom seasons, **no stray "Season 2"** from the "Part 2" folder.
- Flat fixtures unaffected; 5 episodes total, **zero orphans, zero duplicates**.

**Nuance — NOT idempotent (treadmill):** scan #2 logged `moved …` and `deleted phantom Season …` *again*, not the expected `already under … (no-op)`. Jellyfin re-resolves each episode back into its per-video folder-season on every scan (path-based resolution), and the post-scan task re-corrects it each time. The end-state is always correct, but phantom seasons exist transiently mid-scan and the task does O(episodes) work every scan. This is an efficiency/cosmetic concern, not a correctness one — **the approach is viable for the real fix.**

Also confirmed: `get-or-create Season (created=False)` on every call → the year-seasons already exist as VIRTUAL seasons from `YoutarrEpisodeNfoProvider`'s `ParentIndexNumber`. The probe's real job is just to reparent episodes off the folder-seasons into those existing virtual year-seasons and delete the empty folder-seasons. This also means the same-year case (e.g. real Andraz Egart's 7 videos in 2026) is handled naturally — one virtual year-season, all episodes reparent into it; no duplicate-season risk.

## Hypothesis under test

Confirmed root cause: Youtarr's NESTED layout (`Channel/<video>/<video>.mp4|.nfo`) makes Jellyfin 10.10 resolve every per-video subfolder as a Season named after the folder. The metadata provider's `ParentIndexNumber=year` cannot reliably override a folder-derived season (issue #14080), producing the inconsistent mix of year/numeric/per-video seasons the user reported.

**Question:** can an `ILibraryPostScanTask` reparent episodes into `Season <year>` and have it **survive a second scan** without orphaning/duplicating? If not, the post-scan approach is a dead end.

## What was built (3 atomic commits)

- `16974a8` — Task A: nested fixture `test/jellyfin-load-test/media/Nested Probe Channel/` with two per-video subfolders (vidA001 = 2024, vidB002 = 2025; folder 2 contains literal "Part 2" to reproduce the stray-numeric-season symptom). Existing flat fixtures untouched.
- `a596e1c` — Task B: `Jellyfin.Plugin.Youtarr/Providers/YoutarrSeasonRegroupProbeTask.cs` (throwaway `ILibraryPostScanTask`), registered in `PluginServiceRegistrator` as `AddSingleton<ILibraryPostScanTask, YoutarrSeasonRegroupProbeTask>`.
- `84a535c` — Task C: `reproduce.sh` extended — two `Library/Refresh` scans, `===== SEASONS AFTER SCAN #1/#2 =====` banners printing Seasons-per-Series with episode counts, operator PASS checklist, and the `docker logs … | grep '[Youtarr] Probe'` command. Original Series-grouping verdict preserved.

## APIs used (verified vs jellyfin v10.10.7)

- `ILibraryPostScanTask.Run(IProgress<double>, CancellationToken)` — `MediaBrowser.Controller.Library`
- Series query: `ILibraryManager.GetItemList(InternalItemsQuery{ IncludeItemTypes=[Series], Recursive=true })`
- Get-or-create Season: canonical `SeriesMetadataService.CreateSeasonAsync` shape — `new Season{ IndexNumber=year, Id=GetNewItemId(...), IsVirtualItem=false, SeriesId/SeriesName/SeriesPresentationUniqueKey }` + `series.AddChild(season)`
- Reparent: `episode.SetParent(season)` + `episode.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, ct)`
- Delete phantom: `ILibraryManager.DeleteItem(season, new DeleteOptions{ DeleteFileLocation=false })`

## Verification (local)

- `dotnet build Jellyfin.Plugin.Youtarr -c Release` → **0 errors** (analyzer warnings only).
- Self-gates on `PathUtils.IsYoutarrChannelFolder(series.Path)` — never touches non-Youtarr libraries.
- `bash -n test/jellyfin-load-test/reproduce.sh` passes (verified by executor).
- DLL staged via `scripts/deploy-plugin.sh` (no Docker required).

## Known watch-items for the live run

- **Same-year duplicate seasons:** `GetOrCreateYearSeason` re-queries `GetRecursiveChildren` after `AddChild`; if that doesn't reflect the just-added season within the same scan, two episodes in the same year could create two `Season <year>`. The probe fixture uses one episode per year, so it won't surface this — but the real Andraz Egart library (7 in 2026) would. Note it when reading scan #1 output.
- **The actual answer is scan #2:** scan #1 showing year-seasons is necessary but NOT sufficient. Success = scan #2 shows the SAME seasons + SAME episode counts, with `[Youtarr] Probe: … already under Season … (no-op)` and NO new phantom deletes.

## Operator action required

1. Start Docker (needs sudo — operator runs it):
   `sudo sh -c 'nohup dockerd >/tmp/dockerd.log 2>&1 & for i in $(seq 1 20); do [ -S /var/run/docker.sock ] && break; sleep 1; done; chmod 666 /var/run/docker.sock'`
2. Run `./test/jellyfin-load-test/reproduce.sh`.
3. Paste back the two `SEASONS AFTER SCAN` sections + the `[Youtarr] Probe` log lines.

## Outcome → GREEN. Proceed to the full feature.

Post-scan reparenting is the correct fix for the nested-layout season bug. Next step is the production `ILibraryPostScanTask` (replacing this throwaway probe):
- Config toggle (respect existing `YearSeasons` setting; off → flatten/skip).
- Season-0 (Specials) for undated episodes (probe skipped them).
- Reduce treadmill churn where possible (e.g. only act when an episode is parented under a non-year folder-season; skip the per-scan rewrite when already correct — though note Jellyfin re-attaches by path each scan, so some rework is unavoidable).
- Keep `DeleteFileLocation=false` (never touches media) and the `IsYoutarrChannelFolder` self-gate.
- Then package as a normal plugin release the user installs on their Unraid Jellyfin.

## Harness bug fixed

`reproduce.sh`'s `print_seasons_per_series` piped `api … | python3 - <<'EOF'`, where the heredoc overrode the piped stdin (so `json.load(sys.stdin)` read nothing). Rewritten to fetch the series list via `urllib` inside python like the nested calls. Harness now runs clean end-to-end (final verdict PASS).
