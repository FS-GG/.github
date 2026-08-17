#!/usr/bin/env bash
# Fixture for `.github/workflows/fsgg-dispatch-broker.yml` — slice 5 of the GitHub-native executor
# fencing design (`docs/reports/2026-08-04-github-native-executor-fencing-design.md` §5.2, §11.2),
# filed as `.github#2720` under `.github#1858` (Critical: two workers ran one item to completion under
# ONE claim marker, and six repositories got PRs from an unlocked executor).
#
# THREE PROPERTIES, AND EACH ONE NEEDS ITS NEGATIVE LEG.
#
#   1. THE SINGLE-CALLER PROPERTY. The broker is the only caller of `dispatch-sender.yml`, which is
#      what makes the per-receiver operation lock a fence rather than a suggestion. This is an ABSENCE
#      assertion, and an absence assertion is satisfied equally well by a tree with nothing in it and
#      by a MATCHER THAT FINDS NOTHING. This board measured that exact failure the day this landed:
#      `.github#2312` shipped an ordering gate blind to F#'s `_.Id` shorthand, and a critic added a
#      fourth copy of the forbidden pattern with all 823 tests still green. So the assertion here is
#      bound by a CORPUS — nine real caller spellings that MUST match, six lookalikes that MUST NOT,
#      three of them lifted verbatim from live production code — and it is inverted in BOTH directions:
#      adding a caller reds, and REMOVING the broker's own call also reds, because zero callers means
#      the matcher has gone blind and every clean report it makes afterwards is vacuous.
#
#   2. PER-RECEIVER CONCURRENCY. Serialisation only. `cancel-in-progress: false` because cancelling an
#      in-flight dispatch manufactures §8.5's partial application on purpose. Negative legs: a constant
#      group (not per-receiver) and a truthy `cancel-in-progress` each red.
#
#   3. OPKEY DEDUPE. Two triggers in the same window must not produce two dispatches — the parent row's
#      acceptance names idempotence explicitly, and it is testable rather than arguable. Legs D1/D2
#      drive the authorize script TWICE against a comment list that gains the first run's receipt in
#      between, and assert the second run collapses. Concurrency is NOT this property and the fixture
#      says so: `queue: single` is last-writer-wins, which is mutual exclusion without idempotence.
#
# WHY IT TESTS THE SHIPPED SCRIPT, NOT A COPY. Every behaviour leg extracts the `authorize` step's real
# `run:` block from the real workflow and executes it. A re-typed copy would keep passing after someone
# edits the workflow — the fails-open shape epic #266 exists to close, rebuilt inside the fixture meant
# to close it. Same discipline as `tests/dispatch-preflight`.
#
# Every negative leg asserts the REASON, not merely a non-zero exit: the #266 vacuous-failure defect
# was a "must fail" test whose non-zero exit came from a path guard rather than from the thing under
# test.
#
# No network and no runner: `gh` is STUBBED, reproducing exactly the contract the script depends on.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
BROKER_WF="$REPO_ROOT/.github/workflows/fsgg-dispatch-broker.yml"
SELFTEST_WF="$REPO_ROOT/.github/workflows/fsgg-dispatch-broker-selftest.yml"
OPTIONS_FS="$REPO_ROOT/src/FS.GG.Coord.Cli.Kernel/Options.fs"
# `Operation.fs` is no longer READ as text by this fixture — section E BUILDS it and EXECUTES
# `Operation.compose` instead, which is why the K6 grep is gone. It is still what leg B9d requires
# the selftest to trigger on, because a change to it changes the oracle section E compares against.
CLIENT_FS="$REPO_ROOT/src/FS.GG.Coord.Cli/Client.fs"
SCANNER="$HERE/single_caller.py"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/dispatch-broker-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() {
  echo "FAIL  $1"
  # `[ -n ... ] && printf` returns 1 on an empty $2 and, under `set -e`, would abort the fixture
  # mid-run — truncating the remaining legs and the summary. Spell it as an `if`.
  if [ -n "${2:-}" ]; then printf '%s\n' "$2" | sed 's/^/    | /'; fi
  failcount=$((failcount+1))
}

[ -f "$BROKER_WF" ]   || { echo "FAIL  fsgg-dispatch-broker.yml is missing entirely — nothing to test."; exit 1; }
[ -f "$SELFTEST_WF" ] || { echo "FAIL  fsgg-dispatch-broker-selftest.yml is missing — the gate would never run."; exit 1; }
[ -f "$SCANNER" ]     || { echo "FAIL  single_caller.py is missing — the single-caller property is unenforced."; exit 1; }

# ---- pull the REAL scripts + structure out of the REAL workflow ---------------------------------
AUTHORIZE="$WORK/authorize.sh"
RECEIPT="$WORK/receipt.sh"
META="$WORK/meta.json"

python3 - "$BROKER_WF" "$SELFTEST_WF" "$OPTIONS_FS" "$AUTHORIZE" "$RECEIPT" "$META" <<'PY'
import json, re, sys, yaml

broker_path, selftest_path, options_path, authorize_out, receipt_out, meta_out = sys.argv[1:7]
with open(broker_path, encoding="utf-8") as fh:
    wf = yaml.safe_load(fh)
with open(selftest_path, encoding="utf-8") as fh:
    selftest = yaml.safe_load(fh)

jobs = wf.get("jobs") or {}
for want in ("authorize", "dispatch", "receipt"):
    if want not in jobs:
        sys.exit(f"the `{want}` job is GONE from fsgg-dispatch-broker.yml — the fence has been dismantled.")

authorize, dispatch, receipt = jobs["authorize"], jobs["dispatch"], jobs["receipt"]

# Select the verdict step BY ID. A positional pick would silently follow a reordering, and an
# assertion that quietly starts testing a different step is the fixture rebuilding the fail-open it
# exists to catch.
auth_steps = authorize.get("steps") or []
auth_run = {s.get("id"): s for s in auth_steps if "run" in s}
if "authorize" not in auth_run:
    sys.exit("the `authorize` verdict step is GONE from the authorize job.")
step = auth_run["authorize"]
with open(authorize_out, "w", encoding="utf-8") as fh:
    fh.write(step["run"])

receipt_steps = receipt.get("steps") or []
receipt_run = [s for s in receipt_steps if "run" in s]
if not receipt_run:
    sys.exit("the receipt job has no `run:` step — the fsgg:op-effect audit trail is gone.")
with open(receipt_out, "w", encoding="utf-8") as fh:
    fh.write(receipt_run[0]["run"])

# `on:` parses as the YAML 1.1 boolean True unless quoted. Read whichever key is present rather than
# assuming — an assertion that silently reads `None` here would pass for the wrong reason.
def trigger_block(doc):
    return doc.get("on", doc.get(True)) or {}

selftest_on = trigger_block(selftest)
pr = selftest_on.get("pull_request") or {}

# The engine's op-lock table, parsed from the source of truth so the workflow's copy is PINNED rather
# than trusted. Change one without the other and leg B8 reds.
with open(options_path, encoding="utf-8") as fh:
    options_src = fh.read()
table = re.search(r"let private opLockNumbers[^=]*=\s*(?://[^\n]*\n\s*)*\[(.*?)\]", options_src, re.S)
engine_locks = {}
if table:
    for repo, number in re.findall(r'"([^"]+)",\s*(\d+)', table.group(1)):
        engine_locks[f"FS-GG/{repo}"] = int(number)

concurrency = wf.get("concurrency")
meta = {
    "concurrency": concurrency if isinstance(concurrency, dict) else {"__raw": concurrency},
    "concurrency_present": concurrency is not None,
    "permissions": wf.get("permissions") or {},
    "authorize_has_if": "if" in authorize,
    "authorize_continue_on_error": bool(authorize.get("continue-on-error")),
    "authorize_run_steps_continue_on_error": [
        s.get("id") or s.get("name") for s in auth_steps if "run" in s and bool(s.get("continue-on-error"))
    ],
    "authorize_run_interpolates": "${{" in step["run"],
    "authorize_env_keys": sorted((step.get("env") or {}).keys()),
    "dispatch_uses": dispatch.get("uses"),
    "dispatch_needs": [dispatch["needs"]] if isinstance(dispatch.get("needs"), str) else list(dispatch.get("needs") or []),
    "dispatch_if": " ".join(str(dispatch.get("if", "")).split()),
    "receipt_needs": [receipt["needs"]] if isinstance(receipt.get("needs"), str) else list(receipt.get("needs") or []),
    "workflow_op_locks": json.loads((step.get("env") or {}).get("OP_LOCK_ISSUES", "{}")),
    "engine_op_locks": engine_locks,
    "selftest_pr_paths": list(pr.get("paths") or []),
    "selftest_runs_fixture": any(
        "tests/dispatch-broker/run.sh" in str(s.get("run", ""))
        for job in (selftest.get("jobs") or {}).values()
        if isinstance(job, dict)
        for s in (job.get("steps") or [])
        if isinstance(s, dict)
    ),
    # Section E compares the two implementations by EXECUTING both, so it needs the .NET SDK on the
    # runner. Leg E0 already reds when `dotnet` is absent — but that red would arrive as "the fixture
    # failed" long after somebody removed the step, so the step itself is pinned here where the reason
    # is legible.
    "selftest_installs_dotnet": any(
        "setup-dotnet" in str(s.get("uses", ""))
        for job in (selftest.get("jobs") or {}).values()
        if isinstance(job, dict)
        for s in (job.get("steps") or [])
        if isinstance(s, dict)
    ),
}
with open(meta_out, "w", encoding="utf-8") as fh:
    json.dump(meta, fh)
PY

m() { python3 -c "import json,sys;print(json.load(open('$META'))$1)"; }

# ---- the `gh` stub -------------------------------------------------------------------------------
# STUBBED, NOT CALLED: this fixture has no network by design. The stub reproduces exactly the contract
# the script depends on — `gh api --paginate repos/<owner>/<repo>/issues/<n>/comments` prints one JSON
# array, or fails non-zero — so the legs test the step's LOGIC against a real-shaped answer, and a
# change to that contract shows up as a stub that no longer matches the call.
STUB="$WORK/bin"; mkdir -p "$STUB"
cat > "$STUB/gh" <<'SH'
#!/usr/bin/env bash
set -uo pipefail
: "${GH_STUB_DIR:?the fixture must set GH_STUB_DIR}"
# The token the caller chose, recorded so the fixture can assert token SELECTION (leg A21).
echo "${GH_TOKEN:-<unset>}" >> "$GH_STUB_DIR/tokens.log"
method=GET
target=""
prev=""
for a in "$@"; do
  case "$prev" in -X) method="$a";; esac
  case "$a" in repos/*/issues/*/comments) target="$a";; esac
  prev="$a"
done
if [ "$method" = POST ]; then
  cat > "$GH_STUB_DIR/posted.json"
  if [ -n "${GH_STUB_POST_FAIL:-}" ]; then echo "HTTP 403: Resource not accessible" >&2; exit 1; fi
  echo '{"id":1}'
  exit 0
fi
if [ -n "${GH_STUB_FAIL:-}" ]; then echo "HTTP 403: Resource not accessible by integration" >&2; exit 1; fi
[ -n "$target" ] || { echo "stub gh: unrecognised call: $*" >&2; exit 1; }
key="${target#repos/}"; key="${key%/comments}"; key="${key//\//__}"
file="$GH_STUB_DIR/$key.json"
[ -f "$file" ] || { echo "HTTP 404: Not Found ($target)" >&2; exit 1; }
cat "$file"
SH
chmod +x "$STUB/gh"

# write_comments <owner/repo> <number> <spec-json>
#   spec entries: {"id":N,"kind":"claim|claim-nolease|claim-badtime|opeffect|other","age":secs,"lease":mins,...}
write_comments() {
  python3 - "$WORK/stub" "$1" "$2" "$3" <<'PY'
import datetime, json, os, sys
stub_dir, repo, number, spec = sys.argv[1], sys.argv[2], sys.argv[3], json.loads(sys.argv[4])
os.makedirs(stub_dir, exist_ok=True)
now = datetime.datetime.now(datetime.timezone.utc)
out = []
for c in spec:
    at = now - datetime.timedelta(seconds=c.get("age", 5))
    stamp = at.strftime("%Y-%m-%dT%H:%M:%SZ")
    kind = c["kind"]
    if kind == "claim":
        body = ("<!-- fsgg:claim worker=%s lease=%d renewed=639225061244938392 session=s1 "
                "prev=Ready pathRepo=.github -->" % (c.get("worker", "w-%s" % c["id"]), c.get("lease", 10)))
    elif kind == "claim-nolease":
        # A marker whose lease cannot be read. Reads.fs:283/:473-486 — an unclassifiable marker
        # refuses the WHOLE scan rather than shortening the list.
        body = "<!-- fsgg:claim worker=%s renewed=639225061244938392 -->" % c.get("worker", "w")
    elif kind == "claim-badtime":
        stamp = "not-a-timestamp"
        body = "<!-- fsgg:claim worker=w lease=10 renewed=1 -->"
    elif kind == "opeffect":
        body = ("<!-- fsgg:op-effect v=1 opkey=%s grant=1 receiver=FS-GG/FS.GG.Net op=dispatch:e "
                "evidence=https://example.invalid/run/1 -->" % c["opkey"])
    else:
        body = c.get("body", "an ordinary human comment")
    out.append({"id": c["id"], "updated_at": stamp, "body": body})
key = ("%s/issues/%s" % (repo, number)).replace("/", "__")
with open(os.path.join(stub_dir, key + ".json"), "w", encoding="utf-8") as fh:
    json.dump(out, fh)
PY
}

# The canonical happy-path world, rebuilt before each leg so no leg inherits another's state.
ITEM_DEFAULT="FS-GG/.github#2720"
GEN_DEFAULT="5309318888"
RECEIVER_DEFAULT="FS-GG/FS.GG.Net"
EVENT_DEFAULT="fs-gg-coordination-effect"
OP_DEFAULT="dispatch:$EVENT_DEFAULT"
GRANT_DEFAULT="4242424242"

opkey_of() { # <item> <gen> <receiver> <op>
  python3 -c '
import hashlib, sys
print(hashlib.sha256("\n".join(sys.argv[1:5]).encode("utf-8")).hexdigest())' "$1" "$2" "$3" "$4"
}
OPKEY_DEFAULT="$(opkey_of "$ITEM_DEFAULT" "$GEN_DEFAULT" "$RECEIVER_DEFAULT" "$OP_DEFAULT")"

reset_world() {
  rm -rf "$WORK/stub"; mkdir -p "$WORK/stub"
  write_comments "$RECEIVER_DEFAULT" 72 "[{\"id\":$GRANT_DEFAULT,\"kind\":\"claim\",\"age\":30,\"lease\":10,\"worker\":\"godwit-00f5\"}]"
  write_comments "FS-GG/.github" 2720 "[{\"id\":$GEN_DEFAULT,\"kind\":\"claim\",\"age\":60,\"lease\":120,\"worker\":\"godwit-00f5\"}]"
}

# run_authorize [VAR=VALUE ...] -> writes $WORK/out and $WORK/step_output, echoes the exit code
run_authorize() {
  : > "$WORK/step_output"
  env -i \
    PATH="$STUB:/usr/bin:/bin" \
    HOME="$HOME" \
    GH_STUB_DIR="$WORK/stub" \
    GITHUB_OUTPUT="$WORK/step_output" \
    ITEM="$ITEM_DEFAULT" GENERATION="$GEN_DEFAULT" RECEIVER="$RECEIVER_DEFAULT" \
    OP="$OP_DEFAULT" OPKEY="$OPKEY_DEFAULT" GRANT="$GRANT_DEFAULT" \
    EVENT_TYPE="$EVENT_DEFAULT" \
    GH_TOKEN_LOCAL="local-token" GH_TOKEN_APP="app-token" SELF_REPO="FS-GG/.github" \
    OP_LOCK_ISSUES='{"FS-GG/.github": 2714, "FS-GG/FS.GG.SDD": 878, "FS-GG/FS.GG.Rendering": 1245, "FS-GG/FS.GG.Governance": 410, "FS-GG/FS.GG.Templates": 413, "FS-GG/FS.GG.Game": 604, "FS-GG/FS.GG.Audio": 259, "FS-GG/FS.GG.Net": 72}' \
    "$@" \
    bash "$AUTHORIZE" >"$WORK/out" 2>&1 && echo 0 || echo $?
}

out_value() { sed -n "s/^$1=//p" "$WORK/step_output" | tail -1; }

# green <name> [VAR=VALUE ...] — exit 0 AND proceed=true.
green() {
  local name="$1"; shift
  reset_world
  local rc; rc="$(run_authorize "$@")"
  if [ "$rc" != 0 ]; then bad "$name" "$(printf 'expected exit 0, got %s\n%s' "$rc" "$(cat "$WORK/out")")"; return; fi
  if [ "$(out_value proceed)" != "true" ]; then
    bad "$name" "$(printf 'exited 0 but proceed=%q — the dispatch would not fire\n%s' "$(out_value proceed)" "$(cat "$WORK/out")")"; return
  fi
  ok "$name"
}

# red <name> <pattern> [VAR=VALUE ...] — must exit non-zero, emit ::error, AND say why.
red() {
  local name="$1" pat="$2"; shift 2
  reset_world
  local rc; rc="$(run_authorize "$@")"
  if [ "$rc" = 0 ]; then
    bad "$name" "$(printf 'expected a REFUSAL, got exit 0 (proceed=%q) — an unfenced dispatch would fire\n%s' "$(out_value proceed)" "$(cat "$WORK/out")")"; return
  fi
  grep -q '::error' "$WORK/out" || { bad "$name" "exited non-zero but emitted no ::error annotation"; return; }
  grep -qi -- "$pat" "$WORK/out" && ok "$name" \
    || bad "$name" "$(printf 'refused, but not for the stated reason (want %q):\n%s' "$pat" "$(cat "$WORK/out")")"
}

# collapsed <name> <pattern> [VAR=VALUE ...] — exit 0, proceed=false, and it SAYS so. A duplicate is a
# no-op, not a failure: redding it would make an idempotent retry look like a broken one.
collapsed() {
  local name="$1" pat="$2"; shift 2
  local rc; rc="$(run_authorize "$@")"
  if [ "$rc" != 0 ]; then bad "$name" "$(printf 'a duplicate must be a GREEN no-op, got exit %s\n%s' "$rc" "$(cat "$WORK/out")")"; return; fi
  if [ "$(out_value proceed)" = "true" ]; then
    bad "$name" "$(printf 'proceed=true — the duplicate would dispatch a SECOND time\n%s' "$(cat "$WORK/out")")"; return
  fi
  grep -q '::error' "$WORK/out" && { bad "$name" "a duplicate emitted ::error — wrong severity"; return; }
  grep -qi -- "$pat" "$WORK/out" && ok "$name" \
    || bad "$name" "$(printf 'collapsed, but never explains itself (want %q):\n%s' "$pat" "$(cat "$WORK/out")")"
}

echo "== A. authorize: the refusal truth table =="

green "A1  the happy path authorizes"

red "A2  a presented opkey that does not recompute" 'does not recompute' \
    OPKEY=0000000000000000000000000000000000000000000000000000000000000000

# The grant is the ONE check a requester cannot satisfy by typing: the id is server-assigned and the
# winner is decided by a CAS. These three legs are the fence.
red "A3  a grant that is not the live winner" 'is not the live winner' GRANT=9999999999
red "A4  a grant naming a comment that is not there" 'is not the live winner' GRANT=1111111111

echo "-- A5: the winner is LEASE-FILTERED, so a lapsed grant is not a grant --"
reset_world
write_comments "$RECEIVER_DEFAULT" 72 "[{\"id\":$GRANT_DEFAULT,\"kind\":\"claim\",\"age\":100000,\"lease\":10,\"worker\":\"godwit-00f5\"}]"
rc="$(run_authorize)"
if [ "$rc" != 0 ] && grep -q 'no live grant holds' "$WORK/out"; then
  ok "A5  a grant whose lease lapsed holds nothing"
else
  bad "A5  a grant whose lease lapsed holds nothing" "$(printf 'exit %s\n%s' "$rc" "$(cat "$WORK/out")")"
fi

echo "-- A6: a LOWER live marker beats ours; lowest id wins, and the loser is refused --"
reset_world
write_comments "$RECEIVER_DEFAULT" 72 \
  "[{\"id\":$GRANT_DEFAULT,\"kind\":\"claim\",\"age\":30,\"lease\":10,\"worker\":\"godwit-00f5\"},
    {\"id\":11,\"kind\":\"claim\",\"age\":30,\"lease\":10,\"worker\":\"rival-0001\"}]"
rc="$(run_authorize)"
if [ "$rc" != 0 ] && grep -q "rival-0001" "$WORK/out"; then
  ok "A6  a lower live grant wins and the loser is refused BY NAME"
else
  bad "A6  a lower live grant wins and the loser is refused BY NAME" "$(printf 'exit %s\n%s' "$rc" "$(cat "$WORK/out")")"
fi

# The opkey must be recomputed for the changed generation, or this leg refuses at the opkey check and
# never reaches the one it is testing — a #266 vacuous failure inside the fixture itself.
red "A7  a generation that is not the item's live claim" 'is stale for' \
    GENERATION=1234567890 OPKEY="$(opkey_of "$ITEM_DEFAULT" 1234567890 "$RECEIVER_DEFAULT" "$OP_DEFAULT")"

echo "-- A8: no live claim on the item at all — the tenancy has ended --"
reset_world
write_comments "FS-GG/.github" 2720 "[{\"id\":$GEN_DEFAULT,\"kind\":\"claim\",\"age\":100000,\"lease\":120,\"worker\":\"godwit-00f5\"}]"
rc="$(run_authorize)"
if [ "$rc" != 0 ] && grep -q 'names a tenancy that has ended' "$WORK/out"; then
  ok "A8  a released/expired item claim authorizes nothing"
else
  bad "A8  a released/expired item claim authorizes nothing" "$(printf 'exit %s\n%s' "$rc" "$(cat "$WORK/out")")"
fi

red "A9  a receiver with no operation-lock issue" 'absent ref' \
    RECEIVER=FS-GG/FS.GG.NotARepo \
    OPKEY="$(opkey_of "$ITEM_DEFAULT" "$GEN_DEFAULT" FS-GG/FS.GG.NotARepo "$OP_DEFAULT")"

echo "-- A10: an unparseable fsgg:claim marker refuses the WHOLE scan (Reads.fs:473-486) --"
reset_world
write_comments "$RECEIVER_DEFAULT" 72 \
  "[{\"id\":$GRANT_DEFAULT,\"kind\":\"claim\",\"age\":30,\"lease\":10},{\"id\":99,\"kind\":\"claim-nolease\"}]"
rc="$(run_authorize)"
if [ "$rc" != 0 ] && grep -q 'refusing the whole scan' "$WORK/out"; then
  ok "A10 an unreadable marker refuses the scan instead of being dropped"
else
  bad "A10 an unreadable marker refuses the scan instead of being dropped" "$(printf 'exit %s\n%s' "$rc" "$(cat "$WORK/out")")"
fi

echo "-- A11: a read that did not complete is a NO-VERDICT, never a pass --"
reset_world
rc="$(run_authorize GH_STUB_FAIL=1)"
if [ "$rc" != 0 ] && grep -q 'no-verdict, never a pass' "$WORK/out"; then
  ok "A11 a failed REST read refuses rather than reading as an empty issue"
else
  bad "A11 a failed REST read refuses rather than reading as an empty issue" "$(printf 'exit %s\n%s' "$rc" "$(cat "$WORK/out")")"
fi

red "A12 op=merge is not brokered here"        'not brokered here' \
    OP=merge OPKEY="$(opkey_of "$ITEM_DEFAULT" "$GEN_DEFAULT" "$RECEIVER_DEFAULT" merge)"
red "A13 op=publish:<package> carries no dispatch grant" 'publication effect' \
    OP=publish:FS.GG.Contracts OPKEY="$(opkey_of "$ITEM_DEFAULT" "$GEN_DEFAULT" "$RECEIVER_DEFAULT" publish:FS.GG.Contracts)"
red "A14 an op outside the closed vocabulary"  'closed operation vocabulary' \
    OP=teleport OPKEY="$(opkey_of "$ITEM_DEFAULT" "$GEN_DEFAULT" "$RECEIVER_DEFAULT" teleport)"

# The key must name the operation ACTUALLY performed, or every downstream idempotence answer is about
# a different operation than the one that landed.
red "A15 op=dispatch:<x> while dispatching event-type <y>" 'different operation than the one that lands' \
    OP=dispatch:something-else \
    OPKEY="$(opkey_of "$ITEM_DEFAULT" "$GEN_DEFAULT" "$RECEIVER_DEFAULT" dispatch:something-else)"

# The component domain, transcribed from `Operation.preimage`.
red "A16 the board's <repo>#N shorthand is not GitHub grammar" '2107' \
    ITEM='.github#2720' OPKEY="$(opkey_of '.github#2720' "$GEN_DEFAULT" "$RECEIVER_DEFAULT" "$OP_DEFAULT")"
red "A17 a leading-zero generation keys one tenancy two ways" 'leading-zero' \
    GENERATION=0012 OPKEY="$(opkey_of "$ITEM_DEFAULT" 0012 "$RECEIVER_DEFAULT" "$OP_DEFAULT")"
red "A18 the engine's \`released\` sentinel is not a generation" 'canonical server-assigned' \
    GENERATION=released OPKEY="$(opkey_of "$ITEM_DEFAULT" released "$RECEIVER_DEFAULT" "$OP_DEFAULT")"
red "A19 a receiver spelled owner/repo#N is not a receiver" 'not owner/repo' \
    RECEIVER='FS-GG/FS.GG.Net#1' OPKEY="$(opkey_of "$ITEM_DEFAULT" "$GEN_DEFAULT" 'FS-GG/FS.GG.Net#1' "$OP_DEFAULT")"
red "A20 a blank item is refused before any network" 'blank' \
    ITEM='' OPKEY="$(opkey_of '' "$GEN_DEFAULT" "$RECEIVER_DEFAULT" "$OP_DEFAULT")"

echo "-- A21: a repository no credential can reach is a REFUSAL, not an unauthenticated read --"
reset_world
rc="$(run_authorize GH_TOKEN_APP=)"
if [ "$rc" != 0 ] && grep -q 'no credential can read' "$WORK/out"; then
  ok "A21 an empty App token refuses rather than reading a private repo unauthenticated"
else
  bad "A21 an empty App token refuses rather than reading a private repo unauthenticated" "$(printf 'exit %s\n%s' "$rc" "$(cat "$WORK/out")")"
fi

echo "-- A22: token SELECTION — the App for receivers, GITHUB_TOKEN for this repository (§6.1(b)) --"
reset_world
run_authorize >/dev/null
if grep -qx 'app-token' "$WORK/stub/tokens.log" && grep -qx 'local-token' "$WORK/stub/tokens.log"; then
  ok "A22 the receiver is read with the App token and FS-GG/.github with GITHUB_TOKEN"
else
  bad "A22 the receiver is read with the App token and FS-GG/.github with GITHUB_TOKEN" \
      "$(printf 'tokens used:\n%s' "$(cat "$WORK/stub/tokens.log" 2>/dev/null)")"
fi

echo
echo "== D. opkey dedupe: two triggers in one window must not produce two dispatches =="

# D1 is the parent row's own acceptance, made mechanical. Run 1 authorizes; the receipt it would post
# appears on the item; run 2 must collapse. Nothing about the concurrency group is involved — that is
# the point of keeping these two properties apart.
reset_world
rc1="$(run_authorize)"
proceed1="$(out_value proceed)"
write_comments "FS-GG/.github" 2720 \
  "[{\"id\":$GEN_DEFAULT,\"kind\":\"claim\",\"age\":60,\"lease\":120,\"worker\":\"godwit-00f5\"},
    {\"id\":777,\"kind\":\"opeffect\",\"opkey\":\"$OPKEY_DEFAULT\"}]"
if [ "$rc1" = 0 ] && [ "$proceed1" = true ]; then
  collapsed "D1  the SECOND trigger in the same window collapses to a no-op" 'collapsing this repeat'
else
  bad "D1  the SECOND trigger in the same window collapses to a no-op" \
      "$(printf 'the first trigger did not even authorize (exit %s, proceed=%q)' "$rc1" "$proceed1")"
fi

# D2. The dedupe is EXACT on the opkey. A receipt for a different operation must not collapse this
# one — a dedupe that matched loosely would silently drop real work, which is worse than a duplicate.
reset_world
write_comments "FS-GG/.github" 2720 \
  "[{\"id\":$GEN_DEFAULT,\"kind\":\"claim\",\"age\":60,\"lease\":120,\"worker\":\"godwit-00f5\"},
    {\"id\":778,\"kind\":\"opeffect\",\"opkey\":\"$(opkey_of "$ITEM_DEFAULT" "$GEN_DEFAULT" FS-GG/FS.GG.Game "$OP_DEFAULT")\"}]"
rc="$(run_authorize)"
if [ "$rc" = 0 ] && [ "$(out_value proceed)" = true ]; then
  ok "D2  a receipt for a DIFFERENT opkey does not collapse this dispatch"
else
  bad "D2  a receipt for a DIFFERENT opkey does not collapse this dispatch" "$(printf 'exit %s proceed=%q\n%s' "$rc" "$(out_value proceed)" "$(cat "$WORK/out")")"
fi

# D3. THE DEDUPE IS NOT AN AUTHORIZATION, AND ORDER IS THE PROPERTY. A receipt present alongside an
# invalid grant must still REFUSE. If the collapse ran first, a forger could suppress the fence by
# posting a receipt — turning §4.3's "audit and authority for nothing" into authority for everything.
reset_world
write_comments "FS-GG/.github" 2720 \
  "[{\"id\":$GEN_DEFAULT,\"kind\":\"claim\",\"age\":60,\"lease\":120,\"worker\":\"godwit-00f5\"},
    {\"id\":779,\"kind\":\"opeffect\",\"opkey\":\"$OPKEY_DEFAULT\"}]"
rc="$(run_authorize GRANT=9999999999)"
if [ "$rc" != 0 ] && grep -q 'is not the live winner' "$WORK/out"; then
  ok "D3  a receipt cannot suppress the fence — refusal precedes collapse"
else
  bad "D3  a receipt cannot suppress the fence — refusal precedes collapse" "$(printf 'exit %s\n%s' "$rc" "$(cat "$WORK/out")")"
fi

echo
echo "== K. the operation key: composition pinned to \`Operation.preimage\` =="

# K1. A KNOWN-ANSWER VECTOR, computed here from the documented pre-image rather than read back out of
# the thing under test. If the broker's composition ever drifts, this is the leg that says so.
K_ITEM="FS-GG/FS.GG.Net#58"; K_GEN="5177045416"; K_RECV="FS-GG/FS.GG.Net"; K_OP="dispatch:fs-gg-effect"
K_EXPECT="$(python3 -c '
import hashlib
print(hashlib.sha256(b"FS-GG/FS.GG.Net#58\n5177045416\nFS-GG/FS.GG.Net\ndispatch:fs-gg-effect").hexdigest())')"
reset_world
write_comments "$K_RECV" 72 "[{\"id\":$GRANT_DEFAULT,\"kind\":\"claim\",\"age\":30,\"lease\":10}]"
write_comments "FS-GG/FS.GG.Net" 58 "[{\"id\":$K_GEN,\"kind\":\"claim\",\"age\":30,\"lease\":120}]"
rc="$(run_authorize ITEM="$K_ITEM" GENERATION="$K_GEN" RECEIVER="$K_RECV" OP="$K_OP" \
      EVENT_TYPE=fs-gg-effect OPKEY="$K_EXPECT")"
if [ "$rc" = 0 ] && [ "$(out_value opkey)" = "$K_EXPECT" ]; then
  ok "K1  the key is sha256(item \\n gen \\n receiver \\n op), lowercase hex (known-answer vector)"
else
  bad "K1  the key is sha256(item \\n gen \\n receiver \\n op), lowercase hex" \
      "$(printf 'exit %s, opkey=%q, expected %q\n%s' "$rc" "$(out_value opkey)" "$K_EXPECT" "$(cat "$WORK/out")")"
fi

# K2-K5. EVERY component must move the key. A key that ignored one of its four components would let
# two genuinely different operations dedupe onto each other — the failure an idempotence key exists to
# prevent, and the one a single happy-path assertion cannot see.
k_distinct() { # <name> <item> <gen> <receiver> <op>
  local name="$1"; shift
  local got; got="$(opkey_of "$@")"
  if [ "$got" != "$K_EXPECT" ]; then ok "$name"; else bad "$name" "the key did not change — this component is not in the pre-image"; fi
}
k_distinct "K2  changing the item changes the key"      "FS-GG/FS.GG.Net#59" "$K_GEN" "$K_RECV" "$K_OP"
k_distinct "K3  changing the generation changes the key" "$K_ITEM" "5177045417" "$K_RECV" "$K_OP"
k_distinct "K4  changing the receiver changes the key"   "$K_ITEM" "$K_GEN" "FS-GG/FS.GG.Audio" "$K_OP"
k_distinct "K5  changing the operation changes the key"  "$K_ITEM" "$K_GEN" "$K_RECV" "dispatch:other"

# K6 WAS A GREP OVER `Operation.fs`, AND IT IS GONE ON PURPOSE. It asserted that the engine's source
# still contained two exact literals — `let private Separator = "\n"` and the `String.concat` line —
# which is the wrong instrument in BOTH directions. It stayed green on a semantic change the literals
# survived, and it would have redded on a pure REFORMAT of `Operation.fs` while nothing at all was
# wrong: "it reds the moment the producer becomes correct" is the trap this board corrected in
# `tests/receiver-validate/run.sh` the same week. The board analyst named its insufficiency directly
# when it reopened `.github#2720` — "a grep-based constant leg is not that test".
#
# Section E replaces it with the test that WAS asked for: both implementations EXECUTED over one
# corpus, their verdicts, digests and refusal classes compared. That subsumes everything K6 checked,
# because a changed separator or a reordered pre-image now changes an observed digest rather than an
# observed string of source text — and legs E2/E3 prove exactly that by making those two mutations and
# measuring the red.

echo
echo "== S. the single-caller property: a CORPUS, inverted in both directions =="

# The corpus is what stops this being an absence assertion satisfied by a matcher that finds nothing.
# NINE spellings that are real callers and MUST match; SIX lookalikes that MUST NOT. Three entries are
# lifted verbatim from live production code and are labelled where they came from.
corpus_case() { # <slot> <yaml-body>
  local dir="$WORK/corpus/$1/.github/workflows"
  mkdir -p "$dir"
  printf '%s\n' "$2" > "$dir/case.yml"
  python3 "$SCANNER" --root "$WORK/corpus/$1" --json 2>"$WORK/corpus_err"
}

# `case` rather than a pipeline: `printf … | grep -q` kills the writer as soon as grep matches, so
# the status being tested belongs to a pipeline that exited early — the class
# `tests/pipefail-assertions` refuses, and the discipline this board paid for today.
saw_caller() { case "$1" in *'"case.yml"'*) return 0 ;; *) return 1 ;; esac; }
must_match() { # <name> <slot> <yaml>
  local got; got="$(corpus_case "$2" "$3" || true)"
  if saw_caller "$got"; then ok "$1"
  else bad "$1" "$(printf 'the matcher did NOT see this caller — got %s\n%s' "${got:-<nothing>}" "$(cat "$WORK/corpus_err")")"; fi
}
must_not_match() { # <name> <slot> <yaml>
  local got; got="$(corpus_case "$2" "$3" || true)"
  if saw_caller "$got"; then bad "$1" "the matcher called this a caller: $got"
  else ok "$1"; fi
}

echo "-- nine real caller spellings, all of which MUST match --"
must_match "S1  local path (the broker's own spelling)" c1 \
'name: x
on: workflow_dispatch
jobs:
  d:
    uses: ./.github/workflows/dispatch-sender.yml'
must_match "S2  cross-repo @main (docs/coordination/auto-update-fabric.md:74)" c2 \
'name: x
on: workflow_dispatch
jobs:
  d:
    uses: FS-GG/.github/.github/workflows/dispatch-sender.yml@main'
must_match "S3  cross-repo SHA pin — LIVE in FS.GG.Rendering/template-dispatch.yml:85" c3 \
'name: x
on: workflow_dispatch
jobs:
  d:
    uses: FS-GG/.github/.github/workflows/dispatch-sender.yml@5fed2838f9ed085ffca09f4cc18b4f7bc59c1294  # main as of 2026-06-28'
must_match "S4  double-quoted scalar" c4 \
'name: x
on: workflow_dispatch
jobs:
  d:
    uses: "FS-GG/.github/.github/workflows/dispatch-sender.yml@main"'
must_match "S5  single-quoted scalar" c5 \
"name: x
on: workflow_dispatch
jobs:
  d:
    uses: 'FS-GG/.github/.github/workflows/dispatch-sender.yml@main'"
must_match "S6  lowercase owner (GitHub is case-insensitive)" c6 \
'name: x
on: workflow_dispatch
jobs:
  d:
    uses: fs-gg/.github/.github/workflows/dispatch-sender.yml@main'
must_match "S7  the .yaml extension" c7 \
'name: x
on: workflow_dispatch
jobs:
  d:
    uses: FS-GG/.github/.github/workflows/dispatch-sender.yaml@main'
must_match "S8  a fully-spelled refs/heads ref" c8 \
'name: x
on: workflow_dispatch
jobs:
  d:
    uses: FS-GG/.github/.github/workflows/dispatch-sender.yml@refs/heads/main'
# S9 is the spelling a line-anchored regex loses: the value is folded across two lines, so the text
# `uses: FS-GG/...` never appears on any single line of the file.
must_match "S9  a folded block scalar (no single line carries the whole call)" c9 \
'name: x
on: workflow_dispatch
jobs:
  d:
    uses: >-
      FS-GG/.github/.github/workflows/dispatch-sender.yml@main'

echo "-- six lookalikes, none of which may match --"
must_not_match "S10 a comment saying it does NOT call it — LIVE, release-kit.yml:57" n1 \
'# Neither this workflow nor release-coord-engine.yml calls `dispatch-sender.yml`. #1776 asked whether
name: release-kit
on: workflow_dispatch
jobs:
  d:
    runs-on: ubuntu-latest
    steps:
      - run: "true"'
must_not_match "S11 a comment saying it should not — LIVE, release-coord-engine.yml:32" n2 \
'# does not call `dispatch-sender.yml`, and should not: no receiver has an `on: repository_dispatch`
name: release-coord-engine
on: workflow_dispatch
jobs:
  d:
    runs-on: ubuntu-latest
    steps:
      - run: "true"'
must_not_match "S12 the callee naming ITSELF — LIVE, dispatch-sender.yml:29" n3 \
'name: dispatch-sender
on:
  workflow_call:
    inputs:
      target-repo:
        type: string
        required: true
jobs:
  dispatch:
    runs-on: ubuntu-latest
    steps:
      - run: "true"'
must_not_match "S13 the path as text in a run: step — the shape tests/skill-union/current-truth.sh:35 uses" n4 \
'name: x
on: workflow_dispatch
jobs:
  d:
    runs-on: ubuntu-latest
    steps:
      - run: cp "$ROOT/.github/workflows/dispatch-sender.yml" "$target/.github/workflows/"'
must_not_match "S14 a DIFFERENT workflow whose name extends the callee's" n5 \
'name: x
on: workflow_dispatch
jobs:
  d:
    uses: FS-GG/.github/.github/workflows/dispatch-sender-selftest.yml@main'
must_not_match "S15 the path as an env VALUE, not a uses:" n6 \
'name: x
on: workflow_dispatch
env:
  SENDER: .github/workflows/dispatch-sender.yml
jobs:
  d:
    runs-on: ubuntu-latest
    steps:
      - run: "true"'

echo "-- the real tree, and the two mutations that must red it --"
if python3 "$SCANNER" --root "$REPO_ROOT" >"$WORK/sc_out" 2>&1; then
  ok "S16 the REAL tree has exactly one caller, and it is fsgg-dispatch-broker.yml"
else
  bad "S16 the REAL tree has exactly one caller, and it is fsgg-dispatch-broker.yml" "$(cat "$WORK/sc_out")"
fi

# The inversion that matters most. A copy of the real workflow directory plus ONE added caller must
# red, by name. This is the "a newly added caller is refused" half of the property.
MUT="$WORK/mutation/.github/workflows"; mkdir -p "$MUT"
cp "$REPO_ROOT"/.github/workflows/*.yml "$MUT/"
cat > "$MUT/rogue-dispatcher.yml" <<'YML'
name: rogue-dispatcher
on: workflow_dispatch
jobs:
  send:
    uses: FS-GG/.github/.github/workflows/dispatch-sender.yml@main
    with:
      target-repo: FS-GG/FS.GG.Net
      event-type: whatever
      version: "1.0.0"
    secrets:
      app-id: ${{ secrets.FSGG_DISPATCH_APP_ID }}
      app-private-key: ${{ secrets.FSGG_DISPATCH_APP_PRIVATE_KEY }}
YML
if python3 "$SCANNER" --root "$WORK/mutation" >"$WORK/sc_mut" 2>&1; then
  bad "S17 a newly added caller is REFUSED" "the scanner passed a tree with a second caller in it — the lock is a suggestion"
elif grep -q 'rogue-dispatcher.yml' "$WORK/sc_mut"; then
  ok "S17 a newly added caller is REFUSED, by name"
else
  bad "S17 a newly added caller is REFUSED, by name" "$(printf 'it refused, but never named the offender:\n%s' "$(cat "$WORK/sc_mut")")"
fi

# The OTHER inversion, and the one this whole file exists for. Remove the broker's call and the answer
# must be RED, not a cheerful "no second caller found". A matcher that reports a clean tree when it can
# see nothing at all is the `.github#2312` failure, and it is what makes every other leg here vacuous.
BLIND="$WORK/blind/.github/workflows"; mkdir -p "$BLIND"
cp "$REPO_ROOT"/.github/workflows/*.yml "$BLIND/"
rm -f "$BLIND/fsgg-dispatch-broker.yml"
if python3 "$SCANNER" --root "$WORK/blind" >"$WORK/sc_blind" 2>&1; then
  bad "S18 ZERO callers is a REFUSAL, not a clean tree" \
      "the scanner reported success over a tree with NO caller at all. Every 'no second caller' verdict it gives is now vacuous — this is exactly the .github#2312 failure."
elif grep -qi 'vacuous' "$WORK/sc_blind"; then
  ok "S18 ZERO callers is a REFUSAL, and it says why"
else
  bad "S18 ZERO callers is a REFUSAL, and it says why" "$(printf 'it refused, but not for the vacuity reason:\n%s' "$(cat "$WORK/sc_blind")")"
fi

# A workflow that will not parse is a NO-VERDICT (exit 3), never a skip: it may be carrying the caller.
NV="$WORK/noverdict/.github/workflows"; mkdir -p "$NV"
cp "$REPO_ROOT/.github/workflows/fsgg-dispatch-broker.yml" "$NV/"
printf 'name: broken\non: [push\njobs: {\n' > "$NV/broken.yml"
# `cmd; rc=$?` is wrong under `set -e`: the non-zero exit aborts the fixture BEFORE `rc` is read, and
# every remaining leg — the whole structural and receipt sections — silently vanishes with a green-ish
# looking tail. Capture it in the same statement instead.
rc=0; python3 "$SCANNER" --root "$WORK/noverdict" >"$WORK/sc_nv" 2>&1 || rc=$?
if [ "$rc" = 3 ] && grep -q 'no verdict' "$WORK/sc_nv"; then
  ok "S19 an unparseable workflow is a NO-VERDICT (exit 3), never a silent skip"
else
  bad "S19 an unparseable workflow is a NO-VERDICT (exit 3), never a silent skip" "$(printf 'exit %s\n%s' "$rc" "$(cat "$WORK/sc_nv")")"
fi

echo
echo "== B. structure: the fence cannot be edited into decoration (the epic #266 class) =="

# B1-B3. PER-RECEIVER CONCURRENCY, at WORKFLOW level. A job-level group would serialise one job while
# leaving the rest of the run free; the design's §5.2 step 3 asks for the workflow.
if [ "$(m "['concurrency_present']")" = "True" ]; then
  ok "B1  concurrency is declared at WORKFLOW level"
else
  bad "B1  concurrency is declared at WORKFLOW level" "the workflow-level \`concurrency:\` block is gone — dispatches against one receiver no longer serialise at all (design §5.2 step 3)."
fi

GROUP_EXPECTED='fsgg-dispatch-${{ inputs.receiver }}'
GROUP_ACTUAL="$(m "['concurrency'].get('group','')")"
if [ "$GROUP_ACTUAL" = "$GROUP_EXPECTED" ]; then
  ok "B2  the concurrency group is exactly per-RECEIVER"
else
  bad "B2  the concurrency group is exactly per-RECEIVER" \
      "$(printf 'expected %s\nactual   %s\nA group that does not vary with the receiver serialises every receiver behind one queue (a throughput bug); a group that varies with anything ELSE — the item, the run id — serialises nothing at all, which is the fence-shaped hole.' "$GROUP_EXPECTED" "$GROUP_ACTUAL")"
fi

if [ "$(m "['concurrency'].get('cancel-in-progress')")" = "False" ]; then
  ok "B3  cancel-in-progress is false — an in-flight dispatch is never cancelled"
else
  bad "B3  cancel-in-progress is false" \
      "cancel-in-progress is truthy. A cancelled run may already have POSTed to the receiver and not yet written its receipt — that is design §8.5's partial application, manufactured deliberately rather than survived."
fi

# B4. The single `uses:`, and the local spelling. A cross-repo `@main` here would call whatever `main`
# holds at run time rather than the sender this repository ships.
if [ "$(m "['dispatch_uses']")" = "./.github/workflows/dispatch-sender.yml" ]; then
  ok "B4  the dispatch job calls the LOCAL dispatch-sender.yml"
else
  bad "B4  the dispatch job calls the LOCAL dispatch-sender.yml" "dispatch_uses = $(m "['dispatch_uses']")"
fi

# B5. `needs:` and the `if:` are the two edits that make the fence decorative while leaving every line
# of it in place.
case "$(m "['dispatch_needs']")" in *authorize*) DISPATCH_NEEDS_OK=1 ;; *) DISPATCH_NEEDS_OK=0 ;; esac
if [ "$DISPATCH_NEEDS_OK" = 1 ]; then
  ok "B5a the dispatch job still \`needs: authorize\`"
else
  bad "B5a the dispatch job still \`needs: authorize\`" "\`needs: authorize\` was removed, so the dispatch fires regardless of the verdict. The fence is decorative."
fi
if [ "$(m "['dispatch_if']")" = "needs.authorize.outputs.proceed == 'true'" ]; then
  ok "B5b the dispatch job is gated on proceed == 'true'"
else
  bad "B5b the dispatch job is gated on proceed == 'true'" \
      "$(printf 'dispatch.if = %q. Without this exact gate a COLLAPSED duplicate (which exits 0) dispatches a second time — the very thing the opkey dedupe exists to prevent.' "$(m "['dispatch_if']")")"
fi

# B6. A skipped job reports GREEN; a continue-on-error job reports SUCCESS while failing. Either turns
# the fence off silently.
if [ "$(m "['authorize_has_if']")" = "False" ]; then
  ok "B6a the authorize job carries no \`if:\` — it cannot be skipped into green"
else
  bad "B6a the authorize job carries no \`if:\`" "an \`if:\` was added to the authorize job. A skipped job reports GREEN, so this disables the fence for whichever calls the condition excludes."
fi
if [ "$(m "['authorize_continue_on_error']")" = "False" ]; then
  ok "B6b the authorize JOB carries no \`continue-on-error\`"
else
  bad "B6b the authorize JOB carries no \`continue-on-error\`" "a FAILING fence now reports SUCCESS and the dispatch runs anyway."
fi
if [ "$(m "['authorize_run_steps_continue_on_error']")" = "[]" ]; then
  ok "B6c no \`run:\` step in authorize swallows its own failure"
else
  bad "B6c no \`run:\` step in authorize swallows its own failure" "these verdict steps swallow their exit: $(m "['authorize_run_steps_continue_on_error']")"
fi

# B7. The injection rule, from closing-keywords.yml: attacker-controlled text pasted into a `run:`
# block is a script-injection sink. `op`, `event-type` and `version` are free-form caller strings.
if [ "$(m "['authorize_run_interpolates']")" = "False" ]; then
  ok "B7  the authorize script interpolates no \`\${{ }}\` — every input arrives through env:"
else
  bad "B7  the authorize script interpolates no \`\${{ }}\`" \
      "a \`\${{ }}\` expression was pasted into the authorize \`run:\` block. Every input here is attacker-controlled text; this is a script-injection sink (closing-keywords.yml:97-105)."
fi

# B8. THE SECOND COPY OF THE OP-LOCK TABLE, PINNED. A runner cannot reach `Options.opLockRef` without
# building the engine, so the table is restated in the workflow — and pinned here in BOTH directions,
# because a row missing from either side is a receiver that dispatches unfenced or one that cannot
# dispatch at all. `FS.GG.Net` is the row that matters most: the chore-lock table omits it, and
# `FS.GG.Net#58` is one of the two PRs `.github#1858` measured as merged by the unlocked executor.
# Compared as CONTENT, never as rendered text: two dicts that agree row for row but were written in a
# different order are the same table, and a fixture that redded on that would train its next reader to
# ignore it. The diff below names the rows on each side rather than printing two blobs.
LOCK_DIFF="$(python3 - "$META" <<'PY'
import json, sys
meta = json.load(open(sys.argv[1]))
wf = {k: int(v) for k, v in meta["workflow_op_locks"].items()}
engine = {k: int(v) for k, v in meta["engine_op_locks"].items()}
if not engine:
    print("could not parse opLockNumbers out of Options.fs at all — this leg has no verdict, which is not a pass")
elif wf != engine:
    only_wf = sorted(set(wf.items()) - set(engine.items()))
    only_engine = sorted(set(engine.items()) - set(wf.items()))
    print(f"in the workflow only: {only_wf}; in Options.fs only: {only_engine}")
PY
)"
if [ -z "$LOCK_DIFF" ]; then
  ok "B8a the workflow's op-lock table is identical to Options.opLockNumbers"
else
  bad "B8a the workflow's op-lock table is identical to Options.opLockNumbers" \
      "$LOCK_DIFF
A row present in one and not the other is either a receiver that dispatches unfenced or one that cannot dispatch at all."
fi

# B8b. CONTAINMENT, NOT AN EQUALITY ON THE COUNT. This leg used to require exactly `8 True`, which
# would have redded the day a NINTH FS-GG repository was onboarded and correctly added to BOTH tables
# — "an assertion that reds the moment the producer becomes correct", the shape corrected in
# `tests/receiver-validate/run.sh` this same week. What this leg actually owns is that the roster has
# not SHRUNK and that FS.GG.Net specifically is in it; B8a above already pins exact agreement with
# `Options.opLockNumbers`, so growth is checked there, in the one place that can see both sides.
ROSTER="$(python3 -c "
import json, sys
table = json.load(open(sys.argv[1]))['workflow_op_locks']
print(len(table) >= 8, 'FS-GG/FS.GG.Net' in table, len(table))" "$META")"
case "$ROSTER" in
  "True True "*)
    ok "B8b the roster has not shrunk below its eight repositories, FS.GG.Net included" ;;
  *)
    bad "B8b the roster has not shrunk below its eight repositories, FS.GG.Net included" \
        "(>=8, FS.GG.Net present, rows) = $ROSTER. The chore-lock table omits FS.GG.Net, and FS.GG.Net#58 is one of the two PRs .github#1858 measured as merged by the unlocked executor — a per-receiver table built the same way would inherit the hole in exactly the repository the incident reached." ;;
esac

# B8c. The lease the broker filters the op-lock's winners with is `Client.OpLock.LeaseMinutes`. It is a
# constant in two places for the same reason the table is, so it is pinned the same way.
if grep -qE '^\s*let LeaseMinutes = 10\s*$' "$CLIENT_FS"; then
  ok "B8c Client.OpLock.LeaseMinutes is still 10"
else
  bad "B8c Client.OpLock.LeaseMinutes is still 10" \
      "the engine's operation-lock lease changed. The broker filters live grants using each marker's own \`lease=\` field, which the engine writes from this constant — so a change here changes which grants the broker considers live."
fi

# B9. THE GATE MUST BE ABLE TO SEE A NEW CALLER. A `paths:` filter narrower than `.github/workflows/**`
# means a PR that ADDS a caller does not trigger this fixture at all — and the design states the rule
# itself in §6.2: "a filtered workflow produces no check run on a PR that misses the filter".
case "$(m "['selftest_pr_paths']")" in *".github/workflows/**"*) SELFTEST_PATHS_OK=1 ;; *) SELFTEST_PATHS_OK=0 ;; esac
if [ "$SELFTEST_PATHS_OK" = 1 ]; then
  ok "B9a the selftest triggers on ANY workflow change — a new caller cannot slip past the filter"
else
  bad "B9a the selftest triggers on ANY workflow change" \
      "$(printf 'pull_request.paths = %s\nA caller can only live in .github/workflows/, so a filter that does not cover all of it lets a PR add one without ever running this gate.' "$(m "['selftest_pr_paths']")")"
fi
if [ "$(m "['selftest_runs_fixture']")" = "True" ]; then
  ok "B9b the selftest actually runs tests/dispatch-broker/run.sh"
else
  bad "B9b the selftest actually runs tests/dispatch-broker/run.sh" "the selftest workflow no longer invokes this fixture, so none of the legs above run in CI."
fi
# B9d. THE ORACLE'S OWN SOURCE MUST BE IN THE TRIGGER, and it is more load-bearing now than when it
# was first written. Before section E, `Operation.fs` was in the `paths:` filter because a grep read
# it; now the fixture BUILDS it and executes `Operation.compose` as the oracle every digest comparison
# is scored against. A PR that changes the engine's composition and does not run this fixture is
# exactly the drift `.github#2720` was reopened for. CONTAINMENT, not equality: the filter may grow.
case "$(m "['selftest_pr_paths']")" in
  *"src/FS.GG.Coord.Core/Operation.fs"*)
    ok "B9d the selftest triggers on Operation.fs — a changed engine re-runs the differential" ;;
  *)
    bad "B9d the selftest triggers on Operation.fs — a changed engine re-runs the differential" \
        "$(printf 'pull_request.paths = %s\nSection E scores the broker against `Operation.compose`. If a PR can change that function without running this fixture, the two implementations can drift apart in exactly the way this slice was reopened to prevent.' "$(m "['selftest_pr_paths']")")" ;;
esac
if [ "$(m "['selftest_installs_dotnet']")" = "True" ]; then
  ok "B9c the selftest installs the .NET SDK, so section E can EXECUTE the engine in CI"
else
  bad "B9c the selftest installs the .NET SDK, so section E can EXECUTE the engine in CI" \
      "the selftest workflow no longer sets up dotnet. Section E's transcription differential compares the broker against \`Operation.compose\` by executing BOTH; without the SDK on the runner it has no verdict, and a no-verdict section is how .github#2720's grep-based pin got there in the first place."
fi

# B10. The broker writes one comment and reads two issues. `contents: write` would let a compromised
# step push to this repository from a workflow whose whole job is to send a dispatch.
if [ "$(m "['permissions'].get('contents')")" = "read" ] && [ "$(m "['permissions'].get('issues')")" = "write" ]; then
  ok "B10 permissions are contents:read + issues:write and no more"
else
  bad "B10 permissions are contents:read + issues:write and no more" "permissions = $(m "['permissions']")"
fi

# B11. The receipt is written after the effect and is audit for nothing — but it must still be
# ATTEMPTED, or `.github#1858` AC3's audit trail does not exist.
case "$(m "['receipt_needs']")" in *dispatch*) RECEIPT_NEEDS_OK=1 ;; *) RECEIPT_NEEDS_OK=0 ;; esac
if [ "$RECEIPT_NEEDS_OK" = 1 ]; then
  ok "B11 the receipt job runs only after the dispatch it records"
else
  bad "B11 the receipt job runs only after the dispatch it records" "receipt_needs = $(m "['receipt_needs']")"
fi

echo
echo "== E. the transcription differential: the broker's opkey domain vs \`Operation.compose\`, EXECUTED =="

# WHY THIS SECTION EXISTS. `.github#2720` was reopened on 2026-08-17 with two findings, and this is the
# one that lives inside this slice's declared paths:
#
#   "Either have the broker call the engine, or add a test that fails when the transcription and
#    `Operation.preimage` disagree. A grep-based constant leg is not that test."
#
# The broker cannot call the engine — a GitHub runner would have to build `FS.GG.Coord.Core` on the
# critical path of every dispatch — so the second route is taken: `differential.py` drives the SHIPPED
# `run:` block (lifted out of the real workflow by `yaml.safe_load`) and `engine_opkey.fsx` drives the
# SHIPPED `Operation.compose` (through `FS.GG.Coord.Core.dll`), over one corpus of 48 vectors, and
# compares the accept/refuse verdict, the 64-hex digest, and the refusal class. Neither side is
# re-typed. See `differential.py`'s module docstring for why the broker is driven in-process and why
# every vector presents a deliberately wrong opkey.
#
# EVERY LEG BELOW THAT ASSERTS A RED PERFORMS ITS OWN MUTATION AND MEASURES THE RESULT. A differential
# that has never been seen to fail is a differential nobody has any reason to believe, and this
# fixture's whole subject is gates that cannot fail on their subject. E8 is the control that keeps the
# other legs honest: it mutates something section E must NOT be sensitive to and asserts E stays
# green — while asserting that leg B2 reds on that same mutant, so "E did not notice" is demonstrably
# "E is aimed at its own subject" and not "E notices nothing".

DIFFERENTIAL="$HERE/differential.py"
ENGINE_FSX="$HERE/engine_opkey.fsx"
# Filled in below from `scripts/build-gate-engine`. NOT a path inside this checkout: see E0.
CORE_DLL=""

# mutate_broker <slot> <old> <new> — write a mutated copy of the REAL workflow and PROVE the edit
# landed where it was aimed: the anchor must match exactly once, and the file's sha256 must actually
# change. Verified by parse and by hash, never by eye — three separate counts of one YAML file were
# published wrong on this board in a single session, one of them under an explicit "by parse" claim.
mutate_broker() {
  python3 - "$BROKER_WF" "$WORK/mutant-$1.yml" "$2" "$3" "$1" <<'PY'
import hashlib, sys
source_path, out_path, old, new, slot = sys.argv[1:6]
with open(source_path, encoding="utf-8") as fh:
    source = fh.read()
hits = source.count(old)
if hits != 1:
    sys.exit(f"the `{slot}` anchor matched {hits} times, not once — this mutation did not land where it was aimed")
mutated = source.replace(old, new)
before, after = (hashlib.sha256(text.encode("utf-8")).hexdigest() for text in (source, mutated))
if before == after:
    sys.exit(f"the `{slot}` mutation left the artifact hash unchanged — nothing was mutated")
with open(out_path, "w", encoding="utf-8") as fh:
    fh.write(mutated)
print(f"    {slot}: anchor matched once, sha256 {before[:12]} -> {after[:12]}")
PY
}

# mutation_leg <name> <wanted-exit> <slot> <old> <new>
#
# A STALE ANCHOR IS A COUNTED FAILURE, NEVER AN ABORT. `mutate_broker` exits non-zero when its anchor
# no longer matches exactly once, and under `set -e` that would tear the fixture down mid-section —
# truncating every remaining leg and the summary line, which is the same "green-ish looking tail"
# hazard leg S19 above already spells out. It is also not a hypothetical: it was MEASURED here, while
# taking this section's own gate-inversion evidence, because the in-tree mutation being tested had
# changed the very line leg E2's anchor points at. So the failure is caught and reported as what it
# is — the mutation no longer tests what it claims to, which is a finding about the fixture.
mutation_leg() {
  local name="$1" want="$2" slot="$3" old="$4" new="$5"
  if ! mutate_broker "$slot" "$old" "$new" >"$WORK/mutate_out" 2>&1; then
    bad "$name" "$(printf 'the mutation could not be applied, so this leg tested NOTHING:\n%s' "$(cat "$WORK/mutate_out")")"
    return
  fi
  cat "$WORK/mutate_out"
  expect_differential "$name" "$want" "$WORK/mutant-$slot.yml"
}

# differential_exit <workflow> [dll] — echo the exit code. NEVER through a pipe: `$?` after a pipeline
# reads the LAST command's status and not the gate's, which is one of this session's measured
# instrument faults and would silently score every leg here against `head`.
differential_exit() {
  local wf="$1" dll="$2"; shift 2
  local rc=0
  python3 "$DIFFERENTIAL" --workflow "$wf" --fsx "$ENGINE_FSX" --dll "$dll" "$@" \
    >"$WORK/diff_out" 2>&1 || rc=$?
  echo "$rc"
}

# expect_differential <name> <wanted-exit> <workflow> [dll] [extra-flags...]
expect_differential() {
  local name="$1" want="$2" wf="$3" dll="${4:-$CORE_DLL}"; shift 4 2>/dev/null || shift $#
  local rc; rc="$(differential_exit "$wf" "$dll" "$@")"
  if [ "$rc" = "$want" ]; then ok "$name"
  else bad "$name" "$(printf 'expected exit %s, got %s\n%s' "$want" "$rc" "$(cat "$WORK/diff_out")")"; fi
}

if ! command -v dotnet >/dev/null 2>&1; then
  # A RED, NOT A SKIP. Section E's whole claim is that the two implementations are compared by
  # EXECUTION; a fixture that quietly drops that comparison when the toolchain is missing has rebuilt
  # the fails-open shape this slice exists to close, and would report green on a runner where the
  # comparison never happened. `fsgg-dispatch-broker-selftest.yml` installs the SDK for exactly this
  # reason, and leg B9c below asserts that it still does.
  bad "E0  the engine oracle can be executed" \
      "\`dotnet\` is not on PATH, so \`Operation.compose\` cannot be executed and section E has NO VERDICT.
This is deliberately a failure rather than a skip: a transcription differential that silently does not run is
indistinguishable from one that runs and agrees, which is precisely the defect .github#2720 was reopened for."
else
  # .github#2653 — ASK FOR THE ANSWER, DO NOT BUILD INTO THE CHECKOUT, AND DO NOT RESTATE THE LAYOUT.
  # `scripts/build-gate-engine` is the ONE sanctioned way a gate harness gets an engine: it sites both
  # `bin/` and `obj/` outside the checkout, so nothing is ever created here for `scripts/fsgg-coord`'s
  # tier 2a to prefer over the shared checkout's engine. The first head of this section built
  # `FS.GG.Coord.Core` in place and `tests/engine-build-siting` refused it, correctly: a harness build
  # is not the deliberate worktree build tier 2a exists to protect, and it fail-closes every board
  # write from the worktree it poisons. `FS.GG.Coord.Core.dll` sits beside the engine binary because
  # the CLI project references it, so this needs no second path convention of its own.
  #
  # Foreground, with the exit code in its own variable: a BACKGROUNDED `dotnet build` reports exit 0
  # while emitting MSB1003, because a background command does not inherit `cd`.
  build_rc=0
  ENGINE_BIN="$(bash "$REPO_ROOT/scripts/build-gate-engine" 2>"$WORK/build.log")" || build_rc=$?
  if [ "$build_rc" = 0 ] && [ -n "$ENGINE_BIN" ]; then
    CORE_DLL="$(dirname "$ENGINE_BIN")/FS.GG.Coord.Core.dll"
  fi
  if [ ! -f "$CORE_DLL" ]; then
    bad "E0  the engine oracle can be executed" \
        "$(printf 'scripts/build-gate-engine did not yield FS.GG.Coord.Core.dll (exit %s, engine=%q), so there is no oracle to compare against:\n%s' \
           "$build_rc" "$ENGINE_BIN" "$(tail -30 "$WORK/build.log")")"
  fi

  if [ -f "$CORE_DLL" ]; then
    ok "E0  the engine oracle is the shipped FS.GG.Coord.Core assembly, built OUT OF TREE"

    # E0b. THE ORACLE'S ARTIFACT IS NOT IN THIS CHECKOUT, ASSERTED RATHER THAN TRUSTED. The whole
    # reason E0 goes through `scripts/build-gate-engine` is `.github#2653`, and "we call the right
    # script" is a weaker claim than "no shadowing artifact exists". `tests/engine-build-siting` reads
    # the INVOCATION; this reads the RESULT, which is the second method that would catch a
    # build-gate-engine whose redirect had silently stopped working.
    case "$CORE_DLL" in
      "$REPO_ROOT"/*)
        bad "E0b the oracle's artifact is sited OUTSIDE this checkout" \
            "the oracle resolved to $CORE_DLL, which is inside $REPO_ROOT. scripts/fsgg-coord tier 2a prefers a source build under the caller's toplevel, so this artifact would shadow the shared engine and stale_guard would fail-close EVERY board write from this worktree (.github#2653)." ;;
      *)
        if [ -d "$REPO_ROOT/src/FS.GG.Coord.Core/bin" ] || [ -d "$REPO_ROOT/src/FS.GG.Coord.Cli/bin" ]; then
          bad "E0b the oracle's artifact is sited OUTSIDE this checkout" \
              "the oracle itself is out of tree, but src/FS.GG.Coord.{Core,Cli}/bin exists in this checkout. Something built the engine in place; tier 2a will prefer it and stale_guard will refuse this worktree's board writes (.github#2653)."
        else
          ok "E0b the oracle's artifact is sited OUTSIDE this checkout — no tier-2a shadow"
        fi ;;
    esac

    # E1. The claim itself. `--cross-check` additionally drives every env-expressible vector through
    # the real `python3` subprocess boundary and refuses on any disagreement with the in-process
    # harness, so the harness that reaches the ill-formed-text vectors is itself measured.
    expect_differential "E1  the shipped broker agrees with \`Operation.compose\` on every vector" \
                        0 "$BROKER_WF" "$CORE_DLL" --cross-check

    # E2/E3. THE TWO MUTATIONS K6'S GREP EXISTED TO CATCH, now caught by an observed digest instead of
    # an observed string of source text.
    mutation_leg "E2  a changed pre-image SEPARATOR is caught (digests diverge)" 1 sep \
      'preimage = "\n".join([item, generation, receiver, op])' \
      'preimage = "\t".join([item, generation, receiver, op])'

    mutation_leg "E3  a REORDERED pre-image is caught (digests diverge)" 1 order \
      'preimage = "\n".join([item, generation, receiver, op])' \
      'preimage = "\n".join([generation, item, receiver, op])'

    # E4-E7. THE DOMAIN, WHICH THE GREP NEVER REACHED AT ALL. Each of these leaves the digest
    # arithmetic untouched and widens the set of inputs the broker will key — the failure mode that
    # matters, because a broker whose domain is wider than the engine's composes keys the engine would
    # refuse, and those keys can never match a receipt the engine wrote.
    mutation_leg "E4  dropping the leading-zero rule is caught (one tenancy keyed two ways)" 1 zero \
      'return len(value) > 0 and value[0] != "0" and all("0" <= ch <= "9" for ch in value)' \
      'return len(value) > 0 and all("0" <= ch <= "9" for ch in value)'

    mutation_leg "E5  dropping the unpaired-surrogate guard is caught" 1 surrogate \
      'if any(0xD800 <= ord(ch) <= 0xDFFF for ch in value):' 'if False:'

    mutation_leg "E6  dropping the control-character guard is caught" 1 control \
      'if any(unicodedata.category(ch) == "Cc" for ch in value):' 'if False:'

    mutation_leg "E7  a changed owner/repo grammar is caught" 1 grammar \
      'halves = value.split("/")' 'halves = value.split("|")'

    # E8. THE HARNESS CONTROL, and the leg that makes the seven above mean something. It mutates the
    # concurrency group — a real defect, and one section E has no business detecting. If E redded here
    # too, every red above would be evidence only that the differential reacts to ANY edit. It must
    # stay green, AND leg B2's structural check must red on the same mutant, so the pair shows the two
    # gates are aimed at different subjects rather than one of them being blind.
    mutation_leg "E8a CONTROL: a concurrency-group defect does NOT red the differential" 0 concurrency \
      'group: fsgg-dispatch-${{ inputs.receiver }}' 'group: fsgg-dispatch-constant'
    MUTANT_GROUP="$(python3 -c "
import sys, yaml
with open(sys.argv[1], encoding='utf-8') as fh:
    doc = yaml.safe_load(fh)
print((doc.get('concurrency') or {}).get('group', ''))" "$WORK/mutant-concurrency.yml")"
    if [ "$MUTANT_GROUP" != "$GROUP_EXPECTED" ] && [ "$GROUP_ACTUAL" = "$GROUP_EXPECTED" ]; then
      ok "E8b CONTROL: ...and leg B2's per-receiver check DOES red on that same mutant"
    else
      bad "E8b CONTROL: ...and leg B2's per-receiver check DOES red on that same mutant" \
          "$(printf 'mutant group=%q, real group=%q, expected=%q. If B2 does not distinguish these, then E8a proves nothing: "the differential did not notice" would be indistinguishable from "nothing notices".' \
             "$MUTANT_GROUP" "$GROUP_ACTUAL" "$GROUP_EXPECTED")"
    fi

    # E9/E10. THE TWO WAYS THIS SECTION COULD SILENTLY STOP TESTING ANYTHING. Both must be a
    # NO-VERDICT (exit 3) and never a pass.
    expect_differential "E9  an ABSENT engine assembly is a no-verdict, never a pass" \
                        3 "$BROKER_WF" "$WORK/there-is-no-such.dll"

    mutation_leg "E10 a \`run:\` block this differential cannot extract is a no-verdict" 3 heredoc \
      "          set -euo pipefail
          python3 - <<'PY'
          import hashlib" "          set -euo pipefail
          python3 - <<'PYTHON'
          import hashlib"
  fi
fi

echo
echo "== R. the receipt: the fsgg:op-effect marker's exact shape (design §4.3) =="
reset_world
: > "$WORK/stub/posted.json"
if env -i PATH="$STUB:/usr/bin:/bin" HOME="$HOME" GH_STUB_DIR="$WORK/stub" \
     ITEM="$ITEM_DEFAULT" RECEIVER="$RECEIVER_DEFAULT" OP="$OP_DEFAULT" OPKEY="$OPKEY_DEFAULT" \
     GRANT="$GRANT_DEFAULT" GH_TOKEN_LOCAL="local-token" GH_TOKEN_APP="app-token" \
     SELF_REPO="FS-GG/.github" EVIDENCE="https://example.invalid/run/9" \
     bash "$RECEIPT" >"$WORK/out" 2>&1; then
  body="$(python3 -c "import json;print(json.load(open('$WORK/stub/posted.json'))['body'])")"
  missing=""
  for field in "fsgg:op-effect v=1" "opkey=$OPKEY_DEFAULT" "grant=$GRANT_DEFAULT" \
               "receiver=$RECEIVER_DEFAULT" "op=$OP_DEFAULT" "evidence=https://example.invalid/run/9"; do
    case "$body" in *"$field"*) : ;; *) missing="$missing $field" ;; esac
  done
  if [ -z "$missing" ]; then ok "R1  the receipt carries every field design §4.3 names"
  else bad "R1  the receipt carries every field design §4.3 names" "missing:$missing
body: $body"; fi
else
  bad "R1  the receipt carries every field design §4.3 names" "$(cat "$WORK/out")"
fi

# R2. A failed receipt POST is RED and says the effect LANDED. Reporting it as a failed dispatch would
# send an operator to re-drive an operation that already happened.
if env -i PATH="$STUB:/usr/bin:/bin" HOME="$HOME" GH_STUB_DIR="$WORK/stub" GH_STUB_POST_FAIL=1 \
     ITEM="$ITEM_DEFAULT" RECEIVER="$RECEIVER_DEFAULT" OP="$OP_DEFAULT" OPKEY="$OPKEY_DEFAULT" \
     GRANT="$GRANT_DEFAULT" GH_TOKEN_LOCAL="local-token" GH_TOKEN_APP="app-token" \
     SELF_REPO="FS-GG/.github" EVIDENCE="https://example.invalid/run/9" \
     bash "$RECEIPT" >"$WORK/out" 2>&1; then
  bad "R2  a failed receipt POST is RED" "the receipt step exited 0 after the POST failed — the audit gap is invisible"
elif grep -q 'LANDED' "$WORK/out" && grep -q '::error' "$WORK/out"; then
  ok "R2  a failed receipt POST is RED and says the dispatch LANDED anyway"
else
  bad "R2  a failed receipt POST is RED and says the dispatch LANDED anyway" "$(cat "$WORK/out")"
fi

echo
echo "-- $pass passed, $failcount failed --"
[ "$failcount" -eq 0 ] || exit 1
