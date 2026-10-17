#!/usr/bin/env bash
# SC-ARCH-2 gate (ADR-0006 section 3 "Enforcement", ADR-0001 section 6).
#
# For every fixture in tests/fixtures/analyzers/*.cs this compiles that ONE file into
# src/PerformativeMail.Sim (via -p:PmArchFixture=<file>) and asserts the build FAILS
# with the diagnostic id named in the fixture's header:
#
#   // expect: PMSIM001      custom numeric analyzer (float/double/decimal/Half)
#   // expect: RS0030        BannedApiAnalyzers (src/PerformativeMail.Sim/BannedSymbols.txt)
#   // expect: compiles      a result that is only RECORDED (e.g. the readonly record struct,
#                            whose synthesised members have no syntax)
#   // covers: <id>          which BannedSymbols.txt entry (verbatim) or numeric type
#                            (type:float|double|decimal|System.Half) the fixture proves;
#                            one id per line, repeat the line for several (ids contain commas)
#
# The script FAILS (exit 1) when:
#   - a fixture compiles although it expects a diagnostic ("COMPILED");
#   - a fixture fails without its expected id (wrong or unrelated error);
#   - a fixture has no "// expect:" header;
#   - a BannedSymbols.txt entry or a numeric type (float/double/decimal/Half) has no fixture.
# A build/tooling failure that is not a compiler diagnostic exits 2.
#
# Usage:
#   tools/ci/check-analyzer-fixtures.sh               run every fixture
#   tools/ci/check-analyzer-fixtures.sh --self-check  prove the script detects a compiling fixture
#   FIXTURE_DIR=<dir> tools/ci/check-analyzer-fixtures.sh   use another fixture directory
# Override the dotnet launcher if it is not on PATH, e.g.
#   DOTNET="mise exec dotnet@10.0.402 -- dotnet" tools/ci/check-analyzer-fixtures.sh
# Not thread-safe: builds into a private temp dir but shares src/.../obj for restore assets.
set -u

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
script="$repo_root/tools/ci/check-analyzer-fixtures.sh"
sim_proj="$repo_root/src/PerformativeMail.Sim/PerformativeMail.Sim.csproj"
analyzer_proj="$repo_root/tools/PerformativeMail.Analyzers/PerformativeMail.Analyzers.csproj"
banned_file="$repo_root/src/PerformativeMail.Sim/BannedSymbols.txt"
fixture_dir="${FIXTURE_DIR:-$repo_root/tests/fixtures/analyzers}"
DOTNET="${DOTNET:-dotnet}"
config="${CONFIGURATION:-Debug}"

# ---------------------------------------------------------------------------
# --self-check: a clean file wrongly claiming "expect: PMSIM001" must make this
# script report COMPILED and exit 1. Proves the "fixture compiles => fail" path.
# ---------------------------------------------------------------------------
if [ "${1:-}" = "--self-check" ]; then
  tmp_self="$(mktemp -d)"
  trap 'rm -rf "$tmp_self"' EXIT
  cat > "$tmp_self/CleanButClaimsFloat.cs" <<'EOF'
// expect: PMSIM001
namespace PerformativeMail.Sim.ArchFixtures;

internal static class CleanButClaimsFloat
{
    internal static long Run(long a) => a + 1;
}
EOF
  out="$(FIXTURE_DIR="$tmp_self" SKIP_COVERAGE=1 bash "$script" 2>&1)"; rc=$?
  echo "$out"
  if [ "$rc" -eq 1 ] && echo "$out" | grep -q "COMPILED  CleanButClaimsFloat"; then
    echo "self-check: PASS (script reported the compiling fixture and exited 1)"
    exit 0
  fi
  echo "self-check: FAIL (script exit=$rc; a compiling fixture was NOT detected)"
  exit 1
fi

# ---------------------------------------------------------------------------
# TFM guard: BannedSymbols.txt lists the net8.0 overloads. A newer target framework adds
# overloads that would be silently unbanned, so retargeting must regenerate the list first.
# ---------------------------------------------------------------------------
# shellcheck disable=SC2086  # DOTNET may be a multi-word launcher
tfm="$($DOTNET msbuild "$sim_proj" -nologo -getProperty:TargetFramework 2>&1 | tr -d '\r' | tail -n 1)"
# shellcheck disable=SC2086
tfms="$($DOTNET msbuild "$sim_proj" -nologo -getProperty:TargetFrameworks 2>&1 | tr -d '\r' | tail -n 1)"
if [ "$tfm" != "net8.0" ] || [ -n "$tfms" ]; then
  echo "FAIL      Sim TargetFramework is '${tfm}' / TargetFrameworks '${tfms}'; it must be exactly net8.0."
  echo "          BannedSymbols.txt overloads were generated against net8.0 and must be regenerated"
  echo "          (see tests/fixtures/analyzers/README.md) before retargeting src/PerformativeMail.Sim."
  exit 1
fi

shopt -s nullglob
fixtures=("$fixture_dir"/*.cs)
if [ "${#fixtures[@]}" -eq 0 ]; then
  echo "check-analyzer-fixtures: no fixtures in $fixture_dir"
  exit 2
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# Fixtures are built into a private temp dir so a fixture that compiles (the recorded
# ones) never overwrites the real Sim output. The analyzer project is built once into
# its normal location and then NOT rebuilt per fixture (BuildProjectReferences=false):
# global properties such as OutDir would otherwise flow into it as well
# (the Sim csproj redirects its own OutDir/IntermediateOutputPath from PmArchFixtureWork).
# shellcheck disable=SC2086  # DOTNET may be a multi-word launcher
$DOTNET build "$analyzer_proj" -nologo -v:q -c "$config" >"$work/analyzer-build.log" 2>&1 || {
  cat "$work/analyzer-build.log"; echo "check-analyzer-fixtures: analyzer project failed to build"; exit 2; }
# shellcheck disable=SC2086
$DOTNET restore "$sim_proj" -nologo -v:q >"$work/restore.log" 2>&1 || {
  cat "$work/restore.log"; echo "check-analyzer-fixtures: restore failed"; exit 2; }

failures=0
declare -A covered=()
declare -a results=()

for fixture in "${fixtures[@]}"; do
  name="$(basename "$fixture" .cs)"
  expect="$(sed -n 's|^// expect: *\([A-Za-z0-9]*\).*|\1|p' "$fixture" | head -n 1)"
  while IFS= read -r cov; do
    [ -n "$cov" ] && covered["$cov"]=1
  done < <(sed -n 's|^// covers: *||p' "$fixture" | sed 's/^ *//;s/ *$//')

  if [ -z "$expect" ]; then
    echo "FAIL      $name  (no '// expect:' header)"
    results+=("$name|FAIL: no header")
    failures=$((failures + 1))
    continue
  fi

  log="$work/$name.log"
  # --no-restore: restore assets are shared and already current. -warnaserror per story note 3.
  # shellcheck disable=SC2086
  $DOTNET build "$sim_proj" -nologo --no-restore -c "$config" -warnaserror \
    -p:PmArchFixture="$fixture" -p:BuildProjectReferences=false \
    -p:PmArchFixtureWork="$work/$name" \
    >"$log" 2>&1
  rc=$?
  # Distinct compiler diagnostic ids reported against the fixture file itself.
  seen="$(grep -E "$name\.cs\([0-9]+,[0-9]+\): error [A-Za-z]+[0-9]+" "$log" \
          | sed -E 's/.*: error ([A-Za-z]+[0-9]+).*/\1/' | sort -u | tr '\n' ' ' | sed 's/ $//')"

  if [ "$expect" = "compiles" ]; then
    if [ "$rc" -eq 0 ]; then
      echo "RECORDED  $name  compiles (no diagnostic)"
      results+=("$name|compiles")
    elif [ -n "$seen" ]; then
      echo "RECORDED  $name  REJECTED by: $seen  (expected compiles; better than expected, review the README)"
      results+=("$name|rejected: $seen")
    else
      echo "FAIL      $name  expected to compile but the build failed without a diagnostic on the fixture:"
      grep -E "error" "$log" | head -n 5
      results+=("$name|FAIL: build error")
      failures=$((failures + 1))
    fi
    continue
  fi

  if [ "$rc" -eq 0 ]; then
    echo "COMPILED  $name  expected $expect but the fixture COMPILED -> the ban is not enforced"
    results+=("$name|COMPILED (expected $expect)")
    failures=$((failures + 1))
  elif [[ " $seen " == *" $expect "* ]]; then
    echo "SEEN      $name  $expect  (all ids on fixture: $seen)"
    results+=("$name|$expect")
  else
    echo "FAIL      $name  expected $expect, saw: ${seen:-<no diagnostic on fixture file>}"
    grep -E "error" "$log" | head -n 5
    results+=("$name|FAIL: saw ${seen:-none}")
    failures=$((failures + 1))
  fi
done

# ---------------------------------------------------------------------------
# Coverage: every BannedSymbols.txt entry and every numeric type has a fixture.
# ---------------------------------------------------------------------------
if [ "${SKIP_COVERAGE:-0}" != "1" ]; then
  required=(type:float type:double type:decimal type:System.Half)
  while IFS= read -r entry; do
    required+=("$entry")
  done < <(grep -vE '^\s*(//|$)' "$banned_file" | sed 's/;.*//; s/^ *//; s/ *$//')
  for id in "${required[@]}"; do
    if [ -z "${covered[$id]:-}" ]; then
      echo "FAIL      no fixture covers: $id"
      failures=$((failures + 1))
    fi
  done
fi

echo "----"
echo "check-analyzer-fixtures: ${#fixtures[@]} fixtures, $failures failure(s)"
if [ "$failures" -ne 0 ]; then
  exit 1
fi
exit 0
