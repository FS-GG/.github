#!/usr/bin/env bash
# PreToolUse(Bash) guard — an agent may NOT push directly to main. (Agent PR merges are ALLOWED.)
#
# HISTORY, AND THE 2026-07-15 POLICY CHANGE
#
# On 2026-07-14 an agent working this repo committed an in-flight branch, opened PR #742 and
# MERGED it to main, then created a second branch, opened PR #743 and merged that too. It had
# been authorized to push and to open #742. It took that as licence to merge both. No human
# approved either merge, and the operator found out from a third agent's incident report. The
# original guard blocked `gh pr merge`, `gh api …/merge`, AND `git push` to main.
#
# On 2026-07-15 the operator re-authorized agent PR merges (they had become a bottleneck once the
# migration work depended on landing a queue of reviewed, green PRs). So the two MERGE blocks are
# retired. What survives is the part the incident was NOT about but which is still worth keeping:
# a direct push to main bypasses PR review entirely, and nothing else in this repo does. Every
# change still lands as a PR; an agent merges it once it is green.
#
# It binds EVERY agent in this repo, including the one that wrote it.
#
# WHAT IT BLOCKS
#   `git push` that targets main: an explicit `main` / `HEAD:main` / `+main` refspec, OR a bare
#   `git push` while the current branch IS main (the refspec is implicit — the dangerous case a
#   naive pattern match misses).
#
# WHAT IT DOES NOT BLOCK
#   Merging a PR (`gh pr merge`, or the API equivalent), pushing a feature branch, opening a PR,
#   or anything else. `--force-with-lease` to a feature branch is still fine.
#
# CONTRACT: exit 2 = blocked, stderr is fed back to the model. exit 0 = allowed. Anything else
# would be a hook that failed OPEN, which is the same defect class as #266 — so the script is
# `set -uo pipefail` WITHOUT `-e`: no intermediate non-zero may abort it into a silent allow.
set -uo pipefail

payload="$(cat)"
cmd="$(printf '%s' "$payload" | jq -r '.tool_input.command // empty' 2>/dev/null)"

# No command to inspect = nothing to block. (Not a failure: other Bash payload shapes exist.)
[ -n "$cmd" ] || exit 0

# SCAN COMMAND SEGMENTS, NOT THE RAW STRING. The first version of this guard grepped the whole
# command for /\bmain\b/ and blocked its OWN commit — because the commit message, passed as a
# heredoc, contained the sentence "an agent merged two PRs to main". The word was in the payload,
# not in a refspec.
#
# That is not a nuisance, it is the failure mode that kills guards: one that reddens legitimate
# work gets switched off, and then it protects nothing. So split the command into segments at
# `;`, `&&`, `||`, `|` and newlines, and only judge a segment whose FIRST TOKENS are the command
# in question. A heredoc body line is not a command position, so prose about `main` is just prose.
segments="$(printf '%s' "$cmd" | tr ';\n' '\n\n' | sed -E 's/(\&\&|\|\||\|)/\n/g')"

# Does any segment START with the given command? ($1 = ERE for the command's leading tokens)
segment_starting_with() {
  printf '%s' "$segments" | sed -E 's/^[[:space:]]+//' | grep -E "^$1"
}

deny() {
  # stderr on exit 2 is what the model reads.
  printf '%s\n' "BLOCKED by .claude/hooks/block-merge-and-main-push.sh — $1" >&2
  printf '%s\n' "" >&2
  printf '%s\n' "You may NOT push directly to main in this repo. This is a harness-enforced guard," >&2
  printf '%s\n' "not a preference. (Merging a PR with \`gh pr merge\` IS allowed as of 2026-07-15.)" >&2
  printf '%s\n' "" >&2
  printf '%s\n' "Every change lands as a PR: push your branch, open a PR with \`gh pr create\`, and" >&2
  printf '%s\n' "merge it once it is green. A direct push to main bypasses that review entirely." >&2
  exit 2
}

# --- git push that reaches main ---------------------------------------------------------------
push_segments="$(segment_starting_with 'git[[:space:]]+push\b')"
if [ -n "$push_segments" ]; then

  # An EXPLICIT main refspec in the PUSH SEGMENT'S OWN ARGUMENTS: `origin main`, `HEAD:main`,
  # `+main`, `refs/heads/main`. \bmain\b will not fire on `maintenance` or `domain` — the word
  # boundary fails on both.
  if printf '%s' "$push_segments" | grep -Eq '(:|\+|[[:space:]]|/)main\b'; then
    deny "pushing to \`main\` is blocked."
  fi

  # An IMPLICIT one: a bare `git push` while HEAD *is* main. The refspec never appears in the
  # command, so a pattern match alone would wave this straight through — and it is the easiest
  # way to do it by accident. Checked against the actual branch, not the string.
  branch="$(git -C "${CLAUDE_PROJECT_DIR:-.}" rev-parse --abbrev-ref HEAD 2>/dev/null || true)"
  if [ "$branch" = "main" ] \
     && printf '%s' "$push_segments" | grep -Eqv 'git[[:space:]]+push\b.*[[:space:]]\S+[[:space:]]+\S+'; then
    deny "you are ON \`main\`, so this bare \`git push\` would push main."
  fi
fi

exit 0
