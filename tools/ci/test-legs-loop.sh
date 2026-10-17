#!/usr/bin/env bash
# Flakiness gate: runs the Sim test suite on all four legs (net10.0 / net8.0 x Debug / Release)
# N times each and fails if any run fails. Each leg is built once, then run with --no-build.
# Introduced for TD-026 (flaky zero-allocation tests); reusable for any suspected flake.
#
# Usage:  tools/ci/test-legs-loop.sh [runs-per-leg]   (default 5)
#   DOTNET="mise exec dotnet@10.0.402 -- dotnet" tools/ci/test-legs-loop.sh 5
# Logs: $LOG_DIR (default: a fresh mktemp dir), one file per run; failing test names are printed.
set -u

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
project="$repo_root/tests/PerformativeMail.Sim.Tests"
runs="${1:-5}"
DOTNET="${DOTNET:-dotnet}"
LOG_DIR="${LOG_DIR:-$(mktemp -d)}"

legs=("net10.0 Debug" "net10.0 Release" "net8.0 Debug" "net8.0 Release")
total=0
failed=0

for leg in "${legs[@]}"; do
    read -r tfm config <<< "$leg"
    tag="${tfm}-${config}"
    if ! $DOTNET build "$project" -c "$config" -p:PmTestTfm="$tfm" -v q -nologo > "$LOG_DIR/build-$tag.log" 2>&1; then
        echo "BUILD FAILED: $tag (see $LOG_DIR/build-$tag.log)"
        tail -20 "$LOG_DIR/build-$tag.log"
        exit 2
    fi

    for ((i = 1; i <= runs; i++)); do
        log="$LOG_DIR/run-$tag-$i.log"
        total=$((total + 1))
        if $DOTNET test "$project" -c "$config" -p:PmTestTfm="$tfm" --no-build > "$log" 2>&1; then
            summary="$(grep -E 'Passed!|Failed!|Total tests|total:|succeeded:|failed:' "$log" | tail -3 | tr '\n' ' ')"
            echo "PASS  $tag run $i  $summary"
        else
            failed=$((failed + 1))
            echo "FAIL  $tag run $i  (log: $log)"
            grep -E '^\s*(\[FAIL\]|Failed )|failed [A-Za-z_.]+' "$log" | head -10
        fi
    done
done

echo "----"
echo "runs: $total  failed: $failed  logs: $LOG_DIR"
[ "$failed" -eq 0 ]
