#!/usr/bin/env bash
# Fixture for scripts/check-roster-closure.py — the gate on the closed-world assumption behind
# registry/repos.yml (.github#269, epic #266 instance (c)). Proves the gate PASSES on a closed world
# and FAILS on every way the world can be open: a dependencies.yml participant that is not rostered
# (the FS.GG.Audio defect), a repo live in the org but rostered nowhere, a stale or contradictory
# `outside-fabric:` exemption, and — the fails-open traps this epic is about — an org listing that is
# empty, unreachable, or too narrow to see the repos we already know exist.
#
# The gate distinguishes THREE outcomes, and the split is the point (#1154): exit 0 = the world is
# closed; exit 1 = a VIOLATION a human should fix in the roster; exit 3 = NO VERDICT, the gate could
# not look — an unreachable/empty/too-narrow listing, or a token that cannot prove it sees the whole
# org. `expect_finding` (exit 1) and `expect_noverdict` (exit 3) hold those apart, so a regression
# that collapses one into the other — the very defect this file guards — fails a test.
#
# Both the org listing (--org-repos-json) and the org metadata (--org-meta-json) are injected, so
# this runs offline. Mirrors tests/repos-registry/run.sh in shape: throwaway trees under a temp dir,
# no network, no board writes.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
TOOL="$HERE/../../scripts/check-roster-closure.py"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/roster-closure-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

# A two-repo world: the authority plus one framework repo, both rostered, both contract participants.
#
# The `capabilities:` block is not incidental here. A capability a repo RECEIVES must declare how it is
# DETECTED at that receiver, or repos-audit sweeps it in neither direction while it stays a legal
# `receives:` word — the #628 hole. This roster rosters `labels` and `coordination-kit`, so it has to
# say how each is verified: `coordination-kit` by the reusable workflow the receiver calls, `labels`
# not at all, because the AUTHORITY pushes it (apply-labels.sh reads the roster and creates the labels
# via the API). `push: true` is how a roster says that out loud instead of leaving a blank.
ROSTER="$WORK/repos.yml"
cat > "$ROSTER" <<'YAML'
schemaVersion: 5
updated: 2026-07-13
authority: FS-GG/.github
repos:
  - { id: .github, full: FS-GG/.github,   role: authority, receives: [labels] }
  - { id: sdd,     full: FS-GG/FS.GG.SDD, role: framework, receives: [labels, coordination-kit] }
capabilities:
  - { id: coordination-kit, workflow: coordination-coherence.yml }
  - { id: skill-union, caller: skill-union, receivers: none, reason: retired shape kept for the reverse sweep; this is the fixture default }
  - { id: labels, push: true, reason: authority-pushed by apply-labels.sh; nothing is wired at the receiver }
outside-fabric: []
YAML

# `github` vs `.github` is deliberate: dependencies.yml keys the shared repo `github` and names it
# with a full owner/name, while repos.yml gives it id `.github`. Comparing ids would report a phantom
# drift; the gate must canonicalize both sides to the full name.
DEPS="$WORK/dependencies.yml"
cat > "$DEPS" <<'YAML'
schemaVersion: 1
repos:
  github: { name: FS-GG/.github, role: "authority" }
  sdd:    { name: FS.GG.SDD,     role: "spec-driven lifecycle CLI" }
contracts: []
YAML

LIVE="$WORK/live.json"
printf '["FS-GG/.github", "FS-GG/FS.GG.SDD"]\n' > "$LIVE"

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# run <roster> <deps> <live-json> [meta-json] — the gate, never touching the network.
#
# The COUNT leg of org-visibility compares the listing against the org's own repo total, which the
# gate reads from GET /orgs/{org}. When a test does not care about visibility (most do not — they
# exercise the findings or the green path), `run` SYNTHESIZES a matching meta so the count leg passes
# by construction: {public_repos: <#live>, total_private_repos: 0}. The visibility tests in section 8
# pass an explicit meta that disagrees with the listing on purpose.
run() {
  local roster="$1" deps="$2" live="$3" meta="${4:-}"
  if [ -z "$meta" ]; then
    meta="$WORK/auto-meta.json"
    python3 - "$live" > "$meta" <<'PY'
import json, sys
try:
    n = len(json.load(open(sys.argv[1])))
except Exception:
    n = 0
print(json.dumps({"public_repos": n, "total_private_repos": 0}))
PY
  fi
  # --skip-board: this file's directions A/B fixtures (sections 1-8) are not about the board — the
  # board direction gets its own fixtures in section 9, injected via --board-json rather than a live
  # `gh api graphql` call, which would make this offline suite hit the network and hang/fail in CI.
  python3 "$TOOL" --roster "$roster" --deps "$deps" --org FS-GG \
    --org-repos-json "$live" --org-meta-json "$meta" --skip-board 2>&1
}

# expect_finding <name> <needle> <roster> <deps> <live> [meta] — a VIOLATION: exit 1 AND why.
expect_finding() {
  local name="$1" needle="$2" out rc
  out="$(run "$3" "$4" "$5" "${6:-}")" && rc=0 || rc=$?
  if [ "$rc" -eq 0 ]; then
    bad "$name (gate reported GREEN — fails open)" "$out"
  elif [ "$rc" -ne 1 ]; then
    bad "$name (want exit 1 = violation, got $rc — a no-verdict must NOT read as a finding)" "$out"
  elif grep -qF "$needle" <<<"$out"; then
    ok "$name"
  else
    bad "$name (exit 1, but not for the stated reason: want '$needle')" "$out"
  fi
}

# expect_noverdict <name> <needle> <roster> <deps> <live> [meta] — a NO-VERDICT: exit 3 AND why.
# The #1154 distinction under test: a 'could not look' must be 3 — never 0 (a vacuous green) and
# never 1 (a finding a human would waste time trying to fix in the roster).
expect_noverdict() {
  local name="$1" needle="$2" out rc
  out="$(run "$3" "$4" "$5" "${6:-}")" && rc=0 || rc=$?
  if [ "$rc" -eq 0 ]; then
    bad "$name (gate reported GREEN — fails open)" "$out"
  elif [ "$rc" -ne 3 ]; then
    bad "$name (want exit 3 = no verdict, got $rc — a couldn't-look must NOT read as a finding)" "$out"
  elif grep -qF "$needle" <<<"$out"; then
    ok "$name"
  else
    bad "$name (no verdict, but not for the stated reason: want '$needle')" "$out"
  fi
}

# --- 1. the closed world passes -------------------------------------------------------------------
out="$(run "$ROSTER" "$DEPS" "$LIVE")" && ok "a closed world passes" || bad "closed world" "$out"

# --- 2. the FS.GG.Audio defect, both directions ----------------------------------------------------
# (A) registry closure: a dependencies.yml participant with no roster row.
DEPS_AUDIO="$WORK/deps-audio.yml"
sed 's|contracts: \[\]|  audio:  { name: FS.GG.Audio, role: "game audio" }\ncontracts: []|' "$DEPS" > "$DEPS_AUDIO"
expect_finding "dependencies.yml participant absent from the roster fails (registry closure)" \
  "FS-GG/FS.GG.Audio is a contract participant" "$ROSTER" "$DEPS_AUDIO" "$LIVE"

# (B) org closure: a repo live in the org, rostered nowhere. This is the half no other gate could
#     report — repos-audit only iterates repos that ARE in the roster.
LIVE_AUDIO="$WORK/live-audio.json"
printf '["FS-GG/.github", "FS-GG/FS.GG.SDD", "FS-GG/FS.GG.Audio"]\n' > "$LIVE_AUDIO"
expect_finding "repo live in the org but rostered nowhere fails (org closure)" \
  "exists in the GitHub org but is in NEITHER" "$ROSTER" "$DEPS" "$LIVE_AUDIO"

# --- 3. the fails-open traps this epic exists to close --------------------------------------------
# These are "could not look" — exit 3, distinct from a finding, so a transient outage does not send a
# human off to "fix" a roster that was fine (#1154).
# An empty listing cannot distinguish "the org is empty" from "this token sees nothing".
EMPTY="$WORK/empty.json"; printf '[]\n' > "$EMPTY"
expect_noverdict "an EMPTY org listing is no verdict (not a finding, not green)" \
  "returned ZERO repos" "$ROSTER" "$DEPS" "$EMPTY"

# A listing that omits a repo we KNOW exists (because we rostered it) is too narrow to prove absence.
# Without this, a token that cannot see the org would report a vacuously-closed world.
NARROW="$WORK/narrow.json"; printf '["FS-GG/.github"]\n' > "$NARROW"
expect_noverdict "a listing missing a rostered repo is no verdict (unreachable subject, not vacuous green)" \
  "did NOT come back from" "$ROSTER" "$DEPS" "$NARROW"

# An unreachable listing is no verdict, never a skip and never a finding.
rc=0; out="$(python3 "$TOOL" --roster "$ROSTER" --deps "$DEPS" --org FS-GG --skip-board \
        --org-repos-json "$WORK/does-not-exist.json" 2>&1)" || rc=$?
{ [ "$rc" -eq 3 ] && grep -qF "could not read the repo list" <<<"$out"; } \
  && ok "an unreadable org listing is no verdict (exit 3)" || bad "unreadable listing (want exit 3)" "$out"

# A dependencies.yml with no repos: block would make check (A) vacuously true. This is a definite
# offline violation (exit 1), not a no-verdict.
DEPS_NOREPOS="$WORK/deps-norepos.yml"; printf 'schemaVersion: 1\ncontracts: []\n' > "$DEPS_NOREPOS"
expect_finding "an absent dependencies.yml repos: block fails (no vacuous pass)" \
  "no \`repos:\` block" "$ROSTER" "$DEPS_NOREPOS" "$LIVE"

# --- 4. the opt-out list is a reviewed claim, not a mute button -----------------------------------
# Honored when it names a repo that really is live and really is unrostered.
ROSTER_EXEMPT="$WORK/repos-exempt.yml"
sed 's|outside-fabric: \[\]|outside-fabric:\n  - { full: FS-GG/Scratch.Repo, reason: "spike, deliberately outside every fabric" }|' \
  "$ROSTER" > "$ROSTER_EXEMPT"
LIVE_SCRATCH="$WORK/live-scratch.json"
printf '["FS-GG/.github", "FS-GG/FS.GG.SDD", "FS-GG/Scratch.Repo"]\n' > "$LIVE_SCRATCH"
out="$(run "$ROSTER_EXEMPT" "$DEPS" "$LIVE_SCRATCH")" \
  && ok "an explicit outside-fabric exemption is honored" || bad "exemption honored" "$out"

# A stale exemption is a standing licence to ignore a repo that no longer exists.
expect_finding "an outside-fabric row naming a repo not in the org fails (stale exemption)" \
  "Remove the stale exemption" "$ROSTER_EXEMPT" "$DEPS" "$LIVE"

# Both rostered and exempt is a self-contradiction: the fabrics would iterate it either way.
ROSTER_BOTH="$WORK/repos-both.yml"
sed 's|outside-fabric: \[\]|outside-fabric:\n  - { full: FS-GG/FS.GG.SDD, reason: "contradiction" }|' \
  "$ROSTER" > "$ROSTER_BOTH"
expect_finding "a repo both rostered and exempt fails" \
  "BOTH rostered and listed" "$ROSTER_BOTH" "$DEPS" "$LIVE"

# A dependencies.yml whose repos: is the wrong SHAPE must not crash into a traceback either.
DEPS_LIST="$WORK/deps-list.yml"; printf 'schemaVersion: 1\nrepos:\n  - FS.GG.SDD\ncontracts: []\n' > "$DEPS_LIST"
expect_finding "a mis-shaped dependencies.yml repos: block fails cleanly" \
  "expected a mapping" "$ROSTER" "$DEPS_LIST" "$LIVE"

# (continued) the authority repo sits at index 0 of repos[]; jq's `index()` returns 0 for it, which
# is a truthy-to-`jq -e` value. A validator that treated index 0 as "not found" would let the org's
# own authority repo be exempted from the fabric it sources. Pinned here on purpose.
ROSTER_BOTH0="$WORK/repos-both0.yml"
sed 's|outside-fabric: \[\]|outside-fabric:\n  - { full: FS-GG/.github, reason: "index-0 contradiction" }|' \
  "$ROSTER" > "$ROSTER_BOTH0"
expect_finding "the repos[0] repo being both rostered and exempt fails (jq index()==0 is a match)" \
  "BOTH rostered and listed" "$ROSTER_BOTH0" "$DEPS" "$LIVE"

# --- 5. --skip-org is loud, and still runs the offline half ---------------------------------------
rc=0; out="$(python3 "$TOOL" --roster "$ROSTER" --deps "$DEPS_AUDIO" --skip-org --skip-board 2>&1)" || rc=$?
{ [ "$rc" -ne 0 ] && grep -qF "contract participant" <<<"$out"; } \
  && ok "--skip-org still enforces registry closure" || bad "--skip-org enforces (A)" "$out"
# Expects exit 0; guard it anyway, or `set -e` would abort the run instead of reporting a clean FAIL.
rc=0; out="$(python3 "$TOOL" --roster "$ROSTER" --deps "$DEPS" --skip-org --skip-board 2>&1)" || rc=$?
{ [ "$rc" -eq 0 ] && grep -qF "org closure NOT checked" <<<"$out"; } \
  && ok "--skip-org announces the unanswered question" || bad "--skip-org is loud" "$out"

# --- 6. repos.sh validates the outside-fabric shape ------------------------------------------------
REPOS_SH="$REPO_ROOT/scripts/repos.sh"
expect_shape_fail() {
  local name="$1" needle="$2" f="$3" out rc
  out="$(bash "$REPOS_SH" validate --registry "$f" 2>&1)" && rc=0 || rc=$?
  if [ "${rc:-0}" -eq 0 ]; then bad "$name (validator reported OK)" "$out"
  elif grep -qF "$needle" <<<"$out"; then ok "$name"
  else bad "$name (wrong reason: want '$needle')" "$out"; fi
}
NO_REASON="$WORK/no-reason.yml"
sed 's|outside-fabric: \[\]|outside-fabric:\n  - { full: FS-GG/Scratch.Repo }|' "$ROSTER" > "$NO_REASON"
expect_shape_fail "an outside-fabric row without a reason is rejected" "needs a 'reason'" "$NO_REASON"
expect_shape_fail "a repo both rostered and exempt is rejected by repos.sh too" \
  "cannot be both inside and outside" "$ROSTER_BOTH"
expect_shape_fail "the repos[0] repo both rostered and exempt is rejected by repos.sh too" \
  "cannot be both inside and outside" "$ROSTER_BOTH0"

# The valid path: a well-formed exemption naming a repo that is NOT rostered must VALIDATE. The
# `&& err` idiom above must not trip `set -e` on the non-matching (jq -> null, exit 1) branch.
if bash "$REPOS_SH" validate --registry "$ROSTER_EXEMPT" >/dev/null 2>&1; then
  ok "a well-formed, non-rostered outside-fabric row validates"
else
  bad "valid exemption validates" "$(bash "$REPOS_SH" validate --registry "$ROSTER_EXEMPT" 2>&1)"
fi

# --- 7. CI guard on the real, checked-in registry -------------------------------------------------
# Offline half only: the org half needs the network and runs as its own workflow step.
if python3 "$TOOL" --roster "$REPO_ROOT/registry/repos.yml" \
     --deps "$REPO_ROOT/registry/dependencies.yml" --skip-org --skip-board >/dev/null 2>&1; then
  ok "the checked-in registry closes over dependencies.yml"
else
  bad "real registry closure" \
    "$(python3 "$TOOL" --roster "$REPO_ROOT/registry/repos.yml" --deps "$REPO_ROOT/registry/dependencies.yml" --skip-org --skip-board 2>&1)"
fi

# --- 8. org-visibility: the whole org, not just the rostered part (#1154) --------------------------
# The old trust check proved only that the token could see the ROSTERED repos, so a token blind to an
# unrostered PRIVATE repo reported a vacuously-closed world — a false green. Closure now also requires
# the listing to be at least as large as the org's OWN repo total (public + private), read from
# GET /orgs/{org}. These tests inject that meta so it disagrees with the listing on purpose.

# THE ACCEPTANCE CASE: a token that cannot list an existing unrostered repo (an invisible private one)
# must be NO VERDICT, not green. Listing = exactly the two rostered repos; the org total says three.
META_PRIV="$WORK/meta-invisible-private.json"
printf '{"public_repos": 2, "total_private_repos": 1}\n' > "$META_PRIV"
expect_noverdict "an invisible unrostered private repo yields no verdict, not a vacuous green (#1154)" \
  "cannot see the whole org" "$ROSTER" "$DEPS" "$LIVE" "$META_PRIV"

# A run-scoped token cannot READ the org's private-repo count at all (GET /orgs/{org} omits
# total_private_repos), so it cannot rule out an invisible private repo — also no verdict.
META_NOPRIV="$WORK/meta-no-private-field.json"
printf '{"public_repos": 2}\n' > "$META_NOPRIV"
expect_noverdict "a token that cannot read total_private_repos yields no verdict" \
  "did not report \`total_private_repos\`" "$ROSTER" "$DEPS" "$LIVE" "$META_NOPRIV"

# The listing is trusted and closure holds ONLY when the count is at least the org total: a green must
# survive a meta that agrees with the listing (two public, no private).
META_OK="$WORK/meta-all-visible.json"
printf '{"public_repos": 2, "total_private_repos": 0}\n' > "$META_OK"
out="$(run "$ROSTER" "$DEPS" "$LIVE" "$META_OK")" \
  && ok "an org whose whole repo total is visible passes" || bad "all-visible green" "$out"

# Unreadable org metadata is a could-not-look, distinct from a finding: valid listing, but the meta
# read fails.
rc=0; out="$(python3 "$TOOL" --roster "$ROSTER" --deps "$DEPS" --org FS-GG --skip-board \
        --org-repos-json "$LIVE" --org-meta-json "$WORK/meta-does-not-exist.json" 2>&1)" || rc=$?
{ [ "$rc" -eq 3 ] && grep -qF "could not read org metadata" <<<"$out"; } \
  && ok "unreadable org metadata is no verdict (exit 3)" || bad "unreadable metadata (want exit 3)" "$out"

# A mis-shaped org meta (a JSON array, not an object) must be a clean no-verdict, never a traceback —
# the same "no crash into a traceback" contract the mis-shaped dependencies.yml test holds for (A).
META_BADSHAPE="$WORK/meta-bad-shape.json"; printf '[1, 2, 3]\n' > "$META_BADSHAPE"
expect_noverdict "a mis-shaped org meta (array, not object) is a clean no-verdict, not a traceback" \
  "could not read org metadata" "$ROSTER" "$DEPS" "$LIVE" "$META_BADSHAPE"

# --- 9. a rostered repo the ORG DOES NOT OWN (.github#2245) ---------------------------------------
# `GET /orgs/{org}/repos` enumerates the org's own repositories and nothing else. Before this item the
# roster could not name anything else, so direction B could treat "rostered" and "org-owned" as one
# set. `.github#2206`'s maintainer decision rosters `EHotwagner/S.I.R.`, and an owner-blind roster leg
# would report it as an unreachable subject on every run, forever.
#
# THAT COSTS THE GATE, NOT THE MERGE — and the two are easy to confuse in the wrong direction. A
# permanent exit 3 here is NOT a standing red: `main()` skips `org_closure_findings` whenever the
# visibility legs return anything (`if nv: … else:`), and `coherence.yml:96-100` maps exit 3 to
# `::warning::` + `exit 0`. The observable result is a permanently GREEN required check with the whole
# of direction B switched off — the FS.GG.Audio shape stops being reported and nothing says so.
# FAIL-OPEN, which is why the leg below is not decoration: it is the one that catches the silence.
# These legs pin all three halves: the row is not graded, it is not silent, and excusing it does not
# switch off the direction that grades everybody else.
ROSTER_USEROWNED="$WORK/repos-user-owned.yml"
sed 's|^capabilities:|  - { id: sir, full: EHotwagner/S.I.R., role: non-participant, receives: [], reason: "org work on a user-owned repo (.github#2206)" }\ncapabilities:|' \
  "$ROSTER" > "$ROSTER_USEROWNED"

# THE ACCEPTANCE CASE. The listing holds exactly the two FS-GG repos and the org's own total agrees
# with it, so the world IS closed — the user-owned row is simply not this direction's subject.
out="$(run "$ROSTER_USEROWNED" "$DEPS" "$LIVE")" \
  && ok "a rostered repo the org does not own does not make org closure a no-verdict" \
  || bad "user-owned roster row broke org closure" "$out"

# ...AND IT IS NAMED. "Not graded" must never be reachable by silence: a row nobody mentions is
# indistinguishable from a row nobody checked, which is the #266 shape this whole registry fights.
if grep -qF "EHotwagner/S.I.R." <<<"$out" && grep -qF "does not own" <<<"$out"; then
  ok "the ungraded user-owned row is NAMED with its reason, not silently skipped"
else
  bad "the user-owned row was skipped silently" "$out"
fi
# It must also not be counted as verified: the green line reports what direction B established, and a
# row it never looked at cannot be inside that claim.
if grep -qF "does not grade them and does not count them as verified" <<<"$out"; then
  ok "the report states the ungraded row is not counted as verified"
else
  bad "the report does not say the ungraded row is uncounted" "$out"
fi

# THE GUARANTEE MUST NOT LEAK. Excusing user-owned rows may not weaken the leg for the org's OWN
# repos: a listing missing an FS-GG rostered repo is still the unreachable-subject no-verdict.
expect_noverdict "an ORG-owned rostered repo missing from the listing is still a no-verdict" \
  "did NOT come back from" "$ROSTER_USEROWNED" "$DEPS" "$NARROW"

# THE FAIL-OPEN LEG, and the reason it is worded as an exit-1 expectation rather than a green: an
# FS-GG repo live in the org and rostered nowhere must STILL be a finding while a user-owned row sits
# in the same roster. Owner-blind, this exact case exits 3 and the finding VANISHES — `main()` never
# reaches `org_closure_findings` once the visibility legs return anything — and CI would show a
# warning and a pass. `expect_finding` refuses both halves of that: it fails on exit 3 as loudly as on
# exit 0, so "the direction quietly stopped looking" cannot be mistaken for "nothing to report".
expect_finding "an unrostered ORG repo is still a finding alongside a user-owned row (owner-blind, this is where direction B goes SILENT)" \
  "exists in the GitHub org but is in NEITHER" "$ROSTER_USEROWNED" "$DEPS" "$LIVE_AUDIO"

# `outside-fabric:` is the other half of #2245's trap — the escape hatch was closed against exactly
# the repo that needed it. A user-owned exemption must not be reported as a STALE exemption merely
# because the org listing cannot contain it.
ROSTER_USEREXEMPT="$WORK/repos-user-exempt.yml"
sed 's|outside-fabric: \[\]|outside-fabric:\n  - { full: EHotwagner/rogue3, reason: "an external product this fabric does not track" }|' \
  "$ROSTER" > "$ROSTER_USEREXEMPT"
out="$(run "$ROSTER_USEREXEMPT" "$DEPS" "$LIVE")" \
  && ok "a user-owned outside-fabric row is not reported as a stale exemption" \
  || bad "user-owned exemption read as stale" "$out"
# A stale ORG-owned exemption is still a finding — the narrowing is by owner, not a blanket amnesty.
expect_finding "a stale ORG-owned exemption is still a finding alongside a user-owned one" \
  "Remove the stale exemption" "$ROSTER_EXEMPT" "$DEPS" "$LIVE"

# Both halves must survive `repos.sh validate`, or the fixture above is asserting over a roster that
# could never be committed (#2245 acceptance 1).
if bash "$REPOS_SH" validate --registry "$ROSTER_USEROWNED" >/dev/null 2>&1; then
  ok "the user-owned non-participant roster validates under repos.sh"
else
  bad "user-owned roster does not validate" "$(bash "$REPOS_SH" validate --registry "$ROSTER_USEROWNED" 2>&1)"
fi
if bash "$REPOS_SH" validate --registry "$ROSTER_USEREXEMPT" >/dev/null 2>&1; then
  ok "the user-owned outside-fabric roster validates under repos.sh"
else
  bad "user-owned exemption does not validate" "$(bash "$REPOS_SH" validate --registry "$ROSTER_USEREXEMPT" 2>&1)"
fi

# --- 10. BOARD closure (direction C, .github#2206) -------------------------------------------------
# Every case here runs OFFLINE via --board-json (never `gh api graphql`) and --skip-org, so direction
# B contributes nothing and only direction C's verdict shows through. Direction A still runs against
# $DEPS, which names only repos $ROSTER already rosters, so it stays green throughout this section.

# run_board <roster> <board-json> — direction C alone, offline.
run_board() {
  local roster="$1" board="$2"
  python3 "$TOOL" --roster "$roster" --deps "$DEPS" --skip-org --board-json "$board" 2>&1
}

# THE ACCEPTANCE CASE THIS ITEM WAS FILED FOR: a schedulable board row naming a repository absent
# from both repos.yml and outside-fabric: is a violation, and it is NAMED — the .github#2206 shape.
BOARD_VIOLATION="$WORK/board-violation.json"
cat > "$BOARD_VIOLATION" <<'JSON'
[{"owner": "FS-GG", "repo": "Unrostered.Repo", "number": 42, "status": "Ready"}]
JSON
rc=0; out="$(run_board "$ROSTER" "$BOARD_VIOLATION")" || rc=$?
{ [ "$rc" -eq 1 ] && grep -qF "FS-GG/Unrostered.Repo" <<<"$out" && grep -qF "roster-closure-2206 shape" <<<"$out"; } \
  && ok "a schedulable row on an unrostered, unexempt repo is a BOARD closure violation" \
  || bad "board violation (want exit 1, named)" "$out"

# A row on a REPOS.YML-rostered repo is closed — no violation.
BOARD_ROSTERED="$WORK/board-rostered.json"
cat > "$BOARD_ROSTERED" <<'JSON'
[{"owner": "FS-GG", "repo": ".github", "number": 1, "status": "In review"}]
JSON
out="$(run_board "$ROSTER" "$BOARD_ROSTERED")" \
  && ok "a schedulable row on a rostered repo is closed" || bad "rostered row closed" "$out"

# A row on an OUTSIDE-FABRIC repo is closed too — the existing opt-out is BOARD direction's opt-out.
BOARD_EXEMPT="$WORK/board-exempt.json"
cat > "$BOARD_EXEMPT" <<'JSON'
[{"owner": "FS-GG", "repo": "Scratch.Repo", "number": 7, "status": "Ready"}]
JSON
out="$(run_board "$ROSTER_EXEMPT" "$BOARD_EXEMPT")" \
  && ok "a schedulable row on an outside-fabric repo is closed (reused opt-out, no new schema)" \
  || bad "outside-fabric row closed" "$out"

# A DONE row on an unrostered repo is NOT a violation — the maintainer's own predicate
# (.github#2206): closed history costs nothing, only a SCHEDULABLE row re-triggers. This is exactly
# what lets EHotwagner/rogue3 need no roster action while its only board row stays Done.
BOARD_DONE="$WORK/board-done.json"
cat > "$BOARD_DONE" <<'JSON'
[{"owner": "FS-GG", "repo": "Unrostered.ButDone", "number": 99, "status": "Done"}]
JSON
out="$(run_board "$ROSTER" "$BOARD_DONE")" \
  && ok "a Done row on an unrostered repo is NOT a violation (not schedulable)" \
  || bad "Done row should not violate" "$out"
# Status is graded case-insensitively, and an unset/empty status counts as non-Done (schedulable).
BOARD_DONE_CASE="$WORK/board-done-case.json"
cat > "$BOARD_DONE_CASE" <<'JSON'
[{"owner": "FS-GG", "repo": "Unrostered.ButDone", "number": 100, "status": "DONE"}]
JSON
out="$(run_board "$ROSTER" "$BOARD_DONE_CASE")" \
  && ok "Done is matched case-insensitively" || bad "case-insensitive Done" "$out"
BOARD_NOSTATUS="$WORK/board-nostatus.json"
cat > "$BOARD_NOSTATUS" <<'JSON'
[{"owner": "FS-GG", "repo": "Unrostered.NoStatus", "number": 101, "status": ""}]
JSON
rc=0; out="$(run_board "$ROSTER" "$BOARD_NOSTATUS")" || rc=$?
{ [ "$rc" -eq 1 ] && grep -qF "FS-GG/Unrostered.NoStatus" <<<"$out"; } \
  && ok "an unset/empty status counts as non-Done (schedulable)" \
  || bad "empty status should be schedulable" "$out"

# THE .github#2206 SHAPE ITSELF: a USER-OWNED repo with a schedulable row and no roster/exempt entry
# is a violation, proving direction C does not inherit direction B's org-enumeration blind spot — it
# never calls GET /orgs/{org}/repos at all, so a user-owned row is graded exactly like an org-owned
# one instead of being structurally invisible.
BOARD_USERVIOLATION="$WORK/board-user-violation.json"
cat > "$BOARD_USERVIOLATION" <<'JSON'
[{"owner": "EHotwagner", "repo": "rogue3", "number": 96, "status": "In review"}]
JSON
rc=0; out="$(run_board "$ROSTER" "$BOARD_USERVIOLATION")" || rc=$?
{ [ "$rc" -eq 1 ] && grep -qF "EHotwagner/rogue3" <<<"$out"; } \
  && ok "a SCHEDULABLE user-owned repo with no disposition is a violation (no org-enumeration blind spot)" \
  || bad "user-owned schedulable violation" "$out"

# A user-owned repo that IS rostered (role: non-participant, .github#2245's mechanism) is closed —
# the S.I.R. shape.
BOARD_USERROSTERED="$WORK/board-user-rostered.json"
cat > "$BOARD_USERROSTERED" <<'JSON'
[{"owner": "EHotwagner", "repo": "S.I.R.", "number": 138, "status": "In progress"}]
JSON
out="$(run_board "$ROSTER_USEROWNED" "$BOARD_USERROSTERED")" \
  && ok "a rostered non-participant user-owned repo (S.I.R. shape) is closed" \
  || bad "user-owned rostered row closed" "$out"

# A user-owned repo under outside-fabric: is closed too.
BOARD_USEREXEMPT="$WORK/board-user-exempt.json"
cat > "$BOARD_USEREXEMPT" <<'JSON'
[{"owner": "EHotwagner", "repo": "rogue3", "number": 96, "status": "Ready"}]
JSON
out="$(run_board "$ROSTER_USEREXEMPT" "$BOARD_USEREXEMPT")" \
  && ok "a user-owned outside-fabric repo is closed" || bad "user-owned exempt row closed" "$out"

# Multiple schedulable rows on the SAME unrostered repo collapse into ONE violation naming both.
BOARD_MULTI="$WORK/board-multi.json"
cat > "$BOARD_MULTI" <<'JSON'
[{"owner": "FS-GG", "repo": "Unrostered.Repo", "number": 42, "status": "Ready"},
 {"owner": "FS-GG", "repo": "Unrostered.Repo", "number": 43, "status": "Blocked"}]
JSON
rc=0; out="$(run_board "$ROSTER" "$BOARD_MULTI")" || rc=$?
nviol="$(grep -c '::error::roster-closure: FS-GG/Unrostered.Repo holds' <<<"$out" || true)"
{ [ "$rc" -eq 1 ] && [ "$nviol" -eq 1 ] && grep -qF "#42" <<<"$out" && grep -qF "#43" <<<"$out"; } \
  && ok "multiple schedulable rows on one unrostered repo collapse into ONE violation naming both" \
  || bad "multi-row collapse" "$out"

# The `{"items": [...]}` wrapped shape (what a captured snapshot document looks like) is accepted,
# not just a bare array.
BOARD_WRAPPED="$WORK/board-wrapped.json"
cat > "$BOARD_WRAPPED" <<'JSON'
{"schema": "fsgg.coord.snapshot/1", "items": [{"owner": "FS-GG", "repo": ".github", "number": 1, "status": "Ready"}]}
JSON
out="$(run_board "$ROSTER" "$BOARD_WRAPPED")" \
  && ok "the wrapped {items: [...]} board-json shape is accepted" || bad "wrapped shape accepted" "$out"

# --- the fails-open traps AC-4 exists to close: "could not read the board" must fail CLOSED and be
# DISTINGUISHABLE from both a clean green and a violation (#266, #1154's board-direction analog).
rc=0; out="$(run_board "$ROSTER" "$WORK/board-does-not-exist.json")" || rc=$?
{ [ "$rc" -eq 3 ] && grep -qF "could not read the Coordination board" <<<"$out"; } \
  && ok "an unreadable board-json path is a no-verdict (exit 3), not a vacuous green" \
  || bad "unreadable board (want exit 3)" "$out"

BOARD_MALFORMED="$WORK/board-malformed.json"; printf '{"not": "a list"}\n' > "$BOARD_MALFORMED"
rc=0; out="$(run_board "$ROSTER" "$BOARD_MALFORMED")" || rc=$?
{ [ "$rc" -eq 3 ] && grep -qF "could not read the Coordination board" <<<"$out"; } \
  && ok "a malformed board-json (no items array) is a no-verdict, not a traceback" \
  || bad "malformed board (want exit 3)" "$out"

BOARD_EMPTY="$WORK/board-empty.json"; printf '[]\n' > "$BOARD_EMPTY"
rc=0; out="$(run_board "$ROSTER" "$BOARD_EMPTY")" || rc=$?
{ [ "$rc" -eq 3 ] && grep -qF "reported ZERO items" <<<"$out"; } \
  && ok "an EMPTY board read is a no-verdict (exit 3), not a vacuous green (AC-4)" \
  || bad "empty board (want exit 3)" "$out"

# A definite BOARD violation outranks a simultaneous no-verdict elsewhere, same precedence as A/B.
rc=0; out="$(python3 "$TOOL" --roster "$ROSTER" --deps "$DEPS" \
        --org-repos-json "$WORK/does-not-exist.json" --board-json "$BOARD_VIOLATION" 2>&1)" || rc=$?
{ [ "$rc" -eq 1 ] && grep -qF "FS-GG/Unrostered.Repo" <<<"$out"; } \
  && ok "a definite BOARD violation outranks a simultaneous org no-verdict" \
  || bad "violation outranks no-verdict" "$out"

# --skip-board is loud, offline, and leaves the board question unanswered — mirrors --skip-org.
rc=0; out="$(python3 "$TOOL" --roster "$ROSTER" --deps "$DEPS" --skip-org --skip-board 2>&1)" || rc=$?
{ [ "$rc" -eq 0 ] && grep -qF "board closure NOT checked" <<<"$out"; } \
  && ok "--skip-board announces the unanswered question" || bad "--skip-board is loud" "$out"

echo "roster-closure fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::roster-closure fixture FAILED"; exit 1; }
echo "roster-closure fixture — OK"
