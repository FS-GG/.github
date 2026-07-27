#!/usr/bin/env bash
# Wrapper for skillmirror-oracle.fsx — FS-GG/.github#1513.
#
# `#load` takes a string LITERAL, so the oracle cannot point itself at a library checkout supplied on
# the command line. This wrapper writes a two-line driver that `#load`s the library's own `Schemas.fs`
# and `SkillMirror.fs` (absolute paths) and then the oracle body, and runs it under `dotnet fsi`.
#
#   bash tests/skill-union/skillmirror-oracle.sh --lib /path/to/FS.GG.Contracts
#
# Exit 0 = every vector in skillmirror.fixtures.json carries the `library` block the library actually
# returns, AND the table's recorded library-source digest is the one measured here. Exit 1 = at least
# one disagreement, printed table-vs-library. Exit 2 = it could not run (no dotnet, no library) — NOT
# a pass, and never reported as one (#266).
#
# THIS IS NOT RUN BY CI, ON PURPOSE. See the header of skillmirror-oracle.fsx: the hermetic shell-side
# conformance (`skillmirror-conformance.sh`) is the leg that runs on every PR; this is the derivation
# that produced its expectations, kept re-runnable so the table is auditable rather than asserted.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
LIB=""
while [ $# -gt 0 ]; do
  case "$1" in
    --lib) LIB="${2:?--lib needs a value}"; shift 2 ;;
    *) echo "skillmirror-oracle: unknown argument: $1 (expected --lib <dir>)" >&2; exit 2 ;;
  esac
done

[ -n "$LIB" ] || { echo "skillmirror-oracle: --lib <dir containing SkillMirror.fs> is required" >&2; exit 2; }
command -v dotnet >/dev/null 2>&1 || { echo "skillmirror-oracle: dotnet not found — cannot run the library" >&2; exit 2; }
for f in Schemas.fs SkillMirror.fs; do
  [ -f "$LIB/$f" ] || { echo "skillmirror-oracle: $LIB/$f not found" >&2; exit 2; }
done

LIB_ABS="$(cd "$LIB" && pwd)"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/skillmirror-oracle.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

{
  printf '#load @"%s/Schemas.fs"\n' "$LIB_ABS"
  printf '#load @"%s/SkillMirror.fs"\n' "$LIB_ABS"
  printf '#load @"%s/skillmirror-oracle.fsx"\n' "$HERE"
} > "$WORK/driver.fsx"

exec dotnet fsi "$WORK/driver.fsx" --lib "$LIB_ABS" --table "$HERE/skillmirror.fixtures.json"
