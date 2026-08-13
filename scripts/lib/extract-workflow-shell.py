#!/usr/bin/env python3
"""extract-workflow-shell.py — pull every bash/sh `run:` block out of this repo's GitHub Actions
workflow and composite-action YAML, materializing each as a standalone shell file `scripts/lint-shell.sh`
can hand straight to shellcheck (.github#2493).

WHY THIS EXISTS. `.github#2493`: `scripts/lint-shell.sh`'s subject is FILE-shaped — `git ls-files`
filtered by extension or shebang — so it has never once opened a `run:` block embedded in a `.yml`
workflow. `.github#2489` shipped a claim ("CI runs the pinned shellcheck") that was true of the repo's
`.sh`/`.bash`/shebang files and false of the workflow shell it actually touched; the gap is structural,
not that PR's mistake. `.github#2346` is the same shape one level up: a real quoting defect shipped
silently inside `kit-auto-publish.yml`'s own escalation step and was found only in production, in
exactly the kind of shell this script now reaches.

WHY STRUCTURAL, NOT GREP. `lint-shell.sh`'s own header already lived through the file-discovery version
of this mistake once — an extension-only sweep found 47 of 51 shell files and silently skipped
`scripts/fsgg-coord`, the exact file #648 was filed about. A `run:` block is not a file; a `.yml`
extension says nothing about which of its lines are shell. Only a real YAML parse (`tests/kit-auto-publish
/run.sh`'s `extract_escalation_step` already does this for one hand-picked step) finds every step
regardless of which job or file it next moves to.

WHAT THIS DOES NOT DO. It never runs shellcheck and never decides pass/fail — `lint-shell.sh` does both,
over these files plus its own file-discovered subject, through the one pinned binary and one severity
floor (the acceptance criteria's "same pinned binary and severity floor as the rest of the repo's
shell"). This script's whole job is discovery and materialization; treat its exit code as "could I
produce a subject", not as a verdict on the tree.

GITHUB EXPRESSIONS (`${{ ... }}`) ARE NOT SHELL SYNTAX. GitHub Actions substitutes them TEXTUALLY into
the script before any shell ever parses it, so handing shellcheck the literal `${{ }}` would not parse
as shell at all (`${` opens a parameter expansion that `{` cannot legally continue) — every step that
uses one would report a syntax error, drowning any real finding. Deleting the expression outright is
just as wrong: it erases the exact unquoted-expansion / injection risk shellcheck exists to flag, and
that risk is real here — `.github#2346`'s defect was a `${{ }}`-carried value breaking a double-quoted
assignment. So every expression is replaced with a BRACED variable reference, `${GHA_EXPR}` — braced
specifically because an unbraced `$GHA_EXPR` immediately followed by literal text starting `[` (a real
shape in this repo: `"${{ ... }}[bot]"`) reads to shellcheck as unbraced array-subscript syntax and
raises a self-inflicted SC1087 that has nothing to do with the author's script.

Two substitution passes, in order — because a THIRD self-inflicted finding showed up empirically before
this two-pass split existed:

  1. An expression that is the ENTIRE content of a single-quoted token — `'${{ x }}'`, nothing else
     inside the quotes — is replaced INCLUDING its quotes, with `"${GHA_EXPR}"` (a real, double-quoted
     variable reference). Single quotes never interpolate, so leaving them in place would hand
     shellcheck two adjacent STRING LITERALS to compare in the extremely common
     `[ '${{ github.event_name }}' = 'workflow_dispatch' ]` shape — and literal-vs-literal is
     indistinguishable, to a tool that cannot see GitHub's own substitution stage, from a typo'd
     comparison that can never be true (SC2050, "did you forget the $ on a variable?"). It is not a
     typo: the left side is genuinely dynamic, just not dynamic in a way bash's own parser can see.
     Promoting it to a real variable reference is what makes that dynamism visible to shellcheck too.
  2. Every remaining `${{ ... }}` (embedded in a larger string, or bare/unquoted) is replaced in place
     with `${GHA_EXPR}`, preserving whatever quoting already surrounded it:

         "...${{ steps.x.outputs.y }}..."   ->   "...${GHA_EXPR}..."   (inside the author's quotes)
         --flag ${{ inputs.value }}         ->   --flag ${GHA_EXPR}    (still bare — SC2086 can fire)

Both passes preserve whatever quoting the author actually wrote wherever it is preservable, so
shellcheck's read of that quoting is the same read it would give the substituted value at runtime — the
property this is for. `.github#2493`'s own PR discussion is the record of the two false positives (one
per pass) this two-pass design replaced a naive single-pass substitution to eliminate; see its
Verification notes for the exact shellcheck output before and after.

SHELL SELECTION MIRRORS `is_shell()` IN `lint-shell.sh`: only `bash` and `sh` steps are admitted. GitHub
Actions also allows `pwsh`, `python`, `cmd`, `powershell`, or a custom interpreter string, none of which
shellcheck can read — admitting one would be the exact #238 false-accusation shape `is_shell()` already
refused for file discovery (a lint nobody can satisfy is a lint somebody deletes). A step's effective
shell is: step `shell:` > job `defaults.run.shell` > workflow `defaults.run.shell` > the runner's own
default. This repo's own workflows run only on `ubuntu-latest` (`grep -rh runs-on: .github/workflows/ |
sort -u` is exactly one line), whose GitHub-hosted default is bash, so an un-annotated `run:` step here
is admitted as bash. A COMPOSITE action step is different: GitHub never defaults its shell at all, so a
composite `run:` step with no `shell:` is invalid workflow YAML — this script treats that as a discovery
failure (exit 2) rather than silently guessing a shell, per the same "I could not check is not I looked
and it's clean" contract `lint-shell.sh` already enforces (#266).

EXIT CODES (a SUBSET of `lint-shell.sh`'s, because this script only ever discovers-and-writes):
  0  ran to completion; zero or more shell files were materialized under <out-dir> (see stdout)
  2  could not establish the subject — unreadable/unparseable YAML, or a composite `run:` step with
     no `shell:`. Never 0 with a partial subject silently dropped.
"""
from __future__ import annotations

import os
import re
import subprocess
import sys

ADMITTED_SHELLS = {"bash", "sh"}
GHA_EXPR = re.compile(r"\$\{\{.*?\}\}", re.DOTALL)


def substitute_expressions(text: str) -> str:
    """Replace every `${{ ... }}` with the BRACED reference `${GHA_EXPR}`, strictly IN PLACE — nothing
    outside the matched span is touched, so whatever quote structure the author wrote survives byte for
    byte around it. See the module docstring's GITHUB EXPRESSIONS section for why braced (SC1087) and
    why strictly in-place: an earlier draft also rewrote a whole enclosing `'${{ x }}'` token to a
    double-quoted reference to dodge a different self-inflicted finding (SC2050), and that rewrite is
    what introduced SC2027 on `.github/workflows/repos-audit.yml`'s `"...'${{ x }}'..."` shape — the
    `'` characters there are literal bytes inside an OUTER double-quoted string, not real quote syntax,
    and only a real shell tokenizer can tell the two apart. Leaving quote bytes alone entirely is the
    one transform that cannot make that mistake; see .github#2493's PR notes for both false positives
    and the revert between them."""
    return GHA_EXPR.sub("${GHA_EXPR}", text)


def git_ls_files(repo_root: str) -> list[str]:
    out = subprocess.run(
        ["git", "-C", repo_root, "ls-files", "-z"],
        check=True,
        capture_output=True,
    ).stdout
    return [p for p in out.decode("utf-8", "surrogateescape").split("\0") if p]


def is_candidate(path: str) -> bool:
    """A workflow, or a composite/reusable action manifest — discovered by PATH SHAPE, which is what
    GitHub Actions itself uses to decide these are workflow/action YAML at all (workflows must live at
    `.github/workflows/*.yml`; an action manifest is always named `action.yml`/`action.yaml` at the
    root of the action it defines, wherever that root is)."""
    if path.startswith(".github/workflows/") and path.endswith((".yml", ".yaml")):
        return True
    return os.path.basename(path) in ("action.yml", "action.yaml")


def slug(text: str) -> str:
    s = re.sub(r"[^A-Za-z0-9]+", "-", text).strip("-").lower()
    return s or "step"


def effective_shell(declared, job_shell, workflow_shell):
    chosen = declared or job_shell or workflow_shell or "bash"
    # A custom shell string may carry an invocation template, e.g. "bash {0}" — the interpreter is
    # always the first word.
    return chosen.split()[0] if isinstance(chosen, str) and chosen.strip() else chosen


def iter_steps(doc, path: str):
    """Yield (origin_label, run_text, shell) for every `run:` step this script can classify.

    Steps whose shell it cannot read (not bash/sh) are silently omitted — see module docstring; that is
    not a discovery failure. A composite step with NO shell at all raises :class:`DiscoveryError`: that
    is invalid workflow YAML, not a shell to guess at.
    """
    if not isinstance(doc, dict):
        return
    jobs = doc.get("jobs")
    if isinstance(jobs, dict):
        wf_shell = ((doc.get("defaults") or {}).get("run") or {}).get("shell")
        for job_id, job in jobs.items():
            if not isinstance(job, dict):
                continue
            job_shell = ((job.get("defaults") or {}).get("run") or {}).get("shell")
            steps = job.get("steps")
            if not isinstance(steps, list):
                continue
            for i, step in enumerate(steps):
                if not (isinstance(step, dict) and isinstance(step.get("run"), str)):
                    continue
                sh = effective_shell(step.get("shell"), job_shell, wf_shell)
                if sh not in ADMITTED_SHELLS:
                    continue
                label = f"{path}:{job_id}:{i:02d} {step.get('name') or '<unnamed step>'}"
                yield label, step["run"], sh
        return

    runs = doc.get("runs")
    if isinstance(runs, dict) and runs.get("using") == "composite":
        steps = runs.get("steps")
        if isinstance(steps, list):
            for i, step in enumerate(steps):
                if not (isinstance(step, dict) and isinstance(step.get("run"), str)):
                    continue
                declared = step.get("shell")
                if not declared:
                    raise DiscoveryError(
                        f"{path}: composite step {i} ('{step.get('name', '<unnamed>')}') has a run: "
                        "block with no shell: — GitHub Actions never defaults one for a composite "
                        "step, so this is invalid workflow YAML, not a shell for this script to guess."
                    )
                sh = effective_shell(declared, None, None)
                if sh not in ADMITTED_SHELLS:
                    continue
                label = f"{path}:action:{i:02d} {step.get('name') or '<unnamed step>'}"
                yield label, step["run"], sh


def extract(repo_root: str, out_dir: str) -> list[tuple[str, str]]:
    """Return a list of (materialized_path, origin_label) pairs. Raises :class:`DiscoveryError` on any
    unreadable/unparseable candidate or invalid composite step — never drops one silently."""
    import yaml  # deferred: only paid for once a candidate actually exists

    os.makedirs(out_dir, exist_ok=True)
    written: list[tuple[str, str]] = []
    for path in sorted(p for p in git_ls_files(repo_root) if is_candidate(p)):
        full = os.path.join(repo_root, path)
        try:
            with open(full, encoding="utf-8") as fh:
                doc = yaml.safe_load(fh)
        except (OSError, yaml.YAMLError) as e:
            raise DiscoveryError(f"{path}: could not read/parse as YAML: {e}") from e

        for label, run_text, shell in iter_steps(doc, path):
            # "wf__" prefix: `path` starts with ".github/…", and a leading "." makes the materialized
            # name a dotfile — invisible to a bare `ls`/glob and easy to leave behind by accident.
            out_name = f"wf__{path.replace('/', '__')}__{len(written):04d}.sh"
            out_path = os.path.join(out_dir, out_name)
            shebang = "#!/usr/bin/env bash\n" if shell == "bash" else "#!/bin/sh\n"
            body = substitute_expressions(run_text)
            if not body.endswith("\n"):
                body += "\n"
            with open(out_path, "w", encoding="utf-8") as out:
                out.write(shebang)
                out.write(body)
            written.append((out_path, label))
    return written


def main(argv: list[str]) -> int:
    if len(argv) != 3:
        print("usage: extract-workflow-shell.py <repo-root> <out-dir>", file=sys.stderr)
        return 2
    _, repo_root, out_dir = argv
    try:
        pairs = extract(repo_root, out_dir)
    except DiscoveryError as e:
        print(f"::error::{e}", file=sys.stderr)
        return 2
    except subprocess.CalledProcessError as e:
        print(f"::error::git ls-files failed: {e}", file=sys.stderr)
        return 2
    for out_path, label in pairs:
        print(f"{out_path}\t{label}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
