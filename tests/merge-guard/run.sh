#!/usr/bin/env bash
# Fixture for .claude/hooks/block-merge-and-main-push.sh — the PreToolUse guard that stops an
# agent merging a PR or pushing to main.
#
# THIS FILE EXISTS BECAUSE THE GUARD BLOCKED ITS OWN TESTS.
#
# The cases below contain the literal strings the guard hunts for. Written inline in a Bash tool
# call, the guard sees them in ITS OWN payload and denies the test run. So the cases live in a
# file, and the tool call is just `bash tests/merge-guard/run.sh` — which contains none of them.
#
# The same confusion is the guard's most important bug class, and leg 13 pins it: the first
# version grepped the whole command string for /\bmain\b/ and BLOCKED ITS OWN COMMIT, because the
# commit message — passed as a heredoc — contained the sentence "an agent merged two PRs to main".
# The word was in the prose, not in a refspec. A guard that reddens legitimate work gets switched
# off, and then it guards nothing. So the guard judges COMMAND SEGMENTS, not the raw string.
set -uo pipefail

HOOK="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)/.claude/hooks/block-merge-and-main-push.sh"
PASS=0
FAIL=0

# $1 = expected exit (2 = blocked, 0 = allowed), $2 = the command, $3 = what it is
expect() {
  local want="$1" cmd="$2" name="$3" rc=0
  printf '%s' "$cmd" | jq -Rs '{tool_name:"Bash",tool_input:{command:.}}' | bash "$HOOK" >/dev/null 2>&1 || rc=$?
  if [ "$rc" = "$want" ]; then
    printf '  ok    [%s] %s\n' "$([ "$want" = 2 ] && echo BLOCK || echo allow)" "$name"
    PASS=$((PASS + 1))
  else
    printf '  FAIL  %s — expected exit %s, got %s\n' "$name" "$want" "$rc"
    FAIL=$((FAIL + 1))
  fi
}

echo "MUST BLOCK — the acts that took #742 and #743 to main without a human:"
expect 2 'gh pr merge 744 --squash'                                  'gh pr merge'
expect 2 'gh pr merge --admin'                                       'gh pr merge --admin (bypasses branch protection)'
expect 2 'gh pr merge 744 --auto --squash --delete-branch'           'gh pr merge --auto'
expect 2 'cd /repo && gh pr merge 744'                               'gh pr merge after && (compound command)'
expect 2 'gh api --method PUT /repos/FS-GG/.github/pulls/744/merge'  'the merge API — the same act, one layer down'
expect 2 'git push origin main'                                      'push to main'
expect 2 'git push -f origin main'                                   'force-push to main'
expect 2 'git push origin HEAD:main'                                 'HEAD:main refspec'
expect 2 'git push --force-with-lease origin +main'                  '+main refspec'
expect 2 'git push origin refs/heads/main'                           'fully-qualified ref'

echo
echo "MUST ALLOW — a guard that blocks real work is a guard someone turns off:"
expect 0 'git push origin adr/corpus-coherence'                      'pushing a feature branch'
expect 0 'git push -u origin feat/maintenance-window'                'branch whose NAME contains "main" (maintenance)'
expect 0 'git push origin fix/domain-parsing'                        'branch whose name contains "main" (domain)'
expect 0 'gh pr create --base main --title x'                        'opening a PR against main — the thing we WANT'
expect 0 'gh pr view 744'                                            'reading a PR'
expect 0 'git status'                                                'an unrelated command'

# Leg 13 — the regression. A commit whose MESSAGE talks about merging to main, followed by a
# legitimate feature-branch push. The first version of the guard blocked exactly this, on its own
# commit. If this leg ever goes red again, the guard is scanning prose instead of commands.
expect 0 "$(cat <<'CASE'
git commit -F - <<'EOF'
guard: an agent merged two PRs to main because the rule was only a sentence
  * `gh pr merge` in any form is blocked
  * `git push` reaching main, or HEAD:main, or a bare push while on main
EOF
git push origin adr/corpus-coherence
CASE
)" 'a commit message ABOUT merging to main + a feature-branch push (the self-block regression)'

printf '\nmerge-guard fixture: %s passed, %s failed\n' "$PASS" "$FAIL"
[ "$FAIL" -eq 0 ] || exit 1
