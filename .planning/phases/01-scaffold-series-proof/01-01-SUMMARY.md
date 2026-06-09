---
phase: 01-scaffold-series-proof
plan: 01
subsystem: plugin-scaffold
tags: [jellyfin, dotnet8, scaffold, packaging, plugin-identity]
requires: []
provides:
  - "Jellyfin.Plugin.Youtarr buildable net8.0 class library"
  - "Permanent plugin GUID 80302d7f-7fc3-4b1c-9a3f-fd85b98b9a69"
  - "build.yaml packaging descriptor (targetAbi 10.10.0.0)"
  - "Plugin entry point (BasePlugin<PluginConfiguration> + IHasWebPages)"
  - "Constants (ProviderName, YouTubeProviderId)"
  - "PluginConfiguration stub (YearSeasons)"
  - "Embedded config page (configPage.html)"
affects:
  - "plan 01-02 (adds providers + IResolverIgnoreRule + DI wiring)"
  - "plan 01-03 (live Jellyfin load verification)"
  - "Phase 4 (packaging reuses the GUID + build.yaml)"
tech-stack:
  added:
    - "Jellyfin.Controller 10.10.7 (ExcludeAssets=runtime)"
    - "Jellyfin.Model 10.10.7 (ExcludeAssets=runtime)"
  patterns:
    - "ExcludeAssets=runtime on both Jellyfin refs (prevents TypeLoadException)"
    - "Single permanent GUID mirrored in Plugin.cs StaticId and build.yaml guid"
    - "IPluginServiceRegistrator for DI (not a BasePlugin override) in 10.10.x"
key-files:
  created:
    - "Jellyfin.Plugin.Youtarr/Jellyfin.Plugin.Youtarr.csproj"
    - "Jellyfin.Plugin.Youtarr/build.yaml"
    - "Jellyfin.Plugin.Youtarr/Plugin.cs"
    - "Jellyfin.Plugin.Youtarr/Constants.cs"
    - "Jellyfin.Plugin.Youtarr/Configuration/PluginConfiguration.cs"
    - "Jellyfin.Plugin.Youtarr/Configuration/configPage.html"
    - ".gitignore"
  modified: []
decisions:
  - "Generated permanent GUID 80302d7f-7fc3-4b1c-9a3f-fd85b98b9a69 via /proc/sys/kernel/random/uuid (uuidgen not installed)"
  - "DI registration deferred to 01-02 via IPluginServiceRegistrator, not a non-existent BasePlugin.RegisterServices override"
metrics:
  duration: "~10 min"
  completed: "2026-06-09"
  tasks: 3
  files: 7
---

# Phase 1 Plan 01: Plugin Scaffold + Packaging Descriptor Summary

Scaffolded the `Jellyfin.Plugin.Youtarr` .NET 8 class library so it compiles, declares a stable plugin identity, exposes a config page, and publishes to a single plugin DLL with no Jellyfin runtime assemblies bundled — the load-bearing foundation for every later slice (PLUG-01).

## Permanent Plugin GUID (load-bearing — reused by 01-02, 01-03, Phase 4)

```
80302d7f-7fc3-4b1c-9a3f-fd85b98b9a69
```

This GUID appears byte-identical in:
- `Jellyfin.Plugin.Youtarr/build.yaml` → `guid:`
- `Jellyfin.Plugin.Youtarr/Plugin.cs` → `Plugin.StaticId` (and therefore `Plugin.Id`)

It must NEVER change. Jellyfin writes plugin config to `config/plugins/<GUID>/config.xml`; drift orphans configuration (Pitfall 2). All later phases reference this exact value.

## What Was Built

| File | Provides |
|------|----------|
| `Jellyfin.Plugin.Youtarr.csproj` | net8.0 SDK-style class library; Jellyfin.Controller + Jellyfin.Model 10.10.7 each with `<ExcludeAssets>runtime</ExcludeAssets>`; EmbeddedResource for configPage.html |
| `build.yaml` | jprm packaging descriptor: name YoutarrMetadata, the permanent GUID, version 1.0.0.0, targetAbi 10.10.0.0, framework net8.0, category Metadata, owner sandwich, artifact Jellyfin.Plugin.Youtarr.dll |
| `Plugin.cs` | `BasePlugin<PluginConfiguration>, IHasWebPages`; StaticId GUID; Id/Name/Description overrides; `Instance` static; `GetPages()` returning configPage.html embedded resource |
| `Constants.cs` | `ProviderName = "Youtarr"`, `YouTubeProviderId = "YouTube"` |
| `Configuration/PluginConfiguration.cs` | `BasePluginConfiguration` subclass with `YearSeasons` placeholder (default true) |
| `Configuration/configPage.html` | Well-formed embedded HTML config-page stub (full controls deferred to Phase 3) |
| `.gitignore` | Ignores bin/, obj/, dist/, *.zip, manifest.json |

## Project Layout

```
youtarr-jf-plugin/
├── .gitignore
└── Jellyfin.Plugin.Youtarr/
    ├── Jellyfin.Plugin.Youtarr.csproj
    ├── build.yaml
    ├── Plugin.cs
    ├── Constants.cs
    └── Configuration/
        ├── PluginConfiguration.cs
        └── configPage.html
```

## Verification Results

- `dotnet restore` — succeeded; Jellyfin.Controller/Model 10.10.7 resolved from NuGet.
- `dotnet build -c Release` — exit 0 (one benign CA1724 warning; see Deviations).
- `dotnet publish -c Release -o ./dist/Jellyfin.Plugin.Youtarr` — output contains only `Jellyfin.Plugin.Youtarr.dll` (+ `.deps.json`, `.pdb`). NO `MediaBrowser.Controller.dll`, `MediaBrowser.Model.dll`, `MediaBrowser.Common.dll`, or `Jellyfin.Data.dll` — proves ExcludeAssets worked (mitigates T-01-01 / Pitfall 1).
- GUID identical in `Plugin.cs` and `build.yaml` — no drift (mitigates T-01-02 / Pitfall 2).
- `Plugin.cs` implements `IHasWebPages.GetPages()` referencing `...Configuration.configPage.html`.

(Live in-Jellyfin "Active" confirmation is the end-to-end verification owned by plan 01-03.)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] `BasePlugin<T>.RegisterServices` does not exist in Jellyfin 10.10.7**
- **Found during:** Task 2 (Release build failed with CS0115: no suitable method to override).
- **Issue:** The plan (following RESEARCH Open Question #1) instructed adding an empty `RegisterServices` override on `Plugin`. In Jellyfin 10.10.x, `BasePlugin<TConfig>` exposes no `RegisterServices` method to override — DI registration is done by implementing the separate `IPluginServiceRegistrator` interface (a class Jellyfin auto-discovers), not via a base-class override.
- **Fix:** Removed the non-existent override and replaced it with a clearly-marked TODO in `Plugin.cs` documenting the correct `IPluginServiceRegistrator` mechanism for plan 01-02. Also removed the now-unused `Microsoft.Extensions.DependencyInjection` using. Build green; the plan's intent (leave DI wiring to 01-02, keep build green, do not reference YoutarrPrefixIgnoreRule which doesn't exist yet) is fully preserved. Note `IResolverIgnoreRule` implementations are also auto-discovered by Jellyfin DI, so 01-02 verifies whether explicit registration is even required.
- **Files modified:** `Jellyfin.Plugin.Youtarr/Plugin.cs`
- **Commit:** 98108db

### Notes (not deviations)

- **GUID source:** `uuidgen` is not installed in this environment. Generated the permanent GUID via `cat /proc/sys/kernel/random/uuid`, which produces an RFC-4122 v4 UUID (kernel-backed) — functionally equivalent to uuidgen for Jellyfin's plugin-loader format validation.
- **CA1724 warning:** Build emits one warning — the entry-point class name `Plugin` partially conflicts with the `Jellyfin.Plugin` namespace. This is the mandatory Jellyfin convention (the loader discovers a class literally named `Plugin`); renaming would break plugin discovery. `TreatWarningsAsErrors` is false, so it does not affect the build. Left as-is intentionally.

## Threat Mitigations Applied

| Threat ID | Mitigation | Status |
|-----------|-----------|--------|
| T-01-01 (DoS via bundled runtime DLLs) | ExcludeAssets=runtime on both refs; Task 3 publish output verified clean | Mitigated |
| T-01-02 (GUID drift orphans config) | Single GUID mirrored in Plugin.cs StaticId + build.yaml; verify asserted equality | Mitigated |
| T-01-SC (NuGet install legitimacy) | Accepted per RESEARCH Package Legitimacy Audit (official Jellyfin NuGet packages) | Accepted |

## Known Stubs

- `Configuration/configPage.html` — intentional stub with placeholder copy; full configuration controls are a Phase 3 concern (documented in SKELETON.md "Out of Scope"). Does not block PLUG-01: the plugin loads and the config page renders.
- `PluginConfiguration.YearSeasons` — placeholder property; consumed starting Phase 2. Intentional per plan.

Both stubs are expected by the plan and do not prevent this plan's goal (a buildable plugin DLL with stable identity and a config-page hook).

## Commits

- `a2fad84` feat(01-01): scaffold plugin project and packaging descriptor
- `98108db` feat(01-01): add plugin entry point, constants, config stub, config page

## Self-Check: PASSED

All 7 created source/config files plus the SUMMARY exist on disk; both per-task commits (a2fad84, 98108db) are present in git history.
