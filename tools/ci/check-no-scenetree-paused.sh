#!/usr/bin/env bash
# AC-3 (story 004): the Sim is paused by its own tick gate, never SceneTree.Paused.
# Scans src/PerformativeMail.Sim and src/Game/ EXCLUDING src/Game/Smoke/ (those that exist) and
# fails on a match. src/Game/Smoke/ is excluded because the godot-host-shell/003
# `pause-driver-state` smoke scenario must set GetTree().Paused = true to prove that a tree pause
# never stops the Sim (TR-input-014); no shipping code lives there.
# Exit codes: 0 = clean, 1 = match, other = error (an error must not read as "clean").
#
# Matches inside C# comments are skipped (the stripper is tools/ci/lib/strip-cs-comments.awk,
# shared with check-sim-access.sh), so documentation may name the API: `//` and `///`
# lines, a trailing `// ...` after code, and `/* ... */` blocks (also across lines, so `*`
# continuation lines are skipped). Code before a comment or after a closed block on the same
# line is still scanned. `//` and `/*` inside plain "..." / @"..." strings and '...' chars do
# not start a comment; a match inside such a string literal still counts.
#
# Limitation (review-enforced): the regex misses a pause through a variable, e.g.
# `var tree = GetTree(); tree.Paused = true;`.
# Limitation (review-enforced, TD-103): raw """...""" strings and string literals inside $"{...}"
# interpolation holes are not lexed, so a `//` or `/*` in them can open a fake comment and hide
# real code (to the end of the line, or for `/*` until a later `*/`) — a missed violation.
#
# Usage:
#   tools/ci/check-no-scenetree-paused.sh               scan the real directories
#   tools/ci/check-no-scenetree-paused.sh --self-check  prove the scan catches code, skips comments
#                                                       and skips src/Game/Smoke/ only
set -u

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
strip_lib="$script_dir/lib/strip-cs-comments.awk"
[ -r "$strip_lib" ] || { echo "::error::missing $strip_lib" >&2; exit 2; }
export PAUSE_PATTERN='SceneTree\.Paused|GetTree\(\)\.Paused'

# Prints the scan targets under <root>, one per line: src/PerformativeMail.Sim, and every entry
# of src/Game/ except src/Game/Smoke/ (those that exist). Used by the real scan and the self-check.
scan_targets() {
  local root="$1" e
  [ -d "$root/src/PerformativeMail.Sim" ] && printf '%s\n' "$root/src/PerformativeMail.Sim"
  if [ -d "$root/src/Game" ]; then
    for e in "$root"/src/Game/* "$root"/src/Game/.[!.]*; do
      [ -e "$e" ] || continue
      [ "$e" = "$root/src/Game/Smoke" ] && continue
      printf '%s\n' "$e"
    done
  fi
  return 0
}

# Prints offending lines (file:line:text). Returns 0 = match, 1 = clean, other = error.
# grep -l picks the candidate files; awk drops comments from each line and re-tests the code.
scan() {
  local rc=0 out
  out="$(grep -rlE "$PAUSE_PATTERN" "$@")" || rc=$?
  [ "$rc" -eq 1 ] && return 1
  [ "$rc" -ne 0 ] && { echo "::error::grep failed with exit code $rc" >&2; return "$rc"; }
  local files=()
  mapfile -t files <<< "$out"
  rc=0
  out="$(awk -f "$strip_lib" -f <(cat <<'AWK'
    FNR == 1 { seen++; inblock = 0; verbatim = 0 }
    { code = strip($0); if (code ~ ENVIRON["PAUSE_PATTERN"]) { print FILENAME ":" FNR ":" $0; found = 1 } }
    # Every candidate matched grep, so it is non-empty; an unseen file means awk could not read it.
    # The verdict is a printed last line, so an awk crash (any exit code) cannot read as "clean".
    END { if (seen == ARGC - 1) print (found ? "scan-result:match" : "scan-result:clean") }
AWK
  ) "${files[@]}")" || rc=$?
  local verdict="${out##*$'\n'}"
  [ "$out" != "$verdict" ] && printf '%s\n' "${out%$'\n'*}"
  if [ "$rc" -ne 0 ]; then echo "::error::awk failed with exit code $rc" >&2; return 2; fi
  case "$verdict" in
    scan-result:match) return 0 ;;
    scan-result:clean) return 1 ;;
    *) echo "::error::awk gave no verdict (unreadable file?)" >&2; return 2 ;;
  esac
}

if [ "${1:-}" = "--self-check" ]; then
  tmp="$(mktemp -d)" || exit 2
  trap 'rm -rf "$tmp"' EXIT
  # Must be caught: real code, including code with a trailing comment or after a closed block.
  printf 'class A { void F() { GetTree().Paused = true; } }\n' > "$tmp/hit-gettree.cs"
  printf 'class B { void F() { SceneTree.Paused = true; } }\n' > "$tmp/hit-scenetree.cs"
  printf 'class C {\n  void F() { SceneTree.Paused = true; // x\n  }\n}\n' > "$tmp/hit-trailing.cs"
  printf 'class D { /* note */ void F() { GetTree().Paused = true; } }\n' > "$tmp/hit-after-block.cs"
  printf 'class E {\n  /* a\n     b */ void F() { GetTree().Paused = true; }\n}\n' > "$tmp/hit-after-multiline-block.cs"
  printf 'class G { string u = "http://x"; void F() { GetTree().Paused = true; } }\n' > "$tmp/hit-after-url-string.cs"
  # Must not be caught: clean code and comment-only mentions.
  printf 'class H { void F() { var x = 1; } }\n' > "$tmp/clean-code.cs"
  printf 'class I {\n  /// never <c>SceneTree.Paused</c>\n  void F() { }\n}\n' > "$tmp/clean-doc.cs"
  printf 'class J {\n  // GetTree().Paused is banned\n  void F() { }\n}\n' > "$tmp/clean-line.cs"
  printf 'class K {\n  /*\n   * SceneTree.Paused\n   GetTree().Paused */\n  void F() { }\n}\n' > "$tmp/clean-block.cs"
  printf 'class L { /* SceneTree.Paused */ void F() { } }\n' > "$tmp/clean-inline-block.cs"
  printf 'class M {\n  void F() { var x = 1; } // not GetTree().Paused\n}\n' > "$tmp/clean-trailing.cs"
  ok=1
  for f in "$tmp"/*.cs; do
    name="$(basename "$f" .cs)"
    rc=0
    scan "$f" > /dev/null || rc=$?
    case "$name:$rc" in
      hit-*:0 | clean-*:1) ;;
      hit-*:1) echo "self-check: FAIL ($name not matched)"; ok=0 ;;
      clean-*:0) echo "self-check: FAIL ($name matched)"; ok=0 ;;
      *) echo "self-check: FAIL ($name: scan error, exit code $rc)"; ok=0 ;;
    esac
  done
  # Directory selection (the same scan_targets as the real run): src/Game/Smoke/ is skipped,
  # every other src/Game/ entry and the Sim are scanned.
  tree="$tmp/tree"
  mkdir -p "$tree/src/Game/Smoke" "$tree/src/Game/Other" "$tree/src/Game/Hosting" "$tree/src/PerformativeMail.Sim/Core"
  printf 'class X { void F() { GetTree().Paused = true; } }\n' > "$tree/src/Game/Smoke/X.cs"
  printf 'class H { void F() { var p = 1; } }\n' > "$tree/src/Game/Hosting/H.cs"
  printf 'class S { void F() { var p = 1; } }\n' > "$tree/src/PerformativeMail.Sim/Core/S.cs"
  run_tree() {  # <label> <expected rc> <must-list regex or -> <must-not-list regex>
    local label="$1" want="$2" must="$3" mustnot="$4" targets=() out rc=0
    mapfile -t targets < <(scan_targets "$tree")
    out="$(scan "${targets[@]}")" || rc=$?
    if [ "$rc" -ne "$want" ]; then
      echo "self-check: FAIL ($label: exit $rc, expected $want)"; printf '%s\n' "$out"; ok=0; return
    fi
    if [ "$must" != "-" ] && ! printf '%s\n' "$out" | grep -qE "$must"; then
      echo "self-check: FAIL ($label: $must not reported)"; ok=0; return
    fi
    if printf '%s\n' "$out" | grep -qE "$mustnot"; then
      echo "self-check: FAIL ($label: $mustnot reported)"; ok=0; return
    fi
  }
  run_tree "only src/Game/Smoke/ assigns" 1 - 'Smoke/X\.cs'
  printf 'class Y { void F() { GetTree().Paused = true; } }\n' > "$tree/src/Game/Other/Y.cs"
  run_tree "src/Game/Other/ assigns" 0 'src/Game/Other/Y\.cs:1:' 'Smoke/X\.cs'
  rm "$tree/src/Game/Other/Y.cs"
  printf 'class Z { void F() { SceneTree.Paused = true; } }\n' > "$tree/src/PerformativeMail.Sim/Core/Z.cs"
  run_tree "Sim assigns" 0 'PerformativeMail\.Sim/Core/Z\.cs:1:' 'Smoke/X\.cs'
  rm "$tree/src/PerformativeMail.Sim/Core/Z.cs"
  printf 'class T { void F() { GetTree().Paused = true; } }\n' > "$tree/src/Game/Top.cs"
  run_tree "a file directly in src/Game/ assigns" 0 'src/Game/Top\.cs:1:' 'Smoke/X\.cs'
  [ "$ok" = 1 ] || exit 1
  echo "self-check: PASS (GetTree().Paused / SceneTree.Paused caught in code, skipped in comments, clean code not matched; src/Game/Smoke/ skipped, other src/Game/ and Sim files caught)"
  exit 0
fi

dirs=()
mapfile -t dirs < <(scan_targets "$repo_root")
if [ "${#dirs[@]}" -eq 0 ]; then
  echo "No Sim / src/Game directories yet; nothing to scan."
  exit 0
fi
rc=0
scan "${dirs[@]}" || rc=$?
if [ "$rc" -eq 0 ]; then
  echo "::error::SceneTree.Paused found in Sim / src/Game (excluding src/Game/Smoke/) (never pause the Sim via the SceneTree)"
  exit 1
elif [ "$rc" -ne 1 ]; then
  echo "::error::scan failed with exit code $rc"
  exit "$rc"
fi
echo "No SceneTree.Paused in Sim / src/Game (src/Game/Smoke/ excluded; comments skipped)."
