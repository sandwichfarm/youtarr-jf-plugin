#!/usr/bin/env bash
# End-to-end proof against a real Jellyfin 10.11.11 server. The run uses a fresh
# temporary Jellyfin data directory, waits for each asynchronous library scan to
# finish, and validates both repository parentage and UI-facing SeasonId metadata.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${HERE}/../.." && pwd)"
MEDIA_DIR="${HERE}/media"
BASE="${JELLYFIN_URL:-http://localhost:8096}"
ADMIN_USER="${JF_ADMIN_USER:-admin}"
ADMIN_PASS="${JF_ADMIN_PASS:-youtarr-repro-123}"
AUTH_HDR='MediaBrowser Client="repro", Device="repro", DeviceId="repro-device", Version="1.0.0"'

say() { printf '\n\033[1m==> %s\033[0m\n' "$*"; }
fail() { printf '\033[31mFAIL: %s\033[0m\n' "$*" >&2; exit 1; }

case "${BASE}" in
  http://localhost:* | http://127.0.0.1:*) ;;
  *)
    [[ "${YOUTARR_ALLOW_REMOTE_TEST:-}" == "1" ]] \
      || fail "refusing to configure non-local JELLYFIN_URL=${BASE}; set YOUTARR_ALLOW_REMOTE_TEST=1 only for a disposable server"
    ;;
esac

command -v docker >/dev/null || fail "docker not found"
command -v jq >/dev/null || fail "jq not found"
docker info >/dev/null 2>&1 || fail "Docker daemon is not running"

PLUGIN_NAME="$(sed -n -E 's/^name:[[:space:]]*"?([^\"]+)"?[[:space:]]*$/\1/p' "${REPO_ROOT}/Jellyfin.Plugin.Youtarr/build.yaml" | head -1)"
PLUGIN_VERSION="$(sed -n -E 's/^version:[[:space:]]*"?([^\"]+)"?[[:space:]]*$/\1/p' "${REPO_ROOT}/Jellyfin.Plugin.Youtarr/build.yaml" | head -1)"
[[ -n "${PLUGIN_NAME}" && -n "${PLUGIN_VERSION}" ]] || fail "could not read build.yaml"

if [[ -n "${YOUTARR_TEST_STATE_DIR:-}" ]]; then
  TEST_STATE_DIR="${YOUTARR_TEST_STATE_DIR}"
  mkdir -p "${TEST_STATE_DIR}"
else
  TEST_STATE_DIR="$(mktemp -d /tmp/youtarr-jf-load-test.XXXXXX)"
fi

export YOUTARR_TEST_CONFIG_DIR="${TEST_STATE_DIR}/config"
export YOUTARR_TEST_CACHE_DIR="${TEST_STATE_DIR}/cache"
export YOUTARR_TEST_PLUGIN_DIR="${TEST_STATE_DIR}/plugins"
PLUGIN_INSTALL_DIR="${YOUTARR_TEST_PLUGIN_DIR}/${PLUGIN_NAME}_${PLUGIN_VERSION}"
mkdir -p "${YOUTARR_TEST_CONFIG_DIR}" "${YOUTARR_TEST_CACHE_DIR}" "${YOUTARR_TEST_PLUGIN_DIR}"

say "Using clean Jellyfin state at ${TEST_STATE_DIR}"
docker rm -f jellyfin-plugin-test >/dev/null 2>&1 || true

say "Building and staging ${PLUGIN_NAME} ${PLUGIN_VERSION}"
if [[ -n "${YOUTARR_TEST_PLUGIN_DLL:-}" ]]; then
  [[ -f "${YOUTARR_TEST_PLUGIN_DLL}" ]] || fail "plugin DLL not found: ${YOUTARR_TEST_PLUGIN_DLL}"
  mkdir -p "${PLUGIN_INSTALL_DIR}"
  cp -f "${YOUTARR_TEST_PLUGIN_DLL}" "${PLUGIN_INSTALL_DIR}/Jellyfin.Plugin.Youtarr.dll"
  echo "Staged supplied DLL: ${YOUTARR_TEST_PLUGIN_DLL}"
else
  JELLYFIN_PLUGIN_DIR="${PLUGIN_INSTALL_DIR}" bash "${REPO_ROOT}/scripts/deploy-plugin.sh"
fi

say "Starting Jellyfin 10.11.11"
(cd "${HERE}" && docker compose up -d)

say "Waiting for Jellyfin at ${BASE}"
SERVER_VERSION=""
for i in $(seq 1 90); do
  if PUBLIC_INFO="$(curl -fsS "${BASE}/System/Info/Public" 2>/dev/null)"; then
    SERVER_VERSION="$(jq -r '.Version // empty' <<<"${PUBLIC_INFO}")"
    if [[ -n "${SERVER_VERSION}" ]]; then
      echo "Jellyfin ${SERVER_VERSION} answered after ${i}s"
      break
    fi
  fi
  sleep 1
  [[ "${i}" != 90 ]] || fail "Jellyfin startup timed out; inspect docker logs jellyfin-plugin-test"
done

[[ "${SERVER_VERSION}" == "10.11.11" ]] || fail "expected Jellyfin 10.11.11, got ${SERVER_VERSION}"

WIZARD_DONE="$(curl -fsS "${BASE}/System/Info/Public" | jq -r '.StartupWizardCompleted')"
if [[ "${WIZARD_DONE}" != "true" ]]; then
  say "Completing the disposable startup wizard"
  curl -fsS -X POST "${BASE}/Startup/Configuration" \
    -H 'Content-Type: application/json' \
    -d '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}' >/dev/null
  curl -fsS "${BASE}/Startup/User" >/dev/null || true
  curl -fsS -X POST "${BASE}/Startup/User" \
    -H 'Content-Type: application/json' \
    -d "{\"Name\":\"${ADMIN_USER}\",\"Password\":\"${ADMIN_PASS}\"}" >/dev/null
  curl -fsS -X POST "${BASE}/Startup/RemoteAccess" \
    -H 'Content-Type: application/json' \
    -d '{"EnableRemoteAccess":true,"EnableAutomaticPortMapping":false}' >/dev/null
  curl -fsS -X POST "${BASE}/Startup/Complete" >/dev/null
fi

say "Authenticating and checking plugin status"
TOKEN="$(curl -fsS -X POST "${BASE}/Users/AuthenticateByName" \
  -H 'Content-Type: application/json' \
  -H "X-Emby-Authorization: ${AUTH_HDR}" \
  -d "{\"Username\":\"${ADMIN_USER}\",\"Pw\":\"${ADMIN_PASS}\"}" \
  | jq -r '.AccessToken')"
[[ -n "${TOKEN}" && "${TOKEN}" != "null" ]] || fail "authentication failed"
api() { curl -fsS -H "Authorization: MediaBrowser Token=\"${TOKEN}\"" "$@"; }

PLUGIN_STATUS="$(api "${BASE}/Plugins" | jq -r --arg name "${PLUGIN_NAME}" \
  '[.[] | select(.Name == $name)][0].Status // "NOT_INSTALLED"')"
echo "${PLUGIN_NAME}: ${PLUGIN_STATUS}"
[[ "${PLUGIN_STATUS}" == "Active" ]] || fail "plugin is ${PLUGIN_STATUS}, not Active"

say "Creating a Shows library at /media"
HAS_LIBRARY="$(api "${BASE}/Library/VirtualFolders" | jq -r 'any(.[]; .Name == "YouTube")')"
if [[ "${HAS_LIBRARY}" != "true" ]]; then
  curl -fsS -X POST \
    "${BASE}/Library/VirtualFolders?name=YouTube&collectionType=tvshows&refreshLibrary=false" \
    -H "Authorization: MediaBrowser Token=\"${TOKEN}\"" \
    -H 'Content-Type: application/json' \
    -d '{"LibraryOptions":{"EnableRealtimeMonitor":false,"SaveLocalMetadata":false,"MetadataSavers":[],"EnableInternetProviders":false,"TypeOptions":[{"Type":"Series","MetadataFetchers":[],"ImageFetchers":[]},{"Type":"Season","MetadataFetchers":[],"ImageFetchers":[]},{"Type":"Episode","MetadataFetchers":[],"ImageFetchers":[]}],"PathInfos":[{"Path":"/media"}]}}' >/dev/null
else
  echo "Reusing existing YouTube library in ${TEST_STATE_DIR}"
fi

USER_ID="$(api "${BASE}/Users/Me" | jq -r '.Id')"
export TOKEN BASE USER_ID

refresh_task() {
  api "${BASE}/ScheduledTasks" | jq -c '.[] | select(.Key == "RefreshLibrary")'
}

wait_for_idle() {
  for i in $(seq 1 120); do
    local state
    state="$(refresh_task | jq -r '.State')"
    if [[ "${state}" == "Idle" ]]; then
      return
    fi
    sleep 1
  done
  fail "the existing library scan did not become idle"
}

trigger_scan() {
  local label="$1"
  local before task state started status

  wait_for_idle
  before="$(refresh_task | jq -r '.LastExecutionResult.StartTimeUtc // "none"')"
  say "Triggering ${label} and waiting for its terminal result"
  curl -fsS -X POST "${BASE}/Library/Refresh" \
    -H "Authorization: MediaBrowser Token=\"${TOKEN}\"" >/dev/null

  for i in $(seq 1 180); do
    task="$(refresh_task)"
    state="$(jq -r '.State' <<<"${task}")"
    started="$(jq -r '.LastExecutionResult.StartTimeUtc // "none"' <<<"${task}")"
    status="$(jq -r '.LastExecutionResult.Status // "none"' <<<"${task}")"
    if [[ "${state}" == "Idle" && "${started}" != "${before}" ]]; then
      echo "${label}: state=${state}, status=${status}, start=${started}"
      [[ "${status}" == "Completed" ]] || fail "${label} ended with status ${status}"
      return
    fi
    sleep 1
  done

  fail "${label} did not complete within 180s"
}

validate_grouping() {
  local label="$1"
  say "Validating complete season identity after ${label}"
  python3 - "${MEDIA_DIR}" "${label}" <<'PYEOF'
import json
import os
import sys
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
from collections import Counter, defaultdict
from pathlib import Path

media_root = Path(sys.argv[1])
label = sys.argv[2]
base = os.environ["BASE"]
token = os.environ["TOKEN"]
user_id = os.environ["USER_ID"]


def fail(message):
    raise SystemExit(f"FAIL [{label}]: {message}")


def jf_items(**params):
    params.setdefault("userId", user_id)
    url = base + "/Items?" + urllib.parse.urlencode(params)
    request = urllib.request.Request(
        url,
        headers={"Authorization": f'MediaBrowser Token="{token}"'},
    )
    with urllib.request.urlopen(request) as response:
        return json.loads(response.read()).get("Items", [])


expected = defaultdict(list)
for channel_dir in sorted(media_root.iterdir()):
    if not channel_dir.is_dir() or channel_dir.name.startswith("__"):
        continue
    for nfo in sorted(channel_dir.rglob("*.nfo")):
        root = ET.parse(nfo).getroot()
        if root.tag != "movie":
            continue
        title = (root.findtext("title") or nfo.stem).strip()
        premiered = (root.findtext("premiered") or "").strip()
        season_number = int(premiered[:4]) if len(premiered) >= 4 else 0
        youtube_id = next(
            (
                (element.text or "").strip()
                for element in root.findall("uniqueid")
                if (element.get("type") or "").lower() == "youtube" and (element.text or "").strip()
            ),
            (root.findtext("youtubeid") or "").strip(),
        )
        if not youtube_id:
            fail(f"fixture NFO has no YouTube ID: {nfo}")
        expected[channel_dir.name].append(
            {"title": title, "season": season_number, "youtube_id": youtube_id}
        )

series_items = jf_items(Recursive="true", IncludeItemTypes="Series")
actual_series = {item["Name"]: item for item in series_items}
if set(actual_series) != set(expected):
    fail(f"series mismatch: expected {sorted(expected)}, got {sorted(actual_series)}")

for series_name, expected_episodes in sorted(expected.items()):
    series_id = actual_series[series_name]["Id"]
    seasons = jf_items(
        ParentId=series_id,
        IncludeItemTypes="Season",
        Fields="ParentId,Path,SeriesId,IndexNumber,LocationType",
    )
    expected_counts = Counter(item["season"] for item in expected_episodes)
    actual_by_number = defaultdict(list)
    for season in seasons:
        actual_by_number[season.get("IndexNumber")].append(season)

    if set(actual_by_number) != set(expected_counts):
        fail(
            f"{series_name} season numbers mismatch: "
            f"expected {sorted(expected_counts)}, got {sorted(actual_by_number, key=lambda value: -1 if value is None else value)}"
        )
    duplicates = {number: len(items) for number, items in actual_by_number.items() if len(items) != 1}
    if duplicates:
        fail(f"{series_name} has duplicate canonical seasons: {duplicates}")

    season_by_number = {number: items[0] for number, items in actual_by_number.items()}
    episodes = [
        item
        for item in jf_items(
            Recursive="true",
            IncludeItemTypes="Episode",
            Fields="ParentId,Path,ProviderIds,SeriesId,SeasonId,SeasonName,ParentIndexNumber,ProductionYear",
        )
        if item.get("SeriesId") == series_id
    ]

    expected_by_id = {item["youtube_id"]: item for item in expected_episodes}
    actual_by_id = {}
    for episode in episodes:
        provider_ids = episode.get("ProviderIds") or {}
        youtube_id = next(
            (value for key, value in provider_ids.items() if key.lower() == "youtube"),
            None,
        )
        if not youtube_id:
            fail(f"{series_name}/{episode.get('Name')}: missing YouTube provider ID")
        if youtube_id in actual_by_id:
            fail(f"{series_name}: duplicate YouTube ID {youtube_id}")
        actual_by_id[youtube_id] = episode

    if set(actual_by_id) != set(expected_by_id):
        fail(
            f"{series_name} episode IDs mismatch: expected {sorted(expected_by_id)}, "
            f"got {sorted(actual_by_id)}"
        )

    for youtube_id, expected_episode in expected_by_id.items():
        title = expected_episode["title"]
        target_number = expected_episode["season"]
        episode = actual_by_id[youtube_id]
        target_season = season_by_number[target_number]
        target_id = target_season["Id"]
        target_name = target_season["Name"]
        checks = {
            "ParentIndexNumber": target_number,
            "SeasonId": target_id,
            "ParentId": target_id,
            "SeasonName": target_name,
        }
        for field, wanted in checks.items():
            if episode.get(field) != wanted:
                fail(
                    f"{series_name}/{title}: {field}={episode.get(field)!r}, "
                    f"expected {wanted!r}"
                )
        if target_number >= 2005 and episode.get("ProductionYear") != target_number:
            fail(
                f"{series_name}/{title}: ProductionYear={episode.get('ProductionYear')!r}, "
                f"expected {target_number}"
            )

    for number, expected_count in sorted(expected_counts.items()):
        season = season_by_number[number]
        children = jf_items(ParentId=season["Id"], IncludeItemTypes="Episode")
        if len(children) != expected_count:
            fail(
                f"{series_name}/{season['Name']}: {len(children)} repository children, "
                f"expected {expected_count}"
            )

    rendered = ", ".join(
        f"{season_by_number[number]['Name']}={count}"
        for number, count in sorted(expected_counts.items())
    )
    print(f"PASS [{label}]: {series_name}: {rendered}")

print(f"PASS [{label}]: every episode has canonical ParentId + SeasonId + SeasonName")
PYEOF
}

trigger_scan "scan #1"
validate_grouping "scan #1"
trigger_scan "scan #2"
validate_grouping "scan #2"

say "Checking plugin logs for unhandled regroup failures"
if docker logs jellyfin-plugin-test 2>&1 | grep -E '\[Youtarr\].*(error regrouping|Failed to parse)' >/tmp/youtarr-jf-plugin-errors.log; then
  cat /tmp/youtarr-jf-plugin-errors.log >&2
  fail "plugin logged a regroup/parse failure"
fi

printf '\n\033[32mPASS: Jellyfin %s grouped every fixture into its NFO publication-year season across two complete scans.\033[0m\n' "${SERVER_VERSION}"
echo "Container: jellyfin-plugin-test"
echo "State:     ${TEST_STATE_DIR}"
