#!/usr/bin/env bash
# Builds the Godot C# project without the editor and asserts the Export* configurations
# reach the libraries (ADR-0001 sections 2-4, ADR-0019 section 9):
#   (a) PerformativeMail.Game.sln builds for Debug, ExportDebug and ExportRelease, warning-free;
#   (b) PerformativeMail.Game.csproj builds for ExportDebug and ExportRelease and the Sim and
#       Client.Core obj/<Export config>/ directories then exist (configuration passed through);
#   (c) PerformativeMail.sln (Godot-free) never mentions PerformativeMail.Game;
#   (d) evaluation-only shape rows: Compile = src/Game/** only, exactly two ProjectReferences,
#       project.godot assembly_name, the game sln's configuration mapping, no stray nuget.config;
#   (e) tools/ci/check-build-config.sh passes.
#
# Usage:  tools/ci/check-game-build.sh
# Override the dotnet launcher if it is not on PATH, e.g.
#   DOTNET="mise exec dotnet@10.0.402 -- dotnet" tools/ci/check-game-build.sh
# Exit:   non-zero on any failed row, or if no row was checked.
set -u

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
export DOTNET
cd "$repo_root" || exit 2

rows=0
failures=0
log="$(mktemp "${TMPDIR:-/tmp}/check-game-build.XXXXXX")" || exit 2
trap 'rm -f "$log"' EXIT

pass() { rows=$((rows + 1)); echo "PASS  $1"; }
fail() { rows=$((rows + 1)); failures=$((failures + 1)); echo "FAIL  $1"; }

# build_row <label> <solution-or-project> <configuration>
build_row() {
  local label="$1" target="$2" config="$3" rc=0
  # shellcheck disable=SC2086  # DOTNET may be a multi-word launcher
  $DOTNET build "$target" -c "$config" -nologo >"$log" 2>&1 || rc=$?
  if [ "$rc" -ne 0 ]; then
    fail "$label (exit $rc)"
    tail -n 25 "$log" | sed 's/^/      /'
  elif grep -E ' warning ' "$log" >/dev/null; then
    fail "$label (exit 0 but warnings in output)"
    grep -E ' warning ' "$log" | sort -u | head -n 10 | sed 's/^/      /'
  else
    pass "$label (exit 0, no warnings)"
  fi
}

dir_row() {
  if [ -d "$repo_root/$1" ]; then pass "$1 exists"; else fail "$1 missing"; fi
}

build_row "build PerformativeMail.Game.sln -c Debug"         PerformativeMail.Game.sln Debug
build_row "build PerformativeMail.Game.sln -c ExportDebug"   PerformativeMail.Game.sln ExportDebug
build_row "build PerformativeMail.Game.sln -c ExportRelease" PerformativeMail.Game.sln ExportRelease

# The solution maps the libraries to Debug/Release/Release; only a csproj-level build
# passes the Export* configuration through the ProjectReference. Remove any obj/Export*
# left by an earlier local run first, so the directory rows below prove THIS build's
# pass-through rather than a stale one.
for lib in src/PerformativeMail.Sim src/PerformativeMail.Client.Core; do
  rm -rf "$repo_root/$lib/obj/ExportDebug" "$repo_root/$lib/obj/ExportRelease"
done
build_row "build PerformativeMail.Game.csproj -c ExportDebug"   PerformativeMail.Game.csproj ExportDebug
build_row "build PerformativeMail.Game.csproj -c ExportRelease" PerformativeMail.Game.csproj ExportRelease

dir_row src/PerformativeMail.Sim/obj/ExportDebug
dir_row src/PerformativeMail.Client.Core/obj/ExportDebug
dir_row src/PerformativeMail.Sim/obj/ExportRelease
dir_row src/PerformativeMail.Client.Core/obj/ExportRelease

mentions="$(grep -c PerformativeMail.Game PerformativeMail.sln)"
if [ "$mentions" = "0" ]; then
  pass "PerformativeMail.sln does not mention PerformativeMail.Game"
else
  fail "PerformativeMail.sln mentions PerformativeMail.Game ($mentions line(s))"
fi

# --- Evaluation-only shape rows (no build): a dropped Compile Remove or a stray reference
# fails here with a named row instead of an obscure compiler error.
$DOTNET msbuild PerformativeMail.Game.csproj -nologo -getItem:Compile -p:Configuration=ExportRelease >"$log" 2>&1
compile_total="$(grep -c '"Identity"' "$log" || true)"
compile_game="$(grep -c '"Identity": "src/Game/' "$log" || true)"
if [ "$compile_total" -gt 0 ] && [ "$compile_total" = "$compile_game" ]; then
  pass "csproj Compile items are src/Game/** only ($compile_total file(s))"
else
  fail "csproj Compile items outside src/Game/ ($compile_game of $compile_total under src/Game/)"
  grep '"Identity"' "$log" | grep -v '"src/Game/' | head -n 10 | sed 's/^/      /'
fi

$DOTNET msbuild PerformativeMail.Game.csproj -nologo -getItem:ProjectReference >"$log" 2>&1
ref_total="$(grep -c '"Identity"' "$log" || true)"
ref_sim="$(grep -c '"Identity": "src/PerformativeMail.Sim/PerformativeMail.Sim.csproj"' "$log" || true)"
ref_cc="$(grep -c '"Identity": "src/PerformativeMail.Client.Core/PerformativeMail.Client.Core.csproj"' "$log" || true)"
if [ "$ref_total" = "2" ] && [ "$ref_sim" = "1" ] && [ "$ref_cc" = "1" ]; then
  pass "csproj references Sim and Client.Core exactly once each"
else
  fail "csproj ProjectReferences: total=$ref_total sim=$ref_sim client.core=$ref_cc (want 2/1/1)"
fi

if grep -qE '^project/assembly_name="PerformativeMail\.Game"$' project.godot; then
  pass "project.godot [dotnet] assembly_name matches PerformativeMail.Game.csproj"
else
  fail "project.godot project/assembly_name is not \"PerformativeMail.Game\""
fi

# Solution configuration mapping (ADR-0019 section 9): Debug/ExportDebug/ExportRelease only;
# the Game project maps same-named, every library maps Debug/Release/Release.
sln=PerformativeMail.Game.sln
sln_detail=""
for cfg in Debug ExportDebug ExportRelease; do
  grep -qE "^\s*$cfg\|Any CPU = $cfg\|Any CPU\s*$" "$sln" || sln_detail+=" missing solution config $cfg;"
done
grep -qE '^\s*Release\|Any CPU = Release\|Any CPU' "$sln" && sln_detail+=" stray Release solution config;"
game_guid="$(grep -E '= "PerformativeMail\.Game", "PerformativeMail\.Game\.csproj", "\{[0-9A-F-]+\}"' "$sln" | grep -oE '\{[0-9A-F-]+\}"$' | tr -d '"}{')"
[ -n "$game_guid" ] || sln_detail+=" game project not in sln;"
while IFS= read -r guid; do
  [ -z "$guid" ] && continue
  if [ "$guid" = "$game_guid" ]; then
    for cfg in Debug ExportDebug ExportRelease; do
      grep -qE "^\s*\{$guid\}\.$cfg\|Any CPU\.ActiveCfg = $cfg\|Any CPU" "$sln" || sln_detail+=" Game $cfg not same-named;"
    done
  else
    grep -qE "^\s*\{$guid\}\.Debug\|Any CPU\.ActiveCfg = Debug\|Any CPU" "$sln" || sln_detail+=" {$guid} Debug->Debug missing;"
    for cfg in ExportDebug ExportRelease; do
      grep -qE "^\s*\{$guid\}\.$cfg\|Any CPU\.ActiveCfg = Release\|Any CPU" "$sln" || sln_detail+=" {$guid} $cfg->Release missing;"
    done
  fi
done < <(grep -E '^Project\("\{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC\}"\)' "$sln" | grep -oE '\{[0-9A-F-]+\}"$' | tr -d '"}{')
if [ -z "$sln_detail" ]; then
  pass "$sln configurations: Debug/ExportDebug/ExportRelease; Game same-named, libraries Debug/Release/Release"
else
  fail "$sln configuration mapping ->$sln_detail"
fi

# No nuget.config may be committed or left at the root (ADR-0001 section 2); the spike
# prototypes keep theirs under prototypes/, which is a separate Godot project root.
stray_nuget="$(find "$repo_root" \( -name .git -o -name .godot -o -path "$repo_root/prototypes" \) -prune -o -iname nuget.config -print 2>/dev/null)"
if [ -z "$stray_nuget" ]; then
  pass "no nuget.config outside prototypes/"
else
  fail "nuget.config present outside prototypes/: $(echo "$stray_nuget" | tr '\n' ' ')"
fi

cfg_rc=0
bash "$repo_root/tools/ci/check-build-config.sh" || cfg_rc=$?
if [ "$cfg_rc" -eq 0 ]; then
  pass "tools/ci/check-build-config.sh"
else
  fail "tools/ci/check-build-config.sh (exit $cfg_rc)"
fi

if [ "$failures" -ne 0 ]; then
  echo "check-game-build: $failures of $rows rows FAILED"
  exit 1
fi
if [ "$rows" -eq 0 ]; then
  echo "check-game-build: zero rows checked - refusing to report success"
  exit 1
fi
echo "check-game-build: checked $rows rows"
