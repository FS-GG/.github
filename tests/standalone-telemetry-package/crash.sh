#!/usr/bin/env bash
# Sourced by run.sh after the packaged tool is installed.

qualify_remote_crash() {
  local root tls private config spool state mode log token port server_pid="" submit_pid=""
  root="$WORK/remote-crash"
  : "${REMOTE_RECEIVER:=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/receiver.py}"
  tls="$root/tls"
  private="$root/private"
  config="$private/telemetry.json"
  spool="$private/spool"
  state="$root/receiver-state.json"
  mode="$root/receiver-mode"
  log="$root/receiver.log"
  token="package-crash-token"
  mkdir -p "$tls" "$private"
  chmod 700 "$private"
  : > "$log"
  chmod 600 "$log"

  openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj '/CN=FS.GG package crash fixture CA' \
    -addext 'basicConstraints=critical,CA:TRUE' -addext 'keyUsage=critical,keyCertSign,cRLSign' \
    -keyout "$tls/ca.key" -out "$tls/ca.crt" >/dev/null 2>&1
  openssl req -newkey rsa:2048 -nodes -subj '/CN=localhost' \
    -keyout "$tls/server.key" -out "$tls/server.csr" >/dev/null 2>&1
  printf '%s\n' 'subjectAltName=DNS:localhost' 'extendedKeyUsage=serverAuth' > "$tls/server.ext"
  openssl x509 -req -days 1 -in "$tls/server.csr" -CA "$tls/ca.crt" -CAkey "$tls/ca.key" \
    -CAcreateserial -extfile "$tls/server.ext" -out "$tls/server.crt" >/dev/null 2>&1
  chmod 600 "$tls/ca.key" "$tls/server.key"
  export SSL_CERT_FILE="$tls/ca.crt"
  export FSGG_TELEMETRY_CREDENTIAL_CRASH_TOKEN="$token"
  port="$(python3 - <<'PY'
import socket
with socket.socket() as sock:
    sock.bind(('127.0.0.1', 0))
    print(sock.getsockname()[1])
PY
)"
  printf '%s\n' hold > "$mode"
  python3 "$REMOTE_RECEIVER" --port "$port" --cert "$tls/server.crt" --key "$tls/server.key" \
    --state "$state" --mode "$mode" --log "$log" --token "$token" \
    --scope package-crash-workspace package-crash-producer runtime >"$root/server.out" 2>"$root/server.err" &
  server_pid=$!
  cleanup_crash() {
    [ -z "$submit_pid" ] || kill -KILL "$submit_pid" 2>/dev/null || true
    [ -z "$server_pid" ] || kill "$server_pid" 2>/dev/null || true
    [ -z "$submit_pid" ] || wait "$submit_pid" 2>/dev/null || true
    [ -z "$server_pid" ] || wait "$server_pid" 2>/dev/null || true
  }
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
  if [ "$ready" -ne 1 ]; then
    bad "crash fixture TLS receiver starts"
    cleanup_crash
    return
  fi
  if ! "$ENGINE" telemetry workspace activate-remote --config "$config" \
      --workspace package-crash-workspace --producer package-crash-producer --stream runtime \
      --repository FS-GG/package-crash --endpoint "https://localhost:$port/" \
      --credential-reference crash-token --spool-root "$spool" >"$root/activate.out" 2>"$root/activate.err"; then
    bad "crash fixture activates its isolated remote association"
    cleanup_crash
    return
  fi

  local batch_schema input
  batch_schema="fsgg.telemetry.""ingest/1"
  input="$root/input.json"
  printf '{"schema":"%s","ingestId":"package-forced-interruption","sourceIdentity":"package-fixture","generation":"g1","cursor":"crash","eventCount":1,"events":[{"kind":"item","identity":"package-forced-interruption","itemId":"package-forced-interruption","revision":1}]}\n' \
    "$batch_schema" > "$input"
  "$ENGINE" telemetry workspace submit --config "$config" --repository FS-GG/package-crash \
    --input "$input" >"$root/submit.out" 2>"$root/submit.err" &
  submit_pid=$!
  local held=0
  for _ in $(seq 1 200); do
    if [ -f "$root/held-package-forced-interruption" ] \
       && find "$spool" -maxdepth 1 -type f -name '*.ready' -print -quit | grep -q .; then
      held=1
      break
    fi
    kill -0 "$submit_pid" 2>/dev/null || break
    sleep .05
  done
  if [ "$held" -ne 1 ]; then
    bad "receiver holds its response after ready bytes and one synthetic record exist"
    cleanup_crash
    return
  fi
  kill -KILL "$submit_pid"
  wait "$submit_pid" 2>/dev/null || true
  submit_pid=""
  if [ "$(find "$spool" -maxdepth 1 -type f -name '*.ready' | wc -l)" -eq 1 ]; then
    ok "forced producer interruption retains the exact ready envelope"
  else
    bad "forced producer interruption retains the exact ready envelope"
  fi

  printf '%s\n' normal > "$mode"
  if "$ENGINE" telemetry workspace drain --config "$config" --repository FS-GG/package-crash \
      >"$root/drain.out" 2>"$root/drain.err" \
     && [ "$(find "$spool" -maxdepth 1 -type f -name '*.ready' | wc -l)" -eq 0 ] \
     && python3 - "$state" "$spool/outcomes" "$log" <<'PY'
import json, pathlib, sys
state=json.load(open(sys.argv[1]))
outcomes=list(pathlib.Path(sys.argv[2]).glob('*.json'))
events=[json.loads(line) for line in pathlib.Path(sys.argv[3]).read_text().splitlines()]
assert list(state)==['package-forced-interruption']
outcome=json.loads(outcomes[0].read_text())
assert outcome['batchId']=='package-forced-interruption'
assert outcome['digest']==state['package-forced-interruption'] and outcome['status']=='applied'
assert sum(e.get('kind')=='accepted' and e.get('batch')=='package-forced-interruption' for e in events)==1
assert sum(e.get('kind')=='lookup-hit' and e.get('batch')=='package-forced-interruption' for e in events)==1
PY
  then
    ok "fresh packaged drain recovers the held receipt with one synthetic receiver record"
  else
    bad "fresh packaged drain recovers the held receipt with one synthetic receiver record"
  fi
  cleanup_crash
}
