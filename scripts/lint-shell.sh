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
# THE SUBJECT IS ALSO NOT ONLY FILES (.github#2493). A `run:` block embedded in
# `.github/workflows/*.yml` (or a composite action's `action.yml`) is real, hand-written shell too, and
# nothing above ever opens one — `.github#2489` shipped claiming "CI runs the pinned shellcheck" for
# exactly this shape of shell, and it did not, until now. `scripts/lib/extract-workflow-shell.py` does
# a real YAML parse to find every `run:` block whose effective shell is bash or sh (never a grep, for
# the identical reason `is_shell()` above reads bytes instead of trusting a `paths:` filter), and this
# script lints its output through the SAME pinned binary, severity floor, and check set as the
# file-based subject — no blanket exclusion either. The one shellcheck finding the `${{ }}` substitution
# itself can manufacture (SC2050, on a narrow quoting shape) is suppressed PER LINE, inline in the
# materialized file, only on the exact lines the extractor rewrote into that shape — see
# `scripts/lib/extract-workflow-shell.py`'s `substitute_expressions` for why a blanket `-e SC2050` here
# was tried and rejected: it also hid a genuine SC2050 typo with no `${{ }}` involvement at all.
#
# EXIT CODES. "I could not check" is a different fact from "I checked, and it is clean", and a gate
# whose entire job is reading shell does not get to conflate them (#266):
#
#   0  every discovered file AND workflow-embedded step is clean
#   1  shellcheck reported at least one warning/error finding, in either subject
#   4  shellcheck could not follow a sourced file (SC1091), in the file-based subject. Add a
#      file-scoped `source-path=SCRIPTDIR` pragma to the sourcing script; this is distinct
#      from a finding because part of the subject was not read.
#   2  the gate could NOT RUN — no shellcheck, no python3, no git checkout, or the workflow/action YAML
#      could not be parsed. Not a verdict about the tree.
#   3  the gate discovered ZERO shell files AND ZERO workflow-embedded steps. Not a clean tree: a
#      broken discovery. This repo demonstrably contains shell, so an empty subject means discovery
#      broke, and reporting green over nothing is the failure this gate exists to end.
#
# Usage:  scripts/lint-shell.sh [--list]
#   --list            print the discovered subject (file-based, then workflow-embedded) and exit 0
#                     (no linting). For debugging discovery.
#   SHELLCHECK=<path> the shellcheck binary to use (default: `shellcheck` on PATH). CI passes the
#                     pinned one, so this gate reproduces exactly on a laptop with the same pin.
#   SEVERITY=<level>  the severity floor (default: warning).
#   python3           required, to discover workflow-embedded shell (.github#2493) — see
#                     `scripts/lib/extract-workflow-shell.py`.
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

# IGNORE SIGPIPE. `--list` now prints the workflow-embedded subject too (.github#2493) — several
# hundred descriptive lines, easily past a single pipe buffer — so a caller piping it into an
# early-exiting reader (`--list | grep -q PATTERN`, `--list | head`) can catch this script's OWN
# `printf` mid-write with the read end already closed. Left untrapped, that is SIGPIPE (128+13=141),
# which under a CALLER's `set -o pipefail` (`tests/shell-lint/run.sh` carries one) makes the WHOLE
# PIPELINE read as a failure even when the reader found exactly the match it was looking for — this
# is not hypothetical, it is what `.github#2493`'s own fixture leg hit the moment `--list` grew past
# one pipe buffer. Ignoring SIGPIPE turns a killed write into an ordinary EPIPE the finite `--list`
# block below simply finishes past (nothing after it depends on the write having succeeded), so this
# script's own exit code is always the one `exit 0`/`exit N` actually chosen, never a signal artifact.
trap '' PIPE

SHELLCHECK="${SHELLCHECK:-shellcheck}"
SEVERITY="${SEVERITY:-warning}"

# THIS SCRIPT'S OWN DIRECTORY, resolved BEFORE `cd "$TOP"` below changes cwd to the SCANNED repo. The
# two are not always the same tree: `tests/shell-lint/run.sh` invokes this exact file against disposable
# synthetic repos under `$TMPDIR` to prove discovery (#648, and now .github#2493's embedded-shell legs),
# and `scripts/lib/extract-workflow-shell.py` ships beside THIS script, not inside whatever repo it is
# pointed at. Resolving the extractor from `$TOP` instead — the scanned repo — is exactly the bug this
# line exists to prevent: it made every fixture leg on a synthetic repo (none of which vendor this
# repo's own `scripts/lib/`) fail with "could not discover workflow-embedded shell" the moment the
# extractor was wired in, caught only by actually running the fixture rather than the real tree.
GATE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")" && pwd)"

# Exit 2, not 1, when the gate cannot RUN: see the exit-code table above.
command -v git >/dev/null 2>&1 || { echo "::error::git is required by $0"; exit 2; }
command -v "$SHELLCHECK" >/dev/null 2>&1 || {
  echo "::error::shellcheck not found: '$SHELLCHECK'. Set SHELLCHECK=<path>, or install the pinned one (see .github/workflows/shell-lint.yml)."
  exit 2
}
# scripts/lib/extract-workflow-shell.py needs a real YAML parse of every workflow/action manifest
# (.github#2493) — grep cannot tell which lines of a `.yml` are a `run:` block, the same reason
# `is_shell()` below reads bytes instead of trusting a `paths:` filter.
command -v python3 >/dev/null 2>&1 || {
  echo "::error::python3 not found. It is required to discover shell embedded in .github/workflows/*.yml (.github#2493)."
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

# WORKFLOW-EMBEDDED SHELL (.github#2493). A `run:` block in `.github/workflows/*.yml` (or a composite
# action's `action.yml`) is not a FILE, so nothing above ever discovers it — `.github#2489` shipped
# claiming "CI runs shellcheck" for exactly the shell this section now reaches, and it did not. The
# extractor is a real YAML parse (`scripts/lib/extract-workflow-shell.py`), never a grep, for the same
# reason `is_shell()` reads bytes instead of trusting an extension: a `paths:`-style shortcut here would
# have the identical #648 shape one layer up.
wf_dir="$(mktemp -d)" || exit 2
trap 'rm -rf "$wf_dir"' EXIT
wf_manifest="$(mktemp)" || exit 2
trap 'rm -rf "$wf_dir"; rm -f "$wf_manifest"' EXIT
if ! python3 "$GATE_DIR/lib/extract-workflow-shell.py" "$TOP" "$wf_dir" >"$wf_manifest" 2>&1; then
  echo "::error::could not discover workflow-embedded shell (.github#2493):"
  cat "$wf_manifest" >&2
  exit 2
fi
wf_subject=()
wf_labels=()
while IFS=$'\t' read -r wf_path wf_label; do
  [ -n "$wf_path" ] || continue
  wf_subject+=("$wf_path")
  wf_labels+=("$wf_label")
done < "$wf_manifest"

if [ "${1:-}" = "--list" ]; then
  # Guarded: `"${subject[@]}"` on an EMPTY array is an unbound expansion under `set -u` on bash < 4.4
  # (macOS still ships 3.2), and even where it is legal `printf '%s\n'` with no argument prints a
  # blank line — so `--list | wc -l` would answer 1 for a tree with no shell at all.
  [ "${#subject[@]}" -eq 0 ] || printf '%s\n' "${subject[@]}"
  [ "${#wf_labels[@]}" -eq 0 ] || printf 'workflow-embedded: %s\n' "${wf_labels[@]}"
  exit 0
fi

# ZERO IS NOT GREEN — it is exit 3. See the exit-code table. Combined across BOTH subjects: a tree with
# real `.sh` files but zero workflow-embedded shell despite `.github/workflows/*.yml` existing is not
# "clean", it is this section's own discovery having broken — see the fixture leg that pins a known real
# step in the subject for the regression guard this line alone cannot provide.
total=$(( ${#subject[@]} + ${#wf_subject[@]} ))
if [ "$total" -eq 0 ]; then
  echo "::error::discovered ZERO shell files (file-based or workflow-embedded). This repo contains shell, so discovery is broken — not the tree."
  exit 3
fi

echo "shell-lint: shellcheck $("$SHELLCHECK" --version | awk '/^version:/{print $2}'), severity=$SEVERITY, ${#subject[@]} file(s), ${#wf_subject[@]} workflow-embedded step(s)"

# `-x` follows `source`d files, which this repo needs: `scripts/lib/*.sh` are sourced fragments, and
# without -x every caller reads as using undefined functions. Workflow-embedded steps are checked in
# their OWN pass below — they are never `-x`-followed here, because none of this repo's `run:` blocks
# `source` anything, and a materialized temp file has no repo-relative directory to follow one from
# even if it did.
#
# One invocation over the whole subject, NOT one per file: shellcheck's `source` resolution and its
# exit code are both cleaner that way, and a parse failure is REPORTED (SC1009/SC1073 are `error`
# severity, which any floor at or below `warning` includes) rather than swallowed as a clean file.
log="$(mktemp)" || exit 2
trap 'rm -rf "$wf_dir"; rm -f "$wf_manifest" "$log"' EXIT
if [ "${#subject[@]}" -gt 0 ]; then
  "$SHELLCHECK" -x -S info -f gcc "${subject[@]}" >"$log" 2>&1 || true
  if grep -q 'SC1091' "$log"; then
    echo "::error::shellcheck could not follow a sourced file (SC1091). Add # shellcheck source-path=SCRIPTDIR to the sourcing script."
    exit 4
  fi
fi

rc=0
findings_log="$(mktemp)" || exit 2
trap 'rm -rf "$wf_dir"; rm -f "$wf_manifest" "$log" "$findings_log"' EXIT
if [ "${#subject[@]}" -gt 0 ]; then
  "$SHELLCHECK" -x -S "$SEVERITY" -f gcc "${subject[@]}" >>"$findings_log" 2>&1 || rc=$?
fi
wf_rc=0
if [ "${#wf_subject[@]}" -gt 0 ]; then
  # NO `-e SC2050` HERE. `scripts/lib/extract-workflow-shell.py` marks the ONE shape its `${{ }}`
  # substitution cannot make shellcheck-clean by construction — `'${{ x }}'` as the WHOLE content of a
  # single-quoted comparison operand, the common `[ '${{ github.event_name }}' = 'workflow_dispatch' ]`
  # shape — with a PER-LINE `# shellcheck disable=SC2050` pragma, inline in the materialized file
  # itself (see that script's `substitute_expressions` for exactly which lines qualify and why). A
  # blanket `-e SC2050` on this whole invocation was the first version of this fix, and .github#2493's
  # own review round 1 proved it wrong: it also hides a GENUINE SC2050 typo with zero `${{ }}`
  # involvement anywhere in the 360-step subject — `[ 'GITHUB_REF_NAME' = 'main' ]`, missing the `$` —
  # which is exactly the shape of defect this item exists to stop the gate from silently missing. The
  # severity floor and pinned binary here are identical to the file-discovered subject's invocation;
  # the per-line pragmas are the only difference, and each one is provably scoped to a line THIS
  # extractor's own substitution produced, never to an author's line it never touched.
  "$SHELLCHECK" -S "$SEVERITY" -f gcc "${wf_subject[@]}" >>"$findings_log" 2>&1 || wf_rc=$?
fi

# Re-label materialized temp paths back to their origin before a human reads them. The materialized
# name embeds the workflow's own filename (`wf__.github__workflows__ci.yml__0000.sh`), so it carries
# regex metacharacters (`.`) that must be ESCAPED before use as a sed PATTERN — an unescaped one would
# still usually match its own literal path (a wildcarded `.` still matches the same character), but
# "usually" is not "always", and a pattern that can silently under- or over-match is exactly the kind
# of quiet failure this file's own header refuses elsewhere.
if [ -s "$findings_log" ]; then
  idx=0
  while [ "$idx" -lt "${#wf_subject[@]}" ]; do
    escaped="$(printf '%s' "${wf_subject[$idx]}" | sed 's/[.[\*^$/]/\\&/g')"
    sed -i "s|$escaped|workflow-embedded: ${wf_labels[$idx]}|g" "$findings_log" 2>/dev/null || true
    idx=$((idx + 1))
  done
  cat "$findings_log"
fi

# Combine: either subject's shellcheck exiting outside {0,1} means "could not check" for the WHOLE
# gate (#266) — a partial no-verdict silently reported as the other subject's clean is the same failure
# this exit-code table exists to prevent. Otherwise, either subject reporting 1 makes the gate 1.
for code in "$rc" "$wf_rc"; do
  case "$code" in
    0|1) ;;
    *) echo "::error::shellcheck exited $code, which is neither clean (0) nor findings (1) — treating as 'could not check'."
       exit 2 ;;
  esac
done
if [ "$rc" -eq 1 ] || [ "$wf_rc" -eq 1 ]; then
  echo "::error::shellcheck reported findings (see above)."
  exit 1
fi

echo "shell-lint: OK — ${#subject[@]} file(s) and ${#wf_subject[@]} workflow-embedded step(s) clean at severity '$SEVERITY'."
exit 0
