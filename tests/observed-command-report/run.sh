#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"; work="$(mktemp -d)"; trap 'rm -rf "$work"' EXIT
"$root/tests/observed-command-report/run-gate-junit.sh" "$work/ok.xml" bash -c 'exit 0'
grep -q 'failures="0"' "$work/ok.xml"
if "$root/tests/observed-command-report/run-gate-junit.sh" "$work/fail.xml" bash -c 'exit 7'; then exit 1; fi
grep -q 'failures="1"' "$work/fail.xml"
if "$root/tests/observed-command-report/run-gate-junit.sh" "$work/no.xml"; then exit 1; fi
test ! -e "$work/no.xml"
echo 'observed-command-report: 3 controls passed'
