#!/usr/bin/env python3
"""Assert no cross-repo `sparse-checkout` ENUMERATES files, so a fetched script keeps its siblings.

.github#1522, closing the class behind #1510 (fired) and #1515 (latent). Epic #266.

THE DEFECT THIS CLOSES
  A reusable workflow fetches its check script from an authority repo with `actions/checkout` +
  `sparse-checkout`, and NAMES THE FILES it wants. That list is a hand-maintained duplicate of the
  script's real dependency set, living in a file that cannot execute the thing it lists. Nothing
  compares the two, so it drifts silently the moment the script gains a sibling.

  It drifts INVISIBLY here, which is the part that makes it a trap rather than a bug: every suite in
  this repo runs its gate from a FULL checkout, where every dependency is present. The breakage
  surfaces in OTHER PEOPLE'S pipelines, at load, having asserted nothing.

    #1510 — `skill-union-assert.yml` enumerated the assertion script plus ONE of the two libs it
            sources. #525 hoisted the second (`lib/roots.sh`) and never touched the list. EVERY
            receiver caller died at load in ~7 seconds having asserted nothing, found independently
            from FS.GG.SDD#718 and FS.GG.Governance#327 — never from here, where
            `skill-roots-selfcheck.yml` ran the same script green throughout.
    #1515 — `lock-range-coherence.yml` enumerated one file. NOT broken: check-lock-ranges.py imports
            stdlib only. Correct by luck, and the luck was already expiring — see below.

  Both were repaired the same way and are green today. Nothing prevented the third.

WHY THIS IS SCHEDULED RATHER THAN HYPOTHETICAL
  `scripts/lib/gate.py` (#1158/#1159) shipped a STAGED MIGRATION of this repo's ~25
  `scripts/check-*.py` gates onto a shared harness — pin-coherence first, "the rest
  opportunistically". Every gate that migrates GAINS A LOAD-TIME IMPORT. Any workflow still
  enumerating its script's files on the day its script migrates reproduces #1510 exactly, and again
  only a real consumer in another repository would notice. The trigger is a work item on a list, not
  a possibility.

WHAT THIS GATE ASSERTS — THE SYNTACTIC RULE, AND WHY THAT ONE
  For every `actions/checkout` step that names a `repository:` (a cross-repo fetch) AND declares a
  `sparse-checkout:`, every pattern must be an ANCHORED, LITERAL DIRECTORY:

    (1) ANCHORED   — begins with `/`.        [non-cone only]
    (2) DIRECTORY  — ends with `/`.          [non-cone only]
    (3) LITERAL    — no `*`, `?`, `[`, `]`.  [non-cone only]
    (4) EXISTS     — names a real directory, when the fetched repo is the one being audited.

  #1522 offered a spectrum, from a full dependency-closure gate (resolve each script's load-time
  `source`/`import` graph and prove the fetched set covers it) down to this. This is the cheap end,
  and it was chosen deliberately:

    A CLOSURE GATE CANNOT SEE RULE (1). An unanchored `scripts/` is a strict SUPERSET of `/scripts/`
    — it fetches everything the closure needs and more. A dependency-closure gate is green on it by
    construction. But the anchoring was a real finding in #1514's review, not a stylistic one: under
    `sparse-checkout-cone-mode: false` these patterns are gitignore-style, so a pattern with no
    leading or interior slash matches a directory of that name AT ANY DEPTH. This repo has six
    (`.agents|.claude|.codex/skills/work-{board,roadmap}/scripts/`), so bare `scripts/` selects 72
    paths where `/scripts/` selects 66. The elegant gate is BLIND to the exact refinement the two
    repairs landed. A gate that blesses the wrong pattern is worse than a cheaper one that does not.

    IT CANNOT FALSE-ACCUSE. Rules (1)-(3) are properties of the pattern string. There is no script
    to resolve, no import graph to be wrong about, nothing that goes stale when a script is edited.
    A dependency-closure resolver would need to keep pace with bash `source`, Python `import`,
    `from lib … import`, dynamic paths and data files — it would be a second hand-maintained model
    of the thing it is watching, which is the disease, not the cure.

    IT FORECLOSES THE CLASS WITHOUT UNDERSTANDING ANY SCRIPT. Both known instances violate it. So
    does every future instance: you cannot enumerate a file under a rule that only admits
    directories.

WHAT THIS GATE DELIBERATELY DOES NOT CATCH — say it out loud, because a green here is narrow
  * TRUE DEPENDENCY CLOSURE. A script whose load-time dependency lives OUTSIDE the fetched directory
    — `scripts/x.sh` sourcing `../shared/lib.sh`, or a gate importing a top-level `scripts/`
    sibling that is itself excluded — passes this gate and still dies at load. What this asserts is
    the SHAPE that makes closure automatic under the invariant the repo already holds ("every
    load-time dependency of a scripts/ gate lives under scripts/"), not closure itself. That
    invariant is asserted elsewhere: `scripts/generate-skill-union-bundle` inlines `scripts/lib/*`
    and `skill-union-bundle.yml` reds a bundle that still sources a sibling.
  * THE WRONG DIRECTORY, WHEN IT EXISTS. `/docs/` in place of `/scripts/` satisfies all four rules.
    Rule (4) catches the misspelt and the deleted; it cannot catch the plausible.
  * ANYTHING OUTSIDE `<root>/.github/workflows/*.{yml,yaml}`. See THE REACH OF THIS GATE below.
  * A FETCH THAT IS NOT `actions/checkout` — a `curl`, a `gh api`, a `git clone` inside a `run:`
    block. Reading `run:` text to find them is out of the question: a parser cannot tell a MENTION
    from a USE (#683), and `check-paths-coherence.py` already ruled that approach permanently out of
    scope after measuring it on this repo and getting three hits, all three false.

THE REACH OF THIS GATE VERSUS THE CLAIM IT MAKES (#266, and this repo keeps relitigating it)
  This gate reads the workflow files of ONE repository — whichever `--root` names. In CI that is
  FS-GG/.github and nothing else. It says NOTHING about the other nine FS-GG repositories.

  That is a real limit and not a theoretical one: if a sibling repo hand-rolled its own
  `actions/checkout` + `sparse-checkout` against FS-GG/.github, this gate would not see it, would
  not red, and this repo's CI would stay green over a live instance of the defect. A `grep` for
  `sparse-checkout` across all ten repositories returns nothing today — every receiver reaches the
  authority scripts through the reusable workflows here, which is why the class is currently
  confined to this repo's `.github/workflows/`. That is a fact about today's tree, not a property
  anything enforces, and it is the reason the summary line reports the repository it audited by
  name rather than saying "clean".

  Extending the reach is `repos-audit.sh`'s shape, not this gate's: it would need to read ten
  repositories, and a gate that reaches the network is a different animal with different failure
  modes. Filed rather than smuggled in.

NOTHING HERE IS A HAND-MAINTAINED LIST — that would be the same disease one level up
  The subject set is DERIVED, twice over:
    * which steps are graded: any `actions/checkout` with BOTH a `repository:` and a
      `sparse-checkout:`. Not a list of workflow names. A new reusable workflow is graded the day it
      is written, with no edit here.
    * which repository is "ours" for rule (4): read from `git remote get-url origin`, not spelled.
      A checkout of some OTHER repository cannot have its directories resolved from this tree, and
      the gate says so per step instead of quietly passing.
  The only literal in the subject definition is `actions/checkout` itself, which is the action's
  name and cannot drift without the workflows drifting with it.

CONE MODE IS EXEMPT FROM (1)-(3), BY GIT'S SEMANTICS RATHER THAN BY INDULGENCE
  Under `sparse-checkout-cone-mode: true` (actions/checkout's DEFAULT) the patterns are not
  gitignore patterns at all: git takes them as directory prefixes, rooted, with no matching. A file
  cannot be enumerated in cone mode — naming one selects a directory of that name, which is empty,
  which rule (4) catches. So the anchoring and trailing-slash rules would be asserting a spelling
  git does not read, and are not applied. `source-coherence.yml` is the live example: it fetches
  `src/FS.GG.Contracts` from FS.GG.SDD in cone mode, and it fetches source DATA it reads rather
  than a script it executes. It is green here on both counts.

SHAPES THIS GATE REFUSES RATHER THAN SKIPS (exit 3 — a skip is how a coherence gate fails open)
  * A NEGATED (`!`) pattern. Negation makes ORDER significant, so "every pattern is a directory"
    stops being a sound reading of what gets fetched. Not live in this repo; refused anyway, so the
    gate cannot quietly start being unsound the day one appears.
  * A `sparse-checkout:` that is DECLARED and yields no patterns. That is a checkout of an EMPTY
    TREE, and it is not the same fact as declaring none. Reporting it as a full clone is precisely
    the fail-open shape `tests/lock-range-coherence/sparse_set.py` was rewritten to avoid.
  * An unreadable `sparse-checkout-cone-mode:`. It decides what the patterns MEAN.
  * ZERO graded steps. A workflow-auditing gate that grades nothing has been pointed at the wrong
    tree or watched its subject set collapse. Examining nothing is a failure to audit, not a clean
    audit (#266).

  A checkout with NO `sparse-checkout:` at all is a full clone. It under-fetches nothing, so it is
  not a subject — `contract-coherence.yml` and `coordination-coherence.yml` take that shape.

EXIT CODES — THE CONTRACT (scripts/lib/gate.py)
  0  OK — every graded pattern is an anchored, literal directory.
  1  FINDING — at least one pattern enumerates a file, is unanchored, globs, or names nothing.
  3  NO VERDICT (permanent) — a workflow would not parse, a refused shape above, or nothing graded.

  There is deliberately no exit 2: this gate is pure, offline and reads only the working tree.

usage: check-sparse-checkout-closure.py [--root DIR]
"""

from __future__ import annotations

import os
from pathlib import Path
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from lib.gate import (  # noqa: E402  (path shim above must run first)
    GateError,
    base_parser,
    load_yaml,
    read_text,
    report_findings,
    report_ok,
    run,
    workflow_files,
)

NAME = "check-sparse-checkout-closure"

# The tree this gate walks. `workflow_files()` finds the files; this constant is what the walk is
# CHECKED AGAINST below, so it cannot become a decorative copy of a path used nowhere.
WORKFLOW_ROOT = ".github/workflows"

# WHAT THIS GATE READS, FOR THE WORKFLOW THAT RUNS IT (#996, epic #266). `check-paths-coherence.py`
# reads this BY AST and reds `sparse-checkout-closure.yml` if its `paths:` does not select every
# entry. It is the constant the walk uses, not a retyped copy beside it.
PATHS_SUBJECT = (WORKFLOW_ROOT,)

# The action whose `with:` block this gate grades. Steps name it as `actions/checkout@v7`,
# `actions/checkout@<sha>`, or unversioned; match on the owner/name and ignore the ref.
CHECKOUT_ACTION = "actions/checkout"

# gitignore metacharacters. Under non-cone mode a pattern containing one is a MATCH EXPRESSION, not
# a directory, and rule (4) could not resolve it even in principle.
GLOB_METACHARACTERS = ("*", "?", "[", "]")


def origin_repository(root: str) -> str | None:
    """`owner/name` for ``root``'s origin remote, or None if it cannot be read.

    DERIVED, NOT SPELLED. Rule (4) needs to know which checkouts point at the tree it is holding, and
    hardcoding `FS-GG/.github` would put a repository name in a gate that is otherwise repo-agnostic
    — a one-entry hand-maintained list, in a gate about hand-maintained lists.

    None is a legitimate answer (a tree with no origin, a git that will not run). It means rule (4)
    is UNRESOLVED for every step, which is reported per step; it never means green.
    """
    try:
        out = subprocess.run(
            ["git", "-C", root, "remote", "get-url", "origin"],
            capture_output=True,
            text=True,
            check=False,
            timeout=15,
        )
    except (OSError, subprocess.SubprocessError):
        return None
    if out.returncode != 0:
        return None
    url = out.stdout.strip()
    if not url:
        return None
    # https://github.com/OWNER/NAME.git, git@github.com:OWNER/NAME.git, and the suffix-less forms.
    tail = url.rsplit(":", 1)[-1] if "://" not in url else url.split("://", 1)[1].split("/", 1)[-1]
    tail = tail[: -len(".git")] if tail.endswith(".git") else tail
    parts = [p for p in tail.strip("/").split("/") if p]
    return "/".join(parts[-2:]) if len(parts) >= 2 else None


def sparse_steps(document: object, where: str) -> list[tuple[str, dict]]:
    """Every cross-repo `actions/checkout` step in the document, as (job id, `with:` mapping).

    A step qualifies on TWO structural facts and no others: it uses `actions/checkout`, and its
    `with:` names a `repository:`. Whether it declares a `sparse-checkout:` is decided by the caller,
    because "no sparse-checkout" (a full clone, harmless) and "an empty one" (an empty tree) are
    different facts and only one of them is fine.
    """
    found: list[tuple[str, dict]] = []
    jobs = document.get("jobs") if isinstance(document, dict) else None
    if not isinstance(jobs, dict):
        return found
    for job_id, job in jobs.items():
        if not isinstance(job, dict):
            continue
        steps = job.get("steps")
        if not isinstance(steps, list):
            continue
        for step in steps:
            if not isinstance(step, dict):
                continue
            uses = str(step.get("uses") or "").strip()
            if uses.split("@", 1)[0] != CHECKOUT_ACTION:
                continue
            params = step.get("with")
            if not isinstance(params, dict):
                continue
            if not str(params.get("repository") or "").strip():
                continue  # the caller's own checkout; there is no authority tree to under-fetch
            found.append((str(job_id), params))
    return found


def patterns_of(params: dict, where: str) -> list[str] | None:
    """The sparse patterns the runner would receive, or None when none are declared.

    `actions/checkout` splits the input on newlines and drops blanks, so a block scalar and a plain
    string reach git identically. Mirroring that rather than special-casing the YAML shape is what
    keeps a FOLDED scalar (`sparse-checkout: >`) honest: it joins its lines with spaces, so the
    runner gets ONE pattern containing a space that matches nothing. A hand parser reports two clean
    patterns there and fails open; this reports the one the runner actually gets, which then fails
    rule (2) or (4) as it should.
    """
    raw = params.get("sparse-checkout")
    if raw is None:
        return None
    entries = raw if isinstance(raw, list) else str(raw).split("\n")
    patterns = [str(entry).strip() for entry in entries if str(entry).strip()]
    if not patterns:
        raise GateError(
            f"{where}: `sparse-checkout` is declared but yields no patterns, so this step would "
            f"fetch an EMPTY TREE. That is not the same fact as declaring no sparse-checkout, and "
            f"reporting it as a full clone is the fail-open this gate exists to prevent (#266)."
        )
    return patterns


def cone_mode_of(params: dict, where: str) -> bool:
    """`sparse-checkout-cone-mode`, defaulted the way actions/checkout documents it.

    An omitted flag IS `true` — the action's default — not "unknown". It changes what the patterns
    MEAN, so an unreadable one is a no-verdict rather than a guess.
    """
    raw = params.get("sparse-checkout-cone-mode", True)
    if isinstance(raw, bool):
        return raw
    raise GateError(
        f"{where}: unreadable `sparse-checkout-cone-mode: {raw!r}`. It decides whether the patterns "
        f"are gitignore expressions or rooted directory prefixes, so the gate will not guess it."
    )


def grade_pattern(pattern: str, *, cone: bool, where: str, local_root: str | None) -> list[str]:
    """Findings for one pattern. Empty means it is an anchored, literal, existing directory."""
    if pattern.startswith("!"):
        raise GateError(
            f"{where}: negated sparse pattern {pattern!r}. Negation makes ORDER significant — a "
            f"later pattern can re-exclude an earlier one — so 'every pattern is a directory' stops "
            f"being a sound reading of what gets fetched. Refused rather than skipped (#266)."
        )

    findings: list[str] = []

    if not cone:
        if any(ch in pattern for ch in GLOB_METACHARACTERS):
            findings.append(
                f"{where}: sparse pattern {pattern!r} contains a glob metacharacter. Under "
                f"`sparse-checkout-cone-mode: false` that makes it a MATCH EXPRESSION whose result "
                f"nobody can read off the workflow file. Name the directory literally."
            )
            return findings  # the remaining rules read the pattern as a path; it is not one

        if not pattern.endswith("/"):
            findings.append(
                f"{where}: sparse pattern {pattern!r} ENUMERATES A FILE. A sparse-checkout is a "
                f"hand-maintained copy of the fetched script's dependency list, kept in a file that "
                f"cannot execute the thing it lists — it drifts silently the moment the script gains "
                f"a sibling, and the drift is invisible here because this repo's own suites run from "
                f"a full checkout (#1510 killed every receiver at load for weeks; #1515 was the same "
                f"shape, unsprung). Fetch the containing DIRECTORY instead, anchored: `/scripts/`."
            )
        if not pattern.startswith("/"):
            findings.append(
                f"{where}: sparse pattern {pattern!r} is NOT ANCHORED. Under "
                f"`sparse-checkout-cone-mode: false` these are gitignore-style patterns, so one with "
                f"no leading slash matches a directory of that name AT ANY DEPTH — a bare `scripts/` "
                f"also drags in every nested scripts/ directory (this repo has six, under the skill "
                f"bundles: 72 paths selected where `/scripts/` selects 66). Add the leading slash so "
                f"that 'this checkout is scoped to scripts/' is a true claim."
            )

    if findings:
        return findings

    # Rule (4). Only meaningful once the pattern is known to be a literal path, which is why it runs
    # last and only on a pattern the rules above have already accepted.
    if local_root is not None:
        relative = pattern.strip("/")
        if relative and not (Path(local_root) / relative).is_dir():
            findings.append(
                f"{where}: sparse pattern {pattern!r} names no directory in the repository it "
                f"fetches. The runner would materialise an EMPTY TREE and the job would die at load, "
                f"in the caller's pipeline rather than here."
            )
    return findings


def main(argv: list[str]) -> int:
    ap = base_parser(__doc__.splitlines()[0])
    args = ap.parse_args(argv)
    root = args.root

    ours = origin_repository(root)
    workflows = workflow_files(root)

    # WORKFLOW_ROOT is the gate's declared subject (PATHS_SUBJECT). Assert the walk actually stayed
    # inside it rather than trusting that the two agree: a declaration nothing checks is the kind of
    # copy this whole gate is about.
    expected_prefix = os.path.join(root, *WORKFLOW_ROOT.split("/"))
    for path in workflows:
        if not os.path.abspath(path).startswith(os.path.abspath(expected_prefix)):
            raise GateError(
                f"{path} is outside the declared subject {WORKFLOW_ROOT!r}; PATHS_SUBJECT and the "
                f"walk disagree, so the trigger this gate declares cannot be believed."
            )

    findings: list[str] = []
    graded_steps = 0
    graded_patterns = 0
    full_clones = 0
    unresolved: list[str] = []

    for path in workflows:
        document = load_yaml(read_text(path, "workflow"), f"workflow {path}")
        relative = os.path.relpath(path, root)
        for job_id, params in sparse_steps(document, relative):
            where = f"{relative} (job `{job_id}`)"
            patterns = patterns_of(params, where)
            if patterns is None:
                full_clones += 1
                continue

            repository = str(params.get("repository") or "").strip()
            cone = cone_mode_of(params, where)

            # Rule (4) is resolvable only against a tree this process is holding. A checkout of some
            # OTHER repository — or of one named by a `${{ }}` expression — is graded on the
            # syntactic rules alone, and SAYS SO. Silently applying three rules while implying four
            # is the shape #266 is about.
            resolvable = ours is not None and repository == ours
            local_root = root if resolvable else None
            if not resolvable:
                unresolved.append(
                    f"{where}: fetches {repository!r}, which is not the audited repository "
                    f"({ours or 'origin unreadable'}) — existence of its directories was NOT checked"
                )

            graded_steps += 1
            for pattern in patterns:
                graded_patterns += 1
                findings.extend(grade_pattern(pattern, cone=cone, where=where, local_root=local_root))
            mode = "cone" if cone else "non-cone"
            if not findings:
                print(f"  ok   {where:<52} {mode:<8} {' '.join(patterns)}")

    if graded_steps == 0:
        raise GateError(
            f"audited {len(workflows)} workflow(s) under {WORKFLOW_ROOT} and found NO cross-repo "
            f"`actions/checkout` declaring a `sparse-checkout`. This gate's subject set has "
            f"collapsed, or --root points at the wrong tree. Examining nothing is a failure to "
            f"audit, not a clean audit (#266)."
        )

    for note in unresolved:
        print(f"  note {note}")

    if findings:
        return report_findings(NAME, findings)

    return report_ok(
        NAME,
        f"{graded_patterns} sparse pattern(s) across {graded_steps} cross-repo checkout(s) in "
        f"{len(workflows)} workflow(s) of {ours or root} are anchored literal directories; "
        f"{full_clones} full clone(s) not graded; {len(unresolved)} step(s) not existence-checked. "
        f"Says nothing about any other repository — see THE REACH OF THIS GATE.",
    )


if __name__ == "__main__":
    sys.exit(run(main, sys.argv[1:], name=NAME))
