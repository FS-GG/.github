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
# `--source .agents/skills`: the canonical root, and NOT a manifest. Both manifests in registry/ are
# subsets (four and six of thirteen), so either would be green over a tree that had lost the rest —
# .github#1504's coherent-but-incomplete class. The root set comes from .agent-skill-roots, so this
# is character-for-character the invocation .github/workflows/skill-view.yml's dogfood job runs.

set -uo pipefail

DIR="${CLAUDE_PROJECT_DIR:-.}"

# A tree without the tool is not a tree with a finding. Say nothing and leave: a hook that shouted at
# every checkout that predates this change would be noise, and noise is what teaches people to
# scroll past the one message that matters (.github#698).
[ -x "$DIR/scripts/skill-view" ] || exit 0
[ -d "$DIR/.agents/skills" ] || exit 0

out="$(bash "$DIR/scripts/skill-view" check --source .agents/skills --tree "$DIR" 2>&1)"
rc=$?

if [ "$rc" -eq 0 ]; then
  # One line of context, not the whole report: the interesting case is the loud one.
  printf '%s\n' "$out" | tail -n 1
  exit 0
fi

printf 'skill-view: THIS WORKTREE'\''S AGENT SKILLS ARE NOT ALL VISIBLE (exit %d).\n' "$rc" >&2
printf '%s\n' "$out" >&2
printf 'Both runtimes would start over this tree, resolve zero skills, and exit 0 without saying so.\n' >&2
printf 'Repair: scripts/skill-view generate --source <dir>, or restore the missing root. ADR-0067 §8.\n' >&2
exit "$rc"
