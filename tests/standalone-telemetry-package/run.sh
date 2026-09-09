#!/usr/bin/env bash
# Source-free qualification of one exact FS.GG.Coord.Cli tool package.
set -uo pipefail

if [ "$#" -ne 2 ]; then
  echo "usage: $0 /absolute/FS.GG.Coord.Cli.VERSION.nupkg /absolute/evidence.json" >&2
  exit 2
fi

PACKAGE="$1"
EVIDENCE="$2"
case "$PACKAGE:$EVIDENCE" in /*:/*) ;; *) echo "package and evidence paths must be absolute" >&2; exit 2;; esac
[ -f "$PACKAGE" ] || { echo "candidate package is missing: $PACKAGE" >&2; exit 2; }

WORK="$(mktemp -d "${TMPDIR:-/tmp}/standalone-telemetry-package.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT
export DOTNET_CLI_HOME="$WORK/dotnet-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export NUGET_PACKAGES="$WORK/nuget-packages"
export NUGET_HTTP_CACHE_PATH="$WORK/nuget-http-cache"
mkdir -p "$DOTNET_CLI_HOME" "$NUGET_PACKAGES" "$NUGET_HTTP_CACHE_PATH"
PASS=0
FAIL=0
CHECKS="$WORK/checks.tsv"
: > "$CHECKS"
ok() { printf 'PASS  %s\n' "$1"; printf 'pass\t%s\n' "$1" >> "$CHECKS"; PASS=$((PASS+1)); }
bad() { printf 'FAIL  %s\n' "$1" >&2; [ -z "${2:-}" ] || printf '    | %s\n' "$2" >&2; printf 'fail\t%s\n' "$1" >> "$CHECKS"; FAIL=$((FAIL+1)); }

PACKAGE_SHA="$(sha256sum "$PACKAGE" | cut -d' ' -f1)"
PACKAGE_BYTES="$(stat -c %s "$PACKAGE")"
# Same-method public 0.87.0 baseline recorded by the source-free package probe.
BASELINE_PACKAGE_SHA="b1ea5d2bb87b6eeeec59c172e50fb824fe7da891235d66910557acc7eb18ff58"
BASELINE_PACKAGE_BYTES=30958077
BASELINE_INSTALLED_BYTES=125906226
readarray -t META < <(python3 - "$PACKAGE" <<'PY'
import sys, zipfile, xml.etree.ElementTree as ET
with zipfile.ZipFile(sys.argv[1]) as package:
    names = package.namelist()
    nuspecs = [name for name in names if name.endswith('.nuspec')]
    assert len(nuspecs) == 1, 'package must contain exactly one nuspec'
    root = ET.fromstring(package.read(nuspecs[0]))
    def one(local):
        values = [node.text for node in root.iter() if node.tag.rsplit('}', 1)[-1] == local]
        assert len(values) == 1 and values[0], f'missing {local}'
        return values[0]
    print(one('id'))
    print(one('version'))
    print('\n'.join(names))
PY
) || { echo "candidate package metadata is unreadable" >&2; exit 2; }
PACKAGE_ID="${META[0]:-}"
PACKAGE_VERSION="${META[1]:-}"
PACKAGE_LIST="$(printf '%s\n' "${META[@]:2}")"
[ "$PACKAGE_ID" = "FS.GG.Coord.Cli" ] && ok "candidate identity is FS.GG.Coord.Cli $PACKAGE_VERSION" || bad "candidate package identity is exact" "$PACKAGE_ID"

for required in FS.GG.Telemetry.Contracts.dll FS.GG.Telemetry.Client.dll FS.GG.Telemetry.Store.dll; do
  printf '%s\n' "$PACKAGE_LIST" | grep -q "/$required$" && ok "package carries $required" || bad "package carries $required"
done
if printf '%s\n' "$PACKAGE_LIST" | grep -Eq '/FS\.GG\.Telemetry\.Host\.dll$|/Akka(\.FSharp)?\.dll$'; then
  bad "package excludes Host and Akka runtime"
else
  ok "package excludes Host and Akka runtime"
fi
if printf '%s\n' "$PACKAGE_LIST" | grep -Eqi '\.(py|pyc|js|node)$|(^|/)python([^/]*)(/|$)|(^|/)node_modules/'; then
  bad "package adds no Python or Node runtime payload"
else
  ok "package adds no Python or Node runtime payload"
fi

FEED="$WORK/feed"
TOOLS="$WORK/tools"
mkdir -p "$FEED" "$TOOLS"
cp "$PACKAGE" "$FEED/"
cat > "$WORK/NuGet.Config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear/><add key="candidate" value="$FEED"/></packageSources></configuration>
EOF
INSTALL_LOG="$WORK/install.log"
if dotnet tool install "$PACKAGE_ID" --version "$PACKAGE_VERSION" --tool-path "$TOOLS" --configfile "$WORK/NuGet.Config" --no-cache >"$INSTALL_LOG" 2>&1; then
  ok "anonymous local-only tool install succeeds"
else
  bad "anonymous local-only tool install succeeds" "$(tail -5 "$INSTALL_LOG" | tr '\n' ' ')"
fi
ENGINE="$TOOLS/fsgg-coord-engine"
[ -x "$ENGINE" ] && ok "installed package exposes fsgg-coord-engine" || bad "installed package exposes fsgg-coord-engine"
INSTALLED_BYTES="$(find "$TOOLS" -type f -printf '%s\n' | awk '{n+=$1} END {print n+0}')"
PACKAGE_DELTA=$((PACKAGE_BYTES-BASELINE_PACKAGE_BYTES))
INSTALLED_DELTA=$((INSTALLED_BYTES-BASELINE_INSTALLED_BYTES))
[ "$PACKAGE_DELTA" -le $((10*1024*1024)) ] && ok "compressed growth is within 10 MiB of the public baseline" || bad "compressed growth exceeds 10 MiB from the public baseline"
[ "$INSTALLED_DELTA" -le $((30*1024*1024)) ] && ok "installed growth is within 30 MiB of the public baseline" || bad "installed growth exceeds 30 MiB from the public baseline"

WORKSPACE="$WORK/generated-workspace"
PRIVATE="$WORK/private"
CONFIG="$PRIVATE/telemetry.json"
mkdir -p "$WORKSPACE" "$PRIVATE"
chmod 700 "$WORKSPACE" "$PRIVATE"
git -C "$WORKSPACE" init -q
BEFORE="$(find "$WORKSPACE" "$PRIVATE" -mindepth 1 -printf '%P\t%y\n' | sort)"
STATUS_OUT="$(cd "$WORKSPACE" && "$ENGINE" telemetry workspace status --config "$CONFIG" --repository FS-GG/package-fixture 2>"$WORK/status.err")"; STATUS_RC=$?
AFTER="$(find "$WORKSPACE" "$PRIVATE" -mindepth 1 -printf '%P\t%y\n' | sort)"
if [ "$STATUS_RC" -eq 0 ] && printf '%s' "$STATUS_OUT" | grep -q '"status":"unconfigured"' && [ "$BEFORE" = "$AFTER" ]; then
  ok "unconfigured status is read-only in a source-free workspace"
else
  bad "unconfigured status is read-only in a source-free workspace" "rc=$STATUS_RC out=$STATUS_OUT"
fi
[ ! -d "$WORKSPACE/src" ] && ! find "$WORKSPACE" -type f -name '*.fs' -print -quit | grep -q . && ok "generated workspace contains no runtime source checkout" || bad "generated workspace contains runtime source"

FAKEBIN="$WORK/fakebin"
mkdir -p "$FAKEBIN"
cat > "$FAKEBIN/codex" <<'EOF'
#!/bin/sh
printf '%s\n' '{"type":"thread.started","thread_id":"package-thread"}'
printf '%s\n' '{"type":"turn.completed","turn_id":"package-turn","usage":{"input_tokens":2,"cached_input_tokens":0,"output_tokens":1}}'
exit 37
EOF
chmod 700 "$FAKEBIN/codex"
ASSIGNMENT="$PRIVATE/assignment.json"
printf '%s\n' '{"schema":"fsgg.telemetry.codex-assignment/1","featureId":"L1-PACKAGE","itemId":"L1-PACKAGE","attemptId":"package-1","parentAttemptId":null,"producerStream":"package-fixture"}' > "$ASSIGNMENT"
chmod 600 "$ASSIGNMENT"

# Native exit authority is checked once before activation and again through the accepted path below.
(cd "$WORKSPACE" && PATH="$FAKEBIN:$PATH" "$ENGINE" telemetry runtime codex-exec --assignment "$ASSIGNMENT" --config "$CONFIG" --repository FS-GG/package-fixture -- --json --ephemeral synthetic >"$WORK/native-unconfigured.out" 2>"$WORK/native-unconfigured.err"); NATIVE_UNCONFIGURED_RC=$?
[ "$NATIVE_UNCONFIGURED_RC" -eq 37 ] && ok "telemetry failure leaves native work exit 37 unchanged" || bad "telemetry failure changed native work exit" "rc=$NATIVE_UNCONFIGURED_RC"

DURABLE_PARENT="${FSGG_PACKAGE_FIXTURE_DURABLE_PARENT:-$HOME/.fsgg-package-fixture-roots}"
case "$DURABLE_PARENT" in
  "$WORK"*|"$(cd "$(dirname "$0")/../.." && pwd)"*|/tmp/*) echo "durable fixture parent must be user-private and outside repo/temp" >&2; exit 2;;
  /*) ;;
  *) echo "durable fixture parent must be absolute" >&2; exit 2;;
esac
mkdir -p "$DURABLE_PARENT"
chmod 700 "$DURABLE_PARENT"
STORE="$DURABLE_PARENT/l1-package-${PACKAGE_SHA:0:16}-$$"
mkdir "$STORE"
chmod 700 "$STORE"
PROFILE="not-qualified"
PROFILE_REASON="production-assessor-refused"
ACTIVATE_LOG="$WORK/activate.log"
if (cd "$WORKSPACE" && "$ENGINE" telemetry workspace activate-local --config "$CONFIG" --workspace package-workspace --producer package-producer --stream runtime --store-root "$STORE" --repository FS-GG/package-fixture >"$ACTIVATE_LOG" 2>&1); then
  PROFILE="eligible-local"
  PROFILE_REASON="production-assessor-accepted"
  ok "production assessor accepts the explicit user-private local root"

  (cd "$WORKSPACE" && PATH="$FAKEBIN:$PATH" "$ENGINE" telemetry runtime codex-exec --assignment "$ASSIGNMENT" --config "$CONFIG" --repository FS-GG/package-fixture -- --json --ephemeral synthetic >"$WORK/native-local.out" 2>"$WORK/native-local.err"); NATIVE_LOCAL_RC=$?
  [ "$NATIVE_LOCAL_RC" -eq 37 ] && ok "accepted telemetry path leaves native work exit 37 unchanged" || bad "accepted telemetry path changed native work exit" "rc=$NATIVE_LOCAL_RC"
  DRAIN_OUT="$(cd "$WORKSPACE" && "$ENGINE" telemetry workspace drain --config "$CONFIG" --repository FS-GG/package-fixture 2>"$WORK/drain.err")"; DRAIN_RC=$?
  [ "$DRAIN_RC" -eq 0 ] && ok "packaged workspace drain succeeds" || bad "packaged workspace drain succeeds" "rc=$DRAIN_RC"
  LOCAL_STATUS="$(cd "$WORKSPACE" && "$ENGINE" telemetry workspace status --config "$CONFIG" --repository FS-GG/package-fixture 2>"$WORK/local-status.err")"
  printf '%s' "$LOCAL_STATUS" | grep -q '"pending":0' && ok "observed synthetic command drains with no pending batch" || bad "observed synthetic command drains with no pending batch" "$LOCAL_STATUS"
  RECONCILE="$(cd "$WORKSPACE" && "$ENGINE" telemetry store reconcile --store-root "$STORE" --item L1-PACKAGE 2>"$WORK/reconcile.err")"; RECONCILE_RC=$?
  [ "$RECONCILE_RC" -eq 0 ] && printf '%s' "$RECONCILE" | grep -q '"matched":1' && ok "native expected and terminal facts reconcile" || bad "native expected and terminal facts reconcile" "rc=$RECONCILE_RC out=$RECONCILE"
  RECEIPT_COUNTS="$(python3 - "$STORE/telemetry.sqlite3" <<'PY'
import sqlite3, sys
with sqlite3.connect(f'file:{sys.argv[1]}?mode=ro', uri=True) as db:
    total, applied = db.execute("select count(*),sum(case when state='applied' then 1 else 0 end) from transport_receipts").fetchone()
print(f'{total}:{applied or 0}')
PY
)"
  case "$RECEIPT_COUNTS" in [1-9]*:*) APPLIED="${RECEIPT_COUNTS#*:}"; [ "$APPLIED" -gt 0 ] && ok "durable receipt index contains applied observations" || bad "receipt index has no applied observation";; *) bad "durable receipt index contains observations" "$RECEIPT_COUNTS";; esac
else
  if grep -Eqi "overlay|network|memory|not durable|unverified|filesystem" "$ACTIVATE_LOG" && [ ! -e "$CONFIG" ] && [ -z "$(find "$STORE" -mindepth 1 -print -quit)" ]; then
    ok "production assessor refuses the current unqualified filesystem without store/config writes"
  else
    bad "local activation either qualifies or fails closed for filesystem placement" "$(tail -5 "$ACTIVATE_LOG" | tr '\n' ' ')"
  fi
fi

STORE_BEFORE_UNINSTALL="$(cd "$STORE" && find . -type f -print0 | sort -z | xargs -0 -r sha256sum | sha256sum | cut -d' ' -f1)"
if dotnet tool uninstall "$PACKAGE_ID" --tool-path "$TOOLS" >"$WORK/uninstall.log" 2>&1; then
  ok "local tool uninstall succeeds"
else
  bad "local tool uninstall succeeds" "$(tail -5 "$WORK/uninstall.log" | tr '\n' ' ')"
fi
STORE_AFTER_UNINSTALL="$(cd "$STORE" && find . -type f -print0 | sort -z | xargs -0 -r sha256sum | sha256sum | cut -d' ' -f1)"
[ "$STORE_BEFORE_UNINSTALL" = "$STORE_AFTER_UNINSTALL" ] && [ -d "$STORE" ] && ok "tool uninstall preserves telemetry data" || bad "tool uninstall altered telemetry data"

mkdir -p "$(dirname "$EVIDENCE")"
python3 - "$EVIDENCE" "$PACKAGE_SHA" "$PACKAGE_BYTES" "$INSTALLED_BYTES" "$PACKAGE_VERSION" "$PROFILE" "$PROFILE_REASON" "$PASS" "$FAIL" "$CHECKS" "$BASELINE_PACKAGE_SHA" "$BASELINE_PACKAGE_BYTES" "$BASELINE_INSTALLED_BYTES" "$PACKAGE_DELTA" "$INSTALLED_DELTA" <<'PY'
import json, pathlib, sys
out, digest, packed, installed, version, profile, reason, passed, failed, checks, baseline_digest, baseline_packed, baseline_installed, packed_delta, installed_delta = sys.argv[1:]
rows=[]
for line in pathlib.Path(checks).read_text().splitlines():
    result,name=line.split('\t',1); rows.append({'name':name,'result':result})
document={'schema':'fsgg.telemetry.package-fixture-evidence/1','package':{'id':'FS.GG.Coord.Cli','version':version,'sha256':digest,'compressedBytes':int(packed),'installedBytes':int(installed),'compressedDeltaBytes':int(packed_delta),'installedDeltaBytes':int(installed_delta)},'baseline':{'version':'0.87.0','sha256':baseline_digest,'compressedBytes':int(baseline_packed),'installedBytes':int(baseline_installed),'method':'public-source isolated tool install with fresh package cache and no-cache'},'profile':{'result':profile,'reason':reason},'limits':{'compressedIncreaseBytes':10485760,'installedIncreaseBytes':31457280,'responseBytes':4096,'clientAttempts':5},'checks':rows,'summary':{'passed':int(passed),'failed':int(failed)},'claims':{'sourceFreeInstall':True,'publishedRelease':False,'ssdOrPowerLossQualified':False,'mainInstalled':False,'remoteHttpsRehearsedHere':False}}
data=(json.dumps(document,separators=(',',':'),sort_keys=True)+'\n').encode()
assert len(data)<=16384
pathlib.Path(out).write_bytes(data)
PY
chmod 600 "$EVIDENCE"
rm -rf "$STORE"
printf 'standalone-telemetry-package fixture: %d passed, %d failed; profile=%s; evidence=%s\n' "$PASS" "$FAIL" "$PROFILE" "$EVIDENCE"
[ "$FAIL" -eq 0 ]
