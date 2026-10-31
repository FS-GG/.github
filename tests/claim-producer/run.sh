#!/usr/bin/env bash
# Fixture for scripts/check-claim-producer.py (.github#4730 / #5004). Offline: `gh` is stubbed on PATH.
# Every negative leg asserts the finding text, not merely a non-zero exit.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
TOOL="$ROOT/scripts/check-claim-producer.py"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/claim-producer-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT
export PYTHONDONTWRITEBYTECODE=1

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && sed 's/^/    | /' <<<"$2"; failcount=$((failcount+1)); }

REPO="FS-GG/.github"
HEAD="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
OTHER="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"

# The policy is the single source of the expected producer; the fixture reads it rather than hard-coding.
read -r WFID WFPATH WFEVENT < <(python3 - "$ROOT" <<'PY'
import json, sys
r = json.load(open(sys.argv[1] + "/policy/v2-ci-ordinary-settlement.json"))["qualification"]["checkProducers"]["claim-generation"]
print(r["workflowId"], r["path"], r["event"])
PY
)

# stub: serves $W/checks.ndjson for the check-runs query, $W/run-<id>.json for a run, and fails
# everything if $W/api-down exists.
mkdir -p "$WORK/bin"
cat > "$WORK/bin/gh" <<'STUB'
#!/usr/bin/env bash
[ "$1" = api ] || exit 64
shift
[ ! -e "$W/api-down" ] || { echo "gh: HTTP 502" >&2; exit 1; }
for a in "$@"; do
  case "$a" in
    *"/check-runs?"*) cat "$W/checks.ndjson"; exit 0 ;;
    repos/*/actions/runs/*) cat "$W/run-${a##*/}.json"; exit 0 ;;
  esac
done
exit 64
STUB
chmod +x "$WORK/bin/gh"

new_world() {  # new_world <name> -> prints dir, with empty checks
  local d="$WORK/$1"; mkdir -p "$d"; : > "$d/checks.ndjson"; echo "$d"
}
check() {  # check <world> <id> <status> <conclusion> <run-id|none>
  local url="https://github.com/$REPO/actions/runs/$5/job/$2"
  [ "$5" != none ] || url="null"
  python3 - "$1/checks.ndjson" "$2" "$3" "$4" "$url" <<'PY'
import json, sys
p, i, s, c, u = sys.argv[1:6]
open(p, "a").write(json.dumps({"id": int(i), "status": s, "conclusion": c,
    "details_url": None if u == "null" else u, "name": "claim-generation"}) + "\n")
PY
}
run() {  # run <world> <run-id> <workflow_id> <path> <event> <head>
  python3 - "$1/run-$2.json" "$2" "$3" "$4" "$5" "$6" <<'PY'
import json, sys
p, i, w, path, ev, h = sys.argv[1:7]
open(p, "w").write(json.dumps({"id": int(i), "workflow_id": int(w), "path": path, "event": ev, "head_sha": h}))
PY
}
expect() {  # expect <label> <rc> <needle> <world> [extra args...]
  local label="$1" want="$2" needle="$3" w="$4"; shift 4
  local rc=0 out
  out="$(W="$w" PATH="$WORK/bin:$PATH" python3 "$TOOL" --repo "$REPO" --head "$HEAD" --root "$ROOT" "$@" 2>&1)" || rc=$?
  if [ "$rc" = "$want" ]; then
    case "$out" in *"$needle"*) ok "$label" ;; *) bad "$label: exit $rc but output lacks '$needle'" "$out" ;; esac
  else bad "$label: want exit $want, got $rc" "$out"; fi
}

W=$(new_world good); check "$W" 1 completed success 100; run "$W" 100 "$WFID" "$WFPATH" "$WFEVENT" "$HEAD"
expect "A1 one run from the policy's workflow passes" 0 "is from workflow $WFID" "$W"

W=$(new_world cancelled-then-good); check "$W" 1 completed cancelled 100; check "$W" 2 completed success 101
run "$W" 100 "$WFID" "$WFPATH" "$WFEVENT" "$HEAD"; run "$W" 101 "$WFID" "$WFPATH" "$WFEVENT" "$HEAD"
expect "A2 a cancelled earlier run from the right workflow plus a good one passes" 0 "is from workflow" "$W"

W=$(new_world old-coherence); check "$W" 1 completed success 100; run "$W" 100 303535682 .github/workflows/coherence.yml pull_request "$HEAD"
expect "B1 a pre-cutover run from coherence.yml is refused" 1 "workflowId=303535682" "$W"

W=$(new_world mixed); check "$W" 1 completed success 100; check "$W" 2 completed success 101
run "$W" 100 303535682 .github/workflows/coherence.yml pull_request "$HEAD"; run "$W" 101 "$WFID" "$WFPATH" "$WFEVENT" "$HEAD"
expect "B2 one old and one new run on the same head is refused, naming the old one" 1 "from run 100" "$W"

W=$(new_world none)
expect "B3 no claim-generation check run at all is refused" 1 "no \`claim-generation\` check run" "$W"

W=$(new_world sweep); check "$W" 1 completed success 100; run "$W" 100 "$WFID" "$WFPATH" "$WFEVENT" "$HEAD"; check "$W" 9 completed failure none
expect "B4 a check run with no Actions job behind it (the sweep's) is refused" 1 "no Actions job behind it" "$W"

W=$(new_world wrong-head); check "$W" 1 completed success 100; run "$W" 100 "$WFID" "$WFPATH" "$WFEVENT" "$OTHER"
expect "B5 a run whose head_sha is another commit is refused" 1 "head_sha=" "$W"

W=$(new_world wrong-event); check "$W" 1 completed success 100; run "$W" 100 "$WFID" "$WFPATH" push "$HEAD"
expect "B6 a run from the right workflow on another event is refused" 1 "event='push'" "$W"

W=$(new_world wrong-id); check "$W" 1 completed success 100; run "$W" 100 111 "$WFPATH" "$WFEVENT" "$HEAD"
expect "B7 the right path under another workflow id is refused" 1 "workflowId=111" "$W"

W=$(new_world down); touch "$W/api-down"
expect "C1 an unreadable API is exit 2, never a pass" 2 "retry; not a pass" "$W"

W=$(new_world good2); check "$W" 1 completed success 100; run "$W" 100 "$WFID" "$WFPATH" "$WFEVENT" "$HEAD"
rc=0; out="$(W="$W" PATH="$WORK/bin:$PATH" python3 "$TOOL" --repo "$REPO" --head "not-a-sha" --root "$ROOT" 2>&1)" || rc=$?
[ "$rc" = 3 ] && ok "C2 a malformed --head is exit 3" || bad "C2 malformed head: rc=$rc" "$out"
mkdir -p "$WORK/nopolicy"
rc=0; out="$(W="$W" PATH="$WORK/bin:$PATH" python3 "$TOOL" --repo "$REPO" --head "$HEAD" --root "$WORK/nopolicy" 2>&1)" || rc=$?
[ "$rc" = 3 ] && ok "C3 an unreadable policy file is exit 3" || bad "C3 missing policy: rc=$rc" "$out"

# Single source: point the tool at a policy naming a different workflow and the same runs must now FAIL,
# and runs from that other workflow must pass. Nothing is hard-coded in the script.
mkdir -p "$WORK/altroot/policy"
python3 - "$ROOT" "$WORK/altroot" <<'PY'
import json, sys
d = json.load(open(sys.argv[1] + "/policy/v2-ci-ordinary-settlement.json"))
d["qualification"]["checkProducers"]["claim-generation"].update(workflowId=424242, path=".github/workflows/elsewhere.yml")
json.dump(d, open(sys.argv[2] + "/policy/v2-ci-ordinary-settlement.json", "w"))
PY
expect "D1 the expected producer comes from the policy file: the usual runs fail against another policy" 1 "want 424242" "$W" --root "$WORK/altroot"
W=$(new_world alt-good); check "$W" 1 completed success 100; run "$W" 100 424242 .github/workflows/elsewhere.yml "$WFEVENT" "$HEAD"
expect "D2 ...and runs from that policy's workflow pass" 0 "workflow 424242" "$W" --root "$WORK/altroot"

echo
echo "claim-producer fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ]
