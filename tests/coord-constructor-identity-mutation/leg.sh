#!/usr/bin/env bash
# One leg of the constructor-identity mutation matrix (FS-GG/.github#5113). It runs every check that claims to defend
# the content-based ConstructorIdentity decision and prints ONE tally from the runners' own summaries, never from an
# exit code, because an exit code cannot tell a failed assertion from a host that died first (#1812):
#
#   1. tests/coord-board-v2-projection (builds FS.GG.Coord.GitHub): the closure derivation, the change-listing
#      parser, the git evidence reader over a throwaway repository, and the preflight decision;
#   2. the end-to-end test in tests/board-v2-import/run.py, which rebuilds the BoardV2Import host at this checkout's
#      HEAD and drives `preflight` against a repository whose main descends from that revision. It is the only check
#      that reaches the wiring from the command to the evidence reader.
#
# It runs under a private TMPDIR and a private XDG_RUNTIME_DIR, so nothing reaches a live board lock. This file is
# never a mutation target, so no mutation can delete the evidence that it ran.
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
PRIVATE_TMP="$(mktemp -d "${HOME}/.cache/fsgg-constructor-identity-leg.XXXXXX" 2>/dev/null || mktemp -d)"
chmod 700 "$PRIVATE_TMP"
export TMPDIR="$PRIVATE_TMP"
mkdir -m 700 "$PRIVATE_TMP/runtime"
export XDG_RUNTIME_DIR="$PRIVATE_TMP/runtime"
trap 'rm -rf "$PRIVATE_TMP"' EXIT

LOG="$PRIVATE_TMP/unit.log"
dotnet test "$ROOT/tests/coord-board-v2-projection/coord-board-v2-projection.fsproj" -c Release --nologo >"$LOG" 2>&1
SUMMARY="$(grep -aEo 'Failed: +[0-9]+, Passed: +[0-9]+' "$LOG" | tail -1)"
if [ -z "$SUMMARY" ]; then
  echo "constructor-identity leg: NOT MEASURED — coord-board-v2-projection produced no test summary (build failure or host death)"
  tail -30 "$LOG"
  exit 3
fi
failed="$(echo "$SUMMARY" | grep -Eo 'Failed: +[0-9]+' | grep -Eo '[0-9]+')"
passed="$(echo "$SUMMARY" | grep -Eo 'Passed: +[0-9]+' | grep -Eo '[0-9]+')"

E2E="$PRIVATE_TMP/e2e.log"
python3 "$ROOT/tests/board-v2-import/run.py" -v \
  SuccessorEnrollmentTests.test_constructor_identity_admits_a_later_main_only_by_its_unchanged_closure >"$E2E" 2>&1
if grep -aEq '^OK( \(skipped=[0-9]+\))?$' "$E2E"; then
  if grep -aq 'skipped' "$E2E"; then
    echo "constructor-identity leg: NOT MEASURED — the end-to-end test was skipped"
    tail -20 "$E2E"
    exit 3
  fi
  passed=$((passed + 1))
elif grep -aEq '^FAILED \((failures|errors)=' "$E2E"; then
  failed=$((failed + 1))
else
  echo "constructor-identity leg: NOT MEASURED — the end-to-end test produced no verdict (build failure or crash)"
  tail -30 "$E2E"
  exit 3
fi
echo "constructor-identity leg: $passed passed, $failed failed"
[ "$failed" -eq 0 ]
