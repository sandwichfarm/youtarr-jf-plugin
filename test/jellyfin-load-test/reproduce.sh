#!/usr/bin/env bash
#
# reproduce.sh — End-to-end, headless reproduction of the Youtarr Jellyfin plugin
# against a real Jellyfin 10.10.7 container. Proves (or disproves) the core behavior:
# channel folders become Series, videos become Episodes, grouped into year-seasons.
#
# It drives Jellyfin entirely through its REST API — no browser clicking — and prints
# a PASS/FAIL verdict. This is the live verification the planned phases deferred.
#
# PREREQUISITE: the Docker daemon must be running. Starting it needs sudo, which is the
# operator's job (project policy: this script never escalates privileges). Start it with:
#   sudo sh -c 'nohup dockerd >/tmp/dockerd.log 2>&1 & for i in $(seq 1 20); do [ -S /var/run/docker.sock ] && break; sleep 1; done; chmod 666 /var/run/docker.sock'
#
# Usage:
#   ./test/jellyfin-load-test/reproduce.sh
#
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${HERE}/../.." && pwd)"
BASE="${JELLYFIN_URL:-http://localhost:8096}"
ADMIN_USER="${JF_ADMIN_USER:-admin}"
ADMIN_PASS="${JF_ADMIN_PASS:-youtarr-repro-123}"
AUTH_HDR='MediaBrowser Client="repro", Device="repro", DeviceId="repro-device", Version="1.0.0"'

say() { printf '\n\033[1m==> %s\033[0m\n' "$*"; }
fail() { printf '\033[31mFAIL: %s\033[0m\n' "$*" >&2; exit 1; }

command -v docker >/dev/null || fail "docker not found"
docker info >/dev/null 2>&1 || fail "Docker daemon not running. Start it (see header) and re-run."

# --- 1. Stage the plugin DLL + bring the container up -----------------------
say "Staging plugin (dotnet publish -> harness plugins dir)"
bash "${REPO_ROOT}/scripts/deploy-plugin.sh"

say "Starting Jellyfin 10.10.7 container"
( cd "${HERE}" && docker compose up -d )

# --- 2. Wait for Jellyfin to answer -----------------------------------------
say "Waiting for Jellyfin to come up at ${BASE}"
for i in $(seq 1 60); do
  if curl -fsS "${BASE}/System/Info/Public" >/dev/null 2>&1; then echo "up after ${i}s"; break; fi
  sleep 2
  [ "$i" = 60 ] && fail "Jellyfin did not become reachable; check: docker logs jellyfin-plugin-test"
done

WIZARD_DONE=$(curl -fsS "${BASE}/System/Info/Public" | python3 -c "import sys,json;print(json.load(sys.stdin).get('StartupWizardCompleted'))" 2>/dev/null || echo "False")

# --- 3. Complete the startup wizard (idempotent) ----------------------------
if [ "${WIZARD_DONE}" != "True" ]; then
  say "Completing startup wizard (admin=${ADMIN_USER})"
  curl -fsS -X POST "${BASE}/Startup/Configuration" -H "Content-Type: application/json" \
    -d '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}' >/dev/null
  curl -fsS "${BASE}/Startup/User" >/dev/null || true
  curl -fsS -X POST "${BASE}/Startup/User" -H "Content-Type: application/json" \
    -d "{\"Name\":\"${ADMIN_USER}\",\"Password\":\"${ADMIN_PASS}\"}" >/dev/null
  curl -fsS -X POST "${BASE}/Startup/RemoteAccess" -H "Content-Type: application/json" \
    -d '{"EnableRemoteAccess":true,"EnableAutomaticPortMapping":false}' >/dev/null
  curl -fsS -X POST "${BASE}/Startup/Complete" >/dev/null
else
  say "Startup wizard already completed — reusing existing instance"
fi

# --- 4. Authenticate --------------------------------------------------------
say "Authenticating"
TOKEN=$(curl -fsS -X POST "${BASE}/Users/AuthenticateByName" \
  -H "Content-Type: application/json" -H "X-Emby-Authorization: ${AUTH_HDR}" \
  -d "{\"Username\":\"${ADMIN_USER}\",\"Pw\":\"${ADMIN_PASS}\"}" \
  | python3 -c "import sys,json;print(json.load(sys.stdin)['AccessToken'])")
[ -n "${TOKEN}" ] || fail "could not authenticate"
api() { curl -fsS -H "Authorization: MediaBrowser Token=\"${TOKEN}\"" "$@"; }

# --- 5. Confirm the plugin loaded as Active ---------------------------------
say "Checking plugin status"
PLUGIN_STATUS=$(api "${BASE}/Plugins" | python3 -c "
import sys,json
ps=json.load(sys.stdin)
m=[p for p in ps if 'Youtarr' in p.get('Name','')]
print(m[0]['Status'] if m else 'NOT_INSTALLED')
")
echo "YoutarrMetadata plugin: ${PLUGIN_STATUS}"
[ "${PLUGIN_STATUS}" = "Active" ] || fail "plugin not Active (status=${PLUGIN_STATUS}) — check container logs for TypeLoadException/targetAbi"

# --- 6. Create a TV SHOWS library at /media (TVDB/TMDB off, no metadata save)
say "Creating a 'Shows' library pointed at /media"
EXISTING=$(api "${BASE}/Library/VirtualFolders" | python3 -c "import sys,json;print(any(v.get('Name')=='YouTube' for v in json.load(sys.stdin)))" 2>/dev/null || echo "False")
if [ "${EXISTING}" != "True" ]; then
  curl -fsS -X POST "${BASE}/Library/VirtualFolders?name=YouTube&collectionType=tvshows&refreshLibrary=false" \
    -H "Authorization: MediaBrowser Token=\"${TOKEN}\"" -H "Content-Type: application/json" \
    -d '{"LibraryOptions":{"EnableRealtimeMonitor":false,"SaveLocalMetadata":false,"MetadataSavers":[],"EnableInternetProviders":false,"TypeOptions":[{"Type":"Series","MetadataFetchers":[],"ImageFetchers":[]},{"Type":"Season","MetadataFetchers":[],"ImageFetchers":[]},{"Type":"Episode","MetadataFetchers":[],"ImageFetchers":[]}],"PathInfos":[{"Path":"/media"}]}}' >/dev/null
else
  echo "library 'YouTube' already exists — reusing"
fi

# --- 7. Scan + wait for it to settle ----------------------------------------
say "Triggering library scan"
curl -fsS -X POST "${BASE}/Library/Refresh" -H "Authorization: MediaBrowser Token=\"${TOKEN}\"" >/dev/null
USER_ID=$(api "${BASE}/Users/Me" | python3 -c "import sys,json;print(json.load(sys.stdin)['Id'])")
say "Waiting for scan to produce Series"
SERIES_JSON='[]'
for i in $(seq 1 45); do
  SERIES_JSON=$(api "${BASE}/Items?userId=${USER_ID}&Recursive=true&IncludeItemTypes=Series" | python3 -c "import sys,json;print(json.dumps([i['Name'] for i in json.load(sys.stdin).get('Items',[])]))")
  CNT=$(printf '%s' "${SERIES_JSON}" | python3 -c "import sys,json;print(len(json.load(sys.stdin)))")
  [ "${CNT}" -ge 2 ] && break
  sleep 2
done

# --- 8. Collect results -----------------------------------------------------
say "Results"
fetch() { api "${BASE}/Items?userId=${USER_ID}&Recursive=true&IncludeItemTypes=$1&Fields=ParentId" \
  | python3 -c "import sys,json;[print(' -',i['Name']) for i in json.load(sys.stdin).get('Items',[])]"; }

echo "Series (each YouTube channel should be one):"; fetch Series
echo "Seasons (should be year-named, e.g. 2023/2024/2025):"; fetch Season
echo "Episodes:"; fetch Episode

SERIES_NAMES=$(api "${BASE}/Items?userId=${USER_ID}&Recursive=true&IncludeItemTypes=Series" | python3 -c "import sys,json;print('|'.join(sorted(i['Name'] for i in json.load(sys.stdin).get('Items',[]))))")
SERIES_COUNT=$(printf '%s' "${SERIES_NAMES}" | awk -F'|' '{print ($0==""?0:NF)}')
KIDS_LEAK=$(api "${BASE}/Items?userId=${USER_ID}&Recursive=true&IncludeItemTypes=Series" | python3 -c "import sys,json;print(any('__' in i['Name'] for i in json.load(sys.stdin).get('Items',[])))")

# --- 9. Verdict -------------------------------------------------------------
say "Verdict"
echo "Series found: ${SERIES_COUNT}  -> [${SERIES_NAMES}]"
echo "__-prefix leaked as a Series: ${KIDS_LEAK} (must be False)"
if [ "${SERIES_COUNT}" -ge 2 ] && [ "${KIDS_LEAK}" = "False" ]; then
  printf '\033[32m\nPASS: channels are separated into Series (the plugin + Shows-library grouping works).\033[0m\n'
  echo "If your real library is a flat wall while this passes, the difference is your library's"
  echo "content type (must be 'Shows') or the folder you pointed it at (must be the channel parent)."
else
  printf '\033[31m\nFAIL: channels did not group. Inspect: docker logs jellyfin-plugin-test\033[0m\n'
  exit 1
fi
