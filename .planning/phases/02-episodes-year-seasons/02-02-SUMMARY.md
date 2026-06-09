---
phase: 02-episodes-year-seasons
plan: 02
subsystem: nfo-parser-contract-layer
tags: [csharp, nfo, xdocument, dto, parser, tdd, configuration, episodes]
status: complete
requires:
  - "PathUtils StreamReader-UTF8 + XDocument parsing pattern (01-02)"
  - "PluginConfiguration.YearSeasons stub (01-01)"
  - "xUnit + Moq test project (01-01)"
provides:
  - "Jellyfin.Plugin.Youtarr/Models/YoutarrVideoData.cs — pure 11-field DTO, no MediaBrowser.* dependency"
  - "Jellyfin.Plugin.Youtarr/Parsers/YoutarrNfoParser.cs — static Parse(nfoPath) -> YoutarrVideoData? with EPI-07 date guard"
  - "PluginConfiguration.EpisodeNumberingScheme (enum Default=0|YYYYMMDD=1) + MaxDescriptionLength (default 500)"
  - "YoutarrNfoParserTests (21 cases) + PluginConfigurationTests (4 cases)"
affects:
  - "02-03 YoutarrEpisodeNfoProvider consumes YoutarrVideoData without re-parsing XML"
  - "02-03 MapToEpisode/FormatDescription read the two new config fields"
tech-stack:
  added: []
  patterns:
    - "NFO parsing isolated from Jellyfin types (same pattern as PathUtils) so every field map + date edge case is unit-testable offline"
    - "init-only DTO properties; IReadOnlyList<string> for genre/tag collections (CA2227-clean)"
    - "parser surfaces XmlException on malformed XML (does NOT swallow) so the 02-03 provider catches + logs; non-<movie> roots return null"
    - "EPI-07 IsValidYouTubeDate guard (>=2005, <=now+2) applied to <premiered> only; <dateadded> captured raw as a fallback source"
key-files:
  created:
    - "Jellyfin.Plugin.Youtarr/Models/YoutarrVideoData.cs"
    - "Jellyfin.Plugin.Youtarr/Parsers/YoutarrNfoParser.cs"
    - "Jellyfin.Plugin.Youtarr.Tests/Parsers/YoutarrNfoParserTests.cs"
    - "Jellyfin.Plugin.Youtarr.Tests/Configuration/PluginConfigurationTests.cs"
  modified:
    - "Jellyfin.Plugin.Youtarr/Configuration/PluginConfiguration.cs (added EpisodeNumberingScheme enum + 2 properties)"
decisions:
  - "DTO collections typed IReadOnlyList<string> (init-only) instead of List<string>: avoids the CA2227 settable-collection analyzer warning while still exposing Count/Contains the tests need. Downstream 02-03 maps to arrays via ToArray()."
  - "EPI-07 fallback per plan-checker guidance: parser captures <dateadded> into DateAdded (no YouTube-date guard, it is a download timestamp) so the season/date fallback chain (premiered -> dateadded -> Season 0) is possible in 02-03. Parser does not itself decide season assignment."
  - "Parser kept pure (input: file path; output: DTO) with no Plugin.Instance dependency, so unit tests need no running server (plan-checker testability guidance)."
  - "DateTime parsing uses CultureInfo.InvariantCulture + DateTimeStyles.None for deterministic, culture-independent results across CI environments."
requirements: [EPI-01, EPI-02, EPI-03, EPI-04, EPI-05, EPI-06, EPI-07, LIB-04]
metrics:
  duration: "~12 min"
  completed: "2026-06-09"
  tasks: "2 of 2"
  files: 5
---

# Phase 2 Plan 02: NFO Parser + DTO + Config Contract Layer Summary

The testable foundation of the Episode pipeline: a pure `YoutarrNfoParser.Parse(nfoPath)`
that maps a Youtarr `<movie>` NFO into a strongly-typed `YoutarrVideoData` DTO with EPI-07
date validation enforced at the parse boundary, plus the two new `PluginConfiguration`
fields (`EpisodeNumberingScheme`, `MaxDescriptionLength`) the Episode provider will read.
No Docker, no Jellyfin types in the parser/DTO — 21 parser tests + 4 config tests, full
suite 49/49 green in Release. Built TDD (RED test commit precedes each GREEN implementation).

## What Was Built

| File | Provides |
|------|----------|
| `Models/YoutarrVideoData.cs` | Pure DTO with 11 init-only fields: `Title`, `Plot`, `PremiereDate`, `DateAdded`, `RuntimeMinutes`, `DurationInSeconds`, `Studio`, `YouTubeId`, `MpaaRating`, `Genres` (IReadOnlyList), `Tags` (IReadOnlyList). No `MediaBrowser.*` dependency. |
| `Parsers/YoutarrNfoParser.cs` | `public static YoutarrVideoData? Parse(string nfoPath)`. Opens via `StreamReader(path, Encoding.UTF8)` + `XDocument.Load(reader)` (Pitfall 3). Returns `null` for non-`<movie>` roots; surfaces `XmlException` on malformed XML. EPI-07 `IsValidYouTubeDate` guard (>=2005, <=now+2). `uniqueid type="youtube"` (case-insensitive) preferred over `<youtubeid>`. |
| `Configuration/PluginConfiguration.cs` | Added `public enum EpisodeNumberingScheme { Default = 0, YYYYMMDD = 1 }` (XML-doc'd, notes same-day collision per EPI-05) and properties `EpisodeNumberingScheme` (default `Default`) + `MaxDescriptionLength` (default 500). `YearSeasons` (default true) preserved. |
| `Tests/Parsers/YoutarrNfoParserTests.cs` | 21 cases: full field map, non-movie-root null, date validation (missing/empty/invalid/pre-2005/MinValue/epoch/future all -> null `PremiereDate`; valid date parsed), uniqueid-priority + youtubeid fallback, durationinseconds+runtime both captured, multiple genres/tags, empty genre/tag skipped, emoji + XML-entity round-trip, long plot untruncated, `DateAdded` captured raw, malformed XML throws. |
| `Tests/Configuration/PluginConfigurationTests.cs` | 4 cases: default `YearSeasons` true, default scheme `Default`, default `MaxDescriptionLength` 500, enum values stable (Default=0, YYYYMMDD=1). |

## DTO Shape

```csharp
public class YoutarrVideoData
{
    public string? Title { get; init; }
    public string? Plot { get; init; }
    public DateTime? PremiereDate { get; init; }       // <premiered>, guarded (>=2005, <=now+2)
    public DateTime? DateAdded { get; init; }          // <dateadded>, raw (EPI-07 fallback)
    public int? RuntimeMinutes { get; init; }          // <runtime>
    public int? DurationInSeconds { get; init; }       // <fileinfo>...<durationinseconds>
    public string? Studio { get; init; }               // <studio>
    public string? YouTubeId { get; init; }            // <uniqueid type="youtube"> | <youtubeid>
    public string? MpaaRating { get; init; }           // <mpaa>
    public IReadOnlyList<string> Genres { get; init; } // <genre>*
    public IReadOnlyList<string> Tags { get; init; }   // <tag>*
}
```

## Config Fields Added

| Field | Type | Default | Requirement |
|-------|------|---------|-------------|
| `YearSeasons` | bool | true (unchanged) | LIB-04 |
| `EpisodeNumberingScheme` | enum (Default=0, YYYYMMDD=1) | Default | EPI-05 |
| `MaxDescriptionLength` | int | 500 | EPI-04 |

## Verification Results

- `dotnet build Jellyfin.Plugin.Youtarr/...csproj -c Release`: **0 errors** (8 pre-existing CA analyzer warnings on Phase 1 files; `TreatWarningsAsErrors=false`). The new parser/DTO/config files emit **no** analyzer warnings.
- `dotnet test ...csproj -c Release`: **49/49 passed** (24 Phase 1 + 4 config + 21 parser), 0 failed, 0 skipped.
  - `YoutarrNfoParserTests`: 21/21 green.
  - `PluginConfigurationTests`: 4/4 green.
- ExcludeAssets invariant intact: `grep -c "ExcludeAssets>runtime"` = 2 in the csproj; csproj unmodified by this plan. Plugin still publishes DLL-only (no `MediaBrowser.*.dll`).
- Plan grep gates: `public static YoutarrVideoData? Parse` present; `class YoutarrVideoData` present; `2005` date guard present; `MaxDescriptionLength { get; set; } = 500` present; `enum EpisodeNumberingScheme` present.

## TDD Gate Compliance

Plan `type: tdd`. Both tasks observed RED -> GREEN:

- Task 1 (config): `test(02-02)` cd27095 (failing — config members missing) -> `feat(02-02)` f714634 (GREEN, 4/4).
- Task 2 (parser/DTO): `test(02-02)` dd1b66c (failing — types missing) -> `feat(02-02)` 406bea8 (GREEN, 21/21).

RED was a genuine compile failure (5 errors config, 6 errors parser) before each implementation; no test passed unexpectedly during RED. No REFACTOR commit was needed (implementations were clean on first pass).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing critical functionality] Captured `<dateadded>` into `DateAdded` on the DTO**
- **Found during:** Task 2 (per plan-checker EPI-07 guidance carried into the execution context).
- **Issue:** The interfaces block already listed `DateAdded` as a DTO field, but the plan-checker explicitly flagged that the parser must populate it (no YouTube-date guard) so the 02-03 provider can implement the premiered -> dateadded -> Season 0 fallback chain.
- **Fix:** `ParseDate` (no `IsValidYouTubeDate` guard) maps `<dateadded>` into `DateAdded`; a dedicated test (`Parse_DateAdded_CapturedWithoutYouTubeGuard`) asserts a 2026 download timestamp is preserved.
- **Files modified:** `Parsers/YoutarrNfoParser.cs`, `Tests/Parsers/YoutarrNfoParserTests.cs`.
- **Commit:** 406bea8 (impl), dd1b66c (test).

### Design choices within plan scope

- **DTO collections typed `IReadOnlyList<string>`** (init-only) rather than `List<string>`: the literal interface text said "lists default to new()", but a public settable `List<string>` property trips the CA2227 analyzer under `AnalysisMode=AllEnabledByDefault`. `IReadOnlyList<string>` (init-only, defaulting to `new List<string>()`) keeps the build warning-clean while exposing `Count`/`Contains`/`Single` the tests use. Downstream 02-03 converts to arrays via `.ToArray()` exactly as the research skeleton's `MapToEpisode` already does (`data.Genres.Count > 0` / `.ToArray()`), so this is source-compatible with the planned consumer.

No checkpoints, no auth gates, no architectural (Rule 4) changes. No Docker/sudo touched (per the sequencing note, this is the autonomous unit-testable layer).

## Threat Mitigations Applied

| Threat ID | Mitigation | Status |
|-----------|-----------|--------|
| T-02-04 (malformed/huge XML DoS) | Parser does NOT wrap `XDocument.Load`; surfaces `XmlException` to caller (02-03 catches -> HasMetadata false). `Parse_MalformedXml_Throws` asserts the boundary. | Mitigated |
| T-02-05 (path-traversal strings in plot/title) | NFO text mapped to pure data DTO fields; never used as a filesystem path. | Mitigated by design |
| T-02-06 (emoji/BOM crashing UTF-8 read) | `StreamReader(path, Encoding.UTF8)` + `XDocument.Load(reader)`; `Parse_EmojiInPlot_DeserializesCorrectly` asserts correct decode of two multi-byte emoji. | Mitigated |
| T-02-07 (invalid date -> wrong year) | `IsValidYouTubeDate` (>=2005, <=now+2) rejects MinValue/epoch/future for `PremiereDate`; six date-edge tests cover it. | Mitigated |
| T-02-SC (package installs) | No package installs in this plan. | N/A |

## Known Stubs

None. The parser and DTO are fully implemented; no placeholder/empty-value returns. (Season
assignment, description truncation, and Episode mapping are intentionally out of scope for
this contract layer — they live in 02-03's `YoutarrEpisodeNfoProvider`, which consumes this
DTO. That is a planned boundary, not a stub.)

## Commits

- `cd27095` test(02-02): add failing tests for PluginConfiguration Phase 2 settings
- `f714634` feat(02-02): add EpisodeNumberingScheme + MaxDescriptionLength config
- `dd1b66c` test(02-02): add failing tests for YoutarrNfoParser + DTO
- `406bea8` feat(02-02): implement YoutarrVideoData DTO + YoutarrNfoParser

## Self-Check: PASSED

All 4 created files + the modified `PluginConfiguration.cs` exist on disk; all 4 per-task
commits (cd27095, f714634, dd1b66c, 406bea8) are present in git history. Build is clean in
Release; full test suite is 49/49 green.
