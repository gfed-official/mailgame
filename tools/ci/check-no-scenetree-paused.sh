#!/usr/bin/env bash
# AC-3 (story 004): the Sim is paused by its own tick gate, never SceneTree.Paused.
# Greps src/PerformativeMail.Sim and src/Game/Hosting (those that exist) and fails on a match.
# grep exit codes: 0 = match = fail, 1 = clean, other = error (must not read as "clean").
#
# Limitation (review-enforced): the regex misses a pause through a variable, e.g.
# `var tree = GetTree(); tree.Paused = true;`.
#
# Usage:
#   tools/ci/check-no-scenetree-paused.sh               scan the real directories
#   tools/ci/check-no-scenetree-paused.sh --self-check  prove the regex matches both forms
set -u

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
pattern='SceneTree\.Paused|GetTree\(\)\.Paused'

if [ "${1:-}" = "--self-check" ]; then
  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"' EXIT
  printf 'class A { void F() { GetTree().Paused = true; } }\n' > "$tmp/a.cs"
  printf 'class B { void F() { SceneTree.Paused = true; } }\n' > "$tmp/b.cs"
  printf 'class C { void F() { var x = 1; } }\n' > "$tmp/c.cs"
  ok=1
  grep -qE "$pattern" "$tmp/a.cs" || { echo "self-check: FAIL (GetTree().Paused not matched)"; ok=0; }
  grep -qE "$pattern" "$tmp/b.cs" || { echo "self-check: FAIL (SceneTree.Paused not matched)"; ok=0; }
  grep -qE "$pattern" "$tmp/c.cs" && { echo "self-check: FAIL (clean file matched)"; ok=0; }
  [ "$ok" = 1 ] && echo "self-check: PASS (pattern matches GetTree().Paused and SceneTree.Paused, not clean code)"
  [ "$ok" = 1 ] && exit 0 || exit 1
fi

dirs=()
for d in src/PerformativeMail.Sim src/Game/Hosting; do
  [ -d "$repo_root/$d" ] && dirs+=("$repo_root/$d")
done
if [ "${#dirs[@]}" -eq 0 ]; then
  echo "No Sim / Hosting directories yet; nothing to scan."
  exit 0
fi
rc=0
grep -rnE "$pattern" "${dirs[@]}" || rc=$?
if [ "$rc" -eq 0 ]; then
  echo "::error::SceneTree.Paused found in Sim / Hosting (never pause the Sim via the SceneTree)"
  exit 1
elif [ "$rc" -ne 1 ]; then
  echo "::error::grep failed with exit code $rc"
  exit "$rc"
fi
echo "No SceneTree.Paused in Sim / Hosting."
