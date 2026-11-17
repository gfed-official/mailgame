#!/usr/bin/env bash
# Builds the solution and runs the Godot headless smoke twice: the full run (spawn -> pickup ->
# deliver -> wallet -> ... -> results), then the raid playtest (--shift=2 --no-quota --raid).
# Exits 0 only if the game printed "SMOKE OK" both times.
set -euo pipefail
cd "$(dirname "$0")/.."

DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
GODOT="${GODOT:-$HOME/.local/bin/godot}"
SEED="${1:-1}"

# Godot finds the .NET runtime through `dotnet` on PATH or DOTNET_ROOT; make sure it can.
export DOTNET_ROOT="${DOTNET_ROOT:-$(dirname "$(readlink -f "$DOTNET")")}"
export PATH="$DOTNET_ROOT:$PATH"

"$DOTNET" build PerformativeMail.sln -nodeReuse:false -v quiet -nologo
# The content the game is about to boot with must pass the validator CI runs (doc 07 §6).
"$DOTNET" tools/ContentValidator/bin/Debug/net8.0/ContentValidator.dll content
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
# A profile file of its own: the smoke checks that the run's XP is saved, never the player's real profile.
profile="$work/profile.json"

# run_smoke <log> <godot user args...>: one headless run that must exit 0 and print "SMOKE OK".
run_smoke() {
    local log="$1" status
    shift
    set +e
    timeout 300 "$GODOT" --headless --path . -- --smoke "--seed=$SEED" "$@" 2>&1 | tee "$log"
    status=${PIPESTATUS[0]}
    set -e
    if [ "$status" -ne 0 ]; then
        echo "smoke: godot exited with $status ($*)" >&2
        exit "$status"
    fi
    grep -q "^SMOKE OK$" "$log" || { echo "smoke: no SMOKE OK line ($*)" >&2; exit 1; }
}

run_smoke "$work/log" "--profile=$profile"
grep -q '"rankXp": [1-9]' "$profile" || { echo "smoke: no XP in the saved profile $profile" >&2; exit 1; }

# The raid playtest (nit 13): the run starts at shift 2, its raid comes at once and is fought
# through the real HUD, and the missed quota does not end the run.
run_smoke "$work/log-playtest" "--profile=$work/profile-playtest.json" --shift=2 --no-quota --raid
grep -q "^\[smoke\] playtest: run started at shift 2" "$work/log-playtest" || { echo "smoke: the raid playtest did not run" >&2; exit 1; }
