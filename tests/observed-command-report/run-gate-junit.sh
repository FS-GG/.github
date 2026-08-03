#!/usr/bin/env bash
set -euo pipefail
out="$1"; shift
[ "$#" -gt 0 ] || { echo 'usage: run-gate-junit.sh OUT COMMAND...' >&2; exit 2; }
tmp="$(mktemp)"
rc=0
"$@" >"$tmp" 2>&1 || rc=$?
name="$1"
if [ "$rc" -eq 0 ]; then
  printf '<testsuite name="%s" tests="1" failures="0"><testcase name="%s"><system-out><![CDATA[' "$name" "$name" >"$out"
  cat "$tmp" >>"$out"; printf ']]></system-out></testcase></testsuite>\n' >>"$out"
else
  printf '<testsuite name="%s" tests="1" failures="1"><testcase name="%s"><failure message="exit %s"/><system-out><![CDATA[' "$name" "$name" "$rc" >"$out"
  cat "$tmp" >>"$out"; printf ']]></system-out></testcase></testsuite>\n' >>"$out"
fi
exit "$rc"
