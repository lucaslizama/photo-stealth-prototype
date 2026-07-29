#!/usr/bin/env bash
# Export release PC builds (Linux + Windows, x86_64) into build/.
#
# Presets live in export_presets.cfg, which is committed so these builds are
# reproducible. Requires the mono export templates for this exact engine version
# in ~/.local/share/godot/export_templates/<version>.mono/.
set -euo pipefail

cd "$(dirname "$0")"

export GODOT_BIN="${GODOT_BIN:-/usr/bin/godot-mono}"

if ! "$GODOT_BIN" --version 2>/dev/null | grep -q '\.mono\.'; then
  echo "error: $GODOT_BIN is not the mono (C#/.NET) build" >&2
  exit 1
fi

# The export spawns its own engine instance that writes into .godot/mono/temp.
# An editor building into the same place at the same time corrupts the output.
if pgrep -f "godot-mono --editor --path \\." > /dev/null; then
  echo "warning: the Godot editor is open on this project." >&2
  echo "         Close it first — concurrent builds share .godot/mono/temp." >&2
  exit 1
fi

version="$("$GODOT_BIN" --version | head -1)"
echo "Engine: $version"

for preset in Linux Windows; do
  echo
  echo "=== exporting $preset ==="
  # Godot creates the file but not missing parent directories.
  case "$preset" in
    Linux)   mkdir -p build/linux ;;
    Windows) mkdir -p build/windows ;;
  esac

  "$GODOT_BIN" --headless --path . --export-release "$preset"
  echo "$preset: ok"
done

echo
echo "=== output ==="
du -sh build/* 2>/dev/null
echo
echo "Note: the godot_mcp addon is excluded from exports (see PhotoStealthPrototype.csproj)."
