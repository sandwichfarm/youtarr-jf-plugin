---
phase: 01-scaffold-series-proof
plan: 02
subsystem: series-resolution
tags: [jellyfin, dotnet8, metadata-provider, resolver-ignore-rule, path-utils, xunit, tdd]
requires:
  - "Jellyfin.Plugin.Youtarr buildable net8.0 class library (01-01)"
  - "Constants.ProviderName = \"Youtarr\" (01-01)"
  - "Plugin entry point with IPluginServiceRegistrator TODO (01-01)"
provides:
  - "PathUtils (GetChannelNameFromPath, FindFirstNfoInFolder, ReadStudioFromMovieNfo) — Jellyfin-free, unit-testable"
  - "YoutarrPrefixIgnoreRule : IResolverIgnoreRule suppressing __prefix directories"
  - "YoutarrSeriesNfoProvider : ILocalMetadataProvider<Series> deriving Series name from channel folder"
  - "PluginServiceRegistrator : IPluginServiceRegistrator wiring the ignore rule into DI"
  - "Jellyfin.Plugin.Youtarr.Tests xUnit project (24 tests, all green)"
affects:
  - "plan 01-03 (live Jellyfin load verification — confirms A1 and provider Order)"
  - "Phase 2 (Season/Episode providers build on this provider + PathUtils)"
tech-stack:
  added:
    - "xunit 2.9.3 (test project)"
    - "Moq 4.20.72 (mock ILogger / IDirectoryService)"
    - "Jellyfin.Controller/Model 10.10.7 referenced WITHOUT ExcludeAssets in the test project (runtime assemblies needed at test time)"
  patterns:
    - "IResolverIgnoreRule lives in MediaBrowser.Controller.Resolvers (NOT .Library) — verified by reflection"
    - "DI via separate IPluginServiceRegistrator class (10.10.x), not a BasePlugin override"
    - "Pure path/XML logic extracted into PathUtils (no MediaBrowser.* types) for isolation testing"
    - "ILocalMetadataProvider<Series> returns HasMetadata=true to be authoritative (Pitfall 5)"
key-files:
  created:
    - "Jellyfin.Plugin.Youtarr/Utils/PathUtils.cs"
    - "Jellyfin.Plugin.Youtarr/Utils/YoutarrPrefixIgnoreRule.cs"
    - "Jellyfin.Plugin.Youtarr/Providers/YoutarrSeriesNfoProvider.cs"
    - "Jellyfin.Plugin.Youtarr/PluginServiceRegistrator.cs"
    - "Jellyfin.Plugin.Youtarr.Tests/Jellyfin.Plugin.Youtarr.Tests.csproj"
    - "Jellyfin.Plugin.Youtarr.Tests/Utils/PathUtilsTests.cs"
    - "Jellyfin.Plugin.Youtarr.Tests/Utils/YoutarrPrefixIgnoreRuleTests.cs"
    - "Jellyfin.Plugin.Youtarr.Tests/Providers/YoutarrSeriesNfoProviderTests.cs"
  modified:
    - "Jellyfin.Plugin.Youtarr/Plugin.cs (replaced 01-02 DI TODO with note pointing to PluginServiceRegistrator)"
decisions:
  - "DI registration done via PluginServiceRegistrator : IPluginServiceRegistrator, not Plugin.RegisterServices (no such override in 10.10.x)"
  - "IResolverIgnoreRule imported from MediaBrowser.Controller.Resolvers (reflection-verified), correcting the RESEARCH skeleton's MediaBrowser.Controller.Library using"
  - "Test project references Jellyfin.Controller/Model directly WITHOUT ExcludeAssets so MediaBrowser runtime DLLs load at test time"
  - "No custom provider Order set in Phase 1 (RESEARCH Open Question #2 — verified live in 01-03)"
requirements: [LIB-01, SER-01, SER-02, CMP-03]
metrics:
  duration: "~6 min"
  completed: "2026-06-09"
  tasks: 3
  files: 9
---

# Phase 1 Plan 02: Path Utils, __prefix Ignore Rule, and Series Provider Summary

Channel folders now resolve as correctly-named Series and Youtarr `__prefix` grouping folders are suppressed before they can become phantom Series — the core Phase-1 capability slice, delivered as `PathUtils` + `YoutarrPrefixIgnoreRule` (registered via `IPluginServiceRegistrator`) + `YoutarrSeriesNfoProvider`, all TDD-covered by a 24-test xUnit suite that passes in Release.

## What Was Built

| File | Provides |
|------|----------|
| `Utils/PathUtils.cs` | `GetChannelNameFromPath` (folder name, trailing-separator + null/empty tolerant), `FindFirstNfoInFolder` (first `*.nfo`, top dir only), `ReadStudioFromMovieNfo` (`<studio>` from `<movie>` NFO via XDocument/UTF-8, swallows errors to null). No `MediaBrowser.*` types. |
| `Utils/YoutarrPrefixIgnoreRule.cs` | `IResolverIgnoreRule` returning true only for directories whose name starts with `__`. Files and single-underscore dirs pass through. |
| `Providers/YoutarrSeriesNfoProvider.cs` | `ILocalMetadataProvider<Series>, IHasItemChangeMonitor`. `Name => "Youtarr"`. Sets `Series.Name` from folder, reads `<studio>` as best-effort confirmation (folder name wins), `HasMetadata = true`. `HasChanged` returns false (Phase 1). |
| `PluginServiceRegistrator.cs` | `IPluginServiceRegistrator` registering `AddSingleton<IResolverIgnoreRule, YoutarrPrefixIgnoreRule>()` — the 10.10.x DI mechanism. |
| `Jellyfin.Plugin.Youtarr.Tests/` | xUnit project (xunit 2.9.3, Moq 4.20.72), ProjectReference to the plugin, plus direct Jellyfin.Controller/Model refs (no ExcludeAssets) for runtime loading. 24 tests. |

## Verification Results

- **Plugin Release build:** `dotnet build Jellyfin.Plugin.Youtarr/...csproj -c Release` — Build succeeded (no errors/warnings).
- **Full test suite:** `dotnet test ...Tests.csproj -c Release` — **24/24 passed, 0 failed** (PathUtils 12, YoutarrPrefixIgnoreRule 5, YoutarrSeriesNfoProvider 7).
- **Per-file filtered runs** all green: `~PathUtilsTests` (12), `~YoutarrPrefixIgnoreRuleTests` (5), `~YoutarrSeriesNfoProviderTests` (7).
- **DI registration:** `grep AddSingleton<IResolverIgnoreRule, YoutarrPrefixIgnoreRule>` present in `PluginServiceRegistrator.cs`.
- **HasMetadata:** `grep 'result.HasMetadata = true'` present in `YoutarrSeriesNfoProvider.cs` (SER-02 / Pitfall 5).
- **Key link:** provider calls `PathUtils.` (folder-name + studio reader) — grep confirmed.
- **Publish invariant preserved:** `dotnet publish -c Release` output contains only `Jellyfin.Plugin.Youtarr.dll` — NO `MediaBrowser.*` / `Jellyfin.Controller/Model/Data/Common` DLLs. The Wave 1 ExcludeAssets invariant (T-01-01 / Pitfall 1) still holds after adding the new source files.

(Live in-Jellyfin confirmation of Series resolution and `__prefix` suppression is plan 01-03.)

## Requirements Delivered

- **LIB-01** — channel folder → Series name derivation implemented and unit-tested.
- **SER-01** — Series name from folder name with `<studio>` read as fallback/confirmation; folder name wins (ordering test asserts this).
- **SER-02** — synthesized metadata is authoritative via `HasMetadata = true`.
- **CMP-03** — `__prefix` directories suppressed via the registered `IResolverIgnoreRule`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] DI registration mechanism: `IPluginServiceRegistrator`, not `Plugin.RegisterServices`**
- **Found during:** Task 2.
- **Issue:** The plan's `<action>` and `key_links` instructed completing a `RegisterServices` override in `Plugin.cs` calling `AddSingleton<IResolverIgnoreRule, YoutarrPrefixIgnoreRule>()`. Jellyfin 10.10.7's `BasePlugin<T>` has no `RegisterServices` method to override (the same CS0115 issue Wave 1 hit and flagged in 01-01-SUMMARY). The execution-context correction note confirmed this.
- **Fix:** Created `PluginServiceRegistrator : IPluginServiceRegistrator` (interface in `MediaBrowser.Controller.Plugins`, auto-discovered by Jellyfin) containing the `AddSingleton` call. Verified the exact signature `RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)` by reflection against the installed `MediaBrowser.Controller` 10.10.7 assembly. Replaced the 01-02 TODO block in `Plugin.cs` with a short note pointing to the new registrator. The plan's verify-grep (`grep ... Plugin.cs`) therefore matches `PluginServiceRegistrator.cs` instead — the intent ("rule registered via AddSingleton") is fully satisfied.
- **Files modified:** `PluginServiceRegistrator.cs` (new), `Plugin.cs`.
- **Commit:** ca55131

**2. [Rule 3 - Blocking] `IResolverIgnoreRule` namespace is `MediaBrowser.Controller.Resolvers`, not `MediaBrowser.Controller.Library`**
- **Found during:** Task 2.
- **Issue:** The RESEARCH skeleton's `using MediaBrowser.Controller.Library;` for `IResolverIgnoreRule` would have produced a CS0246 (type not found). The plan's `<interfaces>` block also listed it under `MediaBrowser.Controller.Library`.
- **Fix:** Reflection against `MediaBrowser.Controller` 10.10.7 confirmed `IResolverIgnoreRule` is in `MediaBrowser.Controller.Resolvers` with signature `bool ShouldIgnore(FileSystemMetadata fileInfo, BaseItem? parent)`. Used the correct `using` in both `YoutarrPrefixIgnoreRule.cs` and `PluginServiceRegistrator.cs`.
- **Files modified:** `YoutarrPrefixIgnoreRule.cs`, `PluginServiceRegistrator.cs`.
- **Commit:** ca55131

**3. [Rule 3 - Blocking] Test project needs Jellyfin runtime assemblies (FileNotFoundException at test time)**
- **Found during:** Task 2 (first ignore-rule test run: `Could not load file or assembly 'MediaBrowser.Controller, Version=10.10.7.0'`).
- **Issue:** The plugin csproj sets `<ExcludeAssets>runtime</ExcludeAssets>` on the Jellyfin packages (required so plugin DLLs are not bundled — Pitfall 1). That exclusion flows transitively, so the Jellyfin runtime DLLs were absent from the test output, and any test touching a `MediaBrowser` type (`FileSystemMetadata`, `IResolverIgnoreRule`, `Series`, `ItemInfo`) failed to load the assembly.
- **Fix:** Added direct `PackageReference`s to `Jellyfin.Controller` and `Jellyfin.Model` 10.10.7 in the **test** csproj WITHOUT `ExcludeAssets`. The test project is never deployed as a plugin, so bundling runtime DLLs there is correct and harmless; the deployed plugin's clean publish is unaffected (verified).
- **Files modified:** `Jellyfin.Plugin.Youtarr.Tests/Jellyfin.Plugin.Youtarr.Tests.csproj`.
- **Commit:** 560a5ff (committed with the RED tests that exposed it)

## Open Items for Plan 01-03 (live Jellyfin)

- **Assumption A1 (still needs live confirmation):** `YoutarrPrefixIgnoreRule` relies on `FileSystemMetadata.Name` being the bare directory name. If 01-03's live scan shows the full path is passed instead, the one-line fix is to test `Path.GetFileName(fileInfo.FullName)`. A code comment marks this spot.
- **Provider Order (Open Question #2):** No custom `Order` set on `YoutarrSeriesNfoProvider`. 01-03 must confirm it wins over Jellyfin's built-in `SeriesNfoProvider`; if not, add `public int Order => 0;`.
- **DI necessity:** `IResolverIgnoreRule` may be auto-discovered by Jellyfin DI, making the explicit `PluginServiceRegistrator` registration redundant. It is kept as a safety net; 01-03 confirms which path is active.

## Threat Mitigations Applied

| Threat ID | Mitigation | Status |
|-----------|-----------|--------|
| T-01-03 (DoS via malformed NFO) | `ReadStudioFromMovieNfo` swallows parse/IO errors to null; `GetMetadata` wraps the NFO read in try/catch and falls back to folder name; unit tests cover malformed + missing-file NFO | Mitigated |
| T-01-04 (plugin must be read-only) | PathUtils + provider use only read APIs (`Directory.EnumerateFiles`, `XDocument.Load` via StreamReader); no write/move/rename calls anywhere | Mitigated |
| T-01-05 (phantom Series from __prefix / hidden real channels) | `YoutarrPrefixIgnoreRule` limited to directories with the `__` prefix; tests assert single-underscore dirs and files are NOT ignored | Mitigated |

## Commits

- `0755193` test(01-02): add failing PathUtils tests
- `e4537da` feat(01-02): implement PathUtils (channel name + studio NFO reader + nfo lookup)
- `560a5ff` test(01-02): add failing YoutarrPrefixIgnoreRule tests
- `ca55131` feat(01-02): add YoutarrPrefixIgnoreRule and register it via IPluginServiceRegistrator
- `0a5c28f` test(01-02): add failing YoutarrSeriesNfoProvider tests
- `0c52bd8` feat(01-02): implement YoutarrSeriesNfoProvider (ILocalMetadataProvider<Series>)

## TDD Gate Compliance

All three tasks followed RED → GREEN: each `test(...)` commit was confirmed failing (compile error: missing type) before the corresponding `feat(...)` commit made it green. No REFACTOR commits were needed.

## Self-Check: PASSED

All 9 created/modified source + test + csproj files exist on disk; all 6 per-task commits (0755193, e4537da, 560a5ff, ca55131, 0a5c28f, 0c52bd8) are present in git history; full suite 24/24 green in Release; publish output verified free of Jellyfin runtime DLLs.
