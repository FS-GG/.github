#!/usr/bin/env bash

qualify_installed_host() {
  local durable_parent store config secrets tls producer_secret browser_key browser_hash port base pid rc
  durable_parent="${FSGG_HOST_FIXTURE_DURABLE_PARENT:-$HOME/.local/share/fsgg-telemetry-host-fixture}"
  case "$durable_parent" in
    "$WORK"*|"$ROOT"*|/tmp/*) bad "production fixture root is outside checkout and temporary storage" "$durable_parent"; return ;;
    /*) ;;
    *) bad "production fixture root is absolute" "$durable_parent"; return ;;
  esac
  if [ ! -e "$durable_parent" ]; then mkdir -m 700 "$durable_parent"; fi
  if [ ! -d "$durable_parent" ] || [ -L "$durable_parent" ] || [ ! -O "$durable_parent" ]; then
    bad "production fixture parent is an owned non-symlink directory" "$durable_parent"
    return
  fi
  store="$durable_parent/${PACKAGE_SHA:0:16}-$$"
  mkdir -m 700 "$store"
  set +e
  "$ENGINE" init --root "$store" --workspace package-workspace >"$WORK/init.out" 2>"$WORK/init.err"
  rc=$?
  set -e
  if [ "$rc" -ne 0 ]; then
    if [ -z "$(find "$store" -mindepth 1 -print -quit)" ]; then
      local filesystem
      filesystem="$(findmnt -n -o FSTYPE -T "$store" 2>/dev/null || printf unknown)"
      # shellcheck disable=SC2034 # consumed by the sourcing package fixture
      HOST_PRODUCTION_PROFILE="not-qualified:$filesystem"
      ok "installed production command refuses unqualified storage without state writes"
    else
      bad "installed production initialization either qualifies or refuses cleanly" "rc=$rc $(tail -3 "$WORK/init.err" | tr '\n' ' ')"
    fi
    return
  fi

  # shellcheck disable=SC2034 # consumed by the sourcing package fixture
  HOST_PRODUCTION_PROFILE="eligible-linux-x64"
  ok "installed production command qualifies and initializes the selected store"
  secrets="$WORK/host-private"
  tls="$secrets/tls"
  mkdir -m 700 "$secrets" "$tls"
  producer_secret="producer-${PACKAGE_SHA}-package-secret"; producer_secret="${producer_secret:0:48}"
  browser_key="$(python3 -c 'import base64,sys; print(base64.urlsafe_b64encode(bytes.fromhex(sys.argv[1])[:32]).decode().rstrip("="))' "$PACKAGE_SHA")"
  printf '%s\n' "$producer_secret" >"$secrets/producer.key"
  printf '%s\n' package-password >"$tls/password"
  openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj /CN=localhost \
    -addext subjectAltName=DNS:localhost -keyout "$tls/server.key" -out "$tls/server.crt" >/dev/null 2>&1
  openssl pkcs12 -export -out "$tls/server.pfx" -inkey "$tls/server.key" -in "$tls/server.crt" -passout pass:package-password >/dev/null 2>&1
  browser_hash="$(python3 -c 'import base64,hashlib,sys; value=sys.argv[1]; raw=base64.urlsafe_b64decode(value+"="*((4-len(value)%4)%4)); print(hashlib.sha256(raw).hexdigest())' "$browser_key")"
  printf '%s\n' "{\"schema\":\"fsgg.telemetry.browser-key/1\",\"algorithm\":\"sha256\",\"keyHash\":\"$browser_hash\"}" >"$secrets/browser-key.json"
  chmod 600 "$secrets/producer.key" "$secrets/browser-key.json" "$tls/server.key" "$tls/server.crt" "$tls/server.pfx" "$tls/password"
  port="$((24000 + $$ % 10000))"
  base="https://localhost:$port"
  config="$secrets/host.json"
  python3 - "$config" "$base" "$tls/server.pfx" "$tls/password" "$secrets/service.lock" "$store" "$secrets/producer.key" "$secrets/browser-key.json" <<'PY'
import json,pathlib,sys
out,base,pfx,password,lock,store,producer,browser=sys.argv[1:]
data={'Schema':'fsgg.telemetry.host-config/1','ListenUrl':base,'CertificatePath':pfx,'CertificatePasswordFile':password,'ServiceLockPath':lock,
 'Stores':[{'WorkspaceId':'package-workspace','Root':store}],
 'Credentials':[{'Reference':'package-producer','SecretFile':producer,'WorkspaceId':'package-workspace','ProducerId':'package-producer','StreamId':'runtime','Revoked':False}],
 'BrowserPrincipals':[{'PrincipalId':'package-browser','KeyHashFile':browser,'WorkspaceIds':['package-workspace'],'Revoked':False}],
 'BrowserSession':{'IdleSeconds':300,'AbsoluteSeconds':3600,'MaximumSessions':16,'LoginAttemptsPerMinute':8,'LoginAdmission':2,'QueryAdmission':2,'QueryTimeoutSeconds':10}}
pathlib.Path(out).write_text(json.dumps(data,separators=(',',':'))+'\n')
PY
  chmod 600 "$config"
  "$ENGINE" enroll-producer --config "$config" --reference package-producer --secret-file "$secrets/producer.key" --workspace package-workspace --producer package-producer --stream runtime >"$WORK/enroll.out" 2>"$WORK/enroll.err" \
    && ok "installed production command enrolls the declared producer" || bad "installed production command enrolls the declared producer" "$(cat "$WORK/enroll.err")"
  "$ENGINE" preflight --config "$config" >"$WORK/qualified-preflight.out" 2>"$WORK/qualified-preflight.err" \
    && ok "installed production preflight accepts the provisioned service" || bad "installed production preflight accepts the provisioned service" "$(cat "$WORK/qualified-preflight.err")"

  (exec "$ENGINE" serve --config "$config" >"$WORK/serve.out" 2>"$WORK/serve.err") & pid=$!
  for attempt in {1..30}; do
    curl --fail --silent --show-error --cacert "$tls/server.crt" -H "Authorization: Bearer $producer_secret" "$base/private/health" -o "$WORK/health.json" && break
    [ "$attempt" -lt 30 ] || { bad "installed production host reaches authenticated readiness" "$(tail -5 "$WORK/serve.err")"; kill "$pid" 2>/dev/null || true; return; }
    sleep 1
  done
  ok "installed production host reaches authenticated readiness"
  python3 - "$WORK/envelope.json" <<'PY'
import json,pathlib,sys
thread='22222222-2222-2222-2222-222222222222'; turn='33333333-3333-3333-3333-333333333333'
import hashlib
identity='passive-'+hashlib.sha256(('codex-rollout/1\x1f'+thread+'\x1f'+turn+'\x1fpackage-response').encode()).hexdigest()
fact={'kind':'provider-usage-observation/1','identity':identity,'itemId':None,'revision':1,'threadId':thread,'turnId':turn,'responseId':'package-response','provider':None,'model':None,'effort':None,'input':10,'cachedInput':None,'cacheWriteInput':3,'output':2,'reasoning':None,'total':12,'sourceInstance':'a'*64,'sourceGeneration':'source-g1','sourceDigest':'b'*64,'position':1,'capturedAt':'2026-10-18T12:00:00Z','occurredAt':None,'provenance':'codex-local-rollout/1','populationCoverage':'partial','inventoryCoverage':'unknown','terminalCoverage':'unknown','attributionCoverage':'unknown'}
payload={'schema':'fsgg.telemetry.ingest/1','ingestId':'package-batch','sourceIdentity':'package-source','generation':'g1','cursor':'1','eventCount':2,'events':[{'kind':'item','identity':'package-item','itemId':'package-item','revision':1},fact]}
envelope={'schema':'fsgg.telemetry.envelope/1','workspaceId':'package-workspace','producerId':'package-producer','streamId':'runtime','batchId':'package-batch','payload':payload}
pathlib.Path(sys.argv[1]).write_text(json.dumps(envelope,separators=(',',':'))+'\n')
PY
  code="$(curl --silent --show-error --cacert "$tls/server.crt" -H "Authorization: Bearer $producer_secret" -H 'Content-Type: application/json' --data-binary "@$WORK/envelope.json" --output "$WORK/receipt.json" --write-out '%{http_code}' "$base/v1/batches")"
  if [ "$code" = 202 ] && python3 - "$WORK/envelope.json" "$WORK/receipt.json" durably-received <<'PY'
import hashlib,json,sys
envelope=json.load(open(sys.argv[1])); receipt=json.load(open(sys.argv[2])); expected=sys.argv[3]
canonical=json.dumps(envelope,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()
assert receipt=={'schema':'fsgg.telemetry.receipt/1','workspaceId':'package-workspace','producerId':'package-producer','streamId':'runtime','batchId':'package-batch','digest':hashlib.sha256(canonical).hexdigest(),'status':expected,'code':None}
PY
  then ok "installed host returns the complete identity-bound durable receipt"; else bad "installed host returns the complete identity-bound durable receipt" "HTTP $code $(cat "$WORK/receipt.json")"; fi

  kill -KILL "$pid"
  wait "$pid" 2>/dev/null || true
  "$ENGINE" preflight --config "$config" >"$WORK/post-kill-preflight.out" 2>"$WORK/post-kill-preflight.err" \
    && ok "preflight recovers after an actual host SIGKILL" || bad "preflight recovers after an actual host SIGKILL" "$(cat "$WORK/post-kill-preflight.err")"
  (exec "$ENGINE" serve --config "$config" >"$WORK/restart.out" 2>"$WORK/restart.err") & pid=$!
  for attempt in {1..30}; do
    curl --fail --silent --show-error --cacert "$tls/server.crt" -H "Authorization: Bearer $producer_secret" "$base/private/health" -o "$WORK/restart-health.json" && break
    [ "$attempt" -lt 30 ] || { bad "installed host restarts after interruption" "$(tail -5 "$WORK/restart.err")"; kill "$pid" 2>/dev/null || true; return; }
    sleep 1
  done
  ok "installed host restarts after interruption"
  for attempt in {1..50}; do
    code="$(curl --silent --show-error --cacert "$tls/server.crt" -H "Authorization: Bearer $producer_secret" --output "$WORK/applied-receipt.json" --write-out '%{http_code}' "$base/v1/receipts/package-batch")"
    if [ "$code" = 200 ] && python3 - "$WORK/applied-receipt.json" <<'PY'
import json,sys
r=json.load(open(sys.argv[1])); digest=r['digest']; assert r['schema']=='fsgg.telemetry.receipt/1' and r['workspaceId']=='package-workspace' and r['producerId']=='package-producer' and r['streamId']=='runtime' and r['batchId']=='package-batch' and r['status']=='applied' and r['code'] is None and len(digest)==64 and digest==digest.lower() and all(c in '0123456789abcdef' for c in digest)
PY
    then break; fi
    [ "$attempt" -lt 50 ] || { bad "restarted host exposes the applied receipt" "HTTP $code"; kill "$pid" 2>/dev/null || true; return; }
    sleep 0.1
  done
  ok "restarted host exposes the applied receipt"

  python3 - "$WORK/envelope.json" "$WORK/passive-correction.json" <<'PY_PASSIVE'
import json,pathlib,sys
value=json.load(open(sys.argv[1])); value['batchId']='package-passive-correction'
payload=value['payload']; payload['ingestId']=value['batchId']; payload['cursor']='2'; payload['eventCount']=1
fact=payload['events'][1]; fact.update(revision=2,input=20,total=22,sourceDigest='c'*64,position=2)
payload['events']=[fact]
pathlib.Path(sys.argv[2]).write_text(json.dumps(value,separators=(',',':'))+'\n')
PY_PASSIVE
  code="$(curl --silent --show-error --cacert "$tls/server.crt" -H "Authorization: Bearer $producer_secret" -H 'Content-Type: application/json' --data-binary "@$WORK/passive-correction.json" --output "$WORK/correction-submit.json" --write-out '%{http_code}' "$base/v1/batches")"
  case "$code" in 200|202) ;; *) bad "installed host accepts passive correction" "HTTP $code"; kill "$pid" 2>/dev/null || true; return ;; esac
  for attempt in {1..50}; do
    code="$(curl --silent --show-error --cacert "$tls/server.crt" -H "Authorization: Bearer $producer_secret" --output "$WORK/correction-receipt.json" --write-out '%{http_code}' "$base/v1/receipts/package-passive-correction")"
    if [ "$code" = 200 ] && python3 - "$WORK/correction-receipt.json" <<'PY_PASSIVE'
import json,sys
value=json.load(open(sys.argv[1])); assert value['status']=='applied' and value['code'] is None and value['batchId']=='package-passive-correction'
PY_PASSIVE
    then break; fi
    [ "$attempt" -lt 50 ] || { bad "installed passive correction applies" "HTTP $code"; kill "$pid" 2>/dev/null || true; return; }
    sleep 0.1
  done
  assert_passive_state() {
    python3 - "$1/telemetry.sqlite3" <<'PY_PASSIVE'
import json,sqlite3,sys
with sqlite3.connect('file:'+sys.argv[1]+'?mode=ro',uri=True) as db:
    rows=db.execute("SELECT canonical FROM ingest_facts WHERE kind='provider-usage-observation/1'").fetchall()
    assert len(rows)==1; fact=json.loads(rows[0][0])
    assert fact['revision']==2 and fact['input']==20 and fact['total']==22 and fact['cacheWriteInput']==3
    assert fact['cachedInput'] is None and fact['reasoning'] is None and fact['itemId'] is None and fact['model'] is None and 'cost' not in fact
    assert fact['populationCoverage']=='partial' and fact['attributionCoverage']=='unknown'
    assert db.execute("SELECT count(*) FROM corrections WHERE kind='provider-usage-observation/1'").fetchone()[0]==1
    for table in ('usage_observations','runtime_turn_usage','budget_dirty_items'):
        assert db.execute('SELECT count(*) FROM '+table).fetchone()[0]==0,table
    assert db.execute('SELECT count(*) FROM items').fetchone()[0]==1
    assert db.execute('SELECT batch,state,code FROM transport_receipts ORDER BY batch').fetchall()==[('package-batch','applied',None),('package-passive-correction','applied',None)]
PY_PASSIVE
  }
  assert_passive_state "$store" && ok "installed HTTPS passive correction stays canonical-only with absent counters unknown" || bad "installed HTTPS passive projection and item totals"

  local browser_spki
  browser_spki="$(openssl x509 -in "$tls/server.crt" -pubkey -noout | openssl pkey -pubin -outform DER | openssl dgst -sha256 -binary | openssl base64)"
  (cd "$ROOT/tests/FS.GG.Telemetry.Browser.Tests" && FSGG_BROWSER_BASE_URL="$base" FSGG_BROWSER_PRINCIPAL_ID=package-browser FSGG_BROWSER_ACCESS_KEY="$browser_key" FSGG_BROWSER_KNOWN_WORKSPACE=package-workspace FSGG_BROWSER_UNAVAILABLE_WORKSPACE=outside-workspace FSGG_BROWSER_CERTIFICATE_SPKI="$browser_spki" npm run test:browser) >"$WORK/browser.out" 2>"$WORK/browser.err" \
    && ok "installed host passes the pinned-certificate HTTPS browser isolation journey" || bad "installed host passes the pinned-certificate HTTPS browser isolation journey" "$(tail -10 "$WORK/browser.err")"
  node "$ROOT/tests/standalone-telemetry-host-package/installed-browser.js" "$ROOT/tests/FS.GG.Telemetry.Browser.Tests/node_modules/playwright" "$base" package-browser "$browser_key" package-workspace package-item "$browser_spki" 2 >"$WORK/installed-browser.out" 2>"$WORK/installed-browser.err" \
    && ok "installed browser shows the applied item and private corrected unassigned usage" || bad "installed browser item and corrected unassigned usage" "$(tail -10 "$WORK/installed-browser.err")"
  kill -TERM "$pid"
  rc=0
  wait "$pid" || rc=$?
  [ "$rc" -eq 0 ] && ok "installed host performs bounded graceful SIGTERM shutdown" || bad "installed host performs bounded graceful SIGTERM shutdown" "rc=$rc"

  "$ENGINE" backup --config "$config" --output "$WORK/backup" >"$WORK/backup.out" 2>"$WORK/backup.err" \
    && ok "installed host creates a coherent offline backup" || bad "installed host creates a coherent offline backup" "$(cat "$WORK/backup.err")"
  local restored_root restored_config restored_port restored_base
  restored_root="$durable_parent/restored-${PACKAGE_SHA:0:12}-$$"
  "$ENGINE" restore --config "$config" --input "$WORK/backup" --state-root "$restored_root" >"$WORK/restore.out" 2>"$WORK/restore.err" \
    && ok "installed host restores into a fresh qualified root" || bad "installed host restores into a fresh qualified root" "$(cat "$WORK/restore.err")"
  restored_port="$((port + 1))"; restored_base="https://localhost:$restored_port"; restored_config="$secrets/restored-host.json"
  python3 - "$config" "$restored_config" "$restored_base" "$restored_root/package-workspace" "$secrets/restored.lock" <<'PY'
import json,pathlib,sys
source,out,base,root,lock=sys.argv[1:]; data=json.load(open(source)); data['ListenUrl']=base; data['Stores'][0]['Root']=root; data['ServiceLockPath']=lock
pathlib.Path(out).write_text(json.dumps(data,separators=(',',':'))+'\n')
PY
  chmod 600 "$restored_config"
  (exec "$ENGINE" serve --config "$restored_config" >"$WORK/restored-serve.out" 2>"$WORK/restored-serve.err") & pid=$!
  for attempt in {1..30}; do
    curl --fail --silent --show-error --cacert "$tls/server.crt" -H "Authorization: Bearer $producer_secret" "$restored_base/private/health" -o "$WORK/restored-health.json" && break
    [ "$attempt" -lt 30 ] || { bad "restored installed host reaches readiness" "$(tail -5 "$WORK/restored-serve.err")"; kill "$pid" 2>/dev/null || true; return; }
    sleep 1
  done
  code="$(curl --silent --show-error --cacert "$tls/server.crt" -H "Authorization: Bearer $producer_secret" --output "$WORK/restored-receipt.json" --write-out '%{http_code}' "$restored_base/v1/receipts/package-batch")"
  [ "$code" = 200 ] && cmp -s "$WORK/applied-receipt.json" "$WORK/restored-receipt.json" && ok "restored host retains the exact applied receipt" || bad "restored host retains the exact applied receipt" "HTTP $code"
  node "$ROOT/tests/standalone-telemetry-host-package/installed-browser.js" "$ROOT/tests/FS.GG.Telemetry.Browser.Tests/node_modules/playwright" "$restored_base" package-browser "$browser_key" package-workspace package-item "$browser_spki" 2 >"$WORK/restored-browser.out" 2>"$WORK/restored-browser.err" \
    && ok "restored browser retains the item and private corrected unassigned usage" || bad "restored browser item and corrected unassigned usage" "$(tail -10 "$WORK/restored-browser.err")"
  if assert_passive_state "$restored_root/package-workspace"; then
    # shellcheck disable=SC2034 # consumed by the package evidence writer
    HOST_PASSIVE_RECEIPT_QUALIFIED=true
    ok "restored host preserves exact passive correction outside item totals"
  else
    bad "restored passive state changed"
  fi
  kill -TERM "$pid"; rc=0; wait "$pid" || rc=$?
  [ "$rc" -eq 0 ] && ok "restored installed host shuts down cleanly" || bad "restored installed host shuts down cleanly" "rc=$rc"
}
