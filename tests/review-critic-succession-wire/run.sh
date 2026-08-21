#!/usr/bin/env bash
# Fixture for critic succession (.github#2417, tightened by .github#2451) — the JSON wire boundary in
# `ReviewApplication.fs`, and the REFUSALS in `Review.criticSuccessionValid` that are the whole reason
# the recovery path is safe to have.
#
# WHY THIS FILE WAS REWRITTEN, AND IT IS THE POINT OF .github#2537.
#
# The fixture shipped with #2417 as that change's gate-inversion proof, and then no workflow invoked
# it. CI had never run it once. That alone would be an ordinary unwired suite; what made it worth its
# own row is that an unwired INVERSION fixture means the claim "this gate can fail" has never been
# demonstrated anywhere except once, locally, by its author.
#
# Wiring it turned out to be the smaller half. MEASURED at `7ca9474a`, before this rewrite: the
# fixture's four legs were 4-passed/0-failed against an engine with any ONE of the guard's conjuncts
# deleted — all five that #2537 names, and the two it does not. The four legs were ABSENT, NULL, VALID
# and MALFORMED: three wire-parsing legs and one positive case. Deleting a refusal cannot fail any of
# them, because weakening a conjunction can only ADMIT more, and the only admission any of them
# examined was one that must be admitted anyway. So the suite would have gone green in CI on the day
# `Review.criticSuccessionValid` stopped refusing anything at all.
#
# "The workflow now runs the fixture" and "the fixture can say NO" are different claims. Only the
# second is worth a gate, and it is the one this file now makes.
#
# THE FIVE SECTIONS, and each answers a question the one before it cannot.
#
#   1. WIRE (6 legs) — the four from #2417, still load-bearing, plus two this rewrite moved here.
#      Can the engine READ a grant off the JSON wire: absent key, explicit null, well-formed object,
#      a non-object that must fail CLOSED naming the field, and a blank `successorCriticIdentity` or
#      `grantedBy` — which must ALSO fail closed naming its field. A refusal that never receives the
#      receipt refuses nothing.
#
#      WHY THOSE TWO ARE HERE AND NOT IN SECTION 2 — RESOLVED BY .github#2557, which .github#2537 AC5
#      pre-authorized reporting rather than papering over. `Review.criticSuccessionValid` used to carry
#      SEVEN conjuncts, two of which — `not (String.IsNullOrWhiteSpace receipt.SuccessorCriticIdentity)`
#      and the same for `GrantedBy` — could not be reached from this route or from any route in `src/`:
#      `ReviewApplication.readString` refuses an empty string and names the field,
#      `criticSuccessionReceipt` reads EVERY field through it, that is the only site in `src/` that
#      constructs a `CriticSuccessionReceipt`, and the live `review <ref> --pr N` path passes no grant
#      at all. A blank value failed at parse, exit 1, and the guard never ran. Writing those two as
#      REFUSAL legs was this fixture's first draft and it failed honestly: the engine exits 1 where the
#      leg expected a verdict.
#
#      .github#2557 decided (a) of its own two options: THE WIRE IS THE REAL GUARD, and the unreachable
#      Core copy is gone. So these legs are no longer a note about a duplicate — they are the only place
#      the property is asserted at all, which is why section 5 now inverts them. `readString`'s refusal
#      is deleted in a scratch tree and both legs must FLIP to `enterCriticSuccession`: that is the
#      measurement that the surviving copy is load-bearing, and it also shows exactly what the deleted
#      conjuncts would have caught had anything been able to reach them. Do not re-add a blank check to
#      `Review.fs`; give the rule one home and a leg that reds without it.
#
#   2. REFUSALS (5 legs) — one per REACHABLE conjunct of `Review.criticSuccessionValid`: the four
#      .github#2537 names (exact-critic, exact-head, generic-identity, self-grant) counted as five,
#      because "self-grant" is two conjuncts — the successor and the granter — and an implementer can
#      manufacture a succession through either. Each snapshot carries exactly ONE semantic deviation
#      from the accepted grant, so what it measures is that deviation and not a coincidence.
#
#      Four of the five are also one TEXTUAL field. `generic-identity` is not, and the difference is
#      worth stating rather than glossing: it moves the marker's `critic:` AND the receipt's
#      `originalCriticIdentity` together, to `fsgg-critic-best`. That is deliberate and it is what makes
#      the leg sharp — moving only one of the two would ALSO fail the exact-critic conjunct, and the leg
#      would then pass for the wrong reason, indistinguishable from `exact-critic` above. Moving both
#      keeps `receipt.OriginalCriticIdentity = critic` satisfied so that the ONLY conjunct left failing
#      is `not (isGenericCriticIdentity critic)`. Section 3 is what confirms that claim rather than
#      leaving it as reasoning: with only the generic conjunct deleted, this leg — and no other — flips.
#
#      Each asserts three things, and the third is the one that matters: `resumeSameCritic`, no receipt
#      echoed, AND an `actionReason` carrying "refused, not consumed". That last clause is this
#      fixture's non-vacuity witness. `resumeSameCritic` on its own is what you also get from a
#      snapshot that never REACHED the guard — a typo in a marker, a verdict the parser could not read,
#      a head that made the classifier take a different branch entirely — so a leg asserting only the
#      action would pass just as happily against a fixture that had quietly stopped exercising
#      succession at all. `resumeSameCriticReason` appends that clause only when a receipt was SUPPLIED
#      and the guard REFUSED it (`Review.fs`, DEC-001), so the pair "resumeSameCritic + refused, not
#      consumed" is positive evidence that the grant arrived at the guard and the guard said no. The
#      ABSENT leg asserts the clause is missing, which is what makes the pair discriminating rather
#      than decorative.
#
#   3. LEDGER WRITE (5 legs, added by .github#2662) — the half sections 1 and 2 cannot reach, and the
#      reason this fixture was green while the recovery it tests was unusable in production.
#
#      Sections 1 and 2 drive `review --snapshot`: the pure DECISION. `.github#2417` shipped that and
#      not the LEDGER, so `StructuredDecision.validateReviewLedger` had no succession branch and a
#      granted successor could be dispatched, could review, and then could not record a verdict in any
#      honest shape — `confirmation`, `escalation` and a second `initial` were all refused, measured
#      live on two independent chains in one session. Sixteen green legs said nothing about it, because
#      not one of them wrote a record. That is what these legs do.
#
#      They drive the REAL writer — `fsgg-coord-engine review record` — against the loopback
#      `tests/coord-engine-e2e/stateful_server.py`, the same vehicle `tests/coord-engine-e2e/writes.sh`
#      already uses for this command. Still no token and still no network; a server per case, so each
#      chain starts from an empty ledger and no leg can be explained by another leg's state.
#
#      WHAT THESE LEGS DO NOT REACH, stated rather than papered over: they exercise `review record` end
#      to end, so `Client.recordReview`'s backlink checks and the POST are covered, but the REAL grant
#      is still an out-of-band `--snapshot` fact with no durable marker of its own (deferred, and
#      recorded as such). These legs assert what the LEDGER accepts, never that a grant was genuine.
#
#   4. GATE INVERSION (6 legs) — the measurement, on the precedent set by
#      `tests/review-post-acceptance-head-move/run.sh` leg 6. For each conjunct: delete THAT conjunct
#      from `Review.fs` in a scratch copy of the tree, rebuild, and require THAT conjunct's refusal leg
#      to flip to `enterCriticSuccession`. One mutant per refusal, ~10s each, because a single mutant
#      with every conjunct deleted would show that the legs can red without showing that any leg is
#      bound to the refusal it is named for.
#
#      The sixth is section 3's, and it inverts the other way round, which is the point. Sections 1-2
#      guard gates that REFUSE, so their inversion deletes a refusal and requires an admission. Section
#      3 guards a gate that ADMITS, so its inversion removes the admission — `validateReviewLedger`
#      stops seeing the grant at all — and requires the three accepting legs to RED while the two
#      refusing legs stay exactly as red as they were. A mutation that reddened everything would prove
#      only that the mutant is broken.
#
#      A mutation whose anchor text no longer matches FAILS here rather than silently rebuilding an
#      identical engine — the `tests/coord-engine-mutation/` rule, for the same reason: a leg that
#      matched nothing grades NOT MEASURED, and NOT MEASURED spelled like PASSED is the defect this
#      whole file is about.
#
#   5. WIRE INVERSION (2 legs, added by .github#2557) — section 4 for the OTHER layer, and the reason
#      .github#2557 could delete two conjuncts from `Review.fs` without weakening anything.
#
#      Sections 1-4 measure `Review.fs`. The blank-field property is not enforced there and never was
#      (see section 1's header): its one home is `ReviewApplication.readString`. An unmeasured sole
#      copy is worse than a measured duplicate, so this section deletes `readString`'s blank refusal in
#      the scratch tree, rebuilds, and re-runs section 1's two BLANK legs — the same two snapshot files,
#      byte for byte, so nothing about the input can explain the difference. Both must flip from
#      "exit 1 naming the field" to `enterCriticSuccession`, which is simultaneously the proof that the
#      surviving copy is load-bearing and an exact statement of what the deleted `Review.fs` conjuncts
#      would have refused had any producer been able to reach them.
#
# WHY A SHELL FIXTURE. The reason is unchanged from #2417 and still true at #2537: it drives the
# COMPILED `fsgg-coord-engine` — sections 1-2 through `review --snapshot`, the pure DECISION path with
# no board, no token and no network, and section 3 through `review record` against a loopback fixture,
# still with no token and no network — and asserts on JSON stdout and exit code. Sections 4-5 need to
# rebuild the engine from mutated source seven times, which a unit suite inside that same build cannot
# do to itself.
#
# This paragraph used to be headed "AND NOT `tests/FS.GG.Coord.Core.Tests/ReviewTests.fs`", which named
# a file that `b84423e7` ("retire legacy decision authorities") deleted. .github#2557 corrected it, and
# the fact underneath is stronger than the comparison it replaced: `Review.inspect` has NO unit coverage
# anywhere in `tests/` — `grep -rn "Review\.inspect" tests/ --include=*.fs` finds one COMMENT, at
# `DeliveryTests.fs:313`, and nothing that calls it. (`--include=*.fs` is load-bearing: without it the
# same grep also hits generated `bin/**/FS.GG.Coord.Core.xml` doc comments, which are build output and
# evidence of nothing.) So this file is not the better of two gates over `criticSuccessionValid`. It is
# the ONLY one. Weaken it and nothing else is watching.
set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
ENGINE_DLL="$REPO_ROOT/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine.dll"

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

if [ ! -f "$ENGINE_DLL" ]; then
  echo "FAIL  the engine must be built first: dotnet build src/FS.GG.Coord.Cli -c Release" >&2
  echo "      (looked at: $ENGINE_DLL)" >&2
  exit 1
fi

WORK="$(mktemp -d "${TMPDIR:-/tmp}/review-succession-wire.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

# ---- snapshot construction --------------------------------------------------------------------------
# The binding is fixed: head `bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb`, implementer `impl-worker`, ordinary phase, round 1. The marker
# carries `verdict: changes-required` at `reviewed-head: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa`, so the classifier reaches the
# `Some _ ->` arm at `Review.fs` `classify` that consults `criticSuccessionValid` — the ONE branch pair
# (ordinary and repair-phase) where a grant is ever looked at. Change any of that and the legs below
# stop testing succession without stopping passing, which is the trap section 2's third assertion
# exists to catch.

# grant <original-critic> <successor> <granted-by> <candidate-head> -- one JSON object on stdout.
grant() {
  printf '{"originalCriticIdentity":"%s","successorCriticIdentity":"%s","grantedBy":"%s",' "$1" "$2" "$3"
  printf '"reason":"the original critic despawned before confirming the new commit",'
  printf '"candidateHeadSha":"%s"}' "$4"
}

# snapshot <outfile> <marker-critic> <grant-json|absent> -- `absent` omits the key entirely, which is a
# different fact on the wire from an explicit null and is tested as one.
snapshot() {
  python3 - "$1" "$2" "$3" <<'PY'
import hashlib, json, sys

out, critic, granted = sys.argv[1], sys.argv[2], sys.argv[3]
def frame(value):
    raw = value.encode()
    return f"{len(raw)}:{value}"

record = {
    "schema": "fsgg.coord.review-decision/v2",
    "subject": "FS-GG/.github#2537/pr/2554",
    "revision": 1,
    "previousDigest": None,
    "headSha": "a" * 40,
    "critic": critic,
    "verdict": "changes-required",
    "acceptedExceptions": [],
    "routeApplicability": "not-meaningful",
    "routeEvidence": ["pure review succession state"],
    "policyVersion": "structured-decisions/1",
    "kind": "initial",
    "round": 0,
    "initialReview": None,
    "precedingReview": None,
    "diffAuditRequired": False,
    "diffAuditReceipts": [],
    "timestamp": "2026-08-15T00:00:00Z",
}
fields = [
    frame(record["schema"]), frame(record["subject"]), str(record["revision"]), frame(""),
    frame(record["headSha"]), frame(record["critic"]), frame(record["verdict"]), "",
    frame(record["routeApplicability"]), "".join(map(frame, record["routeEvidence"])),
    frame(record["policyVersion"]), frame(record["kind"]), str(record["round"]), frame(""),
    frame(""), str(record["diffAuditRequired"]), "", frame(record["timestamp"]),
]
record["digest"] = hashlib.sha256("|".join(fields).encode()).hexdigest()
facts = {
    "comments": [
        {
            "id": 1,
            "url": "https://reviews/1",
            "body": "<!-- fsgg:review-decision/v2 -->\n" + json.dumps(record, separators=(",", ":")),
        }
    ],
    "checks": "pending",
    "repairPhaseGranted": None,
    "repairRouteAvailable": True,
}
if granted != "absent":
    facts["criticSuccessionGranted"] = json.loads(granted)

json.dump(
    {
        "binding": {
            "itemRef": "FS-GG/.github#2537",
            "pr": 2554,
            "headSha": "b" * 40,
            "claimGeneration": "gen-1",
            "implementerIdentity": "impl-worker",
            "phase": "ordinary",
            "round": 1,
        },
        "facts": facts,
    },
    open(out, "w"),
    indent=2,
)
PY
}

# run_engine <snapshot> [dll] -- stdout is the verdict JSON; the exit code is the caller's to read.
run_engine() { dotnet "${2:-$ENGINE_DLL}" review --snapshot "$1" --json; }

# field <json> <python-subscript> -- e.g. field "$out" '["action"]'
field() {
  printf '%s' "$1" | python3 -c 'import json,sys; d=json.load(sys.stdin); print(eval("d"+sys.argv[1]))' "$2" 2>/dev/null
}

echo "── 1. WIRE: can the engine read a grant off the JSON wire at all"

# ABSENT: no `criticSuccessionGranted` key. The backward-compatibility case every existing snapshot
# producer relies on, and the control that makes section 2's reason assertion discriminating: with no
# receipt supplied there must be NO "refused, not consumed" clause to find.
ABSENT="$WORK/absent.json"
snapshot "$ABSENT" kite absent
out="$(run_engine "$ABSENT")"; rc=$?
if [ "$rc" -ne 0 ]; then
  bad "ABSENT: the absent case must be exit 0 (backward compatible), got $rc" "$out"
else
  action="$(field "$out" '["action"]')"
  receipt="$(field "$out" '["criticSuccessionReceipt"]')"
  reason="$(field "$out" '["actionReason"]')"
  if [ "$action" != "dispatchSuccessor" ] || [ "$receipt" != "None" ]; then
    bad "ABSENT: expected ordinary action=dispatchSuccessor and no legacy receipt" "$out"
  elif printf '%s' "$reason" | grep -q "refused, not consumed"; then
    bad "ABSENT: the pre-#2417 reason text must be UNCHANGED (there was no grant to refuse)" "$reason"
  else
    ok "ABSENT: no key takes the ordinary dispatchSuccessor route with no legacy receipt echoed"
  fi
fi

# NULL: the same fact spelled out explicitly.
NULLED="$WORK/nulled.json"
snapshot "$NULLED" kite null
out="$(run_engine "$NULLED")"; rc=$?
if [ "$rc" -ne 0 ]; then
  bad "NULL: an explicit null must be exit 0, got $rc" "$out"
elif [ "$(field "$out" '["action"]')" = "dispatchSuccessor" ]; then
  ok "NULL: an explicit null parses identically to an absent key -- dispatchSuccessor"
else
  bad "NULL: expected action=dispatchSuccessor" "$out"
fi

# VALID: the ACCEPTED grant. Every refusal snapshot in section 2 is this one with a single field
# changed, so this leg is also what makes those legs single-variable comparisons rather than anecdotes.
VALID="$WORK/valid.json"
snapshot "$VALID" kite "$(grant kite fresh-critic host-9b63 bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb)"
out="$(run_engine "$VALID")"; rc=$?
if [ "$rc" -ne 0 ]; then
  bad "VALID: a well-formed grant must be exit 0" "$out"
else
  action="$(field "$out" '["action"]')"
  successor="$(field "$out" '["criticSuccessionReceipt"]["successorCriticIdentity"]')"
  if [ "$action" = "enterCriticSuccession" ] && [ "$successor" = "fresh-critic" ]; then
    ok "VALID: a matching, well-formed grant yields enterCriticSuccession with the receipt echoed on the wire"
  else
    bad "VALID: expected action=enterCriticSuccession with successorCriticIdentity=fresh-critic" "$out"
  fi
fi

# MALFORMED: a non-object, non-null value fails CLOSED and names the field -- never silently ignored,
# and never a `noVerdict` that could be mistaken for a read failure elsewhere in the protocol.
MALFORMED="$WORK/malformed.json"
snapshot "$MALFORMED" kite '"not-an-object"'
out="$(run_engine "$MALFORMED" 2>&1)"; rc=$?
if [ "$rc" -eq 0 ]; then
  bad "GATE INVERSION: a malformed criticSuccessionGranted value was silently accepted (exit 0)" "$out"
elif printf '%s' "$out" | grep -q "criticSuccessionGranted"; then
  ok "MALFORMED: a non-object value fails CLOSED (exit $rc) and names the field"
else
  bad "MALFORMED: failed closed but did not name the field -- a caller cannot tell which key is bad" "$out"
fi

# BLANK FIELDS: `readString` refuses an empty string and names it, so a grant naming no successor, or
# no accountable granter, never reaches `criticSuccessionValid` at all. See the header: since
# .github#2557 deleted the unreachable Core copy, THIS is the only place the property is enforced, and
# section 5 is the inversion that keeps that sole copy measured rather than merely asserted.
for blank in successorCriticIdentity grantedBy; do
  case "$blank" in
    successorCriticIdentity) g="$(grant kite "" host-9b63 bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb)"; what="a grant naming no successor" ;;
    *)                       g="$(grant kite fresh-critic "" bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb)"; what="a grant nobody accountable issued" ;;
  esac
  BLANKED="$WORK/blank-$blank.json"
  snapshot "$BLANKED" kite "$g"
  out="$(run_engine "$BLANKED" 2>&1)"; rc=$?
  if [ "$rc" -eq 0 ]; then
    bad "GATE INVERSION: $what was accepted onto the wire (exit 0) -- blank $blank never reached a refusal" "$out"
  elif printf '%s' "$out" | grep -q "$blank"; then
    ok "BLANK $blank: $what fails CLOSED at the wire (exit $rc) and names the field"
  else
    bad "BLANK $blank: failed closed but did not name the field -- a caller cannot tell which key is bad" "$out"
  fi
done

echo
echo "── 2. REFUSALS: one reachable conjunct of criticSuccessionValid per leg, one field off VALID each"

# The five REACHABLE refusals, in the order their conjuncts appear in `Review.criticSuccessionValid`.
# Fields are name|marker-critic|original|successor|granted-by|candidate-head|description.
REFUSALS=(
  "exact-critic|kite|kite-OTHER|fresh-critic|host-9b63|bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb|a grant naming a DIFFERENT critic than the one this round is stuck on"
  "generic-identity|fsgg-critic-best|fsgg-critic-best|fresh-critic|host-9b63|bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb|a grant whose critic is the bare agent-type string every critic at that route shares (#2451)"
  "exact-head|kite|kite|fresh-critic|host-9b63|aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa|a stale grant left over from an EARLIER head"
  "self-grant-successor|kite|kite|impl-worker|host-9b63|bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb|an implementer manufacturing ITSELF as the successor critic"
  "self-grant-granter|kite|kite|fresh-critic|impl-worker|bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb|an implementer manufacturing its OWN succession"
)

# refusal_snapshot <name> -- writes $WORK/<name>.json from the REFUSALS row and echoes its path.
refusal_snapshot() {
  local want="$1" row name critic orig succ granted head
  for row in "${REFUSALS[@]}"; do
    IFS='|' read -r name critic orig succ granted head _ <<<"$row"
    [ "$name" = "$want" ] || continue
    snapshot "$WORK/$name.json" "$critic" "$(grant "$orig" "$succ" "$granted" "$head")"
    printf '%s' "$WORK/$name.json"
    return 0
  done
  return 1
}

for row in "${REFUSALS[@]}"; do
  IFS='|' read -r name _ _ _ _ _ description <<<"$row"
  snap="$(refusal_snapshot "$name")"
  out="$(run_engine "$snap")"; rc=$?
  if [ "$rc" -ne 0 ]; then
    bad "$name: a refused grant must still be exit 0 (refusal is a verdict, not a read failure), got $rc" "$out"
    continue
  fi
  action="$(field "$out" '["action"]')"
  receipt="$(field "$out" '["criticSuccessionReceipt"]')"
  reason="$(field "$out" '["actionReason"]')"
  if [ "$action" = "enterCriticSuccession" ]; then
    bad "GATE INVERSION: $name was ADMITTED -- $description entered succession" "$out"
  elif [ "$action" != "dispatchSuccessor" ] || [ "$receipt" != "None" ]; then
    bad "$name: expected ordinary dispatchSuccessor with no legacy receipt echoed" "$out"
  elif ! printf '%s' "$reason" | grep -q "refused, not consumed"; then
    # THE NON-VACUITY ASSERTION. Without it this leg also passes when the snapshot never reached the
    # guard, and a leg that passes for a reason unrelated to its subject has measured nothing.
    bad "$name: dispatchSuccessor, but the reason does not say a legacy grant was REFUSED -- the guard may never have seen it" "$reason"
  else
    ok "$name: $description is refused, and the reason records that a grant was refused rather than absent"
  fi
done

echo
echo "── 3. LEDGER WRITE: can a granted successor actually RECORD a verdict (.github#2662)"

# The ledger cases. Each is an independent two-record chain on a FRESH fixture server:
#   record 1  initial, critic `tern-42`, changes-required, at head aaaa…  (the despawned critic)
#   record 2  the successor `snipe-8934` appending under test
# `grant` is the JSON for the record's `succession` object, or `none` for no grant at all.
#
#   name|kind|successor|succession-json|expect|description
LEDGER_CASES=(
  "granted-confirmation|confirmation|snipe-8934|GRANT|ordinary|a successor's confirmation (ordinary boundary; legacy grant remains readable)"
  "granted-escalation|escalation|snipe-8934|GRANT|accept|a granted successor's escalation"
  "granted-repair-phase|repair-phase|snipe-8934|GRANT|accept|a granted successor's repair-phase record"
  "ungranted-confirmation|confirmation|snipe-8934|none|ordinary|an ordinary fresh successor's confirmation"
  "mismatched-grant|confirmation|snipe-8934|MISMATCH|mismatch|a grant naming a critic who never held the seat"
)

GRANT_JSON='{"originalCritic":"tern-42","grantedBy":"heron-61d6","grantUrl":"https://github.com/FS-GG/.github/pull/2650#issuecomment-5302904754"}'
MISMATCH_JSON='{"originalCritic":"stranger-0000","grantedBy":"heron-61d6","grantUrl":"https://github.com/FS-GG/.github/pull/2650#issuecomment-5302904754"}'

# ledger_draft <outfile> <kind> <critic> <round> <initial-url> <preceding-url> <succession-json|none>
# `revision`/`digest` are deliberately left at 0/"" — the WRITER seals them, and a fixture that sealed
# them itself would be testing its own arithmetic instead of the engine's.
ledger_draft() {
  python3 - "$@" <<'PY'
import json, sys
path, kind, critic, round_number, initial, preceding, succession = sys.argv[1:]
record = {
    "schema": "fsgg.coord.review-decision/v2",
    "subject": "FS-GG/FS.GG.SDD#42/pr/42",
    "revision": 0,
    "previousDigest": None,
    "headSha": "a" * 40,
    "critic": critic,
    "verdict": "changes-required",
    "acceptedExceptions": [],
    "routeApplicability": "not-meaningful",
    "routeEvidence": ["hermetic ledger fixture"],
    "policyVersion": "structured-decisions/1",
    "kind": kind,
    "round": int(round_number),
    "initialReview": initial or None,
    "precedingReview": preceding or None,
    "diffAuditRequired": False,
    "diffAuditReceipts": [],
    "timestamp": "2026-08-15T00:00:00Z",
    "digest": "",
}
if succession != "none":
    record["succession"] = json.loads(succession)
with open(path, "w", encoding="utf-8") as stream:
    json.dump(record, stream, separators=(",", ":"))
PY
}

ledger_wait_draft() {
  python3 - "$@" <<'PY'
import json, sys
from datetime import datetime, timedelta, timezone
path, event, claim, generation, kind, evidence = sys.argv[1:]
now = datetime.now(timezone.utc).replace(microsecond=0)
if event == "enter":
    record = {"schema":"fsgg.coord.review-wait/v1","event":"enter","item":"FS-GG/FS.GG.SDD#42",
              "claimGeneration":claim,"reviewGeneration":generation,"kind":kind,
              "enteredAt":now.isoformat().replace("+00:00", "Z"),
              "expiresAt":(now + timedelta(hours=4)).isoformat().replace("+00:00", "Z"),
              "evidenceRef":evidence}
else:
    record = {"schema":"fsgg.coord.review-wait/v1","event":"complete","reviewGeneration":generation,
              "at":now.isoformat().replace("+00:00", "Z"),"evidenceRef":evidence}
with open(path, "w", encoding="utf-8") as stream:
    json.dump(record, stream, separators=(",", ":"))
PY
}

# ledger_case <dll> <kind> <successor> <succession-json|none> -- drives ONE chain against a fresh
# loopback server and echoes `<rc> <ledger-records-before> <ledger-records-after> <stderr-first-line>`.
# The record COUNT is what makes a refusal leg discriminating: `review record` validates before it
# posts, so a refusal that still appended a comment would be a different and worse defect than one
# that refused, and only the count can tell those apart.
ledger_case() {
  local dll="$1" kind="$2" successor="$3" succession="$4"
  local srv_out srv_pid port draft rc out before after initial_url claim_id second_round second_generation
  srv_out="$(mktemp "$WORK/ledger-srv.XXXXXX")"
  python3 "$REPO_ROOT/tests/coord-engine-e2e/stateful_server.py" >"$srv_out" 2>&1 &
  srv_pid=$!
  port=""
  for _ in $(seq 1 50); do port="$(head -n1 "$srv_out" 2>/dev/null)"; [ -n "$port" ] && break; sleep 0.1; done
  if [ -z "$port" ]; then
    kill "$srv_pid" 2>/dev/null
    printf '99 0 0 the loopback fixture never bound a port'
    return 0
  fi

  draft="$(mktemp "$WORK/ledger-draft.XXXXXX")"
  (
    export FSGG_GITHUB_API_BASE="http://127.0.0.1:$port"
    export GITHUB_TOKEN="fixture-token"
    export FSGG_COORD_OWNER="FS-GG"

    claim_id="$(curl -fsS -X POST -H 'Content-Type: application/json' \
      -d '{"body":"<!-- fsgg:claim worker=fixture-ledger lease=120 -->\nheld"}' \
      "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments" | python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])')"

    ledger_draft "$draft" initial tern-42 0 "" "" none
    ledger_wait_draft "$draft.wait" enter "$claim_id" "$(printf 'a%.0s' {1..40}):initial-review:0" initial-review queue
    dotnet "$dll" review wait FS.GG.SDD#42 "$draft.wait" --pr 42 --json >/dev/null 2>&1
    out="$(dotnet "$dll" review record FS.GG.SDD#42 "$draft" --pr 42 --json 2>&1)"
    initial_url="$(printf '%s' "$out" | python3 -c 'import json,sys; print(json.load(sys.stdin)["commentUrl"])' 2>/dev/null)"
    if [ -z "$initial_url" ]; then
      printf '98 0 0 the initial record could not be written: %s' "$(printf '%s' "$out" | head -1)"
      exit 0
    fi
    ledger_wait_draft "$draft.wait" complete "$claim_id" "$(printf 'a%.0s' {1..40}):initial-review:0" initial-review "$initial_url"
    dotnet "$dll" review wait FS.GG.SDD#42 "$draft.wait" --pr 42 --json >/dev/null 2>&1

    before="$(ledger_count "$port")"
    if [ "$kind" = "confirmation" ]; then
      second_round=1
      ledger_draft "$draft" "$kind" "$successor" 1 "$initial_url" "$initial_url" "$succession"
    else
      second_round=0
      ledger_draft "$draft" "$kind" "$successor" 0 "$initial_url" "$initial_url" "$succession"
    fi
    second_generation="$(printf 'a%.0s' {1..40}):repair-confirmation:$second_round"
    ledger_wait_draft "$draft.wait" enter "$claim_id" "$second_generation" repair-confirmation queue
    dotnet "$dll" review wait FS.GG.SDD#42 "$draft.wait" --pr 42 --json >/dev/null 2>&1
    out="$(dotnet "$dll" review record FS.GG.SDD#42 "$draft" --pr 42 --json 2>&1)"; rc=$?
    after="$(ledger_count "$port")"
    printf '%s %s %s %s' "$rc" "$before" "$after" "$(printf '%s' "$out" | head -1)"
  )
  kill "$srv_pid" 2>/dev/null
  wait "$srv_pid" 2>/dev/null
}

# ledger_count <port> -- how many `fsgg:review-decision/v2` comments the fixture is holding.
ledger_count() {
  python3 - "$1" <<'PY'
import json, sys, urllib.request
url = f"http://127.0.0.1:{sys.argv[1]}/repos/FS-GG/FS.GG.SDD/issues/42/comments"
with urllib.request.urlopen(url) as response:
    body = json.load(response)
print(sum(1 for c in body if c.get("body", "").startswith("<!-- fsgg:review-decision/v2 -->")))
PY
}

# ledger_legs <dll> <mode> -- `mode` is `pristine` (the accepting cases must accept) or `inverted`
# (they must all red, while the refusing cases stay refused).
ledger_legs() {
  local dll="$1" mode="$2" row name kind successor succession expect description succ result rc before after detail
  for row in "${LEDGER_CASES[@]}"; do
    IFS='|' read -r name kind successor succession expect description <<<"$row"
    case "$succession" in
      GRANT) succ="$GRANT_JSON" ;;
      MISMATCH) succ="$MISMATCH_JSON" ;;
      *) succ="none" ;;
    esac
    result="$(ledger_case "$dll" "$kind" "$successor" "$succ")"
    read -r rc before after detail <<<"$result"

    if [ "$rc" = "98" ] || [ "$rc" = "99" ]; then
      bad "$name: the chain could not be set up -- NOT MEASURED, which is not the same as passing" "$detail"
      continue
    fi

    if { [ "$expect" = "accept" ] && [ "$mode" = "pristine" ]; } || [ "$expect" = "ordinary" ]; then
      if [ "$rc" -ne 0 ]; then
        bad "$name: $description must be RECORDABLE, the writer refused it (exit $rc)" "$detail"
      elif [ "$after" != "$((before + 1))" ]; then
        bad "$name: the writer reported success but appended no record ($before -> $after)" "$detail"
      else
        ok "$name: $description is written to the ledger under the successor's own identity"
      fi
    elif [ "$expect" = "accept" ]; then
      if [ "$rc" -eq 0 ]; then
        bad "$name INVERSION SURVIVED: the record was still accepted with the succession allowance removed -- this leg measures something else" "$detail"
      else
        ok "$name inversion: with the allowance removed, the accepting leg REDS (it is bound to the admission it names)"
      fi
    elif [ "$expect" = "mismatch" ] && [ "$mode" = "inverted" ]; then
      if [ "$rc" -eq 0 ] && [ "$after" = "$((before + 1))" ]; then
        ok "$name inversion: deleting legacy grant awareness exposes the ordinary confirmation boundary"
      else
        bad "$name inversion: expected the now-unseen legacy grant to reduce to an ordinary confirmation" "$detail"
      fi
    else
      # The refusing cases are asserted in BOTH modes. In `inverted` they are the control that stops
      # "everything went red" being mistaken for a bound inversion.
      if [ "$rc" -eq 0 ]; then
        bad "$name: $description must be REFUSED -- continuity was weakened generally" "$detail"
      elif [ "$after" != "$before" ]; then
        bad "$name: refused, but a comment was appended anyway ($before -> $after) -- validation must precede the post" "$detail"
      elif [ "$name" = "ungranted-confirmation" ] \
           && ! printf '%s' "$detail" | grep -q "every record in one review generation must bind the same critic"; then
        bad "$name: refused with a DIFFERENT message -- the pre-existing continuity refusal must be unchanged" "$detail"
      else
        ok "$name ($mode): $description is refused and nothing is appended"
      fi
    fi
  done
}

ledger_legs "$ENGINE_DLL" pristine

echo
echo "── 4. GATE INVERSION: delete each conjunct, its own refusal leg must RED"

# Anchors are EXACT source text from `Review.criticSuccessionValid`, in `Review.fs`. Same discipline as
# `tests/coord-engine-mutation/specs.yml`: an anchor that no longer matches is a FAILURE here, never a
# quiet rebuild of an identical engine.
INVERSIONS=(
  "exact-critic|            receipt.OriginalCriticIdentity = critic"
  "generic-identity|            && not (isGenericCriticIdentity critic)"
  "exact-head|            && receipt.CandidateHeadSha = binding.HeadSha"
  "self-grant-successor|            && receipt.SuccessorCriticIdentity <> binding.ImplementerIdentity"
  "self-grant-granter|            && receipt.GrantedBy <> binding.ImplementerIdentity"
)

inversion_ran=0
if [ "${SKIP_GATE_INVERSION:-}" = "1" ] && [ "${CI:-}" = "true" ]; then
  # The escape hatch is for somebody's inner loop, not for the gate. A CI run that skipped the
  # measurement would print the same green summary as one that made it.
  bad "SKIP_GATE_INVERSION=1 is set in CI -- the measurement IS this fixture; refusing to skip it"
elif [ "${SKIP_GATE_INVERSION:-}" = "1" ]; then
  echo "SKIP  gate inversion (SKIP_GATE_INVERSION=1) -- local inner loop only; CI refuses this"
else
  MUT="$WORK/mutant"
  mkdir -p "$MUT"
  # Copy only what the Core+Cli build needs. `.agents` is in the list because `FS.GG.Coord.Cli.fsproj`
  # embeds two scripts from `.agents/skills/work-roadmap/scripts` as <None> content -- the build FAILS
  # without them, and a failed mutant build must never be mistaken for a surviving inversion.
  for d in src .agents Directory.Build.props Directory.Packages.props global.json; do
    cp -r "$REPO_ROOT/$d" "$MUT/" 2>/dev/null || true
  done
  rm -rf "$MUT"/src/*/bin "$MUT"/src/*/obj
  [ -d "$REPO_ROOT/dist/dotnet" ] && { mkdir -p "$MUT/dist"; cp -r "$REPO_ROOT/dist/dotnet" "$MUT/dist/"; }
  cp "$MUT/src/FS.GG.Coord.Core/Review.fs" "$WORK/Review.fs.pristine"
  MUT_DLL="$MUT/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine.dll"
  inversion_ran=1

  for row in "${INVERSIONS[@]}"; do
    IFS='|' read -r name anchor <<<"$row"
    cp "$WORK/Review.fs.pristine" "$MUT/src/FS.GG.Coord.Core/Review.fs"

    if ! ANCHOR="$anchor" python3 - "$MUT/src/FS.GG.Coord.Core/Review.fs" <<'PY'
import os, sys

path, anchor = sys.argv[1], os.environ["ANCHOR"] + "\n"
src = open(path).read()
if anchor not in src:
    sys.exit(f"anchor no longer matches: {anchor.strip()!r}")
out = src.replace(anchor, "", 1)
# Deleting the FIRST conjunct leaves the `when` clause opening with `&&`; promote what follows.
out = out.replace(
    "        | Some receipt, Some critic when\n            && ",
    "        | Some receipt, Some critic when\n            ",
    1,
)
if out == src:
    sys.exit("the mutation changed nothing")
open(path, "w").write(out)
PY
    then
      bad "$name inversion: the mutation did not apply -- NOT MEASURED, which is not the same as passing"
      continue
    fi

    if ! dotnet build "$MUT/src/FS.GG.Coord.Cli" -c Release >"$WORK/mutant-build.log" 2>&1; then
      bad "$name inversion: the mutant tree did not build; the inversion was not measured" \
          "$(tail -20 "$WORK/mutant-build.log")"
      continue
    fi

    snap="$(refusal_snapshot "$name")"
    out="$(run_engine "$snap" "$MUT_DLL")"; rc=$?
    action="$(field "$out" '["action"]')"
    if [ "$rc" -ne 0 ]; then
      bad "$name inversion: the mutant engine did not return a verdict (exit $rc)" "$out"
    elif [ "$action" = "enterCriticSuccession" ]; then
      ok "$name inversion: with that conjunct deleted, its refusal leg REDS (the leg is bound to the refusal it names)"
    else
      bad "$name INVERSION SURVIVED: the refusal still held with its conjunct deleted (action=$action) -- that leg measures something else" "$out"
    fi
  done

  cp "$WORK/Review.fs.pristine" "$MUT/src/FS.GG.Coord.Core/Review.fs"

  # ── the sixth inversion, and the one that runs the other way round ────────────────────────────────
  #
  # Section 3's gate ADMITS, so removing a conjunct from it cannot red anything — weakening a
  # conjunction only ever admits more. The mutation that measures it is the one that stops
  # `validateReviewLedger` from ever SEEING a grant: the two-armed match collapses to the arm it had
  # before .github#2662, which is exactly the code that refused two live successions this session.
  # Its accepting legs must then red, and — the half that makes it a measurement rather than a
  # demolition — its refusing legs must stay green.
  LEDGER_ANCHOR='                      match record.Succession with'
  LEDGER_MUTATION='                      match (if true then None else record.Succession) with'
  cp "$MUT/src/FS.GG.Coord.Core/StructuredDecision.fs" "$WORK/StructuredDecision.fs.pristine"
  if ! ANCHOR="$LEDGER_ANCHOR" REPLACEMENT="$LEDGER_MUTATION" python3 - "$MUT/src/FS.GG.Coord.Core/StructuredDecision.fs" <<'PY'
import os, sys

path = sys.argv[1]
anchor, replacement = os.environ["ANCHOR"] + "\n", os.environ["REPLACEMENT"] + "\n"
src = open(path).read()
if anchor not in src:
    sys.exit(f"anchor no longer matches: {anchor.strip()!r}")
out = src.replace(anchor, replacement, 1)
if out == src:
    sys.exit("the mutation changed nothing")
open(path, "w").write(out)
PY
  then
    bad "succession-allowance inversion: the mutation did not apply -- NOT MEASURED, which is not the same as passing"
  elif ! dotnet build "$MUT/src/FS.GG.Coord.Cli" -c Release >"$WORK/mutant-build.log" 2>&1; then
    bad "succession-allowance inversion: the mutant tree did not build; the inversion was not measured" \
        "$(tail -20 "$WORK/mutant-build.log")"
  else
    echo
    echo "   (re-running section 3's legs against the engine that cannot see a grant)"
    ledger_legs "$MUT_DLL" inverted
  fi
  cp "$WORK/StructuredDecision.fs.pristine" "$MUT/src/FS.GG.Coord.Core/StructuredDecision.fs"

  echo
  echo "── 5. WIRE INVERSION: delete readString's blank refusal, section 1's BLANK legs must FLIP"

  # .github#2557. The blank-field property has exactly ONE home — `ReviewApplication.readString` — and
  # the two conjuncts that used to restate it in `Review.fs` were unreachable and are gone. A sole copy
  # that nothing inverts is the same false green as an unwired fixture, so this deletes that copy in the
  # scratch tree and requires section 1's BLANK legs to stop refusing. They are re-run from the SAME
  # snapshot files section 1 built, so a flip cannot be explained by a different input.
  WIRE_ANCHOR='        if String.IsNullOrWhiteSpace parsed then invalidArg name "must not be empty"'
  cp "$MUT/src/FS.GG.Coord.Cli/ReviewApplication.fs" "$WORK/ReviewApplication.fs.pristine"
  if ! ANCHOR="$WIRE_ANCHOR" python3 - "$MUT/src/FS.GG.Coord.Cli/ReviewApplication.fs" <<'PY'
import os, sys

path = sys.argv[1]
anchor = os.environ["ANCHOR"] + "\n"
src = open(path).read()
if anchor not in src:
    sys.exit(f"anchor no longer matches: {anchor.strip()!r}")
out = src.replace(anchor, "", 1)
if out == src:
    sys.exit("the mutation changed nothing")
open(path, "w").write(out)
PY
  then
    bad "wire inversion: the mutation did not apply -- NOT MEASURED, which is not the same as passing"
  elif ! dotnet build "$MUT/src/FS.GG.Coord.Cli" -c Release >"$WORK/mutant-build.log" 2>&1; then
    bad "wire inversion: the mutant tree did not build; the inversion was not measured" \
        "$(tail -20 "$WORK/mutant-build.log")"
  else
    for blank in successorCriticIdentity grantedBy; do
      out="$(run_engine "$WORK/blank-$blank.json" "$MUT_DLL")"; rc=$?
      action="$(field "$out" '["action"]')"
      if [ "$rc" -ne 0 ]; then
        bad "BLANK $blank INVERSION SURVIVED: the engine still refused it with readString's blank check deleted (exit $rc) -- that leg measures something else" "$out"
      elif [ "$action" = "enterCriticSuccession" ]; then
        ok "BLANK $blank inversion: with readString's refusal deleted the grant is ADMITTED (the wire leg is bound to the refusal it names, and this is what the removed Review.fs conjunct would have caught)"
      else
        bad "BLANK $blank inversion: the mutant returned a verdict but not the admission this leg is bound to (action=$action)" "$out"
      fi
    done
  fi
  cp "$WORK/ReviewApplication.fs.pristine" "$MUT/src/FS.GG.Coord.Cli/ReviewApplication.fs"
fi

# ---- non-vacuity floor -------------------------------------------------------------------------------
# A gutted fixture exits 0 as happily as a whole one (#266, #436). Pin the leg count, so deleting legs
# is a red gate rather than a quiet one. 6 wire + 5 refusals + 5 ledger writes, plus 5 `Review.fs`
# inversions, section 3's 5 legs re-run against the allowance-free engine, and .github#2557's 2 wire
# inversions, when they ran.
floor=16
[ "$inversion_ran" -eq 1 ] && floor=28
if [ "$failcount" -eq 0 ] && [ "$pass" -lt "$floor" ]; then
  bad "non-vacuity: only $pass leg(s) ran, expected at least $floor -- legs have been deleted, and a suite that asserts less than it claims is the defect this fixture exists to catch"
fi

echo
echo "review-critic-succession-wire fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ]
