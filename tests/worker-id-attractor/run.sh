#!/usr/bin/env bash
# Fixture for scripts/check-worker-id-attractor.py — the collision-attractor gate (.github#570).
#
# The gate exists because the attractor has been removed BY HAND TWICE (#532, #551) and grew back in
# between. So the FAILURE LEGS are the point of this fixture: a gate that cannot say NO is the #266
# defect it was written to close, and a fixture that only ever fed it clean input would never notice.
#
# Every leg asserts the EXIT CODE (the gate's contract) and, for findings, that the finding names the
# right file:line and the right reason. No leg greps the gate's prose for a verdict.
#
# THE HEADLINE LEGS ARE THE TWO REAL REGRESSIONS, replayed against the REAL tree:
#   - the `finch-*` literal that #532 had to remove from the skills (#419);
#   - the hand-rolled `od -An -tx1 /dev/urandom` mint that #551 had to remove from
#     docs/coordination/parallel-work.md — the document the skills are a projection OF, and the one
#     the skills-only fix of #532 could not see.
# Both are re-injected into a copy of the shipped tree, and the gate must red-light both.
#
# The last leg runs the gate against THIS REPO's real surface and requires green. Without it, every
# leg above is synthetic, and the gate could pass its own tests while the attractor ships.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
GATE="$REPO_ROOT/scripts/check-worker-id-attractor.py"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/worker-id-attractor-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

export PYTHONDONTWRITEBYTECODE=1

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

expect() {  # expect <name> <want-rc> <needle> <root> [args…]
  local name="$1" want="$2" needle="$3" root="$4"; shift 4
  local out rc=0
  out="$(python3 "$GATE" --root "$root" "$@" 2>&1)" || rc=$?
  if [ "$rc" -ne "$want" ]; then
    bad "$name (exit $rc, want $want)" "$out"
  elif [ -n "$needle" ] && ! grep -qF "$needle" <<<"$out"; then
    bad "$name (exit $want, but not for the stated reason: want '$needle')" "$out"
  else
    ok "$name"
  fi
}

# A synthetic surface: one skill root, one docs dir, both minimal but SHAPED LIKE THE REAL ONES —
# i.e. they mention the worker id (so the non-vacuity guard is satisfied) and teach the mint.
mksurface() {  # mksurface <dir>
  local d="$1"
  mkdir -p "$d/.claude/skills/s" "$d/docs"
  printf '.claude/skills\n' > "$d/.agent-skill-roots"
  { echo '# skill'; echo; echo 'Mint your id, never invent one:'; echo;
    echo '```sh'; echo 'eval "$(scripts/fsgg-coord whoami --mint)"'; echo '```'; echo;
    echo 'Commit with the trailer `FSGG-Worker: <id>` so attribution survives.'; } \
    > "$d/.claude/skills/s/SKILL.md"
  { echo '# protocol'; echo; echo 'Resolution order: `--worker <id>` -> `$FSGG_WORKER` -> worktree.'; } \
    > "$d/docs/protocol.md"
  echo "$d"
}

# =============================================================================================
# 0. The clean surface is GREEN. A gate that reds on correct input is a gate somebody deletes.
# =============================================================================================
CLEAN="$(mksurface "$WORK/clean")"
expect "a clean surface — placeholder trailer, one mint, no literal — is green" \
  0 "ok: no literal worker id" "$CLEAN"

# =============================================================================================
# 1. THE REAL REGRESSIONS, replayed on a copy of the SHIPPED tree.
# =============================================================================================
REAL="$WORK/real"; mkdir -p "$REAL"
cp -r "$REPO_ROOT/.claude" "$REPO_ROOT/.agents" "$REPO_ROOT/docs" \
      "$REPO_ROOT/.agent-skill-roots" "$REAL/"

# Sanity: the shipped tree, copied, is green. Everything below is a mutation of THIS.
expect "the SHIPPED tree, copied verbatim, is green (so every mutation below is the only variable)" \
  0 "ok: no literal worker id" "$REAL"

# (a) #419 / #532 — the `finch-*` literal in a skill. This is the exact shape that put FOUR workers
#     on one id, because agents copy the example rather than mint.
REAL_A="$WORK/real-a"; cp -r "$REAL" "$REAL_A"
printf '\n```sh\nexport FSGG_WORKER=finch-a3f\n```\n' \
  >> "$REAL_A/.claude/skills/pnext-item/SKILL.md"
expect "REGRESSION #419/#532: a literal \`finch-a3f\` in a SKILL is caught" \
  1 "is a LITERAL worker id" "$REAL_A"
expect "REGRESSION: and it says WHY — ids picked by reading converge" \
  1 "four \`finch-*\` workers at once" "$REAL_A"

# (b) #551 — the hand-rolled mint in docs/coordination/parallel-work.md. #532 fixed the SKILLS and
#     this grew back in the doc they are a projection OF. A gate over the skill roots alone would
#     have reported green through the whole window that #551 existed to close.
REAL_B="$WORK/real-b"; cp -r "$REAL" "$REAL_B"
printf '\n```sh\nexport FSGG_WORKER="w-$(od -An -tx1 -N4 /dev/urandom | tr -d '"'"' \\n'"'"')"\n```\n' \
  >> "$REAL_B/docs/coordination/parallel-work.md"
expect "REGRESSION #551: the hand-rolled mint, in the DOC the skills are a projection of, is caught" \
  1 "is a hand-rolled source of randomness" "$REAL_B"
expect "REGRESSION: and the finding names the doc, not just the skills" \
  1 "docs/coordination/parallel-work.md" "$REAL_B"

# (c) The commit-trailer spelling — how one of the two survived #532 in the first place.
REAL_C="$WORK/real-c"; cp -r "$REAL" "$REAL_C"
printf '\n```\nFSGG-Worker: w-4f2a91c7\n```\n' >> "$REAL_C/docs/coordination/parallel-work.md"
expect "REGRESSION #532: the \`FSGG-Worker:\` TRAILER spelling of a literal id is caught too" \
  1 "\`FSGG-Worker:w-4f2a91c7\` is a LITERAL worker id" "$REAL_C"

# =============================================================================================
# 2. Rule 1 — a literal id, in every spelling. And the placeholders that must NOT be flagged.
# =============================================================================================
lit() {  # lit <name> <line>
  local d="$WORK/lit-$RANDOM$RANDOM"; mksurface "$d" >/dev/null
  printf '\n```sh\n%s\n```\n' "$2" >> "$d/docs/protocol.md"
  expect "$1" 1 "is a LITERAL worker id" "$d"
}
lit "a bare \`FSGG_WORKER=w-38e58ee5\` is caught"          'FSGG_WORKER=w-38e58ee5'
lit "an \`export\`ed literal is caught"                     'export FSGG_WORKER=w-4f2a91c7'
lit "a QUOTED literal is caught"                            'export FSGG_WORKER="w-4f2a91c7"'
lit "a single-quoted literal is caught"                     "export FSGG_WORKER='finch-a3f'"

nolit() {  # nolit <name> <line>
  local d="$WORK/nolit-$RANDOM$RANDOM"; mksurface "$d" >/dev/null
  printf '\n```sh\n%s\n```\n' "$2" >> "$d/docs/protocol.md"
  expect "$1" 0 "ok: no literal worker id" "$d"
}
nolit "a PLACEHOLDER \`<id>\` is exactly what a recipe should show — not flagged" \
      'FSGG_WORKER=<id>'
nolit "the trailer placeholder \`FSGG-Worker: <the id claim printed>\` is not flagged" \
      'FSGG-Worker: <the id claim printed>'
nolit "a VARIABLE REFERENCE \`\$FSGG_WORKER\` is not an assignment, and is not flagged" \
      'echo "$FSGG_WORKER"'
nolit "passing the var through (\`FSGG_WORKER=\$FSGG_WORKER\`) is a substitution, not a literal" \
      'FSGG_WORKER=$FSGG_WORKER scripts/fsgg-coord whoami'
nolit "prose ABOUT the trailer (\`the \`FSGG-Worker:\` trailer\`) is not a literal" \
      'Commit with the `FSGG-Worker:` trailer that `claim` prints.'

# =============================================================================================
# 3. Rule 2 — exactly ONE mint idiom. Every rival is a finding.
# =============================================================================================
rival() {  # rival <name> <line> <needle>
  local d="$WORK/rival-$RANDOM$RANDOM"; mksurface "$d" >/dev/null
  printf '\n```sh\n%s\n```\n' "$2" >> "$d/docs/protocol.md"
  expect "$1" 1 "$3" "$d"
}
rival "a hand-rolled \`/dev/urandom\` mint is caught" \
      'export FSGG_WORKER="w-$(od -An -tx1 -N4 /dev/urandom | tr -d " \\n")"' \
      "hand-rolled source of randomness"
rival "\`uuidgen\` is a second idiom, and is caught" \
      'export FSGG_WORKER="w-$(uuidgen | cut -c1-8)"' "hand-rolled source of randomness"
rival "\`openssl rand\` is a second idiom, and is caught" \
      'export FSGG_WORKER="w-$(openssl rand -hex 4)"' "hand-rolled source of randomness"
rival "\`\$RANDOM\` is a second idiom, and is caught" \
      'export FSGG_WORKER="w-$RANDOM"' "hand-rolled source of randomness"
rival "a rival COMMAND SUBSTITUTION is caught even when it uses no known randomness primitive" \
      'export FSGG_WORKER="w-$(date +%s)"' "assigned from a command substitution that is not the sanctioned mint"

# The sanctioned idiom itself must NEVER be flagged — that is the whole point of having one.
SANE="$(mksurface "$WORK/sane")"
printf '\n```sh\neval "$(scripts/fsgg-coord whoami --mint)"\n```\n' >> "$SANE/docs/protocol.md"
expect "the SANCTIONED mint is never flagged, however often it appears" \
  0 "ok: no literal worker id" "$SANE"

# =============================================================================================
# 4. Rule 3 — "exactly one" has a FLOOR. A surface that teaches no mint is a surface whose readers
#    will invent an id, which is #419 from the other end.
# =============================================================================================
NOMINT="$WORK/nomint"; mkdir -p "$NOMINT/.claude/skills/s" "$NOMINT/docs"
printf '.claude/skills\n' > "$NOMINT/.agent-skill-roots"
echo '# skill: set `FSGG_WORKER` per worker.' > "$NOMINT/.claude/skills/s/SKILL.md"
echo '# protocol: `$FSGG_WORKER` identifies a worker.' > "$NOMINT/docs/protocol.md"
expect "a surface that mentions the worker id but TEACHES NO MINT is a finding" \
  1 "no longer shows the sanctioned mint" "$NOMINT"

# =============================================================================================
# 5. Fail closed. "I could not check" is never green, and never a finding either (#266/#320).
# =============================================================================================
EMPTY="$WORK/empty"; mkdir -p "$EMPTY/.claude/skills" "$EMPTY/docs"
printf '.claude/skills\n' > "$EMPTY/.agent-skill-roots"
expect "a surface with NO markdown at all is exit 3, never a vacuous green" \
  3 "found NO Markdown files" "$EMPTY"

# The non-vacuity guard: markdown that mentions no worker id AT ALL means the extractor is broken —
# these documents demonstrably carry them. Green here would make every other leg worthless.
SILENT="$WORK/silent"; mkdir -p "$SILENT/.claude/skills/s" "$SILENT/docs"
printf '.claude/skills\n' > "$SILENT/.agent-skill-roots"
echo '# a skill about nothing in particular' > "$SILENT/.claude/skills/s/SKILL.md"
echo '# a doc about nothing in particular'   > "$SILENT/docs/protocol.md"
expect "markdown that mentions NO worker id at all is exit 3 — the extractor is broken, not the tree" \
  3 "found NO mention of a worker id at all" "$SILENT"

MISSING="$WORK/missing"; mkdir -p "$MISSING/docs"
printf '.claude/skills\n' > "$MISSING/.agent-skill-roots"
echo 'x' > "$MISSING/docs/x.md"
expect "a DECLARED root that does not exist is exit 3 — a broken glob must never read as clean" \
  3 "does not exist under" "$MISSING"

NOROOTS="$WORK/noroots"; mkdir -p "$NOROOTS/docs"
printf '# only comments\n' > "$NOROOTS/.agent-skill-roots"
echo 'x' > "$NOROOTS/docs/x.md"
expect "an .agent-skill-roots that declares NO roots is exit 3" \
  3 "declares no roots" "$NOROOTS"

# =============================================================================================
# 6. THE DOGFOOD LEG. Without this, everything above is synthetic and the gate could pass its own
#    tests while the attractor ships in the real recipes.
# =============================================================================================
expect "the SHIPPED surface of this repo is clean — no literal id, exactly one mint idiom" \
  0 "ok: no literal worker id" "$REPO_ROOT"

# ...and the surface it audits really is the roots + docs, not a hardcoded guess.
if grep -q '^\.claude/skills$' "$REPO_ROOT/.agent-skill-roots" \
   && grep -q '^\.agents/skills$' "$REPO_ROOT/.agent-skill-roots"; then
  ok "the roots come from .agent-skill-roots (#517), so a root added there is audited without touching the gate"
else
  bad "the shipped .agent-skill-roots is not the shape this fixture assumes"
fi

echo
echo "worker-id-attractor fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::worker-id-attractor fixture FAILED"; exit 1; }
echo "worker-id-attractor fixture — OK"
