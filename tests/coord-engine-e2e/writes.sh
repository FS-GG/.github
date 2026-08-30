#!/usr/bin/env bash
# End-to-end: the compiled engine's WRITE commands, over HTTP, against a STATEFUL fixture — no token, no net.
#
# The read fixture proves scan→decide. This proves the other half: the claim CAS and the capability-typed
# writes, driven as real CLI commands against a server that remembers what they posted. The CAS is the
# sharp one — `claim` posts a marker, RE-READS, and wins only if its marker is the lowest live one; a
# fixture that forgot the marker would make the command fail its own re-read, so a green here is a green
# over a real read-modify-reread.
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
ENGINE="${FSGG_COORD_ENGINE_BIN:-$REPO_ROOT/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine}"

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# #1569's ledger is deliberately populated by the executable drivers below, then compared with
# the freshly-built binary's advertised contract at the end.  A hand-maintained expectation is
# useful only as a test subject; it must never be the answer to its own coverage question.
declare -A CONTRACT_DRIVEN=()
declare -A CONDITIONAL_MODES=()
contract_name() {
  case "$1" in
    "reap "*) printf '%s' reap ;;
    "reconcile "*) printf '%s' reconcile ;;
    "flush "*) printf '%s' flush ;;
    "next "*) printf '%s' next ;;
    "room open"*) printf '%s' 'room open' ;;
    *) printf '%s' "$1" ;;
  esac
}
mark_contract() {
  local command; command="$(contract_name "$1")"
  if [ -n "${CONTRACT_DRIVEN[$command]:-}" ]; then
    bad "#1569: $command must have exactly one contract driver" "already driven by ${CONTRACT_DRIVEN[$command]}"
  else
    CONTRACT_DRIVEN[$command]="$2"
  fi
}
mark_mode() { CONDITIONAL_MODES["$1:$2"]=1; }

[ -x "$ENGINE" ] || { echo "FAIL  build the engine first: dotnet build src/FS.GG.Coord.Cli -c Release" >&2; exit 1; }

SRV_OUT="$(mktemp)"; CACHE_DIR="$(mktemp -d)"; PREDICATE_FIX="$(mktemp -d)"; CYCLE_SNAPSHOT="$(mktemp)"; CYCLE_FIX="$(mktemp -d)"; INTAKE_DRAFT="$(mktemp)"; INTAKE_BLOCKED_DRAFT="$(mktemp)"; INTAKE_REPOS="$(mktemp -d)"; ROUTE_RECEIPT="$(mktemp)"; SDD_ROOT="$(mktemp -d)"; RESUME_GIT="$(mktemp -d)"; COMMENT_CREATE_BODY="$(mktemp)"; COMMENT_AMEND_BODY="$(mktemp)"
FORCE_BUDGET_CACHE="$(mktemp -d)"
python3 "$HERE/stateful_server.py" >"$SRV_OUT" 2>&1 &
SRV_PID=$!
trap 'kill "$SRV_PID" 2>/dev/null; rm -f "$SRV_OUT" "$CYCLE_SNAPSHOT" "$INTAKE_DRAFT" "$INTAKE_BLOCKED_DRAFT" "$ROUTE_RECEIPT" "$COMMENT_CREATE_BODY" "$COMMENT_AMEND_BODY"; rm -rf "$CACHE_DIR" "$PREDICATE_FIX" "$CYCLE_FIX" "$FORCE_BUDGET_CACHE" "$INTAKE_REPOS" "$SDD_ROOT" "$RESUME_GIT"' EXIT

PORT=""
for _ in $(seq 1 50); do PORT="$(head -n1 "$SRV_OUT" 2>/dev/null)"; [ -n "$PORT" ] && break; sleep 0.1; done
[ -n "$PORT" ] || { bad "the fixture bound a port"; echo "writes: 0 passed, 1 failed"; exit 1; }

export FSGG_GITHUB_API_BASE="http://127.0.0.1:$PORT"
export GITHUB_TOKEN="fixture-token"
export FSGG_COORD_OWNER="FS-GG" FSGG_COORD_PROJECT="Coordination"
export FSGG_COORD_CACHE="$CACHE_DIR" FSGG_COORD_SCAN_TTL_SEC=0
mkdir -p "$INTAKE_REPOS/FS.GG.SDD/src/Valid"
git -C "$INTAKE_REPOS/FS.GG.SDD" init -q
git -C "$INTAKE_REPOS/FS.GG.SDD" remote add origin https://github.com/FS-GG/FS.GG.SDD.git
export FSGG_REPOS_ROOT="$INTAKE_REPOS"
export FSGG_CYCLE_JOURNAL="$CYCLE_FIX/journal.json"
# A clean identity, so the derivation never depends on the CI runner's env.
#
# `FSGG_WORKER=""` ALONE WAS NOT THAT, and .github#1646 is what made it visible. `Identity.resolve` reads
# `--worker` first, then `$FSGG_WORKER`, then the HARNESS SESSION — so an empty `FSGG_WORKER` falls through
# to `CLAUDE_CODE_SESSION_ID`/`OPENCODE_SESSION_ID`/`FSGG_AGENT_SESSION_ID` and this process derives an
# identity from whatever agent ran the script. That was invisible while nothing consulted it. It is
# consulted now: every `--worker vole-418` below is measured against the id this process derives for
# itself, and the lock verbs refuse the disagreement over vole-418's live marker (#1646). Measured: 13 of
# the 47 assertions here fail under an exported `CLAUDE_CODE_SESSION_ID` and pass without one.
#
# So unset the whole ladder, not just its second rung. What remains is a caller that derives NOTHING and
# names itself with `--worker` — the human-operator case the flag exists for, and what this fixture is.
unset CLAUDE_CODE_SESSION_ID OPENCODE_SESSION_ID FSGG_AGENT_SESSION_ID FSGG_AGENT_HARNESS
export FSGG_WORKER=""

run() { "$ENGINE" "$@" --worker vole-418; }

# ---- #2753: explicit comment amendments prove target ownership before the global PATCH route ------
printf '%s' 'owner-thread-original' >"$COMMENT_CREATE_BODY"
printf '%s' 'cross-thread-replacement' >"$COMMENT_AMEND_BODY"
comment_create_out="$(run comment create FS.GG.SDD#43 FS.GG.SDD#42 "$COMMENT_CREATE_BODY" --json 2>&1)"
comment_create_rc=$?
comment_id="$(printf '%s' "$comment_create_out" | jq -r '.commentId // empty' 2>/dev/null)"
comment_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq -r --argjson id "${comment_id:-0}" '.[] | select(.id == $id) | .body')"
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations" >/dev/null
comment_amend_out="$(run comment amend FS.GG.SDD#42 FS.GG.SDD#42 "${comment_id:-0}" "$COMMENT_AMEND_BODY" --json 2>&1)"
comment_amend_rc=$?
comment_mutations="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations")"
comment_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq -r --argjson id "${comment_id:-0}" '.[] | select(.id == $id) | .body')"
if [ "$comment_create_rc" -eq 0 ] && [ -n "$comment_id" ] && [ "$comment_before" = 'owner-thread-original' ] &&
   [ "$comment_amend_rc" -ne 0 ] && grep -q 'refusing before PATCH' <<<"$comment_amend_out" &&
   [ "$(printf '%s' "$comment_mutations" | jq -r '.count')" -eq 0 ] && [ "$comment_after" = "$comment_before" ]; then
  ok "#2753: comment amend refuses a cross-thread id before PATCH with zero mutation"
else
  bad "#2753: comment amend refuses a cross-thread id before PATCH with zero mutation" "create=$comment_create_rc:$comment_create_out id=$comment_id before=$comment_before amend=$comment_amend_rc:$comment_amend_out mutations=$comment_mutations after=$comment_after"
fi
mark_contract "comment" "target-owned-verified-mutation"

# ---- #2137: delivery-route record is the declared, source-bound comment write ----------------------
#
# This is one COMMAND contract driver, not a helper-level codec check: the real executable reads the
# issue body, validates the exact receipt, then makes its one append-only POST.  The inversion pins the
# security property the command exists for — stale source evidence must fail before the ledger changes.
#
# An `sdd-required` route whose SDD package does not exist yet (or is not yet `implementationReady`) is
# DIFFERENT (#2298): it records — the coordinator's decision is honest and explicit even before the
# package exists, because the CLAIMED WORKER is the actor who produces that package, and could never be
# claimed to do so if recording it required the package first. Each case below asserts the receipt
# posts and reports `sddPackageReady` truthfully, never that it refuses.
# v2 binds authorization directly to its structured scope/dependencies/touch-set and no longer needs
# this writer harness to derive a subject revision from a duplicate copy of the narrative issue body.
route_revision=0
route_digest=""
write_route_record() {
  local path="$1" selected="$2" work_id="$3" rationale="$4" next_revision previous
  next_revision=$((route_revision + 1))
  previous="$route_digest"
  route_digest="$(python3 - "$path" "$selected" "$work_id" "$rationale" "$next_revision" "$previous" <<'PY'
import hashlib, json, sys
path, selected, work_id, rationale, revision, previous = sys.argv[1:]
revision = int(revision)
previous = previous or None
work_id = work_id or None
spec_home = f"work/{work_id}/spec.md" if work_id else None
gates = ["implementationReady", "analyze", "verify", "ship"] if work_id else []
record = {
    "schema": "fsgg.coord.route-decision/v2", "subject": "FS-GG/FS.GG.SDD#42",
    "revision": revision, "previousDigest": previous, "scope": ["fixture-route"],
    "dependencies": ["none"], "touchSet": ["src/Thing/**"],
    "policyVersion": "structured-decisions/1", "route": selected,
    "agent": "fixture-route-record", "timestamp": "2026-01-01T00:00:00Z",
    "reasonCodes": ["fixture"], "rationale": rationale, "sddWorkId": work_id,
    "specHome": spec_home, "requiredGates": gates,
}
def frame(value):
    raw = value.encode()
    return f"{len(raw)}:{value}"
def scalar(value): return frame(value or "")
def strings(values): return "".join(frame(value) for value in values)
fields = [frame(record["schema"]), frame(record["subject"]), str(revision), scalar(previous),
          strings(record["scope"]), strings(record["dependencies"]), strings(record["touchSet"]),
          frame(record["policyVersion"]), frame(selected), frame(record["agent"]),
          frame(record["timestamp"]), strings(record["reasonCodes"]), frame(rationale),
          scalar(work_id), scalar(spec_home), strings(gates)]
record["digest"] = hashlib.sha256("|".join(fields).encode()).hexdigest()
with open(path, "w", encoding="utf-8") as stream:
    json.dump(record, stream, separators=(",", ":"))
print(record["digest"])
PY
)"
  route_revision="$next_revision"
}
write_route_record "$ROUTE_RECEIPT" lightweight "" "Stateful structured route record."
route_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
route_out="$("$ENGINE" delivery-route record FS.GG.SDD#42 "$ROUTE_RECEIPT" 2>&1)"; route_rc=$?
route_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
if [ "$route_rc" -eq 0 ] && printf '%s' "$route_out" | jq -e '.kind == "recorded"' >/dev/null \
   && [ "$route_after" -eq $((route_before + 1)) ]; then
  ok "#2137: delivery-route record posts exactly one current source-bound receipt"
else
  bad "#2137: delivery-route record must post its current receipt once" "rc=$route_rc comments=$route_before->$route_after output=$route_out"
fi
mark_contract "delivery-route" "source-bound-record-and-zero-write-inversions"

route_stale="$(mktemp)"
printf '%s' "{\"schema\":\"fsgg.coord.delivery-route/v1\",\"subject\":\"FS-GG/FS.GG.SDD#42\",\"subjectRevision\":\"stale\",\"route\":\"lightweight\",\"agent\":\"fixture-route-record\",\"timestamp\":\"2026-01-01T00:00:00Z\",\"reasonCodes\":[\"fixture\"],\"rationale\":\"Stale source receipt.\",\"declaredImpacts\":[\"internal\"],\"observedFacts\":[\"localized\"],\"sddWorkId\":null,\"specHome\":null,\"requiredGates\":[]}" >"$route_stale"
route_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
"$ENGINE" delivery-route record FS.GG.SDD#42 "$route_stale" >/dev/null 2>&1; route_rc=$?
route_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
rm -f "$route_stale"
[ "$route_rc" -ne 0 ] && [ "$route_before" = "$route_after" ] \
  && ok "#2137: stale delivery-route receipt is refused with zero writes" \
  || bad "#2137: stale delivery-route receipt must not post" "rc=$route_rc comments=$route_before->$route_after"

route_missing_sdd="$(mktemp)"
write_route_record "$route_missing_sdd" sdd-required does-not-exist "No SDD package yet."
route_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
route_out="$("$ENGINE" delivery-route record FS.GG.SDD#42 "$route_missing_sdd" 2>/dev/null)"; route_rc=$?
route_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
rm -f "$route_missing_sdd"
if [ "$route_rc" -eq 0 ] && printf '%s' "$route_out" | jq -e '.kind == "recorded" and .sddPackageReady == false' >/dev/null \
   && [ "$route_after" -eq $((route_before + 1)) ]; then
  ok "#2298: an sdd-required receipt with no SDD package on disk yet still records, honestly not-ready"
else
  bad "#2298: an sdd-required receipt with no SDD package must still record" "rc=$route_rc comments=$route_before->$route_after output=$route_out"
fi

# A present directory is not enough on its own: the route's named analysis must be for that work and
# must already be `implementationReady` for `sddPackageReady` to report true. None of these run through
# `delivery-route record`'s REFUSAL path any more (#2298) — the SDD package's readiness is now reported
# on the posted receipt, never a precondition for posting it.
mkdir -p "$SDD_ROOT/work/fixture-sdd" "$SDD_ROOT/readiness/fixture-sdd"
printf '%s\n' '# fixture' >"$SDD_ROOT/work/fixture-sdd/spec.md"
route_sdd="$(mktemp)"
route_sdd_case() {
  local label="$1" analysis="$2" expected_ready="$3" route_before route_after route_rc route_out
  printf '%s' "$analysis" >"$SDD_ROOT/readiness/fixture-sdd/analysis.json"
  write_route_record "$route_sdd" sdd-required fixture-sdd "SDD readiness record: $label."
  route_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
  route_out="$(FSGG_COORD_SDD_ROOT="$SDD_ROOT" "$ENGINE" delivery-route record FS.GG.SDD#42 "$route_sdd" 2>/dev/null)"; route_rc=$?
  route_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
  if [ "$route_rc" -eq 0 ] \
     && printf '%s' "$route_out" | jq -e ".kind == \"recorded\" and .sddPackageReady == $expected_ready" >/dev/null \
     && [ "$route_after" -eq $((route_before + 1)) ]; then
    ok "#2298: $label SDD readiness records, reporting sddPackageReady=$expected_ready"
  else
    bad "#2298: $label SDD readiness must record with sddPackageReady=$expected_ready" "rc=$route_rc comments=$route_before->$route_after output=$route_out"
  fi
}
route_sdd_case "mismatched workId" '{"workId":"other-work","status":"implementationReady"}' false
route_sdd_case "unready status" '{"workId":"fixture-sdd","status":"analyzing"}' false
route_sdd_case "current implementationReady" '{"workId":"fixture-sdd","status":"implementationReady"}' true
# The later lock-contract legs intentionally run in the fixture's ordinary lightweight world.  Restore
# that route through the same command rather than reaching into server state, so their precondition is
# explicit and the SDD success probe cannot leak a checkout-local evidence dependency into another test.
write_route_record "$ROUTE_RECEIPT" lightweight "" "Restore the ordinary structured route."
"$ENGINE" delivery-route record FS.GG.SDD#42 "$ROUTE_RECEIPT" >/dev/null 2>&1 \
  || bad "#2137: restore the fixture's ordinary current route after SDD driver"
rm -f "$route_sdd"

# ---- M4: structured review authoring seals and appends initial/confirmation/acceptance ------------
review_draft="$(mktemp)"
# Acceptance binds the winning live claim generation. Scope that setup to this writer leg instead of
# putting it in the shared server's initial state, which must remain neutral for recorded-board replay.
review_claim_id="$(curl -fsS -X POST \
  -H 'Content-Type: application/json' \
  -d '{"body":"<!-- fsgg:claim worker=fixture-review lease=120 -->\nheld"}' \
  "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq -r '.id')"
# .github#2756: the live typed route may not turn an absent or malformed durable wait ledger into an
# actionable critic dispatch. The malformed control is deleted after observation so the authoring
# cases below start from an honestly readable ledger.
review_no_wait_out="$("$ENGINE" review FS.GG.SDD#42 --pr 42 --worker vole-418 --json 2>&1)"; review_no_wait_rc=$?
review_malformed_wait_id="$(curl -fsS -X POST \
  -H 'Content-Type: application/json' \
  -d '{"body":"<!-- fsgg:review-wait/v1 -->\n{\"schema\":"}' \
  "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq -r '.id')"
review_malformed_wait_out="$("$ENGINE" review FS.GG.SDD#42 --pr 42 --worker vole-418 --json 2>&1)"; review_malformed_wait_rc=$?
curl -fsS -X DELETE "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/comments/$review_malformed_wait_id" >/dev/null
if [ "$review_no_wait_rc" -ne 0 ] && [ "$review_malformed_wait_rc" -ne 0 ] \
   && printf '%s' "$review_no_wait_out" | jq -e '.verdict == "noVerdict" and .waitStatus == "noReceipt" and (.action == null)' >/dev/null \
   && printf '%s' "$review_malformed_wait_out" | jq -e '.verdict == "noVerdict" and .waitStatus == "invalid" and (.action == null)' >/dev/null; then
  ok "#2756 live review refuses absent/malformed wait authority before dispatch"
else
  bad "#2756 wait authority must gate the live dispatch route" "absent=$review_no_wait_rc:$review_no_wait_out malformed=$review_malformed_wait_rc:$review_malformed_wait_out"
fi
review_dispatch_wait="$(mktemp)"
write_dispatch_wait() {
  local event="$1" generation="$2"
  python3 - "$review_dispatch_wait" "$event" "$review_claim_id" "$generation" <<'PY'
import json, sys
from datetime import datetime, timedelta, timezone
path, event, claim, generation = sys.argv[1:]
now = datetime.now(timezone.utc).replace(microsecond=0)
if event == "enter":
    record = {"schema":"fsgg.coord.review-wait/v1","event":"enter","item":"FS-GG/FS.GG.SDD#42",
              "claimGeneration":claim,"reviewGeneration":generation,"kind":"initial-review",
              "enteredAt":now.isoformat().replace("+00:00", "Z"),
              "expiresAt":(now + timedelta(hours=4)).isoformat().replace("+00:00", "Z"),
              "evidenceRef":"https://fixture.invalid/review-dispatch"}
else:
    record = {"schema":"fsgg.coord.review-wait/v1","event":"cancel","reviewGeneration":generation,
              "at":now.isoformat().replace("+00:00", "Z"),"evidenceRef":"fixture cleanup"}
with open(path, "w", encoding="utf-8") as stream:
    json.dump(record, stream, separators=(",", ":"))
PY
}
write_dispatch_wait enter wrong-head:initial-review:0
"$ENGINE" review wait FS.GG.SDD#42 "$review_dispatch_wait" --pr 42 --json >/dev/null 2>&1; review_wrong_head_enter_rc=$?
review_wrong_head_out="$("$ENGINE" review FS.GG.SDD#42 --pr 42 --worker vole-418 --json 2>&1)"; review_wrong_head_rc=$?
write_dispatch_wait cancel wrong-head:initial-review:0
"$ENGINE" review wait FS.GG.SDD#42 "$review_dispatch_wait" --pr 42 --json >/dev/null 2>&1; review_wrong_head_cancel_rc=$?
write_dispatch_wait enter aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:initial-review:9
"$ENGINE" review wait FS.GG.SDD#42 "$review_dispatch_wait" --pr 42 --json >/dev/null 2>&1; review_wrong_round_enter_rc=$?
review_wrong_round_out="$("$ENGINE" review FS.GG.SDD#42 --pr 42 --worker vole-418 --json 2>&1)"; review_wrong_round_rc=$?
write_dispatch_wait cancel aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:initial-review:9
"$ENGINE" review wait FS.GG.SDD#42 "$review_dispatch_wait" --pr 42 --json >/dev/null 2>&1; review_wrong_round_cancel_rc=$?
if [ "$review_wrong_head_enter_rc" -eq 0 ] && [ "$review_wrong_head_rc" -ne 0 ] && [ "$review_wrong_head_cancel_rc" -eq 0 ] \
   && [ "$review_wrong_round_enter_rc" -eq 0 ] && [ "$review_wrong_round_rc" -ne 0 ] && [ "$review_wrong_round_cancel_rc" -eq 0 ] \
   && printf '%s' "$review_wrong_head_out" | jq -e '.verdict == "noVerdict" and .waitStatus == "waiting" and (.action == null) and (.reasons[0] | contains("expected generation"))' >/dev/null \
   && printf '%s' "$review_wrong_round_out" | jq -e '.verdict == "noVerdict" and .waitStatus == "waiting" and (.action == null) and (.reasons[0] | contains("expected generation"))' >/dev/null; then
  ok "#2756 dispatch requires the exact canonical wait head and round"
else
  bad "#2756 same-kind waits with a wrong head or round must not dispatch" "wrong-head=$review_wrong_head_enter_rc/$review_wrong_head_rc/$review_wrong_head_cancel_rc:$review_wrong_head_out wrong-round=$review_wrong_round_enter_rc/$review_wrong_round_rc/$review_wrong_round_cancel_rc:$review_wrong_round_out"
fi
rm -f "$review_dispatch_wait"
write_review_draft() {
  local kind="$1" verdict="$2" round="$3" initial="$4" preceding="$5" head="${6:-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa}" critic="${7:-critic-heron-42}"
  python3 - "$review_draft" "$kind" "$verdict" "$round" "$initial" "$preceding" "$head" "$critic" <<'PY'
import json, sys
path, kind, verdict, round_number, initial, preceding, head, critic = sys.argv[1:]
record = {
    "schema": "fsgg.coord.review-decision/v2",
    "subject": "FS-GG/FS.GG.SDD#42/pr/42",
    "revision": 0,
    "previousDigest": None,
    "headSha": head,
    "critic": critic,
    "verdict": verdict,
    "acceptedExceptions": [],
    "routeApplicability": "not-meaningful",
    "routeEvidence": ["hermetic writer fixture"],
    "policyVersion": "structured-decisions/1",
    "kind": kind,
    "round": int(round_number),
    "initialReview": initial or None,
    "precedingReview": preceding or None,
    "timestamp": "2026-08-14T12:00:00Z",
    "digest": ""
}
with open(path, "w", encoding="utf-8") as stream:
    json.dump(record, stream, separators=(",", ":"))
PY
}

review_wait_for_record="$(mktemp)"
enter_review_record_wait() {
  local generation="$1" kind="$2"
  python3 - "$review_wait_for_record" "$review_claim_id" "$generation" "$kind" <<'PY'
import json, sys
from datetime import datetime, timedelta, timezone
path, claim, generation, kind = sys.argv[1:]
entered = datetime.now(timezone.utc).replace(microsecond=0)
with open(path, "w", encoding="utf-8") as stream:
    json.dump({"schema":"fsgg.coord.review-wait/v1","event":"enter","item":"FS-GG/FS.GG.SDD#42",
               "claimGeneration":claim,"reviewGeneration":generation,"kind":kind,
               "enteredAt":entered.isoformat().replace("+00:00", "Z"),
               "expiresAt":(entered + timedelta(hours=4)).isoformat().replace("+00:00", "Z"),
               "evidenceRef":"https://fixture.invalid/review-queue"}, stream, separators=(",", ":"))
PY
  "$ENGINE" review wait FS.GG.SDD#42 "$review_wait_for_record" --pr 42 --json >/dev/null 2>&1
}
complete_review_record_wait() {
  local generation="$1" evidence="$2"
  python3 - "$review_wait_for_record" "$generation" "$evidence" <<'PY'
import json, sys
from datetime import datetime, timezone
with open(sys.argv[1], "w", encoding="utf-8") as stream:
    json.dump({"schema":"fsgg.coord.review-wait/v1","event":"complete",
               "reviewGeneration":sys.argv[2],
               "at":datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
               "evidenceRef":sys.argv[3]}, stream, separators=(",", ":"))
PY
  "$ENGINE" review wait FS.GG.SDD#42 "$review_wait_for_record" --pr 42 --json >/dev/null 2>&1
}

review_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
write_review_draft initial changes-required 0 "" ""
review_unwaited_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
"$ENGINE" review record FS.GG.SDD#42 "$review_draft" --pr 42 --json >/dev/null 2>&1; review_unwaited_rc=$?
review_unwaited_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
review_derived_initial_out="$("$ENGINE" review wait enter FS.GG.SDD#42 --pr 42 --json 2>&1)"; review_derived_initial_rc=$?
review_derived_initial_body="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" \
  | jq -r '[.[] | select(.body | startswith("<!-- fsgg:review-wait/v1 -->"))] | last | .body')"
review_initial_out="$("$ENGINE" review record FS.GG.SDD#42 "$review_draft" --pr 42 --json 2>&1)"; review_initial_rc=$?
review_initial_url="$(printf '%s' "$review_initial_out" | jq -r '.commentUrl // empty')"
review_initial_digest="$(printf '%s' "$review_initial_out" | jq -r '.digest // empty')"
review_marker_id="$(curl -fsS -X POST -H 'Content-Type: application/json' \
  -d '{"body":"<!-- fsgg:independent-review:v1 -->\nprose critic marker"}' \
  "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq -r '.id')"
review_marker_url="https://github.com/FS-GG/FS.GG.SDD/pull/42#issuecomment-$review_marker_id"
review_marker_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
complete_review_record_wait aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:initial-review:0 "$review_marker_url"; review_marker_complete_rc=$?
review_marker_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
review_marker_complete_out="$("$ENGINE" review wait FS.GG.SDD#42 "$review_wait_for_record" --pr 42 --json 2>&1)"; review_marker_repeat_rc=$?
complete_review_record_wait aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:initial-review:0 "sha256:$review_initial_digest"; review_digest_complete_rc=$?
review_normalized_evidence="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" \
  | jq -r '[.[] | select(.body | startswith("<!-- fsgg:review-wait/v1 -->")) | (.body | split("\n")[1] | fromjson) | select(.event == "complete")] | last | .evidenceRef')"
# #3068 second-order inversion: the `repair-phase` spelling is only a caller assertion. In this
# ordinary same-head confirmation topology the writer must derive confirmation, refuse the mismatch,
# and append neither immutable assertion authority nor a wait transition.
review_wrong_purpose_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
review_wrong_purpose_wait_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq '[.[] | select(.body | startswith("<!-- fsgg:review-wait/v1 -->"))] | length')"
review_wrong_purpose_out="$("$ENGINE" review assert-repair repair-phase FS.GG.SDD#42 "$review_initial_url" 'wrong semantic route' --pr 42 --worker accountable-purpose-host --json 2>&1)"; review_wrong_purpose_rc=$?
review_wrong_purpose_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
review_wrong_purpose_wait_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq '[.[] | select(.body | startswith("<!-- fsgg:review-wait/v1 -->"))] | length')"
if [ "$review_wrong_purpose_rc" -ne 0 ] \
   && [[ "$review_wrong_purpose_out" == *"requires purpose=confirmation"* ]] \
   && [ "$review_wrong_purpose_before" = "$review_wrong_purpose_after" ] \
   && [ "$review_wrong_purpose_wait_before" = "$review_wrong_purpose_wait_after" ]; then
  ok "#3068 repair-phase purpose is refused before assertion/wait append in ordinary confirmation topology"
else
  bad "#3068 caller purpose must not select immutable ordinary-confirmation authority" "rc=$review_wrong_purpose_rc:$review_wrong_purpose_out comments=$review_wrong_purpose_before->$review_wrong_purpose_after waits=$review_wrong_purpose_wait_before->$review_wrong_purpose_wait_after"
fi
# Legacy callers may use arbitrary generation names. A name that merely starts like a canonical token
# is still legacy: the canonical grammar is whole-string, not a substring classifier. This is the
# production-shaped inversion for the old unanchored predicate: under that mutation the completion
# tries to resolve a structured record for the junk token, refuses, and strands the wait.
review_legacy_junk_generation=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:initial-review:0-junk
review_legacy_junk_evidence=https://fixture.invalid/legacy-review-evidence
enter_review_record_wait "$review_legacy_junk_generation" initial-review; review_legacy_junk_enter_rc=$?
complete_review_record_wait "$review_legacy_junk_generation" "$review_legacy_junk_evidence"; review_legacy_junk_complete_rc=$?
review_legacy_junk_normalized="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" \
  | jq -r --arg generation "$review_legacy_junk_generation" '[.[] | select(.body | startswith("<!-- fsgg:review-wait/v1 -->")) | (.body | split("\n")[1] | fromjson) | select(.event == "complete" and .reviewGeneration == $generation)] | last | .evidenceRef')"
if [ "$review_derived_initial_rc" -eq 0 ] \
   && printf '%s' "$review_derived_initial_body" | sed -n '2p' | jq -e \
      '.claimGeneration == "'"$review_claim_id"'" and .reviewGeneration == "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:initial-review:0" and .kind == "initial-review" and .evidenceRef == "https://github.com/FS-GG/FS.GG.SDD/pull/42"' >/dev/null \
   && [ "$review_marker_complete_rc" -ne 0 ] && [ "$review_marker_repeat_rc" -ne 0 ] \
   && [ "$review_marker_before" -eq "$review_marker_after" ] \
   && grep -q "structured review-decision record $review_initial_url" <<<"$review_marker_complete_out" \
   && [ "$review_digest_complete_rc" -eq 0 ] && [ "$review_normalized_evidence" = "$review_initial_url" ]; then
  ok "#2859 engine-derived initial wait and pre-append structured-record evidence normalization"
else
  bad "#2859 wait entry/evidence authority must be host-owned and unambiguous" "enter=$review_derived_initial_rc:$review_derived_initial_out body=$review_derived_initial_body marker=$review_marker_complete_rc/$review_marker_repeat_rc:$review_marker_before->$review_marker_after:$review_marker_complete_out digest=$review_digest_complete_rc:$review_normalized_evidence expected=$review_initial_url"
fi
if [ "$review_legacy_junk_enter_rc" -eq 0 ] && [ "$review_legacy_junk_complete_rc" -eq 0 ] \
   && [ "$review_legacy_junk_normalized" = "$review_legacy_junk_evidence" ]; then
  ok "#2859 canonical-generation suffix anchoring: legacy-compatible junk inversion completes unchanged"
else
  bad "#2859 canonical generation classification must reject a trailing-junk suffix" "legacy-junk=$review_legacy_junk_enter_rc/$review_legacy_junk_complete_rc:$review_legacy_junk_normalized expected=$review_legacy_junk_evidence"
fi

# Move the fixture's live head without adding a structured record: two inert schema-name comments make
# the stateful server expose head B while the real ledger still contains only the changes-required
# initial at head A. The host-owned entry must derive confirmation round 1 from those live facts. Cancel
# it, delete the inert controls, and the original M4 chain continues on head A unchanged.
review_shift_one="$(curl -fsS -X POST -H 'Content-Type: application/json' -d '{"body":"fixture mentions fsgg.coord.review-decision/v2"}' "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq -r '.id')"
review_shift_two="$(curl -fsS -X POST -H 'Content-Type: application/json' -d '{"body":"second fixture mentions fsgg.coord.review-decision/v2"}' "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq -r '.id')"
review_derived_confirmation_out="$("$ENGINE" review wait enter FS.GG.SDD#42 --pr 42 --json 2>&1)"; review_derived_confirmation_rc=$?
review_derived_confirmation_body="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" \
  | jq -r '[.[] | select(.body | startswith("<!-- fsgg:review-wait/v1 -->"))] | last | .body')"
write_dispatch_wait cancel bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb:repair-confirmation:1
"$ENGINE" review wait FS.GG.SDD#42 "$review_dispatch_wait" --pr 42 --json >/dev/null 2>&1; review_derived_confirmation_cancel_rc=$?
curl -fsS -X DELETE "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/comments/$review_shift_one" >/dev/null
curl -fsS -X DELETE "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/comments/$review_shift_two" >/dev/null
if [ "$review_derived_confirmation_rc" -eq 0 ] && [ "$review_derived_confirmation_cancel_rc" -eq 0 ] \
   && printf '%s' "$review_derived_confirmation_body" | sed -n '2p' | jq -e \
      '.reviewGeneration == "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb:repair-confirmation:1" and .kind == "repair-confirmation"' >/dev/null; then
  ok "#2859 host-owned successor wait derives the moved head and confirmation round"
else
  bad "#2859 successor wait must derive head/kind/round without caller JSON" "enter=$review_derived_confirmation_rc:$review_derived_confirmation_out body=$review_derived_confirmation_body cancel=$review_derived_confirmation_cancel_rc"
fi

write_review_draft confirmation pass 1 https://fixture.invalid/wrong https://fixture.invalid/wrong
enter_review_record_wait aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:repair-confirmation:1 repair-confirmation
review_wrong_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
"$ENGINE" review record FS.GG.SDD#42 "$review_draft" --pr 42 --json >/dev/null 2>&1; review_wrong_rc=$?
review_wrong_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
write_review_draft confirmation pass 1 "$review_initial_url" "$review_initial_url"
review_confirmation_out="$("$ENGINE" review record FS.GG.SDD#42 "$review_draft" --pr 42 --json 2>&1)"; review_confirmation_rc=$?
review_confirmation_url="$(printf '%s' "$review_confirmation_out" | jq -r '.commentUrl // empty')"
complete_review_record_wait aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:repair-confirmation:1 "$review_confirmation_url"
write_review_draft acceptance accepted 0 "$review_initial_url" "$review_confirmation_url"
review_acceptance_out="$("$ENGINE" review record FS.GG.SDD#42 "$review_draft" --pr 42 --json 2>&1)"; review_acceptance_rc=$?
review_acceptance_id="$(printf '%s' "$review_acceptance_out" | jq -r '.commentId // empty')"
review_acceptance_body="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" \
  | jq -r --argjson id "${review_acceptance_id:-0}" '.[] | select(.id == $id) | .body')"
write_review_draft initial pass 0 "" "" bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb critic-tern-43
enter_review_record_wait bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb:initial-review:0 initial-review
review_moved_initial_out="$("$ENGINE" review record FS.GG.SDD#42 "$review_draft" --pr 42 --json 2>&1)"; review_moved_initial_rc=$?
review_moved_initial_url="$(printf '%s' "$review_moved_initial_out" | jq -r '.commentUrl // empty')"
complete_review_record_wait bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb:initial-review:0 "$review_moved_initial_url"
write_review_draft acceptance accepted 0 "$review_moved_initial_url" "$review_moved_initial_url" bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb critic-tern-43
review_moved_acceptance_out="$("$ENGINE" review record FS.GG.SDD#42 "$review_draft" --pr 42 --json 2>&1)"; review_moved_acceptance_rc=$?
review_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
if [ "$review_initial_rc" -eq 0 ] && [ "$review_confirmation_rc" -eq 0 ] && [ "$review_acceptance_rc" -eq 0 ] \
   && [ "$review_unwaited_rc" -ne 0 ] && [ "$review_unwaited_before" = "$review_unwaited_after" ] \
   && [ "$review_wrong_rc" -ne 0 ] && [ "$review_wrong_before" = "$review_wrong_after" ] \
   && [ "$review_moved_initial_rc" -eq 0 ] && [ "$review_moved_acceptance_rc" -eq 0 ] \
   && printf '%s' "$review_initial_out" | jq -e '.revision == 1 and (.digest | length) == 64' >/dev/null \
   && printf '%s' "$review_confirmation_out" | jq -e '.revision == 2 and (.digest | length) == 64' >/dev/null \
   && printf '%s' "$review_acceptance_out" | jq -e '.revision == 3 and .effectiveChainValidated == true and (.digest | length) == 64' >/dev/null \
   && [[ "$review_acceptance_body" == *'"baseSha":"cccccccccccccccccccccccccccccccccccccccc"'* ]] \
   && [[ "$review_acceptance_body" != *'"baseSha":"9999999999999999999999999999999999999999"'* ]] \
   && printf '%s' "$review_moved_initial_out" | jq -e '.revision == 4 and (.digest | length) == 64' >/dev/null \
   && printf '%s' "$review_moved_acceptance_out" | jq -e '.revision == 5 and .effectiveChainValidated == true and (.digest | length) == 64' >/dev/null \
   && [ "$review_after" -eq $((review_before + 16)) ]; then
  ok "M4 review record validates actual backlinks and retires an accepted generation after head movement"
else
  bad "M4 review record must append parseable v2 generations with actual backlinks" "comments=$review_before->$review_after wrong=$review_wrong_rc:$review_wrong_before->$review_wrong_after initial=$review_initial_rc:$review_initial_out confirmation=$review_confirmation_rc:$review_confirmation_out acceptance=$review_acceptance_rc:$review_acceptance_out moved-initial=$review_moved_initial_rc:$review_moved_initial_out moved-acceptance=$review_moved_acceptance_rc:$review_moved_acceptance_out"
fi
rm -f "$review_wait_for_record"

# .github#2756: queue entry and terminal transition are durable PR writes, fenced to the current
# claim generation. Drive the compiled command, not the codec in isolation.
review_wait_draft="$(mktemp)"
python3 - "$review_wait_draft" "$review_claim_id" <<'PY'
import json, sys
from datetime import datetime, timedelta, timezone
path, claim = sys.argv[1:]
entered = datetime.now(timezone.utc).replace(microsecond=0)
expires = entered + timedelta(hours=4)
with open(path, "w", encoding="utf-8") as stream:
    json.dump({"schema":"fsgg.coord.review-wait/v1","event":"enter","item":"FS-GG/FS.GG.SDD#42",
               "claimGeneration":claim,"reviewGeneration":"fixture-generation-1","kind":"repair-confirmation",
               "enteredAt":entered.isoformat().replace("+00:00", "Z"),
               "expiresAt":expires.isoformat().replace("+00:00", "Z"),
               "evidenceRef":"https://fixture.invalid/review/1"}, stream, separators=(",", ":"))
PY
wait_entered_at="$(jq -r .enteredAt "$review_wait_draft")"
wait_expires_at="$(jq -r .expiresAt "$review_wait_draft")"
wait_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
wait_enter_out="$("$ENGINE" review wait FS.GG.SDD#42 "$review_wait_draft" --pr 42 --json 2>&1)"; wait_enter_rc=$?
"$ENGINE" review wait FS.GG.SDD#42 "$review_wait_draft" --pr 42 --json >/dev/null 2>&1; wait_duplicate_rc=$?
wait_duplicate_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
python3 - "$review_wait_draft" "$review_claim_id" "$wait_entered_at" "$wait_expires_at" <<'PY'
import json, sys
with open(sys.argv[1], "w", encoding="utf-8") as stream:
    json.dump({"schema":"fsgg.coord.review-wait/v1","event":"enter","item":"FS-GG/FS.GG.SDD#42",
               "claimGeneration":sys.argv[2],"reviewGeneration":"fixture-generation-2","kind":"repair-confirmation",
               "enteredAt":sys.argv[3],"expiresAt":sys.argv[4],
               "evidenceRef":"https://fixture.invalid/review/2"}, stream, separators=(",", ":"))
PY
"$ENGINE" review wait FS.GG.SDD#42 "$review_wait_draft" --pr 42 --json >/dev/null 2>&1; wait_parallel_generation_rc=$?
wait_parallel_generation_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
python3 - "$review_wait_draft" "$wait_expires_at" <<'PY'
import json, sys
from datetime import datetime, timedelta
expires = datetime.fromisoformat(sys.argv[2].replace("Z", "+00:00"))
with open(sys.argv[1], "w", encoding="utf-8") as stream:
    json.dump({"schema":"fsgg.coord.review-wait/v1","event":"timeout",
               "reviewGeneration":"fixture-generation-1",
               "at":(expires - timedelta(seconds=1)).isoformat().replace("+00:00", "Z"),
               "evidenceRef":"timer"}, stream, separators=(",", ":"))
PY
"$ENGINE" review wait FS.GG.SDD#42 "$review_wait_draft" --pr 42 --json >/dev/null 2>&1; wait_early_timeout_rc=$?
wait_early_timeout_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
python3 - "$review_wait_draft" "$wait_entered_at" <<'PY'
import json, sys
with open(sys.argv[1], "w", encoding="utf-8") as stream:
    json.dump({"schema":"fsgg.coord.review-wait/v1","event":"complete",
               "reviewGeneration":"fixture-generation-1","at":sys.argv[2],
               "evidenceRef":"https://fixture.invalid/review/pass"}, stream, separators=(",", ":"))
PY
wait_complete_out="$("$ENGINE" review wait FS.GG.SDD#42 "$review_wait_draft" --pr 42 --json 2>&1)"; wait_complete_rc=$?
"$ENGINE" review wait FS.GG.SDD#42 "$review_wait_draft" --pr 42 --json >/dev/null 2>&1; wait_terminal_duplicate_rc=$?
wait_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
wait_projected_out="$("$ENGINE" review FS.GG.SDD#42 --pr 42 --worker vole-418 --json 2>&1)"; wait_projected_rc=$?
if [ "$wait_enter_rc" -eq 0 ] && [ "$wait_duplicate_rc" -ne 0 ] \
   && [ "$wait_duplicate_after" -eq $((wait_before + 1)) ] && [ "$wait_complete_rc" -eq 0 ] \
   && [ "$wait_parallel_generation_rc" -ne 0 ] && [ "$wait_parallel_generation_after" -eq $((wait_before + 1)) ] \
   && [ "$wait_early_timeout_rc" -ne 0 ] && [ "$wait_early_timeout_after" -eq $((wait_before + 1)) ] \
   && [ "$wait_terminal_duplicate_rc" -ne 0 ] && [ "$wait_after" -eq $((wait_before + 2)) ] \
   && [ "$wait_projected_rc" -eq 0 ] && printf '%s' "$wait_projected_out" | jq -e '.waitStatus == "completed" and .waitReceipt.reviewGeneration == "fixture-generation-1"' >/dev/null \
   && printf '%s' "$wait_enter_out" | jq -e '.schema == "fsgg.coord.review-wait-result/v1"' >/dev/null \
   && printf '%s' "$wait_complete_out" | jq -e '.schema == "fsgg.coord.review-wait-result/v1"' >/dev/null; then
  ok "#2756 review wait writer persists one fenced entry, refuses duplicate entry/terminal writes, and projects its consumed state"
else
  bad "#2756 review wait writer must be durable and generation-fenced" "entry=$wait_enter_rc:$wait_enter_out duplicate=$wait_duplicate_rc:$wait_before->$wait_duplicate_after early-timeout=$wait_early_timeout_rc:$wait_early_timeout_after complete=$wait_complete_rc:$wait_complete_out terminal-duplicate=$wait_terminal_duplicate_rc projected=$wait_projected_rc:$wait_projected_out after=$wait_after"
fi

# A terminal transition is fenced to the ENTRY's claim generation, not merely to whichever worker
# happens to hold the item when it calls. Replace the claim after a durable entry and prove the old
# receipt cannot be consumed by the new generation.
python3 - "$review_wait_draft" "$review_claim_id" <<'PY'
import json, sys
from datetime import datetime, timedelta, timezone
entered = datetime.now(timezone.utc).replace(microsecond=0)
with open(sys.argv[1], "w", encoding="utf-8") as stream:
    json.dump({"schema":"fsgg.coord.review-wait/v1","event":"enter","item":"FS-GG/FS.GG.SDD#42",
               "claimGeneration":sys.argv[2],"reviewGeneration":"replacement-fence","kind":"repair-confirmation",
               "enteredAt":entered.isoformat().replace("+00:00", "Z"),
               "expiresAt":(entered + timedelta(hours=4)).isoformat().replace("+00:00", "Z"),
               "evidenceRef":"https://fixture.invalid/review/replacement"}, stream, separators=(",", ":"))
PY
"$ENGINE" review wait FS.GG.SDD#42 "$review_wait_draft" --pr 42 --json >/dev/null 2>&1; wait_replacement_enter_rc=$?
curl -fsS -X DELETE "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/comments/$review_claim_id" >/dev/null
review_claim_id="$(curl -fsS -X POST \
  -H 'Content-Type: application/json' \
  -d '{"body":"<!-- fsgg:claim worker=fixture-review-replacement lease=120 -->\nheld"}' \
  "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq -r '.id')"
python3 - "$review_wait_draft" <<'PY'
import json, sys
from datetime import datetime, timezone
with open(sys.argv[1], "w", encoding="utf-8") as stream:
    json.dump({"schema":"fsgg.coord.review-wait/v1","event":"complete","reviewGeneration":"replacement-fence",
               "at":datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
               "evidenceRef":"https://fixture.invalid/review/replaced-pass"}, stream, separators=(",", ":"))
PY
wait_replacement_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
"$ENGINE" review wait FS.GG.SDD#42 "$review_wait_draft" --pr 42 --json >/dev/null 2>&1; wait_replacement_complete_rc=$?
wait_replacement_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
wait_replacement_projection="$("$ENGINE" review FS.GG.SDD#42 --pr 42 --worker vole-418 --json 2>&1)"; wait_replacement_projection_rc=$?
if [ "$wait_replacement_enter_rc" -eq 0 ] && [ "$wait_replacement_complete_rc" -ne 0 ] \
   && [ "$wait_replacement_before" = "$wait_replacement_after" ] \
   && [ "$wait_replacement_projection_rc" -ne 0 ] \
   && printf '%s' "$wait_replacement_projection" | jq -e '.verdict == "noVerdict" and .waitStatus == "recoverable" and (.waitReceipt.claimGeneration != null)' >/dev/null; then
  ok "#2756 a replacement claim cannot consume the preceding generation's wait receipt"
else
  bad "#2756 terminal writes must be fenced to the entry claim generation" "entry=$wait_replacement_enter_rc complete=$wait_replacement_complete_rc comments=$wait_replacement_before->$wait_replacement_after projection=$wait_replacement_projection_rc:$wait_replacement_projection"
fi
rm -f "$review_wait_draft"

# .github#2797: the ordinary round-three wait is durable evidence even after its owning claim is
# released. Reproduce PR #2818's live sequence on an isolated fixture PR: initial + confirmations 1/2
# are changes-required, confirmation 3 is an immutable pass, its exact wait is completed, the required
# check settles red, the old claim is deleted, legacy exhaustion is recorded, and a fresh claim becomes
# current. Only one structured escalation may cross that boundary.
turnover_draft="$(mktemp)"
turnover_wait="$(mktemp)"
turnover_comment_ids=()
# .github#2807: real repair rounds review new commits. Keep every record exact-head-bound while
# making the history advance; only round three equals the fixture PR's live terminal head.
turnover_heads=(
  "1111111111111111111111111111111111111111"
  "2222222222222222222222222222222222222222"
  "3333333333333333333333333333333333333333"
  "dddddddddddddddddddddddddddddddddddddddd"
)
turnover_head="${turnover_heads[3]}"
turnover_critic="critic-turnover-2797"
turnover_old_claim_id="$(curl -fsS -X POST -H 'Content-Type: application/json' \
  -d '{"body":"<!-- fsgg:claim worker=fixture-turnover-old lease=120 -->\nheld"}' \
  "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq -r '.id')"
turnover_comment_ids+=("$turnover_old_claim_id")

write_turnover_wait() {
  local event="$1" generation="$2" evidence="$3" claim="${4:-$turnover_old_claim_id}"
  python3 - "$turnover_wait" "$event" "$generation" "$evidence" "$claim" <<'PY'
import json, sys
from datetime import datetime, timedelta, timezone
path, event, generation, evidence, claim = sys.argv[1:]
now = datetime.now(timezone.utc).replace(microsecond=0)
if event == "enter":
    value = {"schema":"fsgg.coord.review-wait/v1","event":"enter","item":"FS-GG/FS.GG.SDD#43",
             "claimGeneration":claim,"reviewGeneration":generation,"kind":
             "initial-review" if generation.endswith(":initial-review:0") else "repair-confirmation",
             "enteredAt":now.isoformat().replace("+00:00", "Z"),
             "expiresAt":(now + timedelta(hours=4)).isoformat().replace("+00:00", "Z"),
             "evidenceRef":"https://fixture.invalid/2797/dispatch"}
else:
    value = {"schema":"fsgg.coord.review-wait/v1","event":"complete","reviewGeneration":generation,
             "at":now.isoformat().replace("+00:00", "Z"),"evidenceRef":evidence}
with open(path, "w", encoding="utf-8") as stream:
    json.dump(value, stream, separators=(",", ":"))
PY
}

write_turnover_draft() {
  local kind="$1" round="$2" previous="$3" initial="$4" preceding="$5" head="${6:-$turnover_head}" subject="${7:-FS-GG/FS.GG.SDD#43/pr/43}" verdict="${8:-changes-required}"
  python3 - "$turnover_draft" "$kind" "$round" "$previous" "$initial" "$preceding" "$head" "$subject" "$turnover_critic" "$verdict" <<'PY'
import json, sys
path, kind, round_number, previous, initial, preceding, head, subject, critic, verdict = sys.argv[1:]
record = {
    "schema":"fsgg.coord.review-decision/v2", "subject":subject, "revision":0,
    "previousDigest":previous or None, "headSha":head, "claimGeneration":None, "baseSha":None,
    "critic":critic, "verdict":verdict, "acceptedExceptions":[],
    "routeApplicability":"not-meaningful", "routeEvidence":["claim-turnover writer fixture"],
    "policyVersion":"structured-decisions/1", "kind":kind, "round":int(round_number),
    "initialReview":initial or None, "precedingReview":preceding or None,
    "diffAuditRequired":False, "diffAuditReceipts":[], "succession":None,
    "timestamp":"2026-08-21T07:00:00Z", "digest":""
}
with open(path, "w", encoding="utf-8") as stream:
    json.dump(record, stream, separators=(",", ":"))
PY
}

# Rewrite one already-sealed fixture record while preserving its digest contract. This lets the
# production writer see a readable but semantically noncontiguous historical chain, rather than a
# trivially unreadable JSON/digest mutant that would exercise the wrong refusal.
rewrite_turnover_record() {
  local body="$1" field="$2" value="$3"
  python3 - "$body" "$field" "$value" <<'PY'
import hashlib, json, sys
body, field, raw_value = sys.argv[1:]
marker, payload = body.split("\n", 1)
record = json.loads(payload)
record[field] = int(raw_value) if field in {"revision", "round"} else raw_value
def frame(value):
    value = value or ""
    return f"{len(value.encode('utf-8'))}:{value}"
def strings(values):
    return "".join(frame(value) for value in values)
fields = [
    frame(record["schema"]), frame(record["subject"]), str(record["revision"]),
    frame(record.get("previousDigest")), frame(record["headSha"]), frame(record["critic"]),
    frame(record["verdict"]), strings(record["acceptedExceptions"]),
    frame(record["routeApplicability"]), strings(record["routeEvidence"]),
    frame(record["policyVersion"]), frame(record["kind"]), str(record["round"]),
    frame(record.get("initialReview")), frame(record.get("precedingReview")),
    str(record["diffAuditRequired"]), strings(record["diffAuditReceipts"]),
    frame(record["timestamp"]),
]
succession = record.get("succession")
if succession is not None:
    fields.extend([frame(succession["originalCritic"]), frame(succession["grantedBy"]), frame(succession["grantUrl"])])
if record.get("claimGeneration") is not None or record.get("baseSha") is not None:
    fields.extend([frame(record.get("claimGeneration")), frame(record.get("baseSha"))])
record["digest"] = hashlib.sha256("|".join(fields).encode()).hexdigest()
print(marker + "\n" + json.dumps(record, separators=(",", ":"), sort_keys=True))
PY
}

patch_turnover_comment() {
  local id="$1" body="$2"
  curl -fsS -X PATCH -H 'Content-Type: application/json' \
    -d "$(jq -n --arg body "$body" '{body:$body}')" \
    "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/comments/$id" >/dev/null
}

# Prepend one structurally accepted generation at a moved-off head. The live generation below must
# remain the only input to ordinary-exhaustion classification even though ledger validation consumes
# the complete append-only history.
turnover_retired_head="eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee"
write_turnover_draft initial 0 "" "" "" "$turnover_retired_head" "FS-GG/FS.GG.SDD#43/pr/43" pass
turnover_retired_initial_unsealed="<!-- fsgg:review-decision/v2 -->
$(<"$turnover_draft")"
turnover_retired_initial_body="$(rewrite_turnover_record "$turnover_retired_initial_unsealed" revision 1)"
turnover_retired_initial_digest="$(printf '%s' "$turnover_retired_initial_body" | sed '1d' | jq -r '.digest')"
turnover_retired_initial_id="$(curl -fsS -X POST -H 'Content-Type: application/json' \
  -d "$(jq -n --arg body "$turnover_retired_initial_body" '{body:$body}')" \
  "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq -r '.id')"
turnover_comment_ids+=("$turnover_retired_initial_id")
turnover_retired_initial_url="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" \
  | jq -r --argjson id "$turnover_retired_initial_id" '.[] | select(.id == $id) | .html_url')"
turnover_retired_acceptance_body="$(rewrite_turnover_record "$turnover_retired_initial_body" kind acceptance)"
turnover_retired_acceptance_body="$(rewrite_turnover_record "$turnover_retired_acceptance_body" revision 2)"
turnover_retired_acceptance_body="$(rewrite_turnover_record "$turnover_retired_acceptance_body" previousDigest "$turnover_retired_initial_digest")"
turnover_retired_acceptance_body="$(rewrite_turnover_record "$turnover_retired_acceptance_body" initialReview "$turnover_retired_initial_url")"
turnover_retired_acceptance_body="$(rewrite_turnover_record "$turnover_retired_acceptance_body" precedingReview "$turnover_retired_initial_url")"
turnover_retired_acceptance_body="$(rewrite_turnover_record "$turnover_retired_acceptance_body" verdict accepted)"
turnover_retired_acceptance_id="$(curl -fsS -X POST -H 'Content-Type: application/json' \
  -d "$(jq -n --arg body "$turnover_retired_acceptance_body" '{body:$body}')" \
  "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq -r '.id')"
turnover_comment_ids+=("$turnover_retired_acceptance_id")

turnover_initial_url=""; turnover_preceding_url=""; turnover_previous_digest=""
turnover_initial_id=""; turnover_initial_body=""; turnover_round_one_id=""; turnover_round_one_body=""
turnover_round_two_id=""; turnover_round_two_body=""; turnover_round_three_id=""; turnover_round_three_body=""
for turnover_round in 0 1 2 3; do
  turnover_round_head="${turnover_heads[$turnover_round]}"
  if [ "$turnover_round" -eq 0 ]; then
    turnover_kind="initial"; turnover_generation="$turnover_round_head:initial-review:0"
  else
    turnover_kind="confirmation"; turnover_generation="$turnover_round_head:repair-confirmation:$turnover_round"
  fi
  turnover_verdict="changes-required"
  [ "$turnover_round" -eq 3 ] && turnover_verdict="pass"
  write_turnover_wait enter "$turnover_generation" ""
  turnover_wait_out="$("$ENGINE" review wait FS.GG.SDD#43 "$turnover_wait" --pr 43 --json 2>&1)"; turnover_wait_rc=$?
  turnover_wait_id="$(printf '%s' "$turnover_wait_out" | jq -r '.commentId // empty')"
  [ -n "$turnover_wait_id" ] && turnover_comment_ids+=("$turnover_wait_id")
  write_turnover_draft "$turnover_kind" "$turnover_round" "$turnover_previous_digest" "$turnover_initial_url" "$turnover_preceding_url" "$turnover_round_head" "FS-GG/FS.GG.SDD#43/pr/43" "$turnover_verdict"
  turnover_record_out="$("$ENGINE" review record FS.GG.SDD#43 "$turnover_draft" --pr 43 --json 2>&1)"; turnover_record_rc=$?
  turnover_record_id="$(printf '%s' "$turnover_record_out" | jq -r '.commentId // empty')"
  turnover_record_url="$(printf '%s' "$turnover_record_out" | jq -r '.commentUrl // empty')"
  turnover_previous_digest="$(printf '%s' "$turnover_record_out" | jq -r '.digest // empty')"
  [ -n "$turnover_record_id" ] && turnover_comment_ids+=("$turnover_record_id")
  if [ "$turnover_round" -eq 0 ]; then
    turnover_initial_id="$turnover_record_id"
    turnover_initial_body="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq -r --argjson id "$turnover_record_id" '.[] | select(.id == $id) | .body')"
  elif [ "$turnover_round" -eq 1 ]; then
    turnover_round_one_id="$turnover_record_id"
    turnover_round_one_body="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq -r --argjson id "$turnover_record_id" '.[] | select(.id == $id) | .body')"
  elif [ "$turnover_round" -eq 2 ]; then
    turnover_round_two_id="$turnover_record_id"
    turnover_round_two_body="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq -r --argjson id "$turnover_record_id" '.[] | select(.id == $id) | .body')"
  elif [ "$turnover_round" -eq 3 ]; then
    turnover_round_three_id="$turnover_record_id"
    turnover_round_three_body="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq -r --argjson id "$turnover_record_id" '.[] | select(.id == $id) | .body')"
  fi
  [ "$turnover_round" -eq 0 ] && turnover_initial_url="$turnover_record_url"
  turnover_preceding_url="$turnover_record_url"
  write_turnover_wait complete "$turnover_generation" "$turnover_record_url"
  turnover_complete_out="$("$ENGINE" review wait FS.GG.SDD#43 "$turnover_wait" --pr 43 --json 2>&1)"; turnover_complete_rc=$?
  turnover_complete_id="$(printf '%s' "$turnover_complete_out" | jq -r '.commentId // empty')"
  [ -n "$turnover_complete_id" ] && turnover_comment_ids+=("$turnover_complete_id")
  if [ "$turnover_wait_rc" -ne 0 ] || [ "$turnover_record_rc" -ne 0 ] || [ "$turnover_complete_rc" -ne 0 ]; then
    bad ".github#2797: fixture must establish the exhausted ordinary chain" "round=$turnover_round wait=$turnover_wait_rc:$turnover_wait_out record=$turnover_record_rc:$turnover_record_out complete=$turnover_complete_rc:$turnover_complete_out"
  fi
done

curl -fsS -X DELETE "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/comments/$turnover_old_claim_id" >/dev/null
turnover_early_claim_id="$(curl -fsS -X POST -H 'Content-Type: application/json' \
  -d '{"body":"<!-- fsgg:claim worker=fixture-turnover-early lease=120 -->\nheld"}' \
  "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq -r '.id')"
turnover_comment_ids+=("$turnover_early_claim_id")
write_turnover_draft escalation 3 "$turnover_previous_digest" "$turnover_initial_url" "$turnover_preceding_url"
turnover_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq length)"
"$ENGINE" review record FS.GG.SDD#43 "$turnover_draft" --pr 43 --json >/dev/null 2>&1; turnover_missing_legacy_rc=$?
turnover_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq length)"

# Recover the actual confirmation URLs instead of depending on comment-id spacing: structured records
# are ordered and their URLs are the legacy marker's exact backlinks.
turnover_confirmation_urls="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" \
  | jq -r '[.[] | select(.body | startswith("<!-- fsgg:review-decision/v2 -->")) | .html_url] | .[-3:] | @tsv')"
IFS=$'\t' read -r turnover_confirmation_1 turnover_confirmation_2 turnover_confirmation_3 <<<"$turnover_confirmation_urls"
turnover_legacy_body="$(jq -n -r --arg h "$turnover_head" --arg i "$turnover_initial_url" --arg c1 "$turnover_confirmation_1" --arg c2 "$turnover_confirmation_2" --arg c3 "$turnover_confirmation_3" --arg critic "$turnover_critic" '
  "<!-- fsgg:independent-review-escalation:v1 -->\nexhausted-head: \($h)\ninitial-review: \($i)\nconfirmation-1: \($c1)\nconfirmation-2: \($c2)\nconfirmation-3: \($c3)\ncritic: \($critic)\nverdict: ordinary-chain-exhausted\n\nFixture exhaustion evidence."')"
turnover_legacy_id="$(curl -fsS -X POST -H 'Content-Type: application/json' \
  -d "$(jq -n --arg body "$turnover_legacy_body" '{body:$body}')" \
  "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq -r '.id')"
turnover_comment_ids+=("$turnover_legacy_id")
turnover_nonfresh_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq length)"
"$ENGINE" review record FS.GG.SDD#43 "$turnover_draft" --pr 43 --json >/dev/null 2>&1; turnover_nonfresh_rc=$?
turnover_nonfresh_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq length)"

curl -fsS -X DELETE "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/comments/$turnover_early_claim_id" >/dev/null
turnover_fresh_claim_id="$(curl -fsS -X POST -H 'Content-Type: application/json' \
  -d '{"body":"<!-- fsgg:claim worker=fixture-turnover-fresh lease=120 -->\nheld"}' \
  "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq -r '.id')"
turnover_comment_ids+=("$turnover_fresh_claim_id")

# A second terminal for the exhausted generation is a malformed chain even when the first completion
# remains the projection winner. Inject it at the wire boundary, prove zero-write refusal, then remove
# only the injected mutation so the exact valid history can proceed.
write_turnover_wait complete "$turnover_head:repair-confirmation:3" "https://fixture.invalid/2797/duplicate-terminal"
turnover_malformed_body="$(jq -n -r --rawfile event "$turnover_wait" '"<!-- fsgg:review-wait/v1 -->\n" + $event')"
turnover_malformed_id="$(curl -fsS -X POST -H 'Content-Type: application/json' \
  -d "$(jq -n --arg body "$turnover_malformed_body" '{body:$body}')" \
  "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq -r '.id')"
turnover_comment_ids+=("$turnover_malformed_id")
turnover_malformed_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq length)"
"$ENGINE" review record FS.GG.SDD#43 "$turnover_draft" --pr 43 --json >/dev/null 2>&1; turnover_malformed_rc=$?
turnover_malformed_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq length)"
curl -fsS -X DELETE "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/comments/$turnover_malformed_id" >/dev/null

# `.github#2807`: keep the history readable and digest-valid while making round two noncontiguous.
# The round-three digest is re-sealed against its mutated predecessor, so refusal demonstrates the
# ordered round rule rather than merely tripping JSON or digest parsing. Restore exact bytes afterward.
turnover_noncontiguous_two="$(rewrite_turnover_record "$turnover_round_two_body" round 4)"
turnover_noncontiguous_two_digest="$(printf '%s' "$turnover_noncontiguous_two" | sed '1d' | jq -r '.digest')"
turnover_noncontiguous_three="$(rewrite_turnover_record "$turnover_round_three_body" previousDigest "$turnover_noncontiguous_two_digest")"
turnover_noncontiguous_three_digest="$(printf '%s' "$turnover_noncontiguous_three" | sed '1d' | jq -r '.digest')"
patch_turnover_comment "$turnover_round_two_id" "$turnover_noncontiguous_two"
patch_turnover_comment "$turnover_round_three_id" "$turnover_noncontiguous_three"
write_turnover_draft escalation 3 "$turnover_noncontiguous_three_digest" "$turnover_initial_url" "$turnover_preceding_url"
turnover_noncontiguous_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq length)"
turnover_noncontiguous_out="$("$ENGINE" review record FS.GG.SDD#43 "$turnover_draft" --pr 43 --json 2>&1)"; turnover_noncontiguous_rc=$?
turnover_noncontiguous_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq length)"
patch_turnover_comment "$turnover_round_two_id" "$turnover_round_two_body"
patch_turnover_comment "$turnover_round_three_id" "$turnover_round_three_body"

# A legacy escalation whose confirmation backlink no longer names the exact structured round is not
# exhaustion authority. Mutate only that backlink, prove zero-write refusal, then restore exact bytes.
turnover_bad_backlink_body="${turnover_legacy_body/confirmation-2: $turnover_confirmation_2/confirmation-2: https:\/\/fixture.invalid\/2807\/wrong-confirmation-2}"
patch_turnover_comment "$turnover_legacy_id" "$turnover_bad_backlink_body"
write_turnover_draft escalation 3 "$turnover_previous_digest" "$turnover_initial_url" "$turnover_preceding_url"
turnover_bad_backlink_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq length)"
turnover_bad_backlink_out="$("$ENGINE" review record FS.GG.SDD#43 "$turnover_draft" --pr 43 --json 2>&1)"; turnover_bad_backlink_rc=$?
turnover_bad_backlink_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq length)"
patch_turnover_comment "$turnover_legacy_id" "$turnover_legacy_body"

# A digest-valid pre-round pass used to split the two consumers: projection saw only the terminal
# round/check state and reported ordinary exhaustion, while the live escalation writer separately
# rejected the prefix. Re-seal the whole chain so both production routes must refuse for the shared
# prefix predicate, then restore the exact control bytes.
turnover_prepass_initial="$(rewrite_turnover_record "$turnover_initial_body" verdict pass)"
turnover_prepass_initial_digest="$(printf '%s' "$turnover_prepass_initial" | sed '1d' | jq -r '.digest')"
turnover_prepass_one="$(rewrite_turnover_record "$turnover_round_one_body" previousDigest "$turnover_prepass_initial_digest")"
turnover_prepass_one_digest="$(printf '%s' "$turnover_prepass_one" | sed '1d' | jq -r '.digest')"
turnover_prepass_two="$(rewrite_turnover_record "$turnover_round_two_body" previousDigest "$turnover_prepass_one_digest")"
turnover_prepass_two_digest="$(printf '%s' "$turnover_prepass_two" | sed '1d' | jq -r '.digest')"
turnover_prepass_three="$(rewrite_turnover_record "$turnover_round_three_body" previousDigest "$turnover_prepass_two_digest")"
turnover_prepass_three_digest="$(printf '%s' "$turnover_prepass_three" | sed '1d' | jq -r '.digest')"
patch_turnover_comment "$turnover_initial_id" "$turnover_prepass_initial"
patch_turnover_comment "$turnover_round_one_id" "$turnover_prepass_one"
patch_turnover_comment "$turnover_round_two_id" "$turnover_prepass_two"
patch_turnover_comment "$turnover_round_three_id" "$turnover_prepass_three"
turnover_prepass_projection="$($ENGINE review FS.GG.SDD#43 --pr 43 --worker vole-418 --json 2>&1)"; turnover_prepass_projection_rc=$?
write_turnover_draft escalation 3 "$turnover_prepass_three_digest" "$turnover_initial_url" "$turnover_preceding_url"
turnover_prepass_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq length)"
turnover_prepass_writer="$($ENGINE review record FS.GG.SDD#43 "$turnover_draft" --pr 43 --json 2>&1)"; turnover_prepass_writer_rc=$?
turnover_prepass_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq length)"
patch_turnover_comment "$turnover_initial_id" "$turnover_initial_body"
patch_turnover_comment "$turnover_round_one_id" "$turnover_round_one_body"
patch_turnover_comment "$turnover_round_two_id" "$turnover_round_two_body"
patch_turnover_comment "$turnover_round_three_id" "$turnover_round_three_body"

turnover_projection="$("$ENGINE" review FS.GG.SDD#43 --pr 43 --worker vole-418 --json 2>&1)"; turnover_projection_rc=$?

# Every identity/binding mutation is exercised before the valid escalation exists, so refusal
# cannot be attributed merely to the later duplicate fence.
turnover_mutation_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq length)"
write_turnover_draft escalation 3 "$turnover_previous_digest" "$turnover_initial_url" "$turnover_preceding_url" "$turnover_head" "FS-GG/FS.GG.SDD#42/pr/43"
"$ENGINE" review record FS.GG.SDD#43 "$turnover_draft" --pr 43 --json >/dev/null 2>&1; turnover_wrong_item_rc=$?
turnover_wrong_pr_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
write_turnover_draft escalation 3 "$turnover_previous_digest" "$turnover_initial_url" "$turnover_preceding_url" "$turnover_head" "FS-GG/FS.GG.SDD#43/pr/42"
"$ENGINE" review record FS.GG.SDD#43 "$turnover_draft" --pr 42 --json >/dev/null 2>&1; turnover_wrong_pr_rc=$?
turnover_wrong_pr_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
write_turnover_draft escalation 3 "$turnover_previous_digest" "$turnover_initial_url" "$turnover_preceding_url" "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
"$ENGINE" review record FS.GG.SDD#43 "$turnover_draft" --pr 43 --json >/dev/null 2>&1; turnover_wrong_head_rc=$?
write_turnover_draft escalation 2 "$turnover_previous_digest" "$turnover_initial_url" "$turnover_preceding_url"
"$ENGINE" review record FS.GG.SDD#43 "$turnover_draft" --pr 43 --json >/dev/null 2>&1; turnover_wrong_round_rc=$?
write_turnover_draft escalation 3 "$(printf '0%.0s' {1..64})" "$turnover_initial_url" "$turnover_preceding_url"
"$ENGINE" review record FS.GG.SDD#43 "$turnover_draft" --pr 43 --json >/dev/null 2>&1; turnover_wrong_digest_rc=$?
turnover_mutation_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq length)"

write_turnover_draft escalation 3 "$turnover_previous_digest" "$turnover_initial_url" "$turnover_preceding_url"
turnover_valid_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq length)"
turnover_valid_out="$("$ENGINE" review record FS.GG.SDD#43 "$turnover_draft" --pr 43 --json 2>&1)"; turnover_valid_rc=$?
turnover_valid_id="$(printf '%s' "$turnover_valid_out" | jq -r '.commentId // empty')"
turnover_valid_url="$(printf '%s' "$turnover_valid_out" | jq -r '.commentUrl // empty')"
turnover_valid_digest="$(printf '%s' "$turnover_valid_out" | jq -r '.digest // empty')"
[ -n "$turnover_valid_id" ] && turnover_comment_ids+=("$turnover_valid_id")
turnover_valid_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq length)"
turnover_repair_projection="$("$ENGINE" review FS.GG.SDD#43 --pr 43 --worker vole-418 --json 2>&1)"; turnover_repair_projection_rc=$?

# .github#2865: consume the exhausted predecessor on a genuinely fresh PR. The current claim must be
# newer than the structured escalation it crosses; the repair-phase record then carries all seven
# provenance fields durably, so live inspection reads the same typed receipt instead of substituting
# `RepairPhaseGranted=None`.
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/close-pr/43" >/dev/null
curl -fsS -X DELETE "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/comments/$turnover_fresh_claim_id" >/dev/null

# #3068 regression: production topology does not alias the item and exhausted PR numbers. Item #47
# cross-references exhausted PR #43 and current fresh PR #46. Establish only its fresh initial review,
# then prove live guidance excludes the current PR and discovers the predecessor escalation through the
# typed timeline/PR reads.
topology_claim_id="$(curl -fsS -X POST -H 'Content-Type: application/json' \
  -d '{"body":"<!-- fsgg:claim worker=fixture-topology-impl lease=120 -->\nheld"}' \
  "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/47/comments" | jq -r '.id')"
topology_head="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
write_turnover_wait enter "$topology_head:initial-review:0" "" "$topology_claim_id"
jq '.item="FS-GG/FS.GG.SDD#47"' "$turnover_wait" >"$turnover_wait.topology" && mv "$turnover_wait.topology" "$turnover_wait"
"$ENGINE" review wait FS.GG.SDD#47 "$turnover_wait" --pr 46 --worker fixture-topology-impl --json >/dev/null 2>&1; topology_wait_rc=$?
write_turnover_draft initial 0 "" "" "" "$topology_head" "FS-GG/FS.GG.SDD#47/pr/46" changes-required
topology_initial_out="$("$ENGINE" review record FS.GG.SDD#47 "$turnover_draft" --pr 46 --worker fixture-topology-impl --json 2>&1)"; topology_initial_rc=$?
topology_initial_url="$(printf '%s' "$topology_initial_out" | jq -r '.commentUrl // empty')"
write_turnover_wait complete "$topology_head:initial-review:0" "$topology_initial_url" "$topology_claim_id"
"$ENGINE" review wait FS.GG.SDD#47 "$turnover_wait" --pr 46 --worker fixture-topology-impl --json >/dev/null 2>&1; topology_complete_rc=$?
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/rest-reads" >/dev/null
topology_projection="$("$ENGINE" review FS.GG.SDD#47 --pr 46 --worker fixture-topology-impl --json 2>&1)"; topology_projection_rc=$?
topology_reads="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/rest-reads")"
topology_current_comment_reads="$(printf '%s' "$topology_reads" | jq '[.paths[] | select(. == "GET /repos/FS-GG/FS.GG.SDD/issues/46/comments")] | length')"
if [ "$topology_wait_rc" -eq 0 ] && [ "$topology_initial_rc" -eq 0 ] && [ "$topology_complete_rc" -eq 0 ] \
   && [ "$topology_projection_rc" -eq 0 ] && [ "$topology_current_comment_reads" -eq 1 ] \
   && printf '%s' "$topology_projection" | jq -e '.repairAssertionCommand | contains("review assert-repair repair-phase FS-GG/FS.GG.SDD#47")' >/dev/null; then
  ok ".github#3068: live oracle excludes current PR and resolves a separately numbered exhausted predecessor"
else
  bad ".github#3068: item/PR number aliasing must not be required for repair-purpose guidance" \
    "wait=$topology_wait_rc initial=$topology_initial_rc:$topology_initial_out complete=$topology_complete_rc projection=$topology_projection_rc:$topology_projection current-pr-comment-reads=$topology_current_comment_reads reads=$topology_reads"
fi
curl -fsS -X DELETE "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/comments/$topology_claim_id" >/dev/null
while IFS= read -r topology_comment_id; do
  curl -fsS -X DELETE "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/comments/$topology_comment_id" >/dev/null
done < <(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/46/comments" | jq -r '.[].id')

repair_claim_id="$(curl -fsS -X POST -H 'Content-Type: application/json' \
  -d '{"body":"<!-- fsgg:claim worker=fixture-repair-impl lease=120 -->\nheld"}' \
  "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq -r '.id')"
turnover_comment_ids+=("$repair_claim_id")
repair_head="ffffffffffffffffffffffffffffffffffffffff"
repair_subject="FS-GG/FS.GG.SDD#43/pr/44"

write_turnover_wait enter "$repair_head:initial-review:0" "" "$repair_claim_id"
"$ENGINE" review wait FS.GG.SDD#43 "$turnover_wait" --pr 44 --worker fixture-repair-impl --json >/dev/null 2>&1; repair_initial_wait_rc=$?
write_turnover_draft initial 0 "" "" "" "$repair_head" "$repair_subject" changes-required
repair_initial_out="$("$ENGINE" review record FS.GG.SDD#43 "$turnover_draft" --pr 44 --worker fixture-repair-impl --json 2>&1)"; repair_initial_rc=$?
repair_initial_url="$(printf '%s' "$repair_initial_out" | jq -r '.commentUrl // empty')"
repair_initial_digest="$(printf '%s' "$repair_initial_out" | jq -r '.digest // empty')"
write_turnover_wait complete "$repair_head:initial-review:0" "$repair_initial_url" "$repair_claim_id"
"$ENGINE" review wait FS.GG.SDD#43 "$turnover_wait" --pr 44 --worker fixture-repair-impl --json >/dev/null 2>&1; repair_initial_complete_rc=$?

# Follow the live oracle for the positive write. The actor supplies only its accountable reason and
# minted identity; the repair-phase purpose must be present in the command projected from durable
# predecessor escalation evidence.
repair_oracle_out="$("$ENGINE" review FS.GG.SDD#43 --pr 44 --worker fixture-repair-impl --json 2>&1)"; repair_oracle_rc=$?
repair_oracle_command="$(printf '%s' "$repair_oracle_out" | jq -r '.repairAssertionCommand // empty')"

# #3068: the unchanged repair head no longer needs a hand-authored wait event. The accountable writer
# derives head/grantor and seals one purpose-bound comment; live wait-enter then derives the special
# repair-phase generation zero. Exercise every identity/binding refusal before the valid append, so a
# later duplicate fence cannot be the reason they failed.
repair_assert_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/44/comments" | jq length)"
repair_assert_wait_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/44/comments" | jq '[.[] | select(.body | startswith("<!-- fsgg:review-wait/v1 -->"))] | length')"
repair_wrong_purpose_out="$("$ENGINE" review assert-repair FS.GG.SDD#43 "$repair_initial_url" 'wrong semantic route' --pr 44 --worker accountable-host-107 --json 2>&1)"; repair_wrong_purpose_rc=$?
repair_wrong_purpose_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/44/comments" | jq length)"
repair_wrong_purpose_wait_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/44/comments" | jq '[.[] | select(.body | startswith("<!-- fsgg:review-wait/v1 -->"))] | length')"
if [ "$repair_wrong_purpose_rc" -ne 0 ] \
   && [[ "$repair_wrong_purpose_out" == *"requires purpose=repair-phase-entry"* ]] \
   && [ "$repair_assert_before" = "$repair_wrong_purpose_after" ] \
   && [ "$repair_assert_wait_before" = "$repair_wrong_purpose_wait_after" ]; then
  ok "#3068 ordinary purpose is refused before assertion/wait append in repair-entry topology"
else
  bad "#3068 caller purpose must not select immutable repair-entry authority" "rc=$repair_wrong_purpose_rc:$repair_wrong_purpose_out comments=$repair_assert_before->$repair_wrong_purpose_after waits=$repair_assert_wait_before->$repair_wrong_purpose_wait_after"
fi
repair_self_out="$("$ENGINE" review assert-repair repair-phase FS.GG.SDD#43 "$repair_initial_url" 'comment repair is complete' --pr 44 --worker fixture-repair-impl --json 2>&1)"; repair_self_rc=$?
repair_critic_out="$("$ENGINE" review assert-repair repair-phase FS.GG.SDD#43 "$repair_initial_url" 'comment repair is complete' --pr 44 --worker "$turnover_critic" --json 2>&1)"; repair_critic_rc=$?
repair_wrong_review_out="$("$ENGINE" review assert-repair repair-phase FS.GG.SDD#43 https://fixture.invalid/wrong-review 'comment repair is complete' --pr 44 --worker accountable-host-107 --json 2>&1)"; repair_wrong_review_rc=$?
repair_assert_negative_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/44/comments" | jq length)"
repair_assert_wait_negative_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/44/comments" | jq '[.[] | select(.body | startswith("<!-- fsgg:review-wait/v1 -->"))] | length')"

repair_malformed_assertion_id="$(curl -fsS -X POST -H 'Content-Type: application/json' \
  -d '{"body":"<!-- fsgg:repair-assertion/v1 -->\n{}"}' \
  "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/44/comments" | jq -r '.id')"
repair_malformed_reader_out="$("$ENGINE" review FS.GG.SDD#43 --pr 44 --worker fixture-repair-impl --json 2>&1)"; repair_malformed_reader_rc=$?
curl -fsS -X DELETE "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/comments/$repair_malformed_assertion_id" >/dev/null

repair_literal_command="${repair_oracle_command/scripts\/fsgg-coord/\"$ENGINE\"}"
repair_literal_command="${repair_literal_command/'<accountable-reason>'/'comment-shaped-repair-confirmed-by-host'}"
repair_literal_command="${repair_literal_command/--json/--worker accountable-host-107 --json}"
repair_assert_out="$(eval "$repair_literal_command" 2>&1)"; repair_assert_rc=$?
repair_assert_id="$(printf '%s' "$repair_assert_out" | jq -r '.commentId // empty')"
repair_duplicate_out="$("$ENGINE" review assert-repair repair-phase FS.GG.SDD#43 "$repair_initial_url" 'duplicate' --pr 44 --worker another-host-107 --json 2>&1)"; repair_duplicate_rc=$?
repair_assert_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/44/comments" | jq length)"
repair_entry_wait_out="$("$ENGINE" review wait enter FS.GG.SDD#43 --pr 44 --worker fixture-repair-impl --json 2>&1)"; repair_entry_wait_rc=$?
repair_entry_wait_body="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/44/comments" \
  | jq -r '[.[] | select(.body | startswith("<!-- fsgg:review-wait/v1 -->"))] | last | .body')"
write_turnover_draft repair-phase 0 "$repair_initial_digest" "$repair_initial_url" "$repair_initial_url" "$repair_head" "$repair_subject" changes-required
jq --argjson exhausted 43 --argjson escalation "$turnover_valid_id" --arg claim "$repair_claim_id" --arg branch 'item/43-repair-phase' --arg implementer 'fixture-repair-impl' --arg critic "$turnover_critic" --arg head "$repair_head" \
  '.repairPhaseReceipt={exhaustedPr:$exhausted,escalationCommentId:$escalation,newClaimGeneration:$claim,newBranchOrPr:$branch,newImplementerIdentity:$implementer,newCriticIdentity:$critic,candidateHeadSha:$head}' \
  "$turnover_draft" >"$turnover_draft.receipt"
mv "$turnover_draft.receipt" "$turnover_draft"

repair_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/44/comments" | jq length)"
jq '.repairPhaseReceipt="malformed"' "$turnover_draft" >"$turnover_draft.bad"
repair_malformed_out="$("$ENGINE" review record FS.GG.SDD#43 "$turnover_draft.bad" --pr 44 --worker fixture-repair-impl --json 2>&1)"; repair_malformed_rc=$?
jq '.repairPhaseReceipt.newClaimGeneration="stale-claim"' "$turnover_draft" >"$turnover_draft.stale"
repair_stale_out="$("$ENGINE" review record FS.GG.SDD#43 "$turnover_draft.stale" --pr 44 --worker fixture-repair-impl --json 2>&1)"; repair_stale_rc=$?
jq 'del(.repairPhaseReceipt)' "$turnover_draft" >"$turnover_draft.missing"
repair_missing_out="$("$ENGINE" review record FS.GG.SDD#43 "$turnover_draft.missing" --pr 44 --worker fixture-repair-impl --json 2>&1)"; repair_missing_rc=$?
repair_negative_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/44/comments" | jq length)"
repair_entry_out="$("$ENGINE" review record FS.GG.SDD#43 "$turnover_draft" --pr 44 --worker fixture-repair-impl --json 2>&1)"; repair_entry_rc=$?
repair_entry_url="$(printf '%s' "$repair_entry_out" | jq -r '.commentUrl // empty')"
repair_entry_digest="$(printf '%s' "$repair_entry_out" | jq -r '.digest // empty')"
write_turnover_wait complete "$repair_head:repair-confirmation:0" "$repair_entry_url" "$repair_claim_id"
"$ENGINE" review wait FS.GG.SDD#43 "$turnover_wait" --pr 44 --worker fixture-repair-impl --json >/dev/null 2>&1; repair_entry_complete_rc=$?

write_turnover_wait enter "$repair_head:repair-confirmation:1" "" "$repair_claim_id"
"$ENGINE" review wait FS.GG.SDD#43 "$turnover_wait" --pr 44 --worker fixture-repair-impl --json >/dev/null 2>&1; repair_confirmation_wait_rc=$?
write_turnover_draft confirmation 1 "$repair_entry_digest" "$repair_initial_url" "$repair_entry_url" "$repair_head" "$repair_subject" pass
repair_confirmation_out="$("$ENGINE" review record FS.GG.SDD#43 "$turnover_draft" --pr 44 --worker fixture-repair-impl --json 2>&1)"; repair_confirmation_rc=$?
repair_confirmation_url="$(printf '%s' "$repair_confirmation_out" | jq -r '.commentUrl // empty')"
repair_confirmation_digest="$(printf '%s' "$repair_confirmation_out" | jq -r '.digest // empty')"
write_turnover_wait complete "$repair_head:repair-confirmation:1" "$repair_confirmation_url" "$repair_claim_id"
"$ENGINE" review wait FS.GG.SDD#43 "$turnover_wait" --pr 44 --worker fixture-repair-impl --json >/dev/null 2>&1; repair_confirmation_complete_rc=$?
write_turnover_draft acceptance 0 "$repair_confirmation_digest" "$repair_initial_url" "$repair_confirmation_url" "$repair_head" "$repair_subject" accepted
repair_acceptance_out="$("$ENGINE" review record FS.GG.SDD#43 "$turnover_draft" --pr 44 --worker fixture-repair-impl --json 2>&1)"; repair_acceptance_rc=$?
repair_projection="$("$ENGINE" review FS.GG.SDD#43 --pr 44 --worker fixture-repair-impl --json 2>&1)"; repair_projection_rc=$?

if [ "$repair_initial_wait_rc" -eq 0 ] && [ "$repair_initial_rc" -eq 0 ] && [ "$repair_initial_complete_rc" -eq 0 ] \
   && [ "$repair_oracle_rc" -eq 0 ] && [[ "$repair_oracle_command" == *"review assert-repair repair-phase"* ]] \
   && [ "$repair_wrong_purpose_rc" -ne 0 ] && [[ "$repair_wrong_purpose_out" == *"requires purpose=repair-phase-entry"* ]] \
   && [ "$repair_self_rc" -ne 0 ] && [ "$repair_critic_rc" -ne 0 ] && [ "$repair_wrong_review_rc" -ne 0 ] \
   && [ "$repair_assert_before" = "$repair_assert_negative_after" ] \
   && [ "$repair_assert_wait_before" = "$repair_assert_wait_negative_after" ] \
   && [ "$repair_malformed_reader_rc" -ne 0 ] && [[ "$repair_malformed_reader_out" == *"repair assertion authority is invalid"* ]] \
   && [ "$repair_assert_rc" -eq 0 ] && [ -n "$repair_assert_id" ] && [ "$repair_duplicate_rc" -ne 0 ] \
   && [ "$repair_assert_after" -eq $((repair_assert_negative_after + 1)) ] \
   && printf '%s' "$repair_assert_out" | jq -e '.schema == "fsgg.coord.repair-assertion-result/v1" and .grantedBy == "accountable-host-107" and (.nextCommand | contains("review wait enter"))' >/dev/null \
   && printf '%s' "$repair_entry_wait_body" | sed -n '2p' | jq -e --arg claim "$repair_claim_id" --arg head "$repair_head" \
      '.claimGeneration == $claim and .reviewGeneration == ($head + ":repair-confirmation:0") and .kind == "repair-confirmation"' >/dev/null \
   && [ "$repair_entry_wait_rc" -eq 0 ] && [ "$repair_malformed_rc" -ne 0 ] \
   && [[ "$repair_malformed_out" == *"repairPhaseReceipt"* ]] \
   && [ "$repair_stale_rc" -ne 0 ] && [[ "$repair_stale_out" == *"newClaimGeneration is not current"* ]] \
   && [ "$repair_missing_rc" -ne 0 ] && [[ "$repair_missing_out" == *"requires the seven-field repairPhaseReceipt"* ]] \
   && [ "$repair_before" = "$repair_negative_after" ] && [ "$repair_entry_rc" -eq 0 ] \
   && [ "$repair_entry_complete_rc" -eq 0 ] && [ "$repair_confirmation_wait_rc" -eq 0 ] \
   && [ "$repair_confirmation_rc" -eq 0 ] && [ "$repair_confirmation_complete_rc" -eq 0 ] \
   && [ "$repair_acceptance_rc" -eq 0 ] && [ "$repair_projection_rc" -eq 0 ] \
   && printf '%s' "$repair_projection" | jq -e '.verdict == "next" and .state == "accepted" and .acceptedReceipt.repairPhase == true' >/dev/null \
   && curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/44/comments" \
      | jq -e --argjson escalation "$turnover_valid_id" --arg claim "$repair_claim_id" \
        '[.[] | select(.body | startswith("<!-- fsgg:review-decision/v2 -->")) | (.body | split("\n")[1] | fromjson)] | any(.kind == "repair-phase" and .repairPhaseReceipt.escalationCommentId == $escalation and .repairPhaseReceipt.newClaimGeneration == $claim)' >/dev/null; then
  ok ".github#2865: exhausted escalation plus newer claim enters one typed repair-phase chain and acceptance reports repairPhase=true"
else
  bad ".github#2865: live repair-phase entry must produce and consume the seven-field typed receipt" \
    "initial=$repair_initial_wait_rc/$repair_initial_rc/$repair_initial_complete_rc oracle=$repair_oracle_rc:$repair_oracle_out command=$repair_oracle_command assertion=wrong-purpose:$repair_wrong_purpose_rc:$repair_wrong_purpose_out self:$repair_self_rc:$repair_self_out critic:$repair_critic_rc:$repair_critic_out wrong:$repair_wrong_review_rc:$repair_wrong_review_out malformed-reader:$repair_malformed_reader_rc:$repair_malformed_reader_out valid:$repair_assert_rc:$repair_assert_out duplicate:$repair_duplicate_rc:$repair_duplicate_out counts=$repair_assert_before/$repair_assert_negative_after/$repair_assert_after waits=$repair_assert_wait_before/$repair_assert_wait_negative_after entry=$repair_entry_wait_rc:$repair_entry_wait_out:$repair_entry_wait_body malformed=$repair_malformed_rc:$repair_malformed_out stale=$repair_stale_rc:$repair_stale_out missing=$repair_missing_rc:$repair_missing_out count=$repair_before->$repair_negative_after valid=$repair_entry_rc:$repair_entry_out complete=$repair_entry_complete_rc confirm=$repair_confirmation_wait_rc/$repair_confirmation_rc/$repair_confirmation_complete_rc acceptance=$repair_acceptance_rc:$repair_acceptance_out projection=$repair_projection_rc:$repair_projection"
fi
rm -f "$turnover_draft.bad" "$turnover_draft.stale" "$turnover_draft.missing"

turnover_duplicate_before="$turnover_valid_after"
"$ENGINE" review record FS.GG.SDD#43 "$turnover_draft" --pr 43 --json >/dev/null 2>&1; turnover_duplicate_rc=$?
turnover_duplicate_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq length)"
write_turnover_draft confirmation 4 "$turnover_previous_digest" "$turnover_initial_url" "$turnover_preceding_url"
"$ENGINE" review record FS.GG.SDD#43 "$turnover_draft" --pr 43 --json >/dev/null 2>&1; turnover_round4_rc=$?
turnover_round4_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq length)"
write_turnover_draft acceptance 0 "$turnover_valid_digest" "$turnover_initial_url" "$turnover_valid_url" "$turnover_head" "FS-GG/FS.GG.SDD#43/pr/43" accepted
"$ENGINE" review record FS.GG.SDD#43 "$turnover_draft" --pr 43 --json >/dev/null 2>&1; turnover_acceptance_rc=$?
turnover_acceptance_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq length)"

if [ "$turnover_missing_legacy_rc" -ne 0 ] && [ "$turnover_before" = "$turnover_after" ] \
   && [ "$turnover_nonfresh_rc" -ne 0 ] && [ "$turnover_nonfresh_before" = "$turnover_nonfresh_after" ] \
   && [ "$turnover_malformed_rc" -ne 0 ] && [ "$turnover_malformed_before" = "$turnover_malformed_after" ] \
   && [ "$turnover_noncontiguous_rc" -ne 0 ] && [ "$turnover_noncontiguous_before" = "$turnover_noncontiguous_after" ] \
   && [[ "$turnover_noncontiguous_out" == *"confirmation round must be contiguous within its generation"* ]] \
   && [ "$turnover_bad_backlink_rc" -ne 0 ] && [ "$turnover_bad_backlink_before" = "$turnover_bad_backlink_after" ] \
   && [[ "$turnover_bad_backlink_out" == *"legacy ordinary-exhaustion evidence is missing, duplicated, stale, or malformed"* ]] \
   && [ "$turnover_prepass_projection_rc" -eq 0 ] \
   && ! printf '%s' "$turnover_prepass_projection" | jq -e '.state == "ordinaryExhaustion" or .waitStatus == "ordinaryExhaustion"' >/dev/null \
   && [ "$turnover_prepass_writer_rc" -ne 0 ] && [ "$turnover_prepass_before" = "$turnover_prepass_after" ] \
   && [ "$turnover_projection_rc" -eq 0 ] \
   && printf '%s' "$turnover_projection" | jq -e '.verdict == "next" and .state == "ordinaryExhaustion" and .action == "park" and .waitStatus == "ordinaryExhaustion"' >/dev/null \
   && [ "$turnover_wrong_item_rc" -ne 0 ] && [ "$turnover_wrong_pr_rc" -ne 0 ] \
   && [ "$turnover_wrong_pr_before" = "$turnover_wrong_pr_after" ] && [ "$turnover_wrong_head_rc" -ne 0 ] \
   && [ "$turnover_wrong_round_rc" -ne 0 ] && [ "$turnover_wrong_digest_rc" -ne 0 ] \
   && [ "$turnover_mutation_before" = "$turnover_mutation_after" ] \
   && [ "$turnover_valid_rc" -eq 0 ] && [ "$turnover_valid_after" -eq $((turnover_valid_before + 1)) ] \
   && printf '%s' "$turnover_valid_out" | jq -e '.revision == 7 and (.digest | length) == 64' >/dev/null \
   && [ "$turnover_repair_projection_rc" -ne 0 ] \
   && printf '%s' "$turnover_repair_projection" | jq -e '.verdict == "noVerdict" and .waitStatus == "repairPhaseEntry" and (.reasons[0] | contains("instead of dispatching, resuming, accepting, or manufacturing ordinary round four"))' >/dev/null \
   && [ "$turnover_duplicate_rc" -ne 0 ] && [ "$turnover_duplicate_before" = "$turnover_duplicate_after" ] \
   && [ "$turnover_round4_rc" -ne 0 ] && [ "$turnover_round4_after" = "$turnover_duplicate_after" ] \
   && [ "$turnover_acceptance_rc" -ne 0 ] && [ "$turnover_acceptance_after" = "$turnover_duplicate_after" ]; then
  ok ".github#2819: one structured escalation crosses immutable round-three pass + settled-red claim turnover and every mutation refuses before write"
else
  bad ".github#2819: pass-red exhausted-claim turnover must authorize escalation only" \
    "missing=$turnover_missing_legacy_rc:$turnover_before->$turnover_after nonfresh=$turnover_nonfresh_rc:$turnover_nonfresh_before->$turnover_nonfresh_after malformed=$turnover_malformed_rc:$turnover_malformed_before->$turnover_malformed_after noncontiguous=$turnover_noncontiguous_rc:$turnover_noncontiguous_before->$turnover_noncontiguous_after:$turnover_noncontiguous_out backlink=$turnover_bad_backlink_rc:$turnover_bad_backlink_before->$turnover_bad_backlink_after:$turnover_bad_backlink_out prepass=projection:$turnover_prepass_projection_rc:$turnover_prepass_projection,writer:$turnover_prepass_writer_rc:$turnover_prepass_before->$turnover_prepass_after:$turnover_prepass_writer projection=$turnover_projection_rc:$turnover_projection mutations=item:$turnover_wrong_item_rc,pr:$turnover_wrong_pr_rc:$turnover_wrong_pr_before->$turnover_wrong_pr_after,head:$turnover_wrong_head_rc,round:$turnover_wrong_round_rc,digest:$turnover_wrong_digest_rc:$turnover_mutation_before->$turnover_mutation_after valid=$turnover_valid_rc:$turnover_valid_before->$turnover_valid_after:$turnover_valid_out repair=$turnover_repair_projection_rc:$turnover_repair_projection duplicate=$turnover_duplicate_rc:$turnover_duplicate_before->$turnover_duplicate_after round4=$turnover_round4_rc:$turnover_round4_after acceptance=$turnover_acceptance_rc:$turnover_acceptance_after"
fi

for turnover_id in "${turnover_comment_ids[@]}"; do
  curl -fsS -X DELETE "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/comments/$turnover_id" >/dev/null 2>&1 || true
done
rm -f "$turnover_draft" "$turnover_wait"

# .github#3014: the live engine admitted a contiguous confirmation round 4 after earlier pass/head
# movement, projected that exact five-record generation as ordinaryExhaustion, then its writer wedged:
# turnover destructured exactly initial+1/2/3 and could not author the typed escalation a repair-phase
# receipt requires. Reproduce that observed shape on a separate issue/PR. The historical three-round
# case above stays unchanged; this leg proves the compatible extended marker binds the ACTUAL terminal
# confirmation, refuses a wrong binding without a write, and feeds the unchanged seven-field entry gate.
extended_draft="$(mktemp)"
extended_wait="$(mktemp)"
extended_ids=()
extended_head="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
extended_old_worker="fixture-extended-old"
extended_turnover_worker="fixture-extended-turnover"
extended_repair_worker="fixture-extended-repair"
extended_critic="critic-extended-ordinary"
extended_old_claim_id="$(curl -fsS -X POST -H 'Content-Type: application/json' \
  -d '{"body":"<!-- fsgg:claim worker=fixture-extended-old lease=120 -->\nheld"}' \
  "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/45/comments" | jq -r '.id')"
extended_ids+=("45:$extended_old_claim_id")

write_extended_wait() {
  local event="$1" generation="$2" evidence="$3" claim="$4"
  python3 - "$extended_wait" "$event" "$generation" "$evidence" "$claim" <<'PY'
import json, sys
from datetime import datetime, timedelta, timezone
path, event, generation, evidence, claim = sys.argv[1:]
now = datetime.now(timezone.utc).replace(microsecond=0)
if event == "enter":
    value = {"schema":"fsgg.coord.review-wait/v1","event":"enter","item":"FS-GG/FS.GG.SDD#45",
             "claimGeneration":claim,"reviewGeneration":generation,
             "kind":"initial-review" if generation.endswith(":initial-review:0") else "repair-confirmation",
             "enteredAt":now.isoformat().replace("+00:00", "Z"),
             "expiresAt":(now + timedelta(hours=4)).isoformat().replace("+00:00", "Z"),
             "evidenceRef":"https://fixture.invalid/3014/dispatch"}
else:
    value = {"schema":"fsgg.coord.review-wait/v1","event":"complete","reviewGeneration":generation,
             "at":now.isoformat().replace("+00:00", "Z"),"evidenceRef":evidence}
with open(path, "w", encoding="utf-8") as stream:
    json.dump(value, stream, separators=(",", ":"))
PY
}

extended_initial_url=""; extended_preceding_url=""; extended_previous_digest=""
extended_confirmation_urls=()
turnover_critic="$extended_critic"
for extended_round in 0 1 2 3 4; do
  if [ "$extended_round" -eq 0 ]; then
    extended_kind="initial"; extended_generation="$extended_head:initial-review:0"
  else
    extended_kind="confirmation"; extended_generation="$extended_head:repair-confirmation:$extended_round"
  fi
  # Exact live shape: the first two successors passed; later exact-head reviews found more material work.
  extended_verdict="changes-required"
  [ "$extended_round" -eq 1 ] || [ "$extended_round" -eq 2 ] && extended_verdict="pass"
  write_extended_wait enter "$extended_generation" "" "$extended_old_claim_id"
  "$ENGINE" review wait FS.GG.SDD#45 "$extended_wait" --pr 45 --worker "$extended_old_worker" --json >/dev/null 2>&1; extended_wait_rc=$?
  write_turnover_draft "$extended_kind" "$extended_round" "$extended_previous_digest" "$extended_initial_url" "$extended_preceding_url" "$extended_head" "FS-GG/FS.GG.SDD#45/pr/45" "$extended_verdict"
  extended_record_out="$("$ENGINE" review record FS.GG.SDD#45 "$turnover_draft" --pr 45 --worker "$extended_old_worker" --json 2>&1)"; extended_record_rc=$?
  extended_record_id="$(printf '%s' "$extended_record_out" | jq -r '.commentId // empty')"
  extended_record_url="$(printf '%s' "$extended_record_out" | jq -r '.commentUrl // empty')"
  extended_previous_digest="$(printf '%s' "$extended_record_out" | jq -r '.digest // empty')"
  [ -n "$extended_record_id" ] && extended_ids+=("45:$extended_record_id")
  [ "$extended_round" -eq 0 ] && extended_initial_url="$extended_record_url"
  [ "$extended_round" -gt 0 ] && extended_confirmation_urls+=("$extended_record_url")
  extended_preceding_url="$extended_record_url"
  write_extended_wait complete "$extended_generation" "$extended_record_url" "$extended_old_claim_id"
  "$ENGINE" review wait FS.GG.SDD#45 "$extended_wait" --pr 45 --worker "$extended_old_worker" --json >/dev/null 2>&1; extended_complete_rc=$?
  if [ "$extended_wait_rc" -ne 0 ] || [ "$extended_record_rc" -ne 0 ] || [ "$extended_complete_rc" -ne 0 ]; then
    bad ".github#3014: fixture must establish the admitted five-record chain" "round=$extended_round wait=$extended_wait_rc record=$extended_record_rc:$extended_record_out complete=$extended_complete_rc"
  fi
done

curl -fsS -X DELETE "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/comments/$extended_old_claim_id" >/dev/null
extended_bad_terminal="https://fixture.invalid/3014/wrong-terminal"
extended_legacy_body="$(jq -n -r --arg h "$extended_head" --arg i "$extended_initial_url" \
  --arg c1 "${extended_confirmation_urls[0]}" --arg c2 "${extended_confirmation_urls[1]}" \
  --arg c3 "${extended_confirmation_urls[2]}" --arg terminal "$extended_bad_terminal" --arg critic "$extended_critic" '
  "<!-- fsgg:independent-review-escalation:v1 -->\nexhausted-head: \($h)\ninitial-review: \($i)\nconfirmation-1: \($c1)\nconfirmation-2: \($c2)\nconfirmation-3: \($c3)\nterminal-confirmation: \($terminal)\ncritic: \($critic)\nverdict: ordinary-chain-exhausted\n\nExtended turnover evidence."')"
extended_legacy_id="$(curl -fsS -X POST -H 'Content-Type: application/json' \
  -d "$(jq -n --arg body "$extended_legacy_body" '{body:$body}')" \
  "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/45/comments" | jq -r '.id')"
extended_ids+=("45:$extended_legacy_id")
extended_turnover_claim_id="$(curl -fsS -X POST -H 'Content-Type: application/json' \
  -d '{"body":"<!-- fsgg:claim worker=fixture-extended-turnover lease=120 -->\nheld"}' \
  "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/45/comments" | jq -r '.id')"
extended_ids+=("45:$extended_turnover_claim_id")
write_turnover_draft escalation 4 "$extended_previous_digest" "$extended_initial_url" "$extended_preceding_url" "$extended_head" "FS-GG/FS.GG.SDD#45/pr/45" changes-required
extended_wrong_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/45/comments" | jq length)"
extended_wrong_out="$("$ENGINE" review record FS.GG.SDD#45 "$turnover_draft" --pr 45 --worker "$extended_turnover_worker" --json 2>&1)"; extended_wrong_rc=$?
extended_wrong_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/45/comments" | jq length)"

extended_legacy_body="${extended_legacy_body/terminal-confirmation: $extended_bad_terminal/terminal-confirmation: ${extended_confirmation_urls[3]}}"
patch_turnover_comment "$extended_legacy_id" "$extended_legacy_body"
extended_escalation_out="$("$ENGINE" review record FS.GG.SDD#45 "$turnover_draft" --pr 45 --worker "$extended_turnover_worker" --json 2>&1)"; extended_escalation_rc=$?
extended_escalation_id="$(printf '%s' "$extended_escalation_out" | jq -r '.commentId // empty')"
[ -n "$extended_escalation_id" ] && extended_ids+=("45:$extended_escalation_id")

curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/close-pr/45" >/dev/null
curl -fsS -X DELETE "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/comments/$extended_turnover_claim_id" >/dev/null
extended_repair_claim_id="$(curl -fsS -X POST -H 'Content-Type: application/json' \
  -d '{"body":"<!-- fsgg:claim worker=fixture-extended-repair lease=120 -->\nheld"}' \
  "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/45/comments" | jq -r '.id')"
extended_ids+=("45:$extended_repair_claim_id")
turnover_critic="critic-extended-repair"
write_extended_wait enter "$extended_head:initial-review:0" "" "$extended_repair_claim_id"
"$ENGINE" review wait FS.GG.SDD#45 "$extended_wait" --pr 46 --worker "$extended_repair_worker" --json >/dev/null 2>&1; extended_initial_wait_rc=$?
write_turnover_draft initial 0 "" "" "" "$extended_head" "FS-GG/FS.GG.SDD#45/pr/46" changes-required
extended_initial_out="$("$ENGINE" review record FS.GG.SDD#45 "$turnover_draft" --pr 46 --worker "$extended_repair_worker" --json 2>&1)"; extended_initial_rc=$?
extended_repair_initial_url="$(printf '%s' "$extended_initial_out" | jq -r '.commentUrl // empty')"
extended_repair_initial_digest="$(printf '%s' "$extended_initial_out" | jq -r '.digest // empty')"
write_extended_wait complete "$extended_head:initial-review:0" "$extended_repair_initial_url" "$extended_repair_claim_id"
"$ENGINE" review wait FS.GG.SDD#45 "$extended_wait" --pr 46 --worker "$extended_repair_worker" --json >/dev/null 2>&1; extended_initial_complete_rc=$?
extended_assert_out="$("$ENGINE" review assert-repair repair-phase FS.GG.SDD#45 "$extended_repair_initial_url" 'Accountable host confirms the unchanged repaired head is ready for repair-phase review.' --pr 46 --worker accountable-extended-host --json 2>&1)"; extended_assert_rc=$?
extended_assert_id="$(printf '%s' "$extended_assert_out" | jq -r '.commentId // empty')"
[ -n "$extended_assert_id" ] && extended_ids+=("45:$extended_assert_id")
extended_entry_wait_out="$("$ENGINE" review wait enter FS.GG.SDD#45 --pr 46 --worker "$extended_repair_worker" --json 2>&1)"; extended_entry_wait_rc=$?
write_turnover_draft repair-phase 0 "$extended_repair_initial_digest" "$extended_repair_initial_url" "$extended_repair_initial_url" "$extended_head" "FS-GG/FS.GG.SDD#45/pr/46" changes-required
jq --argjson exhausted 45 --argjson escalation "$extended_escalation_id" --arg claim "$extended_repair_claim_id" --arg branch '46' --arg implementer "$extended_repair_worker" --arg critic "$turnover_critic" --arg head "$extended_head" \
  '.repairPhaseReceipt={exhaustedPr:$exhausted,escalationCommentId:$escalation,newClaimGeneration:$claim,newBranchOrPr:$branch,newImplementerIdentity:$implementer,newCriticIdentity:$critic,candidateHeadSha:$head}' \
  "$turnover_draft" >"$extended_draft"
extended_entry_out="$("$ENGINE" review record FS.GG.SDD#45 "$extended_draft" --pr 46 --worker "$extended_repair_worker" --json 2>&1)"; extended_entry_rc=$?

if [ "$extended_wrong_rc" -ne 0 ] && [ "$extended_wrong_before" = "$extended_wrong_after" ] \
   && [[ "$extended_wrong_out" == *"legacy ordinary-exhaustion evidence"* ]] \
   && [ "$extended_escalation_rc" -eq 0 ] \
   && printf '%s' "$extended_escalation_out" | jq -e '.revision == 6 and (.digest | length) == 64' >/dev/null \
   && [ "$extended_initial_wait_rc" -eq 0 ] && [ "$extended_initial_rc" -eq 0 ] \
   && [ "$extended_initial_complete_rc" -eq 0 ] && [ "$extended_assert_rc" -eq 0 ] \
   && printf '%s' "$extended_assert_out" | jq -e --arg head "$extended_head" \
        '.candidateHeadSha == $head and .purpose == "repair-phase-entry" and (.nextCommand | contains("review wait enter"))' >/dev/null \
   && [ "$extended_entry_wait_rc" -eq 0 ] \
   && [ "$extended_entry_rc" -eq 0 ]; then
  ok ".github#3014: admitted round-4 exhaustion binds its terminal record and enters one typed repair phase"
else
  bad ".github#3014: post-ceiling turnover must bind the actual terminal record without weakening repair entry" \
    "wrong=$extended_wrong_rc:$extended_wrong_before->$extended_wrong_after:$extended_wrong_out escalation=$extended_escalation_rc:$extended_escalation_out initial=$extended_initial_wait_rc/$extended_initial_rc/$extended_initial_complete_rc assertion=$extended_assert_rc:$extended_assert_out entry=$extended_entry_wait_rc:$extended_entry_wait_out/$extended_entry_rc:$extended_entry_out"
fi

for extended_ref in "${extended_ids[@]}"; do
  extended_id="${extended_ref#*:}"
  curl -fsS -X DELETE "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/comments/$extended_id" >/dev/null 2>&1 || true
done
while IFS= read -r extended_pr_id; do
  curl -fsS -X DELETE "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/comments/$extended_pr_id" >/dev/null 2>&1 || true
done < <(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/46/comments" | jq -r '.[].id')
rm -f "$extended_draft" "$extended_wait"

# Remove the review leg's live-claim setup marker. The claim-CAS cases below deliberately start
# unclaimed and prove their own POST/re-read winner rather than inheriting review authorization.
curl -fsS -X DELETE "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/comments/$review_claim_id" >/dev/null

printf '%s%s' '<!-- fsgg:independent-review' ':v1 -->' >"$review_draft"
review_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
"$ENGINE" review record FS.GG.SDD#42 "$review_draft" --pr 42 --json >/dev/null 2>&1; review_legacy_rc=$?
review_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
[ "$review_legacy_rc" -ne 0 ] && [ "$review_before" = "$review_after" ] \
  && ok "M4 review record refuses legacy v1 authoring with zero writes" \
  || bad "M4 legacy review authoring must fail before POST" "rc=$review_legacy_rc comments=$review_before->$review_after"
rm -f "$review_draft"

# ---- the claim CAS: post, re-read, WIN -------------------------------------------------------------
out="$(run claim FS.GG.SDD#42 2>&1)"; rc=$?
if [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'claimed FS.GG.SDD#42 by worker vole-418'; then
  ok "claim WINS the CAS and reports the holder"
else
  bad "claim wins the CAS" "rc=$rc: $out"
fi

# ---- a SECOND worker loses to the live marker ------------------------------------------------------
lost="$("$ENGINE" claim FS.GG.SDD#42 --worker kite-461 2>&1)"; lrc=$?
if [ "$lrc" -ne 0 ] && printf '%s' "$lost" | grep -qi 'already held by vole-418'; then
  ok "a second worker LOSES to the live lock (no double-claim)"
else
  bad "a second worker loses to the live lock" "rc=$lrc: $lost"
fi

# #2268: force is a NEW claim, so capacity admission happens before force can evict the live
# holder.  Drive this through the compiled CLI and the stateful HTTP server: each refusal must leave
# the exact marker body, board Status, and mutation ledger unchanged.  A new cache per mode is
# essential: it proves exhausted/unknown from the response being exercised rather than from a prior
# healthy observation.
force_budget_case() {
  local mode="$1" before_marker before_status after_marker after_status out rc ledger
  curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/rest-budget/$mode" >/dev/null
  before_marker="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments")"
  before_status="$(run ready --repo FS.GG.SDD --status any 2>/dev/null | jq -r '.[] | select(.number == 42) | .status')"
  curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations" >/dev/null
  rm -rf "$FORCE_BUDGET_CACHE"; FORCE_BUDGET_CACHE="$(mktemp -d)"
  out="$(FSGG_COORD_CACHE="$FORCE_BUDGET_CACHE" "$ENGINE" claim FS.GG.SDD#42 --force --worker kite-461 2>&1)"; rc=$?
  ledger="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations")"
  after_marker="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments")"
  after_status="$(run ready --repo FS.GG.SDD --status any 2>/dev/null | jq -r '.[] | select(.number == 42) | .status')"
  if [ "$rc" -ne 0 ] && [ "$before_marker" = "$after_marker" ] && [ "$before_status" = "$after_status" ] \
       && [ "$(printf '%s' "$ledger" | jq -r .count)" = "0" ]; then
    ok "#2268: $mode force admission refuses before marker/status mutation"
  else
    bad "#2268: $mode force admission must not evict or write" "rc=$rc status=$before_status->$after_status ledger=$ledger output=$out"
  fi
}

force_budget_case constrained
force_budget_case exhausted
force_budget_case unknown

curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/rest-budget/healthy" >/dev/null
rm -rf "$FORCE_BUDGET_CACHE"; FORCE_BUDGET_CACHE="$(mktemp -d)"
healthy_force="$(FSGG_COORD_CACHE="$FORCE_BUDGET_CACHE" "$ENGINE" claim FS.GG.SDD#42 --force --worker kite-461 2>&1)"; healthy_force_rc=$?
if [ "$healthy_force_rc" -eq 0 ] && printf '%s' "$healthy_force" | grep -q 'STOLE FS.GG.SDD#42'; then
  ok "#2268: healthy force admission still steals the live claim"
else
  bad "#2268: healthy force admission must preserve force success" "rc=$healthy_force_rc: $healthy_force"
fi

# Hand it back before the longstanding adopt composition proof below.
"$ENGINE" release FS.GG.SDD#42 --worker kite-461 >/dev/null 2>&1
run claim FS.GG.SDD#42 >/dev/null 2>&1

# ---- heartbeat renews the held lease ---------------------------------------------------------------
hb="$(run heartbeat FS.GG.SDD#42 2>&1)"; hrc=$?
if [ "$hrc" -eq 0 ] && printf '%s' "$hb" | grep -q 'heartbeat FS.GG.SDD#42'; then
  ok "heartbeat renews the lease the worker holds"
else
  bad "heartbeat renews the held lease" "rc=$hrc: $hb"
fi

# ---- a NON-HOLDER cannot heartbeat -----------------------------------------------------------------
hbx="$("$ENGINE" heartbeat FS.GG.SDD#42 --worker kite-461 2>&1)"; hxrc=$?
if [ "$hxrc" -ne 0 ] && printf '%s' "$hbx" | grep -qi 'held by vole-418'; then
  ok "a non-holder cannot heartbeat (it names the real holder)"
else
  bad "a non-holder cannot heartbeat" "rc=$hxrc: $hbx"
fi

# ---- .github#1620: THE INSTRUCTION `adopt` PRINTS, FOLLOWED VERBATIM, MUST WORK ---------------------
#
# This is #1620's acceptance criterion 2 made executable, and it is the one that has to run against a REAL
# lock rather than a unit fixture, because the defect was never in one component: `adopt` refused a live
# claim and told the operator to run `claim <ref> --force`; `claim` parsed `--force`, scoped it correctly
# to itself, and read it in a pre-check that had nothing to do with the holder. Every part behaved as
# documented, and the composition dead-ended. So the assertion is a COMPOSITION: take the command the tool
# prints, run THAT, and require it to do what the sentence promised.
#
# It is extracted from the refusal rather than retyped. A test that retypes the command asserts that a
# command works; this asserts that the ADVICE works, which is the thing that was false.
adoptout="$("$ENGINE" adopt FS.GG.SDD#42 --worker kite-461 2>&1)"; adrc=$?
# The last `scripts/fsgg-coord ...` token run on the refusal — the remedy line, with the shim's name
# dropped so the compiled engine can run the same argv.
advice="$(printf '%s\n' "$adoptout" | grep -o 'scripts/fsgg-coord claim [^ ]* --force' | tail -n1 | sed 's|^scripts/fsgg-coord ||')"
if [ "$adrc" -ne 0 ] && [ -n "$advice" ]; then
  ok ".github#1620: adopt refuses a LIVE claim and prints a remedy command to follow"
else
  bad ".github#1620: adopt must refuse a live claim and name a remedy" "rc=$adrc advice='$advice': $adoptout"
fi

# A transport error is not proof the replacement failed to land. Store it, lose only the response,
# and leave both markers visible: the command must discover its marker, clean the old one, and report
# the final one-marker census rather than the ambiguous transport failure or the pre-cleanup census.
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/lose-next-claim-post-response" >/dev/null
response_lost_err="$(mktemp)"
# shellcheck disable=SC2086 # exercising the tool-authored remedy argv verbatim.
response_lost="$("$ENGINE" $advice --worker kite-461 --json 2>"$response_lost_err")"; response_lost_rc=$?
response_lost_ok=false
jq -e \
      '.markerId as $markerId
       | .kind == "stolen"
       and .forcedClaimCensuses.before.winnerMarkerId != null
       and .forcedClaimCensuses.after.winnerMarkerId == $markerId
       and (.forcedClaimCensuses.after.markers | map(.markerId)) == [$markerId]' \
      <<<"$response_lost" >/dev/null && response_lost_ok=true
if [ "$response_lost_rc" -eq 0 ] && [ "$response_lost_ok" = true ] \
   && grep -q 'STOLE FS.GG.SDD#42' "$response_lost_err"; then
  ok ".github#2772: a response-lost replacement POST reconciles both markers and returns the final census"
else
  bad ".github#2772: a stored replacement must survive a lost POST response" \
    "rc=$response_lost_rc stdout=$response_lost stderr=$(cat "$response_lost_err")"
fi
rm -f "$response_lost_err"
"$ENGINE" release FS.GG.SDD#42 --worker kite-461 >/dev/null 2>&1
run claim FS.GG.SDD#42 >/dev/null 2>&1

# The same ambiguous response can race with the incumbent disappearing. The replacement is then the
# authority without having stolen anything, so it must succeed without inventing a theft notice.
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/lose-next-claim-post-response-and-drop-incumbent" >/dev/null
replacement_won_err="$(mktemp)"
# shellcheck disable=SC2086 # exercising the tool-authored remedy argv verbatim.
replacement_won="$("$ENGINE" $advice --worker kite-461 --json 2>"$replacement_won_err")"; replacement_won_rc=$?
replacement_won_ok=false
jq -e \
      '.markerId as $markerId
       | .kind == "replacement-won"
       and .forcedClaimCensuses.before.winnerMarkerId != null
       and .forcedClaimCensuses.after.winnerMarkerId == $markerId
       and (.forcedClaimCensuses.after.markers | map(.markerId)) == [$markerId]' \
      <<<"$replacement_won" >/dev/null && replacement_won_ok=true
if [ "$replacement_won_rc" -eq 0 ] && [ "$replacement_won_ok" = true ] \
   && ! grep -q 'STOLE FS.GG.SDD#42' "$replacement_won_err"; then
  ok ".github#2772: a response-lost replacement that already wins preserves authority without inventing theft"
else
  bad ".github#2772: a replacement winner needs a distinct authoritative result" \
    "rc=$replacement_won_rc stdout=$replacement_won stderr=$(cat "$replacement_won_err")"
fi
rm -f "$replacement_won_err"
"$ENGINE" release FS.GG.SDD#42 --worker kite-461 >/dev/null 2>&1
run claim FS.GG.SDD#42 >/dev/null 2>&1

# A newly observed marker with identical parsed identity but a different opaque renewal token belongs to
# a different request. It must not authorize incumbent deletion, and its non-green result still emits the
# typed final census on stdout while the actionable human diagnostic remains on stderr.
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations" >/dev/null
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/lose-next-claim-post-response-with-mismatch" >/dev/null
mismatch_err="$(mktemp)"
# shellcheck disable=SC2086 # exercising the tool-authored remedy argv verbatim.
mismatch="$("$ENGINE" $advice --worker kite-461 --json 2>"$mismatch_err")"; mismatch_rc=$?
mismatch_mutations="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations")"
if [ "$mismatch_rc" -ne 0 ] \
   && jq -e '.kind == "replacement-post-failed" and .standingWorker == "vole-418"
      and (.forcedClaimCensuses.after.markers | length) == 2' <<<"$mismatch" >/dev/null \
   && ! jq -e '.requests | any(.method == "DELETE")' <<<"$mismatch_mutations" >/dev/null \
   && grep -q 'replacement POST FAILED' "$mismatch_err"; then
  ok ".github#2772: response-lost recovery rejects a same-fields/different-body marker without deleting the incumbent"
else
  bad ".github#2772: only the exact POST draft may authorize ambiguous-response cleanup" \
    "rc=$mismatch_rc stdout=$mismatch stderr=$(cat "$mismatch_err") mutations=$mismatch_mutations"
fi
rm -f "$mismatch_err"
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/reset-claim-42" >/dev/null

# Replacement creation fails before any destructive write. The non-green JSON receipt and stderr prose
# are two projections of the same typed, census-backed old-holder-standing result.
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/fail-next-claim-post" >/dev/null
post_fail_err="$(mktemp)"
# shellcheck disable=SC2086 # exercising the tool-authored remedy argv verbatim.
post_fail="$("$ENGINE" $advice --worker kite-461 --json 2>"$post_fail_err")"; post_fail_rc=$?
if [ "$post_fail_rc" -ne 0 ] \
   && jq -e '.kind == "replacement-post-failed" and .replacementMarkerId == null
      and .standingWorker == "vole-418" and .forcedClaimCensuses.after.winnerMarkerId == .standingMarkerId
      and (.forcedClaimCensuses.after.markers | length) == 1' <<<"$post_fail" >/dev/null \
   && grep -q 'OLD HOLDER STANDS' "$post_fail_err"; then
  ok ".github#2772: ReplacementPostFailed emits a typed final-census receipt and human diagnostic"
else
  bad ".github#2772: ReplacementPostFailed must preserve its census at the CLI boundary" \
    "rc=$post_fail_rc stdout=$post_fail stderr=$(cat "$post_fail_err")"
fi
rm -f "$post_fail_err"
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/reset-claim-42" >/dev/null

# Failed cleanup leaves both incumbent and replacement in the final census and authorizes deterministic
# retry through its retained replacement id.
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/fail-next-claim-delete" >/dev/null
cleanup_err="$(mktemp)"
# shellcheck disable=SC2086 # exercising the tool-authored remedy argv verbatim.
cleanup="$("$ENGINE" $advice --worker kite-461 --json 2>"$cleanup_err")"; cleanup_rc=$?
if [ "$cleanup_rc" -ne 0 ] \
   && jq -e '.kind == "cleanup-required" and .replacementMarkerId != null
      and .failedWorker == "vole-418" and .failedMarkerId == .standingMarkerId
      and (.forcedClaimCensuses.after.markers | length) == 2' <<<"$cleanup" >/dev/null \
   && grep -q 'cleanup is INCOMPLETE' "$cleanup_err"; then
  ok ".github#2772: CleanupRequired emits its retained replacement and final two-marker census"
else
  bad ".github#2772: CleanupRequired must preserve its census at the CLI boundary" \
    "rc=$cleanup_rc stdout=$cleanup stderr=$(cat "$cleanup_err")"
fi
rm -f "$cleanup_err"
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/reset-claim-42" >/dev/null

# If cleanup and its authoritative re-read both fail, `after:null` is the typed observation—not absent
# stdout and not an invented empty census.
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/fail-next-claim-delete-and-census" >/dev/null
unreadable_err="$(mktemp)"
# shellcheck disable=SC2086 # exercising the tool-authored remedy argv verbatim.
unreadable="$("$ENGINE" $advice --worker kite-461 --json 2>"$unreadable_err")"; unreadable_rc=$?
if [ "$unreadable_rc" -ne 0 ] \
   && jq -e '.kind == "post-state-unreadable" and .replacementMarkerId != null
      and .forcedClaimCensuses.before.winnerMarkerId != null
      and .forcedClaimCensuses.after == null' <<<"$unreadable" >/dev/null \
   && grep -q 'post-state is UNREADABLE' "$unreadable_err"; then
  ok ".github#2772: PostStateUnreadable emits a typed receipt with an explicit null final census"
else
  bad ".github#2772: PostStateUnreadable must preserve its census at the CLI boundary" \
    "rc=$unreadable_rc stdout=$unreadable stderr=$(cat "$unreadable_err")"
fi
rm -f "$unreadable_err"
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/reset-claim-42" >/dev/null

# Simulate the replacement vanishing while the incumbent DELETE response fails. The complete re-read
# proves OldHolderStands, distinct from replacement POST failure.
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/lose-replacement-on-next-claim-delete" >/dev/null
old_stands_err="$(mktemp)"
# shellcheck disable=SC2086 # exercising the tool-authored remedy argv verbatim.
old_stands="$("$ENGINE" $advice --worker kite-461 --json 2>"$old_stands_err")"; old_stands_rc=$?
if [ "$old_stands_rc" -ne 0 ] \
   && jq -e '.kind == "old-holder-stands" and .replacementMarkerId != null
      and .standingWorker == "vole-418" and .forcedClaimCensuses.after.winnerMarkerId == .standingMarkerId
      and (.forcedClaimCensuses.after.markers | length) == 1' <<<"$old_stands" >/dev/null \
   && grep -q 'OLD HOLDER STANDS' "$old_stands_err" \
   && ! grep -q 'replacement POST FAILED' "$old_stands_err"; then
  ok ".github#2772: OldHolderStands emits its distinct typed final-census receipt"
else
  bad ".github#2772: OldHolderStands must preserve its census at the CLI boundary" \
    "rc=$old_stands_rc stdout=$old_stands stderr=$(cat "$old_stands_err")"
fi
rm -f "$old_stands_err"
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/reset-claim-42" >/dev/null

# A readable empty final census is a typed anomaly, not omitted output.
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/fail-next-claim-delete-and-drop-all" >/dev/null
no_holder_err="$(mktemp)"
# shellcheck disable=SC2086 # exercising the tool-authored remedy argv verbatim.
no_holder="$("$ENGINE" $advice --worker kite-461 --json 2>"$no_holder_err")"; no_holder_rc=$?
if [ "$no_holder_rc" -ne 0 ] \
   && jq -e '.kind == "no-holder-remaining" and .replacementMarkerId != null
      and .standingWorker == null and .forcedClaimCensuses.after.winnerMarkerId == null
      and (.forcedClaimCensuses.after.markers | length) == 0' <<<"$no_holder" >/dev/null \
   && grep -q 'NO live marker remained' "$no_holder_err"; then
  ok ".github#2772: NoHolderRemaining emits its readable empty final-census receipt"
else
  bad ".github#2772: NoHolderRemaining must preserve its census at the CLI boundary" \
    "rc=$no_holder_rc stdout=$no_holder stderr=$(cat "$no_holder_err")"
fi
rm -f "$no_holder_err"
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/reset-claim-42" >/dev/null

# A newcomer can win after incumbent cleanup. Withdrawal of our replacement completes before the final
# receipt census, which must contain only the foreign winner.
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/race-next-claim-delete" >/dev/null
forced_lost_err="$(mktemp)"
# shellcheck disable=SC2086 # exercising the tool-authored remedy argv verbatim.
forced_lost="$("$ENGINE" $advice --worker kite-461 --json 2>"$forced_lost_err")"; forced_lost_rc=$?
if [ "$forced_lost_rc" -ne 0 ] \
   && jq -e '.kind == "forced-claim-lost" and .replacementMarkerId == null
      and .standingWorker == "otter-77" and .forcedClaimCensuses.after.winnerMarkerId == .standingMarkerId
      and (.forcedClaimCensuses.after.markers | map(.worker)) == ["otter-77"]' <<<"$forced_lost" >/dev/null \
   && grep -q 'replacement did not win and was withdrawn' "$forced_lost_err"; then
  ok ".github#2772: ForcedClaimLost emits the post-withdrawal final-census receipt"
else
  bad ".github#2772: ForcedClaimLost must preserve its census at the CLI boundary" \
    "rc=$forced_lost_rc stdout=$forced_lost stderr=$(cat "$forced_lost_err")"
fi
rm -f "$forced_lost_err"
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/reset-claim-42" >/dev/null

# shellcheck disable=SC2086 # $advice is the tool's OWN argv, split on purpose — that is what "verbatim" means
steal_err="$(mktemp)"
steal="$("$ENGINE" $advice --worker kite-461 --json 2>"$steal_err")"; strc=$?
steal_notice=false
grep -q 'STOLE FS.GG.SDD#42' "$steal_err" && steal_notice=true
steal_census=false
jq -e \
      '.markerId as $markerId
       | .kind == "stolen" and .forcedClaimCensuses.before.winnerMarkerId != null
       and .forcedClaimCensuses.after.winnerMarkerId == $markerId
       and (.forcedClaimCensuses.before.markers | length) >= 1
       and (.forcedClaimCensuses.after.markers | length) == 1' <<<"$steal" >/dev/null && steal_census=true
if [ "$strc" -eq 0 ] && [ "$steal_notice" = true ] && [ "$steal_census" = true ]; then
  ok ".github#1620/#2772: running the remedy takes the item and its receipt carries pre/post censuses"
else
  bad ".github#1620/#2772: adopt's remedy must work and return census evidence" "rc=$strc stdout=$steal stderr=$(cat "$steal_err")"
fi
rm -f "$steal_err"

# THE DISPLACED WORKER MUST FIND OUT. It is still running — that is the whole difference between a steal
# and a stale collection — so a heartbeat that quietly succeeded would leave two workers on one item.
#
# It must also say WHY it might be held by someone else, or this asserts nothing the pre-existing
# non-holder leg above does not already produce: `held by kite-461` is what a worker sees when it simply
# never held the item. A worker that DID hold it needs to be told its claim was taken, not left to read a
# generic non-holder refusal as its own mistake.
hbs="$(run heartbeat FS.GG.SDD#42 2>&1)"; hbsrc=$?
if [ "$hbsrc" -ne 0 ] && printf '%s' "$hbs" | grep -qi 'held by kite-461' \
     && printf '%s' "$hbs" | grep -q -- '--force'; then
  ok ".github#1620: the displaced holder's heartbeat FAILS LOUDLY and names the worker that took it"
else
  bad ".github#1620: a displaced holder must not heartbeat successfully" "rc=$hbsrc: $hbs"
fi

# AND THE THEFT IS RECORDED ON THE ITEM, reachable by the displaced worker through its own inbox. The
# evicted MARKER was deleted, so this notice is the only surviving trace of the claim that was taken.
# `--repo` because this fixture serves no Status column, so the board scan yields no in-progress row for
# the mailbox to derive its repo set from. On a real board the item's own `In progress` row supplies it.
ibx="$(run inbox --repo FS.GG.SDD 2>&1)"; ibxrc=$?
if [ "$ibxrc" -eq 0 ] && printf '%s' "$ibx" | grep -q 'kite-461' && printf '%s' "$ibx" | grep -q 'TAKEN'; then
  ok ".github#1620: ...and the steal is recorded on the item, in the displaced worker's inbox"
else
  bad ".github#1620: a steal must be announced to the worker it displaced" "rc=$ibxrc: $ibx"
fi

# Hand #42 back, so the legs below run against the state they were written for: kite-461 drops the lock it
# stole and vole-418 re-takes it. A fixture leg that leaves the world moved is a leg that breaks its
# neighbours for reasons that have nothing to do with them.
"$ENGINE" release FS.GG.SDD#42 --worker kite-461 >/dev/null 2>&1
run claim FS.GG.SDD#42 >/dev/null 2>&1

# ---- release drops the lock ------------------------------------------------------------------------
rel="$(run release FS.GG.SDD#42 2>&1)"; rrc=$?
if [ "$rrc" -eq 0 ] && printf '%s' "$rel" | grep -q 'released FS.GG.SDD#42'; then
  ok "release drops the held lock"
else
  bad "release drops the held lock" "rc=$rrc: $rel"
fi

# ...and after release, the item is claimable again (the marker really went away).
reclaim="$(run claim FS.GG.SDD#42 2>&1)"; rcrc=$?
[ "$rcrc" -eq 0 ] && ok "the item is claimable again after release (the marker was removed)" \
  || bad "the item is claimable again after release" "rc=$rcrc: $reclaim"
run release FS.GG.SDD#42 >/dev/null 2>&1

# ---- set-field writes a board column ---------------------------------------------------------------
sf="$(run set-field FS.GG.SDD#43 Status 'In progress' 2>&1)"; sfrc=$?
if [ "$sfrc" -eq 0 ] && printf '%s' "$sf" | grep -q 'set FS-GG/FS.GG.SDD#43 Status = In progress'; then
  ok "set-field writes a board column"
else
  bad "set-field writes a board column" "rc=$sfrc: $sf"
fi

# ---- an UNKNOWN field is refused, costing no mutation ----------------------------------------------
sfx="$(run set-field FS.GG.SDD#43 Nonexistent x 2>&1)"; sfxrc=$?
[ "$sfxrc" -ne 0 ] && printf '%s' "$sfx" | grep -qi 'no field named' \
  && ok "set-field refuses an unknown field" \
  || bad "set-field refuses an unknown field" "rc=$sfxrc: $sfx"

# ---- child attaches by id --------------------------------------------------------------------------
ch="$(run child FS.GG.SDD#99 FS.GG.SDD#43 2>&1)"; chrc=$?
[ "$chrc" -eq 0 ] && printf '%s' "$ch" | grep -q 'linked FS.GG.SDD#43 as a sub-issue of FS.GG.SDD#99' \
  && ok "child attaches the sub-issue" \
  || bad "child attaches the sub-issue" "rc=$chrc: $ch"

# ---- say needs no lock -----------------------------------------------------------------------------
sy="$(run say FS.GG.SDD#43 --to kite-461 --message 'heads up, our paths overlap' 2>&1)"; syrc=$?
[ "$syrc" -eq 0 ] && printf '%s' "$sy" | grep -q 'said to kite-461' \
  && ok "say posts a message with no lock required" \
  || bad "say posts a message" "rc=$syrc: $sy"

# ---- widen requires the HELD claim (#706) ----------------------------------------------------------
# Not holding #43 → widen must refuse (the ownership check is an argument, not an if).
wx="$("$ENGINE" widen FS.GG.SDD#43 --worker ghost-000 --paths 'src/New/**' 2>&1)"; wxrc=$?
[ "$wxrc" -ne 0 ] && printf '%s' "$wx" | grep -qi 'does not hold' \
  && ok "#706: widen refuses when the caller does not hold the claim" \
  || bad "#706: widen refuses without the lock" "rc=$wxrc: $wx"

# Now hold it, then widen succeeds.
run claim FS.GG.SDD#43 >/dev/null 2>&1
wd="$("$ENGINE" widen FS.GG.SDD#43 --worker vole-418 --paths 'src/New/**' 'docs/**' 2>&1)"; wdrc=$?
[ "$wdrc" -eq 0 ] && printf '%s' "$wd" | grep -q 'widened FS.GG.SDD#43 → Paths: src/Other/\*\*, src/New/\*\*, docs/\*\*' \
  && ok "#1377: widen unions new paths into a HELD item's existing touch-set" \
  || bad "#1377: widen preserves a held item's existing touch-set" "rc=$wdrc: $wd"

# A second call preserves both earlier generations; repeating it is idempotent (one token, not two).
wd2="$("$ENGINE" widen FS.GG.SDD#43 --worker vole-418 --paths 'src/Third/**' 2>&1)"; wd2rc=$?
[ "$wd2rc" -eq 0 ] && printf '%s' "$wd2" | grep -q 'Paths: src/Other/\*\*, src/New/\*\*, docs/\*\*, src/Third/\*\*' \
  && ok "#1377: a second widen preserves every prior token" \
  || bad "#1377: two widening calls produce the normalized union" "rc=$wd2rc: $wd2"
wd3="$("$ENGINE" widen FS.GG.SDD#43 --worker vole-418 --paths './src/Third/**,' 2>&1)"; wd3rc=$?
third_count="$(printf '%s' "$wd3" | head -n1 | grep -o 'src/Third/\*\*' | wc -l | tr -d ' ')"
[ "$wd3rc" -eq 0 ] && [ "$third_count" = 1 ] \
  && ok "#1377: repeated widen is idempotent" \
  || bad "#1377: repeated widen must not duplicate a token" "rc=$wd3rc count=$third_count: $wd3"

# ---- an unmatchable token is refused BEFORE any write (#273/#523) ----------------------------------
before_bad="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43" | jq -r .body)"
wu="$("$ENGINE" widen FS.GG.SDD#43 --worker vole-418 --paths '**/never.fs' 2>&1)"; wurc=$?
after_bad="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43" | jq -r .body)"
[ "$wurc" -ne 0 ] && printf '%s' "$wu" | grep -qi 'reserve NOTHING' && [ "$after_bad" = "$before_bad" ] \
  && ok "#273/#1377: an unmatchable new token is refused without modifying the union" \
  || bad "#273/#1377: an unmatchable token must leave the existing declaration intact" "rc=$wurc: $wu"

# Replacement remains available, but only under an explicit name; this is the narrowing operation.
sp="$("$ENGINE" set-paths FS.GG.SDD#43 --worker vole-418 --paths 'src/Narrow/**' 2>&1)"; sprc=$?
[ "$sprc" -eq 0 ] && printf '%s' "$sp" | grep -q 'set FS.GG.SDD#43 → Paths: src/Narrow/\*\*' \
  && ok "#1377: set-paths explicitly replaces (and can narrow) the touch-set" \
  || bad "#1377: set-paths is the explicit replacement operation" "rc=$sprc: $sp"

# ---- standalone done is nonterminal completion replay ----------------------------------------------
# Terminal completion authority belongs to `delivery --pr ... --apply`, where the typed lifecycle
# receipt is minted and verified. The compatibility spelling may inspect facts, but it must never
# synthesize the old FSGG-DONE stamp, alter the board, release a claim, or offer a completion chore.
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations" >/dev/null
dn="$(run "done" FS.GG.SDD#42 2>&1)"; dnrc=$?   # quoted: the coord VERB, not the loop keyword (SC1010, #648)
dn_ledger="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations")"
if [ "$dnrc" -ne 0 ] && grep -q 'FSGG-NOT-DONE' <<<"$dn" \
   && grep -q 'delivery --pr' <<<"$dn" && [ "$(jq -r .count <<<"$dn_ledger")" = 0 ]; then
  ok "change-risk: standalone done refuses without any remote mutation and names delivery authority"
else
  bad "change-risk: standalone done must be a nonterminal replay" "rc=$dnrc output=$dn ledger=$dn_ledger"
fi

"$ENGINE" claim FS.GG.SDD#42 --worker done-guard >/dev/null 2>&1
before_done_claim="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments")"
dd="$("$ENGINE" "done" FS.GG.SDD#42 --flip --worker done-guard 2>&1)"; ddrc=$?
after_done_claim="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments")"
if [ "$ddrc" -ne 0 ] && grep -q 'FSGG-NOT-DONE' <<<"$dd" \
   && grep -q 'fsgg:claim' <<<"$before_done_claim" && grep -q 'fsgg:claim' <<<"$after_done_claim" \
   && ! grep -qi 'chore' <<<"$dd"; then
  ok "change-risk: standalone done preserves the live claim and cannot conscript a chore"
else
  bad "change-risk: nonterminal done must preserve claim authority" "rc=$ddrc output=$dd before=$before_done_claim after=$after_done_claim"
fi
"$ENGINE" release FS.GG.SDD#42 --worker done-guard >/dev/null 2>&1

# .github#2981 — this is the production HTTP route and the exact completion-authority equality boundary.
# The receipt carries REST's full merge_commit_sha; Done.facts must preserve GraphQL Commit.oid in full.
# Restoring abbreviatedOid in the fixture or shortening either side turns this named gate red.
done_full_sha='77abc12000000000000000000000000000000000'
done_out="$("$ENGINE" "done" FS-GG/.github#51 --worker snipe-733 2>&1)"; done_rc=$?
if [ "$done_rc" -eq 0 ] && grep -q 'FSGG-DONE' <<<"$done_out" \
   && grep -q "merged PR #77 @ $done_full_sha" <<<"$done_out"; then
  ok ".github#2981: production completion replay observes the exact full GraphQL merge oid"
else
  bad ".github#2981: completion receipt and closer must agree on the exact full merge oid" "rc=$done_rc output=$done_out"
fi

for done_ref in FS.GG.SDD#42 FS.GG.Legacy#60; do
  done_out="$("$ENGINE" "done" "$done_ref" --flip --worker snipe-733 2>&1)"; done_rc=$?
  if [ "$done_rc" -ne 0 ] && grep -q 'FSGG-NOT-DONE' <<<"$done_out" && ! grep -qi 'chore' <<<"$done_out"; then
    ok "change-risk: $done_ref cannot complete or offer a chore through standalone done"
  else
    bad "change-risk: standalone done authority is uniform across repositories" "ref=$done_ref rc=$done_rc output=$done_out"
  fi
done

# ---- next does not conscript legacy completion evidence --------------------------------------------
# `next` remains conditionally write-capable when it offers an authorized chore. This fixture exposes
# only legacy done markers, so the correct arm is the empty answer with an untouched chore-lock thread.
lk0="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/.github/issues/1033/comments")"
printf '%s' "$lk0" | grep -q 'fsgg:claim' \
  && bad ".github#1535: the chore lock is FREE before the probe" "a marker survives: $lk0" \
  || ok ".github#1535: the chore lock is free before the probe (the one variable is the verb)"

# THE NEGATIVE CONTROL FIRST, and it is load-bearing rather than tidy. `batch` is the same scheduling
# decision uncapped and makes NO offer, so it is the spelling a stale engine still permits (`BOARD_READS`)
# and the one the recipe now sends an idling worker to. If `batch` took the lock too, that advice would be
# wrong — so this asserts the substitute really is a read, on the same board, in the same breath.
#
# `--text` IS PART OF THE SPELLING UNDER TEST, not decoration: `batch` defaults to JSON (`Both Json`), and
# the recipe tells a worker to run `batch --text -n 1` precisely because the JSON arm prints `[]` rather
# than the sentence `next` prints. Drive what the docs say to drive, or the leg guards a different command.
#
# THE SAME WORKER RUNS BOTH VERBS, so the pair differs in ONE variable. Two ids would leave "maybe the
# other worker simply was not idle" as an unexcluded explanation for the empty thread — and an offer is
# withheld from a non-idle caller (condition 3), which is exactly how this control could pass while
# proving nothing. `teal-1535` takes no claim from `batch`, so it is still idle at the `next` below.
bn="$("$ENGINE" batch --text --repo .github -n 1 --worker teal-1535 2>&1)"; bnrc=$?
lkb="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/.github/issues/1033/comments")"
[ "$bnrc" -eq 0 ] && ! printf '%s' "$lkb" | grep -q 'fsgg:claim' \
  && ok ".github#1535: \`batch --text\` is the same decision and takes NO lock — the read half of the pair" \
  || bad ".github#1535: batch takes no chore lock" "rc=$bnrc: $bn / lock thread: $lkb"

# ...and it really is the SAME decision, not merely another quiet command: it prints the sentence `next`
# prints. Without this the control could be satisfied by a `batch` that answered nothing at all.
#
# THE WORDS ARE WHAT THIS LEG GRADES, NOT THE STREAM. .github#1562 took `next`'s empty arm off the shared
# `printChosen` so its headline could go to STDERR (`next`'s stdout is a bare-ref machine contract); `batch
# --text` still prints it to STDOUT. Both `bn` here and `nx` below capture MERGED (`2>&1`), so this leg is
# deliberately blind to that split and asserts only the thing #1535's advice rests on — that the substitute
# ANSWERS, in the one `nothingSchedulable` spelling both verbs still share.
printf '%s' "$bn" | grep -q 'nothing schedulable right now.' \
  && ok ".github#1535: ...and it is the same ANSWER — one shared \`nothingSchedulable\` spelling, so the words cannot drift" \
  || bad ".github#1535: batch --text prints next's answer" "rc=$bnrc: $bn"

# Legacy done markers are no longer completion authority, so `next` must not conscript their stale
# projections or take a chore lock for them. A typed delivery receipt is the only terminal source.
nx="$("$ENGINE" next --repo .github --worker teal-1535 2>&1)"; nxrc=$?
lk1="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/.github/issues/1033/comments")"
[ "$nxrc" -eq 0 ] && grep -q 'nothing schedulable right now.' <<<"$nx" \
  && ! grep -q 'fsgg:claim' <<<"$lk1" && ! grep -qi '^chore ' <<<"$nx" \
  && ok "change-risk: next ignores legacy completion markers and takes no chore lock" \
  || bad "change-risk: legacy done evidence must not authorize chore conscription" "rc=$nxrc: $nx / lock thread: $lk1"

# Defensive cleanup keeps downstream tests isolated if this assertion ever regresses.
"$ENGINE" release "FS-GG/.github#1033" --worker teal-1535 >/dev/null 2>&1

# ---- verify-paths: a PR inside its touch-set is OK -------------------------------------------------
vp="$("$ENGINE" verify-paths --pr 500 --repo FS.GG.SDD 2>&1)"; vprc=$?
[ "$vprc" -eq 0 ] && printf '%s' "$vp" | grep -q 'FSGG-PATHS OK' \
  && ok "verify-paths: a PR inside its touch-set is OK (exit 0)" \
  || bad "verify-paths OK" "rc=$vprc: $vp"

# ---- verify-paths: a PR that DRIFTS names the file and fails ---------------------------------------
vd="$("$ENGINE" verify-paths --pr 501 --repo FS.GG.SDD 2>&1)"; vdrc=$?
[ "$vdrc" -ne 0 ] && printf '%s' "$vd" | grep -q 'FSGG-PATHS DRIFT' && printf '%s' "$vd" | grep -q 'docs/x.md' \
  && ok "verify-paths: a drifting PR names the out-of-bounds file and fails" \
  || bad "verify-paths DRIFT" "rc=$vdrc: $vd"

# ---- verify-paths --warn downgrades DRIFT to advisory (exit 0) -------------------------------------
vw="$("$ENGINE" verify-paths --pr 501 --repo FS.GG.SDD --warn 2>&1)"; vwrc=$?
[ "$vwrc" -eq 0 ] && printf '%s' "$vw" | grep -q 'FSGG-PATHS DRIFT' \
  && ok "verify-paths --warn downgrades DRIFT to advisory (exit 0)" \
  || bad "verify-paths --warn is advisory" "rc=$vwrc: $vw"

# ---- #498/ADR-0044: verify-paths subtracts the GENERATED, CI-GATED artifacts -----------------------
# §1 forbids declaring a generated artifact, and `verify-paths` then reported it as DRIFT anyway — the
# gate firing on the behaviour the protocol mandates. These legs pin the subtraction AND, more
# importantly, the three ways it must FAIL CLOSED.
#
# HERMETIC ON PURPOSE, IN A WAY THE OTHER LEGS DO NOT HAVE TO BE. The subtraction is gated on "the
# checkout IS the PR's repo", which the engine answers from `git config remote.origin.url` of its CWD.
# Reading that from the ambient checkout would make these legs pass or fail on WHERE THEY WERE RUN — green
# in CI, red in a fork or a worktree whose remote is a mirror, for a reason having nothing to do with the
# engine. So the CWD is a throwaway git repo whose remote is set explicitly, and the answer is the same
# everywhere. `-c` rather than global config: a fixture must not read the runner's ~/.gitconfig (#709).
VP_CO="$(mktemp -d)"
git -c init.defaultBranch=main init -q "$VP_CO"
git -C "$VP_CO" remote add origin https://github.com/FS-GG/.github.git

# The kit root the engine asks for `scripts/generated-paths` is the REAL repo — so this leg drives the
# REAL roster, not a restatement of it. A stub would prove the plumbing and nothing about the contract.
vg="$(cd "$VP_CO" && FSGG_KIT_ROOT="$REPO_ROOT" "$ENGINE" verify-paths --pr 502 --repo .github 2>/dev/null)"; vgrc=$?
[ "$vgrc" -eq 0 ] && printf '%s' "$vg" | grep -q 'FSGG-PATHS OK' \
  && printf '%s' "$vg" | grep -q 'regenerated (expected)' && printf '%s' "$vg" | grep -q 'registry/repos.lock' \
  && ok "#498: a regenerated CI-gated artifact is subtracted from drift, and reported as expected" \
  || bad "#498: regenerated artifact subtracted" "rc=$vgrc: $vg"

# The split is the deliverable: a real overrun must stay a finding, and must not be buried beside a file
# the reader is not being asked to act on.
vs="$(cd "$VP_CO" && FSGG_KIT_ROOT="$REPO_ROOT" "$ENGINE" verify-paths --pr 503 --repo .github 2>/dev/null)"; vsrc=$?
[ "$vsrc" -ne 0 ] && printf '%s' "$vs" | grep -q 'FSGG-PATHS DRIFT' \
  && printf '%s' "$vs" | sed -n '/undeclared (review)/,/regenerated/p' | grep -q 'docs/x.md' \
  && ! printf '%s' "$vs" | sed -n '/undeclared (review)/,/regenerated/p' | grep -q 'repos.lock' \
  && ok "#498: real drift stays RED and is reported apart from the regenerated artifact" \
  || bad "#498: drift/regenerated split" "rc=$vsrc: $vs"

# FAIL CLOSED 1 — the checkout is NOT the PR's repo. The local generators say nothing about another repo's
# artifacts, so subtracting this repo's set there would suppress REAL drift in a repo nobody asked. This is
# the one fail-open direction the roster itself cannot see, because it is a fact about the CALL.
vx="$(cd "$VP_CO" && FSGG_KIT_ROOT="$REPO_ROOT" "$ENGINE" verify-paths --pr 502 --repo FS.GG.SDD 2>/dev/null)"; vxrc=$?
[ "$vxrc" -ne 0 ] && printf '%s' "$vx" | grep -q 'repos.lock' \
  && ok "#498: subtracts NOTHING when the checkout is not the PR's repo (drift stays reported)" \
  || bad "#498: cross-repo subtraction is refused" "rc=$vxrc: $vx"

# FAIL CLOSED 2 and 3 — an ABSENT `generated-paths`, and one that FAILS. Both must subtract NOTHING and
# leave drift exactly as it is today: "I could not ask what is generated" and "nothing is generated" are
# opposite facts, and only one is safe to act on (#266).
VP_EMPTY="$(mktemp -d)"
va="$(cd "$VP_CO" && FSGG_KIT_ROOT="$VP_EMPTY" "$ENGINE" verify-paths --pr 502 --repo .github 2>/dev/null)"; varc=$?
[ "$varc" -ne 0 ] && printf '%s' "$va" | grep -q 'repos.lock' \
  && ok "#498: an ABSENT generated-paths subtracts nothing (fails closed)" \
  || bad "#498: absent generated-paths fails closed" "rc=$varc: $va"

VP_BAD="$(mktemp -d)"; mkdir -p "$VP_BAD/scripts"
printf '#!/bin/sh\necho boom >&2\nexit 2\n' >"$VP_BAD/scripts/generated-paths"
chmod +x "$VP_BAD/scripts/generated-paths"
vf="$(cd "$VP_CO" && FSGG_KIT_ROOT="$VP_BAD" "$ENGINE" verify-paths --pr 502 --repo .github 2>/dev/null)"; vfrc=$?
[ "$vfrc" -ne 0 ] && printf '%s' "$vf" | grep -q 'repos.lock' \
  && ok "#498: a FAILING generated-paths subtracts nothing (fails closed)" \
  || bad "#498: failing generated-paths fails closed" "rc=$vfrc: $vf"

# ...and it SAYS SO, naming the generator's own reason. Failing closed silently is only half a remedy:
# the artifact reappears under `undeclared (review):`, the recipe tells the worker to go look at the
# generator, and without this nothing says WHICH one or why. `generated-paths` goes out of its way to
# put that on stderr; the engine must not be the thing that throws it away (#266 — a right verdict with
# an unreadable reason).
vm="$(cd "$VP_CO" && FSGG_KIT_ROOT="$VP_BAD" "$ENGINE" verify-paths --pr 502 --repo .github 2>&1 >/dev/null)"
printf '%s' "$vm" | grep -q 'generated-paths exited 2' && printf '%s' "$vm" | grep -q 'boom' \
  && ok "#498: a failing generated-paths forwards its REASON — fails closed, and readably" \
  || bad "#498: failing generated-paths names its reason" "$vm"

# ...and one that SUCCEEDS while listing nothing. Distinct from the failing case: exit 0 is the code a
# reader is most tempted to trust, and an empty answer from a healthy generator still is not a licence to
# subtract everything.
printf '#!/bin/sh\nexit 0\n' >"$VP_BAD/scripts/generated-paths"
ve="$(cd "$VP_CO" && FSGG_KIT_ROOT="$VP_BAD" "$ENGINE" verify-paths --pr 502 --repo .github 2>/dev/null)"; verc=$?
[ "$verc" -ne 0 ] && printf '%s' "$ve" | grep -q 'repos.lock' \
  && ok "#498: an EMPTY generated-paths subtracts nothing (fails closed)" \
  || bad "#498: empty generated-paths fails closed" "rc=$verc: $ve"

# FAIL CLOSED 4 — a HANGING generator. The other three fail closed by returning; this one fails closed
# only because the wait is BOUNDED. It is pinned because the first cut of the bound DID NOT WORK and
# looked like it did: a blocking `ReadToEnd` on stdout ran before the timeout, a stuck child never closes
# stdout, so the read never returned and the timeout was never reached. `verify-paths` — the merge gate —
# hung for as long as it was allowed to. The bug was invisible to every other leg here, because a healthy
# generator exercises none of it. Timeout tunable so this costs ~1s instead of 30.
VP_HANG="$(mktemp -d)"; mkdir -p "$VP_HANG/scripts"
printf '#!/bin/sh\nexec sleep 987654\n' >"$VP_HANG/scripts/generated-paths"
chmod +x "$VP_HANG/scripts/generated-paths"
vh_start="$(date +%s)"
vh="$(cd "$VP_CO" && FSGG_KIT_ROOT="$VP_HANG" FSGG_GENERATED_PATHS_TIMEOUT_MS=1000 \
       "$ENGINE" verify-paths --pr 502 --repo .github 2>&1)"; vhrc=$?
vh_elapsed=$(( $(date +%s) - vh_start ))
[ "$vhrc" -ne 0 ] && printf '%s' "$vh" | grep -q 'repos.lock' && printf '%s' "$vh" | grep -q 'was killed' \
  && [ "$vh_elapsed" -lt 15 ] \
  && ok "#498: a HANGING generated-paths is killed and subtracts nothing (bounded, ${vh_elapsed}s)" \
  || bad "#498: hanging generated-paths is bounded" "rc=$vhrc elapsed=${vh_elapsed}s: $vh"

# ...and the hang is REAPED, not orphaned. `Kill true` takes the process tree: killing the script while
# leaving the generator it is blocked on alive would leak the actual hang, one process per PR.
sleep 0.5
[ "$(pgrep -f 'sleep 987654' | wc -l)" -eq 0 ] \
  && ok "#498: the killed generator leaves no orphaned process behind" \
  || bad "#498: hanging generator is reaped" "$(pgrep -af 'sleep 987654')"

rm -rf "$VP_CO" "$VP_EMPTY" "$VP_BAD" "$VP_HANG"

# ---- .github#2211: the compiled binary reaches the cross-owner board-side route -------------------
#
# The external issue's own `projectItems` query deliberately omits Coordination, just as GitHub does for
# EHotwagner/rogue3#96.  These legs therefore fail when an engine falls back to the issue-side route:
# `claim` cannot converge, `release` has no column to restore, and `add` defaults a live row to Backlog.
# They drive the real argv parser and HttpTransport; reading the Python fixture directly would only test
# the fixture's shape, not the seam that previously escaped binary-level coverage.
cross_claim="$("$ENGINE" claim EHotwagner/rogue3#96 --worker osprey-2211 --json 2>&1)"; cross_claim_rc=$?
cross_converged="$(printf '%s' "$cross_claim" | jq -r '.converged // empty' 2>/dev/null)"
[ "$cross_claim_rc" -eq 0 ] && [ "$cross_converged" = true ] \
  && ok "#2211: cross-owner claim converges through the board-side item and Status read" \
  || bad "#2211: cross-owner claim must converge" "rc=$cross_claim_rc converged=$cross_converged: $cross_claim"

cross_release="$("$ENGINE" release EHotwagner/rogue3#96 --worker osprey-2211 2>&1)"; cross_release_rc=$?
[ "$cross_release_rc" -eq 0 ] && printf '%s' "$cross_release" | grep -q 'Backlog' \
  && ok "#2211: bare cross-owner release restores the pre-claim Backlog column" \
  || bad "#2211: bare cross-owner release must restore its pre-claim column" "rc=$cross_release_rc: $cross_release"

# Make the existing row's column live before exercising `add`. A pre-claim `In progress` column has the
# same footprint as the claim's own write, for which bare `release` correctly falls back to Ready.
cross_live="$("$ENGINE" set-field EHotwagner/rogue3#96 Status 'In progress' --worker osprey-2211 2>&1)"; cross_live_rc=$?
[ "$cross_live_rc" -eq 0 ] && printf '%s' "$cross_live" | grep -q 'Status = In progress' \
  && ok "#2211: fixture establishes a live external In progress column before add" \
  || bad "#2211: fixture must establish the live external column" "rc=$cross_live_rc: $cross_live"

cross_add="$("$ENGINE" add EHotwagner/rogue3#96 --worker osprey-2211 2>&1)"; cross_add_rc=$?
[ "$cross_add_rc" -eq 0 ] && printf '%s' "$cross_add" | grep -q "Status='In progress'.*LEFT AS IT IS" \
  && ! printf '%s' "$cross_add" | grep -q 'Status=Backlog' \
  && ok "#2211: add preserves a live cross-owner Status instead of writing the Backlog default" \
  || bad "#2211: add must not overwrite a live cross-owner Status" "rc=$cross_add_rc: $cross_add"

# ---- .github#1740 / .github#1779: a live claim reserves whatever its board COLUMN says ---------------
#
# THE DEFECT, CONSTRUCTED RATHER THAN WAITED FOR. `activeCollisions` picked which claims to check by
# reading the board's `Status` COLUMN, so a claim whose MARKER was live but whose column had not landed
# reserved nothing — and answered `DISJOINT`, the one verdict the touch-set protocol exists to make
# trustworthy. Measured live on 2026-07-28: two workers declared `src/FS.GG.Coord.Cli/Client.fs` 41
# seconds apart and `widen` said DISJOINT 53 seconds later.
#
# WHY THESE LEGS AND NOT A TIMING TEST. The failure is a race, and a race reproduced by sleeping is a test
# that passes when the timing happens to work. The fixture's `defer-next-field-write` /
# `fail-next-field-write` / `off_board` make each state DETERMINISTIC: `claim` posts its marker, its
# `Status` write meets the injected condition, and the column is left where it was. That is not a
# simulation of the bug's state, it is the bug's state, reached the way production reaches it.
#
# THREE STATES, AND THEY ARE NOT VARIATIONS OF ONE. `claim` distinguishes `statusWrite: deferred | failed
# | not-on-board`, exits GREEN on all three, and #510 queues only the FIRST. #1740 closed it by reading
# the deferral queue, which by construction says nothing about the other two. Each leg below asserts its
# own precondition off the RECEIPT and off the BOARD before asserting the verdict — a leg whose state was
# never built would otherwise measure the ordinary `In progress` path and pass for the wrong reason.
#
# THE UNIT LEGS COVER THE CACHE HALF (this script runs with FSGG_COORD_SCAN_TTL_SEC=0, so there is no
# stale scan here to have). These cover the halves no amount of freshness can reach.

# #43 leaves vole-418's hands declaring the SAME path #44 declares, and parked in a NON-claim column.
"$ENGINE" set-paths FS.GG.SDD#43 --worker vole-418 --paths 'src/Verify/**' >/dev/null 2>&1
"$ENGINE" release FS.GG.SDD#43 --worker vole-418 --status Ready >/dev/null 2>&1

# ARM, then claim: the marker lands, the column write does not.
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/defer-next-field-write" >/dev/null
cl="$("$ENGINE" claim FS.GG.SDD#43 --worker otter-777 --json 2>&1)"; clrc=$?
sw="$(printf '%s' "$cl" | jq -r '.statusWrite' 2>/dev/null)"

# THE PRECONDITION IS ITSELF AN ASSERTION, and it is read off the BOARD rather than off the receipt. If
# the claim converged, the state under test was never built and everything below would be measuring the
# ordinary `In progress` path — passing for the wrong reason. This is AC3's fixture stated literally: the
# row still reads `Ready`, and it is not lying, because the write genuinely has not happened.
col43="$("$ENGINE" ready --repo FS.GG.SDD --worker vole-418 2>/dev/null | jq -r '.[] | select(.number==43) | .status')"
[ "$clrc" -eq 0 ] && [ "$sw" = "deferred" ] && [ "$col43" = "Ready" ] \
  && ok "#1740: the fixture built the state — marker live, Status write DEFERRED, board row still reads '$col43'" \
  || bad "#1740: build a live claim whose Status column has not landed" "rc=$clrc statusWrite=$sw column=$col43: $cl"

# THE ASSERTION. #44 declares `src/Verify/**`; #43 now declares it too and is HELD by otter-777. The
# column says `Ready` and is not lying — the write genuinely has not happened.
ov="$("$ENGINE" overlap FS.GG.SDD#44 --active --worker vole-418 2>&1)"; ovrc=$?
[ "$ovrc" -eq 6 ] && printf '%s' "$ov" | grep -q 'OVERLAP' && printf '%s' "$ov" | grep -q 'otter-777' \
  && ok "#1740: a live claim whose Status column has NOT landed still COLLIDES (no false DISJOINT)" \
  || bad "#1740: the collision scan sees a claim the board column does not" "rc=$ovrc: $ov"

# .github#1779 — THE QUEUE IS NO LONGER WHAT CARRIES THE RESERVATION, AND THIS IS WHERE THAT IS PROVED.
#
# This leg was #1740's control, and it asserted the opposite: take the DEFERRAL QUEUE away (a fresh cache
# root has none) and the reservation was supposed to vanish with it. It did, and that was the bug one
# level up — #43's marker was live and its declaration collided in both runs, so `DISJOINT` was never the
# right answer here; the queue's presence was the only thing standing between a worker and a file another
# worker held. The candidate set is the repo's OPEN ISSUES now, so the empty queue changes nothing.
EMPTY_CACHE="$(mktemp -d)"
ovc="$(FSGG_COORD_CACHE="$EMPTY_CACHE" "$ENGINE" overlap FS.GG.SDD#44 --active --worker vole-418 2>&1)"; ovcrc=$?
rm -rf "$EMPTY_CACHE"
[ "$ovcrc" -eq 6 ] && printf '%s' "$ovc" | grep -q 'OVERLAP' && printf '%s' "$ovc" | grep -q 'otter-777' \
  && ok "#1779: the SAME state with an EMPTY deferral queue still collides — the marker carries it, not the queue" \
  || bad "#1779: an empty queue must not resurrect the false DISJOINT" "rc=$ovcrc: $ovc"

# THE CONTROL THAT MATTERS NOW, and without it every leg here is satisfied by "report every open issue".
# Same board, same column, same declaration — the MARKER is released. A declaration nobody holds is work
# nobody is doing, and reporting it would stop a worker who has nothing to stop for.
"$ENGINE" release FS.GG.SDD#43 --worker otter-777 --status Ready >/dev/null 2>&1
ovn="$("$ENGINE" overlap FS.GG.SDD#44 --active --worker vole-418 2>&1)"; ovnrc=$?
[ "$ovnrc" -eq 0 ] && printf '%s' "$ovn" | grep -q 'DISJOINT' \
  && ok "#1779: control — the same colliding declaration with NO live claim reads DISJOINT (the marker is the lock)" \
  || bad "#1779: colliding tokens alone must not reserve" "rc=$ovnrc: $ovn"

# ---- .github#1779 leg 1: a `Status` write that FAILED PERMANENTLY still reserves ---------------------
#
# #510 QUEUES A DEFERRAL AND REFUSES TO QUEUE A FAILURE, and that is correct — a write replayed forever is
# a promise nobody can keep. The consequence is that nothing will EVER write this column, so no freshness
# tier and no queue read can conjure it. #1740 named this as its declined remainder.
# THE QUEUE DEPTH IS READ BEFORE AND AFTER, NOT COMPARED TO ZERO. `pendingBoardWrites` is the depth of
# the whole shared queue, and the DEFERRED leg above legitimately left an entry in it — an assertion of
# `== 0` would be testing "no other test deferred anything", which is a fact about test ordering and not
# about #510. The load-bearing fact is that this failure ADDED NOTHING: a permanent failure is not queued,
# so nothing will ever replay it, so nothing will ever write that column.
q_before="$("$ENGINE" budget --json --worker vole-418 2>/dev/null | jq -r '.pendingBoardWrites')"
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/fail-next-field-write" >/dev/null
clf="$("$ENGINE" claim FS.GG.SDD#43 --worker stoat-311 --json 2>&1)"; clfrc=$?
swf="$(printf '%s' "$clf" | jq -r '.statusWrite' 2>/dev/null)"
q_after="$(printf '%s' "$clf" | jq -r '.pendingBoardWrites' 2>/dev/null)"
colf="$("$ENGINE" ready --repo FS.GG.SDD --worker vole-418 2>/dev/null | jq -r '.[] | select(.number==43) | .status')"

# THE UNCHANGED QUEUE DEPTH IS WHAT SEPARATES THIS LEG FROM THE ONE ABOVE. Without it "failed" and
# "deferred" are indistinguishable from outside the receipt, and this would be the deferral-queue leg
# again wearing a different name — passing on the very mechanism it claims not to depend on.
[ "$clfrc" -eq 0 ] && [ "$swf" = "failed" ] && [ "$q_after" = "$q_before" ] && [ "$colf" = "Ready" ] \
  && ok "#1779: the fixture built the state — marker live, Status write FAILED, queue depth UNCHANGED at $q_after, column still '$colf'" \
  || bad "#1779: build a live claim whose Status write failed permanently" "rc=$clfrc statusWrite=$swf queue $q_before -> $q_after column=$colf: $clf"

ovf="$("$ENGINE" overlap FS.GG.SDD#44 --active --worker vole-418 2>&1)"; ovfrc=$?
[ "$ovfrc" -eq 6 ] && printf '%s' "$ovf" | grep -q 'OVERLAP' && printf '%s' "$ovf" | grep -q 'stoat-311' \
  && ok "#1779: a live claim whose Status write FAILED PERMANENTLY still COLLIDES" \
  || bad "#1779: a permanently-failed column write must not cost a false DISJOINT" "rc=$ovfrc: $ovf"

"$ENGINE" release FS.GG.SDD#43 --worker stoat-311 --status Ready >/dev/null 2>&1

# ---- .github#1779 leg 2: a live claim on an item that is NOT ON THE BOARD AT ALL ---------------------
#
# There is no row, so a row-derived candidate set cannot select it by construction — not by being fresher,
# and not by reading the queue. #46 is open in the repo, declares `src/Verify/**`, and the fixture's
# `projectItems` lookup answers with no node for it, so `boardWrite` returns `NotOnBoard`.
cln="$("$ENGINE" claim FS.GG.SDD#46 --worker heron-822 --json 2>&1)"; clnrc=$?
swn="$(printf '%s' "$cln" | jq -r '.statusWrite' 2>/dev/null)"
cvn="$(printf '%s' "$cln" | jq -r '.converged' 2>/dev/null)"
# READ OFF THE BOARD TOO, not only off the receipt: #46 must be absent from `ready`'s rows entirely.
row46="$("$ENGINE" ready --repo FS.GG.SDD --status any --worker vole-418 2>/dev/null | jq -r '[.[] | select(.number==46)] | length')"
[ "$clnrc" -eq 0 ] && [ "$swn" = "not-on-board" ] && [ "$cvn" = "false" ] && [ "$row46" = "0" ] \
  && ok "#1779: the fixture built the state — marker live on #46, statusWrite=not-on-board, NO board row" \
  || bad "#1779: build a live claim on an item that is not on the board" "rc=$clnrc statusWrite=$swn converged=$cvn rows=$row46: $cln"

ovn2="$("$ENGINE" overlap FS.GG.SDD#44 --active --worker vole-418 2>&1)"; ovn2rc=$?
[ "$ovn2rc" -eq 6 ] && printf '%s' "$ovn2" | grep -q 'OVERLAP' && printf '%s' "$ovn2" | grep -q 'heron-822' \
  && ok "#1779: a live claim on an item the board has NEVER LISTED still COLLIDES" \
  || bad "#1779: an off-board claim must not cost a false DISJOINT" "rc=$ovn2rc: $ovn2"

# ---- .github#1779 AC2: the API cost, MEASURED across the process boundary ---------------------------
#
# `.github#1086` got this same trade wrong by an order of magnitude by ESTIMATING it, and the first draft
# of #1779 declined the whole design over an estimate of "~74 REST marker reads per widen". So the fixture
# counts. `/_fixture/rest-reads` reports and resets; `/_fixture/board-reads` is the GraphQL board query.
# #2250 deliberately makes ONE cached-board scan so CLOSED, unstamped holders use the same candidate
# universe as the scheduler. Disable its 90-second scan cache for this one invocation: the assertion is a
# stable cold measurement (one page), not an accident of whatever an earlier e2e leg happened to cache.
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/rest-reads" >/dev/null   # reset the meter
br_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/board-reads" | jq -r '.boardReads')"
FSGG_COORD_SCAN_TTL_SEC=0 "$ENGINE" overlap FS.GG.SDD#44 --active --worker vole-418 >/dev/null 2>&1
meter="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/rest-reads")"
br_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/board-reads" | jq -r '.boardReads')"
rest_n="$(printf '%s' "$meter" | jq -r '.count')"
lists="$(printf '%s' "$meter" | jq -r '[.paths[] | select(test("^GET /repos/[^/]+/[^/]+/issues$"))] | length')"
markers="$(printf '%s' "$meter" | jq -r '[.paths[] | select(test("/comments$"))] | length')"
# THE NEGATIVE HALF, AND IT IS THE HALF THAT PINS THE CLAIM. A count alone would keep passing if the scan
# read some OTHER two issues' markers. #42 declares `src/Thing/**` and #99 declares `none`; both are open,
# both are candidates for a scan that reads first and filters second, and NEITHER can ever collide with
# `src/Verify/**`. Their markers must not be read at all.
noise="$(printf '%s' "$meter" | jq -r '[.paths[] | select(test("/(42|99)/comments$"))] | length')"

# The repo has FIVE open issues. Two of them declare `src/Verify/**` — #43 (whose marker was released
# above, so it reserves nothing) and #46 (held by heron-822) — and #44 is the subject. So: ONE issue list,
# THREE marker reads: the target verification plus one per shortlisted row rather than one per open row or
# ONE GraphQL board page. #2250 adds it to include the post-merge window; it must stay ONE page, while
# the REST shape remains token-first: the target verification, one open-issue list, then markers only for
# shortlisted rows. The old scan read a marker AND a body for every `In progress` row.
[ "$lists" = "1" ] && [ "$markers" = "3" ] && [ "$noise" = "0" ] && [ "$br_after" -eq "$((br_before + 1))" ] \
  && ok "#1779/#2250 AC2: overlap --active spent $rest_n REST calls (target verification, 1 issue list, and 1 marker per shortlisted row) and 1 GraphQL board read for closed-unstamped holders" \
  || bad "#1779 AC2: the measured cost is not the claimed cost" "rest=$rest_n lists=$lists markers=$markers noise=$noise boardReads $br_before -> $br_after: $meter"

"$ENGINE" release FS.GG.SDD#46 --worker heron-822 >/dev/null 2>&1
# #43 back into vole-418's hands, declaring `src/Verify/**`, for the AC5 legs below.
"$ENGINE" claim FS.GG.SDD#43 --worker otter-777 >/dev/null 2>&1

# ---- .github#1740 AC5: a NARROWING is never reported as having INTRODUCED a collision ----------------
# A token-subset names strictly fewer files, so it cannot introduce a collision — whatever the scan finds
# predates the command. Saying "the path update introduced a collision" over one sent the worker who filed
# #1740 looking for a mistake in their own narrowing instead of at the claim that was already there.
"$ENGINE" claim FS.GG.SDD#44 --worker vole-418 >/dev/null 2>&1
"$ENGINE" widen FS.GG.SDD#43 --worker otter-777 --paths 'docs/**' >/dev/null 2>&1
nr="$("$ENGINE" set-paths FS.GG.SDD#43 --worker otter-777 --paths 'src/Verify/**' 2>&1)"; nrrc=$?
if [ "$nrrc" -eq 6 ] \
   && printf '%s' "$nr" | grep -qi 'NARROWED' \
   && printf '%s' "$nr" | grep -qi 'cannot have introduced' \
   && ! printf '%s' "$nr" | grep -qi 'may or may not'; then
  ok "#1740 AC5: a narrowing that collides is reported as PRE-EXISTING, not as introduced"
else
  bad "#1740 AC5: a narrowing must not be blamed for a collision it cannot have caused" "rc=$nrrc: $nr"
fi

# THE COUNTER-EXAMPLE, and without it the leg above is satisfied by a constant. Same board, same live
# claim, same colliding token — but the declaration GROWS, so the tool must NOT say the overlap predates
# it. The negated grep above only means something because this leg proves the other sentence exists.
wn="$("$ENGINE" widen FS.GG.SDD#43 --worker otter-777 --paths 'src/Thing/**' 2>&1)"; wnrc=$?
if [ "$wnrc" -eq 6 ] \
   && printf '%s' "$wn" | grep -qi 'may or may not' \
   && ! printf '%s' "$wn" | grep -qi 'cannot have introduced'; then
  ok "#1740 AC5: a WIDENING that collides is NOT reported as pre-existing (the sentences differ)"
else
  bad "#1740 AC5: a widening must not borrow the narrowing's exoneration" "rc=$wnrc: $wn"
fi

# ---- #1569: read-contract ledger, first executable group -------------------------------------------
no_mutation() {
  local name="$1"; shift
  mark_contract "$name" "never-or-dry-run"
  case "$(contract_name "$name")" in
    reap|reconcile) mark_mode "$(contract_name "$name")" bare ;;
    flush) mark_mode flush dry-run ;;
  esac
  curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations" >/dev/null
  local output
  output="$("$@" 2>&1)"; local rc=$?
  local ledger
  ledger="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations")"
  if [ "$rc" -eq 0 ] && [ "$(printf '%s' "$ledger" | jq -r .count)" = 0 ]; then
    ok "#1569: $name is a valid never-write invocation (wire ledger empty)"
  else
    bad "#1569: $name must not mutate" "rc=$rc output=$output ledger=$ledger fixture=$(tail -n +2 \"$SRV_OUT\")"
  fi
}

# Some read commands return a non-green, but still fully evaluated, verdict.  Their distinct
# exit codes are part of the API (not parser refusals), so retain the wire assertion while making
# the expected verdict explicit.
no_mutation_verdict() {
  local name="$1" expected_rc="$2"; shift 2
  mark_contract "$name" "never-verdict"
  curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations" >/dev/null
  "$@" >/dev/null 2>&1; local rc=$?
  local ledger
  ledger="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations")"
  if [ "$rc" -eq "$expected_rc" ] && [ "$(printf '%s' "$ledger" | jq -r .count)" = 0 ]; then
    ok "#1569: $name reached its valid read verdict without mutating"
  else
    bad "#1569: $name must reach its declared read verdict without mutation" "rc=$rc expected=$expected_rc ledger=$ledger"
  fi
}

no_mutation "batch" run batch --repo FS.GG.SDD --text
no_mutation "board" run board
no_mutation "bootstrap" run bootstrap
no_mutation "budget" run budget
no_mutation "command-contract" run command-contract --json
no_mutation "driver" run driver --repo FS.GG.SDD --json
no_mutation "facts" run facts
no_mutation "field-id" run field-id Status
no_mutation "inbox" run inbox --repo FS.GG.SDD
no_mutation "issues" run issues FS.GG.SDD
no_mutation "option-id" run option-id Status Ready

# The first cut above deliberately started with the cache-shaped readers.  Keep extending the
# ledger with SUCCESSFUL invocations: a parser refusal costs no wire mutation too, but proves
# nothing about a command that actually reached its implementation (#266).
no_mutation "scan" run scan --repo FS.GG.SDD
no_mutation "ready" run ready --repo FS.GG.SDD
no_mutation "who" run who --repo FS.GG.SDD
no_mutation_verdict "landable" 4 run landable 500 --repo FS.GG.SDD
no_mutation "overlap" run overlap FS.GG.SDD#42 FS.GG.SDD#44
no_mutation "verify-paths" run verify-paths --pr 500 --repo FS.GG.SDD
no_mutation "item-id" run item-id FS.GG.SDD#42
no_mutation "body-edits" run body-edits FS.GG.SDD#42
no_mutation "graphql" run graphql meter
no_mutation "lint" run lint --repo .github
no_mutation "whoami" run whoami

# `intake apply` is the public receipt-bound create/projection transaction.  The fixture's mutation
# ledger classifies its REST issue POST separately from the two GraphQL board mutations, so this is a
# real executable driver rather than a parser-only mark in the command-contract inventory.
printf '%s\n' '{"schema":"fsgg.coord.intake/v1","id":"e2e-intake-wrong-repo-2134","owner":"FS-GG","repository":"FS.GG.SDD","title":"wrong checkout path","observed":"o","rootCause":"r","acceptance":"a","verification":"v","paths":["src/FS.GG.Coord.Core/**"],"class":"hardening","status":"Backlog","backlogReason":"not-yet-actionable","disposition":"create"}' >"$INTAKE_DRAFT"
wrong_path_out="$(run intake validate "$INTAKE_DRAFT" 2>&1)"; wrong_path_rc=$?
if [ "$wrong_path_rc" -ne 0 ] && printf '%s' "$wrong_path_out" | grep -q 'target repository FS-GG/FS.GG.SDD'; then
  ok "#2134: intake paths are validated in the draft target repository"
else
  bad "#2134: a path from the coordinator checkout must not validate for the target repository" "rc=$wrong_path_rc output=$wrong_path_out"
fi
printf '%s\n' '{"schema":"fsgg.coord.intake/v1","id":"e2e-intake-2134","owner":"FS-GG","repository":"FS.GG.SDD","title":"e2e intake transaction","observed":"o","rootCause":"r","acceptance":"a","verification":"v","paths":["src/Valid/**"],"class":"hardening","phase":"execution","severity":"high","status":"Backlog","backlogReason":"not-yet-actionable","disposition":"create"}' >"$INTAKE_DRAFT"
mark_contract "intake" "public-create-and-projection"
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations" >/dev/null
intake_out="$(run intake apply "$INTAKE_DRAFT" 2>&1)"; intake_rc=$?
intake_ledger="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations")"
intake_pending="$(run budget --json 2>/dev/null | jq -r .pendingBoardWrites)"
if [ "$intake_rc" -eq 0 ] \
  && printf '%s' "$intake_out" | jq -e --argjson pending "$intake_pending" '.kind == "applied" and .status == "Backlog" and .pendingWrites == $pending and (.fields | index("Class")) and (.fields | index("Phase")) and (.fields | index("Severity")) and .issueUrl == "https://github.com/FS-GG/FS.GG.SDD/issues/700" and .board.owner == "FS-GG" and .board.title == "Coordination" and .board.number == 12 and .board.id == "PVT_coord"' >/dev/null 2>&1 \
  && printf '%s' "$intake_ledger" | jq -e '
      .count == 3 and
      ([.requests[] | select(.kind == "rest-mutation" and .method == "POST" and .path == "/repos/FS-GG/FS.GG.SDD/issues")] | length == 1) and
      ([.requests[] | select(.kind == "graphql-mutation")] | length == 2)' >/dev/null 2>&1; then
  ok "#2134: intake applies one public create and converged board projection"
else
  bad "#2134: intake public transaction must create and project" "rc=$intake_rc output=$intake_out ledger=$intake_ledger"
fi

for projected_field in Class Phase Severity; do
  field_id="$(printf '%s' "$projected_field" | tr '[:upper:]' '[:lower:]')"
  printf '%s\n' "{\"schema\":\"fsgg.coord.intake/v1\",\"id\":\"e2e-intake-missing-$field_id-2134\",\"owner\":\"FS-GG\",\"repository\":\"FS.GG.SDD\",\"title\":\"missing $projected_field projection\",\"observed\":\"o\",\"rootCause\":\"r\",\"acceptance\":\"a\",\"verification\":\"v\",\"paths\":[\"src/Valid/**\"],\"class\":\"hardening\",\"phase\":\"execution\",\"severity\":\"high\",\"status\":\"Backlog\",\"backlogReason\":\"not-yet-actionable\",\"disposition\":\"create\"}" >"$INTAKE_DRAFT"
  curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/drop-intake-field/$projected_field" >/dev/null
  missing_field_out="$(run intake apply "$INTAKE_DRAFT" 2>&1)"; missing_field_rc=$?
  if [ "$missing_field_rc" -ne 0 ] && printf '%s' "$missing_field_out" | grep -q "fresh $projected_field readback"; then
    ok "#2134: missing $projected_field persistence fails the intake transaction"
  else
    bad "#2134: projectionFresh requires live $projected_field readback" "rc=$missing_field_rc output=$missing_field_out"
  fi
done

# Restore the canonical draft for the receipt-first queued retry below.
printf '%s\n' '{"schema":"fsgg.coord.intake/v1","id":"e2e-intake-2134","owner":"FS-GG","repository":"FS.GG.SDD","title":"e2e intake transaction","observed":"o","rootCause":"r","acceptance":"a","verification":"v","paths":["src/Valid/**"],"class":"hardening","phase":"execution","severity":"high","status":"Backlog","backlogReason":"not-yet-actionable","disposition":"create"}' >"$INTAKE_DRAFT"

printf '%s\n' '{"schema":"fsgg.coord.intake/v1","id":"e2e-intake-blocked-2134","owner":"FS-GG","repository":"FS.GG.SDD","title":"e2e blocked intake","observed":"o","rootCause":"r","acceptance":"a","verification":"v","paths":["src/Valid/**"],"class":"hardening","status":"Blocked","blockedBy":"FS-GG/FS.GG.SDD#42","disposition":"create"}' >"$INTAKE_BLOCKED_DRAFT"
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations" >/dev/null
blocked_out="$(run intake apply "$INTAKE_BLOCKED_DRAFT" 2>&1)"; blocked_rc=$?
blocked_ledger="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations")"
if [ "$blocked_rc" -eq 0 ] && printf '%s' "$blocked_out" | jq -e '.status == "Blocked" and .projectionFresh == true and (.fields | index("Blocked by"))' >/dev/null 2>&1 \
  && printf '%s' "$blocked_ledger" | jq -e '
      .count == 5 and
      .requests[0] == {"method":"POST","path":"/repos/FS-GG/FS.GG.SDD/issues","kind":"rest-mutation"} and
      .requests[1] == {"method":"POST","path":"/graphql","kind":"graphql-mutation"} and
      .requests[2].method == "POST" and .requests[2].kind == "rest-mutation" and
      (.requests[2].path | test("^/repos/FS-GG/FS[.]GG[.]SDD/issues/[0-9]+/comments$")) and
      .requests[3] == {"method":"POST","path":"/graphql","kind":"graphql-mutation"} and
      .requests[4].method == "DELETE" and .requests[4].kind == "rest-mutation" and
      (.requests[4].path | test("^/repos/FS-GG/FS[.]GG[.]SDD/issues/comments/[0-9]+$"))' >/dev/null 2>&1; then
  ok "#2134: Blocked intake creates and projects Status plus dependency coherently"
else
  bad "#2134: Blocked intake must project a coherent dependency" "rc=$blocked_rc output=$blocked_out ledger=$blocked_ledger"
fi

# A queued projection retry is receipt-first too: the first batch is refused and queued, the retry
# converges without a second issue POST, retires its queued pairs, and reports zero pending writes.
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/defer-next-field-write" >/dev/null
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations" >/dev/null
queued_out="$(run intake apply "$INTAKE_DRAFT" 2>&1)"; queued_rc=$?
run flush >/dev/null 2>&1
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations" >/dev/null
retry_out="$(run intake apply "$INTAKE_DRAFT" 2>&1)"; retry_rc=$?
queued_ledger="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations")"
pending_after_retry="$(run budget --json 2>/dev/null | jq -r .pendingBoardWrites)"
if [ "$queued_rc" -eq 75 ] && [ "$retry_rc" -eq 0 ] && [ "$pending_after_retry" = "0" ] \
  && printf '%s' "$retry_out" | jq -e '.kind == "applied" and .pendingWrites == 0' >/dev/null 2>&1 \
  && [ "$(printf '%s' "$queued_ledger" | jq '[.requests[] | select(.kind == "rest-mutation" and .path == "/repos/FS-GG/FS.GG.SDD/issues")] | length')" = "0" ]; then
  ok "#2134: queued intake projection retries to zero pending without a second create"
else
  bad "#2134: queued intake retry must converge receipt-first" "first=$queued_rc:$queued_out retry=$retry_rc:$retry_out pending=$pending_after_retry ledger=$queued_ledger"
fi

# `cycle` is another pure snapshot boundary.  Feed it a valid one-unit ledger, rather than a
# parser refusal, so command-contract coverage proves the command reaches its actual decision
# path while the fixture's HTTP mutation ledger remains empty.
printf '%s\n' \
  '{"sourceRevision":"fixture-source","units":[{"id":"first","providerCycleId":"roadmap-fixture-m1-first","dependencies":[],"completed":false,"evidence":[]}]}' \
  >"$CYCLE_SNAPSHOT"
no_mutation "cycle" run cycle inspect --snapshot "$CYCLE_SNAPSHOT" --json
printf '%s\n' \
  '{"sourceRevision":"fixture-source","units":[{"id":"a","providerCycleId":"roadmap-fixture-m1-a","dependencies":["b"],"completed":false,"evidence":[]},{"id":"b","providerCycleId":"roadmap-fixture-m2-b","dependencies":["a"],"completed":false,"evidence":[]}]}' \
  >"$CYCLE_SNAPSHOT"
cycle_bad="$(run cycle inspect --snapshot "$CYCLE_SNAPSHOT" --json 2>&1)"; cycle_bad_rc=$?
if [ "$cycle_bad_rc" -ne 0 ] && printf '%s' "$cycle_bad" | grep -q 'dependency cycle'; then
  ok "#2133: cycle production route rejects a cyclic ledger"
else
  bad "#2133: cycle production route must reject a cyclic ledger" "rc=$cycle_bad_rc output=$cycle_bad"
fi

# The provider boundary consumes exact artifact bytes, not provenance fields asserted by the cycle
# caller. Registration supplies the canonical cycle id used by all three provider envelopes.
printf '%s\n' \
  '{"sourceRevision":"base","units":[{"id":"2206-board-roster-closure","providerCycleId":"roadmap-cycle-ledger-m1-production","dependencies":[],"completed":false,"evidence":[]}],"executor":"worker","repository":".github","baseCommit":"base","liveCycles":[]}' \
  >"$CYCLE_SNAPSHOT"
cycle_register="$(run cycle register --snapshot "$CYCLE_SNAPSHOT" --json 2>&1)"; cycle_register_rc=$?
cycle_id="$(printf '%s' "$cycle_register" | jq -r '.cycleId // empty' 2>/dev/null)"
if [ "$cycle_register_rc" -eq 0 ] && [ -n "$cycle_id" ]; then
  ok "#2133: cycle production route emits the canonical registered cycle id"
else
  bad "#2133: cycle production route must register a canonical cycle" "rc=$cycle_register_rc output=$cycle_register"
fi

provider_root="$CYCLE_FIX/provider-root"
provider_cycle="roadmap-cycle-ledger-m1-production"
candidate_head="$(git -C "$REPO_ROOT" rev-parse HEAD)"
mkdir -p "$provider_root/reviews/roadmap" "$provider_root/feedback/audits"
ln -s "$REPO_ROOT/.agents" "$provider_root/.agents"
jq -n --arg cycle "$provider_cycle" --arg head "$candidate_head" \
  '{schema_version:3,cycle_id:$cycle,milestone:"M1 — production",critic:"fixture-critic",initial_reviewed_commit:$head,scope:["requirements","diff","tests","architecture","roadmap-evidence"],initial_verdict:"pass",repair_rounds:0,reviewed_commits:[$head],findings:[],confirmation:{reviewed_commit:$head,verdict:"pass","unresolved_blocker_major":[]},human_escalation:null,game_functionality:false,player_journeys:[],uncovered_functionality:[],entry_point_not_test_ownable:false,entry_point_not_test_ownable_reason:null}' \
  >"$provider_root/reviews/roadmap/$provider_cycle.json"
printf '%s\n' '---' 'feedbackSchema: 2' "cycle: $provider_cycle" '---' '## §1 Provenance and confidence' '- **activation:** active' '- **phases:** implementation-test-evidence, verify-ship-pr' '- **material events:** 0' '- **zero-event reason:** exercised phases produced no actionable findings' '## §2 Findings' >"$provider_root/feedback/$provider_cycle.md"
report_digest="$(sed 's/\r$//' "$provider_root/feedback/$provider_cycle.md" | sha256sum | cut -d' ' -f1)"
jq -n --arg report "feedback/$provider_cycle.md" --arg digest "$report_digest" '{auditSchema:1,report:$report,reportSha256:$digest,findings:[]}' >"$provider_root/feedback/audits/$provider_cycle.audit.json"

jq -n --arg cycle "$cycle_id" --arg providerCycle "$provider_cycle" --arg head "$candidate_head" --arg repo "$REPO_ROOT" --arg providerRoot "$provider_root" \
  '{sourceRevision:"base",units:[{id:"2206-board-roster-closure",providerCycleId:$providerCycle,dependencies:[],completed:false,evidence:[]}],cycle:{id:$cycle,unitId:"2206-board-roster-closure",executor:"worker",repository:".github",baseCommit:"base"},implementation:{rootPath:$repo,artifactPath:"readiness/2206-board-roster-closure/verify.json"},review:{rootPath:$providerRoot,artifactPath:("reviews/roadmap/"+$providerCycle+".json")},feedback:{rootPath:$providerRoot,artifactPath:("feedback/"+$providerCycle+".md"),auditPath:("feedback/audits/"+$providerCycle+".audit.json"),phases:["implementation-test-evidence","verify-ship-pr"]},evidence:{implementationHead:$head,reviewHead:$head,feedbackCycle:$cycle,feedbackActive:true,mergedPr:7,mergeHead:$head,evidencePaths:["evidence/report.json"],dispositions:["all-findings-disposed"]}}' >"$CYCLE_SNAPSHOT"
cycle_advance="$(run cycle advance --snapshot "$CYCLE_SNAPSHOT" --json 2>&1)"; cycle_advance_rc=$?
if [ "$cycle_advance_rc" -eq 0 ] && [ "$(printf '%s' "$cycle_advance" | jq -r .action 2>/dev/null)" = advance ]; then
  ok "#2133: cycle advance validates real SDD, critique, and feedback provider artifact shapes"
else
  bad "#2133: cycle advance must consume valid provider artifacts" "rc=$cycle_advance_rc output=$cycle_advance"
fi

cp "$provider_root/reviews/roadmap/$provider_cycle.json" "$CYCLE_FIX/valid-critique.json"
printf '%s\n' "{\"schema_version\":3,\"cycle_id\":\"$provider_cycle\",\"repair_rounds\":0,\"confirmation\":{\"reviewed_commit\":\"$candidate_head\",\"verdict\":\"pass\"},\"game_functionality\":false,\"player_journeys\":[],\"uncovered_functionality\":[]}" >"$provider_root/reviews/roadmap/$provider_cycle.json"
minimal_critique="$(run cycle advance --snapshot "$CYCLE_SNAPSHOT" --json 2>&1)"; minimal_critique_rc=$?
mv "$CYCLE_FIX/valid-critique.json" "$provider_root/reviews/roadmap/$provider_cycle.json"
if [ "$minimal_critique_rc" -ne 0 ] && printf '%s' "$minimal_critique" | grep -q 'provider validator refused'; then
  ok "#2133: production advance runs the exact schema-v3 critique validator against minimal lookalikes"
else
  bad "#2133: production advance must reject critique-shaped files the canonical validator rejects" "rc=$minimal_critique_rc output=$minimal_critique"
fi

cp "$provider_root/feedback/$provider_cycle.md" "$CYCLE_FIX/valid-feedback.md"
cp "$provider_root/feedback/audits/$provider_cycle.audit.json" "$CYCLE_FIX/valid-feedback.audit.json"
printf '%s\n' '---' 'feedbackSchema: 2' "cycle: $provider_cycle" '---' '## §1 Provenance and confidence' '- **activation:** active' '- **phases:** implementation-test-evidence, verify-ship-pr' '## §2 Findings' >"$provider_root/feedback/$provider_cycle.md"
report_digest="$(sed 's/\r$//' "$provider_root/feedback/$provider_cycle.md" | sha256sum | cut -d' ' -f1)"
jq -n --arg report "feedback/$provider_cycle.md" --arg digest "$report_digest" '{auditSchema:1,report:$report,reportSha256:$digest,findings:[]}' >"$provider_root/feedback/audits/$provider_cycle.audit.json"
minimal_feedback="$(run cycle advance --snapshot "$CYCLE_SNAPSHOT" --json 2>&1)"; minimal_feedback_rc=$?
mv "$CYCLE_FIX/valid-feedback.md" "$provider_root/feedback/$provider_cycle.md"
mv "$CYCLE_FIX/valid-feedback.audit.json" "$provider_root/feedback/audits/$provider_cycle.audit.json"
if [ "$minimal_feedback_rc" -ne 0 ] && printf '%s' "$minimal_feedback" | grep -q 'provider validator refused'; then
  ok "#2133: production advance runs the exact schema-v2 feedback validator against minimal lookalikes"
else
  bad "#2133: production advance must reject feedback-shaped files the canonical validator rejects" "rc=$minimal_feedback_rc output=$minimal_feedback"
fi

# The artifact root is data, never validator authority. A caller used to place no-op scripts under
# this alternate root and thereby turn the two canonical validator checks into unconditional passes.
malicious_root="$CYCLE_FIX/caller-validator-root"
mkdir -p "$malicious_root/.agents/skills/work-roadmap/scripts" "$malicious_root/reviews/roadmap" "$malicious_root/feedback/audits"
printf '%s\n' '#!/usr/bin/env python3' 'raise SystemExit(0)' >"$malicious_root/.agents/skills/work-roadmap/scripts/validate-critique-state.py"
printf '%s\n' '#!/usr/bin/env python3' 'raise SystemExit(0)' >"$malicious_root/.agents/skills/work-roadmap/scripts/validate-feedback-state.py"
printf '%s\n' "{\"schema_version\":3,\"cycle_id\":\"$provider_cycle\",\"repair_rounds\":0,\"confirmation\":{\"reviewed_commit\":\"$candidate_head\",\"verdict\":\"pass\"},\"game_functionality\":false,\"player_journeys\":[],\"uncovered_functionality\":[]}" >"$malicious_root/reviews/roadmap/$provider_cycle.json"
printf '%s\n' '---' 'feedbackSchema: 2' "cycle: $provider_cycle" '---' '## §1 Provenance and confidence' '- **activation:** active' '- **phases:** implementation-test-evidence, verify-ship-pr' '## §2 Findings' >"$malicious_root/feedback/$provider_cycle.md"
printf '%s\n' '{}' >"$malicious_root/feedback/audits/$provider_cycle.audit.json"

jq --arg root "$malicious_root" --arg cycle "$provider_cycle" '.review.rootPath=$root | .review.artifactPath=("reviews/roadmap/"+$cycle+".json")' "$CYCLE_SNAPSHOT" >"$CYCLE_FIX/substituted-critique-validator.json"
substituted_critique="$(run cycle advance --snapshot "$CYCLE_FIX/substituted-critique-validator.json" --json 2>&1)"; substituted_critique_rc=$?
if [ "$substituted_critique_rc" -ne 0 ] && printf '%s' "$substituted_critique" | grep -q 'provider validator refused'; then
  ok "#2133: caller-controlled artifact roots cannot substitute a no-op critique validator"
else
  bad "#2133: critique validator authority must come from the engine, not artifact rootPath" "rc=$substituted_critique_rc output=$substituted_critique"
fi

jq --arg root "$malicious_root" --arg cycle "$provider_cycle" '.feedback.rootPath=$root | .feedback.artifactPath=("feedback/"+$cycle+".md") | .feedback.auditPath=("feedback/audits/"+$cycle+".audit.json")' "$CYCLE_SNAPSHOT" >"$CYCLE_FIX/substituted-feedback-validator.json"
substituted_feedback="$(run cycle advance --snapshot "$CYCLE_FIX/substituted-feedback-validator.json" --json 2>&1)"; substituted_feedback_rc=$?
if [ "$substituted_feedback_rc" -ne 0 ] && printf '%s' "$substituted_feedback" | grep -q 'provider validator refused'; then
  ok "#2133: caller-controlled artifact roots cannot substitute a no-op feedback validator"
else
  bad "#2133: feedback validator authority must come from the engine, not artifact rootPath" "rc=$substituted_feedback_rc output=$substituted_feedback"
fi

trusted_critique_validator="$(dirname "$ENGINE")/provider-validators/validate-critique-state.py"
cp "$trusted_critique_validator" "$CYCLE_FIX/trusted-critique-validator.py"
printf '%s\n' '# unsupported validator mutation' >>"$trusted_critique_validator"
tampered_validator="$(run cycle advance --snapshot "$CYCLE_SNAPSHOT" --json 2>&1)"; tampered_validator_rc=$?
mv "$CYCLE_FIX/trusted-critique-validator.py" "$trusted_critique_validator"
if [ "$tampered_validator_rc" -ne 0 ] && printf '%s' "$tampered_validator" | grep -q 'validator identity is unsupported'; then
  ok "#2133: engine-shipped provider validator bytes are bound to a supported identity digest"
else
  bad "#2133: an unpinned engine-side validator replacement must fail closed" "rc=$tampered_validator_rc output=$tampered_validator"
fi

printf '%s\n' "{\"schema\":\"fsgg.sdd.verify/1\",\"provider\":\"fsgg-sdd\",\"workId\":\"2206-board-roster-closure\",\"cycleId\":\"$cycle_id\",\"sourceRevision\":\"base\",\"candidateHead\":\"$candidate_head\",\"verdict\":\"pass\",\"round\":0,\"playerJourney\":null,\"generator\":{\"id\":\"FS.GG.SDD.Artifacts\",\"version\":\"1.0.0\"}}" >"$CYCLE_FIX/forged-sdd.json"
jq --arg root "$CYCLE_FIX" '.implementation.rootPath=$root | .implementation.artifactPath="forged-sdd.json"' "$CYCLE_SNAPSHOT" >"$CYCLE_FIX/forged-advance.json"
forged_advance="$(run cycle advance --snapshot "$CYCLE_FIX/forged-advance.json" --json 2>&1)"; forged_advance_rc=$?
if [ "$forged_advance_rc" -ne 0 ] && printf '%s' "$forged_advance" | grep -q 'SDD verification artifact must be'; then
  ok "#2133: production advance rejects supported-looking caller-authored provider envelopes"
else
  bad "#2133: production advance must reject invented provider provenance" "rc=$forged_advance_rc output=$forged_advance"
fi

# .github#2465: the accepted `fsgg-sdd verify` `toolVersion` is an explicitly-vetted LIST, not a bare
# equality against one hardcoded literal — and it must still fail closed on an unvetted version, with
# a message that names the exact reported version rather than a six-way conflated "identity, version,
# command, or work binding is unsupported" a caller had to reverse-engineer via unrelated #2133 red.
# A fake `fsgg-sdd` ahead on PATH reports an otherwise well-formed but unvetted toolVersion, so this
# isolates the version check from the identity/command/work-binding checks it used to share a branch with.
fake_sdd_dir="$CYCLE_FIX/fake-sdd-bin"
mkdir -p "$fake_sdd_dir"
cat >"$fake_sdd_dir/fsgg-sdd" <<'FAKE_SDD'
#!/usr/bin/env bash
cat <<'JSON'
{"schema":"fsgg.sdd.verify/1","toolVersion":"9.9.9","command":{"name":"verify"},"context":{"workId":"2206-board-roster-closure"},"coherent":true,"outcome":"noChange"}
JSON
FAKE_SDD
chmod +x "$fake_sdd_dir/fsgg-sdd"
unvetted_version="$(PATH="$fake_sdd_dir:$PATH" run cycle advance --snapshot "$CYCLE_SNAPSHOT" --json 2>&1)"; unvetted_version_rc=$?
if [ "$unvetted_version_rc" -ne 0 ] && printf '%s' "$unvetted_version" | grep -q 'fsgg-sdd validator toolVersion 9.9.9 is not vetted'; then
  ok "#2465: an unvetted fsgg-sdd validator toolVersion fails closed and names the reported version"
else
  bad "#2465: an unvetted fsgg-sdd validator toolVersion must fail closed and name the reported version" "rc=$unvetted_version_rc output=$unvetted_version"
fi

jq '{sourceRevision,units,cycle,evidence}' "$CYCLE_SNAPSHOT" >"$CYCLE_FIX/update.json"
cycle_update="$(run cycle update --snapshot "$CYCLE_FIX/update.json" --json 2>&1)"; cycle_update_rc=$?
update_receipt="$(printf '%s' "$cycle_update" | jq -c '.updateReceipt // empty' 2>/dev/null)"
if [ "$cycle_update_rc" -eq 0 ] && [ -n "$update_receipt" ] && [ "$(printf '%s' "$update_receipt" | jq -r .schema)" = 'fsgg.coord.cycle-update/1' ]; then
  ok "#2133: production update emits a source/head/evidence/disposition-bound receipt"
else
  bad "#2133: production update must emit its verifiable receipt" "rc=$cycle_update_rc output=$cycle_update"
fi

jq --argjson receipt "$update_receipt" '.units[0].completed=true | .units[0].evidence=["evidence/report.json"] | .acceptedCycles=[.cycle] | .guardedUpdates=[$receipt] | .rollupCycleIds=[.cycle.id] | del(.cycle,.evidence)' "$CYCLE_FIX/update.json" >"$CYCLE_FIX/complete.json"
cycle_complete="$(run cycle complete --snapshot "$CYCLE_FIX/complete.json" --json 2>&1)"; cycle_complete_rc=$?
if [ "$cycle_complete_rc" -eq 0 ] && [ "$(printf '%s' "$cycle_complete" | jq -r .action 2>/dev/null)" = complete ]; then
  ok "#2133: production complete accepts the exact emitted guarded-update receipt"
else
  bad "#2133: production complete must verify the emitted update receipt" "rc=$cycle_complete_rc output=$cycle_complete"
fi

rm -f "$FSGG_CYCLE_JOURNAL"
cp "$CYCLE_FIX/complete.json" "$CYCLE_FIX/invented-complete.json"
invented_complete="$(run cycle complete --snapshot "$CYCLE_FIX/invented-complete.json" --json 2>&1)"; invented_complete_rc=$?
if [ "$invented_complete_rc" -ne 0 ] && printf '%s' "$invented_complete" | grep -q 'durable update journal'; then
  ok "#2133: production complete rejects a full recomputed receipt without durable update history"
else
  bad "#2133: production complete must reject invented update receipts" "output=$invented_complete"
fi

configured_journal="$FSGG_CYCLE_JOURNAL"
unset FSGG_CYCLE_JOURNAL
default_journal="$(git -C "$REPO_ROOT" rev-parse --git-path fsgg-cycle-journal.json)"
rm -f "$default_journal"
default_update="$(run cycle update --snapshot "$CYCLE_FIX/update.json" --json 2>&1)"; default_update_rc=$?
if [ "$default_update_rc" -eq 0 ] && [ -f "$default_journal" ] && [ "$(printf '%s' "$default_update" | jq -r .action 2>/dev/null)" = update ]; then
  ok "#2133: production update resolves its default durable journal through a linked worktree gitdir"
else
  bad "#2133: default update journal must work when .git is a linked-worktree file" "rc=$default_update_rc journal=$default_journal output=$default_update"
fi
rm -f "$default_journal"
export FSGG_CYCLE_JOURNAL="$configured_journal"

# `followup add` is intentionally a local-file write, not a shared-board write.  It is a valid
# (and therefore non-vacuous) driver for the command-contract row; `list` then proves the add
# reached the command rather than being accepted and ignored.
no_mutation "followup" run followup add FS.GG.SDD#42
followups="$(run followup list 2>&1)"; followups_rc=$?
if [ "$followups_rc" -eq 0 ] && printf '%s' "$followups" | grep -q 'FS.GG.SDD#42'; then
  ok "#1569: followup's local driver completed successfully"
else
  bad "#1569: followup's local driver must be valid" "rc=$followups_rc: $followups"
fi

# These two pure commands consume the real snapshot that `scan` emitted.  Supplying that
# snapshot, rather than malformed JSON, makes an empty wire ledger evidence about the execution
# path rather than an accidental parser refusal.
no_mutation_snapshot() {
  local name="$1" command="$2"
  mark_contract "$name" never
  curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations" >/dev/null
  snapshot="$(run scan --repo FS.GG.SDD)"; snapshot_rc=$?
  result="$(printf '%s' "$snapshot" | "$ENGINE" "$command" --worker vole-418 2>&1)"; result_rc=$?
  ledger="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations")"
  if [ "$snapshot_rc" -eq 0 ] && [ "$result_rc" -eq 0 ] && [ "$(printf '%s' "$ledger" | jq -r .count)" = 0 ]; then
    ok "#1569: $name is a valid never-write invocation (wire ledger empty)"
  else
    bad "#1569: $name must execute without mutating" "scan_rc=$snapshot_rc rc=$result_rc output=$result ledger=$ledger"
  fi
}
no_mutation_snapshot "decide" decide
no_mutation_snapshot "lanes" lanes

# `delivery --snapshot` is the hermetic no-IO boundary for the lifecycle decision.  Exercise both
# contract polarities against a complete accepted/guarded-land fact set: `--apply` is meaningful only
# for the live adapter, so the snapshot boundary must still return the same decision without issuing a
# merge.  This makes the newly advertised command and its conditional argv surface executable without
# inventing a second HTTP fixture solely for the pure adapter.
delivery_snapshot='{"freshness":{"itemRef":"FS-GG/.github#2131","claimGeneration":"fixture-claim","executor":"vole-418","branch":"item/2131-fixture","worktree":"/tmp/fixture","pullRequest":42,"headSha":"fixture-head","declaredPaths":["tests/coord-engine-e2e"],"boardState":"In review"},"itemBranchCanonical":true,"closingLinkageCanonical":true,"pathsVerified":true,"inReview":true,"review":{"markerValid":true,"criticIdentity":"curlew-ced5","headSha":"fixture-head","rounds":[1],"repairPhase":false,"checksGreen":true,"hostAccepted":true,"routeNotMeaningfulReason":"hermetic fixture"},"landable":true,"merged":false,"mergeReachable":false,"issueClosed":false,"boardDone":false,"claimReleased":false,"pendingWrites":0,"cleanupEligible":false,"obligationsDeclared":true,"obligations":[],"parkedReason":null}'
mark_contract "delivery" "snapshot-conditional-driver"
for delivery_mode in bare apply; do
  curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations" >/dev/null
  if [ "$delivery_mode" = apply ]; then
    delivery_out="$(printf '%s' "$delivery_snapshot" | run delivery --snapshot /dev/stdin --apply --json 2>&1)"; delivery_rc=$?
  else
    delivery_out="$(printf '%s' "$delivery_snapshot" | run delivery --snapshot /dev/stdin --json 2>&1)"; delivery_rc=$?
  fi
  delivery_ledger="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations")"
  if [ "$delivery_rc" -eq 0 ] \
     && printf '%s' "$delivery_out" | jq -e '.verdict == "next" and .action == "guardedLand"' >/dev/null \
     && [ "$(printf '%s' "$delivery_ledger" | jq -r .count)" = 0 ]; then
    mark_mode delivery "$delivery_mode"
    ok "#2131: delivery --snapshot $delivery_mode executes its lifecycle decision without a wire mutation"
  else
    bad "#2131: delivery --snapshot $delivery_mode must execute without a wire mutation" "rc=$delivery_rc output=$delivery_out ledger=$delivery_ledger"
  fi
done

# .github#2871 — live delivery crosses closure with two different representations available:
# the closed board projection deliberately has no body, while the authoritative REST issue still
# carries `Paths: src/Closed/**`. The production command must use the latter and continue
# implementation. Reverting LiveHandlers to `candidate.Item.TouchSet` makes this exact leg return the
# false `declared paths were never declared` no-verdict seen in the release cut.
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/activate-issue/49" >/dev/null
curl -fsS -X POST -H 'Content-Type: application/json' \
  -d '{"body":"<!-- fsgg:claim worker=vole-418 lease=120 -->"}' \
  "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/49/comments" >/dev/null
closed_delivery="$(run delivery FS.GG.SDD#49 --json 2>&1)"; closed_delivery_rc=$?
if [ "$closed_delivery_rc" -eq 0 ] \
   && printf '%s' "$closed_delivery" | jq -e '.verdict == "next" and .stage == "implementation" and .action == "continueImplementation"' >/dev/null; then
  ok ".github#2871: closed-item delivery reads authoritative Paths instead of the empty board projection"
else
  bad ".github#2871: closure must not erase delivery Paths" "rc=$closed_delivery_rc output=$closed_delivery"
fi

# An unread authority is not an absent declaration. Arm one real REST failure and require the typed
# unread reason; the old projection reader never makes this request and therefore emits the wrong
# no-Paths diagnosis, so this is also the producer-route reachability control.
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/fail-next-issue-body/49" >/dev/null
unread_delivery="$(run delivery FS.GG.SDD#49 --json 2>&1)"; unread_delivery_rc=$?
if [ "$unread_delivery_rc" -ne 0 ] \
   && printf '%s' "$unread_delivery" | jq -e '.verdict == "noVerdict" and (.reason | contains("declared paths were not read")) and (.reason | contains("authoritative issue body unreadable"))' >/dev/null; then
  ok ".github#2871: unread authoritative Paths fail closed without blaming the issue body"
else
  bad ".github#2871: unread Paths must remain distinct from undeclared Paths" "rc=$unread_delivery_rc output=$unread_delivery"
fi

# Positive negative-control: once the authoritative body is successfully read and really contains no
# declaration, preserve the established undeclared diagnosis. This proves the repair did not merely
# suppress that refusal.
curl -fsS -X PATCH -H 'Content-Type: application/json' \
  -d '{"body":"A genuinely undeclared closed delivery item."}' \
  "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/49" >/dev/null
undeclared_delivery="$(run delivery FS.GG.SDD#49 --json 2>&1)"; undeclared_delivery_rc=$?
if [ "$undeclared_delivery_rc" -ne 0 ] \
   && printf '%s' "$undeclared_delivery" | jq -e '.verdict == "noVerdict" and (.reason | contains("declared paths were never declared (no Paths: line)"))' >/dev/null; then
  ok ".github#2871: a readable body with no Paths retains the undeclared diagnosis"
else
  bad ".github#2871: genuine undeclared Paths must still refuse" "rc=$undeclared_delivery_rc output=$undeclared_delivery"
fi
curl -fsS -X PATCH -H 'Content-Type: application/json' \
  -d '{"body":"A closed delivery item.\n\nPaths: src/Closed/**"}' \
  "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/49" >/dev/null

# `review --snapshot` (.github#2175) is the hermetic no-IO boundary for the resumable review/repair
# protocol, unconditionally `writes: never` (unlike `delivery`, it has no `--apply` arm at all), so one
# invocation against a real snapshot, checked against the wire ledger exactly as `no_mutation` checks
# any other never-write verb, is the whole of its contract driver.
mark_contract "review" "never-or-dry-run"
review_snapshot='{"binding":{"itemRef":"FS-GG/.github#2175","pr":42,"headSha":"fixture-head","claimGeneration":"fixture-claim","implementerIdentity":"vole-418","phase":"ordinary","round":1},"facts":{"comments":[],"checks":"pending","repairPhaseGranted":null,"repairRouteAvailable":true}}'
review_snapshot_file="$(mktemp)"
printf '%s' "$review_snapshot" >"$review_snapshot_file"
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations" >/dev/null
review_out="$(run review --snapshot "$review_snapshot_file" --json 2>&1)"; review_rc=$?
review_ledger="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations")"
rm -f "$review_snapshot_file"
if [ "$review_rc" -eq 0 ] \
   && printf '%s' "$review_out" | jq -e '.verdict == "next" and .state == "awaitingInitialReview" and .action == "dispatchCritic"' >/dev/null \
   && [ "$(printf '%s' "$review_ledger" | jq -r .count)" = 0 ]; then
  ok "#2175: review --snapshot reaches its typed state/action without a wire mutation"
else
  bad "#2175: review --snapshot must execute without mutating" "rc=$review_rc output=$review_out ledger=$review_ledger"
fi

# #2207 adds an optional delivery diagnostic.  The established snapshot producer above predates it,
# so omission must retain the guarded-land decision.  Invert the subject by supplying an empty value:
# it must refuse rather than silently treating an invalid observed diagnostic as absent.
delivery_empty_problem="${delivery_snapshot/\"landable\"/\"reviewProblem\":\"\",\"landable\"}"
delivery_problem_out="$(printf '%s' "$delivery_empty_problem" | run delivery --snapshot /dev/stdin --json 2>&1)"; delivery_problem_rc=$?
if [ "$delivery_problem_rc" -ne 0 ] && printf '%s' "$delivery_problem_out" | grep -F 'reviewProblem' >/dev/null; then
  ok "#2207: an omitted legacy reviewProblem defaults to none, while an empty supplied diagnostic refuses"
else
  bad "#2207: an empty supplied reviewProblem must not masquerade as an omitted legacy field" "rc=$delivery_problem_rc output=$delivery_problem_out"
fi

# `predicate` is local too, but its successful arm needs a registry and its owning manifest.
# The dedicated predicate suite owns the broader truth table; this small fixture merely makes the
# command-contract driver's no-wire claim executable here.
mkdir -p "$PREDICATE_FIX/registry" "$PREDICATE_FIX/.repos/FS.GG.Game/template/skill-manifest"
printf '%s\n' \
  'schemaVersion: 1' \
  'skills:' \
  '  - { id: contract-probe, scope: product, owner: fs-gg-game, source: x, sha256: x, mirrored: false }' \
  >"$PREDICATE_FIX/registry/skills.yml"
printf '%s\n' '{"skills":[{"id":"contract-probe","mirrored":false}]}' \
  >"$PREDICATE_FIX/.repos/FS.GG.Game/template/skill-manifest/skill-manifest.json"
no_mutation_verdict "predicate" 4 env FSGG_REGISTRY="$PREDICATE_FIX/registry/skills.yml" FSGG_REPOS_ROOT="$PREDICATE_FIX/.repos" "$ENGINE" predicate contract-probe mirrored false --worker vole-418

# `diff-audit` reads two immutable objects and declared paths from this checkout.  This drives the real
# parser's item-body declaration shape: `-` means no prior receipt, and the standalone declaration makes
# the zero-occurrence receipt mandatory (typed red, never a parser refusal).
printf '%s\n' 'Bulk rename: true' >"$PREDICATE_FIX/item-body.md"
no_mutation_verdict "diff-audit" 3 "$ENGINE" diff-audit HEAD HEAD oldName newName - "$PREDICATE_FIX/item-body.md" --repo "$REPO_ROOT" --paths src/FS.GG.Coord.Core/SemanticDiff.fs

# `packet validate` (.github#2737) reads ONE local file and decides. It is the finder's pre-flight over
# an `fsgg.coord.finding-packet/v1` document, and by DEC-001 it sits outside the write path entirely:
# it can refuse no post, so a wire mutation from it would be a defect rather than a design choice.
# The packet below is `.github#2691` comment 5304198465, lifted field by field — a real filed packet,
# so the green arm proves the validator ACCEPTS as well as refuses.
printf '%s\n' '{"schema":"fsgg.coord.finding-packet/v1","surface":"src/FS.GG.Coord.Cli/DeliveryRouteApplication.fs","cause":{"established":"the verb was never wired into the command surface"},"redToday":{"found":"nothing dispatches to DeliveryRouteApplication.run"},"derivedBy":{"notSearched":"an adjudicator should check whether a gate already derives this"},"classRow":{"notSearched":"this may be evidence on a wiring/coverage class row"},"whyNotHere":"no claim and no lane; the fix is engine source the pass did not declare","paths":["src/FS.GG.Coord.Cli/Options.fs"],"finder":"merlin-efd3"}' >"$PREDICATE_FIX/finding-packet.json"
no_mutation "packet" "$ENGINE" packet validate "$PREDICATE_FIX/finding-packet.json"

# ...AND THE NO-WIRE CONTRACT GETS ITS OWN LEG, BECAUSE THE SHARED HELPER'S RED IS AMBIGUOUS (I7, PR
# #2751 round 0). `no_mutation` conjoins two independent legs — `rc -eq 0` and an empty wire ledger —
# under one message that says only "must not mutate". So an invocation that merely exits non-zero reds a
# line asserting a mutation nobody made, and the red does not identify which contract broke. The helper
# is pre-existing and has ~20 sibling call sites, so it is not rewritten here; instead THIS row's own
# claim is stated as a leg whose SOLE predicate is the ledger. It is deliberately not conditioned on the
# exit code — that half is already asserted by the line above and by `PacketCliTests` — so a red here
# can mean exactly one thing: `packet validate` reached the wire.
#
# VERIFIED AS AN INSTRUMENT RATHER THAN ASSUMED, AND THE FIRST ATTEMPT AT THAT IS WHY THIS PARAGRAPH IS
# LONG. Substituting `run heartbeat FS.GG.SDD#42` for the subject left this leg GREEN — not because the
# predicate is dead, but because by this point in the file vole-418's lease has expired, so heartbeat
# REFUSES before the transport: `rc=1`, `count: 0`, no request recorded. A mutation that reaches the
# branch but never its boundary proves nothing. Substituting `run say FS.GG.SDD#43 --to kite-461` — a
# verb that actually posts — reds it: `count: 1` with `POST /repos/FS-GG/FS.GG.SDD/issues/43/comments`
# recorded, at `rc=0`. Both halves matter: the red arrives on a ZERO exit code, so it cannot have come
# from an exit-status leg, which is precisely the ambiguity in `no_mutation` this leg exists to remove.
# That heartbeat run is also the ambiguity itself, executed: `no_mutation` would have reported "must not
# mutate" over an invocation whose wire ledger was empty.
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations" >/dev/null
packet_out="$("$ENGINE" packet validate "$PREDICATE_FIX/finding-packet.json" 2>&1)"; packet_rc=$?
packet_ledger="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations")"
if [ "$(printf '%s' "$packet_ledger" | jq -r .count)" = 0 ]; then
  ok "#2737: packet validate issued ZERO wire mutations — the sole predicate of this leg, so its red names mutation and nothing else"
else
  bad "#2737: packet validate must not reach the wire at all (DEC-001: it can refuse no post)" \
      "ledger=$packet_ledger rc=$packet_rc output=$packet_out"
fi

# `--apply` is a valid alternative argv shape even when this fixture finds no safe repair/reap.
# Do not call a non-zero no-op (or a parser refusal) evidence: both commands must complete their
# read/decision path.  Their bare arms above are the no-write proofs; these establish the gate's
# other spelling as executable.
valid_driver() {
  local name="$1"; shift
  mark_contract "$name" "argv-cannot-say"
  curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations" >/dev/null
  "$@" >/dev/null 2>&1; local rc=$?
  if [ "$rc" -eq 0 ]; then
    ok "#1569: $name is a valid driver"
  else
    bad "#1569: $name must not be a parser refusal" "rc=$rc"
  fi
}

# `next` is the one conditional row whose mutation cannot be inferred from argv.  The exemption
# is taken from the emitted field, so a renamed/new argvCannotSay row is not silently omitted.
next_reason="$(run command-contract --json | jq -r '.commands[] | select(.name == "next") | .writesWhen.argvCannotSay // empty')"
if [ -n "$next_reason" ]; then
  valid_driver "next (argvCannotSay: $next_reason)" run next --repo FS.GG.SDD
  mark_mode next argvCannotSay
else
  bad "#1569: next must declare its argvCannotSay exemption" "command-contract omitted writesWhen.argvCannotSay"
fi

# `flush` is the opposite polarity from the two `--apply` commands: dry-run is the read arm.
# A deferred field write supplies real pending work, so neither invocation is being accepted over
# an empty, unexercised queue.  The normal arm must then land that queued write.
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/defer-next-field-write" >/dev/null
run set-field FS.GG.SDD#42 Status Ready >/dev/null 2>&1
no_mutation "flush --dry-run" run flush --dry-run
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations" >/dev/null
flush_out="$(run flush 2>&1)"; flush_rc=$?
flush_ledger="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations")"
mark_mode flush apply
if [ "$flush_rc" -eq 0 ] && [ "$(printf '%s' "$flush_ledger" | jq -r .count)" -gt 0 ]; then
  ok "#1569: flush without --dry-run mutates the queued board write"
else
  bad "#1569: flush without --dry-run must mutate pending work" "rc=$flush_rc output=$flush_out ledger=$flush_ledger"
fi

# The older write suite proves the lock, marker, Status, path, child, say, and done verbs in
# their dedicated state transitions.  These are the three remaining `always` rows without an
# explicit valid driver in the command-contract ledger.  Each starts from an empty wire ledger:
# success alone is insufficient because an idempotent/no-op implementation could still return 0.
must_mutate() {
  local name="$1"; shift
  case "$(contract_name "$name")" in
    reap|reconcile) mark_mode "$(contract_name "$name")" apply ;;
    *) mark_contract "$name" always ;;
  esac
  curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations" >/dev/null
  mutation_out="$("$@" 2>&1)"; mutation_rc=$?
  mutation_ledger="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations")"
  if [ "$mutation_rc" -eq 0 ] && [ "$(printf '%s' "$mutation_ledger" | jq -r .count)" -gt 0 ]; then
    ok "#1569: $name is a valid always-write invocation (wire ledger observed a mutation)"
  else
    bad "#1569: $name must execute and mutate" "rc=$mutation_rc output=$mutation_out ledger=$mutation_ledger"
  fi
}

# `self-host` advertises a write because its `record` arm appends stable-engine-authorized bootstrap
# evidence to the accountable item. Mint the receipt with the candidate's real bytes/version, then
# prove the record arm reaches the fixture comment thread.
self_host_proposal="$CYCLE_FIX/self-host-proposal.json"
self_host_snapshot="$CYCLE_FIX/self-host-snapshot.json"
self_host_receipt="$CYCLE_FIX/self-host-receipt.txt"
printf '%s\n' '{}' >"$self_host_snapshot"
printf '%s\n' '{"baseSha":"fixture-base","candidateHeadSha":"fixture-head","sharedRefusal":"fixture shared engine refused a relocated decision boundary","reason":"relocated-decision-boundary","evidence":{"build":"fixture-build","unit":"fixture-unit","focusedProductionRoute":"fixture-route","provenance":"fixture-provenance","inversion":"fixture-inversion"},"candidateDecisionKey":"fixture-decision","candidateActionKey":"fixture-action","hostAcceptance":{"actor":"host/ron000","acceptedAt":"2026-08-22T12:00:00Z"}}' >"$self_host_proposal"
self_host_mint="$("$ENGINE" self-host mint "$self_host_proposal" "$ENGINE" "$self_host_snapshot" "$self_host_receipt" 2>&1)"; self_host_mint_rc=$?
if [ "$self_host_mint_rc" -eq 0 ] && grep -q 'SELF-HOST-RECEIPT' <<<"$self_host_mint"; then
  must_mutate "self-host" run self-host record FS.GG.SDD#42 "$self_host_receipt"
else
  mark_contract "self-host" "record-driver-mint-failed"
  bad "#1569: self-host receipt must mint before its record mutation" "rc=$self_host_mint_rc output=$self_host_mint"
fi

# #46 is deliberately off-board, so `add` cannot be satisfied by its idempotent existing-item arm.
must_mutate "add" run add FS.GG.SDD#46
# Use a new worker: the fixture's normal driver holds an item, and the one-item-per-worker guard
# would be a parser-shaped false driver rather than the scheduling-and-claim path being tested.
"$ENGINE" release FS.GG.SDD#43 --worker otter-777 >/dev/null 2>&1
# The preceding collision legs deliberately leave #43 at the claim column while releasing its marker.
# `take` must start from a schedulable world, not from an unowned `In progress` projection that blocks
# #42's overlapping path and turns its mutation proof into EX_NONE.  Establish and read back the one
# precondition here; the `must_mutate` helper below still proves that `take` itself executes and writes.
take_reset="$("$ENGINE" set-field FS.GG.SDD#43 Status Ready --worker ledger-take 2>&1)"; take_reset_rc=$?
take_reset_status="$(run ready --repo FS.GG.SDD --worker ledger-take 2>/dev/null | jq -r '.[] | select(.number == 43) | .status')"
take_reset_comments="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments")"
[ "$take_reset_rc" -eq 0 ] && [ "$take_reset_status" = "Ready" ] && ! printf '%s' "$take_reset_comments" | grep -q 'fsgg:claim' \
  && ok "#1569: take precondition is a Ready #43 with no claim marker" \
  || bad "#1569: take must start after the collision fixture cleanup" "rc=$take_reset_rc status=$take_reset_status output=$take_reset comments=$take_reset_comments"
must_mutate "take" "$ENGINE" take --repo FS.GG.SDD --worker ledger-take
# A room is a net-new REST issue POST followed by body PATCHes on every member.  The fixture returns
# the created issue number, letting the real command finish rather than treating the POST as enough.
must_mutate "room open" "$ENGINE" room open --over FS.GG.SDD#42,FS.GG.SDD#43 --worker ledger-room

# CONDITIONAL, BOTH POLARITIES.  These controls are intentionally adjacent: the bare form must
# inspect the SAME actionable state without a wire mutation; `--apply` then changes that state.
# A parser refusal cannot satisfy either half because both commands have to return green.

# A real claim first creates the marker; only then does the fixture age it.  This keeps every
# ordinary CAS leg on real time while giving reap an unambiguously stale lock to report and collect.
"$ENGINE" claim FS.GG.SDD#42 --worker stale-reap >/dev/null 2>&1
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/expire-claim/42" >/dev/null
no_mutation "reap (bare, expired claim)" "$ENGINE" reap --repo FS.GG.SDD --worker stale-reap
must_mutate "reap --apply (expired claim)" "$ENGINE" reap --repo FS.GG.SDD --apply --worker stale-reap

# #45 is CLOSED while its board projection is still Ready: the typed CLOSED-ISSUE-NOT-DONE
# chore is mechanically safe.  Bare reconcile reports it; --apply writes Status=Done.
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/reset-reconcile-45" >/dev/null
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/arm-reconcile-45-projection" >/dev/null
no_mutation "reconcile (bare, actionable chore)" "$ENGINE" reconcile --repo FS.GG.SDD --worker reconcile-probe
must_mutate "reconcile --apply (actionable chore)" "$ENGINE" reconcile --repo FS.GG.SDD --apply --worker reconcile-probe

# .github#2157 — the single lifecycle reducer's blocker-clear result is a coupled repair. The JSON receipt
# names BOTH intended writes and BOTH fresh observations. The Blocked-by lease POST/DELETE must bracket
# the one GraphQL batch, and the durable lifecycle receipt is written only after the lease is released.
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/reset-reconcile-47" >/dev/null
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations" >/dev/null
blocker_cleared="$("$ENGINE" reconcile --repo FS.GG.SDD --apply --worker reconcile-probe --json)"; blocker_cleared_rc=$?
blocker_mutations="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations")"
printf '%s' "$blocker_cleared" | jq -e '
  map(select(.subject == "FS.GG.SDD#47")) | length == 1 and
  .[0].outcome == "written" and
  .[0].writes == [{"field":"Status","value":"Ready"},{"field":"Blocked by","value":""}] and
  .[0].observed == [{"field":"Status","value":"Ready"},{"field":"Blocked by","value":""}]' >/dev/null 2>&1 \
  && [ "$blocker_cleared_rc" -eq 0 ] \
  && printf '%s' "$blocker_mutations" | jq -e '
      .count == 4 and
      .requests[0] == {"method":"POST","path":"/repos/FS-GG/FS.GG.SDD/issues/47/comments","kind":"rest-mutation"} and
      .requests[1] == {"method":"POST","path":"/graphql","kind":"graphql-mutation"} and
      .requests[2].method == "DELETE" and .requests[2].kind == "rest-mutation" and
      (.requests[2].path | test("^/repos/FS-GG/FS[.]GG[.]SDD/issues/comments/[0-9]+$")) and
      .requests[3] == {"method":"POST","path":"/repos/FS-GG/FS.GG.SDD/issues/47/comments","kind":"rest-mutation"}' >/dev/null 2>&1 \
  && ok "#2157: reducer result atomically writes/observes Status=Ready plus empty Blocked by and records its receipt" \
  || bad "#2157: lifecycle receipt and atomic batch" "rc=$blocker_cleared_rc receipt=$blocker_cleared mutations=$blocker_mutations"

# Negative control 1: an acknowledged batch that only projects Status is a FAILED repair, never a
# written/converged receipt.  The stale Blocked by edge must remain visible in the verification error.
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/reset-reconcile-47" >/dev/null
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/arm-reconcile-47-partial" >/dev/null
partial="$("$ENGINE" reconcile --repo FS.GG.SDD --apply --worker reconcile-probe --json 2>/dev/null)"; partial_rc=$?
printf '%s' "$partial" | jq -e '
  map(select(.subject == "FS.GG.SDD#47")) | length == 1 and
  .[0].outcome == "failed" and
  (.[0].error | contains("Blocked by")) and
  .[0].observed == [{"field":"Status","value":"Ready"},{"field":"Blocked by","value":"FS-GG/FS.GG.SDD#48"}]' >/dev/null 2>&1 \
  && [ "$partial_rc" -ne 0 ] \
  && ok "#2157: partial Status-only projection fails closed and retains both fresh observations" \
  || bad "#2157: partial BLOCKER-CLEARED must fail closed" "rc=$partial_rc receipt=$partial"

# Negative control 2: if the row disappears between acknowledgement and the fresh scan, there is no
# observation to bless.  A missing row is therefore a FAILED receipt, never an empty successful one.
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/reset-reconcile-47" >/dev/null
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/arm-reconcile-47-missing" >/dev/null
missing="$("$ENGINE" reconcile --repo FS.GG.SDD --apply --worker reconcile-probe --json 2>/dev/null)"; missing_rc=$?
printf '%s' "$missing" | jq -e 'map(select(.subject == "FS.GG.SDD#47")) | length == 1 and .[0].outcome == "failed" and (.[0].error | contains("left the board")) and .[0].observed == []' >/dev/null 2>&1 \
  && [ "$missing_rc" -ne 0 ] \
  && ok "#2157: a missing fresh row cannot masquerade as BLOCKER-CLEARED convergence" \
  || bad "#2157: missing BLOCKER-CLEARED row must fail closed" "rc=$missing_rc receipt=$missing"

# ---- .github#2312: `op-lock acquire`/`release` TAKE AND DROP A REAL DISPATCH GRANT ------------------
# The production caller slice 2 landed without. `Client.OpLock.acquire` and `Options.opLockRef` landed
# correct and reachable from nothing but their own unit tests, so no `fsgg:claim` marker could ever appear
# on an op-lock issue, so `fsgg-dispatch-broker.yml`'s `grant` input had no value any caller could supply
# and its step-5 refusal ("no live grant holds this receiver's operation lock") was unreachable BY
# CONSTRUCTION. These legs are the end-to-end form of the repair: a grant is obtained from a real CAS
# against a real comment thread, and the id printed is the one the SERVER assigned.
#
# FS.GG.SDD#878 IS THE OP-LOCK ISSUE, resolved from the engine's own embedded table rather than named on
# argv, so a table that lost the row reds here. It is not in `ISSUES` for `.github#1033`'s reason, restated:
# a lock issue is off the board by construction, and `Writes.claim` reaches it as a bare comment thread,
# which is all a CAS needs.
#
# EMPTY BEFORE, OURS AFTER, ONE VARIABLE — the same discipline the #1535 chore-lock legs above use, and for
# the same reason: a leg that found a marker either way would pass without the verb having written anything.
oplk0="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/878/comments")"
grep -qF -- 'fsgg:claim' <<<"$oplk0" \
  && bad ".github#2312: the operation lock is FREE before the probe" "a marker survives: $oplk0" \
  || ok ".github#2312: FS.GG.SDD's operation lock is free before the probe (the one variable is the verb)"

opa="$("$ENGINE" op-lock acquire "FS-GG/FS.GG.SDD#44" 650045 "FS-GG/FS.GG.SDD" dispatch:coordination-kit \
       --worker otter-2312 --json 2>/dev/null)"; oparc=$?
opgrant="$(printf '%s' "$opa" | jq -r '.grant // empty')"
opkey="$(printf '%s' "$opa" | jq -r '.opkey // empty')"
[ "$oparc" -eq 0 ] && [ -n "$opgrant" ] && grep -qE -- '^[0-9a-f]{64}$' <<<"$opkey" \
  && ok ".github#2312: \`op-lock acquire\` succeeds and prints a grant and a 64-hex opkey" \
  || bad ".github#2312: op-lock acquire must succeed on a free lock" "rc=$oparc: $opa"

# THE GRANT IS THE SERVER'S ANSWER, NOT THE CALLER'S. Every other field of that document echoes something
# the caller typed; this one cannot, and that is the entire authorization mechanism (design §3.2 — "nobody
# can mint one locally, nobody can choose its value, and nobody can forge its ordering"). So the assertion
# is not "a grant was printed" but "the printed grant IS the id this fixture assigned to the marker that
# actually appeared", which no amount of echoing argv could satisfy.
oplk1="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/878/comments")"
opmarker="$(printf '%s' "$oplk1" | jq -r '[.[] | select(.body | contains("fsgg:claim"))] | if length == 1 then .[0].id else empty end')"
[ -n "$opmarker" ] && [ "$opmarker" = "$opgrant" ] \
  && grep -qF -- 'worker=otter-2312' <<<"$oplk1" \
  && ok ".github#2312: the grant printed IS the server-assigned comment id of the marker on the lock issue" \
  || bad ".github#2312: the grant must be the server's id" "printed=$opgrant thread marker=$opmarker: $oplk1"

# A BUSY RECEIVER IS THE FENCE WORKING, AND IT EXITS 6. `ExitContended` says "back off and retry"; exit 1
# would say "change something first". A caller handed the wrong one either stops retrying a receiver that
# will free itself, or retries an unrostered one for ever.
opb="$("$ENGINE" op-lock acquire "FS-GG/FS.GG.SDD#44" 650045 "FS-GG/FS.GG.SDD" dispatch:coordination-kit \
       --worker crake-2312 --json 2>&1)"; opbrc=$?
oplk2="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/878/comments")"
[ "$opbrc" -eq 6 ] \
  && [ "$(printf '%s' "$oplk2" | jq -r '[.[] | select(.body | contains("fsgg:claim"))] | length')" = "1" ] \
  && grep -qF -- 'otter-2312' <<<"$opb" \
  && ok ".github#2312: a second executor is REFUSED with exit 6 and names the holder — the lock working" \
  || bad ".github#2312: a live holder must refuse the grant at exit 6" "rc=$opbrc: $opb / thread: $oplk2"

# AND THE REFUSED EXECUTOR CANNOT DROP THE HOLDER'S GRANT EITHER. `release` DELETES, so a verb that found
# its target by anything other than the capability `Writes.verifyHeld` grants would break a lock somebody
# is standing in — #550, on the subject where it costs a duplicate dispatch.
opnr="$("$ENGINE" op-lock release "FS-GG/FS.GG.SDD" --worker crake-2312 2>&1)"; opnrrc=$?
oplk3="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/878/comments")"
[ "$opnrrc" -eq 1 ] \
  && [ "$(printf '%s' "$oplk3" | jq -r '[.[] | select(.body | contains("fsgg:claim"))] | length')" = "1" ] \
  && ok ".github#2312: a non-holder's \`op-lock release\` REFUSES and deletes nothing" \
  || bad ".github#2312: only the holder may drop the grant" "rc=$opnrrc: $opnr / thread: $oplk3"

opr="$("$ENGINE" op-lock release "FS-GG/FS.GG.SDD" --worker otter-2312 --json 2>/dev/null)"; oprrc=$?
oplk4="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/878/comments")"
[ "$oprrc" -eq 0 ] && [ "$(printf '%s' "$opr" | jq -r '.grant')" = "$opgrant" ] \
  && ! grep -qF -- 'fsgg:claim' <<<"$oplk4" \
  && ok ".github#2312: the holder's \`op-lock release\` drops the grant and the lock is free again" \
  || bad ".github#2312: the holder must be able to release" "rc=$oprrc: $opr / thread: $oplk4"

# THE INJECTED ROSTER REACHES PRODUCTION. `opLockRef`'s `extra` parameter is documented as the per-deployment
# roster a vendored tenant brings, and until this row it had no production reader at all — the only caller
# would have passed `[]` for ever, which is this row's own reader-without-writer defect one level down. A
# DIFFERENT issue number is the assertion: the marker must land on 4242, not on the embedded 878.
opinj="$(FSGG_COORD_OP_LOCKS="FS-GG/FS.GG.SDD#4242" "$ENGINE" op-lock acquire "FS-GG/FS.GG.SDD#44" 650045 \
         "FS-GG/FS.GG.SDD" dispatch:coordination-kit --worker teal-2312 --json 2>/dev/null)"; opinjrc=$?
opinjg="$(printf '%s' "$opinj" | jq -r '.grant // empty')"
opinj878="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/878/comments")"
opinj4242="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/4242/comments")"
[ "$opinjrc" -eq 0 ] && [ -n "$opinjg" ] \
  && grep -qF -- 'worker=teal-2312' <<<"$opinj4242" \
  && ! grep -qF -- 'fsgg:claim' <<<"$opinj878" \
  && ok ".github#2312: FSGG_COORD_OP_LOCKS repoints the lock — the marker lands on the injected issue, not the embedded one" \
  || bad ".github#2312: the injected op-lock roster must reach opLockRef" \
       "rc=$opinjrc: $opinj / 4242: $opinj4242 / 878: $opinj878"

FSGG_COORD_OP_LOCKS="FS-GG/FS.GG.SDD#4242" "$ENGINE" op-lock release "FS-GG/FS.GG.SDD" --worker teal-2312 >/dev/null 2>&1

mark_contract "op-lock acquire" "oplock-grant-round-trip"
mark_contract "op-lock release" "oplock-grant-round-trip"

# ---- .github#2801: compiled mutual-overlap arbitration transaction -------------------------------
# Build the real incident shape through HTTP: two live generations reserve one token, then each holder
# records its wait. The second command must discover the cycle from the authoritative threads, create
# one ADR-0051 room, and back-reference both items. Arbitration is then applied by the losing holder;
# the claim comment must survive while the shared token disappears from its body.
for n in 42 43; do
  curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/$n/comments" \
    | jq -r '.[] | select(.body | contains("fsgg:claim") or contains("fsgg:overlap-wait/")) | .id' \
    | while read -r cid; do
        [ -z "$cid" ] || curl -fsS -X DELETE "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/comments/$cid" >/dev/null
      done
  curl -fsS -X PATCH -H 'Content-Type: application/json' \
    -d '{"body":"Mutual-overlap fixture.\n\nPaths: src/Mutual.fs"}' \
    "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/$n" >/dev/null
done
# The host string is no longer authority. Acquire one immutable live board-orchestrator lease and bind
# arbitration to that authority ref plus the current caller identity.
curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/.github/issues/2801/comments" \
  | jq -r '.[] | select(.body | contains("fsgg:board-orchestrator-")) | .id' \
  | while read -r cid; do
      [ -z "$cid" ] || curl -fsS -X DELETE "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/comments/$cid" >/dev/null
    done
authority="$($ENGINE overlap orchestrate FS-GG/.github#2801 FS.GG.SDD mutual-overlap-host FS.GG.SDD#42 vole-418 --worker vole-418 2>&1)"; authority_rc=$?
fractured="$($ENGINE overlap orchestrate FS.GG.SDD#44 FS.GG.SDD fractured-domain FS.GG.SDD#42 vole-418 --worker vole-418 2>&1)"; fractured_rc=$?
if [ "$fractured_rc" -ne 0 ] && grep -qF 'cannot fracture the lease domain' <<<"$fractured"; then
  ok ".github#2801: arbitrary caller authority refs cannot create a second lease domain"
else
  bad ".github#2801: singleton authority must reject arbitrary caller refs" "rc=$fractured_rc: $fractured"
fi
spoof="$($ENGINE overlap orchestrate FS-GG/.github#2801 FS.GG.SDD copied-public-values FS.GG.SDD#42 vole-418 --worker smew-e1d9 2>&1)"; spoof_rc=$?
if [ "$spoof_rc" -ne 0 ] && grep -qF 'identity is authoritative' <<<"$spoof"; then
  ok ".github#2801: copied public lease values cannot spoof the live orchestrator caller"
else
  bad ".github#2801: active-A authority must bind the current caller, not supplied strings" "rc=$spoof_rc: $spoof"
fi
routed="$($ENGINE overlap orchestrate FS-GG/.github#2801 FS.GG.SDD external-block FS.GG.SDD#42 smew-e1d9 --worker smew-e1d9 2>&1)"; routed_rc=$?
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations" >/dev/null
promoted="$($ENGINE overlap orchestrate FS-GG/.github#2801 FS.GG.SDD run-priority FS.GG.SDD#42 vole-418 --worker vole-418 2>&1)"; promoted_rc=$?
promotion_ledger="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations")"
if [ "$routed_rc" -eq 0 ] && grep -qF 'ROUTED' <<<"$routed" \
   && [ "$promoted_rc" -eq 0 ] && grep -qF 'promoted blocking request' <<<"$promoted" \
   && printf '%s' "$promotion_ledger" | jq -e '.count == 1 and .requests[0].kind == "graphql-mutation"' >/dev/null 2>&1; then
  ok ".github#2801: active A performs the highest-safe board priority mutation before ordinary work"
else
  bad ".github#2801: request priority must be projected, not merely printed" \
      "route=$routed_rc:$routed promote=$promoted_rc:$promoted ledger=$promotion_ledger"
fi
wait42_gen="$(curl -fsS -X POST -H 'Content-Type: application/json' \
  -d '{"body":"<!-- fsgg:claim worker=vole-418 lease=120 -->\nheld"}' \
  "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq -r '.id')"
wait43_gen="$(curl -fsS -X POST -H 'Content-Type: application/json' \
  -d '{"body":"<!-- fsgg:claim worker=smew-e1d9 lease=120 -->\nheld"}' \
  "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43/comments" | jq -r '.id')"

wait_a="$($ENGINE overlap wait FS.GG.SDD#42 FS.GG.SDD#43 host/root --worker vole-418 2>&1)"; wait_a_rc=$?
wait_b="$($ENGINE overlap wait FS.GG.SDD#43 FS.GG.SDD#42 host/root --worker smew-e1d9 2>&1)"; wait_b_rc=$?
rooms="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues" \
  | jq '[.[] | select(.body | contains("fsgg:mutual-overlap-room/v1"))]')"
room_number="$(printf '%s' "$rooms" | jq -r 'if length == 1 then .[0].number else empty end')"
body42="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42" | jq -r '.body')"
body43="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43" | jq -r '.body')"
if [ "$wait_a_rc" -eq 0 ] && grep -qF 'WAIT RECORDED' <<<"$wait_a" \
   && [ "$wait_b_rc" -eq 6 ] && grep -qF 'MUTUAL OVERLAP' <<<"$wait_b" \
   && [ -n "$room_number" ] && grep -qF "Rooms: #$room_number" <<<"$body42" \
   && grep -qF "Rooms: #$room_number" <<<"$body43"; then
  ok ".github#2801: reciprocal generation-bound waits create exactly one automatic room and both backrefs"
else
  bad ".github#2801: compiled reciprocal wait route must freeze both holders in one room" \
      "gens=$wait42_gen/$wait43_gen first=$wait_a_rc:$wait_a second=$wait_b_rc:$wait_b rooms=$rooms body42=$body42 body43=$body43"
fi

# The cycle is already durable here, and both holders must be unable to mutate the shared token away
# before precedence. This is the exact production-writer escape the initial critic demonstrated.
cycle_escape="$($ENGINE set-paths FS.GG.SDD#42 --paths src/Other.fs --worker vole-418 2>&1)"; cycle_escape_rc=$?
cycle_escape_body="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42" | jq -r '.body')"
if [ "$cycle_escape_rc" -ne 0 ] && grep -qF 'cycle freeze refused removal' <<<"$cycle_escape" \
   && grep -qF 'src/Mutual.fs' <<<"$cycle_escape_body" && ! grep -qF 'src/Other.fs' <<<"$cycle_escape_body"; then
  ok ".github#2801: both-holder production freeze blocks mutation away before precedence"
else
  bad ".github#2801: detected cycle must freeze the production set-paths writer" \
      "rc=$cycle_escape_rc:$cycle_escape body=$cycle_escape_body"
fi

arb="$($ENGINE overlap arbitrate FS.GG.SDD#43 FS.GG.SDD#42 FS-GG/.github#2801 --worker vole-418 2>&1)"; arb_rc=$?
after42_body="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42" | jq -r '.body')"
after42_thread="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments")"
room_thread="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/$room_number/comments")"
if [ "$authority_rc" -eq 0 ] && grep -qF 'ACQUIRED' <<<"$authority" \
   && [ "$arb_rc" -eq 0 ] && grep -qF 'PRECEDENCE APPLIED' <<<"$arb" \
   && grep -qF 'Paths: any' <<<"$after42_body" && ! grep -qF 'src/Mutual.fs' <<<"$after42_body" \
   && [ "$(printf '%s' "$after42_thread" | jq --argjson gen "$wait42_gen" '[.[] | select(.id == $gen and (.body | contains("fsgg:claim worker=vole-418")))] | length')" = "1" ] \
   && [ "$(printf '%s' "$room_thread" | jq '[.[] | select(.body | contains("fsgg.coord.overlap-precedence/v1"))] | length')" = "1" ]; then
  ok ".github#2801: precedence narrows the loser atomically without releasing its live claim"
else
  bad ".github#2801: compiled arbitration must preserve the loser claim and leave one current precedence" \
      "authority=$authority_rc:$authority rc=$arb_rc:$arb body=$after42_body item-thread=$after42_thread room-thread=$room_thread"
fi

frozen="$($ENGINE widen FS.GG.SDD#42 --paths src/Mutual.fs --worker vole-418 2>&1)"; frozen_rc=$?
frozen_body="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42" | jq -r '.body')"
if [ "$frozen_rc" -ne 0 ] && grep -qF 'loser resume refused' <<<"$frozen" \
   && grep -qF 'Paths: any' <<<"$frozen_body" && ! grep -qF 'src/Mutual.fs' <<<"$frozen_body"; then
  ok ".github#2801: production widen enforces the generation-bound freeze before PATCH"
else
  bad ".github#2801: an active loser must not re-add shared reservations" "rc=$frozen_rc:$frozen body=$frozen_body"
fi

curl -fsS -X DELETE "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/comments/$wait43_gen" >/dev/null
curl -fsS -X PATCH -H 'Content-Type: application/json' -d '{"state":"closed"}' \
  "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43" >/dev/null
git -C "$RESUME_GIT" init -q
git -C "$RESUME_GIT" config user.email fixture@example.invalid
git -C "$RESUME_GIT" config user.name fixture
git -C "$RESUME_GIT" commit --allow-empty -qm unfetched-base
git -C "$RESUME_GIT" update-ref refs/remotes/origin/main HEAD
unfetched="$(cd "$RESUME_GIT" && "$ENGINE" widen FS.GG.SDD#42 --paths src/Mutual.fs --worker vole-418 2>&1)"; unfetched_rc=$?
if [ "$unfetched_rc" -ne 0 ] && grep -qF 'winner base was not fetched' <<<"$unfetched"; then
  ok ".github#2801: locally manufactured or unfetched origin/main cannot satisfy loser resume"
else
  bad ".github#2801: loser resume must independently prove a fetched current base" "rc=$unfetched_rc:$unfetched"
fi
# The production resume predicate reads the checkout, so give this positive leg an isolated repository
# whose fetched base is contained by HEAD. CI's checkout intentionally does not promise an origin/main
# ref, and mutating the real checkout's remote-tracking ref would make the fixture alter its caller.
git -C "$RESUME_GIT" init --bare -q remote.git
git -C "$RESUME_GIT/remote.git" symbolic-ref HEAD refs/heads/main
git -C "$RESUME_GIT" remote add origin "$RESUME_GIT/remote.git"
git -C "$RESUME_GIT" branch -M main
git -C "$RESUME_GIT" push -q -u origin main
git -C "$RESUME_GIT" fetch -q origin main
stale_base="$(git -C "$RESUME_GIT" rev-parse refs/remotes/origin/main)"
git -C "$RESUME_GIT" commit --allow-empty -qm remote-advanced
git -C "$RESUME_GIT" push -q origin main
git -C "$RESUME_GIT" update-ref refs/remotes/origin/main "$stale_base"
stale_fetch="$(cd "$RESUME_GIT" && "$ENGINE" widen FS.GG.SDD#42 --paths src/Mutual.fs --worker vole-418 2>&1)"; stale_fetch_rc=$?
if [ "$stale_fetch_rc" -ne 0 ] && grep -qF 'winner base was not fetched' <<<"$stale_fetch"; then
  ok ".github#2801: a stale fetched origin/main ref cannot satisfy loser resume"
else
  bad ".github#2801: loser resume must compare its fetched ref with current server main" "rc=$stale_fetch_rc:$stale_fetch"
fi
git -C "$RESUME_GIT" fetch -q origin main
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/open-pr-42-unreviewed" >/dev/null
unreviewed="$(cd "$RESUME_GIT" && "$ENGINE" widen FS.GG.SDD#42 --paths src/Mutual.fs --worker vole-418 2>&1)"; unreviewed_rc=$?
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/close-pr-42" >/dev/null
if [ "$unreviewed_rc" -ne 0 ] && grep -qF 'changed loser head lacks exact-head review' <<<"$unreviewed"; then
  ok ".github#2801: an open loser PR with no exact-head passing review cannot resume"
else
  bad ".github#2801: unreviewed loser heads must fail the production resume gate" "rc=$unreviewed_rc:$unreviewed"
fi
resumed="$(cd "$RESUME_GIT" && "$ENGINE" widen FS.GG.SDD#42 --paths src/Mutual.fs --worker vole-418 2>&1)"; resumed_rc=$?
resumed_body="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42" | jq -r '.body')"
if [ "$resumed_rc" -eq 0 ] && grep -qF 'src/Mutual.fs' <<<"$resumed_body"; then
  ok ".github#2801: production resume gates winner-land, rebase, clear overlap and explicit re-widen"
else
  bad ".github#2801: loser resume must pass only after the complete production predicate" \
      "rc=$resumed_rc:$resumed body=$resumed_body"
fi

# These ten write rows are driven by their dedicated state-transition assertions above.  Marking
# them here keeps the ledger at one entry per advertised command while the assertions remain next
# to the preconditions that make each mutation meaningful.
for command in adopt child claim "done" heartbeat release say set-field set-paths widen; do
  mark_contract "$command" "dedicated-write-driver"
done

# THE COMPLETENESS GATE (#1569). Compare names from this process's freshly-built contract, not a
# copy in bash.  The count is deliberately derived rather than frozen: command-contract is total
# over the Command union, so a newly advertised verb changes the subject this test must drive.
# Every advertised row must appear once; every conditional row must have both executable polarities
# (or its typed argvCannotSay exemption) recorded.  On a mismatch, print both sets so the missing
# driver is actionable rather than a bare cardinality failure.
mapfile -t advertised < <(run command-contract --json | jq -r '.commands[].name' | sort)
mapfile -t driven < <(printf '%s\n' "${!CONTRACT_DRIVEN[@]}" | sort)
if [ "${#advertised[@]}" -gt 0 ] && [ "$(printf '%s\n' "${advertised[@]}")" = "$(printf '%s\n' "${driven[@]}")" ]; then
  ok "#1569: every one of the ${#advertised[@]} advertised contract rows has exactly one executable driver"
else
  missing="$(comm -23 <(printf '%s\n' "${advertised[@]}") <(printf '%s\n' "${driven[@]}"))"
  unexpected="$(comm -13 <(printf '%s\n' "${advertised[@]}") <(printf '%s\n' "${driven[@]}"))"
  duplicates="$(printf '%s\n' "${!CONTRACT_DRIVEN[@]}" | sort | uniq -d)"
  bad "#1569: command-contract coverage must be exact and non-vacuous" "missing: ${missing:-none}\nunexpected: ${unexpected:-none}\nduplicate driven rows: ${duplicates:-none}"
fi
for mode in 'delivery:bare' 'delivery:apply' 'flush:dry-run' 'flush:apply' 'reap:bare' 'reap:apply' 'reconcile:bare' 'reconcile:apply' 'next:argvCannotSay'; do
  if [ -n "${CONDITIONAL_MODES[$mode]:-}" ]; then
    ok "#1569: conditional contract polarity exercised: $mode"
  else
    bad "#1569: conditional contract polarity is missing: $mode"
  fi
done

# ---- report ----------------------------------------------------------------------------------------
echo
echo "coord-engine writes: $((pass + failcount)) assertion(s), $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::coord-engine writes FAILED"; exit 1; }
echo "green — the engine's write commands land, over HTTP, hermetically."
