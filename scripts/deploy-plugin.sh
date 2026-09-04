#!/usr/bin/env bash
#
# Build and stage the plugin for either the disposable load-test server or a local
# Jellyfin installation. A local .NET 9 SDK is preferred; Docker is the fallback.
#
# Usage:
#   ./scripts/deploy-plugin.sh
#   JELLYFIN_PLUGIN_DIR=/path/to/YoutarrMetadata_1.1.0.0 \
#     JELLYFIN_CONTAINER=jellyfin ./scripts/deploy-plugin.sh
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT_REL="Jellyfin.Plugin.Youtarr"
PROJECT_DIR="${REPO_ROOT}/${PROJECT_REL}"
PUBLISH_REL="dist/Jellyfin.Plugin.Youtarr"
PUBLISH_DIR="${REPO_ROOT}/${PUBLISH_REL}"
PLUGIN_DLL="Jellyfin.Plugin.Youtarr.dll"

read_yaml_value() {
  local key="$1"
  sed -n -E "s/^${key}:[[:space:]]*\"?([^\"]+)\"?[[:space:]]*$/\\1/p" \
    "${PROJECT_DIR}/build.yaml" | head -1
}

PLUGIN_NAME="$(read_yaml_value name)"
PLUGIN_VERSION="$(read_yaml_value version)"
if [[ -z "${PLUGIN_NAME}" || -z "${PLUGIN_VERSION}" ]]; then
  echo "ERROR: could not read plugin name/version from ${PROJECT_DIR}/build.yaml" >&2
  exit 1
fi

DEFAULT_PLUGIN_DIR="${REPO_ROOT}/test/jellyfin-load-test/plugins/${PLUGIN_NAME}_${PLUGIN_VERSION}"
DEST_DIR="${JELLYFIN_PLUGIN_DIR:-${DEFAULT_PLUGIN_DIR}}"

has_dotnet_9_sdk() {
  command -v dotnet >/dev/null 2>&1 \
    && dotnet --list-sdks 2>/dev/null | grep -Eq '^9\.'
}

echo "==> Publishing ${PLUGIN_NAME} ${PLUGIN_VERSION} (Release) ..."
if has_dotnet_9_sdk; then
  dotnet publish "${PROJECT_DIR}" -c Release -o "${PUBLISH_DIR}"
elif command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
  echo "    .NET 9 SDK not found locally; using mcr.microsoft.com/dotnet/sdk:9.0"
  docker run --rm \
    --user "$(id -u):$(id -g)" \
    -e DOTNET_CLI_HOME=/tmp/youtarr-dotnet-home \
    -e NUGET_PACKAGES=/tmp/youtarr-nuget-packages \
    -v "${REPO_ROOT}:/src" \
    -w /src \
    mcr.microsoft.com/dotnet/sdk:9.0 \
    bash -lc \
    "dotnet publish '${PROJECT_REL}' -c Release -o '${PUBLISH_REL}' && dotnet clean '${PROJECT_REL}' -c Release"
else
  echo "ERROR: building requires either the .NET 9 SDK or a running Docker daemon." >&2
  exit 1
fi

if [[ ! -f "${PUBLISH_DIR}/${PLUGIN_DLL}" ]]; then
  echo "ERROR: expected ${PUBLISH_DIR}/${PLUGIN_DLL}, but it is missing." >&2
  exit 1
fi

echo "==> Staging plugin into ${DEST_DIR} ..."
mkdir -p "${DEST_DIR}"
cp -f "${PUBLISH_DIR}/${PLUGIN_DLL}" "${DEST_DIR}/${PLUGIN_DLL}"

echo "==> Staged ${DEST_DIR}/${PLUGIN_DLL}"

if [[ -n "${JELLYFIN_CONTAINER:-}" ]]; then
  echo "==> Restarting Jellyfin container ${JELLYFIN_CONTAINER} ..."
  docker restart "${JELLYFIN_CONTAINER}" >/dev/null
  echo "==> Restarted ${JELLYFIN_CONTAINER}"
else
  echo "Next: restart Jellyfin so it loads the new DLL."
fi
