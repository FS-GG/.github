#!/usr/bin/env bash
# Fixture for scripts/check-engine-flag-narrative.py — the retired `--engine` flag gate (.github#917).
#
# The gate exists because the narrative outlived the thing it described, FOUR times (#836, #866/#910,
# #912, #917), and every hand-pass was scoped by a `Paths:` touch-set that structurally could not see
# the next tier. So the FAILURE LEGS are the point of this fixture, and the TIER-FIVE leg below is the
# point of the gate: it plants the flag in a directory this fixture invents on the spot, one nobody
# listed anywhere, and requires the gate to find it. A gate that only looks where the last four passes
# looked would be the fifth instance of the bug it closes.
#
# Every leg asserts the EXIT CODE (the gate's contract) and, for findings, that the finding names the
# right file:line. No leg greps the gate's prose for a verdict.
#
# The last leg runs the gate against THIS REPO's real tree and requires green. Without it, every leg
# above is synthetic and the gate could pass its own tests while the shipped tree describes a flag
# nobody can select.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
GATE="$REPO_ROOT/scripts/check-engine-flag-narrative.py"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/engine-flag-narrative-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

echo "engine-flag-narrative fixture — work='$WORK'"

# The flag, assembled rather than written, so this fixture can talk about the string it plants without
# every leg below tripping the gate when the fixture directory itself is audited in the real-tree leg.
# (tests/engine-flag-narrative/ is allowlisted, so this is belt-and-braces — but a fixture that only
# passes because of its own allowlist entry is one that stops proving anything the day that entry moves.)
FS_FLAG="--engine=fs"
BASH_FLAG="--engine=bash"

# Seed a throwaway tree with the NON-VACUITY subject the gate fails closed on: an ADR that legitimately
# carries the flag. Every synthetic tree needs one, or the gate correctly refuses to render a verdict
# over a tree with no mention of its subject at all — which is leg 9's job to prove.
seed() {
  local dir="$1"
  mkdir -p "$dir/docs/adr"
  printf 'ADR-0034: the engine ships behind `%s`, defaulting off.\n' "$FS_FLAG" \
    > "$dir/docs/adr/0034-typed-coordination-engine.md"
}

# usage: gate_on <case> -> sets $RC and $OUT   (call after building $WORK/<case>)
gate_on() {
  set +e
  OUT="$(python3 "$GATE" --root "$WORK/$1" 2>&1)"
  RC=$?
  set -e
}

# ---------------------------------------------------------------------------------------------
# FAILURE LEGS — each must fire. If any of these goes green the gate is decorative.
# ---------------------------------------------------------------------------------------------

# 1. The live #917 bug: a present-tense consequence clause in an F# doc comment.
seed "$WORK/src-doc-comment"
mkdir -p "$WORK/src-doc-comment/src/FS.GG.Coord.Core"
cat > "$WORK/src-doc-comment/src/FS.GG.Coord.Core/Types.fs" <<EOF
    /// An item bash called BLOCKED then reached the engine as unblocked, which under \`$FS_FLAG\` is a
    /// worker being handed blocked work.
    type Blocker = { Ref: Ref option }
EOF
gate_on src-doc-comment
if [ "$RC" = 1 ] && printf '%s' "$OUT" | grep -q 'src/FS.GG.Coord.Core/Types.fs:1:'; then
  ok "an F# doc comment describing the dead flag as live is a FINDING, named at its line"
else
  bad "src/ doc comment should exit 1 and name Types.fs:1 (got rc=$RC)" "$OUT"
fi

# 2. TIER FIVE — the whole point. A directory invented right here, listed in no allowlist, named by no
#    issue, that none of the four hand-passes could have known to look in. The gate's surface is the
#    TREE, so this must be a finding on the day the directory is created, by nobody's remembering. If
#    this leg ever goes green, the gate has been narrowed back to a touch-set and is worthless.
seed "$WORK/tier-five"
mkdir -p "$WORK/tier-five/deploy/terraform/modules"
printf 'run = "fsgg-coord take %s"  # a tier nobody predicted\n' "$FS_FLAG" \
  > "$WORK/tier-five/deploy/terraform/modules/runner.tf"
gate_on tier-five
if [ "$RC" = 1 ] && printf '%s' "$OUT" | grep -q 'deploy/terraform/modules/runner.tf:1:'; then
  ok "TIER FIVE: the flag in an unlisted, unpredicted directory is a FINDING"
else
  bad "tier five should exit 1 and name runner.tf:1 — the gate has been narrowed to a touch-set (got rc=$RC)" "$OUT"
fi

# 3. #912's tier: a workflow. Its own gate workflow is allowlisted by exact path, so a DIFFERENT
#    workflow must still red — an allowlist entry that leaked to the directory would re-open tier three.
seed "$WORK/workflow-tier"
mkdir -p "$WORK/workflow-tier/.github/workflows"
printf 'name: x\njobs:\n  a:\n    steps:\n      - run: fsgg-coord decide %s\n' "$FS_FLAG" \
  > "$WORK/workflow-tier/.github/workflows/release.yml"
gate_on workflow-tier
if [ "$RC" = 1 ] && printf '%s' "$OUT" | grep -q '.github/workflows/release.yml:5:'; then
  ok "a workflow other than the gate's own is a FINDING (#912's tier stays closed)"
else
  bad "workflow tier should exit 1 and name release.yml:5 (got rc=$RC)" "$OUT"
fi

# 4. #866/#910's tier: docs/architecture.md. `docs/` at large is NOT allowlisted, and this is why —
#    a blanket `docs/` rule would silently re-open the tier #866 closed.
seed "$WORK/docs-tier"
printf 'The client selects the engine with `%s`.\n' "$FS_FLAG" > "$WORK/docs-tier/docs/architecture.md"
gate_on docs-tier
if [ "$RC" = 1 ] && printf '%s' "$OUT" | grep -q 'docs/architecture.md:1:'; then
  ok "docs/architecture.md is a FINDING — docs/ at large is not allowlisted (#866's tier stays closed)"
else
  bad "docs/architecture.md should exit 1 (got rc=$RC)" "$OUT"
fi

# 5. The SIBLING SPELLINGS. #917 found `=fs`; the whole flag went with ADR-0040, and gating only the
#    spelling that happened to be found this time is the touch-set mistake wearing a different hat.
seed "$WORK/spellings"
mkdir -p "$WORK/spellings/src"
printf 'let a = 1 // rollback with %s\nlet b = 2 // pass --engine to pick\n' "$BASH_FLAG" \
  > "$WORK/spellings/src/Client.fs"
gate_on spellings
if [ "$RC" = 1 ] \
  && printf '%s' "$OUT" | grep -q 'src/Client.fs:1:' \
  && printf '%s' "$OUT" | grep -q 'src/Client.fs:2:'; then
  ok "the sibling spellings ($BASH_FLAG, and a bare --engine) are FINDINGS too"
else
  bad "both spellings should be findings, at lines 1 and 2 (got rc=$RC)" "$OUT"
fi

# ---------------------------------------------------------------------------------------------
# NEGATIVE LEGS — a gate that cries wolf gets ignored, and then it protects nothing. Each of these
# is a place the flag is HISTORY, and a red here would push somebody to falsify a record.
# ---------------------------------------------------------------------------------------------

# 6. The ADRs. They introduced, gated and retired the flag; an ADR edited to remove it is a falsified
#    record, which is a worse defect than the one this gate closes. The seed alone is this tree.
seed "$WORK/adr-only"
gate_on adr-only
if [ "$RC" = 0 ]; then
  ok "docs/adr/ is history, not a finding"
else
  bad "an ADR mention must not be a finding (got rc=$RC)" "$OUT"
fi

# 7. The rejection test. The string IS its subject: strip it and the assertion that the flag is refused
#    stops existing — the one test standing between this gate and the flag quietly coming back.
seed "$WORK/rejection-test"
mkdir -p "$WORK/rejection-test/tests/FS.GG.Coord.Cli.Tests"
cat > "$WORK/rejection-test/tests/FS.GG.Coord.Cli.Tests/OptionsTests.fs" <<EOF
        let e = parse [ "decide"; "$FS_FLAG" ] |> rejected
        Assert.Contains("$FS_FLAG", e)
EOF
gate_on rejection-test
if [ "$RC" = 0 ]; then
  ok "the rejection test keeps its string without redding the gate"
else
  bad "OptionsTests.fs asserts the REJECTION and must be allowed (got rc=$RC)" "$OUT"
fi

# 8. A DIFFERENT flag that merely starts with the same letters is not this gate's business. Crying
#    wolf on `--engine-log` is how a gate gets switched off.
seed "$WORK/other-flag"
mkdir -p "$WORK/other-flag/src"
printf 'let x = 1 // pass --engine-log=debug to trace\n' > "$WORK/other-flag/src/Trace.fs"
gate_on other-flag
if [ "$RC" = 0 ]; then
  ok "--engine-log=debug is a different flag, not a finding"
else
  bad "a longer flag sharing the prefix must not fire (got rc=$RC)" "$OUT"
fi

# ---------------------------------------------------------------------------------------------
# FAIL-CLOSED LEGS (#266) — "I could not check" must never share a code with "I checked, and it's
# fine", nor with "I checked, and it's broken" (#320).
# ---------------------------------------------------------------------------------------------

# 9. NON-VACUITY. A tree with no mention of the flag ANYWHERE means the walker or the pattern is
#    broken — the real tree's ADRs are built out of this flag. Examining nothing is a failure to
#    audit, not a clean audit, so this must be 3 (no verdict) and NEVER 0.
mkdir -p "$WORK/vacuous/src"
printf 'let x = 1 // nothing to see\n' > "$WORK/vacuous/src/X.fs"
gate_on vacuous
if [ "$RC" = 3 ] && printf '%s' "$OUT" | grep -q 'no verdict'; then
  ok "a tree with NO mention at all is NO VERDICT (3), not a clean pass"
else
  bad "non-vacuity guard must exit 3, never 0 — a broken walker would read as green (got rc=$RC)" "$OUT"
fi

# 10. A root that is not a directory: no verdict, not a pass.
gate_on definitely-not-a-directory
if [ "$RC" = 3 ]; then
  ok "a missing root is NO VERDICT (3)"
else
  bad "a missing root must exit 3 (got rc=$RC)" "$OUT"
fi

# 10b. THE CANARY, and the fail-open this gate ALMOST shipped with. The global "did we see the flag
#      at all?" count is satisfied by the ADRs alone — so a walk that silently stopped covering
#      `tests/` still counted ~70 allowlisted mentions and printed a confident green over a tree it
#      never read (measured, on the real tree, before the guard landed: `ok … 77 mention(s)`, rc=0).
#      That is #266's signature and .github#930's sub-class (b): the subject is not covered, the
#      check is satisfied, the gate stays green — the very family this gate belongs to.
#
#      Here the rejection test EXISTS and carries no mention, which is impossible while the reader
#      and the pattern work: its whole subject is that string. So it must be NO VERDICT, not a pass.
seed "$WORK/canary-blind"
mkdir -p "$WORK/canary-blind/tests/FS.GG.Coord.Cli.Tests"
printf 'let e = parse [ "decide" ] |> rejected  // the flag is gone from its own assertion\n' \
  > "$WORK/canary-blind/tests/FS.GG.Coord.Cli.Tests/OptionsTests.fs"
gate_on canary-blind
if [ "$RC" = 3 ] && printf '%s' "$OUT" | grep -q 'no verdict'; then
  ok "the CANARY fires: the rejection test carrying no mention is NO VERDICT (3), not a pass"
else
  bad "canary guard must exit 3 — the ADR count alone would otherwise green a blind read (got rc=$RC)" "$OUT"
fi

# 10c. COVERAGE. Once the repair lands, `src/` legitimately carries ZERO mentions — so "was `src/`
#      audited?" cannot be answered by looking for content in it: absence is the correct state AND
#      what a broken walk produces. The guard therefore asserts the tree was REACHED, not what was
#      in it. A `src/` the walk never reaches a file in is an audit with no opinion about `src/`,
#      and that must not read as clean.
seed "$WORK/coverage-blind"
mkdir -p "$WORK/coverage-blind/src"   # exists, holds nothing the walk can reach
gate_on coverage-blind
if [ "$RC" = 3 ] && printf '%s' "$OUT" | grep -q "src/"; then
  ok "COVERAGE: a src/ the walk reached no file in is NO VERDICT (3), and the message names it"
else
  bad "coverage guard must exit 3 and name src/ (got rc=$RC)" "$OUT"
fi

# ---------------------------------------------------------------------------------------------
# THE REAL TREE. Without this, every leg above is synthetic: the gate could pass its own fixture
# while the shipped tree describes a flag no worker can select.
# ---------------------------------------------------------------------------------------------

# 11.
set +e
OUT="$(python3 "$GATE" --root "$REPO_ROOT" 2>&1)"; RC=$?
set -e
if [ "$RC" = 0 ]; then
  ok "the SHIPPED tree is green"
else
  bad "the shipped tree must be green (got rc=$RC)" "$OUT"
fi

# 12. ...and green for the right REASON. A gate whose walker silently stopped finding files would
#     print a clean pass over a tree it never read — #266's signature exactly. The real tree carries
#     the flag in its ADRs and its rejection test, so a plausible mention count is the tell that the
#     walk actually happened.
if printf '%s' "$OUT" | grep -qE 'mention\(s\) audited' \
  && [ "$(printf '%s' "$OUT" | sed -n 's/.*— \([0-9]*\) mention(s) audited.*/\1/p')" -ge 20 ]; then
  ok "the shipped tree's green reports a plausible mention count (the walk really ran)"
else
  bad "green must be backed by a real audit count — a walker that found nothing reads as clean" "$OUT"
fi

echo
echo "engine-flag-narrative fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1
