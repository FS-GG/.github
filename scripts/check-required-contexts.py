#!/usr/bin/env python3
"""Assert every required status check a branch demands is a context some workflow can actually produce.

.github#549, epic #266 (coherence gates that fail open). Found while working FS.GG.Audio#60 — the
first FS-GG repo to require a reusable-workflow-nested context.

THE DEFECT THIS CLOSES. When a caller invokes a reusable workflow, GitHub names the resulting check
`<caller job> / <callee job>`. FS.GG.Audio's branch protection requires exactly that string:

    lock-ranges / lock-ranges
    ^^^^^^^^^^^   ^^^^^^^^^^^
    Audio's job   the job id inside FS-GG/.github's lock-range-coherence.yml, at @main

HALF OF THAT REQUIRED CONTEXT IS OWNED BY ANOTHER REPO, AND TRACKED AT A MOVING REF. Rename the
`lock-ranges:` job in FS-GG/.github — an ordinary, apparently-internal refactor, in a repo whose CI
is green, reviewed by people with no reason to look downstream — and Audio's gate starts reporting
`lock-ranges / <newname>`. The context Audio REQUIRES is then never reported, so GitHub holds every
PR at "Expected — waiting for status to be reported", forever. Audio's protection is
`enforce_admins: true` with no required reviews, so there is no bypass: the whole repo stops
merging, no commit in Audio changed, and the cause is a commit somewhere else.

A REUSABLE WORKFLOW'S JOB IDS ARE THEREFORE PUBLIC API, exactly as its `permissions:` block (#478)
and its secrets (#482) are. This is the fourth coupling across the `workflow_call` boundary that
neither side can see and no check asserted. See docs/coordination/reusable-workflow-contract.md.

WHAT IT ASSERTS. For the repo it is pointed at:

  every context in `branches/<branch>/protection`'s required status checks
      is PRODUCIBLE — reported by some workflow in the working tree on EVERY pull request,
      and DURABLE   — every cross-repo `uses:` its producers cross tracks a ref this org has
                      decided a required verdict may rest on (.github#1783; see the block on
                      ACCEPTED_MOVING_REFS below, which IS that decision).

The two are separate defects with separate repairs. A context that never reports deadlocks the repo
at "Expected — waiting for status to be reported". A context that reports through a moving foreign
ref merges, and then answers differently tomorrow on byte-identical content — which branch
protection cannot defend against at all, because a green required check is not a durable statement.

"Producible" means reported on EVERY pull request, and that definition is load-bearing (.github#1508).
It is deliberately NOT the weaker "produced by some workflow that triggers on `pull_request`" this
gate used to settle for. The weaker one is strictly WIDER, and only the stronger one is what a
required context actually needs: a workflow whose `pull_request` trigger carries a `paths:` filter
DOES trigger on `pull_request`, and still reports nothing on a PR that touches none of those paths.
See PATH FILTERS below.

It is computed STATICALLY, from the YAML — not by sampling live check runs. That is the
whole point: a check run that has not reported yet and a check run that can NEVER report look
identical at any given moment, and it is the second one that deadlocks the repo. Only the static
derivation can tell them apart, and it catches the plain typo in a protection setting for free — a
misspelled required context is indistinguishable from a renamed one, and both deadlock identically.

PATH FILTERS ARE PART OF THE SUBJECT (.github#1508). GitHub does NOT synthesise a neutral or skipped
check run for a workflow its `paths:`/`paths-ignore:` filter excluded — it never creates the check run
at all. Branch protection cannot tell "excluded by a filter" from "has not reported yet", so it holds
the PR at "Expected — waiting for status to be reported" indefinitely. A required context produced
only by a path-filtered workflow therefore blocks every PR that touches none of those paths, which is
normally MOST of them. The two repairs are: drop the filter so the job always runs and always reports,
or stop requiring the context. This gate reports the combination; it does not choose between them.

  Found in docs/coordination/skill-union-assertion.md, whose receiver caller was `paths:`-filtered to
  the three ADR-0011 skill roots AND told the receiver to make the resulting context required. Seven
  rostered receivers were queued behind that instruction. This gate scored the shape GREEN, because
  the filtered workflow does trigger on `pull_request` — the exact fail-open (#266) it exists to close.

  A filtered producer does NOT condemn a context some OTHER workflow reports on every PR: the question
  is whether the context always reports, not whether one of its producers is filtered. Nor does a
  filter that excludes nothing (`paths: ["**"]`), or a filter on any event other than the PR ones — a
  `push:` filter has no bearing on what a pull request sees. Nor, crucially, does GitHub's OWN
  documented remedy for this ("Handling skipped but required checks"): a `paths: P` producer beside a
  no-op `paths-ignore: P` one reporting the same context covers every pull request between them, and
  is reported as fine. Coverage by any OTHER combination of filters is not computable here, and is a
  no-verdict rather than a guess in either direction.

WHICH PULL-REQUEST TRIGGER FILTERS THIS MODELS
  `paths:`/`paths-ignore:`, `branches:`/`branches-ignore:` against the audited base branch, and
  `types:`. A type set must contain at least one normal PR event (`opened`, `synchronize`,
  `reopened`); a superset is wider and remains producible.

HOW A CONTEXT IS DERIVED (this is GitHub's naming, and getting it wrong would make the gate lie)
  normal job        -> the job's `name:` if it has one, else its job id
  `uses:` job       -> "<caller display> / <callee job display>", one context per job in the callee,
                       recursively (a callee may itself call a reusable workflow)
  matrix job        -> "<display> (v1, v2)" for each combination, in the matrix's declaration order

  A workflow that does NOT trigger on `pull_request` produces NOTHING on a PR. A context required
  from such a workflow can never report, and is a finding — not a skip.

  A workflow whose `pull_request` trigger is PATH-FILTERED produces nothing on a PR that touches none
  of those paths. A context required only from such a workflow is a finding too, for the same reason.

EVERY ONE OF THESE IS AN ERROR, NOT A SKIP
  - A required context no `pull_request` workflow can produce      (the repo is deadlocked, or will be)
  - A required context produced only behind a `paths:` filter      (deadlocked for most PRs — #1508)
  - A required context whose producer reaches its callee through a cross-repo `uses:` at a ref that is
    neither a 40-hex commit nor an ACCEPTED_MOVING_REF                             (no verdict — #1783)
  - A required context whose producers are ALL filtered, in a combination whose joint coverage cannot
    be computed, or whose filter is written in a shape that cannot be read (both exit 3, no verdict)
  - A workflow, on either side, that will not parse
  - A callee that cannot be resolved at the ref its caller pins
  - A matrix this gate cannot enumerate (`include`/`exclude`/an expression). Guessing would produce
    a WRONG producible set — and a wrong set that happens to contain the required context is a
    VACUOUS GREEN over a deadlocked repo. Refuse instead.

WHAT IT DELIBERATELY DOES NOT JUDGE
  A required context whose `app_id` is not GitHub Actions. A third-party CI app's contexts are not
  derivable from this repo's YAML, and red-lighting them would be a gate crying wolf about a repo it
  cannot see. They are counted and named, never guessed at.

  A branch with NO protection, or none requiring status checks: that is a real answer, and the
  invariant holds vacuously. It is reported as such, loudly enough that "this repo requires nothing"
  cannot be misread as "everything this repo requires is fine".

GITHUB KEEPS REQUIRED CHECKS IN TWO SEPARATE STORES, AND READING ONE IS A VACUOUS GREEN (#574)
  `branches/<b>/protection` (classic) does NOT report ruleset rules, and `rules/branches/<b>`
  (rulesets) does NOT report classic protection. A branch may be governed by either, both, or
  neither, and GitHub enforces BOTH — so the required set is their UNION.

  This gate read classic protection alone, and took its 404 to mean "not protected, requires
  nothing". FS.GG.Governance is protected by a repository RULESET requiring five status checks, and
  answers 404 on the classic endpoint — so the gate reported `requires NO status checks` and exited
  0 over a fully-protected repo, holding an admin token. A 404 from ONE store is not an answer
  about the branch; it is an answer about that store.

Usage:
  check-required-contexts.py --repo <owner/name> [--root <dir>] [--branch main]
                             [--protection <file>]   # a saved classic payload; skips that API call
                             [--rules <file>]        # a saved ruleset payload; skips that API call
Exit: 0 = every required context is producible AND durable; 1 = at least one can never report, or
rests on a ref a required verdict may not rest on (both are findings about a required check, and the
message says which); 2 = no verdict,
RETRYABLE — the API could not be read (rate limit, outage); 3 = no verdict, PERMANENT — protection
is unreadable for want of permission, a workflow will not parse, a callee is missing, or a matrix
cannot be enumerated.

"I could not check" must never share an exit code with "I checked, and it's fine" (#266) — nor with
"I checked, and it's broken" (#320) — nor "try again" with "a human must fix a file" (#335).
"""
from __future__ import annotations

import argparse
import fnmatch
import glob
import itertools
import json
import os
import re
import subprocess
import sys
import time
import traceback

import yaml

OK, FINDING, NO_VERDICT_RETRYABLE, NO_VERDICT_PERMANENT = 0, 1, 2, 3

AUTHORITY = "FS-GG/.github"

# GitHub Actions' own app id. A required check from any other app is produced by something outside
# this repo's YAML, and is therefore not ours to derive. Overridable for the fixture.
ACTIONS_APP_ID = int(os.environ.get("FSGG_ACTIONS_APP_ID", "15368"))

# `uses: <owner>/<repo>/.github/workflows/<file>@<ref>` — a remote reusable workflow.
REMOTE_USES_RE = re.compile(
    r"^(?P<repo>[^/]+/[^/]+)/\.github/workflows/(?P<file>[^@/]+\.ya?ml)@(?P<ref>.+)$"
)
# `uses: ./.github/workflows/<file>` — a local one, always resolved against the working tree.
LOCAL_USES_RE = re.compile(r"^\./\.github/workflows/(?P<file>[^/]+\.ya?ml)$")

# A 40-hex commit. The only ref that is immutable by construction: a tag is not (.github#1784).
SHA_RE = re.compile(r"^[0-9a-f]{40}$")

# THE DECISION OF .github#1783, IN THE FORM SOMETHING EXECUTES.
#
# ADR-0067 §2: a gate's verdict MUST be a pure function of (tree under test, pinned ref). A required
# context produced through a cross-repo `uses:` at a MOVING ref is not that — the org measured the
# cost as `FS.GG.SDD#724`, green on merged SHA 0376309 at 08:15Z and red on byte-identical content at
# 08:21Z (.github#1584). Eleven required contexts across all seven receivers are produced that way
# today, every one of them `@main`.
#
# #1783 DECIDED to accept `@main` for those calls rather than pin them, and the reasoning — including
# why pinning `uses:` would NOT by itself have discharged §2 — is in
# docs/coordination/reusable-workflow-contract.md, "Reusable-workflow calls are NOT pinned". Read it
# before widening this set: adding a ref here is re-deciding that, and it is a decision, not a fix
# for a red gate.
#
# What the decision does NOT accept is any OTHER moving ref. `@main` is a tip the hub's own required
# checks have passed; `@some-branch`, `@v1` or a fork's ref is a required verdict sourced from a tree
# nobody gated, and nothing detected that before this. A 40-hex commit is accepted because it is the
# property §2 actually asks for.
ACCEPTED_MOVING_REFS = frozenset({"main"})

GH_TRIES = int(os.environ.get("FSGG_CONTEXT_TRIES", "3"))
GH_RETRY_DELAY = float(os.environ.get("FSGG_CONTEXT_RETRY_DELAY", "2"))
GH_TIMEOUT = float(os.environ.get("FSGG_CONTEXT_TIMEOUT", "30"))

# GitHub refuses to nest reusable workflows beyond 4 levels; a cycle would otherwise hang the gate.
MAX_DEPTH = 8


class GateError(Exception):
    """A condition under which the gate must fail rather than skip. Maps to exit 3."""


class Unreachable(Exception):
    """We do not know what is there. Maps to exit 2 — never to green, never to a finding."""


class Missing(Exception):
    """A 404. That is a real answer from the API, not a failure to reach it."""


class Forbidden(Exception):
    """A 403/401. The token cannot see this. A human must grant a permission; retrying will not."""


def gh_api(*args: str) -> str:
    """Read the GitHub API, distinguishing "not there" (404) from "not allowed" (403) from "could
    not tell" (anything else). Mirrors scripts/check-workflow-permissions.py's transport so the
    fixture can stub `gh` on PATH."""
    delay = GH_RETRY_DELAY
    for attempt in range(1, GH_TRIES + 1):
        try:
            proc = subprocess.run(
                ["gh", "api", *args],
                capture_output=True, text=True, check=False, timeout=GH_TIMEOUT,
            )
        except subprocess.TimeoutExpired:
            if attempt >= GH_TRIES:
                raise Unreachable(f"gh api {' '.join(args)} timed out after {GH_TIMEOUT}s") from None
            time.sleep(delay)
            delay *= 2
            continue
        if proc.returncode == 0:
            return proc.stdout
        err = " ".join(proc.stderr.split())
        # Match the HTTP status, not gh's prose — the status is the API's contract.
        if re.search(r"\(HTTP 404\)", err):
            raise Missing(err)
        if re.search(r"\(HTTP 40[13]\)", err):
            raise Forbidden(err)
        if attempt >= GH_TRIES:
            raise Unreachable(err or f"gh exited {proc.returncode}")
        time.sleep(delay)
        delay *= 2
    raise Unreachable("unreachable")  # pragma: no cover


def load_yaml(text: str, what: str) -> dict:
    try:
        doc = yaml.safe_load(text)
    except yaml.YAMLError as e:
        raise GateError(f"{what}: not parsable as YAML — {e}") from e
    if not isinstance(doc, dict):
        raise GateError(f"{what}: not a YAML mapping")
    return doc


def triggers(doc: dict) -> dict:
    """The `on:` block. PyYAML resolves the bare key `on` to the boolean True (YAML 1.1), so a plain
    doc["on"] misses it and EVERY workflow would look like it does not trigger on pull_request —
    deriving an empty producible set and reporting every required context as a finding."""
    for key in ("on", True):
        if key in doc:
            got = doc[key]
            if isinstance(got, dict):
                return got
            if isinstance(got, list):
                return {k: None for k in got}
            if isinstance(got, str):
                return {got: None}
    return {}


# The pull-request events. Either one puts a check run on a PR, so either one, unfiltered, is enough
# to make a context report on every PR.
PR_EVENTS = ("pull_request", "pull_request_target")
DEFAULT_PR_TYPES = frozenset({"opened", "synchronize", "reopened"})

# A `paths:` entry matching EVERY changed file, so a filter naming it excludes no pull request and is
# not the #1508 defect. Reporting one would be the gate crying wolf about a repo that is fine.
#
# `**/*` is deliberately NOT here. In GitHub's filter patterns `*` does not span `/`, so `**/*`
# requires at least one slash and does NOT match a root-level file — a PR touching only README.md
# would get no check run. `**` alone is safe under either reading, and the fail-CLOSED choice on a
# pattern whose semantics are not documented precisely is to keep it out of this set.
UNIVERSAL_PATHS = frozenset({"**"})


class PathFilter:
    """One `paths:`/`paths-ignore:` filter on one pull-request event of one workflow.

    Carries the four facts a message needs — which workflow, which event, which key, which entries —
    because "which filter, on which event" is the whole of the repair, and a finding that merely says
    "a filter" sends the reader hunting.

    `entries` is None for a filter shape this gate cannot model (an empty list, a mapping, a list with
    a non-string in it). That is recorded as a filter rather than as "no filter", which is the FAIL-
    CLOSED direction: an unmodellable shape can then only ever cost a REQUIRED context its verdict,
    never hand one a green it did not earn.
    """

    __slots__ = ("workflow", "event", "key", "entries")

    def __init__(self, workflow: str, event: str, key: str, entries: tuple[str, ...] | None) -> None:
        self.workflow, self.event, self.key, self.entries = workflow, event, key, entries

    def __str__(self) -> str:
        shown = "a shape this gate cannot model" if self.entries is None else repr(list(self.entries))
        return f"{self.workflow} `on.{self.event}.{self.key}:` {shown}"

    @property
    def excluded(self) -> str:
        """Which pull requests this filter withholds the check run from.

        `paths:` and `paths-ignore:` exclude OPPOSITE sets, and one message for both would tell half
        its readers the exact inverse of which PRs deadlock.
        """
        if self.key == "paths":
            return "a pull request that touches none of those paths"
        if self.key == "paths-ignore":
            return "a pull request that touches only those paths"
        if self.key.startswith("branches"):
            return "a pull request targeting the audited branch"
        return "an ordinary pull request open, push, or reopen"


def _event_path_filter(cfg: object, event: str, workflow: str, branch: str) -> PathFilter | None:
    """The path filter on ONE pull-request event, or None if it excludes no pull request."""
    if not isinstance(cfg, dict):
        return None  # `pull_request:` bare, or listed in `on: [pull_request]` — no filter at all
    for key in ("paths", "paths-ignore"):
        if key not in cfg:
            continue
        raw = cfg[key]
        if isinstance(raw, str):
            raw = [raw]
        if not isinstance(raw, list) or not raw or not all(isinstance(p, str) for p in raw):
            return PathFilter(workflow, event, key, None)
        entries = tuple(p.strip() for p in raw)
        # A `paths:` naming a universal pattern excludes nothing — UNLESS it also SUBTRACTS. GitHub
        # supports `!`-negated entries, and `paths: ["**", "!docs/**"]` runs on everything EXCEPT a
        # docs-only PR. Blessing that as universal would score the #1508 deadlock green in the very
        # shape a receiver reaches for when told to widen its filter. `paths-ignore:` gets no such
        # shortcut at all: every entry it carries subtracts pull requests, which is the defect.
        if (
            key == "paths"
            and any(e in UNIVERSAL_PATHS for e in entries)
            and not any(e.startswith("!") for e in entries)
        ):
            return None
        return PathFilter(workflow, event, key, entries)

    for key in ("branches", "branches-ignore"):
        if key not in cfg:
            continue
        raw = cfg[key]
        if isinstance(raw, str): raw = [raw]
        if not isinstance(raw, list) or not raw or not all(isinstance(p, str) for p in raw):
            return PathFilter(workflow, event, key, None)
        matches = any(fnmatch.fnmatchcase(branch, p) for p in raw)
        if (key == "branches" and matches) or (key == "branches-ignore" and not matches):
            continue
        return PathFilter(workflow, event, key, tuple(raw))

    if "types" in cfg:
        raw = cfg["types"]
        if isinstance(raw, str): raw = [raw]
        if not isinstance(raw, list) or not raw or not all(isinstance(p, str) for p in raw):
            return PathFilter(workflow, event, "types", None)
        if not DEFAULT_PR_TYPES.intersection(raw):
            return PathFilter(workflow, event, "types", tuple(raw))
    return None


def pr_path_filters(doc: dict, workflow: str, branch: str) -> list[PathFilter] | None:
    """The filters withholding this workflow from some pull requests; None if it runs on every one.

    THE DEFECT THIS MODELS (.github#1508). "Triggers on `pull_request`" is a strictly WIDER claim than
    "reports on every pull request", and only the second is what a required context needs. A
    path-filtered workflow satisfies the first and fails the second, and GitHub does not skip the
    resulting required check — it never creates the check run, so branch protection waits forever.

    Only the PR events are consulted. A `push:` filter, however narrow, has no bearing on what a pull
    request sees, and treating one as a filter would report a deadlock over a perfectly healthy repo.

    If the workflow declares BOTH PR events and either is unfiltered, it reports on every PR — the
    unfiltered one is enough on its own, so the filter on the other is not a finding.
    """
    trig = triggers(doc)
    out: list[PathFilter] = []
    for event in PR_EVENTS:
        if event not in trig:
            continue
        got = _event_path_filter(trig[event], event, workflow, branch)
        if got is None:
            return None
        out.append(got)
    return out or None


def filters_cover_every_pr(filters: list[PathFilter]) -> bool:
    """True when these filters JOINTLY leave no pull request without a check run.

    Exactly ONE arrangement is decided here, and it is the one GitHub documents as the remedy for a
    required path-filtered check ("Handling skipped but required checks"): a real job filtered
    `paths: P` beside a no-op job reporting the SAME context filtered `paths-ignore: P`, on the same
    P. Every pull request matches at least one of those two, so the context always reports. Order and
    duplication within P are irrelevant, hence the set comparison.

    Everything else is left UNDECIDED rather than guessed. General glob-set coverage is not something
    this gate can compute, and both guesses are unacceptable in the way this file's other refusals
    describe: a wrong green is a vacuous pass over a deadlocked repo, and a wrong red is a confident
    "your repo is deadlocked" about a repo that is fine. main() turns the undecided case into a
    no-verdict, and only for a context that is actually REQUIRED.
    """
    paths = {frozenset(f.entries) for f in filters if f.key == "paths" and f.entries is not None}
    ignores = {
        frozenset(f.entries) for f in filters if f.key == "paths-ignore" and f.entries is not None
    }
    return bool(paths & ignores)


def matrix_suffixes(job: dict, subject: str) -> list[str]:
    """The ` (v1, v2)` suffixes GitHub appends to a matrix job's check name — one per combination.

    A job with no matrix yields [""], i.e. one context, unsuffixed.

    Refuses anything it cannot enumerate exactly. A guessed suffix set that happens to contain the
    required context would be a VACUOUS GREEN over a repo that is already deadlocked, which is the
    #266 defect this gate exists to close — so an `include`/`exclude`/expression matrix is exit 3,
    not a shrug.
    """
    strategy = job.get("strategy")
    if not isinstance(strategy, dict) or "matrix" not in strategy:
        return [""]
    matrix = strategy["matrix"]
    if not isinstance(matrix, dict):
        raise GateError(
            f"{subject}: `strategy.matrix` is not a mapping (an expression?), so the check-run names "
            f"it produces cannot be enumerated. This gate will not guess a producible set."
        )
    for key in ("include", "exclude"):
        if key in matrix:
            raise GateError(
                f"{subject}: `strategy.matrix.{key}` is not supported by this gate — the resulting "
                f"check-run names cannot be derived exactly, and a guessed set that happened to "
                f"contain the required context would be a vacuous green over a deadlocked repo."
            )
    axes = []
    for key, values in matrix.items():
        if not isinstance(values, list) or not values:
            raise GateError(
                f"{subject}: `strategy.matrix.{key}` is not a non-empty list, so its combinations "
                f"cannot be enumerated."
            )
        axes.append([str(v) for v in values])
    # GitHub joins the values in the matrix's declaration order: `job (ubuntu-latest, 8.0)`.
    return [f" ({', '.join(combo)})" for combo in itertools.product(*axes)]


class Workflows:
    """Workflow documents, resolved from the working tree or from the API at a pinned ref."""

    def __init__(self, root: str, repo: str) -> None:
        self.root = root
        self.repo = repo
        self._cache: dict[tuple[str, str], dict] = {}

    def local(self, filename: str) -> dict:
        path = os.path.join(self.root, ".github", "workflows", filename)
        if not os.path.isfile(path):
            raise GateError(
                f"{self.repo} has no .github/workflows/{filename} in the working tree — a job that "
                f"calls it cannot start, so nothing it would name can ever report"
            )
        with open(path, encoding="utf-8") as fh:
            return load_yaml(fh.read(), f"{self.repo}/.github/workflows/{filename}")

    def at_ref(self, repo: str, filename: str, ref: str) -> dict:
        key = (f"{repo}/{filename}", ref)
        if key not in self._cache:
            try:
                text = gh_api(
                    "-H", "Accept: application/vnd.github.raw",
                    f"repos/{repo}/contents/.github/workflows/{filename}?ref={ref}",
                )
            except Missing as e:
                raise GateError(
                    f"{repo} has no .github/workflows/{filename} at ref {ref} — the caller pinning "
                    f"it cannot start, so no context it would name can ever report: {e}"
                ) from e
            self._cache[key] = load_yaml(text, f"{repo}/.github/workflows/{filename}@{ref}")
        return self._cache[key]

    def callee(self, uses: str, subject: str) -> dict:
        uses = uses.strip()
        if m := LOCAL_USES_RE.match(uses):
            return self.local(m.group("file"))
        if m := REMOTE_USES_RE.match(uses):
            return self.at_ref(m.group("repo"), m.group("file"), m.group("ref"))
        raise GateError(
            f"{subject}: `uses: {uses}` is not a workflow reference this gate understands "
            f"(expected ./.github/workflows/<f>.yml or <owner>/<repo>/.github/workflows/<f>.yml@<ref>)"
        )


def display_name(job_id: str, job: dict) -> str:
    """What GitHub calls this job: its `name:` if it has one, else its job id."""
    name = job.get("name")
    return str(name) if isinstance(name, (str, int, float)) and str(name) else str(job_id)


def jobs_of(doc: dict, what: str) -> dict:
    jobs = doc.get("jobs")
    if not isinstance(jobs, dict) or not jobs:
        raise GateError(f"{what}: declares no `jobs:` mapping — it cannot run")
    for job_id, job in jobs.items():
        if not isinstance(job, dict):
            raise GateError(f"{what} [{job_id}]: the job is not a YAML mapping")
    return jobs


def job_condition(job: dict, subject: str) -> tuple[bool, str | None, bool]:
    """Whether a job is proven to report on every normal pull request.

    Returns ``(always_runs, description, known_suppressed)``.  GitHub evaluates job-level
    ``if:`` at run time, so an expression we cannot prove always-run must not be treated as a
    producer of a required context.  ``!cancelled()`` is the deliberately supported control: it
    prevents cancellation from leaving dependent work running, while still running on a normal PR.
    """
    if "if" not in job:
        return True, None, False
    raw = job["if"]
    if raw is True:
        return True, None, False
    if raw is False:
        return False, "`if: false`", True
    if not isinstance(raw, str):
        return False, f"`if:` has unsupported value {raw!r}", False
    expression = raw.strip()
    if expression.startswith("${{") and expression.endswith("}}"):
        expression = expression[3:-2].strip()
    normalized = re.sub(r"\s+", "", expression).lower()
    # The workflow itself is already known to be a pull-request workflow, so this common guard is
    # true for every normal PR event.  It is used by the production kit-materializer caller.
    proven_terms = {
        "true",
        "!cancelled()",
        "github.event_name=='pull_request'",
        'github.event_name=="pull_request"',
    }
    # A conjunction of independently proven normal-PR predicates remains proven.  Keep this
    # intentionally narrow: OR, negation beyond !cancelled(), and arbitrary context expressions
    # all need runtime values this static gate does not possess.
    if normalized.split("&&") and all(term in proven_terms for term in normalized.split("&&")):
        return True, None, False
    if normalized == "false":
        return False, f"`if: {raw}`", True
    return False, f"`if: {raw}`", False


def contexts_of(
    doc: dict,
    what: str,
    wf: Workflows,
    prefix: str = "",
    depth: int = 0,
    crossings: dict[str, set[str]] | None = None,
    conditions: dict[str, list[tuple[str, bool]]] | None = None,
) -> set[str]:
    """Every check-run name the jobs of `doc` produce, prefixed by any calling job's display name.

    `crossings`, when given, is filled in as a side effect: context -> the set of CROSS-REPO `uses:`
    strings that had to be followed to reach it. That is the provenance #1783's ref check judges, and
    it is collected HERE rather than by a second walk because the derivation and the provenance are
    the same traversal — two walks would be two chances to disagree about which callee produced what
    (ADR-0067 §3). The same dict is threaded to every depth: a context's name accumulates its callers'
    prefixes as it is derived, so the key a nested call records is already the final context string.
    """
    if depth > MAX_DEPTH:
        raise GateError(
            f"{what}: reusable-workflow nesting exceeded {MAX_DEPTH} levels — a cycle, or deeper "
            f"than GitHub itself permits"
        )
    out: set[str] = set()
    for job_id, job in jobs_of(doc, what).items():
        subject = f"{what} [{job_id}]"
        always_runs, condition, known_suppressed = job_condition(job, subject)
        for suffix in matrix_suffixes(job, subject):
            display = f"{prefix}{display_name(job_id, job)}{suffix}"
            # An expression is resolved at run time, from values this gate cannot see. Deriving the
            # LITERAL `Build ${{ matrix.os }}` would produce a context that can never match the real
            # one — and the gate would then report exit 1: "this repo is deadlocked, every PR will
            # hang". A confident, alarming, WRONG finding is worse than no verdict, so refuse.
            if "${{" in display:
                raise GateError(
                    f"{subject}: the check-run name derives to {display!r}, which contains an "
                    f"expression. Its real name is decided at run time by values this gate cannot "
                    f"see, so the context cannot be derived exactly. This gate will not guess: a "
                    f"guessed name that failed to match would be reported as a deadlocked repo, and "
                    f"one that matched by luck would be a green verdict over a real deadlock."
                )
            if "uses" in job:
                uses = str(job["uses"]).strip()
                callee = wf.callee(uses, subject)
                produced = contexts_of(callee, uses, wf, prefix=f"{display} / ",
                                       depth=depth + 1, crossings=crossings, conditions=conditions)
                out |= produced
                if not always_runs and conditions is not None:
                    for context in produced:
                        conditions.setdefault(context, []).append((condition or "`if:`", known_suppressed))
                if crossings is not None and REMOTE_USES_RE.match(uses):
                    for context in produced:
                        crossings.setdefault(context, set()).add(uses)
            else:
                out.add(display)
                if not always_runs and conditions is not None:
                    conditions.setdefault(display, []).append((condition or "`if:`", known_suppressed))
    return out


def unsound_crossings(uses_strings: set[str]) -> list[str]:
    """The cross-repo `uses:` calls in a context's provenance that a REQUIRED verdict may not rest on.

    Returns one clause per offending call, ready to be joined into a finding. Empty means every
    crossing is either an accepted moving ref or an immutable commit.
    """
    out: list[str] = []
    for uses in sorted(uses_strings):
        m = REMOTE_USES_RE.match(uses)
        if not m:  # a local `./` call: resolved against the caller's own tree, pinned by definition
            continue
        repo, ref = m.group("repo"), m.group("ref").strip()
        if repo != AUTHORITY:
            out.append(
                f"`uses: {uses}` resolves its callee from {repo}, which is not the org authority "
                f"{AUTHORITY}. Half of this required context would then be a job id in a repository "
                f"outside the org's own review and gates"
            )
            continue
        if SHA_RE.match(ref) or ref in ACCEPTED_MOVING_REFS:
            continue
        out.append(
            f"`uses: {uses}` tracks the ref {ref!r}, which is neither a 40-hex commit nor one of the "
            f"moving refs this org has DECIDED a required verdict may rest on "
            f"({', '.join(sorted(ACCEPTED_MOVING_REFS))})"
        )
    return out


def producible_contexts(
    root: str, repo: str, branch: str
) -> tuple[set[str], dict[str, list[str]], dict[str, list[tuple[str, bool]]], list[str], dict[str, set[str]]]:
    """Every context the repo reports on an ARBITRARY pull request, and the two ways of falling short.

    Returns (always, filtered, conditional, non_pr, crossings):

      always    contexts reported on EVERY pull request — the only set a required context may live in.
      filtered  context -> the path-filtered workflow(s) that are its ONLY producers, so it reports on
                some pull requests and not others (.github#1508). Keyed for the message, because
                "which filter" is the whole of the repair.
      non_pr    contexts produced only by workflows that do not trigger on a PR at all.
      crossings context -> the cross-repo `uses:` calls its derivation followed (.github#1783).

    The middle two exist so a finding can name WHICH way the context falls short — "only on push",
    "only when .claude/skills/** changes" — rather than the useless "no workflow produces it".

    `crossings` is collected only for the PR-triggered workflows. A context nothing reports on a pull
    request is already a finding of its own, and grading the ref of a call that can never deadlock
    anything would be a second alarm about the same defect.
    """
    wf = Workflows(root, repo)
    d = os.path.join(root, ".github", "workflows")
    if not os.path.isdir(d):
        raise GateError(f"{d} is not a directory — this repo has no workflows, so it produces nothing")

    files = sorted(f for ext in ("yml", "yaml") for f in glob.glob(os.path.join(d, f"*.{ext}")))
    if not files:
        raise GateError(f"{d} contains no workflow files — this repo produces no contexts at all")

    pr_contexts: set[str] = set()
    filtered: dict[str, list[PathFilter]] = {}
    conditional: dict[str, list[tuple[str, bool]]] = {}
    non_pr: list[str] = []
    crossings: dict[str, set[str]] = {}
    for path in files:
        rel = os.path.relpath(path, root)
        with open(path, encoding="utf-8") as fh:
            doc = load_yaml(fh.read(), rel)
        if any(e in triggers(doc) for e in PR_EVENTS):
            # A path filter must not cost us the DERIVATION: the finding names the context, so it has
            # to be derived exactly here, and an unparsable filtered workflow is still exit 3.
            local_conditions: dict[str, list[tuple[str, bool]]] = {}
            produced = contexts_of(doc, rel, wf, crossings=crossings, conditions=local_conditions)
            path_filters = pr_path_filters(doc, rel, branch)
            if path_filters is None:
                pr_contexts |= produced - local_conditions.keys()
                for context, conditions in local_conditions.items():
                    conditional.setdefault(context, []).extend(conditions)
            else:
                for context in sorted(produced):
                    filtered.setdefault(context, []).extend(path_filters)
        else:
            # Not a finding by itself — most workflows are not PR gates. But a required context that
            # matches one of these is a repo that will hang forever, and the message must say so.
            #
            # Nothing here may fail the run. This branch exists ONLY to sharpen a message, and a
            # non-PR workflow cannot deadlock a PR whatever it contains — so an unparsable release.yml,
            # or an outage fetching a callee that only `push` ever calls, must not cost the gate its
            # verdict on the contexts that DO matter. Unreachable is caught alongside GateError for
            # exactly that reason: without it, a flaky fetch for a workflow we are not judging would
            # surface as exit 2 over a repo whose PR contexts are all provably fine.
            try:
                non_pr.extend(sorted(contexts_of(doc, rel, wf)))
            except (GateError, Unreachable):
                pass

    # A context some UNFILTERED workflow also reports is fine, whoever else produces it behind a
    # filter. The question this gate asks is "does it always report?", not "is some producer of it
    # filtered?" — and answering the second would be a confident, wrong "your repo is deadlocked".
    for context in pr_contexts:
        filtered.pop(context, None)

    # ...and neither is GitHub's own documented remedy a deadlock. A `paths: P` producer beside a
    # `paths-ignore: P` one covers every pull request between them, so the context always reports
    # even though NO single producer is unfiltered.
    for context, fs in list(filtered.items()):
        if filters_cover_every_pr(fs):
            pr_contexts.add(context)
            del filtered[context]
    return pr_contexts, filtered, conditional, non_pr, crossings


def classic_contexts(repo: str, branch: str, saved: str | None) -> list[dict]:
    """Required checks from CLASSIC branch protection. A 404 means "no classic protection" — which
    is NOT the same as "requires nothing"; see required_contexts()."""
    if saved:
        try:
            with open(saved, encoding="utf-8") as fh:
                payload = json.load(fh)
        except (OSError, json.JSONDecodeError) as e:
            raise GateError(f"cannot read the saved protection payload {saved}: {e}") from e
    else:
        try:
            payload = json.loads(gh_api(f"repos/{repo}/branches/{branch}/protection"))
        except Missing:
            return []  # no CLASSIC protection. A ruleset may still protect the branch.
        except Forbidden as e:
            raise GateError(
                f"cannot read {repo}'s branch protection: {e}\n"
                f"Reading required status checks needs `administration: read`, and THIS TOKEN DOES "
                f"NOT HAVE IT.\n"
                f"Do not try to fix that in a workflow: `administration` is NOT a valid "
                f"`permissions:` scope for a GITHUB_TOKEN — declaring it is a validation error and "
                f"the run dies at startup, producing no check run at all (the #478 blind spot). The "
                f"org's dispatch App does not hold the scope either (#463, re-verified 2026-07-14: "
                f"contents, metadata, packages, pull_requests — no administration).\n"
                f"Run this tool with a token that has admin rights on {repo} (a PAT, or an App "
                f"installation with `administration: read`). The check that DOES run in CI without a "
                f"credential is reusable-job-id-coherence.yml, which catches the rename in FS-GG/"
                f".github before it can reach a receiver. See "
                f"docs/coordination/reusable-workflow-contract.md.\n"
                f"NOTE: rulesets were NOT consulted, and reading them would not rescue this run. "
                f"`rules/branches/<b>` needs only `metadata: read`, so it is tempting to think a "
                f"ruleset-protected repo can be audited without admin — it cannot, BY THIS TOOL. A "
                f"403 here does not mean 'there is no classic protection'; it means 'I cannot see "
                f"whether there is'. The required set is the UNION of both stores, so an unreadable "
                f"store makes the union unknowable, and a half-read is not a verdict."
            ) from e
        except json.JSONDecodeError as e:
            raise GateError(f"{repo}: branch protection was not valid JSON — {e}") from e

    rsc = payload.get("required_status_checks")
    if not rsc:
        return []  # protected, but not on status checks
    checks = rsc.get("checks")
    if checks is None:
        # The legacy shape: `contexts: [str]`, with no app attribution.
        return [{"context": c, "app_id": None} for c in (rsc.get("contexts") or [])]
    return list(checks)


def ruleset_contexts(repo: str, branch: str, saved: str | None) -> list[dict]:
    """Required checks from RULESETS — the other, entirely separate, place GitHub keeps them.

    `branches/<b>/protection` does NOT report ruleset rules, and `rules/branches/<b>` does NOT
    report classic protection. They are two stores, and a branch may be governed by either, both,
    or neither. This endpoint reports repository AND organization rulesets that apply to the branch,
    and — unlike the classic endpoint — it needs only `metadata: read`.
    """
    if saved:
        try:
            with open(saved, encoding="utf-8") as fh:
                rules = json.load(fh)
        except (OSError, json.JSONDecodeError) as e:
            raise GateError(f"cannot read the saved rules payload {saved}: {e}") from e
    else:
        try:
            rules = json.loads(gh_api(f"repos/{repo}/rules/branches/{branch}"))
        except Missing as e:
            # This endpoint answers `[]` for a branch with no rules, so a 404 is NOT "no rules" —
            # it is "no such repo or branch", and guessing "unprotected" from it would be the very
            # fail-open this function exists to close.
            raise GateError(
                f"cannot read {repo}@{branch}'s rulesets: {e}\n"
                f"A branch with no rules answers `[]`, not 404 — so this is not 'no rulesets', it "
                f"is 'no such repo or branch'. Refusing to infer that the branch is unprotected."
            ) from e
        except Forbidden as e:
            raise GateError(
                f"cannot read {repo}@{branch}'s rulesets: {e}\n"
                f"This needs only `metadata: read`. A token that cannot read it cannot see half of "
                f"what protects the branch, and a half-read is not a verdict."
            ) from e
        except json.JSONDecodeError as e:
            raise GateError(f"{repo}: the ruleset rules were not valid JSON — {e}") from e

    if not isinstance(rules, list):
        raise GateError(
            f"{repo}@{branch}: expected `rules/branches/{branch}` to answer a list of rules, got "
            f"{type(rules).__name__}. Refusing to guess what protects this branch."
        )

    out: list[dict] = []
    for rule in rules:
        if not isinstance(rule, dict) or rule.get("type") != "required_status_checks":
            continue
        params = rule.get("parameters") or {}
        for check in params.get("required_status_checks") or []:
            context = check.get("context") if isinstance(check, dict) else None
            if not isinstance(context, str) or not context:
                # A required check we cannot even name. Coercing it to "" would send an empty
                # string down the audit and report `REQUIRES the status check ''` — a confident,
                # WRONG claim that the repo is deadlocked, from a payload we did not understand.
                raise GateError(
                    f"{repo}@{branch}: a `required_status_checks` rule names a check with no "
                    f"readable `context` ({check!r}). Refusing to guess at a required check's "
                    f"name — an unreadable requirement is no verdict, not a finding."
                )
            # A ruleset names the producing app as `integration_id`, where classic protection says
            # `app_id`. Same meaning, different spelling; normalise so main() judges them alike.
            out.append({"context": context, "app_id": check.get("integration_id")})
    return out


def required_contexts(
    repo: str, branch: str, saved: str | None, saved_rules: str | None
) -> list[dict]:
    """Every status check the branch requires — from BOTH places GitHub keeps them.

    THE FAIL-OPEN THIS CLOSES (#574). This gate used to read classic branch protection alone and
    treat its 404 as the answer "the branch is not protected, so it requires nothing". That is true
    only in a world without rulesets, and the org left that world: FS.GG.Governance is protected by
    a REPOSITORY RULESET requiring five status checks, and `branches/main/protection` answers 404
    for it. So the gate reported

        ok: FS-GG/FS.GG.Governance@main requires NO status checks

    and exited 0 — a VACUOUS GREEN over a fully-protected repo, with an admin token, which is the
    exact class (#266) this gate's own docstring is written against. The blindness was never a
    credential problem: it read the wrong store.

    Rulesets and classic protection STACK — GitHub enforces both — so the required set is their
    UNION, and "requires nothing" is only true when both sources say so and both were readable.
    """
    # A saved payload for ONE store is a half-world, and a half-world is how this bug started: the
    # offline caller would get a confident verdict computed from whichever store they happened to
    # hand over. Both, or neither.
    if bool(saved) != bool(saved_rules):
        given, missing = ("--protection", "--rules") if saved else ("--rules", "--protection")
        raise GateError(
            f"{given} was given without {missing}. Required checks live in TWO stores (classic "
            f"branch protection and rulesets) and the required set is their UNION, so a saved "
            f"payload for one store alone describes half a world. Pass BOTH (an empty ruleset "
            f"payload is the JSON list `[]`; an unprotected classic payload is `{{}}`), or pass "
            f"NEITHER and let the gate read both from the API."
        )

    classic = classic_contexts(repo, branch, saved)
    rules = ruleset_contexts(repo, branch, saved_rules)

    # Dedup on the CONTEXT, not on (context, app_id): the two stores spell the producing app
    # differently and a ruleset routinely omits it entirely (every one of FS.GG.Governance's five
    # checks does). Keying on the pair would let one context required by BOTH stores survive twice
    # — audited twice, printed twice, and, if it is unproducible, reported as two deadlocks when
    # there is one. Prefer the ATTRIBUTED entry, so a context GitHub pinned to an app is judged by
    # that app rather than by an unattributed twin.
    merged: dict[str, dict] = {}
    for check in [*classic, *rules]:
        context = str(check.get("context", ""))
        if merged.get(context, {}).get("app_id") is None:
            merged[context] = check
    return list(merged.values())


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description=__doc__.splitlines()[0],
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    ap.add_argument("--repo", required=True, help="owner/name of the repo whose protection to read")
    ap.add_argument("--root", default=".", help="that repo's working tree (default: .)")
    ap.add_argument("--branch", default="main", help="the protected branch (default: main)")
    ap.add_argument("--protection", default=None,
                    help="a saved classic-protection payload; skips that API call (for fixtures)")
    ap.add_argument("--rules", default=None,
                    help="a saved ruleset-rules payload; skips that API call (for fixtures)")
    args = ap.parse_args(argv)

    required = required_contexts(args.repo, args.branch, args.protection, args.rules)
    if not required:
        print(
            f"ok: {args.repo}@{args.branch} requires NO status checks — nothing can deadlock on a "
            f"context that never reports. (Checked BOTH stores: classic branch protection and "
            f"rulesets. This is not a statement that the repo's gates are green; it is a statement "
            f"that none of them are required.)"
        )
        return OK

    pr_contexts, filtered, conditional, non_pr, crossings = producible_contexts(
        args.root, args.repo, args.branch
    )

    findings: list[str] = []
    ref_findings: list[str] = []
    audited = 0
    skipped: list[str] = []

    for check in required:
        context = str(check.get("context", ""))
        app_id = check.get("app_id")
        # A third-party app's context is not derivable from this repo's YAML. Counting it as a
        # finding would be the gate crying wolf about a producer it cannot see.
        if app_id is not None and int(app_id) != ACTIONS_APP_ID:
            skipped.append(f"{context} (app_id {app_id}, not GitHub Actions)")
            continue
        audited += 1
        if context in pr_contexts:
            # It reports. The remaining question is whether it reports a DURABLE verdict, which is a
            # different defect with a different repair: a context sourced through a moving cross-repo
            # ref answers differently on byte-identical content depending on when it ran, so a green
            # merge is a statement about the clock (ADR-0067 §2, .github#1584, #1783).
            unsound = unsound_crossings(crossings.get(context, set()))
            if unsound:
                ref_findings.append(
                    f"{args.repo}@{args.branch} REQUIRES the status check {context!r}, and its "
                    f"producer reaches the callee that names it through a call this org has not "
                    f"decided a required verdict may rest on: {'; '.join(unsound)}. The verdict is "
                    f"then a function of whatever that ref pointed at when the job happened to run — "
                    f"not of the tree under test — so a pull request can be green and red on "
                    f"byte-identical content, with no commit in this repo between them "
                    f"(ADR-0067 §2; `FS.GG.SDD#724` is the measured instance, .github#1584). Pin the "
                    f"call at a 40-hex commit, or return it to a ref the decision accepts. The "
                    f"decision, and why these calls are NOT pinned today, is in "
                    f"docs/coordination/reusable-workflow-contract.md."
                )
                continue
            print(f"  ok   {context}")
            continue

        # A PATH-FILTERED producer deadlocks differently from an absent one, and the repair is
        # different too, so it gets its own finding rather than a clause bolted onto the generic one
        # (.github#1508). "Every PR" would be wrong here — it is every PR the filter excludes, and
        # `paths:` and `paths-ignore:` exclude OPPOSITE sets, so the message is built from the filter
        # rather than written once for both.
        if context in filtered:
            fs = filtered[context]
            unmodellable = [f for f in fs if f.entries is None]
            if unmodellable:
                raise GateError(
                    f"{args.repo}@{args.branch} REQUIRES the status check {context!r}, and a "
                    f"pull-request filter on a workflow that produces it is written in a shape this "
                    f"gate cannot model: {'; '.join(str(f) for f in unmodellable)}. Which pull "
                    f"requests that withholds the check run from is therefore unknown, and an "
                    f"unknown is not a verdict — this gate will not guess a repo green or "
                    f"deadlocked off a filter it could not read. Write the filter as a non-empty "
                    f"list of glob strings."
                )
            if len({f.workflow for f in fs}) > 1:
                raise GateError(
                    f"{args.repo}@{args.branch} REQUIRES the status check {context!r}, and EVERY "
                    f"workflow producing it filters its pull-request trigger: "
                    f"{'; '.join(str(f) for f in fs)}. Whether those filters JOINTLY cover every "
                    f"pull request is glob-set coverage, which this gate cannot compute — so it has "
                    f"no verdict here rather than a guess. A wrong green would be a vacuous pass "
                    f"over a deadlocked repo; a wrong red would announce a deadlock in a repo that "
                    f"is fine. The one arrangement it DOES decide is GitHub's documented remedy — a "
                    f"`paths: P` producer beside a `paths-ignore: P` producer on the SAME P — so "
                    f"making the two filters exact complements resolves this, as does giving the "
                    f"context one producer that runs on every pull request."
                )
            f = fs[0]
            findings.append(
                f"{args.repo}@{args.branch} REQUIRES the status check {context!r}, but its only "
                f"producer is PATH-FILTERED and so withholds it from some pull requests: {f}. "
                f"GitHub does not skip a filtered required check — it never creates the check run "
                f"at all, and branch protection cannot tell that apart from one that has not "
                f"reported yet. So {f.excluded} is held at \"Expected — waiting for status to be "
                f"reported\" and can never merge. Give this context a producer that runs on EVERY "
                f"pull request: drop the `{f.key}:` filter, or add GitHub's documented twin — a "
                f"no-op job reporting the same context, filtered by the exact complement — or stop "
                f"requiring the context."
            )
            continue

        # A job-level `if:` is evaluated after the workflow trigger.  Unlike a workflow-level
        # filter, GitHub creates a skipped job, but that is still not a reported required context
        # on which branch protection can rely.  An explicit false condition is a definite finding;
        # an expression we cannot prove always-run is a no-verdict rather than a guessed green.
        if context in conditional:
            conditions = conditional[context]
            unknown = [description for description, known_suppressed in conditions if not known_suppressed]
            if unknown:
                raise GateError(
                    f"{args.repo}@{args.branch} REQUIRES the status check {context!r}, but EVERY "
                    f"producer is guarded by a job-level condition this gate cannot prove runs on "
                    f"a normal pull request: {', '.join(unknown)}. The check may be suppressed, "
                    f"so this gate has no verdict rather than guessing the required context reports. "
                    "Use an unconditional job or the proven always-run `${{ !cancelled() }}` form."
                )
            findings.append(
                f"{args.repo}@{args.branch} REQUIRES the status check {context!r}, but its only "
                f"producer is suppressed by a job-level condition: {', '.join(description for description, _ in conditions)}. "
                f"GitHub creates no successful required check for that job, so branch protection "
                f"holds pull requests at \"Expected — waiting for status to be reported\". Remove "
                f"the suppressing condition, give the context an always-running producer, or stop "
                f"requiring it."
            )
            continue

        why = (
            f"is produced ONLY by a workflow that does not trigger on `pull_request`, so it can "
            f"never report on a PR"
            if context in non_pr
            else "is produced by no workflow in this repo"
        )
        near = sorted(c for c in pr_contexts if c.split(" / ")[0] == context.split(" / ")[0])
        hint = ""
        if near:
            hint = (
                f" The same job DOES produce: {', '.join(repr(n) for n in near)} — if this is a "
                f"reusable-workflow context, the callee's JOB ID has changed, and the callee's job "
                f"ids are API (see docs/coordination/reusable-workflow-contract.md)."
            )
        findings.append(
            f"{args.repo}@{args.branch} REQUIRES the status check {context!r}, but it {why}. "
            f"GitHub will hold every pull request at \"Expected — waiting for status to be "
            f"reported\" indefinitely; the branch cannot merge, and no commit in this repo need "
            f"have changed for that to start.{hint}"
        )

    for s in skipped:
        print(f"  skip {s}")

    if audited == 0:
        raise GateError(
            f"{args.repo}@{args.branch} requires {len(required)} status check(s), but NONE of them "
            f"is produced by GitHub Actions, so this gate audited nothing. Examining nothing is a "
            f"failure to audit, not a clean audit."
        )

    if findings or ref_findings:
        for f in findings + ref_findings:
            print(f"::error::check-required-contexts: {f}", file=sys.stderr)
        # The two findings are NOT the same defect and must not be summarised as one. "This repo is
        # deadlocked" over a repo whose contexts all report — they just report a verdict that moves —
        # would send the reader to branch protection for a problem that lives in a `uses:` line.
        summary = []
        if findings:
            summary.append(
                f"{len(findings)} required context(s) can never report — this repo is deadlocked, "
                f"or will be on its next pull request"
            )
        if ref_findings:
            summary.append(
                f"{len(ref_findings)} required context(s) report a verdict that is not a function "
                f"of the tree under test"
            )
        print(f"\n{'; '.join(summary)}. Of {audited} audited.", file=sys.stderr)
        return FINDING

    print(
        f"ok: every required context is producible, and every cross-repo `uses:` its producers "
        f"cross is one a required verdict may rest on — {audited} audited"
        + (f", {len(skipped)} skipped (not GitHub Actions)" if skipped else "")
        + f", against {len(pr_contexts)} context(s) this repo reports on EVERY pull request"
        + (
            f" ({len(filtered)} further context(s) are withheld from some pull requests by a path "
            f"filter and are therefore not requirable as they stand — none of them is required)"
            if filtered
            else ""
        )
        + "."
    )
    return OK


def cli(argv: list[str]) -> int:
    """Guarantee the exit code is a VERDICT, never an accident.

    Python exits 1 on any uncaught exception — and 1 is this gate's "a required context can never
    report", i.e. "this repo is deadlocked". A crash would therefore be reported as a specific,
    confident, WRONG claim that somebody's branch protection is broken. "I could not check" must
    never share a code with "I checked, and it's broken" (#266, #320).
    """
    try:
        return main(argv)
    except Unreachable as e:
        print(f"::error::check-required-contexts: no verdict — {e}", file=sys.stderr)
        return NO_VERDICT_RETRYABLE
    except GateError as e:
        print(f"::error::check-required-contexts: no verdict — {e}", file=sys.stderr)
        return NO_VERDICT_PERMANENT
    except Exception:  # noqa: BLE001 — deliberately broad; see the docstring
        traceback.print_exc()
        print(
            "::error::check-required-contexts: the gate crashed, so it has NO VERDICT. This is not "
            "a finding about any required context — it is a bug in the gate. See the traceback.",
            file=sys.stderr,
        )
        return NO_VERDICT_PERMANENT


if __name__ == "__main__":
    sys.exit(cli(sys.argv[1:]))
