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
      is producible by some workflow in the working tree that triggers on `pull_request`.

"Producible" is computed STATICALLY, from the YAML — not by sampling live check runs. That is the
whole point: a check run that has not reported yet and a check run that can NEVER report look
identical at any given moment, and it is the second one that deadlocks the repo. Only the static
derivation can tell them apart, and it catches the plain typo in a protection setting for free — a
misspelled required context is indistinguishable from a renamed one, and both deadlock identically.

HOW A CONTEXT IS DERIVED (this is GitHub's naming, and getting it wrong would make the gate lie)
  normal job        -> the job's `name:` if it has one, else its job id
  `uses:` job       -> "<caller display> / <callee job display>", one context per job in the callee,
                       recursively (a callee may itself call a reusable workflow)
  matrix job        -> "<display> (v1, v2)" for each combination, in the matrix's declaration order

  A workflow that does NOT trigger on `pull_request` produces NOTHING on a PR. A context required
  from such a workflow can never report, and is a finding — not a skip.

EVERY ONE OF THESE IS AN ERROR, NOT A SKIP
  - A required context no `pull_request` workflow can produce      (the repo is deadlocked, or will be)
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
Exit: 0 = every required context is producible; 1 = at least one can never report; 2 = no verdict,
RETRYABLE — the API could not be read (rate limit, outage); 3 = no verdict, PERMANENT — protection
is unreadable for want of permission, a workflow will not parse, a callee is missing, or a matrix
cannot be enumerated.

"I could not check" must never share an exit code with "I checked, and it's fine" (#266) — nor with
"I checked, and it's broken" (#320) — nor "try again" with "a human must fix a file" (#335).
"""
from __future__ import annotations

import argparse
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


def contexts_of(doc: dict, what: str, wf: Workflows, prefix: str = "", depth: int = 0) -> set[str]:
    """Every check-run name the jobs of `doc` produce, prefixed by any calling job's display name."""
    if depth > MAX_DEPTH:
        raise GateError(
            f"{what}: reusable-workflow nesting exceeded {MAX_DEPTH} levels — a cycle, or deeper "
            f"than GitHub itself permits"
        )
    out: set[str] = set()
    for job_id, job in jobs_of(doc, what).items():
        subject = f"{what} [{job_id}]"
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
                callee = wf.callee(str(job["uses"]), subject)
                out |= contexts_of(callee, f"{job['uses']}", wf, prefix=f"{display} / ",
                                   depth=depth + 1)
            else:
                out.add(display)
    return out


def producible_contexts(root: str, repo: str) -> tuple[set[str], list[str]]:
    """Every context the repo's `pull_request`-triggered workflows can report on a PR.

    Also returns the workflows that exist but do NOT trigger on pull_request, so a finding can say
    "this context is produced, but only on push" rather than the useless "no workflow produces it".
    """
    wf = Workflows(root, repo)
    d = os.path.join(root, ".github", "workflows")
    if not os.path.isdir(d):
        raise GateError(f"{d} is not a directory — this repo has no workflows, so it produces nothing")

    files = sorted(f for ext in ("yml", "yaml") for f in glob.glob(os.path.join(d, f"*.{ext}")))
    if not files:
        raise GateError(f"{d} contains no workflow files — this repo produces no contexts at all")

    pr_contexts: set[str] = set()
    non_pr: list[str] = []
    for path in files:
        rel = os.path.relpath(path, root)
        with open(path, encoding="utf-8") as fh:
            doc = load_yaml(fh.read(), rel)
        if "pull_request" in triggers(doc) or "pull_request_target" in triggers(doc):
            pr_contexts |= contexts_of(doc, rel, wf)
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
    return pr_contexts, non_pr


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
                f"docs/coordination/reusable-workflow-contract.md."
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
            # A ruleset names the producing app as `integration_id`, where classic protection says
            # `app_id`. Same meaning, different spelling; normalise so main() judges them alike.
            out.append({
                "context": str(check.get("context", "")),
                "app_id": check.get("integration_id"),
            })
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
    classic = classic_contexts(repo, branch, saved)
    rules = ruleset_contexts(repo, branch, saved_rules)

    merged: list[dict] = []
    seen: set[tuple[str, object]] = set()
    for check in [*classic, *rules]:
        key = (str(check.get("context", "")), check.get("app_id"))
        if key not in seen:
            seen.add(key)
            merged.append(check)
    return merged


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

    pr_contexts, non_pr = producible_contexts(args.root, args.repo)

    findings: list[str] = []
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
            print(f"  ok   {context}")
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

    if findings:
        for f in findings:
            print(f"::error::check-required-contexts: {f}", file=sys.stderr)
        print(
            f"\n{len(findings)} required context(s) can never report, of {audited} audited. "
            f"This repo is deadlocked, or will be on its next pull request.",
            file=sys.stderr,
        )
        return FINDING

    print(
        f"ok: every required context is producible — {audited} audited"
        + (f", {len(skipped)} skipped (not GitHub Actions)" if skipped else "")
        + f", against {len(pr_contexts)} context(s) this repo can produce on a PR."
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
