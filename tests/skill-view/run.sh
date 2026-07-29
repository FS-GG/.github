#!/usr/bin/env bash
# Fixture for scripts/skill-view — .github#1635 (ADR-0067 phase 2), epic .github#1611.
#
# THE FAILURE LEGS ARE THE POINT, AND THIS TIME THEY ARE THE ACCEPTANCE CRITERION ITSELF.
# ADR-0067 §8 forbids shipping the generated view without a LOUD absence check, in these words:
# *"A rewrite that removes the loud failure and adds the quiet one is worse than no rewrite."*
# Phase 1 (.github#1621 §1 Q4, §2) measured the three modes that make it necessary — an ABSENT root,
# a DANGLING symlink, and a `core.symlinks=false` checkout of a COMMITTED symlink — as **exit 0 with
# no diagnostic in BOTH runtimes** (Claude Code 2.1.220, Codex CLI 0.145.0). Legs 4, 5 and 6 below are
# those three modes, one leg each, asserted RED. If any of them ever goes green, the rewrite has
# re-introduced the silence it exists to remove, and this suite is the only thing that would say so.
#
# EVERY "MUST FAIL" LEG ASSERTS THE EXIT CODE **AND** THE REASON CLASS. tests/feed-coherence/run.sh:10
# names the trap this closes: a must-fail leg whose non-zero exit came from a path guard, a typo in the
# invocation, or a missing dependency would pass against a subject broken in a completely different
# way. So each red leg matches the bracketed class the checker prints — `[absent-root]`,
# `[dangling-root]`, `[text-file-root]` — and a leg that reds for the wrong reason FAILS.
#
# THE WINDOWS LEG IS A REAL `git -c core.symlinks=false clone`, NOT A HAND-BUILT LOOKALIKE.
# tests/coord-engine-parity/shim.sh §3e records why that distinction is not fastidiousness: a
# lookalike certifies the path the real tool never takes. A regular file containing `../canonical/skills`
# is trivial to `printf`, and it would prove only that the checker can read a file. What phase 1
# actually measured is git's own checkout behaviour, so this suite reproduces it with git — including
# the index mode 120000 the checker uses as its strongest tell.
#
# ANTI-VACUITY. The leg count is asserted at the end (tests/skill-union/skillmirror-conformance.sh's
# rule). A suite that silently ran three of sixteen legs and printed "0 failed" is the shape epic #266
# exists to refuse, and it is reachable here by one `set -e` in the wrong place.
#
# OFFLINE, NO NETWORK, NO `gh`. Only git, bash, python3 and this repo's own scripts.

set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
SV="$ROOT/scripts/skill-view"
UNION="$ROOT/scripts/skill-union-assert.sh"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/skill-view-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

export GIT_CONFIG_GLOBAL="$WORK/gitconfig"
export GIT_CONFIG_NOSYSTEM=1
: > "$GIT_CONFIG_GLOBAL"

pass=0
failcount=0
legs=0
skipped=0
ok()  { printf 'PASS  %s\n' "$1"; pass=$((pass + 1)); }
bad() {
  printf 'FAIL  %s\n' "$1"
  [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'
  failcount=$((failcount + 1))
}
leg() { legs=$((legs + 1)); }

# expect <label> <want-exit> <want-substring> -- <argv...>
# The two halves are asserted together on purpose; see the header.
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

# expect_silent <label> -- <argv...>
# Exit 0 AND NOT ONE BYTE on either stream. `expect "$label" 0 ""` would NOT do: .github#1700's whole
# defect was an exit 0 that said nothing, so a leg asserting only the exit code passes against the
# exact bug it exists to catch. Silence is the assertion here, not a side effect of one.
expect_silent() {
  local label="$1"; shift
  [ "$1" = "--" ] && shift
  leg
  local out rc=0
  out="$("$@" 2>&1)" || rc=$?
  if [ "$rc" -ne 0 ]; then
    bad "$label — exit $rc, want 0" "$out"
    return
  fi
  if [ -n "$out" ]; then
    bad "$label — exit 0 as wanted, but it PRINTED (silence was the assertion)" "$out"
    return
  fi
  ok "$label"
}

# ---------------------------------------------------------------------------------------------
# make_tree <dir> [<n-skills>] — a repository whose ONE source of truth is canonical/skills, whose
# roots are DECLARED (so the suite exercises lib/roots.sh's real precedence rather than a default),
# and whose generated roots are git-ignored (ADR-0067's "nothing it emits is tracked").
# ---------------------------------------------------------------------------------------------
make_tree() {
  local dir="$1" n="${2:-3}" i id
  mkdir -p "$dir/canonical/skills"
  for i in $(seq 1 "$n"); do
    id="skill-$i"
    mkdir -p "$dir/canonical/skills/$id"
    printf -- '---\nname: %s\ndescription: fixture skill %s.\n---\n\nBody of %s.\n' "$id" "$i" "$id" \
      > "$dir/canonical/skills/$id/SKILL.md"
  done
  printf '.claude/skills\n.agents/skills\n' > "$dir/.agent-skill-roots"
  printf '/.claude/skills\n/.agents/skills\n' > "$dir/.gitignore"
  git init -q "$dir"
  git -C "$dir" add -A
  git -C "$dir" -c user.email=f@fixture -c user.name=fixture commit -qm "fixture source"
}

# make_endstate_tree <dir> — ADR-0067 §5's shape: `.agents/skills` IS the source (tracked, canonical,
# because Codex discovers it with no configuration) and `.claude/skills` is the generated view of it.
# The root set still declares BOTH, because §5's flip to two roots is a separate change (.github#1636)
# and this phase does not anticipate it.
make_endstate_tree() {
  local dir="$1" i id
  mkdir -p "$dir/.agents/skills"
  for i in 1 2 3; do
    id="skill-$i"
    mkdir -p "$dir/.agents/skills/$id"
    printf -- '---\nname: %s\ndescription: fixture skill %s.\n---\n\nBody of %s.\n' "$id" "$i" "$id" \
      > "$dir/.agents/skills/$id/SKILL.md"
  done
  printf '.claude/skills\n.agents/skills\n' > "$dir/.agent-skill-roots"
  printf '/.claude/skills\n' > "$dir/.gitignore"
  git init -q "$dir"
  git -C "$dir" add -A
  git -C "$dir" -c user.email=f@fixture -c user.name=fixture commit -qm "canonical .agents/skills"
}

# =============================================================================================
# 1 — GREEN: the generated view, and it leaves the tree CLEAN (ADR-0067 §6 / AC1)
# =============================================================================================
T1="$WORK/green-auto"; make_tree "$T1"
expect "1a generate (auto) succeeds and self-verifies" 0 "skill-view check: OK" \
  -- bash "$SV" generate --source "$T1/canonical/skills" --tree "$T1"

leg
if [ -L "$T1/.claude/skills" ] && [ -d "$T1/.claude/skills" ]; then
  ok "1b auto mode linked on this platform, and the link resolves"
else
  bad "1b expected a resolving symlink at .claude/skills under --mode auto" "$(ls -la "$T1/.claude" 2>&1)"
fi

leg
status="$(git -C "$T1" status --porcelain)"
if [ -z "$status" ]; then
  ok "1c git status --porcelain is EMPTY after generate — nothing it emits is tracked or reported"
else
  bad "1c git status is not empty after generate" "$status"
fi

expect "1d check alone is green over the generated view" 0 "3 declared skill(s) visible in every one of 2 root(s)" \
  -- bash "$SV" check --source "$T1/canonical/skills" --tree "$T1"

expect "1e generate is idempotent — a second run over its own view is green" 0 "skill-view check: OK" \
  -- bash "$SV" generate --source "$T1/canonical/skills" --tree "$T1"

# =============================================================================================
# 2 — GREEN: copy mode. Windows cannot link; the choice must not be visible to the check.
# =============================================================================================
T2="$WORK/green-copy"; make_tree "$T2"
expect "2a generate --mode copy succeeds and self-verifies" 0 "generated (copy)" \
  -- bash "$SV" generate --source "$T2/canonical/skills" --tree "$T2" --mode copy

leg
if [ ! -L "$T2/.claude/skills" ] && [ -f "$T2/.claude/skills/skill-1/SKILL.md" ]; then
  ok "2b copy mode produced real files, not a link"
else
  bad "2b expected a real directory of files at .claude/skills under --mode copy" "$(ls -la "$T2/.claude" 2>&1)"
fi

leg
status="$(git -C "$T2" status --porcelain)"
if [ -z "$status" ]; then
  ok "2c git status --porcelain is EMPTY after a COPY-mode generate too"
else
  bad "2c git status is not empty after copy-mode generate" "$status"
fi

expect "2d check is green over the copied view — the mechanism is invisible to it" 0 "skill-view check: OK" \
  -- bash "$SV" check --source "$T2/canonical/skills" --tree "$T2"

# =============================================================================================
# 3 — THE END-STATE LAYOUT, AND WHAT `skill-union-assert.sh` ACTUALLY DOES OVER IT (AC4).
#
#     ADR-0067 §5's end state makes ONE root the canonical source — `.agents/skills`, tracked,
#     because Codex reads it with no configuration — and the other root a generated view of it.
#     `generate` must therefore treat the source root as an IDENTITY (nothing to do) rather than as
#     something to replace, and the existing gate must stay green over the result with NO code
#     change. Legs 3a-3c assert exactly that.
#
#     LEG 3d IS A KNOWN LIMITATION, PINNED IN BOTH DIRECTIONS (the house form —
#     tests/skill-union/skillmirror.fixtures.json's `KNOWN-DIVERGENCE-*` vectors). ADR-0067's
#     Consequences say `skill-union-assert` "needs no code change to run alongside the resolved
#     layout". MEASURED, that holds only while at least ONE declared root is a real directory:
#     `union_ids()` at scripts/skill-union-assert.sh:456 runs `find "$PRODUCT/$r" -mindepth 1
#     -maxdepth 1 -type d` with no `-L`, and POSIX `find` does not dereference a starting point that
#     is a symlink — so a root that IS a view contributes ZERO ids. Phase 1's demonstration passed
#     because its `.agents/skills` was real. A layout where EVERY root is a view reports exit 2,
#     "no skills found under any root". It fails CLOSED, so it is loud rather than dangerous — but a
#     required gate that reds on a correct tree is a phase-3 landmine, and it is filed rather than
#     worked around here (ADR-0067 §9 forbids this phase from touching that gate).
#     Asserting it in both directions is what stops it either vanishing or growing unnoticed: the day
#     `find` gains its `-L`, THIS leg goes red and points at the record to amend.
# =============================================================================================
T3="$WORK/endstate"; make_endstate_tree "$T3"
expect "3a generate over the END-STATE layout: the source root is an IDENTITY, not a replacement" 0 "identity (this root IS the source)" \
  -- bash "$SV" generate --source "$T3/.agents/skills" --tree "$T3"

leg
status="$(git -C "$T3" status --porcelain)"
if [ -z "$status" ] && [ -d "$T3/.claude/skills/skill-1" ] && [ -f "$T3/.agents/skills/skill-1/SKILL.md" ]; then
  ok "3b the tracked canonical root survived, the view exists, and git status is EMPTY"
else
  bad "3b end-state layout is not what generate promised" "status=[$status]$(ls -la "$T3/.claude" "$T3/.agents" 2>&1)"
fi

expect "3c skill-union-assert (UNCHANGED) is green over the end-state two-root layout" 0 "OK — all roots hold the byte-identical union" \
  -- bash "$UNION" --product "$T3" --roots ".claude/skills .agents/skills"

expect "3d KNOWN LIMITATION — skill-union-assert over an ALL-VIEW layout sees no skills (find, no -L)" 2 "no skills found under any root" \
  -- bash "$UNION" --product "$T1" --roots ".claude/skills .agents/skills"

expect "3e ...and a COPIED all-view layout is green, which is what isolates the cause to the symlink" 0 "OK — all roots hold the byte-identical union" \
  -- bash "$UNION" --product "$T2" --roots ".claude/skills .agents/skills"

# =============================================================================================
# 4 — RED: ABSENT ROOT. Phase 1: exit 0, no diagnostic, in both runtimes.
# =============================================================================================
T4="$WORK/absent"; make_tree "$T4"
bash "$SV" generate --source "$T4/canonical/skills" --tree "$T4" >/dev/null 2>&1
rm -rf "$T4/.claude/skills"
expect "4a absent root is LOUD" 1 "[absent-root]" \
  -- bash "$SV" check --source "$T4/canonical/skills" --tree "$T4"
expect "4b absent root exits 1, and says which root" 1 ".claude/skills does not exist" \
  -- bash "$SV" check --source "$T4/canonical/skills" --tree "$T4"

# =============================================================================================
# 5 — RED: DANGLING SYMLINK ROOT. Phase 1: exit 0, no diagnostic, in both runtimes.
# =============================================================================================
T5="$WORK/dangling"; make_tree "$T5"
bash "$SV" generate --source "$T5/canonical/skills" --tree "$T5" >/dev/null 2>&1
rm -f "$T5/.claude/skills"
ln -s ../canonical/nowhere "$T5/.claude/skills"
expect "5a dangling symlink root is LOUD, and is NOT called absent" 1 "[dangling-root]" \
  -- bash "$SV" check --source "$T5/canonical/skills" --tree "$T5"

# =============================================================================================
# 6 — RED: THE WINDOWS TEXT-FILE SYMLINK, reproduced with a REAL `core.symlinks=false` clone.
#     ADR-0067 §6: a committed symlink does not degrade there, it silently evaporates.
# =============================================================================================
T6SRC="$WORK/winsrc"; make_tree "$T6SRC"
# A COMMITTED symlink — the form ADR-0067 §6 rejects and phase 1's own demo used.
rm -f "$T6SRC/.gitignore"
ln -s ../canonical/skills "$T6SRC/.claude/skills" 2>/dev/null || { mkdir -p "$T6SRC/.claude"; ln -s ../canonical/skills "$T6SRC/.claude/skills"; }
mkdir -p "$T6SRC/.agents"
ln -s ../canonical/skills "$T6SRC/.agents/skills"
git -C "$T6SRC" add -A
git -C "$T6SRC" -c user.email=f@fixture -c user.name=fixture commit -qm "committed symlink roots"

leg
mode="$(git -C "$T6SRC" ls-files --stage -- .claude/skills | awk '{print $1}')"
if [ "$mode" = "120000" ]; then
  ok "6a the fixture really did commit a symlink (index mode 120000)"
else
  bad "6a expected index mode 120000 for the committed root, got '$mode'" ""
fi

T6="$WORK/winclone"
git -c core.symlinks=false clone -q "$T6SRC" "$T6"

leg
if [ -f "$T6/.claude/skills" ] && [ ! -L "$T6/.claude/skills" ]; then
  ok "6b core.symlinks=false checked the root out as a regular FILE ($(wc -c <"$T6/.claude/skills") bytes)"
else
  bad "6b expected a regular file at .claude/skills in the core.symlinks=false clone" "$(ls -la "$T6/.claude" 2>&1)"
fi

expect "6c the Windows text-file root is LOUD, and is named for what it is" 1 "[text-file-root]" \
  -- bash "$SV" check --source "$T6/canonical/skills" --tree "$T6"
expect "6d ...and the diagnostic names core.symlinks=false, not a missing producer" 1 "core.symlinks=false" \
  -- bash "$SV" check --source "$T6/canonical/skills" --tree "$T6"

# =============================================================================================
# 7 — RED: the root is fine and a DECLARED SKILL is not there. The subset failure (.github#1504)
#     one layer down: a view that resolves is not a view that is complete.
# =============================================================================================
T7="$WORK/partial"; make_tree "$T7"
bash "$SV" generate --source "$T7/canonical/skills" --tree "$T7" --mode copy >/dev/null 2>&1
rm -rf "$T7/.claude/skills/skill-2"
expect "7a a declared skill missing from one root is LOUD" 1 "[missing-skill]" \
  -- bash "$SV" check --source "$T7/canonical/skills" --tree "$T7"

# =============================================================================================
# 8 — RED: a PER-SKILL dangling link. Phase 1 measured per-skill links as resolvable in both
#     runtimes, so a broken one is the same silence one level finer.
# =============================================================================================
T8="$WORK/perskill"; make_tree "$T8"
bash "$SV" generate --source "$T8/canonical/skills" --tree "$T8" --mode copy >/dev/null 2>&1
rm -f "$T8/.claude/skills/skill-3/SKILL.md"
ln -s ../../../canonical/skills/gone/SKILL.md "$T8/.claude/skills/skill-3/SKILL.md"
expect "8a a dangling per-skill link is LOUD, and is not called missing" 1 "[dangling-skill]" \
  -- bash "$SV" check --source "$T8/canonical/skills" --tree "$T8"

# =============================================================================================
# 9 — RED: present but UNREADABLE, and present but EMPTY. A runtime that cannot read a skill has
#     no skill; "the file is there" is not the question anyone is asking.
# =============================================================================================
T9="$WORK/unreadable"; make_tree "$T9"
bash "$SV" generate --source "$T9/canonical/skills" --tree "$T9" --mode copy >/dev/null 2>&1
: > "$T9/.claude/skills/skill-1/SKILL.md"
expect "9a an EMPTY SKILL.md is LOUD" 1 "[empty-skill]" \
  -- bash "$SV" check --source "$T9/canonical/skills" --tree "$T9"

leg
if [ "$(id -u)" -eq 0 ]; then
  # SKIPS ARE COUNTED AND PRINTED. A silently skipped leg and a passing leg are indistinguishable
  # from the summary line, which is the whole failure this repo keeps re-finding (epic #266).
  printf 'SKIP  9b unreadable SKILL.md — running as uid 0, where chmod 000 is not a permission\n'
  skipped=$((skipped + 1))
else
  chmod 000 "$T9/.claude/skills/skill-2/SKILL.md"
  out="$(bash "$SV" check --source "$T9/canonical/skills" --tree "$T9" 2>&1)"; rc=$?
  chmod 644 "$T9/.claude/skills/skill-2/SKILL.md"
  if [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -qF -- "[unreadable-skill]"; then
    ok "9b an UNREADABLE SKILL.md is LOUD"
  else
    bad "9b expected exit 1 with [unreadable-skill], got exit $rc" "$out"
  fi
fi

# =============================================================================================
# 10 — EXIT 2, NOT 1: the checker refuses to reach a verdict it cannot support. These are the
#      vacuity guards, and they are the reason a green run from this tool means anything.
# =============================================================================================
expect "10a check with no expected set REFUSES (exit 2), it does not pass over nothing" 2 "Deriving the expected set from the roots" \
  -- bash "$SV" check --tree "$T1"

T10="$WORK/emptysrc"; make_tree "$T10"
mkdir -p "$T10/empty"
expect "10b an EMPTY expected set is exit 2, never a pass" 2 "expected skill set is EMPTY" \
  -- bash "$SV" check --source "$T10/empty" --tree "$T10"

expect "10c generate refuses an EMPTY source" 2 "refusing to generate an EMPTY view" \
  -- bash "$SV" generate --source "$T10/empty" --tree "$T10"

printf 'not json at all\n' > "$T10/broken.json"
expect "10d an UNREADABLE --manifest is exit 2, not an empty expected set" 2 "could not read --manifest" \
  -- bash "$SV" check --manifest "$T10/broken.json" --tree "$T10"

printf '{"schemaVersion":2,"skills":[]}\n' > "$T10/nothing.json"
expect "10e a manifest that declares NOTHING is exit 2 — 'declares nothing' is not 'all visible'" 2 "expected skill set is EMPTY" \
  -- bash "$SV" check --manifest "$T10/nothing.json" --tree "$T10"

# =============================================================================================
# 11 — ADR-0067 §9: THIS TOOL RETIRES NOTHING. A generator able to clobber a TRACKED root would
#      execute phase 4 by accident, in a repo whose CI is green, on a run nobody read.
# =============================================================================================
#      THE TRACKED ROOT HERE ALSO CARRIES A `.skill-view` RECEIPT, AND THAT IS THE POINT. Without it
#      the "not mine to replace" guard would catch the same case one line later, and this leg would
#      pass while the guard it names did nothing — the must-fail-for-the-wrong-reason trap
#      tests/feed-coherence/run.sh:10 warns about. With the receipt present, the tracked-root refusal
#      is the ONLY thing standing between the generator and committed skills, so removing it makes
#      leg 11b red rather than leaving this suite green.
T11="$WORK/tracked"
make_tree "$T11"
rm -f "$T11/.gitignore"
mkdir -p "$T11/.claude/skills/already-committed"
printf -- '---\nname: already-committed\n---\nbody\n' > "$T11/.claude/skills/already-committed/SKILL.md"
printf 'a receipt this tree committed\n' > "$T11/.claude/skills/.skill-view"
git -C "$T11" add -A
git -C "$T11" -c user.email=f@fixture -c user.name=fixture commit -qm "a committed root"
expect "11a generate REFUSES a git-tracked root" 2 "git TRACKS content there" \
  -- bash "$SV" generate --source "$T11/canonical/skills" --tree "$T11"

leg
if [ -f "$T11/.claude/skills/already-committed/SKILL.md" ]; then
  ok "11b ...and the committed skills are still there afterwards"
else
  bad "11b the refused generate deleted committed content" ""
fi

T11B="$WORK/foreign"; make_tree "$T11B"
mkdir -p "$T11B/.claude/skills/somebody-elses"
expect "11c generate REFUSES a pre-existing directory it did not create" 2 "carries no .skill-view receipt" \
  -- bash "$SV" generate --source "$T11B/canonical/skills" --tree "$T11B"

# =============================================================================================
# 12 — the root set is NOT this tool's to invent: it comes from lib/roots.sh, the same parser
#      skill-union-assert and coordination-sync resolve with (.github#525).
# =============================================================================================
expect "12a a declared .agent-skill-roots is what gets checked" 0 "(from .agent-skill-roots)" \
  -- bash "$SV" check --source "$T1/canonical/skills" --tree "$T1"

expect "12b --roots overrides the declaration, and the banner says so" 0 "(from --roots)" \
  -- bash "$SV" check --source "$T1/canonical/skills" --tree "$T1" --roots ".claude/skills"

expect "12c a root the declaration names but the tree lacks is still LOUD under --roots" 1 "[absent-root]" \
  -- bash "$SV" check --source "$T1/canonical/skills" --tree "$T1" --roots ".claude/skills .codex/skills"

# =============================================================================================
# 13 — the two lanes `check` took over from seven hand-written receiver alarms (.github#1710).
#
#      THE BEHAVIOUR OF THE LANES IS DEMONSTRATED BY `skill-view selftest`, WHICH IS ITSELF A LEG
#      HERE (13a) — the fixture trees, the can-fire cases and the per-class assertions all live in
#      the tool, so a receiver runs the same demonstration this suite does rather than a copy of it.
#      That is the whole point of the collapse: the previous arrangement had seven demos of one
#      invariant, two of which were missing the lane they were supposed to demonstrate.
#
#      What is asserted HERE and not there is the CLI CONTRACT — the refusals that keep the new
#      flags from being used in a way that quietly grades nothing. A carve-out reachable by accident
#      is a carve-out that will be reached by accident.
# =============================================================================================
expect "13a every lane of check demonstrates it can fire (skill-view selftest)" 0 "0 failed" \
  -- bash "$SV" selftest

T13="$WORK/t13"; make_tree "$T13" 2
printf '<Project>\n  <FsggKitSkillRoots>.claude/skills</FsggKitSkillRoots>\n  <FsggKitViewSkillRoots>.agents/skills</FsggKitViewSkillRoots>\n</Project>\n' > "$T13/good.proj"
printf '<Project>\n  <FsggKitSkillRoots>.claude/skills</FsggKitSkillRoots>\n</Project>\n' > "$T13/nodecl.proj"

# The membership lane end-to-end from this harness, so the suite does not depend solely on the
# tool's own account of itself.
expect "13b a receiver project that drops the view root is LOUD, and names the declaration" 1 "[roots-declaration]" \
  -- bash "$SV" check --tree "$T13" --source "$T13/canonical/skills" --receiver-proj "$T13/nodecl.proj"

# The declaration is graded BEFORE the roots are resolved, and a wrong one stops the run: reporting
# "0 violations" over a root set the tree does not declare is the vacuous pass wearing this lane's
# clothes.
expect "13c ...and it does NOT go on to report on a root set the tree never declared" 1 "refusing to go on" \
  -- bash "$SV" check --tree "$T13" --source "$T13/canonical/skills" --receiver-proj "$T13/nodecl.proj"

# --absent-ok is a MEASURED carve-out or it is nothing. need_val rejects the empty value, so the
# flag cannot be spelled in a way that excuses an absent root without saying what covers it.
expect "13d --absent-ok with no reason is REFUSED, not silently accepted" 2 "needs a value" \
  -- bash "$SV" check --tree "$T13" --source "$T13/canonical/skills" --receiver-proj "$T13/good.proj" --absent-ok ""

# The carve-out is scoped to VIEW roots, and only the receiver project says which those are.
expect "13e --absent-ok without --receiver-proj is REFUSED — nothing says which roots are views" 2 "which roots those are" \
  -- bash "$SV" check --tree "$T13" --source "$T13/canonical/skills" --absent-ok "no declaration to scope this to"

# Grading one root set and resolving another is the two-sources-of-truth bug lib/roots.sh exists to
# have ended (#525), arriving through a new flag.
expect "13f --roots alongside --receiver-proj is REFUSED" 2 "would grade one set and resolve another" \
  -- bash "$SV" check --tree "$T13" --source "$T13/canonical/skills" --receiver-proj "$T13/good.proj" --roots ".claude/skills"

# `generate` now reads this same declaration rather than accepting a hand-copied source/root pair.
# Start with the live root only, precisely as a receiver checkout does before its generate target.
T13G="$WORK/t13-generate"; make_tree "$T13G" 2
mkdir -p "$T13G/.claude"
mv "$T13G/canonical/skills" "$T13G/.claude/skills"
printf '<Project>\n  <FsggKitSkillRoots>.claude/skills</FsggKitSkillRoots>\n  <FsggKitViewSkillRoots>.agents/skills</FsggKitViewSkillRoots>\n</Project>\n' > "$T13G/receiver.proj"

expect "13g generate follows --receiver-proj for both source and view roots" 0 "skill-view check: OK" \
  -- bash "$SV" generate --tree "$T13G" --receiver-proj "$T13G/receiver.proj"

expect "13h generate REFUSES --source alongside --receiver-proj" 2 "--source and --receiver-proj" \
  -- bash "$SV" generate --tree "$T13G" --receiver-proj "$T13G/receiver.proj" --source "$T13G/.claude/skills"

expect "13i generate REFUSES --roots alongside --receiver-proj" 2 "--roots and --receiver-proj" \
  -- bash "$SV" generate --tree "$T13G" --receiver-proj "$T13G/receiver.proj" --roots ".agents/skills"

# =============================================================================================
# 14 — THE CHECKOUT HALF (.github#1700). Everything above grades the TOOL; §8 requires the assertion
#      "at checkout AND in CI", and the checkout half is `.claude/hooks/skill-view-check.sh`. It had
#      a `[ -d "$DIR/.agents/skills" ] || exit 0` guard, and `-d` is FALSE for a dangling link, for a
#      `core.symlinks=false` text file, and for a non-directory — so the hook exited 0 in silence on
#      three of the five classes, which are exactly the three phase 1 measured as exit-0-and-silent
#      in both runtimes. The tool was loud over the very same tree. Legs 14c/14d/14e are that tree.
#
#      SILENCE IS ASSERTED AS PRECISELY AS LOUDNESS HERE. `expect_silent` demands zero output, and
#      leg 14j re-runs 14c's tree through a RECONSTRUCTION of the deleted guard: if 14c ever passes
#      for a reason other than the repair, 14j is what shows it, because the two legs disagree only
#      when the fix is real.
# =============================================================================================
HOOK="$ROOT/.claude/hooks/skill-view-check.sh"

# make_hook_tree <dir> — THIS repo's shape, because this hook is .github's own and is not shipped:
# two DECLARED roots that are committed mirrors of one another, `.agents/skills` canonical
# (ADR-0067 §5), and `scripts/skill-view` plus BOTH libraries present, since the hook execs the tool
# out of the tree it is judging rather than out of this checkout.
make_hook_tree() {
  local dir="$1" root i id
  for root in .claude/skills .agents/skills; do
    for i in 1 2; do
      id="skill-$i"
      mkdir -p "$dir/$root/$id"
      printf -- '---\nname: %s\ndescription: fixture skill %s.\n---\n\nBody of %s.\n' "$id" "$i" "$id" \
        > "$dir/$root/$id/SKILL.md"
    done
  done
  printf '.claude/skills\n.agents/skills\n' > "$dir/.agent-skill-roots"
  mkdir -p "$dir/scripts/lib"
  cp "$SV" "$dir/scripts/skill-view"
  cp "$ROOT/scripts/lib/args.sh" "$ROOT/scripts/lib/roots.sh" "$dir/scripts/lib/"
}

# Every leg below runs from THIS suite's working directory, never from inside the fixture tree. That
# is not incidental: `--source` resolves against the process's cwd while `--tree` resolves on its
# own, so a hook that did not `cd` first would grade one tree's roots against another tree's source.
H_OK="$WORK/hook-ok"; make_hook_tree "$H_OK"
expect "14a a coherent tree is green, and says so in ONE line" 0 "declared skill(s) visible in every one of" \
  -- env CLAUDE_PROJECT_DIR="$H_OK" bash "$HOOK"

H_ABS="$WORK/hook-canon-absent"; make_hook_tree "$H_ABS"; rm -rf "$H_ABS/.agents/skills"
expect_silent "14b a tree that simply has NO view root stays silent (.github#698)" \
  -- env CLAUDE_PROJECT_DIR="$H_ABS" bash "$HOOK"

H_DANG="$WORK/hook-canon-dangling"; make_hook_tree "$H_DANG"
rm -rf "$H_DANG/.agents/skills"; ln -s ../nowhere "$H_DANG/.agents/skills"
expect "14c a DANGLING view root is LOUD, and is not called absent" 1 "[dangling-root]" \
  -- env CLAUDE_PROJECT_DIR="$H_DANG" bash "$HOOK"

# The tool's classification of this class is already proven against a REAL `core.symlinks=false`
# clone by leg 6; what 14d adds is that the HOOK reaches it and relays the class rather than
# swallowing it, so the checked-out link BODY is the right subject here.
H_TXT="$WORK/hook-canon-textlink"; make_hook_tree "$H_TXT"
rm -rf "$H_TXT/.agents/skills"; printf '../../.claude/skills' > "$H_TXT/.agents/skills"
expect "14d a core.symlinks=false text-file view root is LOUD" 1 "[text-file-root]" \
  -- env CLAUDE_PROJECT_DIR="$H_TXT" bash "$HOOK"

H_NOTDIR="$WORK/hook-canon-notdir"; make_hook_tree "$H_NOTDIR"
rm -rf "$H_NOTDIR/.agents/skills"; printf 'not a link body\nand not a directory\n' > "$H_NOTDIR/.agents/skills"
expect "14e a NON-DIRECTORY view root is LOUD" 1 "[not-a-directory-root]" \
  -- env CLAUDE_PROJECT_DIR="$H_NOTDIR" bash "$HOOK"

# The excuse is scoped to ONE finding on ONE root — the same tree the deleted guard excused and no
# other. A repair that widened it into "absence is quiet" would red here, which is the point.
H_MIRR="$WORK/hook-mirror-absent"; make_hook_tree "$H_MIRR"; rm -rf "$H_MIRR/.claude/skills"
expect "14f an absent .claude/skills is STILL loud — the excuse did not widen" 1 "[absent-root]" \
  -- env CLAUDE_PROJECT_DIR="$H_MIRR" bash "$HOOK"

H_NONE="$WORK/hook-both-absent"; make_hook_tree "$H_NONE"
rm -rf "$H_NONE/.claude/skills" "$H_NONE/.agents/skills"
expect_silent "14g a tree carrying no skill apparatus at all stays silent" \
  -- env CLAUDE_PROJECT_DIR="$H_NONE" bash "$HOOK"

H_NOTOOL="$WORK/hook-no-tool"; make_hook_tree "$H_NOTOOL"; rm -f "$H_NOTOOL/scripts/skill-view"
expect_silent "14h a tree without the tool is not a tree with a finding" \
  -- env CLAUDE_PROJECT_DIR="$H_NOTOOL" bash "$HOOK"

# Neither root readable: the checker refuses BEFORE classifying anything, so no class can honestly be
# named. The hook says that, loudly, and does not invent one (.github#1858).
H_BOTHBAD="$WORK/hook-both-dangling"; make_hook_tree "$H_BOTHBAD"
rm -rf "$H_BOTHBAD/.claude/skills" "$H_BOTHBAD/.agents/skills"
ln -s ../nowhere "$H_BOTHBAD/.claude/skills"; ln -s ../nowhere "$H_BOTHBAD/.agents/skills"
expect "14i neither root readable is LOUD, and names that rather than guessing a class" 2 "it names no class for them" \
  -- env CLAUDE_PROJECT_DIR="$H_BOTHBAD" bash "$HOOK"

# ANTI-VACUITY FOR 14c: the deleted guard, reconstructed, over 14c's own tree.
cat > "$WORK/legacy-skill-view-check.sh" <<'LEGACY'
#!/usr/bin/env bash
set -uo pipefail
DIR="${CLAUDE_PROJECT_DIR:-.}"
[ -x "$DIR/scripts/skill-view" ] || exit 0
[ -d "$DIR/.agents/skills" ] || exit 0
out="$(bash "$DIR/scripts/skill-view" check --source .agents/skills --tree "$DIR" 2>&1)"
rc=$?
if [ "$rc" -eq 0 ]; then printf '%s\n' "$out" | tail -n 1; exit 0; fi
printf '%s\n' "$out" >&2
exit "$rc"
LEGACY
expect_silent "14j the DELETED -d guard is silent over 14c's tree — so 14c is not vacuous" \
  -- env CLAUDE_PROJECT_DIR="$H_DANG" bash "$WORK/legacy-skill-view-check.sh"

# The excuse is matched against a root NAME, and a name interpolated into a grep pattern is a REGEX.
# `.agents/skills` unescaped also matches `Xagents/skills`, so an absent root that is not the excused
# one bought silence — this item's own defect (a test that cannot tell two things apart) one layer
# down, in the single place where a false positive buys quiet. Declared root names are the subject
# here, so the leg declares one.
# `Xagents/skills` is DECLARED and never created. The assertion names the ROOT and not just the
# class, because both candidate roots emit `[absent-root]` and telling two names apart is this leg's
# entire point — matching the class alone would pass against the bug.
H_RE="$WORK/hook-regex-canon"; make_hook_tree "$H_RE"
printf 'Xagents/skills\n.agents/skills\n' > "$H_RE/.agent-skill-roots"
expect "14k an absent root that merely LOOKS like the excused one is not excused" 1 "[absent-root] Xagents/skills" \
  -- env CLAUDE_PROJECT_DIR="$H_RE" bash "$HOOK"

# An untagged exit 2 is NOT automatically a source refusal, and the hook's own sentence about one is
# a claim it has to earn. An empty roots declaration is a counterexample: the checker stops for a
# different reason entirely, and asserting "neither root could be read as the expected set" there
# would be a mechanism nobody measured (.github#1858). This leg asserts the relay AND the silence of
# that sentence, so a repair that emits it unconditionally reds here.
H_NOROOTS="$WORK/hook-empty-roots"; make_hook_tree "$H_NOROOTS"
: > "$H_NOROOTS/.agent-skill-roots"
leg
out14l="$(env CLAUDE_PROJECT_DIR="$H_NOROOTS" bash "$HOOK" 2>&1)"; rc14l=$?
if [ "$rc14l" -eq 0 ]; then
  bad "14l an empty roots declaration is LOUD, and claims no source refusal — exit $rc14l, want non-zero" "$out14l"
elif ! printf '%s' "$out14l" | grep -qF -- "declares no roots"; then
  bad "14l ...it must relay the checker's OWN reason" "$out14l"
elif printf '%s' "$out14l" | grep -qF -- "it names no class for them"; then
  bad "14l ...but it claimed a --source refusal that never happened" "$out14l"
else
  ok "14l an empty roots declaration is LOUD, and claims no source refusal"
fi

# =============================================================================================
# Summary — and the leg count, so a suite that ran three of these cannot print "0 failed".
# =============================================================================================
EXPECTED_LEGS=57
printf '\nskill-view fixture: %d passed, %d failed, %d skipped, %d leg(s) run\n' \
  "$pass" "$failcount" "$skipped" "$legs"

if [ "$legs" -ne "$EXPECTED_LEGS" ]; then
  printf '::error::skill-view fixture ran %d leg(s), expected %d — a suite that stops early prints a clean summary over the legs it never reached (epic #266).\n' \
    "$legs" "$EXPECTED_LEGS" >&2
  exit 2
fi

[ "$failcount" -eq 0 ] || exit 1
exit 0
