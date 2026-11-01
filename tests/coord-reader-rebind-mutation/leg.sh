#!/usr/bin/env bash
# One leg of the reader-rebind mutation matrix (FS-GG/.github#5120). It runs every check that claims to defend the
# `rebind-reader` stage and the RuntimeBindingIntegrity acceptance of its output, and prints ONE tally from the runners'
# own summaries, never from an exit code, because an exit code cannot tell a failed assertion from a host that died
# first (#1812):
#
#   1. tests/coord-board-v2-projection (builds FS.GG.Coord.GitHub): the splice and the preflight decision;
#   2. the BoardV2Import end-to-end test in tests/board-v2-import/run.py (rebuilds the tool at this checkout's HEAD):
#      the verification, authentication, already-bound and record guards, and the next cohort's preflight;
#   3. the engine end-to-end test (builds the engine, then runs it through scripts/fsgg-coord): the migration-scope rebind,
#      which only the engine host can do.
#
# It runs under a private TMPDIR and a private XDG_RUNTIME_DIR, so nothing reaches a live board lock. This file is never
# a mutation target, so no mutation can delete the evidence that it ran.
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
PRIVATE_TMP="$(mktemp -d "${HOME}/.cache/fsgg-reader-rebind-leg.XXXXXX" 2>/dev/null || mktemp -d)"
chmod 700 "$PRIVATE_TMP"
export TMPDIR="$PRIVATE_TMP"
mkdir -m 700 "$PRIVATE_TMP/runtime"
export XDG_RUNTIME_DIR="$PRIVATE_TMP/runtime"
trap 'rm -rf "$PRIVATE_TMP"' EXIT

not_measured() { echo "reader-rebind leg: NOT MEASURED — $1"; tail -30 "$2"; exit 3; }

LOG="$PRIVATE_TMP/unit.log"
dotnet test "$ROOT/tests/coord-board-v2-projection/coord-board-v2-projection.fsproj" -c Release --nologo >"$LOG" 2>&1
SUMMARY="$(grep -aEo 'Failed: +[0-9]+, Passed: +[0-9]+' "$LOG" | tail -1)"
[ -n "$SUMMARY" ] || not_measured "coord-board-v2-projection produced no test summary (build failure or host death)" "$LOG"
failed="$(echo "$SUMMARY" | grep -Eo 'Failed: +[0-9]+' | grep -Eo '[0-9]+')"
passed="$(echo "$SUMMARY" | grep -Eo 'Passed: +[0-9]+' | grep -Eo '[0-9]+')"

# One unittest verdict: OK without a skip counts one pass, FAILED counts one failure, anything else is no measurement.
tally() {
  if grep -aEq '^OK$' "$1"; then passed=$((passed + 1))
  elif grep -aEq '^FAILED \((failures|errors)=' "$1"; then failed=$((failed + 1))
  else not_measured "$2 produced no verdict, or was skipped" "$1"
  fi
}

TOOL="$PRIVATE_TMP/tool.log"
python3 "$ROOT/tests/board-v2-import/run.py" \
  SuccessorEnrollmentTests.test_rebind_reader_rebinds_only_the_reader_identity_to_the_executing_verified_reader >"$TOOL" 2>&1
tally "$TOOL" "the BoardV2Import end-to-end test"

BUILD="$PRIVATE_TMP/engine-build.log"
dotnet build "$ROOT/src/FS.GG.Coord.Cli" -c Release --nologo >"$BUILD" 2>&1 || not_measured "the engine did not build" "$BUILD"
ENGINE="$PRIVATE_TMP/engine.log"
FSGG_COORD_ENGINE_BIN="$ROOT/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine" python3 "$ROOT/tests/board-v2-import/run.py" --no-build \
  EngineShimTests.test_rebind_reader_rebinds_a_migration_scope_to_the_engine_host >"$ENGINE" 2>&1
tally "$ENGINE" "the engine end-to-end test"

echo "reader-rebind leg: $passed passed, $failed failed"
[ "$failed" -eq 0 ]
