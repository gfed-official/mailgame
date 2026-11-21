#!/usr/bin/env bash
# What CI runs (doc 07 §10), in order, on the box and in .github/workflows/ci.yml alike:
#   content  the validator over content/ (doc 07 §6), and that it still turns broken content away
#   test     the Sim and Net unit tests
#   smoke    tools/smoke.sh: the headless game, two runs, the island bot (needs Godot)
# Exits non-zero at the first stage that fails.
#   tools/ci.sh [content] [test] [smoke]      (no stage named: all three)
set -euo pipefail
cd "$(dirname "$0")/.."

if [ -z "${DOTNET:-}" ]; then
    if [ -x "$HOME/.dotnet/dotnet" ]; then DOTNET="$HOME/.dotnet/dotnet"; else DOTNET="$(command -v dotnet)"; fi
fi
export DOTNET
export DOTNET_ROOT="${DOTNET_ROOT:-$(dirname "$(readlink -f "$DOTNET")")}"
export PATH="$DOTNET_ROOT:$PATH"
# Stale MSBuild nodes of other builds can hang a build here (and nothing is gained by them in CI).
export MSBUILDDISABLENODEREUSE=1

stages=("$@")
[ ${#stages[@]} -eq 0 ] && stages=(content test smoke)

stage_content() {
    echo "== content"
    tools/validate-content.sh content
    # The validator must also fail: a copy of the content with a recipe for an item nobody defines.
    local broken
    broken="$(mktemp -d)"
    cp -r content/. "$broken"
    sed -i 's/"item": "log"/"item": "no_such_item"/' "$broken/recipes/recipes.json"
    if tools/validate-content.sh "$broken" --quiet > "$broken.out" 2>&1; then
        rm -rf "$broken" "$broken.out"
        echo "ci: the validator passed content with an unknown item in a recipe" >&2
        return 1
    fi
    grep -q "no_such_item" "$broken.out" || { cat "$broken.out"; rm -rf "$broken" "$broken.out"; echo "ci: the validator failed without naming the bad item" >&2; return 1; }
    rm -rf "$broken" "$broken.out"
    echo "content OK"
}

stage_test() {
    echo "== test"
    "$DOTNET" test tests/Sim.Tests -nodeReuse:false -v quiet -nologo
    "$DOTNET" test tests/Net.Tests -nodeReuse:false -v quiet -nologo
    echo "test OK"
}

stage_smoke() {
    echo "== smoke"
    tools/smoke.sh
}

for stage in "${stages[@]}"; do
    case "$stage" in
        content|test|smoke) "stage_$stage" ;;
        *) echo "ci: unknown stage '$stage' (content, test, smoke)" >&2; exit 2 ;;
    esac
done
echo "CI OK"
