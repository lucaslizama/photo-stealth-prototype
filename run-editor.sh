#!/usr/bin/env bash
# Launch the Godot editor for this project with Godot-MCP in LOCAL (Custom) mode.
#
# Why this script exists:
#   1. This machine has TWO Godot 4.7.1 binaries on PATH. `godot` is the
#      GDScript-only build and CANNOT compile the Godot-MCP addon. We must use
#      `godot-mono`. godot-cli only prefers the mono build on Windows, so on
#      Linux it would pick the wrong one — GODOT_BIN settles it.
#   2. Godot-MCP defaults to Cloud mode (ai-game.dev). We deliberately run
#      fully local: no account, no project data leaving this machine.
set -euo pipefail

cd "$(dirname "$0")"

export GODOT_BIN="${GODOT_BIN:-/usr/bin/godot-mono}"
export GODOT_MCP_CONNECTION_MODE=Custom
export GODOT_MCP_HOST="${GODOT_MCP_HOST:-http://localhost:8080}"

if [[ ! -x "$GODOT_BIN" ]]; then
  echo "error: GODOT_BIN=$GODOT_BIN is not executable" >&2
  exit 1
fi

if ! "$GODOT_BIN" --version 2>/dev/null | grep -q '\.mono\.'; then
  echo "error: $GODOT_BIN is not the mono (C#/.NET) build — the addon will not compile" >&2
  exit 1
fi

# Build first so the addon assembly exists on the editor's first load.
dotnet build

echo "Launching Godot editor (mode=Custom host=$GODOT_MCP_HOST)"
exec "$GODOT_BIN" --editor --path . "$@"
