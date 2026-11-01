#!/usr/bin/env bash
# One leg of the board-tend derivation mutation matrix (FS-GG/.github#5136 phase 2 PR 2): build and run the V2Tend
# tests in coord-board-v2-projection, and print ONE tally line from the runner's own summary, never from the exit
# code (#1812). Runs under a private TMPDIR.
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
PRIVATE_TMP="$(mktemp -d "${HOME}/.cache/fsgg-tend-leg.XXXXXX" 2>/dev/null || mktemp -d)"
chmod 700 "$PRIVATE_TMP"
export TMPDIR="$PRIVATE_TMP"
trap 'rm -rf "$PRIVATE_TMP"' EXIT

LOG="$PRIVATE_TMP/test.log"
# `leg.sh` runs the V2Tend tests (derivation, reads, writer); `leg.sh cli` runs the tend CLI tests in cli-join.
if [ "${1:-}" = "cli" ]; then
  dotnet test "$ROOT/tests/coord-board-v2-cli-join/coord-board-v2-cli-join.fsproj" -c Release --nologo --filter "FullyQualifiedName~TendApplication" >"$LOG" 2>&1
else
  dotnet test "$ROOT/tests/coord-board-v2-projection/coord-board-v2-projection.fsproj" -c Release --nologo --filter "FullyQualifiedName~V2Tend" >"$LOG" 2>&1
fi
SUMMARY="$(grep -Eo 'Failed: +[0-9]+, Passed: +[0-9]+' "$LOG" | tail -1)"
if [ -z "$SUMMARY" ]; then
  echo "board-tend leg: NOT MEASURED — no test summary (build failure or host death)"
  tail -30 "$LOG"
  exit 3
fi
failed="$(echo "$SUMMARY" | grep -Eo 'Failed: +[0-9]+' | grep -Eo '[0-9]+')"
passed="$(echo "$SUMMARY" | grep -Eo 'Passed: +[0-9]+' | grep -Eo '[0-9]+')"
echo "board-tend leg: $passed passed, $failed failed"
[ "$failed" -eq 0 ]
