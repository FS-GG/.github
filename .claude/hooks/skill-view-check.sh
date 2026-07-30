#!/usr/bin/env bash
# SessionStart hook — the WORKER-PREAMBLE half of ADR-0067 §8's loud absence check (.github#1635).
#
# §8 requires the assertion "at checkout AND in CI", and the two halves catch different things. CI
# judges a pull request; this judges the tree a worker is about to work in. They matter separately
# because the failure they guard is invisible from inside: an agent whose skill root is absent, a
# dangling link, or a Windows text-file symlink starts normally, resolves ZERO skills, and reports
# nothing — measured on Claude Code 2.1.220 and Codex CLI 0.145.0 (.github#1621 §1 Q4). The worker
# then does the work badly and no one learns why. This hook is the only moment that can say so.
#
# IT IS ADVISORY, AND DELIBERATELY SO. A SessionStart hook cannot block, and this one does not try:
# it prints the checker's own diagnostic and exits non-zero so the message surfaces. Refusing to
# start a session over a skills problem would make a repair session impossible in the one repo where
# the repair lives — which is the failure mode the whole item was warned about.
#
# `--source .agents/skills`: the canonical root (ADR-0067 §5), and NOT a manifest. Both manifests in
# registry/ are subsets (four and six of thirteen), so either would be green over a tree that had lost
# the rest — .github#1504's coherent-but-incomplete class. The root set comes from .agent-skill-roots,
# so this is character-for-character the invocation .github/workflows/skill-view.yml's dogfood job
# runs — now literally so, because this script `cd`s into the tree first (see WHERE IT RUNS below).
#
# ---------------------------------------------------------------------------------------------
# WHY THE OLD `[ -d .agents/skills ] || exit 0` GUARD IS GONE (.github#1700)
#
# It exited 0, SILENTLY, on the exact failure this hook exists to catch. `-d` is false for a DANGLING
# view root, for a `core.symlinks=false` text-file symlink, and for a non-directory — three of the
# five classes, and precisely the three phase 1 (.github#1621) measured as *exit 0 with no diagnostic
# in both runtimes*. Measured 2026-07-28 on a tree with `.claude/skills` populated and
# `.agents/skills -> ../nowhere`: this hook printed NOTHING and exited 0 while
# `scripts/skill-view check` over the same tree printed `[dangling-root]` and exited 1. The guard that
# existed to make the check tolerant was also what made it blind — epic #266's vacuous-pass shape.
#
# The distinction the guard needed is ABSENT (say nothing) vs PRESENT-BUT-BROKEN (shout), and
# `scripts/skill-view`'s own `classify_root` already draws exactly that line — `absent` /
# `dangling-link` / `text-file-link` / `not-a-directory` / `dir`. So this hook ASKS THE CHECKER and
# reads the bracketed class off its answer. It does not re-derive a weaker test.
#
# WHY THE CHECKER IS ASKED TWICE. `check` needs a readable `--source` to know the expected set, and
# in this repo the `--source` IS one of the subjects. When `.agents/skills` is absent OR broken,
# `check --source .agents/skills` dies the SAME WAY for both (`--source is not a directory`, exit 2),
# which is a verdict that cannot tell them apart — it is the `-d` guard's blindness wearing the
# tool's clothes. So on exit 2 the checker is re-asked with `.claude/skills` as the expected set, and
# THAT run classifies `.agents/skills` properly. The two roots are byte-identical mirrors in this
# repo (`skillmirror-freshness` is the gate that keeps them so), so the expected set does not change
# — only which root can be read to state it.
#
# WHAT STAYS EXCUSED, AND NOTHING MORE. Exactly one finding is silent: `[absent-root] .agents/skills`
# ALONE. That is the same tree the deleted `-d` guard excused and no other, so this change adds
# loudness and removes none (.github#698: a hook that shouted at every checkout predating this change
# would be noise, and noise is what teaches people to scroll past the one message that matters). An
# absent `.claude/skills`, or an absent `.agents/skills` alongside any second finding, stays LOUD —
# as it is today.
#
# WHERE IT RUNS. `--source` is resolved against the PROCESS's working directory while `--tree` is
# resolved on its own, so the old form could grade one tree's roots against another tree's source
# whenever the session's cwd was not $CLAUDE_PROJECT_DIR. `cd` first, then both are the same tree.
# ---------------------------------------------------------------------------------------------

set -uo pipefail

DIR="${CLAUDE_PROJECT_DIR:-.}"

# A tree without the tool is not a tree with a finding. Say nothing and leave (.github#698).
[ -x "$DIR/scripts/skill-view" ] || exit 0
cd "$DIR" || exit 0

roots="$(bash scripts/skill-view roots --tree . 2>&1)" || {
  printf 'skill-view: could not classify this worktree\047s declared skill roots.\n%s\n' "$roots" >&2
  exit 2
}

# No root at all is the pre-apparatus checkout #698 deliberately leaves alone. Every other non-dir
# class is a loud, per-root finding; this consumes the tool's vocabulary instead of re-deriving it.
if awk -F '\t' '$2 != "dir" { bad=1 } END { exit bad }' <<<"$roots"; then
  source_root="$(printf '%s\n' "$roots" | awk -F '\t' 'NR == 1 { print $1 }')"
  out="$(bash scripts/skill-view check --source "$source_root" --tree . 2>&1)"; rc=$?
  [ "$rc" -eq 0 ] && { printf '%s\n' "$out" | tail -n 1; exit 0; }
  printf 'skill-view: THIS WORKTREE'\''S AGENT SKILLS ARE NOT ALL VISIBLE (exit %d).\n%s\n' "$rc" "$out" >&2
  exit "$rc"
fi

if awk -F '\t' '$2 != "absent" { bad=1 } END { exit bad }' <<<"$roots"; then
  exit 0
fi

# This authority's generated view is `.agents/skills`; retain #698's narrowly measured quiet case
# without using it as an expected-source fallback.  The root report above still judges every other
# declared root, including any future third root.
if [ "$(printf '%s\n' "$roots" | awk -F '\t' '$2 != "dir" { print }')" = $'.agents/skills\tabsent' ]; then
  exit 0
fi

if printf '%s\n' "$roots" | awk -F '\t' '$2 != "dir" { c=$2; if (c == "dangling-link") c="dangling"; if (c == "text-file-link") c="text-file"; found=1; printf "::error::skill-view: [%s-root] %s\n", c, $1 } END { exit !found }' >&2; then :; fi

printf 'skill-view: THIS WORKTREE'\''S AGENT SKILLS ARE NOT ALL VISIBLE (exit 1).\n' >&2
printf '%s\n' "$roots" >&2
printf 'Both runtimes would start over this tree, resolve zero skills, and exit 0 without saying so.\n' >&2
printf 'Repair: scripts/skill-view generate --source <dir>, or restore the missing root. ADR-0067 §8.\n' >&2
exit 1
