#!/usr/bin/env bash
# Download (once) and run the local GameDev-MCP-Server that Godot-MCP talks to.
#
# The Godot-MCP addon dock can do this for you (Server card -> "Start Server"),
# but this script does the same thing headlessly so the server can be started
# from a terminal, CI, or before the editor is up.
#
# SERVER_VERSION must match the addon's pinned `ServerVersion` constant in
# addons/godot_mcp/Runtime/Connection/GodotMcpServerView.cs — the plugin and the
# server it talks to must never drift.
set -euo pipefail

cd "$(dirname "$0")"

PINNED_FROM="addons/godot_mcp/Runtime/Connection/GodotMcpServerView.cs"
SERVER_VERSION="$(grep -oP 'ServerVersion\s*=\s*"\K[^"]+' "$PINNED_FROM")"
RID=linux-x64
PORT="${PORT:-8080}"
DEST=".godot/mcp-server/$RID"
BIN="$DEST/gamedev-mcp-server"
BASE="https://github.com/IvanMurzak/GameDev-MCP-Server/releases/download/v$SERVER_VERSION"

if [[ ! -x "$BIN" || "$(cat "$DEST/.version" 2>/dev/null)" != "$SERVER_VERSION" ]]; then
  echo "Downloading gamedev-mcp-server v$SERVER_VERSION ($RID)..."
  rm -rf "$DEST"
  mkdir -p "$DEST"
  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"' EXIT

  curl -fsSL "$BASE/gamedev-mcp-server-$RID.zip" -o "$tmp/server.zip"
  curl -fsSL "$BASE/SHA256SUMS" -o "$tmp/SHA256SUMS"

  # Verify the zip against the published checksum before executing anything.
  expected="$(grep "gamedev-mcp-server-$RID.zip" "$tmp/SHA256SUMS" | awk '{print $1}')"
  actual="$(sha256sum "$tmp/server.zip" | awk '{print $1}')"
  if [[ -z "$expected" || "$expected" != "$actual" ]]; then
    echo "error: checksum mismatch for gamedev-mcp-server-$RID.zip" >&2
    echo "  expected: ${expected:-<not found in SHA256SUMS>}" >&2
    echo "  actual:   $actual" >&2
    exit 1
  fi

  # The release zip wraps everything in a top-level <rid>/ directory; flatten it
  # so the binary lands at $BIN rather than $DEST/<rid>/gamedev-mcp-server.
  unzip -q "$tmp/server.zip" -d "$tmp/x"
  if [[ -d "$tmp/x/$RID" ]]; then
    mv "$tmp/x/$RID"/* "$DEST/"
  else
    mv "$tmp/x"/* "$DEST/"
  fi

  if [[ ! -f "$BIN" ]]; then
    echo "error: gamedev-mcp-server binary not found in the release zip" >&2
    exit 1
  fi
  chmod +x "$BIN"
  echo "$SERVER_VERSION" > "$DEST/.version"
fi

echo "Starting gamedev-mcp-server v$SERVER_VERSION on http://localhost:$PORT/mcp"
exec "$BIN" --client-transport streamableHttp --port "$PORT"
