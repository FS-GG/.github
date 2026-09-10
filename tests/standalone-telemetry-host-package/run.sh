#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 2 ]; then
  echo "usage: $0 <FS.GG.Telemetry.Host.VERSION.nupkg> <evidence.json>" >&2
  exit 2
fi
PACKAGE="$(realpath "$1")"
EVIDENCE="$(realpath -m "$2")"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/telemetry-host-package.XXXXXX")"
PASS=0
FAIL=0
ok() { PASS=$((PASS+1)); printf 'PASS  %s\n' "$1"; }
bad() { FAIL=$((FAIL+1)); printf 'FAIL  %s\n' "$1"; [ "$#" -lt 2 ] || printf '      %s\n' "$2"; }

[ -f "$PACKAGE" ] || { echo "candidate package is missing" >&2; exit 2; }
PACKAGE_SHA="$(sha256sum "$PACKAGE" | cut -d' ' -f1)"
PACKAGE_BYTES="$(stat -c %s "$PACKAGE")"
python3 - "$PACKAGE" "$WORK/metadata.json" <<'PY'
import json,pathlib,sys,zipfile
from xml.etree import ElementTree
package,out=map(pathlib.Path,sys.argv[1:])
with zipfile.ZipFile(package) as archive:
    names=sorted(archive.namelist())
    nuspec=[n for n in names if n.endswith('.nuspec') and '/' not in n]
    assert len(nuspec)==1
    root=ElementTree.fromstring(archive.read(nuspec[0]))
    def one(local):
        values=[e.text for e in root.iter() if e.tag.rsplit('}',1)[-1]==local]
        assert len(values)==1 and values[0]
        return values[0]
    tool=[n for n in names if n.endswith('/DotnetToolSettings.xml')]
    assert len(tool)==1
    settings=ElementTree.fromstring(archive.read(tool[0]))
    command=[e for e in settings.iter() if e.tag.rsplit('}',1)[-1]=='Command']
    assert len(command)==1
    out.write_text(json.dumps({'id':one('id'),'version':one('version'),'names':names,
      'command':command[0].attrib},sort_keys=True,separators=(',',':'))+'\n')
PY
PACKAGE_ID="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["id"])' "$WORK/metadata.json")"
PACKAGE_VERSION="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["version"])' "$WORK/metadata.json")"
[ "$PACKAGE_ID" = FS.GG.Telemetry.Host ] && [[ "$PACKAGE_VERSION" =~ ^0\.1\.[0-9]+$ ]] && ok "candidate identity is independently versioned Host $PACKAGE_VERSION" || bad "candidate identity is independently versioned Host 0.1.x" "$PACKAGE_ID $PACKAGE_VERSION"
python3 - "$WORK/metadata.json" <<'PY' && ok "tool settings bind the one supported command and entry point" || bad "tool settings bind the one supported command and entry point"
import json,sys
m=json.load(open(sys.argv[1])); c=m['command']
assert c.get('Name')=='fsgg-telemetry-host' and c.get('EntryPoint')=='FS.GG.Telemetry.Host.dll' and c.get('Runner')=='dotnet'
PY
python3 - "$WORK/metadata.json" <<'PY' && ok "package contains required runtime and project-reference closure" || bad "package contains required runtime and project-reference closure"
import json,sys
names=json.load(open(sys.argv[1]))['names']
required=['FS.GG.Telemetry.Host.dll','FS.GG.Telemetry.Dashboard.dll','FS.GG.Telemetry.Contracts.dll','FS.GG.Telemetry.Store.dll','FS.GG.Coord.Core.dll','Akka.dll','Akka.FSharp.dll','Microsoft.Data.Sqlite.dll','FS.GG.Telemetry.Host.deps.json','FS.GG.Telemetry.Host.runtimeconfig.json','README.md']
for item in required:
    assert any(n.endswith('/'+item) or n==item for n in names), item
PY
python3 - "$WORK/metadata.json" <<'PY' && ok "package excludes source, tests, mutable config, and secrets" || bad "package excludes source, tests, mutable config, and secrets"
import json,re,sys
names=json.load(open(sys.argv[1]))['names']
tool='tools/net10.0/linux-x64/'
assert sum(n==tool+'FS.GG.Telemetry.Host.dll' for n in names)==1
assert tool+'libe_sqlite3.so' in names
for n in names:
    lower=n.lower()
    if lower.startswith('tools/'):
        assert lower.startswith(tool), n
    assert '/runtimes/' not in lower, n
    assert not re.search(r'\.(fs|fsi|fsx|py|pyc|cs)$',lower), n
    assert not any(part in lower.split('/') for part in ('test','tests','fixture','fixtures','secret','secrets','config')), n
PY
python3 - "$WORK/metadata.json" "$ROOT/tests/standalone-telemetry-host-package/allowed-files.txt" <<'PY' \
  && ok "package payload matches the reviewed runtime allowlist" || bad "package payload matches the reviewed runtime allowlist"
import json,pathlib,sys
actual=json.load(open(sys.argv[1]))['names']
expected=pathlib.Path(sys.argv[2]).read_text().splitlines()
assert actual==expected, {'missing':sorted(set(expected)-set(actual)),'extra':sorted(set(actual)-set(expected))}
PY

mkdir -p "$WORK/feed" "$WORK/tools"
cp "$PACKAGE" "$WORK/feed/"
cat >"$WORK/NuGet.Config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear/><add key="candidate" value="$WORK/feed"/></packageSources></configuration>
EOF
if dotnet tool install "$PACKAGE_ID" --version "$PACKAGE_VERSION" --tool-path "$WORK/tools" --configfile "$WORK/NuGet.Config" --no-cache >"$WORK/install.log" 2>&1; then ok "fresh anonymous local package install succeeds"; else bad "fresh anonymous local package install succeeds" "$(tail -5 "$WORK/install.log" | tr '\n' ' ')"; fi
ENGINE="$WORK/tools/fsgg-telemetry-host"
[ -x "$ENGINE" ] && ok "installed package exposes fsgg-telemetry-host" || bad "installed package exposes fsgg-telemetry-host"
MISSING="$WORK/private/missing.json"
mkdir -m 700 "$WORK/private"
BEFORE="$(find "$WORK/private" -mindepth 1 -printf '%P\t%y\n' | sort)"
set +e
"$ENGINE" status --config "$MISSING" >"$WORK/status.out" 2>"$WORK/status.err"; STATUS_RC=$?
"$ENGINE" preflight --config "$MISSING" >"$WORK/preflight.out" 2>"$WORK/preflight.err"; PREFLIGHT_RC=$?
set -e
AFTER="$(find "$WORK/private" -mindepth 1 -printf '%P\t%y\n' | sort)"
[ "$STATUS_RC" -ne 0 ] && [ "$PREFLIGHT_RC" -ne 0 ] && [ "$BEFORE" = "$AFTER" ] && ok "installed status and preflight fail closed without provisioning" || bad "installed status and preflight fail closed without provisioning" "status=$STATUS_RC preflight=$PREFLIGHT_RC"

HOST_PRODUCTION_PROFILE="not-run"
export HOST_PRODUCTION_PROFILE
# shellcheck source-path=SCRIPTDIR
# shellcheck source=production.sh
. "$(cd "$(dirname "$0")" && pwd)/production.sh"
qualify_installed_host

if dotnet tool uninstall "$PACKAGE_ID" --tool-path "$WORK/tools" >"$WORK/uninstall.log" 2>&1; then ok "local tool uninstall succeeds"; else bad "local tool uninstall succeeds" "$(tail -5 "$WORK/uninstall.log" | tr '\n' ' ')"; fi

python3 - "$EVIDENCE" "$PACKAGE_SHA" "$PACKAGE_BYTES" "$PASS" "$FAIL" "$HOST_PRODUCTION_PROFILE" "$PACKAGE_VERSION" <<'PY'
import json,pathlib,platform,sys
out,sha,size,passed,failed,profile,version=sys.argv[1:]
pathlib.Path(out).write_text(json.dumps({'schema':'fsgg.telemetry-host-package-evidence/v1','packageId':'FS.GG.Telemetry.Host','version':version,'archiveSha256':sha,'archiveBytes':int(size),'passed':int(passed),'failed':int(failed),'runtime':platform.machine(),'productionStorage':profile,'syntheticTls':profile=='eligible-linux-x64'},sort_keys=True,separators=(',',':'))+'\n')
PY
printf 'standalone-telemetry-host-package fixture: %d passed, %d failed; evidence=%s\n' "$PASS" "$FAIL" "$EVIDENCE"
[ "$FAIL" -eq 0 ]
