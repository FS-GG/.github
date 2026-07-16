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
#   `git push` that targets main: an explicit `main` / `HEAD:main` / `+main` refspec, OR a push
#   that names no refspec while the pushing directory's branch IS main (the refspec is implicit —
#   the dangerous case a naive pattern match misses).
#
# WHAT IT DOES NOT BLOCK
#   Merging a PR (`gh pr merge`, or the API equivalent), pushing a feature branch, opening a PR,
#   or anything else. `--force-with-lease` to a feature branch is still fine.
#
# ------------------------------------------------------------------------------------------------
# THE 2026-07-16 REPAIR (#909) — WHY THIS PARSES git's COMMAND LINE INSTEAD OF GREPPING IT
#
# Two defects, one root cause: the guard did not understand `git`'s GLOBAL OPTIONS.
#
#   1. IT FAILED OPEN.  The segment matcher required `git` to be immediately followed by `push`
#      (`^git[[:space:]]+push\b`). Any global option in between — `-C <dir>`, `-c k=v`,
#      `--no-pager`, `--git-dir=…` — meant NO segment matched, so the ENTIRE guard was skipped and
#      `git -C . push origin main` was ALLOWED. Every check, including the explicit-refspec one.
#      #909's report stated the guard "is not bypassable" and that the refspec check "still catches
#      origin main"; both were verified in the bare `git push …` form only, and neither held in
#      this one.
#
#   2. IT FAILED NOISY, AND THE NOISE TAUGHT THE BYPASS.  The implicit-refspec check asked
#      CLAUDE_PROJECT_DIR — the SHARED checkout — what branch it was on. Under pnext-item §2 every
#      worker works in a per-item worktree while the shared checkout sits on `main` (the same fact
#      #564 turns on), so a bare `git push` from a worktree was denied with a statement that was
#      simply FALSE: "you are ON main" when you were not. A gate that cries wolf on the mandated
#      happy path teaches one lesson — "BLOCKED is noise, work around it" — and the workaround it
#      taught was `git -C <worktree> push …`, which is defect 1's bypass verbatim. The false
#      positive was training workers straight through the hole.
#
# The fix for both is one thing: walk the token stream after `git`, skip the global options
# (consuming their arguments), and find the real subcommand. That closes the bypass AND yields the
# `-C` directory, which is what the implicit check should have been asking about all along.
#
# Consequences worth knowing:
#   * The refspec check now scans the push's OWN ARGUMENTS, not the whole segment. That also fixes
#     a false positive nobody had filed: `git -C ../main-repo push origin feature` matched the old
#     `/main\b` pattern on its PATH, not on a refspec.
#   * Where a push runs cannot always be known — a PreToolUse hook cannot see the cwd of the
#     command it is gating, because any `cd` happens inside the very string being inspected. We
#     resolve what we can (`-C`, and a `cd` earlier in the same command) and FALL BACK to the
#     project dir. When the fallback is what we checked, the denial SAYS so rather than asserting
#     a branch the worker may not be on. Honest-and-fail-closed beats confident-and-wrong; a check
#     that reports confidently about a subject it did not look at is #266's signature.
#
# THIS SCRIPT DOES ITS OWN FILTERING, AND MUST.  settings.json deliberately wires it with NO `if`
# condition. Claude Code's docs are explicit that the `if` filter is best-effort and "fails open …
# when the Bash command can't be parsed", and that you should "use the permission system rather
# than a hook to enforce a hard allow or deny". An `if: Bash(git push *)` gate is a gate on
# INVOCATION — a command it does not match never spawns this script at all — so any filtering
# precision here would be moot behind it. Do not re-add one.
#
# WHAT THIS GUARD IS NOT — STATED, SO NOBODY HAS TO REDISCOVER IT
#
# It stops an agent pushing to main by ACCIDENT or OVERREACH. It is not a sandbox, and it is not
# proof against a command written to defeat it. Two known gaps, both measured, both left open on
# purpose:
#
#   * A push inside `$(…)` or backticks is not judged. `echo $(git push origin main)` is ALLOWED.
#     Splitting into command substitutions would make a HEREDOC BODY a command position, which is
#     the self-block that killed the first version of this guard (fixture leg 13) — prose about a
#     push would deny the commit that documents it. A guard that reddens legitimate work gets
#     switched off, and then it guards nothing. The trade is deliberate: the reachable-by-accident
#     case is the bare/refspec push, and that one is covered.
#   * `git push` reached through an alias, a wrapper script, or a shell function is not judged —
#     the guard reads the command it is given, not what that command eventually runs.
#
# If you need a hard boundary rather than a safety net, the enforcement layer is the PERMISSION
# system (a `deny` rule), not a PreToolUse hook: Claude Code's own docs say so, and this file
# cannot be more than best-effort while it is parsing shell.
#
# CONTRACT: exit 2 = blocked, stderr is fed back to the model. exit 0 = allowed. Anything else
# would be a hook that failed OPEN, which is the same defect class as #266 — so the script is
# `set -uo pipefail` WITHOUT `-e`: no intermediate non-zero may abort it into a silent allow.
set -uo pipefail

payload="$(cat)"

# CHEAP EXIT FIRST. settings.json wires this on every Bash call (see above), so the overwhelmingly
# common case is a command that has nothing to do with pushing. A builtin substring test costs no
# fork; jq does. If the payload does not contain "push" anywhere — prose, refspec or otherwise —
# there is no `git push` in it. Erring toward running the full parse is free; erring the other way
# would be a hook that failed open, so the test is deliberately over-broad.
case "$payload" in
  *push*) ;;
  *) exit 0 ;;
esac

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

# --- split a segment into tokens, respecting quotes -------------------------------------------
#
# `read -a` splits on IFS and knows nothing about quoting, which is a HOLE, not an inconvenience:
# `git -C "/has space" push origin main` tokenizes as [git, -C, "/has, space", push, …], so `-C`
# eats `"/has`, the stray `space"` looks like a subcommand, and the scan gives up BEFORE reaching
# `push` — the segment is never judged and the push to main is allowed. That is the same shape as
# the bug this whole file is repairing (something between `git` and `push` hides the push), so a
# tokenizer that cannot see through quotes would leave the job half done.
#
# Handles '…', "…" and backslash escapes. Sets TOKENS.
tokenize() {
  local s="$1" tok='' q='' c i n esc=0
  TOKENS=()
  n="${#s}"
  for ((i = 0; i < n; i++)); do
    c="${s:i:1}"
    if [ "$esc" = 1 ]; then tok+="$c"; esc=0; continue; fi
    case "$q" in
      "'")  if [ "$c" = "'" ]; then q=''; else tok+="$c"; fi ;;
      '"')  if [ "$c" = '"' ]; then q=''; elif [ "$c" = '\' ]; then esc=1; else tok+="$c"; fi ;;
      *)
        case "$c" in
          "'"|'"') q="$c" ;;
          '\')     esc=1 ;;
          ' '|$'\t')
            # A quoted empty string is still a token: `git -C "" push` must not let `-C` swallow
            # `push`. Only an UNQUOTED run of spaces separates tokens, so track emptiness.
            if [ -n "$tok" ]; then TOKENS+=("$tok"); tok=''; fi ;;
          *) tok+="$c" ;;
        esac ;;
    esac
  done
  [ -n "$tok" ] && TOKENS+=("$tok")
  return 0
}

# --- is this segment a `git … push …`? --------------------------------------------------------
#
# Walk the tokens after `git`, skipping GLOBAL options until the subcommand appears. An unknown
# `-x` is SKIPPED rather than ending the scan: a future git option must not be able to hide a
# `push` behind it. Sets PUSH_FOUND / PUSH_DIR / PUSH_ARGS (a token array).
parse_push_segment() {
  PUSH_FOUND=0; PUSH_DIR=''; PUSH_ARGS=()
  local -a t
  tokenize "$1"
  t=("${TOKENS[@]+"${TOKENS[@]}"}")
  [ "${#t[@]}" -gt 0 ] || return 0

  # Leading VAR=value assignments are a command prefix, not the command (`GIT_DIR=x git push`).
  local i=0 n="${#t[@]}"
  while [ "$i" -lt "$n" ]; do
    case "${t[$i]}" in
      [A-Za-z_]*=*) i=$((i + 1)) ;;
      *) break ;;
    esac
  done
  [ "$i" -lt "$n" ] || return 0
  [ "${t[$i]}" = "git" ] || return 0
  i=$((i + 1))

  while [ "$i" -lt "$n" ]; do
    case "${t[$i]}" in
      # -C <dir> is the one we actually want: it names where the push will run.
      -C)
        PUSH_DIR="${t[$((i + 1))]:-}"; i=$((i + 2)) ;;
      # Global options that consume a SEPARATE argument.
      -c|--exec-path|--git-dir|--work-tree|--namespace|--super-prefix|--config-env|--attr-source)
        i=$((i + 2)) ;;
      # The same, in --opt=value form: a single token.
      --exec-path=*|--git-dir=*|--work-tree=*|--namespace=*|--super-prefix=*|--config-env=*|--attr-source=*)
        i=$((i + 1)) ;;
      # The subcommand. Everything after it belongs to `push`.
      push)
        PUSH_FOUND=1
        [ "$((i + 1))" -lt "$n" ] && PUSH_ARGS=("${t[@]:$((i + 1))}")
        return 0 ;;
      # Any other flag: a valueless global option (--no-pager, --bare, -P, …) or one we do not
      # know. Skip it and keep hunting for the subcommand — never stop scanning on a flag.
      -*)
        i=$((i + 1)) ;;
      # A non-flag token that is not `push`: another subcommand (status, commit, …). Done.
      *)
        return 0 ;;
    esac
  done
}

# --- does this push name main EXPLICITLY? ------------------------------------------------------
#
# `origin main`, `HEAD:main`, `+main`, `refs/heads/main`. \bmain\b will not fire on `maintenance`
# or `domain` — the word boundary fails on both. Scans the push's OWN ARGUMENTS, so a `-C` path
# or a repo directory containing "main" is not mistaken for a refspec.
push_names_main() {
  printf '%s' "${PUSH_ARGS[*]+"${PUSH_ARGS[*]}"}" | grep -Eq '(^|:|\+|[[:space:]]|/)main\b'
}

# --- the push's REFSPEC ARGUMENTS, with option values removed ----------------------------------
#
# `push`'s OWN options are not all valueless: `-o <opt>`, `--repo <r>`, `--receive-pack <p>` and
# `--exec <p>` each consume the token after them. Counting that value as a refspec is a fail-OPEN:
# `git push -o ci.skip origin` on main looks like two refspec args, so the branch check below is
# skipped and main is pushed. Sets REFSPEC_ARGS.
push_refspec_args() {
  local a i=0 n="${#PUSH_ARGS[@]}"
  REFSPEC_ARGS=()
  while [ "$i" -lt "$n" ]; do
    a="${PUSH_ARGS[$i]}"
    case "$a" in
      -o|--push-option|--repo|--receive-pack|--exec) i=$((i + 2)) ;;
      -*) i=$((i + 1)) ;;
      *) REFSPEC_ARGS+=("$a"); i=$((i + 1)) ;;
    esac
  done
  return 0
}

# --- does this push resolve to the CURRENT branch? ---------------------------------------------
#
# Two ways it can, and both must run the branch check:
#
#   * NO refspec at all — `git push`, `git push -f`, `git push origin`. git pushes the current
#     branch (push.default). This is the easiest way to hit main by accident.
#   * The refspec IS the current branch — `git push origin HEAD`, `origin @`, `origin +HEAD`.
#     A pattern match sees no "main" here and waves it through, but on main it pushes main, and
#     `git push origin HEAD` is a thoroughly common idiom.
#
# `HEAD:<branch>` is NOT this case: it pushes the current branch to a DIFFERENT ref, and if that
# ref is main then push_names_main has already denied it.
push_targets_current_branch() {
  local a
  push_refspec_args
  [ "${#REFSPEC_ARGS[@]}" -lt 2 ] && return 0
  for a in "${REFSPEC_ARGS[@]}"; do
    case "$a" in
      HEAD|@|+HEAD|+@) return 0 ;;
    esac
  done
  return 1
}

# --- where will this push actually run? --------------------------------------------------------
#
# Best effort, and the guard is honest about which. Sets PUSH_DIR_RESOLVED, and DIR_IS_GUESS=1 when
# we fell back to the project dir rather than reading the directory out of the command itself.
#
# It sets GLOBALS rather than printing, deliberately: a `dir="$(resolve_push_dir …)"` would run the
# whole thing in a SUBSHELL, so DIR_IS_GUESS would never reach the caller — and under `set -u` that
# is an unbound variable, i.e. the guard exits 1. Exit 1 is neither 0 nor 2, so the harness treats
# it as a hook that ERRORED. Do not "tidy" this back into a command substitution.
resolve_push_dir() {
  local explicit_dir="$1" cd_hint="$2" base="${CLAUDE_PROJECT_DIR:-.}"
  DIR_IS_GUESS=0
  if [ -n "$explicit_dir" ]; then
    PUSH_DIR_RESOLVED="$explicit_dir"   # `git -C <dir> push` — stated outright
  elif [ -n "$cd_hint" ]; then
    PUSH_DIR_RESOLVED="$cd_hint"        # `cd <dir> && git push` — stated in an earlier segment
  else
    PUSH_DIR_RESOLVED="$base"           # nothing said; the project dir is a GUESS, not a fact
    DIR_IS_GUESS=1
  fi
  # A relative dir in the command is relative to the tool's cwd, which we cannot see. The project
  # dir is the only base we have; use it when it resolves to something real.
  case "$PUSH_DIR_RESOLVED" in
    /*) ;;
    *) [ -d "$base/$PUSH_DIR_RESOLVED" ] && PUSH_DIR_RESOLVED="$base/$PUSH_DIR_RESOLVED" ;;
  esac

  # A HINT WE COULD NOT RESOLVE IS NOT EVIDENCE OF ANYTHING — FALL BACK, DO NOT WAVE THROUGH.
  #
  # This is the one way this rewrite could be WORSE than the guard it replaces, and it was: an
  # unresolvable dir makes `git rev-parse` fail, branch_of returns '', '' is not 'main', and the
  # push sails past a check the old guard would have made against the project dir. Two everyday
  # commands hit it — `cd - && git push` (back to the shared checkout, i.e. ONTO main) and
  # `cd $SOME_VAR && git push` (the hook sees the literal, unexpanded string). Both were BLOCKED
  # before this file was touched and ALLOWED after, until this fallback went in.
  #
  # So: if the directory we picked is not a git work tree, we learned nothing from the command.
  # Revert to the project dir and mark the answer a GUESS, which is exactly what it is.
  if ! git -C "$PUSH_DIR_RESOLVED" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    PUSH_DIR_RESOLVED="$base"
    DIR_IS_GUESS=1
  fi
}

branch_of() {
  git -C "$1" rev-parse --abbrev-ref HEAD 2>/dev/null || true
}

# --- walk the segments IN ORDER, tracking any `cd` ---------------------------------------------
cd_hint=''
while IFS= read -r raw_segment; do
  segment="$(printf '%s' "$raw_segment" | sed -E 's/^[[:space:]]+//; s/[[:space:]]+$//')"
  [ -n "$segment" ] || continue

  # A `cd <dir>` earlier in the same command tells us where a later bare push will run.
  case "$segment" in
    cd\ *)
      cd_hint="$(printf '%s' "$segment" | sed -E 's/^cd[[:space:]]+//; s/[[:space:]].*$//')"
      continue ;;
  esac

  parse_push_segment "$segment"
  [ "$PUSH_FOUND" = 1 ] || continue

  # 1. An EXPLICIT main refspec. Independent of where it runs — always denied.
  if push_names_main; then
    deny "pushing to \`main\` is blocked."
  fi

  # 2. The push resolves to whatever branch its directory is on — no refspec, or an explicit HEAD.
  if push_targets_current_branch; then
    resolve_push_dir "$PUSH_DIR" "$cd_hint"
    dir="$PUSH_DIR_RESOLVED"
    branch="$(branch_of "$dir")"
    if [ "$branch" = "main" ]; then
      if [ "$DIR_IS_GUESS" = 1 ]; then
        # We did not see a directory in the command, so we checked the project dir. Say exactly
        # that — do NOT assert the worker is on main. Under pnext-item §2 they are probably in a
        # worktree, and the project dir is on main by construction (#909).
        deny "this bare \`git push\` names no refspec, and the project dir (\`$dir\`) is on \`main\`. The command names no directory, so this guard cannot tell where the push would actually run. If you are in a worktree on a feature branch, push it by name: \`git push origin <your-branch>\`."
      fi
      deny "\`$dir\` is on \`main\`, so this bare \`git push\` would push main."
    fi
  fi
done <<<"$segments"

exit 0
