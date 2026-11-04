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
python3 - "$WORK/manifest.json" <<'PY' && ok "manifest declares the schema-10-to-12 migration boundary" || bad "manifest declares the schema-10-to-12 migration boundary"
import json,sys
manifest=json.load(open(sys.argv[1]))
assert manifest["supportedStoreSchemaMin"] == 10
assert manifest["supportedStoreSchemaMax"] == 12
PY
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
python3 - "$ROOT" "$WORK" <<'PY' && ok "workflow diagnostics retain exact mismatch while strict payload verification still refuses" || bad "workflow diagnostics retain exact mismatch while strict payload verification still refuses"
import hashlib,json,pathlib,shlex,subprocess,sys,textwrap
root,work=map(pathlib.Path,sys.argv[1:])
workflow=(root/'.github/workflows/telemetry-host-package.yml').read_text()
begin='          # BEGIN normalized producer payload diagnostic\n'
end='          # END normalized producer payload diagnostic\n'
assert workflow.count(begin)==workflow.count(end)==1
code=textwrap.dedent(workflow.split(begin,1)[1].split(end,1)[0])
diagnostic=work/'workflow-diagnostic.py'
diagnostic.write_text(code)
verify=['python3',str(root/'scripts/telemetry-host-release.py'),'verify','--manifest',str(work/'manifest.json'),'--payload-only']
for candidate,expected_count in [('signed.nupkg',0),('changed.nupkg',1)]:
    delta=work/(candidate+'.delta.json')
    observed=subprocess.run(['python3',str(diagnostic),str(work/'host.nupkg'),str(work/candidate),str(delta)],cwd=root,capture_output=True,text=True)
    assert observed.returncode==0,observed.stderr
    evidence=json.loads(delta.read_text())
    assert evidence['equal']==(expected_count==0)
    assert len(evidence['changedEntries'])==expected_count
    if expected_count:
        member=evidence['changedEntries'][0]
        assert member=={'path':'tools/net10.0/linux-x64/FS.GG.Telemetry.Host.dll','aSha256':hashlib.sha256(b'host').hexdigest(),'bSha256':hashlib.sha256(b'changed').hexdigest()}
    refused=subprocess.run(verify+['--package',str(work/candidate)],cwd=root,capture_output=True,text=True)
    assert refused.returncode==(0 if expected_count==0 else 2)
    if expected_count:
        assert 'observed package producer payload differs' in refused.stderr
        sentinel=work/'unreachable-after-mismatch'
        command=shlex.join(verify+['--package',str(work/candidate)])+'\ntouch '+shlex.quote(str(sentinel))
        stopped=subprocess.run(['bash','-e','-c',command],cwd=root,capture_output=True,text=True)
        assert stopped.returncode==2 and not sentinel.exists()
retention=workflow.split('      - name: Retain original producer payload comparison failure\n',1)[1].split('      - uses:',1)[0]
assert "if: ${{ failure() && steps.host_payload_comparison.outcome == 'failure' }}" in retention
for artifact in ['artifacts/telemetry-host-pack-a/FS.GG.Telemetry.Host.*.nupkg','artifacts/telemetry-host-pack-b/FS.GG.Telemetry.Host.*.nupkg','artifacts/telemetry-host-manifest.json','artifacts/telemetry-host-payload-delta.json']:
    assert artifact in retention
assert 'continue-on-error' not in retention
assert workflow.index('# END normalized producer payload diagnostic')<workflow.index('python3 scripts/telemetry-host-release.py verify --manifest artifacts/telemetry-host-manifest.json --package "$package_b" --payload-only')
PY
must_fail "wrong independent tag is refused" "${TOOL[@]}" prepare --package "$WORK/host.nupkg" --lock "$WORK/packages.lock.json" --assets "$WORK/assets" --source-sha 0123456789abcdef0123456789abcdef01234567 --version 0.1.0 --tag coord-engine/v0.1.0 --output "$WORK/wrong.json"
# Seed an incompatible journal and prove the helper refuses it before replacement.
printf '%s\n' '{"schema":"fsgg.telemetry-host-release-journal/v1","manifestSha256":"sha256:foreign","observations":{}}' >"$WORK/foreign.json"
must_fail "foreign journal is refused" "${TOOL[@]}" verify --manifest "$WORK/manifest.json" --package "$WORK/signed.nupkg" --feed github --journal "$WORK/foreign.json"
printf '%s\t%s\n' 1111111111111111111111111111111111111111 refs/tags/telemetry-host/v0.1.0 >"$WORK/lightweight.txt"
printf '%s\t%s\n%s\t%s\n' 2222222222222222222222222222222222222222 refs/tags/telemetry-host/v0.1.0 3333333333333333333333333333333333333333 'refs/tags/telemetry-host/v0.1.0^{}' >"$WORK/annotated.txt"
[ "$("${TOOL[@]}" resolve-tag --tag telemetry-host/v0.1.0 --input "$WORK/lightweight.txt")" = 1111111111111111111111111111111111111111 ] && ok "lightweight remote tag resolves directly" || bad "lightweight remote tag resolves directly"
[ "$("${TOOL[@]}" resolve-tag --tag telemetry-host/v0.1.0 --input "$WORK/annotated.txt")" = 3333333333333333333333333333333333333333 ] && ok "annotated remote tag resolves through its peeled commit" || bad "annotated remote tag resolves through its peeled commit"

printf 'telemetry-host-release fixture: %d passed, %d failed\n' "$PASS" "$FAIL"
[ "$FAIL" -eq 0 ]
