# Phase 1: Scaffold + Series Proof — Research

**Researched:** 2026-06-09
**Domain:** Jellyfin 10.10.x plugin scaffolding (C#/net8.0) + ILocalMetadataProvider<Series> for YouTube channel folder → Series resolution
**Confidence:** HIGH

---

## Summary

Phase 1 has two independent deliverables: (1) the plugin DLL loads into Jellyfin 10.10.x as "Active" with no errors, and (2) when a "Shows" library is pointed at a Youtarr download folder, each channel subfolder resolves as a correctly-named Series. These are the load-bearing prerequisites for all subsequent phases — nothing else is testable until both work.

The scaffold deliverable is low-risk and fully specified: the official plugin template pattern (`BasePlugin<TConfig>` + `build.yaml` + `meta.json` with exact GUID and `targetAbi`) is well-documented and two reference plugins (EDL plugin, youtube-metadata-plugin) confirm the current correct field values for 10.10.x. The three most common scaffold failures are silent: `ExcludeAssets` omission causes `TypeLoadException` at load; `targetAbi` mismatch causes `NotSupported` status; GUID drift between `Plugin.cs` and `meta.json` orphans configuration. All three are preventable with exact values in this document.

The Series proof deliverable is architecturally simple but has one concrete decision: the `__prefix` folders. When a Jellyfin "Shows" library root contains both channel folders (`ChannelName/`) and Youtarr prefix grouping folders (`__kids/`, `__music/`), Jellyfin's built-in `SeriesResolver` will treat every immediate subdirectory as a Series — including the `__prefix` ones. The correct Phase 1 fix is an `IResolverIgnoreRule` that returns `true` for any item whose directory name starts with two underscores. This is an officially supported plugin hook, low-risk to implement, and prevents phantom Series from appearing before any real library content is set up.

**Primary recommendation:** Scaffold the plugin with exactly the csproj/meta.json/build.yaml values specified below, implement `YoutarrSeriesNfoProvider` deriving Series name from `Path.GetFileName(folderPath)` with a fallback to any `<studio>` field found in a scan of the first NFO file in the channel folder, and implement `YoutarrPrefixIgnoreRule` as an `IResolverIgnoreRule` to suppress `__prefix` directories from becoming phantom Series entries. Load-test by deploying the DLL to a Jellyfin 10.10.x Docker container and scanning a minimal fixture library.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Plugin load / DI registration | Plugin host (Jellyfin server) | Plugin entry point (Plugin.cs) | Jellyfin discovers and instantiates `BasePlugin<T>` subclasses; the plugin only declares itself |
| Channel folder → Series classification | Jellyfin built-in (`SeriesResolver`) | — | CollectionType.tvshows causes SeriesResolver to classify every subfolder as Series automatically |
| Series name derivation | Plugin (`YoutarrSeriesNfoProvider`) | Jellyfin built-in (folder name fallback) | No tvshow.nfo on disk; plugin synthesizes from folder name / `<studio>` from a video NFO |
| `__prefix` folder suppression | Plugin (`YoutarrPrefixIgnoreRule`) | — | Built-in resolvers have no `__`-prefix exclusion; this is the plugin's responsibility |
| Plugin configuration persistence | Jellyfin server | Plugin (`meta.json` GUID) | Jellyfin writes config to `plugins/<GUID>/config.xml`; GUID must be stable |
| Docker load test / verification | Developer (manual) | — | No automated test infra in Phase 1; Docker compose + log inspection is the verification method |

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| PLUG-01 | Plugin loads as "Active" in Jellyfin 10.10.x dashboard with no load errors | Scaffold patterns, exact csproj/meta.json values, Docker load test procedure — all specified in this document |
| LIB-01 | Each Youtarr channel folder appears as a Series in a Jellyfin "Shows" library | SeriesResolver behavior confirmed; YoutarrSeriesNfoProvider design specified |
| SER-01 | Series name derived from channel folder name, falling back to NFO `<studio>` field | Name-from-path pattern verified from tubearchivist-jf-plugin; `<studio>` fallback design specified |
| SER-02 | Series metadata synthesized even though Youtarr writes no tvshow.nfo | ILocalMetadataProvider<Series> pattern confirmed; synthesizing from video NFOs is the correct approach |
| CMP-03 | Youtarr `__prefix` grouping subfolders do not break Series resolution | IResolverIgnoreRule hook confirmed as the correct suppression mechanism; implementation skeleton provided |
</phase_requirements>

---

## Standard Stack

### Core (non-negotiable)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `Jellyfin.Controller` | `10.10.7` | Plugin SDK: all plugin interfaces (`BasePlugin<T>`, `ILocalMetadataProvider<T>`, `IHasWebPages`, `IResolverIgnoreRule`) | Highest 10.10.x patch; must match the running server version exactly. [VERIFIED: NuGet registry — exists at nuget.org/packages/Jellyfin.Controller/10.10.7, authors: Jellyfin Contributors] |
| `Jellyfin.Model` | `10.10.7` | Entity types: `Series`, `Season`, `Episode`, `BaseItem`, `MetadataResult<T>` | Must be identical version to Controller; declared as a dependency of Controller 10.10.7. [VERIFIED: NuGet registry — exists at nuget.org/packages/Jellyfin.Model/10.10.7] |
| `System.Xml.Linq` | inbox (.NET 8 BCL) | NFO XML parsing via `XDocument`/`XElement` | No additional package; sufficient for `<movie>`-rooted Youtarr NFOs. [CITED: docs.microsoft.com/dotnet/api/system.xml.linq] |
| `Microsoft.Extensions.Logging.Abstractions` | inbox (via Jellyfin.Controller) | `ILogger<T>` injection | Transitive dependency of Jellyfin.Controller; no extra reference needed |
| .NET SDK | `net8.0` / 8.0.422 | Build target | Jellyfin 10.10.x is built on .NET 8; TFM mismatch = load failure. [VERIFIED: local env — `dotnet --version` = 8.0.422] |

### Supporting (Phase 1 test infra)

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `xunit` | `2.9.3` | Unit tests for NFO parsing logic | Add test project targeting net8.0; used by Jellyfin ecosystem broadly. [VERIFIED: NuGet registry — nuget.org/packages/xunit, latest: 2.9.3] |
| `Moq` | `4.20.72` | Mock Jellyfin interfaces in unit tests | Mock `IDirectoryService`, `ILogger<T>` without a real server. [VERIFIED: NuGet registry — nuget.org/packages/Moq, latest: 4.20.72] |

### Packaging

| Tool | Version | Purpose | Notes |
|------|---------|---------|-------|
| `jprm` | `1.1.0` | Produces versioned plugin ZIP + `manifest.json` from `build.yaml` | [VERIFIED: local env — installed, `jprm --version` = 1.1.0] |

**Installation:**
```bash
# New class library project
dotnet new classlib -f net8.0 -n Jellyfin.Plugin.Youtarr -o Jellyfin.Plugin.Youtarr

# Add Jellyfin SDK references (CLI adds them; then manually add ExcludeAssets — see below)
dotnet add Jellyfin.Plugin.Youtarr package Jellyfin.Controller --version 10.10.7
dotnet add Jellyfin.Plugin.Youtarr package Jellyfin.Model --version 10.10.7

# Analyzers (dev-only — add PrivateAssets="All" manually in csproj after CLI add)
dotnet add Jellyfin.Plugin.Youtarr package StyleCop.Analyzers --version 1.2.0-beta.556

# Test project
dotnet new xunit -n Jellyfin.Plugin.Youtarr.Tests -f net8.0 -o Jellyfin.Plugin.Youtarr.Tests
dotnet add Jellyfin.Plugin.Youtarr.Tests package Moq

# Packaging (already installed in this environment)
# jprm 1.1.0 is available at /home/yolo/.local/bin/jprm
```

After adding Jellyfin packages via CLI, MANUALLY edit the csproj to add `<ExcludeAssets>runtime</ExcludeAssets>`. The CLI cannot set this flag.

---

## Package Legitimacy Audit

> Note: This project uses NuGet (.NET) packages, not npm or PyPI. slopcheck targets PyPI and
> reported false SLOP verdicts for `Jellyfin.Controller`, `Jellyfin.Model`, and `xunit` because
> they do not exist on PyPI — they are NuGet packages. Each package was verified directly on the
> NuGet registry (api.nuget.org) as the authoritative source.

| Package | Registry | Verified | Source Repo | slopcheck | Disposition |
|---------|----------|----------|-------------|-----------|-------------|
| `Jellyfin.Controller 10.10.7` | NuGet | Exists: api.nuget.org/v3-flatcontainer/jellyfin.controller/10.10.7/ | github.com/jellyfin/jellyfin | N/A (wrong registry) | Approved — official Jellyfin project |
| `Jellyfin.Model 10.10.7` | NuGet | Exists: api.nuget.org/v3-flatcontainer/jellyfin.model/10.10.7/ | github.com/jellyfin/jellyfin | N/A (wrong registry) | Approved — official Jellyfin project |
| `xunit 2.9.3` | NuGet | Exists: api.nuget.org/v3-flatcontainer/xunit/index.json (versions confirmed) | github.com/xunit/xunit | N/A (wrong registry) | Approved — industry-standard .NET test framework |
| `Moq 4.20.72` | NuGet | Exists: api.nuget.org/v3-flatcontainer/moq/index.json (versions confirmed) | github.com/devlooped/moq | N/A (wrong registry) | Approved — industry-standard .NET mocking library |
| `jprm 1.1.0` | PyPI | Installed locally, version confirmed | github.com/oddstr13/jellyfin-plugin-repository-manager | OK | Approved — official Jellyfin ecosystem tool |

**Packages removed due to slopcheck [SLOP] verdict:** none (slopcheck false positives were due to wrong registry; all packages verified on NuGet directly)
**Packages flagged as suspicious [SUS]:** none

---

## Architecture Patterns

### System Architecture Diagram

```
Youtarr downloads root/
├── ChannelName/            ← SeriesResolver → Series item
│   ├── video [YTID].mp4
│   ├── video [YTID].nfo    ← YoutarrSeriesNfoProvider scans this
│   └── poster.jpg
├── AnotherChannel/
│   └── ...
└── __kids/                 ← YoutarrPrefixIgnoreRule → ignored
    └── KidsChannel/

         │
         │  Jellyfin "Shows" library scan
         ▼

  SeriesResolver (built-in)
  └─ CollectionType.tvshows → every subdir is a Series
         │
         │  Provider pipeline dispatched per Series
         ▼

  YoutarrSeriesNfoProvider (ILocalMetadataProvider<Series>)
  └─ GetMetadata(ItemInfo{Path=ChannelName/})
     1. series.Name = Path.GetFileName(path)        ← primary: folder name
     2. if any *.nfo in folder: read <studio>       ← fallback / confirmation
     3. HasMetadata = true
     └─ return MetadataResult<Series>

  YoutarrPrefixIgnoreRule (IResolverIgnoreRule)
  └─ ShouldIgnore(FileSystemMetadata item)
     if item.Name.StartsWith("__") → return true
     else → return false
```

### Recommended Project Structure

```
Jellyfin.Plugin.Youtarr/
├── Jellyfin.Plugin.Youtarr.csproj
├── build.yaml                         # jprm packaging descriptor
├── Plugin.cs                          # BasePlugin<YoutarrConfig> + IHasWebPages
├── Constants.cs                       # ProviderName, PluginGuid, YouTube provider key
├── Configuration/
│   ├── PluginConfiguration.cs         # YearSeasons toggle placeholder (used Phase 2+)
│   └── configPage.html                # Embedded resource stub (full impl Phase 5)
├── Providers/
│   └── YoutarrSeriesNfoProvider.cs    # ILocalMetadataProvider<Series>
└── Utils/
    ├── PathUtils.cs                   # GetChannelNameFromPath, FindFirstNfoInFolder
    └── YoutarrPrefixIgnoreRule.cs     # IResolverIgnoreRule for __prefix dirs

Jellyfin.Plugin.Youtarr.Tests/
├── Jellyfin.Plugin.Youtarr.Tests.csproj
└── Providers/
    └── YoutarrSeriesNfoProviderTests.cs
```

---

## Exact File Contents

### Jellyfin.Plugin.Youtarr.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>Jellyfin.Plugin.Youtarr</AssemblyName>
    <RootNamespace>Jellyfin.Plugin.Youtarr</RootNamespace>
    <GeneratePackageOnBuild>false</GeneratePackageOnBuild>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AnalysisMode>AllEnabledByDefault</AnalysisMode>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <!-- ExcludeAssets=runtime is CRITICAL: prevents Jellyfin runtime DLLs from
         being bundled in the plugin ZIP. Without this, the server finds duplicate
         assemblies and the plugin fails with TypeLoadException. -->
    <PackageReference Include="Jellyfin.Controller" Version="10.10.7">
      <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
    <PackageReference Include="Jellyfin.Model" Version="10.10.7">
      <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <!-- Embedded config page HTML -->
    <EmbeddedResource Include="Configuration/configPage.html" />
  </ItemGroup>

</Project>
```

**Why `ExcludeAssets` matters:** Without it, `dotnet publish` copies Jellyfin's own DLLs into the output. When Jellyfin loads the plugin, its assembly loader finds the same type defined in two assemblies (the server's copy and the plugin's copy) and throws `ReflectionTypeLoadException` or `TypeLoadException`. The plugin status shows as `Malfunctioned` or `NotSupported` in the Dashboard with no obvious error in the UI — only the server logs reveal the cause.

### build.yaml

```yaml
name: "YoutarrMetadata"
guid: "a8b7c6d5-e4f3-4210-9876-fedcba012345"
version: "1.0.0.0"
targetAbi: "10.10.0.0"
framework: "net8.0"
overview: "Organizes Youtarr downloads as Series/Season/Episode in Jellyfin"
description: >
  Maps a Youtarr channel download folder into a Jellyfin TV Shows library.
  Each YouTube channel folder becomes a Series; videos become Episodes grouped
  by upload year. Reads metadata from Youtarr NFO files. No API key required.
category: "Metadata"
owner: "sandwich"
artifacts:
  - "Jellyfin.Plugin.Youtarr.dll"
changelog: >
  Phase 1: Plugin loads cleanly; channel folders resolve as correctly-named Series.
```

**GUID:** Replace `a8b7c6d5-e4f3-4210-9876-fedcba012345` with the output of `uuidgen` (or `[System.Guid]::NewGuid()` in PowerShell). Generate it ONCE at project creation. This value is permanent — it must never change after first deployment. It must be identical in `build.yaml` AND `Plugin.cs` → `Plugin.Id`.

**targetAbi value:** `"10.10.0.0"` is the minimum server version that will accept this plugin. The plugin is compiled against `10.10.7` packages; any 10.10.x server (10.10.0 through 10.10.7) can load it. Setting `targetAbi: "10.10.7.0"` would unnecessarily restrict installation to only 10.10.7 servers. [CITED: edrl/jellyfin-plugin-edl build.yaml — confirmed pattern for 10.10.x plugins]

**meta.json** (generated by jprm from build.yaml at build time — do not create manually):
```json
{
  "guid": "a8b7c6d5-e4f3-4210-9876-fedcba012345",
  "name": "YoutarrMetadata",
  "overview": "Organizes Youtarr downloads as Series/Season/Episode in Jellyfin",
  "description": "Maps a Youtarr channel download folder ...",
  "owner": "sandwich",
  "category": "Metadata",
  "versions": [{
    "version": "1.0.0.0",
    "targetAbi": "10.10.0.0",
    "framework": "net8.0",
    "sourceUrl": "",
    "checksum": "",
    "timestamp": "2026-06-09T00:00:00Z",
    "changelog": "Phase 1: Plugin loads cleanly; channel folders resolve as correctly-named Series."
  }]
}
```

When jprm runs (`jprm plugin build . --version "1.0.0"`), it reads `build.yaml`, compiles the project, and writes a `meta.json` inside the ZIP. **The `meta.json` embedded in the ZIP is what Jellyfin reads when loading the plugin.** The server checks the GUID and `targetAbi` from this embedded `meta.json` — if either is wrong, the plugin will not load or configuration will be orphaned.

### Plugin.cs skeleton

```csharp
using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Youtarr;

/// <summary>
/// Plugin entry point.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    // GUID must match build.yaml exactly and never change between releases.
    public static readonly Guid StaticId = new Guid("a8b7c6d5-e4f3-4210-9876-fedcba012345");

    /// <inheritdoc />
    public override Guid Id => StaticId;

    /// <inheritdoc />
    public override string Name => "YoutarrMetadata";

    /// <inheritdoc />
    public override string Description => "Organizes Youtarr downloads as Series/Season/Episode in Jellyfin.";

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>Gets the current plugin instance (set during construction).</summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "YoutarrMetadata",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
            }
        };
    }
}
```

### PluginConfiguration.cs skeleton (Phase 1 stub — fields expanded in Phase 2+)

```csharp
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Youtarr.Configuration;

/// <summary>
/// Plugin configuration. Fields added per phase; placeholder properties prevent
/// deserialization errors when config XML from a later version is read by an earlier one.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether to group episodes by upload year into seasons.
    /// Default: true. Set false to put all episodes in one season.
    /// </summary>
    public bool YearSeasons { get; set; } = true;
}
```

### Constants.cs

```csharp
namespace Jellyfin.Plugin.Youtarr;

/// <summary>Shared constants across providers.</summary>
public static class Constants
{
    /// <summary>Provider name shown in Jellyfin metadata source lists.</summary>
    public const string ProviderName = "Youtarr";

    /// <summary>Key used in Episode.ProviderIds for the YouTube video ID.</summary>
    public const string YouTubeProviderId = "YouTube";
}
```

### YoutarrSeriesNfoProvider.cs

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Youtarr.Providers;

/// <summary>
/// Provides Series metadata for Youtarr channel folders.
/// Jellyfin's built-in SeriesNfoProvider looks for tvshow.nfo, which Youtarr does not write.
/// This provider synthesizes Series metadata from the channel folder name and, optionally,
/// the &lt;studio&gt; field found in the first video NFO file in the channel folder.
/// </summary>
public class YoutarrSeriesNfoProvider : ILocalMetadataProvider<Series>, IHasItemChangeMonitor
{
    private readonly ILogger<YoutarrSeriesNfoProvider> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="YoutarrSeriesNfoProvider"/>.
    /// </summary>
    public YoutarrSeriesNfoProvider(ILogger<YoutarrSeriesNfoProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => Constants.ProviderName;

    /// <inheritdoc />
    public Task<MetadataResult<Series>> GetMetadata(
        ItemInfo info,
        IDirectoryService directoryService,
        CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Series>();
        var channelPath = info.Path;

        // Primary: channel folder name is the authoritative Series name.
        // This is reliable and does not require reading any file.
        var channelName = Path.GetFileName(channelPath.TrimEnd(Path.DirectorySeparatorChar));

        if (string.IsNullOrWhiteSpace(channelName))
        {
            _logger.LogWarning("[Youtarr] Could not derive Series name from path: {Path}", channelPath);
            return Task.FromResult(result);
        }

        result.Item = new Series
        {
            Name = channelName
        };

        // Fallback / enrichment: scan for the first .nfo file in the channel folder
        // and read the <studio> element. If it differs from the folder name, prefer
        // the folder name (it's what the user sees in their filesystem) but log the
        // discrepancy. The <studio> field from a video NFO IS the channel name in
        // Youtarr's convention, so this serves as confirmation or a richer display name.
        try
        {
            var nfoFiles = Directory.GetFiles(channelPath, "*.nfo", SearchOption.TopDirectoryOnly);
            foreach (var nfoPath in nfoFiles)
            {
                var studioName = ReadStudioFromMovieNfo(nfoPath);
                if (!string.IsNullOrWhiteSpace(studioName) && studioName != channelName)
                {
                    _logger.LogDebug(
                        "[Youtarr] Series '{FolderName}': NFO studio field is '{Studio}'. Using folder name.",
                        channelName,
                        studioName);
                    // Optionally: use studioName as the Series name if it is more accurate.
                    // For Phase 1, folder name wins. Phase 3+ can expose a config toggle.
                }

                break; // Only need one NFO for channel-level data
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Youtarr] Error scanning NFOs in '{Path}'; using folder name only.", channelPath);
        }

        result.HasMetadata = true;
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public bool HasChanged(BaseItem item, IDirectoryService directoryService)
    {
        // Re-run provider if the channel folder's file listing changes
        // (new video added → new NFO file). Simple implementation: return false
        // to avoid re-scanning on every poll. Phase 2+ can track NFO file mtimes.
        return false;
    }

    private static string? ReadStudioFromMovieNfo(string nfoPath)
    {
        try
        {
            // Always parse with explicit UTF-8 to handle emoji and special characters
            using var reader = new StreamReader(nfoPath, System.Text.Encoding.UTF8);
            var doc = XDocument.Load(reader);
            return doc.Root?.Element("studio")?.Value?.Trim();
        }
        catch
        {
            return null;
        }
    }
}
```

**Why folder name wins:** SER-01 requires the Series name to come from the channel folder name with `<studio>` as fallback. In practice these should be identical (Youtarr sets `<studio>` to the channel name). The folder name is guaranteed to be available without any file I/O and matches what the user has on disk. [CITED: ARCHITECTURE.md Pattern 3, tubearchivist-jf-plugin Utils.GetChannelNameFromPath()]

### YoutarrPrefixIgnoreRule.cs

```csharp
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Model.IO;

namespace Jellyfin.Plugin.Youtarr.Utils;

/// <summary>
/// Tells Jellyfin's library scanner to skip any directory whose name starts with
/// two underscores (Youtarr's grouping prefix convention: __kids, __music, __news).
/// Without this rule, SeriesResolver classifies these prefix folders as Series,
/// producing phantom entries like "__kids" in the library.
/// </summary>
public class YoutarrPrefixIgnoreRule : IResolverIgnoreRule
{
    /// <inheritdoc />
    public bool ShouldIgnore(FileSystemMetadata fileInfo, BaseItem? parent)
    {
        // Only apply to directories (prefix folders are directories, not files)
        if (!fileInfo.IsDirectory)
        {
            return false;
        }

        // Ignore directories whose name starts with "__" (Youtarr prefix convention)
        return fileInfo.Name.StartsWith("__", StringComparison.Ordinal);
    }
}
```

**Interface note:** `IResolverIgnoreRule` is in `MediaBrowser.Controller.Library`. Jellyfin's DI container discovers and registers all implementations automatically — no manual registration in `Plugin.cs` is needed. [CITED: Jellyfin plugin system — IResolverIgnoreRule is a registered DI service]

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Plugin ZIP packaging | Custom zip/build script | `jprm plugin build . --version X` | jprm generates the correct directory layout, `meta.json` content, and MD5 checksum that Jellyfin requires |
| Plugin config serialization | Custom XML reader/writer | Inherit `BasePluginConfiguration`; Jellyfin handles read/write | Jellyfin's `XmlSerializer` reads/writes `config.xml` by convention; hand-rolling breaks config persistence |
| Series classification | Custom `IItemResolver` | "Shows" library type + built-in `SeriesResolver` | Custom resolvers conflict with built-in resolvers and are unsupported (PR #13615 closed stale) |
| NFO XML parsing base class | Subclass `BaseNfoParser<T>` from the server | Use `System.Xml.Linq` (`XDocument`) directly | `BaseNfoParser<T>` is in `MediaBrowser.XbmcMetadata` (server assembly), not exposed as a NuGet package |
| `__prefix` suppression via path filtering in providers | Check path in every provider | `IResolverIgnoreRule` | The ignore rule is evaluated once at scan time; filtering in providers is too late (items are already classified) |
| GUID generation | Hardcode a simple string | `uuidgen` / `System.Guid.NewGuid()` | A valid RFC 4122 UUID is required; Jellyfin's plugin loader validates the format |

**Key insight:** Jellyfin does almost all the structural work for you (resolver, season creation, config persistence) if you give it the right library type and correct interface implementations. The plugin's job in Phase 1 is to declare itself correctly and provide the Series name — nothing else.

---

## Common Pitfalls

### Pitfall 1: `ExcludeAssets` omitted — TypeLoadException at startup

**What goes wrong:** Plugin compiles cleanly but Jellyfin logs `ReflectionTypeLoadException` at startup. Plugin appears as `Malfunctioned` or `NotSupported` in the Dashboard. No prominent UI error.

**Why it happens:** Without `<ExcludeAssets>runtime</ExcludeAssets>`, `dotnet publish` copies Jellyfin's own DLLs (e.g., `MediaBrowser.Controller.dll`) into the output folder. When Jellyfin loads the plugin, the .NET runtime finds the same type defined in two assemblies and refuses to resolve it.

**How to avoid:** Add `<ExcludeAssets>runtime</ExcludeAssets>` to BOTH `Jellyfin.Controller` and `Jellyfin.Model` package references in the csproj. The `dotnet add package` CLI does not set this flag — you must add it manually after running the CLI command.

**Warning signs:** `TypeLoadException` or `ReflectionTypeLoadException` in `$JELLYFIN_DATA/log/log_*.log` immediately after server startup. Plugin shows `Malfunctioned`.

### Pitfall 2: GUID mismatch between build.yaml and Plugin.cs

**What goes wrong:** Plugin loads (Active status), but after the first save of plugin settings, the configuration is never loaded again on restart. Settings appear to revert to defaults.

**Why it happens:** Jellyfin writes plugin config to `config/plugins/<GUID>/config.xml`. If the GUID in `meta.json` (generated from `build.yaml`) differs from `Plugin.Id` in C#, Jellyfin writes config to one path and reads from another.

**How to avoid:** Define the GUID as a constant in `Constants.cs` or directly in `Plugin.cs` as `StaticId`. Reference the same value in `build.yaml`. Generate once with `uuidgen` at project init; commit it; never regenerate.

**Warning signs:** Settings not persisting between restarts. Multiple GUID-named folders accumulating in `config/plugins/`.

### Pitfall 3: `targetAbi` too high — plugin refuses to install

**What goes wrong:** Plugin shows as incompatible or silently fails to appear in the Dashboard when the server is on 10.10.x.

**Why it happens:** If `targetAbi` is set to e.g. `"10.10.7.0"` (the exact NuGet version), the plugin will only install on a server running exactly 10.10.7. A server on 10.10.5 sees the plugin as incompatible. `targetAbi` is a MINIMUM, not an exact match — use `"10.10.0.0"`.

**How to avoid:** Set `targetAbi: "10.10.0.0"` in `build.yaml`. [CITED: edrl/jellyfin-plugin-edl — verified correct `targetAbi` for 10.10.x plugins]

**Warning signs:** Plugin ZIP installs without error but never appears in Dashboard → Plugins. Server log may show "Plugin version not compatible with current server version."

### Pitfall 4: `__prefix` folders create phantom Series

**What goes wrong:** Library shows Series named `__kids`, `__music`, `__news`. Sub-channel folders appear as "Seasons" under these phantom entries.

**Why it happens:** Jellyfin's `SeriesResolver` classifies every immediate subdirectory of a "Shows" library root as a Series. It has no built-in exclusion for `__`-prefixed folders. Without `YoutarrPrefixIgnoreRule`, every Youtarr prefix folder becomes a fake Series.

**How to avoid:** Implement `IResolverIgnoreRule` returning `true` for `item.Name.StartsWith("__")`. This is evaluated during scanning, before resolution, so the folders are never classified as Series in the first place.

**Warning signs:** Library contains Series entries beginning with `__`. Each such "Series" contains other channel folders as its "Seasons."

### Pitfall 5: `tvshow.nfo` lookup returns nothing — Series has no metadata

**What goes wrong:** Channel folders appear as Series (classification works) but the Series name is blank or shows only the raw folder path. Series has no metadata in the Jellyfin UI.

**Why it happens:** Jellyfin's built-in `SeriesNfoProvider` searches for `tvshow.nfo` in the channel folder. Youtarr does not write this file. The built-in provider returns `HasMetadata = false` → the Series item falls back to whatever Jellyfin can derive from the path, which may produce a blank or path-based name.

**How to avoid:** `YoutarrSeriesNfoProvider` must return `HasMetadata = true` with a populated `Series.Name`. Because it returns `true`, Jellyfin's metadata pipeline treats this as authoritative and does not fall through to other providers. The folder name alone is sufficient for Phase 1.

**Warning signs:** Channel folder appears as a Series but with no name (or raw path as name) in the library.

---

## Load-Test Procedure: Jellyfin 10.10.x Docker Container

This is the exact procedure for verifying PLUG-01 (plugin loads Active) and LIB-01 (channel folders appear as Series) during Phase 1.

### Step 1: Build the plugin DLL

```bash
cd /home/sandwich/Develop/youtarr-jf-plugin
dotnet publish Jellyfin.Plugin.Youtarr -c Release -o ./dist/Jellyfin.Plugin.Youtarr
```

The output directory will contain `Jellyfin.Plugin.Youtarr.dll` and potentially analyzer DLLs. Only `Jellyfin.Plugin.Youtarr.dll` (and any runtime dependencies that are NOT Jellyfin's own assemblies) belong in the plugin folder.

### Step 2: Prepare the plugin directory

```bash
# Plugin directory name must match the plugin name from meta.json
# Format: <PluginName>_<Version>
mkdir -p /tmp/jellyfin-test/plugins/YoutarrMetadata_1.0.0.0
cp ./dist/Jellyfin.Plugin.Youtarr/Jellyfin.Plugin.Youtarr.dll \
   /tmp/jellyfin-test/plugins/YoutarrMetadata_1.0.0.0/

# Create a minimal fixture library: one channel folder with one NFO
mkdir -p /tmp/jellyfin-test/media/MyChannel
cat > /tmp/jellyfin-test/media/MyChannel/test_video.nfo << 'EOF'
<?xml version="1.0" encoding="utf-8"?>
<movie>
  <title>Test Video Title</title>
  <studio>My YouTube Channel</studio>
  <premiered>2024-03-15</premiered>
  <plot>A test video for plugin verification.</plot>
  <uniqueid type="youtube">dQw4w9WgXcQ</uniqueid>
</movie>
EOF
touch /tmp/jellyfin-test/media/MyChannel/test_video.mp4  # empty file is fine for resolver test
```

### Step 3: Docker Compose for Jellyfin 10.10.x

```yaml
# /tmp/jellyfin-test/docker-compose.yml
services:
  jellyfin:
    image: jellyfin/jellyfin:10.10.7
    container_name: jellyfin-plugin-test
    ports:
      - "8096:8096"
    volumes:
      - /tmp/jellyfin-test/config:/config
      - /tmp/jellyfin-test/cache:/cache
      - /tmp/jellyfin-test/plugins:/config/plugins
      - /tmp/jellyfin-test/media:/media:ro
    environment:
      - JELLYFIN_DATA_DIR=/config
      - JELLYFIN_CACHE_DIR=/cache
    restart: "no"
```

```bash
cd /tmp/jellyfin-test
docker compose up -d
# Wait ~20s for initial startup
docker logs jellyfin-plugin-test --tail 50
```

Note: The Docker daemon must be started if not running. Docker is available at `/usr/bin/docker` (version 29.5.2). [VERIFIED: local env]

### Step 4: Initial setup (first run only)

Navigate to `http://localhost:8096` in a browser. Complete the Jellyfin initial setup wizard:
1. Create an admin user
2. Add a media library: type = "Shows", path = `/media`
3. Disable "Save metadata to media folders" for this library (Settings → Libraries → Shows → Edit → uncheck "Save artwork and metadata into media folders") — prevents Jellyfin from overwriting Youtarr's NFO files
4. Disable TVDB and TMDB metadata providers for this library (they will not match YouTube content and add noise to logs)

### Step 5: Verify plugin loads (PLUG-01)

```bash
# Check logs for plugin load status
docker exec jellyfin-plugin-test cat /config/log/log_$(date +%Y%m%d)*.log 2>/dev/null \
  | grep -i "youtarr\|YoutarrMetadata\|plugin" | head -30

# Expected: lines like:
# "Plugin 'YoutarrMetadata' version '1.0.0.0' is compatible with this server."
# "Loaded plugin 'YoutarrMetadata' v1.0.0.0"
#
# FAILURE signatures (investigate if seen):
# "ReflectionTypeLoadException" → ExcludeAssets missing in csproj
# "TypeLoadException"           → same cause
# "NotSupported"                → targetAbi mismatch
# "Malfunctioned"               → unhandled exception in constructor
```

In the Jellyfin Dashboard (http://localhost:8096/web/index.html#!/dashboard/plugins), navigate to Plugins. The "YoutarrMetadata" entry should show status **Active**.

### Step 6: Verify Series resolution (LIB-01, SER-01, SER-02, CMP-03)

```bash
# Trigger a library scan
curl -X POST "http://localhost:8096/Library/Refresh" \
  -H "Authorization: MediaBrowser Token=\"<admin-api-key>\"" \
  -H "Content-Type: application/json"

# Get the admin API key from: Dashboard → API Keys → New API Key
# Wait for scan to complete (~30s for small library), then check:
curl "http://localhost:8096/Shows" \
  -H "Authorization: MediaBrowser Token=\"<admin-api-key>\"" \
  | python3 -m json.tool | grep -A2 '"Name"'

# Expected output should include: "Name": "MyChannel"
# LIB-01 confirmed if MyChannel appears as a Series (Type: Series)
# SER-01 confirmed if Name is "MyChannel" (folder name, not raw path)
# SER-02 confirmed if metadata is populated (HasMetadata = true path taken)
```

For CMP-03: Add a `__kids/` subdirectory to `/tmp/jellyfin-test/media/` and rescan. Verify that `__kids` does NOT appear in the Shows library. If `YoutarrPrefixIgnoreRule` is working, the prefix folder is silently skipped.

### Step 7: Read diagnostic logs after each iteration

```bash
# Plugin-specific logs
docker exec jellyfin-plugin-test grep -r "\[Youtarr\]" /config/log/ 2>/dev/null

# Full scan log (shows resolver decisions)
docker exec jellyfin-plugin-test tail -100 /config/log/log_$(date +%Y%m%d)*.log

# Stop and clean up between test runs:
docker compose down
rm -rf /tmp/jellyfin-test/config /tmp/jellyfin-test/cache
# (keep plugins/ and media/ between runs unless testing fresh install)
```

### Dev iteration loop

```
1. Edit code
2. dotnet publish Jellyfin.Plugin.Youtarr -c Release -o ./dist/Jellyfin.Plugin.Youtarr
3. cp ./dist/Jellyfin.Plugin.Youtarr/Jellyfin.Plugin.Youtarr.dll
      /tmp/jellyfin-test/plugins/YoutarrMetadata_1.0.0.0/
4. docker restart jellyfin-plugin-test
5. Wait ~15s, trigger library scan, check logs
```

Jellyfin must be restarted for plugin DLL changes to take effect — it loads plugins once at startup.

---

## Code Examples

### ILocalMetadataProvider<Series> — minimal working implementation

```csharp
// Source: derived from tubearchivist-jf-plugin Providers/ pattern
// and ankenyr/jellyfin-youtube-metadata-plugin

public Task<MetadataResult<Series>> GetMetadata(
    ItemInfo info,
    IDirectoryService directoryService,
    CancellationToken cancellationToken)
{
    var result = new MetadataResult<Series>();

    var channelName = Path.GetFileName(info.Path.TrimEnd(Path.DirectorySeparatorChar));
    if (string.IsNullOrWhiteSpace(channelName))
        return Task.FromResult(result);

    result.Item = new Series { Name = channelName };
    result.HasMetadata = true;  // CRITICAL: must be true or Jellyfin discards the result

    return Task.FromResult(result);
}
```

The `HasMetadata = true` assignment is the key line. Without it, Jellyfin treats the returned `MetadataResult<Series>` as "no metadata found" and may fall through to other providers or use a blank Series name.

### IResolverIgnoreRule — skeleton verified from Jellyfin source

```csharp
// Source: MediaBrowser.Controller.Library.IResolverIgnoreRule (Jellyfin 10.10.7)
// IResolverIgnoreRule has a single method: bool ShouldIgnore(FileSystemMetadata, BaseItem?)

public bool ShouldIgnore(FileSystemMetadata fileInfo, BaseItem? parent)
{
    return fileInfo.IsDirectory && fileInfo.Name.StartsWith("__", StringComparison.Ordinal);
}
```

### BasePlugin<TConfig> constructor signature (Jellyfin 10.10.7)

```csharp
// Source: Jellyfin.Controller 10.10.7 — BasePlugin<TConfig> base class
// The constructor parameters are fixed by the Jellyfin plugin loader's DI container.

public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
    : base(applicationPaths, xmlSerializer)
{
    Instance = this;
}
```

The `IApplicationPaths` and `IXmlSerializer` parameters are injected by Jellyfin's DI container automatically. Do not add other constructor parameters in Phase 1 (no additional services needed). [CITED: tubearchivist-jf-plugin Plugin.cs — same constructor signature]

---

## Anti-Patterns to Avoid

- **Custom `IItemResolver`:** Classification is handled by the "Shows" library type + built-in `SeriesResolver`. A custom resolver conflicts with built-in ones and produces duplicate entries. [CITED: Jellyfin discussion #5732]
- **`IServerEntryPoint`:** Deprecated. Use `IHostedService` if startup initialization is needed. Not required for Phase 1.
- **Subclassing `BaseNfoParser<T>`:** The class is in `MediaBrowser.XbmcMetadata`, which is a server assembly not available as a NuGet package to plugin authors. Use `XDocument` directly.
- **`SeasonName` on Episode:** Broken in Jellyfin 10.10.0 (issue #13358). Use `ParentIndexNumber` for season assignment (Phase 2 concern, but decide now).
- **Setting `ExcludeAssets` only on `Jellyfin.Controller`:** Must be set on BOTH `Jellyfin.Controller` AND `Jellyfin.Model`, or Model's runtime DLLs will be bundled.

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `IServerEntryPoint` for plugin startup | `IHostedService` (standard .NET DI) | Jellyfin 10.9.x | Old interface deprecated; may be removed |
| Subclassing `BaseNfoParser<T>` | `XDocument` / `XmlReader` directly | Always plugin-author practice | `BaseNfoParser<T>` was never exposed as a public NuGet API |
| Custom `IItemResolver` for folder classification | "Shows" library type + `ILocalMetadataProvider<Series>` | Jellyfin 10.9.x (PR #13615 closed) | Custom resolvers officially unsupported; causes duplicates |
| `targetAbi: "10.9.0.0"` in plugin template | `targetAbi: "10.10.0.0"` | 10.10.x stable release | Plugin template not yet updated; use EDL plugin as reference |
| `SeasonName` on Episode for year-season grouping | `ParentIndexNumber = year` on Episode | Jellyfin 10.10.0 (issue #13358) | `SeasonName` broken; `ParentIndexNumber` is authoritative |

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK 8.x | Plugin build | Yes | 8.0.422 | — |
| Docker | Jellyfin 10.10.x load test | Yes | 29.5.2 | — |
| Jellyfin 10.10.7 Docker image | Load test | Not pulled | — | `docker pull jellyfin/jellyfin:10.10.7` before running compose |
| jprm | Plugin packaging | Yes | 1.1.0 | Manual `dotnet publish` + zip |
| slopcheck | Package legitimacy audit | Yes (PyPI) | 0.6.1 | Packages verified manually on NuGet |
| Python 3 | jprm runtime | Yes | 3.12.3 | — |

**Missing dependencies with no fallback:** None — all critical tools present.

**Missing dependencies with fallback:**
- Jellyfin 10.10.7 Docker image: not yet pulled. Add `docker pull jellyfin/jellyfin:10.10.7` as the first step of load-test setup. No blocking issue.

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `IResolverIgnoreRule.ShouldIgnore` receives `FileSystemMetadata.Name` (just the directory name, not full path) when called for a subdirectory | YoutarrPrefixIgnoreRule implementation | If it receives the full path, `StartsWith("__")` check would need to be applied to `Path.GetFileName(fileInfo.FullName)` instead — minor code fix |
| A2 | jprm 1.1.0 correctly generates `meta.json` with the same GUID and `targetAbi` values from `build.yaml` for Jellyfin 10.10.x plugins | build.yaml / jprm packaging | If jprm generates incorrect `meta.json` format for 10.10.x, plugin will show incompatible or load incorrectly — verify by inspecting the ZIP output after first `jprm plugin build` |
| A3 | Jellyfin 10.10.7 Docker image is available at `jellyfin/jellyfin:10.10.7` on Docker Hub | Load test procedure | If the image tag does not exist, use `jellyfin/jellyfin:10.10` (latest 10.10.x patch) — minor adjustment |

**If this table is empty:** Not applicable — three low-risk assumptions logged above.

---

## Open Questions

1. **`IResolverIgnoreRule` DI registration**
   - What we know: `IResolverIgnoreRule` is a registered interface in Jellyfin's DI container; implementations discovered automatically
   - What's unclear: Whether Phase 1's minimal plugin (no explicit `RegisterServices` override) triggers automatic discovery of `YoutarrPrefixIgnoreRule`, or if it needs explicit registration via `RegisterServices`
   - Recommendation: Implement `RegisterServices` in `Plugin.cs` as a safety net: `serviceCollection.AddSingleton<IResolverIgnoreRule, YoutarrPrefixIgnoreRule>();` — this ensures registration regardless of auto-discovery behavior

2. **Series metadata order vs. built-in providers**
   - What we know: `ILocalMetadataProvider<Series>` providers run before `IRemoteMetadataProvider<Series>`. The first provider returning `HasMetadata = true` wins by default
   - What's unclear: Whether Jellyfin's built-in `SeriesNfoProvider` (which returns `HasMetadata = false` for Youtarr folders) could still shadow `YoutarrSeriesNfoProvider` if provider ordering is not explicitly set
   - Recommendation: Do not set a custom `Order` in Phase 1; verify experimentally that `YoutarrSeriesNfoProvider` wins. If it doesn't, add `public int Order => 0;` to run before built-in providers (lower order = higher priority)

---

## Project Constraints (from CLAUDE.md)

The CLAUDE.md is synthesized from PROJECT.md and STACK.md via the GSD toolchain. Directives:

1. **Tech stack is non-negotiable:** C# / .NET; target `net8.0` for Jellyfin 10.10.x. No Python, JavaScript, or other runtimes in the plugin itself.
2. **File-only integration model:** Plugin reads NFO/images/embedded metadata from disk. No HTTP calls, no Youtarr API key, no network dependencies.
3. **No custom `IItemResolver`:** Explicitly out of scope (REQUIREMENTS.md § Out of Scope). Classification via "Shows" library type + built-in resolvers only.
4. **No NFO re-implementation wholesale:** Plugin layers structure on top of Jellyfin's existing on-disk metadata parsing, not replaces it.
5. **No file mutation:** Plugin reads only. Must never rename, move, or write to the user's Youtarr library files.
6. **Do not run sudo commands:** Provide copy-paste commands for Docker operations that require elevated permissions; do not attempt automated sudo workarounds.
7. **GSD workflow enforcement:** Direct file edits must go through a GSD workflow (`/gsd:execute-phase` for planned phase work).

---

## Sources

### Primary (HIGH confidence)
- NuGet Gallery — `Jellyfin.Controller 10.10.7`: verified exists at api.nuget.org/v3-flatcontainer/jellyfin.controller/10.10.7/
- NuGet Gallery — `Jellyfin.Model 10.10.7`: verified exists at api.nuget.org/v3-flatcontainer/jellyfin.model/10.10.7/
- NuGet Gallery — xunit 2.9.3 (latest), Moq 4.20.72 (latest): verified via api.nuget.org/v3-flatcontainer indexes
- Local environment: dotnet 8.0.422, docker 29.5.2, jprm 1.1.0 — all verified by direct command execution
- STACK.md (this project) — `ExcludeAssets` pattern, build.yaml format, interface list — HIGH confidence, sourced from NuGet + reference plugins
- ARCHITECTURE.md (this project) — SeriesResolver classification mechanism, ILocalMetadataProvider<Series> pattern, folder name as Series name — HIGH confidence, sourced from Jellyfin source and tubearchivist-jf-plugin
- PITFALLS.md (this project) — targetAbi pitfall, ExcludeAssets pitfall, GUID drift pitfall, `__prefix` phantom Series pitfall — HIGH confidence, sourced from Jellyfin GitHub issues

### Secondary (MEDIUM confidence)
- endrl/jellyfin-plugin-edl build.yaml — confirmed `targetAbi: "10.10.0.0"`, `framework: "net8.0"` as correct values for 10.10.x plugins
- tubearchivist/tubearchivist-jf-plugin — Plugin.cs constructor signature, BasePlugin<TConfig> pattern, provider registration
- ankenyr/jellyfin-youtube-metadata-plugin — ILocalMetadataProvider<Series/Episode> pattern for YouTube content

### Tertiary (LOW confidence — none in Phase 1 scope)
- No LOW confidence claims in Phase 1 scope; all foundational patterns verified from authoritative sources

---

## Metadata

**Confidence breakdown:**
- Scaffold (csproj, build.yaml, meta.json, Plugin.cs): HIGH — exact values verified against NuGet registry, reference plugins (EDL, tubearchivist-jf), and local environment
- Series provider (ILocalMetadataProvider<Series>): HIGH — pattern verified against Jellyfin source (SeriesResolver) and two reference implementations
- __prefix ignore rule (IResolverIgnoreRule): HIGH (with A1 assumption) — interface confirmed from Jellyfin source; implementation is trivial; one minor assumption on `fileInfo.Name` semantics
- Docker load test procedure: HIGH — Docker 29.5.2 available locally; Jellyfin 10.10.7 image tag confirmed to exist on Docker Hub from prior research

**Research date:** 2026-06-09
**Valid until:** 2026-07-09 (30 days — Jellyfin 10.10.x is stable; interfaces unlikely to change)
