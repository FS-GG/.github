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

SRV_OUT="$(mktemp)"; CACHE_DIR="$(mktemp -d)"; PREDICATE_FIX="$(mktemp -d)"; CYCLE_SNAPSHOT="$(mktemp)"; CYCLE_FIX="$(mktemp -d)"; INTAKE_DRAFT="$(mktemp)"; INTAKE_BLOCKED_DRAFT="$(mktemp)"; INTAKE_REPOS="$(mktemp -d)"; ROUTE_RECEIPT="$(mktemp)"; SDD_ROOT="$(mktemp -d)"
FORCE_BUDGET_CACHE="$(mktemp -d)"
python3 "$HERE/stateful_server.py" >"$SRV_OUT" 2>&1 &
SRV_PID=$!
trap 'kill "$SRV_PID" 2>/dev/null; rm -f "$SRV_OUT" "$CYCLE_SNAPSHOT" "$INTAKE_DRAFT" "$INTAKE_BLOCKED_DRAFT" "$ROUTE_RECEIPT"; rm -rf "$CACHE_DIR" "$PREDICATE_FIX" "$CYCLE_FIX" "$FORCE_BUDGET_CACHE" "$INTAKE_REPOS" "$SDD_ROOT"' EXIT

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

review_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
write_review_draft initial changes-required 0 "" ""
review_initial_out="$("$ENGINE" review record FS.GG.SDD#42 "$review_draft" --pr 42 --json 2>&1)"; review_initial_rc=$?
review_initial_url="$(printf '%s' "$review_initial_out" | jq -r '.commentUrl // empty')"
write_review_draft confirmation pass 1 https://fixture.invalid/wrong https://fixture.invalid/wrong
review_wrong_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
"$ENGINE" review record FS.GG.SDD#42 "$review_draft" --pr 42 --json >/dev/null 2>&1; review_wrong_rc=$?
review_wrong_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
write_review_draft confirmation pass 1 "$review_initial_url" "$review_initial_url"
review_confirmation_out="$("$ENGINE" review record FS.GG.SDD#42 "$review_draft" --pr 42 --json 2>&1)"; review_confirmation_rc=$?
review_confirmation_url="$(printf '%s' "$review_confirmation_out" | jq -r '.commentUrl // empty')"
write_review_draft acceptance accepted 0 "$review_initial_url" "$review_confirmation_url"
review_acceptance_out="$("$ENGINE" review record FS.GG.SDD#42 "$review_draft" --pr 42 --json 2>&1)"; review_acceptance_rc=$?
review_acceptance_id="$(printf '%s' "$review_acceptance_out" | jq -r '.commentId // empty')"
review_acceptance_body="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" \
  | jq -r --argjson id "${review_acceptance_id:-0}" '.[] | select(.id == $id) | .body')"
write_review_draft initial pass 0 "" "" bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb critic-tern-43
review_moved_initial_out="$("$ENGINE" review record FS.GG.SDD#42 "$review_draft" --pr 42 --json 2>&1)"; review_moved_initial_rc=$?
review_moved_initial_url="$(printf '%s' "$review_moved_initial_out" | jq -r '.commentUrl // empty')"
write_review_draft acceptance accepted 0 "$review_moved_initial_url" "$review_moved_initial_url" bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb critic-tern-43
review_moved_acceptance_out="$("$ENGINE" review record FS.GG.SDD#42 "$review_draft" --pr 42 --json 2>&1)"; review_moved_acceptance_rc=$?
review_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | jq length)"
if [ "$review_initial_rc" -eq 0 ] && [ "$review_confirmation_rc" -eq 0 ] && [ "$review_acceptance_rc" -eq 0 ] \
   && [ "$review_wrong_rc" -ne 0 ] && [ "$review_wrong_before" = "$review_wrong_after" ] \
   && [ "$review_moved_initial_rc" -eq 0 ] && [ "$review_moved_acceptance_rc" -eq 0 ] \
   && printf '%s' "$review_initial_out" | jq -e '.revision == 1 and (.digest | length) == 64' >/dev/null \
   && printf '%s' "$review_confirmation_out" | jq -e '.revision == 2 and (.digest | length) == 64' >/dev/null \
   && printf '%s' "$review_acceptance_out" | jq -e '.revision == 3 and .effectiveChainValidated == true and (.digest | length) == 64' >/dev/null \
   && [[ "$review_acceptance_body" == *'"baseSha":"cccccccccccccccccccccccccccccccccccccccc"'* ]] \
   && [[ "$review_acceptance_body" != *'"baseSha":"9999999999999999999999999999999999999999"'* ]] \
   && printf '%s' "$review_moved_initial_out" | jq -e '.revision == 4 and (.digest | length) == 64' >/dev/null \
   && printf '%s' "$review_moved_acceptance_out" | jq -e '.revision == 5 and .effectiveChainValidated == true and (.digest | length) == 64' >/dev/null \
   && [ "$review_after" -eq $((review_before + 5)) ]; then
  ok "M4 review record validates actual backlinks and retires an accepted generation after head movement"
else
  bad "M4 review record must append parseable v2 generations with actual backlinks" "comments=$review_before->$review_after wrong=$review_wrong_rc:$review_wrong_before->$review_wrong_after initial=$review_initial_rc:$review_initial_out confirmation=$review_confirmation_rc:$review_confirmation_out acceptance=$review_acceptance_rc:$review_acceptance_out moved-initial=$review_moved_initial_rc:$review_moved_initial_out moved-acceptance=$review_moved_acceptance_rc:$review_moved_acceptance_out"
fi

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

# shellcheck disable=SC2086 # $advice is the tool's OWN argv, split on purpose — that is what "verbatim" means
steal="$("$ENGINE" $advice --worker kite-461 2>&1)"; strc=$?
if [ "$strc" -eq 0 ] && printf '%s' "$steal" | grep -q 'STOLE FS.GG.SDD#42'; then
  ok ".github#1620: ...and running it VERBATIM takes the item — the advertised route is real"
else
  bad ".github#1620: adopt's remedy must work when followed" "rc=$strc: $steal"
fi

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

# ---- done stamps a completed item ------------------------------------------------------------------
dn="$(run "done" FS.GG.SDD#42 2>&1)"; dnrc=$?   # quoted: the coord VERB, not the loop keyword (SC1010, #648)
[ "$dnrc" -eq 0 ] && printf '%s' "$dn" | grep -q 'FSGG-DONE' \
  && ok "done stamps an item closed by a merged PR" \
  || bad "done stamps a completed item" "rc=$dnrc: $dn"

# ---- #1151: done SURFACES a DEFERRED Status write, keeps the GREEN stamp, and flush completes it -----
# The exact condition #1151 closes: on an exhausted budget the Status=Done write returns `Deferred`
# (QUEUED, and nothing replays it on its own). The Green arm used to `|> ignore` that outcome, so `done`
# printed green, exited 0, and said NOTHING about the flush the board now needs — silent drift. It must
# instead keep the green verdict (the WORK is done), DROP the claim (the lock's lifetime is the work's,
# #533), and PRINT the flush remedy. Then `flush` lands the queued stamp.
run claim FS.GG.SDD#42 >/dev/null 2>&1                                            # a live marker for done to drop
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/defer-next-field-write" >/dev/null      # arm: the next field write RATE-LIMITS
dd="$(run "done" FS.GG.SDD#42 --flip 2>&1)"; ddrc=$?
if [ "$ddrc" -eq 0 ] \
   && printf '%s' "$dd" | grep -q 'FSGG-DONE' \
   && printf '%s' "$dd" | grep -qi 'DEFERRED' \
   && printf '%s' "$dd" | grep -q 'scripts/fsgg-coord flush'; then
  ok "#1151: done keeps the GREEN stamp but SURFACES the deferred Status write with flush advice"
else
  bad "#1151: done surfaces the deferred Status write with flush advice" "rc=$ddrc: $dd"
fi

# ...and the claim marker is DROPPED even though the column deferred — the lock must not outlive the work.
after1151="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments")"
printf '%s' "$after1151" | grep -q 'fsgg:claim' \
  && bad "#1151: done drops the claim marker on a deferred write" "the marker survives: $after1151" \
  || ok "#1151: done drops the claim marker even when the Status write DEFERS"

# ...and flush REPLAYS the queued Status=Done write against the now-healthy server — the stamp is not lost.
fdd="$(run flush 2>&1)"; fddrc=$?
[ "$fddrc" -eq 0 ] && ! printf '%s' "$fdd" | grep -qi 'DROPPED' \
  && ok "#1151: flush replays the deferred Status=Done write (the queued stamp lands)" \
  || bad "#1151: flush replays the deferred stamp" "rc=$fddrc: $fdd"

# ---- #1086: a worker mid-item in ANOTHER repo is NOT idle, and is offered nothing --------------------
# The whole of #1086, end to end. `vole-418` holds a live claim on FS.GG.SDD#43 (taken above and never
# released), and is here stamping a `.github` item. Condition 3 says they must not be handed a side-quest:
# they are mid-lease with a live touch-set — in a different repo, which is exactly what makes it invisible.
#
# It was invisible. The offer asked "are you idle?" of a board scoped to `.github`, in which that SDD claim
# does not appear, so the honest guard answered "idle" and handed over the chore. The board is read
# UNFILTERED now and the scope rides in the type, so the claim is seen and the offer is withheld.
#
# IT RUNS FIRST, BEFORE ANY LEG TAKES THE CHORE LOCK, and that ordering is the whole assertion. Written
# after the snipe-733 legs it PASSED WITH THE BUG SIMULATED: snipe-733 holds `.github#1033` by then, so
# vole-418's offer lost the LOCK and returned None for a reason having nothing to do with idleness. A leg
# that cannot fail is not evidence — it is the "shaped to pass" defect this issue's own review history
# (plover-a4cf, #733) caught one level up. So: lock free, chore real, worker busy — one variable.
#
# The chore is REAL and offerable on exactly this board: snipe-733 is handed it by the very next leg.
vb="$("$ENGINE" "done" FS-GG/.github#51 --flip --worker vole-418 2>&1)"; vbrc=$?
[ "$vbrc" -eq 0 ] && printf '%s' "$vb" | grep -q 'FSGG-DONE' && ! printf '%s' "$vb" | grep -qi 'chore' \
  && ok "#1086: a worker holding a claim in ANOTHER repo is not idle — no chore, though one is on offer" \
  || bad "#1086: cross-repo claim makes us busy" "rc=$vbrc: $vb"

# ---- #733/§4.6: `done` is a SAFE POINT, and it is the one a working fleet reaches --------------------
# Condition 3 names two boundaries — after `done`, or at `next`. #1056 wired `next`; `Chore.AfterDone` was
# a case Core declared and NOTHING minted. That matters because of WHERE the two sit in the recipe:
# `/pnext-item` takes (§1), stamps (§5), and loops back to `take` (§6), calling `next` only in its
# "take found nothing" DIAGNOSTIC. An offer that fires only at `next` fires only when the board has no
# work — and a board with no work has no fleet to conscript. This leg is the whole claim: stamping an item
# offers a chore.
#
# .github#51 is stamped; .github#50 is the chore (CLOSED, board column still Ready → CLOSED-ISSUE-NOT-DONE).
dc="$("$ENGINE" "done" FS-GG/.github#51 --flip --worker snipe-733 2>&1)"; dcrc=$?
[ "$dcrc" -eq 0 ] && printf '%s' "$dc" | grep -q 'FSGG-DONE' \
  && printf '%s' "$dc" | grep -q '^chore \[quick\] \.github#50: fresh lifecycle facts project Status=Done' \
  && ok "M6: done offers only the direct reducer's receipt-backed lifecycle projection" \
  || bad "M6: done did not offer the direct reducer result" "rc=$dcrc: $dc"

# THE OFFER IS A COURTESY, AND THE STAMP OUTRANKS IT. The chore rides on STDERR, never stdout: `done`'s
# stdout carries the FSGG-DONE verdict a caller greps, and an offer printed there would corrupt the answer
# it is attached to — the same rule `next` keeps for its item ref.
dso="$("$ENGINE" "done" FS-GG/.github#51 --flip --worker snipe-733 2>/dev/null)"
printf '%s' "$dso" | grep -q 'FSGG-DONE' && ! printf '%s' "$dso" | grep -qi 'chore' \
  && ok "#733: the AfterDone offer is on stderr — done's stdout verdict is untouched" \
  || bad "#733: offer does not pollute done's stdout" "$dso"

# #1087 — A RECEIVER NOW DRAINS. Before #1087 `choreLockRef` knew only `.github#1033`, so a `done` in any
# receiver was refused for want of a lock and the queue drained in `.github` alone. The six receivers now
# have closed `[chore-lock]` issues (SDD#518 among them) and the map resolves all seven, so stamping an SDD
# item offers an SDD chore under SDD's OWN lock. This is the rollout's whole point, and it reds on pre-#1087
# code (SDD had no lock). The direct reducer selects the first stale SDD projection (#44 → Ready).
rc="$("$ENGINE" "done" FS.GG.SDD#42 --worker snipe-1087 2>&1)"; rcrc=$?
[ "$rcrc" -eq 0 ] && printf '%s' "$rc" | grep -q 'FSGG-DONE' \
  && printf '%s' "$rc" | grep -q '^chore \[quick\] FS.GG.SDD#44: fresh lifecycle facts project Status=Ready' \
  && ok "M6: receiver done also offers the direct reducer's fresh projection" \
  || bad "M6: receiver done did not offer the direct reducer result" "rc=$rcrc: $rc"

# AN UNROSTERED REPO IS REFUSED, FOR FREE. All seven FS-GG repos have a lock now, so the honest "no lock"
# case is a repo `choreLockRef` does not know (FS.GG.Legacy). `Chores.offer`'s step 1 is that pure string
# match, placed first "because it spends nothing" — so a `done` there stamps, offers nothing, and never
# reads the board. No assertion on OUTPUT can catch a stray read (the output is identical either way:
# nothing), so the fixture counts board reads and this asserts the count does not move.
br0="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/board-reads" | sed 's/[^0-9]//g')"
dn2="$("$ENGINE" "done" FS.GG.Legacy#60 --worker snipe-1087 2>&1)"; dn2rc=$?
[ "$dn2rc" -eq 0 ] && printf '%s' "$dn2" | grep -q 'FSGG-DONE' && ! printf '%s' "$dn2" | grep -qi 'chore' \
  && ok "#1087: an UNROSTERED repo is offered nothing, and still stamps" \
  || bad "#1087: unrostered repo offers nothing" "rc=$dn2rc: $dn2"
br1="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/board-reads" | sed 's/[^0-9]//g')"
[ "$br0" = "$br1" ] \
  && ok "#1087: an unrostered repo costs NO board read — the free question is asked first (${br0}→${br1})" \
  || bad "#1087: unrostered done spends no board read" "board reads went ${br0} → ${br1}"

# ---- .github#1535: `next` TAKES THE CHORE LOCK, and `batch` does not --------------------------------
# The decision #1535 asked for, pinned as behaviour: `next` WRITES. Its contract everywhere a caller met
# it — `/pnext-item` §1, the take exit-code table, and `tests/coord-engine-parity/shim.sh`, which used it
# as the canonical READ verb in the stale-engine guard's read leg for that leg's whole life until #1528 —
# said "tell me what to work on", and after printing that answer it POSTs a claim marker taking the
# repo's chore lock (`.github#1033`,
# ADR-0041). The write is real, it is deliberate (#733/§4.6 conscription), and it is now DECLARED rather
# than discovered. This leg is what stops the declaration and the code drifting apart again.
#
# THE MARKER IS THE ASSERTION, NOT THE PRINTED OFFER. The chore text on stderr is what a human sees, and
# a leg that grepped only for it would pass on an engine that printed the offer and took no lock — the
# offer is a courtesy, the LOCK is the write, and only one of them is #1535's subject. So this reads the
# comment thread on #1033 and asserts a marker naming THIS worker appeared on it.
#
# THE PRECONDITION IS ESTABLISHED, NOT ASSUMED, and that is the whole reason for the release and the
# BEFORE assertion. `snipe-733` still holds #1033 from the AfterDone legs above; a leg written without
# this would find a marker on #1033 either way and pass WITHOUT `next` having written anything — the
# "shaped to pass" defect this file's own #1086 note records being caught one level up. Empty before,
# ours after, one variable.
"$ENGINE" release "FS-GG/.github#1033" --worker snipe-733 >/dev/null 2>&1
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

# ...AND `next`, same worker, same board, POSTS.
nx="$("$ENGINE" next --repo .github --worker teal-1535 2>&1)"; nxrc=$?
lk1="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/.github/issues/1033/comments")"
[ "$nxrc" -eq 0 ] && printf '%s' "$lk1" | grep -q 'fsgg:claim' \
  && printf '%s' "$lk1" | grep -q 'worker=teal-1535' \
  && ok ".github#1535: \`next\` POSTs a claim marker to .github#1033 — it WRITES, and is declared so" \
  || bad ".github#1535: next takes the chore lock" "rc=$nxrc: $nx / lock thread: $lk1"

# ...and the offer it printed names the chore it took the lock FOR, so the write and the reason a caller
# is given for it are the same fact. A lock taken for a chore the caller is never told about would be the
# write with none of the conscription that justifies it.
#
# ANCHORED ON THE CHORE LINE, not searched for `#50` anywhere in the output: `next` also prints `#50` in
# its passed-over list, so an unanchored grep would be satisfied by an offer naming a DIFFERENT subject —
# a leg that cannot see the thing it claims to check.
printf '%s' "$nx" | grep -q '^chore \[quick\] \.github#50:' \
  && ok ".github#1535: the lock \`next\` took is the one the offer it printed names (#50)" \
  || bad ".github#1535: next's offer names its subject" "rc=$nxrc: $nx"

# Hand the lock back, so nothing downstream inherits a held chore lock from a diagnostic verb.
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
if [ "$blocked_rc" -eq 0 ] && printf '%s' "$blocked_out" | jq -e '.status == "Blocked" and (.fields | index("Blocked by"))' >/dev/null 2>&1 \
  && [ "$(printf '%s' "$blocked_ledger" | jq -r .count)" = "3" ]; then
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
# names BOTH intended writes and BOTH fresh observations; one GraphQL batch plus its durable receipt are
# the complete mutation pair.
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
  && printf '%s' "$blocker_mutations" | jq -e '.count == 2 and .requests[0].kind == "graphql-mutation" and .requests[1].kind == "rest-mutation"' >/dev/null 2>&1 \
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
  .[0].observed == [{"field":"Status","value":"Ready"},{"field":"Blocked by","value":"FS-GG/FS.GG.SDD#45"}]' >/dev/null 2>&1 \
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
