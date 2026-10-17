#!/usr/bin/env bash
# Build once, then run every headless probe. Windowed probes (S-34) are run separately:
#   ./run_probe.sh window --windowed --display-driver wayland   (then rename logs/window.* -> logs/window-wayland.*)
#   ./run_probe.sh window --windowed --display-driver x11       (then rename logs/window.* -> logs/window-x11.*)
set -u
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$here"
mise exec dotnet@10.0.402 -- bash -c '~/.local/bin/godot-mono --headless --path . --build-solutions --quit' > logs/build.engine.txt 2>&1
echo "build exit=$?"
for p in baseline order devices alloc exitleak focus; do ./run_probe.sh "$p"; done
./run_b3_set.sh > logs/b3.engine.txt 2>&1; echo "b3 exit=$?"
