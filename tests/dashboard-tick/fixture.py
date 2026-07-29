#!/usr/bin/env python3
"""The failure legs of scripts/dashboard-tick.py (.github#1923 AC3/AC4, epic #266).

A GATE THAT CANNOT FAIL ON ITS SUBJECT IS NOT A GATE. dashboard-tick is a WRITER, and the specific
way writers in this area go wrong is by reporting success over nothing at all — a release that tells
zero receivers, a `PATCH` that 403s, a `PATCH` that returns 200 and changes nothing. Each of those is
driven here, against a fake GitHub API on localhost, and each must go RED.

It also pins the two PRECISION rules, because the blast radius of getting them wrong is somebody
else's dependency: the `-all-` boxes are never ticked, and `fs.gg.coord` must never tick an
`fs.gg.coord.cli` hold.

The subject is the REAL script driven as a subprocess, and the REAL registry/repos.yml — not a
re-typed copy of either. A copy would keep passing after somebody edits the thing it copies, which is
the fails-open shape this fixture exists to close.
"""

from __future__ import annotations

import http.server
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import threading
import urllib.parse

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "dashboard-tick.py"
RECEIVERS = [
    "FS-GG/FS.GG.SDD",
    "FS-GG/FS.GG.Rendering",
    "FS-GG/FS.GG.Governance",
    "FS-GG/FS.GG.Templates",
    "FS-GG/FS.GG.Game",
    "FS-GG/FS.GG.Audio",
    "FS-GG/FS.GG.Net",
]

FAILURES: list[str] = []


# ── dashboard bodies, shaped after the real ones read on 2026-07-29 ──────────────────────────────

HELD = """\
## Rate-Limited

 - [ ] <!-- unlimit-branch=renovate/fs.gg.coord.cli-0.x -->chore(deps): update dependency fs.gg.coord.cli to v0.15.0
 - [ ] <!-- unlimit-branch=renovate/fs.gg.kit-0.x -->chore(deps): update dependency fs.gg.kit to 0.21.0
 - [ ] <!-- create-all-rate-limited-prs -->Create all rate-limited PRs at once

## Open

 - [ ] <!-- rebase-all-open-prs -->Click on this checkbox to rebase all open PRs at once

---
 - [ ] <!-- manual job -->Check this box to trigger a request for Renovate to run again
"""

CURRENT = """\
## Detected Dependencies

 - `FS.GG.Kit 0.21.0`

---
 - [ ] <!-- manual job -->Check this box to trigger a request for Renovate to run again
"""

STALE = """\
## Detected Dependencies

 - `FS.GG.Kit 0.19.1`

---
 - [ ] <!-- manual job -->Check this box to trigger a request for Renovate to run again
"""

# Everything unchecked here is either an `-all-` box or SOMEBODY ELSE's dependency at the very
# version being published. FS.GG.Kit 0.21.0 appears nowhere.
DECOYS = """\
## Rate-Limited

 - [ ] <!-- unlimit-branch=renovate/other.package-0.x -->chore(deps): update dependency other.package to 0.21.0
 - [ ] <!-- create-all-rate-limited-prs -->Create all rate-limited PRs at once
 - [ ] <!-- approve-all-pending-prs -->Approve all pending PRs

## Open

 - [ ] <!-- rebase-all-open-prs -->Rebase all open PRs

---
 - [ ] <!-- manual job -->Check this box to trigger a request for Renovate to run again
"""


class Repo:
    """One fake receiver. Every knob here is a way the real GitHub can answer."""

    def __init__(
        self,
        body: str = CURRENT,
        *,
        dashboard: bool = True,
        list_status: int = 200,
        patch_status: int = 200,
        patch_noop: bool = False,
    ) -> None:
        self.body = body
        self.dashboard = dashboard
        self.list_status = list_status
        self.patch_status = patch_status
        self.patch_noop = patch_noop


class Fake(http.server.BaseHTTPRequestHandler):
    state: dict[str, Repo] = {}
    log: list[tuple[str, str]] = []

    def log_message(self, *_args) -> None:  # noqa: D401 - silence the default access log
        return

    def _repo(self, path: str):
        match = re.match(r"^/repos/([^/]+/[^/]+)/issues", path)
        if not match:
            return None, None
        return match.group(1), self.state.get(match.group(1))

    def _send(self, status: int, payload) -> None:
        raw = json.dumps(payload).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(raw)))
        self.end_headers()
        self.wfile.write(raw)

    def do_GET(self) -> None:  # noqa: N802 - BaseHTTPRequestHandler's contract
        path = urllib.parse.urlparse(self.path).path
        type(self).log.append(("GET", path))
        name, repo = self._repo(self.path)
        if repo is None:
            self._send(404, {"message": f"no such repo {name}"})
            return
        if path.endswith("/issues"):
            if repo.list_status != 200:
                self._send(repo.list_status, {"message": "refused"})
                return
            issues = []
            if repo.dashboard:
                issues.append({"number": 7, "title": "Dependency Dashboard"})
            # A PULL REQUEST with the dashboard's exact title. The writer must skip it; PATCHing a
            # PR body instead of the dashboard would report success and tell nobody.
            issues.append({"number": 8, "title": "Dependency Dashboard", "pull_request": {}})
            self._send(200, issues)
            return
        self._send(200, {"number": 7, "body": repo.body})

    def do_PATCH(self) -> None:  # noqa: N802 - BaseHTTPRequestHandler's contract
        path = urllib.parse.urlparse(self.path).path
        type(self).log.append(("PATCH", path))
        _name, repo = self._repo(self.path)
        if repo is None:
            self._send(404, {"message": "no such repo"})
            return
        if repo.patch_status != 200:
            self._send(repo.patch_status, {"message": "refused"})
            return
        length = int(self.headers.get("Content-Length") or 0)
        sent = json.loads(self.rfile.read(length).decode("utf-8")) if length else {}
        if not repo.patch_noop:
            repo.body = str(sent.get("body") or repo.body)
        self._send(200, {"number": 7, "body": repo.body})


def serve(state: dict[str, Repo]):
    Fake.state = state
    Fake.log = []
    server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Fake)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    return server


def run(args: list[str], *, port: int | None, token: str = "t0ken") -> tuple[int, str]:
    env = dict(os.environ)
    env["DASHBOARD_TICK_TOKEN"] = token
    if port is not None:
        env["GITHUB_API_URL"] = f"http://127.0.0.1:{port}"
    done = subprocess.run(
        [sys.executable, str(SCRIPT), *args], capture_output=True, text=True, env=env, cwd=str(ROOT)
    )
    return done.returncode, done.stdout + done.stderr


def check(label: str, condition: bool, detail: str = "") -> None:
    if condition:
        print(f"  ok   {label}")
        return
    FAILURES.append(f"{label}{': ' + detail if detail else ''}")
    print(f"  FAIL {label}{': ' + detail if detail else ''}")


def leg(title: str) -> None:
    print(f"\n{title}")


def all_repos(**kwargs) -> dict[str, Repo]:
    return {name: Repo(**kwargs) for name in RECEIVERS}


def kit(*args: str) -> list[str]:
    return ["--package", "FS.GG.Kit", "--version", "0.21.0", *args]


# ── the legs ────────────────────────────────────────────────────────────────────────────────────


def leg_named_hold_is_released() -> None:
    leg("A named rate-limit hold is released, and NOTHING else is touched")
    state = all_repos()
    state["FS-GG/FS.GG.Net"] = Repo(HELD)
    server = serve(state)
    rc, out = run(kit(), port=server.server_port)
    body = state["FS-GG/FS.GG.Net"].body
    check("exit 0", rc == 0, f"exit {rc}\n{out}")
    check("the kit's own hold is ticked", "- [x] <!-- unlimit-branch=renovate/fs.gg.kit-0.x -->" in body)
    check(
        "another dependency's hold is NOT ticked",
        "- [ ] <!-- unlimit-branch=renovate/fs.gg.coord.cli-0.x -->" in body,
    )
    check("`create-all-rate-limited-prs` is NOT ticked", "- [ ] <!-- create-all-rate-limited-prs -->" in body)
    check("`rebase-all-open-prs` is NOT ticked", "- [ ] <!-- rebase-all-open-prs -->" in body)
    check("`manual job` is NOT ticked when a precise box existed", "- [ ] <!-- manual job -->" in body)
    check(
        "exactly one receiver was written to",
        sum(1 for method, _ in Fake.log if method == "PATCH") == 1,
        str(Fake.log),
    )
    server.shutdown()


def leg_unextracted_receiver_gets_manual_job() -> None:
    leg("A receiver that has not re-extracted since publication gets `manual job`")
    state = all_repos()
    state["FS-GG/FS.GG.Governance"] = Repo(STALE)
    server = serve(state)
    rc, out = run(kit(), port=server.server_port)
    body = state["FS-GG/FS.GG.Governance"].body
    check("exit 0", rc == 0, f"exit {rc}\n{out}")
    check("`manual job` is ticked", "- [x] <!-- manual job -->" in body)
    server.shutdown()


def leg_nothing_held_is_not_a_write() -> None:
    leg("A release nobody is holding writes NOTHING, and is still a success")
    server = serve(all_repos())
    rc, out = run(kit(), port=server.server_port)
    check("exit 0", rc == 0, f"exit {rc}\n{out}")
    check("no PATCH was made at all", not any(m == "PATCH" for m, _ in Fake.log), str(Fake.log))
    server.shutdown()


def leg_zero_receivers_is_red() -> None:
    leg("A release that reaches ZERO receivers is RED (#1923 AC4)")
    server = serve(all_repos(dashboard=False))
    rc, out = run(kit(), port=server.server_port)
    check("exit 1", rc == 1, f"exit {rc}\n{out}")
    check("and it says so in those words", "reached ZERO receivers" in out, out)
    server.shutdown()


def leg_unreadable_receiver_is_red() -> None:
    leg("A receiver the App cannot read at all is RED, and is named")
    state = all_repos()
    state["FS-GG/FS.GG.Net"] = Repo(HELD, list_status=403)
    server = serve(state)
    rc, out = run(kit(), port=server.server_port)
    check("exit 1", rc == 1, f"exit {rc}\n{out}")
    check("the receiver is named", "FS-GG/FS.GG.Net" in out, out)
    server.shutdown()


def leg_refused_write_is_red() -> None:
    leg("A refused write (403) is RED — the release did not tell that receiver")
    state = all_repos()
    state["FS-GG/FS.GG.Net"] = Repo(HELD, patch_status=403)
    server = serve(state)
    rc, out = run(kit(), port=server.server_port)
    check("exit 1", rc == 1, f"exit {rc}\n{out}")
    check("403 is named", "403" in out, out)
    server.shutdown()


def leg_silent_noop_write_is_red() -> None:
    leg("A PATCH that returns 200 and CHANGES NOTHING is RED — 200 is not evidence")
    state = all_repos()
    state["FS-GG/FS.GG.Net"] = Repo(HELD, patch_noop=True)
    server = serve(state)
    rc, out = run(kit(), port=server.server_port)
    check("exit 1", rc == 1, f"exit {rc}\n{out}")
    check("the read-back is what caught it", "BYTE-IDENTICAL" in out, out)
    server.shutdown()


def leg_missing_dashboard_is_undetermined() -> None:
    leg("A receiver with no Dependency Dashboard is UNDETERMINED, never ok (#1923 AC6)")
    state = all_repos()
    state["FS-GG/FS.GG.Audio"] = Repo(CURRENT, dashboard=False)
    server = serve(state)
    rc, out = run(kit(), port=server.server_port)
    check("exit 2 — a no verdict, not a green", rc == 2, f"exit {rc}\n{out}")
    check("the receiver is named", "FS-GG/FS.GG.Audio" in out, out)
    server.shutdown()


def leg_decoys_are_never_ticked() -> None:
    leg("`-all-` boxes and other dependencies' holds are NEVER ticked (#1923 AC3)")
    state = all_repos()
    state["FS-GG/FS.GG.Game"] = Repo(DECOYS)
    server = serve(state)
    rc, out = run(kit(), port=server.server_port)
    body = state["FS-GG/FS.GG.Game"].body
    check("exit 0", rc == 0, f"exit {rc}\n{out}")
    check("`manual job` is what got ticked", "- [x] <!-- manual job -->" in body)
    for marker in (
        "unlimit-branch=renovate/other.package-0.x",
        "create-all-rate-limited-prs",
        "approve-all-pending-prs",
        "rebase-all-open-prs",
    ):
        check(f"`{marker}` is untouched", f"- [ ] <!-- {marker} -->" in body, body)
    server.shutdown()


def leg_word_boundaries() -> None:
    leg("`fs.gg.coord` must not tick an `fs.gg.coord.cli` hold, and 0.2.1 is not 0.2.10")
    state = all_repos()
    state["FS-GG/FS.GG.Net"] = Repo(HELD)
    server = serve(state)
    rc, out = run(["--package", "FS.GG.Coord", "--version", "0.15.0"], port=server.server_port)
    body = state["FS-GG/FS.GG.Net"].body
    check("exit 0", rc == 0, f"exit {rc}\n{out}")
    check(
        "the .Cli hold is NOT ticked",
        "- [ ] <!-- unlimit-branch=renovate/fs.gg.coord.cli-0.x -->" in body,
        body,
    )
    server.shutdown()

    state = all_repos()
    state["FS-GG/FS.GG.Net"] = Repo(
        " - [ ] <!-- unlimit-branch=renovate/fs.gg.kit-0.x -->update dependency fs.gg.kit to 0.2.10\n"
        " - [ ] <!-- manual job -->run again\n"
    )
    server = serve(state)
    rc, out = run(["--package", "FS.GG.Kit", "--version", "0.2.1"], port=server.server_port)
    body = state["FS-GG/FS.GG.Net"].body
    check("0.2.1 did not match 0.2.10", "- [ ] <!-- unlimit-branch=renovate/fs.gg.kit-0.x -->" in body, body)
    server.shutdown()


def leg_pull_request_is_not_the_dashboard() -> None:
    leg("A PULL REQUEST titled `Dependency Dashboard` is never mistaken for it")
    state = all_repos()
    state["FS-GG/FS.GG.Net"] = Repo(HELD)
    server = serve(state)
    rc, _out = run(kit(), port=server.server_port)
    check("the write went to issue 7, not 8", ("PATCH", "/repos/FS-GG/FS.GG.Net/issues/7") in Fake.log, str(Fake.log))
    check("exit 0", rc == 0)
    server.shutdown()


def leg_probe_is_read_only() -> None:
    leg("`--probe` asserts reach and writes NOTHING (#1923 AC1)")
    state = all_repos(body=HELD)
    server = serve(state)
    rc, out = run(["--probe"], port=server.server_port)
    check("exit 0 when every dashboard is readable", rc == 0, f"exit {rc}\n{out}")
    check("no PATCH was made", not any(m == "PATCH" for m, _ in Fake.log), str(Fake.log))
    server.shutdown()

    state = all_repos()
    state["FS-GG/FS.GG.Net"] = Repo(CURRENT, list_status=404)
    server = serve(state)
    rc, out = run(["--probe"], port=server.server_port)
    check("exit 1 when the App cannot reach a receiver", rc == 1, f"exit {rc}\n{out}")
    check("AC1 is named as unsatisfied", "AC1 is NOT satisfied" in out, out)
    server.shutdown()


def leg_permanent_no_verdicts() -> None:
    leg("An unreadable roster and an empty credential are PERMANENT no-verdicts, before any request")
    server = serve(all_repos())
    rc, out = run(kit("--registry", "/nonexistent/repos.yml"), port=server.server_port)
    check("exit 3 on a roster that will not read", rc == 3, f"exit {rc}\n{out}")
    check("nothing was requested", not Fake.log, str(Fake.log))
    server.shutdown()

    server = serve(all_repos())
    rc, out = run(kit(), port=server.server_port, token="")
    check("exit 3 on an empty credential", rc == 3, f"exit {rc}\n{out}")
    check("nothing was requested", not Fake.log, str(Fake.log))
    check("the secret is named", "DASHBOARD_TICK_TOKEN" in out, out)
    server.shutdown()


def leg_the_roster_is_the_real_one() -> None:
    leg("The subject set is registry/repos.yml, not this fixture's opinion of it")
    done = subprocess.run(
        [
            "bash",
            str(ROOT / "scripts" / "repos.sh"),
            "list",
            "--receives",
            "coordination-kit",
            "--kit-delivery",
            "package",
        ],
        capture_output=True,
        text=True,
    )
    rows = [line.strip() for line in done.stdout.splitlines() if line.strip()]
    check(
        "the fixture's receiver list is the roster's",
        rows == RECEIVERS,
        f"roster={rows}\nfixture={RECEIVERS}",
    )


# ── structural legs: a truth table proves the script is right; it cannot prove it still RUNS ─────


def leg_the_wiring_is_still_there() -> None:
    leg("The wiring that makes any of this fire cannot be quietly removed")
    selftest = (ROOT / ".github/workflows/dashboard-tick-selftest.yml").read_text(encoding="utf-8")
    check("the selftest runs this fixture", "tests/dashboard-tick/run.sh" in selftest)
    check("the selftest probes the credential", "scripts/dashboard-tick.py --probe" in selftest)
    check("the credential job asks for issues:write", "permission-issues: write" in selftest)
    # A YAML KEY, not the word — the comment above that step explains why there is no such key, and
    # a substring test would fail on its own rationale.
    check(
        "the credential job hand-lists no receivers",
        re.search(r"^\s+repositories:", selftest, re.M) is None,
        "a `repositories:` block is a second, drifting copy of the roster (#1923 AC2)",
    )

    for name, package in (
        ("release-kit.yml", "FS.GG.Kit"),
        ("release-coord-engine.yml", "FS.GG.Coord.Cli"),
    ):
        workflow = (ROOT / ".github/workflows" / name).read_text(encoding="utf-8")
        check(f"{name} calls the writer for {package}", f"--package {package}" in workflow)
        check(f"{name} gates the writer on a real publish", "steps.v.outputs.push == 'true'" in workflow)
        check(f"{name} mints issues:write for it", "permission-issues: write" in workflow)
        check(
            f"{name} does not swallow the writer's failure",
            re.search(r"^\s*continue-on-error:", workflow, re.M) is None,
            "a `continue-on-error` here turns a release that told nobody green (#1923 AC4)",
        )
        # A `case` arm that let rc=2 pass would make "I could not tell them" wear the same green as
        # "I told them" — the exact #266 shape, and invisible in a diff without this assertion.
        check(f"{name} treats a no-verdict (2) as red", re.search(r"^\s*2\)[^\n]*exit 1", workflow, re.M) is not None)


def main() -> int:
    leg_named_hold_is_released()
    leg_unextracted_receiver_gets_manual_job()
    leg_nothing_held_is_not_a_write()
    leg_zero_receivers_is_red()
    leg_unreadable_receiver_is_red()
    leg_refused_write_is_red()
    leg_silent_noop_write_is_red()
    leg_missing_dashboard_is_undetermined()
    leg_decoys_are_never_ticked()
    leg_word_boundaries()
    leg_pull_request_is_not_the_dashboard()
    leg_probe_is_read_only()
    leg_permanent_no_verdicts()
    leg_the_roster_is_the_real_one()
    leg_the_wiring_is_still_there()

    print()
    if FAILURES:
        for failure in FAILURES:
            print(f"::error::dashboard-tick fixture: {failure}")
        print(f"\n{len(FAILURES)} assertion(s) failed.")
        return 1
    print("dashboard-tick: every failure leg fails, and every precision rule holds.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
