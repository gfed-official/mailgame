#!/usr/bin/env bash
# Builds the solution and runs the Godot headless smoke (spawn -> pickup -> deliver -> wallet).
# Exits 0 only if the game printed "SMOKE OK".
set -euo pipefail
cd "$(dirname "$0")/.."

DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
GODOT="${GODOT:-$HOME/.local/bin/godot}"
SEED="${1:-1}"

"$DOTNET" build PerformativeMail.sln -nodeReuse:false -v quiet -nologo
log="$(mktemp)"
trap 'rm -f "$log"' EXIT
set +e
timeout 300 "$GODOT" --headless --path . -- --smoke "--seed=$SEED" 2>&1 | tee "$log"
status=${PIPESTATUS[0]}
set -e
if [ "$status" -ne 0 ]; then
    echo "smoke: godot exited with $status" >&2
    exit "$status"
fi
grep -q "^SMOKE OK$" "$log" || { echo "smoke: no SMOKE OK line" >&2; exit 1; }
