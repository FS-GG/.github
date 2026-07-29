#!/usr/bin/env bash
# Fixture for scripts/check-retirement-order-coherence.py — .github#1750, epic #266.
#
# THE FAILURE LEGS ARE THE POINT (#1750 AC 5). This gate exists because NOTHING noticed that the
# retirement order's standing verdict had gone stale — five times, once in the very row filed to fix the
# staleness. A suite that only ever watched it pass would be what was there before (nothing) with a
# green tick on top. So every leg asserts the EXIT CODE **and** a needle from the reason:
# `tests/feed-coherence/run.sh:10` names the trap — a "must fail" leg whose non-zero exit comes from a
# path guard rather than from the thing under test passes against a gate broken a completely different
# way.
#
# OFFLINE, AND THE SUBJECT IS SYNTHETIC ON BOTH SIDES.
#   * The tree is built here: a synthetic `registry/repos.yml`, a synthetic `scripts/skill-view`, and a
#     synthetic retirement order. NONE of them names a real FS-GG receiver. That is not tidiness — it is
#     AC 2's assertion made unfakeable: a gate carrying its own receiver list, or its own root set,
#     cannot pass a single green leg below, because the roster it would have to use is not the one these
#     legs describe.
#   * `gh` is STUBBED ON PATH and FAILS LIKE THE REAL ONE — a 404 is an answer, a 403 rate limit is not
#     — so the fail-closed legs exercise the real transport rather than a convenience shim. A
#     fail-closed claim proved against a mock of itself is not proved.
#   * The stub ASSERTS THE REQUEST. It refuses a repository it was not told to serve and refuses a tree
#     read that is not `?recursive=1`, so a gate that read a non-recursive tree — and therefore saw only
#     the top level, where a skill root is a TREE and not a blob, and would call every receiver retired
#     — fails here instead of quietly passing.
set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
GATE="$ROOT/scripts/check-retirement-order-coherence.py"
PY="${PYTHON:-python3}"
PYBIN="$(command -v "$PY")" || { echo "fixture: no interpreter at ${PY}" >&2; exit 1; }

WORK="$(mktemp -d "${TMPDIR:-/tmp}/retirement-order-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

export PYTHONDONTWRITEBYTECODE=1
# No backoff and one try: the transport knobs exist so the fixture runs at full speed against a stub.
export FSGG_RETIREMENT_TRIES=1 FSGG_RETIREMENT_RETRY_DELAY=0 FSGG_RETIREMENT_TIMEOUT=30

pass=0
failcount=0
ok() { printf 'PASS  %s\n' "$1"; pass=$((pass + 1)); }
bad() {
  printf 'FAIL  %s\n' "$1"
  [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'
  failcount=$((failcount + 1))
}

# ---------------------------------------------------------------------------------------------
# The `gh` stub. Serves a WORLD directory, one subdirectory per repository slug:
#   $WORLD/<owner>__<name>/branch      the default branch it reports
#   $WORLD/<owner>__<name>/tree        one blob path per line
#   $WORLD/<owner>__<name>/roots       served at contents/.agent-skill-roots (absent => 404)
#   $WORLD/<owner>__<name>/truncated   the tree read reports `"truncated": true`
#   $WORLD/<owner>__<name>/invisible   the repository endpoint 404s (we cannot see the repo at all)
#   $WORLD/ratelimit                   every call fails 403 rate-limited — an answer about nothing
# ---------------------------------------------------------------------------------------------
STUB="$WORK/stub"
mkdir -p "$STUB"
cat >"$STUB/gh" <<'STUB'
#!/usr/bin/env bash
set -uo pipefail

notfound() { echo "gh: Not Found (HTTP 404)" >&2; exit 1; }
apifail()  { echo "gh: API rate limit exceeded for installation (HTTP 403)" >&2; exit 1; }
refuse()   { echo "stub: $1" >&2; exit 9; }

[ "${1:-}" = "api" ] || refuse "expected \`gh api\`, got: $*"
shift

path=""
while [ $# -gt 0 ]; do
  case "$1" in
    # `shift 2` with ONE argument left returns 1 and shifts NOTHING under `set -uo pipefail`, so the
    # loop spins forever and the job dies on its timeout — a non-zero for the wrong reason, reported as
    # infrastructure rather than as a finding. Refuse the malformed call instead.
    -H|--jq) [ $# -ge 2 ] || refuse "dangling $1 with no value"; shift 2 ;;
    repos/*) path="$1"; shift ;;
    *) shift ;;
  esac
done
[ -n "$path" ] || refuse "no repos/ path in the request"
[ -e "$FIXTURE_WORLD/ratelimit" ] && apifail

rest="${path#repos/}"
owner="${rest%%/*}"; rest="${rest#*/}"
name="${rest%%/*}"; rest="${rest#"$name"}"
dir="$FIXTURE_WORLD/${owner}__${name}"
[ -d "$dir" ] || refuse "this stub was not told to serve repos/$owner/$name"

case "$rest" in
  "")
    [ -e "$dir/invisible" ] && notfound
    printf '{"full_name":"%s/%s","default_branch":"%s"}\n' "$owner" "$name" "$(cat "$dir/branch")"
    ;;
  /git/trees/*)
    [ -e "$dir/invisible" ] && notfound
    [ -e "$dir/no-commits" ] && notfound
    case "$rest" in *"recursive=1"*) ;; *) refuse "tree read is not ?recursive=1: $rest" ;; esac
    # THE BRANCH IS ASSERTED, not answered. Without this a gate that hardcoded `/git/trees/main` and
    # never read `default_branch` passes every leg in this file.
    case "$rest" in
      "/git/trees/$(cat "$dir/branch")?recursive=1") ;;
      *) refuse "tree read is not at this repo's default branch ($(cat "$dir/branch")): $rest" ;;
    esac
    truncated=false; [ -e "$dir/truncated" ] && truncated=true
    TREE_FILE="$dir/tree" TRUNCATED="$truncated" python3 -c '
import json, os
paths = [p for p in open(os.environ["TREE_FILE"]).read().split() if p]
print(json.dumps({"truncated": os.environ["TRUNCATED"] == "true",
                  "tree": [{"path": p, "type": "blob"} for p in paths]}))
'
    ;;
  /contents/.agent-skill-roots)
    [ -e "$dir/invisible" ] && notfound
    # Reachable: leg 24 puts `.agent-skill-roots` in the TREE without a `roots` file, which is the
    # blob-present/contents-404 disagreement the gate must refuse rather than crash on.
    [ -f "$dir/roots" ] || notfound
    cat "$dir/roots"
    ;;
  *) refuse "unexpected endpoint: $path" ;;
esac
STUB
chmod +x "$STUB/gh"
export PATH="$STUB:$PATH"

# An empty directory, for the legs that must run with no `gh` reachable.
NOGH="$WORK/nogh"
mkdir -p "$NOGH"

# ---------------------------------------------------------------------------------------------
# Tree builders. `mkworld` writes the receivers; `mktree` writes the repo under test.
# ---------------------------------------------------------------------------------------------

# mkrepo <world> <owner/name> <blob-path>...
mkrepo() {
  local world="$1" slug="$2"; shift 2
  local dir="$world/${slug%%/*}__${slug#*/}"
  mkdir -p "$dir"
  echo main >"$dir/branch"
  printf '%s\n' "$@" >"$dir/tree"
}

# mktree <dir> <verdict-block-body> <headline-count> [extra-markdown]
# Writes a synthetic repo root: roster (two receivers), skill-view root set, retirement order.
mktree() {
  local dir="$1" block="$2" headline="$3" extra="${4:-}"
  mkdir -p "$dir/registry" "$dir/scripts" "$dir/docs/coordination"
  cat >"$dir/registry/repos.yml" <<'YAML'
schemaVersion: 1
repos:
  - { id: hub,    full: Fixture-Org/Fixture.Hub,   role: authority, receives: [labels] }
  - { id: alpha,  full: Fixture-Org/Fixture.Alpha, role: framework, receives: [labels, coordination-kit] }
  - { id: beta,   full: Fixture-Org/Fixture.Beta,  role: framework, receives: [labels, coordination-kit] }
YAML
  printf 'DEFAULT_ROOTS=".src/skills .view/skills"\n' >"$dir/scripts/skill-view"
  {
    printf '# The fixture retirement order\n\n'
    printf '> Where the live state is: the count lives in the standing verdict and nowhere else.\n'
    [ -n "$extra" ] && printf '%s\n' "$extra"
    printf '\n---\n\n## 4. The verdict\n\n'
    printf '### The standing verdict: **%s retired.**\n\n' "$headline"
    printf '%s\n' "$block"
    printf '\n> A dated, append-only record: it was 1 of 2 yesterday, and 0 of 2 the day before.\n'
  } >"$dir/docs/coordination/skill-apparatus-retirement-order.md"
}

BLOCK_BOTH='<!-- fsgg:retirement-verdict
retired: alpha, beta
in-flight:
-->'
BLOCK_ONE='<!-- fsgg:retirement-verdict
retired: alpha
in-flight:
-->'

# expect <want-exit> <dir> <name> <needle>...  — runs the gate against <dir> with $FIXTURE_WORLD set.
expect() {
  local want="$1" dir="$2" name="$3"; shift 3
  local rc=0 out needle
  out="$($PY "$GATE" --root "$dir" 2>&1)" || rc=$?
  if [ "$rc" != "$want" ]; then
    bad "$name" "expected exit $want, got $rc
$out"
    return
  fi
  for needle in "$@"; do
    if ! printf '%s' "$out" | grep -qiF -- "$needle"; then
      bad "$name" "exit $rc was right, but the message never said: $needle
$out"
      return
    fi
  done
  ok "$name (exit $rc)"
}

# ---------------------------------------------------------------------------------------------
# Leg 1 — GREEN. Both receivers retired (one root each), document says so.
# ---------------------------------------------------------------------------------------------
WORLD="$WORK/w1"; export FIXTURE_WORLD="$WORLD"
mkrepo "$WORLD" Fixture-Org/Fixture.Alpha ".src/skills/a/SKILL.md"
mkrepo "$WORLD" Fixture-Org/Fixture.Beta  ".src/skills/b/SKILL.md"
mktree "$WORK/t1" "$BLOCK_BOTH" "2 of 2"
expect 0 "$WORK/t1" "green: both retired, document agrees" "2 of 2 retired" "2 receiver tree(s) agree"

# ---------------------------------------------------------------------------------------------
# Leg 2 — THE MUTATION AC 5 ASKS FOR. The trees are unchanged; the VERDICT is mutated to drop a
# receiver. This is the exact staleness the item was filed for: a stage landed, the document did not
# move. If this leg ever goes green, the gate has stopped being the thing it was built to be.
# ---------------------------------------------------------------------------------------------
mktree "$WORK/t2" "$BLOCK_ONE" "1 of 2"
expect 1 "$WORK/t2" "mutated verdict: a retired receiver is missing from the list" \
  "Fixture-Org/Fixture.Beta (beta) IS retired" "does not list it as retired"

# ---------------------------------------------------------------------------------------------
# Leg 3 — the other direction: the document claims a retirement that did not happen. A verdict that
# recorded an unobserved outcome is the defect #1723 existed to fix, and it must red too.
# ---------------------------------------------------------------------------------------------
WORLD="$WORK/w3"; export FIXTURE_WORLD="$WORLD"
mkrepo "$WORLD" Fixture-Org/Fixture.Alpha ".src/skills/a/SKILL.md"
mkrepo "$WORLD" Fixture-Org/Fixture.Beta  ".src/skills/b/SKILL.md" ".view/skills/b/SKILL.md"
mktree "$WORK/t3" "$BLOCK_BOTH" "2 of 2"
expect 1 "$WORK/t3" "claimed retirement that did not happen" \
  "(beta) is NOT retired" "still commits more than one skill root"

# ---------------------------------------------------------------------------------------------
# Leg 4 — the root set is READ, not hardcoded. Beta declares its own three-root set and commits two
# of them, so it is NOT retired even though `.view/skills` (the default second root) is empty. A gate
# that spelled the roots itself calls this retired and passes leg 3's shape while missing this one.
# ---------------------------------------------------------------------------------------------
WORLD="$WORK/w4"; export FIXTURE_WORLD="$WORLD"
mkrepo "$WORLD" Fixture-Org/Fixture.Alpha ".src/skills/a/SKILL.md"
mkrepo "$WORLD" Fixture-Org/Fixture.Beta ".agent-skill-roots" ".src/skills/b/SKILL.md" ".other/skills/b/SKILL.md"
printf '# a comment line\n.src/skills\n.other/skills\n' >"$WORLD/Fixture-Org__Fixture.Beta/roots"
mktree "$WORK/t4" "$BLOCK_BOTH" "2 of 2"
expect 1 "$WORK/t4" "per-receiver .agent-skill-roots is honoured" \
  "(beta) is NOT retired" ".other/skills"

# ---------------------------------------------------------------------------------------------
# Leg 5 — an in-flight receiver is NOT graded, and the green SAYS SO. Beta's tree contradicts the
# document; parking it in `in-flight:` suppresses the comparison, and the OK line still names it and
# its ref. A hole a green does not mention is the hole #266 is about.
# ---------------------------------------------------------------------------------------------
mktree "$WORK/t5" '<!-- fsgg:retirement-verdict
retired: alpha
in-flight: beta(#4242)
-->' "1 of 2"
expect 0 "$WORK/t5" "in-flight receiver is not graded, and the green names it" \
  "NOT GRADED (in flight)" "beta #4242" "1 receiver tree(s) agree"

# ---------------------------------------------------------------------------------------------
# Leg 6 — an in-flight entry with no ref is REFUSED. Parking a receiver out of the comparison is
# allowed; parking it anonymously is not, or `in-flight:` becomes this gate's mute button.
# ---------------------------------------------------------------------------------------------
mktree "$WORK/t6" '<!-- fsgg:retirement-verdict
retired: alpha
in-flight: beta
-->' "1 of 2"
expect 3 "$WORK/t6" "in-flight without a ref is refused" "not \`<receiver>(<ref>)\`" "anonymously"

# ---------------------------------------------------------------------------------------------
# Leg 7 — the headline and the block must agree. The block is an HTML comment and renders as
# nothing, so without this leg the sentence a human reads is ungraded.
# ---------------------------------------------------------------------------------------------
WORLD="$WORK/w1"; export FIXTURE_WORLD="$WORLD"
mktree "$WORK/t7" "$BLOCK_BOTH" "1 of 2"
expect 1 "$WORK/t7" "headline disagrees with the verdict block" "the heading says 1 of 2" "lists 2 retired"

# ---------------------------------------------------------------------------------------------
# Leg 8 — a heading with no count at all is a finding, not a pass. "The gate found nothing to
# compare" must never render as "the gate compared it and it was fine".
# ---------------------------------------------------------------------------------------------
mktree "$WORK/t8" "$BLOCK_BOTH" "the sequence is complete"
expect 1 "$WORK/t8" "headline states no count" "states no count out of 2"

# ---------------------------------------------------------------------------------------------
# Legs 9 and 10 — a SECOND LIVE COUNT, in each of the two surfaces the gate defines: the header
# block above the first `---`, and any heading. These are the two real restatements measured on the
# live document while this gate was written.
# ---------------------------------------------------------------------------------------------
mktree "$WORK/t9" "$BLOCK_BOTH" "2 of 2" "> The sequence is COMPLETE — 2 of 2."
expect 1 "$WORK/t9" "second count in the header block" "the header block states a second count"

mktree "$WORK/t10" "$BLOCK_BOTH" "2 of 2"
printf '\n## 9. What is still open after 2 of 2\n' >>"$WORK/t10/docs/coordination/skill-apparatus-retirement-order.md"
expect 1 "$WORK/t10" "second count in a heading" "a heading states a second count"

# A count with a DIFFERENT denominator is about something else, and must not be manufactured into a
# finding — the surest way to get a gate muted (#698).
mktree "$WORK/t11" "$BLOCK_BOTH" "2 of 2"
printf '\n## Unrelated: 0 of 7 receivers pass include-build-config\n' >>"$WORK/t11/docs/coordination/skill-apparatus-retirement-order.md"
expect 0 "$WORK/t11" "a count with another denominator is not this document's count" "2 of 2 retired"

# A `# …` line INSIDE a fenced block is a shell comment, not a heading. This document is full of
# fenced commands — the standing verdict's own measurement is one — and grading them as headings would
# eventually red on a legitimate append, which is how a gate stops being read (#698).
mktree "$WORK/t11b" "$BLOCK_BOTH" "2 of 2"
{
  printf '\n```\n# a measurement command, 1 of 2 receivers\ngh api repos/x/y\n```\n'
} >>"$WORK/t11b/docs/coordination/skill-apparatus-retirement-order.md"
expect 0 "$WORK/t11b" "a # comment inside a fence is not a heading" "2 of 2 retired"

# ---------------------------------------------------------------------------------------------
# Legs 12-14 — the anchor itself. Missing, doubled, or naming a repo the roster does not carry.
# Every one is a NO VERDICT (3): with no unambiguous block there is nothing to compare, and the
# most complete-looking pass would cover the least work.
# ---------------------------------------------------------------------------------------------
mktree "$WORK/t12" "(no verdict block here)" "2 of 2"
expect 3 "$WORK/t12" "no anchor is a no-verdict, not a pass" "carries no" "certifies nothing"

mktree "$WORK/t13" "$BLOCK_BOTH
$BLOCK_ONE" "2 of 2"
expect 3 "$WORK/t13" "two anchors is a no-verdict" "carries 2"

mktree "$WORK/t14" '<!-- fsgg:retirement-verdict
retired: alpha, beta, gamma
in-flight:
-->' "3 of 2"
expect 3 "$WORK/t14" "a receiver the roster does not list" "gamma" "roster"

# A block with no `in-flight:` key at all is refused rather than defaulted to empty — a gate that
# defaulted it would be grading against a list nobody wrote.
mktree "$WORK/t15" '<!-- fsgg:retirement-verdict
retired: alpha, beta
-->' "2 of 2"
expect 3 "$WORK/t15" "a missing in-flight key is refused, not defaulted" "has no \`in-flight:\` line"

# ---------------------------------------------------------------------------------------------
# Leg 16 — an EMPTY roster is a broken read, not a completed sequence (AC 2). This is the shape a
# gate with a hardcoded receiver list could never reach, and the one that certifies everything by
# examining nothing.
# ---------------------------------------------------------------------------------------------
mktree "$WORK/t16" "$BLOCK_BOTH" "2 of 2"
cat >"$WORK/t16/registry/repos.yml" <<'YAML'
schemaVersion: 1
repos:
  - { id: hub, full: Fixture-Org/Fixture.Hub, role: authority, receives: [labels] }
YAML
expect 3 "$WORK/t16" "an empty roster is a no-verdict" "ZERO receivers" "broken read"

# A root-set declaration with fewer than two roots makes "no receiver commits a SECOND root"
# unfalsifiable, so it is refused rather than passed vacuously.
mktree "$WORK/t17" "$BLOCK_BOTH" "2 of 2"
printf 'DEFAULT_ROOTS=".src/skills"\n' >"$WORK/t17/scripts/skill-view"
expect 3 "$WORK/t17" "a one-root default set is refused" "cannot be false"

# ---------------------------------------------------------------------------------------------
# Legs 18-20 — the transport. Every unreadable-subject condition is a no-verdict, and the RETRYABLE
# and PERMANENT ones keep different codes: a token that cannot see a repository is not the same
# event as a tree the API truncated, and neither is ever green.
# ---------------------------------------------------------------------------------------------
WORLD="$WORK/w18"; export FIXTURE_WORLD="$WORLD"
mkrepo "$WORLD" Fixture-Org/Fixture.Alpha ".src/skills/a/SKILL.md"
mkrepo "$WORLD" Fixture-Org/Fixture.Beta  ".src/skills/b/SKILL.md"
touch "$WORLD/Fixture-Org__Fixture.Beta/invisible"
mktree "$WORK/t18" "$BLOCK_BOTH" "2 of 2"
expect 2 "$WORK/t18" "a repository we cannot see is retryable, not a finding" "404" "we did not look"

WORLD="$WORK/w19"; export FIXTURE_WORLD="$WORLD"
mkrepo "$WORLD" Fixture-Org/Fixture.Alpha ".src/skills/a/SKILL.md"
mkrepo "$WORLD" Fixture-Org/Fixture.Beta  ".src/skills/b/SKILL.md"
touch "$WORLD/Fixture-Org__Fixture.Beta/truncated"
mktree "$WORK/t19" "$BLOCK_BOTH" "2 of 2"
expect 3 "$WORK/t19" "a truncated tree is not a tree" "TRUNCATED" "partial tree"

WORLD="$WORK/w20"; export FIXTURE_WORLD="$WORLD"
mkrepo "$WORLD" Fixture-Org/Fixture.Alpha ".src/skills/a/SKILL.md"
mkrepo "$WORLD" Fixture-Org/Fixture.Beta  ".src/skills/b/SKILL.md"
touch "$WORLD/ratelimit"
mktree "$WORK/t20" "$BLOCK_BOTH" "2 of 2"
expect 2 "$WORK/t20" "a rate limit is retryable, not a finding" "rate limit"

# ---------------------------------------------------------------------------------------------
# Leg 21 — no `gh` at all is PERMANENT, and not a finding. There is no subject to report on.
# `$NOGH` is an EMPTY directory and the interpreter is invoked by absolute path: emptying `PATH`
# outright would also remove `python3`, and the leg would then pass on a 127 that says nothing about
# this gate — the very "non-zero for the wrong reason" trap this file's header names.
# Leg 22 — `--offline` grades the document and reads nothing, so it survives leg 21's world; its OK
# line must SAY that it read no tree, or a caller would mistake it for the real verdict.
# ---------------------------------------------------------------------------------------------
WORLD="$WORK/w1"; export FIXTURE_WORLD="$WORLD"
mktree "$WORK/t21" "$BLOCK_BOTH" "2 of 2"
rc=0; out="$(PATH="$NOGH" "$PYBIN" "$GATE" --root "$WORK/t21" 2>&1)" || rc=$?
if [ "$rc" = 3 ] && printf '%s' "$out" | grep -qi "not on PATH"; then
  ok "no gh on PATH is a permanent no-verdict (exit 3)"
else
  bad "no gh on PATH is a permanent no-verdict" "expected exit 3 naming PATH, got $rc
$out"
fi

rc=0; out="$(PATH="$NOGH" "$PYBIN" "$GATE" --root "$WORK/t21" --offline 2>&1)" || rc=$?
if [ "$rc" = 0 ] && printf '%s' "$out" | grep -qi "NO receiver tree was read"; then
  ok "--offline reads no tree and says so (exit 0)"
else
  bad "--offline reads no tree and says so" "expected exit 0 disclaiming the tree read, got $rc
$out"
fi

# `--offline` must still red on a document defect, or it is a way to skip the document legs too.
mktree "$WORK/t23" "$BLOCK_BOTH" "1 of 2"
rc=0; out="$(PATH="$NOGH" "$PYBIN" "$GATE" --root "$WORK/t23" --offline 2>&1)" || rc=$?
if [ "$rc" = 1 ] && printf '%s' "$out" | grep -qi "the heading says 1 of 2"; then
  ok "--offline still reds on a document defect (exit 1)"
else
  bad "--offline still reds on a document defect" "expected exit 1, got $rc
$out"
fi

# ---------------------------------------------------------------------------------------------
# Legs 24-32 — every one of these was a REPRODUCED defect in this gate's first reviewed revision,
# and every one of them was a way to reach GREEN, or a crash, without establishing the subject.
# ---------------------------------------------------------------------------------------------

# `in-flight:` IS a mute button unless it has a floor. With every receiver parked the gate read ZERO
# trees and reported OK having invoked `gh` not once — green over nothing, the same shape the empty
# roster leg already refuses one level up. The ref requirement makes parking attributable, not bounded.
WORLD="$WORK/w1"; export FIXTURE_WORLD="$WORLD"
mktree "$WORK/t24" '<!-- fsgg:retirement-verdict
retired:
in-flight: alpha(#1), beta(#2)
-->' "0 of 2"
expect 3 "$WORK/t24" "parking EVERY receiver in-flight is refused, not green" "ZERO trees" "not a way to switch the comparison off"

# An `.agent-skill-roots` that parses to NOTHING is a misconfiguration, not an empty root set.
# Falling back to the default there silently substitutes a set the tree explicitly declined —
# `scripts/lib/roots.sh:resolve_roots` dies on exactly this, and so must this gate. The receiver below
# commits TWO roots it once declared; the fall-through graded it retired, green.
WORLD="$WORK/w25"; export FIXTURE_WORLD="$WORLD"
mkrepo "$WORLD" Fixture-Org/Fixture.Alpha ".src/skills/a/SKILL.md"
mkrepo "$WORLD" Fixture-Org/Fixture.Beta ".agent-skill-roots" ".other/skills/b/SKILL.md" ".another/skills/b/SKILL.md"
printf '# all comments, no roots\n' >"$WORLD/Fixture-Org__Fixture.Beta/roots"
mktree "$WORK/t25" "$BLOCK_BOTH" "2 of 2"
expect 3 "$WORK/t25" "a roots declaration that parses to nothing is refused" "declares no roots" "explicitly declined"

# A `./`-prefixed root never prefix-matches a tree path, so BOTH roots looked empty and the receiver
# was graded retired — green, forever, on a declaration `roots.sh` and `skill-view` both accept.
WORLD="$WORK/w26"; export FIXTURE_WORLD="$WORLD"
mkrepo "$WORLD" Fixture-Org/Fixture.Alpha ".src/skills/a/SKILL.md"
mkrepo "$WORLD" Fixture-Org/Fixture.Beta ".agent-skill-roots" ".src/skills/b/SKILL.md" ".view/skills/b/SKILL.md"
printf './.src/skills\n./.view/skills\n' >"$WORLD/Fixture-Org__Fixture.Beta/roots"
mktree "$WORK/t26" "$BLOCK_BOTH" "2 of 2"
expect 1 "$WORK/t26" "a ./-prefixed root still matches the tree" "(beta) is NOT retired"

# An INLINE `#` comment is stripped to end-of-line, exactly as `roots.sh:read_roots_decl`'s
# `sed 's/#.*$//'` does. Treating only whole-line comments as comments turned the rest of the line into
# roots — `docs` then matched `docs/x.md` and the gate red, blaming the receiver for a root it never had.
WORLD="$WORK/w27"; export FIXTURE_WORLD="$WORLD"
mkrepo "$WORLD" Fixture-Org/Fixture.Alpha ".src/skills/a/SKILL.md"
mkrepo "$WORLD" Fixture-Org/Fixture.Beta ".agent-skill-roots" ".src/skills/b/SKILL.md" "docs/x.md"
printf '.src/skills # docs are not skills\n' >"$WORLD/Fixture-Org__Fixture.Beta/roots"
mktree "$WORK/t27" "$BLOCK_BOTH" "2 of 2"
expect 0 "$WORK/t27" "an inline # comment is stripped, as roots.sh strips it" "2 of 2 retired"

# A readable repository whose default branch has NO TREE (no commits) 404s on the tree read. That 404
# escaped uncaught and surfaced as "the gate CRASHED — a bug in the gate", which is the wrong story
# about routine remote state; and an absent tree is not an absent root either way.
WORLD="$WORK/w28"; export FIXTURE_WORLD="$WORLD"
mkrepo "$WORLD" Fixture-Org/Fixture.Alpha ".src/skills/a/SKILL.md"
mkrepo "$WORLD" Fixture-Org/Fixture.Beta  ".src/skills/b/SKILL.md"
touch "$WORLD/Fixture-Org__Fixture.Beta/no-commits"
mktree "$WORK/t28" "$BLOCK_BOTH" "2 of 2"
expect 3 "$WORK/t28" "a repo with no tree on its default branch is refused, not a crash" "has no tree (404)" "not a receiver that retired"

# `.agent-skill-roots` is a blob in the tree and its contents read 404s — the two reads disagree about
# the same tree. Also uncaught, also a crash. Falling back to the default here would grade the receiver
# against a set it may have declined.
WORLD="$WORK/w29"; export FIXTURE_WORLD="$WORLD"
mkrepo "$WORLD" Fixture-Org/Fixture.Alpha ".src/skills/a/SKILL.md"
mkrepo "$WORLD" Fixture-Org/Fixture.Beta ".agent-skill-roots" ".src/skills/b/SKILL.md"
mktree "$WORK/t29" "$BLOCK_BOTH" "2 of 2"
expect 3 "$WORK/t29" "a tree/contents disagreement about .agent-skill-roots is refused, not a crash" \
  "contents read for it answers 404" "cannot establish which roots"

# ZERO occupied roots is not a retirement. An emptied tree is indistinguishable from a completed
# retirement, and scoring it "retired" is the same absence-as-evidence the truncated-tree guard refuses.
WORLD="$WORK/w30"; export FIXTURE_WORLD="$WORLD"
mkrepo "$WORLD" Fixture-Org/Fixture.Alpha ".src/skills/a/SKILL.md"
mkrepo "$WORLD" Fixture-Org/Fixture.Beta  "README.md"
mktree "$WORK/t30" "$BLOCK_BOTH" "2 of 2"
expect 3 "$WORK/t30" "a receiver committing no skill root at all is refused" "commits NO blobs" "zero is not 'retired'"

# The DEFAULT BRANCH is read, not assumed. The stub refuses a tree read that is not at this repo's
# branch, so a gate that hardcoded `/git/trees/main` cannot pass this leg.
WORLD="$WORK/w31"; export FIXTURE_WORLD="$WORLD"
mkrepo "$WORLD" Fixture-Org/Fixture.Alpha ".src/skills/a/SKILL.md"
mkrepo "$WORLD" Fixture-Org/Fixture.Beta  ".src/skills/b/SKILL.md"
echo trunk >"$WORLD/Fixture-Org__Fixture.Beta/branch"
mktree "$WORK/t31" "$BLOCK_BOTH" "2 of 2"
expect 0 "$WORK/t31" "the tree is read at the repo's OWN default branch" "2 of 2 retired"

# ---------------------------------------------------------------------------------------------
# Legs 32-34 — the document's live surface, in the two shapes that made half of leg 4 evaporate.
# ---------------------------------------------------------------------------------------------

# A count WRAPPED across a line break was invisible to a per-line match. The document is hard-wrapped
# at ~100 columns, so the very sentence this change removed was one reflow from surviving the gate.
WORLD="$WORK/w1"; export FIXTURE_WORLD="$WORLD"
mktree "$WORK/t32" "$BLOCK_BOTH" "2 of 2" "> The sequence is COMPLETE — 2
> of 2."
expect 1 "$WORK/t32" "a count wrapped across a line break is still a second count" "the header block states a second count"

# A `---` at line 1 (YAML front matter, or a leading rule) collapsed the header block to nothing and
# SILENTLY retired half of leg 4 — one added front-matter block, no signal at all.
mktree "$WORK/t33" "$BLOCK_BOTH" "2 of 2" "> The sequence is COMPLETE — 2 of 2."
printf '%s\n' '---' "title: x" '---' "$(cat "$WORK/t33/docs/coordination/skill-apparatus-retirement-order.md")" \
  >"$WORK/t33/docs/coordination/skill-apparatus-retirement-order.md.new"
mv "$WORK/t33/docs/coordination/skill-apparatus-retirement-order.md.new" \
   "$WORK/t33/docs/coordination/skill-apparatus-retirement-order.md"
expect 3 "$WORK/t33" "a rule at line 1 empties the header block and is refused" "header block" "is EMPTY"

# No `---` at all: the gate cannot tell the header block from the body, so it refuses rather than
# grading every historical record as a live restatement (#698).
mktree "$WORK/t34" "$BLOCK_BOTH" "2 of 2"
python3 - "$WORK/t34/docs/coordination/skill-apparatus-retirement-order.md" <<'PY'
import sys
p = sys.argv[1]
# Read BEFORE opening for write. `open(p,"w").write(<expr reading p>)` truncates first and the
# expression then reads an empty file — which made this leg pass on the wrong refusal entirely.
kept = [l for l in open(p).read().splitlines() if l.rstrip() != "---"]
open(p, "w").write("\n".join(kept) + "\n")
PY
expect 3 "$WORK/t34" "no --- rule at all is refused" "has no" "rule"

# An unknown key in the verdict block is refused: a key the gate ignores is a fact a writer believes
# is graded and is not.
mktree "$WORK/t35" '<!-- fsgg:retirement-verdict
retired: alpha, beta
in-flight:
refused: sdd
-->' "2 of 2"
expect 3 "$WORK/t35" "an unknown key in the verdict block is refused" "does not read"

# An explicitly EMPTY transport knob is a misconfiguration, not the default — two ways of getting a
# knob wrong must not have opposite consequences.
rc=0; out="$(FSGG_RETIREMENT_TRIES='' $PY "$GATE" --root "$WORK/t1" 2>&1)" || rc=$?
if [ "$rc" = 3 ] && printf '%s' "$out" | grep -qiF -- "FSGG_RETIREMENT_TRIES"; then
  ok "an explicitly empty transport knob is refused (exit 3)"
else
  bad "an explicitly empty transport knob is refused" "expected exit 3 naming the variable, got $rc
$out"
fi

printf '\n%s passed, %s failed\n' "$pass" "$failcount"
[ "$failcount" -eq 0 ] || exit 1
