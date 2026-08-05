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
# A stand-in for the real fsgg-coord shim, so the #1077 invariant leg below (a client that delivers
# scripts/fsgg-coord) has an existing source and fails on the INVARIANT, not on "source missing".
printf '#!/usr/bin/env bash\necho shim\n' > "$ROOT/scripts/fsgg-coord"
printf 'on:\n  workflow_call:\njobs:\n  x:\n    runs-on: ubuntu-latest\n' \
  > "$ROOT/.github/workflows/coordination-coherence.yml"
printf 'on:\n  workflow_call:\njobs:\n  x:\n    runs-on: ubuntu-latest\n' \
  > "$ROOT/.github/workflows/lockfile-sync.yml"
# A workflow that is NOT reusable: no `workflow_call:` trigger, so no repo could ever `uses:` it.
printf 'on:\n  push:\njobs:\n  x:\n    runs-on: ubuntu-latest\n' \
  > "$ROOT/.github/workflows/not-reusable.yml"
# A `caller:` capability's SUBJECT is a reusable workflow too, and validate proves it exists and is
# callable for exactly the `workflow:` reason: the audit greps receivers for a `uses:` of it, so a
# missing or non-reusable subject reports every receiver unwired forever (#1504).
printf 'on:\n  workflow_call:\njobs:\n  skill-union:\n    runs-on: ubuntu-latest\n' \
  > "$ROOT/.github/workflows/skill-union-assert.yml"
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

# `skill-union` is in CAPS_REQUIRING_ROW_AT_ZERO_RECEIVERS (#1806): its capability row is now mandatory
# in EVERY roster this fixture builds, regardless of whether any repo receives it — the same way
# `labels` has been mandatory since #628, just for a narrower, explicitly-named reason (the reverse
# sweep, not the forward one). This is the ordinary, uninteresting default row a roster carries when it
# is not the thing a given leg is testing; legs that DO exercise the `caller:` detector pass their own
# `id: skill-union` row instead, and `capreg` below detects that and skips this one so ids never collide.
SKILL_UNION_CAP='  - { id: skill-union, caller: skill-union, receivers: none, reason: retired shape kept for the reverse sweep; this is the fixture default }'

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
$SKILL_UNION_CAP
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
  # A leg exercising the `caller:` detector passes its OWN `id: skill-union` row; appending the
  # default on top of that would be a duplicate capability id, not a closure fix.
  local has_su=0 row
  for row in "$@"; do
    case "$row" in *"id: skill-union"*) has_su=1 ;; esac
  done
  { printf 'schemaVersion: 5\nupdated: 2026-07-13\nauthority: FS-GG/.github\nrepos:\n'
    printf '  - { id: .github, full: FS-GG/.github,   role: authority, receives: [labels] }\n'
    printf '  - { id: sdd,     full: FS-GG/FS.GG.SDD, role: framework, receives: [%s] }\n' "$recv"
    printf 'kit:\n'
    printf '  - { id: demo-skill, kind: skill,  source: .claude/skills/demo-skill }\n'
    printf '  - { id: democlient, kind: client, source: scripts/democlient }\n'
    printf 'capabilities:\n'
    printf '  %s\n' "$@"
    # Every roster here rosters `labels`, so every roster here must say how `labels` is DETECTED, or
    # the #628 closure rejects it — correctly, and for a reason no leg below is trying to test. Same
    # rule, same reasoning, now applies to `skill-union` via CAPS_REQUIRING_ROW_AT_ZERO_RECEIVERS (#1806).
    [ "$has_su" -eq 1 ] || printf '%s\n' "$SKILL_UNION_CAP"
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

# repository_dispatch contracts are a top-level graph, not an optional bag of strings.  Each endpoint
# must be rostered and every triple unique; otherwise repos-audit has no finite subject to compare to
# the sender/listener workflows.
dispatch_variant() { local n="$1" body="$2" f; f="$WORK/$n.yml"; cp "$BASE" "$f"; printf '\ndispatches:\n%s\n' "$body" >> "$f"; relock "$f"; printf '%s' "$f"; }
expect_pass "a rostered dispatch triple passes" "$(dispatch_variant dispatchok '  - { producer: FS-GG/FS.GG.SDD, target: FS-GG/FS.GG.SDD, event-type: fixture-event }')"
expect_fail "dispatch row refuses an empty object" 1 "each dispatch" "$(dispatch_variant dispatchempty '  - {}')"
expect_fail "dispatch row refuses a missing producer" 1 "each dispatch" "$(dispatch_variant dispatchmissing '  - { target: FS-GG/FS.GG.SDD, event-type: fixture-event }')"
expect_fail "dispatch row refuses wrong-typed values" 1 "each dispatch" "$(dispatch_variant dispatchtyped '  - { producer: 7, target: FS-GG/FS.GG.SDD, event-type: fixture-event }')"
expect_fail "dispatch producer must be rostered" 1 "producer 'FS-GG/FS.GG.Ghost' is not rostered" "$(dispatch_variant dispatchproducer '  - { producer: FS-GG/FS.GG.Ghost, target: FS-GG/FS.GG.SDD, event-type: fixture-event }')"
expect_fail "dispatch target must be rostered" 1 "target 'FS-GG/FS.GG.Ghost' is not rostered" "$(dispatch_variant dispatchtarget '  - { producer: FS-GG/FS.GG.SDD, target: FS-GG/FS.GG.Ghost, event-type: fixture-event }')"
expect_fail "dispatch event-type must be non-empty" 1 "each dispatch" "$(dispatch_variant dispatchemptyevent '  - { producer: FS-GG/FS.GG.SDD, target: FS-GG/FS.GG.SDD, event-type: "" }')"
expect_fail "duplicate dispatch triple is refused" 1 "duplicate dispatch" "$(dispatch_variant dispatchdup $'  - { producer: FS-GG/FS.GG.SDD, target: FS-GG/FS.GG.SDD, event-type: fixture-event }\n  - { producer: FS-GG/FS.GG.SDD, target: FS-GG/FS.GG.SDD, event-type: fixture-event }')"

# --- violation classes (all exit 1) ---
expect_fail "unknown receives capability"     1 "unknown capability" "$(variant unknowncap  's/coordination-kit\]/bogus-cap]/')"
expect_fail "duplicate repo id"               1 "duplicate"          "$(variant dupid       's/id: sdd,/id: .github,/')"
expect_fail "invalid role"                    1 "role"               "$(variant badrole     's/role: framework/role: banana/')"
expect_fail "not exactly one authority"       1 "exactly one"        "$(variant twoauth     's/role: framework/role: authority/')"
# kit-delivery (ADR-0062/#1287): a bad value, and the field on a repo that does not receive the kit.
expect_fail "kit-delivery bad value"          1 "byte-copy|package"  "$(variant kdbad  's/receives: \[labels, coordination-kit\]/receives: [labels, coordination-kit], kit-delivery: bogus/')"
expect_fail "kit-delivery on non-receiver"    1 "does not receive coordination-kit" "$(variant kdnonrecv 's/role: authority, receives: \[labels\]/role: authority, receives: [labels], kit-delivery: package/')"
expect_pass "kit-delivery: package on a coordination-kit receiver" "$(variant kdpkg 's/receives: \[labels, coordination-kit\]/receives: [labels, coordination-kit], kit-delivery: package/')"

# absence-cover (#1785/#1869): the historical field name now records whether an unexcused view-root
# assertion or materialize path is branch-required. The vocabulary is two words, and the field is not
# the authority: repos-audit derives the real answer from workflows + the API daily and reds on drift.
expect_fail "absence-cover bad value"       1 "required|unrequired"    "$(variant acbad 's/receives: \[labels, coordination-kit\]/receives: [labels, coordination-kit], absence-cover: sometimes/')"
expect_fail "absence-cover on non-receiver" 1 "does not receive coordination-kit" "$(variant acnonrecv 's/role: authority, receives: \[labels\]/role: authority, receives: [labels], absence-cover: required/')"
expect_pass "absence-cover: required on a coordination-kit receiver"   "$(variant acreq  's/receives: \[labels, coordination-kit\]/receives: [labels, coordination-kit], absence-cover: required/')"
expect_pass "absence-cover: unrequired on a coordination-kit receiver" "$(variant acunreq 's/receives: \[labels, coordination-kit\]/receives: [labels, coordination-kit], absence-cover: unrequired/')"
# THE WORD A ROW MAY NOT SAY. `none` is a state the daily sweep DERIVES and reds on; a roster row
# asserting it would be the org writing down that this receiver has no such detected path.
# It is refused at parse time, so it can never be merged and later read as a licence.
expect_fail "absence-cover: 'none' is not declarable" 1 "not a declarable state" "$(variant acnone 's/receives: \[labels, coordination-kit\]/receives: [labels, coordination-kit], absence-cover: none/')"
expect_fail "authority receives the kit"      1 "must not RECEIVE"   "$(variant authkit     's/full: FS-GG\/.github,   role: authority, receives: \[labels\]/full: FS-GG\/.github,   role: authority, receives: [labels, coordination-kit]/')"
expect_fail "kit source missing"              1 "source missing"     "$(variant nosource    's/source: scripts\/democlient/source: scripts\/nope/')"
expect_fail "kit id is not kebab/dotted"      1 "kit id"             "$(variant badkitid    's/id: demo-skill,/id: Demo Skill,/')"

# --- kit `kind: config` rows (#1077): a config names its own dest, and ONLY a config may ---
expect_fail "config kit row without a dest"   1 "no 'dest'"          "$(variant cfgnodest   's/kind: client, source: scripts\/democlient/kind: config, source: scripts\/democlient/')"
expect_fail "config dest is absolute"         1 "receiver-RELATIVE"  "$(variant cfgabsdest  's/kind: client, source: scripts\/democlient }/kind: config, source: scripts\/democlient, dest: \/etc\/x }/')"
expect_fail "config dest escapes the root"    1 "escape"             "$(variant cfgdotdot   's/kind: client, source: scripts\/democlient }/kind: config, source: scripts\/democlient, dest: ..\/x }/')"
expect_fail "non-config row carrying a dest"  1 "only 'config'"      "$(variant clientdest  's/kind: client, source: scripts\/democlient }/kind: client, source: scripts\/democlient, dest: foo }/')"
# THE #1077 INVARIANT IS NO LONGER ASSERTED HERE (#1615, 2026-07-28, ADR-0068).
#
# THIS LEG USED TO READ:
#   # THE #1077 INVARIANT: a kit delivering the fsgg-coord shim MUST deliver the engine manifest too.
#   expect_fail "shim delivered without its engine manifest" 1 "engine manifest" \
#     "$(variant shimnomanifest 's/source: scripts\/democlient/source: scripts\/fsgg-coord/')"
#
# It is REPLACED, not deleted, and the replacement is deliberately not in this file. `validate` reads
# a roster; the invariant is about a RECEIVER'S TREE. The old rule could only say "these two rows ride
# one fabric" and inferred the receiver property from that arrangement, so a receiver that deleted its
# own `.config/dotnet-tools.json` stayed green forever. `scripts/repos-audit.sh`'s engine-manifest
# sweep reads each receiver's actual manifest instead; its legs — including the mutation proof — live
# in `tests/repos-audit/run.sh`.
#
# SO THE LEG THAT BELONGS HERE NOW IS THE OPPOSITE ONE: a roster delivering the shim and NOT the
# manifest must PASS validation, because that is the roster this org now ships. Without this, the next
# worker to "restore" the deleted rule would find every fixture green and no argument against it.
expect_pass "shim WITHOUT an engine manifest row is a valid roster (#1615/ADR-0068 — the invariant moved to repos-audit's engine-manifest sweep, which reads the receiver's tree)" \
  "$(variant shimnomanifest 's/source: scripts\/democlient/source: scripts\/fsgg-coord/')"
# `full:` IS OWNER-QUALIFIED SINCE .github#2245, so `GH/FS.GG.SDD` — the old mutation here — is now a
# legal roster row and cannot be this leg's subject. What is still refused is a name that is not an
# `<owner>/<repo>` at all: the shape check is what keeps a typo a violation once the owner stopped
# being pinned to one literal. Both classes are asserted (no slash, and an owner GitHub cannot issue).
expect_fail "repo full with no owner at all"  1 "<owner>/<repo>"     "$(variant badfull     's/full: FS-GG\/FS.GG.SDD/full: FS.GG.SDD/')"
expect_fail "repo full with an illegal owner" 1 "<owner>/<repo>"     "$(variant badowner    's/full: FS-GG\/FS.GG.SDD/full: has.dots\/FS.GG.SDD/')"
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

expect_pass "a package-materialized capability passes on the supported compound detector" \
  "$(capreg cap_materializer "labels, build-config" \
     "- { id: build-config, materializer: build-config, reason: explicit package opt-in plus CI enforcement }")"

expect_fail "capability naming an unsupported materializer detector" 1 "unsupported materializer detector" \
  "$(capreg cap_badmaterializer "labels, build-config" \
     "- { id: build-config, materializer: imaginary, reason: no audit implementation exists }")"

# The CALLER detector (#1504). Same closed vocabulary as `materializer:`, and the same subject-exists
# rule as `workflow:`/`script:` — because the audit greps receivers for a `uses:` of the named reusable
# workflow, so a subject that is gone or not callable reports every receiver unwired forever.
expect_pass "a caller-detected capability passes on the supported compound detector" \
  "$(capreg cap_caller "labels, skill-union" \
     "- { id: skill-union, caller: skill-union, reason: own-root call plus a trigger over those roots }")"

expect_fail "capability naming an unsupported caller detector" 1 "unsupported caller detector" \
  "$(capreg cap_badcaller "labels, skill-union" \
     "- { id: skill-union, caller: imaginary, reason: no audit implementation exists }")"

# The subject of `caller: skill-union` is skill-union-assert.yml. Delete it and the roster is invalid —
# which is what makes the selftest paths:-coverage leg below load-bearing rather than decorative.
mv "$ROOT/.github/workflows/skill-union-assert.yml" "$ROOT/.github/workflows/skill-union-assert.yml.bak"
expect_fail "caller detector whose subject workflow does not exist" 1 "is not in .github/workflows/" \
  "$(capreg cap_caller_nosubject "labels, skill-union" \
     "- { id: skill-union, caller: skill-union, reason: subject deleted }")"
printf 'on:\n  push:\njobs:\n  skill-union:\n    runs-on: ubuntu-latest\n' \
  > "$ROOT/.github/workflows/skill-union-assert.yml"
expect_fail "caller detector whose subject workflow is not reusable" 1 "no 'workflow_call:' trigger" \
  "$(capreg cap_caller_notreusable "labels, skill-union" \
     "- { id: skill-union, caller: skill-union, reason: subject cannot be called }")"
mv "$ROOT/.github/workflows/skill-union-assert.yml.bak" "$ROOT/.github/workflows/skill-union-assert.yml"

expect_fail "caller plus another detector is rejected as ambiguous" 1 "more than one detector" \
  "$(capreg cap_twodet_caller "labels, skill-union" \
     "- { id: skill-union, caller: skill-union, workflow: coordination-coherence.yml }")"

# `.yaml` is as valid a spelling as `.yml`, and the DETECTOR matches `skill-union-assert.ya?ml`. A
# validator that accepted only one would call the roster invalid over a rename the audit handles fine.
mv "$ROOT/.github/workflows/skill-union-assert.yml" "$ROOT/.github/workflows/skill-union-assert.yaml"
expect_pass "caller detector subject may be spelled .yaml, as the detector's own regex allows" \
  "$(capreg cap_caller_yaml "labels, skill-union" \
     "- { id: skill-union, caller: skill-union, reason: subject spelled .yaml }")"
mv "$ROOT/.github/workflows/skill-union-assert.yaml" "$ROOT/.github/workflows/skill-union-assert.yml"

# THE AUTHORITY MAY NOT RECEIVE A CAPABILITY IT IS THE SOURCE OF — and this is the INVARIANT, not the
# roster row. `repos-audit` detects a receiver by a `uses:` of the AUTHORITY's copy and deliberately never
# matches a repo running its own, so a rostered authority produces a gap no edit to `.github` can ever
# close, whose diagnostic ("nothing calls it") is false about the repo that IS it. `coordination-kit` has
# been guarded since the roster existed; `skill-union` needs the same guard for the same reason (#1504).
authreg() { # <name> <cap-id> — the authority rostered as a receiver of <cap-id>
  local f="$WORK/$1.yml"
  { printf 'schemaVersion: 8\nupdated: 2026-07-27\nauthority: FS-GG/.github\nrepos:\n'
    printf '  - { id: .github, full: FS-GG/.github,   role: authority, receives: [labels, %s] }\n' "$2"
    printf '  - { id: sdd,     full: FS-GG/FS.GG.SDD, role: framework, receives: [labels, %s] }\n' "$2"
    printf 'kit:\n'
    printf '  - { id: demo-skill, kind: skill,  source: .claude/skills/demo-skill }\n'
    printf '  - { id: democlient, kind: client, source: scripts/democlient }\n'
    printf 'capabilities:\n'
    printf '  - { id: skill-union, caller: skill-union }\n'
    printf '  - { id: coordination-kit, workflow: coordination-coherence.yml }\n'
    printf '%s\n' "$LABELS_CAP"; } > "$f"
  relock "$f"
  printf '%s' "$f"
}
expect_fail "the authority may not RECEIVE skill-union — it is the source" 1 \
  "must not RECEIVE skill-union" "$(authreg auth_skillunion skill-union)"
expect_fail "...and the coordination-kit guard still holds, from the same list" 1 \
  "must not RECEIVE coordination-kit" "$(authreg auth_kit coordination-kit)"

# Two detectors is ambiguous: repos-audit would have to pick one, and a receiver satisfying the loose
# one would mask a gap in the strict one.
expect_fail "capability declaring two detectors" 1 "more than one detector" \
  "$(capreg cap_twodet "labels, coordination-kit" \
     "- { id: coordination-kit, workflow: coordination-coherence.yml, script: sync-build-config.sh }")"
expect_fail "materializer plus another detector is also rejected as ambiguous" 1 "more than one detector" \
  "$(capreg cap_twodet_materializer "labels, build-config" \
     "- { id: build-config, script: sync-build-config.sh, materializer: build-config }")"

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
# BASE now carries THREE rows, not two: skill-union's is mandatory here regardless of receivers
# (#1806, CAPS_REQUIRING_ROW_AT_ZERO_RECEIVERS) and sits between coordination-kit and labels in file
# order, which is the order `caps` preserves.
caps_tsv="$(bash "$REPOS_SH" caps --registry "$BASE")"
[ "$caps_tsv" = "$(printf 'coordination-kit\tcoordination-coherence.yml\t\t\t\t\t\t\nskill-union\t\t\t\tskill-union\t\tnone\tretired shape kept for the reverse sweep; this is the fixture default\nlabels\t\t\t\t\ttrue\t\tauthority-pushed by apply-labels.sh; nothing is wired at the receiver')" ] \
  && ok "caps -> a TSV row per capability: id, workflow, script, materializer, caller, push, receivers, reason" \
  || bad "caps TSV" "got: $(printf '%s' "$caps_tsv" | cat -A | head -3)"
[ "$(bash "$REPOS_SH" caps --field caller --registry "$BASE")" = "$(printf '\nskill-union')" ] \
  && ok "caps --field caller -> 'skill-union' for its caller row, empty for the other two" \
  || bad "caps --field caller" "got: $(bash "$REPOS_SH" caps --field caller --registry "$BASE")"
caps_ids="$(bash "$REPOS_SH" caps --field id --registry "$BASE")"
[ "$caps_ids" = "$(printf 'coordination-kit\nskill-union\nlabels')" ] && ok "caps --field id -> the capability ids" \
  || bad "caps --field id" "got: $caps_ids"

# `push` is a YAML BOOLEAN, and `.push // ""` cannot blank it — `false // ""` is `false`, not "". A
# `push: false` row would therefore reach repos-audit.sh as the five characters "false" and be read as
# a live push detector, muting the capability's entire sweep. Normalized to "true"/"" at the seam.
[ "$(bash "$REPOS_SH" caps --field push --registry "$BASE")" = "$(printf '\n\ntrue')" ] \
  && ok "caps --field push -> 'true' or empty, never the string 'false'" \
  || bad "caps --field push" "got: $(bash "$REPOS_SH" caps --field push --registry "$BASE")"
[ -z "$(bash "$REPOS_SH" caps --field materializer --registry "$BASE")" ] \
  && ok "caps --field materializer -> empty for non-materializer rows" \
  || bad "caps --field materializer" "got: $(bash "$REPOS_SH" caps --field materializer --registry "$BASE")"

# === .github#2245: THE ROSTER'S THREE-WAY TRAP ====================================================
#
# `registry/repos.yml` encoded EXACTLY ONE shape — an FS-GG-owned repository participating in at least
# one fabric — and `.github#2206`'s maintainer decision needed a row that is none of those things.
# Three independent legs refused it at once, and the two about ownership closed `outside-fabric:` for
# the same repo, so NEITHER registry-side disposition ("rostered" or "excused with a reason") could be
# written and only "delete it from the board" remained. Each leg gets its own assertion, in both
# directions: the new shape VALIDATES, and every guarantee the old legs carried still REFUSES.

# `nonpart <name> <row-fields>` — BASE plus one extra repo row, locked. The rows below are the shapes
# #2245 is about, so they are written out rather than sed-mutated onto an existing row.
nonpart() {
  # SEPARATE `local` STATEMENTS, not one: bash expands a `local`'s whole word list before assigning
  # any of it, so `local n="$1" f="$WORK/$n.yml"` reads an unset `n` — and under `set -u` that aborts
  # the helper mid-fixture, leaving eight legs failing on "--registry needs a value" instead of on
  # their own subject.
  local n="$1" row="$2"
  local f="$WORK/$n.yml"
  sed "s|^capabilities:|  - { $row }\ncapabilities:|" "$BASE" > "$f"
  relock "$f"
  printf '%s' "$f"
}

# LEG 1 — a USER-OWNED row. The `^FS-GG/` regex at repos.sh:796 refused this outright.
expect_pass "a user-owned repos[] row VALIDATES (#2245 acceptance 1)" \
  "$(nonpart useredrow 'id: sir, full: EHotwagner/S.I.R., role: non-participant, receives: [], reason: org work on a user-owned repo (.github#2206)')"

# LEG 1b — the same for the OPT-OUT list, which applied the identical regex at repos.sh:868. With
# both closed, a user-owned repo could be neither inside the fabric nor outside it.
OUTSIDE_USER="$WORK/outside-user.yml"
sed 's|^capabilities:|outside-fabric:\n  - { full: EHotwagner/rogue3, reason: an external product this fabric does not track }\ncapabilities:|' \
  "$BASE" > "$OUTSIDE_USER"; relock "$OUTSIDE_USER"
expect_pass "a user-owned outside-fabric row VALIDATES (#2245 acceptance 1)" "$OUTSIDE_USER"

# LEG 2 — the role vocabulary. A non-participant must say WHY it is rostered: it is excused from every
# capability sweep, which is precisely the standing licence #269 refused to grant without a reason.
expect_fail "a non-participant row with NO recorded reason is REJECTED (#2245 acceptance 2)" \
  1 "carries no 'reason'" \
  "$(nonpart noreason 'id: sir, full: EHotwagner/S.I.R., role: non-participant, receives: []')"
# ...and the word means what it says: a row that receives something IS a participant.
expect_fail "a non-participant that RECEIVES a capability is REJECTED" \
  1 "IS a fabric participant" \
  "$(nonpart partrecv 'id: sir, full: EHotwagner/S.I.R., role: non-participant, receives: [labels], reason: contradiction')"
# The reason is refused where nothing reads it, rather than silently ignored (the rule this file
# already applies to kit-delivery and absence-cover set on a non-receiver).
expect_fail "a 'reason' on a PARTICIPATING row is REJECTED, not ignored" \
  1 "explains why a NON-PARTICIPANT is rostered" \
  "$(nonpart reasononfw 'id: spike, full: FS-GG/Spike.Repo, role: framework, receives: [labels], reason: nothing reads this')"

# LEG 3 — `receives: []`. A DISTINCT coding defect with the same effect, and independent of ownership:
# jq emitted the empty string for `[]`, bash collapsed the empty tab-delimited field (tab is IFS
# WHITESPACE), the NEXT column shifted left into `$recv`, and the validator reported a capability
# named `-`. An ordinary org row reproduced it.
expect_pass "an ORG-owned row with receives: [] VALIDATES (#2245 acceptance 3)" \
  "$(nonpart emptyrecv 'id: spike, full: FS-GG/Spike.Repo, role: framework, receives: []')"
# THE SENTINEL MUST NOT DISABLE THE VOCABULARY CHECK. This is the fail-open direction of the fix: if
# `-` mapped to "no capabilities" everywhere, an unknown word would still have to red — and a literal
# `-` under receives must be refused rather than silently read as "receives nothing".
expect_fail "an unknown capability is STILL rejected after the sentinel fix" \
  1 "unknown capability 'bogus'" \
  "$(nonpart unknownstill 'id: spike, full: FS-GG/Spike.Repo, role: framework, receives: [bogus]')"
expect_fail "a literal '-' under receives is REFUSED, never read as the empty-list sentinel" \
  1 "sentinel for an EMPTY receives list" \
  "$(nonpart dashcap 'id: spike, full: FS-GG/Spike.Repo, role: framework, receives: ["-"]')"
# ...and the role vocabulary is still closed: growing it by one word is not opening it.
expect_fail "a role outside the GROWN vocabulary is STILL rejected" \
  1 "authority|framework|non-participant" \
  "$(nonpart badrole2 'id: spike, full: FS-GG/Spike.Repo, role: banana, receives: [labels]')"

# THE ROW #2206 DECIDED, VALIDATED AGAINST THE REAL TREE. The roster does not carry it yet — the
# collaborator-only intake boundary applies to every rostered repository and `EHotwagner/S.I.R.` is
# still `ALL`, so the row lands with that policy rather than before it (#2245 acceptance 5/6, and the
# comment beside `id: net` in registry/repos.yml). What this leg holds is the half that IS this item's:
# the exact row, spelled as #2206 decided it, VALIDATES against the checked-in registry and root — so
# whoever adds it is adding a line, not reopening this defect.
REALSIR="$WORK/real-with-sir.yml"
sed 's|^  - { id: net,|  - { id: sir, full: EHotwagner/S.I.R., role: non-participant, receives: [], reason: "user-owned and doing org work (.github#2206); it takes no org fabric" }\n  - { id: net,|' \
  "$REPO_ROOT/registry/repos.yml" > "$REALSIR"
cp "$REPO_ROOT/registry/repos.lock" "${REALSIR%.yml}.lock"
if bash "$REPOS_SH" validate --registry "$REALSIR" --root "$REPO_ROOT" >/dev/null 2>&1; then
  ok "#2206's decided S.I.R. row validates against the REAL checked-in roster (#2245 acceptance 6 is now one line)"
else
  bad "the decided S.I.R. row does not validate against the real roster" \
    "$(bash "$REPOS_SH" validate --registry "$REALSIR" --root "$REPO_ROOT" 2>&1)"
fi
# It must participate in NOTHING: a `receives` word here would be a permanent unfixable gap in
# whichever sweep owns that capability.
for realcap in labels coordination-kit build-config lockfile-sync contract-coherence skill-union; do
  if bash "$REPOS_SH" list --receives "$realcap" --registry "$REALSIR" | grep -qx 'EHotwagner/S.I.R.'; then
    bad "the S.I.R. row receives '$realcap'" "a non-participant must take no fabric"
  fi
done
ok "the decided S.I.R. row receives no capability at all"
# ...and `list --all` yields it, so every sweep that starts there sees it the day it lands.
sir_full="$(bash "$REPOS_SH" list --all --registry "$REALSIR" | grep -c '^EHotwagner/S.I.R.$' || true)"
[ "$sir_full" = "1" ] && ok "list --all yields the user-owned row, so every sweep starting there sees it" \
  || bad "list --all does not yield EHotwagner/S.I.R." "got $sir_full match(es)"

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

# --- kit-delivery filter (ADR-0062/#1287): coordination-propagate asks for byte-copy only ---
# BASE: sdd receives coordination-kit with the field ABSENT, i.e. byte-copy.
kd_bc="$(bash "$REPOS_SH" list --receives coordination-kit --kit-delivery byte-copy --registry "$BASE")"
[ "$kd_bc" = "FS-GG/FS.GG.SDD" ] && ok "absent kit-delivery counts as byte-copy" || bad "kit-delivery byte-copy (absent)" "got: $kd_bc"
kd_pkg="$(bash "$REPOS_SH" list --receives coordination-kit --kit-delivery package --registry "$BASE")"
[ -z "$kd_pkg" ] && ok "no package receivers when none migrated" || bad "kit-delivery package (none)" "got: $kd_pkg"
KDPKG="$(variant kdpkg2 's/receives: \[labels, coordination-kit\]/receives: [labels, coordination-kit], kit-delivery: package/')"
kd_bc2="$(bash "$REPOS_SH" list --receives coordination-kit --kit-delivery byte-copy --registry "$KDPKG")"
[ -z "$kd_bc2" ] && ok "a migrated receiver drops out of the byte-copy set (propagate skips it)" || bad "kit-delivery byte-copy (migrated)" "got: $kd_bc2"
kd_pkg2="$(bash "$REPOS_SH" list --receives coordination-kit --kit-delivery package --registry "$KDPKG")"
[ "$kd_pkg2" = "FS-GG/FS.GG.SDD" ] && ok "a migrated receiver is in the package set" || bad "kit-delivery package (migrated)" "got: $kd_pkg2"
# usage guards
rc=0; bash "$REPOS_SH" list --receives coordination-kit --kit-delivery bogus --registry "$BASE" >/dev/null 2>&1 || rc=$?
[ "$rc" -ne 0 ] && ok "list --kit-delivery rejects a bad value" || bad "kit-delivery bad value not rejected"
rc=0; bash "$REPOS_SH" list --all --kit-delivery package --registry "$BASE" >/dev/null 2>&1 || rc=$?
[ "$rc" -ne 0 ] && ok "list --kit-delivery refuses --all (it narrows a --receives query)" || bad "kit-delivery with --all not rejected"
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
# repos-registry-selftest.yml is armed by a hand-maintained `paths:` filter that must enumerate every
# kit source, and a missing entry fails SILENTLY — the workflow simply never runs, and a workflow that
# never runs reports nothing at all: `validate` is the only thing asserting the kit digests, so a kit
# source outside its filter is never digest-checked and the gate is green because it never ran (#266).
#
# (coordination-propagate.yml — the byte-copy PUSH arm — was the OTHER subject asserted here until
# #1262 step 3 retired it. Every receiver now takes the kit as the FS.GG.Kit package via
# kit-materialize, so there is no byte-copy `paths:` filter left to keep complete.)
#
# Assert every kit source (and, for skills, the .agents mirror that carries the same bytes) is covered,
# on each trigger the workflow is supposed to fire on.
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

# A `caller:` row's subject is a reusable workflow too — validate proves it exists and is callable, so it
# carries EXACTLY the `workflow:` exposure and must be covered the same way. The detector-id → subject
# mapping lives in scripts/repos.sh; this guard has to be TAUGHT each id rather than letting an unknown
# one fall through to a silent `continue`, which is how build-config came to be ungated (#628).
CALLER_SUBJECT = {"skill-union": ".github/workflows/skill-union-assert.yml"}

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
        elif "caller" in cap:
            probe = CALLER_SUBJECT.get(cap["caller"])
            if probe is None:
                gaps.append(f"{trigger}: capability '{cap['id']}' names caller detector "
                            f"{cap['caller']!r}, whose subject this guard does not know — teach it, "
                            f"or the detector is ungated exactly as build-config was (#628)")
                continue
        else:                  continue                     # push: nothing detectable to gate
        if not any(matches(probe, p) for p in pats):
            gaps.append(f"{trigger}: capability '{cap['id']}' subject {probe}")
print("\n".join(gaps))
PY
}
uncovered="$(caps_uncovered_for ".github/workflows/repos-registry-selftest.yml" "pull_request,push")"
if [ -z "$uncovered" ]; then ok "every capability's detector subject is covered by the selftest paths: filter"
else bad "capability detector ungated — renaming it leaves the roster invalid, green" "$uncovered"; fi

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

# ---- skill-union: the RESIDUAL receiver set after the caller was retired (#1504, then #1715) ------
# THIS EXPECTATION CHANGED ON 2026-07-28 AND THE OLD ONE IS RECORDED HERE ON PURPOSE. It was
# `audio,game,governance,net,rendering,sdd,templates` — every framework repo, none of the authority —
# on the reasoning that "a framework repo that does not gate its committed roots is a repo whose
# non-Claude runtimes can be silently partitioned". That reasoning still holds. What stopped holding is
# that THIS capability is the thing that gates them: under ADR-0067 phase 4 one runtime root becomes a
# generated VIEW, `union_ids()` enumerates with `find` and no `-L` so a view root contributes ZERO ids,
# and presence is then tested with `[ -d ]` through the symlink and cannot fail. Both of the
# capability's invariants become tautologies. Measured on all three wired receivers' own trees; the
# decision is #1715 (ADR-0067 §9 phase 4, blocker B5, shape (b)) and the record is
# docs/coordination/skill-apparatus-retirement-order.md §5.1.
#
# So the three repos that actually WIRED a caller — governance, rendering, sdd — had it retired and
# their rows removed in the same change, and each gained a required `skill-view-check` context instead.
# That gate is NOT a capability of this fabric and must not be given a row: it is the receiver's own
# workflow running the receiver's own pinned `scripts/skill-view`, with no `uses:` of the authority, so
# there is nothing for a `workflow:`/`caller:` detector to see and inventing one that matched a
# FILENAME would certify the shape instead of the subject.
#
# THE EXPECTATION CHANGED AGAIN ON 2026-07-28 (#1742) AND BOTH PRIOR ONES ARE RECORDED HERE ON PURPOSE.
# The set has now been emptied. Its history, newest last:
#
#   audio,game,governance,net,rendering,sdd,templates  — every framework repo (#1504)
#   audio,game,net,templates                           — after #1715 retired the caller and the three
#                                                        that WIRED one dropped it
#   (empty)                                            — after #1742: the four that only ever DECLARED
#                                                        it dropped the word too
#
# WHY EMPTY IS THE PINNED ANSWER AND NOT A REGRESSION. Those four never wired a caller, so the only act
# that could ever have discharged their rows was wiring the retired shape — which #1715 forbids. A row
# satisfiable only by doing the forbidden thing is a trap, and it held `repos-audit` red continuously
# from 2026-07-27T11:37Z, hiding every new finding behind a standing one (#1611 category D, #1582).
# Their skill-view subject is NOT ungated: all seven receivers generate `<FsggKitViewSkillRoots>` before
# the kit's `FsggKitCheckSkillView`, swept in both directions by #1759 and #1785.
#
# THE CAPABILITY ROW ITSELF SURVIVES, with `receivers: none` + a reason — asserted separately below.
# That is the half that keeps this honest: repos-audit still scans EVERY rostered repo for a real
# caller and fails if it finds one, so "nobody receives skill-union" is falsifiable rather than merely
# declared. Deleting the row instead would have made wiring the retired shape invisible, which is the
# one mistake #1715 is trying to prevent.
#
#   `.github` is out, as it always was — it is the SOURCE of the assertion and asserts its own roots
#     with skill-roots-selfcheck.yml. Rostering the authority would demand a `uses:` of its own reusable
#     workflow, which is the phantom-adopter failure repos-audit refuses by name. Its coherence is
#     proven, it is simply proven here rather than by this capability.
su_receivers="$(bash "$REPOS_SH" list --receives skill-union --field id | sort | tr '\n' ',')"
if [ -z "$su_receivers" ]; then
  ok "skill-union: no repo declares the retired shape, and the authority is still out (#1715, #1742)"
else
  bad "skill-union: the retired capability has receivers again (#1504, #1715, #1742)" \
      "declared: $su_receivers
expected: (empty)
governance/rendering/sdd were REMOVED by #1715: the caller is retired because on a generated view its
two invariants are tautologies, and their replacement is a required skill-view-check context.
audio/game/net/templates were REMOVED by #1742: they never wired a caller, and after #1715 they never
may, so their rows were standing instructions to re-create the blocker #1715 cleared.
Re-adding ANY of them means wiring the retired shape — read
docs/coordination/skill-apparatus-retirement-order.md §5.1 and the skill-union capability row in
registry/repos.yml before changing this line.
The AUTHORITY must stay out: it is the assertion's source and dogfoods it via skill-roots-selfcheck.yml."
fi

# THE OTHER HALF OF #1742, AND IT IS WHAT STOPS THE LINE ABOVE BECOMING A MUTE BUTTON. An empty receiver
# set is only honest while the capability row is still there declaring `receivers: none` WITH a reason
# and WITH its detector: that is what makes repos-audit keep sweeping the org in the reverse direction.
# Delete the row and the receiver set is still empty, this file still says ok, and nothing anywhere
# would ever notice a repo wiring the retired caller. So assert the row, not just the absence.
# `caps` emits id<TAB>workflow<TAB>script<TAB>materializer<TAB>caller<TAB>push<TAB>receivers<TAB>reason.
# Read the fields POSITIONALLY rather than grepping the whole line: `skill-union` is its own id AND its
# own caller value, so a substring match cannot tell the row existing from the detector surviving, and
# the reason prose below mentions both words several times over.
su_row="$(bash "$REPOS_SH" caps | awk -F'\t' '$1=="skill-union"{print $5"|"$7"|"(length($8)>0?"reason":"BLANK")}')"
if [ "$su_row" = "skill-union|none|reason" ]; then
  ok "skill-union: the capability row survives its receiver set — detector + 'receivers: none' + reason intact, so the reverse sweep still runs (#1742)"
else
  bad "skill-union: the capability row lost its detector, its 'receivers: none' claim, or its reason (#1742)" \
      "caller|receivers|reason = ${su_row:-<no skill-union row at all>}
expected: skill-union|none|reason
An empty receiver set plus NO capability row is not 'retired', it is UNSWEPT: repos-audit would stop
scanning for a real caller entirely, so a repo wiring the shape #1715 retired would be invisible in
both directions. #1742 declined exactly this (shape 2) on the record. Restore the row with
'receivers: none' and a reason, or re-open that decision first."
fi

# #1806: THE ABOVE ASSERTS THE REAL FILE'S ROW SURVIVES. IT DOES NOT PROVE THAT DELETING IT WOULD BE
# CAUGHT — `validate`'s roster-keyed closure (#628) is vacuous once a word's receiver set is empty,
# which is precisely what let the row above be silently deletable before this change (measured on
# #1742's branch, 2026-07-28). Mutation-prove `validate` in BOTH directions, on the fixture rather than
# the real file, so a bad edit here cannot pass by leaving the real registry untouched.
expect_fail "deleting the skill-union capability row reds, even though its receiver set stays empty (#1806)" 1 \
  "retired-with-no-receiver" \
  "$(variant su_row_deleted '/id: skill-union,/d')"

# ...AND THE OTHER DIRECTION, so this closure cannot regress into an eleventh check-that-cannot-fail
# (#1806 AC2) by reding on any edit anywhere NEAR the capabilities: block. A plain roster growth that
# never touches `capabilities:` — adding a repo that only receives an already-detected word — must
# stay green.
su_untouched="$WORK/su_untouched.yml"
{ printf 'schemaVersion: 5\nupdated: 2026-07-13\nauthority: FS-GG/.github\nrepos:\n'
  printf '  - { id: .github, full: FS-GG/.github,   role: authority, receives: [labels] }\n'
  printf '  - { id: sdd,     full: FS-GG/FS.GG.SDD, role: framework, receives: [labels, coordination-kit] }\n'
  printf '  - { id: extra,   full: FS-GG/FS.GG.Extra, role: framework, receives: [labels] }\n'
  printf 'capabilities:\n'
  printf '  - { id: coordination-kit, workflow: coordination-coherence.yml }\n'
  printf '%s\n' "$SKILL_UNION_CAP"
  printf '%s\n' "$LABELS_CAP"
  printf 'kit:\n'
  printf '  - { id: demo-skill, kind: skill,  source: .claude/skills/demo-skill }\n'
  printf '  - { id: democlient, kind: client, source: scripts/democlient }\n'; } > "$su_untouched"
relock "$su_untouched"
expect_pass "a roster edit that adds a repo but touches no capability row stays green (#1806)" "$su_untouched"

# The byte-copy PUSH arm (build-config-propagate.yml) was RETIRED in #1262 (ADR-0062): build config now
# ships as the FS.GG.Kit package, so there is no propagate workflow whose shape (roster-driven, non-empty,
# wildcard paths) this could assert. Each receiver materializes Directory.Build.props / Directory.Packages.props
# from its pinned FS.GG.Kit, and its own `build-config-drift` job asserts the committed .props still match
# that package. The receiver-set invariant above still holds — build config is still RECEIVED (now via the
# package), it is simply no longer PUSHED.

# ---- the scoped branch-protection writer (#1613) -------------------------------------------------
#
# `repos.sh require-context` / `unrequire-context` can WEAKEN protection across the whole roster, so
# the negative legs matter more than the positive ones here. The bar this section is held to: prove it
# REFUSES, not that it works. Every leg below either asserts a refusal, or asserts the API traffic the
# tool did NOT generate — because "it did not remove anything" is a claim about calls, and a claim
# about calls cannot be checked by reading the tool's own summary line.
#
# Stubbed `gh` on PATH, per tests/repos-audit/run.sh, plus a call log: `$GH_CALL_LOG` records
# `METHOD<TAB>PATH` for every request, and several assertions read nothing else.
PSTUB="$WORK/pstub"; PFIX="$WORK/pfix"; PLOG="$WORK/gh-calls.log"
mkdir -p "$PSTUB" "$PFIX"
export PFIX

# The stub models the ONE resource this tool is allowed to touch: a branch's classic required status
# checks. It deliberately implements no other endpoint, so a tool that reached for
# `…/protection` itself, or for enforce_admins, would get an unrecognised-path failure rather than a
# convenient success — the stub cannot be the reason a scope violation passes.
#
#   $PFIX/<slug>.contexts     the branch's current required contexts, one per line (absent => 404)
#   $PFIX/<slug>.checksshape  report them via `checks[]` with an EMPTY `contexts[]` (the migrated shape)
#   $PFIX/<slug>.failread     the READ 403s (no administration: read)
#   $PFIX/<slug>.failwrite    the WRITE 403s (no administration: write)
#   $PFIX/<slug>.liar         the write returns 200 and changes NOTHING (the 200-is-not-evidence case)
#   $PFIX/<slug>.dropother    the write applies AND silently drops a pre-existing context
cat > "$PSTUB/gh" <<'STUB'
#!/usr/bin/env bash
set -uo pipefail
method=GET; path=""; ctxarg=""
args=("$@"); n=$#
for ((i=0;i<n;i++)); do
  case "${args[i]}" in
    --method)        method="${args[i+1]:-}" ;;
    repos/*)         path="${args[i]}" ;;
    'contexts[]='*)  ctxarg="${args[i]#contexts[]=}" ;;
  esac
done
[ -n "${GH_CALL_LOG:-}" ] && printf '%s\t%s\n' "$method" "$path" >> "$GH_CALL_LOG"
repo="${path#repos/}"; repo="${repo%%/branches/*}"; slug="${repo//\//__}"
f="$PFIX/$slug.contexts"

notfound()  { echo "gh: Not Found (HTTP 404)" >&2; exit 1; }
forbidden() { echo "gh: Resource not accessible by integration (HTTP 403)" >&2; exit 1; }

# Any path other than the two this tool is permitted is an outright failure, not a 404 — a 404 would
# be read as "no classic block", which is a FINDING about the repo, and would let an out-of-scope
# call masquerade as a legitimate one.
case "$path" in
  */branches/*/protection/required_status_checks|*/branches/*/protection/required_status_checks/contexts) ;;
  *) echo "gh: STUB REFUSED an endpoint outside required_status_checks: $path" >&2; exit 1 ;;
esac

case "$method" in
  GET)
    [ -f "$PFIX/$slug.failread" ] && forbidden
    [ -f "$f" ] || notfound
    if [ -f "$PFIX/$slug.checksshape" ]; then
      jq -R -s 'split("\n") | map(select(length>0))
                | { contexts: [], checks: map({context: ., app_id: null}) }' < "$f"
    else
      jq -R -s 'split("\n") | map(select(length>0)) | { contexts: ., checks: [] }' < "$f"
    fi ;;
  POST|DELETE)
    [ -f "$PFIX/$slug.failwrite" ] && forbidden
    [ -f "$f" ] || notfound
    if [ ! -f "$PFIX/$slug.liar" ]; then
      if [ "$method" = POST ]; then printf '%s\n' "$ctxarg" >> "$f"
      else { grep -Fxv -- "$ctxarg" "$f" || true; } > "$f.t"; mv "$f.t" "$f"; fi
      # A server that applies the requested change AND quietly drops something else. The tool's
      # read-back must compare the whole SET, not just look for the context it asked about.
      [ -f "$PFIX/$slug.dropother" ] && { { grep -Fxv -- "collateral" "$f" || true; } > "$f.t"; mv "$f.t" "$f"; }
      LC_ALL=C sort -u -o "$f" "$f"
    fi
    echo '{"contexts":[]}' ;;
  *) echo "gh: STUB REFUSED method $method" >&2; exit 1 ;;
esac
STUB
chmod +x "$PSTUB/gh"

PROTREG="$WORK/protect.yml"
mkprotreg() {  # mkprotreg <charlie-receives>
  cat > "$PROTREG" <<YAML
schemaVersion: 8
updated: 2026-07-28
authority: FS-GG/.github
repos:
  - { id: .github, full: FS-GG/.github,      role: authority, receives: [labels] }
  - { id: alpha,   full: FS-GG/FS.GG.Alpha,   role: framework, receives: [labels, coordination-kit] }
  - { id: bravo,   full: FS-GG/FS.GG.Bravo,   role: framework, receives: [labels, coordination-kit] }
  - { id: charlie, full: FS-GG/FS.GG.Charlie, role: framework, receives: [$1] }
capabilities:
  - { id: coordination-kit, workflow: coordination-coherence.yml }
$LABELS_CAP
YAML
}
mkprotreg "labels"

GUARD="kit-bump-shape"
pset()   { local slug="${1//\//__}"; shift; : > "$PFIX/$slug.contexts"; printf '%s\n' "$@" | grep -v '^$' >> "$PFIX/$slug.contexts" || true; }
pget()   { local slug="${1//\//__}"; LC_ALL=C sort "$PFIX/$slug.contexts" 2>/dev/null | tr '\n' ',' ; }
pclear() { rm -f "$PFIX"/*.contexts "$PFIX"/*.failread "$PFIX"/*.failwrite "$PFIX"/*.liar "$PFIX"/*.dropother "$PFIX"/*.checksshape; }
pmark()  { local slug="${1//\//__}"; : > "$PFIX/$slug.$2"; }
# A fresh, fully-protected roster: both kit receivers already require two OTHER contexts, so every
# leg below runs against a branch that has something to lose.
pbase()  { pclear; pset FS-GG/FS.GG.Alpha "Deterministic gate" collateral; pset FS-GG/FS.GG.Bravo "Deterministic gate" collateral; }

POUT=""; PRC=0
prun() { PRC=0; : > "$PLOG"
         POUT="$(GH_CALL_LOG="$PLOG" PATH="$PSTUB:$PATH" bash "$REPOS_SH" "$@" --registry "$PROTREG" 2>&1)" || PRC=$?; }
ncalls()  { grep -c "^$1	" "$PLOG" 2>/dev/null || true; }   # ncalls <METHOD>
allcalls() { wc -l < "$PLOG" | tr -d ' '; }

# expect_refusal <name> <expected-exit> <substr> — the command must fail AND must have made NO API
# call whatsoever. A refusal that already touched the API is not a refusal.
expect_refusal() {
  local n="$1" want="$2" substr="$3"; shift 3
  prun "$@"
  if [ "$PRC" -eq 0 ]; then bad "$n" "expected a refusal, got exit 0: $POUT"; return; fi
  if [ "$PRC" -ne "$want" ]; then bad "$n" "expected exit $want, got $PRC: $POUT"; return; fi
  case "$POUT" in *"$substr"*) ;; *) bad "$n" "exit $PRC but missing '$substr': $POUT"; return ;; esac
  if [ "$(allcalls)" != "0" ]; then bad "$n" "refused, but made $(allcalls) API call(s): $(cat "$PLOG")"; return; fi
  ok "$n"
}

echo "-- branch-protection writer (#1613)"

# === THE SEPARATION OF ADD FROM REMOVE ===========================================================
# This is the item's binding constraint: adding and removing are different operations with different
# authority, and removal must never fire as a side effect of anything else. Four legs, all negative.

pbase
expect_refusal "remove REFUSES without --confirm-remove" 2 "REFUSING to remove a required context" \
  unrequire-context --context "$GUARD" --receives coordination-kit --apply
expect_refusal "remove REFUSES a --confirm-remove naming a DIFFERENT context" 2 "does not match" \
  unrequire-context --context "$GUARD" --receives coordination-kit --confirm-remove "some-other-check" --apply
# The add verb has no removal flag, and the removal verb's confirmation is not even a WORD it knows.
# Tolerating-and-ignoring it would be worse than rejecting it: a flag that is accepted on the wrong
# verb is a flag somebody will believe did something.
expect_refusal "add REJECTS --confirm-remove outright, and names the separate verb" 2 "unrequire-context" \
  require-context --context "$GUARD" --receives coordination-kit --confirm-remove "$GUARD" --apply
for flag in --remove --delete --unrequire --op --mode --prune; do
  expect_refusal "add has no '$flag' — there is no argument that flips it to removal" 2 "unknown arg" \
    require-context --context "$GUARD" --receives coordination-kit "$flag" remove --apply
done

# === WHAT PROVES REMOVE CANNOT FIRE AS A SIDE EFFECT: THE RECORDED TRAFFIC =======================
# The tool's own summary saying "added" is not evidence that nothing was deleted. The call log is.
pbase
prun require-context --context "$GUARD" --receives coordination-kit --apply
if [ "$PRC" -eq 0 ] && [ "$(ncalls DELETE)" = "0" ] && [ "$(ncalls PUT)" = "0" ] \
   && [ "$(ncalls PATCH)" = "0" ] && [ "$(ncalls POST)" = "2" ]; then
  ok "add path issues GET+POST only — zero DELETE, zero PUT, zero PATCH over the whole roster"
else
  bad "add path issued a mutating call it must never make" \
      "exit $PRC; POST=$(ncalls POST) DELETE=$(ncalls DELETE) PUT=$(ncalls PUT) PATCH=$(ncalls PATCH)
$(cat "$PLOG")"
fi
# `enforce_admins: true` is live on several receivers, and the endpoint that can clear it is the
# whole-object PUT on `…/protection` — the one this tool must never name. Asserted on TRAFFIC, over
# both verbs, because a source-level promise is not a runtime guarantee.
badpath=0
for p in enforce_admins required_pull_request_reviews restrictions required_signatures; do
  grep -q "$p" "$PLOG" && badpath=1
done
grep -qE '	repos/[^	]*/protection$' "$PLOG" && badpath=1
if [ "$badpath" = 0 ]; then ok "add path never names the protection object, enforce_admins, or review requirements"
else bad "add path reached outside required_status_checks" "$(cat "$PLOG")"; fi

# === SOURCE-LEVEL CONTAINMENT: the endpoints are the guarantee, not the flag parsing =============
# Every `gh api` call in repos.sh must go through the one wrapper, and every call of that wrapper must
# pass a path built by one of the two path functions. That is what makes "no bug in this file can
# reach enforce_admins" a checkable statement rather than a comment.
# CODE ONLY — comment lines are stripped first. This file's headers discuss the endpoints it must
# never call, at length and on purpose, and a check that counted prose would be satisfiable by
# rewording a comment. It is the executable lines that have to be clean.
code() { grep -vE '^[[:space:]]*#' "$REPOS_SH" | grep -n "$1" || true; }
napi="$(code '[^_]gh api ' | wc -l | tr -d ' ')"
if [ "$napi" = "1" ]; then ok "repos.sh invokes 'gh api' from exactly ONE place (the audited wrapper)"
else bad "repos.sh has $napi 'gh api' call sites — every API path must funnel through one wrapper" \
        "$(code '[^_]gh api ')"; fi
offsite="$(code 'gh_api_capture ' | grep -v 'rsc_read_path\|rsc_write_path\|gh_api_capture() {' || true)"
if [ -z "$offsite" ]; then ok "every gh_api_capture call passes an rsc_read_path/rsc_write_path path"
else bad "a gh_api_capture call names an API path outside required_status_checks" "$offsite"; fi
if [ "$(grep -c "branches/%s/protection/required_status_checks" "$REPOS_SH" || true)" = "2" ] \
   && [ "$(grep -c "branches/%s/protection'" "$REPOS_SH" || true)" = "0" ]; then
  ok "the only two protection paths repos.sh names are both under required_status_checks"
else
  bad "repos.sh names a branch-protection path outside required_status_checks" \
      "$(grep -n 'branches/%s/protection' "$REPOS_SH")"
fi

# === DRY RUN BY DEFAULT ==========================================================================
pbase
prun require-context --context "$GUARD" --receives coordination-kit
if [ "$PRC" -eq 0 ] && [ "$(ncalls POST)" = "0" ] && [ "$(ncalls DELETE)" = "0" ] \
   && [ "$(pget FS-GG/FS.GG.Alpha)" = "Deterministic gate,collateral," ] \
   && case "$POUT" in *WOULD-ADD*) true ;; *) false ;; esac; then
  ok "no --apply: reports WOULD-ADD, issues zero writes, leaves the branch untouched"
else
  bad "the dry run is not dry" "exit $PRC; POST=$(ncalls POST); alpha=$(pget FS-GG/FS.GG.Alpha)
$POUT"
fi

# === APPLY, READ BACK, AND IDEMPOTENCE ===========================================================
pbase
prun require-context --context "$GUARD" --receives coordination-kit --apply
if [ "$PRC" -eq 0 ] && [ "$(pget FS-GG/FS.GG.Alpha)" = "Deterministic gate,collateral,$GUARD," ] \
   && [ "$(pget FS-GG/FS.GG.Bravo)" = "Deterministic gate,collateral,$GUARD," ]; then
  ok "--apply arms every roster-derived receiver, preserving the contexts already required"
else bad "--apply did not arm the roster" "exit $PRC; alpha=$(pget FS-GG/FS.GG.Alpha) bravo=$(pget FS-GG/FS.GG.Bravo)
$POUT"; fi
# Idempotence is not "the second run also ends green" — it is that the second run WRITES NOTHING.
prun require-context --context "$GUARD" --receives coordination-kit --apply
if [ "$PRC" -eq 0 ] && [ "$(ncalls POST)" = "0" ] \
   && case "$POUT" in *"UNCHANGED (already required)"*) true ;; *) false ;; esac; then
  ok "a second --apply reports UNCHANGED and issues zero writes (idempotent, and provably so)"
else bad "the second run wrote something" "exit $PRC; POST=$(ncalls POST)
$POUT"; fi

# The migrated protection shape: contexts reported via `checks[]` with an EMPTY `contexts[]`. Reading
# only the deprecated array would say "nothing is required here" about a fully protected branch, and
# the tool would then POST a context that is already required and compare the read-back to a fiction.
pbase; pset FS-GG/FS.GG.Alpha "Deterministic gate" collateral "$GUARD"; pmark FS-GG/FS.GG.Alpha checksshape
prun require-context --context "$GUARD" --receives coordination-kit --only FS-GG/FS.GG.Alpha --apply
if [ "$PRC" -eq 0 ] && [ "$(ncalls POST)" = "0" ]; then
  ok "a branch reporting its contexts via checks[] is read correctly — no redundant write"
else bad "the checks[] protection shape was read as empty" "exit $PRC; POST=$(ncalls POST)
$POUT"; fi

# === FAIL CLOSED ON PARTIAL APPLICATION ==========================================================
pbase; pmark FS-GG/FS.GG.Bravo failwrite
prun require-context --context "$GUARD" --receives coordination-kit --apply
if [ "$PRC" -eq 1 ] \
   && [ "$(pget FS-GG/FS.GG.Alpha)" = "Deterministic gate,collateral,$GUARD," ] \
   && case "$POUT" in *"1 added, 0 unchanged, 1 failed"*) true ;; *) false ;; esac \
   && case "$POUT" in *"administration: write"*) true ;; *) false ;; esac \
   && [ "$(ncalls POST)" = "2" ]; then
  ok "a repo whose write 403s: per-repo outcomes, exit 1, the credential named, and NO blind retry"
else
  bad "partial application did not fail closed" "exit $PRC; POST=$(ncalls POST) (a retry would make it 3)
$POUT"
fi

# A branch with no CLASSIC required-status-checks block. Creating one is a whole-object PUT — the one
# call that can disable enforce_admins by omission — so this must REFUSE, not helpfully arm it.
pbase; rm -f "$PFIX/FS-GG__FS.GG.Bravo.contexts"
prun require-context --context "$GUARD" --receives coordination-kit --apply
if [ "$PRC" -eq 1 ] && [ "$(ncalls POST)" = "1" ] \
   && case "$POUT" in *"will not CREATE one"*) true ;; *) false ;; esac; then
  ok "an unprotected branch is a REFUSAL — the tool never creates protection it would have to PUT"
else bad "the tool tried to create protection" "exit $PRC; POST=$(ncalls POST)
$POUT"; fi

# HTTP 200 IS NOT EVIDENCE. The write succeeds and the branch does not hold the context.
pbase; pmark FS-GG/FS.GG.Bravo liar
prun require-context --context "$GUARD" --receives coordination-kit --apply
if [ "$PRC" -eq 1 ] && [ "$(ncalls POST)" = "2" ] \
   && case "$POUT" in *"READ-BACK MISMATCH"*) true ;; *) false ;; esac; then
  ok "a write that returns 200 and changes nothing is a FAILURE, caught by read-back, not retried"
else bad "success was inferred from an HTTP 200" "exit $PRC; POST=$(ncalls POST)
$POUT"; fi

# REFUSE TO REDUCE. The write applies the requested context AND silently drops another. Looking for
# `$GUARD` in the read-back would pass this; comparing the whole SET is what catches it.
pbase; pmark FS-GG/FS.GG.Bravo dropother
prun require-context --context "$GUARD" --receives coordination-kit --apply
if [ "$PRC" -eq 1 ] && case "$POUT" in *"READ-BACK MISMATCH"*) true ;; *) false ;; esac; then
  ok "an add that would REDUCE the required set (a context vanished) fails read-back"
else bad "the add path let a pre-existing required context disappear" "exit $PRC
$POUT"; fi

# === THE REMOVAL VERB, FULLY CONFIRMED ===========================================================
pbase; pset FS-GG/FS.GG.Alpha "Deterministic gate" collateral "$GUARD"
pset FS-GG/FS.GG.Bravo "Deterministic gate" collateral "$GUARD"
prun unrequire-context --context "$GUARD" --receives coordination-kit --confirm-remove "$GUARD" --apply
if [ "$PRC" -eq 0 ] && [ "$(ncalls DELETE)" = "2" ] && [ "$(ncalls POST)" = "0" ] \
   && [ "$(pget FS-GG/FS.GG.Alpha)" = "Deterministic gate,collateral," ]; then
  ok "the removal verb, fully confirmed, removes exactly the named context and nothing else"
else bad "the removal verb misbehaved" "exit $PRC; DELETE=$(ncalls DELETE) POST=$(ncalls POST) alpha=$(pget FS-GG/FS.GG.Alpha)
$POUT"; fi
# Naming a context that is not there is a typo, not a no-op: the operator believes they removed
# something. Silence would turn the typo into "done".
prun unrequire-context --context "$GUARD" --receives coordination-kit --confirm-remove "$GUARD" --apply
if [ "$PRC" -eq 1 ] && [ "$(ncalls DELETE)" = "0" ] \
   && case "$POUT" in *"nothing to remove"*) true ;; *) false ;; esac; then
  ok "removing a context that is NOT required is a failure, not a silent no-op"
else bad "an absent context removed silently" "exit $PRC; DELETE=$(ncalls DELETE)
$POUT"; fi

# === THE TARGET SET IS THE ROSTER, AND MOVES WITH IT ==============================================
pbase
expect_refusal "a capability NO repo receives is refused, never swept as an empty set" 2 "EMPTY target set" \
  require-context --context "$GUARD" --receives skill-union --apply
expect_refusal "--only NARROWS the roster set and cannot extend it" 2 "is not among the repos that receive" \
  require-context --context "$GUARD" --receives coordination-kit --only FS-GG/FS.GG.Charlie --apply
expect_refusal "--context is required" 2 "--context" require-context --receives coordination-kit
# Every comparison in the tool — the target-set membership test, `want`, and the read-back equality —
# is line-oriented. A multi-line context would make the read-back verify a set it never wrote.
expect_refusal "a multi-line --context is refused, not silently mis-compared" 2 "SINGLE LINE" \
  require-context --context "$(printf 'a\nb')" --receives coordination-kit --apply
expect_refusal "--receives is required — targets are never hand-listed" 2 "never hand-listed" \
  require-context --context "$GUARD"

pbase; prun require-context --context "$GUARD" --receives coordination-kit --only FS-GG/FS.GG.Alpha --apply
if [ "$PRC" -eq 0 ] && [ "$(pget FS-GG/FS.GG.Alpha)" = "Deterministic gate,collateral,$GUARD," ] \
   && [ "$(pget FS-GG/FS.GG.Bravo)" = "Deterministic gate,collateral," ]; then
  ok "--only arms exactly one roster member — the recovery path after a partial application"
else bad "--only did not narrow" "alpha=$(pget FS-GG/FS.GG.Alpha) bravo=$(pget FS-GG/FS.GG.Bravo)"; fi

# THE ANTI-FIFTH-COPY LEG (#1507/#1510/#1515/#1528/#1538): the target set moves when the ROSTER moves,
# with no edit anywhere else. If this ever fails, a repo list has grown somewhere it should not have.
pbase; pset FS-GG/FS.GG.Charlie "Deterministic gate"
mkprotreg "labels, coordination-kit"
prun require-context --context "$GUARD" --receives coordination-kit --apply
mkprotreg "labels"
if [ "$PRC" -eq 0 ] && [ "$(pget FS-GG/FS.GG.Charlie)" = "Deterministic gate,$GUARD," ] \
   && case "$POUT" in *"3 repo(s)"*) true ;; *) false ;; esac; then
  ok "adding a receiver to the ROSTER moves the target set, with no other edit (no fifth copy)"
else bad "the target set did not follow the roster" "exit $PRC; charlie=$(pget FS-GG/FS.GG.Charlie)
$POUT"; fi

# The real roster's seven kit receivers are what `#1587` AC2 needs armed. Pinned so a roster change
# that silently drops one from the fabric is visible here rather than at apply time.
kit_receivers="$(bash "$REPOS_SH" list --receives coordination-kit --field id | sort | tr '\n' ',')"
if [ "$kit_receivers" = "audio,game,governance,net,rendering,sdd,templates," ]; then
  ok "require-context's real target set for coordination-kit is the SEVEN receivers #1587 names"
else bad "the coordination-kit receiver set is not the seven #1587 must arm" "declared: $kit_receivers"; fi

echo "repos-registry fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::repos-registry fixture FAILED"; exit 1; }
echo "repos-registry fixture — OK"
