# Jellyfin Load-Test Harness (Youtarr Metadata Plugin)

A reusable Docker harness for verifying the `YoutarrMetadata` plugin against a real
**Jellyfin 10.10.7** server. Every later phase reuses this exact loop:

> edit code → `scripts/deploy-plugin.sh` → restart container → rescan → read logs

This README is the operator runbook. The build agent stages the plugin DLL for you
(`scripts/deploy-plugin.sh` requires no Docker), but **starting the Docker daemon and
bringing the container up are your steps** — project policy forbids the agent from
running `sudo` or `docker`. All commands you need are copy-paste blocks below.

---

## What is in this directory

```
test/jellyfin-load-test/
├── docker-compose.yml                          jellyfin/jellyfin:10.10.7, media mounted :ro
├── media/                                       fixture library (COMMITTED)
│   ├── MyChannel/                               → must resolve as a Series named "MyChannel"
│   │   ├── test_video.nfo                       <movie> NFO with <studio>
│   │   └── test_video.mp4                       empty content file
│   └── __kids/                                  → must NOT appear as a Series (CMP-03)
├── plugins/                                     staged DLL (GENERATED, git-ignored)
│   └── YoutarrMetadata_1.0.0.0/
│       └── Jellyfin.Plugin.Youtarr.dll
├── config/                                      Jellyfin data dir (GENERATED, git-ignored)
└── cache/                                       Jellyfin cache    (GENERATED, git-ignored)
```

`config/`, `cache/`, and `plugins/` are regenerated and git-ignored. `media/` is committed.

---

## 0. Stage the plugin DLL (no Docker required)

From the repo root:

```bash
./scripts/deploy-plugin.sh
```

This runs `dotnet publish -c Release` and copies `Jellyfin.Plugin.Youtarr.dll` into
`test/jellyfin-load-test/plugins/YoutarrMetadata_1.0.0.0/`. The folder name
(`YoutarrMetadata_1.0.0.0` = `<name>_<version>` from `build.yaml`) is what Jellyfin
looks for — do not rename it. Confirm the DLL landed:

```bash
ls test/jellyfin-load-test/plugins/YoutarrMetadata_1.0.0.0/Jellyfin.Plugin.Youtarr.dll
```

---

## 1. Start the Docker daemon and pull the image (requires sudo — operator runs this)

The agent cannot run `sudo`. Copy-paste these yourself:

```bash
sudo systemctl start docker
```

Then pull the exact server image (no sudo needed once the daemon is up and your user
is in the `docker` group):

```bash
docker pull jellyfin/jellyfin:10.10.7
```

> If your user is NOT in the `docker` group, prefix the `docker` commands below with
> `sudo` as well.

---

## 2. Bring the container up

From this harness directory:

```bash
cd test/jellyfin-load-test
docker compose up -d
# Wait ~20s for first-run initialization
docker logs jellyfin-plugin-test --tail 50
```

Open <http://localhost:8096> in a browser.

---

## 3. First-run setup wizard

In the browser at <http://localhost:8096>:

1. **Create an admin user** (any username/password — this is a throwaway test server).
2. **Add a media library:**
   - Content type: **Shows**
   - Folder: **`/media`**
3. **DISABLE "Save metadata to media folders"** for this library.
   - Dashboard → Libraries → (your Shows library) → Edit → uncheck
     **"Save artwork and metadata into media folders"**.
   - This is mandatory: otherwise Jellyfin overwrites Youtarr's on-disk NFOs
     (Jellyfin issue #12197). The media is mounted read-only anyway, but disabling
     this avoids scan errors.
4. **Disable TVDB and TMDB** metadata providers for this library (uncheck them in the
   same library edit dialog). They will never match YouTube content and only add
   noise to the logs.

Save and let the initial scan finish.

---

## 4. Verify the plugin loaded — status "Active" (PLUG-01)

In the UI: **Dashboard → Plugins** — the **YoutarrMetadata** entry must show status
**Active**.

From the logs (copy-paste):

```bash
docker exec jellyfin-plugin-test sh -c \
  'cat /config/log/log_$(date +%Y%m%d)*.log' 2>/dev/null \
  | grep -i "youtarr\|YoutarrMetadata\|plugin" | head -30
```

**Expected (success) signatures:**

```
Plugin 'YoutarrMetadata' version '1.0.0.0' is compatible with this server.
Loaded plugin 'YoutarrMetadata' v1.0.0.0
```

**Failure signatures → root cause:**

| Log signature                              | Root cause                                            | Fix |
| ------------------------------------------ | ----------------------------------------------------- | --- |
| `ReflectionTypeLoadException` / `TypeLoadException` | Jellyfin runtime DLLs bundled in the plugin     | Ensure `<ExcludeAssets>runtime</ExcludeAssets>` on both Jellyfin package refs (already set; publish output is verified clean) |
| `NotSupported`                             | `targetAbi` mismatch                                  | `build.yaml` must use `targetAbi: "10.10.0.0"` (already set) |
| `Malfunctioned`                            | Unhandled exception in the plugin constructor          | Inspect the full stack in the log; check `Plugin.cs` constructor |

---

## 5. Verify Series resolution (LIB-01, SER-01, SER-02) and __prefix suppression (CMP-03)

Trigger a library scan from the UI (**Dashboard → Scheduled Tasks → Scan All Libraries
→ run**), or via the API:

```bash
# Create an API key first: Dashboard → API Keys → New API Key, then:
API_KEY="<paste-admin-api-key>"

curl -X POST "http://localhost:8096/Library/Refresh" \
  -H "Authorization: MediaBrowser Token=\"${API_KEY}\""

# Wait ~30s for the small library to finish, then list Series:
curl -s "http://localhost:8096/Shows" \
  -H "Authorization: MediaBrowser Token=\"${API_KEY}\"" \
  | python3 -m json.tool | grep -A2 '"Name"'
```

**Pass criteria:**

- **LIB-01 / SER-01** — a Series named exactly **`MyChannel`** appears (folder name, not
  a raw path).
- **SER-02** — that Series has metadata populated **even though there is no
  `tvshow.nfo`** on disk (the plugin synthesizes it; `HasMetadata = true`).
- **CMP-03** — **`__kids` does NOT appear** as a Series anywhere in the library.

Confirm `__kids` is absent in the UI (Shows view) and in the `/Shows` JSON above.

Read plugin-specific diagnostic lines any time:

```bash
docker exec jellyfin-plugin-test grep -r "\[Youtarr\]" /config/log/ 2>/dev/null
```

---

## 6. Dev iteration loop (reused by every later phase)

After changing plugin code:

```bash
# 1. Re-publish + re-stage the DLL (from repo root)
./scripts/deploy-plugin.sh

# 2. Restart Jellyfin so it reloads the plugin (plugins load once, at startup)
docker restart jellyfin-plugin-test

# 3. Wait ~15s, trigger a rescan
curl -X POST "http://localhost:8096/Library/Refresh" \
  -H "Authorization: MediaBrowser Token=\"${API_KEY}\""

# 4. Check logs
docker exec jellyfin-plugin-test grep -r "\[Youtarr\]" /config/log/ 2>/dev/null
```

> Jellyfin only loads plugins at startup, so a code change always requires
> `deploy-plugin.sh` **and** a container restart.

---

## 7. Teardown / reset

```bash
cd test/jellyfin-load-test
docker compose down

# Optional: wipe server state for a fresh first-run wizard
# (keeps the staged plugin and the committed media fixtures)
rm -rf config cache
```
