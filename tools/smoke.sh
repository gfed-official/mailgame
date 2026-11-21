#!/usr/bin/env bash
# Builds the solution and runs the Godot headless smoke twice: the full run (spawn -> pickup ->
# deliver -> wallet -> ... -> results), then the raid playtest (--shift=2 --no-quota --raid
# --playtest-package).
# Exits 0 only if the game printed "SMOKE OK" both times and leaked nothing on its way out, and then
# a bot played the generated Small Island town (doc 02) of the same seed to "BOT OK".
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

# run_smoke <log> <godot user args...>: one headless run that must exit 0, print "SMOKE OK" and
# leave nothing behind at exit (nit 16).
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
    if grep -q "leaked at exit" "$log"; then
        echo "smoke: objects leaked at exit ($*)" >&2
        exit 1
    fi
}

run_smoke "$work/log" "--profile=$profile"
grep -q '"rankXp": [1-9]' "$profile" || { echo "smoke: no XP in the saved profile $profile" >&2; exit 1; }

# The raid playtest (nits 13 and 18): the run starts at shift 2, its raid comes at once and is
# fought through the real HUD, with the packages --playtest-package hands out and the hammer,
# and the missed quota does not end the run.
run_smoke "$work/log-playtest" "--profile=$work/profile-playtest.json" --shift=2 --no-quota --raid --playtest-package
grep -q "^\[smoke\] playtest: run started at shift 2" "$work/log-playtest" || { echo "smoke: the raid playtest did not run" >&2; exit 1; }
# Between them the two runs take a card of the Draft both ways: by its key, and with a click on it.
grep -q "^\[smoke\] draft: .* a card taken by its key" "$work/log" || { echo "smoke: no card of the Draft was taken by its key" >&2; exit 1; }
grep -q "^\[smoke\] playtest: .* a card of the Draft taken with a click" "$work/log-playtest" || { echo "smoke: no card of the Draft was taken with a click" >&2; exit 1; }
# Build mode (doc 09 §2.7): the full run builds by hand with what the land gave.
grep -q "^\[smoke\] build: a Wooden Wall and a Chest built" "$work/log" || { echo "smoke: nothing was built in Build mode" >&2; exit 1; }
# The starting kit (doc 03 §4.4): the Land kit in the pockets, the team's in the chest and by the counter.
grep -q "^\[smoke\] kit: the Land kit" "$work/log" || { echo "smoke: the starting kit was not handed out" >&2; exit 1; }
# The host's world in the Lobby (doc 06 §5): over to the seed's island, a new seed, and back.
grep -q "^\[smoke\] world: Test Street, then Small Island" "$work/log" || { echo "smoke: the host did not switch the world in the Lobby" >&2; exit 1; }
# The map (doc 09 §2.6): up with mail in the hotbar, the route to its house drawn.
grep -q "^\[smoke\] map: up with mail" "$work/log" || { echo "smoke: the map was not shown with a route" >&2; exit 1; }
# The shop's first blueprint (doc 10 §3 bp_defense): bought in shift 2 and built with.
grep -q "^\[smoke\] fortify: Blueprint: Fortify bought" "$work/log" || { echo "smoke: the Fortify blueprint was not bought and built with" >&2; exit 1; }
grep -q "^\[smoke\] fortify: .*, a Stone Wall (800 HP" "$work/log" || { echo "smoke: no Stone Wall was built with the Fortify blueprint" >&2; exit 1; }
# Healing (doc 05 §1.1): a Bandage at once, Berries over their seconds, the health bar following.
grep -q "^\[smoke\] heal: a Bandage gave" "$work/log" || { echo "smoke: healing with a Bandage and Berries was not checked" >&2; exit 1; }
# The Gate and the Alarm Post (doc 05 §4): built, walked through, and ringing in the shift's raid.
grep -q "^\[smoke\] defences: a Gate built" "$work/log" || { echo "smoke: no Gate and Alarm Post were built" >&2; exit 1; }
# The bike (doc 04 §3.1): the Land kit's (the card sells a lone carrier no second), got on through its prompt,
# ridden at road speed, its rack opened, got off.
grep -q "^\[smoke\] bike: the Land kit's, ridden" "$work/log" || { echo "smoke: the kit's bike was not ridden" >&2; exit 1; }

# The generated Small Island town (doc 02): twenty seeds make valid towns (SeedViewer), and a bot
# plays the smoke seed's town for 25 s, the client building it from the seed and checking its hash.
"$DOTNET" tools/SeedViewer/bin/Debug/net8.0/SeedViewer.dll 1-20 > "$work/seeds" || { cat "$work/seeds"; echo "smoke: a seed made no valid town" >&2; exit 1; }
set +e
timeout 120 "$GODOT" --headless --path . -- --bot --solo --world=island "--seed=$SEED" --quit-after=25 "--profile=$work/profile-island.json" 2>&1 | tee "$work/log-island"
status=${PIPESTATUS[0]}
set -e
[ "$status" -eq 0 ] || { echo "smoke: the bot on the generated town exited with $status" >&2; exit "$status"; }
grep -q "^BOT OK$" "$work/log-island" || { echo "smoke: the bot on the generated town did not finish OK" >&2; exit 1; }
grep -q "on Small Island #$SEED" "$work/log-island" || { echo "smoke: the bot did not play on the generated town" >&2; exit 1; }
grep -q "leaked at exit" "$work/log-island" && { echo "smoke: objects leaked at exit (island bot)" >&2; exit 1; }
echo "island OK"
