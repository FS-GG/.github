#!/usr/bin/env bash
# Fixture for .claude/hooks/block-merge-and-main-push.sh — the PreToolUse guard that stops an
# agent pushing directly to main. (Agent PR merges were re-authorized 2026-07-15; the guard no
# longer blocks `gh pr merge` — this fixture pins that they are now ALLOWED.)
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

echo "MUST BLOCK — a direct push to main bypasses PR review, and nothing else in this repo does:"
expect 2 'git push origin main'                                      'push to main'
expect 2 'git push -f origin main'                                   'force-push to main'
expect 2 'git push origin HEAD:main'                                 'HEAD:main refspec'
expect 2 'git push --force-with-lease origin +main'                  '+main refspec'
expect 2 'git push origin refs/heads/main'                           'fully-qualified ref'

echo
echo "MUST ALLOW — including agent PR merges, re-authorized 2026-07-15 (a guard that blocks real work gets turned off):"
expect 0 'gh pr merge 744 --squash'                                  'gh pr merge — re-authorized, no longer blocked'
expect 0 'gh pr merge --admin'                                       'gh pr merge --admin — re-authorized'
expect 0 'gh api --method PUT /repos/FS-GG/.github/pulls/744/merge'  'the merge API — re-authorized'
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

# ------------------------------------------------------------------------------------------------
# #909 — THE TWO DEFECTS THE LEGS ABOVE COULD NOT SEE
#
# Every leg above exercises the EXPLICIT-refspec path in the bare `git push …` form. Two whole
# behaviours went untested, and both were broken:
#
#   1. The IMPLICIT check (a push naming no refspec) was never tested AT ALL, because it depends on
#      the branch of a real directory. So the fixture could not see that it asked the WRONG
#      directory — CLAUDE_PROJECT_DIR, the shared checkout, which pnext-item §2 guarantees is on
#      main while the worker is in a worktree.
#   2. `git -C <dir> push origin main` was ALLOWED. The old segment matcher wanted `git` adjacent
#      to `push`, so ANY global option between them skipped the entire guard — the explicit check
#      included. The bypass and the false positive are the same root cause, which is why the fix
#      is one parser and the legs are one section.
#
# These need REAL repos on REAL branches, so they build a throwaway fixture: a "shared checkout" on
# main plus a worktree on a feature branch, exactly the layout §2 mandates.
FIXDIR="$(mktemp -d)"
trap 'rm -rf "$FIXDIR"' EXIT
git_q() { git -c user.email=f@x -c user.name=f -c init.defaultBranch=main "$@" >/dev/null 2>&1; }

git_q init -b main "$FIXDIR/shared"
git_q -C "$FIXDIR/shared" commit --allow-empty -m init
git_q -C "$FIXDIR/shared" worktree add "$FIXDIR/wt" -b item/909-guard
# A repo whose PATH contains "main" but whose branch does not — the `/main\b` path false positive.
git_q init -b feature "$FIXDIR/main-repo"
git_q -C "$FIXDIR/main-repo" commit --allow-empty -m init

# $1 = expected exit, $2 = CLAUDE_PROJECT_DIR, $3 = command, $4 = name
expect_in() {
  local want="$1" projdir="$2" cmd="$3" name="$4" rc=0
  printf '%s' "$cmd" | jq -Rs '{tool_name:"Bash",tool_input:{command:.}}' \
    | CLAUDE_PROJECT_DIR="$projdir" bash "$HOOK" >/dev/null 2>&1 || rc=$?
  if [ "$rc" = "$want" ]; then
    printf '  ok    [%s] %s\n' "$([ "$want" = 2 ] && echo BLOCK || echo allow)" "$name"
    PASS=$((PASS + 1))
  else
    printf '  FAIL  %s — expected exit %s, got %s\n' "$name" "$want" "$rc"
    FAIL=$((FAIL + 1))
  fi
}

echo
echo "MUST BLOCK — #909's bypass: a git GLOBAL OPTION must not hide the push (these were ALLOWED):"
expect_in 2 "$FIXDIR/shared" "git -C $FIXDIR/shared push origin main"        '-C <dir> push origin main'
expect_in 2 "$FIXDIR/shared" 'git --no-pager push origin main'               '--no-pager push origin main'
expect_in 2 "$FIXDIR/shared" 'git -c push.default=current push origin main'  '-c k=v push origin main'
expect_in 2 "$FIXDIR/shared" 'git --git-dir=.git --work-tree=. push origin main' '--git-dir/--work-tree push origin main'
expect_in 2 "$FIXDIR/shared" "git -C $FIXDIR/shared push origin HEAD:main"   '-C <dir> push HEAD:main'
expect_in 2 "$FIXDIR/shared" "git -C $FIXDIR/shared push --force origin +main" '-C <dir> force-push +main'
expect_in 2 "$FIXDIR/shared" 'GIT_TRACE=1 git push origin main'              'VAR=value prefix before git'

echo
echo "MUST BLOCK — the IMPLICIT refspec: no refspec named, and the pushing dir IS on main:"
expect_in 2 "$FIXDIR/shared" 'git push'                                      'bare push, project dir on main'
expect_in 2 "$FIXDIR/shared" 'git push origin'                               'push <remote> only, on main'
expect_in 2 "$FIXDIR/shared" 'git push -f'                                   'bare force-push, on main'
expect_in 2 "$FIXDIR/shared" "git -C $FIXDIR/shared push"                    '-C onto a dir that IS on main'
expect_in 2 "$FIXDIR/shared" "cd $FIXDIR/shared && git push"                 'cd onto a dir that IS on main'

echo
echo "MUST ALLOW — #909's false positive: the worker is in a worktree, NOT on main:"
expect_in 0 "$FIXDIR/shared" "git -C $FIXDIR/wt push"                        '-C <worktree on a feature branch>'
expect_in 0 "$FIXDIR/shared" "cd $FIXDIR/wt && git push"                     'cd <worktree> && bare push'
expect_in 0 "$FIXDIR/shared" "git -C $FIXDIR/wt push origin item/909-guard"  '-C <worktree> + explicit branch'
expect_in 0 "$FIXDIR/shared" "git -C $FIXDIR/main-repo push origin feature"  'a -C PATH containing "main" is not a refspec'


# A repo whose PATH CONTAINS A SPACE — the quoting hole. Unquoted tokenizing splits the path and
# loses the `push` behind it, so the guard never judges the segment at all.
git_q init -b main "$FIXDIR/has space"
git_q -C "$FIXDIR/has space" commit --allow-empty -m init

echo
echo "MUST BLOCK — these ALSO push main, and every one of them was ALLOWED at some point in review:"
expect_in 2 "$FIXDIR/shared" "git -C \"$FIXDIR/has space\" push origin main" 'a QUOTED -C path with a space must not hide the push'
expect_in 2 "$FIXDIR/shared" 'git push origin HEAD'                          'HEAD IS the current branch (on main)'
expect_in 2 "$FIXDIR/shared" 'git push origin @'                             '@ is HEAD (on main)'
expect_in 2 "$FIXDIR/shared" 'git push origin +HEAD'                         'force-push HEAD (on main)'
expect_in 2 "$FIXDIR/shared" 'git push -o ci.skip origin'                    "-o's VALUE is not a refspec (on main)"
expect_in 2 "$FIXDIR/shared" 'git push --repo origin'                        "--repo's VALUE is not a refspec (on main)"

echo
echo "MUST BLOCK — the #909 REGRESSION legs: an unresolvable dir hint must FALL BACK, not wave through:"
expect_in 2 "$FIXDIR/shared" "cd $FIXDIR/shared && cd - && git push"         'cd - (back onto main)'
expect_in 2 "$FIXDIR/shared" 'cd $UNSET_VAR && git push'                     'an unexpanded variable in the cd'
expect_in 2 "$FIXDIR/shared" 'cd /nonexistent-xyz && git push'               'a cd to a dir that does not exist'
expect_in 2 "$FIXDIR/shared" 'git -C /nonexistent-xyz push'                  'a -C to a dir that does not exist'

echo
echo "MUST ALLOW — the fixes above must not cost us the legitimate cases:"
expect_in 0 "$FIXDIR/shared" "git -C $FIXDIR/wt push origin HEAD"            'HEAD from a worktree on a feature branch'
expect_in 0 "$FIXDIR/shared" "git -C $FIXDIR/wt push -o ci.skip origin"      '-o from a worktree on a feature branch'
expect_in 0 "$FIXDIR/shared" 'git push origin HEAD:feature'                  'HEAD:<other> pushes current to a DIFFERENT ref'

printf '\nmerge-guard fixture: %s passed, %s failed\n' "$PASS" "$FAIL"
[ "$FAIL" -eq 0 ] || exit 1
