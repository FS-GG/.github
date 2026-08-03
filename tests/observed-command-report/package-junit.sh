#!/usr/bin/env bash
set -euo pipefail
out="$1"; root="$(cd "$(dirname "$0")/../.." && pwd)"; tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
rc=0; bash "$root/src/FS.GG.Kit/verify-package.sh" >"$tmp/kit" 2>&1 || rc=1; kit=$rc
rc=0; bash "$root/src/FS.GG.Drivers/verify-package.sh" >"$tmp/drivers" 2>&1 || rc=1; drivers=$rc
printf '<testsuite name="package-verification" tests="2" failures="%d">' "$((kit + drivers))" >"$out"
for n in kit drivers; do
  case "$n" in kit) r="$kit" ;; drivers) r="$drivers" ;; esac
  printf '<testcase name="%s">' "$n" >>"$out"; [ "$r" -eq 0 ] || printf '<failure message="exit %s"/>' "$r" >>"$out"; printf '<system-out><![CDATA[' >>"$out"; cat "$tmp/$n" >>"$out"; printf ']]></system-out></testcase>' >>"$out"
done
printf '</testsuite>\n' >>"$out"; [ "$((kit + drivers))" -eq 0 ]
