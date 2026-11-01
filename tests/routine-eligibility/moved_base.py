#!/usr/bin/env python3
"""Causal fixture for the routine-eligibility moved-base race (.github#4730).

Every scenario executes the REAL workflow shell (`run: |` block of the workflow under test) against a
REAL shallow `file://` transport, the REAL base-loaded envelope and the REAL validator/policy copied from
this working tree. Only the GitHub origin URL is redirected (git `url.<file>.insteadOf`). The pre-fix
workflow bytes (fixtures/legacy-routine-eligibility.workflow.txt) are the mutation control: identical
inputs, only the workflow differs.
"""
from __future__ import annotations

import base64
import os
import pathlib
import shutil
import subprocess
import sys
import tempfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
NEW = ROOT / ".github/workflows/routine-eligibility.yml"
LEGACY = ROOT / "tests/routine-eligibility/fixtures/legacy-routine-eligibility.workflow.txt"
REPOSITORY = "FS-GG/private-fixture"
PR = "7"
TOKEN = "synthetic-fetch-only-credential"
COPIED = [
    "scripts/check-claim-generation.py",
    "scripts/lib/gate.py",
    "scripts/check-routine-eligibility-envelope.py",
    ".fsgg/routine-development.json",
]

RESULTS: list[tuple[str, str]] = []


def workflow_shell(path: pathlib.Path) -> str:
    text = path.read_text(encoding="utf-8")
    body = text.split("        run: |\n", 1)[1]
    return "\n".join(line[10:] for line in body.splitlines()) + "\n"


class Fixture:
    """A bare `origin` plus a working repo; mutations are pushed to origin like merges to main."""

    def __init__(self, base: pathlib.Path) -> None:
        self.base = base
        self.src = base / "src"
        self.origin = base / "origin.git"
        self.env = dict(os.environ, HOME=str(base), GIT_CONFIG_GLOBAL="/dev/null", GIT_CONFIG_NOSYSTEM="1",
                        GIT_AUTHOR_NAME="f", GIT_AUTHOR_EMAIL="f@example.invalid",
                        GIT_COMMITTER_NAME="f", GIT_COMMITTER_EMAIL="f@example.invalid")
        self.git("init", "-q", "-b", "main", str(self.src), cwd=base)
        self.git("init", "-q", "--bare", str(self.origin), cwd=base)
        self.git("remote", "add", "origin", f"file://{self.origin}")
        for rel in COPIED:
            self.put(rel, (ROOT / rel).read_text(encoding="utf-8"))
        self.put(".github/workflows/routine-eligibility.yml", NEW.read_text(encoding="utf-8"))
        self.base0 = self.commit("base")
        self.push_main()

    def git(self, *args: str, cwd: pathlib.Path | None = None, check: bool = True) -> str:
        done = subprocess.run(["git", *args], cwd=cwd or self.src, env=self.env, text=True,
                              capture_output=True, check=False)
        if check and done.returncode != 0:
            raise AssertionError(f"git {' '.join(args)} failed: {done.stderr}")
        return done.stdout.strip()

    def put(self, rel: str, text: str) -> None:
        path = self.src / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")

    def commit(self, message: str) -> str:
        self.git("add", "-A")
        self.git("commit", "-q", "-m", message)
        return self.git("rev-parse", "HEAD")

    def push_main(self) -> None:
        self.git("push", "-q", "--force", "origin", "HEAD:refs/heads/main")

    def start_pr(self, edits: dict[str, str] | None = None) -> str:
        """Branch from the current main tip, commit the PR change, publish it as refs/pull/7/head."""
        self.git("checkout", "-q", "-B", "pr", self.git("rev-parse", "origin/main" if False else "main"))
        for rel, text in (edits or {"docs/pr-change.md": "routine change\n"}).items():
            self.put(rel, text)
        head = self.commit("pr")
        self.git("push", "-q", "--force", "origin", f"{head}:refs/pull/{PR}/head")
        self.git("checkout", "-q", "main")
        return head

    def move_main(self, edits: dict[str, str], message: str = "main moved") -> str:
        self.git("checkout", "-q", "main")
        for rel, text in edits.items():
            self.put(rel, text)
        tip = self.commit(message)
        self.push_main()
        return tip

    def move_main_bulk(self, count: int) -> str:
        """Advance main by `count` commits through fast-import (cheap enough for >5000)."""
        self.git("checkout", "-q", "main")
        parent = self.git("rev-parse", "main")
        lines = []
        for index in range(count):
            lines += ["commit refs/heads/bulk", "committer f <f@example.invalid> 1700000000 +0000",
                      "data 1", "b"]
            if index == 0:
                lines += [f"from {parent}"]
            content = f"{index}"
            lines += ["M 100644 inline bulk.txt", f"data {len(content)}", content, ""]
        stream = "\n".join(lines) + "\n"
        done = subprocess.run(["git", "fast-import", "--quiet"], cwd=self.src, env=self.env,
                              input=stream, text=True, capture_output=True, check=False)
        if done.returncode != 0:
            raise AssertionError(done.stderr)
        self.git("checkout", "-q", "-B", "main", "bulk")
        self.push_main()
        return self.git("rev-parse", "main")

    def set_pull_head(self, sha: str) -> None:
        self.git("push", "-q", "--force", "origin", f"{sha}:refs/pull/{PR}/head")

    def run(self, workflow: pathlib.Path, *, event_base: str, event_head: str, body: str,
            head_ref: str = "routine/fixture", event: dict[str, str] | None = None) -> tuple[int, str]:
        runner = self.base / f"runner-{len(list(self.base.glob('runner-*')))}"
        runner.mkdir()
        env = dict(self.env, RUNNER_TEMP=str(runner), RECEIVER_FETCH_TOKEN=TOKEN, PR_BODY=body,
                   PR_NUMBER=PR, BASE_REF="main", DEFAULT_BRANCH="main", BASE_SHA=event_base,
                   HEAD_REF=head_ref, HEAD_SHA=event_head, REPOSITORY=REPOSITORY,
                   GIT_CONFIG_COUNT="1", GIT_CONFIG_KEY_0=f"url.file://{self.origin}.insteadOf",
                   GIT_CONFIG_VALUE_0=f"https://github.com/{REPOSITORY}.git", **(event or {}))
        done = subprocess.run(["bash", "-c", workflow_shell(workflow)], cwd=runner, env=env, text=True,
                              capture_output=True, check=False, timeout=120)
        output = done.stdout + done.stderr
        derived = base64.b64encode(f"x-access-token:{TOKEN}".encode()).decode()
        visible = "\n".join(line for line in output.splitlines() if line != f"::add-mask::{derived}")
        assert TOKEN not in visible and derived not in visible, "credential leaked"
        return done.returncode, output


def marker(head: str, operation: str = "source-change") -> str:
    return f"<!-- fsgg:routine-development/v1 head={head} operation={operation} -->"


def scenario(name: str, kind: str):
    def wrap(fn):
        def run() -> None:
            with tempfile.TemporaryDirectory(prefix="routine-moved-base.") as scratch:
                fn(Fixture(pathlib.Path(scratch)))
            RESULTS.append((name, kind))
        run.__name__ = fn.__name__
        SCENARIOS.append(run)
        return run
    return wrap


SCENARIOS: list = []


def expect(rc_output: tuple[int, str], *, ok: bool, needle: str) -> str:
    rc, output = rc_output
    assert (rc == 0) == ok and needle in output, f"expected ok={ok} with {needle!r}; got rc={rc}:\n{output}"
    return output


# ---------------------------------------------------------------- parity: unmoved base is unchanged
@scenario("unmoved-base-admits-routine-change", "admit")
def _(f: Fixture) -> None:
    head = f.start_pr()
    out = expect(f.run(NEW, event_base=f.base0, event_head=head, body=marker(head)), ok=True, needle="routine delivery admitted")
    assert "changedPaths=1" in out and "base moved" not in out


@scenario("unmoved-base-identical-under-legacy-workflow", "mutation")
def _(f: Fixture) -> None:
    head = f.start_pr()
    expect(f.run(LEGACY, event_base=f.base0, event_head=head, body=marker(head)), ok=True, needle="routine delivery admitted")


# ---------------------------------------------------------------- the race
@scenario("moved-base-legacy-refuses-with-the-reported-message", "mutation")
def _(f: Fixture) -> None:
    head = f.start_pr()
    f.move_main({"unrelated.txt": "main moved\n"})
    expect(f.run(LEGACY, event_base=f.base0, event_head=head, body=marker(head)), ok=False, needle="differs from event base")


@scenario("moved-base-unchanged-routine-change-passes-without-rebase", "admit")
def _(f: Fixture) -> None:
    head = f.start_pr()
    live = f.move_main({"unrelated.txt": "main moved\n"})
    out = expect(f.run(NEW, event_base=f.base0, event_head=head, body=marker(head)), ok=True, needle="routine delivery admitted")
    assert f"base moved {f.base0} -> {live}" in out
    # Positive control for the measurement: the naive two-dot diff against the live tip WOULD have
    # counted main's own file; the merge-base diff counts only the PR's change.
    naive = f.git("diff", "--name-only", f"{live}..{head}").splitlines()
    assert len(naive) == 2, naive
    assert "changedPaths=1" in out, out


@scenario("moved-base-uses-live-base-executables-not-event-base", "admit")
def _(f: Fixture) -> None:
    head = f.start_pr()
    f.move_main({"scripts/check-claim-generation.py": "#!/usr/bin/env python3\nprint('LIVE-BASE-TRUSTED')\n"})
    expect(f.run(NEW, event_base=f.base0, event_head=head, body=marker(head)), ok=True, needle="LIVE-BASE-TRUSTED")


@scenario("moved-base-candidate-cannot-replace-executables", "admit")
def _(f: Fixture) -> None:
    head = f.start_pr({
        "scripts/check-claim-generation.py": "#!/usr/bin/env python3\nprint('CANDIDATE-CONTROLLED')\n",
        "scripts/check-routine-eligibility-envelope.py": "#!/usr/bin/env python3\nprint('CANDIDATE-ENVELOPE')\n",
        "scripts/lib/gate.py": "raise SystemExit('CANDIDATE-GATE')\n",
        ".fsgg/routine-development.json": "{}\n",
    })
    f.move_main({"unrelated.txt": "main moved\n"})
    out = expect(f.run(NEW, event_base=f.base0, event_head=head, body=marker(head)), ok=True, needle="routine delivery admitted")
    assert "CANDIDATE-" not in out


@scenario("moved-base-live-policy-ratchet-refuses-newly-disallowed-operation", "refuse")
def _(f: Fixture) -> None:
    head = f.start_pr()
    policy = (ROOT / ".fsgg/routine-development.json").read_text(encoding="utf-8")
    assert '"source-change",' in policy
    f.move_main({".fsgg/routine-development.json": policy.replace('"source-change",\n    "internal-docs"', '"internal-docs"')})
    expect(f.run(NEW, event_base=f.base0, event_head=head, body=marker(head)), ok=False, needle="routine-protected-operation")


@scenario("moved-base-sensitive-path-verdict-equals-unmoved-verdict", "admit")
def _(f: Fixture) -> None:
    # The policy lists sensitive paths as explicit heavy-process NON-triggers: the verdict must neither
    # tighten nor loosen when the base moves. Same PR, same verdict before and after the move.
    head = f.start_pr({".github/workflows/sensitive.yml": "name: x\n", "scripts/sensitive.py": "x = 1\n"})
    before = f.run(NEW, event_base=f.base0, event_head=head, body=marker(head))
    f.move_main({"unrelated.txt": "main moved\n"})
    after = f.run(NEW, event_base=f.base0, event_head=head, body=marker(head))
    assert before[0] == after[0] == 0 and "changedPaths=2" in before[1] and "changedPaths=2" in after[1], (before, after)


# ---------------------------------------------------------------- eligibility is never weakened
@scenario("moved-base-protected-operation-still-refuses", "refuse")
def _(f: Fixture) -> None:
    head = f.start_pr()
    f.move_main({"unrelated.txt": "main moved\n"})
    expect(f.run(NEW, event_base=f.base0, event_head=head, body=marker(head, "publish")), ok=False, needle="routine-protected-operation")
    expect(f.run(NEW, event_base=f.base0, event_head=head, body=marker(head, "unknown-op")), ok=False, needle="routine-protected-operation")


@scenario("moved-base-missing-or-ambiguous-marker-still-refuses", "refuse")
def _(f: Fixture) -> None:
    head = f.start_pr()
    f.move_main({"unrelated.txt": "main moved\n"})
    expect(f.run(NEW, event_base=f.base0, event_head=head, body="no marker"), ok=False, needle="routine-missing")
    expect(f.run(NEW, event_base=f.base0, event_head=head, body=marker(head) + marker(head)), ok=False, needle="exactly one routine-development marker")


@scenario("moved-base-stale-or-forged-marker-head-still-refuses", "refuse")
def _(f: Fixture) -> None:
    head = f.start_pr()
    f.move_main({"unrelated.txt": "main moved\n"})
    expect(f.run(NEW, event_base=f.base0, event_head=head, body=marker(f.base0)), ok=False, needle="routine-changed-head")
    expect(f.run(NEW, event_base=f.base0, event_head=head, body=marker("f" * 40)), ok=False, needle="routine-changed-head")


@scenario("moved-base-moved-head-is-refused-not-followed", "refuse")
def _(f: Fixture) -> None:
    head = f.start_pr()
    f.move_main({"unrelated.txt": "main moved\n"})
    other = f.start_pr({"docs/pr-change.md": "force-pushed after the event\n"})
    assert other != head
    expect(f.run(NEW, event_base=f.base0, event_head=head, body=marker(head)), ok=False, needle="differs from event head")


@scenario("moved-base-non-routine-branch-is-still-not-applicable", "admit")
def _(f: Fixture) -> None:
    head = f.start_pr()
    f.move_main({"unrelated.txt": "main moved\n"})
    expect(f.run(NEW, event_base=f.base0, event_head=head, body="", head_ref="feature/other"), ok=True, needle="not applicable to non-routine branch")


# ---------------------------------------------------------------- a changed base is re-evaluated, not trusted
@scenario("moved-base-containing-the-head-is-refused-and-runs-no-candidate-code", "refuse")
def _(f: Fixture) -> None:
    head = f.start_pr({
        "scripts/check-claim-generation.py": "#!/usr/bin/env python3\nprint('CANDIDATE-CONTROLLED')\n",
        "scripts/check-routine-eligibility-envelope.py": "#!/usr/bin/env python3\nprint('CANDIDATE-ENVELOPE')\n",
    })
    f.git("checkout", "-q", "-B", "main", head)
    f.push_main()  # the PR landed by another route: live main now equals/contains the head
    out = expect(f.run(NEW, event_base=f.base0, event_head=head, body=marker(head)), ok=False, needle="already contained in live base")
    assert "CANDIDATE-" not in out
    f.move_main({"later.txt": "after the landing\n"})
    out = expect(f.run(NEW, event_base=f.base0, event_head=head, body=marker(head)), ok=False, needle="already contained in live base")
    assert "CANDIDATE-" not in out


@scenario("moved-base-that-is-not-a-descendant-of-the-event-base-refuses", "refuse")
def _(f: Fixture) -> None:
    moved = f.move_main({"unrelated.txt": "main moved\n"})
    head = f.start_pr()  # based on the moved tip, so the event saw `moved` as its base
    f.git("checkout", "-q", "-B", "main", f.base0)
    f.push_main()  # main rewound behind the event base: an older policy must never be accepted
    expect(f.run(NEW, event_base=moved, event_head=head, body=marker(head)), ok=False, needle="is not an ancestor of live base")


@scenario("moved-base-diverged-lineage-refuses-before-running-its-envelope", "refuse")
def _(f: Fixture) -> None:
    head = f.start_pr()
    f.git("checkout", "-q", "--orphan", "rogue")
    f.put("scripts/check-routine-eligibility-envelope.py", "#!/usr/bin/env python3\nprint('ROGUE-ENVELOPE')\n")
    f.commit("unrelated lineage")
    f.git("push", "-q", "--force", "origin", "HEAD:refs/heads/main")
    out = expect(f.run(NEW, event_base=f.base0, event_head=head, body=marker(head)), ok=False, needle="is not an ancestor of live base")
    assert "ROGUE-ENVELOPE" not in out


@scenario("moved-base-far-beyond-first-depth-connects-by-bounded-deepening", "admit")
def _(f: Fixture) -> None:
    head = f.start_pr()
    f.move_main_bulk(300)
    expect(f.run(NEW, event_base=f.base0, event_head=head, body=marker(head)), ok=True, needle="routine delivery admitted")


@scenario("moved-base-beyond-the-deepening-bound-fails-closed", "refuse")
def _(f: Fixture) -> None:
    head = f.start_pr()
    f.move_main_bulk(6000)
    expect(f.run(NEW, event_base=f.base0, event_head=head, body=marker(head)), ok=False, needle="is not an ancestor of live base")


@scenario("moved-base-empty-effective-change-is-refused", "refuse")
def _(f: Fixture) -> None:
    # A head with no change relative to its merge base is routine-empty even when main has moved.
    base = f.base0
    empty = f.git("commit-tree", f.git("rev-parse", f"{base}^{{tree}}"), "-p", base, "-m", "empty")
    f.set_pull_head(empty)
    f.move_main({"unrelated.txt": "main moved\n"})
    expect(f.run(NEW, event_base=base, event_head=empty, body=marker(empty)), ok=False, needle="routine-empty")


# ---------------------------------------------------------------- the installed envelope contract, without the shell
def envelope(f: Fixture, *, event_base: str, event_head: str, body: str) -> tuple[int, str]:
    clone = f.base / f"envelope-{len(list(f.base.glob('envelope-*')))}"
    f.git("init", "-q", str(clone), cwd=f.base)
    f.git("-C", str(clone), "fetch", "-q", "--no-tags", f"file://{f.origin}",
          "+refs/heads/main:refs/remotes/origin/routine-base", f"+refs/pull/{PR}/head:refs/remotes/origin/routine-head", cwd=f.base)
    live = f.git("-C", str(clone), "rev-parse", "refs/remotes/origin/routine-base", cwd=f.base)
    trusted = clone / "trusted-base"
    (trusted / "lib").mkdir(parents=True)
    for rel in ("scripts/check-claim-generation.py", "scripts/check-routine-eligibility-envelope.py"):
        (trusted / pathlib.Path(rel).name).write_text(f.git("-C", str(clone), "show", f"{live}:{rel}", cwd=f.base) + "\n")
    (trusted / "lib/gate.py").write_text(f.git("-C", str(clone), "show", f"{live}:scripts/lib/gate.py", cwd=f.base) + "\n")
    (trusted / "body.md").write_text(body)
    done = subprocess.run([sys.executable, str(trusted / "check-routine-eligibility-envelope.py"),
                           "--git-dir", str(clone), "--repository", REPOSITORY, "--default-branch", "main", "--base-ref", "main",
                           "--base-revision", "refs/remotes/origin/routine-base", "--base-sha", event_base,
                           "--head-ref", "routine/fixture", "--head-revision", "refs/remotes/origin/routine-head",
                           "--head-sha", event_head, "--body", str(trusted / "body.md"),
                           "--validator", str(trusted / "check-claim-generation.py")],
                          env=f.env, text=True, capture_output=True, check=False)
    return done.returncode, done.stdout + done.stderr


@scenario("envelope-alone-admits-a-moved-base-and-measures-from-the-merge-base", "admit")
def _(f: Fixture) -> None:
    head = f.start_pr()
    f.move_main({"unrelated.txt": "main moved\n"})
    out = expect(envelope(f, event_base=f.base0, event_head=head, body=marker(head)), ok=True, needle="routine delivery admitted")
    assert "changedPaths=1" in out and f"merge base {f.base0}" in out


@scenario("envelope-alone-refuses-a-rewound-or-unrelated-event-base", "refuse")
def _(f: Fixture) -> None:
    head = f.start_pr()
    f.move_main({"unrelated.txt": "main moved\n"})
    expect(envelope(f, event_base=head, event_head=head, body=marker(head)), ok=False, needle="is not an ancestor of live base")
    expect(envelope(f, event_base="e" * 40, event_head=head, body=marker(head)), ok=False, needle="cannot compare")


@scenario("envelope-alone-refuses-a-live-base-that-contains-the-head", "refuse")
def _(f: Fixture) -> None:
    head = f.start_pr()
    f.git("checkout", "-q", "-B", "main", head)
    f.push_main()
    expect(envelope(f, event_base=f.base0, event_head=head, body=marker(head)), ok=False, needle="already contained in live base")


@scenario("envelope-alone-refuses-an-ambiguous-criss-cross-merge-base", "refuse")
def _(f: Fixture) -> None:
    f.start_pr({"a.txt": "a\n"})
    a = f.git("rev-parse", "pr")
    f.git("checkout", "-q", "main")
    b = f.move_main({"b.txt": "b\n"})
    tree = f.git("rev-parse", f"{b}^{{tree}}")
    merged_pr = f.git("commit-tree", tree, "-p", a, "-p", b, "-m", "pr merges main")
    merged_main = f.git("commit-tree", tree, "-p", b, "-p", a, "-m", "main merges pr lineage")
    f.set_pull_head(merged_pr)
    f.git("push", "-q", "--force", "origin", f"{merged_main}:refs/heads/main")
    out = expect(envelope(f, event_base=b, event_head=merged_pr, body=marker(merged_pr)), ok=False, needle="no single merge base")
    assert "(2 found)" in out
    out = expect(f.run(NEW, event_base=b, event_head=merged_pr, body=marker(merged_pr)), ok=False, needle="no single merge base")


def main() -> int:
    for run in SCENARIOS:
        run()
    total = len(RESULTS)
    count = {kind: sum(1 for _, k in RESULTS if k == kind) for kind in ("admit", "refuse", "mutation")}
    assert sum(count.values()) == total
    print(f"routine-eligibility moved-base: scenarios={total} passed={total} admit={count['admit']} "
          f"refuse={count['refuse']} mutation-controls={count['mutation']}")
    for name, kind in RESULTS:
        print(f"  ok [{kind}] {name}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
