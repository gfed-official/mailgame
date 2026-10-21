#!/usr/bin/env bash
# Asserts the scene-root rules of ADR-0019 (single GameLoop root, no autoloads, no scene
# changes) and ADR-0002 (no SceneTree.Paused, no _PhysicsProcess, no low-processor mode,
# no physics interpolation). Each rule is one counted PASS/FAIL row:
#   R1  project.godot has no [autoload] section and no autoload/ keys
#   R2  no ChangeSceneToFile/ChangeSceneToPacked (or GDScript spellings) under src/Game/
#   R3  project.godot does not set application/run/low_processor_mode=true
#   R4  project.godot does not set physics/common/physics_interpolation=true
#   R5  no _PhysicsProcess / _physics_process override under src/Game/
#   R6  no ".Paused =" / ".paused =" assignment under src/Game/ EXCLUDING src/Game/Smoke/ (reads
#       and "==" are fine). src/Game/Smoke/ is excluded because the godot-host-shell/003
#       `pause-driver-state` smoke must set SceneTree.Paused = true to prove a tree pause never
#       stops the Sim (TR-input-014); no shipping code lives there. Other rules scan Smoke too.
#   R7  application/run/main_scene="res://src/Game/Main.tscn" is set (exact value)
#   R9  presenters/views that override _Ready do no node/tree lookups (inert until Initialize)
#   R8a exactly one .tscn under src/Game/ references GameLoop.cs as an ext_resource script
#   R8b exactly one .cs under src/Game/ declares "partial class GameLoop"
# Rules 2, 5 and 6 ignore lines that are wholly a comment (leading //, /*, *, # or ;), so
# a doc comment may name a forbidden API; a trailing comment on a code line is still scanned.
#
# Usage:  tools/ci/check-scene-rules.sh [--self-check]
#   --self-check  builds a throwaway fake repo under $TMPDIR, proves the checks pass on a
#                 clean layout and are seen FAILING (on the intended rule) per planted violation.
# Env:    REPO_ROOT overrides the repository root (default: two levels above this script).
# Exit:   non-zero on any violated rule, or if no rule was checked.
set -u

script_path="${BASH_SOURCE[0]}"
root="${REPO_ROOT:-$(cd "$(dirname "$script_path")/../.." && pwd)}"

# ---------------------------------------------------------------- self-check
if [ "${1:-}" = "--self-check" ]; then
  tmp="$(mktemp -d "${TMPDIR:-/tmp}/check-scene-rules-selfcheck.XXXXXX")" || exit 2
  trap 'rm -rf "$tmp"' EXIT
  script_abs="$(cd "$(dirname "$script_path")" && pwd)/$(basename "$script_path")"

  make_clean() {
    rm -rf "$tmp/repo"; mkdir -p "$tmp/repo/src/Game/Hosting"
    # The script greps the full "application/run/..." key form that Godot writes per section.
    cat > "$tmp/repo/project.godot" <<'EOF'
; Engine configuration file.
config_version=5

[application]

config/name="Fake"
run/main_scene="res://src/Game/Main.tscn"
run/low_processor_mode=false

[physics]

common/physics_interpolation=false
EOF
    cat > "$tmp/repo/src/Game/Main.tscn" <<'EOF'
[gd_scene format=3 uid="uid://fakemain"]

[ext_resource type="Script" path="res://src/Game/Hosting/GameLoop.cs" id="1_loop"]

[node name="Main" type="Node"]
script = ExtResource("1_loop")
EOF
    # Clean control: comment-only mentions, reads and "==" comparisons must not trip R2/R5/R6.
    mkdir -p "$tmp/repo/src/Game/Presentation"
    printf 'using Godot;\n// Inert: no _Ready; GetNode is only mentioned in this comment.\npublic partial class WorldPresenter : Node3D { public bool IsInitialized => false; }\n' > "$tmp/repo/src/Game/Presentation/WorldPresenter.cs"
    cat > "$tmp/repo/src/Game/Hosting/GameLoop.cs" <<'EOF'
using Godot;
namespace PerformativeMail.Game.Hosting;

// Never call ChangeSceneToFile or ChangeSceneToPacked; never override _PhysicsProcess;
// never assign GetTree().Paused = true (ADR-0002).
/// <summary>Reads GetTree().Paused only; see ADR-0002.</summary>
public partial class GameLoop : Node
{
    public override void _Process(double delta)
    {
        if (GetTree().Paused == false) { }
        bool p = GetTree().Paused;
    }
}
EOF
  }
  run_child() { REPO_ROOT="$tmp/repo" bash "$script_abs" >"$tmp/out.txt" 2>&1; }

  bad=0; planted=0
  make_clean
  if run_child; then
    echo "PASS  self-check: clean fake layout accepted"
  else
    echo "FAIL  self-check: clean fake layout rejected"; cat "$tmp/out.txt"; bad=$((bad + 1))
  fi

  plant_check() {  # <rule id> <label> <planting command run inside the fake repo>
    local rule="$1" label="$2"; shift 2
    planted=$((planted + 1))
    make_clean
    (cd "$tmp/repo" && eval "$*")
    if run_child; then
      echo "FAIL  self-check: planted violation NOT detected: $label"; bad=$((bad + 1))
    elif ! grep -q "^FAIL  \[$rule\]" "$tmp/out.txt"; then
      echo "FAIL  self-check: planted violation rejected, but not by rule $rule: $label"; bad=$((bad + 1))
      cat "$tmp/out.txt"
    else
      echo "PASS  self-check: planted violation detected by $rule: $label"
    fi
  }
  plant_check R1  "[autoload] section"            'printf "\n[autoload]\n\nFoo=\"*res://foo.gd\"\n" >> project.godot'
  plant_check R1  "autoload/ key (top level)"     'sed -i "1a autoload/Foo=\"*res://foo.gd\"" project.godot'
  plant_check R2  "ChangeSceneToFile in .cs"      'printf "class X { void F() { GetTree().ChangeSceneToFile(\"res://a.tscn\"); } }\n" > src/Game/Hosting/Nav.cs'
  plant_check R2  "ChangeSceneToPacked in .cs"    'printf "class X { void F() { GetTree().ChangeSceneToPacked(null); } }\n" > src/Game/Hosting/Nav.cs'
  plant_check R2  "change_scene_to_file in .gd"   'printf "func f():\n\tget_tree().change_scene_to_file(\"res://a.tscn\")\n" > src/Game/nav.gd'
  plant_check R3  "low_processor_mode=true"       'sed -i "s|^run/low_processor_mode=.*|run/low_processor_mode=true|" project.godot'
  plant_check R4  "physics_interpolation=true"    'sed -i "s|^common/physics_interpolation=.*|common/physics_interpolation=true|" project.godot'
  plant_check R5  "_PhysicsProcess override"      'printf "partial class Y : Node { public override void _PhysicsProcess(double d) { } }\n" > src/Game/Hosting/Phys.cs'
  plant_check R6  "GetTree().Paused = true"       'printf "class Z { void F() { GetTree().Paused = true; } }\n" > src/Game/Hosting/Pause.cs'
  plant_check R6  "get_tree().paused = true (.gd)" 'printf "func f():\n\tget_tree().paused = true\n" > src/Game/pause.gd'
  plant_check R6  "non-Smoke assignment next to a Smoke one" 'mkdir -p src/Game/Smoke src/Game/Other && printf "class S { void F() { ctx.Tree.Paused = true; } }\n" > src/Game/Smoke/S.cs && printf "class O { void F() { GetTree().Paused = true; } }\n" > src/Game/Other/O.cs'
  plant_check R7  "wrong main_scene"              'sed -i "s|^run/main_scene=.*|run/main_scene=\"res://src/Game/Other.tscn\"|" project.godot'
  plant_check R7  "main_scene absent"             'sed -i "/^run\/main_scene=/d" project.godot'
  plant_check R7  "project.godot absent"          'rm project.godot'
  plant_check R8a "second scene referencing GameLoop.cs" 'cp src/Game/Main.tscn src/Game/Other.tscn'
  plant_check R8a "no scene referencing GameLoop.cs"     'rm src/Game/Main.tscn'
  plant_check R8b "second partial class GameLoop"        'printf "namespace Q { public partial class GameLoop : Node { } }\n" > src/Game/Hosting/GameLoop2.cs'
  plant_check R8b "no partial class GameLoop"            'rm src/Game/Hosting/GameLoop.cs'
  plant_check R3  "flat-form low_processor_mode=true (top level)" 'sed -i "1a application/run/low_processor_mode=true" project.godot'
  plant_check R7  "flat-form main_scene wrong + section key removed" 'sed -i "/^run\/main_scene=/d; 1a application/run/main_scene=\"res://x.tscn\"" project.godot'
  plant_check R9  "presenter GetNode in _Ready"           'mkdir -p src/Game/Presentation && printf "using Godot;\npublic partial class HudPresenter : Node { public override void _Ready() { var n = GetNode<Node>(\"../World\"); } }\n" > src/Game/Presentation/HudPresenter.cs'
  plant_check R9  "view GetTree in _Ready"                'printf "using Godot;\npublic partial class PlayerView : Node3D { public override void _Ready() { var t = GetTree(); } }\n" > src/Game/PlayerView.cs'
  plant_check R1  "[autoload_prepend] section"            'printf "\n[autoload_prepend]\n\nFoo=\"*res://foo.gd\"\n" >> project.godot'
  plant_check R2  "src/Game/ absent"                     'rm -rf src/Game'

  # src/Game/Smoke/ is excluded from R6 only: a Smoke assignment alone must be accepted.
  make_clean
  mkdir -p "$tmp/repo/src/Game/Smoke"
  printf 'class S { void F() { GetTree().Paused = true; ctx.Tree.Paused = false; } }\n' > "$tmp/repo/src/Game/Smoke/PauseSmoke.cs"
  if run_child && grep -q '^PASS  \[R6\]' "$tmp/out.txt"; then
    echo "PASS  self-check: src/Game/Smoke/ Paused assignment does not trip R6"
  else
    echo "FAIL  self-check: src/Game/Smoke/ Paused assignment tripped R6 or the run failed"; cat "$tmp/out.txt"; bad=$((bad + 1))
  fi
  # ...but the exclusion is R6 only: a Smoke _PhysicsProcess still trips R5.
  plant_check R5  "_PhysicsProcess override in src/Game/Smoke/" 'mkdir -p src/Game/Smoke && printf "partial class Y : Node { public override void _PhysicsProcess(double d) { } }\n" > src/Game/Smoke/Phys.cs'

  if [ "$bad" -ne 0 ]; then echo "check-scene-rules --self-check: $bad failure(s)"; exit 1; fi
  echo "check-scene-rules --self-check: clean layout accepted, $planted planted violations detected"
  exit 0
fi

# -------------------------------------------------------------------- checks
failures=0
rules=0

pass() { rules=$((rules + 1)); echo "PASS  $1"; }
fail() { rules=$((rules + 1)); failures=$((failures + 1)); echo "FAIL  $1"; }

project="$root/project.godot"
game="$root/src/Game"
# A line that is wholly a comment: "path:lineno:" then optional blanks then a comment marker.
comment_line='^[^:]+:[0-9]+:[[:space:]]*(//|/\*|\*|#|;)'

# project_has <ERE> : sets proj_rc (0 = match, 1 = no match, 2 = project.godot unreadable/error).
# project.godot keys are section-relative ("[application]" then "run/main_scene=..."), so the
# file is first normalised to full "section/key=value" lines; a flat "section/key=" line written
# outside any section is kept as-is. Section headers are kept so "[autoload]" is still visible.
project_normalised() {
  awk '
    /^[[:space:]]*\[[^]]+\][[:space:]]*$/ { print; sec=$0; sub(/^[[:space:]]*\[/, "", sec); sub(/\][[:space:]]*$/, "", sec); next }
    /^[[:space:]]*[;#]/ || /^[[:space:]]*$/ { print; next }
    /=/ { if (sec != "") { print sec "/" $0 } else { print } ; next }
    { print }
  ' "$project"
}
project_has() {
  if [ ! -f "$project" ]; then proj_rc=2; return; fi
  project_normalised | grep -Eq -e "$1"; proj_rc=$?
}

# src_scan <id> <description> <ERE> <include glob>... : fails on any non-comment hit under src/Game/.
# If $scan_exclude is non-empty, hits in files under that directory prefix are ignored.
scan_exclude=""
src_scan() {
  local id="$1" desc="$2" pat="$3"; shift 3
  if [ ! -d "$game" ]; then fail "[$id] $desc: src/Game/ is missing"; return; fi
  local inc=() g
  for g in "$@"; do inc+=("--include=$g"); done
  local raw rc hits
  raw="$(grep -rnE "${inc[@]}" -e "$pat" "$game" 2>&1)"; rc=$?
  if [ "$rc" -gt 1 ]; then fail "[$id] $desc: grep error: $raw"; return; fi
  hits="$(printf '%s\n' "$raw" | grep -vE "$comment_line" | grep -v '^$')"
  if [ -n "$scan_exclude" ] && [ -n "$hits" ]; then
    hits="$(printf '%s\n' "$hits" | awk -v p="$scan_exclude" 'index($0, p) != 1 && $0 != ""')"
  fi
  if [ -z "$hits" ]; then pass "[$id] $desc"; else fail "[$id] $desc:"; printf '%s\n' "$hits" | sed 's/^/        /'; fi
}

# R1 - no autoloads.
project_has '^\[autoload|^autoload/'
case "$proj_rc" in
  1) pass "[R1] project.godot has no [autoload] section and no autoload/ keys" ;;
  0) fail "[R1] project.godot declares autoloads (ADR-0019)" ;;
  *) fail "[R1] project.godot missing or unreadable" ;;
esac

# R2 - no scene changes.
src_scan R2 "no ChangeSceneToFile/ChangeSceneToPacked under src/Game/" \
  'ChangeSceneTo(File|Packed)|change_scene_to_(file|packed)' '*.cs' '*.gd' '*.tscn'

# R3 / R4 - low-processor mode and physics interpolation stay off (absent or =false passes).
project_has '^[[:space:]]*application/run/low_processor_mode[[:space:]]*=[[:space:]]*true'
case "$proj_rc" in
  1) pass "[R3] application/run/low_processor_mode is not true" ;;
  0) fail "[R3] application/run/low_processor_mode=true (ADR-0002 section 3)" ;;
  *) fail "[R3] project.godot missing or unreadable" ;;
esac
project_has '^[[:space:]]*physics/common/physics_interpolation[[:space:]]*=[[:space:]]*true'
case "$proj_rc" in
  1) pass "[R4] physics/common/physics_interpolation is not true" ;;
  0) fail "[R4] physics/common/physics_interpolation=true (ADR-0002 section 3)" ;;
  *) fail "[R4] project.godot missing or unreadable" ;;
esac

# R5 - no _PhysicsProcess override.
src_scan R5 "no _PhysicsProcess override under src/Game/" \
  'override[[:space:]]+void[[:space:]]+_PhysicsProcess|_physics_process[[:space:]]*\(' '*.cs' '*.gd'

# R6 - SceneTree.Paused is never assigned ("==" comparisons and reads are fine).
scan_exclude="$game/Smoke/"
src_scan R6 "no SceneTree.Paused assignment under src/Game/ outside src/Game/Smoke/ (ADR-0002)" \
  '\.(Paused|paused)[[:space:]]*=([^=]|$)' '*.cs' '*.gd'
scan_exclude=""

# R7 - main scene (exact value; tolerate a trailing CR).
project_has '^application/run/main_scene="res://src/Game/Main\.tscn"[[:space:]]*$'
case "$proj_rc" in
  0) pass "[R7] application/run/main_scene=\"res://src/Game/Main.tscn\"" ;;
  1) fail "[R7] application/run/main_scene=\"res://src/Game/Main.tscn\" is not set in project.godot" ;;
  *) fail "[R7] project.godot missing or unreadable" ;;
esac

# R8a / R8b - exactly one GameLoop root scene and exactly one GameLoop class.
if [ ! -d "$game" ]; then
  fail "[R8a] src/Game/ is missing"
  fail "[R8b] src/Game/ is missing"
else
  raw="$(grep -rlE --include='*.tscn' -e '\[ext_resource[^]]*path="[^"]*GameLoop\.cs"' "$game" 2>&1)"; rc=$?
  if [ "$rc" -gt 1 ]; then
    fail "[R8a] grep error: $raw"
  else
    n="$(printf '%s\n' "$raw" | grep -c .)"
    if [ "$n" -eq 1 ]; then pass "[R8a] exactly one scene under src/Game/ references GameLoop.cs (${raw#"$root"/})"
    else fail "[R8a] expected exactly 1 scene referencing GameLoop.cs under src/Game/, found $n:$(printf '%s\n' "$raw" | grep . | sed "s|^$root/| |" | tr '\n' ' ')"; fi
  fi
  raw="$(grep -rlE --include='*.cs' -e '\bpartial[[:space:]]+class[[:space:]]+GameLoop\b' "$game" 2>&1)"; rc=$?
  if [ "$rc" -gt 1 ]; then
    fail "[R8b] grep error: $raw"
  else
    n="$(printf '%s\n' "$raw" | grep -c .)"
    if [ "$n" -eq 1 ]; then pass "[R8b] exactly one .cs under src/Game/ declares partial class GameLoop (${raw#"$root"/})"
    else fail "[R8b] expected exactly 1 'partial class GameLoop' under src/Game/, found $n:$(printf '%s\n' "$raw" | grep . | sed "s|^$root/| |" | tr '\n' ' ')"; fi
  fi
fi

# R9 - presenters/views are inert until Initialize(deps): a file under src/Game/Presentation/
# or named *View.cs that overrides _Ready must not look anything up there (ADR-0019
# Implementation Guidelines: "Views must never look dependencies up in _Ready").
if [ ! -d "$game" ]; then
  fail "[R9] src/Game/ is missing"
else
  r9_bad=""
  while IFS= read -r f; do
    [ -z "$f" ] && continue
    if grep -qE 'override[[:space:]]+void[[:space:]]+_Ready[[:space:]]*\(' "$f"; then
      hits="$(grep -nE 'GetNode(OrNull)?(<|\()|GetTree[[:space:]]*\(|FindChild[[:space:]]*\(|GetChild[[:space:]]*\(|Engine\.GetSingleton|"%' "$f" | grep -vE '^[0-9]+:[[:space:]]*(//|/\*|\*)' || true)"
      [ -n "$hits" ] && r9_bad+=" ${f#"$root"/}"
    fi
  done < <({ find "$game/Presentation" -name '*.cs' 2>/dev/null; find "$game" -name '*View.cs' 2>/dev/null; } | sort -u)
  if [ -z "$r9_bad" ]; then pass "[R9] no node/tree lookups in a presenter or view _Ready (inert until Initialize(deps))"
  else fail "[R9] presenter/view overrides _Ready and looks nodes up:$r9_bad"; fi
fi

if [ "$failures" -ne 0 ]; then
  echo "check-scene-rules: $failures of $rules rules FAILED"
  exit 1
fi
if [ "$rules" -eq 0 ]; then
  echo "check-scene-rules: zero rules checked - refusing to report success"
  exit 1
fi
echo "check-scene-rules: checked $rules rules"
