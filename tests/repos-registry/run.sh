#!/usr/bin/env bash
# Fixture for scripts/repos.sh — the validator/query for registry/repos.yml (the org repo roster,
# ADR-0019). Proves validate PASSES on a well-formed roster and FAILS on each violation class the
# gate must catch (unknown capability, duplicate id, bad role, not-exactly-one-authority, authority
# receiving the kit, kit digest drift, missing kit source, malformed repo name, duplicate kit id,
# two kit skill rows whose sources share a destination basename), that `list` returns
# the right repos for a capability, that `digest` follows the dir->SKILL.md / file rule, and — as the
# CI guard on the real file — that the checked-in registry/repos.yml validates. Mirrors
# tests/skill-union/run.sh and tests/fsgg-coord/run.sh: throwaway trees under a temp dir, no network.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPOS_SH="$HERE/../../scripts/repos.sh"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/repos-registry-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

# A throwaway root whose kit sources exist, so digest checks have something real to hash — and whose
# .github/workflows exist, because a capability row names the reusable workflow that audits it and
# validate now proves that workflow is really there and really callable (#503).
ROOT="$WORK/root"
mkdir -p "$ROOT/.claude/skills/demo-skill" "$ROOT/scripts" "$ROOT/.github/workflows"
printf '# demo skill\n' > "$ROOT/.claude/skills/demo-skill/SKILL.md"
printf '#!/usr/bin/env bash\necho hi\n' > "$ROOT/scripts/democlient"
printf 'on:\n  workflow_call:\njobs:\n  x:\n    runs-on: ubuntu-latest\n' \
  > "$ROOT/.github/workflows/coordination-coherence.yml"
printf 'on:\n  workflow_call:\njobs:\n  x:\n    runs-on: ubuntu-latest\n' \
  > "$ROOT/.github/workflows/lockfile-sync.yml"
# A workflow that is NOT reusable: no `workflow_call:` trigger, so no repo could ever `uses:` it.
printf 'on:\n  push:\njobs:\n  x:\n    runs-on: ubuntu-latest\n' \
  > "$ROOT/.github/workflows/not-reusable.yml"
# A SCRIPT-delivered capability names a script in scripts/, and validate proves it is really there —
# the audit greps receivers for a reference to it, so a script that does not exist would report every
# receiver unwired (#628). Same guarantee the `workflow:` detector already gets.
printf '#!/usr/bin/env bash\necho drift-check\n' > "$ROOT/scripts/sync-build-config.sh"
SKILL_SHA="$(sha256sum "$ROOT/.claude/skills/demo-skill/SKILL.md" | cut -d' ' -f1)"
CLIENT_SHA="$(sha256sum "$ROOT/scripts/democlient" | cut -d' ' -f1)"

# EVERY roster in this file rosters `receives: [labels, …]`, and until #628 no `capabilities:` row for
# `labels` existed anywhere — not here, and not in the real registry. So a capability could be legal to
# receive and impossible to detect, and was swept in neither direction. That is now a hard failure, so
# every roster must declare how `labels` is verified; the honest answer is that it is not verifiable at
# the receiver at all, because the AUTHORITY pushes it (apply-labels.sh reads the roster and creates
# the labels via the API). `push: true` is how a roster says that out loud, and `validate` refuses it
# without a reason.
LABELS_CAP='  - { id: labels, push: true, reason: authority-pushed by apply-labels.sh; nothing is wired at the receiver }'

BASE="$WORK/base.yml"
cat > "$BASE" <<YAML
schemaVersion: 5
updated: 2026-07-13
authority: FS-GG/.github
repos:
  - { id: .github, full: FS-GG/.github,   role: authority, receives: [labels] }
  - { id: sdd,     full: FS-GG/FS.GG.SDD, role: framework, receives: [labels, coordination-kit] }
capabilities:
  - { id: coordination-kit, workflow: coordination-coherence.yml }
$LABELS_CAP
kit:
  - { id: demo-skill, kind: skill,  source: .claude/skills/demo-skill }
  - { id: democlient, kind: client, source: scripts/democlient }
YAML

# capreg <name> <sdd-receives> <capability-row>… — BASE's repos + kit, with a custom capabilities
# block. The rows are multi-line YAML, so the `variant` sed helper cannot express them.
capreg() {
  local n="$1" recv="$2"; shift 2
  local f="$WORK/$n.yml"
  { printf 'schemaVersion: 5\nupdated: 2026-07-13\nauthority: FS-GG/.github\nrepos:\n'
    printf '  - { id: .github, full: FS-GG/.github,   role: authority, receives: [labels] }\n'
    printf '  - { id: sdd,     full: FS-GG/FS.GG.SDD, role: framework, receives: [%s] }\n' "$recv"
    printf 'kit:\n'
    printf '  - { id: demo-skill, kind: skill,  source: .claude/skills/demo-skill }\n'
    printf '  - { id: democlient, kind: client, source: scripts/democlient }\n'
    printf 'capabilities:\n'
    printf '  %s\n' "$@"
    # Every roster here rosters `labels`, so every roster here must say how `labels` is DETECTED, or
    # the #628 closure rejects it — correctly, and for a reason no leg below is trying to test.
    printf '%s\n' "$LABELS_CAP"; } > "$f"
  relock "$f"
  printf '%s' "$f"
}

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# relock <registry> — regenerate <registry>'s sibling .lock (#527). The digests moved OUT of the
# roster into a generated lock, so a registry with no lock is INVALID by construction. Every helper
# that mints a registry locks it, so a new variant cannot forget and then fail for the wrong reason.
relock() { bash "$REPOS_SH" relock --registry "$1" --root "$ROOT" >/dev/null 2>&1 || true; }

# variant <name> <sed-expr> — copy BASE, apply one mutation, echo the path
variant() { local f="$WORK/$1.yml"; sed "$2" "$BASE" > "$f"; relock "$f"; printf '%s' "$f"; }

# expect_pass <name> <registry>
expect_pass() {
  local n="$1" reg="$2" out
  if out="$(bash "$REPOS_SH" validate --registry "$reg" --root "$ROOT" 2>&1)"; then ok "$n"
  else bad "$n" "$out"; fi
}
# expect_fail <name> <expected-exit> <substr> <registry>
expect_fail() {
  local n="$1" want="$2" substr="$3" reg="$4" out rc=0
  out="$(bash "$REPOS_SH" validate --registry "$reg" --root "$ROOT" 2>&1)" || rc=$?
  if [ "$rc" -eq 0 ]; then bad "$n" "expected failure, passed"; return; fi
  if [ "$rc" -ne "$want" ]; then bad "$n" "expected exit $want, got $rc: $out"; return; fi
  case "$out" in *"$substr"*) ok "$n — $(printf '%s' "$out" | grep -m1 "$substr" | sed 's/::error::repos-registry: //')" ;;
                 *) bad "$n" "exit $rc but missing substring '$substr': $out" ;; esac
}

echo "repos-registry fixture"

relock "$BASE"   # the roster's digests live in its sibling lock now (#527)

# --- happy path ---
expect_pass "valid roster passes" "$BASE"

# --- violation classes (all exit 1) ---
expect_fail "unknown receives capability"     1 "unknown capability" "$(variant unknowncap  's/coordination-kit\]/bogus-cap]/')"
expect_fail "duplicate repo id"               1 "duplicate"          "$(variant dupid       's/id: sdd,/id: .github,/')"
expect_fail "invalid role"                    1 "role"               "$(variant badrole     's/role: framework/role: banana/')"
expect_fail "not exactly one authority"       1 "exactly one"        "$(variant twoauth     's/role: framework/role: authority/')"
expect_fail "authority receives the kit"      1 "must not RECEIVE"   "$(variant authkit     's/full: FS-GG\/.github,   role: authority, receives: \[labels\]/full: FS-GG\/.github,   role: authority, receives: [labels, coordination-kit]/')"
expect_fail "kit source missing"              1 "source missing"     "$(variant nosource    's/source: scripts\/democlient/source: scripts\/nope/')"
expect_fail "kit id is not kebab/dotted"      1 "kit id"             "$(variant badkitid    's/id: demo-skill,/id: Demo Skill,/')"
expect_fail "malformed repo full name"        1 "FS-GG"              "$(variant badfull     's/full: FS-GG\/FS.GG.SDD/full: GH\/FS.GG.SDD/')"
# --- the kit lock: the digests moved OUT of the roster into a generated artifact (#527) -----------
# This section replaces the old single "kit digest drift" assertion, and must be at LEAST as strong:
# the digest guarantee is the whole reason the kit is content-addressed, and it now lives in a file
# the roster does not contain. Every way the lock can stop being a faithful function of the roster is
# a fail-open on the receivers' bytes (#266), so each gets its own leg.
LOCKED="$(variant locked 's/^$//')"            # a pristine, correctly-locked copy of BASE
LOCKF="${LOCKED%.yml}.lock"

expect_pass "lock: a correctly-locked roster passes" "$LOCKED"

# Drift: the source changed and nobody relocked. This is the old "kit digest drift" case, moved.
cp "$LOCKF" "$WORK/locked.lock.bak"
sed -i "s/^$SKILL_SHA/0000000000000000000000000000000000000000000000000000000000000000/" "$LOCKF"
expect_fail "lock: a STALE digest fails (the old drift case, now in the lock)" 1 "STALE" "$LOCKED"
cp "$WORK/locked.lock.bak" "$LOCKF"

# Absent: no lock at all. Must NOT read as "nothing to check" — that is the #266 fail-open exactly.
mv "$LOCKF" "$WORK/locked.lock.away"
expect_fail "lock: a MISSING lock fails closed, not 'nothing to check'" 1 "lock missing" "$LOCKED"
mv "$WORK/locked.lock.away" "$LOCKF"

# Incomplete: a kit row the lock does not cover is an UNGUARDED kit item — its receivers' bytes could
# drift with nothing to say so. The whole-file comparison catches it; assert that it does.
grep -v 'scripts/democlient' "$LOCKF" > "$WORK/partial" && mv "$WORK/partial" "$LOCKF"
expect_fail "lock: a kit source the lock OMITS fails (an unguarded kit item)" 1 "STALE" "$LOCKED"
cp "$WORK/locked.lock.bak" "$LOCKF"

# Stale extra: a pin that outlived its roster row. Nothing consumes it, and it quietly implies a
# guarantee about a source the kit no longer ships.
printf '%s  scripts/gone\n' "$CLIENT_SHA" >> "$LOCKF"
expect_fail "lock: a pin for a source NOT in the roster fails (a stale pin)" 1 "STALE" "$LOCKED"
cp "$WORK/locked.lock.bak" "$LOCKF"

# Split truth: the digest creeping back into the roster. Reject it rather than tolerate-and-ignore —
# a tolerated field gets hand-edited, and a hand-edited digest nothing checks is the silent staleness
# this whole move exists to prevent. Two places to state one fact is the bug, not the symptom.
expect_fail "lock: a 'sha256:' back in a kit row is SPLIT TRUTH and is rejected" 1 "sha256" \
  "$(variant shabackinroster 's|source: .claude/skills/demo-skill }|source: .claude/skills/demo-skill, sha256: 0000000000000000000000000000000000000000000000000000000000000000 }|')"

# relock is a pure function of the roster + tree: running it twice is a no-op. If it were not, the
# gate would go red on a clean tree and workers would learn to ignore it.
relock "$LOCKED"; cp "$LOCKF" "$WORK/once"; relock "$LOCKED"
if diff -q "$WORK/once" "$LOCKF" >/dev/null; then ok "lock: relock is idempotent"
else bad "lock: relock is NOT idempotent" "a generator that never settles reds a clean tree"; fi

# A roster with NO kit: block ships no kit, so it needs no lock — several rosters in
# tests/roster-closure are exactly this. The exemption is narrow, and the next assertion is why it is
# not a hole.
NOKIT="$WORK/nokit.yml"
sed '/^kit:/,$d' "$BASE" > "$NOKIT"; rm -f "${NOKIT%.yml}.lock"
expect_pass "lock: a roster with no kit: block needs no lock" "$NOKIT"

# ...but DELETING the kit: block from a roster that HAS a lock must not silence the check. Every pin
# in the lock is now an orphan — a standing guarantee about a kit the roster no longer ships — and
# the whole-file comparison still runs whenever a lock exists. This is the leg that keeps the
# exemption above from becoming a #266 fail-open: "no kit" excuses the lock only when there is
# genuinely no kit, never when someone removed one and left its pins behind.
cp "$WORK/locked.lock.bak" "${NOKIT%.yml}.lock"
expect_fail "lock: dropping kit: while the lock still has pins fails (orphan pins, not a free pass)" \
  1 "STALE" "$NOKIT"
rm -f "${NOKIT%.yml}.lock"

# Two kit rows that collide at the receiver — the registry is valid but the fabric cannot honour it
# (.github#348). A duplicate id is the pre-#347 vector; a shared skill-source basename is the post-#347
# one, because coordination-sync writes each skill to <root>/<basename source>/SKILL.md.
expect_fail "duplicate kit id"                 1 "duplicate kit id"   "$(variant dupkitid    's/id: democlient,/id: demo-skill,/')"
# A second, legitimately-digested skill whose source basename collides with demo-skill's. Distinct id,
# distinct real source, correct sha — so the ONLY defect is the shared destination path.
mkdir -p "$ROOT/vendor/demo-skill"
printf '# impostor demo skill\n' > "$ROOT/vendor/demo-skill/SKILL.md"
VENDOR_SHA="$(sha256sum "$ROOT/vendor/demo-skill/SKILL.md" | cut -d' ' -f1)"
DUPBASENAME="$WORK/dupbasename.yml"
{ cat "$BASE"; printf '  - { id: vendored-demo, kind: skill, source: vendor/demo-skill, sha256: %s }\n' "$VENDOR_SHA"; } > "$DUPBASENAME"
expect_fail "duplicate skill source basename"  1 "share destination basename" "$DUPBASENAME"

# --- audited capabilities (#503) ---
# repos-audit.sh reads its whole mandate from this block, so every way it can be wrong is a way the
# audit goes quiet. A capability whose workflow is misspelled, missing, or not actually reusable
# audits nothing and says nothing — the vacuous green, relocated from the roster into the mapping.

expect_pass "a capability naming a real reusable workflow passes" \
  "$(capreg cap_ok "labels, coordination-kit" "- { id: coordination-kit, workflow: coordination-coherence.yml }")"

expect_fail "capability names a workflow that does not exist" 1 "is not in .github/workflows/" \
  "$(capreg cap_nofile "labels, coordination-kit" "- { id: coordination-kit, workflow: no-such.yml }")"

# The subtle one: the file is there, but it has no `workflow_call:` trigger, so nothing can `uses:` it.
# Every declared receiver would be reported unwired, forever, against a workflow that cannot be wired.
expect_fail "capability names a workflow that is not reusable" 1 "no 'workflow_call:' trigger" \
  "$(capreg cap_notreusable "labels, coordination-kit" "- { id: coordination-kit, workflow: not-reusable.yml }")"

expect_fail "capability with no detector at all" 1 "declares no detector" \
  "$(capreg cap_nowf "labels, coordination-kit" "- { id: coordination-kit }")"

# --- the #628 detector schema -------------------------------------------------------------------
# A capability declares EXACTLY ONE detector. Zero is the defect: it is then swept in NEITHER
# direction while remaining a legal `receives:` word — which is how `build-config` came to be enforced
# by four repos (SDD's as a REQUIRED check) and audited by nothing.

expect_pass "a script-delivered capability passes on its script detector" \
  "$(capreg cap_script "labels, build-config" \
     "- { id: build-config, script: sync-build-config.sh, reason: receivers inline a job that runs it }")"

# Two detectors is ambiguous: repos-audit would have to pick one, and a receiver satisfying the loose
# one would mask a gap in the strict one.
expect_fail "capability declaring two detectors" 1 "more than one detector" \
  "$(capreg cap_twodet "labels, coordination-kit" \
     "- { id: coordination-kit, workflow: coordination-coherence.yml, script: sync-build-config.sh }")"

# The script detector's subject must EXIST, for the same reason the workflow's must: the audit greps
# receivers for a reference to it, so a typo'd script reports EVERY receiver unwired — a gate that is
# confidently wrong about every repo at once.
expect_fail "capability naming a script that does not exist" 1 "which is not in scripts/" \
  "$(capreg cap_badscript "labels, build-config" \
     "- { id: build-config, script: no-such-script.sh, reason: typo }")"

# A path, not a basename, is refused: receivers check .github out wherever they like (governance uses
# `_org-build/`), so only the basename is stable across them — and the detector matches on it.
expect_fail "capability naming a script by path rather than basename" 1 "must be a BARE filename" \
  "$(capreg cap_pathscript "labels, build-config" \
     "- { id: build-config, script: scripts/sync-build-config.sh, reason: over-specified }")"

# `push:` is the ONE honest way to be unauditable at the receiver, so it is the one place this roster
# can hold an unfalsifiable claim — it must therefore be a REVIEWED one, never a blank. Same rule the
# `receivers: none` exemption already lives under.
expect_fail "a push capability with no reason" 1 "with no 'reason'" \
  "$(capreg cap_pushnoreason "labels, coordination-kit" \
     "- { id: coordination-kit, workflow: coordination-coherence.yml }" \
     "- { id: build-config, push: true }")"

# THE CLOSURE, and the half that makes this a fix rather than a relocation: a capability a repo
# RECEIVES with no `capabilities:` row is invisible to the audit in both directions. This is the exact
# state `build-config` was in — and `labels` with it — for as long as either existed.
expect_fail "a capability that is RECEIVED but has no detector row" 1 "received but NOT detectable" \
  "$(capreg cap_undetectable "labels, coordination-kit, build-config" \
     "- { id: coordination-kit, workflow: coordination-coherence.yml }")"

# ...and a COMMENT must not satisfy the reusability check. An unanchored `grep -q workflow_call:`
# matches the word anywhere in the file, so a workflow whose prose merely mentions it would pass as
# reusable — a check whose subject is "can this really be called?" satisfied by writing about calling.
printf 'on:\n  push:\n# deliberately NOT a workflow_call: trigger — just prose about one\njobs:\n  x:\n    runs-on: ubuntu-latest\n' \
  > "$ROOT/.github/workflows/prose-only.yml"
expect_fail "a workflow whose only 'workflow_call:' is in a comment is not reusable" 1 "no 'workflow_call:' trigger" \
  "$(capreg cap_prose "labels, coordination-kit" "- { id: coordination-kit, workflow: prose-only.yml }")"

expect_fail "capability outside the receives vocabulary" 1 "not in the receives vocabulary" \
  "$(capreg cap_unknown "labels, coordination-kit" "- { id: bogus-cap, workflow: coordination-coherence.yml }")"

expect_fail "duplicate capability id" 1 "duplicate capability id" \
  "$(capreg cap_dup "labels, coordination-kit" \
      "- { id: coordination-kit, workflow: coordination-coherence.yml }" \
      "- { id: coordination-kit, workflow: lockfile-sync.yml }")"

# `receivers: none` is a reviewed claim, like outside-fabric — so it needs a reason, and it must not
# contradict the roster. (Its OTHER guard is at audit time: repos-audit.sh scans for a real adopter
# and fails if one exists, so the claim is falsifiable rather than merely asserted here.)
expect_pass "'receivers: none' with a reason, and no repo rostering it, passes" \
  "$(capreg cap_none_ok "labels" \
      "- { id: coordination-kit, workflow: coordination-coherence.yml, receivers: none, reason: nobody has adopted it }")"

expect_fail "'receivers: none' with no reason" 1 "mute button" \
  "$(capreg cap_none_noreason "labels" \
      "- { id: coordination-kit, workflow: coordination-coherence.yml, receivers: none }")"

expect_fail "'receivers: none' contradicted by a rostered receiver" 1 "but repo(s) roster it" \
  "$(capreg cap_none_contra "labels, coordination-kit" \
      "- { id: coordination-kit, workflow: coordination-coherence.yml, receivers: none, reason: nobody has adopted it }")"

expect_fail "'receivers:' with a value other than none" 1 "is invalid" \
  "$(capreg cap_recv_bad "labels, coordination-kit" \
      "- { id: coordination-kit, workflow: coordination-coherence.yml, receivers: some }")"

# --- caps query (repos-audit.sh reads its mandate through this) ---
caps_tsv="$(bash "$REPOS_SH" caps --registry "$BASE")"
[ "$caps_tsv" = "$(printf 'coordination-kit\tcoordination-coherence.yml\t\t\t\t\nlabels\t\t\ttrue\t\tauthority-pushed by apply-labels.sh; nothing is wired at the receiver')" ] \
  && ok "caps -> a TSV row per capability: id, workflow, script, push, receivers, reason" \
  || bad "caps TSV" "got: $(printf '%s' "$caps_tsv" | cat -A | head -2)"
caps_ids="$(bash "$REPOS_SH" caps --field id --registry "$BASE")"
[ "$caps_ids" = "$(printf 'coordination-kit\nlabels')" ] && ok "caps --field id -> the capability ids" \
  || bad "caps --field id" "got: $caps_ids"

# `push` is a YAML BOOLEAN, and `.push // ""` cannot blank it — `false // ""` is `false`, not "". A
# `push: false` row would therefore reach repos-audit.sh as the five characters "false" and be read as
# a live push detector, muting the capability's entire sweep. Normalized to "true"/"" at the seam.
[ "$(bash "$REPOS_SH" caps --field push --registry "$BASE")" = "$(printf '\ntrue')" ] \
  && ok "caps --field push -> 'true' or empty, never the string 'false'" \
  || bad "caps --field push" "got: $(bash "$REPOS_SH" caps --field push --registry "$BASE")"

# --- list --all (the unrostered-adopter sweep starts from every repo, not from a declaration) ---
all_repos="$(bash "$REPOS_SH" list --all --field id --registry "$BASE" | tr '\n' ',')"
[ "$all_repos" = ".github,sdd," ] && ok "list --all -> every rostered repo, receives or not" \
  || bad "list --all" "got: $all_repos"

# --- misconfiguration (exit 2) ---
expect_fail "empty repos[] is misconfig"      2 "empty"              "$(variant emptyrepos  's/^repos:/repos: []\n_repos:/')"

# A usage error is exit 2 ("I was called wrong"), never exit 1 (the code reserved for "the roster is
# invalid") — #341's rule, applied to the two query surfaces #503 added. `--all` and `--receives` ask
# opposite questions; taking both silently would answer only one of them.
for bad_call in "list --all --receives coordination-kit" "list" "caps --field bogus" "caps --field"; do
  # shellcheck disable=SC2086
  out="$(bash "$REPOS_SH" $bad_call --registry "$BASE" 2>&1)" && rc=0 || rc=$?
  [ "$rc" -eq 2 ] && ok "usage error ('$bad_call') -> exit 2, never 1 (a roster finding)" \
    || bad "usage error must not masquerade as a roster finding" "call=$bad_call rc=$rc: $out"
done

# --- list + digest ---
list_kit="$(bash "$REPOS_SH" list --receives coordination-kit --registry "$BASE")"
[ "$list_kit" = "FS-GG/FS.GG.SDD" ] && ok "list --receives coordination-kit -> the one receiver" \
  || bad "list --receives coordination-kit" "got: $list_kit"
list_labels="$(bash "$REPOS_SH" list --receives labels --field id --registry "$BASE" | tr '\n' ',')"
[ "$list_labels" = ".github,sdd," ] && ok "list --receives labels --field id -> both, in order" \
  || bad "list labels ids" "got: $list_labels"
[ "$(bash "$REPOS_SH" digest "$ROOT/.claude/skills/demo-skill")" = "$SKILL_SHA" ] \
  && ok "digest skill dir -> sha256 of SKILL.md" || bad "digest skill dir"
[ "$(bash "$REPOS_SH" digest "$ROOT/scripts/democlient")" = "$CLIENT_SHA" ] \
  && ok "digest file -> sha256 of file" || bad "digest file"

# --- kit query (coordination-propagate builds its PR title from this) ---
kit_ids="$(bash "$REPOS_SH" kit --registry "$BASE" | tr '\n' ',')"
[ "$kit_ids" = "demo-skill,democlient," ] && ok "kit -> item ids, in roster order" \
  || bad "kit ids" "got: $kit_ids"
kit_kinds="$(bash "$REPOS_SH" kit --field kind --registry "$BASE" | tr '\n' ',')"
[ "$kit_kinds" = "skill,client," ] && ok "kit --field kind" || bad "kit kinds" "got: $kit_kinds"
kit_srcs="$(bash "$REPOS_SH" kit --field source --registry "$BASE" | tr '\n' ',')"
[ "$kit_srcs" = ".claude/skills/demo-skill,scripts/democlient," ] && ok "kit --field source" \
  || bad "kit sources" "got: $kit_srcs"
rc=0; bash "$REPOS_SH" kit --field bogus --registry "$BASE" >/dev/null 2>&1 || rc=$?
[ "$rc" -eq 2 ] && ok "kit --field bogus -> misconfig (exit 2)" || bad "kit bad field" "got exit $rc"

# --- kit --kind (coordination-sync derives its distributed skill set from this) ---
kit_skills="$(bash "$REPOS_SH" kit --kind skill --registry "$BASE" | tr '\n' ',')"
[ "$kit_skills" = "demo-skill," ] && ok "kit --kind skill -> only the skill rows" \
  || bad "kit --kind skill" "got: $kit_skills"
kit_clients="$(bash "$REPOS_SH" kit --kind client --registry "$BASE" | tr '\n' ',')"
[ "$kit_clients" = "democlient," ] && ok "kit --kind client -> only the client rows" \
  || bad "kit --kind client" "got: $kit_clients"
kit_srcs_skill="$(bash "$REPOS_SH" kit --field source --kind skill --registry "$BASE" | tr '\n' ',')"
[ "$kit_srcs_skill" = ".claude/skills/demo-skill," ] && ok "kit --field source --kind skill" \
  || bad "kit --field/--kind compose" "got: $kit_srcs_skill"
# No --kind is "every row" — coordination-propagate's title depends on it staying unfiltered.
kit_all="$(bash "$REPOS_SH" kit --registry "$BASE" | tr '\n' ',')"
[ "$kit_all" = "demo-skill,democlient," ] && ok "kit without --kind -> every row" \
  || bad "kit unfiltered" "got: $kit_all"
# An unknown kind is a usage error, not an empty list: silence here would distribute nothing, green.
rc=0; bash "$REPOS_SH" kit --kind bogus --registry "$BASE" >/dev/null 2>&1 || rc=$?
[ "$rc" -eq 2 ] && ok "kit --kind bogus -> misconfig (exit 2), not an empty list" \
  || bad "kit bad kind" "got exit $rc"

# The title the propagate workflow renders. Guards the `paste -sd` delimiter-cycling trap: a
# multi-char delimiter list would yield "demo-skill,democlient" with a SPACE before the last item.
kit_title="$(bash "$REPOS_SH" kit --registry "$BASE" | paste -sd, - | sed 's/,/, /g')"
[ "$kit_title" = "demo-skill, democlient" ] && ok "kit -> comma-joined PR title" \
  || bad "kit title join" "got: $kit_title"

# The real roster must name a kit, or the propagate title would render empty.
[ -n "$(bash "$REPOS_SH" kit)" ] && ok "the checked-in roster declares kit items" || bad "real roster kit empty"

# --- a missing flag value is misconfiguration (exit 2), never a finding (exit 1) — #341 ---
# `${2:?…}` let bash exit 1, the code reserved for "I checked the roster, and it is invalid". A
# caller reading the `# Exit:` contract could not tell a typo'd command line from a broken registry.
#
# expect_usage <name> <subcommand> <args…> — asserts the exit code AND the diagnostic. Pinning the
# `::error::repos-registry: <sub>:` prefix is half the point: exit 2 with a raw bash message (which
# still contains "needs a value") would annotate nothing in Actions and name no subcommand.
expect_usage() {
  local n="$1"; shift
  local sub="$1" out rc=0
  out="$(bash "$REPOS_SH" "$@" 2>&1)" || rc=$?
  if [ "$rc" -eq 1 ]; then bad "$n" "exit 1 — a usage error reported itself as a roster finding"; return; fi
  if [ "$rc" -ne 2 ]; then bad "$n" "expected exit 2 (misconfig), got $rc: $out"; return; fi
  case "$out" in "::error::repos-registry: $sub: "*"needs a value"*) ok "$n" ;;
                 *) bad "$n" "exit 2, but not a prefixed '$sub: <flag> needs a value': $out" ;; esac
}
# Every flag of every subcommand, three ways it can lack a value. `--field`/`--registry` are shared
# across subcommands, so each is asserted under each — that is what pins the prefix.
for spec in "list:--receives --field --registry" \
            "kit:--field --kind --registry" \
            "validate:--registry --root"; do
  sub="${spec%%:*}"
  for flag in ${spec#*:}; do
    # absent: `repos.sh list --receives`
    expect_usage "$sub $flag (absent value) -> exit 2"        "$sub" "$flag"
    # empty-but-present: an unset variable upstream, which is how this reaches a caller in practice
    expect_usage "$sub $flag '' (empty value) -> exit 2"      "$sub" "$flag" ""
    # the next flag swallowed as the value: must blame $flag, not the token two args later
    expect_usage "$sub $flag --registry (flag as value) -> exit 2" "$sub" "$flag" --registry
  done
done
# `digest` takes a positional, not a flag; it already routes through die(). Guard it against a
# regression that makes the same class of mistake.
rc=0; bash "$REPOS_SH" digest >/dev/null 2>&1 || rc=$?
[ "$rc" -eq 2 ] && ok "digest (no path) -> misconfig (exit 2)" || bad "digest no path" "got exit $rc"

# --- CI guard on the real, checked-in roster ---
if bash "$REPOS_SH" validate >/dev/null 2>&1; then ok "the checked-in registry/repos.yml validates"
else bad "real registry/repos.yml validates" "$(bash "$REPOS_SH" validate 2>&1)"; fi

# --- CI guard on the gate itself (#266) ---
# Two DIFFERENT workflows are armed by a hand-maintained `paths:` filter that must enumerate every
# kit source, and in both a missing entry fails SILENTLY — the workflow simply never runs, and a
# workflow that never runs reports nothing at all:
#
#   repos-registry-selftest.yml  `validate` is the only thing asserting the kit digests. A kit source
#                                outside its filter is never digest-checked; the gate is green because
#                                it never ran (#266).
#   coordination-propagate.yml   the PUSH arm. A kit source outside its filter PROPAGATES NOTHING:
#                                receivers keep an old copy, their `coordination-kit` gate reddens,
#                                and someone hand-syncs it (#463). Its own header warns of exactly
#                                this, and nothing asserted it — so assert it here.
#
# Same check, same kit, two subjects. Assert every kit source (and, for skills, the .agents mirror
# that carries the same bytes) is covered, on each trigger the workflow is supposed to fire on.
uncovered_for() {                             # <workflow-path> <trigger>[,<trigger>...]
  python3 - "$REPO_ROOT" "$1" "$2" <<'PY'
import sys, yaml, re, pathlib
root, wf_rel, want = pathlib.Path(sys.argv[1]), sys.argv[2], sys.argv[3].split(",")
wf  = yaml.safe_load((root / wf_rel).read_text())
reg = yaml.safe_load((root / "registry/repos.yml").read_text())

# YAML 1.1 reads a bare `on:` as the boolean True; a quoted "on": stays a string. Accept either
# rather than KeyError-ing, and say so plainly if the key is gone — this assertion exists precisely
# because a gate that cannot find its subject must fail loudly, not vanish.
triggers = wf.get(True, wf.get("on"))
if not isinstance(triggers, dict):
    sys.exit(f"{wf_rel} has no readable `on:` block — cannot check its paths: filter")

def matches(path, pat):                       # GitHub glob: ** spans /, * does not
    rx = "".join(r"[^/]*" if p == "*" else ".*" if p == "**" else re.escape(p)
                 for p in re.split(r"(\*\*|\*)", pat))
    return re.fullmatch(rx, path) is not None

def probes(item):
    # `digest` hashes a dir's SKILL.md and a file's own bytes, so those are the paths whose edits
    # stale the sha256 — and, identically, the paths whose edits the receivers need pushed to them.
    # Skills are mirrored byte-for-byte into .agents/, so an edit lands in either.
    if item["kind"] != "skill":
        return [item["source"]]
    claude = f"{item['source']}/SKILL.md"
    return sorted({claude, claude.replace(".claude/", ".agents/", 1)})

gaps = []
for trigger in want:
    if trigger not in triggers:
        # The workflow cannot run on this event at all — the widest possible coverage gap.
        gaps.append(f"{trigger}: trigger absent — never runs on {trigger}")
        continue
    # A trigger that is null, or carries no `paths:`, is unfiltered: every path fires it, so every
    # kit source is trivially covered. Only an explicitly empty list matches nothing.
    cfg = triggers.get(trigger) or {}
    if "paths" not in cfg:
        continue
    pats = cfg["paths"]
    if not pats:
        gaps.append(f"{trigger}: paths: is empty — matches nothing")
        continue
    for item in reg["kit"]:
        for probe in probes(item):
            if not any(matches(probe, p) for p in pats):
                gaps.append(f"{trigger}: kit '{item['id']}' source {probe}")
    # The lock itself (#527). It is the artifact the digests LIVE in now, so a `paths:` filter that
    # omits it lets a stale — or hand-edited — lock merge without ever re-running the gate whose only
    # job is to catch that. Exactly the #334 shape the kit-source probes above already guard against:
    # the check is fine, its TRIGGER fails open. Only assert it where the gate actually validates.
    if wf_rel.endswith("repos-registry-selftest.yml") and not any(
            matches("registry/repos.lock", p) for p in pats):
        gaps.append(f"{trigger}: registry/repos.lock (the kit digests) is not covered")
print("\n".join(gaps))
PY
}

# The digest gate runs on both PR and push; the propagate arm is push-to-main only (there is nothing
# to propagate from an unmerged PR), so each is asserted against the triggers it actually declares.
uncovered="$(uncovered_for ".github/workflows/repos-registry-selftest.yml" "pull_request,push")"
if [ -z "$uncovered" ]; then ok "every kit source is covered by the selftest paths: filter"
else bad "kit source ungated — an edit to it skips the digest check" "$uncovered"; fi

# ...and the same gate-on-the-gate for the CAPABILITY workflows (#503). `validate` now proves each
# capabilities[].workflow exists and really carries a `workflow_call:` trigger, so registry/repos.yml's
# validity depends on those files. If the selftest's paths: filter does not cover them, renaming or
# deleting one leaves the roster INVALID and merges green — the check is fine and its trigger fails
# open, which is #334 over again, and the reason that leg above exists at all.
caps_uncovered_for() {  # <workflow-rel-path> <trigger,trigger…> -> uncovered "trigger: cap … wf …" lines
  python3 - "$REPO_ROOT" "$1" "$2" <<'PY'
import sys, yaml, re, pathlib
root, wf_rel, want = pathlib.Path(sys.argv[1]), sys.argv[2], sys.argv[3].split(",")
wf  = yaml.safe_load((root / wf_rel).read_text())
reg = yaml.safe_load((root / "registry/repos.yml").read_text())

triggers = wf.get(True, wf.get("on"))
if not isinstance(triggers, dict):
    sys.exit(f"{wf_rel} has no readable `on:` block — cannot check its paths: filter")

def matches(path, pat):                       # GitHub glob: ** spans /, * does not
    rx = "".join(r"[^/]*" if p == "*" else ".*" if p == "**" else re.escape(p)
                 for p in re.split(r"(\*\*|\*)", pat))
    return re.fullmatch(rx, path) is not None

gaps = []
for trigger in want:
    if trigger not in triggers:
        gaps.append(f"{trigger}: trigger absent — never runs on {trigger}")
        continue
    cfg = triggers.get(trigger) or {}
    if "paths" not in cfg:
        continue                              # unfiltered: everything fires it
    pats = cfg["paths"]
    if not pats:
        gaps.append(f"{trigger}: paths: is empty — matches nothing")
        continue
    # A capability's DETECTOR SUBJECT — whichever kind it is. `validate` proves the subject exists, so
    # renaming or deleting it makes the roster invalid; if the selftest's `paths:` filter does not
    # cover the subject, that rename never fires this gate and the roster sits invalid AND green.
    #
    # The `script:` kind has exactly the same exposure as `workflow:` and must be covered the same way
    # (#628) — it was `cap['workflow']` here, which both KeyError'd on a script row and, had it been
    # written as a `.get()`, would have silently skipped it: the fail-open one level up, in the guard
    # against the fail-open. A `push:` capability has NO subject in this repo (the authority pushes it
    # via a script that is not the detector), so it has nothing to cover.
    for cap in reg.get("capabilities", []):
        if "workflow" in cap:  probe = f".github/workflows/{cap['workflow']}"
        elif "script" in cap:  probe = f"scripts/{cap['script']}"
        else:                  continue                     # push: nothing detectable to gate
        if not any(matches(probe, p) for p in pats):
            gaps.append(f"{trigger}: capability '{cap['id']}' subject {probe}")
print("\n".join(gaps))
PY
}
uncovered="$(caps_uncovered_for ".github/workflows/repos-registry-selftest.yml" "pull_request,push")"
if [ -z "$uncovered" ]; then ok "every capability's detector subject is covered by the selftest paths: filter"
else bad "capability detector ungated — renaming it leaves the roster invalid, green" "$uncovered"; fi

uncovered="$(uncovered_for ".github/workflows/coordination-propagate.yml" "push")"
if [ -z "$uncovered" ]; then ok "every kit source is covered by the propagate paths: filter"
else bad "kit source unpropagated — an edit to it never reaches the receivers (#463)" "$uncovered"; fi

# ---- build-config: the capability that was a legal word nobody said (#626) ------------------------
# FOUR repos (sdd, rendering, governance, game) run a `Shared-build-config drift check` against
# `.github@main`'s dist/dotnet/. The ENFORCEMENT shipped; the DISTRIBUTION never did — `build-config` sat
# in repos.sh's KNOWN_CAPS with zero `receives:` rows and no propagate arm. So every edit to dist/dotnet/
# red-lit those four until a human hand-synced each, and the red landed on the coordination kit's own
# delivery vehicle: #627 added the engine to the tool manifest, and a day later SDD's kit-sync PR was
# STILL blocked by the drift it caused (#634 found it stuck).
#
# THE INVARIANT: a repo receives build-config IFF it enforces build-config.
#
# Both directions are a real defect, and they fail in opposite ways:
#   enforces + does not receive -> it goes red on drift and NOTHING ever sends it a fix. The ratchet.
#   receives + does not enforce -> a bot writes build files into a repo that never adopted them. Templates
#                                  has no Directory.Build.props at all; Audio's is hand-authored, and
#                                  #387's guard exists to REFUSE overwriting it. Onboarding either is a
#                                  deliberate `--adopt`, not a propagation.
#
# The fixture cannot reach the receivers' CI to check the symmetry itself — that needs network, and it is
# repos-audit's job (#628, still open: its mandate covers only capabilities wired by a REUSABLE workflow,
# and this one is wired by an inline `run:`). What IS assertable here is the reviewed claim: the declared
# set is exactly the four adopters, so silently adding a fifth trips this test and has to be argued for.
bc_receivers="$(bash "$REPOS_SH" list --receives build-config --field id | sort | tr '\n' ',')"
if [ "$bc_receivers" = "game,governance,rendering,sdd," ]; then
  ok "build-config: the four ADOPTERS receive it — and templates/audio, which never adopted, do not (#626, #628)"
else
  bad "build-config: the receiver set does not match the repos that actually enforce it (#626)" \
      "declared: $bc_receivers
expected: game,governance,rendering,sdd,
A repo that ENFORCES but does not RECEIVE can only go red and stay red.
A repo that RECEIVES but never ADOPTED gets build files written into it by a bot."
fi

# THE CHANNEL EXISTS AND IS ROSTER-DRIVEN. A propagate arm with a hardcoded target list is the roster
# rotting in a second place; a missing arm is the bug itself.
BCP=".github/workflows/build-config-propagate.yml"
if [ -f "$REPO_ROOT/$BCP" ]; then ok "build-config: the propagate arm exists — the enforcement has a distribution half"
else bad "build-config: NO propagate arm — the drift check enforces a config nothing ever sends (#626)"; fi

if grep -q -- "--receives build-config" "$REPO_ROOT/$BCP" 2>/dev/null; then
  ok "build-config: propagate reads its receivers from the ROSTER, not a hardcoded list"
else
  bad "build-config: propagate does not read the roster — a second copy of the receiver list will rot"
fi

# A ZERO-RECEIVER RUN MUST FAIL, NOT SUCCEED QUIETLY. This capability's entire history is "iterated the
# empty set and nobody noticed", and #503 is the same lesson one layer up: a guard that sums pairs across
# capabilities reports green having checked a third of its mandate.
if grep -q "propagate to nobody" "$REPO_ROOT/$BCP" 2>/dev/null; then
  ok "build-config: a zero-receiver plan is an ERROR — the empty set may not report success (#503, #626)"
else
  bad "build-config: propagate would iterate an empty receiver set and exit 0 — that is the bug, again"
fi

# The path filter must cover what the syncer actually writes. Unlike the kit's hand-maintained list, this
# one is a WILDCARD over dist/dotnet/ — so assert it stays a wildcard rather than decaying into an
# enumeration that a new managed file can silently fall out of.
if grep -qE '^\s+- "dist/dotnet/\*\*"' "$REPO_ROOT/$BCP" 2>/dev/null; then
  ok "build-config: the path filter is a WILDCARD over dist/dotnet/ — a new managed file cannot fall out of it"
else
  bad "build-config: the propagate path filter enumerates files — a managed file missing from it propagates NOTHING, silently"
fi

echo "repos-registry fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::repos-registry fixture FAILED"; exit 1; }
echo "repos-registry fixture — OK"
