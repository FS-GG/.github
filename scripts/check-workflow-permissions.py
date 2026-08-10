#!/usr/bin/env python3
"""Assert every caller of an FS-GG reusable workflow grants the permissions its callee declares.

.github#478, epic #266 (coherence gates that fail open). Found while working FS.GG.Game#137.

A called workflow CANNOT request a permission the calling workflow did not grant: the callee's
GITHUB_TOKEN is the INTERSECTION of the two. When the caller under-grants, GitHub kills the run at
*startup* — before the job-level `if:` is evaluated, before any step runs.

That failure is invisible to every "is the gate green" check we have. A `startup_failure` is not a
red check on the PR; it is a workflow that never ran. FS.GG.Game's lockfile-sync caller granted only
`contents: read` against a callee declaring `contents: read` + `packages: read`, and ALL 119 runs
ended in startup_failure — it had never executed once, while grepping for adoption said the repo had
the caller file, so the adoption table gave it a green tick (FS.GG.Audio#43). That is the #266
signature exactly: a gate that is present on paper and does nothing.

This gate is a pure function of the caller/callee YAML pair. It reads no run history.

WHAT IT ASSERTS
  For every workflow in a rostered repo containing
      uses: FS-GG/.github/.github/workflows/<callee>.yml@<ref>
  the CALLER's effective permissions for that job grant AT LEAST every scope the CALLEE declares, at
  AT LEAST the callee's level (none < read < write).

  "Effective" = the job's own `permissions:` block if it has one, else the workflow's top-level
  block. That is GitHub's precedence, and reading only the top-level one would report green on a job
  that narrows its own grant.

EVERY ONE OF THESE IS AN ERROR, NOT A SKIP
  - A caller that under-grants a scope its callee declares         (it provably cannot start)
  - A caller with NO `permissions:` block at either level, when its callee declares scopes. The
    effective token is then the org/repo DEFAULT, which is not in the file and which an admin can
    flip; we cannot prove from the pair that the run starts. Doctrine is an explicit block
    (docs/coordination/contract-coherence-gate.md), so demand one.
  - A caller naming a callee that does not exist at the ref it pins (also a startup_failure)
  - A workflow, on either side, that will not parse
  - A repo whose workflows cannot be read (that is exit 2, "no verdict" — never green)
  - Auditing ZERO caller/callee pairs. Examining nothing is a failure to audit, not a clean audit.

WHY IT DOES NOT USE `receives:` TO FIND ADOPTERS
  registry/repos.yml's `receives:` vocabulary reserves `lockfile-sync` but NO repo declares it (it
  is unpopulated in slice 1). Enumerating adopters that way audits zero repos and reports a vacuous
  green — which is the very defect class this gate exists to close. Adopters are therefore
  discovered from the caller workflows that actually exist: the roster supplies the repos to LOOK
  in, and the `uses:` lines say who is really an adopter.

HOW A CALLEE IS RESOLVED (this decides what the gate protects)
  At the ref the CALLER pins, because that is the YAML GitHub will actually load:
    @main              -> the LOCAL working tree. On a PR that edits a callee, this is the PROPOSED
                          content, so adding `packages: read` to a callee red-lights the callers who
                          do not grant it — BEFORE the merge strands them. Checking such a caller
                          against origin/main instead would report green on the very change that
                          breaks it.
    everything else    -> fetched from the API AT THAT REF (a sha, a tag, another branch). That
                          content is not what this PR changes. Judging such a caller against the
                          working tree would invent findings (a scope this PR adds to a callee the
                          caller does not point at) and miss real ones (a scope the pinned callee
                          needs that the working tree has since dropped).

Usage:
  check-workflow-permissions.py [--root <dir>] [--registry <file>] [--repo <full> ...]
      [--app-grants scope:level,...]
Exit: 0 = every caller grants at least its callee; 1 = at least one caller cannot start; 2 = no
verdict, RETRYABLE — a repo or workflow that could not be read (rate limit, auth, outage); 3 = no
verdict, PERMANENT — an unreadable roster, an audit that examined nothing, a callee that is missing
or unparsable, or a bad invocation.

"I could not check" must never share an exit code with "I checked, and it's fine" (#266) — nor with
"I checked, and it's broken" (#320) — nor "try again" with "a human must fix a file" (#335).
"""
from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
import time
import traceback

import yaml

OK, FINDING, NO_VERDICT_RETRYABLE, NO_VERDICT_PERMANENT = 0, 1, 2, 3

AUTHORITY = "FS-GG/.github"

# `uses: FS-GG/.github/.github/workflows/<callee>.yml@<ref>` — the org's reusable-workflow call.
CALL_RE = re.compile(
    r"^" + re.escape(AUTHORITY) + r"/\.github/workflows/(?P<callee>[^@/]+\.ya?ml)@(?P<ref>.+)$"
)

# The ONE ref that resolves to the working tree: this repo's default branch. A caller pinning it
# tracks what we are about to ship, so on a PR the proposed content is the honest subject.
#
# EVERY other ref — a tag, a sha, another branch — names content this PR does not change, and is
# fetched at that ref. Resolving a tag against the working tree instead would both invent findings
# (a scope this PR adds to a callee the tag does not point at) and miss real ones (a scope the
# tagged callee needs that the working tree has dropped).
AUTHORITY_DEFAULT_BRANCH = "main"

LEVELS = {"none": 0, "read": 1, "write": 2}

GH_TRIES = int(os.environ.get("FSGG_PERMS_TRIES", "3"))
GH_RETRY_DELAY = float(os.environ.get("FSGG_PERMS_RETRY_DELAY", "2"))
GH_TIMEOUT = float(os.environ.get("FSGG_PERMS_TIMEOUT", "30"))


class GateError(Exception):
    """A condition under which the gate must fail rather than skip. Maps to exit 3."""


class Unreachable(Exception):
    """We do not know what is there. Maps to exit 2 — never to green, never to a finding."""


class Missing(Exception):
    """A 404. That is a real answer from the API, not a failure to reach it."""


def gh_api(*args: str) -> str:
    """Read the GitHub API, distinguishing "not there" (404) from "could not tell" (anything else).

    Mirrors scripts/repos-audit.sh's transport deliberately: a 404 is an answer, and every other
    failure is retried and then surfaces as Unreachable. Isolated in one function so the fixture can
    stub `gh` on PATH.
    """
    delay = GH_RETRY_DELAY
    for attempt in range(1, GH_TRIES + 1):
        try:
            proc = subprocess.run(
                ["gh", "api", *args],
                capture_output=True, text=True, check=False, timeout=GH_TIMEOUT,
            )
        except subprocess.TimeoutExpired:
            # A hung socket is "could not tell", not "not there" — and without this it is not even
            # that: subprocess.run would block forever and the retry loop below would never run.
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
        if attempt >= GH_TRIES:
            raise Unreachable(err or f"gh exited {proc.returncode}")
        time.sleep(delay)
        delay *= 2
    raise Unreachable("unreachable")  # pragma: no cover — the loop always returns or raises


def load_yaml(text: str, what: str) -> dict:
    try:
        doc = yaml.safe_load(text)
    except yaml.YAMLError as e:
        raise GateError(f"{what}: not parsable as YAML — {e}") from e
    if not isinstance(doc, dict):
        raise GateError(f"{what}: not a YAML mapping")
    return doc


def triggers(doc: dict) -> dict:
    """The `on:` block. PyYAML resolves the bare key `on` to the boolean True (YAML 1.1), so a
    plain doc["on"] misses it and every reusable workflow would look like a non-callee."""
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


def parse_permissions(raw: object, what: str) -> dict[str, int]:
    """Normalise a present `permissions:` value to {scope: level}.

    Handles the shorthands: `read-all`, `write-all`, and `{}` (which grants nothing). The shorthands
    are returned as the sentinel scope "*", which satisfies any scope at that level.

    Never returns None. An ABSENT block is the caller's business to detect and is a different fact
    from a block that grants nothing — conflating them is how `permissions:` with no value came out
    as "grants nothing" instead of "this file is ambiguous, a human must look".
    """
    if raw is None:
        raise GateError(
            f"{what}: `permissions:` is present but has no value. That is not an empty grant and it "
            f"is not an absent block — it is ambiguous. Write an explicit mapping, `{{}}`, "
            f"`read-all`, or `write-all`."
        )
    if isinstance(raw, str):
        if raw == "read-all":
            return {"*": LEVELS["read"]}
        if raw == "write-all":
            return {"*": LEVELS["write"]}
        raise GateError(f"{what}: unknown `permissions:` shorthand {raw!r}")
    if isinstance(raw, dict):
        out: dict[str, int] = {}
        for scope, level in raw.items():
            if not isinstance(level, str) or level not in LEVELS:
                raise GateError(
                    f"{what}: `permissions: {scope}: {level!r}` is not one of "
                    f"{', '.join(LEVELS)}"
                )
            out[str(scope)] = LEVELS[level]
        return out  # {} is a real, empty grant — not an absent block
    raise GateError(f"{what}: `permissions:` is neither a mapping nor a known shorthand")


def granted(perms: dict[str, int], scope: str) -> int:
    """The level a caller grants for one scope. `read-all`/`write-all` cover every scope."""
    if "*" in perms:
        return perms["*"]
    return perms.get(scope, LEVELS["none"])


def level_name(level: int) -> str:
    return next(name for name, value in LEVELS.items() if value == level)


class Callees:
    """The reusable workflows of FS-GG/.github, resolved at whatever ref a caller pins."""

    def __init__(self, root: str) -> None:
        self.root = root
        self._cache: dict[tuple[str, str], dict[str, int] | None] = {}

    def _local(self, callee: str) -> str:
        path = os.path.join(self.root, ".github", "workflows", callee)
        if not os.path.isfile(path):
            raise GateError(
                f"{AUTHORITY} has no .github/workflows/{callee} in the working tree — a caller "
                f"pinning a mutable ref to it cannot start (startup_failure)"
            )
        with open(path, encoding="utf-8") as fh:
            return fh.read()

    def _at_ref(self, callee: str, ref: str) -> str:
        try:
            return gh_api(
                "-H", "Accept: application/vnd.github.raw",
                f"repos/{AUTHORITY}/contents/.github/workflows/{callee}?ref={ref}",
            )
        except Missing as e:
            raise GateError(
                f"{AUTHORITY} has no .github/workflows/{callee} at ref {ref} — the caller pinning "
                f"it cannot start (startup_failure): {e}"
            ) from e

    def permissions(self, callee: str, ref: str) -> dict[str, int] | None:
        """The callee's declared top-level permissions, or None if it declares no block at all.

        A callee with no block imposes no floor — it inherits whatever token the caller job has, so
        there is nothing the caller can under-grant.
        """
        key = (callee, ref)
        if key not in self._cache:
            text = (
                self._local(callee)
                if ref == AUTHORITY_DEFAULT_BRANCH
                else self._at_ref(callee, ref)
            )
            doc = load_yaml(text, f"{AUTHORITY}/.github/workflows/{callee}@{ref}")
            if "workflow_call" not in triggers(doc):
                raise GateError(
                    f"{AUTHORITY}/.github/workflows/{callee}@{ref} is called with `uses:` but does "
                    f"not declare `on: workflow_call` — the call cannot start"
                )
            what = f"{AUTHORITY}/.github/workflows/{callee}@{ref}"
            raw = doc["permissions"] if "permissions" in doc else ABSENT
            self._cache[key] = None if raw is ABSENT else parse_permissions(raw, what)
        return self._cache[key]


def rostered_repos(registry: str) -> list[str]:
    try:
        with open(registry, encoding="utf-8") as fh:
            roster = load_yaml(fh.read(), registry)
    except OSError as e:
        raise GateError(f"cannot read the roster {registry}: {e}") from e
    repos = [str(r["full"]).strip() for r in (roster.get("repos") or []) if r.get("full")]
    if not repos:
        raise GateError(f"{registry} rosters no repos — there is nothing to audit")
    return repos


def caller_workflows(repo: str) -> list[tuple[str, str]]:
    """Every workflow file in a repo, as (filename, text). Fails closed on an unreadable repo."""
    try:
        listing = gh_api(f"repos/{repo}/contents/.github/workflows", "--jq", ".[]?.name")
    except Missing:
        # A repo with no .github/workflows is not a finding — it adopts nothing. But confirm we can
        # actually SEE the repo, so a 404 from a private/renamed/deleted repo is never read as "it
        # has no workflows".
        try:
            gh_api(f"repos/{repo}", "--jq", ".full_name")
        except (Missing, Unreachable) as e:
            raise Unreachable(
                f"{repo} is not readable (private, renamed, or gone?) — {e}"
            ) from e
        return []

    out = []
    for name in (n.strip() for n in listing.splitlines()):
        if not name.endswith((".yml", ".yaml")):
            continue
        try:
            text = gh_api(
                "-H", "Accept: application/vnd.github.raw",
                f"repos/{repo}/contents/.github/workflows/{name}",
            )
        except Missing as e:  # listed a moment ago, now gone — we do not have a clean read
            raise Unreachable(f"{repo}: .github/workflows/{name} vanished mid-audit — {e}") from e
        out.append((name, text))
    return out


ABSENT = object()  # a job that declares no `permissions:` block, distinct from one declaring `{}`


def calls_in(doc: dict) -> list[tuple[str, str, str, object]]:
    """Every reusable-workflow call: (job_id, callee, ref, the job's own permissions or ABSENT)."""
    calls = []
    jobs = doc.get("jobs")
    if not isinstance(jobs, dict):
        return calls
    for job_id, job in jobs.items():
        if not isinstance(job, dict):
            continue
        uses = job.get("uses")
        if not isinstance(uses, str):
            continue
        m = CALL_RE.match(uses.strip())
        if m:
            perms = job["permissions"] if "permissions" in job else ABSENT
            calls.append((str(job_id), m.group("callee"), m.group("ref"), perms))
    return calls


def parse_app_grants(spec: str) -> dict[str, int]:
    """Parse the pinned installation grant inventory supplied by the workflow.

    GitHub's installation API requires org administration, which the ordinary workflow token
    cannot read.  PRs therefore compare against this explicit, reviewed inventory; the scheduled
    credential check is responsible for noticing a changed installation grant.  A malformed
    inventory is a no-verdict, never a green assumption.
    """
    grants: dict[str, int] = {}
    for entry in (part.strip() for part in spec.split(",")):
        if not entry or ":" not in entry:
            raise GateError("--app-grants must be non-empty comma-separated scope:read|write entries")
        scope, level = (part.strip() for part in entry.split(":", 1))
        scope = scope.replace("-", "_")
        if not re.fullmatch(r"[a-z][a-z0-9_]*", scope) or level not in LEVELS:
            raise GateError(f"--app-grants has invalid entry {entry!r}")
        grants[scope] = LEVELS[level]
    if not grants:
        raise GateError("--app-grants must name at least one installation grant")
    return grants


def app_token_requests(doc: dict, where: str) -> list[tuple[str, dict[str, int]]]:
    """Read static create-github-app-token requests in one workflow.

    The action treats any ungranted requested scope as fatal.  Dynamic permission values cannot
    be compared before merge, so they deliberately produce no verdict rather than a false green.
    """
    found: list[tuple[str, dict[str, int]]] = []
    jobs = doc.get("jobs")
    if not isinstance(jobs, dict):
        return found
    for job_id, job in jobs.items():
        if not isinstance(job, dict) or not isinstance(job.get("steps"), list):
            continue
        for index, step in enumerate(job["steps"], start=1):
            if not isinstance(step, dict) or not isinstance(step.get("uses"), str):
                continue
            if not step["uses"].strip().startswith("actions/create-github-app-token@"):
                continue
            inputs = step.get("with", {})
            if not isinstance(inputs, dict):
                raise GateError(f"{where} [{job_id}] App-token step {index}: `with:` is not a mapping")
            requested: dict[str, int] = {}
            for key, value in inputs.items():
                key = str(key)
                if not key.startswith("permission-"):
                    continue
                scope = key.removeprefix("permission-").replace("-", "_")
                if not isinstance(value, str) or "${{" in value or value not in LEVELS:
                    raise GateError(
                        f"{where} [{job_id}] App-token step {index}: {key} must be a static "
                        "none, read, or write value so the grant can be checked before merge"
                    )
                requested[scope] = LEVELS[value]
            found.append((f"{where} [{job_id}] App-token step {index}", requested))
    return found


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description=__doc__.splitlines()[0],
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    ap.add_argument("--root", default=".", help="the FS-GG/.github working tree (default: .)")
    ap.add_argument("--registry", default=None, help="the roster (default: <root>/registry/repos.yml)")
    ap.add_argument("--repo", action="append", default=[],
                    help="audit only this repo (repeatable); default: every rostered repo")
    ap.add_argument("--app-grants", default=None,
                    help="pinned installation grants (scope:read|write,...) to compare against App-token requests")
    args = ap.parse_args(argv)

    registry = args.registry or os.path.join(args.root, "registry", "repos.yml")
    callees = Callees(args.root)

    findings: list[str] = []
    pairs = 0

    try:
        app_grants = parse_app_grants(args.app_grants) if args.app_grants is not None else None
    except GateError as e:
        print(f"::error::check-workflow-permissions: no verdict — {e}", file=sys.stderr)
        return NO_VERDICT_PERMANENT

    if app_grants is not None:
        workflow_dir = os.path.join(args.root, ".github", "workflows")
        try:
            filenames = sorted(name for name in os.listdir(workflow_dir) if name.endswith((".yml", ".yaml")))
        except OSError as e:
            print(f"::error::check-workflow-permissions: no verdict — cannot read {workflow_dir}: {e}", file=sys.stderr)
            return NO_VERDICT_PERMANENT
        for name in filenames:
            path = os.path.join(workflow_dir, name)
            try:
                with open(path, encoding="utf-8") as fh:
                    requests = app_token_requests(load_yaml(fh.read(), path), path)
            except (OSError, GateError) as e:
                print(f"::error::check-workflow-permissions: no verdict — {e}", file=sys.stderr)
                return NO_VERDICT_PERMANENT
            for subject, requested in requests:
                short = [
                    f"{scope}: requests {level_name(want)}, installation grants {level_name(granted(app_grants, scope))}"
                    for scope, want in sorted(requested.items())
                    if granted(app_grants, scope) < want
                ]
                if short:
                    findings.append(
                        f"{subject}: App-token request is not granted by the pinned installation inventory: "
                        f"{'; '.join(short)}. GitHub refuses the whole mint before later steps run."
                    )

    try:
        repos = args.repo or rostered_repos(registry)
    except GateError as e:
        print(f"::error::check-workflow-permissions: {e}", file=sys.stderr)
        return NO_VERDICT_PERMANENT

    for repo in repos:
        try:
            workflows = caller_workflows(repo)
        except Unreachable as e:
            print(f"::error::check-workflow-permissions: no verdict for {repo} — {e}",
                  file=sys.stderr)
            return NO_VERDICT_RETRYABLE

        for name, text in workflows:
            where = f"{repo}/.github/workflows/{name}"
            try:
                doc = load_yaml(text, where)
            except GateError as e:
                print(f"::error::check-workflow-permissions: {e}", file=sys.stderr)
                return NO_VERDICT_PERMANENT

            top = doc["permissions"] if "permissions" in doc else ABSENT

            for job_id, callee, ref, job_perms in calls_in(doc):
                pairs += 1
                subject = f"{where} [{job_id}] -> {callee}@{ref}"
                try:
                    need = callees.permissions(callee, ref)
                except GateError as e:
                    print(f"::error::check-workflow-permissions: {subject}: {e}", file=sys.stderr)
                    return NO_VERDICT_PERMANENT
                except Unreachable as e:
                    print(f"::error::check-workflow-permissions: no verdict for {subject} — {e}",
                          file=sys.stderr)
                    return NO_VERDICT_RETRYABLE

                if not need:  # the callee declares no scopes, so nothing can be under-granted
                    print(f"  ok   {repo:22} {name:28} -> {callee:28} (callee declares no scopes)")
                    continue

                # The job's own block wins over the workflow's; that is GitHub's precedence.
                raw = job_perms if job_perms is not ABSENT else top
                if raw is ABSENT:
                    findings.append(
                        f"{subject}: the caller declares NO `permissions:` block, at either the "
                        f"workflow or the job level, but the callee declares "
                        f"{', '.join(f'{s}: {level_name(l)}' for s, l in sorted(need.items()))}. "
                        f"The effective token is then the org/repo DEFAULT, which is not in this "
                        f"file and which an admin can change — so it cannot be proven that the run "
                        f"starts. Declare the grant explicitly."
                    )
                    continue

                try:
                    have = parse_permissions(raw, subject)
                except GateError as e:
                    print(f"::error::check-workflow-permissions: {e}", file=sys.stderr)
                    return NO_VERDICT_PERMANENT

                short = []
                for scope, want in sorted(need.items()):
                    got = granted(have, scope)
                    if got < want:
                        short.append(
                            f"{scope}: needs {level_name(want)}, caller grants {level_name(got)}"
                        )
                if short:
                    findings.append(
                        f"{subject}: the caller under-grants {'; '.join(short)}. A called workflow "
                        f"cannot request a permission its caller did not grant, so GitHub kills "
                        f"this run at startup (startup_failure) — it never executes, and it shows "
                        f"as neither red nor green on the PR."
                    )
                else:
                    print(f"  ok   {repo:22} {name:28} -> {callee:28} "
                          f"({', '.join(f'{s}: {level_name(l)}' for s, l in sorted(need.items()))})")

    if pairs == 0:
        print(
            "::error::check-workflow-permissions: audited 0 caller/callee pair(s) over "
            f"{len(repos)} repo(s) — no rostered repo calls a reusable workflow of {AUTHORITY}. "
            "Examining nothing is a failure to audit, not a clean audit.",
            file=sys.stderr,
        )
        return NO_VERDICT_PERMANENT

    if findings:
        for f in findings:
            print(f"::error::check-workflow-permissions: {f}", file=sys.stderr)
        print(f"\n{len(findings)} caller(s) cannot start, of {pairs} caller/callee pair(s) over "
              f"{len(repos)} repo(s).", file=sys.stderr)
        return FINDING

    print(f"ok: every caller grants at least its callee — {pairs} caller/callee pair(s) over "
          f"{len(repos)} rostered repo(s).")
    return OK


def cli(argv: list[str]) -> int:
    """Guarantee the exit code is a VERDICT, never an accident.

    Python exits 1 on any uncaught exception — and 1 is this gate's "a caller under-grants, its
    workflow provably cannot start". So a crash anywhere in here would be dressed up by
    permission-coherence.yml as a specific, confident, WRONG finding about somebody's permissions
    block. That is precisely the conflation the exit-code contract exists to prevent (#266, #320):
    "I could not check" must never share a code with "I checked, and it's broken".

    An unforeseen exception means the gate does not know. It says so, and exits 3.
    """
    try:
        return main(argv)
    except Unreachable as e:
        print(f"::error::check-workflow-permissions: no verdict — {e}", file=sys.stderr)
        return NO_VERDICT_RETRYABLE
    except GateError as e:
        print(f"::error::check-workflow-permissions: no verdict — {e}", file=sys.stderr)
        return NO_VERDICT_PERMANENT
    except Exception:  # noqa: BLE001 — deliberately broad; see the docstring
        traceback.print_exc()
        print(
            "::error::check-workflow-permissions: the gate crashed, so it has NO VERDICT. This is "
            "not a finding about any caller's permissions — it is a bug in the gate. See the "
            "traceback above.",
            file=sys.stderr,
        )
        return NO_VERDICT_PERMANENT


if __name__ == "__main__":
    sys.exit(cli(sys.argv[1:]))
