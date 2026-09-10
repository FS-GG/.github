#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/telemetry-host-release.XXXXXX")"
PASS=0
FAIL=0
ok() { PASS=$((PASS+1)); printf 'PASS  %s\n' "$1"; }
bad() { FAIL=$((FAIL+1)); printf 'FAIL  %s\n' "$1"; }
must_pass() { local name="$1"; shift; if "$@" >"$WORK/out" 2>"$WORK/err"; then ok "$name"; else bad "$name"; cat "$WORK/err"; fi; }
must_fail() { local name="$1"; shift; if "$@" >"$WORK/out" 2>"$WORK/err"; then bad "$name"; else ok "$name"; fi; }

mkdir -p "$WORK/assets"
printf 'fixed-ui\n' >"$WORK/assets/app.js"
printf '{"version":2,"dependencies":{}}\n' >"$WORK/packages.lock.json"
python3 - "$WORK" <<'PY'
import pathlib,sys,zipfile
root=pathlib.Path(sys.argv[1])
nuspec='''<?xml version="1.0"?><package><metadata><id>FS.GG.Telemetry.Host</id><version>0.1.0</version></metadata></package>'''
with zipfile.ZipFile(root/'host.nupkg','w') as z:
    z.writestr('FS.GG.Telemetry.Host.nuspec',nuspec)
    z.writestr('tools/net10.0/linux-x64/FS.GG.Telemetry.Host.dll',b'host')
with zipfile.ZipFile(root/'signed.nupkg','w') as z:
    z.writestr('FS.GG.Telemetry.Host.nuspec',nuspec)
    z.writestr('tools/net10.0/linux-x64/FS.GG.Telemetry.Host.dll',b'host')
    z.writestr('.signature.p7s',b'registry-signature')
with zipfile.ZipFile(root/'changed.nupkg','w') as z:
    z.writestr('FS.GG.Telemetry.Host.nuspec',nuspec)
    z.writestr('tools/net10.0/linux-x64/FS.GG.Telemetry.Host.dll',b'changed')
PY

TOOL=(python3 "$ROOT/scripts/telemetry-host-release.py")
must_pass "prepare binds independent package identity and source" "${TOOL[@]}" prepare --package "$WORK/host.nupkg" --lock "$WORK/packages.lock.json" --assets "$WORK/assets" --source-sha 0123456789abcdef0123456789abcdef01234567 --version 0.1.0 --tag telemetry-host/v0.1.0 --output "$WORK/manifest.json"
must_pass "prepared archive verifies exactly" "${TOOL[@]}" verify --manifest "$WORK/manifest.json" --package "$WORK/host.nupkg"
python3 - "$ROOT/tests/telemetry-host-release/payload-vector.json" "$WORK/manifest.json" <<'PY' && ok "producer payload digest matches the shared no-newline canonical vector" || bad "producer payload digest matches the shared no-newline canonical vector"
import json,sys
vector,manifest=(json.load(open(path)) for path in sys.argv[1:])
assert manifest['producerPayloadSha256']==vector['expectedProducerPayloadSha256']
PY
must_fail "a second archive is not mistaken for the prepared archive" "${TOOL[@]}" verify --manifest "$WORK/manifest.json" --package "$WORK/signed.nupkg"
must_pass "a clean second pack may compare producer payload without a feed journal" "${TOOL[@]}" verify --manifest "$WORK/manifest.json" --package "$WORK/signed.nupkg" --payload-only
must_pass "registry signature may alter archive while preserving producer payload" "${TOOL[@]}" verify --manifest "$WORK/manifest.json" --package "$WORK/signed.nupkg" --feed nuget --journal "$WORK/journal.json"
must_fail "changed producer payload is refused" "${TOOL[@]}" verify --manifest "$WORK/manifest.json" --package "$WORK/changed.nupkg" --feed github --journal "$WORK/journal.json"
must_fail "wrong independent tag is refused" "${TOOL[@]}" prepare --package "$WORK/host.nupkg" --lock "$WORK/packages.lock.json" --assets "$WORK/assets" --source-sha 0123456789abcdef0123456789abcdef01234567 --version 0.1.0 --tag coord-engine/v0.1.0 --output "$WORK/wrong.json"
# Seed an incompatible journal and prove the helper refuses it before replacement.
printf '%s\n' '{"schema":"fsgg.telemetry-host-release-journal/v1","manifestSha256":"sha256:foreign","observations":{}}' >"$WORK/foreign.json"
must_fail "foreign journal is refused" "${TOOL[@]}" verify --manifest "$WORK/manifest.json" --package "$WORK/signed.nupkg" --feed github --journal "$WORK/foreign.json"

printf 'telemetry-host-release fixture: %d passed, %d failed\n' "$PASS" "$FAIL"
[ "$FAIL" -eq 0 ]
