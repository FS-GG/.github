#!/usr/bin/env bash
# Fixture for scripts/skill-view-parity.sh — .github#1674 (ADR-0067 phase 3), epic .github#1611.
#
# PHASE 3 IS "AGREEMENT, NOT REPLACEMENT", AND THIS SUITE IS WHERE THAT IS ASSERTED RATHER THAN
# ASSUMED. ADR-0067 §9: *"build the view and the absence check; run it alongside the existing gates
# unchanged; PORT the fixtures rather than re-derive them; retire the old apparatus per repo."* The
# harness under test runs `scripts/skill-union-assert.sh` as a SUBPROCESS, unmodified. Nothing here
# retires anything — retirement is phase 4 (.github#1676).
#
# THE FIXTURES BELOW ARE PORTS, AND EACH ONE CARRIES ITS CLASS. Phase 1 (.github#1621 §5.7) measured
# that the existing fixtures do NOT retire at the same rate, and ADR-0067 records the consequence:
# *"Losing the first is cheap; losing either of the others is a straight regression."*
#
#   class 1  coherent-but-incomplete (#1504)                  — GENUINELY DISSOLVES under one source.
#            Its LESSON does not (group 4): the subset-manifest trap is reachable through
#            `skill-view check --manifest`, and group 4 pins that this harness does not inherit it.
#   class 2  two implementations of one invariant disagreeing
#            (#1506, #1513, #1547, #1589)                     — SURVIVES UNTOUCHED; §3 retires it,
#            not this phase. Groups 2, 3 and 6 port each fact and assert, executably, that the
#            generated-view check does NOT subsume it — which is the argument for keeping the
#            originals, made as a test rather than as a paragraph.
#   class 3  verify-against-a-moving-target (#1549)           — ORTHOGONAL, owned by §2. Ported as
#            group 1d: this harness's verdict is a pure function of the tree, with no remote tip.
#
# A PORT ASKS "DOES THIS STILL ASSERT WHAT IT ASSERTED?", A RE-DERIVATION ASKS "WHAT SHOULD THIS
# ASSERT NOW?" — and they differ exactly where it matters. So every leg below names the issue whose
# fact it carries, and the fact is stated in the leg label, not inferred from the fixture's shape.
#
# EVERY "MUST FAIL" LEG ASSERTS THE EXIT CODE **AND** THE REASON. tests/feed-coherence/run.sh:10 names
# the trap: a red leg whose non-zero exit came from a path typo or a missing dependency would pass
# against a subject broken in a completely different way. Exit 1 (a disagreement) and exit 2 (not
# comparable) are DIFFERENT verdicts here and are never accepted for each other.
#
# ANTI-VACUITY. The leg count is asserted at the end (tests/skill-union/skillmirror-conformance.sh's
# rule). A suite that silently ran four of its legs and printed "0 failed" is the shape epic #266
# exists to refuse.
#
# OFFLINE, NO NETWORK, NO `gh`. Only git, bash, python3 and this repo's own scripts.

set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
PARITY="$ROOT/scripts/skill-view-parity.sh"
SV="$ROOT/scripts/skill-view"
UNION="$ROOT/scripts/skill-union-assert.sh"

for f in "$PARITY" "$SV" "$UNION"; do
  [ -f "$f" ] || { printf '::error::skill-view-parity fixture: missing %s\n' "$f" >&2; exit 2; }
done

WORK="$(mktemp -d "${TMPDIR:-/tmp}/skill-view-parity-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

export GIT_CONFIG_GLOBAL="$WORK/gitconfig"
export GIT_CONFIG_NOSYSTEM=1
: > "$GIT_CONFIG_GLOBAL"

pass=0
failcount=0
legs=0
ok()  { printf 'PASS  %s\n' "$1"; pass=$((pass + 1)); }
bad() {
  printf 'FAIL  %s\n' "$1"
  [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'
  failcount=$((failcount + 1))
}
leg() { legs=$((legs + 1)); }

# expect <label> <want-exit> <want-substring> -- <argv...>
expect() {
  local label="$1" want_rc="$2" want_txt="$3"; shift 3
  [ "$1" = "--" ] && shift
  leg
  local out rc=0
  out="$("$@" 2>&1)" || rc=$?
  if [ "$rc" -ne "$want_rc" ]; then
    bad "$label — exit $rc, want $want_rc" "$out"
    return
  fi
  if [ -n "$want_txt" ] && ! printf '%s' "$out" | grep -qF -- "$want_txt"; then
    bad "$label — exit $want_rc as expected, but the reason is wrong (want substring: $want_txt)" "$out"
    return
  fi
  ok "$label"
}

skill_md() {  # <dir> <id> [<suffix>]
  mkdir -p "$1/$2"
  printf -- '---\nname: %s\ndescription: fixture skill %s.\n---\n\nBody of %s.%s\n' \
    "$2" "$2" "$2" "${3:-}" > "$1/$2/SKILL.md"
}

# ---------------------------------------------------------------------------------------------
# make_copied <dir> <n> — TODAY'S layout: two roots, each a real committed COPY. This is what every
# rostered repo is in as this lands, and it is the tree the "alongside" comparison is about.
# ---------------------------------------------------------------------------------------------
make_copied() {
  local dir="$1" n="${2:-3}" i
  mkdir -p "$dir/.claude/skills" "$dir/.agents/skills"
  for i in $(seq 1 "$n"); do
    skill_md "$dir/.claude/skills" "skill-$i"
    skill_md "$dir/.agents/skills" "skill-$i"
  done
  printf '.claude/skills\n.agents/skills\n' > "$dir/.agent-skill-roots"
  git init -q "$dir"
  git -C "$dir" add -A
  git -C "$dir" -c user.email=f@fixture -c user.name=fixture commit -qm "copied roots"
}

# ---------------------------------------------------------------------------------------------
# make_resolved <dir> <n> — ADR-0067 §5's END STATE: `.agents/skills` IS the source (tracked,
# canonical, because Codex discovers it with no configuration) and `.claude/skills` is a view of it,
# produced by the phase-2 generator itself rather than by a hand-rolled lookalike.
# ---------------------------------------------------------------------------------------------
make_resolved() {
  local dir="$1" n="${2:-3}" i
  mkdir -p "$dir/.agents/skills"
  for i in $(seq 1 "$n"); do skill_md "$dir/.agents/skills" "skill-$i"; done
  printf '.claude/skills\n.agents/skills\n' > "$dir/.agent-skill-roots"
  printf '/.claude/skills\n' > "$dir/.gitignore"
  git init -q "$dir"
  git -C "$dir" add -A
  git -C "$dir" -c user.email=f@fixture -c user.name=fixture commit -qm "canonical .agents/skills"
  bash "$SV" generate --source "$dir/.agents/skills" --tree "$dir" >/dev/null 2>&1
}

# ---------------------------------------------------------------------------------------------
# make_allview <dir> <n> — the layout where EVERY declared root is a view of an out-of-root source.
# This is the .github#1685 shape, and the reason this suite has a group 5c.
# ---------------------------------------------------------------------------------------------
make_allview() {
  local dir="$1" n="${2:-3}" i
  mkdir -p "$dir/canonical/skills"
  for i in $(seq 1 "$n"); do skill_md "$dir/canonical/skills" "skill-$i"; done
  printf '.claude/skills\n.agents/skills\n' > "$dir/.agent-skill-roots"
  printf '/.claude/skills\n/.agents/skills\n' > "$dir/.gitignore"
  git init -q "$dir"
  git -C "$dir" add -A
  git -C "$dir" -c user.email=f@fixture -c user.name=fixture commit -qm "canonical source"
  bash "$SV" generate --source "$dir/canonical/skills" --tree "$dir" >/dev/null 2>&1
}

# =============================================================================================
# 1 — THE TWO LIVE LAYOUTS AGREE, and the harness is a pure function of the tree (AC1, AC2;
#     class 3 port of #1549 in 1d/1e).
# =============================================================================================
T_COPIED="$WORK/copied"; make_copied "$T_COPIED" 3
expect "1a copied tree (today's layout): the two checkers AGREE, both ok" 0 "AGREE; old=ok new=ok" \
  -- bash "$PARITY" --tree "$T_COPIED"

expect "1b copied tree: byte-identity is named as an OLD-ONLY fact that is still live here" 0 \
  "fact byte-identity: OLD-ONLY, still live on this COPIED tree" \
  -- bash "$PARITY" --tree "$T_COPIED"

T_RES="$WORK/resolved"; make_resolved "$T_RES" 3
expect "1c end-state resolved layout (one real canonical root + one generated view): AGREE, both ok, with the old gate UNCHANGED" 0 "AGREE; old=ok new=ok" \
  -- bash "$PARITY" --tree "$T_RES"

expect "1d resolved layout: byte-identity is STRUCTURALLY IMPOSSIBLE, and that is CHECKED (same realpath), not assumed" 0 \
  "STRUCTURALLY IMPOSSIBLE" \
  -- bash "$PARITY" --tree "$T_RES"

# #1549's class, ported: the guard that fails is the one whose answer depends on something other than
# its subject. This harness's verdict must be f(tree, roots) and nothing else.
leg
first="$(bash "$PARITY" --tree "$T_COPIED" 2>&1 | tail -1)"
second="$(bash "$PARITY" --tree "$T_COPIED" 2>&1 | tail -1)"
if [ "$first" = "$second" ] && [ -n "$first" ]; then
  ok "1e PORT (class 3, #1549 — verify against an immutable subject, never a moving tip): two runs over an unchanged tree give the identical verdict"
else
  bad "1e the verdict is not a pure function of the tree" "$first
$second"
fi

# Comments are stripped FIRST and the grep runs over what is left. Grepping the file whole would
# match this tool's own header — which says the words `gh`, `curl` and `wget` in order to say it does
# not use them — and a check that its own documentation can trip is a check nobody keeps.
leg
if sed 's/#.*$//' "$PARITY" | grep -qE '(^|[^-[:alnum:]_./])(gh|curl|wget|nc)[[:space:]]'; then
  bad "1f the parity harness invokes a network tool — ADR-0067 §2 forbids a verdict that depends on a remote tip" \
    "$(sed 's/#.*$//' "$PARITY" | grep -nE '(^|[^-[:alnum:]_./])(gh|curl|wget|nc)[[:space:]]' | head -5)"
else
  ok "1f PORT (class 3, #1549 / ADR-0067 §2): the harness contains no network call — no gh, curl, wget or nc"
fi

leg
status="$(git -C "$T_COPIED" status --porcelain 2>&1)"
if [ -z "$status" ]; then
  ok "1g the harness writes NOTHING into the tree it examines (git status --porcelain empty after two runs)"
else
  bad "1g the harness dirtied the tree under test" "$status"
fi

# =============================================================================================
# 2 — PORT (class 2, .github#1506): PRESENCE AND BYTE-IDENTITY ARE INDEPENDENT FACTS.
#
#     #1506's substance: `[partitioned]` is not `[divergent]`, the shell short-circuited one past the
#     other, and a byte-identity count without a denominator was read as a coherence claim. The
#     rewrite does not retire that — it retires a CALLER of the digest, not the digest — so the fact
#     is ported here in the only form that is honest about the new checker: the view check answers
#     presence and DOES NOT ANSWER bytes, and this harness says so out loud instead of letting a
#     green exit code imply otherwise.
# =============================================================================================
T_PART="$WORK/partitioned"; make_copied "$T_PART" 3
rm -rf "$T_PART/.agents/skills/skill-2"
expect "2a PORT (class 2, #1506 — presence): a partitioned id is named by BOTH checkers, so they AGREE while both red" 0 \
  "AGREE; old=violation new=violation" \
  -- bash "$PARITY" --tree "$T_PART"

expect "2b PORT (class 2, #1506 — the id SET, not the exit code): the agreement is over the same 1 id" 0 \
  "both name the same 1 id(s) as not visible in every root" \
  -- bash "$PARITY" --tree "$T_PART"

T_DIV="$WORK/divergent"; make_copied "$T_DIV" 3
skill_md "$T_DIV/.agents/skills" "skill-2" " DRIFTED"
expect "2c PORT (class 2, #1506 — bytes): an id present everywhere whose COPIES DIFFER is a DISAGREEMENT, loudly, because the view check cannot see divergence at all" 1 \
  "BYTE-IDENTITY DISAGREEMENT" \
  -- bash "$PARITY" --tree "$T_DIV"

expect "2d PORT (class 2, #1506 — no short-circuit): the same run still reports the presence fact as AGREEing, rather than stopping at the first finding" 1 \
  "fact presence: AGREE" \
  -- bash "$PARITY" --tree "$T_DIV"

T_BOTH="$WORK/partitioned-and-divergent"; make_copied "$T_BOTH" 3
rm -rf "$T_BOTH/.agents/skills/skill-3"
skill_md "$T_BOTH/.agents/skills" "skill-2" " DRIFTED"
expect "2e PORT (class 2, #1513 — two facts true at once, both reported): partitioned AND divergent yields the byte disagreement" 1 \
  "BYTE-IDENTITY DISAGREEMENT" \
  -- bash "$PARITY" --tree "$T_BOTH"

expect "2f PORT (class 2, #1513): …and the presence fact is still compared as a set on the same run" 1 \
  "both name the same 1 id(s) as not visible in every root" \
  -- bash "$PARITY" --tree "$T_BOTH"

# =============================================================================================
# 3 — PORT (class 2, .github#1513): PER-ROOT, NEVER PER-REPRESENTATIVE — AND EVERY COUNT CARRIES
#     ITS POPULATION.
#
#     #1513's fail-open was digesting ONE root's copy as representative of the id: a tree whose clean
#     root is the representative reports nothing at all. The generated-view check is per-root by
#     construction, and that is asserted here rather than trusted, on the tree where a representative
#     check would be green.
# =============================================================================================
T_ONEROOT="$WORK/one-root-short"; make_copied "$T_ONEROOT" 2
rm -rf "$T_ONEROOT/.claude/skills/skill-1"
expect "3a PORT (class 2, #1513 — per-root): a root that is short is named even though the OTHER root is whole" 0 \
  "AGREE; old=violation new=violation" \
  -- bash "$PARITY" --tree "$T_ONEROOT"

expect "3b PORT (class 2, #1513 — the denominator): the view check prints visible/declared PER ROOT, so a count cannot be read as coverage it never had" 1 \
  ".claude/skills: 1/2 declared skill(s) visible" \
  -- bash "$SV" check --source "$T_ONEROOT/.agents/skills" --tree "$T_ONEROOT"

expect "3c PORT (class 2, #1513): the whole root is reported with ITS denominator on the same run" 1 \
  ".agents/skills: 2/2 declared skill(s) visible" \
  -- bash "$SV" check --source "$T_ONEROOT/.agents/skills" --tree "$T_ONEROOT"

# =============================================================================================
# 4 — PORT OF #1504'S LESSON (class 1's SUBJECT dissolves; its lesson does not).
#
#     #1504: coherent ≠ complete. A byte-identical SUBSET passes every drift check and is still
#     wrong. Under one source there is no subset to be coherent about, so the class-1 fixture itself
#     has no counterpart here — but the trap is still REACHABLE, through `--manifest`, and
#     `scripts/skill-view`'s own header warns of it. 4a proves the trap is real rather than
#     theoretical; 4b proves this harness does not walk into it, because its population is the UNION
#     of the tree's own roots and never a manifest.
# =============================================================================================
T_SUB="$WORK/subset-manifest"; make_copied "$T_SUB" 4
rm -rf "$T_SUB/.agents/skills/skill-3" "$T_SUB/.agents/skills/skill-4"
cat > "$WORK/subset.json" <<'JSON'
{ "skills": [ { "id": "skill-1" }, { "id": "skill-2" } ] }
JSON
expect "4a #1504 REPRODUCED, deliberately: a SUBSET manifest is GREEN over a tree that has lost half its skills" 0 \
  "2 declared skill(s) visible in every one of 2 root(s)" \
  -- bash "$SV" check --manifest "$WORK/subset.json" --tree "$T_SUB"

expect "4b PORT of the LESSON: the parity harness takes the UNION as its population, so the same tree is RED" 0 \
  "AGREE; old=violation new=violation" \
  -- bash "$PARITY" --tree "$T_SUB"

expect "4c …and it says which population it held both checkers to, with its size" 0 \
  "4 id(s) in the union (the SAME set both checkers are held to)" \
  -- bash "$PARITY" --tree "$T_SUB"

# =============================================================================================
# 5 — FAIL CLOSED, AND LOUDLY (ADR-0067 §8, epic #266). "I could not compare them" and "they agree"
#     are opposite facts, and only one of them is safe to act on.
# =============================================================================================
T_EMPTY="$WORK/empty"; mkdir -p "$T_EMPTY/.claude/skills" "$T_EMPTY/.agents/skills"
printf '.claude/skills\n.agents/skills\n' > "$T_EMPTY/.agent-skill-roots"
expect "5a an EMPTY union is exit 2, never 'they agree about nothing'" 2 "is EMPTY" \
  -- bash "$PARITY" --tree "$T_EMPTY"

T_ABS="$WORK/absent-root"; make_copied "$T_ABS" 2
rm -rf "$T_ABS/.claude/skills"
expect "5b an ABSENT root is NOT COMPARABLE (the old gate declines to render a verdict), never a pass" 2 \
  "NOT COMPARABLE" \
  -- bash "$PARITY" --tree "$T_ABS"

T_ALLVIEW="$WORK/all-view"; make_allview "$T_ALLVIEW" 3
expect "5c KNOWN LIMITATION (.github#1685) — an ALL-VIEW layout is exit 2, and the message names the issue that owns the fix" 2 \
  ".github#1685" \
  -- bash "$PARITY" --tree "$T_ALLVIEW"

# Pinned in BOTH directions, the house form (tests/skill-union/skillmirror.fixtures.json's
# KNOWN-DIVERGENCE-* vectors). The day `union_ids()` gains its `-L`, THIS leg goes red and points at
# the record to amend — rather than the divergence quietly closing and nobody learning that it did.
expect "5d …and the cause is still the old gate's own empty union, not something this harness invented" 2 \
  "no skills found under any root" \
  -- bash "$UNION" --product "$T_ALLVIEW" --roots ".claude/skills .agents/skills"

expect "5e the all-view layout is CORRECT — the view checker is green over the very tree the old gate cannot enumerate" 0 \
  "3 declared skill(s) visible in every one of 2 root(s)" \
  -- bash "$SV" check --source "$T_ALLVIEW/canonical/skills" --tree "$T_ALLVIEW"

# =============================================================================================
# 6 — PORT (class 2, .github#1547 / #1589): THE CANONICAL DIGEST, AND THE PROOF THAT THE NEW
#     APPARATUS DOES NOT SUBSUME IT.
#
#     ADR-0067: the two-implementations-disagreeing class "survives the rewrite untouched and is
#     retired by §3 instead". That is an argument for KEEPING tests/skill-union/skillmirror.fixtures.json,
#     and an argument is not a test. 6a and 6b pin the two measured digest facts; 6c makes the
#     argument executable, by showing the generated-view check is SILENT on a divergence the old
#     apparatus reports — so retiring the old apparatus without §3 would drop the fact, not move it.
#
#     6b IS PINNED TRUTH, NOT AN ENDORSEMENT. Phase 1 (.github#1621 §5.5) recorded that it could find
#     NO executable fixture in this repo for the invalid-UTF-8 class, and named it unverified. This is
#     that fixture. It asserts what this repo's shell MEASURABLY does — hash the raw bytes, exit 0 —
#     which is also what phase 1 found its producers do. ADR-0014's "REFUSE, do not rehash" decision
#     is about the LIBRARY's byte-level entry point; the day this shell is aligned to refuse, this leg
#     reds and points here, which is the whole purpose of pinning it in both directions.
# =============================================================================================
D_LF="$WORK/digest-lf"; D_CRLF="$WORK/digest-crlf"
mkdir -p "$D_LF" "$D_CRLF"
printf -- '---\nname: d\n---\n\nBody line.\n'                 > "$D_LF/SKILL.md"
printf -- '---\r\nname: d\r\n---\r\n\r\nBody line.\r\n'       > "$D_CRLF/SKILL.md"
leg
lf="$(bash "$UNION" --digest "$D_LF" 2>&1)"
crlf="$(bash "$UNION" --digest "$D_CRLF" 2>&1)"
if [ -n "$lf" ] && [ "$lf" = "$crlf" ]; then
  ok "6a PORT (class 2, #1547): a CRLF SKILL.md and its LF twin have the SAME canonical digest — the library's fold, followed by the shell"
else
  bad "6a the CRLF/LF canonical digests parted company" "lf=$lf
crlf=$crlf"
fi

D_BAD="$WORK/digest-invalid-utf8"; mkdir -p "$D_BAD"
printf -- '---\nname: d\n---\n\nInvalid: \xff\xfe here\n' > "$D_BAD/SKILL.md"
leg
badrc=0
baddigest="$(bash "$UNION" --digest "$D_BAD" 2>&1)" || badrc=$?
rawdigest="$(python3 -c '
import hashlib, sys
sys.stdout.write(hashlib.sha256(open(sys.argv[1], "rb").read()).hexdigest())
' "$D_BAD/SKILL.md")"
if [ "$badrc" -eq 0 ] && [ "$baddigest" = "$rawdigest" ]; then
  ok "6b PORT (class 2, #1589 — the fixture phase 1 could not find): invalid UTF-8 is hashed as RAW BYTES, exit 0, digest == sha256(file). Pinned, not endorsed."
else
  bad "6b the shell's invalid-UTF-8 behaviour moved" "rc=$badrc digest=$baddigest raw=$rawdigest"
fi

T_CRLF="$WORK/crlf-across-roots"; make_copied "$T_CRLF" 2
printf -- '---\r\nname: skill-1\r\ndescription: fixture skill skill-1.\r\n---\r\n\r\nBody of skill-1.\r\n' \
  > "$T_CRLF/.agents/skills/skill-1/SKILL.md"
expect "6c PORT (class 2 — the argument, made executable): a CRLF/LF pair across roots is [divergent] to the OLD gate…" 1 "[divergent]" \
  -- bash "$UNION" --product "$T_CRLF" --roots ".claude/skills .agents/skills"

expect "6d …and the generated-view check is SILENT over the identical tree, which is why §3 — not phase 3 or phase 4 — is what retires that fixture set" 0 \
  "2 declared skill(s) visible in every one of 2 root(s)" \
  -- bash "$SV" check --source "$T_CRLF/.agents/skills" --tree "$T_CRLF"

expect "6e …so the harness calls it a DISAGREEMENT rather than letting the quieter checker win" 1 \
  "BYTE-IDENTITY DISAGREEMENT" \
  -- bash "$PARITY" --tree "$T_CRLF"

# =============================================================================================
# 7 — THE OLD GATE IS RUN, NOT REIMPLEMENTED (ADR-0067 §9's "keeps running unchanged").
# =============================================================================================
leg
if grep -q 'bash "$UNION_GATE" --product' "$PARITY"; then
  ok "7a the harness INVOKES scripts/skill-union-assert.sh as a subprocess — it holds no second copy of the invariant (#1611 rule 2)"
else
  bad "7a the harness no longer runs the existing gate as a subprocess" "$(grep -n 'UNION_GATE' "$PARITY" | head -5)"
fi

# NOT "is the gate unmodified in this working tree?" — that leg would go red in .github#1685's own
# branch, which is AUTHORISED to change that file, and a gate that accuses the one worker allowed to
# do the thing is a gate that gets disabled. The assertion that belongs to THIS phase is narrower and
# self-contained: running the comparison must not change the gate it runs.
leg
before="$(sha256sum "$UNION" | cut -d' ' -f1)"
bash "$PARITY" --tree "$T_COPIED" >/dev/null 2>&1 || true
after="$(sha256sum "$UNION" | cut -d' ' -f1)"
if [ "$before" = "$after" ]; then
  ok "7b running the comparison does not modify scripts/skill-union-assert.sh — the old gate runs unchanged (ADR-0067 §9), sha256 identical before and after"
else
  bad "7b the parity run mutated the gate it is supposed to be running unchanged" "$before -> $after"
fi

# =============================================================================================
# 8 — THE TWO APPARATUSES DO NOT AGREE ON WHAT A SKILL *IS*, AND THAT IS A FINDING, NOT A ROUNDING.
#     The old gate's union is every immediate SUBDIRECTORY of a root; the view checker's unit is
#     `<id>/SKILL.md`. A directory with no SKILL.md is therefore a skill to one and not to the other.
#     It occurs on no rostered repo today (measured on .github#1674, 8/8 clean), so it is pinned here
#     rather than filed — and under the resolved layout it cannot occur at all, because
#     `skill-view generate` never emits a directory it did not recognise as a skill.
# =============================================================================================
T_NOMD="$WORK/dir-without-skill-md"; make_copied "$T_NOMD" 2
mkdir -p "$T_NOMD/.claude/skills/not-a-skill" "$T_NOMD/.agents/skills/not-a-skill"
printf 'notes\n' > "$T_NOMD/.claude/skills/not-a-skill/README.md"
printf 'notes\n' > "$T_NOMD/.agents/skills/not-a-skill/README.md"
expect "8a a root subdirectory with NO SKILL.md is a skill to the old gate and not to the view check — reported as a PRESENCE DISAGREEMENT, never averaged away" 1 \
  "PRESENCE DISAGREEMENT" \
  -- bash "$PARITY" --tree "$T_NOMD"

expect "8b …and the old gate is green over that same tree, which is why the disagreement is the finding" 0 \
  "OK — all roots hold the byte-identical union" \
  -- bash "$UNION" --product "$T_NOMD" --roots ".claude/skills .agents/skills"

# =============================================================================================
# Summary — and the leg count, so a suite that ran four of these cannot print "0 failed".
# =============================================================================================
EXPECTED_LEGS=33
printf '\nskill-view-parity fixture: %d passed, %d failed, %d leg(s) run\n' "$pass" "$failcount" "$legs"

if [ "$legs" -ne "$EXPECTED_LEGS" ]; then
  printf '::error::skill-view-parity fixture ran %d leg(s), expected %d — a suite that stops early prints a clean summary over the legs it never reached (epic #266).\n' \
    "$legs" "$EXPECTED_LEGS" >&2
  exit 2
fi

[ "$failcount" -eq 0 ] || exit 1
exit 0
