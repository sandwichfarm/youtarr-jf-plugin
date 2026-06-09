# Phase 3: Artwork + Configuration Page — Research

**Researched:** 2026-06-09
**Domain:** Jellyfin `ILocalImageProvider`, `LocalImageProvider` built-in behavior, `EpisodeLocalImageProvider` built-in behavior, `IHasWebPages` / Dashboard config page pattern
**Confidence:** HIGH (built-in provider behavior confirmed from Jellyfin source; config page pattern confirmed from tubearchivist-jf-plugin source)

---

## Summary

Phase 3 covers two independent workstreams: channel artwork surfacing (ART-01 through ART-04) and the Jellyfin Dashboard plugin configuration page (PLUG-03).

**Artwork — The Big Finding:** Jellyfin's built-in image providers already handle nearly everything:
- ART-01 (`poster.jpg` → Series Primary): COVERED by built-in `LocalImageProvider` which explicitly searches for `poster`/`folder`/`cover`/`default`/`show` base names as Series Primary image. No custom provider needed.
- ART-03 (`<videofilename>.jpg` → Episode Primary): COVERED by built-in `EpisodeLocalImageProvider` which searches for same-basename `.jpg` (and `<name>-thumb.jpg`) next to video files as Episode Primary. No custom provider needed.
- ART-04 (graceful degradation): COVERED by both built-in providers; when files are absent, `GetImages` returns an empty list. No exception is raised.
- ART-02 (Series backdrop): **NOT COVERED** by the built-in. The `LocalImageProvider` looks for `fanart`/`background`/`backdrop` base names for Series Backdrop. Youtarr writes ONLY `poster.jpg`; no `fanart.jpg` or `backdrop.jpg` exists. A custom `ILocalImageProvider` is needed SOLELY for ART-02, to register `poster.jpg` a second time as `ImageType.Backdrop`.

**Net scope reduction:** Phase 3 needs only one new image provider class (`YoutarrSeriesImageProvider`) with a narrow responsibility: return `poster.jpg` as `ImageType.Backdrop` for Series items. The Primary image for Series and all Episode images are handled by the built-in stack at no cost.

**Config page:** The Jellyfin Dashboard plugin config page pattern is fully established. The three settings (`YearSeasons`, `EpisodeNumberingScheme`, `MaxDescriptionLength`) map to a checkbox, a `<select>`, and a number `<input>` respectively. `ApiClient.getPluginConfiguration` / `updatePluginConfiguration` with the plugin GUID persist settings to the config XML, which survives server restarts automatically.

**Primary recommendation:** Build one narrow `YoutarrSeriesImageProvider` (backdrop-only), add image fixtures to the harness, and flesh out `configPage.html` with the three settings wired to `ApiClient`. ART-01/ART-03/ART-04 require no new code — document the built-in handling and add a live confirmation step in the harness.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Series poster (Primary) image | Jellyfin built-in (`LocalImageProvider`) | — | Built-in searches `poster`/`folder`/`cover` filenames for Series Primary; `poster.jpg` always matches |
| Series backdrop image | Plugin (`YoutarrSeriesImageProvider`) | — | Built-in searches `fanart`/`backdrop` filenames; Youtarr writes neither — must reuse `poster.jpg` via custom provider |
| Episode thumbnail (Primary) image | Jellyfin built-in (`EpisodeLocalImageProvider`) | — | Built-in searches same-basename `.jpg` + `-thumb.jpg` next to video files; matches Youtarr's naming |
| Graceful degradation (ART-04) | Jellyfin built-in (both providers above) | Plugin (empty list return) | Built-in returns empty list on file-not-found; custom provider must also return empty list, never throw |
| Configuration page HTML/JS | Plugin (`configPage.html` embedded resource) | Jellyfin Dashboard host | `IHasWebPages.GetPages()` serves the embedded HTML; Dashboard JS calls `ApiClient.getPluginConfiguration` |
| Configuration persistence | Jellyfin core (BasePlugin XML serializer) | — | `BasePlugin<PluginConfiguration>` serializes config to `config/plugins/<GUID>/<GUID>.xml` automatically |
| Config defaults + new fields | Plugin (`PluginConfiguration.cs`) | — | `EpisodeNumberingScheme` and `MaxDescriptionLength` already added in Phase 2 plans; `YearSeasons` from Phase 1 |

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| ART-01 | Channel `poster.jpg` shown as Series Primary image | Built-in `LocalImageProvider` handles `poster.jpg` automatically — no custom code needed; confirm live in harness |
| ART-02 | Channel image surfaced as Series Backdrop | Built-in does NOT handle `poster.jpg` as Backdrop; requires `YoutarrSeriesImageProvider` returning `ImageType.Backdrop` |
| ART-03 | `<videofilename>.jpg` shown as Episode Primary image | Built-in `EpisodeLocalImageProvider` handles same-basename `.jpg` next to video files — no custom code needed; confirm live |
| ART-04 | Missing artwork degrades gracefully — no errors | Both built-in providers return empty list on missing files; custom provider must also return empty on missing `poster.jpg` |
| PLUG-03 | Dashboard config page exposing all three settings; changes persist across restarts | `IHasWebPages` + embedded `configPage.html`; `ApiClient.getPluginConfiguration/updatePluginConfiguration`; XML persisted by `BasePlugin<T>` |
</phase_requirements>

---

## Project Constraints (from CLAUDE.md)

1. **Tech stack non-negotiable:** C# / net8.0; `Jellyfin.Controller 10.10.7` + `Jellyfin.Model 10.10.7` with `<ExcludeAssets>runtime</ExcludeAssets>`.
2. **File-only, read-only:** No HTTP calls. Plugin must never write or mutate any user media file.
3. **No custom `IItemResolver`:** Architecture constraint from Phase 1, unchanged.
4. **No sudo from agents:** Docker/daemon operations go in copy-paste blocks for the operator.
5. **GSD workflow:** All code changes via `/gsd:execute-phase`.

---

## Standard Stack

### Core (no new packages required for Phase 3)

| Library | Version | Purpose | Status |
|---------|---------|---------|--------|
| `Jellyfin.Controller` | `10.10.7` | `ILocalImageProvider`, `LocalImageInfo`, `ImageType` | Already in csproj [VERIFIED: NuGet registry, Phase 1] |
| `Jellyfin.Model` | `10.10.7` | `BaseItem`, `Series` | Already in csproj [VERIFIED: NuGet registry, Phase 1] |
| `System.IO` | BCL / net8.0 | `File.Exists`, `Path.Combine` | Already used in Phase 1/2 patterns |
| `xunit` / `Moq` | `2.9.3` / `4.20.72` | Tests | Already in test project |

No new NuGet packages. No `dotnet add package` commands required.

### Existing Codebase Assets to Reuse

| Asset | File | Phase 3 Reuse |
|-------|------|---------------|
| `Constants.ProviderName` | `Constants.cs` | `Name` property on new image provider |
| `PathUtils.GetChannelNameFromPath` | `Utils/PathUtils.cs` | Logging context in `YoutarrSeriesImageProvider` |
| `Plugin.cs` `GetPages()` | `Plugin.cs` | Already wires `configPage.html` — only flesh out the HTML |
| `PluginConfiguration.cs` | `Configuration/PluginConfiguration.cs` | Add `EpisodeNumberingScheme` + `MaxDescriptionLength` (Phase 2 plan specifies these) |
| `PluginServiceRegistrator.cs` | `PluginServiceRegistrator.cs` | Add `AddSingleton<ILocalImageProvider, YoutarrSeriesImageProvider>()` if needed |
| `ILocalMetadataProvider<Series>` pattern | `YoutarrSeriesNfoProvider.cs` | `ILocalImageProvider` follows the same DI auto-discovery and `Name` property pattern |

---

## Package Legitimacy Audit

Phase 3 installs no new packages. Existing packages (`Jellyfin.Controller 10.10.7`, `xunit 2.9.3`, `Moq 4.20.72`) were vetted in Phase 1.

**No new packages to audit.**

---

## The Built-in Provider Decision: What Needs Custom Code

### ART-01 and ART-03: Built-in Covers These — No Custom Provider

**`LocalImageProvider` (Jellyfin core) — handles Series** [VERIFIED: Jellyfin source `MediaBrowser.LocalMetadata/Images/LocalImageProvider.cs`]:
- `Supports(BaseItem item)`: returns `true` for Series (non-Episode, non-Audio, non-Photo items that support local metadata)
- Searches for base filenames `"poster"`, `"folder"`, `"cover"`, `"default"`, `"show"` → returns as `ImageType.Primary`
- Searches for `"fanart"`, `"background"`, `"art"`, `"backdrop"` + numeric suffixes and `extrafanart/` directory → returns as `ImageType.Backdrop`
- `poster.jpg` in the channel folder matches the `"poster"` pattern → returned as `ImageType.Primary` automatically
- This provider has `Order = 0` (highest local priority)

**Conclusion for ART-01:** Jellyfin's built-in `LocalImageProvider` will pick up `poster.jpg` as Series Primary with no plugin intervention. **Do NOT write a custom Series Primary image provider** — it would compete with the built-in for no gain and add an unnecessary priority conflict risk.

**`EpisodeLocalImageProvider` (Jellyfin core) — handles Episodes** [VERIFIED: Jellyfin source `MediaBrowser.LocalMetadata/Images/EpisodeLocalImageProvider.cs`]:
- `Supports(BaseItem item)`: returns `true` for `Episode` items
- Searches for files matching the episode's basename (e.g., for `Video [ytid].mp4` → looks for `Video [ytid].jpg`, `Video [ytid].png`) — same directory and `metadata/` subdirectory
- Also matches `<basename>-thumb.jpg` suffix variant
- Returns all matches as `ImageType.Primary`
- `Order = 0`

**Conclusion for ART-03:** Jellyfin's built-in `EpisodeLocalImageProvider` will pick up `<videofilename>.jpg` as Episode Primary with no plugin intervention. **Do NOT write a custom Episode image provider** — it is already handled.

**Conclusion for ART-04:** Both built-in providers return empty lists when files are absent. No exception paths. Graceful degradation is the default behavior.

### ART-02: Requires a Custom Provider

**The gap:** The built-in `LocalImageProvider` looks for `fanart.jpg`, `background.jpg`, `backdrop.jpg`, or `art.jpg` for Series Backdrop. Youtarr writes ONLY `poster.jpg` at channel level — no `fanart.jpg` or `backdrop.jpg`. [VERIFIED: Youtarr source `videoDownloadPostProcessFiles.js` confirms this]

**The solution:** `YoutarrSeriesImageProvider : ILocalImageProvider` that returns `poster.jpg` as BOTH `ImageType.Primary` (redundant with built-in — the built-in wins if it runs first, no harm) and `ImageType.Backdrop`.

**Alternatively:** Only return `ImageType.Backdrop` from the custom provider and let the built-in handle Primary. This is cleaner — one responsibility per provider.

**Recommended approach:** Return ONLY `ImageType.Backdrop` from `YoutarrSeriesImageProvider`. The built-in handles Primary with higher priority. This avoids any ordering conflict.

**Aspect ratio tradeoff:** Youtarr's `poster.jpg` is portrait aspect (2:3 typically, ~300x450 or 1000x1500). Jellyfin's backdrop is displayed at 16:9. Using a portrait image as backdrop means Jellyfin will letter-box or crop it when displaying the backdrop. This is visually suboptimal (narrow image on a wide canvas) but it is the only image Youtarr provides. **Document this tradeoff clearly** — it is the correct behavior given the available files. Users who want a proper backdrop must manually add a `fanart.jpg` to the channel folder; the built-in will then pick it up automatically.

---

## Architecture Patterns

### System Architecture Diagram

```
Youtarr channel folder on disk:
  /media/MyChannel/
    ├── poster.jpg                   ← portrait 2:3 channel art
    ├── Video [ytid].mp4
    └── Video [ytid].jpg             ← per-video thumbnail (any aspect)

         │
         │  Jellyfin image refresh pipeline
         ▼

  For Series (MyChannel):
    LocalImageProvider (built-in, Order=0)
      → searches for "poster"  → poster.jpg found → ImageType.Primary  ✓  (ART-01)
      → searches for "fanart"  → not found
      → searches for "backdrop"→ not found
      → returns: [ {poster.jpg, Primary} ]

    YoutarrSeriesImageProvider (plugin, Order=99 or higher)
      → Supports(Series) = true
      → GetSupportedImages() = [ ImageType.Backdrop ]
      → GetImages(series, dirSvc):
          posterPath = Path.Combine(series.Path, "poster.jpg")
          if File.Exists(posterPath):
              yield LocalImageInfo { FileInfo = fileInfo, Type = ImageType.Backdrop }
          // else: empty enumerable — ART-04 graceful degradation
      → returns: [ {poster.jpg, Backdrop} ]  (ART-02)

  ProviderManager merges: Series gets Primary = poster.jpg, Backdrop = poster.jpg

  For Episode (Video [ytid].mp4):
    EpisodeLocalImageProvider (built-in, Order=0)
      → Supports(Episode) = true
      → searches for "Video [ytid].jpg" → found → ImageType.Primary  ✓  (ART-03)
      → returns: [ {Video [ytid].jpg, Primary} ]

         │
         ▼

  Jellyfin UI:
    Series MyChannel: poster art displayed; backdrop = poster.jpg (portrait, letter-boxed)
    Episode Video [ytid]: thumbnail displayed as episode card image
```

### Recommended Project Structure After Phase 3

```
Jellyfin.Plugin.Youtarr/
├── Configuration/
│   ├── PluginConfiguration.cs       # +EpisodeNumberingScheme, +MaxDescriptionLength (Phase 2)
│   └── configPage.html              # FLESHED OUT: 3 settings wired to ApiClient
├── Constants.cs                     # Unchanged
├── Parsers/
│   └── YoutarrNfoParser.cs          # Phase 2
├── Models/
│   └── YoutarrVideoData.cs          # Phase 2
├── Providers/
│   ├── YoutarrSeriesNfoProvider.cs  # Phase 1
│   ├── YoutarrEpisodeNfoProvider.cs # Phase 2
│   └── YoutarrSeriesImageProvider.cs  # NEW Phase 3 — ILocalImageProvider, Backdrop only
└── Utils/
    ├── PathUtils.cs
    └── YoutarrPrefixIgnoreRule.cs
```

No new `Parsers/` or `Models/` files. Only one new provider class and one HTML update.

---

## Core Pattern: ILocalImageProvider for Series (Backdrop only)

### Interface — Verified Signatures

```csharp
// MediaBrowser.Controller.Providers.ILocalImageProvider (marker interface)
// Extends IImageProvider which provides:
//   string Name { get; }
//   bool Supports(BaseItem item)
// Plus:
//   IEnumerable<LocalImageInfo> GetImages(BaseItem item, IDirectoryService directoryService)
//
// Note: ILocalImageProvider does NOT have GetSupportedImages — that is on IRemoteImageProvider.
// Local providers return LocalImageInfo (not RemoteImageInfo).
// [VERIFIED: Jellyfin source MediaBrowser.Controller/Providers/ILocalImageProvider.cs]
```

### YoutarrSeriesImageProvider Skeleton

```csharp
// Source: ILocalImageProvider pattern confirmed from Jellyfin source
// [CITED: github.com/jellyfin/jellyfin MediaBrowser.LocalMetadata/Images/LocalImageProvider.cs]
using System.Collections.Generic;
using System.IO;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Youtarr.Providers;

/// <summary>
/// Supplements Jellyfin's built-in LocalImageProvider for Series items.
/// The built-in already picks up <c>poster.jpg</c> as the Series Primary image.
/// This provider's ONLY responsibility is ART-02: returning <c>poster.jpg</c> as
/// <c>ImageType.Backdrop</c>, since Youtarr writes no separate fanart/backdrop file.
/// Gracefully returns an empty list when <c>poster.jpg</c> is absent (ART-04).
/// </summary>
public class YoutarrSeriesImageProvider : ILocalImageProvider
{
    private readonly ILogger<YoutarrSeriesImageProvider> _logger;

    public YoutarrSeriesImageProvider(ILogger<YoutarrSeriesImageProvider> logger)
    {
        _logger = logger;
    }

    // MUST match Constants.ProviderName — same as all other Youtarr providers
    public string Name => Constants.ProviderName;

    public bool Supports(BaseItem item)
        => item is Series;

    public IEnumerable<LocalImageInfo> GetImages(BaseItem item, IDirectoryService directoryService)
    {
        var posterPath = Path.Combine(item.Path, "poster.jpg");
        if (!File.Exists(posterPath))
        {
            _logger.LogDebug("[Youtarr] No poster.jpg found for Series at {Path}; skipping backdrop.", item.Path);
            yield break; // ART-04: empty enumerable, no exception
        }

        // Return poster.jpg as Backdrop ONLY.
        // The built-in LocalImageProvider already returns poster.jpg as Primary (Order=0).
        // Providing it again as Primary here would be redundant and could cause ordering issues.
        yield return new LocalImageInfo
        {
            FileInfo = new FileSystemMetadata { FullName = posterPath },
            Type = ImageType.Backdrop,
        };
    }
}
```

**DI registration:** `ILocalImageProvider` implementations are auto-discovered by Jellyfin's DI at startup (same mechanism as `ILocalMetadataProvider<T>`). No explicit `AddSingleton` call in `PluginServiceRegistrator` is required. The precedent (Phase 1 SUMMARY.md) showed `IResolverIgnoreRule` was added explicitly as a safety net — for `ILocalImageProvider` apply the same conservative approach: register explicitly in `PluginServiceRegistrator` as a safety net:

```csharp
serviceCollection.AddSingleton<ILocalImageProvider, YoutarrSeriesImageProvider>();
```

**FileSystemMetadata note:** `LocalImageInfo.FileInfo` requires a `FileSystemMetadata` object from `MediaBrowser.Model.IO`. Only `FullName` needs to be set for image serving. `directoryService.GetFileSystemEntries()` could be used to get a proper `FileSystemMetadata`, but `new FileSystemMetadata { FullName = posterPath }` is used by the built-in `EpisodeLocalImageProvider` and is sufficient.

---

## ART-02 Tradeoff: Portrait Poster as Backdrop

| Aspect | Detail |
|--------|--------|
| `poster.jpg` typical dimensions | 1000×1500 (2:3 portrait) or similar |
| Jellyfin backdrop display area | 16:9 landscape (e.g., 1920×1080) |
| Visual result | Jellyfin letter-boxes or center-crops the portrait image on the backdrop canvas — narrow black bars on sides or top cropped off |
| Alternative if available | If a user manually adds `fanart.jpg` to the channel folder, the built-in `LocalImageProvider` picks it up as Backdrop automatically and takes precedence (it runs at Order=0 before the plugin) |
| Recommendation | Accept the tradeoff. Document in README that users can add `fanart.jpg` to any channel folder for a proper 16:9 backdrop. The plugin provides backdrop-from-poster as a "better than blank" fallback. |

**No behavior change needed.** The tradeoff is intentional and documented. Jellyfin will serve whatever image is provided; it is the client's responsibility to display it.

---

## PLUG-03: Configuration Page

### Persistence Mechanism

`BasePlugin<PluginConfiguration>` in Jellyfin 10.10.x serializes the `PluginConfiguration` object to XML at:
```
config/plugins/<GUID>/<GUID>.xml
```

This XML file is read on server startup and survives restarts automatically. `ApiClient.updatePluginConfiguration(guid, config)` in the Dashboard JS calls the server-side `POST /Plugins/{PluginId}/Configuration` endpoint, which invokes `BasePlugin.UpdateConfiguration(config)` and triggers serialization. No additional persistence code is needed in the plugin — `BasePlugin<T>` handles it. [ASSUMED: based on Jellyfin plugin architecture; pattern confirmed by tubearchivist-jf-plugin behavior]

### Config Page HTML/JS Pattern

Pattern confirmed from tubearchivist-jf-plugin `configPage.html` [CITED: github.com/tubearchivist/tubearchivist-jf-plugin]:

```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <title>YoutarrMetadata</title>
</head>
<body>
    <!-- data-role="page" is required — Jellyfin's Dashboard SPA uses this for routing -->
    <!-- data-require lists the Emby/Jellyfin web component dependencies -->
    <div id="YoutarrConfigPage"
         data-role="page"
         class="page type-interior pluginConfigurationPage"
         data-require="emby-input,emby-button,emby-select,emby-checkbox">
        <div data-role="content">
            <div class="content-primary">
                <h2>Youtarr Metadata</h2>
                <p>
                    Organizes your Youtarr download folder as a Jellyfin TV Shows library.
                    Each YouTube channel folder becomes a Series; each video becomes an Episode.
                </p>

                <form id="YoutarrConfigForm">

                    <!-- ART-01 / LIB-03: Year Seasons Toggle -->
                    <div class="checkboxContainer checkboxContainer-withDescription">
                        <label class="emby-checkbox-label">
                            <input is="emby-checkbox" type="checkbox"
                                   id="YearSeasons" name="YearSeasons" />
                            <span>Group episodes into year-based seasons</span>
                        </label>
                        <div class="fieldDescription">
                            When enabled, episodes are grouped by upload year (e.g. Season 2024).
                            When disabled, all episodes are placed in a single Season 1.
                        </div>
                    </div>

                    <!-- EPI-05: Episode Numbering Scheme -->
                    <div class="inputContainer">
                        <label class="inputLabel inputLabelUnfocused" for="EpisodeNumberingScheme">
                            Episode Numbering
                        </label>
                        <select is="emby-select"
                                id="EpisodeNumberingScheme"
                                name="EpisodeNumberingScheme"
                                class="emby-select-withcolor emby-select">
                            <option value="Default">Default (Jellyfin auto-sequences)</option>
                            <option value="YYYYMMDD">YYYYMMDD (e.g. 20250804 for August 4th, 2025)</option>
                        </select>
                        <div class="fieldDescription">
                            YYYYMMDD encodes the upload date as the episode number.
                            Note: two videos uploaded on the same day will share an episode number.
                        </div>
                    </div>

                    <!-- EPI-04: Max Description Length -->
                    <div class="inputContainer">
                        <label class="inputLabel inputLabelUnfocused" for="MaxDescriptionLength">
                            Max Description Length (characters)
                        </label>
                        <input is="emby-input" type="number"
                               id="MaxDescriptionLength"
                               name="MaxDescriptionLength"
                               min="0" />
                        <div class="fieldDescription">
                            YouTube descriptions are often very long. Set 0 for no truncation.
                            Default: 500 characters.
                        </div>
                    </div>

                    <div>
                        <button is="emby-button" type="submit"
                                class="raised button-submit block">
                            <span>Save</span>
                        </button>
                    </div>
                </form>
            </div>
        </div>
    </div>

    <script>
        /*
         * Plugin GUID: must match Plugin.StaticId exactly — never change between releases.
         * Source: Jellyfin.Plugin.Youtarr/Plugin.cs StaticId field.
         */
        var YoutarrConfig = {
            pluginUniqueId: '80302d7f-7fc3-4b1c-9a3f-fd85b98b9a69'
        };

        // PAGE SHOW: load current config from server and populate form
        document.querySelector('#YoutarrConfigPage')
            .addEventListener('pageshow', function () {
                Dashboard.showLoadingMsg();
                ApiClient.getPluginConfiguration(YoutarrConfig.pluginUniqueId)
                    .then(function (config) {
                        document.querySelector('#YearSeasons').checked =
                            config.YearSeasons;
                        document.querySelector('#EpisodeNumberingScheme').value =
                            config.EpisodeNumberingScheme;
                        document.querySelector('#MaxDescriptionLength').value =
                            config.MaxDescriptionLength;
                        Dashboard.hideLoadingMsg();
                    });
            });

        // FORM SUBMIT: save config changes to server
        document.querySelector('#YoutarrConfigForm')
            .addEventListener('submit', function (e) {
                Dashboard.showLoadingMsg();
                ApiClient.getPluginConfiguration(YoutarrConfig.pluginUniqueId)
                    .then(function (config) {
                        config.YearSeasons =
                            document.querySelector('#YearSeasons').checked;
                        config.EpisodeNumberingScheme =
                            document.querySelector('#EpisodeNumberingScheme').value;
                        config.MaxDescriptionLength =
                            parseInt(document.querySelector('#MaxDescriptionLength').value, 10);

                        ApiClient.updatePluginConfiguration(
                            YoutarrConfig.pluginUniqueId, config
                        ).then(function (result) {
                            Dashboard.processPluginConfigurationUpdateResult(result);
                        });
                    });
                e.preventDefault();
                return false;
            });
    </script>
</body>
</html>
```

**Key details from tubearchivist-jf-plugin pattern:**
- `data-require="emby-input,emby-button,emby-select,emby-checkbox"` — all four web components must be declared if any are used
- `<select is="emby-select">` — Jellyfin Dashboard uses custom element upgrades; the `is="emby-*"` attribute is required for proper styling
- `<input is="emby-checkbox" type="checkbox">` — same pattern for checkboxes
- `ApiClient.getPluginConfiguration(guid)` returns a Promise resolving to the config object with property names matching `PluginConfiguration.cs` property names exactly (case-sensitive)
- `config.EpisodeNumberingScheme` will be the string `"Default"` or `"YYYYMMDD"` because the C# enum serializes by name in XML — the `<select>` option values must match these strings exactly
- `parseInt(..., 10)` is required for `MaxDescriptionLength` — the input's `.value` is always a string; failure to convert causes the server to receive `"500"` (string) which may fail deserialization into `int`

**IHasWebPages wiring (already done in Phase 1):**

```csharp
// Plugin.cs — GetPages() is already implemented; no change needed for Phase 3
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
```

The `configPage.html` is already registered as an embedded resource in the `.csproj`. Phase 3 only replaces the stub HTML content.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Series Primary image | Custom `ILocalImageProvider` returning `poster.jpg` as Primary | Built-in `LocalImageProvider` | It already searches `"poster"` filename pattern for Series; redundant code causes ordering confusion |
| Episode Primary image | Custom `ILocalImageProvider` for episodes | Built-in `EpisodeLocalImageProvider` | It already matches same-basename `.jpg` next to video files with Order=0 |
| Config XML persistence | Custom serialization, file writes | `BasePlugin<PluginConfiguration>` (automatic) | `BasePlugin<T>` handles all serialization/deserialization to the per-GUID XML path |
| Config page routing | Embedding URL routes in plugin code | `IHasWebPages.GetPages()` with `EmbeddedResourcePath` | Already wired in Phase 1; Dashboard handles routing automatically |
| Backdrop image download | Fetching backdrop from YouTube | `poster.jpg` reused as Backdrop | Youtarr writes no separate backdrop file; this is the correct file-only approach |

---

## Common Pitfalls

### Pitfall 1: Adding a Custom Series Primary Provider Conflicts With the Built-in

**What goes wrong:** `YoutarrSeriesImageProvider` returns `ImageType.Primary` for `poster.jpg`. The built-in `LocalImageProvider` also returns `ImageType.Primary` for `poster.jpg`. Jellyfin deduplicates by path AND type — if both return the same file as Primary, the result is fine. But if the custom provider has a higher `Order` value (lower priority number) than the built-in, there can be unexpected resolution order. More subtly, if the user has "Replace all images" enabled, having two competing Primary sources can cause the image to be cleared then re-applied on each scan.

**How to avoid:** Return ONLY `ImageType.Backdrop` from `YoutarrSeriesImageProvider`. Let the built-in handle Primary.

**Warning signs:** Series poster flickers or disappears on rescan; "image replaced" entries in scan logs.

---

### Pitfall 2: Image Cache Staleness After Adding New Fixture Files

**What goes wrong:** Adding `poster.jpg` to the test harness while Jellyfin has already scanned the channel folder causes the cached metadata to show "no artwork." Jellyfin cached the "no images found" result and does not re-check on subsequent scans unless forced.

**How to avoid:** After updating fixtures or changing image providers, always use "Refresh metadata → Replace all images" from the Dashboard for the affected Series, or call:

```bash
# Force-refresh images for a specific series
curl -X POST "http://localhost:8096/Items/{seriesId}/Refresh?Recursive=true&ImageRefreshMode=FullRefresh&MetadataRefreshMode=Default&ReplaceAllImages=true" \
  -H "Authorization: MediaBrowser Token=\"${API_KEY}\""
```

---

### Pitfall 3: `LocalImageInfo.FileInfo` Requires FullName — Other Fields Optional

**What goes wrong:** `LocalImageInfo.FileInfo` is typed as `FileSystemMetadata` from `MediaBrowser.Model.IO`. If only the `FullName` property is set and other properties (like `IsDirectory`, `Length`) are left at defaults, Jellyfin may log a warning. In practice, Jellyfin uses only `FullName` for image serving at this stage.

**How to avoid:** Set at minimum `FullName`. If the provider has access to a real `FileSystemMetadata` from `directoryService.GetFileSystemEntries()`, use that. For the minimal case, `new FileSystemMetadata { FullName = posterPath }` is sufficient and is the pattern used in the built-in `EpisodeLocalImageProvider`.

---

### Pitfall 4: Config Page `<select>` Option Values Must Match C# Enum Names

**What goes wrong:** `PluginConfiguration.EpisodeNumberingScheme` is a C# enum serialized to XML by name (not by integer value). `<option value="0">Default</option>` would send the string `"0"` to the server, which cannot deserialize `"0"` into the `EpisodeNumberingScheme` enum — it would throw a deserialization exception and reset the setting to default.

**How to avoid:** Use exact enum member names as option values:
- `<option value="Default">...</option>` — matches `EpisodeNumberingScheme.Default`
- `<option value="YYYYMMDD">...</option>` — matches `EpisodeNumberingScheme.YYYYMMDD`

---

### Pitfall 5: `parseInt` Required for Number Inputs in Config Page JS

**What goes wrong:** `config.MaxDescriptionLength = document.querySelector('#MaxDescriptionLength').value` sends a string (e.g., `"500"`) to the server. Jellyfin's config deserializer may or may not coerce strings to integers depending on the serializer version — this is fragile.

**How to avoid:** Always `parseInt(value, 10)` for numeric fields before assigning to the config object.

---

### Pitfall 6: `EpisodeLocalImageProvider` Only Works When `poster.jpg` Is Not Confused for Episode Image

**What goes wrong:** In the flat layout, `poster.jpg` is in the channel folder. Its basename is `poster`. The `EpisodeLocalImageProvider` looks for files whose basename matches the video file's basename. Since `poster` does not match any video basename (e.g., `Video Title [ytid]`), the built-in will never mistakenly assign `poster.jpg` as an Episode image.

**But:** If a user's video file is accidentally named `poster.mp4`, its sidecar `poster.jpg` would be picked up as Episode Primary AND the built-in Series LocalImageProvider would also try to claim `poster.jpg` as Series Primary. Edge case — document but no code change needed.

---

## Live Verification Approach: Extending the Harness

### New Fixtures Required

Add to `test/jellyfin-load-test/media/ChannelA/` (which already exists after Phase 2):

```
test/jellyfin-load-test/media/
├── ChannelA/
│   ├── poster.jpg             ← NEW: any small JPEG (even 1×1 pixel; just needs to exist)
│   ├── Video 2023 [ytid1].jpg ← NEW: per-video thumbnail (any small JPEG)
│   ├── Video 2024 [ytid2].jpg ← NEW: per-video thumbnail
│   ├── Video 2023 [ytid1].mp4 ← existing
│   ├── Video 2023 [ytid1].nfo ← existing
│   ├── Video 2024 [ytid2].mp4 ← existing
│   └── Video 2024 [ytid2].nfo ← existing
└── ChannelB/
    ├── Missing Date [ytid3].mp4 ← existing (no poster.jpg — tests ART-04)
    └── Missing Date [ytid3].nfo ← existing
```

**ChannelB intentionally has no `poster.jpg` and no video thumbnail** — this is the ART-04 graceful-degradation fixture. The scan must complete without error.

**Creating minimal 1×1 JPEG fixtures:** Any valid JPEG works. Use `convert -size 1x1 xc:red poster.jpg` if ImageMagick is available, or use a pre-committed tiny binary blob. The simplest approach: copy a 1×1 pixel JPEG from a known-good source. Alternatively, embed a base64-decoded minimal JPEG in a fixture creation script.

### Verification Commands

```bash
API_KEY="<admin api key from Dashboard → API Keys>"
BASE="http://localhost:8096"

# 1. Force a full image refresh (replaces any cached "no images" state)
curl -s -X POST "${BASE}/Items/Refresh?Recursive=true" \
  -H "Authorization: MediaBrowser Token=\"${API_KEY}\""

# Wait 30-60 seconds for scan to complete

# 2. Verify ART-01: Series Primary image set for ChannelA
CHANALA_ID=$(curl -s "${BASE}/Items?SearchTerm=ChannelA&IncludeItemTypes=Series" \
  -H "Authorization: MediaBrowser Token=\"${API_KEY}\"" \
  | python3 -m json.tool | grep '"Id"' | head -1 | tr -d ' "Id:,')

curl -s "${BASE}/Items/${CHANALA_ID}" \
  -H "Authorization: MediaBrowser Token=\"${API_KEY}\"" \
  | python3 -m json.tool | grep -A5 '"ImageTags"'
# PASS: "Primary" key present in ImageTags

# 3. Verify ART-02: Series Backdrop set for ChannelA
curl -s "${BASE}/Items/${CHANALA_ID}" \
  -H "Authorization: MediaBrowser Token=\"${API_KEY}\"" \
  | python3 -m json.tool | grep -A5 '"BackdropImageTags"'
# PASS: BackdropImageTags array is non-empty

# 4. Verify ART-03: Episode thumbnail set
EPISODE_ID=$(curl -s "${BASE}/Shows/${CHANALA_ID}/Episodes" \
  -H "Authorization: MediaBrowser Token=\"${API_KEY}\"" \
  | python3 -m json.tool | grep '"Id"' | head -1 | tr -d ' "Id:,')

curl -s "${BASE}/Items/${EPISODE_ID}" \
  -H "Authorization: MediaBrowser Token=\"${API_KEY}\"" \
  | python3 -m json.tool | grep '"Primary"'
# PASS: Primary image tag present on episode

# 5. Verify ART-04: ChannelB (no artwork) scan completes without error
CHANB_ID=$(curl -s "${BASE}/Items?SearchTerm=ChannelB&IncludeItemTypes=Series" \
  -H "Authorization: MediaBrowser Token=\"${API_KEY}\"" \
  | python3 -m json.tool | grep '"Id"' | head -1 | tr -d ' "Id:,')
curl -s "${BASE}/Items/${CHANB_ID}" \
  -H "Authorization: MediaBrowser Token=\"${API_KEY}\"" \
  | python3 -m json.tool | grep '"ImageTags"'
# PASS: ImageTags is empty {}; no exceptions in logs; series still resolves
docker exec jellyfin-plugin-test grep -i "exception\|error" /config/log/log_*.log | grep -v "^Binary" | tail -20
# PASS: no new exceptions from the artwork scan

# 6. Verify PLUG-03: Config page renders
# Navigate to Dashboard → Plugins → YoutarrMetadata → Settings
# PASS: page loads; all 3 fields visible; changing MaxDescriptionLength and clicking Save
# persists the change (reload page and verify value is retained)
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Writing `tvshow.nfo` / `fanart.jpg` for artwork | `ILocalImageProvider` returning existing files by type | Jellyfin 10.x (plugin era) | Plugin never writes files; reads only |
| `IRemoteImageProvider` for all artwork | `ILocalImageProvider` when files are on disk | Jellyfin plugin API evolution | Local is faster, offline, no network dependency |
| Configuring per-user settings via separate admin API | `BasePlugin<T>` XML config + `IHasWebPages` + `ApiClient.updatePluginConfiguration` | Jellyfin 10.x plugin SDK | Standard pattern, survives restarts automatically |

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `BasePlugin<PluginConfiguration>` serializes config to XML and reloads on startup automatically, with no extra code | PLUG-03: Persistence Mechanism | Low risk — Phase 1 already confirms config round-trips; confirmed by tubearchivist-jf-plugin working the same way |
| A2 | `ApiClient.updatePluginConfiguration` call in JS is the correct method name for Jellyfin 10.10.7 web UI | Config Page HTML/JS Pattern | Low risk — confirmed from tubearchivist-jf-plugin which targets 10.11.x; method names are stable across Jellyfin 10.x |
| A3 | `new FileSystemMetadata { FullName = posterPath }` is sufficient for `LocalImageInfo.FileInfo` (Jellyfin only uses FullName for serving) | YoutarrSeriesImageProvider skeleton | Low risk — EpisodeLocalImageProvider in Jellyfin source uses equivalent minimal construction; can be observed live |
| A4 | Phase 2 has been fully executed before Phase 3 runs (EpisodeNfoProvider, YoutarrNfoParser, updated PluginConfiguration are in the codebase) | All sections assume Phase 2 artifacts | Medium risk — if Phase 2 is incomplete, the config page references fields not yet added; planner must gate Phase 3 on Phase 2 complete |

---

## Open Questions

1. **Phase 2 execution state**
   - What we know: Phase 2 plans (02-01 through 02-04) exist but no SUMMARY files exist — Phase 2 may be partially or fully un-executed
   - What's unclear: Whether `YoutarrEpisodeNfoProvider`, `YoutarrNfoParser`, `YoutarrVideoData`, and the new `PluginConfiguration` fields exist on disk when Phase 3 begins
   - Recommendation: Phase 3 planner must check for these files and either (a) gate Phase 3 behind Phase 2 completion, or (b) include Phase 2 artifact creation as Wave 0 tasks in Phase 3

2. **`ILocalImageProvider` DI auto-discovery vs explicit registration**
   - What we know: Phase 1 SUMMARY noted that `IResolverIgnoreRule` was registered both explicitly (via `PluginServiceRegistrator`) and potentially auto-discovered; Phase 1 live verify was pending
   - What's unclear: Whether `ILocalImageProvider` plugin implementations are auto-discovered by Jellyfin 10.10.7 DI without explicit `AddSingleton`
   - Recommendation: Add explicit `AddSingleton<ILocalImageProvider, YoutarrSeriesImageProvider>()` in `PluginServiceRegistrator` as a safety net (same conservative pattern as `IResolverIgnoreRule`)

3. **Minimal JPEG fixture creation without ImageMagick**
   - What we know: Test harness needs valid JPEG files; empty files may not be valid images
   - What's unclear: Whether Jellyfin's image pipeline requires a valid JPEG header or just a file existence check before serving
   - Recommendation: Commit a tiny (< 1KB) valid JPEG as a fixture file in the repo. The planner can generate one via a base64-encoded blob in the plan or use Python Pillow to create a 1×1 pixel JPEG.

---

## Environment Availability

Phase 3 has no new external dependencies. All required tools were audited in Phase 1/2:

| Dependency | Required By | Available | Notes |
|------------|------------|-----------|-------|
| `dotnet SDK 8.x` | Build, publish | Confirmed Phase 1 | Unchanged |
| Docker | Live verification harness | Agent cannot start — operator runs `sudo systemctl start docker` | Unchanged |
| `jellyfin/jellyfin:10.10.7` image | Load-test harness | Already pulled if Phase 1/2 ran | Re-pull if stale |

---

## Security Domain

`security_enforcement: true`, `security_asvs_level: 1` from config.

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | No | Plugin does not handle auth; Jellyfin's own auth gates the config page |
| V3 Session Management | No | Handled by Jellyfin core |
| V4 Access Control | Partial | Config page is Dashboard-only; Jellyfin restricts it to admin users automatically |
| V5 Input Validation | Yes | `MaxDescriptionLength` number input: validate server-side that value is >= 0; JS parseInt is client-side only |
| V6 Cryptography | No | No cryptographic operations |

### Known Threat Patterns

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Malformed `poster.jpg` path traversal | Tampering | `Path.Combine(item.Path, "poster.jpg")` is safe — `item.Path` is Jellyfin-provided and already canonicalized; the concatenated filename is a constant (`"poster.jpg"`), not user input |
| Config page XSS | Tampering | Jellyfin Dashboard handles HTML encoding; plugin HTML does not render user-supplied strings into the page |
| `MaxDescriptionLength` extreme value (e.g., `-1`, `INT_MAX`) | Tampering | Server-side: clamp to `>= 0` in the provider; accept any non-negative value as valid |

---

## Sources

### Primary (HIGH confidence)
- Jellyfin source `MediaBrowser.LocalMetadata/Images/LocalImageProvider.cs` — `Supports()` method explicitly excludes Episodes; Series uses `poster`/`folder`/`cover` for Primary, `fanart`/`backdrop`/`background` for Backdrop [VERIFIED via WebFetch]
- Jellyfin source `MediaBrowser.LocalMetadata/Images/EpisodeLocalImageProvider.cs` — matches same-basename `.jpg` next to video for Episode Primary; `Supports(Episode) = true`; `Order = 0` [VERIFIED via WebFetch]
- Jellyfin source `MediaBrowser.Controller/Providers/ILocalImageProvider.cs` — marker interface extending `IImageProvider`; `GetImages(BaseItem, IDirectoryService)` returns `IEnumerable<LocalImageInfo>` [VERIFIED via WebFetch]
- tubearchivist-jf-plugin `configPage.html` — `ApiClient.getPluginConfiguration`/`updatePluginConfiguration` pattern; `pageshow` event listener; `Dashboard.processPluginConfigurationUpdateResult(result)` [VERIFIED via WebFetch]
- Youtarr source `videoDownloadPostProcessFiles.js` — only `poster.jpg` at channel level; no `fanart.jpg`, no `backdrop.jpg` [CITED: Youtarr source, confirmed in FEATURES.md Phase 1 research]
- Phase 1 Phase 2 research files in `.planning/research/` — established patterns, plugin GUID, DI mechanism [VERIFIED: read directly from codebase]

### Secondary (MEDIUM confidence)
- CLAUDE.md STACK.md section — `LocalImageProvider source` note citing `LocalImageProvider.cs` for image file patterns
- ARCHITECTURE.md `## Integration Points` table — `LocalImageProvider (core)` noted as detecting `poster.jpg` as Series Primary
- PITFALLS.md Pitfall 10 — "Channel-Level Series Artwork Not Picked Up" documents filenames expected by Jellyfin

---

## Metadata

**Confidence breakdown:**
- ART-01/ART-03 decision (built-in covers): HIGH — verified from Jellyfin source; `LocalImageProvider` and `EpisodeLocalImageProvider` behavior directly confirmed
- ART-02 decision (custom provider for backdrop only): HIGH — absence of `fanart.jpg` from Youtarr is confirmed; built-in backdrop logic confirmed from source
- PLUG-03 config page: HIGH — tubearchivist-jf-plugin is a working reference implementation; `IHasWebPages` wiring already done in Phase 1

**Research date:** 2026-06-09
**Valid until:** 2026-09-09 (stable Jellyfin 10.10.x; provider interfaces unlikely to change)
