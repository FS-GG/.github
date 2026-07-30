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

    (1) ANCHORED   — begins with `/`.                        [non-cone only]
    (2) DIRECTORY  — ends with `/`.                          [non-cone only]
    (3) LITERAL    — no whitespace, no `..`, no `* ? [ ]`.   [both modes]
    (4) SELECTS    — brings at least one TRACKED path with it, when the fetched repo is the
                     audited one and git will say.           [both modes, when resolvable]

  #1522 offered a spectrum, from a full dependency-closure gate (resolve each script's load-time
  `source`/`import` graph and prove the fetched set covers it) down to this. This is the cheap end,
  and it was chosen deliberately:

    A CLOSURE GATE CANNOT SEE RULE (1). An unanchored `scripts/` is a strict SUPERSET of `/scripts/`
    — it fetches everything the closure needs and more. A dependency-closure gate is green on it by
    construction. But the anchoring was a real finding in #1514's review, not a stylistic one: under
    `sparse-checkout-cone-mode: false` these patterns are gitignore-style, so a pattern with no
    leading or interior slash matches a directory of that name AT ANY DEPTH — and this repo has SIX
    such directories (`.agents|.claude/skills/work-{board,roadmap}/scripts/`), every one of
    which a bare `scripts/` drags in. The elegant gate is BLIND to the exact refinement the two
    repairs landed. A gate that blesses the wrong pattern is worse than a cheaper one that does not.

    (The count of EXTRA DIRECTORIES is the load-bearing number and is stable. An absolute pair of
    selected-path counts was written here first and was stale in the same commit that introduced it,
    because that commit added a file under scripts/ — a hand-typed count of a derived quantity,
    drifting at birth, inside a gate about hand-maintained duplicates. It has been removed.)

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
  * A CONE-MODE FETCH OF A REPOSITORY THIS GATE CANNOT RESOLVE. Nothing can be asserted about it —
    see CONE MODE below — so it is reported UNGRADED and counted separately. It is NOT folded into
    the green claim, because "I could not look" is not "I looked and it is fine" (#266). The obvious
    cheap rule for it, "a final component containing a dot is a file", was considered and REJECTED:
    the one live cone-mode subject in this repo is `src/FS.GG.Contracts`, a directory with two dots
    in its name, so that heuristic would false-accuse the only real instance it would ever see.
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
  name and cannot drift without the workflows drifting with it. Since #1553 that literal, and the
  qualification it serves, are `lib/sparse.py`'s and are shared with `sparse_set.py` — which had been
  answering the same question with no `uses:` check at all and a case-SENSITIVE `repository:`.

CONE MODE IS EXEMPT FROM (1)-(2), BY GIT'S SEMANTICS RATHER THAN BY INDULGENCE
  Under `sparse-checkout-cone-mode: true` (actions/checkout's DEFAULT) the patterns are not
  gitignore patterns at all: git takes them as directory prefixes, rooted, with no matching. So the
  anchoring and trailing-slash rules would be asserting a spelling git does not read, and are not
  applied. Rule (3) still is — whitespace and `..` break a cone pattern just as thoroughly.

  BUT THE EXEMPTION HAS A PRICE, AND IT IS PAID EXPLICITLY. A file CAN be named in cone mode; git
  simply reads it as a directory of that name, which turns out to be empty. Nothing about the
  pattern STRING distinguishes that from a real directory — only rule (4) can, by asking the tree.
  So for a cone-mode fetch of a repository this gate cannot resolve, no rule detects enumeration at
  all. Such a step is counted UNGRADED and named on its own line, never printed `ok` and never
  counted toward the green claim. The first draft of this gate printed `ok` over exactly that shape
  and asserted in its summary that the patterns were "anchored literal directories" — a sentence
  about work it had not done, over #1510's own shape one repository across. That is the failure this
  gate exists to end, so it is not one it may commit.

  `source-coherence.yml` is the live example: it fetches `src/FS.GG.Contracts` from FS.GG.SDD in
  cone mode. It is reported UNGRADED, honestly, rather than green.

SHAPES THIS GATE REFUSES RATHER THAN SKIPS (exit 3 — a skip is how a coherence gate fails open)
  * A NEGATED (`!`) pattern. Negation makes ORDER significant, so "every pattern is a directory"
    stops being a sound reading of what gets fetched. Not live in this repo; refused anyway, so the
    gate cannot quietly start being unsound the day one appears.
  * A `sparse-checkout:` KEY THAT SUPPLIES NO PATTERN — `""`, `"   "`, `[]`, or a bare key with no
    value. All four are refused ALIKE, because the runner cannot tell them apart either; giving them
    different verdicts would be this gate inventing a distinction its subject does not make.
  * An unreadable `sparse-checkout-cone-mode:` — an unevaluated `${{ }}`, say. It decides what the
    patterns MEAN. A QUOTED boolean is not unreadable and is accepted; see `lib/sparse.py`.
  * NO CROSS-REPO CHECKOUT ANYWHERE. A workflow-auditing gate that found not one has been pointed at
    the wrong tree. Note what this is NOT: a tree whose cross-repo checkouts are all FULL CLONES is
    coherent — that is the class permanently foreclosed, the best end state there is — and it exits
    0. Redding the ideal outcome would be a gate arguing for its own subject's survival.

  A checkout with NO `sparse-checkout:` at all is a full clone. It under-fetches nothing, so it is
  not a subject — `contract-coherence.yml` and `coordination-coherence.yml` take that shape.

  Refusals are COLLECTED, not raised on sight: one unreadable step must not suppress every real
  finding in the rest of the tree. Findings print first, then the refusals, then exit 3.

  THE FIRST THREE OF THOSE REFUSALS ARE NOT THIS GATE'S ANY MORE (#1530). READING the block —
  newline-splitting the patterns the way the action does, defaulting the cone flag the way the action
  documents, refusing a declared-but-empty block — lives in `lib/sparse.py` and is shared with
  `tests/lock-range-coherence/sparse_set.py`, which resolves the same block through real
  `git sparse-checkout`. The two had each written that reading out independently and had already
  drifted in two places; the fourth refusal (a NEGATED pattern) and all of the GRADING in rules
  (1)-(4) are judgements about what was read, and stay here.

EXIT CODES — THE CONTRACT (scripts/lib/gate.py)
  0  OK — every graded pattern is an anchored, literal directory that selects something.
  1  FINDING — at least one pattern enumerates a file, is unanchored, globs, or selects nothing.
  3  NO VERDICT (permanent) — a workflow would not parse, a refused shape above, or no cross-repo
     checkout anywhere.

  There is deliberately no exit 2: this gate is pure, offline and reads only the local repository.

usage: check-sparse-checkout-closure.py [--root DIR]
"""

from __future__ import annotations

import os
from pathlib import PurePosixPath
import subprocess
import sys
from typing import Callable, NamedTuple

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from lib.gate import (  # noqa: E402  (path shim above must run first)
    ExitCode,
    GateError,
    base_parser,
    load_yaml,
    read_text,
    report_findings,
    report_ok,
    run,
    workflow_files,
)

# THE PARSE IS NOT THIS GATE'S (#1530). What patterns and cone flag a checkout step declares is read
# by `lib/sparse.py`, which `tests/lock-range-coherence/sparse_set.py` also takes. The three readings
# it holds — the newline split that makes a folded scalar visible, the omitted cone flag that IS
# `true`, the declared-but-empty block that is an empty tree — were correct in two files only because
# the second author had read the first, and they had ALREADY drifted apart in two places. What stays
# here is the GRADING (rules 1-4), which is this gate's alone.
#
# AND SINCE #1553, SO IS THE STEP SELECTOR. "Is this step an `actions/checkout` aimed at repository R"
# was still answered twice — and the second copy read no `uses:` at all and compared `repository:`
# case-SENSITIVELY, where this gate's own `repo-casing` leg asserts a case-variant spelling must still
# resolve. The qualification is now `lib/sparse.py`'s `checkout_steps` / `repository_matches`; what
# stays here is which of those steps this gate wants (all of them) and the GRADING (rules 1-4).
from lib.sparse import (  # noqa: E402  (path shim above must run first)
    SparseRefusal,
    checkout_steps,
    cone_mode_of,
    patterns_of,
    repository_matches,
)

NAME = "check-sparse-checkout-closure"

# The tree this gate walks. `workflow_files()` finds the files; this constant is what the walk is
# CHECKED AGAINST below, so it cannot become a decorative copy of a path used nowhere.
WORKFLOW_ROOT = ".github/workflows"

# WHAT THIS GATE READS, FOR THE WORKFLOW THAT RUNS IT (#996, epic #266). `check-paths-coherence.py`
# reads this BY AST and reds `sparse-checkout-closure.yml` if its `paths:` does not select every
# entry. It is the constant the walk uses, not a retyped copy beside it.
#
# It is the workflow tree ONLY, and that is a genuine incompleteness rather than an oversight: rule
# (4) also consults git's index for whatever directory a pattern happens to name, which is not
# statically knowable — the read-set is decided by the workflows being graded. A `paths:` entry
# cannot express "wherever those patterns point", so the declaration covers the part that is
# declarable and this comment covers the rest.
PATHS_SUBJECT = (WORKFLOW_ROOT,)

# The action whose `with:` block this gate grades — `actions/checkout`, matched on owner/name,
# casefolded, with the ref ignored — is `lib/sparse.py`'s `CHECKOUT_ACTION` since #1553. It is not
# restated here: a constant retyped beside the reading that uses it is the duplicate this gate's whole
# subject is about.

# gitignore metacharacters. A pattern containing one is a MATCH EXPRESSION rather than a directory,
# and rule (4) could not resolve it even in principle.
GLOB_METACHARACTERS = ("*", "?", "[", "]")




def origin_repository(root: str) -> str | None:
    """`owner/name` for ``root``'s origin remote, or None if it cannot be read.

    DERIVED, NOT SPELLED. Rule (4) needs to know which checkouts point at the tree it is holding, and
    hardcoding `FS-GG/.github` would put a repository name in a gate that is otherwise repo-agnostic
    — a one-entry hand-maintained list, in a gate about hand-maintained lists.

    None is a legitimate answer (a tree with no origin, a git that will not run). It means rule (4)
    cannot run for any step, which is reported per step; it never means rule (4) PASSED.

    A remote that is not a URL with a host — a path-style remote like `/srv/git/foo` — returns None
    rather than a slug invented from its last two components. A fabricated `owner/name` is worse than
    no answer: it could collide with a `repository:` a workflow really names, and resolve rule (4)
    against a tree that is not that repository at all.
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

    if "://" in url:
        tail = url.split("://", 1)[1]
        if "/" not in tail:
            return None
        tail = tail.split("/", 1)[1]  # drop the host
    elif "@" in url and ":" in url.split("@", 1)[1]:
        tail = url.split("@", 1)[1].split(":", 1)[1]  # scp-style git@host:owner/name
    else:
        return None  # not a URL with a host; do not invent an owner

    tail = tail[: -len(".git")] if tail.endswith(".git") else tail
    parts = [part for part in tail.strip("/").split("/") if part]
    return "/".join(parts[-2:]) if len(parts) >= 2 else None


def tracked_paths(root: str) -> set[str] | None:
    """Every path git has in ``root``'s index, or None if git will not answer.

    RULE (4) ASKS GIT, NOT THE FILESYSTEM, and the difference is the whole value of the rule. An
    `is_dir()` is true for an UNTRACKED directory — `obj/`, `bin/`, `node_modules/`, a stray
    `artifacts/` — none of which the runner would receive, so a pattern naming one selects an empty
    tree and the filesystem answer is a fail-open. Worse, it makes the verdict a function of whatever
    is lying in the developer's working tree rather than of the repository.

    It is also true for a directory reached through `..` or through a symlink, which is an answer
    from outside the repository entirely. Asking the index makes those impossible to express: an
    index path is always repo-relative and never begins with `..`.
    """
    try:
        out = subprocess.run(
            ["git", "-C", root, "ls-files", "-z"],
            capture_output=True,
            text=True,
            check=False,
            timeout=60,
        )
    except (OSError, subprocess.SubprocessError):
        return None
    if out.returncode != 0:
        return None
    paths = {entry for entry in out.stdout.split("\0") if entry}
    return paths or None


def selects_anything(relative: str, tracked: set[str]) -> bool:
    """Would this literal DIRECTORY prefix bring any tracked path with it?

    Strictly under the prefix, and an exact match against a tracked path is deliberately NOT enough.
    That is what makes rule (4) able to see a file named in CONE mode: git reads a cone pattern as a
    directory, so `scripts/lib/args.sh` selects the (non-existent, therefore empty) directory of that
    name, not the file. Counting the file itself as a hit would have reported #1510's cone-mode twin
    as green — which is the one thing rule (4) is carrying alone in that mode.

    In non-cone mode the question never arises: rule (2) has already required a trailing slash, so
    the prefix is a directory before this is asked.
    """
    if not relative:
        # The whole repository. An OVER-fetch, which this gate does not forbid — the defect class is
        # fetching too little. Spelled out rather than falling out of an empty string by accident.
        return True
    prefix = relative + "/"
    return any(path.startswith(prefix) for path in tracked)


def sparse_steps(document: object) -> list[tuple[str, dict]]:
    """Every cross-repo `actions/checkout` step in the document, as (job id, `with:` mapping).

    A step qualifies on TWO structural facts and no others: it uses `actions/checkout` (casefolded,
    ref ignored), and its `with:` names a non-empty `repository:`. That qualification is
    `lib/sparse.py`'s (#1553) and is NOT restated here; this gate's own answer is the FILTER, which is
    "all of them" — every cross-repo checkout is a subject, and rules 1-4 below do the grading.
    Whether a step declares a `sparse-checkout:` is decided by the caller of this function, because
    "no sparse-checkout" (a full clone, harmless) and "a key supplying nothing" differ.

    KEPT AS A NAMED FUNCTION ON THIS MODULE ON PURPOSE, AND `main()` BELOW DOES NOT CALL IT.
    `scripts/repos-audit.sh` loads this file by path and asserts `sparse_steps` among the seven names
    it borrows (#1529), so this is a published interface with a caller that fails CLOSED — and loudly,
    naming the symbol — if it disappears. Its two-tuple shape is part of that contract. `main()` takes
    `checkout_steps` directly instead, because it needs the `repository:` the qualification read and
    this shape drops it; deleting this delegate as "unused" is what the borrower's check is for.
    """
    return [(step.job_id, step.params) for step in checkout_steps(document)]


def grade_pattern(pattern: str, *, cone: bool, where: str, tracked: set[str] | None) -> list[str]:
    """Findings for one pattern. Empty means every rule that COULD run did, and passed."""
    if pattern.startswith("!"):
        raise GateError(
            f"{where}: negated sparse pattern {pattern!r}. Negation makes ORDER significant — a "
            f"later pattern can re-exclude an earlier one — so 'every pattern is a directory' stops "
            f"being a sound reading of what gets fetched. Refused rather than skipped (#266)."
        )

    # WHITESPACE, IN BOTH MODES. This is the folded-scalar defect as the runner actually receives it:
    # `sparse-checkout: >` joins its lines with a space, so git gets ONE pattern containing a space
    # and matches nothing. Catching it HERE rather than leaving it to rule (4) is what makes the
    # folded-scalar claim true even for a repository whose tree this gate cannot resolve.
    if any(character.isspace() for character in pattern):
        return [
            f"{where}: sparse pattern {pattern!r} contains whitespace, so it is ONE pattern that "
            f"matches nothing and fetches an empty tree. This is what a FOLDED block scalar "
            f"(`sparse-checkout: >`) does to its lines — use a literal block scalar (`|`)."
        ]

    if ".." in PurePosixPath(pattern).parts:
        return [
            f"{where}: sparse pattern {pattern!r} contains a `..` component. git does not resolve "
            f"one here, so it selects nothing — and a path that climbs out of the repository is not "
            f"something a checkout can fetch."
        ]

    if any(character in pattern for character in GLOB_METACHARACTERS):
        return [
            f"{where}: sparse pattern {pattern!r} contains a glob metacharacter. That makes it a "
            f"MATCH EXPRESSION whose result nobody can read off the workflow file. Name the "
            f"directory literally."
        ]

    findings: list[str] = []
    if not cone:
        if not pattern.endswith("/"):
            findings.append(
                f"{where}: sparse pattern {pattern!r} ENUMERATES A FILE. A sparse-checkout is a "
                f"hand-maintained copy of the fetched script's dependency list, kept in a file that "
                f"cannot execute the thing it lists — it drifts silently the moment the script gains "
                f"a sibling, and the drift is invisible here because this repo's own suites run from "
                f"a full checkout (#1510 killed every receiver at load; #1515 was the same shape, "
                f"unsprung). Fetch the containing DIRECTORY instead, anchored: `/scripts/`."
            )
        if not pattern.startswith("/"):
            findings.append(
                f"{where}: sparse pattern {pattern!r} is NOT ANCHORED. Under "
                f"`sparse-checkout-cone-mode: false` these are gitignore-style patterns, so one with "
                f"no leading slash matches a directory of that name AT ANY DEPTH — a bare `scripts/` "
                f"also drags in the four nested scripts/ directories under this repo's skill bundles. "
                f"Add the leading slash so 'this checkout is scoped to scripts/' is a true claim."
            )

    if findings:
        return findings

    # Rule (4), and only on a pattern the rules above have already reduced to a literal path.
    if tracked is not None and not selects_anything(pattern.strip("/"), tracked):
        findings.append(
            f"{where}: sparse pattern {pattern!r} selects no tracked path in the repository it "
            f"fetches. The runner would materialise an EMPTY TREE and the job would die at load, in "
            f"the caller's pipeline rather than here."
        )
    return findings


class StepVerdict(NamedTuple):
    """One cross-repository checkout, graded without rendering or I/O.

    ``resolver`` is deliberately a callback: the gate has one local git index while the roster
    audit can request and cache a different tree per repository.  Making either caller pre-resolve
    the index is the orchestration duplication this seam removes.
    """

    where: str
    repository: str
    patterns: list[str] | None
    cone: bool | None
    findings: list[str]
    refusal: str | None
    full_clone: bool
    resolvable: bool
    enumeration_checked: bool
    resolution_kind: str | None
    resolution_reason: str | None


Resolver = Callable[[str], tuple[str, set[str] | None, str | None]]


def grade_document(document: object, where_prefix: str, *, resolver: Resolver) -> list[StepVerdict]:
    """Grade every cross-repo checkout in ``document`` using the caller's tree resolver.

    The result is structured so callers choose their own rendering and retry policy, while parsing,
    full-clone handling, refusals, rule-4 applicability and enumeration accounting remain one rule.
    """
    verdicts: list[StepVerdict] = []
    for job_id, repository, params in checkout_steps(document):
        where = f"{where_prefix} (job `{job_id}`)"
        try:
            patterns = patterns_of(params, where)
            if patterns is None:
                verdicts.append(StepVerdict(where, repository, None, None, [], None, True, False,
                                            False, None, None))
                continue
            cone = cone_mode_of(params, where)
        except SparseRefusal as error:
            verdicts.append(StepVerdict(where, repository, None, None, [], str(error), False, False,
                                        False, None, None))
            continue

        kind, tracked, reason = resolver(repository)
        resolvable = tracked is not None
        enumeration_checked = (not cone) or resolvable
        try:
            findings = [finding for pattern in patterns for finding in grade_pattern(
                pattern, cone=cone, where=where, tracked=tracked)]
            refusal = None
        except GateError as error:
            findings, refusal = [], str(error)
        verdicts.append(StepVerdict(where, repository, patterns, cone, findings, refusal, False,
                                    resolvable, enumeration_checked, kind, reason))
    return verdicts


def main(argv: list[str]) -> int:
    ap = base_parser(__doc__.splitlines()[0])
    args = ap.parse_args(argv)
    root = args.root

    ours = origin_repository(root)
    tracked = tracked_paths(root)
    workflows = workflow_files(root)

    # A TRIPWIRE, not an independent check: `workflow_files()` builds its paths from this same root
    # and directory, so nothing can make this fire today. It exists so that widening that helper —
    # to recurse, say — cannot silently widen this gate's subject past the PATHS_SUBJECT it declares.
    expected = os.path.abspath(os.path.join(root, *WORKFLOW_ROOT.split("/")))
    for path in workflows:
        if os.path.dirname(os.path.abspath(path)) != expected:
            raise GateError(
                f"{path} is outside the declared subject {WORKFLOW_ROOT!r}; PATHS_SUBJECT and the "
                f"walk disagree, so the trigger this gate declares cannot be believed."
            )

    findings: list[str] = []
    refusals: list[str] = []
    graded_steps = 0
    graded_patterns = 0
    full_clones = 0
    cross_repo_steps = 0
    ungraded: list[str] = []
    unresolved: list[str] = []

    for path in workflows:
        document = load_yaml(read_text(path, "workflow"), f"workflow {path}")
        relative = os.path.relpath(path, root)
        def resolve(repository: str) -> tuple[str, set[str] | None, str | None]:
            if tracked is not None and repository_matches(repository, ours):
                return "ok", tracked, None
            return (
                "unresolved",
                None,
                f"fetches {repository!r}, which is not the audited repository "
                f"({ours or 'origin unreadable'}) — rule (4) did not run",
            )

        for verdict in grade_document(document, relative, resolver=resolve):
            cross_repo_steps += 1
            # A refusal is COLLECTED rather than raised on sight, so one unreadable step cannot
            # suppress every real finding in the rest of the tree. The exit code is still 3.
            #
            # `SparseRefusal` is `lib/sparse.py`'s no-verdict, deliberately NOT a `GateError`: that
            # module has no dependency on this harness, so the mapping from "unreadable block" to
            # "this gate's exit 3" is made HERE, where the exit-code contract lives.
            if verdict.full_clone:
                full_clones += 1
                continue
            if verdict.refusal:
                refusals.append(verdict.refusal)
                continue
            assert verdict.patterns is not None and verdict.cone is not None
            graded_patterns += len(verdict.patterns)
            findings.extend(verdict.findings)
            if verdict.enumeration_checked:
                graded_steps += 1
            else:
                ungraded.append(
                    f"{verdict.where}: cone mode against {verdict.repository!r}, which this gate cannot resolve — "
                    f"NOTHING was asserted about whether these patterns name files or directories"
                )
            # Per-STEP, never the global accumulator: a clean step after a dirty one must still print
            # its `ok` line, or the reader cannot tell "graded and clean" from "never reached".
            if verdict.resolution_reason:
                unresolved.append(f"{verdict.where}: {verdict.resolution_reason}")
            if not verdict.findings and verdict.enumeration_checked:
                mode = "cone" if verdict.cone else "non-cone"
                print(f"  ok   {verdict.where:<52} {mode:<8} {' '.join(verdict.patterns)}")

    for note in ungraded:
        print(f"  UNGRADED {note}")
    for note in unresolved:
        print(f"  note {note}")

    # NOT `graded_steps == 0`. A tree whose cross-repo checkouts are ALL full clones is the class
    # permanently foreclosed — the best end state available — and redding it would be this gate
    # arguing for its own subject's survival. What is genuinely suspect is finding no cross-repo
    # checkout AT ALL, which means the parser or the --root is wrong.
    if cross_repo_steps == 0:
        raise GateError(
            f"audited {len(workflows)} workflow(s) under {WORKFLOW_ROOT} and found NO cross-repo "
            f"`actions/checkout` at all. This gate's subject set has collapsed, or --root points at "
            f"the wrong tree. Examining nothing is a failure to audit, not a clean audit (#266)."
        )

    if findings:
        report_findings(NAME, findings)
    if refusals:
        raise GateError(
            f"{len(refusals)} step(s) could not be graded, and a shape this gate cannot read is not "
            f"one it will pass:\n  - " + "\n  - ".join(refusals)
        )
    if findings:
        return int(ExitCode.FINDING)

    return report_ok(
        NAME,
        f"{graded_patterns} sparse pattern(s) across {graded_steps} fully-graded cross-repo "
        f"checkout(s) in {len(workflows)} workflow(s) of {ours or root}; {full_clones} full "
        f"clone(s) not graded; {len(ungraded)} step(s) UNGRADED; {len(unresolved)} step(s) without "
        f"rule (4). Says nothing about any other repository — see THE REACH OF THIS GATE.",
    )


if __name__ == "__main__":
    sys.exit(run(main, sys.argv[1:], name=NAME))
