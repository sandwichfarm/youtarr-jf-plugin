---
phase: 01-scaffold-series-proof
plan: 03
subsystem: load-test-harness
tags: [jellyfin, docker, load-test, deploy-script, runbook, fixtures]
status: harness-ready-live-verify-pending
requires:
  - "Jellyfin.Plugin.Youtarr buildable + clean publish (01-01)"
  - "YoutarrSeriesNfoProvider + YoutarrPrefixIgnoreRule + PluginServiceRegistrator (01-02)"
  - "Permanent GUID 80302d7f-7fc3-4b1c-9a3f-fd85b98b9a69 / build.yaml name YoutarrMetadata v1.0.0.0 (01-01)"
provides:
  - "test/jellyfin-load-test/docker-compose.yml — Jellyfin 10.10.7 harness, media mounted :ro"
  - "test/jellyfin-load-test/media/ fixture library (MyChannel + __kids prefix folder)"
  - "scripts/deploy-plugin.sh — publish + stage DLL into YoutarrMetadata_1.0.0.0/ (no docker/sudo)"
  - "test/jellyfin-load-test/README.md — operator runbook (setup/verify/iterate loop)"
affects:
  - "Phase 1 completion gate (live PLUG-01/LIB-01/SER-01/SER-02/CMP-03 still pending the live run)"
  - "All later phases reuse scripts/deploy-plugin.sh + this harness for live verification"
tech-stack:
  added:
    - "docker compose harness targeting jellyfin/jellyfin:10.10.7"
  patterns:
    - "media mounted read-only (:ro) so the plugin physically cannot mutate user files"
    - "deploy script resolves repo root from its own location; set -euo pipefail; idempotent"
    - "generated harness state (config/cache/plugins) git-ignored; media/ committed as fixtures"
    - "plugin folder name YoutarrMetadata_1.0.0.0 = <name>_<version> from build.yaml"
key-files:
  created:
    - "test/jellyfin-load-test/docker-compose.yml"
    - "test/jellyfin-load-test/media/MyChannel/test_video.nfo"
    - "test/jellyfin-load-test/media/MyChannel/test_video.mp4"
    - "test/jellyfin-load-test/media/__kids/.gitkeep"
    - "scripts/deploy-plugin.sh"
    - "test/jellyfin-load-test/README.md"
  modified:
    - ".gitignore (ignore harness config/cache/plugins; keep media/)"
decisions:
  - "Live human-verify checkpoint (Task 3) intentionally NOT executed: Docker daemon is not running and starting it requires sudo (project policy + scope limit). Left for the orchestrator/user to run once the daemon is up."
  - "Added an empty test_video.mp4 alongside the fixture NFO (Rule 2): a Shows folder needs content to resolve as a Series; an empty file satisfies the resolver."
  - "Plan counter NOT advanced and requirements NOT marked complete: live verification is the gate evidence for PLUG-01/LIB-01/SER-01/SER-02/CMP-03 and has not yet run."
requirements: [PLUG-01, LIB-01, SER-01, SER-02, CMP-03]
metrics:
  duration: "~5 min"
  completed: "2026-06-09 (harness only)"
  tasks: "2 of 3 (Task 3 live checkpoint pending)"
  files: 7
---

# Phase 1 Plan 03: Jellyfin Load-Test Harness Summary (live verify pending)

A reusable Jellyfin 10.10.7 docker-compose load-test harness, a committed fixture library
(one channel folder + a `__kids` prefix folder), a no-Docker deploy script that stages the
plugin DLL into the `YoutarrMetadata_1.0.0.0/` folder Jellyfin expects, and an operator
runbook documenting the full setup/verify/iterate loop — all built and verified offline.
The final live in-Jellyfin verification (plugin Active + Series resolution + `__kids`
suppression) is intentionally deferred to the operator because the Docker daemon is not
running and starting it requires `sudo` (project policy: the agent never runs sudo).

## What Was Built

| File | Provides |
|------|----------|
| `test/jellyfin-load-test/docker-compose.yml` | `jellyfin/jellyfin:10.10.7`, container `jellyfin-plugin-test`, port 8096, `restart: "no"`; volumes `./config`→/config, `./cache`→/cache, `./plugins`→/config/plugins, `./media`→/media **:ro** (read-only). Paths relative to the file so it is portable. |
| `test/jellyfin-load-test/media/MyChannel/test_video.nfo` | `<movie>` NFO with `<title>`, `<studio>My YouTube Channel</studio>`, `<premiered>`, `<plot>`, `<uniqueid type="youtube">`. |
| `test/jellyfin-load-test/media/MyChannel/test_video.mp4` | Empty content file so the channel folder resolves as a Series (Rule 2 addition). |
| `test/jellyfin-load-test/media/__kids/.gitkeep` | CMP-03 fixture: a `__` prefix grouping folder that must be suppressed. |
| `scripts/deploy-plugin.sh` | `dotnet publish -c Release` → copies `Jellyfin.Plugin.Youtarr.dll` into `test/jellyfin-load-test/plugins/YoutarrMetadata_1.0.0.0/`. `set -euo pipefail`, idempotent, executable, **no docker/sudo**. |
| `test/jellyfin-load-test/README.md` | Operator runbook: sudo daemon start + image pull, compose up, first-run wizard (Shows lib at /media, disable "Save metadata to media folders", disable TVDB/TMDB), Active + log-signature verification, scan + Series/`__kids` checks, dev iteration loop. |
| `.gitignore` | Ignores generated `test/jellyfin-load-test/{config,cache,plugins}/`; keeps `media/` committed. |

## Verification Results (offline)

- **Task 1 gate (all pass):** compose uses `jellyfin/jellyfin:10.10.7` and mounts `/media:ro`; fixture NFO contains `<studio>`; `media/__kids/` exists; deploy script contains `YoutarrMetadata_1.0.0.0`, passes `bash -n`, and is executable.
- **deploy-plugin.sh has no docker/sudo invocations** — `docker`/`sudo` appear only in comments and `echo`'d operator hints, never as executed commands.
- **deploy-plugin.sh ran successfully:** `dotnet publish` Build succeeded (pre-existing CA1031/CA1062/CA1724/CA1848 analyzer warnings only; `TreatWarningsAsErrors=false`), and `Jellyfin.Plugin.Youtarr.dll` (11264 bytes) was staged into `test/jellyfin-load-test/plugins/YoutarrMetadata_1.0.0.0/`.
- **Publish invariant preserved:** the publish output contains **no** `MediaBrowser.*` or `Jellyfin.Controller/Model/Data/Common` DLLs — the Wave 1 ExcludeAssets invariant (Pitfall 1) still holds.
- **Task 2 gate (all pass):** staged DLL present; README contains "Save metadata to media folders", "Active", "docker compose up", "scripts/deploy-plugin.sh", and "sudo systemctl start docker".

## Live Verification — PENDING (Task 3, operator runs this)

The live human-verify checkpoint was NOT executed (Docker daemon down; sudo required).
**Exact command sequence to complete it** (also in `test/jellyfin-load-test/README.md`):

```bash
# 0. (already done by this plan) stage the DLL — re-run only after code changes:
./scripts/deploy-plugin.sh

# 1. start the Docker daemon (REQUIRES SUDO — operator runs this):
sudo systemctl start docker
docker pull jellyfin/jellyfin:10.10.7

# 2. bring the harness up:
cd test/jellyfin-load-test
docker compose up -d        # wait ~20s
docker logs jellyfin-plugin-test --tail 50

# 3. browser: http://localhost:8096
#    - create admin user
#    - add a "Shows" library pointed at /media
#    - DISABLE "Save metadata to media folders" (issue #12197)
#    - disable TVDB + TMDB for that library

# 4. confirm plugin Active (PLUG-01):
docker exec jellyfin-plugin-test sh -c 'cat /config/log/log_$(date +%Y%m%d)*.log' 2>/dev/null \
  | grep -i "youtarr\|YoutarrMetadata\|plugin" | head -30
#    expect: "Loaded plugin 'YoutarrMetadata' v1.0.0.0"  and status Active in Dashboard → Plugins
#    failure: ReflectionTypeLoadException/TypeLoadException → ExcludeAssets;
#             NotSupported → targetAbi;  Malfunctioned → constructor

# 5. scan + confirm Series resolution (LIB-01/SER-01/SER-02) and __kids suppression (CMP-03):
API_KEY="<admin api key from Dashboard → API Keys>"
curl -X POST "http://localhost:8096/Library/Refresh" \
  -H "Authorization: MediaBrowser Token=\"${API_KEY}\""
curl -s "http://localhost:8096/Shows" \
  -H "Authorization: MediaBrowser Token=\"${API_KEY}\"" | python3 -m json.tool | grep -A2 '"Name"'
#    PASS: a Series named exactly "MyChannel" appears with metadata populated, and
#          "__kids" does NOT appear anywhere in the library.
```

When run, record in the checkpoint reply:
- Whether Series resolution required **no** manual provider-order tweak (Open Question #2 —
  if `YoutarrSeriesNfoProvider` won without `Order => 0`, the question resolves "no tweak needed").
- Whether `__kids` was correctly skipped (confirms Assumption A1: `FileSystemMetadata.Name`
  is the bare directory name).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing critical functionality] Added an empty `test_video.mp4` fixture**
- **Found during:** Task 1.
- **Issue:** The plan's `<files>` listed only `test_video.nfo` under `MyChannel/`. A Jellyfin "Shows" folder needs at least one media (video) file for the resolver to classify it as a Series; an NFO alone may not produce a Series in the live scan.
- **Fix:** `touch test/jellyfin-load-test/media/MyChannel/test_video.mp4` (empty file is sufficient for the resolver, per RESEARCH Step 2). Committed with Task 1.
- **Commit:** ec1f1a7

### Scope deviation (per execution scope limit, not a defect)

- **Task 3 (live human-verify checkpoint) not executed.** The Docker daemon is not running
  and starting it requires `sudo`, which is the operator's responsibility (project policy +
  the orchestrator's scope limit for this run). The harness, deploy step, and runbook are
  complete and verified offline; the live run is queued for the orchestrator/user.
  Consequently the plan counter was NOT advanced and the requirements were NOT marked
  complete — the live run is the gate evidence for PLUG-01/LIB-01/SER-01/SER-02/CMP-03.

## Threat Mitigations Applied

| Threat ID | Mitigation | Status |
|-----------|-----------|--------|
| T-01-06 (sudo for daemon start) | Agent never runs sudo; README surfaces copy-paste `sudo systemctl start docker` for the operator | Transferred to operator |
| T-01-07 (tampering with user media) | `./media:/media:ro` in compose (grep-verified); README instructs disabling "Save metadata to media folders" | Mitigated |
| T-01-08 (Malfunctioned plugin destabilizes server) | `restart: "no"` disposable container; README maps failure log signatures to root causes | Mitigated |

## Commits

- `ec1f1a7` feat(01-03): add Jellyfin load-test harness, fixtures, and deploy script
- `a6a2093` docs(01-03): add load-test runbook (setup/verify/iterate loop)

## Self-Check: PASSED

All 6 created files + the modified `.gitignore` exist on disk; the staged DLL exists at
`test/jellyfin-load-test/plugins/YoutarrMetadata_1.0.0.0/Jellyfin.Plugin.Youtarr.dll`
(git-ignored, not committed, by design); both per-task commits (ec1f1a7, a6a2093) are
present in git history. Live in-Jellyfin verification (Task 3) is pending the operator
starting the Docker daemon.
