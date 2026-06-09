# Youtarr for Jellyfin

A Jellyfin plugin that turns a [Youtarr](https://github.com/DialmasterOrg/Youtarr) download folder into a proper TV library: **each YouTube channel becomes a Show, each video an Episode, grouped into seasons by upload year** — instead of a flat wall of videos.

It reads the NFO files and images Youtarr already writes to disk. **No API key, no Youtarr connection, nothing phones home.** The plugin only ever reads your files.

- Channel folder → **Series** (named after the channel)
- Video → **Episode** (title, description, air date, runtime, genres, tags, YouTube ID, content rating)
- Episodes → grouped into **Season 2024 / Season 2025 / …** by upload year (toggleable)
- Channel `poster.jpg` → Series **poster + backdrop**; per-video thumbnail → Episode image

---

## Requirements

- **Jellyfin 10.10.x** (the plugin targets ABI `10.10.0.0`). Other major versions will refuse to load.
- A **Youtarr** install that writes NFO files + images to disk (the default).
- A Jellyfin library configured as **TV Shows** pointed at your Youtarr downloads folder (see [Set up the library](#2-set-up-the-library) — this is the step people miss).

---

## Install

### Option A — Plugin repository (recommended, auto-updates)

1. In Jellyfin: **Dashboard → Plugins → Repositories → `+`**.
2. Add this repository:
   - **Name:** `Youtarr`
   - **URL:** `https://raw.githubusercontent.com/sandwichfarm/youtarr-jf-plugin/master/manifest.json`
3. Go to **Catalog**, find **YoutarrMetadata** under *Metadata*, click **Install**.
4. **Restart Jellyfin.**

> Requires a published release (the maintainer pushes a `v*` tag; CI builds and attaches the ZIP). If the catalog install fails to download, use Option B until a release is published.

### Option B — Manual install

1. Download `youtarrmetadata_<version>.zip` from the [Releases](https://github.com/sandwichfarm/youtarr-jf-plugin/releases) page (or build it — see [From source](#build-from-source)).
2. Create a folder named **`YoutarrMetadata_<version>`** (e.g. `YoutarrMetadata_1.0.0.0`) inside your Jellyfin **plugins** directory:
   - Docker: `<your config volume>/plugins/`
   - Linux (native): `/var/lib/jellyfin/plugins/`
   - Windows: `%LOCALAPPDATA%\jellyfin\plugins\`
   - macOS: `~/.local/share/jellyfin/plugins/`
3. Extract the ZIP **into that folder** (you should end up with `meta.json` and `Jellyfin.Plugin.Youtarr.dll` directly inside it).
4. **Restart Jellyfin.**

Confirm it loaded: **Dashboard → Plugins** shows **YoutarrMetadata** as **Active**.

---

## Quick start

### 1. Point Jellyfin at your Youtarr folder

Youtarr organizes downloads one folder per channel:

```
/youtube/                     ← point your library here
├─ Itchy Boots/
│  ├─ poster.jpg              ← becomes the Series poster + backdrop
│  ├─ I got stopped … [D3alKgbEn5A].mp4
│  ├─ I got stopped … [D3alKgbEn5A].nfo   ← per-video metadata
│  └─ I got stopped … [D3alKgbEn5A].jpg   ← becomes the Episode thumbnail
└─ Another Channel/
   └─ …
```

Both Youtarr layouts work: flat (`Channel/video.mp4`) and nested (`Channel/video/video.mp4`). Youtarr `__`-prefixed grouping folders (e.g. `__kids`, `__music`) are skipped, not turned into junk shows — point a separate library at each of those if you use them.

### 2. Set up the library

This is the step that makes or breaks it. In **Dashboard → Libraries → Add Media Library**:

| Setting | Value | Why |
|---|---|---|
| **Content type** | **Shows** | Required. A *Movies* or *Mixed* library will never group channels into Series. |
| **Folder** | your Youtarr downloads root | e.g. `/youtube` above |
| **"Save metadata into media folders"** | **OFF** | Jellyfin can otherwise overwrite Youtarr's NFOs. |
| Metadata downloaders (TheTVDB, TMDb) | **OFF / unchecked** | They'd fetch wrong data for YouTube channels and fight the plugin. |
| Metadata savers | leave default/off | The plugin supplies metadata from disk. |

Save, then let the scan run (or **⋯ → Scan Library**).

### 3. Done

Each channel appears as a Show, videos as Episodes under year seasons, with posters and thumbnails. Adjust behavior in the plugin config (below).

---

## Configuration

**Dashboard → Plugins → YoutarrMetadata → Settings.** Changes apply on the next library scan / metadata refresh.

| Setting | Default | What it does |
|---|---|---|
| **Group episodes into year seasons** (`YearSeasons`) | **On** | On: episodes are grouped into `Season 2024`, `Season 2025`, … by upload year. Off: all episodes go in a single season. |
| **Episode numbering** (`EpisodeNumberingScheme`) | **Default** | *Default*: Jellyfin auto-numbers episodes in scan order. *YYYYMMDD*: the upload date is the episode number (e.g. `2024-03-15` → `20240315`) — stable and date-sortable. |
| **Max description length** (`MaxDescriptionLength`) | **500** | Truncates long YouTube descriptions (which often run thousands of characters with links and hashtags). Raise it if you want full descriptions. |

> **YYYYMMDD note:** two videos uploaded on the *same day* get the same episode number. If a channel posts multiple times per day, prefer **Default** numbering.

---

## What maps to what

| Youtarr (on disk) | Jellyfin |
|---|---|
| Channel folder name | Series title (falls back to the NFO `<studio>` field) |
| `<title>` | Episode title |
| `<plot>` | Episode overview (truncated to *Max description length*) |
| `<premiered>` | Episode air date **and** the year-season it lands in |
| `<runtime>` / `<durationinseconds>` | Episode runtime |
| `<genre>`, `<tag>` | Episode genres and tags |
| `<uniqueid type="youtube">` / `<youtubeid>` | YouTube provider ID (links back to the video) |
| `<mpaa>` (if present) | Content rating |
| channel `poster.jpg` | Series poster **and** backdrop |
| per-video `<name>.jpg` | Episode thumbnail |

Videos with a missing or unparseable upload date don't break the scan — they land in **Season 0 (Specials)**.

---

## Troubleshooting

**Plugin shows "Malfunctioned" / "Not Supported", or doesn't appear.**
You're not on Jellyfin **10.10.x**. The plugin targets ABI `10.10.0.0`; install a matching server, or a build for your version. Check **Dashboard → Logs** for `TypeLoadException` / `ReflectionTypeLoadException`.

**Channels show up as a flat list of movies, not as Shows.**
The library **Content type** isn't **Shows**. You can't change the type of an existing library — remove it and re-add it as **Shows** (see [step 2](#2-set-up-the-library)).

**Metadata is wrong, generic, or keeps reverting.**
Turn **off** the TheTVDB/TMDb metadata downloaders on the library, and turn **off** "Save metadata into media folders". Then **⋯ → Refresh Metadata → Replace all metadata** on the library.

**Episodes aren't grouped into year seasons (everything's in one season).**
Make sure **Group episodes into year seasons** is **On** in the plugin settings, then refresh metadata. Seasons are created from each video's `<premiered>` year — videos with no valid date go to Season 0.

**Posters or thumbnails don't show.**
Confirm `poster.jpg` sits in the channel folder and a `<video>.jpg` sits next to each video (Youtarr writes these). Jellyfin caches images aggressively — run **⋯ → Refresh Metadata → Replace all images**. (Channel art is portrait, so it's also reused as the backdrop — that's expected.)

**Descriptions are huge / full of links.**
That's the raw YouTube description. Lower **Max description length** (default 500) in the plugin settings and refresh.

**I changed a setting and nothing happened.**
Settings apply on the next scan/refresh. Run **⋯ → Refresh Metadata** on the library (use *Replace all metadata* to re-apply to existing items).

---

## Build from source

Requires the **.NET 8 SDK**.

```bash
# Build + run the test suite
dotnet test Jellyfin.Plugin.Youtarr.Tests/Jellyfin.Plugin.Youtarr.Tests.csproj -c Release

# Produce the installable ZIP + repository manifest into ./dist
#   (requires jprm:  pip install --user jprm)
./scripts/package.sh
```

`scripts/package.sh` writes `dist/youtarrmetadata_<version>.zip` and refreshes the repo-root `manifest.json`. To cut a release, push a tag — `.github/workflows/release.yml` builds the ZIP, attaches it to the GitHub Release, and updates the manifest:

```bash
git tag v1.0.0.0 && git push origin v1.0.0.0
```

---

## Notes & limitations

- **File-only:** the plugin reads Youtarr's on-disk NFOs and images. It never connects to Youtarr or YouTube and never modifies your media.
- **Watched-status / playlist sync** between Jellyfin and Youtarr is **not** included (that would require a Youtarr API). It may come in a future version.
- Channel *description* as the Series overview isn't available yet — Youtarr doesn't write a channel-level NFO.
- Designed and tested against **Jellyfin 10.10.x**.

## License

No license has been chosen yet. Until a `LICENSE` file is added, all rights are reserved by the project owner.
