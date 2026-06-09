---
phase: 02-episodes-year-seasons
plan: 03
subsystem: episode-nfo-provider
tags: [csharp, jellyfin, episode, metadata-provider, year-seasons, tdd, nfo]
status: complete
requires:
  - "YoutarrNfoParser.Parse + YoutarrVideoData DTO (02-02)"
  - "PluginConfiguration.YearSeasons / EpisodeNumberingScheme / MaxDescriptionLength (02-02)"
  - "YoutarrSeriesNfoProvider ILocalMetadataProvider pattern + Constants (01-02)"
  - "PathUtils helper style (01-02)"
provides:
  - "Jellyfin.Plugin.Youtarr/Providers/YoutarrEpisodeNfoProvider.cs — ILocalMetadataProvider<Episode> mapping YoutarrVideoData -> Episode (internal static MapToEpisode/FormatDescription, unit-testable)"
  - "PathUtils.FindNfoForVideo — same-basename .nfo sidecar resolver (flat CMP-01 + nested CMP-02)"
  - "Properties/AssemblyInfo.cs — InternalsVisibleTo(Jellyfin.Plugin.Youtarr.Tests)"
  - "YoutarrEpisodeNfoProviderTests (27 cases) + 4 FindNfoForVideo cases"
affects:
  - "02-04 live verify confirms ParentIndexNumber/virtual-season behaviour end-to-end and decides explicit DI registration"
  - "Phase 3 (artwork/config) Episode items now carry full metadata for image/UI work"
tech-stack:
  added: []
  patterns:
    - "Config-dependent mapping extracted to internal static methods taking an explicit PluginConfiguration; GetMetadata is a thin wrapper over Plugin.Instance.Configuration — unit-testable without a running server"
    - "RunTimeTicks computed via TimeSpan.FromSeconds/FromMinutes (long arithmetic) — never a manual ticks multiply (Pitfall 4 / T-02-11)"
    - "Same-basename Path.ChangeExtension(video, .nfo) resolves both flat and nested Youtarr layouts identically"
    - "Malformed/missing/non-movie NFO degrades to HasMetadata=false, never throws (T-02-08)"
key-files:
  created:
    - "Jellyfin.Plugin.Youtarr/Providers/YoutarrEpisodeNfoProvider.cs"
    - "Jellyfin.Plugin.Youtarr/Properties/AssemblyInfo.cs"
    - "Jellyfin.Plugin.Youtarr.Tests/Providers/YoutarrEpisodeNfoProviderTests.cs"
  modified:
    - "Jellyfin.Plugin.Youtarr/Utils/PathUtils.cs (added FindNfoForVideo)"
    - "Jellyfin.Plugin.Youtarr.Tests/Utils/PathUtilsTests.cs (added 4 FindNfoForVideo cases)"
decisions:
  - "MapToEpisode/FormatDescription are internal static taking an explicit PluginConfiguration (per plan-checker testability guidance); GetMetadata reads Plugin.Instance?.Configuration ?? new PluginConfiguration() and delegates. Added [assembly: InternalsVisibleTo] in a new AssemblyInfo.cs so tests exercise the mapping directly with no Plugin.Instance."
  - "EPI-07 fallback realizes premiered -> dateadded -> Season 0: when <premiered> is invalid/missing but DateAdded is valid, PremiereDate is set from DateAdded (episode still has a date) while ParentIndexNumber=0 and IndexNumber=null route the SEASON to Season 0. Only when BOTH are missing: PremiereDate=null too. The download date never influences the year/season."
  - "No explicit DI registration added (auto-discovery default, Pitfall 7) — consistent with how YoutarrSeriesNfoProvider is registered. 02-04's live probe decides whether explicit registration is needed."
  - "GetMetadata uses Path.ChangeExtension directly (identical resolution to PathUtils.FindNfoForVideo); FindNfoForVideo is a reusable pure helper covered by its own tests."
requirements: [LIB-02, LIB-03, LIB-04, EPI-01, EPI-02, EPI-03, EPI-04, EPI-05, EPI-06, EPI-07, CMP-01, CMP-02]
metrics:
  duration: "~10 min"
  completed: "2026-06-10"
  tasks: "2 of 2"
  files: 5
---

# Phase 2 Plan 03: YoutarrEpisodeNfoProvider (NFO → Episode) Summary

The unit-tested core of Phase 2: an `ILocalMetadataProvider<Episode>` that discovers the
co-located `<movie>` NFO, parses it with `YoutarrNfoParser` (02-02), and maps the DTO onto a
Jellyfin `Episode` — title, truncated plot, premiere date, runtime, genres, tags, YouTube
provider id, content rating — with year-based season assignment + the LIB-04 flatten toggle,
the EPI-05 numbering schemes, and the EPI-07 `premiered → dateadded → Season 0` date fallback.
Plus `PathUtils.FindNfoForVideo` for flat/nested sidecar discovery. Built TDD (RED test commit
precedes each GREEN implementation). Full suite **80/80 green** in Release; publish stays
DLL-only.

## What Was Built

| File | Provides |
|------|----------|
| `Providers/YoutarrEpisodeNfoProvider.cs` | `ILocalMetadataProvider<Episode>, IHasItemChangeMonitor`. `GetMetadata` resolves the same-basename `.nfo`, parses inside try/catch, maps via `internal static MapToEpisode(data, config)`; sets `HasMetadata=true` only on success. `HasChanged` returns `File.GetLastWriteTimeUtc(nfo) > item.DateLastSaved`. `FormatDescription` is `internal static`. |
| `Utils/PathUtils.cs` (+`FindNfoForVideo`) | `public static string? FindNfoForVideo(string? videoPath)` → `Path.ChangeExtension(videoPath, ".nfo")` when `File.Exists`, else null; null/empty/whitespace tolerant. Resolves flat (CMP-01) and nested (CMP-02) identically. |
| `Properties/AssemblyInfo.cs` | `[assembly: InternalsVisibleTo("Jellyfin.Plugin.Youtarr.Tests")]` so the mapping internals are testable. |
| `Tests/Providers/YoutarrEpisodeNfoProviderTests.cs` | 27 cases (see below). |
| `Tests/Utils/PathUtilsTests.cs` (+4) | `FindNfoForVideo`: flat-exists, missing, nested, null/empty/whitespace. |

## MapToEpisode Logic

**Field map (EPI-01/02/03/06):** `Name=Title`; `Overview=FormatDescription(Plot, MaxDescriptionLength)`;
`Genres/Tags → arrays` when non-empty; `ProviderIds["YouTube"]=YouTubeId`; `OfficialRating=MpaaRating`;
`Studios=[Studio]`.

**Runtime (EPI-01, Pitfall 4 / T-02-11):** prefer `TimeSpan.FromSeconds(DurationInSeconds).Ticks`,
else `TimeSpan.FromMinutes(RuntimeMinutes).Ticks`. 300 s → `3_000_000_000`; 5 min → `3_000_000_000`.
No manual ticks multiply (overflow-safe long arithmetic).

**Season assignment + numbering (valid `<premiered>`):**
- `PremiereDate=date`, `ProductionYear=date.Year`.
- LIB-03/LIB-04: `ParentIndexNumber = YearSeasons ? date.Year : 1`.
- EPI-05: `IndexNumber = YYYYMMDD ? (y*10000 + m*100 + d) : null` (2024-03-15 → `20240315`; Default → null, Jellyfin auto-sequences).

**EPI-07 date fallback chain (no valid `<premiered>`):**
1. valid `<premiered>` → use it, `ParentIndexNumber = year`.
2. `<premiered>` invalid/missing, `<dateadded>` valid → `ParentIndexNumber=0` (Season 0), `IndexNumber=null`, **`PremiereDate=DateAdded`** (episode still carries a date; the download date never sets the year).
3. both missing → `ParentIndexNumber=0`, `IndexNumber=null`, `PremiereDate=null`.

In code, step 2/3 collapse to: `ParentIndexNumber=0; IndexNumber=null; PremiereDate = data.DateAdded;`
(`DateAdded` is null when both are missing, giving step 3 for free). A warning is logged distinguishing
"no usable date" from "using <dateadded>".

**Plot (EPI-04 / T-02-09):** truncate to `MaxDescriptionLength` via range operator, then
`Replace("\n","<br>", StringComparison.Ordinal)`. Null/empty passes through unchanged.

## FindNfoForVideo (CMP-01 / CMP-02)

`Path.ChangeExtension(videoPath, ".nfo")` produces the same-basename sidecar, which is correct for
**both** Youtarr layouts because each places the `.nfo` next to the video with a matching base name:
- Flat (CMP-01): `Channel/video.mp4` → `Channel/video.nfo`.
- Nested (CMP-02): `Channel/Title/Title.mp4` → `Channel/Title/Title.nfo`.

Returns null for null/empty/whitespace input or when the sidecar is absent. Pure aside from the
`File.Exists` probe.

## Test Coverage (31 new)

**FindNfoForVideo (4):** flat-exists → sibling path, missing → null, nested → correct path, null/empty/whitespace → null.

**Episode provider (27):** Name constant; GetMetadata no-NFO/malformed/non-movie-root → HasMetadata false,
valid NFO → HasMetadata true + Name mapped; title→Name; FormatDescription long-truncate / newline→`<br>` / null-empty passthrough; MapToEpisode plot truncate + `<br>`; valid-year → ParentIndexNumber=year + ProductionYear + PremiereDate; YearSeasons off → 1; missing date → ParentIndexNumber=0; missing date → IndexNumber null + PremiereDate null; premiered-missing/dateadded-valid → Season 0 + IndexNumber null + PremiereDate=DateAdded; YYYYMMDD → 20240315; Default → null; durationSeconds → ticks; runtimeMinutes-only → ticks; duration-preferred-over-runtime; YouTubeId → ProviderIds; mpaa → OfficialRating; no-mpaa → null; genres/tags/studio populated.

## DI Decision Deferred to 02-04

No explicit DI registration was added in this plan. `YoutarrEpisodeNfoProvider` relies on Jellyfin's
auto-discovery of `ILocalMetadataProvider<Episode>` implementations — the same mechanism that registers
`YoutarrSeriesNfoProvider` (Pitfall 7). `PluginServiceRegistrator` was left untouched. The 02-01 live
probe / 02-04 live verify wave records whether explicit registration or a custom provider `Order` is
required; if so, 02-04 adds it. Keeping this consistent with the Series provider avoids a speculative DI
change that the live run might contradict.

## Verification Results

- `dotnet build Jellyfin.Plugin.Youtarr/...csproj -c Release`: **0 errors**, 13 CA-analyzer warnings (`TreatWarningsAsErrors=false`). The new provider emits only the same CA1031 (intentional broad catch for malformed NFOs), CA1062 (DI-supplied args), and CA1848 (direct `ILogger` calls) warnings the existing `YoutarrSeriesNfoProvider` already emits — no new warning categories; consistent with the established Phase 1 provider style.
- `dotnet test ...csproj -c Release`: **80/80 passed**, 0 failed, 0 skipped (49 prior + 4 FindNfoForVideo + 27 episode provider). No Phase 1 / 02-02 regression.
- `dotnet publish -c Release`: output contains **only** `Jellyfin.Plugin.Youtarr.dll`, **zero** `MediaBrowser.*` DLLs — ExcludeAssets invariant preserved.

## TDD Gate Compliance

Plan `type: tdd`. Both tasks observed genuine RED → GREEN:

- Task 1 (FindNfoForVideo): `test(02-03)` 9a4e92f (failing — 6 CS0117, method missing) → `feat(02-03)` 66471bb (GREEN, PathUtils 16/16).
- Task 2 (Episode provider): `test(02-03)` 7690b00 (failing — CS0246, type missing) → `feat(02-03)` 7bba3e5 (GREEN, suite 80/80).

RED was a real compile failure before each implementation; no test passed unexpectedly during RED. No REFACTOR commit needed (clean first pass).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing critical functionality] EPI-07 `<dateadded>` fallback for PremiereDate**
- **Found during:** Task 2 (per the plan-checker EPI-07 guidance carried in the execution context).
- **Issue:** The RESEARCH `MapToEpisode` skeleton's `else` branch set only `ParentIndexNumber=0` + `IndexNumber=null` and left `PremiereDate` unset, dropping the documented `premiered → dateadded → Season 0` chain — an episode with a valid `<dateadded>` but no `<premiered>` would lose its date entirely.
- **Fix:** The fallback branch sets `episode.PremiereDate = data.DateAdded` (null-safe: yields the both-missing case for free) while still forcing Season 0 + null IndexNumber. The download date is never used for the year/season. Covered by `MapToEpisode_PremiereMissing_DateAddedValid_Season0_PremiereFromDateAdded`.
- **Files modified:** `Providers/YoutarrEpisodeNfoProvider.cs`, `Tests/Providers/YoutarrEpisodeNfoProviderTests.cs`.
- **Commit:** 7bba3e5 (impl), 7690b00 (test).

### Design choices within plan scope

- **`InternalsVisibleTo` added via a new `Properties/AssemblyInfo.cs`** rather than the csproj, keeping the csproj's ExcludeAssets block untouched. Enables direct unit-testing of `MapToEpisode`/`FormatDescription` without constructing `Plugin.Instance` (plan-checker testability guidance — definitive, not conditional).
- **`GetMetadata` uses `Path.ChangeExtension` directly** for discovery (the plan explicitly allows either this or `FindNfoForVideo`, since both resolve identically). `FindNfoForVideo` remains a tested reusable helper.

No checkpoints, no auth gates, no architectural (Rule 4) changes. No Docker/sudo touched (this is the autonomous unit-testable layer; live checkpoints 02-01/02-04 run when the daemon is up).

## Threat Mitigations Applied

| Threat ID | Mitigation | Status |
|-----------|-----------|--------|
| T-02-08 (malformed NFO crashing GetMetadata) | `Parse` wrapped in try/catch → log + `HasMetadata=false`; `GetMetadata_MalformedNfo_HasMetadataFalse_NoThrow` asserts it. | Mitigated |
| T-02-09 (unbounded plot) | `FormatDescription` truncates to `MaxDescriptionLength` before assignment; `FormatDescription_LongPlot_TruncatedToMaxLength` + `MapToEpisode_Plot_TruncatedToMaxLength` assert length. | Mitigated |
| T-02-10 (wrong year from download date) | `ParentIndexNumber`/`ProductionYear` derive from `<premiered>` only; `<dateadded>` only ever sets `PremiereDate` in the Season-0 fallback, never the year. | Mitigated |
| T-02-11 (RunTimeTicks overflow) | `TimeSpan.FromSeconds/FromMinutes(...).Ticks` (long arithmetic), no manual multiply; runtime tests assert exact ticks. | Mitigated |
| T-02-SC (package installs) | No package installs in this plan. | N/A |

## Known Stubs

None. The provider fully maps every Phase 2 metadata, season, numbering, and fallback requirement.
Explicit DI registration is intentionally deferred to 02-04's live verify (auto-discovery is the default
and matches the Series provider) — a planned boundary, not a stub.

## Commits

- `9a4e92f` test(02-03): add failing tests for PathUtils.FindNfoForVideo
- `66471bb` feat(02-03): add PathUtils.FindNfoForVideo sidecar resolver
- `7690b00` test(02-03): add failing tests for YoutarrEpisodeNfoProvider
- `7bba3e5` feat(02-03): implement YoutarrEpisodeNfoProvider (NFO -> Episode)

## Self-Check: PASSED

All created files (`YoutarrEpisodeNfoProvider.cs`, `Properties/AssemblyInfo.cs`,
`YoutarrEpisodeNfoProviderTests.cs`) and modified files (`PathUtils.cs`, `PathUtilsTests.cs`) exist on
disk; all four per-task commits (9a4e92f, 66471bb, 7690b00, 7bba3e5) are present in git history. Build is
clean in Release; full suite 80/80 green; publish output is DLL-only.
