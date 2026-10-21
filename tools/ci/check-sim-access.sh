#!/usr/bin/env bash
# Story godot-host-shell/003 (ADR-0002 section 3, TR-sim-008 shell half, spike F11). Two checks:
#   (1) only the host shell's Hosting/ and Boot/ code may name the Sim's frame pump, world or
#       client shell: a code reference matching FrameRunner|SimWorld|ClientLayer|\.RunFrame\(
#       in a .cs file under src/Game/ outside src/Game/Hosting/ and src/Game/Boot/ fails.
#       (A declaration `void RunFrame()` has no leading dot and is not a call.)
#   (2) GameLoop never reads `delta`: the body of `_Process(double ...)` in
#       src/Game/Hosting/GameLoop.cs (brace-matched from the signature; an expression body runs to
#       its `;`) must not contain the word `delta` in code. The signature itself is not scanned.
#       A missing GameLoop.cs or a missing _Process is an error, never "clean".
# Comments are skipped with the shared stripper tools/ci/lib/strip-cs-comments.awk (string and char
# literals are kept, so a `delta` inside a string in the body still counts - conservative).
# Limitations (review-enforced): braces inside string/char literals are counted by (2); the TD-103
# lexer limits of the stripper apply; a Sim reference through `var`/an alias is not seen by (1).
#
# Usage:  tools/ci/check-sim-access.sh [--self-check]
#   --self-check  builds throwaway fake repos under $TMPDIR and proves each check catches its
#                 planted violation, skips what it must skip, and errors on a missing _Process.
# Env:    REPO_ROOT overrides the repository root (default: two levels above this script).
# Exit:   0 = clean, 1 = violation, other = error (an error must not read as "clean").
set -u

script_path="${BASH_SOURCE[0]}"
script_dir="$(cd "$(dirname "$script_path")" && pwd)"
root="${REPO_ROOT:-$(cd "$script_dir/../.." && pwd)}"
strip_lib="$script_dir/lib/strip-cs-comments.awk"
[ -r "$strip_lib" ] || { echo "::error::missing $strip_lib" >&2; exit 2; }
export SIM_ACCESS_PATTERN='FrameRunner|SimWorld|ClientLayer|\.RunFrame\('

# ---------------------------------------------------------------- self-check
if [ "${1:-}" = "--self-check" ]; then
  tmp="$(mktemp -d "${TMPDIR:-/tmp}/check-sim-access-selfcheck.XXXXXX")" || exit 2
  trap 'rm -rf "$tmp"' EXIT
  script_abs="$script_dir/$(basename "$script_path")"

  make_clean() {
    rm -rf "$tmp/repo"
    mkdir -p "$tmp/repo/src/Game/Hosting" "$tmp/repo/src/Game/Boot" "$tmp/repo/src/Game/Smoke" "$tmp/repo/src/Game/Presentation"
    cat > "$tmp/repo/src/Game/Hosting/GameLoop.cs" <<'CS'
public partial class GameLoop : Node
{
    // F11: delta is never read.
    public override void _Process(double delta)
    {
        /* not delta either */
        if (_seq is null) { return; }
        { _seq.Run(); }
    }

    private void Other(double delta) { var d = delta; }
}
CS
    printf 'sealed class FrameSequence { IFrameRun _run; void F() { _run.RunFrame(); } FrameRunner _r; }\n' > "$tmp/repo/src/Game/Hosting/FrameSequence.cs"
    printf 'sealed class Boot { SimWorld _w; ClientLayer _c; void F() { _r.RunFrame(); } }\n' > "$tmp/repo/src/Game/Boot/Boot.cs"
    printf '// FrameRunner, SimWorld and ClientLayer are mentioned here only.\nsealed class Run : IFrameRun { public void RunFrame() { } /* x.RunFrame( */ void G() { seq.Run(); } }\n' > "$tmp/repo/src/Game/Smoke/Doubles.cs"
    printf '/// <summary>Never calls <c>SimWorld</c>.</summary>\nsealed class P { }\n' > "$tmp/repo/src/Game/Presentation/P.cs"
  }
  run_child() { REPO_ROOT="$tmp/repo" bash "$script_abs" >"$tmp/out.txt" 2>&1; }

  bad=0; planted=0
  expect_rc() {  # <expected rc> <label> <planting command run inside the fake repo>
    local want="$1" label="$2" rc=0; shift 2
    planted=$((planted + 1))
    make_clean
    (cd "$tmp/repo" && eval "$*")
    run_child || rc=$?
    if [ "$rc" -eq "$want" ]; then
      echo "PASS  self-check: $label (exit $rc)"
    else
      echo "FAIL  self-check: $label: exit $rc, expected $want"; cat "$tmp/out.txt"; bad=$((bad + 1))
    fi
  }
  expect_rc 0 "clean fake layout accepted (Hosting/Boot skipped, comment mentions skipped, RunFrame declaration ok)" ':'
  expect_rc 1 "FrameRunner referenced outside Hosting/Boot" 'printf "class X { FrameRunner _r; }\n" > src/Game/Smoke/X.cs'
  expect_rc 1 "SimWorld referenced outside Hosting/Boot"    'printf "class X { void F(SimWorld w) { } }\n" > src/Game/Presentation/X.cs'
  expect_rc 1 "ClientLayer referenced outside Hosting/Boot" 'mkdir -p src/Game/Ui && printf "class X { object o = typeof(ClientLayer); }\n" > src/Game/Ui/X.cs'
  expect_rc 1 ".RunFrame( called outside Hosting/Boot"      'printf "class X { void F() { run.RunFrame(); } }\n" > src/Game/X.cs'
  expect_rc 1 "reference after a closed block comment"      'printf "class X { /* c */ SimWorld _w; }\n" > src/Game/Smoke/X.cs'
  expect_rc 1 "delta read in the _Process body"             'sed -i "s|{ _seq.Run(); }|{ _seq.Run(); var t = delta; }|" src/Game/Hosting/GameLoop.cs'
  expect_rc 1 "delta read in an expression-bodied _Process" 'printf "public partial class GameLoop : Node\n{\n    public override void _Process(double delta) => Step(delta);\n}\n" > src/Game/Hosting/GameLoop.cs'
  expect_rc 1 "delta read with the brace on the signature line" 'printf "class GameLoop {\n  public override void _Process(double delta) { if (delta > 0) { } }\n}\n" > src/Game/Hosting/GameLoop.cs'
  expect_rc 0 "delta only in the signature, comments and another method" 'printf "class GameLoop {\n  public override void _Process(double delta) // delta\n  {\n    // delta\n  }\n  void G(double delta) { _ = delta; }\n}\n" > src/Game/Hosting/GameLoop.cs'
  expect_rc 2 "no _Process in GameLoop.cs is an error" 'printf "class GameLoop { void F(double delta) { } }\n" > src/Game/Hosting/GameLoop.cs'
  expect_rc 2 "unterminated _Process body is an error" 'printf "class GameLoop {\n  public override void _Process(double delta)\n  {\n" > src/Game/Hosting/GameLoop.cs'
  expect_rc 2 "missing GameLoop.cs is an error" 'rm src/Game/Hosting/GameLoop.cs'
  expect_rc 2 "missing src/Game/ is an error" 'rm -rf src/Game'

  if [ "$bad" -ne 0 ]; then echo "check-sim-access --self-check: $bad failure(s)"; exit 1; fi
  echo "check-sim-access --self-check: PASS ($planted cases)"
  exit 0
fi

# -------------------------------------------------------------------- checks
game="$root/src/Game"
loop="$game/Hosting/GameLoop.cs"
[ -d "$game" ] || { echo "::error::src/Game/ is missing under $root"; exit 2; }

violations=0

# (1) Sim access outside Hosting/ and Boot/.
files=()
mapfile -t files < <(find "$game" -type f -name '*.cs' \
  -not -path "$game/Hosting/*" -not -path "$game/Boot/*" | sort)
if [ "${#files[@]}" -gt 0 ]; then
  rc=0
  out="$(awk -f "$strip_lib" -f <(cat <<'AWK'
    FNR == 1 { seen++; inblock = 0; verbatim = 0 }
    { code = strip($0); if (code ~ ENVIRON["SIM_ACCESS_PATTERN"]) { print FILENAME ":" FNR ":" $0; found = 1 } }
    END { if (seen == ARGC - 1 || ARGC - 1 == 0) print (found ? "scan-result:match" : "scan-result:clean") }
AWK
  ) "${files[@]}")" || rc=$?
  verdict="${out##*$'\n'}"
  [ "$out" != "$verdict" ] && printf '%s\n' "${out%$'\n'*}"
  if [ "$rc" -ne 0 ]; then echo "::error::awk failed with exit code $rc"; exit 2; fi
  case "$verdict" in
    scan-result:match) echo "::error::Sim access (FrameRunner/SimWorld/ClientLayer/.RunFrame() under src/Game/ outside Hosting/ and Boot/ (ADR-0002, TR-sim-008)"; violations=$((violations + 1)) ;;
    scan-result:clean) echo "PASS  no FrameRunner/SimWorld/ClientLayer/.RunFrame( in ${#files[@]} src/Game/ files outside Hosting/ and Boot/ (comments skipped)" ;;
    *) echo "::error::awk gave no verdict (unreadable file?)"; exit 2 ;;
  esac
else
  echo "PASS  no .cs files under src/Game/ outside Hosting/ and Boot/"
fi

# (2) `delta` in the GameLoop._Process body.
[ -f "$loop" ] || { echo "::error::$loop is missing; the delta check cannot run"; exit 2; }
rc=0
out="$(awk -f "$strip_lib" -f <(cat <<'AWK'
  FNR == 1 { inblock = 0; verbatim = 0 }
  {
    code = strip($0)
    if (state == 0) {
      p = match(code, /_Process[ \t]*\([ \t]*double/)
      if (p == 0) next
      found++; state = 1; depth = 0
      rest = substr(code, p)
      q = index(rest, ")")
      if (q == 0) { print "scan-error:signature not closed on its line at " FNR; exit 0 }
      code = substr(rest, q + 1)
    }
    if (state == 1) {
      # Before the body: skip blanks; "{" opens a block body, "=>" an expression body.
      sub(/^[ \t]+/, "", code)
      if (code == "") next
      if (substr(code, 1, 2) == "=>") { state = 3; code = substr(code, 3) }
      else if (substr(code, 1, 1) == "{") { state = 2 }
      else { print "scan-error:unexpected text after the _Process signature at " FNR; exit 0 }
    }
    if (state == 3) {
      semi = index(code, ";")
      body = semi ? substr(code, 1, semi - 1) : code
      if (body ~ /(^|[^A-Za-z0-9_])delta([^A-Za-z0-9_]|$)/) { print FILENAME ":" FNR ":" $0; hit = 1 }
      if (semi) state = 4
      next
    }
    if (state == 2) {
      seg = ""; n = length(code)
      for (i = 1; i <= n; i++) {
        c = substr(code, i, 1)
        if (c == "{") depth++
        else if (c == "}") { depth--; if (depth == 0) { seg = seg c; state = 4; break } }
        seg = seg c
      }
      if (seg ~ /(^|[^A-Za-z0-9_])delta([^A-Za-z0-9_]|$)/) { print FILENAME ":" FNR ":" $0; hit = 1 }
    }
  }
  END {
    if (found == 0) print "scan-error:no _Process(double ...) found"
    else if (found > 1) print "scan-error:more than one _Process(double ...) found"
    else if (state != 4) print "scan-error:_Process body not terminated"
    else print (hit ? "scan-result:match" : "scan-result:clean")
  }
AWK
) "$loop")" || rc=$?
verdict="${out##*$'\n'}"
[ "$out" != "$verdict" ] && printf '%s\n' "${out%$'\n'*}"
if [ "$rc" -ne 0 ]; then echo "::error::awk failed with exit code $rc"; exit 2; fi
case "$verdict" in
  scan-result:match) echo "::error::GameLoop._Process reads delta (F11: time comes only from the monotonic clock)"; violations=$((violations + 1)) ;;
  scan-result:clean) echo "PASS  GameLoop._Process body does not read delta (comments skipped)" ;;
  scan-error:*) echo "::error::${verdict#scan-error:} in ${loop#"$root"/}"; exit 2 ;;
  *) echo "::error::awk gave no verdict"; exit 2 ;;
esac

if [ "$violations" -ne 0 ]; then
  echo "check-sim-access: $violations check(s) FAILED"
  exit 1
fi
echo "check-sim-access: clean"
