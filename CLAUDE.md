<!-- GSD:project-start source:PROJECT.md -->

## Project

**Youtarr Jellyfin Plugin**

A Jellyfin plugin (C#/.NET, targeting Jellyfin 10.10.x) that turns a [Youtarr](https://github.com/DialmasterOrg/Youtarr) download folder into a clean, well-organized Jellyfin library. Instead of a flat wall of videos, each YouTube channel becomes a **Show (Series)**, each video becomes an **Episode**, and episodes are grouped into **seasons by upload year**. Channel artwork is surfaced as the Series poster/backdrop. It works entirely from the NFO files and images Youtarr writes to disk — no API key required.

It is modeled on [`tubearchivist-jf-plugin`](https://github.com/tubearchivist/tubearchivist-jf-plugin) but solves a different problem: TubeArchivist keeps metadata in its own database and writes nothing to disk, so its plugin *must* call an API. Youtarr already writes per-video `<movie>` NFOs, poster images, and embedded MP4 metadata, so this plugin can be file-only. The value it adds over Jellyfin's built-in NFO import is **structure** — grouping the flat collection into channels-as-shows with year seasons — not basic metadata reading.

**Core Value:** A Youtarr download folder, pointed at by a Jellyfin library, shows up as channels-grouped Shows with year seasons and correct per-video metadata — not a flat undifferentiated wall of videos. If everything else fails, the channel→Show / video→Episode grouping must work.

### Constraints

- **Tech stack**: C# / .NET (the Jellyfin plugin SDK is .NET-only) — non-negotiable for a Jellyfin plugin. Target the .NET version matching Jellyfin 10.10.x.
- **Compatibility**: Target Jellyfin 10.10.x (current stable); note a minimum supported version. Plugin must load via Jellyfin's plugin API for that release line.
- **Integration model**: File-only by default — read NFO/images/embedded metadata from the media folders. No network dependency on a running Youtarr instance.
- **Data source shape**: Bound by what Youtarr actually writes to disk (NFO schema, image filenames, folder layout). The plugin must map *Youtarr's* on-disk conventions, which differ from TubeArchivist's.
- **Distribution**: Personal use first, but architected cleanly so it can be published to a Jellyfin plugin repository later (proper versioning, plugin manifest, build/packaging, ideally CI).

<!-- GSD:project-end -->

<!-- GSD:stack-start source:research/STACK.md -->

## Technology Stack

## Version Baseline: 10.10 vs 10.11

- Jellyfin 10.10.x → `net8.0`, `Jellyfin.Controller` / `Jellyfin.Model` at `10.10.7`
- Jellyfin 10.11.x → `net9.0`, `Jellyfin.Controller` / `Jellyfin.Model` at `10.11.11`

## Recommended Stack

### Core Technologies

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| .NET / C# | `net8.0` | Plugin runtime | Jellyfin 10.10.x ships on .NET 8. This is non-negotiable — the server loads the plugin into its own process; mismatched TFM = load failure. Verified: `Jellyfin.Controller 10.10.7` targets `net8.0`. |
| `Jellyfin.Controller` | `10.10.7` | Plugin SDK, all plugin interfaces | Provides `ILocalMetadataProvider<T>`, `IRemoteImageProvider`, `IHasWebPages`, `BasePlugin<TConfig>`, `IScheduledTask`, `ILibraryPostScanTask`. This is the highest 10.10.x patch. |
| `Jellyfin.Model` | `10.10.7` | Entity types (`Series`, `Season`, `Episode`, `BaseItem`, `MetadataResult<T>`) | Required alongside Controller; versions must match exactly. `Jellyfin.Controller 10.10.7` declares `Jellyfin.Model >= 10.10.7` as a dependency. |
| `Microsoft.NET.Sdk` | (SDK project) | Build SDK | Standard SDK-style project. Used by all official Jellyfin plugins. |

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

## Installation

# Create the project (SDK-style class library)

# Add Jellyfin SDK packages (ExcludeAssets is critical — set in csproj, not CLI)

# Analyzers (dev-only, PrivateAssets="All")

# Test project

# Packaging tool

## Plugin Interface Recommendations

### Use These Interfaces

### Avoid These Approaches

## build.yaml / Packaging Format

## CI Patterns (GitHub Actions)

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative |
|-------------|-------------|-------------------------|
| `ILocalMetadataProvider<Series/Season/Episode>` | `IRemoteMetadataProvider` | When the data source is a network API (e.g., YouTube Data API optional enrichment in a later phase) |
| `net8.0` + `10.10.7` packages | `net9.0` + `10.11.x` packages | When targeting Jellyfin 10.11.x (current stable). Upgrade path: bump TargetFramework, package versions, and `targetAbi` in `build.yaml`. No interface changes expected. |
| `System.Xml.Linq` (XDocument) for NFO parsing | Newtonsoft.Json / `XmlSerializer` | Use `XmlSerializer` only if the NFO schema becomes complex or NFO-writing is needed. `XDocument` is sufficient for read-only parsing of a known-small element set. |
| `ILocalImageProvider` | `IRemoteImageProvider` | Use `IRemoteImageProvider` if image bytes must be proxied through the plugin (e.g., images not in standard-named files). Adds complexity. |
| jprm for packaging | Manual `dotnet publish` + zip | For quick personal builds without the manifest workflow. `dotnet publish -c Release -o ./dist` then zip the output. |

## What NOT to Use

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| Custom `IItemResolver` | No official plugin resolver support as of Jellyfin 10.11 (PR #13615 closed stale). Hacks break on server updates. | TV Shows library type + correct folder layout (Series/Season/Episode hierarchy). Providers supply metadata. |
| `Jellyfin.Controller` / `Jellyfin.Model` with `ExcludeAssets` omitted | Without `<ExcludeAssets>runtime</ExcludeAssets>`, the Jellyfin runtime DLLs are copied into the plugin output directory. The server finds two copies of the same assemblies and the plugin fails to load or type-resolution breaks. | Always add `<ExcludeAssets>runtime</ExcludeAssets>` to both references. |
| `IServerEntryPoint` | Deprecated in favor of standard .NET `IHostedService`. May be removed in a future Jellyfin version. | Register an `IHostedService` via the plugin's DI `RegisterServices` override if startup initialization is needed. |
| `MediaBrowser.XbmcMetadata` (internal) | Not exposed as a NuGet package for plugin authors. The `BaseNfoParser<T>` base class is in the server assembly. Attempting to reference it via a file/project reference creates a brittle dependency on server internals. | Use `System.Xml.Linq` (XDocument) to parse NFO XML directly. |
| Jellyfin 10.9.x as target | Outdated; 10.10.x has been stable since October 2024. The plugin template's current `build.yaml` already moved to `targetAbi: "10.9.0.0"` as minimum, but new plugins should not regress to 10.9 conventions. | Target `10.10.0.0` as the minimum ABI. |
| Targeting 10.11.x immediately | The user's current server may be on 10.10.x; 10.11.x upgrade path has been bumpy (EF Core migration issues). A 10.11 plugin refuses to install on 10.10. | Build for 10.10.x; design for easy upgrade later. |

## Version Compatibility

| Package | Compatible With | Notes |
|---------|-----------------|-------|
| `Jellyfin.Controller 10.10.7` | `net8.0`, Jellyfin server 10.10.x | Highest patch in the 10.10.x line. Verified on NuGet (released April 5, 2025). |
| `Jellyfin.Model 10.10.7` | `net8.0`, Jellyfin server 10.10.x | Declared as dependency of `Jellyfin.Controller 10.10.7` — versions must be identical. |
| `Jellyfin.Controller 10.11.11` | `net9.0`, Jellyfin server 10.11.x | Current latest as of June 2026. Switch the entire stack simultaneously: bump `TargetFramework`, both NuGet versions, `targetAbi` in `build.yaml`, and `dotnet-version` in CI to `9.0.x`. |
| `xUnit 2.x` | `net8.0` | Standard .NET test framework; no Jellyfin-version dependency. |
| `Moq 4.x` | `net8.0` | Interface mocking; no Jellyfin-version dependency. |

## Key Pitfall: Episode `ParentIndexNumber` / Season Assignment

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

<!-- GSD:stack-end -->

<!-- GSD:conventions-start source:CONVENTIONS.md -->

## Conventions

Conventions not yet established. Will populate as patterns emerge during development.
<!-- GSD:conventions-end -->

<!-- GSD:architecture-start source:ARCHITECTURE.md -->

## Architecture

Architecture not yet mapped. Follow existing patterns found in the codebase.
<!-- GSD:architecture-end -->

<!-- GSD:skills-start source:skills/ -->

## Project Skills

No project skills found. Add skills to any of: `.claude/skills/`, `.agents/skills/`, `.cursor/skills/`, `.github/skills/`, or `.codex/skills/` with a `SKILL.md` index file.
<!-- GSD:skills-end -->

<!-- GSD:workflow-start source:GSD defaults -->

## GSD Workflow Enforcement

Before using Edit, Write, or other file-changing tools, start work through a GSD command so planning artifacts and execution context stay in sync.

Use these entry points:

- `/gsd:quick` for small fixes, doc updates, and ad-hoc tasks
- `/gsd:debug` for investigation and bug fixing
- `/gsd:execute-phase` for planned phase work

Do not make direct repo edits outside a GSD workflow unless the user explicitly asks to bypass it.
<!-- GSD:workflow-end -->

<!-- GSD:profile-start -->

## Developer Profile

> Profile not yet configured. Run `/gsd:profile-user` to generate your developer profile.
> This section is managed by `generate-claude-profile` -- do not edit manually.
<!-- GSD:profile-end -->
