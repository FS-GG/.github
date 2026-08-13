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
# THE THREE SECTIONS, and each answers a question the one before it cannot.
#
#   1. WIRE (6 legs) — the four from #2417, still load-bearing, plus two this rewrite moved here.
#      Can the engine READ a grant off the JSON wire: absent key, explicit null, well-formed object,
#      a non-object that must fail CLOSED naming the field, and a blank `successorCriticIdentity` or
#      `grantedBy` — which must ALSO fail closed naming its field. A refusal that never receives the
#      receipt refuses nothing.
#
#      WHY THOSE TWO ARE HERE AND NOT IN SECTION 2, which is a finding and not a filing choice
#      (.github#2557, and .github#2537 AC5 asked for exactly this to be reported rather than papered
#      over). `Review.criticSuccessionValid` has SEVEN conjuncts, and two of them —
#      `not (String.IsNullOrWhiteSpace receipt.SuccessorCriticIdentity)` and the same for `GrantedBy` —
#      cannot be reached from this route, or from any route in `src/`. `ReviewApplication.readString`
#      refuses an empty string and names the field, `criticSuccessionReceipt` reads EVERY field through
#      it, and that is the only site in `src/` that constructs a `CriticSuccessionReceipt`. So a blank
#      value fails at parse, exit 1, and the guard never runs. Writing those two as REFUSAL legs was
#      this fixture's first draft and it failed honestly: the engine exits 1 where the leg expected a
#      verdict. They are pinned here, as the wire-layer refusal that actually enforces the property,
#      and they get NO gate-inversion leg in section 3 — deleting either conjunct from `Review.fs`
#      changes nothing this fixture, or any suite in this repo, can observe. Claiming inversion
#      evidence for them would be the exact false green .github#2537 was filed about. Delete this
#      paragraph when .github#2557 resolves which of the two copies is the real guard.
#
#   2. REFUSALS (5 legs) — one per REACHABLE conjunct of `Review.criticSuccessionValid`: the four
#      .github#2537 names (exact-critic, exact-head, generic-identity, self-grant) counted as five,
#      because "self-grant" is two conjuncts — the successor and the granter — and an implementer can
#      manufacture a succession through either. Every one of the five snapshots differs from the
#      ACCEPTED grant in exactly one field, so what it measures is that field and not a coincidence.
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
#   3. GATE INVERSION (5 legs) — the measurement, on the precedent set by
#      `tests/review-post-acceptance-head-move/run.sh` leg 6. For each conjunct: delete THAT conjunct
#      from `Review.fs` in a scratch copy of the tree, rebuild, and require THAT conjunct's refusal leg
#      to flip to `enterCriticSuccession`. One mutant per refusal, ~10s each, because a single mutant
#      with every conjunct deleted would show that the legs can red without showing that any leg is
#      bound to the refusal it is named for.
#
#      A mutation whose anchor text no longer matches FAILS here rather than silently rebuilding an
#      identical engine — the `tests/coord-engine-mutation/` rule, for the same reason: a leg that
#      matched nothing grades NOT MEASURED, and NOT MEASURED spelled like PASSED is the defect this
#      whole file is about.
#
# WHY A SHELL FIXTURE AND NOT `tests/FS.GG.Coord.Core.Tests/ReviewTests.fs`. Unchanged from #2417 and
# still true at #2537: it drives the COMPILED `fsgg-coord-engine review --snapshot` — the pure DECISION
# path, no board, no token, no network — and asserts on JSON stdout and exit code. Section 3 needs to
# rebuild the engine from mutated source five times, which a unit suite inside that same build cannot
# do to itself.
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
# The binding is fixed: head `head2`, implementer `impl-worker`, ordinary phase, round 1. The marker
# carries `verdict: changes-required` at `reviewed-head: head1`, so the classifier reaches the
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
import json, sys

out, critic, granted = sys.argv[1], sys.argv[2], sys.argv[3]
facts = {
    "comments": [
        {
            "id": 1,
            "url": "https://reviews/1",
            "body": (
                "<!-- fsgg:independent-review:v1 -->\n"
                f"critic: {critic}\nreviewed-head: head1\nverdict: changes-required"
            ),
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
            "headSha": "head2",
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
  if [ "$action" != "resumeSameCritic" ] || [ "$receipt" != "None" ]; then
    bad "ABSENT: expected action=resumeSameCritic and no receipt" "$out"
  elif printf '%s' "$reason" | grep -q "refused, not consumed"; then
    bad "ABSENT: the pre-#2417 reason text must be UNCHANGED (there was no grant to refuse)" "$reason"
  else
    ok "ABSENT: no key at all parses as no grant -- resumeSameCritic, unchanged reason, no receipt echoed"
  fi
fi

# NULL: the same fact spelled out explicitly.
NULLED="$WORK/nulled.json"
snapshot "$NULLED" kite null
out="$(run_engine "$NULLED")"; rc=$?
if [ "$rc" -ne 0 ]; then
  bad "NULL: an explicit null must be exit 0, got $rc" "$out"
elif [ "$(field "$out" '["action"]')" = "resumeSameCritic" ]; then
  ok "NULL: an explicit null parses identically to an absent key -- resumeSameCritic"
else
  bad "NULL: expected action=resumeSameCritic" "$out"
fi

# VALID: the ACCEPTED grant. Every refusal snapshot in section 2 is this one with a single field
# changed, so this leg is also what makes those legs single-variable comparisons rather than anecdotes.
VALID="$WORK/valid.json"
snapshot "$VALID" kite "$(grant kite fresh-critic host-9b63 head2)"
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
# no accountable granter, never reaches `criticSuccessionValid` at all. See the header: this is where
# the property is actually enforced, and .github#2557 is where the duplicate Core conjuncts are decided.
for blank in successorCriticIdentity grantedBy; do
  case "$blank" in
    successorCriticIdentity) g="$(grant kite "" host-9b63 head2)"; what="a grant naming no successor" ;;
    *)                       g="$(grant kite fresh-critic "" head2)"; what="a grant nobody accountable issued" ;;
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
  "exact-critic|kite|kite-OTHER|fresh-critic|host-9b63|head2|a grant naming a DIFFERENT critic than the one this round is stuck on"
  "generic-identity|fsgg-critic-best|fsgg-critic-best|fresh-critic|host-9b63|head2|a grant whose critic is the bare agent-type string every critic at that route shares (#2451)"
  "exact-head|kite|kite|fresh-critic|host-9b63|head1|a stale grant left over from an EARLIER head"
  "self-grant-successor|kite|kite|impl-worker|host-9b63|head2|an implementer manufacturing ITSELF as the successor critic"
  "self-grant-granter|kite|kite|fresh-critic|impl-worker|head2|an implementer manufacturing its OWN succession"
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
  elif [ "$action" != "resumeSameCritic" ] || [ "$receipt" != "None" ]; then
    bad "$name: expected resumeSameCritic with no receipt echoed" "$out"
  elif ! printf '%s' "$reason" | grep -q "refused, not consumed"; then
    # THE NON-VACUITY ASSERTION. Without it this leg also passes when the snapshot never reached the
    # guard, and a leg that passes for a reason unrelated to its subject has measured nothing.
    bad "$name: resumeSameCritic, but the reason does not say a grant was REFUSED -- the guard may never have seen it" "$reason"
  else
    ok "$name: $description is refused, and the reason records that a grant was refused rather than absent"
  fi
done

echo
echo "── 3. GATE INVERSION: delete each conjunct, its own refusal leg must RED"

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
fi

# ---- non-vacuity floor -------------------------------------------------------------------------------
# A gutted fixture exits 0 as happily as a whole one (#266, #436). Pin the leg count, so deleting legs
# is a red gate rather than a quiet one. 6 wire + 5 refusals, plus 5 inversions when they ran.
floor=11
[ "$inversion_ran" -eq 1 ] && floor=16
if [ "$failcount" -eq 0 ] && [ "$pass" -lt "$floor" ]; then
  bad "non-vacuity: only $pass leg(s) ran, expected at least $floor -- legs have been deleted, and a suite that asserts less than it claims is the defect this fixture exists to catch"
fi

echo
echo "review-critic-succession-wire fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ]
