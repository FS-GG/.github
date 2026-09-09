#!/usr/bin/env bash
# Sourced by run.sh; exercises the installed workspace adapter over trusted localhost TLS.

qualify_remote() {
  local fixture_dir tls config spool state mode log token port
  REMOTE_PID=""
  fixture_dir="$WORK/remote"
  tls="$fixture_dir/tls"
  config="$fixture_dir/private/telemetry.json"
  spool="$fixture_dir/private/spool"
  state="$fixture_dir/receiver-state.json"
  mode="$fixture_dir/receiver-mode"
  log="$fixture_dir/receiver.log"
  token="package-fixture-token"
  mkdir -p "$tls" "$fixture_dir/private"
  chmod 700 "$fixture_dir/private"
  : > "$log"
  chmod 600 "$log"

  openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj '/CN=FS.GG package fixture CA' \
    -addext 'basicConstraints=critical,CA:TRUE' -addext 'keyUsage=critical,keyCertSign,cRLSign' \
    -keyout "$tls/ca.key" -out "$tls/ca.crt" >/dev/null 2>&1
  openssl req -newkey rsa:2048 -nodes -subj '/CN=localhost' \
    -keyout "$tls/server.key" -out "$tls/server.csr" >/dev/null 2>&1
  printf '%s\n' 'subjectAltName=DNS:localhost' 'extendedKeyUsage=serverAuth' > "$tls/server.ext"
  openssl x509 -req -days 1 -in "$tls/server.csr" -CA "$tls/ca.crt" -CAkey "$tls/ca.key" \
    -CAcreateserial -extfile "$tls/server.ext" -out "$tls/server.crt" >/dev/null 2>&1
  chmod 600 "$tls/ca.key" "$tls/server.key"
  export SSL_CERT_FILE="$tls/ca.crt"
  export FSGG_TELEMETRY_CREDENTIAL_PACKAGE_TOKEN="$token"

  port="$(python3 - <<'PY'
import socket
with socket.socket() as sock:
    sock.bind(('127.0.0.1', 0))
    print(sock.getsockname()[1])
PY
)"
  start_receiver() {
    printf '%s\n' "${1:-normal}" > "$mode"
    python3 "$REMOTE_RECEIVER" --port "$port" --cert "$tls/server.crt" --key "$tls/server.key" \
      --state "$state" --mode "$mode" --log "$log" --token "$token" \
      --scope package-remote-workspace package-remote-producer runtime >"$fixture_dir/server.out" 2>"$fixture_dir/server.err" &
    REMOTE_PID=$!
    local ready=0
    for _ in $(seq 1 50); do
      if python3 - "$port" "$tls/ca.crt" <<'PY' >/dev/null 2>&1
import socket, ssl, sys
context=ssl.create_default_context(cafile=sys.argv[2])
with socket.create_connection(('127.0.0.1',int(sys.argv[1])),timeout=.2) as raw:
    with context.wrap_socket(raw,server_hostname='localhost'):
        pass
PY
      then ready=1; break; fi
      sleep .05
    done
    [ "$ready" -eq 1 ] || { bad "synthetic TLS receiver starts with trusted localhost identity" "$(tail -5 "$fixture_dir/server.err" | tr '\n' ' ')"; return 1; }
  }
  stop_receiver() {
    if [ -n "$REMOTE_PID" ] && kill -0 "$REMOTE_PID" 2>/dev/null; then
      kill "$REMOTE_PID" 2>/dev/null || true
      wait "$REMOTE_PID" 2>/dev/null || true
    fi
    REMOTE_PID=""
  }
  trap 'stop_receiver; rm -rf "$WORK"' EXIT

  start_receiver normal || return
  if "$ENGINE" telemetry workspace activate-remote --config "$config" \
      --workspace package-remote-workspace --producer package-remote-producer --stream runtime \
      --repository FS-GG/package-remote --endpoint "https://localhost:$port/" \
      --credential-reference package-token --spool-root "$spool" >"$fixture_dir/activate.out" 2>"$fixture_dir/activate.err"; then
    ok "installed workspace adapter activates a private remote HTTPS destination"
  else
    bad "installed workspace adapter activates a private remote HTTPS destination" "$(tail -5 "$fixture_dir/activate.err" | tr '\n' ' ')"
    stop_receiver
    return
  fi

  make_batch() {
    local batch="$1" output="$2" batch_schema
    batch_schema="fsgg.telemetry.""ingest/1"
    printf '{"schema":"%s","ingestId":"%s","sourceIdentity":"package-fixture","generation":"g1","cursor":"%s","eventCount":1,"events":[{"kind":"item","identity":"%s","itemId":"%s","revision":1}]}\n' \
      "$batch_schema" "$batch" "$batch" "$batch" "$batch" > "$output"
  }
  submit() {
    "$ENGINE" telemetry workspace submit --config "$config" --repository FS-GG/package-remote --input "$1"
  }
  drain() {
    "$ENGINE" telemetry workspace drain --config "$config" --repository FS-GG/package-remote
  }
  ready_count() { find "$spool" -maxdepth 1 -type f -name '*.ready' | wc -l; }
  outcome_count() { find "$spool/outcomes" -maxdepth 1 -type f -name '*.json' 2>/dev/null | wc -l; }

  local input="$fixture_dir/success.json"
  make_batch package-success "$input"
  if submit "$input" >"$fixture_dir/success.out" 2>"$fixture_dir/success.err" \
     && [ "$(ready_count)" -eq 0 ] && [ "$(outcome_count)" -eq 1 ] \
     && python3 - "$spool/outcomes" "$state" <<'PY'
import json, pathlib, sys
outcomes=list(pathlib.Path(sys.argv[1]).glob('*.json'))
outcome=json.loads(outcomes[0].read_text())
state=json.load(open(sys.argv[2]))
assert set(outcome)=={'schema','batchId','digest','status','code'}
assert outcome['schema']=='fsgg.telemetry.workspace-outcome/1'
assert outcome['batchId']=='package-success' and outcome['digest']==state['package-success']
assert outcome['status']=='applied' and outcome['code'] is None
PY
  then
    ok "remote submit persists a matching applied outcome and clears its spool entry"
  else
    bad "remote submit persists a matching applied outcome and clears its spool entry"
  fi

  printf '%s\n' drop-exit > "$mode"
  make_batch package-lost-response "$fixture_dir/lost.json"
  submit "$fixture_dir/lost.json" >"$fixture_dir/lost.out" 2>"$fixture_dir/lost.err"; local lost_rc=$?
  wait "$REMOTE_PID" 2>/dev/null || true
  REMOTE_PID=""
  if [ "$lost_rc" -ne 0 ] && [ "$(ready_count)" -eq 1 ] && grep -q '"kind":"response-dropped"' "$log"; then
    ok "lost response after receiver acceptance retains the exact ready envelope"
  else
    bad "lost response after receiver acceptance retains the exact ready envelope" "rc=$lost_rc ready=$(ready_count)"
  fi
  start_receiver normal || return
  if drain >"$fixture_dir/lost-drain.out" 2>"$fixture_dir/lost-drain.err" \
     && [ "$(ready_count)" -eq 0 ] \
     && [ "$(python3 -c 'import json,sys; print(len(json.load(open(sys.argv[1]))))' "$state")" -eq 2 ] \
     && grep -q '"kind":"lookup-hit","batch":"package-lost-response"' "$log"; then
    ok "restart lookup recovers the lost response with one simulated receiver record"
  else
    bad "restart lookup recovers the lost response with one simulated receiver record"
  fi

  stop_receiver
  make_batch package-interrupted "$fixture_dir/interrupted.json"
  submit "$fixture_dir/interrupted.json" >"$fixture_dir/interrupted.out" 2>"$fixture_dir/interrupted.err"; local interrupted_rc=$?
  [ "$interrupted_rc" -ne 0 ] && [ "$(ready_count)" -eq 1 ] \
    && ok "receiver outage retains a pending envelope across CLI process exit" \
    || bad "receiver outage retains a pending envelope across CLI process exit" "rc=$interrupted_rc ready=$(ready_count)"
  start_receiver normal || return
  drain >"$fixture_dir/interrupted-drain.out" 2>"$fixture_dir/interrupted-drain.err"
  [ $? -eq 0 ] && [ "$(ready_count)" -eq 0 ] \
    && ok "a fresh CLI drain recovers the interrupted pending envelope" \
    || bad "a fresh CLI drain recovers the interrupted pending envelope"

  printf '%s\n' mismatch > "$mode"
  make_batch package-mismatch "$fixture_dir/mismatch.json"
  submit "$fixture_dir/mismatch.json" >"$fixture_dir/mismatch.out" 2>"$fixture_dir/mismatch.err"; local mismatch_rc=$?
  [ "$mismatch_rc" -ne 0 ] && [ "$(ready_count)" -eq 1 ] \
    && ok "mismatched receipts cannot remove the ready envelope" \
    || bad "mismatched receipts cannot remove the ready envelope" "rc=$mismatch_rc ready=$(ready_count)"
  printf '%s\n' normal > "$mode"
  drain >/dev/null 2>"$fixture_dir/mismatch-drain.err"

  make_batch package-auth "$fixture_dir/auth.json"
  export FSGG_TELEMETRY_CREDENTIAL_PACKAGE_TOKEN="wrong-token"
  submit "$fixture_dir/auth.json" >"$fixture_dir/auth.out" 2>"$fixture_dir/auth.err"; local auth_rc=$?
  [ "$auth_rc" -ne 0 ] && [ "$(ready_count)" -eq 1 ] && grep -q '"kind":"auth-refused"' "$log" \
    && ok "authentication refusal retains the ready envelope without logging its token" \
    || bad "authentication refusal retains the ready envelope" "rc=$auth_rc ready=$(ready_count)"
  export FSGG_TELEMETRY_CREDENTIAL_PACKAGE_TOKEN="$token"
  drain >/dev/null 2>"$fixture_dir/auth-drain.err"

  printf '%s\n' oversized > "$mode"
  make_batch package-oversized-response "$fixture_dir/oversized.json"
  submit "$fixture_dir/oversized.json" >"$fixture_dir/oversized.out" 2>"$fixture_dir/oversized.err"; local oversized_rc=$?
  [ "$oversized_rc" -ne 0 ] && [ "$(ready_count)" -eq 1 ] \
    && ok "oversized receipt responses stay unacknowledged and retain the ready envelope" \
    || bad "oversized receipt responses stay unacknowledged and retain the ready envelope" "rc=$oversized_rc ready=$(ready_count)"
  printf '%s\n' normal > "$mode"
  drain >/dev/null 2>"$fixture_dir/oversized-drain.err"

  if ! find "$fixture_dir" -type f \( -name '*.out' -o -name '*.err' -o -name '*.log' \) \
         -exec grep -Eql "$token|wrong-token" {} \; -print -quit | grep -q . \
     && [ "$(ready_count)" -eq 0 ]; then
    ok "CLI and receiver logs exclude credentials and all recoverable spools settle"
  else
    bad "CLI and receiver logs exclude credentials and all recoverable spools settle"
  fi
  REMOTE_OBLIGATIONS="$(python3 -c 'import json,sys; print(len(json.load(open(sys.argv[1]))))' "$state")"
  REMOTE_PROFILE="synthetic-tls-qualified"
  export REMOTE_OBLIGATIONS REMOTE_PROFILE
  stop_receiver
  trap 'rm -rf "$WORK"' EXIT
}
