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
# ...and the ENGINE SOURCE, for rule 4 (#569). The remedy a worker runs is the one the TOOL prints,
# so the tool's own strings are part of the shipped surface — copying only the docs is the blind spot
# #569 lived in. `bin/`/`obj/` are excluded: build output is not a surface anybody edits.
mkdir -p "$REAL/src" "$REAL/scripts"
cp -r "$REPO_ROOT/src/FS.GG.Coord.Cli" "$REAL/src/"
rm -rf "$REAL/src/FS.GG.Coord.Cli/bin" "$REAL/src/FS.GG.Coord.Cli/obj"
cp "$REPO_ROOT/scripts/fsgg-coord" "$REAL/scripts/"

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

# (d) #569 — THE TOOL'S OWN REMEDY. Every doc said `scripts/fsgg-coord`; the ENGINE said
#     `fsgg-coord-engine`, which is on nobody's PATH. So the one mint idiom's own remedy was
#     `command not found`, printed to the one reader who is holding a warning and doing as they are
#     told. A gate over docs alone reported green through the whole life of the bug — the same shape
#     as (b), one level down: (b) was the doc the skills project FROM, this is the TOOL they describe.
REAL_D="$WORK/real-d"; cp -r "$REAL" "$REAL_D"
sed -i 's|eval \\"\$(scripts/fsgg-coord whoami --mint)\\"|eval \\"$(fsgg-coord-engine whoami --mint)\\"|g' \
  "$REAL_D/src/FS.GG.Coord.Cli/Client.fs"
expect "REGRESSION #569: the ENGINE printing a remedy that is not on PATH is caught" \
  1 "which is not on PATH" "$REAL_D"
expect "REGRESSION #569: and the finding names the ENGINE source, not just the docs" \
  1 "src/FS.GG.Coord.Cli/Client.fs" "$REAL_D"
expect "REGRESSION #569: and it names the command that DOES run, so the fix is on the page" \
  1 "eval \"\$(scripts/fsgg-coord whoami --mint)\"" "$REAL_D"

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

# A NON-HEX id is every bit as pasteable, and every bit as collidable. #551's acceptance grep
# (`FSGG_WORKER=[a-z]*-[0-9a-f]`) would have missed all three of these — matching that grep's blind
# spot rather than the rule it was reaching for is how a gate ships already-broken.
lit "a non-hex literal (\`w-alice\`) is caught — the grep in #551 would have missed it" \
    'export FSGG_WORKER=w-alice'
lit "a hyphenless literal (\`alice\`) is caught — an assignment's RHS is never prose" \
    'export FSGG_WORKER=alice'
lit "the attractor WORD itself, unsuffixed (\`finch\`), is caught — the word is the attractor (#419)" \
    'export FSGG_WORKER=finch'

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

# ONE mistake must produce ONE finding. `FSGG_WORKER="w-$(od -An …)"` trips both the randomness rule
# and the rival-substitution rule; reporting it twice trains the reader to skim the gate's output.
DUP="$(mksurface "$WORK/dup")"
printf '\n```sh\nexport FSGG_WORKER="w-$(od -An -tx1 -N4 /dev/urandom)"\n```\n' >> "$DUP/docs/protocol.md"
n=$(python3 "$GATE" --root "$DUP" 2>&1 | grep -c '::error::check-worker-id-attractor: docs/protocol.md' || true)
if [ "$n" = "1" ]; then
  ok "one bad line yields ONE finding, not two — the gate does not train you to skim it"
else
  bad "one bad line yielded $n findings, want 1"
fi

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
# 4b. Rule 4 — the remedy the TOOL prints must RUN as printed (#569).
# =============================================================================================
# A synthetic engine, so these legs test the RULE rather than today's tree.
mkengine() {  # mkengine <surface-dir> <remedy-line>
  local d="$1"
  mkdir -p "$d/src/FS.GG.Coord.Cli"
  printf 'let warn () =\n    eprint "  Mint one (do NOT invent one):  %s"\n' "$2" \
    > "$d/src/FS.GG.Coord.Cli/Client.fs"
  echo "$d"
}

ENG_BAD="$(mksurface "$WORK/eng-bad")"
mkengine "$ENG_BAD" 'eval \"$(fsgg-coord-engine whoami --mint)\"' >/dev/null
expect "an engine printing \`fsgg-coord-engine\` — not on PATH — is a finding" \
  1 "which is not on PATH" "$ENG_BAD"

ENG_BARE="$(mksurface "$WORK/eng-bare")"
mkengine "$ENG_BARE" 'eval \"$(fsgg-coord whoami --mint)\"' >/dev/null
expect "a BARE \`fsgg-coord\` is caught too — it is not on PATH either, which is #569 as filed" \
  1 "naming \`fsgg-coord\`" "$ENG_BARE"

# The rule is about the COMMAND, not about the `eval "$(…)"` wrapper around it. Keying on the wrapper
# is the obvious way to write this and it fails OPEN: the same `command not found`, minus the eval,
# sails through — and the summary then reports N remedies "run as printed" while one of them does
# not. Caught reviewing this change; the first draft had exactly this hole.
ENG_NOEVAL="$(mksurface "$WORK/eng-noeval")"
mkengine "$ENG_NOEVAL" 'run: fsgg-coord-engine whoami --mint' >/dev/null
expect "a remedy printed WITHOUT the eval wrapper is caught — the command is the subject, not the wrapper" \
  1 "which is not on PATH" "$ENG_NOEVAL"

ENG_OK="$(mksurface "$WORK/eng-ok")"
mkengine "$ENG_OK" 'eval \"$(scripts/fsgg-coord whoami --mint)\"' >/dev/null
expect "the RESOLVER path — the one spelling that runs from a plain checkout — is never flagged" \
  0 "ok: no literal worker id" "$ENG_OK"

# Prose ABOUT the ritual is not a line anybody pastes, and flagging it would be the gate crying wolf
# at the sentence that teaches the rule — the carve-out `is_literal_trailer` already makes.
#
# Each of these carries a REAL remedy as well as the prose, so the engine non-vacuity guard is
# satisfied and the leg tests what it claims to. Prose alone is exit 3 (nothing to audit), which is
# correct and is asserted separately below — an earlier draft of this leg omitted the real remedy and
# "passed" on a tree the gate had never looked at.
ell() {  # ell <name> <prose-line>
  local d; d="$(mksurface "$WORK/ell-$RANDOM$RANDOM")"
  mkengine "$d" 'eval \"$(scripts/fsgg-coord whoami --mint)\"' >/dev/null
  printf '\n/// %s is the whole ritual.\n' "$2" >> "$d/src/FS.GG.Coord.Cli/Client.fs"
  expect "$1" 0 "ok: no literal worker id" "$d"
}
ell "a UNICODE ellipsis in a doc comment is prose, not a remedy — not flagged" \
    'eval "$(… whoami --mint)"'
ell "an ASCII \`...\` placeholder is prose too — not flagged" \
    'eval "$(... whoami --mint)"'
ell "prose naming NO command (\`whoami --mint\` prints one line) is not a remedy — not flagged" \
    '`whoami --mint`'

# Build output carries a copy of every doc comment. A finding there is one nobody can act on: you
# cannot fix a generated file, and the regeneration would put it straight back.
ENG_GEN="$(mksurface "$WORK/eng-gen")"
mkengine "$ENG_GEN" 'eval \"$(scripts/fsgg-coord whoami --mint)\"' >/dev/null
mkdir -p "$ENG_GEN/src/FS.GG.Coord.Cli/obj/Debug" "$ENG_GEN/src/FS.GG.Coord.Cli/bin/Release"
printf 'eval "$(fsgg-coord-engine whoami --mint)"\n' > "$ENG_GEN/src/FS.GG.Coord.Cli/obj/Debug/g.fs"
printf 'eval "$(fsgg-coord-engine whoami --mint)"\n' > "$ENG_GEN/src/FS.GG.Coord.Cli/bin/Release/g.fs"
expect "BUILD OUTPUT (bin/, obj/) is not audited — a finding nobody can fix is noise, not a gate" \
  0 "ok: no literal worker id" "$ENG_GEN"

# The gate must NAME the broken idiom to forbid it. Reding on its own docstring would be the gate
# failing its own rule by stating it — and it must stay exempt when audited as a COPY, or the fixture
# above red-lights a verbatim copy of a tree the gate calls green.
ENG_SELF="$(mksurface "$WORK/eng-self")"
mkengine "$ENG_SELF" 'eval \"$(scripts/fsgg-coord whoami --mint)\"' >/dev/null
mkdir -p "$ENG_SELF/scripts"; cp "$GATE" "$ENG_SELF/scripts/"
expect "the gate does not red on a COPY of ITSELF — it must quote the counter-example to forbid it" \
  0 "ok: no literal worker id" "$ENG_SELF"

# Fail closed on rule 4's own subject. The engine PRINTS these remedies; finding none means the glob,
# the suffix list, or the regex broke — and every leg above is then worthless rather than clean.
ENG_MUTE="$(mksurface "$WORK/eng-mute")"
mkdir -p "$ENG_MUTE/src/FS.GG.Coord.Cli"
echo 'let warn () = eprint "nothing about minting here"' > "$ENG_MUTE/src/FS.GG.Coord.Cli/Client.fs"
expect "an ENGINE that prints NO remedy at all is exit 3 — the extractor is broken, not the tree" \
  3 "found NO mint remedy in" "$ENG_MUTE"

# ...but a tree with no engine in it has genuinely nothing to audit, and must stay green. This is the
# line between "I looked and there is nothing" and "I could not look" (#266) — the doc-only fixtures
# above depend on it.
expect "a doc-only tree (no engine at all) is green — nothing there prints a remedy" \
  0 "ok: no literal worker id" "$CLEAN"

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

# THE REMEDY NAMES SOMETHING THAT IS ACTUALLY THERE. Rule 4 asserts every printed remedy names
# `scripts/fsgg-coord` — which is worth exactly nothing if `scripts/fsgg-coord` is not a runnable
# command. That is #569's whole complaint (the remedy named a program that does not exist), so a gate
# that swapped one absent name for another would satisfy every leg above and still ship the bug.
if [ -x "$REPO_ROOT/scripts/fsgg-coord" ]; then
  ok "the command every remedy names (\`scripts/fsgg-coord\`) exists and is executable — #569's actual ask"
else
  bad "scripts/fsgg-coord is not an executable file — the sanctioned remedy names a program that is not there"
fi

# ...and a BARE `fsgg-coord` is on nobody's PATH, which is WHY the remedy must name the path.
#
# THIS LEG ALSO ASKED ABOUT `fsgg-coord-engine`, AND THAT WAS NEVER RULE 4'S BUSINESS (#1018). Rule 4
# governs the NAME a remedy prints — `scripts/fsgg-coord`, because a bare `fsgg-coord` is `command not
# found` (#569). No remedy anywhere prints `fsgg-coord-engine`: it is the ENGINE the shim execs, not a
# name any doc tells you to type, so whether it is on PATH cannot make a printed remedy resolve or fail.
# The leg red-flagged the receivers' DOCUMENTED shape (`dotnet tool install -g` — the shim's own tier 3,
# and the first remedy its `die()` prints) as a violation of a rule that shape does not touch. That is
# why it fired on developer machines and never in CI, and why it read as noise: for rule 4, it WAS noise.
#
# It was also, by accident, the only thing in this repo pointing at a REAL hazard — a global engine
# preempting `.github`'s authoritative source build, which falsely closed epic #889 (#1018) and made the
# shim's own fixtures post 2 live claims (#1008). That hazard is now fixed at the root: the shim resolves
# the source build ABOVE any packaged engine, and `tests/coord-engine-parity/shim.sh` §4 pins it directly
# and hermetically — it puts a tool on PATH ITSELF rather than waiting to notice one, so it holds on a CI
# runner where this leg could never fire. The canary is not lost; it is replaced by a test of the thing it
# was accidentally sensing, and this leg goes back to asking only its own question.
if command -v fsgg-coord >/dev/null 2>&1; then
  bad "a bare \`fsgg-coord\` is on PATH — rule 4 assumes it is not; revisit #569 rather than skip this"
else
  ok "no bare \`fsgg-coord\` is on PATH — so a bare name in a remedy really is \`command not found\` (#569)"
fi

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
