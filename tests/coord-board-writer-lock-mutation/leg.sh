#!/usr/bin/env bash
# One leg of the board writer-lock mutation matrix (FS-GG/.github#5136 PR 1, #5126): build the engine and run the
# coord-board-v2-cli-join tests, which hold the V2WriterLock tests, the injected-directory causal control and the
# cross-process flock test against Python. Prints ONE tally line from the runner's own summary, never from the exit
# code, because an exit code cannot tell a failed assertion from a host that died first (#1812).
#
# It runs under a private TMPDIR and a private XDG_RUNTIME_DIR (a live migration may hold the production lock), and
# every seam the tests call takes an injected lock directory.
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
PRIVATE_TMP="$(mktemp -d "${HOME}/.cache/fsgg-writer-lock-leg.XXXXXX" 2>/dev/null || mktemp -d)"
chmod 700 "$PRIVATE_TMP"
export TMPDIR="$PRIVATE_TMP"
# A private XDG_RUNTIME_DIR too: no mutant can reach the production lock under the real runtime directory (#5126).
mkdir -m 700 "$PRIVATE_TMP/runtime"
export XDG_RUNTIME_DIR="$PRIVATE_TMP/runtime"
trap 'rm -rf "$PRIVATE_TMP"' EXIT

LOG="$PRIVATE_TMP/test.log"
dotnet test "$ROOT/tests/coord-board-v2-cli-join/coord-board-v2-cli-join.fsproj" -c Release --nologo >"$LOG" 2>&1
SUMMARY="$(grep -Eo 'Failed: +[0-9]+, Passed: +[0-9]+' "$LOG" | tail -1)"
if [ -z "$SUMMARY" ]; then
  echo "board-writer-lock leg: NOT MEASURED — no test summary (build failure or host death)"
  tail -30 "$LOG"
  exit 3
fi
failed="$(echo "$SUMMARY" | grep -Eo 'Failed: +[0-9]+' | grep -Eo '[0-9]+')"
passed="$(echo "$SUMMARY" | grep -Eo 'Passed: +[0-9]+' | grep -Eo '[0-9]+')"
echo "board-writer-lock leg: $passed passed, $failed failed"
[ "$failed" -eq 0 ]
