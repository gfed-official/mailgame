#!/usr/bin/env bash
# Asserts the ADR-0001 section 4 build-configuration table for src/PerformativeMail.Sim:
#   Optimize, SIM_ASSERT / INVENTORY_ASSERT in DefineConstants, CheckForOverflowUnderflow,
# for Debug, Release, Release -p:SimAsserts=true, ExportDebug and ExportRelease.
#
# Usage:  tools/ci/check-build-config.sh
# Override the dotnet launcher if it is not on PATH, e.g.
#   DOTNET="mise exec dotnet@10.0.402 -- dotnet" tools/ci/check-build-config.sh
set -u

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
project="$repo_root/src/PerformativeMail.Sim/PerformativeMail.Sim.csproj"
DOTNET="${DOTNET:-dotnet}"

failures=0

# prop <property> [msbuild args...] -> evaluated value of the property
prop() {
  local name="$1"; shift
  # shellcheck disable=SC2086  # DOTNET may be a multi-word launcher
  $DOTNET msbuild "$project" -nologo -getProperty:"$name" "$@" 2>&1 | tr -d '\r' | tail -n 1
}

# check_row <label> <expectAsserts true|false> <expectOptimize true|false> <expectDebugSymbols true|""> [msbuild args...]
check_row() {
  local label="$1" asserts="$2" optimize="$3" debugsym="$4"; shift 4
  local actual_opt actual_def actual_chk actual_dbg
  actual_opt="$(prop Optimize "$@")"
  actual_def="$(prop DefineConstants "$@")"
  actual_chk="$(prop CheckForOverflowUnderflow "$@")"
  actual_dbg="$(prop DebugSymbols "$@")"

  local ok=1 detail=""
  # Optimize: an unset value means false (the SDK leaves it empty in Debug).
  local opt_norm="${actual_opt:-false}"
  [ "${opt_norm,,}" = "$optimize" ] || { ok=0; detail+=" Optimize='$actual_opt' (want $optimize);"; }

  local has_sim=false has_inv=false
  case ";$actual_def;" in *";SIM_ASSERT;"*) has_sim=true ;; esac
  case ";$actual_def;" in *";INVENTORY_ASSERT;"*) has_inv=true ;; esac
  [ "$has_sim" = "$asserts" ] || { ok=0; detail+=" SIM_ASSERT=$has_sim (want $asserts);"; }
  [ "$has_inv" = "$asserts" ] || { ok=0; detail+=" INVENTORY_ASSERT=$has_inv (want $asserts);"; }

  local chk_norm="${actual_chk:-false}"
  [ "${chk_norm,,}" = "$asserts" ] || { ok=0; detail+=" CheckForOverflowUnderflow='$actual_chk' (want $asserts);"; }

  if [ -n "$debugsym" ]; then
    [ "${actual_dbg,,}" = "$debugsym" ] || { ok=0; detail+=" DebugSymbols='$actual_dbg' (want $debugsym);"; }
  fi

  if [ "$ok" = 1 ]; then
    echo "PASS  $label  (Optimize=$opt_norm asserts=$asserts checked=$chk_norm)"
  else
    echo "FAIL  $label ->$detail"
    echo "      DefineConstants=$actual_def"
    failures=$((failures + 1))
  fi
}

check_row "Debug"                          true  false ""     -p:Configuration=Debug
check_row "Release"                        false true  ""     -p:Configuration=Release
check_row "Release -p:SimAsserts=true"     true  true  ""     -p:Configuration=Release -p:SimAsserts=true
check_row "ExportDebug"                    true  false true   -p:Configuration=ExportDebug
check_row "ExportRelease"                  false true  ""     -p:Configuration=ExportRelease

if [ "$failures" -ne 0 ]; then
  echo "check-build-config: $failures row(s) FAILED"
  exit 1
fi
echo "check-build-config: all 5 rows passed"
