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
payload={'schema':'fsgg.telemetry.ingest/1','ingestId':'package-batch','sourceIdentity':'package-source','generation':'g1','cursor':'1','eventCount':1,'events':[{'kind':'item','identity':'package-item','itemId':'package-item','revision':1}]}
envelope={'schema':'fsgg.telemetry.envelope/1','workspaceId':'package-workspace','producerId':'package-producer','streamId':'runtime','batchId':'package-batch','payload':payload}
pathlib.Path(sys.argv[1]).write_text(json.dumps(envelope,separators=(',',':'))+'\n')
PY
  code="$(curl --silent --show-error --cacert "$tls/server.crt" -H "Authorization: Bearer $producer_secret" -H 'Content-Type: application/json' --data-binary "@$WORK/envelope.json" --output "$WORK/receipt.json" --write-out '%{http_code}' "$base/v1/batches")"
  [ "$code" = 202 ] && ok "installed host accepts one scoped receipt envelope" || bad "installed host accepts one scoped receipt envelope" "HTTP $code $(cat "$WORK/receipt.json")"

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

  local browser_spki
  browser_spki="$(openssl x509 -in "$tls/server.crt" -pubkey -noout | openssl pkey -pubin -outform DER | openssl dgst -sha256 -binary | openssl base64)"
  (cd "$ROOT/tests/FS.GG.Telemetry.Browser.Tests" && FSGG_BROWSER_BASE_URL="$base" FSGG_BROWSER_PRINCIPAL_ID=package-browser FSGG_BROWSER_ACCESS_KEY="$browser_key" FSGG_BROWSER_KNOWN_WORKSPACE=package-workspace FSGG_BROWSER_UNAVAILABLE_WORKSPACE=outside-workspace FSGG_BROWSER_CERTIFICATE_SPKI="$browser_spki" npm run test:browser) >"$WORK/browser.out" 2>"$WORK/browser.err" \
    && ok "installed host passes the pinned-certificate HTTPS browser isolation journey" || bad "installed host passes the pinned-certificate HTTPS browser isolation journey" "$(tail -10 "$WORK/browser.err")"
  kill -TERM "$pid"
  rc=0
  wait "$pid" || rc=$?
  [ "$rc" -eq 0 ] && ok "installed host performs bounded graceful SIGTERM shutdown" || bad "installed host performs bounded graceful SIGTERM shutdown" "rc=$rc"

  "$ENGINE" backup --config "$config" --output "$WORK/backup" >"$WORK/backup.out" 2>"$WORK/backup.err" \
    && ok "installed host creates a coherent offline backup" || bad "installed host creates a coherent offline backup" "$(cat "$WORK/backup.err")"
  "$ENGINE" restore --config "$config" --input "$WORK/backup" --state-root "$durable_parent/restored-${PACKAGE_SHA:0:12}-$$" >"$WORK/restore.out" 2>"$WORK/restore.err" \
    && ok "installed host restores into a fresh qualified root" || bad "installed host restores into a fresh qualified root" "$(cat "$WORK/restore.err")"
}
