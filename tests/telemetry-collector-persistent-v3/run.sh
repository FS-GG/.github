#!/usr/bin/env bash
set -euo pipefail

: "${FSGG_C2_MANAGER_EXE:?FSGG_C2_MANAGER_EXE is required}"
: "${FSGG_C2_PREPARATION_EXE:?FSGG_C2_PREPARATION_EXE is required}"
: "${FSGG_C2_TEST_EXE:?FSGG_C2_TEST_EXE is required}"

test -x "$FSGG_C2_MANAGER_EXE"
test -x "$FSGG_C2_PREPARATION_EXE"
test -x "$FSGG_C2_TEST_EXE"
exec "$FSGG_C2_TEST_EXE"
