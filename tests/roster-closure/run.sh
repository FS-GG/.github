#!/usr/bin/env bash
# Fixture for scripts/check-roster-closure.py — the gate on the closed-world assumption behind
# registry/repos.yml (.github#269, epic #266 instance (c)). Proves the gate PASSES on a closed world
# and FAILS on every way the world can be open: a dependencies.yml participant that is not rostered
# (the FS.GG.Audio defect), a repo live in the org but rostered nowhere, a stale or contradictory
# `outside-fabric:` exemption, and — the fails-open traps this epic is about — an org listing that is
# empty, unreachable, or too narrow to see the repos we already know exist.
#
# The org listing is injected with --org-repos-json, so this runs offline. Mirrors
# tests/repos-registry/run.sh in shape: throwaway trees under a temp dir, no network, no board writes.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
TOOL="$HERE/../../scripts/check-roster-closure.py"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/roster-closure-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

# A two-repo world: the authority plus one framework repo, both rostered, both contract participants.
ROSTER="$WORK/repos.yml"
cat > "$ROSTER" <<'YAML'
schemaVersion: 1
updated: 2026-07-09
authority: FS-GG/.github
repos:
  - { id: .github, full: FS-GG/.github,   role: authority, receives: [labels] }
  - { id: sdd,     full: FS-GG/FS.GG.SDD, role: framework, receives: [labels, coordination-kit] }
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

# run <roster> <deps> <live-json> — the gate, never touching the network
run() { python3 "$TOOL" --roster "$1" --deps "$2" --org FS-GG --org-repos-json "$3" 2>&1; }

# expect_fail <name> <needle> <roster> <deps> <live> — exit 1 AND the message says why
expect_fail() {
  local name="$1" needle="$2" out rc
  out="$(run "$3" "$4" "$5")" && rc=0 || rc=$?
  if [ "$rc" -eq 0 ]; then
    bad "$name (gate reported GREEN — fails open)" "$out"
  elif grep -qF "$needle" <<<"$out"; then
    ok "$name"
  else
    bad "$name (failed, but not for the stated reason: want '$needle')" "$out"
  fi
}

# --- 1. the closed world passes -------------------------------------------------------------------
out="$(run "$ROSTER" "$DEPS" "$LIVE")" && ok "a closed world passes" || bad "closed world" "$out"

# --- 2. the FS.GG.Audio defect, both directions ----------------------------------------------------
# (A) registry closure: a dependencies.yml participant with no roster row.
DEPS_AUDIO="$WORK/deps-audio.yml"
sed 's|contracts: \[\]|  audio:  { name: FS.GG.Audio, role: "game audio" }\ncontracts: []|' "$DEPS" > "$DEPS_AUDIO"
expect_fail "dependencies.yml participant absent from the roster fails (registry closure)" \
  "FS-GG/FS.GG.Audio is a contract participant" "$ROSTER" "$DEPS_AUDIO" "$LIVE"

# (B) org closure: a repo live in the org, rostered nowhere. This is the half no other gate could
#     report — repos-audit only iterates repos that ARE in the roster.
LIVE_AUDIO="$WORK/live-audio.json"
printf '["FS-GG/.github", "FS-GG/FS.GG.SDD", "FS-GG/FS.GG.Audio"]\n' > "$LIVE_AUDIO"
expect_fail "repo live in the org but rostered nowhere fails (org closure)" \
  "exists in the GitHub org but is in NEITHER" "$ROSTER" "$DEPS" "$LIVE_AUDIO"

# --- 3. the fails-open traps this epic exists to close ---------------------------------------------
# An empty listing cannot distinguish "the org is empty" from "this token sees nothing".
EMPTY="$WORK/empty.json"; printf '[]\n' > "$EMPTY"
expect_fail "an EMPTY org listing fails closed" \
  "returned ZERO repos" "$ROSTER" "$DEPS" "$EMPTY"

# A listing that omits a repo we KNOW exists (because we rostered it) is too narrow to prove absence.
# Without this, a token that cannot see the org would report a vacuously-closed world.
NARROW="$WORK/narrow.json"; printf '["FS-GG/.github"]\n' > "$NARROW"
expect_fail "a listing missing a rostered repo fails (unreachable subject, not vacuous green)" \
  "did NOT come back from" "$ROSTER" "$DEPS" "$NARROW"

# An unreachable subject is an error, never a skip.
rc=0; out="$(python3 "$TOOL" --roster "$ROSTER" --deps "$DEPS" --org FS-GG \
        --org-repos-json "$WORK/does-not-exist.json" 2>&1)" || rc=$?
{ [ "$rc" -ne 0 ] && grep -qF "Failing closed" <<<"$out"; } \
  && ok "an unreadable org listing fails closed" || bad "unreadable listing" "$out"

# A dependencies.yml with no repos: block would make check (A) vacuously true.
DEPS_NOREPOS="$WORK/deps-norepos.yml"; printf 'schemaVersion: 1\ncontracts: []\n' > "$DEPS_NOREPOS"
expect_fail "an absent dependencies.yml repos: block fails (no vacuous pass)" \
  "no \`repos:\` block" "$ROSTER" "$DEPS_NOREPOS" "$LIVE"

# --- 4. the opt-out list is a reviewed claim, not a mute button -------------------------------------
# Honored when it names a repo that really is live and really is unrostered.
ROSTER_EXEMPT="$WORK/repos-exempt.yml"
sed 's|outside-fabric: \[\]|outside-fabric:\n  - { full: FS-GG/Scratch.Repo, reason: "spike, deliberately outside every fabric" }|' \
  "$ROSTER" > "$ROSTER_EXEMPT"
LIVE_SCRATCH="$WORK/live-scratch.json"
printf '["FS-GG/.github", "FS-GG/FS.GG.SDD", "FS-GG/Scratch.Repo"]\n' > "$LIVE_SCRATCH"
out="$(run "$ROSTER_EXEMPT" "$DEPS" "$LIVE_SCRATCH")" \
  && ok "an explicit outside-fabric exemption is honored" || bad "exemption honored" "$out"

# A stale exemption is a standing licence to ignore a repo that no longer exists.
expect_fail "an outside-fabric row naming a repo not in the org fails (stale exemption)" \
  "Remove the stale exemption" "$ROSTER_EXEMPT" "$DEPS" "$LIVE"

# Both rostered and exempt is a self-contradiction: the fabrics would iterate it either way.
ROSTER_BOTH="$WORK/repos-both.yml"
sed 's|outside-fabric: \[\]|outside-fabric:\n  - { full: FS-GG/FS.GG.SDD, reason: "contradiction" }|' \
  "$ROSTER" > "$ROSTER_BOTH"
expect_fail "a repo both rostered and exempt fails" \
  "BOTH rostered and listed" "$ROSTER_BOTH" "$DEPS" "$LIVE"

# A dependencies.yml whose repos: is the wrong SHAPE must not crash into a traceback either.
DEPS_LIST="$WORK/deps-list.yml"; printf 'schemaVersion: 1\nrepos:\n  - FS.GG.SDD\ncontracts: []\n' > "$DEPS_LIST"
expect_fail "a mis-shaped dependencies.yml repos: block fails cleanly" \
  "expected a mapping" "$ROSTER" "$DEPS_LIST" "$LIVE"

# --- 4. the opt-out list is a reviewed claim, not a mute button -------------------------------------
# (continued) the authority repo sits at index 0 of repos[]; jq's `index()` returns 0 for it, which
# is a truthy-to-`jq -e` value. A validator that treated index 0 as "not found" would let the org's
# own authority repo be exempted from the fabric it sources. Pinned here on purpose.
ROSTER_BOTH0="$WORK/repos-both0.yml"
sed 's|outside-fabric: \[\]|outside-fabric:\n  - { full: FS-GG/.github, reason: "index-0 contradiction" }|' \
  "$ROSTER" > "$ROSTER_BOTH0"
expect_fail "the repos[0] repo being both rostered and exempt fails (jq index()==0 is a match)" \
  "BOTH rostered and listed" "$ROSTER_BOTH0" "$DEPS" "$LIVE"

# --- 5. --skip-org is loud, and still runs the offline half -----------------------------------------
rc=0; out="$(python3 "$TOOL" --roster "$ROSTER" --deps "$DEPS_AUDIO" --skip-org 2>&1)" || rc=$?
{ [ "$rc" -ne 0 ] && grep -qF "contract participant" <<<"$out"; } \
  && ok "--skip-org still enforces registry closure" || bad "--skip-org enforces (A)" "$out"
# Expects exit 0; guard it anyway, or `set -e` would abort the run instead of reporting a clean FAIL.
rc=0; out="$(python3 "$TOOL" --roster "$ROSTER" --deps "$DEPS" --skip-org 2>&1)" || rc=$?
{ [ "$rc" -eq 0 ] && grep -qF "org closure NOT checked" <<<"$out"; } \
  && ok "--skip-org announces the unanswered question" || bad "--skip-org is loud" "$out"

# --- 6. repos.sh validates the outside-fabric shape --------------------------------------------------
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

# --- 7. CI guard on the real, checked-in registry ----------------------------------------------------
# Offline half only: the org half needs the network and runs as its own workflow step.
if python3 "$TOOL" --roster "$REPO_ROOT/registry/repos.yml" \
     --deps "$REPO_ROOT/registry/dependencies.yml" --skip-org >/dev/null 2>&1; then
  ok "the checked-in registry closes over dependencies.yml"
else
  bad "real registry closure" \
    "$(python3 "$TOOL" --roster "$REPO_ROOT/registry/repos.yml" --deps "$REPO_ROOT/registry/dependencies.yml" --skip-org 2>&1)"
fi

echo "roster-closure fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::roster-closure fixture FAILED"; exit 1; }
echo "roster-closure fixture — OK"
