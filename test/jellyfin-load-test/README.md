# Jellyfin 10.11.11 load test

This disposable harness proves the plugin's full on-disk behavior against a real
Jellyfin server. It covers flat and nested Youtarr layouts, multiple videos in the
same publication year, numeric video-folder names, an undated video, and two complete
library scans.

The validator checks both sides of Jellyfin's season model:

- repository containment (`Episode.ParentId`);
- UI/API identity (`Episode.SeasonId`, `SeasonName`, and `ParentIndexNumber`);
- exactly one `Season <year>` per NFO `<premiered>` year;
- every expected episode appears exactly once under that season;
- no numeric or per-video phantom seasons remain;
- a second scan produces the same canonical result.

## Run

From the repository root:

```bash
./test/jellyfin-load-test/reproduce.sh
```

Prerequisites are Docker, Compose, `curl`, `jq`, and Python 3. A local .NET 9 SDK is
optional: `scripts/deploy-plugin.sh` automatically uses the official
`mcr.microsoft.com/dotnet/sdk:9.0` image when the SDK is not installed locally.

Each run creates a fresh state directory under `/tmp`, removes only the prior container
named `jellyfin-plugin-test`, starts `jellyfin/jellyfin:10.11.11`, performs the startup
wizard through the API, creates a Shows library at `/media`, and waits for each scan's
scheduled-task result to become terminal before validating it. The media mount is
read-only.

The script refuses a non-local `JELLYFIN_URL` because it creates an admin user and media
library. `YOUTARR_ALLOW_REMOTE_TEST=1` is available only for an intentionally disposable
remote test server.

On success, the container is left running for manual inspection at
<http://localhost:8096>; the script prints the temporary state directory. Stop it with:

```bash
cd test/jellyfin-load-test
docker compose down
```

To deliberately reuse a state directory while debugging:

```bash
YOUTARR_TEST_STATE_DIR=/tmp/my-youtarr-jf-state \
  ./test/jellyfin-load-test/reproduce.sh
```

## Why the nested fixture matters

Youtarr defaults to `Channel/<video folder>/<video>.mp4`. Jellyfin's Shows resolver
treats a directory directly below a Series as a Season candidate; numbers in the video
folder can therefore surface as `Season 18`, `Season 20`, `Season 1820`, and similar.
The plugin's post-scan reconciler reads the authoritative NFO `<premiered>` value,
updates the complete episode-season identity, and deletes only empty phantom seasons
without deleting media.
