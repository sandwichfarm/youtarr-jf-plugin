# Pitfalls Research

**Domain:** Jellyfin 10.10.x metadata/provider plugin for YouTube archive (Youtarr) — file-only, channels-as-Shows, year-as-Seasons
**Researched:** 2026-06-09
**Confidence:** HIGH (pitfalls sourced from verified Jellyfin GitHub issues, official docs, and plugin template; LOW-confidence items flagged)

---

## Critical Pitfalls

### Pitfall 1: The `<movie>` NFO Root Element Classification Trap

**What goes wrong:**
Youtarr writes per-video NFOs with a `<movie>` root element. Jellyfin's resolver pipeline classifies items based on both library content-type and on-disk structure — not on the NFO root element alone. If the library is configured as "Movies" (or "Mixed"), every video is resolved as a `Movie`, not an `Episode`. The `<movie>` root NFO actively reinforces this: when the `MovieResolver` runs and finds a `<movie>` NFO, it wins with `HasMetadata = true` before any `EpisodeNfoProvider` can run. Attempts to add a custom `ILocalMetadataProvider<Episode>` that reads these same `<movie>` NFOs will be skipped because the item was already resolved and typed as `Movie` upstream, before metadata providers run.

**Why it happens:**
Type classification (resolver) happens during library scanning, earlier in the pipeline than metadata fetching. The NFO root element is not the trigger for type classification — folder/file structure and library type setting are. A `<movie>` NFO under a "Shows" library does not automatically create an Episode; the resolver decides the item type first. Developers assume the NFO drives the type; it does not.

**How to avoid:**
The library containing the Youtarr download folder **must** be created as a "Shows/TV Shows" content-type library. Jellyfin's `SeriesResolver` and `SeasonResolver` will then classify the channel folders as Series and year-named subfolders as Seasons, and video files as Episodes. The `<movie>` NFO root element will confuse `EpisodeNfoProvider` (which expects `<episodedetails>`), so the plugin's custom `ILocalMetadataProvider<Episode>` must explicitly parse the `<movie>` XML root and remap its fields to `MetadataResult<Episode>`. Do not rely on Jellyfin's built-in `EpisodeNfoProvider` to handle Youtarr's `<movie>` NFOs — it will silently return `HasMetadata = false` and log "EpisodeNfoProvider returned no metadata."

**Warning signs:**
- Videos appear in the Jellyfin Movies library instead of Shows
- "EpisodeNfoProvider returned no metadata for [path]" in server logs
- Channel folders appear as Movies, not Series
- Per-video metadata (plot, premiered, genre) is blank despite NFO files being present

**Phase to address:** Phase 1 (core resolver + library type setup) — this is the foundation; nothing else works until type classification is correct.

---

### Pitfall 2: Season Folder Naming Must Be `Season YYYY` — Custom Names Break Since 10.9.4

**What goes wrong:**
Since Jellyfin 10.9.4, the `SeasonResolver` only treats a folder as a distinct Season if it is named `Season ##` (with a numeric suffix). Folders named with a raw year (`2023/`, `YouTube-2023/`, `[ChannelName] 2023/`) are grouped together into a single unnamed season rather than creating one Season per year. Deleting the NFO files does not help; the issue is in the resolver, not the metadata layer.

**Why it happens:**
A behavior change in v10.9.4 tightened season folder name detection. Prior to 10.9.4, any folder inside a Series folder was treated as a Season regardless of name. GitHub issue #11916 confirms this regression and it was still unresolved as of mid-2024.

**How to avoid:**
Either: (a) enforce naming your year-subdirectories as `Season 2023` / `Season 2022` (the numeric string happens to work because `SeasonResolver` parses the number from `Season ##`), or (b) accept that the plugin-provided `ILocalMetadataProvider<Season>` will need to write correct `IndexNumber` from the year to ensure the right season year is displayed even if the folder resolves to a merged/default season. Option (a) is far more reliable. If Youtarr writes flat year-named folders, the plugin should either create symlinks/hardlinks with the correct naming, or document clearly that users must configure Youtarr to use `Season YYYY` naming. Do NOT name folders with the channel name prefix (`[ChannelName] - Season 2023`) — this triggers the duplicate season entry bug in 10.11.4+ (issue #15804).

**Warning signs:**
- All videos from multiple years collapse into one "Season 1" or "Unknown Season"
- Season folders all appear under a single entry in the library
- `season.nfo` files are deleted and replaced with blank ones on rescan

**Phase to address:** Phase 1 (library structure design) — must be decided before any metadata provider work.

---

### Pitfall 3: NFO Season Tag Is Ignored — Only Filename and Folder Path Drive Season Assignment

**What goes wrong:**
Even if a plugin writes correct `<season>` values into episode NFO sidecar files, Jellyfin 10.10.3+ ignores the NFO `<season>` tag during import. Season assignment is determined by the folder structure and the `SxxExx` filename pattern, not by the NFO. A video named `video_20230415.mp4` in a folder named `Season 2023` will be placed in Season 2023. But if the NFO says `<season>2022</season>`, that value is discarded. Episode numbers from NFO are respected; season numbers are not (Jellyfin GitHub issue #13197, filed December 2024, unresolved).

**Why it happens:**
Jellyfin uses the path-based resolver result as the authoritative season reference during the initial database insert. The metadata refresh that reads NFO values happens after the item is already in the DB with a season determined from path. Season NFO override was never reliably implemented.

**How to avoid:**
Do not rely on an NFO `<season>` field to control season grouping. Season grouping must be achieved via physical folder structure. The plugin's approach of using `Season YYYY` subdirectories (one per year) is the only reliable mechanism. The `ILocalMetadataProvider<Season>` can supply the `PremiereDate`, `Name`, and `IndexNumber` after the fact, but the folder must already correctly named.

**Warning signs:**
- Episodes appear in the wrong year-season despite correct NFO `<season>` tags
- Episodes appear in two seasons simultaneously (one from filename, one from path)
- After a full rescan, episodes move between seasons unexpectedly

**Phase to address:** Phase 1 (folder structure contract) and Phase 2 (metadata provider implementation).

---

### Pitfall 4: `targetAbi` Version Mismatch Causes Silent Plugin Load Failure

**What goes wrong:**
A plugin compiled against Jellyfin 10.9.x NuGet packages but with `targetAbi` set to `10.8.0.0` in `meta.json` will install without error but fail to load after restart. The plugin appears in the dashboard with status `NotSupported` or `Malfunctioned` — no runtime error is surfaced to the user in the UI. The logs show a `ReflectionTypeLoadException`: "Could not load type 'MediaBrowser.Controller.Plugins.IServerEntryPoint' from assembly 'MediaBrowser.Controller'."

Conversely, a plugin compiled against `10.10.x` packages but set `targetAbi: "10.9.0.0"` may install on 10.9.x servers and hard-crash on the first method call that uses a type that changed between versions.

**Why it happens:**
Jellyfin's plugin compatibility check uses `targetAbi` as the *minimum* required server version, not a locked version. The catalog shows plugins for higher ABI than the running server (issue #11331 and #4688). When the assembly's compiled version of a type does not match the loaded server's assembly, .NET's type system fails at load time. The failure is caught by the plugin loader which marks the plugin `NotSupported` — but no prominent error appears to the user.

**How to avoid:**
- Set `targetAbi` in `meta.json` to match exactly the NuGet package version used in the `.csproj` (e.g., if referencing `Jellyfin.Model 10.10.7`, set `"targetAbi": "10.10.7.0"`).
- In `.csproj`, set `<PackageReference Include="Jellyfin.Model" Version="10.10.7">` with `<ExcludeAssets>runtime</ExcludeAssets>` — this prevents Jellyfin's own assemblies from being bundled into the plugin ZIP and avoids version collision at load time.
- Check `$JELLYFIN_LOG/log_*.log` after every DLL deployment for `ReflectionTypeLoadException` or `NotSupported` entries.
- Keep the plugin template's `<PrivateAssets>all</PrivateAssets>` on Jellyfin NuGet references.

**Warning signs:**
- Plugin appears in Dashboard → Plugins but shows "NotSupported" or "Malfunctioned" status
- Plugin page is blank / throws 404
- `ReflectionTypeLoadException` or `TypeLoadException` in server logs immediately after startup
- Plugin was working, then stopped working after a Jellyfin server update

**Phase to address:** Phase 1 (project scaffolding / `.csproj` configuration).

---

### Pitfall 5: Custom Resolver Plugins Are Not Officially Supported — They Conflict with Built-in Resolvers

**What goes wrong:**
A plugin implementing `IItemResolver` to classify channel folders as Series runs, but the built-in `SeriesResolver` and `MovieResolver` also run on the same paths. Both resolvers produce results; Jellyfin deduplicates these results imperfectly. The result is duplicate library entries, items appearing in both Movie and Series views, or items appearing and disappearing on subsequent rescans. The Jellyfin team has explicitly stated "the library scanning code is unfortunately not made for [custom resolvers] and they cause a lot of issues" (official discussion #5732).

**Why it happens:**
Resolvers are ordered by priority, but all registered resolvers still run for each path. There is no mechanism to "short-circuit" the resolver pipeline from a plugin and prevent the built-in resolvers from firing. The resolver conflict is inherent to Jellyfin's architecture as of 10.10.x.

**How to avoid:**
Do not implement `IItemResolver`. Instead, rely on the physical folder structure matching what Jellyfin's built-in `SeriesResolver`/`SeasonResolver`/`EpisodeResolver` expect from a "Shows" library. The plugin's job is to supply correct *metadata* via `ILocalMetadataProvider<Series>`, `ILocalMetadataProvider<Season>`, and `ILocalMetadataProvider<Episode>` — not to reclassify items the resolver has already typed. The channel folders need `tvshow.nfo` files to be recognized as Series; the season folders need `season.nfo`; video files need their sidecar `.nfo`. The plugin's metadata providers read Youtarr's `<movie>` NFOs and synthesize the correct `tvshow.nfo`, `season.nfo`, and `episodedetails` metadata from them.

**Warning signs:**
- Duplicate entries for the same media in the library
- Items appear in both Movies and Shows sections
- Items vanish and reappear on repeated rescans
- Log shows two different resolver types claiming the same path

**Phase to address:** Phase 1 (architecture decision — metadata provider approach vs. resolver approach).

---

### Pitfall 6: Jellyfin Overwrites Plugin Metadata With Its Own NFO Writer (10.9.0+ Regression)

**What goes wrong:**
Jellyfin 10.9.0 introduced behavior where Jellyfin's internal metadata writer replaces existing NFO files with auto-generated ones containing only filename-derived titles, runtime, and an empty plot — locking the file with `<lockdata>true</lockdata>`. Custom or plugin-supplied metadata is destroyed. Re-enabling the NFO reader after disabling it does not help; disabling the NFO reader only prevents Jellyfin from reading NFOs at all. This is documented in Jellyfin issue #12197 (closed as "Done" without a clear fix) and issue #13655 (still "Needs Testing").

**Why it happens:**
Jellyfin's `IMetadataSaver` implementations write local NFO files when metadata is saved. If a library scan results in a new item being added, Jellyfin may invoke its NFO saver to write a baseline NFO before a metadata refresh is fully applied. On 10.9.0, the merge behavior changed: instead of reading existing NFO content as the starting point, the saver sometimes starts from scratch.

**How to avoid:**
The plugin should not write NFO files (the file-only read-only design avoids this). But ensure the plugin's `ILocalMetadataProvider` implementations are registered with a lower `Order` value (higher priority) than Jellyfin's built-in `GenericXmlMetadataProvider` / `MovieNfoProvider`. Check whether "Save metadata to media folders" is enabled in the library settings — disable this to prevent Jellyfin from generating its own NFOs that would shadow Youtarr's NFOs. Consider using `item.LockedFields` / `item.IsLocked` to prevent Jellyfin from overwriting metadata fetched by the plugin. Document clearly in the README that users should not enable "Save metadata to media folders" for the Youtarr library.

**Warning signs:**
- Per-video plots/descriptions are blank after a rescan that was working before
- `<lockdata>true</lockdata>` appears in NFO files that Youtarr generated
- Episode titles revert to filenames after a scheduled metadata refresh
- NFO files gain a `<runtime>` but lose `<plot>`, `<premiered>`, `<genre>`

**Phase to address:** Phase 2 (metadata provider implementation) — test with both "save metadata" on and off.

---

## Moderate Pitfalls

### Pitfall 7: Episode `SxxExx` Filename Pattern Is Required for Seasons to Work Correctly

**What goes wrong:**
Jellyfin's `EpisodeResolver` uses the filename to determine season and episode number via regex patterns (looking for `S01E01`, `1x01`, etc.). Youtarr names files with the YouTube ID and title (e.g., `dQw4w9WgXcQ - Rick Astley - Never Gonna Give You Up.mp4`). There is no `SxxExx` pattern. Without one, Jellyfin places all videos in "Season 1, Episode 0" or "Unknown Season" unless the plugin explicitly returns `IndexNumber` (episode number) and `ParentIndexNumber` (season number) from its `ILocalMetadataProvider<Episode>`.

**How to avoid:**
The `ILocalMetadataProvider<Episode>` implementation must return a non-null `MetadataResult<Episode>` with `Episode.IndexNumber` (episode number within the year-season) and `Episode.ParentIndexNumber` (the year, e.g., `2023`). The plugin must derive the episode number from the upload date or a stable sort order (e.g., YYYYMMDD as an integer, or sequential index within year). The plugin must also set `Episode.PremiereDate` from the `<premiered>` field in the Youtarr NFO.

**Warning signs:**
- All episodes show as "Episode 0" or share the same episode number
- All videos collapse into "Unknown Season" despite year folders existing
- Sorting by episode number produces random order instead of chronological

**Phase to address:** Phase 2 (metadata provider — episode number derivation strategy).

---

### Pitfall 8: Episode Numbering Collisions Within a Year-Season

**What goes wrong:**
Using `YYYYMMDD` as episode number works until two videos are uploaded on the same day — both get the same `IndexNumber`. Jellyfin treats duplicate `IndexNumber` values in the same season as the same episode, showing only one of them or merging them, breaking library completeness.

**How to avoid:**
Use a compound approach: `YYYYMMDDNN` where `NN` is a within-day sequence counter (00, 01, 02...), or use a Unix timestamp-derived integer. Alternatively, use a sequential integer derived from sorting all videos in a channel by `premiered` date ascending and assigning 1-based indices. The sequential approach is simpler but requires the plugin to have a full list of a channel's videos at metadata time — cache this per-channel during the scan task. Document the chosen scheme in the architecture.

**Warning signs:**
- Channel has fewer episodes displayed than files on disk
- Two videos with the same upload date, only one shows in Jellyfin
- Episode sort order seems correct but some episodes are invisible

**Phase to address:** Phase 2 (metadata provider design — episode numbering scheme decision).

---

### Pitfall 9: Missing or Garbage Upload Dates Break Year-Season Assignment

**What goes wrong:**
Youtarr's NFO `<premiered>` field may be missing (not all YouTube videos have extractable upload dates), malformed (e.g., `0001-01-01` from yt-dlp fallback), or set to the download date instead of the upload date. A missing/zero premiered date causes the plugin's year extraction to fail, placing the video in a fallback season (e.g., Season 0 or Season 1900) rather than the correct year.

**How to avoid:**
Implement a multi-source fallback for the video year:
1. Parse `<premiered>` from the Youtarr NFO
2. Fall back to the `durationinseconds`-adjacent `fileinfo` block
3. Fall back to parsing a date from the filename or folder name
4. Final fallback: Season 0 (specials) with a clear log message

Validate that the year is within a sane range (e.g., 2005–present for YouTube). Flag `premiered` values of `0001-01-01` or `1970-01-01` as "effectively missing." Log a warning with the video path whenever the fallback chain is invoked, so users can identify problematic files.

**Warning signs:**
- A "Season 1" or "Season 0" contains videos from many different years
- Videos with dates from before 2005 in the library
- Videos in "Season 1900" or "Season 1970"

**Phase to address:** Phase 2 (metadata provider implementation — date parsing and validation).

---

### Pitfall 10: Channel-Level Series Artwork Not Picked Up Without Correct Filenames

**What goes wrong:**
Youtarr writes channel images to the channel folder. The filename Youtarr uses (`poster.jpg`, `folder.jpg`, `fanart.jpg`, `banner.jpg`) must exactly match what Jellyfin's local image scanner expects. If Youtarr writes `channel-art.jpg` or `thumb.jpg` (meant as a thumbnail, not the series primary), Jellyfin will not pick it up as a Series poster. Additionally, after a library scan, Jellyfin caches artwork to its own `/config/metadata/` directory — a stale cache will show the old (or absent) image even after the file is updated on disk.

**How to avoid:**
Map Youtarr's actual output image filenames to Jellyfin's expected names:
- `poster.jpg` or `folder.jpg` → Series Primary image (poster aspect ratio 2:3)
- `fanart.jpg` or `backdrop.jpg` → Series Backdrop/Fanart (16:9)
- `banner.jpg` → Series Banner (758×140 or similar wide format)

If Youtarr writes `thumb.jpg` as the channel thumbnail, the plugin's `IImageProvider<Series>` should return this as the Primary type (remapping the Youtarr filename to the Jellyfin `ImageType.Primary`). Implement `ILocalImageProvider` rather than relying on the file naming alone — the provider can explicitly return the correct `ImageType` for each file regardless of the filename on disk.

After testing artwork changes, always use "Refresh metadata" → "Replace all images" on the Series to clear the image cache.

**Warning signs:**
- Series shows a blank/generic poster despite images being in the channel folder
- After updating channel art, the old image persists in Jellyfin
- Series backdrop is the wrong aspect ratio (portrait/square instead of landscape)
- Banner image type is missing from the Series metadata page

**Phase to address:** Phase 3 (artwork/image provider implementation).

---

### Pitfall 11: Jellyfin XML/NFO Parser Has UTF-8 Encoding Issues With Emoji and Special Characters

**What goes wrong:**
Youtarr plots (`<plot>`) can contain YouTube video descriptions — which frequently include emoji (📺, 🔔), hashtags, URLs, and characters from multiple Unicode planes. Jellyfin's NFO writer was documented (issue #9611, closed "not planned") to export NFO files with OEM-US charset despite the XML declaration saying `UTF-8`. On reimport, special characters and emoji render as `?` or `?` glyphs. Separately, unescaped XML special characters in plot text (`&`, `<`, `>`, `"`) will corrupt the NFO XML and cause a complete parse failure for that video.

**How to avoid:**
The plugin reads NFOs, it does not write them. But when the plugin's metadata provider returns strings to Jellyfin from parsed NFO content, it must:
1. Parse the NFO with an `XmlReader` configured for UTF-8 (specify `Encoding.UTF8` explicitly, do not rely on auto-detection).
2. Validate that the NFO file bytes are valid UTF-8 before parsing — log and skip gracefully if not.
3. When returning `Overview`/`Plot` strings, strip or normalize problematic characters rather than passing raw YouTube description text: strip raw URLs (they add no value to Jellyfin's display), collapse consecutive whitespace/newlines, and limit overview length to ~2000 characters (Jellyfin's UI truncates long overviews but the DB stores the full string, which degrades performance on large queries).

**Warning signs:**
- Plot descriptions show `?` or boxes where emoji were
- NFO parse errors in logs mentioning "unexpected token" or malformed XML
- Some videos have no metadata at all while adjacent ones do (XML parse failure for one file)
- Very long overview strings in the Jellyfin DB (check via admin API if performance degrades)

**Phase to address:** Phase 2 (metadata provider implementation — NFO parsing robustness).

---

### Pitfall 12: Youtarr `__prefix` Subfolders Are Treated as Additional Series by Jellyfin

**What goes wrong:**
Youtarr uses `__kids`, `__music`, `__news` prefixes as folder groupings for channel subsets. If the Youtarr root folder contains both channel folders and `__prefix` folders, and the entire root is one library, Jellyfin will try to resolve the `__prefix` folders as additional Series. The `__` prefix does not match any built-in Jellyfin ignore pattern. The result is phantom "Series" named `__kids`, `__music`, etc., each containing sub-channel folders as "Seasons," which breaks the intended hierarchy.

**How to avoid:**
Two options: (a) add an `IResolverIgnoreRule` plugin implementation that returns `true` for any path component starting with `__` (this is a supported plugin pattern), or (b) document to users that `__prefix` folders should each be configured as separate Jellyfin libraries rather than being children of a single library. Option (a) is cleaner UX. The `IResolverIgnoreRule` approach is low-risk and officially supported for this use case.

**Warning signs:**
- Library shows Series named `__kids`, `__music`, `__news`, etc.
- Sub-channel folders appear as "Seasons" under the prefix-named series
- Season count for `__kids` is the number of channels in that group, not years

**Phase to address:** Phase 2 (library resolver configuration) or Phase 4 (multi-library support).

---

### Pitfall 13: Plugin Configuration Lost After GUID Change or Version Bump

**What goes wrong:**
Jellyfin stores plugin configuration as an XML file at `config/plugins/<PluginGUID>/<PluginGUID>.xml`. If the plugin GUID changes between releases (e.g., developer regenerates it), the old configuration file is orphaned and never loaded. The plugin starts fresh with default settings, silently discarding user configuration. This is especially damaging if the plugin has settings like "year-seasons toggle" or a custom episode numbering scheme — the user notices their settings reverted after an update.

The tubearchivist-jf-plugin GitHub issue #78 ("Settings persistence") documents exactly this problem.

**How to avoid:**
- Generate the GUID once at project creation and treat it as permanent. Commit it to version control as a constant — never regenerate it.
- When adding new configuration fields, use default values and not required fields, so old XML files deserialize cleanly.
- Consider a migration step in the plugin's `OnUninstalling` / startup path that reads old config by GUID and writes it under the new GUID if upgrading.
- In the plugin template's `meta.json`, verify the GUID matches `Plugin.Id` in the C# class — mismatches cause config to be read from a different path than where it was written.

**Warning signs:**
- User reports settings reverted after a plugin update
- Plugin startup log shows "No configuration found, using defaults" even though the user had configured it
- Old XML files accumulate under the plugins config directory with no corresponding plugin

**Phase to address:** Phase 1 (project scaffolding — set GUID once and lock it).

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Reading NFO with `XDocument.Load(path)` without explicit UTF-8 | Simple, one line | Silently misparses special chars, emoji, non-ASCII; breaks on BOM | Never — always use `XmlReader` with explicit `Encoding.UTF8` |
| Using `YYYYMMDD` alone as episode index | Simple to compute | Collisions on same-day uploads; duplicate IndexNumbers | Only if you verify the channel has no same-day uploads (fragile) |
| Hard-coding `Season 00` as fallback for undated videos | Simple placeholder | Grows indefinitely; undated videos from different channels all mix in Season 00 | Never for production; acceptable in a dev stub only |
| Relying on Jellyfin to pick up `folder.jpg` without an ILocalImageProvider | No code needed for basic case | Fails when Youtarr writes a different image filename; no control over ImageType mapping | Only if you control Youtarr's output filenames precisely |
| Registering plugin's metadata provider with default Order (100) | No extra code | Built-in NFO provider may win first if it finds any tag, silently discarding plugin metadata | Never — always set Order explicitly lower than built-in providers |
| Embedding Jellyfin NuGet runtime assemblies in the plugin ZIP | Avoids "missing assembly" errors during dev | Plugin ZIP is large; runtime assembly version mismatch at load time causes `TypeLoadException` | Never — use `<ExcludeAssets>runtime</ExcludeAssets>` |
| Single library for root Youtarr folder including `__prefix` subdirs | One library to manage | `__prefix` folders become phantom Series; hard to filter after the fact | Acceptable only if `IResolverIgnoreRule` for `__` prefix is implemented first |

---

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| Jellyfin NFO `<movie>` → Episode | Using `EpisodeNfoProvider` to read Youtarr's `<movie>` NFOs | Implement a custom `ILocalMetadataProvider<Episode>` that parses `<movie>` root and maps fields manually |
| Jellyfin image caching | Editing image files on disk and expecting Jellyfin to pick them up immediately | Always trigger "Refresh metadata → Replace all images" from the UI or via the API after changing artwork |
| Library type setup | Pointing Jellyfin at the Youtarr folder as a "Movies" or "Mixed" library | Library **must** be "Shows/TV Shows" type for Series/Season/Episode resolution to work |
| NuGet package version | Using `Jellyfin.Model` latest from NuGet (may be newer than installed server) | Pin NuGet package version to exactly the installed Jellyfin server version |
| `tvshow.nfo` for channels | Expecting Jellyfin to auto-generate Series metadata without a `tvshow.nfo` | Plugin must supply or synthesize a `tvshow.nfo` (or return a `MetadataResult<Series>` with `HasMetadata = true`) for each channel folder |
| Season folder naming post-10.9.4 | Using bare year as season folder name (`2023/`) | Use `Season 2023` naming; Jellyfin's `SeasonResolver` extracts the number from the `Season ##` pattern |
| Episode numbering | Assuming Jellyfin fills in `IndexNumber` from filename | For non-`SxxExx` filenames, the plugin must explicitly return `IndexNumber` in its `MetadataResult<Episode>` |

---

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| Metadata provider reads all NFO files in a channel on every Episode refresh | Refresh takes hours on channels with 1000+ videos; CPU spikes during library scan | Cache the channel's episode list per scan task; only read the single NFO for the current episode in `GetMetadata`; pre-sort/index in a scheduled task | ~500+ episodes per channel |
| Overview strings stored untruncated | Queries for episode lists return 10MB+ JSON responses; Jellyfin UI lags | Truncate `Overview` to ~2000 chars in the provider; warn if truncating | ~500 episodes with long descriptions |
| Image provider returning high-res thumbnails for all episodes | Thumbnail generation phase is slow; disk I/O for thousands of images | Return correct local file path; let Jellyfin handle transcoding to thumbnail size; do not load image bytes in the provider | ~1000 episodes |
| Scanning large library with "real-time monitoring" enabled | Continuous CPU use as Youtarr writes new files while Jellyfin is watching | Enable real-time monitoring only after initial scan; Youtarr downloads trigger dozens of events that coalesce into rescans (45s debounce) | Any active downloading session |

---

## "Looks Done But Isn't" Checklist

- [ ] **Series classification:** Channel folder shows as a Series in the UI — verify it also has correct metadata (title = channel name, not folder path) and that `tvshow.nfo` or provider metadata is returning `HasMetadata = true`
- [ ] **Season year grouping:** Seasons display as "Season 2023", "Season 2022" — verify `IndexNumber` is the year integer (2023, not 1 or 0) and `PremiereDate` is set so Jellyfin shows the year label
- [ ] **Episode ordering:** Episodes within a season sort chronologically — verify `IndexNumber` is unique within the season and matches upload date order, not random
- [ ] **Per-video metadata:** Plot shows video description — verify the `<movie>` NFO XML parser handles: missing fields gracefully, long descriptions (>2000 chars), emoji, unescaped XML entities
- [ ] **Channel artwork:** Series poster shows channel image — verify the plugin's `ILocalImageProvider<Series>` returns `ImageType.Primary` for the poster file and `ImageType.Backdrop` for fanart, not swapped
- [ ] **Plugin loads:** Plugin is listed as "Active" in Dashboard → Plugins — verify by checking server logs for `ReflectionTypeLoadException` at startup
- [ ] **Plugin config persists:** After saving plugin settings and restarting Jellyfin, settings are retained — verify GUID in `meta.json` matches GUID in the C# `Plugin.Id`
- [ ] **Rescan idempotency:** Running a full library rescan twice produces the same result — verify no duplicate Series/Season entries appear; check for `IndexNumber: null` seasons
- [ ] **`__prefix` folders ignored:** If Youtarr uses prefix folders, verify they are not appearing as phantom Series entries — check `IResolverIgnoreRule` is registered

---

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| Videos classified as Movies instead of Episodes | HIGH | Delete the library, recreate it as "Shows" type, rescan; metadata for Movies library may need manual cleanup in the DB |
| Stale image cache after artwork update | LOW | Dashboard → Library → Edit → "Refresh metadata" → "Replace all images" for the affected Series |
| Season number collapse (all in Season 1) | MEDIUM | Rename season folders to `Season YYYY`, trigger full library rescan; or implement `ILocalMetadataProvider<Season>` returning correct `IndexNumber` |
| Plugin GUID change losing config | LOW | Locate old `<OldGUID>.xml` in `config/plugins/`, rename to new GUID; restart Jellyfin |
| NFO overwritten by Jellyfin's metadata saver | MEDIUM | Disable "Save metadata to media folders" in library settings; restore NFOs from Youtarr redownload or backup; force rescan |
| `NotSupported` plugin status after server update | MEDIUM | Rebuild plugin against new server NuGet version, update `targetAbi` in `meta.json`, redeploy DLL, restart server |
| Duplicate Season entries | MEDIUM | Remove show from library, clean library, rename season folders to avoid title-prefix patterns, re-add and rescan |

---

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| `<movie>` NFO classification trap | Phase 1 (core architecture) | Channel folders resolve as Series, videos as Episodes in a "Shows" library |
| Season folder naming (`Season YYYY`) | Phase 1 (library structure design) | Year folders resolve as distinct named seasons, not merged into one |
| NFO season tag ignored | Phase 1 (structure) + Phase 2 (metadata) | Season assignment driven by folder structure, not NFO field |
| `targetAbi` / ABI mismatch | Phase 1 (project scaffolding) | Plugin shows "Active" status in Dashboard after first DLL deploy |
| Custom resolver conflicts | Phase 1 (architecture decision) | No IItemResolver implemented; only metadata providers used |
| Jellyfin overwriting NFOs | Phase 2 (metadata provider) | Test with "save metadata to media folders" ON and OFF |
| No `SxxExx` pattern — episode numbers | Phase 2 (episode metadata provider) | Episode numbers are unique, sequential within year, sort chronologically |
| Same-day upload collisions | Phase 2 (episode numbering scheme) | Two same-day videos both appear as distinct episodes |
| Missing premiere dates | Phase 2 (date parsing) | Videos with no NFO date land in "Season 0" with a log warning |
| Channel artwork not picked up | Phase 3 (image provider) | Series poster and backdrop display correct channel art |
| UTF-8 / emoji encoding | Phase 2 (NFO parser) | Plots with emoji display correctly; no parse errors in logs |
| `__prefix` folder phantom Series | Phase 2 or Phase 4 | No `__kids`/`__music` entries appear in the library |
| Plugin config lost on GUID change | Phase 1 (scaffolding) | Settings persist across plugin updates |
| Episode numbering collisions | Phase 2 | Two same-day videos have distinct `IndexNumber` values |
| Plugin dev loop slow iteration | Phase 1 (dev setup) | Documented rebuild → deploy → restart → rescan workflow; IDE debugger attached |

---

## Sources

- Jellyfin GitHub issue #11331 — ABI/targetAbi compatibility bug (newer Jellyfin installs older targetAbi plugins)
- Jellyfin GitHub issue #4688 — Plugin catalog lists plugins from invalid ABI version
- Jellyfin GitHub issue #12197 — Jellyfin overwriting custom NFO metadata (10.9.0+)
- Jellyfin GitHub issue #13655 — NFO changes not respected on rescan (10.10.0+)
- Jellyfin GitHub issue #11916 — Custom season folder names grouped into one season (10.9.4 regression)
- Jellyfin GitHub issue #13197 — NFO season tag ignored during import (10.10.3+)
- Jellyfin GitHub issue #15804 — Duplicate season entries with title-prefixed folder names (10.11.4+)
- Jellyfin GitHub discussion #5732 — Custom resolvers not supported, cause conflicts
- Jellyfin DeepWiki — Metadata Management (provider ordering, HasMetadata first-wins logic)
- Jellyfin DeepWiki — Plugin System (PluginManager, GUID, NotSupported/Malfunctioned states)
- Jellyfin DeepWiki — File System and Library Scanning (resolver pipeline order)
- Jellyfin GitHub issue #9611 — NFO UTF-8 encoding issue (closed not planned)
- TubeArchivist-jf-plugin GitHub issue #78 — Settings persistence lost after update
- TubeArchivist-jf-plugin GitHub issue #85 — Collection display problems (UUID names)
- Jellyfin plugin template (jellyfin/jellyfin-plugin-template) — ExcludeAssets, targetAbi, net9.0 target
- Official Jellyfin docs — TV Shows folder structure and image naming conventions
- Official Jellyfin docs — Local NFO metadata format and supported fields
- Jellyfin 10.9.0 release notes — .NET 8 upgrade, season names from NFO
- Jellyfin Plugin Repositories docs — manifest format, checksum, GUID requirements
- Jellyfin feature request — "Make NFO an adjustable metadata provider" (not implemented as of 2026)

---
*Pitfalls research for: Jellyfin 10.10.x plugin — Youtarr YouTube archive channels-as-Shows*
*Researched: 2026-06-09*
