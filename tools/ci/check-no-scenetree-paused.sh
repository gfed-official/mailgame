#!/usr/bin/env bash
# AC-3 (story 004): the Sim is paused by its own tick gate, never SceneTree.Paused.
# Scans src/PerformativeMail.Sim and src/Game/Hosting (those that exist) and fails on a match.
# Exit codes: 0 = clean, 1 = match, other = error (an error must not read as "clean").
#
# Matches inside C# comments are skipped, so documentation may name the API: `//` and `///`
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
#   tools/ci/check-no-scenetree-paused.sh --self-check  prove the scan catches code and skips comments
set -u

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
export PAUSE_PATTERN='SceneTree\.Paused|GetTree\(\)\.Paused'

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
  out="$(awk '
    FNR == 1 { seen++; inblock = 0; verbatim = 0 }
    { code = strip($0); if (code ~ ENVIRON["PAUSE_PATTERN"]) { print FILENAME ":" FNR ":" $0; found = 1 } }
    # Every candidate matched grep, so it is non-empty; an unseen file means awk could not read it.
    # The verdict is a printed last line, so an awk crash (any exit code) cannot read as "clean".
    END { if (seen == ARGC - 1) print (found ? "scan-result:match" : "scan-result:clean") }

    # Returns the line without comments. Block-comment and verbatim-string state carry over lines.
    function strip(s,   out, i, n, c, nx, j) {
      out = ""; n = length(s); i = 1
      while (i <= n) {
        c = substr(s, i, 1); nx = substr(s, i + 1, 1)
        if (inblock) {
          if (c == "*" && nx == "/") { inblock = 0; out = out " "; i += 2 } else i++
        } else if (verbatim) {
          out = out c
          if (c == "\"" && nx == "\"") { out = out nx; i += 2 } else { if (c == "\"") verbatim = 0; i++ }
        } else if (c == "/" && nx == "/") {
          break
        } else if (c == "/" && nx == "*") {
          inblock = 1; i += 2
        } else if (c == "@" && nx == "\"") {
          verbatim = 1; out = out c nx; i += 2
        } else if (c == "\"" || c == "\047") {
          # Regular string or char literal: copy through the closing quote, honouring \ escapes.
          for (j = i + 1; j <= n; j++) {
            if (substr(s, j, 1) == "\\") j++
            else if (substr(s, j, 1) == c) break
          }
          out = out substr(s, i, j - i + 1); i = j + 1
        } else {
          out = out c; i++
        }
      }
      return out
    }
  ' "${files[@]}")" || rc=$?
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
  [ "$ok" = 1 ] || exit 1
  echo "self-check: PASS (GetTree().Paused / SceneTree.Paused caught in code, skipped in comments, clean code not matched)"
  exit 0
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
scan "${dirs[@]}" || rc=$?
if [ "$rc" -eq 0 ]; then
  echo "::error::SceneTree.Paused found in Sim / Hosting (never pause the Sim via the SceneTree)"
  exit 1
elif [ "$rc" -ne 1 ]; then
  echo "::error::scan failed with exit code $rc"
  exit "$rc"
fi
echo "No SceneTree.Paused in Sim / Hosting (comments skipped)."
