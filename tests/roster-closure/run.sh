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
  python3 "$TOOL" --roster "$roster" --deps "$deps" --org FS-GG \
    --org-repos-json "$live" --org-meta-json "$meta" 2>&1
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
rc=0; out="$(python3 "$TOOL" --roster "$ROSTER" --deps "$DEPS" --org FS-GG \
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
rc=0; out="$(python3 "$TOOL" --roster "$ROSTER" --deps "$DEPS_AUDIO" --skip-org 2>&1)" || rc=$?
{ [ "$rc" -ne 0 ] && grep -qF "contract participant" <<<"$out"; } \
  && ok "--skip-org still enforces registry closure" || bad "--skip-org enforces (A)" "$out"
# Expects exit 0; guard it anyway, or `set -e` would abort the run instead of reporting a clean FAIL.
rc=0; out="$(python3 "$TOOL" --roster "$ROSTER" --deps "$DEPS" --skip-org 2>&1)" || rc=$?
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
     --deps "$REPO_ROOT/registry/dependencies.yml" --skip-org >/dev/null 2>&1; then
  ok "the checked-in registry closes over dependencies.yml"
else
  bad "real registry closure" \
    "$(python3 "$TOOL" --roster "$REPO_ROOT/registry/repos.yml" --deps "$REPO_ROOT/registry/dependencies.yml" --skip-org 2>&1)"
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
rc=0; out="$(python3 "$TOOL" --roster "$ROSTER" --deps "$DEPS" --org FS-GG \
        --org-repos-json "$LIVE" --org-meta-json "$WORK/meta-does-not-exist.json" 2>&1)" || rc=$?
{ [ "$rc" -eq 3 ] && grep -qF "could not read org metadata" <<<"$out"; } \
  && ok "unreadable org metadata is no verdict (exit 3)" || bad "unreadable metadata (want exit 3)" "$out"

# A mis-shaped org meta (a JSON array, not an object) must be a clean no-verdict, never a traceback —
# the same "no crash into a traceback" contract the mis-shaped dependencies.yml test holds for (A).
META_BADSHAPE="$WORK/meta-bad-shape.json"; printf '[1, 2, 3]\n' > "$META_BADSHAPE"
expect_noverdict "a mis-shaped org meta (array, not object) is a clean no-verdict, not a traceback" \
  "could not read org metadata" "$ROSTER" "$DEPS" "$LIVE" "$META_BADSHAPE"

echo "roster-closure fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::roster-closure fixture FAILED"; exit 1; }
echo "roster-closure fixture — OK"
