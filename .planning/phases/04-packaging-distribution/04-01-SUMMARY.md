---
phase: 04-packaging-distribution
plan: 01
subsystem: infra
tags: [jprm, packaging, manifest, zip, jellyfin-plugin, build.yaml]

# Dependency graph
requires:
  - phase: 01-scaffold-series-proof
    provides: build.yaml + GUID 80302d7f-7fc3-4b1c-9a3f-fd85b98b9a69 (locked); deploy-plugin.sh conventions
  - phase: 03-config-and-images
    provides: complete v1 feature set (Series/Episode/year-seasons, NFO metadata, channel artwork, config page) that the changelog describes
provides:
  - "scripts/package.sh: single-command jprm build + manifest generation"
  - "build.yaml v1.0.0.0 changelog feeding the ZIP meta.json + manifest"
  - "dist/youtarrmetadata_1.0.0.0.zip (PKG-01) — versioned, flat install ZIP"
  - "dist/manifest.json (PKG-02) — schema-correct plugin repository manifest with MD5 checksum"
affects: [04-02-PLAN docker-harness install verification, DIST-01 CI v2]

# Tech tracking
tech-stack:
  added: []  # jprm 1.1.0 already installed in a prior session; no new packages
  patterns:
    - "jprm plugin build run from the plugin subdir (build.yaml location), not repo root"
    - "Guarded jprm repo init ([[ ! -f manifest ]]) + idempotent jprm repo add"
    - "build.yaml version: as single source of truth; csproj left without <Version> to avoid jprm mutation"

key-files:
  created:
    - scripts/package.sh
  modified:
    - Jellyfin.Plugin.Youtarr/build.yaml

key-decisions:
  - "Keep csproj free of <Version>/<FileVersion>/<AssemblyVersion> so jprm's set_project_version is a no-op (no dirty git tree on every build)"
  - "Use placeholder GitHub releases sourceUrl (sandwich/youtarr-jf-plugin) — updated at DIST-01/v2 when the repo is published"
  - "dist/ ZIP + manifest.json are build artifacts; gitignored and NOT committed (only scripts/package.sh + build.yaml are tracked)"
  - "MD5 checksum kept (not upgraded to SHA-256) — Jellyfin plugin catalog protocol mandates MD5 (RESEARCH V6 / T-04-01 accepted)"

patterns-established:
  - "Packaging entry point: ./scripts/package.sh produces ZIP + manifest reproducibly in one command"
  - "jprm working-directory discipline: cd into Jellyfin.Plugin.Youtarr before plugin build (repo root = silent no-op)"

requirements-completed: [PKG-01, PKG-02]

# Metrics
duration: 2min
completed: 2026-06-09
---

# Phase 4 Plan 01: Packaging (ZIP + manifest.json) Summary

**A single `./scripts/package.sh` run now produces a versioned, schema-correct install ZIP (`dist/youtarrmetadata_1.0.0.0.zip`) plus a publish-ready `dist/manifest.json` whose MD5 checksum matches the ZIP — reproducibly and without mutating the csproj.**

## Performance

- **Duration:** ~2 min
- **Started:** 2026-06-09T22:16:17Z
- **Completed:** 2026-06-09T22:18:20Z
- **Tasks:** 3 completed
- **Files modified:** 2 tracked (build.yaml, scripts/package.sh) + 4 gitignored dist artifacts produced

## Accomplishments

- Updated `build.yaml` changelog to the full v1.0.0.0 release notes (Series/Episode resolution, year seasons, per-video NFO metadata, channel artwork, config page, file-only) — this text flows verbatim into the ZIP's `meta.json` and the manifest.
- Created `scripts/package.sh` (87 lines, executable): cd into the plugin subdir, `jprm plugin build`, guarded `jprm repo init`, idempotent `jprm repo add` with a placeholder `--plugin-url`, loud stderr failure if no ZIP is produced, and echoed (never executed) docker harness install steps.
- Ran the script twice and verified the produced ZIP is flat (`meta.json` + `Jellyfin.Plugin.Youtarr.dll`), the manifest schema is correct (guid/name/versions[] with version/changelog/targetAbi/sourceUrl/checksum/timestamp), and the manifest checksum equals the ZIP's MD5. Second run is idempotent and leaves the csproj unmodified in git.

## Task Commits

Each implementation task was committed atomically. Task 3 produces only gitignored build artifacts, so it has no commit (per plan: do NOT commit dist/).

1. **Task 1: Update build.yaml changelog for v1.0.0.0** - `7839027` (feat)
2. **Task 2: Create scripts/package.sh (ZIP + manifest.json)** - `98ba093` (feat)
3. **Task 3: Run package.sh and assert ZIP + manifest schema** - no commit (gitignored dist artifacts only)

**Plan metadata:** committed in the final docs commit (SUMMARY + STATE + ROADMAP + REQUIREMENTS).

## Files Created/Modified

- `scripts/package.sh` (created) - Single-command packaging entry point. Resolves REPO_ROOT from BASH_SOURCE, `set -euo pipefail`, cd's into `Jellyfin.Plugin.Youtarr` before `jprm plugin build . -o dist`, guards `jprm repo init` behind `[[ ! -f manifest ]]`, runs `jprm repo add` with the placeholder `--plugin-url`, fails loudly to stderr (exit 1) if no ZIP appears, echoes harness install steps. No privilege escalation, no docker execution.
- `Jellyfin.Plugin.Youtarr/build.yaml` (modified) - Only the `changelog:` field changed to the v1.0.0.0 release notes. guid (80302d7f-...), version ("1.0.0.0"), targetAbi ("10.10.0.0"), framework ("net8.0"), name, owner, category, description, overview, artifacts all unchanged.

## Build.yaml Change

The single field changed (a YAML `>` block scalar):

```yaml
changelog: >
  Initial release v1.0.0.0. Channel folders resolve as Series; videos resolve as
  Episodes grouped into year seasons. Per-video NFO metadata (title, plot, premiere
  date, genres, tags, YouTube ID). Channel artwork as Series poster and backdrop.
  Plugin configuration page with year-seasons toggle. File-only — no API key required.
```

## package.sh Design

- Runs from the repo root; resolves `REPO_ROOT` from `BASH_SOURCE` so it is callable from any cwd.
- `PLUGIN_DIR=${REPO_ROOT}/Jellyfin.Plugin.Youtarr`, `DIST_DIR=${REPO_ROOT}/dist`, `VERSION="1.0.0.0"`.
- `PLUGIN_URL` = placeholder `https://github.com/sandwich/youtarr-jf-plugin/releases/download/v1.0.0.0/youtarrmetadata_1.0.0.0.zip` (documented as a DIST-01/v2 placeholder).
- Steps: `mkdir -p dist` → `cd PLUGIN_DIR` → `jprm plugin build . -o dist` → resolve newest `youtarrmetadata_*.zip` (fail to stderr + exit 1 if none) → guarded `jprm repo init` → `jprm repo add ... --plugin-url` → "Done" summary + echoed harness install commands.
- jprm owns ZIP packaging and manifest generation — no hand-rolled zip/JSON. Header documents the placeholder-sourceUrl note and the never-edit-the-ZIP / re-run-to-regenerate checksum note.

## Produced Artifacts (verified)

| Artifact | Path | Notes |
|----------|------|-------|
| Install ZIP (PKG-01) | `dist/youtarrmetadata_1.0.0.0.zip` | 11811 bytes; flat layout `{meta.json, Jellyfin.Plugin.Youtarr.dll}` |
| MD5 sidecar | `dist/youtarrmetadata_1.0.0.0.zip.md5sum` | `0df19253725581bc18c997c430971662` |
| jprm meta sidecar | `dist/youtarrmetadata_1.0.0.0.zip.meta.json` | consumed by `jprm repo add` |
| Manifest (PKG-02) | `dist/manifest.json` | array; guid `80302d7f-...`, name `YoutarrMetadata`, versions[0] complete |

**Checksum verification:** manifest `checksum` = `0df19253725581bc18c997c430971662` == Python `hashlib.md5(zip)` == `.md5sum` sidecar. All three match (the ZIP was not edited post-build).

**manifest.json versions[0]:** version `1.0.0.0`, non-empty changelog (the build.yaml v1 release notes), targetAbi `10.10.0.0`, placeholder sourceUrl, checksum `0df19253...`, timestamp `2026-06-09T22:17:50Z`.

**Idempotency:** Second `./scripts/package.sh` run exited 0, skipped `repo init` (guard worked), kept `versions[]` at exactly 1 entry (in-place update, no dupe), and left the csproj with 0 git diff lines (jprm's `set_project_version` is a no-op without `<Version>` tags). `git status --short` is empty — dist/ is gitignored.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Reworded a comment to satisfy the plan's `! grep -q 'sudo'` verifier**
- **Found during:** Task 2 verification.
- **Issue:** The script's header comment explained the no-privilege-escalation policy using the literal word "sudo". The plan's automated verify asserts `! grep -q 'sudo' scripts/package.sh` (the literal string must not appear anywhere), so the descriptive comment failed the check.
- **Fix:** Reworded the comment to "never escalates privileges" / "no privilege escalation" — same intent, no literal "sudo". The script never invoked sudo; only the comment text was adjusted.
- **Files modified:** `scripts/package.sh`
- **Commit:** `98ba093`

**2. [Rule 3 - Blocking] Made the `cd` line self-documenting to satisfy the `cd.*Jellyfin\.Plugin\.Youtarr` key-link pattern**
- **Found during:** Task 2 verification.
- **Issue:** The script used `cd "${PLUGIN_DIR}"` (a variable), so no single line literally contained `cd ...Jellyfin.Plugin.Youtarr`. The plan's `key_links` pattern `cd.*Jellyfin\.Plugin\.Youtarr` and the verify regex require the literal on one line.
- **Fix:** Added an inline trailing comment `cd "${PLUGIN_DIR}"  # = ${REPO_ROOT}/Jellyfin.Plugin.Youtarr` and referenced the literal subdir in the preceding comment. Behavior unchanged.
- **Files modified:** `scripts/package.sh`
- **Commit:** `98ba093`

Both fixes were applied before the Task 2 commit, so they appear in the single `98ba093` commit rather than as separate follow-ups.

## Out-of-scope Observations (not fixed)

- `jprm plugin build` surfaced pre-existing `CAxxxx` analyzer warnings (CA1848/CA1062/CA1031/CA1859/CA1724) from prior phases' provider/parser code. These are not introduced by this plan and are out of scope (build still succeeds, 85 tests pass). Logged here only; not modified.
- jprm's benign `warning: Neither image nor imageUrl is specified.` — expected per RESEARCH (Open Question 2); the ZIP and manifest are valid without a plugin icon. Adding `image:`/`imageUrl:` is deferred.

## Known Stubs

None — the manifest `sourceUrl` is a documented placeholder (not a stub): PKG-02 only requires a valid manifest, and the URL does not need to resolve for local packaging. It is intentionally updated at DIST-01/v2 (CI).

## Verification Summary

- build.yaml: valid YAML, changelog contains "Initial release", guid/version/targetAbi/framework unchanged. PASS.
- package.sh: executable, `bash -n` clean, cd's into plugin subdir, guarded repo init, repo add with --plugin-url, fails loudly with no ZIP, no `sudo`, no docker execution. PASS.
- `./scripts/package.sh`: exits 0, produces flat ZIP + md5sum + meta.json + manifest.json; manifest schema correct; checksum == ZIP MD5; idempotent across re-runs; csproj unmodified; no dist artifact staged. PASS.

## Self-Check: PASSED

- Files: scripts/package.sh, Jellyfin.Plugin.Youtarr/build.yaml, 04-01-SUMMARY.md, dist/youtarrmetadata_1.0.0.0.zip, dist/manifest.json — all present.
- Commits: 7839027 (Task 1), 98ba093 (Task 2) — both present in git log.
