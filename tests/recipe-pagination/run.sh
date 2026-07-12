#!/usr/bin/env bash
# Fixture for scripts/check-recipe-pagination.py — the `gh api` LIST-read pagination gate (.github#547).
#
# The gate exists because an unpaginated `gh api` collection read returns the first 30 items and
# exits 0: the output does not look truncated, it looks like an answer. So the FAILURE LEGS are the
# point of this fixture. A gate that cannot say NO is the #266 defect it was written to close, and a
# fixture that only ever feeds it clean input would never notice.
#
# Every leg below asserts the EXIT CODE (the gate's contract) and, for findings, that the finding
# names the right file:line. No leg greps the gate's prose for a verdict.
#
# The last leg runs the gate against THIS REPO's real recipes and requires green. Without it, every
# leg above is synthetic and the gate could pass its own tests while the shipped skills truncate.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
GATE="$REPO_ROOT/scripts/check-recipe-pagination.py"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/recipe-pagination-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

echo "recipe-pagination fixture — work='$WORK'"

# Build a throwaway recipe tree and run the gate over it.
# usage: gate_on <case-name> <markdown-body>   -> sets $RC and $OUT
gate_on() {
  local name="$1" body="$2" dir="$WORK/$1/recipes/kit"
  mkdir -p "$dir"
  printf '%s\n' "$body" > "$dir/SKILL.md"
  set +e
  OUT="$(cd "$WORK/$1" && python3 "$GATE" --root . --recipes recipes 2>&1)"
  RC=$?
  set -e
}

# ---------------------------------------------------------------------------------------------
# FAILURE LEGS — each must fire. If any of these goes green the gate is decorative.
# ---------------------------------------------------------------------------------------------

# 1. The live #547 bug: the look-before-you-file sub_issues read.
gate_on unpaginated-subissues '# kit

```sh
gh api repos/FS-GG/<repo>/issues/<parent>/sub_issues --jq '"'"'.[] | "#\(.number)"'"'"'
```'
if [ "$RC" = 1 ] && printf '%s' "$OUT" | grep -q 'SKILL.md:4:'; then
  ok "unpaginated sub_issues read is a FINDING, named at its line"
else
  bad "unpaginated sub_issues read should exit 1 and name SKILL.md:4 (got rc=$RC)" "$OUT"
fi

# 2. The merge gate: an aggregate over `.check_runs[]`. Truncation here reports pending=0 failed=0
#    while the checks that would have stopped the merge sit on page 2.
gate_on unpaginated-checkruns '# kit

```sh
gh api "repos/FS-GG/<repo>/commits/$SHA/check-runs" \
  --jq '"'"'"pending=\([.check_runs[]|select(.status!="completed")]|length)"'"'"'
```'
if [ "$RC" = 1 ] && printf '%s' "$OUT" | grep -q 'iterates the response as an array'; then
  ok "unpaginated check-runs aggregate is a FINDING (nested array iteration)"
else
  bad "unpaginated check-runs aggregate should exit 1 (got rc=$RC)" "$OUT"
fi

# 3. The backstop: a collection read with NO jq at all. The jq signal cannot see this one, so if the
#    endpoint heuristic ever regresses this leg is the only thing standing between a worker and a
#    truncated list.
gate_on unpaginated-nojq '# kit

```sh
gh api repos/FS-GG/<repo>/issues/<parent>/sub_issues
```'
if [ "$RC" = 1 ] && printf '%s' "$OUT" | grep -q 'collection `sub_issues`'; then
  ok "collection read with NO jq is a FINDING (endpoint backstop)"
else
  bad "collection read with no jq should exit 1 via the endpoint backstop (got rc=$RC)" "$OUT"
fi

# ---------------------------------------------------------------------------------------------
# NEGATIVE LEGS — a gate that cries wolf gets ignored, and then it protects nothing.
# ---------------------------------------------------------------------------------------------

# 4. A single-resource GET reaching for a scalar. `pnext-item` §5 does exactly this to get the head
#    SHA; flagging it would be a false positive on correct code.
gate_on single-resource '# kit

```sh
SHA=$(gh api repos/FS-GG/<repo>/pulls/<n> --jq .head.sha)
```'
[ "$RC" = 0 ] && ok "single-resource scalar read is NOT flagged" \
              || bad "single-resource read must not be flagged (got rc=$RC)" "$OUT"

# 5. Writes. `--paginate` is meaningless on a POST, and both `pnext-item` §4 and §5 POST to
#    collection endpoints (`/issues`, `/pulls`) — the endpoint backstop must not fire on them.
gate_on writes '# kit

```sh
gh api -X POST repos/FS-GG/<target>/issues --input - --jq '"'"'"#\(.number)"'"'"'
gh api -X PUT repos/FS-GG/<repo>/pulls/<n>/merge -f merge_method=squash
gh api -X DELETE repos/FS-GG/<repo>/git/refs/heads/item/<n>-<slug>
```'
[ "$RC" = 0 ] && ok "writes to collection endpoints are NOT flagged" \
              || bad "writes must not be flagged (got rc=$RC)" "$OUT"

# 6. Prose. These skills DISCUSS `gh api` at length ("`gh api repos/…` will still open your PR").
#    A gate that flags prose trains workers to ignore it.
gate_on prose '# kit

When the budget is gone, `gh api repos/FS-GG/<repo>/issues/<parent>/sub_issues --jq .[]` still
works, because REST is a separate budget. That sentence is not a command.'
[ "$RC" = 3 ] && ok "prose outside a fence is not audited (and auditing NOTHING is rc=3, not green)" \
              || bad "prose-only recipe should audit nothing and exit 3 (got rc=$RC)" "$OUT"

# 7. The multi-line quoted jq — the shape the fixed recipe actually ships. `--slurp` cannot be
#    combined with `--jq`, so the aggregate is piped into a separate `jq` whose script spans lines
#    inside ONE pair of single quotes, with no backslash. A line-at-a-time reader would see the
#    second line as its own command, miss the `--paginate` on the first, and flag correct code.
gate_on multiline-quoted-jq '# kit

```sh
gh api "repos/FS-GG/<repo>/commits/$SHA/check-runs" --paginate --slurp \
  | jq -r '"'"'[.[].check_runs[]]
           | "checks=\(length) pending=\([.[]|select(.status!="completed")]|length)"'"'"'
```'
[ "$RC" = 0 ] && ok "paginated read with a multi-line quoted jq is NOT flagged" \
              || bad "multi-line quoted jq must not be flagged — logical-line joining is broken (got rc=$RC)" "$OUT"

# ---------------------------------------------------------------------------------------------
# NO-VERDICT LEGS — "I could not check" must never be mistaken for "I checked, and it's fine" (#266).
# ---------------------------------------------------------------------------------------------

# 8. Auditing zero commands. If the fence extractor breaks, EVERY recipe reads as clean — the gate
#    would go green across the org while the truncation it exists to catch went right on happening.
gate_on nothing-to-audit '# kit

No commands here at all.'
[ "$RC" = 3 ] && ok "auditing ZERO gh api commands is rc=3 (no verdict), NOT green" \
              || bad "a recipe tree with no gh api commands must exit 3 (got rc=$RC)" "$OUT"

# 9. A recipe dir that is not there. Same rule: cannot read the subject => no verdict.
set +e
OUT="$(cd "$WORK" && python3 "$GATE" --root . --recipes does-not-exist 2>&1)"; RC=$?
set -e
[ "$RC" = 3 ] && ok "a missing recipe dir is rc=3 (no verdict), NOT green" \
              || bad "a missing recipe dir must exit 3 (got rc=$RC)" "$OUT"

# ---------------------------------------------------------------------------------------------
# DOGFOOD — the leg that makes the eight above mean something.
# ---------------------------------------------------------------------------------------------

# 10. The shipped recipes, in every agent-skill root. Without this the gate could pass a fixture
#     suite built entirely from strings in this file while the skills a worker actually copies
#     truncate at 30 — a fixture green on a subject it never read, which is #266 one level up.
set +e
OUT="$(cd "$REPO_ROOT" && python3 "$GATE" --root . 2>&1)"; RC=$?
set -e
if [ "$RC" = 0 ]; then
  ok "this repo's own shipped recipes pass: $(printf '%s' "$OUT" | tail -1)"
else
  bad "this repo's shipped recipes do NOT pass the gate (rc=$RC)" "$OUT"
fi

echo
echo "recipe-pagination: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1
