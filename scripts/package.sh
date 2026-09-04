#!/usr/bin/env bash
#
# package.sh — Produces the distribution ZIP and the plugin-repository
# manifest.json for the Youtarr Jellyfin plugin in one reproducible command.
#
# Outputs:
#   dist/youtarrmetadata_<version>.zip           — install ZIP (PKG-01); gitignored
#   dist/youtarrmetadata_<version>.zip.md5sum    — MD5 sidecar (jprm-written); gitignored
#   dist/youtarrmetadata_<version>.zip.meta.json — jprm build metadata; gitignored
#   dist/manifest.json                           — jprm working manifest; gitignored
#   manifest.json (repo root)                    — TRACKED plugin-repository manifest
#
# The repo-root manifest.json is the file a user adds as a Jellyfin plugin
# repository ("Dashboard → Plugins → Repositories → Add"). It is identical to
# the jprm-generated dist/manifest.json EXCEPT its sourceUrl is rewritten to a
# resolvable GitHub Release asset URL so the catalog install actually works.
#
# The ZIP contains a FLAT layout (no subdirectory):
#   meta.json
#   Jellyfin.Plugin.Youtarr.dll
#
# jprm owns ZIP packaging (dotnet publish + meta.json + MD5 sidecar + slug naming)
# and manifest generation (correct schema + MD5 checksum + version sorting). Do NOT
# hand-roll zip/JSON — re-run this script to regenerate instead.
#
# CHECKSUM NOTE: manifest.json stores the MD5 of the ZIP, computed by jprm at build
# time. Never manually edit the ZIP after building — that invalidates the checksum
# and Jellyfin's catalog installer will reject the install. Re-run this script in
# full to rebuild the ZIP and regenerate both manifests.
#
# sourceUrl: the repo-root manifest points at a GitHub Release asset:
#   ${REPO_URL}/releases/download/v<version>/youtarrmetadata_<version>.zip
# REPO_URL is derived from (in order): the REPO_URL env var; `git remote get-url
# origin`; or the project default. The release that hosts this asset is created by
# .github/workflows/release.yml when a v<version> tag is pushed.
#
# This script never runs docker and never escalates privileges. Starting the Docker
# daemon and bringing the harness container up are operator steps (project policy:
# no privilege escalation). The harness install commands are echoed at the end,
# not run.
#
# Usage:
#   ./scripts/package.sh
#   REPO_URL=https://github.com/OWNER/REPO ./scripts/package.sh   # override
#
set -euo pipefail

# Resolve the repo root from this script's location so it is runnable from anywhere.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

PLUGIN_DIR="${REPO_ROOT}/Jellyfin.Plugin.Youtarr"
DIST_DIR="${REPO_ROOT}/dist"

# Single source of truth for the version: build.yaml. Bump it there only.
VERSION="$(grep -E '^version:' "${PLUGIN_DIR}/build.yaml" | head -1 | sed -E 's/^version:[[:space:]]*"?([^"]+)"?[[:space:]]*$/\1/')"
if [[ -z "${VERSION}" ]]; then
  echo "ERROR: could not read version from ${PLUGIN_DIR}/build.yaml" >&2
  exit 1
fi

# --- Derive the canonical https repo URL ------------------------------------
# Precedence: explicit REPO_URL env (used by CI) > git origin > project default.
normalize_repo_url() {
  # Normalizes a git remote URL to https://github.com/OWNER/REPO (no .git suffix).
  local url="$1"
  url="${url%.git}"
  case "${url}" in
    git@*:*)
      # git@github.com:OWNER/REPO  ->  https://github.com/OWNER/REPO
      local host="${url#git@}"        # github.com:OWNER/REPO
      host="${host%%:*}"             # github.com
      local path="${url#*:}"          # OWNER/REPO
      printf 'https://%s/%s' "${host}" "${path}"
      ;;
    ssh://git@*)
      # ssh://git@github.com/OWNER/REPO -> https://github.com/OWNER/REPO
      printf 'https://%s' "${url#ssh://git@}"
      ;;
    *)
      printf '%s' "${url}"
      ;;
  esac
}

if [[ -n "${REPO_URL:-}" ]]; then
  REPO_URL="$(normalize_repo_url "${REPO_URL}")"
elif ORIGIN_URL="$(git -C "${REPO_ROOT}" remote get-url origin 2>/dev/null)" && [[ -n "${ORIGIN_URL}" ]]; then
  REPO_URL="$(normalize_repo_url "${ORIGIN_URL}")"
else
  REPO_URL="https://github.com/sandwichfarm/youtarr-jf-plugin"
fi

SOURCE_URL="${REPO_URL}/releases/download/v${VERSION}/youtarrmetadata_${VERSION}.zip"

echo "==> Building plugin ZIP with jprm ..."
mkdir -p "${DIST_DIR}"

# A preceding Docker-fallback build uses an isolated NuGet cache. Restore with the
# active host SDK first so jprm's clean/build sequence never consumes stale container
# paths from obj/project.assets.json.
dotnet restore "${PLUGIN_DIR}"

# jprm plugin build MUST run from the directory containing build.yaml — i.e.
# Jellyfin.Plugin.Youtarr/. Running it from the repo root is a silent no-op
# (exit 0, no ZIP) because jprm searches for build.yaml in the given PATH, not
# recursively. PLUGIN_DIR points at "${REPO_ROOT}/Jellyfin.Plugin.Youtarr".
cd "${PLUGIN_DIR}"  # = ${REPO_ROOT}/Jellyfin.Plugin.Youtarr
jprm plugin build . -o "${DIST_DIR}"

# Resolve the exact current-version ZIP. Never select a stale higher-version artifact.
ZIP="${DIST_DIR}/youtarrmetadata_${VERSION}.zip"
if [[ -z "${ZIP}" || ! -f "${ZIP}" ]]; then
  echo "ERROR: jprm produced no ZIP in ${DIST_DIR}." >&2
  echo "       Confirm 'jprm plugin build' ran from ${PLUGIN_DIR} (where build.yaml lives)." >&2
  exit 1
fi
echo "==> Built: ${ZIP}"

# Generate / update the working manifest in dist/. The tracked root manifest is
# authoritative for already-published versions; seed from it so a stale local dist
# manifest can never rewrite an older release's checksum or timestamp.
DIST_MANIFEST="${DIST_DIR}/manifest.json"
ROOT_MANIFEST="${REPO_ROOT}/manifest.json"
if [[ -f "${ROOT_MANIFEST}" ]]; then
  cp -f "${ROOT_MANIFEST}" "${DIST_MANIFEST}"
elif [[ ! -f "${DIST_MANIFEST}" ]]; then
  echo "==> Initializing dist/manifest.json ..."
  jprm repo init "${DIST_MANIFEST}"
fi

echo "==> Adding plugin to dist/manifest.json ..."
# --plugin-url sets sourceUrl directly to the resolvable release asset, so the
# dist manifest and the repo-root manifest carry an identical, working URL.
jprm repo add "${DIST_MANIFEST}" "${ZIP}" --plugin-url "${SOURCE_URL}"

# Emit the tracked repo-root manifest. It is the dist manifest with the sourceUrl
# guaranteed-resolvable; jprm already wrote that sourceUrl above, so we copy the
# jprm output verbatim (preserving checksum/targetAbi/timestamp/changelog/guid/name)
# and re-assert sourceUrl defensively in case REPO_URL was overridden mid-run.
echo "==> Writing tracked repo-root manifest.json ..."
python3 - "${DIST_MANIFEST}" "${ROOT_MANIFEST}" "${VERSION}" "${SOURCE_URL}" <<'PY'
import json, sys

dist_manifest, root_manifest, version, source_url = sys.argv[1:5]

with open(dist_manifest, "r", encoding="utf-8") as fh:
    data = json.load(fh)

if not isinstance(data, list) or not data:
    sys.exit("ERROR: dist manifest is not a non-empty JSON array")

# Rewrite sourceUrl for the matching version entry across all plugin entries so
# the published manifest always points at the resolvable release asset.
for plugin in data:
    for entry in plugin.get("versions", []):
        if entry.get("version") == version:
            entry["sourceUrl"] = source_url

with open(root_manifest, "w", encoding="utf-8") as fh:
    json.dump(data, fh, indent=4)
    fh.write("\n")
PY

echo ""
echo "==> Done."
echo "    ZIP:           ${ZIP}"
echo "    Dist manifest: ${DIST_MANIFEST}"
echo "    Repo manifest: ${ROOT_MANIFEST}"
echo "    sourceUrl:     ${SOURCE_URL}"
echo ""
echo "To verify install in the Docker harness (operator runs these — NOT this script):"
echo "    mkdir -p ${REPO_ROOT}/test/jellyfin-load-test/plugins/YoutarrMetadata_${VERSION}"
echo "    unzip -o ${ZIP} -d ${REPO_ROOT}/test/jellyfin-load-test/plugins/YoutarrMetadata_${VERSION}/"
echo "    docker restart jellyfin-plugin-test"
