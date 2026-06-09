#!/usr/bin/env bash
#
# package.sh — Produces the distribution ZIP and manifest.json for the
# Youtarr Jellyfin plugin in one reproducible command.
#
# Outputs (all under dist/, which is gitignored):
#   dist/youtarrmetadata_<version>.zip           — install ZIP (PKG-01)
#   dist/youtarrmetadata_<version>.zip.md5sum    — MD5 sidecar (jprm-written)
#   dist/youtarrmetadata_<version>.zip.meta.json — jprm build metadata
#   dist/manifest.json                           — plugin repository manifest (PKG-02)
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
# and Jellyfin's catalog installer will reject the install. If a change is needed,
# re-run this script in full to rebuild the ZIP and regenerate the manifest.
#
# sourceUrl NOTE: PLUGIN_URL below is a PLACEHOLDER GitHub releases URL. No GitHub
# repository is published yet. Update PLUGIN_URL when the repo is live (v2 DIST-01).
#
# This script never runs docker and never escalates privileges. Starting the Docker
# daemon and bringing the harness container up are operator steps (project policy:
# no privilege escalation). The harness install commands are echoed at the end,
# not run.
#
# Usage:
#   ./scripts/package.sh
#
set -euo pipefail

# Resolve the repo root from this script's location so it is runnable from anywhere.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

PLUGIN_DIR="${REPO_ROOT}/Jellyfin.Plugin.Youtarr"
DIST_DIR="${REPO_ROOT}/dist"
VERSION="1.0.0.0"

# Placeholder URL — update when the GitHub repo is published (DIST-01 / v2).
PLUGIN_URL="https://github.com/sandwich/youtarr-jf-plugin/releases/download/v${VERSION}/youtarrmetadata_${VERSION}.zip"

echo "==> Building plugin ZIP with jprm ..."
mkdir -p "${DIST_DIR}"

# jprm plugin build MUST run from the directory containing build.yaml — i.e.
# Jellyfin.Plugin.Youtarr/. Running it from the repo root is a silent no-op
# (exit 0, no ZIP) because jprm searches for build.yaml in the given PATH, not
# recursively. PLUGIN_DIR points at "${REPO_ROOT}/Jellyfin.Plugin.Youtarr".
cd "${PLUGIN_DIR}"  # = ${REPO_ROOT}/Jellyfin.Plugin.Youtarr
jprm plugin build . -o "${DIST_DIR}"

# Resolve the produced ZIP. Fail loudly if the build silently produced nothing.
ZIP="$(ls "${DIST_DIR}"/youtarrmetadata_*.zip 2>/dev/null | sort -V | tail -1 || true)"
if [[ -z "${ZIP}" || ! -f "${ZIP}" ]]; then
  echo "ERROR: jprm produced no ZIP in ${DIST_DIR}." >&2
  echo "       Confirm 'jprm plugin build' ran from ${PLUGIN_DIR} (where build.yaml lives)." >&2
  exit 1
fi
echo "==> Built: ${ZIP}"

# Generate / update the repository manifest. 'jprm repo init' errors if the file
# already exists, so guard it; 'jprm repo add' is idempotent (updates in place).
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
echo "To verify install in the Docker harness (operator runs these — NOT this script):"
echo "    mkdir -p ${REPO_ROOT}/test/jellyfin-load-test/plugins/YoutarrMetadata_${VERSION}"
echo "    unzip -o ${ZIP} -d ${REPO_ROOT}/test/jellyfin-load-test/plugins/YoutarrMetadata_${VERSION}/"
echo "    docker restart jellyfin-plugin-test"
