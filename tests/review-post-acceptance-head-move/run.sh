#!/usr/bin/env bash
# Fixture for the post-acceptance head-move recovery (.github#2527) — chain retirement in
# `Driver.liveReviewComments`, read by `Review.classify`.
#
# THE DEFECT, MEASURED LIVE. `.github#2512` / PR #2514 was reviewed and host-accepted at head
# `f1d6218d`, deliberately not merged (its `landable` was red on a non-required arm), and by the time
# that cleared, sibling merges had conflicted the branch. The worker merged `origin/main`, moving the
# head to `8bb57aae`. The only honest response to a moved head is a full fresh review — and it was not
# ceremony: the fresh review caught release notes claiming `src/FS.GG.Coord.*` was "byte-identical to
# 0.50.4", true when first reviewed and FALSE after the merge, bound for an irreversible dual-feed
# publish through a gate that deliberately does not judge prose. But that fresh review posted a SECOND
# initial marker, and the engine fail-closed: "the initial review marker is carried by 2 comments;
# exactly one is required" — a message that describes the symptom and names no remedy. Every recovery
# path the contract had assumed the chain was INCOMPLETE (critic succession requires a stuck round; the
# repair phase requires exhaustion), so none fitted.
#
# WHAT THE FIX IS, AND WHAT IT MUST NOT BECOME. A chain is retired — dropped from the evidence the
# protocol classifies, never edited at the source — when a host-acceptance marker names that chain's
# initial-review comment URL AND carries an `accepted-head` other than the current head. That structure
# is written in the acceptance marker's own REQUIRED fields, so the engine OBSERVES it rather than being
# TOLD it. The distinction is the whole safety argument: the one-initial-marker rule is what stops a
# stranger silently continuing another critic's chain, so a mechanism that admitted a second chain on an
# ASSERTION would be a laundering route for exactly that. Legs 2-4 are that controlled counterpart, and
# they matter more than leg 1.
#
# WHY A SHELL FIXTURE AND NOT `tests/FS.GG.Coord.Core.Tests/ReviewTests.fs`. That path is declared by a
# DIFFERENT live claim (.github#2525) for the duration of this item, and `widen` refuses on OVERLAP
# rather than silently taking it. Same constraint, same answer, and the same precedent as
# `tests/review-critic-succession-wire/run.sh` (.github#2417): drive the COMPILED
# `fsgg-coord-engine review --snapshot` — the pure DECISION path, no board, no token, no network — and
# assert on its JSON stdout and exit code.
#
# LEG 6 IS THE GATE-INVERSION EVIDENCE and it is not a hand-wave: it rebuilds `Driver.fs` with
# `liveReviewComments` reduced to the identity function, in a scratch copy of the tree, and requires
# leg 1 to RED against that build. A gate whose inversion survives has measured nothing.
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

WORK="$(mktemp -d "${TMPDIR:-/tmp}/review-post-acceptance-head-move.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

jq_field() { printf '%s' "$1" | python3 -c 'import json,sys;d=json.load(sys.stdin);print(eval("d"+sys.argv[1]))' "$2" 2>/dev/null; }

# A passing initial/confirmation marker must carry route-applicability evidence, so every `pass` verdict
# below appends the not-meaningful form. This is the marker grammar the real critics write, not a
# fixture shortcut: a chain that omitted it would fail for an unrelated reason and prove nothing.
NOT_MEANINGFUL='\nroute-applicability: not-meaningful\nroute-not-meaningful-reason: a pure decision function with no runtime route'

# Chain A: initial review by `kite` at head1, host-accepted at head1.
CHAIN_A_INITIAL="{ \"id\": 1, \"url\": \"https://reviews/1\", \"body\": \"<!-- fsgg:independent-review:v1 -->\\ncritic: kite-ad75\\nreviewed-head: head1\\nverdict: pass${NOT_MEANINGFUL}\" }"
ACCEPT_A_AT_HEAD1="{ \"id\": 2, \"url\": \"https://reviews/2\", \"body\": \"<!-- fsgg:review-accepted:v1 -->\\naccepted-head: head1\\ninitial-review: https://reviews/1\\nlatest-confirmation: https://reviews/1\" }"
ACCEPT_A_AT_HEAD2="{ \"id\": 2, \"url\": \"https://reviews/2\", \"body\": \"<!-- fsgg:review-accepted:v1 -->\\naccepted-head: head2\\ninitial-review: https://reviews/1\\nlatest-confirmation: https://reviews/1\" }"
ACCEPT_A_DANGLING="{ \"id\": 2, \"url\": \"https://reviews/2\", \"body\": \"<!-- fsgg:review-accepted:v1 -->\\naccepted-head: head1\\ninitial-review: https://reviews/nowhere\\nlatest-confirmation: https://reviews/nowhere\" }"

# Chain B: a genuinely fresh initial review, by a DIFFERENT critic, of the head that will actually merge.
CHAIN_B_INITIAL="{ \"id\": 3, \"url\": \"https://reviews/3\", \"body\": \"<!-- fsgg:independent-review:v1 -->\\ncritic: kestrel-5595\\nreviewed-head: head2\\nverdict: pass${NOT_MEANINGFUL}\" }"

# $1 = output file, $2... = comment JSON objects. Binding head is always head2 — the CURRENT head.
snapshot() {
  local out="$1"; shift
  local comments=""
  local first=1
  for c in "$@"; do
    if [ "$first" -eq 1 ]; then comments="$c"; first=0; else comments="$comments,$c"; fi
  done
  cat > "$out" <<JSON
{
  "binding": {
    "itemRef": "FS-GG/.github#2527",
    "pr": 2528,
    "headSha": "head2",
    "claimGeneration": "gen-1",
    "implementerIdentity": "impl-worker",
    "phase": "ordinary",
    "round": 1
  },
  "facts": {
    "comments": [ $comments ],
    "checks": "green",
    "repairPhaseGranted": null,
    "repairRouteAvailable": true
  }
}
JSON
}

run_engine() { dotnet "${2:-$ENGINE_DLL}" review --snapshot "$1" --json; }

# ---- 1. RECOVERY: accepted at head1, head moved to head2, a fresh chain reviews head2 -----------------
RECOVERY="$WORK/recovery.json"
snapshot "$RECOVERY" "$CHAIN_A_INITIAL" "$ACCEPT_A_AT_HEAD1" "$CHAIN_B_INITIAL"

out="$(run_engine "$RECOVERY")"; rc=$?
if [ "$rc" -eq 0 ]; then
  state="$(jq_field "$out" '["state"]')"
  accepted="$(jq_field "$out" '["acceptedReceipt"]')"
  retired_url="$(jq_field "$out" '["retiredChains"][0]["initialReview"]')"
  retired_head="$(jq_field "$out" '["retiredChains"][0]["acceptedHead"]')"
  retired_n="$(printf '%s' "$out" | python3 -c 'import json,sys;print(len(json.load(sys.stdin)["retiredChains"]))' 2>/dev/null)"
  if [ "$state" = "malformedEvidence" ]; then
    bad "GATE INVERSION: the accepted-then-moved head still fails closed as malformedEvidence" "$out"
  elif [ "$state" = "awaitingHostAcceptance" ] \
       && [ "$accepted" = "None" ] \
       && [ "$retired_n" = "1" ] \
       && [ "$retired_url" = "https://reviews/1" ] \
       && [ "$retired_head" = "head1" ]; then
    ok "RECOVERY: the stale chain is retired, the fresh chain binds, acceptedReceipt stays null (AC-001/AC-006/AC-007)"
  else
    bad "RECOVERY: expected awaitingHostAcceptance, acceptedReceipt null, exactly one retired chain naming https://reviews/1 at head1" "$out"
  fi
else
  bad "RECOVERY: expected exit 0, got $rc" "$out"
fi

# ---- 2. NO ACCEPTANCE: two competing initial markers, nothing accepted -- STILL FAILS CLOSED ----------
#     This is AC5's controlled counterpart and the reason the mechanism is safe. Two initial markers with
#     no intervening accepted-then-moved head must refuse exactly as before, or the fix is a laundering
#     route for the stranger-continues-a-chain case the one-initial-marker rule exists to stop.
NOACCEPT="$WORK/no-acceptance.json"
snapshot "$NOACCEPT" "$CHAIN_A_INITIAL" "$CHAIN_B_INITIAL"

out="$(run_engine "$NOACCEPT")"; rc=$?
state="$(jq_field "$out" '["state"]')"
reason="$(jq_field "$out" '["actionReason"]')"
if [ "$state" = "malformedEvidence" ]; then
  if printf '%s' "$reason" | grep -q "no host-acceptance marker on this pull request names an initial review"; then
    ok "NO ACCEPTANCE: two competing initial markers still fail closed, and the refusal says why (AC-002/AC-005)"
  else
    bad "NO ACCEPTANCE: failed closed but the refusal did not name the retirement rule -- a reader gets no remedy" "$reason"
  fi
else
  bad "GATE INVERSION: two competing initial markers with no acceptance were ADMITTED (state=$state)" "$out"
fi

# ---- 3. ACCEPTANCE STILL BINDING: accepted-head IS the current head -- retires nothing ----------------
#     An acceptance that still binds has not been superseded by anything. If this retired the chain, any
#     critic could post a second initial marker on a freshly-accepted PR and have it silently win.
STILLBINDS="$WORK/still-binds.json"
snapshot "$STILLBINDS" "$CHAIN_A_INITIAL" "$ACCEPT_A_AT_HEAD2" "$CHAIN_B_INITIAL"

out="$(run_engine "$STILLBINDS")"; rc=$?
state="$(jq_field "$out" '["state"]')"
reason="$(jq_field "$out" '["actionReason"]')"
retired_n="$(printf '%s' "$out" | python3 -c 'import json,sys;print(len(json.load(sys.stdin)["retiredChains"]))' 2>/dev/null)"
if [ "$state" = "malformedEvidence" ] && [ "$retired_n" = "0" ]; then
  if printf '%s' "$reason" | grep -q "is the current head, so that chain still binds and is not retired"; then
    ok "STILL BINDING: an acceptance bound to the CURRENT head retires nothing, and the refusal says so (AC-003/AC-005)"
  else
    bad "STILL BINDING: failed closed but did not name which condition failed" "$reason"
  fi
else
  bad "GATE INVERSION: an acceptance still bound to the current head retired a chain (state=$state retired=$retired_n)" "$out"
fi

# ---- 4. DANGLING REFERENCE: the acceptance names an initial review that is not on this PR -------------
#     Retirement addresses a chain by the URL the acceptance itself carries. A reference matching nothing
#     must retire nothing -- never "the only other chain", which would let a malformed acceptance pick a
#     victim by elimination.
DANGLING="$WORK/dangling.json"
snapshot "$DANGLING" "$CHAIN_A_INITIAL" "$ACCEPT_A_DANGLING" "$CHAIN_B_INITIAL"

out="$(run_engine "$DANGLING")"; rc=$?
state="$(jq_field "$out" '["state"]')"
reason="$(jq_field "$out" '["actionReason"]')"
retired_n="$(printf '%s' "$out" | python3 -c 'import json,sys;print(len(json.load(sys.stdin)["retiredChains"]))' 2>/dev/null)"
if [ "$state" = "malformedEvidence" ] && [ "$retired_n" = "0" ]; then
  if printf '%s' "$reason" | grep -q "names no initial review marker on this pull request"; then
    ok "DANGLING: an acceptance naming no present initial review retires nothing, and says so (AC-004/AC-005)"
  else
    bad "DANGLING: failed closed but did not name which condition failed" "$reason"
  fi
else
  bad "GATE INVERSION: a dangling acceptance reference retired a chain (state=$state retired=$retired_n)" "$out"
fi

# ---- 5. SINGLE CHAIN, MOVED HEAD: unchanged from before this fix --------------------------------------
#     Retirement is a TIE-BREAKER between chains, never a re-classification of one. With one chain the
#     accepted-then-moved head is still refused, exactly as `ReviewTests.fs` `#2175 a changed head after
#     acceptance invalidates the prior accepted evidence` pins it. This leg is what proves the change
#     cannot move any verdict the protocol could already describe.
SINGLE="$WORK/single-chain.json"
snapshot "$SINGLE" "$CHAIN_A_INITIAL" "$ACCEPT_A_AT_HEAD1"

out="$(run_engine "$SINGLE")"; rc=$?
state="$(jq_field "$out" '["state"]')"
retired_n="$(printf '%s' "$out" | python3 -c 'import json,sys;print(len(json.load(sys.stdin)["retiredChains"]))' 2>/dev/null)"
if [ "$state" = "malformedEvidence" ] && [ "$retired_n" = "0" ]; then
  ok "SINGLE CHAIN: one chain accepted at a stale head is still refused, and retires nothing (AC-008 boundary)"
else
  bad "SINGLE CHAIN: the pre-existing single-chain refusal moved (state=$state retired=$retired_n)" "$out"
fi

# ---- 6. GATE INVERSION: delete the mechanism, leg 1 must RED ------------------------------------------
#     The mechanism is `Driver.liveReviewComments`. Reduce it to the identity function -- the exact shape
#     the code had before this change -- rebuild, and require the recovery leg to fail. A gate whose
#     inversion survives is a material finding by definition, so this measures rather than asserts.
if [ "${SKIP_GATE_INVERSION:-}" = "1" ]; then
  echo "SKIP  gate inversion (SKIP_GATE_INVERSION=1)"
else
  MUT="$WORK/mutant"
  mkdir -p "$MUT"
  # Copy only what the Core+Cli build needs; a full-tree copy would drag bin/obj and the test trees in.
  # `.agents/skills/work-roadmap/scripts` is in the copy list because `FS.GG.Coord.Cli.fsproj` embeds two
  # scripts from it as <None> content -- the build FAILS without them, and a failed mutant build would
  # look like a passing inversion to a fixture that only checked the exit code.
  for d in src .agents Directory.Build.props Directory.Packages.props global.json; do
    cp -r "$REPO_ROOT/$d" "$MUT/" 2>/dev/null || true
  done
  rm -rf "$MUT"/src/*/bin "$MUT"/src/*/obj
  [ -d "$REPO_ROOT/dist/dotnet" ] && { mkdir -p "$MUT/dist"; cp -r "$REPO_ROOT/dist/dotnet" "$MUT/dist/"; }

  python3 - "$MUT/src/FS.GG.Coord.Core/Driver.fs" <<'PY'
import re, sys
path = sys.argv[1]
src = open(path).read()
anchor = "    let liveReviewComments (currentHead: string) (comments: ReviewComment list) : LiveReviewComments =\n"
end = "    let private parseReviewCommentsCore"
start = src.index(anchor)
stop = src.index(end)
mutant = anchor + "        { Live = comments; Retired = []; Diagnostics = [] }\n\n"
open(path, "w").write(src[:start] + mutant + src[stop:])
print("mutated: liveReviewComments reduced to the identity function")
PY

  if dotnet build "$MUT/src/FS.GG.Coord.Cli" -c Release >"$WORK/mutant-build.log" 2>&1; then
    MUT_DLL="$MUT/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine.dll"
    out="$(run_engine "$RECOVERY" "$MUT_DLL")"; rc=$?
    state="$(jq_field "$out" '["state"]')"
    if [ "$state" = "malformedEvidence" ]; then
      ok "GATE INVERSION: with liveReviewComments reduced to identity, the recovery leg REDS (AC-009)"
    else
      bad "GATE INVERSION SURVIVED: the recovery still passed without the mechanism (state=$state) -- leg 1 measures nothing" "$out"
    fi
  else
    bad "GATE INVERSION: the mutant tree did not build; the inversion was not measured" "$(tail -20 "$WORK/mutant-build.log")"
  fi
fi

echo
echo "review-post-acceptance-head-move fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ]
