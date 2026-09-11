#!/usr/bin/env bash
#
# Cloud Agent install: prepare this repository for building and testing on
# Linux.
#
# What can be built here, and what cannot:
#   - src/GitApp.Core        (net10.0)  builds and is fully testable on Linux.
#   - tests/GitApp.Core.Tests (net10.0) runs here; it is the whole test suite.
#   - src/GitApp             is the .NET MAUI app and targets only
#                            net10.0-windows10.0.19041.0 and net10.0-maccatalyst.
#                            It cannot be built on Linux by design, so it is
#                            excluded from the Linux build below. Build and run
#                            it on Windows or macOS as AGENTS.md describes.
#
# This script is idempotent: it may run repeatedly against cached state.

set -euo pipefail

DOTNET_CHANNEL="10.0"
DOTNET_INSTALL_DIR="/usr/local/dotnet"

# 1. Install the .NET SDK to a fixed system location, if it is not already the
#    channel we want. dotnet-install.sh is itself idempotent and skips the
#    download when the requested version is already present.
if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  echo "Installing .NET SDK ${DOTNET_CHANNEL} into ${DOTNET_INSTALL_DIR}..."
  tmp_script="$(mktemp)"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$tmp_script"
  sudo bash "$tmp_script" --channel "$DOTNET_CHANNEL" --install-dir "$DOTNET_INSTALL_DIR"
  rm -f "$tmp_script"
else
  echo ".NET SDK 10 already present; skipping download."
fi

# 2. Put dotnet on PATH for every shell.
#    - A symlink in /usr/local/bin covers non-login shells (the agent's shell
#      tool runs commands without sourcing profile scripts).
#    - A profile.d entry covers interactive login shells.
sudo ln -sf "${DOTNET_INSTALL_DIR}/dotnet" /usr/local/bin/dotnet
sudo tee /etc/profile.d/dotnet.sh >/dev/null <<EOF
export DOTNET_ROOT="${DOTNET_INSTALL_DIR}"
export PATH="\$PATH:${DOTNET_INSTALL_DIR}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
EOF

export DOTNET_ROOT="${DOTNET_INSTALL_DIR}"
export PATH="${DOTNET_INSTALL_DIR}:${PATH}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

echo "Using $(dotnet --version) from $(command -v dotnet)"

# 3. Restore and build the buildable, testable part of the tree. Warnings are
#    treated as errors in this project, so a clean build here is meaningful.
dotnet build src/GitApp.Core/GitApp.Core.csproj -c Debug
dotnet build tests/GitApp.Core.Tests/GitApp.Core.Tests.csproj -c Debug

echo "Install complete."
