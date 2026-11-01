#!/usr/bin/env python3
"""Causal controls for the routine marker's push-parent allowance (.github#5189).

A push cannot carry a marker naming a head that did not exist when the body was written. Before this
change every push to a routine PR failed `routine-eligibility` once and then cost a body edit to clear.
The workflow now offers GitHub's own `synchronize` `before` as a provisional binding when the push only
added commits on top of it; the exact binding is enforced at the merge boundary instead.

Every workflow scenario executes the REAL workflow shell, the REAL base-loaded envelope and the REAL
validator/policy over a real shallow `file://` transport (the moved_base.py fixture). The pre-#5189
workflow bytes (fixtures/pre-5189-routine-eligibility.workflow.txt) are the mutation control: identical
inputs, only the workflow differs. The merge-boundary control calls the real `routine_contract` of
tools/routine-delivery.py with a recording native API.
"""
from __future__ import annotations

import importlib.util
import pathlib
import subprocess
import sys
import tempfile

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import moved_base as mb  # noqa: E402  (the shared real-shell fixture)

ROOT = mb.ROOT
NEW = mb.NEW
PRE = HERE / "fixtures/pre-5189-routine-eligibility.workflow.txt"
marker = mb.marker
expect = mb.expect
RESULTS: list[tuple[str, str]] = []
SCENARIOS: list = []


def scenario(name: str, kind: str):
    def wrap(fn):
        def run() -> None:
            with tempfile.TemporaryDirectory(prefix="routine-push-parent.") as scratch:
                fn(mb.Fixture(pathlib.Path(scratch)))
            RESULTS.append((name, kind))
        SCENARIOS.append(run)
        return run
    return wrap


def push(f: mb.Fixture, parent: str, count: int = 1, tag: str = "push") -> str:
    """Add `count` commits on top of `parent` and publish the tip as the PR head (a fast-forward push)."""
    f.git("checkout", "-q", "-B", "pr", parent)
    for index in range(count):
        f.put(f"docs/{tag}-{index}.md", f"{tag} {index}\n")
        f.commit(f"{tag} {index}")
    tip = f.git("rev-parse", "HEAD")
    f.set_pull_head(tip)
    f.git("checkout", "-q", "main")
    return tip


def sync(before: str) -> dict[str, str]:
    return {"EVENT_ACTION": "synchronize", "EVENT_BEFORE": before}


def edited() -> dict[str, str]:
    return {"EVENT_ACTION": "edited", "EVENT_BEFORE": ""}


# ---------------------------------------------------------------- the defect and its fix
@scenario("push-parent-marker-is-admitted-provisionally-with-a-rebind-warning", "admit")
def _(f: mb.Fixture) -> None:
    h1 = f.start_pr()
    h2 = push(f, h1)
    out = expect(f.run(NEW, event_base=f.base0, event_head=h2, body=marker(h1), event=sync(h1)),
                 ok=True, needle="routine delivery provisionally admitted")
    assert f"push added commits on top of previous head {h1}" in out, out
    assert "::warning::" in out and f"rebind the marker to head={h2} before merge" in out, out
    assert "changedPaths=2" in out, out  # the new head's whole change is still measured


@scenario("pre-5189-workflow-refuses-the-same-push", "mutation")
def _(f: mb.Fixture) -> None:
    h1 = f.start_pr()
    h2 = push(f, h1)
    expect(f.run(PRE, event_base=f.base0, event_head=h2, body=marker(h1), event=sync(h1)),
           ok=False, needle="routine-changed-head")


@scenario("multi-commit-push-connects-by-bounded-deepening", "admit")
def _(f: mb.Fixture) -> None:
    h1 = f.start_pr()
    h2 = push(f, h1, count=20)
    expect(f.run(NEW, event_base=f.base0, event_head=h2, body=marker(h1), event=sync(h1)),
           ok=True, needle="routine delivery provisionally admitted")


@scenario("push-parent-with-a-moved-base-is-still-admitted", "admit")
def _(f: mb.Fixture) -> None:
    h1 = f.start_pr()
    h2 = push(f, h1)
    f.move_main({"unrelated.txt": "main moved\n"})
    out = expect(f.run(NEW, event_base=f.base0, event_head=h2, body=marker(h1), event=sync(h1)),
                 ok=True, needle="routine delivery provisionally admitted")
    assert "base moved" in out and "changedPaths=2" in out, out


# ---------------------------------------------------------------- rebind
@scenario("rebind-edit-is-admitted-exactly-not-provisionally", "admit")
def _(f: mb.Fixture) -> None:
    h1 = f.start_pr()
    h2 = push(f, h1)
    out = expect(f.run(NEW, event_base=f.base0, event_head=h2, body=marker(h2), event=edited()),
                 ok=True, needle=f"routine delivery admitted at exact head {h2}")
    assert "provisionally" not in out and "::warning::" not in out, out


@scenario("rebound-marker-on-a-synchronize-run-is-exact", "admit")
def _(f: mb.Fixture) -> None:
    h1 = f.start_pr()
    h2 = push(f, h1)
    out = expect(f.run(NEW, event_base=f.base0, event_head=h2, body=marker(h2), event=sync(h1)),
                 ok=True, needle=f"routine delivery admitted at exact head {h2}")
    assert "provisionally" not in out, out


# ---------------------------------------------------------------- stale markers
@scenario("stale-marker-from-an-unrelated-head-refuses", "refuse")
def _(f: mb.Fixture) -> None:
    h1 = f.start_pr()
    other = f.start_pr({"docs/other-pr.md": "a different PR's head\n"})  # sibling of h1, not its ancestor
    f.set_pull_head(h1)
    h2 = push(f, h1)
    expect(f.run(NEW, event_base=f.base0, event_head=h2, body=marker(other), event=sync(h1)),
           ok=False, needle="routine-changed-head")


@scenario("marker-two-pushes-behind-refuses", "refuse")
def _(f: mb.Fixture) -> None:
    h1 = f.start_pr()
    h2 = push(f, h1, tag="first")
    h3 = push(f, h2, tag="second")
    out = expect(f.run(NEW, event_base=f.base0, event_head=h3, body=marker(h1), event=sync(h2)),
                 ok=False, needle="routine-changed-head")
    assert f"or its push parent {h2}" in out, out


@scenario("push-parent-marker-on-an-edited-run-refuses", "refuse")
def _(f: mb.Fixture) -> None:
    # An edit carries no trusted previous head: a body edit that does not rebind stays red.
    h1 = f.start_pr()
    h2 = push(f, h1)
    expect(f.run(NEW, event_base=f.base0, event_head=h2, body=marker(h1), event=edited()),
           ok=False, needle="routine-changed-head")
    expect(f.run(NEW, event_base=f.base0, event_head=h2, body=marker(h1),
                 event={"EVENT_ACTION": "reopened", "EVENT_BEFORE": h1}), ok=False, needle="routine-changed-head")


# ---------------------------------------------------------------- forced push
@scenario("forced-push-offers-no-parent-and-refuses", "refuse")
def _(f: mb.Fixture) -> None:
    h1 = f.start_pr()
    rewritten = f.start_pr({"docs/pr-change.md": "amended and force-pushed\n"})  # replaces h1, not on top of it
    assert rewritten != h1
    out = expect(f.run(NEW, event_base=f.base0, event_head=rewritten, body=marker(h1), event=sync(h1)),
                 ok=False, needle="routine-changed-head")
    assert f"previous head {h1} is not a proven ancestor" in out, out


# ---------------------------------------------------------------- forged markers and inputs
@scenario("forged-marker-and-before-naming-an-absent-commit-refuse", "refuse")
def _(f: mb.Fixture) -> None:
    h1 = f.start_pr()
    h2 = push(f, h1)
    forged = "f" * 40
    out = expect(f.run(NEW, event_base=f.base0, event_head=h2, body=marker(forged), event=sync(forged)),
                 ok=False, needle="routine-changed-head")
    assert "is not a proven ancestor" in out, out
    expect(f.run(NEW, event_base=f.base0, event_head=h2, body=marker(forged), event=sync(h1)),
           ok=False, needle="routine-changed-head")
    expect(f.run(NEW, event_base=f.base0, event_head=h2, body=marker(h1), event=sync("not-a-sha")),
           ok=False, needle="routine-changed-head")


@scenario("duplicate-or-protected-marker-is-not-rescued-by-the-parent", "refuse")
def _(f: mb.Fixture) -> None:
    h1 = f.start_pr()
    h2 = push(f, h1)
    expect(f.run(NEW, event_base=f.base0, event_head=h2, body=marker(h1) + marker(h2), event=sync(h1)),
           ok=False, needle="exactly one routine-development marker")
    expect(f.run(NEW, event_base=f.base0, event_head=h2, body=marker(h1, "publish"), event=sync(h1)),
           ok=False, needle="routine-protected-operation")
    expect(f.run(NEW, event_base=f.base0, event_head=h2,
                 body=f"<!-- fsgg:routine-development/v1 head={h1} head={h2} operation=source-change -->",
                 event=sync(h1)), ok=False, needle="routine-ambiguous")


def envelope(f: mb.Fixture, *, head: str, body: str, previous: str) -> tuple[int, str]:
    clone = f.base / f"envelope-{len(list(f.base.glob('envelope-*')))}"
    f.git("init", "-q", str(clone), cwd=f.base)
    f.git("-C", str(clone), "fetch", "-q", "--no-tags", f"file://{f.origin}",
          "+refs/heads/main:refs/remotes/origin/routine-base", f"+refs/pull/{mb.PR}/head:refs/remotes/origin/routine-head",
          cwd=f.base)
    trusted = clone / "trusted-base"
    (trusted / "lib").mkdir(parents=True)
    for rel in ("scripts/check-claim-generation.py", "scripts/check-routine-eligibility-envelope.py"):
        (trusted / pathlib.Path(rel).name).write_text(f.git("-C", str(clone), "show", f"{f.base0}:{rel}", cwd=f.base) + "\n")
    (trusted / "lib/gate.py").write_text(f.git("-C", str(clone), "show", f"{f.base0}:scripts/lib/gate.py", cwd=f.base) + "\n")
    (trusted / "body.md").write_text(body)
    done = subprocess.run([sys.executable, str(trusted / "check-routine-eligibility-envelope.py"),
                           "--git-dir", str(clone), "--repository", mb.REPOSITORY, "--default-branch", "main",
                           "--base-ref", "main", "--base-revision", "refs/remotes/origin/routine-base",
                           "--base-sha", f.base0, "--head-ref", "routine/fixture",
                           "--head-revision", "refs/remotes/origin/routine-head", "--head-sha", head,
                           "--body", str(trusted / "body.md"), "--validator", str(trusted / "check-claim-generation.py"),
                           "--previous-head", previous],
                          env=f.env, text=True, capture_output=True, check=False)
    return done.returncode, done.stdout + done.stderr


@scenario("envelope-re-proves-the-parent-and-refuses-a-forged-one", "refuse")
def _(f: mb.Fixture) -> None:
    h1 = f.start_pr()
    sibling = f.start_pr({"docs/pr-change.md": "sibling\n"})  # never reachable from the fetched refs
    h2 = push(f, h1)
    expect(envelope(f, head=h2, body=marker(h1), previous=h1), ok=True, needle="routine delivery provisionally admitted")
    expect(envelope(f, head=h2, body=marker(sibling), previous=sibling), ok=False, needle="cannot compare")
    live = f.move_main({"unrelated.txt": "main moved\n"})  # present in the clone, but not under the head
    expect(envelope(f, head=h2, body=marker(live), previous=live), ok=False, needle="is not an ancestor of head")
    expect(envelope(f, head=h2, body=marker(h2), previous=h2), ok=False, needle="distinct from the head")
    expect(envelope(f, head=h2, body=marker(h1), previous="F" * 40), ok=False, needle="exact lowercase 40-hex")


@scenario("validator-refuses-a-previous-head-outside-the-trusted-route", "refuse")
def _(f: mb.Fixture) -> None:
    h1 = f.start_pr()
    h2 = push(f, h1)
    body = f.base / "body.md"
    body.write_text(marker(h1))
    done = subprocess.run([sys.executable, str(ROOT / "scripts/check-claim-generation.py"), "--repo", mb.REPOSITORY,
                           "--head-ref", "routine/fixture", "--head-sha", h2, "--body", str(body),
                           "--routine-previous-head", h1],
                          cwd=f.src, env=f.env, text=True, capture_output=True, check=False)
    assert done.returncode != 0 and "--routine-previous-head needs --routine-policy-ref" in done.stdout + done.stderr, done


# ---------------------------------------------------------------- the merge boundary
def load_tool():
    spec = importlib.util.spec_from_file_location("routine_delivery_5189", ROOT / "tools/routine-delivery.py")
    module = importlib.util.module_from_spec(spec)
    assert spec and spec.loader
    sys.modules[spec.name] = module  # dataclasses resolve their defining module by name
    spec.loader.exec_module(module)
    return module


class RecordingApi:
    def __init__(self) -> None:
        self.calls: list[tuple[str, str, str]] = []

    def routine_eligibility(self, repo: str, head: str, branch: str) -> None:
        self.calls.append((repo, head, branch))


@scenario("merge-with-an-unrebound-marker-is-refused-by-the-live-body-check", "refuse")
def _(f: mb.Fixture) -> None:
    del f
    tool = load_tool()
    h1, h2 = "1" * 40, "2" * 40
    pr = {"head": {"ref": "routine/fixture"}, "body": "body\n" + marker(h1)}
    api = RecordingApi()
    try:
        tool.routine_contract(api, "FS-GG/.github", pr, h2)
    except RuntimeError as error:
        assert "stale/missing head" in str(error), error
    else:
        raise AssertionError("an unrebound push-parent marker reached the merge")
    # It is refused before the eligibility check is even consulted, so a provisional green cannot admit it.
    assert api.calls == [], api.calls
    pr["body"] = "body\n" + marker(h2)
    assert tool.routine_contract(api, "FS-GG/.github", pr, h2) == "passed"
    assert api.calls == [("FS-GG/.github", h2, "routine/fixture")], api.calls


def main() -> int:
    for run in SCENARIOS:
        run()
    total = len(RESULTS)
    count = {kind: sum(1 for _, k in RESULTS if k == kind) for kind in ("admit", "refuse", "mutation")}
    assert sum(count.values()) == total
    print(f"routine-eligibility push-parent: scenarios={total} passed={total} admit={count['admit']} "
          f"refuse={count['refuse']} mutation-controls={count['mutation']}")
    for name, kind in RESULTS:
        print(f"  ok [{kind}] {name}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
