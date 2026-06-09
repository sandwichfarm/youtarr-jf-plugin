# Phase 4: Packaging + Distribution - Research

**Researched:** 2026-06-09
**Domain:** Jellyfin plugin packaging (jprm 1.1.0, build.yaml, manifest.json generation, manual ZIP install verification)
**Confidence:** HIGH — all findings verified by reading jprm 1.1.0 source code directly and executing a live build + manifest generation against the actual project.

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| PLUG-02 | User can install the plugin by dropping a downloadable ZIP/DLL into the Jellyfin plugins directory | `jprm plugin build` produces `youtarrmetadata_1.0.0.0.zip` with flat `meta.json + Jellyfin.Plugin.Youtarr.dll`; extract to `$JELLYFIN_DATA/plugins/YoutarrMetadata_1.0.0.0/` = done |
| PKG-01 | Build produces a versioned plugin ZIP suitable for manual install | `scripts/package.sh` calls `jprm plugin build` from `Jellyfin.Plugin.Youtarr/`; places ZIP in `dist/`; confirmed working |
| PKG-02 | A plugin-repository `manifest.json` is generated so the plugin can be published later | `jprm repo init dist/manifest.json` + `jprm repo add` reads the ZIP + meta and appends a versioned entry; exact schema confirmed live |
</phase_requirements>

---

## Summary

Phase 4 is the packaging and distribution closure for v1. jprm 1.1.0 is already installed (via `~/.local`), already confirmed to produce a correct ZIP from the existing `build.yaml`, and the manifest generation workflow has been exercised live. The phase is **narrow and mechanical**: write `scripts/package.sh`, update `build.yaml` changelog for v1.0.0.0, and verify the ZIP installs cleanly in the Docker harness. CI (DIST-01) is explicitly deferred to v2.

**Primary recommendation:** `scripts/package.sh` runs `jprm plugin build` from the `Jellyfin.Plugin.Youtarr/` subdirectory with `-o ../../dist`, then `jprm repo init/add` against `dist/manifest.json`. One script, no new dependencies, no new packages to install.

The jprm `plugin build` command must be run from the directory containing `build.yaml` (i.e., `Jellyfin.Plugin.Youtarr/`). Running it from the repo root produces a "No config found" error because jprm searches for `build.yaml` in the given PATH, not recursively.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| ZIP packaging (DLL + meta.json) | Build tooling (jprm) | — | jprm wraps `dotnet publish`, writes meta.json, zips artifacts |
| manifest.json generation | Build tooling (jprm) | — | `jprm repo add` reads the ZIP's embedded meta, computes MD5, emits the repository manifest |
| Manual install (user flow) | File system (operator) | — | User extracts ZIP contents to `$JELLYFIN_DATA/plugins/YoutarrMetadata_1.0.0.0/`; Jellyfin reads `meta.json` on startup |
| Install verification | Docker harness (test) | — | Reuse existing `test/jellyfin-load-test`; check Dashboard shows "Active" after ZIP extract + restart |

---

## Standard Stack

### Core (all already present, no new installs)

| Tool | Version | Purpose | Why Standard |
|------|---------|---------|--------------|
| `jprm` | 1.1.0 | Packaging — `dotnet publish` + ZIP + meta.json; also generates `manifest.json` | Official Jellyfin Plugin Repository Manager; used by the plugin template and every reference plugin including TubeArchivist. Source-verified. |
| `dotnet` SDK | 8.0.422 | Compiles the plugin DLL | Already used in every prior phase. |
| `python3` | 3.12.3 | jprm runtime | Already present. jprm is installed at `~/.local/bin/jprm`. |

**jprm is already installed** at `/home/yolo/.local/lib/python3.12/site-packages/jprm/` (version 1.1.0). No install step required for the agent. The `jprm` binary is on PATH.

### No new packages required for this phase.

---

## Package Legitimacy Audit

| Package | Registry | Source Repo | slopcheck | Disposition |
|---------|----------|-------------|-----------|-------------|
| `jprm` | PyPI | github.com/oddstr13/jellyfin-plugin-repository-manager | [OK] | Approved — already installed; official Jellyfin tooling; author is Odd Stråbø (known Jellyfin ecosystem contributor); 12 published versions (0.2.0 through 1.1.0) |

slopcheck result: `[OK]` — confirmed via `slopcheck install jprm` (slopcheck 0.6.1 was available).

**Packages removed due to [SLOP]:** none
**Packages flagged as [SUS]:** none

---

## jprm Behavior (Verified from Source + Live Execution)

### What `jprm plugin build` Does

Source: `/home/yolo/.local/lib/python3.12/site-packages/jprm/__init__.py`, functions `build_plugin` and `package_plugin`. [VERIFIED: jprm source read directly]

1. Reads `build.yaml` (or `jprm.yaml` / `meta.yaml` — any of the CONFIG_LOCATIONS list) from the given PATH.
2. Looks for a `.sln` file in PATH; if found, enumerates its projects. If no `.sln`, finds the first `.csproj` in PATH.
3. Calls `set_project_version` — patches `<Version>`, `<FileVersion>`, `<AssemblyVersion>` in the `.csproj` to the version from `build.yaml` (or `--version` override). **This mutates the csproj.** With `version: "1.0.0.0"` in `build.yaml` and no `<Version>` tag in the csproj, the regex does nothing (no-op for missing tags).
4. Calls `dotnet clean`, `dotnet restore --no-cache`, `dotnet publish --nologo --no-restore --configuration=Release --framework=net8.0 -p:PublishDir=<tempdir> -p:Version=1.0.0.0 -maxcpucount:1`.
5. Copies artifacts listed in `build.yaml artifacts:` from `<tempdir>` into a second temp dir.
6. Generates `meta.json` from `build.yaml` fields (see fields table below).
7. Zips the temp dir into `<output>/<slug>_<version>.zip` where `slug = slugify(name)` (python-slugify; "YoutarrMetadata" → "youtarrmetadata").
8. Writes `<output>/<slug>_<version>.zip.md5sum` (MD5 of the ZIP).
9. Writes `<output>/<slug>_<version>.zip.meta.json` (the same meta.json content, for use by `jprm repo add`).

**Live verified output** (from `cd Jellyfin.Plugin.Youtarr && jprm plugin build . -o /tmp/jprm-dist-test`):

```
/tmp/jprm-dist-test/
  youtarrmetadata_1.0.0.0.zip          (5788 bytes)
  youtarrmetadata_1.0.0.0.zip.md5sum   (62 bytes: "0c53e13533ebe5b770254f1388f2ae19 *youtarrmetadata_1.0.0.0.zip\n")
  youtarrmetadata_1.0.0.0.zip.meta.json
```

**ZIP contents** (flat, no subdirectory prefix):

```
meta.json
Jellyfin.Plugin.Youtarr.dll
```

### build.yaml Fields jprm Reads

[VERIFIED: jprm source, `generate_metadata` function]

| Field | Required | Used In | Notes |
|-------|----------|---------|-------|
| `name` | YES | meta.json `name`, ZIP slug | `slugify(name)` → ZIP filename prefix |
| `guid` | YES | meta.json `guid` | Must be valid UUID; jprm calls `str(uuid.UUID(guid))` — will error on invalid GUIDs |
| `version` | YES | meta.json `version`, ZIP filename, `dotnet publish -p:Version=` | Must be parseable as `M.m.b.r` (uses `Version.full()` which zero-pads missing parts) |
| `targetAbi` | YES | meta.json `targetAbi` | Written verbatim |
| `framework` | YES (default: `netstandard2.1`) | `dotnet publish --framework` | Use `net8.0` for 10.10.x |
| `overview` | YES | meta.json `overview` | Short 1-line description |
| `description` | YES | meta.json `description` | Longer description; YAML `>` block scalar → trailing `\n` preserved |
| `category` | YES | meta.json `category` | E.g. `"Metadata"` |
| `owner` | YES | meta.json `owner` | GitHub username or display name |
| `artifacts` | YES | Files copied into ZIP | List of DLL filenames relative to `dotnet publish` output dir |
| `changelog` | YES | meta.json `changelog` | Release notes for this version; YAML `>` block → trailing `\n` preserved |
| `imageUrl` | NO | meta.json `imageUrl` | URL to plugin icon image; omit if no hosted image yet — jprm logs a warning but does not error |
| `image` | NO | ZIP contents, meta.json `image` | Local image file path; if present, is bundled into the ZIP |

**Warning on missing image:** jprm emits `"warning: Neither image nor imageUrl is specified."` but exits 0. The ZIP is fully valid without an image. This warning is benign for Phase 4.

### Current build.yaml (already correct)

[VERIFIED: read from `Jellyfin.Plugin.Youtarr/build.yaml`]

```yaml
name: "YoutarrMetadata"
guid: "80302d7f-7fc3-4b1c-9a3f-fd85b98b9a69"
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

**The only field to update for Phase 4:** `changelog` — change to reflect the v1.0.0.0 complete release. All other fields are already correct and should not change.

### What `jprm repo init` Does

Creates an empty JSON array (`[]`) at the given path. Fails if the file already exists.

```bash
jprm repo init dist/manifest.json
```

### What `jprm repo add` Does

[VERIFIED: jprm source, `generate_plugin_manifest` and `cli_repo_add`]

Reads the `.zip.meta.json` sidecar (if it exists alongside the ZIP) or reads `meta.json` from inside the ZIP. Computes MD5 of the ZIP. Generates a plugin manifest entry and appends it (or updates existing GUID) to the `manifest.json` array.

```bash
jprm repo add dist/manifest.json dist/youtarrmetadata_1.0.0.0.zip \
  --plugin-url "https://github.com/OWNER/youtarr-jf-plugin/releases/download/v1.0.0/youtarrmetadata_1.0.0.0.zip"
```

Without `--plugin-url`, jprm generates a `sourceUrl` of `/<slug>/<slug>_<version>.zip` relative to a base URL. Since there is no hosted repository yet, always provide `--plugin-url` with a placeholder URL. For a local-only Phase 4 manifest (PKG-02 just requires the file to exist), a placeholder is fine.

---

## manifest.json Schema (Verified Live)

[VERIFIED: live `jprm repo add` execution]

The manifest.json produced by jprm is a JSON array. Each element is one plugin with a `versions` array:

```json
[
    {
        "guid": "80302d7f-7fc3-4b1c-9a3f-fd85b98b9a69",
        "name": "YoutarrMetadata",
        "description": "Maps a Youtarr channel download folder into a Jellyfin TV Shows library. Each YouTube channel folder becomes a Series; videos become Episodes grouped by upload year. Reads metadata from Youtarr NFO files. No API key required.\n",
        "overview": "Organizes Youtarr downloads as Series/Season/Episode in Jellyfin",
        "owner": "sandwich",
        "category": "Metadata",
        "versions": [
            {
                "version": "1.0.0.0",
                "changelog": "Initial v1 release. Channel folders resolve as Series; videos as Episodes grouped by year. Per-video NFO metadata, channel artwork, and plugin configuration page.\n",
                "targetAbi": "10.10.0.0",
                "sourceUrl": "https://github.com/OWNER/youtarr-jf-plugin/releases/download/v1.0.0/youtarrmetadata_1.0.0.0.zip",
                "checksum": "<md5-of-zip>",
                "timestamp": "2026-06-09T21:40:15Z"
            }
        ]
    }
]
```

**Notes on the schema:**
- `checksum` is MD5 of the ZIP file (not the DLL). jprm computes this automatically.
- `timestamp` is UTC ISO-8601 set at build time.
- `sourceUrl` must point to the publicly downloadable ZIP. For Phase 4 local packaging, a placeholder URL is acceptable. The URL only matters when submitting to a Jellyfin plugin repository.
- When `jprm repo add` is run again for a newer version, it appends to `versions[]` and sorts descending by version number. The old entry is preserved.
- `imageUrl` is absent if neither `image` nor `imageUrl` was in `build.yaml`. This is valid.

---

## Manual Install Flow (PLUG-02)

[VERIFIED: ZIP content inspection + Jellyfin plugin loader conventions from PITFALLS.md and deploy-plugin.sh]

The jprm-produced ZIP contains files **flat** (no subdirectory inside the ZIP):

```
meta.json
Jellyfin.Plugin.Youtarr.dll
```

To install manually:

1. Download `youtarrmetadata_1.0.0.0.zip`.
2. Create directory `$JELLYFIN_DATA/plugins/YoutarrMetadata_1.0.0.0/`.
   - Folder naming: `<name>_<version>` where `name` is the `name` field from `build.yaml` (not the slug). The `deploy-plugin.sh` already uses `YoutarrMetadata_1.0.0.0` — same convention.
3. Extract the ZIP contents (flat) into that directory.
4. Restart Jellyfin.
5. Verify "Active" status in Dashboard → Plugins.

**Result directory:**
```
$JELLYFIN_DATA/plugins/YoutarrMetadata_1.0.0.0/
  meta.json
  Jellyfin.Plugin.Youtarr.dll
```

This is identical to what `deploy-plugin.sh` stages for the Docker harness (except `deploy-plugin.sh` only copies the DLL, not meta.json — the harness does not need meta.json because the plugin DLL's assembly metadata is sufficient for the test harness). The final ZIP install should include `meta.json`.

**The ZIP is the PLUG-02 deliverable.** The user drops the ZIP, extracts it into a versioned folder, and restarts Jellyfin.

---

## Packaging Script Design (`scripts/package.sh`)

The planner should create `scripts/package.sh` as the single entry point for Phase 4 packaging. It builds on the pattern of `scripts/deploy-plugin.sh` (which the user already knows).

**Design principles:**
- Run from the repo root (consistent with `deploy-plugin.sh`).
- jprm `plugin build` must be invoked from the `Jellyfin.Plugin.Youtarr/` subdirectory (where `build.yaml` lives) — this is a hard constraint from jprm's `get_config()` which searches in the given PATH.
- Output goes to `dist/` (already in `.gitignore`; `dist/` directory already exists from `deploy-plugin.sh` usage).
- `manifest.json` goes to `dist/manifest.json` (also gitignored — note `.gitignore` currently ignores `manifest.json` at root; needs to cover `dist/manifest.json` or the gitignore needs updating).
- Script must be idempotent: re-running produces same outputs.

**Script skeleton (pseudocode for planner):**

```bash
#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PLUGIN_DIR="${REPO_ROOT}/Jellyfin.Plugin.Youtarr"
DIST_DIR="${REPO_ROOT}/dist"

# 1. Build + package via jprm (must run from plugin dir)
mkdir -p "${DIST_DIR}"
cd "${PLUGIN_DIR}"
jprm plugin build . -o "${DIST_DIR}"

# 2. Generate/update manifest.json
MANIFEST="${DIST_DIR}/manifest.json"
ZIP=$(ls "${DIST_DIR}"/*.zip | head -1)   # one ZIP per build

# init only if manifest doesn't exist
if [[ ! -f "${MANIFEST}" ]]; then
  jprm repo init "${MANIFEST}"
fi

jprm repo add "${MANIFEST}" "${ZIP}" \
  --plugin-url "https://github.com/OWNER/youtarr-jf-plugin/releases/download/v1.0.0/$(basename "${ZIP}")"

echo "==> Packaged: ${ZIP}"
echo "==> Manifest: ${MANIFEST}"
```

**Placeholder URL:** The `--plugin-url` must be a string; for local Phase 4 use, a GitHub release URL with a placeholder owner is fine. PKG-02 requires the manifest to exist and be valid JSON — the sourceUrl does not need to resolve. Document this in the script.

**`.gitignore` adjustment:** The current `.gitignore` has `manifest.json` (root) but the manifest will live at `dist/manifest.json`. Either update the gitignore to `dist/manifest.json` or add `dist/*.json`. Verify and fix in Wave 0.

---

## Version Strategy

[VERIFIED: from build.yaml, csproj, and jprm source behavior]

| Location | Current Value | jprm Behavior |
|----------|---------------|---------------|
| `build.yaml version:` | `"1.0.0.0"` | Source of truth; jprm uses this for ZIP filename and meta.json |
| `csproj <Version>` | not set | jprm's `set_project_version` regex finds no `<Version>` tag → no-op; dotnet uses default `1.0.0` |
| `csproj <AssemblyVersion>` | not set | same — jprm does not inject; assembly version defaults to `1.0.0.0` |
| ZIP filename | `youtarrmetadata_1.0.0.0.zip` | `slugify(name) + "_" + version.full()` |
| Plugin folder | `YoutarrMetadata_1.0.0.0` | `name + "_" + version` (Jellyfin convention, NOT the slug) |

**Recommendation:** Keep `build.yaml version: "1.0.0.0"` as the single version source. Do not add `<Version>` to the csproj — it would cause jprm to mutate the csproj on every build, which is noisy in git diff. The assembly version being `1.0.0.0` by default is consistent.

For future version bumps: update only `build.yaml version:` and `changelog:`.

**targetAbi must stay `"10.10.0.0"`** — changing this is an upgrade path concern (see STACK.md), not a Phase 4 concern.

---

## Install Verification Approach

[VERIFIED: from docker-compose.yml, README.md, deploy-plugin.sh — all read from the harness]

The existing `test/jellyfin-load-test/` Docker harness is the verification target. Phase 4 adds a ZIP-based install path test alongside the existing DLL-drop path.

**Verification steps for PLUG-02:**

1. Run `scripts/package.sh` → confirm `dist/youtarrmetadata_1.0.0.0.zip` exists.
2. Extract ZIP to `test/jellyfin-load-test/plugins/YoutarrMetadata_1.0.0.0/` (overwrite DLL + add meta.json).
3. `docker restart jellyfin-plugin-test` (operator step — requires docker daemon running).
4. Check Dashboard → Plugins shows "Active" for YoutarrMetadata 1.0.0.0.
5. Check logs: `docker exec jellyfin-plugin-test grep -r "YoutarrMetadata\|Youtarr" /config/log/ 2>/dev/null`.
6. Expected log: `Plugin 'YoutarrMetadata' version '1.0.0.0' is compatible with this server.`

**Extract command (the operator runs this):**

```bash
cd test/jellyfin-load-test
mkdir -p plugins/YoutarrMetadata_1.0.0.0
unzip -o ../../dist/youtarrmetadata_1.0.0.0.zip -d plugins/YoutarrMetadata_1.0.0.0/
```

This overwrites the DLL with the jprm-built one and adds `meta.json`. After this point the harness plugins directory matches what a real end-user install looks like.

---

## Architecture Patterns

### Recommended Project Structure (packaging additions)

```
youtarr-jf-plugin/
├── Jellyfin.Plugin.Youtarr/
│   └── build.yaml           # jprm config (already exists, update changelog only)
├── dist/                    # gitignored; all packaging output
│   ├── youtarrmetadata_1.0.0.0.zip
│   ├── youtarrmetadata_1.0.0.0.zip.md5sum
│   ├── youtarrmetadata_1.0.0.0.zip.meta.json
│   └── manifest.json        # PKG-02 deliverable
└── scripts/
    ├── deploy-plugin.sh     # existing dev loop script
    └── package.sh           # NEW: packaging + manifest generation
```

### Pattern: jprm Build + Manifest Generation

```bash
# Step 1: Build (must run from the plugin subdirectory)
cd Jellyfin.Plugin.Youtarr
jprm plugin build . -o ../dist

# Step 2: Init manifest (first time only)
jprm repo init ../dist/manifest.json

# Step 3: Add plugin to manifest
jprm repo add ../dist/manifest.json ../dist/youtarrmetadata_1.0.0.0.zip \
  --plugin-url "https://example.com/youtarrmetadata_1.0.0.0.zip"
```

### Anti-Patterns to Avoid

- **Running `jprm plugin build` from the repo root.** jprm searches CONFIG_LOCATIONS in the given path; `build.yaml` is inside `Jellyfin.Plugin.Youtarr/`, not at the root. `jprm plugin build .` from repo root returns null config and exits silently with no output.
- **Running `jprm repo init` on an existing manifest.** The `RepoPathParam(should_exist=False)` check will fail with "There is already an existing repository at...". Guard with `[[ ! -f manifest.json ]]`.
- **Committing `manifest.json` to git before DIST-01.** The current `.gitignore` correctly ignores it. The manifest is a build artifact; only commit it when setting up a published repo (v2 DIST-01).
- **Using the ZIP directly in the harness plugins dir without extracting.** Jellyfin expects a directory with `meta.json` and the DLL, not a ZIP file. The ZIP is for distribution; extract it for install.
- **Forgetting to update `changelog:` in build.yaml before building.** The changelog in the ZIP's `meta.json` (and thus in the repository manifest) comes from `build.yaml`. It will reflect whatever is in the file at build time.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| ZIP packaging of DLL + meta.json | Custom bash zip loop | `jprm plugin build` | jprm handles dotnet publish, version injection, meta.json generation, MD5 sidecar, slug naming — all in one command |
| manifest.json generation | Manual JSON construction | `jprm repo add` | Correct schema, MD5 computation, version deduplication, version sorting — complex to get right manually |
| Version consistency across ZIP / meta.json / folder name | Shell string substitution | `build.yaml version:` as single source | jprm's `Version.full()` normalizes to 4-part; all outputs are consistent |

---

## Common Pitfalls

### Pitfall 1: jprm Mutates the .csproj on Build

**What goes wrong:** `jprm plugin build` calls `set_project_version()` which uses regex to replace `<Version>`, `<FileVersion>`, `<AssemblyVersion>` tags in the `.csproj`. If the csproj has a `<Version>` tag, jprm will overwrite it with the `build.yaml version` value every time. If the csproj is under git, every `package.sh` run creates a dirty working tree.

**Why it happens:** jprm is designed for CI workflows where a version bump is injected at build time. In a local dev workflow the mutation is persistent.

**How to avoid:** The current csproj has NO `<Version>`, `<FileVersion>`, or `<AssemblyVersion>` tags. This means jprm's regex finds nothing to replace — it is a no-op. Do not add these tags to the csproj. jprm passes `-p:Version=1.0.0.0` to `dotnet publish` anyway, which is sufficient.

**Warning signs:** `git diff` shows csproj changes after running `package.sh`.

### Pitfall 2: Wrong jprm Working Directory

**What goes wrong:** Running `jprm plugin build .` from the repo root produces no output and no error (exit 0), because `get_config()` returns None when build.yaml is not found in the given path (`.`). The build never runs.

**Why it happens:** jprm searches CONFIG_LOCATIONS in the provided PATH argument, not recursively. `build.yaml` is at `Jellyfin.Plugin.Youtarr/build.yaml`, not at repo root.

**How to avoid:** Always `cd Jellyfin.Plugin.Youtarr` before `jprm plugin build .`, or pass the full path: `jprm plugin build Jellyfin.Plugin.Youtarr/` from the repo root.

**Warning signs:** `jprm plugin build` exits 0 but prints nothing and creates no ZIP.

### Pitfall 3: manifest.json checksum Mismatch After Editing the ZIP

**What goes wrong:** If a developer manually edits the ZIP after jprm built it (e.g., to add a file), the MD5 checksum stored in `manifest.json` no longer matches the modified ZIP. Jellyfin's plugin catalog installer verifies the checksum before installing from a repository manifest. A mismatch causes installation to silently fail.

**Why it happens:** jprm computes `checksum_file(output_path)` (MD5) at build time and writes it to both the `.md5sum` sidecar and the `manifest.json`. Any post-hoc modification to the ZIP invalidates this.

**How to avoid:** Never manually edit the ZIP. If changes are needed, re-run `package.sh` entirely and regenerate the manifest. For Phase 4 local-only use (no live repository), this only matters when actually publishing. Document this in the script.

### Pitfall 4: `manifest.json` gitignore Scope

**What goes wrong:** The current `.gitignore` has `manifest.json` (matches root-level `manifest.json`) but the script writes to `dist/manifest.json`. Depending on gitignore pattern matching, `dist/manifest.json` may or may not be ignored.

**Why it happens:** `.gitignore` pattern `manifest.json` without a leading `/` matches in any directory (glob rule). So `dist/manifest.json` IS ignored by the current rule. Verify this is intentional — it is correct behavior for Phase 4. If a future phase wants to commit a manifest, the rule must be updated.

**How to avoid:** Leave `.gitignore` as-is. The `dist/*.json` pattern is covered by the current `manifest.json` rule. Add a comment to clarify intent.

### Pitfall 5: GUID Must Never Change

**What goes wrong:** If someone regenerates the GUID in `build.yaml`, Jellyfin stores plugin config at `config/plugins/<GUID>/config.xml`. Old config is orphaned. Users lose settings.

**How to avoid:** GUID `80302d7f-7fc3-4b1c-9a3f-fd85b98b9a69` is locked. Confirm it is identical in `build.yaml` and `Plugin.cs StaticId` (already verified in Phase 1, plan 01-01).

---

## CI / GitHub Actions (DIST-01) — DEFERRED TO v2

**This section documents the deferred work. Phase 4 does NOT implement CI.**

v2 requirement DIST-01 reads: "CI pipeline auto-builds and publishes a release + manifest on tagged push."

The reference pattern (from STACK.md, TubeArchivist plugin):

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
        with: { dotnet-version: '8.0.x' }
      - name: Install jprm
        run: pip install jprm
      - name: Build
        run: |
          cd Jellyfin.Plugin.Youtarr
          jprm plugin build . -o ../dist
      - name: Create GitHub release
        uses: ncipollo/release-action@v1
        with:
          artifacts: "dist/*.zip"
      - name: Update manifest
        run: |
          jprm repo init dist/manifest.json || true
          jprm repo add dist/manifest.json dist/*.zip \
            --plugin-url "${{ steps.release.outputs.upload_url }}"
          git add dist/manifest.json
          git commit -m "chore: update manifest for ${{ github.ref_name }}"
          git push
```

**Why deferred:** Requires a GitHub repository, PAT or GITHUB_TOKEN setup, and a hosted release URL to put in the manifest. All of that is out of scope for the v1 local-packaging goal. The local `scripts/package.sh` pattern is the exact same commands — CI is just the automation wrapper.

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| `jprm` | PKG-01, PKG-02 | Yes | 1.1.0 at `~/.local/bin/jprm` | None needed |
| `python3` | jprm runtime | Yes | 3.12.3 at `/usr/bin/python3` | None needed |
| `dotnet` SDK | jprm invokes `dotnet publish` | Yes | 8.0.422 at `~/.local/bin/dotnet` | None needed |
| Docker daemon | PLUG-02 verification | Operator must start | Requires `sudo systemctl start docker` | Cannot automate; operator step |
| `unzip` | Extract ZIP for harness install | [ASSUMED] — standard Linux utility | Unknown | `python3 -m zipfile -e` |

**Missing dependencies with no fallback:** None (all tool dependencies confirmed available).

**Docker:** Daemon startup requires `sudo` — an operator step, same as all prior phases. The agent uses `docker compose` commands only after the daemon is up.

---

## Project Constraints (from CLAUDE.md)

CLAUDE.md directives that apply to this phase:

1. **No sudo.** `scripts/package.sh` must not use `sudo`. jprm is at `~/.local/bin/jprm` (user install). All `dotnet` calls run as the current user. Docker daemon startup remains an operator step.
2. **GSD workflow enforcement.** All file changes must go through a GSD command. Direct repo edits outside a GSD workflow are forbidden unless the user explicitly requests it.
3. **AGENTS.md guidelines.** Check and follow guidelines in AGENTS.md (if present at `.planning/AGENTS.md`).

---

## Security Domain

`security_enforcement: true`, `security_asvs_level: 1`.

### Applicable ASVS Categories

| ASVS Category | Applies | Notes |
|---------------|---------|-------|
| V2 Authentication | No | Packaging is a local build step; no auth |
| V3 Session Management | No | — |
| V4 Access Control | No | — |
| V5 Input Validation | Minimal | The only external input is the `build.yaml` file (developer-controlled). jprm reads it with `yaml.SafeLoader` — prevents YAML deserialization attacks. No user input. |
| V6 Cryptography | Yes | MD5 checksum used by jprm for ZIP integrity. MD5 is sufficient for plugin catalog integrity per Jellyfin convention; this is NOT a security-critical hash (it guards against accidental corruption, not adversarial tampering). Do not upgrade to SHA-256 unilaterally — the Jellyfin plugin catalog protocol requires MD5. |

### Known Threat Patterns

| Pattern | STRIDE | Notes |
|---------|--------|-------|
| ZIP containing malicious DLL | Tampering | Not applicable — the developer controls the build pipeline end-to-end. No third-party code is being packaged. |
| jprm executing arbitrary shell commands | Tampering | Not applicable — jprm's `run_os_command` uses `subprocess.run` with a split command list (not `shell=True`) for `dotnet` invocations. The only shell=True path is explicitly not used in the `cli_plugin_build` flow. |
| jprm PyPI supply chain | Elevation of Privilege | jprm 1.1.0 verified OK by slopcheck; source code manually reviewed. No network calls in the build path, no `postinstall` hooks. |

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `unzip` is available on the target system for the extract-to-harness step | Environment Availability | Low — fallback is `python3 -m zipfile -e`; document both options in the script |
| A2 | The Jellyfin plugin folder naming convention `<Name>_<Version>` uses the `build.yaml name:` field (not the slug) for the directory name | Manual Install Flow | Medium — if Jellyfin uses the slug, the folder should be `youtarrmetadata_1.0.0.0`; deploy-plugin.sh already uses `YoutarrMetadata_1.0.0.0` and it works (verified in Phase 1 PLUG-01) |

**If this table is empty:** All other claims in this research were verified or cited.

---

## Open Questions

1. **Placeholder owner in sourceUrl**
   - What we know: PKG-02 requires a valid manifest.json; the sourceUrl must be a string but does not need to resolve for local Phase 4 use.
   - What's unclear: What placeholder URL to use? GitHub hasn't been set up yet.
   - Recommendation: Use `https://github.com/sandwich/youtarr-jf-plugin/releases/download/v1.0.0/youtarrmetadata_1.0.0.0.zip` as the placeholder. This is the conventional GitHub releases URL pattern. Update when DIST-01 is implemented.

2. **imageUrl / plugin icon**
   - What we know: jprm warns when neither `image` nor `imageUrl` is in `build.yaml`. The warning is benign; the ZIP and manifest are valid without it.
   - What's unclear: Should Phase 4 include a plugin icon (e.g., `image.png`)?
   - Recommendation: Defer to a later phase or treat as optional. The warning does not affect functionality. If the user wants an icon, add `image: "image.png"` to `build.yaml` and place the PNG in `Jellyfin.Plugin.Youtarr/image.png`.

---

## Code Examples

### Complete `scripts/package.sh`

```bash
#!/usr/bin/env bash
#
# package.sh — Produces the distribution ZIP and manifest.json for the
# Youtarr Jellyfin plugin.
#
# Outputs:
#   dist/youtarrmetadata_<version>.zip          — install ZIP (PKG-01)
#   dist/youtarrmetadata_<version>.zip.md5sum   — MD5 sidecar
#   dist/youtarrmetadata_<version>.zip.meta.json — jprm build metadata
#   dist/manifest.json                           — plugin repository manifest (PKG-02)
#
# NOTE: sourceUrl in manifest.json uses a placeholder GitHub releases URL.
# Update the PLUGIN_URL variable when the GitHub repository is published (v2 DIST-01).
#
# Usage:
#   ./scripts/package.sh
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PLUGIN_DIR="${REPO_ROOT}/Jellyfin.Plugin.Youtarr"
DIST_DIR="${REPO_ROOT}/dist"

# Placeholder URL — update when GitHub repo is live (DIST-01 / v2)
VERSION="1.0.0.0"
PLUGIN_URL="https://github.com/sandwich/youtarr-jf-plugin/releases/download/v${VERSION}/youtarrmetadata_${VERSION}.zip"

echo "==> Building plugin ZIP with jprm ..."
mkdir -p "${DIST_DIR}"
cd "${PLUGIN_DIR}"
jprm plugin build . -o "${DIST_DIR}"

ZIP=$(ls "${DIST_DIR}"/youtarrmetadata_*.zip | sort -V | tail -1)
echo "==> Built: ${ZIP}"

MANIFEST="${DIST_DIR}/manifest.json"
if [[ ! -f "${MANIFEST}" ]]; then
  echo "==> Initializing manifest.json ..."
  jprm repo init "${MANIFEST}"
fi

echo "==> Adding plugin to manifest.json ..."
jprm repo add "${MANIFEST}" "${ZIP}" --plugin-url "${PLUGIN_URL}"

echo ""
echo "==> Done."
echo "    ZIP:      ${ZIP}"
echo "    Manifest: ${MANIFEST}"
echo ""
echo "To verify install in the Docker harness (operator runs these):"
echo "    mkdir -p ${REPO_ROOT}/test/jellyfin-load-test/plugins/YoutarrMetadata_${VERSION}"
echo "    unzip -o ${ZIP} -d ${REPO_ROOT}/test/jellyfin-load-test/plugins/YoutarrMetadata_${VERSION}/"
echo "    docker restart jellyfin-plugin-test"
```

### Updated `build.yaml` changelog for v1.0.0.0

Only `changelog` needs updating. All other fields stay as-is.

```yaml
changelog: >
  Initial release v1.0.0.0. Channel folders resolve as Series; videos resolve as
  Episodes grouped into year seasons. Per-video NFO metadata (title, plot, premiere
  date, genres, tags, YouTube ID). Channel artwork as Series poster and backdrop.
  Plugin configuration page with year-seasons toggle. File-only — no API key required.
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Manual `dotnet publish` + hand-written `meta.json` + bash zip | `jprm plugin build` (one command) | jprm existed since 2020; ecosystem standard | Correct meta.json schema, consistent naming, MD5 sidecar |
| Publishing to a custom static server | GitHub Releases + manifest PR to `jellyfin/jellyfin-plugin-repository` | Current practice for official plugins | Phase 4 generates the manifest; hosting is DIST-01 |

**Deprecated/outdated:**
- `netstandard2.1` as jprm's default framework: jprm still defaults to `netstandard2.1` if `framework:` is absent from `build.yaml`, but Jellyfin 10.10.x requires `net8.0`. The `build.yaml` already has `framework: "net8.0"`, so the default is never reached.

---

## Sources

### Primary (HIGH confidence)
- jprm 1.1.0 source: `/home/yolo/.local/lib/python3.12/site-packages/jprm/__init__.py` — read directly; `build_plugin`, `package_plugin`, `generate_metadata`, `generate_plugin_manifest`, `update_plugin_manifest` functions
- Live jprm execution: `jprm plugin build . -o /tmp/jprm-dist-test` and `jprm repo init/add` — outputs inspected directly
- `Jellyfin.Plugin.Youtarr/build.yaml` — read directly
- `Jellyfin.Plugin.Youtarr/Jellyfin.Plugin.Youtarr.csproj` — read directly
- `scripts/deploy-plugin.sh` — read directly
- `test/jellyfin-load-test/docker-compose.yml` and `README.md` — read directly
- `.planning/phases/01-scaffold-series-proof/01-01-SUMMARY.md` — GUID and build.yaml provenance
- `.planning/research/STACK.md` — jprm patterns, CI reference
- `.planning/research/PITFALLS.md` — GUID stability, targetAbi, checksum pitfalls

### Secondary (MEDIUM confidence)
- TubeArchivist-jf-plugin GitHub Actions workflow (in STACK.md) — CI pattern reference [CITED: STACK.md]
- Jellyfin Plugin Repositories documentation — manifest format (via PITFALLS.md sources) [CITED: PITFALLS.md]

---

## Metadata

**Confidence breakdown:**
- jprm behavior: HIGH — source code read + live execution verified
- build.yaml fields: HIGH — source verified + live meta.json output inspected
- manifest.json schema: HIGH — live jprm repo add output inspected
- Manual install flow: HIGH — ZIP contents inspected + deploy-plugin.sh convention verified from Phase 1
- CI / GitHub Actions: HIGH for the pattern (from STACK.md); DEFERRED (not implementing in Phase 4)

**Research date:** 2026-06-09
**Valid until:** Stable — jprm 1.1.0 is a mature tool with 12 published versions; no breaking changes expected. The build.yaml schema has been stable since jprm 0.4.x. Revisit if jprm 2.x is released.
