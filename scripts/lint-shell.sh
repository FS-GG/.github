#!/usr/bin/env bash
#
# lint-shell.sh — run a PINNED shellcheck over every piece of shell this repo authors (#648).
#
# WHY THIS EXISTS. `.github` is the repo that WRITES the org's shell and hands it out: the coordination
# kit, the sync scripts, the generators. It is also, until this gate, the repo that never read any of
# it. The hole was found from the outside: FS.GG.Game#266 put a shellcheck gate over its own shell and
# had to EXCLUDE `scripts/fsgg-coord`, because the kit is vendored there — a receiver cannot fix a
# finding in a file the next sync overwrites, and reporting one would be a false accusation against a
# file nobody in that repo owns. That exclusion is correct, and it leaves the obvious hole: the kit is
# linted in no repo at all, including the one that owns it.
#
# The evidence that nothing ever ran was already in the tree, in the form of `# shellcheck` pragmas
# written FOR a run that never happened — `scripts/lib/roots.sh` opens with `# shellcheck shell=bash`,
# and `scripts/repos-audit.sh` carries `# shellcheck source=lib/args.sh`. Directives addressed to a
# reader that did not exist.
#
# THE SUBJECT IS DISCOVERED, NOT LISTED. A hand-maintained file list goes stale in silence, and the
# silence here is a script nobody linted. `git ls-files` is the enumeration, and membership is decided
# per file below.
#
# ...AND IT IS NOT DECIDED BY EXTENSION. This is the whole reason `is_shell` reads bytes. Four of this
# repo's shell files have NO extension — `scripts/fsgg-coord`, `scripts/coordination-sync`,
# `scripts/generate-projections`, `scripts/generate-skill-union-bundle` — because they are COMMANDS,
# spelled the way they are invoked. An extension-only sweep finds 47 of 51 files, reports green over
# the four it never opened, and the first of those is `scripts/fsgg-coord`: the kit, which is to say
# the exact file this gate was filed to cover. A gate that silently skips its own subject and passes
# is epic #266's signature, so the shebang read is not thoroughness — it is the requirement.
#
# EXIT CODES. "I could not check" is a different fact from "I checked, and it is clean", and a gate
# whose entire job is reading shell does not get to conflate them (#266):
#
#   0  every discovered file is clean
#   1  shellcheck reported at least one finding
#   2  the gate could NOT RUN — no shellcheck, no git checkout. Not a verdict about the tree.
#   3  the gate discovered ZERO shell files. Not a clean tree: a broken discovery. This repo
#      demonstrably contains shell, so an empty subject means `is_shell` broke, and reporting green
#      over nothing is the failure this gate exists to end.
#
# Usage:  scripts/lint-shell.sh [--list]
#   --list            print the discovered subject and exit 0 (no linting). For debugging discovery.
#   SHELLCHECK=<path> the shellcheck binary to use (default: `shellcheck` on PATH). CI passes the
#                     pinned one, so this gate reproduces exactly on a laptop with the same pin.
#   SEVERITY=<level>  the severity floor (default: warning).
#
# THE PIN IS THE WORKFLOW'S JOB, NOT THIS SCRIPT'S. A gate whose verdict is a function of whichever
# linter the runner image happens to ship is a gate that reddens a PR over a `run:` block its author
# never wrote, on the morning GitHub bumps the image (FS.GG.Game#261 argued this and won; this repo
# takes the same pin, 0.11.0, and verifies the same checksum). But the pin lives in
# `.github/workflows/shell-lint.yml`, so that a developer can point this script at any binary to
# reproduce a finding.
#
# (A COMMENT MAY NOT BEGIN WITH THE LINTER'S NAME. `# shellcheck <word>` is a DIRECTIVE wherever it
# appears, so prose that happens to start with it is parsed as a malformed one and errors the file —
# SC1072/SC1073. This script caught that in its own header, the moment it was first tracked: until
# then it was untracked, `git ls-files` could not see it, and the gate had never once read itself.)
set -uo pipefail

SHELLCHECK="${SHELLCHECK:-shellcheck}"
SEVERITY="${SEVERITY:-warning}"

# Exit 2, not 1, when the gate cannot RUN: see the exit-code table above.
command -v git >/dev/null 2>&1 || { echo "::error::git is required by $0"; exit 2; }
command -v "$SHELLCHECK" >/dev/null 2>&1 || {
  echo "::error::shellcheck not found: '$SHELLCHECK'. Set SHELLCHECK=<path>, or install the pinned one (see .github/workflows/shell-lint.yml)."
  exit 2
}

TOP="$(git rev-parse --show-toplevel 2>/dev/null)" || { echo "::error::not inside a git checkout"; exit 2; }
cd "$TOP" || { echo "::error::cannot cd to $TOP"; exit 2; }

# IS THIS FILE SHELL? Extension first (cheap, and it is how `.sh` files are spelled), then the shebang.
#
# THE SHEBANG TEST NAMES ITS SHELLS EXACTLY, and the interpreter must be a whole PATH COMPONENT —
# anchored to the `/` or the `env ` in front of it. Both halves are load-bearing, and the second one
# was a live bug in this gate's first draft: written as `(/[^[:space:]]*)*(ba|da|k)?sh`, the leading
# group happily ate `/bin/z` and left `sh` to match the tail, so `#!/bin/zsh` was "shell". So were
# `/bin/fish`, `/bin/csh` and `/bin/tcsh` — every interpreter whose name merely ENDS in `sh`.
#
# That is not a cosmetic over-match. shellcheck cannot read any of those dialects, so it would emit
# nonsense findings against a correct fish script and red the PR of an author who has no way to
# satisfy it — the FALSE ACCUSATION this org refuses (#238), and a lint nobody can satisfy is a lint
# somebody deletes. The comment on the first draft claimed zsh was "deliberately absent" while the
# code matched it: prose asserting a property the regex did not have.
#
# The set is bash/sh/dash/ksh — exactly the dialects shellcheck speaks — spelled as a full alternation
# rather than an `(ba|da|k)?sh` cleverness that cannot say which words it admits.
#
# NO PIPE, and that is the #266 shape rather than tidiness. `head -n1 "$f" | grep -q …` under the
# `set -o pipefail` above returns 141 when grep exits first and head takes SIGPIPE — and `is_shell`
# reads any non-zero as "not shell", so a file would be SILENTLY DROPPED from the subject by a race,
# with no finding and no error. A gate that quietly stops reading its own subject is the exact defect
# this one exists to end, and tests/repos-audit already carries a leg for that race in another script.
# Reading the line with the shell's own `read` has no pipe, no fork, and no race — and it drops ~100
# processes from a 51-file sweep as a side effect.
is_shell() {
  case "$1" in
    *.sh|*.bash) return 0 ;;
  esac
  local first=''
  # An empty or unreadable file has no first line: `read` fails, and it is not shell.
  IFS= read -r first < "$1" 2>/dev/null || return 1
  # Cheap reject before the regex, and it is also what keeps a binary blob out: no `#!`, not a shebang.
  [[ $first == '#!'* ]] || return 1
  [[ $first =~ ^#![[:space:]]*([^[:space:]]*/)?(env[[:space:]]+)?(bash|sh|dash|ksh)([[:space:]]|$) ]]
}

# `git ls-files -z`, so a path with a space or a newline in it is enumerated correctly rather than
# silently split into two paths that are not files and are therefore never linted.
subject=()
while IFS= read -r -d '' f; do
  [ -f "$f" ] || continue          # a deleted-but-staged path, or a symlink to nowhere
  is_shell "$f" && subject+=("$f")
done < <(git ls-files -z)

if [ "${1:-}" = "--list" ]; then
  # Guarded: `"${subject[@]}"` on an EMPTY array is an unbound expansion under `set -u` on bash < 4.4
  # (macOS still ships 3.2), and even where it is legal `printf '%s\n'` with no argument prints a
  # blank line — so `--list | wc -l` would answer 1 for a tree with no shell at all.
  [ "${#subject[@]}" -eq 0 ] || printf '%s\n' "${subject[@]}"
  exit 0
fi

# ZERO IS NOT GREEN — it is exit 3. See the exit-code table.
if [ "${#subject[@]}" -eq 0 ]; then
  echo "::error::discovered ZERO shell files. This repo contains shell, so discovery is broken — not the tree."
  exit 3
fi

echo "shell-lint: shellcheck $("$SHELLCHECK" --version | awk '/^version:/{print $2}'), severity=$SEVERITY, ${#subject[@]} file(s)"

# `-x` follows `source`d files, which this repo needs: `scripts/lib/*.sh` are sourced fragments, and
# without -x every caller reads as using undefined functions.
#
# One invocation over the whole subject, NOT one per file: shellcheck's `source` resolution and its
# exit code are both cleaner that way, and a parse failure is REPORTED (SC1009/SC1073 are `error`
# severity, which any floor at or below `warning` includes) rather than swallowed as a clean file.
rc=0
"$SHELLCHECK" -x -S "$SEVERITY" -f gcc "${subject[@]}" || rc=$?

case "$rc" in
  0) echo "shell-lint: OK — ${#subject[@]} file(s) clean at severity '$SEVERITY'." ;;
  1) echo "::error::shellcheck reported findings (see above)." ;;
  *) echo "::error::shellcheck exited $rc, which is neither clean (0) nor findings (1) — treating as 'could not check'."
     exit 2 ;;
esac

exit "$rc"
