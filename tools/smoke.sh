#!/usr/bin/env bash
# Builds the solution and runs the Godot headless smoke (spawn -> pickup -> deliver -> wallet).
# Exits 0 only if the game printed "SMOKE OK".
set -euo pipefail
cd "$(dirname "$0")/.."

DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
GODOT="${GODOT:-$HOME/.local/bin/godot}"
SEED="${1:-1}"

# Godot finds the .NET runtime through `dotnet` on PATH or DOTNET_ROOT; make sure it can.
export DOTNET_ROOT="${DOTNET_ROOT:-$(dirname "$(readlink -f "$DOTNET")")}"
export PATH="$DOTNET_ROOT:$PATH"

"$DOTNET" build PerformativeMail.sln -nodeReuse:false -v quiet -nologo
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
log="$work/log"
# A profile file of its own: the smoke checks that the run's XP is saved, never the player's real profile.
profile="$work/profile.json"
set +e
timeout 300 "$GODOT" --headless --path . -- --smoke "--seed=$SEED" "--profile=$profile" 2>&1 | tee "$log"
status=${PIPESTATUS[0]}
set -e
if [ "$status" -ne 0 ]; then
    echo "smoke: godot exited with $status" >&2
    exit "$status"
fi
grep -q "^SMOKE OK$" "$log" || { echo "smoke: no SMOKE OK line" >&2; exit 1; }
grep -q '"rankXp": [1-9]' "$profile" || { echo "smoke: no XP in the saved profile $profile" >&2; exit 1; }
