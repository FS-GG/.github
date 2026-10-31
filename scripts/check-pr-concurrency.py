#!/usr/bin/env python3
"""Assert the PR workflows named in GOVERNED carry PR-keyed `concurrency`, and nothing broader.

.github#4730 (CI load). Actions capacity, not review, was the binding constraint on delivery: about
6,233 runs a day, about 51 jobs per PR head, and a plan that runs about 20 jobs at once. A workflow with
no `concurrency` lets every superseded head — and every body edit that re-triggers `edited` — run to
completion and hold a runner for nothing. 223 stale-head runs a day completed for exactly that reason.

WHAT EACH GOVERNED WORKFLOW MUST DECLARE, verbatim:

    concurrency:
      group: <workflow name>-${{ github.event.pull_request.number || github.sha }}
      cancel-in-progress: ${{ github.event_name == 'pull_request' }}

WHY `github.sha` AND NOT `github.ref` FOR A NON-PR EVENT. Root's decision keyed the fallback on
`github.ref` so that "main push runs stay unchanged". That intent is not met by `github.ref` with
`cancel-in-progress: false`: GitHub keeps at most ONE running and ONE pending run per group and
discards an older pending run when a newer one arrives, whatever `cancel-in-progress` says. Two quick
merges to main would then share the `refs/heads/main` group and the first merge's pending run would be
dropped — a main push run that never reports. The commit sha gives every push, merge_group entry and
dispatch its own group, so there is nothing to be pending-replaced and nothing to cancel.

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
    "architecture-map", "coherence", "coordination-sync-selftest", "fsgg-claim-fence",
    "fsgg-dispatch-broker-selftest", "generator-list", "new-sdd-workspace-selftest",
    "recipe-followup", "recipe-landable", "repos-registry-selftest", "skill-quality",
    "skill-registry-coherence", "skill-roots-selfcheck", "skill-view", "skill-view-parity",
    "telemetry-git-safety", "touch-set-drift",
]

GROUP_FORMAT = "{name}-${{{{ github.event.pull_request.number || github.sha }}}}"
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


def pr(number: int, sha: str, name: str = "pull_request") -> dict:
    return {"name": name, "pr": number, "sha": sha, "ref": f"refs/pull/{number}/merge"}


def main_push(sha: str) -> dict:
    return {"name": "push", "pr": None, "sha": sha, "ref": "refs/heads/main"}


def merge_group(sha: str) -> dict:
    return {"name": "merge_group", "pr": None, "sha": sha, "ref": f"refs/heads/gh-readonly-queue/main/pr-1-{sha}"}


def dispatch(sha: str) -> dict:
    return {"name": "workflow_dispatch", "pr": None, "sha": sha, "ref": "refs/heads/main"}


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
    return out


def audit(root: Path) -> tuple[int, list[str], int]:
    findings: list[str] = []
    checked = 0
    for name in GOVERNED:
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
        want_group = GROUP_FORMAT.format(name=name)
        if block.get("group") != want_group:
            findings.append(f"{name}.yml: concurrency.group is {block.get('group')!r}, want {want_group!r}")
        if block.get("cancel-in-progress") != CANCEL_EXPR:
            findings.append(f"{name}.yml: cancel-in-progress is {block.get('cancel-in-progress')!r}, want {CANCEL_EXPR!r}")
        if set(block) - {"group", "cancel-in-progress"}:
            findings.append(f"{name}.yml: unexpected concurrency key(s) {sorted(set(block) - {'group', 'cancel-in-progress'})}")
        try:
            for text, observed, wanted in scenarios(name, block):
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
    args = ap.parse_args()
    if args.list:
        print("\n".join(GOVERNED))
        return 0
    rc, findings, checked = audit(Path(args.root))
    if rc == 3:
        return 3
    for line in findings:
        print(f"FINDING {line}")
    if rc == 0:
        print(f"ok: {len(GOVERNED)} governed workflow(s) declare the PR-keyed shape; "
              f"{checked} causal scenario(s) hold (cancel on supersede, no cancel or pending-drop on main push)")
    return rc


if __name__ == "__main__":
    sys.exit(main())
