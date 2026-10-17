#!/usr/bin/env bash
# Usage: ./run_probe.sh <probe-id> [--windowed] [extra godot args, e.g. --display-driver x11]
# Runs one probe (headless unless --windowed). Probe writes logs/<id>.log itself;
# the engine's raw console output (stdout+stderr) is saved to logs/<id>.engine.txt.
set -u
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
id="$1"; shift
mode="--headless"
extra=()
for a in "$@"; do if [ "$a" = "--windowed" ]; then mode=""; else extra+=("$a"); fi; done
cd "$here"
mise exec dotnet@10.0.402 -- bash -c "timeout 120 ~/.local/bin/godot-mono $mode --path . ${extra[*]:-} --quit-after 100000 -- --probe=$id" > "logs/$id.engine.txt" 2>&1
echo "exit=$? (log: logs/$id.log, engine console: logs/$id.engine.txt)"
