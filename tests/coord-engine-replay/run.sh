#!/usr/bin/env bash
# The CI leg for `.github#2401` — replaying RECORDED board transcripts against the compiled engine,
# rather than a hand-authored belief about what GitHub returns (see `scripts/record-board-fixture.py`'s
# docstring for why that distinction is the whole point).
#
# TWO KINDS OF ASSERTION HERE:
#
#   1. A SELF-CHECK (leg 0) that the capture→replay round trip is faithful: it drives the compiled engine
#      DIRECTLY against the same hermetic board `fixtures/smoke/` was captured from
#      (`tests/coord-engine-e2e/stateful_server.py`) and asserts the DIRECT answer equals the checked-in
#      `fixtures/smoke/expected/*.json` — the same file every fixture leg below compares its REPLAYED
#      answer against. If replay ever silently altered a fact, this leg and the fixture leg would agree
#      with each other while both disagreeing with a live connection, and this is what would catch that.
#
#   2. Per-fixture REPLAY legs: for each `fixtures/<name>/transcript.json`, start
#      `replay_server.py` on it, drive `reconcile`/`ready`/`driver --events` against the replay, and
#      diff the JSON against `fixtures/<name>/expected/*.json`. A fixture may declare
#      `manifest.json`'s `expectFailure` — naming which command(s) are ALLOWED to mismatch and why —
#      which is how `fixtures/2216-oscillation/` proves the instrument catches the class `.github#2384`
#      tracks without also fixing it here (`.github#2401` AC3).
#
# Every fixture also asserts NO REQUEST WENT UNMATCHED (`/_fixture/misses`): an unmatched request means
# the fixture is stale or incomplete, never a projection finding, and is a hard failure regardless of
# any `expectFailure` declaration.
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
ENGINE="${FSGG_COORD_ENGINE_BIN:-$REPO_ROOT/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine}"

pass=0; failcount=0; xfail=0
ok()    { echo "PASS  $1"; pass=$((pass+1)); }
bad()   { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }
xok()   { echo "XFAIL $1  (expected — $2)"; xfail=$((xfail+1)); }
xbad()  { echo "XPASS $1  — this fixture's expected-failure marker may be stale; see its manifest.json"; xfail=$((xfail+1)); }

if [ ! -x "$ENGINE" ]; then
  echo "FAIL  the engine must be built first: dotnet build src/FS.GG.Coord.Cli -c Release" >&2
  echo "      (looked at: $ENGINE)" >&2
  exit 1
fi

TMP_ROOT="$(mktemp -d)"
SERVERS_TO_KILL=()
cleanup() {
  for pid in "${SERVERS_TO_KILL[@]:-}"; do kill "$pid" 2>/dev/null; done
  rm -rf "$TMP_ROOT"
}
trap cleanup EXIT

start_server() {
  # $1 = script, remaining args passed through. Prints the bound port on stdout, once ready.
  local script="$1"; shift
  local out; out="$(mktemp -p "$TMP_ROOT")"
  python3 "$script" "$@" >"$out" 2>>"$TMP_ROOT/server.err" &
  local pid=$!
  SERVERS_TO_KILL+=("$pid")
  local port=""
  for _ in $(seq 1 50); do
    port="$(head -n1 "$out" 2>/dev/null)"
    [ -n "$port" ] && break
    sleep 0.1
  done
  echo "$port"
}

run_command() {
  # $1 = command name (reconcile|ready|driver-events), $2 = repo, $3 = api base → writes JSON to stdout
  case "$1" in
    reconcile)     "$ENGINE" reconcile --repo "$2" --json ;;
    ready)         "$ENGINE" ready     --repo "$2" --json ;;
    driver-events) "$ENGINE" driver --events --repo "$2" --json ;;
  esac
}

common_env() {
  export GITHUB_TOKEN="fixture-token"
  export FSGG_COORD_OWNER="FS-GG"
  export FSGG_COORD_PROJECT="Coordination"
  export FSGG_COORD_SCAN_TTL_SEC=0
  # ISOLATED, not merely unset. `FSGG_COORD_CACHE` unset falls back to `Cache.root()`'s default —
  # `$XDG_CACHE_HOME/fsgg-coord`, PERSISTENT across every invocation on the runner — and every fixture
  # here plus the self-check leg name the SAME repo (`FS.GG.SDD`). A shared or leaked cache would let
  # one leg's read answer another's, exactly the isolation `tests/coord-engine-e2e/run.sh` gives its
  # own run with `mktemp -d`; this is that same fix, called fresh before EVERY leg below rather than
  # once for the whole script, so no two legs — self-check, smoke, 2216-oscillation — ever share one.
  local cache_dir
  cache_dir="$(mktemp -d -p "$TMP_ROOT")"
  export FSGG_COORD_CACHE="$cache_dir"
}

health_report_valid() {
  python3 - "$1" <<'PY'
import json, pathlib, sys
row=json.loads(pathlib.Path(sys.argv[1]).read_text())
subjects=row.get("subjects")
assert row.get("schemaVersion") == 1
assert row.get("completeReadBoundary") == "typed-complete-success/1"
assert isinstance(subjects, list) and row.get("subjectCount") == len(subjects) and subjects
assert all(isinstance(item.get("readComplete"), bool) and isinstance(item.get("reversed"), bool)
           and item.get("subject") and item.get("intent") and item.get("applied") and item.get("intended")
           for item in subjects)
PY
}

# ---- leg 0: self-check — a DIRECT hermetic connection must equal the smoke fixture's expectation ----
common_env
PORT0="$(start_server "$REPO_ROOT/tests/coord-engine-e2e/stateful_server.py")"
if [ -z "$PORT0" ]; then
  bad "self-check fixture server bound a port"
else
  export FSGG_GITHUB_API_BASE="http://127.0.0.1:$PORT0"
  for cmd in reconcile ready driver-events; do
    out="$(mktemp -p "$TMP_ROOT")"
    shadow_out=""
    if [ "$cmd" = reconcile ]; then
      shadow_out="$(mktemp -p "$TMP_ROOT")"
      health_out="$(mktemp -p "$TMP_ROOT")"
      export FSGG_COORD_LIFECYCLE_SHADOW_REPORT="$shadow_out"
      export FSGG_COORD_HEALTH_REPORT="$health_out"
    else
      unset FSGG_COORD_LIFECYCLE_SHADOW_REPORT
      unset FSGG_COORD_HEALTH_REPORT
    fi
    run_command "$cmd" FS.GG.SDD >"$out" 2>"$TMP_ROOT/direct-$cmd.err"
    rc=$?
    expected="$HERE/fixtures/smoke/expected/$cmd.json"
    if [ "$rc" -ne 0 ]; then
      bad "self-check: \`$cmd\` runs directly against the hermetic board" "rc=$rc: $(cat "$TMP_ROOT/direct-$cmd.err")"
    elif diffmsg="$(python3 "$HERE/compare.py" "$out" "$expected")"; then
      ok "self-check: direct \`$cmd\` matches fixtures/smoke/expected/$cmd.json"
      if [ "$cmd" = reconcile ]; then
        shadow_expected="$HERE/fixtures/smoke/expected/reconcile-shadow.json"
        if [ "${FSGG_UPDATE_SHADOW_EXPECTED:-0}" = 1 ]; then
          cp "$shadow_out" "$shadow_expected"
          ok "self-check: refreshed classified intent shadow expectation"
        elif shadow_diff="$(python3 "$HERE/compare.py" "$shadow_out" "$shadow_expected")"; then
          ok "self-check: direct \`reconcile\` records classified intent shadow differences"
        else
          bad "self-check: direct \`reconcile\` records classified intent shadow differences" "$shadow_diff"
        fi
        if health_report_valid "$health_out"; then
          ok "self-check: direct \`reconcile\` records complete per-subject machine health"
        else
          bad "self-check: direct \`reconcile\` records complete per-subject machine health"
        fi
      fi
    else
      bad "self-check: direct \`$cmd\` matches fixtures/smoke/expected/$cmd.json" "$diffmsg"
    fi
  done
fi

# ---- per-fixture replay legs ------------------------------------------------------------------------
shopt -s nullglob
for fixture_dir in "$HERE"/fixtures/*/; do
  name="$(basename "$fixture_dir")"
  transcript="$fixture_dir/transcript.json"
  [ -f "$transcript" ] || continue

  read -r repo commands_csv < <(python3 -c "
import json
d = json.load(open('$transcript'))
print(d['repo'], ','.join(d['commands']))
")
  IFS=',' read -r -a commands <<< "$commands_csv"

  xfail_commands=""
  xfail_reason=""
  manifest="$fixture_dir/manifest.json"
  if [ -f "$manifest" ]; then
    read -r xfail_commands xfail_reason < <(python3 -c "
import json
d = json.load(open('$manifest'))
ef = d.get('expectFailure')
if ef:
    print(','.join(ef.get('commands', [])), '|', ef.get('reason', ''))
else:
    print('', '|', '')
")
    xfail_reason="${xfail_reason#| }"
  fi

  common_env
  PORT="$(start_server "$HERE/replay_server.py" "$transcript")"
  if [ -z "$PORT" ]; then
    bad "$name: replay server bound a port"
    continue
  fi
  export FSGG_GITHUB_API_BASE="http://127.0.0.1:$PORT"

  for cmd in "${commands[@]}"; do
    expected="$fixture_dir/expected/$cmd.json"
    if [ ! -f "$expected" ]; then
      bad "$name/$cmd: expected/$cmd.json is missing"
      continue
    fi
    out="$(mktemp -p "$TMP_ROOT")"
    shadow_out=""
    if [ "$cmd" = reconcile ]; then
      shadow_out="$(mktemp -p "$TMP_ROOT")"
      health_out="$(mktemp -p "$TMP_ROOT")"
      export FSGG_COORD_LIFECYCLE_SHADOW_REPORT="$shadow_out"
      export FSGG_COORD_HEALTH_REPORT="$health_out"
    else
      unset FSGG_COORD_LIFECYCLE_SHADOW_REPORT
      unset FSGG_COORD_HEALTH_REPORT
    fi
    run_command "$cmd" "$repo" >"$out" 2>"$TMP_ROOT/$name-$cmd.err"
    rc=$?
    is_xfail=0
    case ",$xfail_commands," in *",$cmd,"*) is_xfail=1 ;; esac

    if [ "$rc" -ne 0 ]; then
      if grep -q "REPLAY-UNMATCHED-REQUEST" "$TMP_ROOT/$name-$cmd.err" 2>/dev/null; then
        bad "$name/$cmd: engine call failed — REPLAY-UNMATCHED-REQUEST (fixture is stale/incomplete)" "$(cat "$TMP_ROOT/$name-$cmd.err")"
      else
        bad "$name/$cmd: \`$cmd\` exits 0 against the replay" "rc=$rc: $(cat "$TMP_ROOT/$name-$cmd.err")"
      fi
      continue
    fi

    if diffmsg="$(python3 "$HERE/compare.py" "$out" "$expected")"; then
      if [ "$is_xfail" -eq 1 ]; then
        xbad "$name/$cmd"
      else
        ok "$name/$cmd matches its recorded expectation"
        if [ "$cmd" = reconcile ]; then
          shadow_expected="$fixture_dir/expected/reconcile-shadow.json"
          if [ "${FSGG_UPDATE_SHADOW_EXPECTED:-0}" = 1 ]; then
            cp "$shadow_out" "$shadow_expected"
            ok "$name/$cmd refreshed its classified intent shadow expectation"
          elif [ ! -f "$shadow_expected" ]; then
            bad "$name/$cmd: expected/reconcile-shadow.json is missing"
          elif shadow_diff="$(python3 "$HERE/compare.py" "$shadow_out" "$shadow_expected")"; then
            ok "$name/$cmd records its classified intent shadow differences"
          else
            bad "$name/$cmd records its classified intent shadow differences" "$shadow_diff"
          fi
          if health_report_valid "$health_out"; then
            ok "$name/$cmd records complete per-subject machine health"
          else
            bad "$name/$cmd records complete per-subject machine health"
          fi
        fi
      fi
    else
      if [ "$is_xfail" -eq 1 ]; then
        xok "$name/$cmd" "$xfail_reason"
      else
        bad "$name/$cmd matches its recorded expectation" "$diffmsg"
      fi
    fi
  done

  misses="$(curl -s "http://127.0.0.1:$PORT/_fixture/misses" 2>/dev/null)"
  miss_count="$(printf '%s' "$misses" | python3 -c "import json,sys; print(len(json.load(sys.stdin).get('misses', [])))" 2>/dev/null || echo "?")"
  if [ "$miss_count" = "0" ]; then
    ok "$name: every replayed request matched a recorded entry"
  else
    bad "$name: every replayed request matched a recorded entry" "misses=$misses"
  fi
done

# ---- report --------------------------------------------------------------------------------------
echo
echo "coord-engine-replay: $((pass + failcount)) assertion(s), $pass passed, $failcount failed, $xfail expected-failure marker(s)"
[ "$failcount" -eq 0 ] || { echo "::error::coord-engine-replay FAILED"; exit 1; }
echo "green — recorded board transcripts replay faithfully through the compiled engine."
