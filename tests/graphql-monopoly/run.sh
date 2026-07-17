#!/usr/bin/env bash
# Fixture for scripts/check-graphql-monopoly.py — the client is the org's ONLY GraphQL principal (#587).
#
# The gate exists because the GraphQL budget is 5,000 pts/hr SHARED BY THE WHOLE FLEET (N agents, one
# account), and a recipe that reaches past `fsgg-coord` is an unmetered principal on it — nothing
# meters it, caches it, or queues it when the budget dies. #528 (a worker who could not land finished
# work) and #538 (a reconciler that drained the budget it needed) are both that.
#
# THE FAILURE LEGS ARE THE POINT. A gate that cannot say NO is the #266 defect it was written to close.
# Two of the legs below are regressions this gate ALREADY suffered, in its first hour:
#
#   * the BLOCKQUOTED fence. The first cut matched `^\s*```` and could not see `> ```sh`, so it
#     reported GREEN over a real `gh project create` in docs/coordination/README.md. Epic #266's shape,
#     inside the gate written to enforce #266's rule.
#   * the PROSE leg. `graphql-budget.md`'s cost table exists precisely to say "never use
#     `gh project item-list`". A checker that flagged it would be demanding the docs stop teaching the
#     rule they teach — so a naive grep is not merely noisy, it is WRONG.
#
# The last leg runs the gate against THIS REPO's real surfaces and requires green. Without it every
# leg above is synthetic, and the gate could pass its own tests while the shipped skills spend GraphQL.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
GATE="$REPO_ROOT/scripts/check-graphql-monopoly.py"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/graphql-monopoly-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

echo "graphql-monopoly fixture — work='$WORK'"

# gate_on <case> <markdown> -> sets $RC and $OUT
gate_on() {
  local name="$1" body="$2" dir="$WORK/$1/.claude/skills/kit"
  # "${WORK:?}", not "$WORK": this is an `rm -rf`, and an unset/empty WORK would make it `rm -rf /$name`
  # (SC2115). WORK is a mktemp -d and cannot realistically be empty — which is exactly the reasoning
  # that makes an unguarded rm survive review until the day it is wrong. The guard costs nothing. #648
  rm -rf "${WORK:?}/$name"; mkdir -p "$dir"
  printf '%s\n' "$body" > "$dir/SKILL.md"
  set +e
  OUT="$(python3 "$GATE" --root "$WORK/$name" 2>&1)"
  RC=$?
  set -e
}

expect_rc() {  # <case> <want-rc> <label>
  if [ "$RC" = "$2" ]; then ok "$3"; else bad "$3" "want rc=$2, got rc=$RC
$OUT"; fi
}

# ---- 1. THE VIOLATIONS. A runnable line inside a fence, for each banned command. -----------------
for cmd in \
  'gh api graphql -f query='"'"'{viewer{login}}'"'"'' \
  'gh project item-add 1 --owner FS-GG --url https://github.com/FS-GG/x/issues/1' \
  'gh project item-list 1 --owner FS-GG' \
  'gh issue view 42 --repo FS-GG/x' \
  'gh issue list --repo FS-GG/x --label cross-repo' \
  'gh issue create --repo FS-GG/x --title t' \
  'gh pr create --fill --base main' \
  'gh pr checks 12 --watch' \
  'gh pr merge 12 --squash'
do
  gate_on "v" "# Recipe

\`\`\`sh
$cmd
\`\`\`
"
  short="$(printf '%s' "$cmd" | cut -c1-28)"
  expect_rc v 1 "FINDING: a fenced \`$short…\` is refused"
done

# ---- 2. PROSE IS NOT A PRESCRIPTION. The docs must be free to TEACH the rule. --------------------
# This is the leg that makes a naive grep wrong, not merely noisy.
gate_on prose '# Budget

Never reach for `gh project item-list` — it costs 6 points to read FIVE items. And
`gh issue view` is 2 points; use `fsgg-coord issues`, which is REST and free.

| call | cost |
|---|---|
| `gh project item-list --limit 5` | 6 pts |
| `gh issue list` / `gh issue view` | 2 pts each |

```sh
scripts/fsgg-coord ready --repo sdd
```
'
expect_rc prose 0 "PROSE: a doc that WARNS you off a command is not a violation of it"

# ...and a `#` comment inside a fence is prose too — it is not a line the worker runs.
gate_on comment '# Recipe

```sh
# do NOT use `gh project item-list` here — 6 pts to read five items
scripts/fsgg-coord ready
```
'
expect_rc comment 0 "PROSE: a # comment inside a fence is not a runnable line"

# ---- 3. THE BLOCKQUOTED FENCE — a regression this gate already had. ------------------------------
# The first cut matched `^\s*``` `, so `> ```sh ` was invisible and a real `gh project create` in
# docs/coordination/README.md passed GREEN. A gate with a blind spot is the bug it exists to catch.
gate_on quoted '# Setup

> One-time, needs the project scope:
> ```sh
> gh project create --owner FS-GG --title "Coordination"
> ```
'
expect_rc quoted 1 "BLIND SPOT: a fence inside a BLOCKQUOTE is still a fence — the gate's own #266"

# ---- 4. THE EXEMPTION is explicit, and it is SCOPED. ---------------------------------------------
gate_on exempt '# Setup

<!-- graphql-monopoly: exempt — one-time board provisioning, run once by a human -->
```sh
gh project create --owner FS-GG --title "Coordination"
```
'
expect_rc exempt 0 "EXEMPT: an explicitly marked provisioning block is allowed"

# ...and it exempts ONE block, not the rest of the file. An exemption that leaked forward would be a
# fail-open with a comment on it.
gate_on exempt_scope '# Setup

<!-- graphql-monopoly: exempt — one-time board provisioning -->
```sh
gh project create --owner FS-GG --title "Coordination"
```

Now the worker path:

```sh
gh project item-add 1 --owner FS-GG --url https://github.com/FS-GG/x/issues/1
```
'
expect_rc exempt_scope 1 "EXEMPT: the marker covers ONE fence, and does not leak to the next"

# ...and it must not drift onto a block it was never written for: prose between the marker and the
# fence cancels it.
gate_on exempt_drift '# Setup

<!-- graphql-monopoly: exempt — one-time board provisioning -->

Some intervening prose that means this marker was for something else.

```sh
gh project item-add 1 --owner FS-GG --url https://github.com/FS-GG/x/issues/1
```
'
expect_rc exempt_drift 1 "EXEMPT: prose between the marker and the fence cancels it"

# ---- 5. NON-VACUITY. A checker that reaches nothing must NOT report green. ------------------------
# This is the guard the whole repo is built on: an unverifiable subject is not a clean one (#266).
rm -rf "$WORK/empty"; mkdir -p "$WORK/empty"
set +e
OUT="$(python3 "$GATE" --root "$WORK/empty" 2>&1)"; RC=$?
set -e
expect_rc empty 3 "NON-VACUITY: no surfaces at all -> exit 3 (NO_VERDICT), never a vacuous green"

# ...and surfaces that exist but yield NO fenced lines are equally unverifiable.
rm -rf "$WORK/nofence"; mkdir -p "$WORK/nofence/.claude/skills/kit"
printf '# A skill with prose and no fenced code at all.\n' > "$WORK/nofence/.claude/skills/kit/SKILL.md"
set +e
OUT="$(python3 "$GATE" --root "$WORK/nofence" 2>&1)"; RC=$?
set -e
expect_rc nofence 3 "NON-VACUITY: files but zero fenced lines -> exit 3, the extractor is broken"

# ---- 6. THE CLIENT ITSELF IS THE SANCTIONED PATH. ------------------------------------------------
gate_on clean '# Recipe

```sh
scripts/fsgg-coord add FS-GG/FS.GG.SDD#42
scripts/fsgg-coord set-field 42 Status Backlog
scripts/fsgg-coord issues sdd --label cross-repo
gh api -X POST repos/FS-GG/FS.GG.SDD/issues -f title=t
gh api -X PUT repos/FS-GG/FS.GG.SDD/pulls/1/merge -f merge_method=squash
```
'
expect_rc clean 0 "CLEAN: the client, and REST, are the sanctioned path"

# ---- 7. THE SHIPPED TREE. Without this, every leg above is synthetic. -----------------------------
set +e
OUT="$(python3 "$GATE" 2>&1)"; RC=$?
set -e
if [ "$RC" = 0 ]; then ok "SHIPPED: this repo's real skills + docs spend no GraphQL directly"
else bad "SHIPPED: this repo's real skills + docs spend no GraphQL directly" "$OUT"; fi

echo
echo "graphql-monopoly fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "graphql-monopoly fixture FAILED" >&2; exit 1; }
