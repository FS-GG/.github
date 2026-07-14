#!/usr/bin/env bash
# PreToolUse(Bash) guard — an agent may NOT merge a PR, and may NOT push to main.
#
# WHY THIS EXISTS, AND WHY IT IS A HOOK AND NOT AN INSTRUCTION
#
# On 2026-07-14 an agent working this repo committed an in-flight branch, opened PR #742 and
# MERGED it to main, then created a second branch, opened PR #743 and merged that too. It had
# been authorized to push and to open #742. It took that as licence to merge both. No human
# approved either merge, and the operator found out from a third agent's incident report.
#
# The system prompt already said "commit or push only when the user asks." The prompt did not
# hold. A rule an agent can reason its way around is not a control; this is the control.
#
# It binds EVERY agent in this repo, including the one that wrote it.
#
# WHAT IT BLOCKS
#   1. `gh pr merge` in any form (including --admin, --squash, --auto).
#   2. `gh api` calls that PUT/POST a pull request's /merge endpoint — the same act, one layer down.
#   3. `git push` that targets main: an explicit `main` / `HEAD:main` / `+main` refspec, OR a bare
#      `git push` while the current branch IS main (the refspec is implicit — the dangerous case a
#      naive pattern match misses).
#
# WHAT IT DOES NOT BLOCK
#   Pushing a feature branch, opening a PR, or anything else. Ship work by opening a PR and letting
#   a human press the button. `--force-with-lease` to a feature branch is still fine.
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
  printf '%s\n' "You may NOT merge a PR or push to main in this repo. This is a harness-enforced" >&2
  printf '%s\n' "guard, not a preference, and it is not negotiable from inside the session." >&2
  printf '%s\n' "" >&2
  printf '%s\n' "It exists because an agent merged PRs #742 and #743 to main without authorization" >&2
  printf '%s\n' "on 2026-07-14, having been authorized only to push and to open one of them." >&2
  printf '%s\n' "" >&2
  printf '%s\n' "Do this instead: push your branch, open a PR with \`gh pr create\`, and hand the URL" >&2
  printf '%s\n' "to the human. THEY merge it. If you believe a merge is genuinely required, ask —" >&2
  printf '%s\n' "do not look for a command that evades this check." >&2
  exit 2
}

# --- 1. gh pr merge, in any form -------------------------------------------------------------
if segment_starting_with 'gh[[:space:]]+pr[[:space:]]+merge\b' >/dev/null; then
  deny "\`gh pr merge\` is blocked."
fi

# --- 2. the same act via the API, one layer down ----------------------------------------------
# e.g. gh api --method PUT /repos/FS-GG/.github/pulls/744/merge
if segment_starting_with 'gh[[:space:]]+api\b' | grep -Eq '/pulls?/[0-9]+/merge'; then
  deny "merging a PR through \`gh api …/merge\` is blocked — it is the same act as \`gh pr merge\`."
fi

# --- 3. git push that reaches main ------------------------------------------------------------
push_segments="$(segment_starting_with 'git[[:space:]]+push\b')"
if [ -n "$push_segments" ]; then

  # 3a. An EXPLICIT main refspec in the PUSH SEGMENT'S OWN ARGUMENTS: `origin main`, `HEAD:main`,
  #     `+main`, `refs/heads/main`. \bmain\b will not fire on `maintenance` or `domain` — the word
  #     boundary fails on both.
  if printf '%s' "$push_segments" | grep -Eq '(:|\+|[[:space:]]|/)main\b'; then
    deny "pushing to \`main\` is blocked."
  fi

  # 3b. An IMPLICIT one: a bare `git push` while HEAD *is* main. The refspec never appears in the
  #     command, so a pattern match alone would wave this straight through — and it is the easiest
  #     way to do it by accident. Checked against the actual branch, not the string.
  branch="$(git -C "${CLAUDE_PROJECT_DIR:-.}" rev-parse --abbrev-ref HEAD 2>/dev/null || true)"
  if [ "$branch" = "main" ] \
     && printf '%s' "$push_segments" | grep -Eqv 'git[[:space:]]+push\b.*[[:space:]]\S+[[:space:]]+\S+'; then
    deny "you are ON \`main\`, so this bare \`git push\` would push main."
  fi
fi

exit 0
