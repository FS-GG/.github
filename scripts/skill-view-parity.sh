#!/usr/bin/env bash
# shellcheck source-path=SCRIPTDIR
# skill-view-parity — run the GENERATED VIEW's check ALONGSIDE the existing copied-tree gate, over
# ONE tree, and say whether they AGREE.
#
# ADR-0067 §9, phase 3 (.github#1674). §9's order, verbatim: *"build the view and the absence check;
# run it alongside the existing gates unchanged; PORT the fixtures rather than re-derive them; retire
# the old apparatus per repo, with the freshness sweep last."* Phase 2 (.github#1635) built
# `scripts/skill-view`. This is the "alongside" step, and its success condition is AGREEMENT, NOT
# REPLACEMENT.
#
# IT RETIRES NOTHING, AND IT CANNOT (ADR-0067 §9). It runs `scripts/skill-union-assert.sh` as a
# SUBPROCESS, unmodified, with the arguments a caller would use — no edit, no flag, no fork. Every
# gate ADR-0067 names for eventual replacement keeps running beside it: *"This record is not authority
# to delete any of them."* Retirement is phase 4 (.github#1676).
#
# WHAT "AGREEMENT" MEANS HERE, EXACTLY. The two checkers do not answer the same question, and
# pretending they do is how a phase-4 retirement drops a fact nobody noticed was load-bearing. So the
# comparison is per FACT, not per exit code:
#
#   presence  — is every id in the union visible at EVERY configured root? BOTH answer this. It is
#               compared as a SET OF IDS, not as a pair of exit codes: two reds for different ids are
#               not an agreement, and this harness refuses to call them one.
#   byte-identity — only the OLD gate answers it. Under the resolved layout the roots are views of ONE
#               source, so divergence is structurally impossible and losing the check costs nothing.
#               That claim is CHECKED (every root's realpath must be the same object), never assumed —
#               and on a copied tree, where it is false, an old-gate byte finding the new check cannot
#               see is reported as a DISAGREEMENT rather than absorbed.
#   readability — only the NEW check answers it ([unreadable-skill], [empty-skill], the three silent
#               root failure modes of ADR-0067 §8). Additive: a fact gained is never a weakening.
#
# THE POPULATION IS THE UNION, NEVER A MANIFEST (.github#1504). The expected set handed to
# `skill-view check` is the union of the tree's own roots — the SAME population the old gate takes its
# verdict over — because that is the only way two verdicts are comparable at all. It is deliberately
# NOT `registry/coordination-kit-skill-manifest.json` (4 of this repo's 13 skills) or
# `registry/driver-skill-manifest.json` (6): either would be green over a tree that had lost the other
# nine. That is #1504's coherent-but-incomplete class exactly, and re-introducing it inside the tool
# that is supposed to be checking for it would be the joke writing itself.
#
# THE UNION IS ENUMERATED WITH `find -L`, AND THE OLD GATE'S IS NOT (.github#1685). `union_ids()` at
# scripts/skill-union-assert.sh:456 runs `find` with no `-L`, and POSIX `find` does not dereference a
# starting point that IS a symlink — so a root that is a generated VIEW contributes ZERO ids there. It
# fails CLOSED (exit 2, "no skills found under any root"), which is why #1685 is filed rather than
# worked around: ADR-0067 §9 forbids THIS phase from touching that gate. This harness therefore
# enumerates BOTH ways, names the divergence when the two populations differ, and refuses to render a
# comparison over a side that reached no verdict. An all-view layout is exit 2 here, pointing at
# #1685 — not a quiet green.
#
# NO NETWORK, NO REMOTE TIP, NO `gh` (ADR-0067 §2). The verdict is a pure function of (tree under
# test, root set). A gate that reads another repository's `main` at check time has no verdict, because
# its answer changes with no change to its subject — the rule §2 binds NOW, independently of the
# rewrite, and this tool is held to it like any other. It also writes nothing into the tree it
# examines: the generated manifest lives in a temp dir that is removed on exit.
#
# THE ROSTER-WIDE COMPARISON IS A MEASUREMENT, NOT A JOB — and this is its recipe, so that the
# numbers on .github#1674 are re-derivable rather than trusted. Per repo, against a RECORDED pin:
#
#   git clone --depth 1 https://github.com/FS-GG/<repo>.git /tmp/<repo>
#   git -C /tmp/<repo> rev-parse HEAD                      # the pin the verdict belongs to
#   scripts/skill-view-parity.sh --tree /tmp/<repo> --label <repo> --roots ".claude/skills .agents/skills"
#
# and, for the layout phase 4 will retire INTO (the resolved equivalent of that same tree):
#
#   cp -R /tmp/<repo>/.agents/skills /tmp/<repo>-resolved/.agents/skills
#   printf '.claude/skills\n.agents/skills\n' > /tmp/<repo>-resolved/.agent-skill-roots
#   printf '/.claude/skills\n' > /tmp/<repo>-resolved/.gitignore && git init -q … && git commit …
#   scripts/skill-view generate --source /tmp/<repo>-resolved/.agents/skills --tree /tmp/<repo>-resolved
#   scripts/skill-view-parity.sh --tree /tmp/<repo>-resolved --label "<repo> (resolved)"
#
# RECEIVER PIN STATE IS NOT A DISAGREEMENT (.github#1674's own note). Kit staleness is parked behind
# this rewrite, so a receiver sitting on FS.GG.Kit 0.6.0 with a perfectly coherent tree is green here
# and green correctly — this harness reads the TREE, never the pin. "Is my pin current?" has a
# different owner (`scripts/repos-audit.sh`'s freshness sweep) and ADR-0067 §9 retires that LAST.
#
# Usage:
#   skill-view-parity.sh --tree <dir> [--roots "<r1> <r2> ..."] [--label <name>]
#
# Exit: 0 = the two checkers AGREE on this tree (every shared fact, and no old-only fact left behind)
#       1 = a DISAGREEMENT — the finding, printed with the ids or the fact that differ
#       2 = NOT COMPARABLE: a side reached no verdict, the population is empty, or a tool is missing.
#           Never a pass. "I could not compare them" and "they agree" are opposite facts (epic #266).

set -euo pipefail

TREE="."
ROOTS=""
ROOTS_ARG=""
ROOTS_SRC=""
LABEL=""

DEFAULT_ROOTS=".claude/skills .agents/skills"
DEFAULT_LABEL="default (ADR-0065's two)"

die() { echo "::error::skill-view-parity: $*" >&2; exit 2; }

# shellcheck source=lib/args.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib/args.sh"
# shellcheck source=lib/roots.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib/roots.sh"

usage() {
  cat <<'EOF'
skill-view-parity — run the generated view's absence check ALONGSIDE the copied-tree gate and
compare their verdicts on ONE tree. Retires nothing (ADR-0067 §9); phase 3, .github#1674.

  skill-view-parity.sh --tree <dir> [--roots "<r1> <r2>"] [--label <name>]

  --tree <dir>    the repository root to compare over (default: .).
  --roots "<...>" override the root set. Precedence: --roots > $AGENT_SKILL_ROOTS >
                  <tree>/.agent-skill-roots > ADR-0065's two.
  --label <name>  a name for this tree in the report line (default: the tree's basename).

Exit: 0 agree; 1 disagree; 2 not comparable (never a pass).
EOF
}

while [ $# -gt 0 ]; do
  case "$1" in
    --tree)  need_val "$@"; TREE="$2"; shift 2 ;;
    --roots) need_val "$@"; ROOTS_ARG="$2"; shift 2 ;;
    --label) need_val "$@"; LABEL="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) usage >&2; die "unknown argument: $1" ;;
  esac
done

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
UNION_GATE="$HERE/skill-union-assert.sh"
VIEW_CHECK="$HERE/skill-view"
[ -f "$UNION_GATE" ] || die "the existing gate is missing: $UNION_GATE (this harness RUNS it; it does not reimplement it)."
[ -f "$VIEW_CHECK" ] || die "the view checker is missing: $VIEW_CHECK (phase 2, .github#1635)."
command -v python3 >/dev/null 2>&1 || die "python3 not found (required to write the union manifest)."

[ -d "$TREE" ] || die "--tree is not a directory: $TREE"
TREE_ABS="$(cd "$TREE" && pwd)"
[ -n "$LABEL" ] || LABEL="$(basename "$TREE_ABS")"

resolve_roots "$TREE_ABS" "$DEFAULT_ROOTS" "$DEFAULT_LABEL" "$ROOTS_ARG"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/skill-view-parity.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

# ---------------------------------------------------------------------------------------------
# The two populations. They are enumerated SEPARATELY on purpose — see the `find -L` note above.
# ---------------------------------------------------------------------------------------------
ids_deref() {   # what a runtime would see: symlinked roots dereferenced
  local r
  for r in $ROOTS; do
    find -L "$TREE_ABS/$r" -mindepth 1 -maxdepth 1 -type d -exec basename {} \; 2>/dev/null
  done | LC_ALL=C sort -u
}
ids_nodere() {  # what scripts/skill-union-assert.sh's union_ids() sees today (.github#1685)
  local r
  for r in $ROOTS; do
    find "$TREE_ABS/$r" -mindepth 1 -maxdepth 1 -type d -exec basename {} \; 2>/dev/null
  done | LC_ALL=C sort -u
}

UNION_DEREF="$(ids_deref || true)"
UNION_PLAIN="$(ids_nodere || true)"

pin="untracked"
if git -C "$TREE_ABS" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  pin="$(git -C "$TREE_ABS" rev-parse HEAD 2>/dev/null || echo unborn)"
fi

echo "skill-view-parity: tree='$LABEL' pin=$pin roots='$ROOTS' (from $ROOTS_SRC)"

# THE LAYOUT, CLASSIFIED BEFORE ANYTHING IS RUN — because it is what decides whether an old-only fact
# is structurally impossible or genuinely lost.
real_first=""
resolved_layout=1
view_roots=0
for r in $ROOTS; do
  p="$TREE_ABS/$r"
  kind="absent"
  real=""
  if [ -L "$p" ]; then kind="view(symlink)"; view_roots=$((view_roots + 1)); fi
  if [ -d "$p" ]; then
    [ "$kind" = "absent" ] && kind="directory"
    real="$(cd "$p" && pwd -P)"
    if [ -z "$real_first" ]; then real_first="$real"
    elif [ "$real" != "$real_first" ]; then resolved_layout=0; fi
  else
    resolved_layout=0
  fi
  echo "  root $r: $kind${real:+ -> $real}"
done
[ -n "$real_first" ] || resolved_layout=0

if [ "$UNION_DEREF" != "$UNION_PLAIN" ]; then
  echo "::warning::skill-view-parity: the two enumerations DISAGREE about the population — following symlinks yields $(printf '%s' "$UNION_DEREF" | grep -c . || true) id(s), the old gate's own \`find\` (no -L) yields $(printf '%s' "$UNION_PLAIN" | grep -c . || true). That is .github#1685: a root that IS a view contributes zero ids there. Fails closed, and is why the comparison below may be NOT COMPARABLE rather than green." >&2
fi

[ -n "$UNION_DEREF" ] || die "the union under '$ROOTS' is EMPTY — there is nothing to compare two verdicts over, and 'they agree about nothing' is not a pass (epic #266)."

# The union manifest, written where the tree cannot see it. json.dump, not printf: an id with a quote
# in it would otherwise produce a manifest that reads as a DIFFERENT set.
MANIFEST="$WORK/union-manifest.json"
printf '%s\n' "$UNION_DEREF" | python3 -c '
import json, sys
ids = [line.strip() for line in sys.stdin if line.strip()]
json.dump({"skills": [{"id": i} for i in ids]}, open(sys.argv[1], "w", encoding="utf-8"), indent=2)
' "$MANIFEST"

union_ct="$(printf '%s\n' "$UNION_DEREF" | grep -c . || true)"
echo "  population: $union_ct id(s) in the union (the SAME set both checkers are held to)"

# ---------------------------------------------------------------------------------------------
# Run both. Unmodified, as subprocesses, with their own arguments.
# ---------------------------------------------------------------------------------------------
OLD_OUT="$WORK/old.txt"
NEW_OUT="$WORK/new.txt"

old_rc=0
bash "$UNION_GATE" --product "$TREE_ABS" --roots "$ROOTS" >"$OLD_OUT" 2>&1 || old_rc=$?
new_rc=0
bash "$VIEW_CHECK" check --manifest "$MANIFEST" --tree "$TREE_ABS" --roots "$ROOTS" >"$NEW_OUT" 2>&1 || new_rc=$?

verdict_of() {
  case "$1" in
    0) echo "ok" ;;
    1) echo "violation" ;;
    2) echo "no-verdict" ;;
    *) echo "no-verdict" ;;
  esac
}
old_v="$(verdict_of "$old_rc")"
new_v="$(verdict_of "$new_rc")"

echo "  copied-tree gate (scripts/skill-union-assert.sh, UNCHANGED): exit $old_rc -> $old_v"
echo "  generated-view check (scripts/skill-view check):             exit $new_rc -> $new_v"

# ---------------------------------------------------------------------------------------------
# Fact 1 — PRESENCE, compared as a set of ids.
# ---------------------------------------------------------------------------------------------
OLD_ABSENT="$WORK/old-absent.txt"
NEW_ABSENT="$WORK/new-absent.txt"

{ sed -n "s/.*\[partitioned\] skill '\([^']*\)'.*/\1/p" "$OLD_OUT" | LC_ALL=C sort -u > "$OLD_ABSENT"; } || true
[ -f "$OLD_ABSENT" ] || : > "$OLD_ABSENT"
# Every class the view checker uses for "this id is not visible HERE". They are separate diagnostics
# on purpose (a runtime cannot use an unreadable skill any more than an absent one), and they all
# collapse to the same presence fact.
# `|| true` on the whole pipeline, not on grep: under `pipefail` a grep that matches NOTHING — the
# green case — is a pipeline failure, and `set -e` would turn the healthiest tree in the org into an
# exit before a single fact was compared.
{ grep -oE '\[(missing-skill|dangling-skill|not-a-file-skill|unreadable-skill|empty-skill)\] [^ ]+/SKILL\.md' "$NEW_OUT" 2>/dev/null \
  | awk -F/ '{print $(NF-1)}' | LC_ALL=C sort -u > "$NEW_ABSENT"; } || true
[ -f "$NEW_ABSENT" ] || : > "$NEW_ABSENT"

# Root-level failures have no per-id set on either side: the old gate DIES (exit 2) on an absent root
# and the new one reports one root-level class instead of N derived ones. Comparing id sets across
# that is comparing nothing.
root_level="$( { grep -oE '\[(absent-root|dangling-root|text-file-root|not-a-directory-root)\]' "$NEW_OUT" 2>/dev/null | LC_ALL=C sort -u | tr '\n' ' '; } || true)"

disagreements=0
notcomparable=0

if [ "$old_v" = "no-verdict" ] || [ "$new_v" = "no-verdict" ]; then
  notcomparable=1
  echo "::error::skill-view-parity: NOT COMPARABLE — a side reached no verdict (old=$old_v new=$new_v). Two checkers cannot agree when one of them declined to answer." >&2
  sed -n 's/^/    old | /p' "$OLD_OUT" | grep -E 'error|no skills|skill-union-assert:' | head -6 >&2 || true
  sed -n 's/^/    new | /p' "$NEW_OUT" | grep -E 'error|skill-view' | head -6 >&2 || true
  if [ "$view_roots" -gt 0 ] && [ "$old_rc" -eq 2 ] && grep -q 'no skills found under any root' "$OLD_OUT"; then
    echo "::error::skill-view-parity: this is .github#1685 — $view_roots of the configured root(s) IS a generated view, and the old gate enumerates with \`find\` and no \`-L\`, so it saw an empty union over a tree that is correct. Fails CLOSED. ADR-0067 §9 forbids phase 3 from repairing that gate; #1685 owns the one-line fix and its vector." >&2
  fi
else
  if ! diff -q "$OLD_ABSENT" "$NEW_ABSENT" >/dev/null 2>&1; then
    disagreements=$((disagreements + 1))
    echo "::error::skill-view-parity: PRESENCE DISAGREEMENT — the two checkers name different ids as not-visible-everywhere." >&2
    diff -u "$OLD_ABSENT" "$NEW_ABSENT" | sed 's/^/    /' >&2 || true
  else
    n="$(grep -c . "$OLD_ABSENT" || true)"
    echo "  fact presence: AGREE — both name the same $n id(s) as not visible in every root."
  fi
  if [ -n "$root_level" ]; then
    disagreements=$((disagreements + 1))
    echo "::error::skill-view-parity: the view check reported ROOT-LEVEL failure(s) ($root_level) while the old gate rendered a verdict of '$old_v'. ADR-0067 §8's three silent modes are exactly here; do not read this as agreement." >&2
  fi
fi

# ---------------------------------------------------------------------------------------------
# Fact 2 — BYTE-IDENTITY, which only the OLD gate answers.
# ---------------------------------------------------------------------------------------------
differing="$(sed -n 's/.*byte-differing=\([0-9]*\).*/\1/p' "$OLD_OUT" | head -n 1)"
[ -n "$differing" ] || differing="?"
if [ "$notcomparable" -ne 0 ]; then
  # The old gate never reached its summary line, so there is no byte fact to read. Saying "could not
  # read byte-differing=" here would name a SECOND cause for one failure, and a diagnostic that names
  # the wrong cause sends the reader to the wrong repair.
  echo "  fact byte-identity: NOT ANSWERED — the old gate rendered no verdict on this tree, so it printed no summary to read it from."
elif [ "$resolved_layout" -eq 1 ]; then
  echo "  fact byte-identity: OLD-ONLY, and STRUCTURALLY IMPOSSIBLE to violate here — every configured root resolves to the same object ($real_first), so there is one copy and nothing to diverge. Checked, not assumed."
elif [ "$differing" = "0" ]; then
  echo "  fact byte-identity: OLD-ONLY, still live on this COPIED tree, and the old gate answers it: byte-differing=0. The new check does not look, so this fact does NOT survive a retirement of the old gate on this layout."
elif [ "$differing" = "?" ]; then
  notcomparable=1
  echo "::error::skill-view-parity: could not read byte-differing= from the old gate's summary — the byte-identity fact was neither answered nor ruled impossible." >&2
else
  disagreements=$((disagreements + 1))
  echo "::error::skill-view-parity: BYTE-IDENTITY DISAGREEMENT — the old gate found byte-differing=$differing on a tree whose roots are separate copies, and the generated-view check cannot see divergence at all. On this layout that fact is LOST, not subsumed." >&2
fi

# ---------------------------------------------------------------------------------------------
# Fact 3 — READABILITY: new-only, and additive.
# ---------------------------------------------------------------------------------------------
newonly="$( { grep -oE '\[(unreadable-skill|empty-skill|not-a-file-skill|dangling-skill)\]' "$NEW_OUT" 2>/dev/null | LC_ALL=C sort -u | tr '\n' ' '; } || true)"
if [ -n "$newonly" ]; then
  echo "  fact readability: NEW-ONLY, and it FIRED: $newonly — a fact the copied-tree gate never asked. Additive; never a reason to call the pair disagreeing."
else
  echo "  fact readability: NEW-ONLY, quiet on this tree (ADR-0067 §8's classes)."
fi

if [ "$notcomparable" -ne 0 ]; then
  echo "skill-view-parity: $LABEL — NOT COMPARABLE (old=$old_v new=$new_v)."
  exit 2
fi
if [ "$disagreements" -ne 0 ]; then
  echo "skill-view-parity: $LABEL — DISAGREE ($disagreements fact(s)); old=$old_v new=$new_v."
  exit 1
fi
echo "skill-view-parity: $LABEL — AGREE; old=$old_v new=$new_v over $union_ct id(s) in $(printf '%s' "$ROOTS" | wc -w) root(s)."
