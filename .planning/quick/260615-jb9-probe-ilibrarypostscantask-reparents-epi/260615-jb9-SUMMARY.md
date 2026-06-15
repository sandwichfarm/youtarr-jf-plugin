---
phase: quick
plan: 260615-jb9
status: incomplete
type: execute
date: 2026-06-15
---

# Quick Task 260615-jb9 — Season-regroup probe — Summary

**Status:** Built & verified locally; **awaiting live operator validation** (the hypothesis is only answered by a live two-scan run). Marked `incomplete` until the operator pastes back logs.

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

## Outcome

TBD — pending the operator paste. If scan #2 survives → proceed to the full feature (config toggle, Season-0 fallback, artwork, same-year dedupe). If not → abandon post-scan reparenting and pivot.
