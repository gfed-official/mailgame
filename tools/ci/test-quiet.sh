#!/usr/bin/env bash
# Quiet test runner: runs `dotnet test` with the full console log and a TRX written to a
# per-run directory, and prints only the run summary plus, for each failed test, its name,
# the first lines of its message and the top of its stack. Skipped-test blocks are never
# printed. Meant for agent / local dev runs, where a full log costs context; CI is unchanged.
#
# Usage:  tools/ci/test-quiet.sh [--self-check] [--expect-skipped N] [--expect-skipped-names FILE] [--log-dir DIR] -- <dotnet test args>
#   tools/ci/test-quiet.sh -- --solution PerformativeMail.sln -c Debug --no-build -- --filter-class "*PickMoveTests"
#   tools/ci/test-quiet.sh --expect-skipped 8 -- --solution PerformativeMail.sln -c Release -p:PmTestTfm=net8.0 --no-build
#   Everything after the first `--` goes to `dotnet test` verbatim; the wrapper adds
#   `--report-xunit-trx --report-xunit-trx-filename quiet.trx --results-directory <run dir>`
#   (xUnit v3's MTP TRX options) before the caller's inner `--`, or at the end if there is none.
#   --expect-skipped N  warn when the skipped count is not N; exits 3 if dotnet passed,
#                       otherwise keeps dotnet's exit code.
#   --expect-skipped-names FILE  FILE lists the fully-qualified skipped test names (TRX outcome
#                       NotExecuted), one per line; warn with the missing / unexpected names when
#                       the run's set differs (catches a skip swapped for another at the same
#                       count). Same exit rule as --expect-skipped.
#   --log-dir DIR       per-run directory (default: <repo>/TestResults/quiet/<timestamp>-<pid>/,
#                       gitignored and hidden from Godot by TestResults/.gdignore).
#   --self-check        runs the wrapper against a fake DOTNET stub and planted logs/TRX under
#                       $TMPDIR (no dotnet), proves pass/fail/skip-mismatch parsing and exit-code
#                       propagation, and that each check is seen FAILING on the wrong output.
# Env:    DOTNET overrides the dotnet command (default: dotnet), e.g.
#   DOTNET="mise exec dotnet@10.0.402 -- dotnet" tools/ci/test-quiet.sh -- --solution PerformativeMail.sln
# Exit:   dotnet's exit code (0 pass, 2 test failure, 8 zero tests ran); 3 on --expect-skipped
#         or --expect-skipped-names mismatch after a passing run; 64 on bad usage.
set -u

script_path="${BASH_SOURCE[0]}"
repo_root="$(cd "$(dirname "$script_path")/../.." && pwd)"
DOTNET="${DOTNET:-dotnet}"

# ---------------------------------------------------------------- self-check
if [ "${1:-}" = "--self-check" ]; then
  tmp="$(mktemp -d "${TMPDIR:-/tmp}/test-quiet-selfcheck.XXXXXX")" || exit 2
  trap 'rm -rf "$tmp"' EXIT
  script_abs="$(cd "$(dirname "$script_path")" && pwd)/$(basename "$script_path")"

  # Fake dotnet: records its argv, prints $STUB_LOG, copies $STUB_TRX to the requested
  # results directory / file name, exits $STUB_RC.
  cat > "$tmp/dotnet-stub" <<'STUB'
#!/usr/bin/env bash
printf '%s\n' "$@" > "$STUB_ARGS"
dir=""; name=""
while [ $# -gt 0 ]; do
  case "$1" in
    --results-directory) dir="$2"; shift ;;
    --report-xunit-trx-filename) name="$2"; shift ;;
  esac
  shift
done
cat "$STUB_LOG"
[ -n "${STUB_TRX:-}" ] && [ -n "$dir" ] && [ -n "$name" ] && mkdir -p "$dir" && cp "$STUB_TRX" "$dir/$name"
exit "$STUB_RC"
STUB
  chmod +x "$tmp/dotnet-stub"

  ns='xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"'
  cat > "$tmp/pass.trx" <<EOF
<TestRun $ns><Results>
<UnitTestResult testName="Pm.Tests.AlphaTests.Works" outcome="Passed" />
<UnitTestResult testName="Pm.Tests.AlphaTests.SkippedInDebugOnly" outcome="NotExecuted"><Output><StdOut>SKIPBODY-MARKER</StdOut><ErrorInfo><Message>SKIPREASON-MARKER</Message></ErrorInfo></Output></UnitTestResult>
</Results><ResultSummary outcome="Completed"><Counters total="2" executed="1" passed="1" failed="0" notExecuted="1" /></ResultSummary></TestRun>
EOF
  cat > "$tmp/fail.trx" <<EOF
<TestRun $ns><Results>
<UnitTestResult testName="Pm.Tests.AlphaTests.Works" outcome="Passed" />
<UnitTestResult testName="Pm.Tests.AlphaTests.SkippedInDebugOnly" outcome="NotExecuted"><Output><ErrorInfo><Message>SKIPREASON-MARKER</Message></ErrorInfo></Output></UnitTestResult>
<UnitTestResult testName="Pm.Tests.BetaTests.Breaks_Planted" outcome="Failed"><Output><ErrorInfo><Message>Assert.Equal() Failure: Values differ
Expected: 3
Actual:   4
MSGLINE-4
MSGLINE-5
MSGLINE-6-HIDDEN</Message><StackTrace>   at Pm.Tests.BetaTests.Breaks_Planted() in /x/BetaTests.cs:line 42
   at frame2
   at frame3
   at frame4
   at frame5
   at frame6
   at STACKLINE-9-HIDDEN</StackTrace></ErrorInfo></Output></UnitTestResult>
</Results><ResultSummary outcome="Failed"><Counters total="3" executed="2" passed="1" failed="1" notExecuted="1" /></ResultSummary></TestRun>
EOF
  summary() {  # <verdict> <total> <failed> <succeeded> <skipped>
    printf 'noise line 1\n  skipped Pm.Tests.AlphaTests.SkippedInDebugOnly (0ms)\n  SKIPBODY-MARKER\n'
    printf 'Test run summary: %s\n  total: %s\n  failed: %s\n  succeeded: %s\n  skipped: %s\n  duration: 1s 234ms\n' "$@"
  }
  summary 'Passed!' 2 0 1 1 > "$tmp/pass.log"
  summary 'Failed!' 3 1 1 1 > "$tmp/fail.log"
  printf 'error CS1002: ; expected\nBuild FAILED.\n' > "$tmp/broken.log"

  # run_case <name> <log> <trx|-> <rc> [wrapper options...]  -> $tmp/<name>.out, $tmp/<name>.rc
  run_case() {
    local name="$1" log="$2" trx="$3" rc="$4"; shift 4
    [ "$trx" = "-" ] && trx=""
    STUB_LOG="$log" STUB_TRX="$trx" STUB_RC="$rc" STUB_ARGS="$tmp/$name.args" DOTNET="$tmp/dotnet-stub" \
      bash "$script_abs" --log-dir "$tmp/run-$name" "$@" -- --solution X.sln -c Debug -- --filter-class "*Alpha Tests" \
      > "$tmp/$name.out" 2>&1
    echo $? > "$tmp/$name.rc"
  }
  has()   { grep -qF -- "$2" "$1"; }
  rc_is() { [ "$(cat "${1%.out}.rc")" = "$2" ]; }
  no_skips() { ! has "$1" SKIPBODY-MARKER && ! has "$1" SKIPREASON-MARKER && ! has "$1" SkippedInDebugOnly; }

  # Each verifier gets the case's .out file; it must accept its own case and reject the others.
  verify_pass()     { local o="$1"; rc_is "$o" 0 && has "$o" 'Test run summary: Passed!' && has "$o" 'skipped: 1' \
                        && has "$o" "$tmp/run-" && ! has "$o" 'FAILED TEST' && ! has "$o" 'WARNING' && no_skips "$o"; }
  verify_fail()     { local o="$1"; rc_is "$o" 2 && has "$o" 'FAILED TEST Pm.Tests.BetaTests.Breaks_Planted' \
                        && has "$o" 'Expected: 3' && has "$o" 'MSGLINE-5' && ! has "$o" 'MSGLINE-6-HIDDEN' \
                        && has "$o" 'BetaTests.cs:line 42' && ! has "$o" 'STACKLINE-9-HIDDEN' \
                        && has "$o" 'Test run summary: Failed!' && no_skips "$o"; }
  verify_mismatch() { local o="$1"; rc_is "$o" 3 && has "$o" 'WARNING: skipped 1, expected 5'; }
  verify_mismatch_on_fail() { local o="$1"; rc_is "$o" 2 && has "$o" 'WARNING: skipped 1, expected 5' && has "$o" 'FAILED TEST'; }
  verify_match()    { local o="$1"; rc_is "$o" 0 && ! has "$o" 'WARNING'; }
  verify_names_match() { local o="$1"; rc_is "$o" 0 && ! has "$o" 'WARNING'; }
  verify_names_swap()  { local o="$1"; rc_is "$o" 3 && has "$o" 'WARNING: skipped test names differ from' \
                           && has "$o" 'missing:    Pm.Tests.AlphaTests.OtherSkip' \
                           && has "$o" 'unexpected: Pm.Tests.AlphaTests.SkippedInDebugOnly'; }
  verify_names_swap_on_fail() { local o="$1"; rc_is "$o" 2 && has "$o" 'WARNING: skipped test names differ from' \
                           && has "$o" 'missing:    Pm.Tests.AlphaTests.OtherSkip' && has "$o" 'FAILED TEST'; }
  verify_broken()   { local o="$1"; rc_is "$o" 1 && has "$o" 'Build FAILED.' && has "$o" 'no test summary'; }
  verify_zero()     { local o="$1"; rc_is "$o" 8; }

  run_case pass     "$tmp/pass.log"   "$tmp/pass.trx" 0
  run_case fail     "$tmp/fail.log"   "$tmp/fail.trx" 2
  run_case mismatch "$tmp/pass.log"   "$tmp/pass.trx" 0 --expect-skipped 5
  run_case mismatch_on_fail "$tmp/fail.log" "$tmp/fail.trx" 2 --expect-skipped 5
  run_case match    "$tmp/pass.log"   "$tmp/pass.trx" 0 --expect-skipped 1
  printf 'Pm.Tests.AlphaTests.SkippedInDebugOnly\n' > "$tmp/names-ok.txt"
  printf 'Pm.Tests.AlphaTests.OtherSkip\n' > "$tmp/names-swapped.txt"
  run_case names_match "$tmp/pass.log" "$tmp/pass.trx" 0 --expect-skipped 1 --expect-skipped-names "$tmp/names-ok.txt"
  run_case names_swap  "$tmp/pass.log" "$tmp/pass.trx" 0 --expect-skipped 1 --expect-skipped-names "$tmp/names-swapped.txt"
  run_case names_swap_on_fail "$tmp/fail.log" "$tmp/fail.trx" 2 --expect-skipped-names "$tmp/names-swapped.txt"
  run_case broken   "$tmp/broken.log" -               1
  run_case zero     "$tmp/pass.log"   "$tmp/pass.trx" 8

  bad=0
  cases=(pass fail mismatch mismatch_on_fail match names_match names_swap names_swap_on_fail broken zero)
  for c in "${cases[@]}"; do
    if "verify_$c" "$tmp/$c.out"; then
      echo "PASS  self-check: $c case parsed and exit code propagated"
    else
      echo "FAIL  self-check: $c case"; sed 's/^/      /' "$tmp/$c.out"; echo "      rc=$(cat "$tmp/$c.rc")"; bad=$((bad + 1))
    fi
  done
  # Negative controls: each verifier must be seen FAILING on another case's output.
  pairs=("pass fail" "pass mismatch" "fail pass" "mismatch match" "mismatch_on_fail fail" "match mismatch" \
         "names_match names_swap" "names_swap names_match" "names_swap_on_fail names_swap" "broken pass" "zero pass")
  for pair in "${pairs[@]}"; do
    read -r v o <<< "$pair"
    if "verify_$v" "$tmp/$o.out"; then
      echo "FAIL  self-check: verify_$v accepted the $o output"; bad=$((bad + 1))
    else
      echo "PASS  self-check: verify_$v rejects the $o output"
    fi
  done
  # Verbatim pass-through: the quoted filter with a space arrives as one argument, and the
  # TRX options land before the caller's inner `--`.
  expected_args="$(printf '%s\n' test --solution X.sln -c Debug \
    --report-xunit-trx --report-xunit-trx-filename quiet.trx --results-directory "$tmp/run-pass" \
    -- --filter-class '*Alpha Tests')"
  if [ "$(cat "$tmp/pass.args")" = "$expected_args" ]; then
    echo "PASS  self-check: dotnet test arguments passed verbatim, TRX options inserted before the inner --"
  else
    echo "FAIL  self-check: dotnet test arguments differ:"; sed 's/^/      /' "$tmp/pass.args"; bad=$((bad + 1))
  fi
  if [ "$expected_args" = "$(cat "$tmp/fail.args" | sed 's/run-fail/run-pass/')" ] && \
     [ "$(cat "$tmp/pass.args")" != "$(cat "$tmp/fail.args")" ]; then
    echo "PASS  self-check: argument check is seen FAILING on a different run directory"
  else
    echo "FAIL  self-check: argument check does not discriminate"; bad=$((bad + 1))
  fi

  if [ "$bad" -ne 0 ]; then echo "test-quiet --self-check: $bad failure(s)"; exit 1; fi
  echo "test-quiet --self-check: ${#cases[@]} cases parsed, ${#pairs[@]} negative controls and the argument check seen failing on wrong output"
  exit 0
fi

# -------------------------------------------------------------------- options
usage() { sed -n '7,8p' "$script_path" | sed 's/^# //' >&2; exit 64; }
expect_skipped=""
expect_names=""
run_dir=""
while [ $# -gt 0 ]; do
  case "$1" in
    --expect-skipped) [ $# -ge 2 ] || usage; expect_skipped="$2"; shift 2 ;;
    --expect-skipped-names) [ $# -ge 2 ] || usage; expect_names="$2"; shift 2 ;;
    --log-dir)        [ $# -ge 2 ] || usage; run_dir="$2"; shift 2 ;;
    --)               shift; break ;;
    *)                echo "test-quiet: unknown option '$1' (dotnet test args go after --)" >&2; usage ;;
  esac
done
case "$expect_skipped" in ""|*[!0-9]*) [ -z "$expect_skipped" ] || usage ;; esac
if [ -n "$expect_names" ] && [ ! -r "$expect_names" ]; then
  echo "test-quiet: --expect-skipped-names file '$expect_names' is not readable" >&2; usage
fi
[ $# -gt 0 ] || usage

if [ -z "$run_dir" ]; then
  mkdir -p "$repo_root/TestResults"
  [ -e "$repo_root/TestResults/.gdignore" ] || : > "$repo_root/TestResults/.gdignore"
  run_dir="$repo_root/TestResults/quiet/$(date +%Y%m%d-%H%M%S)-$$"
fi
mkdir -p "$run_dir" || exit 2
run_dir="$(cd "$run_dir" && pwd)"
log="$run_dir/console.log"
trx="$run_dir/quiet.trx"
echo "test-quiet: log + TRX in $run_dir"

# The TRX options go before the caller's inner `--` (after it, `dotnet test` reads the results
# directory as a positional project path), or at the end if there is none.
trx_opts=(--report-xunit-trx --report-xunit-trx-filename quiet.trx --results-directory "$run_dir")
args=()
added=0
for a in "$@"; do
  if [ "$a" = "--" ] && [ "$added" -eq 0 ]; then args+=("${trx_opts[@]}"); added=1; fi
  args+=("$a")
done
[ "$added" -eq 1 ] || args+=("${trx_opts[@]}")

$DOTNET test "${args[@]}" > "$log" 2>&1
rc=$?

# ------------------------------------------------------------------- summary
# The "Test run summary" block (verdict, total, failed, succeeded, skipped, duration).
summary="$(sed -n '/^Test run summary:/,/^ *duration:/p' "$log")"
if [ -n "$summary" ]; then
  echo "$summary"
else
  echo "test-quiet: no test summary in the log (build or host failure?); last lines:"
  tail -15 "$log"
fi

if [ -f "$trx" ]; then
  python3 -I - "$trx" <<'PY'
import sys
import xml.etree.ElementTree as ET
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
try:
    root = ET.parse(sys.argv[1]).getroot()
except ET.ParseError as e:
    print(f"test-quiet: TRX unreadable ({e})")
    sys.exit(0)
failed = [r for r in root.iterfind(".//t:UnitTestResult", ns) if r.get("outcome") in ("Failed", "Error", "Timeout", "Aborted")]
MAX_TESTS, MSG_LINES, STACK_LINES = 20, 5, 6
for r in failed[:MAX_TESTS]:
    print(f"FAILED TEST {r.get('testName')}")
    msg = r.findtext("t:Output/t:ErrorInfo/t:Message", default="", namespaces=ns).strip().splitlines()
    stack = r.findtext("t:Output/t:ErrorInfo/t:StackTrace", default="", namespaces=ns).strip().splitlines()
    for line in msg[:MSG_LINES]:
        print(f"    {line}")
    for line in stack[:STACK_LINES]:
        print(f"    {line.strip()}")
if len(failed) > MAX_TESTS:
    print(f"... {len(failed) - MAX_TESTS} more failed tests (see TRX)")
with open(sys.argv[1] + ".skipped-names", "w") as f:
    for r in root.iterfind(".//t:UnitTestResult", ns):
        if r.get("outcome") == "NotExecuted":
            f.write(f"{r.get('testName')}\n")
c = root.find(".//t:ResultSummary/t:Counters", ns)
if c is not None:
    with open(sys.argv[1] + ".skipped", "w") as f:
        f.write(c.get("notExecuted", ""))
PY
fi

if [ -n "$expect_skipped" ]; then
  skipped=""
  [ -f "$trx.skipped" ] && skipped="$(cat "$trx.skipped")"
  [ -n "$skipped" ] || skipped="$(printf '%s\n' "$summary" | sed -n 's/^ *skipped: *\([0-9]*\).*/\1/p')"
  if [ "$skipped" != "$expect_skipped" ]; then
    echo "WARNING: skipped ${skipped:-<unknown>}, expected $expect_skipped"
    [ "$rc" -eq 0 ] && rc=3
  fi
fi

if [ -n "$expect_names" ]; then
  if [ -f "$trx.skipped-names" ]; then
    names_diff="$(LC_ALL=C comm -3 <(grep -v '^[[:space:]]*$' "$expect_names" | LC_ALL=C sort -u) \
                                    <(LC_ALL=C sort -u "$trx.skipped-names"))"
    if [ -n "$names_diff" ]; then
      echo "WARNING: skipped test names differ from $expect_names"
      printf '%s\n' "$names_diff" | sed -e 's/^\t/  unexpected: /' -e t -e 's/^/  missing:    /'
      [ "$rc" -eq 0 ] && rc=3
    fi
  else
    echo "WARNING: skipped test names unknown (no readable TRX), expected the list in $expect_names"
    [ "$rc" -eq 0 ] && rc=3
  fi
fi
exit "$rc"
