#!/usr/bin/env bash
# Builds and runs tools/ContentValidator over content/ (or the directory given).
# Exit code 0: valid; 1: problems, one "error: ..." line each; 2: wrong usage.
#   tools/validate-content.sh [CONTENT_DIR] [--quiet]
set -euo pipefail
cd "$(dirname "$0")/.."

DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
"$DOTNET" build tools/ContentValidator -nodeReuse:false -v quiet -nologo >/dev/null
exec "$DOTNET" tools/ContentValidator/bin/Debug/net8.0/ContentValidator.dll "$@"
