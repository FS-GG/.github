#!/usr/bin/env python3
"""Assert the PR workflows named in GOVERNED carry PR-keyed `concurrency`, and nothing broader.

.github#4730 (CI load). Actions capacity, not review, was the binding constraint on delivery: about
6,233 runs a day, about 51 jobs per PR head, and a plan that runs about 20 jobs at once. A workflow with
no `concurrency` lets every superseded head — and every body edit that re-triggers `edited` — run to
completion and hold a runner for nothing. 223 stale-head runs a day completed for exactly that reason.

WHAT EACH GOVERNED WORKFLOW MUST DECLARE, verbatim:

    concurrency:
      group: <workflow name>-${{ github.event.pull_request.number || github.run_id }}
      cancel-in-progress: ${{ github.event_name == 'pull_request' }}

WHY `github.run_id` FOR A NON-PR EVENT (not `github.ref`, not `github.sha`). GitHub keeps at most ONE running
and ONE pending run per group and discards an older pending run when a newer one arrives, whatever
`cancel-in-progress` says. So any non-PR key that two events can share loses a run:
  * `github.ref` (Root's first key): two quick merges to main share `refs/heads/main`, and the first
    merge's pending run is dropped, a main push run that never reports.
  * `github.sha` (the key #5001 merged with): every push and merge_group entry has its own commit, but
    events that arrive at ONE commit share it: two `workflow_dispatch` runs, an `issues` burst, a
    `repository_dispatch` pair, or a `schedule` tick on an unchanged main. The older pending one is dropped.
A run id is unique to a run, so a non-PR event never shares a group with anything and there is nothing to
cancel or replace. PR events still key on the PR number, so a superseded head is cancelled. (Root decision on
#4730, replacing the sha fallback; the scenarios below include same-commit pairs, which the sha key fails.)

A JOB-LEVEL `concurrency:` IS REFUSED TOO. GitHub accepts one on any job, with its own group and its own
cancel flag, and it does not have to agree with the workflow-level block: a job-level
`cancel-in-progress: true` on a stable group would cancel main-push or merge_group runs of that job
while the workflow-level block (and every scenario modelled below) still reads clean. None of the 136
pull_request-triggered workflows has one today, so refusing it costs nothing and closes the bypass.

WHY THESE (the basis, recorded rather than implied).
  * The first 17 are Root's decision on #4730 from the capacity analysis (stale-head runs that ran to
    completion for lack of `concurrency`). Verified when #5001 added them: each triggers on `pull_request`
    and had no workflow-level block.
  * The next 41 are every other workflow that triggers on `pull_request` with no workflow-level block on
    origin/main (bce309c8 + 4 commits), minus the exclusions below, ordered here by pull_request /
    pull_request_target run count over 2026-10-24..2026-10-31 read from the Actions API (the analysis's own
    per-workflow counts were not available to me): atlas 79, board-v2-import-preflight 42, work-programme 21,
    registry-changelog 16, skill-union-selftest 12, sync-build-config-selftest 11, org-permission-policy 7,
    then 6 down to 0 for the rest (twelve of them 0, being path-filtered selftests). Every one is read-only
    (`contents: read`, at most `packages: read`), so cancelling a superseded PR run cannot strand a write.
  * `routine-eligibility` (398 runs, the largest consumer) was held for #4994, which has merged. It runs on
    `pull_request_target` only, where `github.ref` is the default branch, so it has its own shape in
    PR_ONLY: group `<name>-${{ github.event.pull_request.number }}`, unconditional cancel.
  * Excluded on purpose: `release-successor-authenticate-source`, an inert protected-intake caller whose own
    checker (`check-release-successor-authenticate.py`, 'extra source caller surface') accepts exactly the keys
    name/on/permissions/jobs, so a top-level `concurrency` is refused there; relaxing a protected-intake
    checker is not this gate's call. `update-fsgg-coord-cli-selftest`, which is red on any PR that touches it
    (`test_fake_installer_inherits_runtime_resource_policy` compares an RLIMIT read in the parent with one read
    in the installer's child: 18446744073709551615 expected, -1 got, on the runner at 2026-10-31; it passed on
    2026-10-19 and passes locally; filed as #5018), a latent red that path filtering hides and that
    concurrency would neither cause nor fix.
  * `cross-repo-request-predicate` (also triggers on `issues`) and `feed-autofix` (`repository_dispatch`) were
    held back while the fallback was `github.sha`, because events at one commit would share a group. With
    `github.run_id` that hazard is gone and both are governed.
  * `landed-revert-guard` (landed by #5008) already had the PR-only block; it is listed in PR_ONLY so the gate
    pins it. `claim-generation` (#5004, held) is not on main yet; its block is the default shape, so it goes in
    GOVERNED when it lands.
  * `wizard-graph-seal` already declares `<name>-${{ github.ref }}`, cancel true, and is one of the 73 PR
    workflows with a block of their own (many `<name>-${{ github.ref }}`, which also cancels main pushes).
  On that tree 136 workflows trigger on pull_request or pull_request_target: 60 governed here, 74 with their
  own block, 2 with none (the exclusions). Extending the list means reading each workflow first.

THE MODEL BELOW is this file's causal control. It evaluates the two declared expressions for a set of
events and applies GitHub's documented group semantics, so "a superseded PR head is cancellable and a
main push is not" is derived from the workflow text, not asserted next to it. tests/pr-concurrency/run.sh
mutates every governed workflow and demands each mutation be refused.

THE EXPRESSION GRAMMAR IS CLOSED. Anything outside `a || b` of context paths for the group and
`context == 'literal'` for the cancel flag is refused rather than guessed (#266: examining nothing, or
something this gate cannot read, is not a clean audit).

Usage:  check-pr-concurrency.py [--root <dir>] [--list]
Exit:   0 = every governed workflow declares the exact shape and the model agrees;
        1 = at least one does not; 3 = no verdict, PERMANENT (a workflow is missing or unparsable).
"""
from __future__ import annotations

import argparse
import itertools
import re
import sys
from pathlib import Path

try:
    import yaml
except ImportError:  # pragma: no cover - the policy bootstrap installs PyYAML
    print("check-pr-concurrency: PyYAML is required", file=sys.stderr)
    sys.exit(3)

# The workflows #4730 PR-1 governs. wizard-graph-seal.yml already declares `<name>-${{ github.ref }}`
# with cancel-in-progress true (PR-keyed through refs/pull/N/merge, and it has no push trigger), so it
# is deliberately NOT listed. routine-eligibility.yml is held back until #4994 merges.
GOVERNED = [
    "architecture-map", "atlas", "board-paths-live-selftest", "board-v2-import-preflight",
    "c3-audio-ordinary-enrollment-bridge-selftest",
    "c3-coordination-ordinary-enrollment-bridge-selftest",
    "c3-game-ordinary-enrollment-bridge-selftest", "c3-governance-ordinary-enrollment-bridge-selftest",
    "c3-net-ordinary-enrollment-bridge-selftest", "c3-rendering-ordinary-enrollment-bridge-selftest",
    "coherence", "coord-report-selftest", "coordination-sync-selftest", "cross-repo-request-predicate",
    "dispatch-preflight-selftest", "feed-autofix", "fsc-progress-evidence", "fsgg-claim-fence",
    "fsgg-dispatch-broker-selftest", "generator-list", "gs2-09-7-receiver-observer-probe-selftest",
    "kit-package-selftest", "learn-finite-profile-selftest", "learn-seal-claim-selftest",
    "lock-range-coherence-selftest", "lockfile-cold-selftest", "new-sdd-workspace-selftest",
    "observed-command-report-selftest", "org-permission-policy", "org-policy", "parity-fixtures",
    "project-field-options", "protected-intake-preflight-selftest", "provisioned-podman-source",
    "recipe-followup", "recipe-landable", "registry-changelog", "release-saga-tooling",
    "release-tag-anchors-selftest", "repos-audit-selftest", "repos-registry-selftest",
    "rest-budget-preflight-selftest", "routine-eligibility-selftest", "skill-quality",
    "skill-registry-autofix", "skill-registry-coherence", "skill-roots-selfcheck",
    "skill-union-selftest", "skill-view", "skill-view-parity", "skillmirror-redrive",
    "surface-impact-selftest", "sync-build-config-selftest", "telemetry-git-safety", "touch-set-drift",
    "touch-set-drift-selftest", "usage-census-selftest", "work-programme",
]

# Workflows whose ONLY pull-request trigger is the single event they run on (`pull_request_target`, or
# `pull_request` with nothing else): every run is a PR run, so the key is the PR number alone (for
# `pull_request_target`, `github.ref` is the DEFAULT branch, so it could not be the key anyway) and the
# cancel is unconditional. Same closed grammar, its own shape. `landed-revert-guard` already had exactly
# this block when #5008 landed it; listing it here makes the gate pin it.
PR_ONLY = ["landed-revert-guard", "routine-eligibility"]
PR_ONLY_GROUP = "{name}-${{{{ github.event.pull_request.number }}}}"

GROUP_FORMAT = "{name}-${{{{ github.event.pull_request.number || github.run_id }}}}"
CANCEL_EXPR = "${{ github.event_name == 'pull_request' }}"

_EXPR = re.compile(r"^\$\{\{\s*(.*?)\s*\}\}$")


def _unwrap(text: str) -> str:
    m = _EXPR.match(text.strip())
    if not m:
        raise ValueError(f"not a single ${{{{ }}}} expression: {text!r}")
    return m.group(1)


def _lookup(path: str, event: dict):
    """Resolve a github-context path against a synthetic event; absent or 0 is falsy (None)."""
    table = {
        "github.event.pull_request.number": event.get("pr"),
        "github.sha": event.get("sha"),
        "github.run_id": event.get("run_id"),
        "github.ref": event.get("ref"),
        "github.event_name": event.get("name"),
    }
    if path not in table:
        raise ValueError(f"context {path!r} is outside this gate's closed grammar")
    return table[path] or None


def group_of(workflow: str, group: str, event: dict) -> str:
    """Evaluate `<prefix>-${{ a || b }}` (the only accepted group grammar) for one event."""
    m = re.fullmatch(r"(?P<prefix>[A-Za-z0-9_.-]+-)(?P<expr>\$\{\{.*\}\})", group.strip())
    if not m:
        raise ValueError(f"group {group!r} is not `<literal>-${{{{ expr }}}}`")
    operands = [part.strip() for part in _unwrap(m.group("expr")).split("||")]
    for operand in operands:
        value = _lookup(operand, event)
        if value is not None:
            return f"{workflow}:{m.group('prefix')}{value}"
    return f"{workflow}:{m.group('prefix')}"


def cancel_of(cancel, event: dict) -> bool:
    if isinstance(cancel, bool):
        return cancel
    m = re.fullmatch(r"(?P<lhs>[a-z_.]+)\s*==\s*'(?P<rhs>[A-Za-z_]+)'", _unwrap(str(cancel)))
    if not m:
        raise ValueError(f"cancel-in-progress {cancel!r} is outside this gate's closed grammar")
    return _lookup(m.group("lhs"), event) == m.group("rhs")


_RUNS = itertools.count(1000)


def _event(name: str, pr: int | None, sha: str, ref: str) -> dict:
    """One synthetic event. `run_id` is unique per call, as GitHub's is per run."""
    return {"name": name, "pr": pr, "sha": sha, "ref": ref, "run_id": next(_RUNS)}


def pr(number: int, sha: str, name: str = "pull_request") -> dict:
    return _event(name, number, sha, f"refs/pull/{number}/merge")


def main_push(sha: str) -> dict:
    return _event("push", None, sha, "refs/heads/main")


def merge_group(sha: str) -> dict:
    return _event("merge_group", None, sha, f"refs/heads/gh-readonly-queue/main/pr-1-{sha}")


def dispatch(sha: str) -> dict:
    return _event("workflow_dispatch", None, sha, "refs/heads/main")


def other(name: str, sha: str) -> dict:
    """`schedule`, `issues`, `repository_dispatch`: all run at the default branch's current commit."""
    return _event(name, None, sha, "refs/heads/main")


def displaced(workflow: str, block: dict, running: dict, arriving: dict) -> str:
    """What happens to `running` when `arriving` is queued under GitHub's group semantics.

    'cancelled'  : same group and the arriving run cancels in-progress runs.
    'none'       : different group, or same group without cancel (a PENDING run, not this one, is replaced).
    """
    same = group_of(workflow, block["group"], running) == group_of(workflow, block["group"], arriving)
    return "cancelled" if same and cancel_of(block["cancel-in-progress"], arriving) else "none"


def pending_replaced(workflow: str, block: dict, pending: dict, arriving: dict) -> bool:
    """A PENDING run is dropped whenever a newer run lands in the same group, cancel flag or not."""
    return group_of(workflow, block["group"], pending) == group_of(workflow, block["group"], arriving)


def scenarios(workflow: str, block: dict) -> list[tuple[str, bool, bool]]:
    """(description, observed, wanted) — each is one causal claim derived from the declared block."""
    out = []

    def claim(text: str, observed: bool, wanted: bool) -> None:
        out.append((text, observed, wanted))

    claim("a PR push (new head) cancels the superseded head's run",
          displaced(workflow, block, pr(7, "a" * 40), pr(7, "b" * 40)) == "cancelled", True)
    claim("a PR body edit (same head) cancels the run it supersedes",
          displaced(workflow, block, pr(7, "a" * 40), pr(7, "a" * 40)) == "cancelled", True)
    claim("another PR's run is never cancelled",
          displaced(workflow, block, pr(7, "a" * 40), pr(8, "b" * 40)) == "cancelled", False)
    claim("a main push does not cancel the previous main push run",
          displaced(workflow, block, main_push("a" * 40), main_push("b" * 40)) == "cancelled", False)
    claim("a main push does not drop the previous main push's PENDING run",
          pending_replaced(workflow, block, main_push("a" * 40), main_push("b" * 40)), False)
    claim("a main push does not cancel a PR run",
          displaced(workflow, block, pr(7, "a" * 40), main_push("b" * 40)) == "cancelled", False)
    claim("a PR run does not cancel a main push run",
          displaced(workflow, block, main_push("a" * 40), pr(7, "b" * 40)) == "cancelled", False)
    claim("a merge_group run is never cancelled by a later one",
          displaced(workflow, block, merge_group("a" * 40), merge_group("b" * 40)) == "cancelled", False)
    claim("a manual dispatch is never cancelled by a later one",
          displaced(workflow, block, dispatch("a" * 40), dispatch("b" * 40)) == "cancelled", False)
    # Events that arrive at ONE commit (a sha key shares a group between them).
    for kind in ("workflow_dispatch", "schedule", "issues", "repository_dispatch", "push"):
        first, second = other(kind, "c" * 40), other(kind, "c" * 40)
        claim(f"two {kind} runs at one commit are not cancelled",
              displaced(workflow, block, first, second) == "cancelled", False)
        claim(f"two {kind} runs at one commit do not drop the older PENDING run",
              pending_replaced(workflow, block, first, second), False)
    return out


def pr_only_scenarios(workflow: str, block: dict, event: str = "pull_request_target") -> list[tuple[str, bool, bool]]:
    """The same causal claims for a PR-event-only workflow (every run is a PR run)."""
    def ev(number: int, sha: str) -> dict:
        ref = "refs/heads/main" if event == "pull_request_target" else f"refs/pull/{number}/merge"
        return {"name": event, "pr": number, "sha": sha, "ref": ref}

    out = []
    out.append(("a PR push (new head) cancels the superseded head's run",
                displaced(workflow, block, ev(7, "a" * 40), ev(7, "b" * 40)) == "cancelled", True))
    out.append(("a PR body edit (same head) cancels the run it supersedes",
                displaced(workflow, block, ev(7, "a" * 40), ev(7, "a" * 40)) == "cancelled", True))
    out.append(("another PR's run is never cancelled",
                displaced(workflow, block, ev(7, "a" * 40), ev(8, "a" * 40)) == "cancelled", False))
    return out


def audit(root: Path) -> tuple[int, list[str], int]:
    findings: list[str] = []
    checked = 0
    for name in GOVERNED + PR_ONLY:
        target = name in PR_ONLY
        path = root / ".github/workflows" / f"{name}.yml"
        if not path.is_file():
            print(f"check-pr-concurrency: no verdict — {path} does not exist", file=sys.stderr)
            return 3, findings, checked
        try:
            doc = yaml.safe_load(path.read_text(encoding="utf-8"))
        except yaml.YAMLError as exc:
            print(f"check-pr-concurrency: no verdict — {path} will not parse: {exc}", file=sys.stderr)
            return 3, findings, checked
        block = doc.get("concurrency") if isinstance(doc, dict) else None
        if not isinstance(block, dict):
            findings.append(f"{name}.yml: no workflow-level `concurrency:` mapping")
            continue
        trig = (doc.get("on", doc.get(True)) if isinstance(doc, dict) else None) or {}
        event = "pull_request_target" if "pull_request_target" in trig else "pull_request"
        want_group = (PR_ONLY_GROUP if target else GROUP_FORMAT).format(name=name)
        want_cancel = True if target else CANCEL_EXPR
        if block.get("group") != want_group:
            findings.append(f"{name}.yml: concurrency.group is {block.get('group')!r}, want {want_group!r}")
        if block.get("cancel-in-progress") != want_cancel:
            findings.append(f"{name}.yml: cancel-in-progress is {block.get('cancel-in-progress')!r}, want {want_cancel!r}")
        if set(block) - {"group", "cancel-in-progress"}:
            findings.append(f"{name}.yml: unexpected concurrency key(s) {sorted(set(block) - {'group', 'cancel-in-progress'})}")
        for jid, job in (doc.get("jobs") or {}).items():
            if isinstance(job, dict) and "concurrency" in job:
                findings.append(f"{name}.yml: job `{jid}` declares its own job-level concurrency, which can cancel "
                                "runs the workflow-level block leaves alone")
        try:
            for text, observed, wanted in (lambda n, b: pr_only_scenarios(n, b, event) if target else scenarios(n, b))(name, block):
                checked += 1
                if observed != wanted:
                    findings.append(f"{name}.yml: model says it is NOT true that {text}" if wanted
                                    else f"{name}.yml: model says it IS true that {text}")
        except ValueError as exc:
            findings.append(f"{name}.yml: {exc}")
    return (1 if findings else 0), findings, checked


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--root", default=".")
    ap.add_argument("--list", action="store_true", help="print the governed workflow names and exit")
    ap.add_argument("--list-pr-only", action="store_true", help="print the PR-event-only names and exit")
    args = ap.parse_args()
    if args.list:
        print("\n".join(GOVERNED))
        return 0
    if args.list_pr_only:
        print("\n".join(PR_ONLY))
        return 0
    rc, findings, checked = audit(Path(args.root))
    if rc == 3:
        return 3
    for line in findings:
        print(f"FINDING {line}")
    if rc == 0:
        print(f"ok: {len(GOVERNED)} governed workflow(s) (+{len(PR_ONLY)} PR-event-only) declare the PR-keyed shape; "
              f"{checked} causal scenario(s) hold (cancel on supersede, no cancel or pending-drop on main push)")
    return rc


if __name__ == "__main__":
    sys.exit(main())
