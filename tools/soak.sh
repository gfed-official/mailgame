#!/usr/bin/env bash
# Multi-process soak over real ENet sockets: one headless host and N-1 headless guests, each played
# by a DeliveryBot, for SECONDS of real time. Every client checks every container version against
# the server's hash. Exits 0 only if every process ended with "BOT OK".
#   tools/soak.sh [players=8] [seconds=600] [seed=1]
set -euo pipefail
cd "$(dirname "$0")/.."

DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
GODOT="${GODOT:-$HOME/.local/bin/godot}"
PLAYERS="${1:-8}"
SECONDS_TO_RUN="${2:-600}"
SEED="${3:-1}"
PORT="${PORT:-$((20000 + RANDOM % 20000))}"

export DOTNET_ROOT="${DOTNET_ROOT:-$(dirname "$(readlink -f "$DOTNET")")}"
export PATH="$DOTNET_ROOT:$PATH"

"$DOTNET" build PerformativeMail.sln -nodeReuse:false -v quiet -nologo
work="$(mktemp -d)"
trap 'kill $(jobs -p) 2>/dev/null || true; rm -rf "$work"' EXIT

# The host outlives the guests by a few seconds, so none of them ends on "Host lost".
"$GODOT" --headless --path . -- --bot "--seed=$SEED" "--port=$PORT" --name=Host "--wait-players=$PLAYERS" \
    "--quit-after=$((SECONDS_TO_RUN + 5))" >"$work/host.log" 2>&1 &
pids=($!)
sleep 2
for i in $(seq 2 "$PLAYERS"); do
    "$GODOT" --headless --path . -- --bot "--join=127.0.0.1:$PORT" "--name=Bot$i" \
        "--quit-after=$SECONDS_TO_RUN" >"$work/bot$i.log" 2>&1 &
    pids+=($!)
done

failed=0
for pid in "${pids[@]}"; do
    wait "$pid" || failed=1
done
for log in "$work"/*.log; do
    name="$(basename "$log" .log)"
    grep -E "^\[bot\]" "$log" | tail -n 1 | sed "s/^/$name: /" || true
    if ! grep -q "^BOT OK$" "$log"; then
        failed=1
        echo "soak: $name did not end with BOT OK" >&2
        grep -E "BOT FAIL|ERROR|\[boot\]" "$log" | tail -n 5 >&2 || true
    fi
done
[ "$failed" -eq 0 ] && echo "SOAK OK: $PLAYERS players, $SECONDS_TO_RUN s" || { echo "SOAK FAIL" >&2; exit 1; }
