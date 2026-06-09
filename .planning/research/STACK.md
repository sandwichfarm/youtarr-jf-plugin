# Stack Research

**Domain:** Jellyfin 10.10.x plugin (C#/.NET) — YouTube archive folder → Series/Season/Episode library
**Researched:** 2026-06-09
**Confidence:** HIGH (all key findings verified against official NuGet, official plugin template, reference plugins, and Jellyfin source)

---

## Version Baseline: 10.10 vs 10.11

**Critical context:** The project spec says "target Jellyfin 10.10.x." As of June 2026, the current stable release is **Jellyfin 10.11.11** (released June 6, 2026). Jellyfin 10.10.7 was the last patch of the 10.10 line. The upstream migration path explicitly requires 10.10.7 before upgrading to 10.11. Many community users are still on 10.10.x while 10.11's EF Core migration stabilizes.

**The fork matters for plugins:**
- Jellyfin 10.10.x → `net8.0`, `Jellyfin.Controller` / `Jellyfin.Model` at `10.10.7`
- Jellyfin 10.11.x → `net9.0`, `Jellyfin.Controller` / `Jellyfin.Model` at `10.11.11`

A plugin built for `net8.0` / `targetAbi: "10.10.0.0"` will **not** load in a 10.11 server (ABI version mismatch). They are separate build targets.

**Recommendation:** Build for 10.10.x now (personal use baseline). Structure the project so upgrading to 10.11.x later requires only bumping the NuGet versions, TargetFramework, and targetAbi — not a code rewrite. The interfaces used (ILocalMetadataProvider, IRemoteImageProvider, etc.) are stable across both versions.

---

## Recommended Stack

### Core Technologies

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| .NET / C# | `net8.0` | Plugin runtime | Jellyfin 10.10.x ships on .NET 8. This is non-negotiable — the server loads the plugin into its own process; mismatched TFM = load failure. Verified: `Jellyfin.Controller 10.10.7` targets `net8.0`. |
| `Jellyfin.Controller` | `10.10.7` | Plugin SDK, all plugin interfaces | Provides `ILocalMetadataProvider<T>`, `IRemoteImageProvider`, `IHasWebPages`, `BasePlugin<TConfig>`, `IScheduledTask`, `ILibraryPostScanTask`. This is the highest 10.10.x patch. |
| `Jellyfin.Model` | `10.10.7` | Entity types (`Series`, `Season`, `Episode`, `BaseItem`, `MetadataResult<T>`) | Required alongside Controller; versions must match exactly. `Jellyfin.Controller 10.10.7` declares `Jellyfin.Model >= 10.10.7` as a dependency. |
| `Microsoft.NET.Sdk` | (SDK project) | Build SDK | Standard SDK-style project. Used by all official Jellyfin plugins. |

Both packages **must** be referenced with `<ExcludeAssets>runtime</ExcludeAssets>` in the csproj. Without this flag, the runtime DLLs are copied into the plugin output and the server's assembly loader finds two copies of the same types — the plugin will fail to register.

```xml
<PackageReference Include="Jellyfin.Controller" Version="10.10.7">
  <ExcludeAssets>runtime</ExcludeAssets>
</PackageReference>
<PackageReference Include="Jellyfin.Model" Version="10.10.7">
  <ExcludeAssets>runtime</ExcludeAssets>
</PackageReference>
```

### Supporting Libraries

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `System.Xml.Linq` | (inbox, .NET 8 BCL) | NFO / XML parsing | Always. `XDocument` / `XElement` are sufficient for Kodi-format `<movie>` NFOs. No additional NuGet package needed — it is part of the BCL. Use `XDocument.Load(path)` + `XElement.Element("premiered")` etc. Do not ship a full XmlSerializer-based pipeline; Youtarr NFOs are simple and the element set is well-known. |
| `Microsoft.Extensions.Logging.Abstractions` | (inbox via Jellyfin.Controller) | Plugin logging | Always. Inject `ILogger<T>` via DI. Already a transitive dependency of `Jellyfin.Controller`. |
| `Newtonsoft.Json` | `13.0.3` | JSON (if needed for any sidecar format) | Only if Youtarr produces `.info.json` or similar. Not currently required — Youtarr writes NFO + images + embedded MP4 metadata. Keep as optional; add only if reading embedded metadata from MP4 tags. |
| `TagLibSharp` | latest stable | MP4 embedded metadata (ID3/MP4 tags) | Only if NFO-based metadata is insufficient and embedded tags in `.mp4` must be read as fallback. Medium complexity to add. Defer to later phase. |

### Development Tools

| Tool | Purpose | Notes |
|------|---------|-------|
| `dotnet SDK 8.x` | Build, publish, pack | Target `net8.0`. Use `dotnet publish -c Release` to produce the DLL. |
| `jprm` (Python) | Plugin packaging — produces the versioned `.zip` and updates `manifest.json` | Install via `pip install jprm`. Used by the official Jellyfin template and reference plugins. Reads `build.yaml` to produce `<PluginName>_<version>.zip` and generates/updates the repository `manifest.json`. The TubeArchivist plugin CI workflow invokes jprm to build and then creates the GitHub Release. |
| `xUnit` | Unit testing | Standard .NET test framework. Used across the Jellyfin ecosystem. Pair with `Moq` for mocking `ILibraryManager`, `IDirectoryService`, etc. Most Jellyfin plugins do not ship tests; add for NFO parsing logic at minimum. |
| `Moq` | Mocking in tests | Mock injected Jellyfin interfaces (`IDirectoryService`, `ILogger<T>`) without spinning up a real server. |
| GitHub Actions | CI/CD | Build on push, produce release zip + manifest on tag. See CI Patterns section below. |
| Visual Studio Code or Rider | IDE | Both work. `.editorconfig` + `jellyfin.ruleset` enforce Jellyfin code style. |

---

## Installation

```bash
# Create the project (SDK-style class library)
dotnet new classlib -f net8.0 -n Jellyfin.Plugin.Youtarr

# Add Jellyfin SDK packages (ExcludeAssets is critical — set in csproj, not CLI)
dotnet add package Jellyfin.Controller --version 10.10.7
dotnet add package Jellyfin.Model --version 10.10.7

# Analyzers (dev-only, PrivateAssets="All")
dotnet add package SerilogAnalyzer --version 0.15.0
dotnet add package StyleCop.Analyzers --version 1.2.0-beta.556
dotnet add package SmartAnalyzers.MultithreadingAnalyzer --version 1.1.31

# Test project
dotnet new xunit -n Jellyfin.Plugin.Youtarr.Tests -f net8.0
dotnet add Jellyfin.Plugin.Youtarr.Tests package Moq

# Packaging tool
pip install jprm
```

After adding via CLI, **manually edit the csproj** to add `<ExcludeAssets>runtime</ExcludeAssets>` to both Jellyfin package references. The `dotnet add` CLI does not support this flag directly.

---

## Plugin Interface Recommendations

This is the core architectural decision. The goal is: **channel folder → Series, year subfolder (or virtual year) → Season, video file → Episode, with metadata read from Youtarr NFO files and images found from local folder images.**

### Use These Interfaces

**`ILocalMetadataProvider<Series>`**
Implement this to read channel-level metadata. When Jellyfin scans a TV Shows library and encounters a channel folder (e.g., `./ChannelName/`), this provider is called to supply the `Series` metadata. In `GetMetadata()`, read a `tvshow.nfo` (if present) or synthesize Series info from the channel folder name and any Youtarr-generated channel image/metadata. Set `Name`, `Overview`, `ProviderIds` (add a YouTube channel ID under a custom key). This is the correct interface because Youtarr already writes sidecar files and channel images to disk — no network call needed.

**`ILocalMetadataProvider<Season>`**
Implement to supply Season-level metadata. The season folder is either a physical subdirectory (if Youtarr groups by year) or a virtual construct the plugin creates. For file-based seasons: read the folder name as the season year, set `IndexNumber` (e.g., year 2023 → season index 2023 or a sequential number), set `Name` ("2023"). Since seasons have no NFO, synthesize from the folder name.

**`ILocalMetadataProvider<Episode>`**
Implement to read per-video metadata from Youtarr NFO files. Each video file `./ChannelName/[year]/VideoTitle [ytid].mp4` has a companion `VideoTitle [ytid].nfo`. In `GetMetadata()`: find the `.nfo` sidecar by replacing the file extension, parse the `<movie>`-rooted XML using `XDocument`, extract `title`, `plot`, `premiered`, `runtime`, `studio`, `genre`, `tag`, `uniqueid type="youtube"`. Set `Name`, `Overview`, `PremiereDate`, `IndexNumber` (episode number within the year group — derive from sort order or a counter), `ParentIndexNumber` (season year). Return a `MetadataResult<Episode>` with `HasMetadata = true`.

**`ILocalImageProvider` (for Series)**
Implement to surface channel artwork (poster/banner/backdrop) from local files. The `LocalImageProvider` built into Jellyfin looks for `poster.jpg`, `folder.jpg`, `fanart.jpg`, `backdrop.jpg` in the Series folder. If Youtarr writes standard-named images, the built-in provider may be sufficient. Implement a custom `ILocalImageProvider` only if Youtarr uses non-standard image names that the built-in provider won't find. Implementing this is lower risk than `IRemoteImageProvider` since no network is involved.

**`IRemoteImageProvider` (optional, for Series)**
Acceptable alternative to `ILocalImageProvider` for the Series poster/backdrop if you want to serve the image bytes directly from the plugin (e.g., read from a channel metadata file that isn't standard-named). TubeArchivist plugin uses `IRemoteImageProvider` even for on-disk images by pointing its HTTP response at the image path. For a file-only plugin, `ILocalImageProvider` is cleaner.

**`BasePlugin<PluginConfiguration>`**
The plugin entry point. Must inherit this base class. `PluginConfiguration` extends `BasePluginConfiguration` and holds the user-configurable settings (year-seasons toggle, episode numbering scheme). The DI container discovers and registers the plugin automatically.

**`IHasWebPages`**
Implement on the `Plugin` class alongside `BasePlugin<T>` to expose a configuration HTML page embedded in the DLL. Return a `PluginPageInfo` pointing to the embedded resource at `Jellyfin.Plugin.Youtarr.Configuration.configPage.html`. This is how the TubeArchivist plugin and all official plugins expose their settings page.

**`ILibraryPostScanTask`**
Useful for post-scan maintenance — e.g., reconciling episode numbers after a library scan completes, or reordering episodes within a season. Implement if the provider-based approach alone leaves gaps (e.g., episode numbers out of order across multiple scans). Not required for the MVP.

**`IScheduledTask`**
Use for recurring background work if needed (e.g., periodic re-read of updated NFOs). Deferred to a later phase. The core metadata providers already run on every library scan.

### Avoid These Approaches

**Custom `IItemResolver` / Custom folder resolution**
Official custom resolver support was proposed in PR #13615 (closed as stale, May 2026) and remains unsupported by the server. Third-party resolvers can be made to run via `ResolverPriority.First` hacks, but they conflict with default resolvers and break unpredictably on server updates. **Do not implement a custom resolver.** Instead, use the correct library content type (TV Shows) and folder structure conventions, then let Jellyfin's built-in resolver assign Series/Season/Episode types from the folder hierarchy. The providers then supply metadata on top of the correctly-typed items.

**Folder structure dependency on custom names**
Jellyfin's built-in Show resolver requires the folder layout to match its conventions: `SeriesFolder/Season N/file.ext`. If Youtarr organizes as `ChannelName/YYYY/video.mp4`, that is a two-level hierarchy which maps naturally to Series → Season (year folder) → Episode. **This layout works.** The file must include episode/season indicators OR the `ILocalMetadataProvider<Episode>` must supply `IndexNumber` and `ParentIndexNumber` explicitly (Jellyfin fills these from the filename parser first, then the provider can override — see Pitfall below).

**`IRemoteMetadataProvider` for the core providers**
Avoid for the primary metadata path. This plugin's core value is file-only operation. `IRemoteMetadataProvider` implies a network call and a search-results API (`GetSearchResults`). Use `ILocalMetadataProvider` for Series/Season/Episode since Youtarr writes everything to disk. `IRemoteMetadataProvider` is only appropriate if adding an optional YouTube Data API enrichment path in a later phase.

**Jellyfin's internal `BaseNfoParser<T>` / `BaseNfoProvider<T>` subclassing**
These are in `MediaBrowser.XbmcMetadata`, which is part of the Jellyfin server assembly, not the plugin SDK. They are not exposed as a public NuGet package for plugin authors to subclass. Use `System.Xml.Linq` directly to parse the `<movie>`-rooted NFO. The field set is small and well-known from the PROJECT.md sample NFO.

**`IServerEntryPoint`**
Deprecated in favor of `IHostedService` (standard .NET) in recent Jellyfin releases. If startup initialization is needed, use `IHostedService` registered via DI in the plugin's `RegisterServices` override.

---

## build.yaml / Packaging Format

The `build.yaml` file at the repository root drives both jprm packaging and the GitHub Actions release workflow. For a 10.10.x plugin:

```yaml
name: "YoutarrMetadata"
guid: "<generate-with-uuidgen-or-dotnet-guid>"
version: "1.0.0.0"
targetAbi: "10.10.0.0"
framework: "net8.0"
overview: "Organize Youtarr downloads as Series/Season/Episode in Jellyfin"
description: >
  Maps a Youtarr channel download folder into a Jellyfin TV Shows library.
  Each channel becomes a Series, videos become Episodes grouped by upload year.
  Reads metadata from Youtarr NFO files. No API key required.
category: "Metadata"
owner: "<yourname>"
artifacts:
  - "Jellyfin.Plugin.Youtarr.dll"
changelog: >
  Initial release.
```

The `targetAbi` value `"10.10.0.0"` is the minimum Jellyfin server version that will load this plugin. Jellyfin enforces ABI compatibility — a plugin with `targetAbi: "10.10.0.0"` will not install into a server running 10.9.x.

The `manifest.json` (for publishing to a plugin repository) is generated by jprm from `build.yaml` + the release zip checksum. Its structure is a JSON array of plugin objects; each version entry contains `version`, `targetAbi`, `sourceUrl`, `checksum` (MD5), and `timestamp`.

---

## CI Patterns (GitHub Actions)

Reference pattern from `tubearchivist-jf-plugin/.github/workflows/build.yaml`:

```yaml
on:
  push:
    tags: ['v*']

jobs:
  release:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'          # net8.0 for 10.10.x target
      - name: Get version
        run: echo "VERSION=${GITHUB_REF_NAME#v}" >> $GITHUB_ENV
      - name: Build plugin
        run: |
          pip install jprm
          jprm plugin build . --version "$VERSION"
      - name: Create release
        uses: ncipollo/release-action@v1
        with:
          artifacts: "bin/*.zip"
          bodyFile: "CHANGELOG.md"
      - name: Update manifest
        run: |
          jprm repo add --plugin-url "$RELEASE_URL" manifest.json bin/*.zip
          git add manifest.json
          git commit -m "Update manifest for $VERSION"
          git push
```

For a private/personal plugin, the manifest update step can be skipped. A manual DLL drop (copy the `.zip` contents into `<jellyfin-data>/plugins/Youtarr_<version>/`) is sufficient for personal use.

---

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative |
|-------------|-------------|-------------------------|
| `ILocalMetadataProvider<Series/Season/Episode>` | `IRemoteMetadataProvider` | When the data source is a network API (e.g., YouTube Data API optional enrichment in a later phase) |
| `net8.0` + `10.10.7` packages | `net9.0` + `10.11.x` packages | When targeting Jellyfin 10.11.x (current stable). Upgrade path: bump TargetFramework, package versions, and `targetAbi` in `build.yaml`. No interface changes expected. |
| `System.Xml.Linq` (XDocument) for NFO parsing | Newtonsoft.Json / `XmlSerializer` | Use `XmlSerializer` only if the NFO schema becomes complex or NFO-writing is needed. `XDocument` is sufficient for read-only parsing of a known-small element set. |
| `ILocalImageProvider` | `IRemoteImageProvider` | Use `IRemoteImageProvider` if image bytes must be proxied through the plugin (e.g., images not in standard-named files). Adds complexity. |
| jprm for packaging | Manual `dotnet publish` + zip | For quick personal builds without the manifest workflow. `dotnet publish -c Release -o ./dist` then zip the output. |

---

## What NOT to Use

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| Custom `IItemResolver` | No official plugin resolver support as of Jellyfin 10.11 (PR #13615 closed stale). Hacks break on server updates. | TV Shows library type + correct folder layout (Series/Season/Episode hierarchy). Providers supply metadata. |
| `Jellyfin.Controller` / `Jellyfin.Model` with `ExcludeAssets` omitted | Without `<ExcludeAssets>runtime</ExcludeAssets>`, the Jellyfin runtime DLLs are copied into the plugin output directory. The server finds two copies of the same assemblies and the plugin fails to load or type-resolution breaks. | Always add `<ExcludeAssets>runtime</ExcludeAssets>` to both references. |
| `IServerEntryPoint` | Deprecated in favor of standard .NET `IHostedService`. May be removed in a future Jellyfin version. | Register an `IHostedService` via the plugin's DI `RegisterServices` override if startup initialization is needed. |
| `MediaBrowser.XbmcMetadata` (internal) | Not exposed as a NuGet package for plugin authors. The `BaseNfoParser<T>` base class is in the server assembly. Attempting to reference it via a file/project reference creates a brittle dependency on server internals. | Use `System.Xml.Linq` (XDocument) to parse NFO XML directly. |
| Jellyfin 10.9.x as target | Outdated; 10.10.x has been stable since October 2024. The plugin template's current `build.yaml` already moved to `targetAbi: "10.9.0.0"` as minimum, but new plugins should not regress to 10.9 conventions. | Target `10.10.0.0` as the minimum ABI. |
| Targeting 10.11.x immediately | The user's current server may be on 10.10.x; 10.11.x upgrade path has been bumpy (EF Core migration issues). A 10.11 plugin refuses to install on 10.10. | Build for 10.10.x; design for easy upgrade later. |

---

## Version Compatibility

| Package | Compatible With | Notes |
|---------|-----------------|-------|
| `Jellyfin.Controller 10.10.7` | `net8.0`, Jellyfin server 10.10.x | Highest patch in the 10.10.x line. Verified on NuGet (released April 5, 2025). |
| `Jellyfin.Model 10.10.7` | `net8.0`, Jellyfin server 10.10.x | Declared as dependency of `Jellyfin.Controller 10.10.7` — versions must be identical. |
| `Jellyfin.Controller 10.11.11` | `net9.0`, Jellyfin server 10.11.x | Current latest as of June 2026. Switch the entire stack simultaneously: bump `TargetFramework`, both NuGet versions, `targetAbi` in `build.yaml`, and `dotnet-version` in CI to `9.0.x`. |
| `xUnit 2.x` | `net8.0` | Standard .NET test framework; no Jellyfin-version dependency. |
| `Moq 4.x` | `net8.0` | Interface mocking; no Jellyfin-version dependency. |

---

## Key Pitfall: Episode `ParentIndexNumber` / Season Assignment

**Verified from Jellyfin issue #14080:** When an `IRemoteMetadataProvider<Episode>` (and likely `ILocalMetadataProvider<Episode>`) returns a `MetadataResult<Episode>` with `ParentIndexNumber` set, the merge logic in `MetadataService` only overwrites the existing `ParentIndexNumber` if `replaceData` is true OR if the target has no value. Because Jellyfin's filename parser runs first and populates `ParentIndexNumber` from path patterns before the provider runs, the provider's value can be silently discarded.

**Mitigation strategy:** Name video files and/or season folders to match what Jellyfin's regex expects for season/episode number extraction. A file at `ChannelName/2023/VideoTitle.mp4` inside a folder named `2023` will be parsed differently than `ChannelName/Season 2023/VideoTitle.mp4`. Test the naming pattern against a live Jellyfin 10.10.x instance early, before building the full metadata pipeline. Research this in the implementation phase.

---

## Sources

- NuGet Gallery — `Jellyfin.Controller 10.10.7` (net8.0, released 2025-04-05): https://www.nuget.org/packages/Jellyfin.Controller/10.10.7
- NuGet Gallery — `Jellyfin.Controller 10.10.6` dependencies verified: https://www.nuget.org/packages/Jellyfin.Controller/10.10.6
- TubeArchivist JF Plugin csproj (Jellyfin.Controller 10.11.0, net9.0 — reference baseline): https://github.com/tubearchivist/tubearchivist-jf-plugin/blob/master/Jellyfin.Plugin.TubeArchivistMetadata/Jellyfin.Plugin.TubeArchivistMetadata.csproj
- Jellyfin Plugin Template build.yaml (targetAbi 10.9.0.0, net8.0 — template not yet updated to 10.10): https://github.com/jellyfin/jellyfin-plugin-template/blob/master/build.yaml
- EDL Plugin build.yaml (verified targetAbi "10.10.0.0", framework "net8.0"): https://github.com/endrl/jellyfin-plugin-edl/blob/main/build.yaml
- YoutubeMetadata Plugin (ILocalMetadataProvider<Series/Season/Episode> reference implementation): https://github.com/ankenyr/jellyfin-youtube-metadata-plugin
- Jellyfin MetadataManagement DeepWiki (provider pipeline architecture): https://deepwiki.com/jellyfin/jellyfin/2.2-metadata-management
- Jellyfin issue #14080 — Plugin cannot change Episode ParentIndexNumber: https://github.com/jellyfin/jellyfin/issues/14080
- Jellyfin discussion #5732 — Custom resolver support status: https://github.com/jellyfin/jellyfin/discussions/5732
- jprm (Jellyfin Plugin Repository Manager): https://github.com/oddstr13/jellyfin-plugin-repository-manager
- Jellyfin 10.11.0 release (net9.0 upgrade confirmation): https://jellyfin.org/posts/jellyfin-release-10.11.0/
- LocalImageProvider source (image file patterns for Series vs Episode): https://github.com/jellyfin/jellyfin/blob/master/MediaBrowser.LocalMetadata/Images/LocalImageProvider.cs
- SeriesNfoProvider source (tvshow.nfo discovery pattern): https://github.com/jellyfin/jellyfin/blob/master/MediaBrowser.XbmcMetadata/Providers/SeriesNfoProvider.cs
- BaseNfoParser source (XML element handling): https://github.com/jellyfin/jellyfin/blob/master/MediaBrowser.XbmcMetadata/Parsers/BaseNfoParser.cs

---

*Stack research for: Jellyfin 10.10.x plugin (C#/.NET), Youtarr YouTube-archive → Series/Season/Episode*
*Researched: 2026-06-09*
