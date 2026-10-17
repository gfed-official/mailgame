#!/usr/bin/env bash
# B3 "can be set true in project.godot": run probe b3 from a temp copy whose project.godot sets the key.
set -eu
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
tmp=/tmp/spike-oq-ic-9-b3
rm -rf "$tmp"; mkdir -p "$tmp"
cp -r "$here"/. "$tmp"/
printf '\n[input_devices]\n\njoypads/ignore_joypad_on_unfocused_application=true\n' >> "$tmp/project.godot"
cd "$tmp"
mise exec dotnet@10.0.402 -- bash -c "timeout 120 ~/.local/bin/godot-mono --headless --path . --quit-after 100000 -- --probe=b3"
cp "$tmp/logs/b3.log" "$here/logs/b3.log"
