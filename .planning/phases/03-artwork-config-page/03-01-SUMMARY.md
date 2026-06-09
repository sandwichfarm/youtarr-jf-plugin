---
phase: 03-artwork-config-page
plan: 01
subsystem: series-image-provider
tags: [csharp, jellyfin, artwork, image-provider, backdrop, tdd, ilocalimageprovider]
status: complete
requires:
  - "Constants.ProviderName (01-02)"
  - "YoutarrSeriesNfoProvider ILocalMetadataProvider pattern + ILogger injection style (01-02)"
  - "PluginServiceRegistrator AddSingleton safety-net pattern for IResolverIgnoreRule (01-02)"
provides:
  - "Jellyfin.Plugin.Youtarr/Providers/YoutarrSeriesImageProvider.cs — ILocalImageProvider returning channel poster.jpg as ImageType.Backdrop only (ART-02); empty enumerable when poster.jpg absent (ART-04)"
  - "PluginServiceRegistrator: explicit AddSingleton<ILocalImageProvider, YoutarrSeriesImageProvider>() safety-net"
  - "YoutarrSeriesImageProviderTests (5 cases): backdrop emission, never-Primary boundary, missing-poster graceful path, Series-only support gate, identity"
affects:
  - "03-03 live verify confirms ART-01/ART-02/ART-03/ART-04 end-to-end in the Docker harness and decides whether explicit DI registration / custom Order is required"
  - "Series items now carry a Backdrop (poster.jpg) in addition to the built-in Primary"
tech-stack:
  added: []
  patterns:
    - "ILocalImageProvider is a marker interface over IImageProvider — only GetImages(BaseItem, IDirectoryService) is added; Name + Supports come from IImageProvider (no GetSupportedImages — that is IRemoteImageProvider)"
    - "Iterator GetImages with yield break for the graceful empty path (ART-04) — never throws, LogDebug-only"
    - "LocalImageInfo.FileInfo built as new FileSystemMetadata { FullName = posterPath } (FullName is the only field the image pipeline needs — mirrors built-in EpisodeLocalImageProvider)"
    - "Backdrop-only: the built-in LocalImageProvider (Order=0) owns Series Primary; the plugin must not re-emit Primary (Pitfall 1) to avoid rescan flicker"
key-files:
  created:
    - "Jellyfin.Plugin.Youtarr/Providers/YoutarrSeriesImageProvider.cs"
    - "Jellyfin.Plugin.Youtarr.Tests/Providers/YoutarrSeriesImageProviderTests.cs"
  modified:
    - "Jellyfin.Plugin.Youtarr/PluginServiceRegistrator.cs (added ILocalImageProvider registration + remarks)"
decisions:
  - "Backdrop ONLY — the provider never emits ImageType.Primary. The built-in LocalImageProvider already returns poster.jpg as Series Primary at Order=0 (ART-01); re-emitting Primary risks 'image replaced' flicker on rescans (RESEARCH Pitfall 1). The Primary-boundary is asserted by GetImages_SeriesWithPoster_NeverEmitsPrimary."
  - "Explicit AddSingleton<ILocalImageProvider, YoutarrSeriesImageProvider>() added in PluginServiceRegistrator as a conservative safety net (RESEARCH Open Question #2), mirroring the IResolverIgnoreRule precedent. 03-03 live verify decides whether auto-discovery makes it redundant."
  - "No custom Order set — matches the Phase 1 conservative stance. The built-in stays at Order=0 for Primary; a custom Order is only added in 03-03 if live behaviour requires it."
  - "Interface signatures verified by reflection against the installed Jellyfin.Controller 10.10.7 assembly before coding (per the 01-02 namespace-correction precedent): ILocalImageProvider.GetImages(BaseItem, IDirectoryService) -> IEnumerable<LocalImageInfo>; LocalImageInfo has FileInfo (FileSystemMetadata, MediaBrowser.Model) + Type (ImageType); ImageType.Backdrop exists. The RESEARCH skeleton's namespaces were correct as written."
requirements: [ART-02, ART-04]
metrics:
  duration: "~5 min"
  completed: "2026-06-10"
  tasks: "2 of 2"
  files: 3
---

# Phase 3 Plan 01: YoutarrSeriesImageProvider (poster.jpg → Backdrop) Summary

The only custom artwork code in Phase 3: an `ILocalImageProvider` that surfaces a Youtarr
channel's `poster.jpg` as the Series **Backdrop** image (ART-02). Jellyfin's built-in
`LocalImageProvider` already claims `poster.jpg` as Series Primary (ART-01) and
`EpisodeLocalImageProvider` already claims per-video thumbnails (ART-03) — this provider does
not touch either; it fills the one gap where Youtarr writes no `fanart`/`backdrop` file. When
`poster.jpg` is absent it returns an empty enumerable and never throws (ART-04). Built TDD
(RED test commit precedes GREEN implementation). Full suite **85/85 green** in Release (80 prior
+ 5 new); publish stays DLL-only.

## What Was Built

| File | Provides |
|------|----------|
| `Providers/YoutarrSeriesImageProvider.cs` | `ILocalImageProvider`. `Name => Constants.ProviderName`. `Supports(item) => item is Series`. `GetImages` builds `Path.Combine(item.Path, "poster.jpg")`; if `!File.Exists` → `LogDebug` + `yield break` (ART-04); else `yield return new LocalImageInfo { FileInfo = new FileSystemMetadata { FullName = posterPath }, Type = ImageType.Backdrop }`. Backdrop only — no Primary. No custom Order. |
| `PluginServiceRegistrator.cs` (+1 line) | `serviceCollection.AddSingleton<ILocalImageProvider, YoutarrSeriesImageProvider>()` as a conservative DI safety net (mirrors `IResolverIgnoreRule`); class remarks updated to document it. |
| `Tests/Providers/YoutarrSeriesImageProviderTests.cs` | 5 cases (see below). |

## Provider Design: Backdrop-Only

`ILocalImageProvider` is a marker interface over `IImageProvider`; it adds only
`GetImages(BaseItem, IDirectoryService) : IEnumerable<LocalImageInfo>`. `Name` and
`Supports(BaseItem)` come from `IImageProvider`. There is **no** `GetSupportedImages` on the
local interface (that lives on `IRemoteImageProvider`) — confirmed by reflection against the
installed `Jellyfin.Controller 10.10.7` assembly.

The architectural responsibility split (RESEARCH "Built-in Provider Decision"):

| Capability | Owner | Notes |
|------------|-------|-------|
| Series Primary (ART-01) | Built-in `LocalImageProvider` (Order=0) | Searches `poster`/`folder`/`cover` → `poster.jpg` matches automatically. Plugin does NOT compete. |
| Series **Backdrop** (ART-02) | **This plugin** | Built-in searches `fanart`/`backdrop`/`background`; Youtarr writes none → custom provider reuses `poster.jpg`. |
| Episode thumbnail (ART-03) | Built-in `EpisodeLocalImageProvider` | Same-basename `.jpg` next to video. Plugin does NOT touch. |
| Graceful degradation (ART-04) | Built-in + this plugin | Both return empty on missing file; this provider `yield break`s, never throws. |

**Why Backdrop only (Pitfall 1):** re-emitting `poster.jpg` as Primary would duplicate the
built-in's claim and risk "image replaced"/flicker on rescans (especially with "Replace all
images"). Returning only `ImageType.Backdrop` keeps one responsibility per provider and avoids
any ordering conflict. The `GetImages_SeriesWithPoster_NeverEmitsPrimary` test asserts no emitted
image carries `ImageType.Primary`.

**Portrait-as-backdrop tradeoff (accepted, no code change):** Youtarr's `poster.jpg` is portrait
(~2:3); Jellyfin's backdrop canvas is 16:9, so the client letter-boxes/crops it. This is a
"better than blank" fallback. A user who drops a real `fanart.jpg` into the channel folder gets a
proper backdrop from the built-in at Order=0 (which takes precedence). Documented in RESEARCH.

## ART-04 Empty Behaviour

`GetImages` is an iterator. When `File.Exists(posterPath)` is false it logs at Debug level and
`yield break`s, producing an empty `IEnumerable<LocalImageInfo>` — no exception, no partial item.
Asserted by `GetImages_SeriesWithoutPoster_ReturnsEmpty_NoThrow`. The path is built with
`Path.Combine(item.Path, "poster.jpg")` where the filename is a constant (not user input), so no
path-traversal surface is introduced.

## DI Registration

`serviceCollection.AddSingleton<ILocalImageProvider, YoutarrSeriesImageProvider>()` was added to
`PluginServiceRegistrator.RegisterServices` alongside the existing `IResolverIgnoreRule`
registration. Per RESEARCH Open Question #2 this is a conservative safety net — `ILocalImageProvider`
implementations may already be auto-discovered by Jellyfin's DI (as `ILocalMetadataProvider<T>` are),
but the explicit registration mirrors the Phase 1 precedent and harms nothing. 03-03's live verify
records whether it is required. No custom `Order` was set (Phase 1 conservative stance).

## Test Coverage (5 new)

1. `GetImages_SeriesWithPoster_ReturnsSingleBackdrop` (ART-02): real `poster.jpg` in the Series temp dir → exactly one `LocalImageInfo`, `Type == ImageType.Backdrop`, `FileInfo.FullName` ends with `poster.jpg`.
2. `GetImages_SeriesWithPoster_NeverEmitsPrimary` (ART-02 boundary): no emitted image has `Type == ImageType.Primary`.
3. `GetImages_SeriesWithoutPoster_ReturnsEmpty_NoThrow` (ART-04): Series temp dir with no `poster.jpg` → empty enumerable, no exception.
4. `Supports_SeriesTrue_NonSeriesFalse`: `Supports(Series)=true`; `Supports(Movie)=false`; `Supports(Episode)=false`.
5. `Name_ReturnsConstantsProviderName`: `Name == Constants.ProviderName == "Youtarr"`.

Fixtures use a 4-byte JPEG-marker blob (`FF D8 FF D9`) in a per-test `System.IO` temp dir,
cleaned up in `finally` — the provider only does `File.Exists`, so the bytes need not be a valid
JPEG. `IDirectoryService` is a bare `Mock<IDirectoryService>.Object` (unused by the provider, which
reads `item.Path` + `File.Exists`).

## Verification Results

- `dotnet build Jellyfin.Plugin.Youtarr/...csproj -c Release`: **0 errors**. The new provider emits only the same analyzer-warning categories the existing `YoutarrSeriesNfoProvider` does — CA1848 (direct `ILogger` call) and CA1062 (DI-supplied `item` not null-validated) — no new categories; `TreatWarningsAsErrors=false`, consistent with established Phase 1 style.
- `dotnet test ...csproj -c Release`: **85/85 passed**, 0 failed, 0 skipped (80 prior + 5 new). No Phase 1/2 regression.
- `grep 'ImageType.Backdrop' YoutarrSeriesImageProvider.cs`: match present. `grep -c 'ImageType.Primary'`: **0**.
- `grep 'AddSingleton<ILocalImageProvider' PluginServiceRegistrator.cs`: match present.
- `dotnet publish -c Release`: output contains **only** `Jellyfin.Plugin.Youtarr.dll` — **zero** `MediaBrowser.*`/`Jellyfin.Controller`/`Jellyfin.Model` runtime DLLs. ExcludeAssets publish-clean invariant preserved.

## TDD Gate Compliance

Plan `type: tdd`. Genuine RED → GREEN observed:

- Task 1 (RED): `test(03-01)` 3e22944 — test project fails to **compile** (CS0246, `YoutarrSeriesImageProvider` type missing). No test passed unexpectedly during RED.
- Task 2 (GREEN): `feat(03-01)` 65c8229 — provider + DI registration; image-provider tests 5/5, full suite 85/85.

No REFACTOR commit needed (clean first pass). Both gate commits present in `git log`.

## Deviations from Plan

### Minor wording adjustment (verification fidelity)

The implementation's XML doc comment originally referenced `ImageType.Primary` in prose
("never emits `<see cref="ImageType.Primary"/>`"), which made the plan's
`grep -c 'ImageType.Primary' ... returns 0` verification report `1` (the doc mention, not emitting
code). Reworded the comment to "never emits a Primary image" so the literal verification passes and
the file contains zero `ImageType.Primary` tokens. Behaviour is unchanged and the runtime
no-Primary guarantee is proven by `GetImages_SeriesWithPoster_NeverEmitsPrimary`. Not a Rule 1-3
deviation — a comment edit to align with the plan's stated verification command.

No bugs, no missing critical functionality, no blocking issues, no architectural (Rule 4) changes,
no auth gates, no checkpoints. No Docker/sudo touched — live ART-01..04 confirmation is 03-03's wave.

## Threat Mitigations Applied

| Threat / Concern | Mitigation | Status |
|------------------|-----------|--------|
| Malformed `poster.jpg` path traversal (Tampering) | `Path.Combine(item.Path, "poster.jpg")` — `item.Path` is Jellyfin-canonicalized; the appended filename is a constant, not user input. | Mitigated (by construction) |
| Scan crash on missing artwork (ART-04 / Availability) | `yield break` on `!File.Exists`; iterator returns empty, never throws. `GetImages_SeriesWithoutPoster_ReturnsEmpty_NoThrow` asserts it. | Mitigated |
| Package installs (slopsquat surface) | No package installs in this plan. | N/A |

## Known Stubs

None. The provider fully implements ART-02 (Backdrop emission) and the custom-path ART-04
(graceful empty). Explicit DI registration is intentionally a conservative safety net pending
03-03's live verify (a planned boundary, not a stub). The portrait-as-backdrop visual tradeoff is
an accepted, documented design choice — not a stub.

## Commits

- `3e22944` test(03-01): add failing tests for YoutarrSeriesImageProvider
- `65c8229` feat(03-01): implement YoutarrSeriesImageProvider (poster.jpg -> Backdrop)

## Self-Check: PASSED

All created files (`YoutarrSeriesImageProvider.cs`, `YoutarrSeriesImageProviderTests.cs`) and the
modified `PluginServiceRegistrator.cs` exist on disk; both per-task commits (3e22944, 65c8229) are
present in git history. Build is clean in Release; full suite 85/85 green; publish output is
DLL-only; verification greps pass (Backdrop present, Primary count 0, DI registration present).
