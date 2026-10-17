#!/usr/bin/env bash
# Asserts the .gdignore policy of ADR-0019 section 8:
#   - tests/, tools/, prototypes/, docs/, design/, production/,
#     src/PerformativeMail.Sim/ and src/PerformativeMail.Client.Core/ each carry a .gdignore;
#   - src/.gdignore does not exist (it would hide src/Game/); src/Game/ and every
#     directory named "content" (and everything below them) carry none;
#   - no bin/, obj/ or TestResults/ directory is visible to Godot, i.e. each has an
#     ancestor (or itself) carrying a .gdignore. The generated, gitignored repo-root
#     TestResults/ gets TestResults/.gdignore created here, idempotently.
# .git and .godot are never scanned.
#
# Usage:  tools/ci/check-gdignore.sh [--self-check]
#   --self-check  builds a throwaway fake repo under $TMPDIR, proves the checks pass on
#                 a clean layout and are seen FAILING on each planted violation.
# Env:    REPO_ROOT overrides the repository root (default: two levels above this script).
# Exit:   non-zero on any violated rule, or if no rule was checked.
set -u

script_path="${BASH_SOURCE[0]}"
root="${REPO_ROOT:-$(cd "$(dirname "$script_path")/../.." && pwd)}"

# The Godot-free libraries are every src/PerformativeMail.* folder (ADR-0019 section 8),
# so a library added later is picked up without editing this list.
required_dirs=(tests tools prototypes docs design production)
for lib in "$root"/src/PerformativeMail.*/; do
  [ -d "$lib" ] && required_dirs+=("src/$(basename "$lib")")
done

# ---------------------------------------------------------------- self-check
if [ "${1:-}" = "--self-check" ]; then
  tmp="$(mktemp -d "${TMPDIR:-/tmp}/check-gdignore-selfcheck.XXXXXX")" || exit 2
  trap 'rm -rf "$tmp"' EXIT
  script_abs="$(cd "$(dirname "$script_path")" && pwd)/$(basename "$script_path")"

  make_clean() {
    rm -rf "$tmp/repo"; mkdir -p "$tmp/repo"
    local d
    for d in "${required_dirs[@]}"; do
      mkdir -p "$tmp/repo/$d"; : > "$tmp/repo/$d/.gdignore"
    done
    mkdir -p "$tmp/repo/src/Game" "$tmp/repo/content/art" "$tmp/repo/TestResults" \
             "$tmp/repo/src/PerformativeMail.Sim/obj" "$tmp/repo/tests/bin" "$tmp/repo/.godot/obj"
  }
  run_child() { REPO_ROOT="$tmp/repo" bash "$script_abs" >"$tmp/out.txt" 2>&1; }

  bad=0
  make_clean
  if run_child; then
    echo "PASS  self-check: clean fake layout accepted"
  else
    echo "FAIL  self-check: clean fake layout rejected"; cat "$tmp/out.txt"; bad=$((bad + 1))
  fi
  [ -f "$tmp/repo/TestResults/.gdignore" ] || { echo "FAIL  self-check: TestResults/.gdignore not created"; bad=$((bad + 1)); }

  plant_check() {  # <label> <planting command run inside the fake repo>
    local label="$1"; shift
    make_clean
    (cd "$tmp/repo" && eval "$*")
    if run_child; then
      echo "FAIL  self-check: planted violation NOT detected: $label"; bad=$((bad + 1))
    else
      echo "PASS  self-check: planted violation detected: $label"
    fi
  }
  plant_check "src/.gdignore present"          ': > src/.gdignore'
  plant_check ".gdignore under src/Game"       ': > src/Game/.gdignore'
  plant_check ".gdignore under content/"       ': > content/art/.gdignore'
  plant_check "missing tests/.gdignore"        'rm tests/.gdignore'
  plant_check "visible obj/ directory"         'mkdir -p src/Game/obj'
  plant_check "visible bin/ directory"         'mkdir -p bin'
  plant_check "repo-root .gdignore present"    ': > .gdignore'
  plant_check "new src/PerformativeMail.* library without .gdignore" 'mkdir -p src/PerformativeMail.Extra'

  if [ "$bad" -ne 0 ]; then echo "check-gdignore --self-check: $bad failure(s)"; exit 1; fi
  echo "check-gdignore --self-check: clean layout accepted, 8 planted violations detected"
  exit 0
fi

# -------------------------------------------------------------------- checks
failures=0
rules=0

pass() { rules=$((rules + 1)); echo "PASS  $1"; }
fail() { rules=$((rules + 1)); failures=$((failures + 1)); echo "FAIL  $1"; }

# Prune .git and .godot wherever they occur.
scan() { find "$root" \( -name .git -o -name .godot \) -prune -o "$@"; }

for d in "${required_dirs[@]}"; do
  if [ ! -d "$root/$d" ]; then
    fail "$d/ is missing"
  elif [ -f "$root/$d/.gdignore" ]; then
    pass "$d/.gdignore present"
  else
    fail "$d/.gdignore missing"
  fi
done

if [ -e "$root/.gdignore" ]; then fail "repo-root .gdignore exists (it would hide the whole project)"; else pass "no repo-root .gdignore"; fi
if [ -e "$root/src/.gdignore" ]; then fail "src/.gdignore exists (it would hide src/Game/)"; else pass "no src/.gdignore"; fi

hits="$(find "$root/src/Game" -name .gdignore 2>/dev/null)"
if [ -d "$root/src/Game" ] && [ -z "$hits" ]; then
  pass "no .gdignore under src/Game/"
else
  fail "src/Game/ missing or carries .gdignore: ${hits:-<directory absent>}"
fi

content_bad=""; found=""
while IFS= read -r c; do
  [ -z "$c" ] && continue
  found="$(find "$c" -name .gdignore -print -quit 2>/dev/null)"
  [ -n "$found" ] && content_bad+=" ${found#"$root"/}"
done < <(scan -type d -name content -print)
if [ -z "$content_bad" ]; then pass "no .gdignore at or under any content/ directory"; else fail "content/ must not carry .gdignore:$content_bad"; fi

# Generated repo-root TestResults/: mark it hidden (idempotent).
if [ -d "$root/TestResults" ]; then
  if [ -e "$root/TestResults/.gdignore" ]; then
    echo "TestResults/.gdignore already present"
  else
    : > "$root/TestResults/.gdignore" && echo "created TestResults/.gdignore"
  fi
fi

# hidden_from_godot <dir> : true if <dir> or any ancestor up to the root carries .gdignore.
hidden_from_godot() {
  local d="$1"
  while :; do
    [ -e "$d/.gdignore" ] && return 0
    [ "$d" = "$root" ] && return 1
    d="$(dirname "$d")"
    case "$d" in "$root"|"$root"/*) ;; *) return 1 ;; esac
  done
}

for name in bin obj TestResults; do
  visible=""
  while IFS= read -r v; do
    [ -z "$v" ] && continue
    hidden_from_godot "$v" || visible+=" ${v#"$root"/}"
  done < <(scan -type d -name "$name" -print -prune)
  if [ -z "$visible" ]; then pass "no $name/ directory visible to Godot"; else fail "$name/ visible to Godot:$visible"; fi
done

if [ "$failures" -ne 0 ]; then
  echo "check-gdignore: $failures of $rules rules FAILED"
  exit 1
fi
if [ "$rules" -eq 0 ]; then
  echo "check-gdignore: zero rules checked - refusing to report success"
  exit 1
fi
echo "check-gdignore: checked $rules rules"
