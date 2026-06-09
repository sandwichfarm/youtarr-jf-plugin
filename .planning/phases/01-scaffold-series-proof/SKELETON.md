# Walking Skeleton — Youtarr Jellyfin Plugin

**Phase:** 1
**Generated:** 2026-06-09

## Capability Proven End-to-End

A Youtarr download folder, pointed at by a Jellyfin 10.10.7 "Shows" library, shows each channel subfolder as a correctly-named Series — delivered by a plugin DLL that the server loads as "Active" — while Youtarr `__prefix` grouping folders are silently ignored.

This exercises the full stack: build (.NET 8 → DLL) → packaging identity (GUID/build.yaml) → plugin load (BasePlugin + DI) → a real `ILocalMetadataProvider<Series>` and `IResolverIgnoreRule` → live verification in a real Jellyfin container.

## Architectural Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Language / runtime | C# / `net8.0` | Jellyfin 10.10.x loads the plugin into its own .NET 8 process; mismatched TFM = load failure. Non-negotiable for a Jellyfin plugin. |
| Plugin SDK | `Jellyfin.Controller` 10.10.7 + `Jellyfin.Model` 10.10.7, both with `<ExcludeAssets>runtime</ExcludeAssets>` | Highest 10.10.x patch; ExcludeAssets prevents bundling Jellyfin runtime DLLs (TypeLoadException at load). |
| Item classification | "Shows" library type + built-in `SeriesResolver` (NO custom `IItemResolver`) | Custom resolvers are officially unsupported in 10.10.x and cause duplicate entries; the library type does the classification for free. |
| Series metadata | `ILocalMetadataProvider<Series>` synthesizing name from folder name, fallback to NFO `<studio>` | Youtarr writes no `tvshow.nfo`; the provider returns `HasMetadata = true` so it is authoritative. |
| `__prefix` suppression | `IResolverIgnoreRule` returning true for directories starting with `__` | Officially supported scan-time hook; evaluated before resolution so prefix folders never become Series. |
| NFO parsing | `System.Xml.Linq` (`XDocument`) directly | `BaseNfoParser<T>` lives in the server-only `MediaBrowser.XbmcMetadata` assembly (not on NuGet); Youtarr NFOs are simple `<movie>` docs. |
| Plugin identity | One permanent GUID generated at scaffold, mirrored in `Plugin.cs` `StaticId` and `build.yaml` | GUID drift orphans config (`config/plugins/<GUID>/config.xml`); the value must never change across releases. |
| Packaging | `jprm` reading `build.yaml`, `targetAbi: "10.10.0.0"` | targetAbi is a *minimum* — 10.10.0.0 lets any 10.10.x server install the plugin. Full packaging is Phase 4; build.yaml is established now. |
| Directory layout | `Jellyfin.Plugin.Youtarr/` (src) + `Jellyfin.Plugin.Youtarr.Tests/` (xUnit) + `test/jellyfin-load-test/` (Docker harness) + `scripts/` | Matches RESEARCH "Recommended Project Structure"; testable string/XML logic isolated in `Utils/PathUtils.cs`. |
| Verification env | `jellyfin/jellyfin:10.10.7` via docker-compose, media mounted `:ro` | The only environment where "Active" + Series resolution can be truthfully confirmed; read-only mount enforces the no-file-mutation constraint. |

## Stack Touched in Phase 1

- [x] Project scaffold — .NET 8 class library + xUnit test project + build.yaml packaging descriptor (plan 01-01, 01-02)
- [x] Routing equivalent — plugin entry point registers itself + a config page (`IHasWebPages`) and DI services (`RegisterServices`) (plan 01-01, 01-02)
- [x] Real "read" — `YoutarrSeriesNfoProvider` reads channel folder names + `<studio>` from on-disk NFOs; `YoutarrPrefixIgnoreRule` reads directory names at scan time (plan 01-02). (Plugin is read-only by design; no writes in any phase.)
- [x] UI wired to logic — Jellyfin Dashboard shows the plugin Active and the Shows library renders channel folders as Series (plan 01-03 live verification)
- [x] Deployment — `scripts/deploy-plugin.sh` + docker-compose run a full Jellyfin 10.10.7 stack with the plugin loaded; documented dev iteration loop in `test/jellyfin-load-test/README.md` (plan 01-03)

## Out of Scope (Deferred to Later Slices)

Explicitly NOT in the skeleton — do not re-litigate Phase 1's minimalism:

- Episodes, year-seasons, `ParentIndexNumber` season grouping → Phase 2 (highest-risk; must validate virtual seasons on a live instance first)
- Per-video metadata mapping (`<movie>` → Episode: title, plot, premiered, runtime, genres, tags, mpaa, YouTube id) → Phase 2
- Flat vs. nested layout handling (CMP-01/CMP-02) → Phase 2
- Channel poster/backdrop + episode thumbnail artwork providers → Phase 3
- A functional configuration page with real controls (year-seasons toggle, numbering scheme, max description length) → Phase 3 (Phase 1 ships only an embedded HTML stub)
- Versioned ZIP + publishable `manifest.json` via jprm (PKG-01/PKG-02) → Phase 4 (build.yaml exists now; packaging run deferred)
- Any network/API dependency, Youtarr API key, or file mutation → permanently out of scope (file-only, read-only design)

## Subsequent Slice Plan

Each later phase adds one vertical slice on top of this skeleton without altering its architectural decisions (library type, SDK versions, GUID, read-only file-only model):

- **Phase 2:** Each video becomes an Episode under its channel's Series, grouped into year-seasons, with full per-video NFO metadata. Validate virtual-season creation from `ParentIndexNumber` on a live 10.10.x instance before building the full pipeline.
- **Phase 3:** Channel artwork as Series poster/backdrop, per-video thumbnails as Episode images, and a fully functional Dashboard configuration page.
- **Phase 4:** Produce the installable versioned ZIP and a repository `manifest.json` from the existing `build.yaml` via jprm.
