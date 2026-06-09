#!/usr/bin/env bash
#
# deploy-plugin.sh — dev iteration loop for the Youtarr Jellyfin plugin.
#
# Publishes the plugin in Release mode and stages the single plugin DLL into the
# load-test harness plugins directory under the versioned folder Jellyfin expects:
#
#     test/jellyfin-load-test/plugins/YoutarrMetadata_1.0.0.0/Jellyfin.Plugin.Youtarr.dll
#
# The folder name MUST be "YoutarrMetadata_1.0.0.0" — <PluginName>_<version> from
# build.yaml (name: YoutarrMetadata, version: 1.0.0.0). Drift here = plugin not found.
#
# This script does NOT run docker or sudo. Starting the Docker daemon and bringing
# the Jellyfin container up are operator steps documented in
# test/jellyfin-load-test/README.md (project policy: never escalate via sudo).
#
# Usage:
#     ./scripts/deploy-plugin.sh
#
# After running, restart the container to pick up the new DLL:
#     docker restart jellyfin-plugin-test
#
set -euo pipefail

# Resolve the repo root from this script's location so the script is runnable
# from any working directory.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

PROJECT_DIR="${REPO_ROOT}/Jellyfin.Plugin.Youtarr"
PUBLISH_DIR="${REPO_ROOT}/dist/Jellyfin.Plugin.Youtarr"
PLUGIN_DLL="Jellyfin.Plugin.Youtarr.dll"
PLUGIN_FOLDER="YoutarrMetadata_1.0.0.0"
DEST_DIR="${REPO_ROOT}/test/jellyfin-load-test/plugins/${PLUGIN_FOLDER}"

echo "==> Publishing plugin (Release) ..."
dotnet publish "${PROJECT_DIR}" -c Release -o "${PUBLISH_DIR}"

if [[ ! -f "${PUBLISH_DIR}/${PLUGIN_DLL}" ]]; then
  echo "ERROR: expected ${PUBLISH_DIR}/${PLUGIN_DLL} after publish, but it is missing." >&2
  exit 1
fi

echo "==> Staging DLL into ${DEST_DIR} ..."
mkdir -p "${DEST_DIR}"
cp -f "${PUBLISH_DIR}/${PLUGIN_DLL}" "${DEST_DIR}/${PLUGIN_DLL}"

echo "==> Done. Staged:"
echo "    ${DEST_DIR}/${PLUGIN_DLL}"
echo
echo "Next: restart the Jellyfin test container to load the new DLL:"
echo "    docker restart jellyfin-plugin-test"
