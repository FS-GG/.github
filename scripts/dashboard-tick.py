#!/usr/bin/env python3
"""The PUSH half of the kit/engine delivery fabric (.github#1923, from #1776).

WHAT THIS IS. After the hub publishes FS.GG.Kit or FS.GG.Coord.Cli, a receiver learns about it only
when Renovate next runs AND is not holding the branch back. Two hold states are live and both need a
HUMAN TO TICK A CHECKBOX IN ANOTHER REPO, which nobody watches. On 2026-07-29, four versions after
FS.GG.Kit 0.18.0, FS.GG.Rendering and FS.GG.Net were both sitting behind an unticked box; on
2026-07-28 `scripts/repos-audit.sh` records the same thing being repaired by hand.

WHY IT IS NOT `repository_dispatch`, WHICH IS THE OBVIOUS ANSWER AND THE WRONG ONE. That was measured
and refused in .github#1776 — the record is release-kit.yml's header. Two independent reasons: no
receiver has an `on: repository_dispatch` listener for a kit or engine release, so a dispatch would
take seven 204s and change nothing; and Renovate is the hosted Mend App, which a dispatch cannot make
re-scan. The mechanism that DOES work needs no receiver-side workflow at all: Renovate's own
re-trigger surface is the Dependency Dashboard ISSUE BODY, and ticking one of its boxes is a
`PATCH /repos/{owner}/{repo}/issues/{n}`. The hub already performs the READ half of exactly this, in
scripts/check-pin-coherence.py under pin-coherence.yml's `issues: read`.

WHICH BOX, AND WHY ONLY TWO OF THEM (#1923 AC3). The dashboard carries several markers. This writer
ticks exactly two:

  <!-- unlimit-branch=<branch> -->   releases ONE rate-limited branch, named.
  <!-- manual job -->               forces a re-extraction of the whole repo.

and never `<!-- create-all-rate-limited-prs -->`, `<!-- rebase-all-open-prs -->`,
`<!-- approve-all-pending-prs -->`, `<!-- rebase-branch=... -->` or any box belonging to another
dependency. The three `-all-` boxes would make the hub's release responsible for every unrelated
dependency in a receiver, which is not what a release is entitled to say; `rebase-branch` acts on a
PR that ALREADY EXISTS, which means the receiver was already told and there is nothing to push.

WHICH REMEDY FOR THE RATE-LIMITED CASE — #1923 asked this to be settled before building, because a
one-line `prHourlyLimit` / `branchConcurrentLimit` change in default.json would also have removed
these two particular holds. Settled: the tick, and NOT the preset, and they are not alternatives.

  • For `manual job` the preset cannot help at all. A re-extraction the bot has not done is not a
    budget problem; no limit makes it look again sooner.
  • For `unlimit-branch` the preset is a WORSE INSTRUMENT for the same outcome. Raising a limit
    enlarges the bot's autonomous budget for EVERY dependency in EVERY repo, so whether this
    release's branch escapes the hold is a race against every other dependency that hour — it makes
    the hold less likely, never absent. Ticking removes the hold for THE branch THIS release
    created, immediately, and says which one it removed. Ticking a hold and raising the ceiling on
    holds are different acts, exactly as #1776 said.
  • The preset question is therefore adjacent and still open, and it is NOT this file's to answer:
    default.json is #1828's and #1626's subject and is deliberately untouched here.

WHAT IT CANNOT SEE, SAID OUT LOUD (#1923 AC6, epic #266).

  • A tick that lands and a Renovate run that then does nothing are DIFFERENT FAILURES. This writer
    proves the first — the box is ticked, read back from GitHub's own post-write state. It has no
    way to observe the second. #1768's bump-offer sweep in scripts/repos-audit.sh is the observer
    for that, and it runs on its own schedule.
  • A receiver whose dashboard is MISSING or RENAMED is `undetermined`, never `ok`, and the run
    carries a no-verdict exit for it.
  • The GitHub issues API has no `If-Match` on `PATCH`, so the body written is derived from the body
    read a moment earlier. If Renovate edits the dashboard inside that window this write clobbers
    that edit. The window is one HTTP round trip and the loss is one dashboard render, which
    Renovate rewrites on its next run; there is no API affordance that closes it.

EXIT CODES — the contract, deliberately shaped like repos-audit.sh's so a caller classifies by code
and never by prose (#335).

  0  every receiver was reached and graded, and nothing was refused.
  1  a FINDING. Either ZERO receivers were reached — a release that tells nobody, which is #1923
     AC4's headline leg and the failure this whole area keeps producing — or a write was REFUSED
     (403/404), or a `PATCH` returned 200 and CHANGED NOTHING. A 200 that changes nothing is
     indistinguishable from success unless somebody reads it back, so this reads it back.
  2  NO VERDICT, retryable. At least one receiver was reached, but at least one could not be graded:
     no Dependency Dashboard, or a read that failed. "I could not check" never wears the same green
     as "I checked".
  3  NO VERDICT, permanent. The roster will not parse, names no receiver, or the invocation is
     wrong. A second identical pass returns an identical answer, so it is never retried.

USAGE

  dashboard-tick.py --package FS.GG.Kit --version 0.21.0 [--registry FILE] [--dry-run]
  dashboard-tick.py --probe [--registry FILE]

`--probe` is the READ-ONLY credential assertion dashboard-tick-selftest.yml runs (#1923 AC1): can
this token reach every receiver's dashboard? It writes nothing, on purpose — a no-op `PATCH` would
prove the write scope harder and would also edit seven live issues on every scheduled run.

The receiver set is DERIVED, never a hand list (#1507/#1510/#1515/#1528/#1538, and #1759's
wrong-field trap): `repos.sh list --receives coordination-kit --kit-delivery package`.

Credential in `DASHBOARD_TICK_TOKEN`; API base overridable with `GITHUB_API_URL` (the fixture points
it at a localhost fake, which is how the failure legs are provable without a network).
"""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import urllib.error
import urllib.request

API = os.environ.get("GITHUB_API_URL", "https://api.github.com").rstrip("/")
DASHBOARD_TITLE = "Dependency Dashboard"
ROSTER_SCRIPT = "scripts/repos.sh"
ROSTER_QUERY = ["list", "--receives", "coordination-kit", "--kit-delivery", "package"]

# check-paths-coherence.py rule (c) (#996): this script's subject, COMPOSED from the constant it
# actually shells out to rather than retyped beside it. Any workflow whose `paths:` names this file
# must also select everything here, so a change to the roster query re-runs the gate that covers it.
PATHS_SUBJECT = (ROSTER_SCRIPT,)

# Renovate's dashboard is authored by the App, and the AUTHOR is half the identity of the issue this
# writer PATCHes — see find_dashboard.
RENOVATE_LOGIN = "renovate[bot]"

# An HTTP status that means "this credential may not do that here". A FINDING about the receiver,
# never a transport failure, and 401 belongs with 403/404: an expired token is a credential fact, and
# routing it to the retryable arm would hand the operator seven identical no-verdicts instead of one
# sentence naming the credential.
REFUSED_STATUSES = (401, 403, 404)

# The boxes this writer must NEVER tick, listed explicitly rather than left implied by the two narrow
# allow-checks in `choose` (`startswith("unlimit-branch=")` and `== "manual job"`). Those two are the
# allow list, and they are expressed where they are used so there is no third place to drift; this
# list exists on top of them because the blast radius of a wrong tick is every dependency in the
# receiver, and a deny list also refuses a marker that is BOTH — a hypothetical
# `unlimit-branch=...-all-...` — instead of admitting it on a prefix.
DENIED_MARKERS = (
    "create-all-rate-limited-prs",
    "rebase-all-open-prs",
    "approve-all-pending-prs",
    "rebase-branch=",
    "approve-branch=",
    "retry-branch=",
)

# The open-issue list is walked oldest-first (the dashboard is created once, then edited forever), so
# the cap is only reached on a repo with hundreds of open issues. Reaching it is "I could not finish
# looking", which is NOT the same fact as "it is not there" — see find_dashboard.
PAGE_CAP = 10

UNCHECKED = re.compile(r"^(?P<indent>\s*)-\s\[ \]\s*(?P<rest>.*)$")
MARKER = re.compile(r"<!--\s*(?P<marker>[^>]*?)\s*-->")


class Permanent(Exception):
    """Exit 3 — a second identical pass returns an identical answer."""


class Retryable(Exception):
    """Exit 2 — the read failed, which is not a verdict about the receiver."""


def token() -> str:
    value = os.environ.get("DASHBOARD_TICK_TOKEN", "").strip()
    if not value:
        raise Permanent(
            "DASHBOARD_TICK_TOKEN is empty. `required: true` on a forwarded secret means PROVIDED, "
            "not NON-EMPTY (.github#482/#468), so an empty credential must be refused HERE rather "
            "than become seven identical 401s that name neither the App nor the secret."
        )
    return value


def api(path: str, *, method: str = "GET", payload: dict | None = None) -> tuple[int, object]:
    """One request. Returns (status, decoded-body). HTTP errors are RETURNED, never raised.

    A 403/404 on a receiver is a FINDING about that receiver, not a failure of this process, and the
    caller must be able to name which receiver refused. Only a transport failure is exceptional.
    """
    data = json.dumps(payload).encode("utf-8") if payload is not None else None
    request = urllib.request.Request(
        f"{API}{path}",
        data=data,
        method=method,
        headers={
            "Authorization": f"Bearer {token()}",
            "Accept": "application/vnd.github+json",
            "Content-Type": "application/json",
            "User-Agent": "fsgg-dashboard-tick",
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            raw = response.read().decode("utf-8")
            return response.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as error:
        raw = error.read().decode("utf-8", "replace")
        try:
            body = json.loads(raw) if raw else None
        except ValueError:
            body = raw
        return error.code, body
    except (urllib.error.URLError, OSError, ValueError) as error:
        raise Retryable(f"{method} {path} could not be made at all ({error})") from error


def receivers(registry: str | None) -> list[str]:
    """The roster query, run as a subprocess. One derivation, not a second copy of the predicate."""
    script = Path(__file__).resolve().parents[1] / ROSTER_SCRIPT
    command = ["bash", str(script), *ROSTER_QUERY]
    if registry:
        command += ["--registry", registry]
    try:
        completed = subprocess.run(command, capture_output=True, text=True, timeout=120)
    except (OSError, subprocess.SubprocessError) as error:
        raise Permanent(f"could not run the roster query {command!r}: {error}") from error
    if completed.returncode != 0:
        raise Permanent(
            f"the roster query exited {completed.returncode}, so the subject set is unknown and "
            f"NOTHING may be graded from it: {completed.stderr.strip() or '(no stderr)'}"
        )
    rows = [line.strip() for line in completed.stdout.splitlines() if line.strip()]
    if not rows:
        raise Permanent(
            "the roster names no `receives: coordination-kit` + `kit-delivery: package` receiver. "
            "An empty subject set would let this report success over nothing at all (epic #266)."
        )
    return rows


def find_dashboard(repo: str) -> tuple[int | None, str, str]:
    """Renovate's Dependency Dashboard issue: (number, kind, why).

    `kind` is a TOKEN, not prose — `ok`, `refused`, `absent`, `unreadable` — because the caller acts
    differently on each and #335 is emphatic that a reason string is a diagnostic, never an
    interface. Classifying by grepping this function's own sentences would let a reword silently
    downgrade a 403 from a finding to a shrug.

    IDENTITY IS TITLE **AND** AUTHOR, and the author half is what makes this safe to WRITE through.
    `scripts/check-pin-coherence.py` — the READ half of this same mechanism — restricts to
    `author:app/renovate` before it title-matches, and a writer may not be less careful than the
    reader it was modelled on. Any issue somebody titles `Dependency Dashboard` would otherwise
    receive a cross-repo `PATCH` that rewrites its body.

    Deliberately the issues LIST, not `/search/issues`: search is eventually consistent, rate-limited
    on a separate and much smaller budget, and would make a release's fate depend on an index.
    OLDEST FIRST, because the dashboard is created once and then edited forever — it is among the
    first issues a repo ever got, and the API's default `direction=desc` would put it last.
    """
    walked = 0
    for page in range(1, PAGE_CAP + 1):
        status, body = api(
            f"/repos/{repo}/issues?state=open&sort=created&direction=asc&per_page=100&page={page}"
        )
        if status in REFUSED_STATUSES:
            return None, "refused", f"the issues list returned {status} — the App cannot read {repo}"
        if status != 200 or not isinstance(body, list):
            return None, "unreadable", f"the issues list returned {status}"
        for item in body:
            # The issues endpoint returns pull requests too. A PR titled `Dependency Dashboard`
            # would otherwise be PATCHed instead of the dashboard. Tested with `in`, not for
            # truthiness: the key's PRESENCE is what marks a PR, and an empty object is a perfectly
            # legal value for it — a truthiness test passes the PR straight through.
            if "pull_request" in item:
                continue
            login = str((item.get("user") or {}).get("login") or "")
            if login.lower() != RENOVATE_LOGIN:
                continue
            if str(item.get("title", "")).strip() == DASHBOARD_TITLE:
                return int(item["number"]), "ok", ""
        walked += len(body)
        if len(body) < 100:
            return None, "absent", (
                f"Renovate has opened no {DASHBOARD_TITLE!r} issue in {repo} ({walked} open issue(s) "
                f"read, the whole list). The org preset enables `:dependencyDashboard`, so its "
                f"absence means the bot is not running here at all — a bigger finding than a held "
                f"bump, and not one a release may report green over"
            )
    return None, "unreadable", (
        f"walked {walked} open issues in {repo} over {PAGE_CAP} pages and the list was STILL full at "
        f"the cap, so the dashboard was not found and was not ruled out either. That is 'I could not "
        f"finish looking', which is a different fact from 'it is not there' and must not be reported "
        f"as the bot being absent (#266)"
    )


def names(line: str, package: str, version: str) -> bool:
    """Does this line name THIS package at THIS version?

    Both halves are word-bounded. `fs.gg.coord` must not match an `fs.gg.coord.cli` line, and
    `0.2.1` must not match `0.2.10` — a substring test gets both wrong, and the failure is a tick on
    somebody else's dependency.
    """
    low = line.lower()
    package_hit = re.search(rf"(?<![\w.]){re.escape(package.lower())}(?![\w.])", low)
    # Renovate writes `to 0.21.0` for some managers and `to v0.15.0` for others; both are this
    # version, and neither is `v0.15.01`. Lowercased like the package half — a `0.21.0-RC1` compared
    # against a lowercased line matches nothing, and the symptom is not an error but the blunt
    # `manual job` box being pressed on every receiver that had a precise one.
    version_hit = re.search(rf"(?<![\w.])v?{re.escape(version.lower())}(?![\w.])", low)
    return bool(package_hit and version_hit)


def choose(body: str, package: str, version: str) -> tuple[str, str, str]:
    """(outcome, line, why) — which box, if any, this release is entitled to tick.

    Order is the whole design. A named hold on THIS release is the precise instrument and is tried
    first; only when the release is nowhere in the dashboard at all — meaning the bot has not looked
    since it was published — is the blunt `manual job` box justified.
    """
    lines = body.splitlines()

    for line in lines:
        match = UNCHECKED.match(line)
        if not match:
            continue
        rest = match.group("rest")
        marker = MARKER.search(rest)
        if not marker:
            continue
        text = marker.group("marker")
        if any(denied in text for denied in DENIED_MARKERS):
            continue
        if not text.startswith("unlimit-branch="):
            continue
        if names(rest, package, version):
            return "ticked-unlimit", line, f"released the rate-limited branch holding {package} {version}"

    if any(names(line, package, version) for line in lines):
        return (
            "already-offered",
            "",
            f"{package} {version} already appears on the dashboard (an open bump, or a detected "
            f"update Renovate has not rate-limited), so nothing is held and there is nothing to push",
        )

    for line in lines:
        match = UNCHECKED.match(line)
        if not match:
            continue
        marker = MARKER.search(match.group("rest"))
        if marker and marker.group("marker").strip() == "manual job":
            return (
                "ticked-manual",
                line,
                f"{package} {version} appears nowhere on the dashboard, so the bot has not "
                f"re-extracted since it was published; forcing a re-run is the only thing that makes "
                f"it look",
            )

    # A `manual job` box that is ALREADY TICKED is a re-extraction somebody has already requested and
    # Renovate has not yet consumed. Re-ticking it is impossible (there is no unchecked box left) and
    # grading it `undetermined` would turn a perfectly healthy state into a red release — the
    # false-accusation shape this org refuses (#238). The push this receiver needs is already in
    # flight; say so and count it reached.
    if re.search(r"^\s*-\s\[x\]\s*<!--\s*manual job\s*-->", body, re.M | re.I):
        return (
            "already-offered",
            "",
            f"{package} {version} appears nowhere on the dashboard, but its `manual job` box is "
            f"ALREADY ticked — a re-extraction is queued and Renovate has not consumed it yet. "
            f"Nothing to press",
        )

    return (
        "undetermined",
        "",
        f"{package} {version} appears nowhere on the dashboard AND it carries no unchecked "
        f"`manual job` box, so there is no surface to push through. Not graded as ok",
    )


def tick(repo: str, number: int, body: str, line: str) -> tuple[str, str]:
    """PATCH the one line, then READ THE ANSWER BACK. Returns (state, detail).

    A `PATCH` that returns 200 and changes nothing is indistinguishable from one that worked, and
    that no-op-reporting-success is precisely what #1923 AC4 refuses. GitHub's response body IS the
    stored resource after the write, so it is the read-back — and it is the RIGHT one to use, rather
    than a second GET, because Renovate consumes a ticked box and resets it within seconds; a later
    GET showing `- [ ]` would be evidence of the mechanism WORKING, and would fail this assertion.
    """
    ticked_line = line.replace("- [ ]", "- [x]", 1)
    if ticked_line == line:
        return "refused", "the selected line does not carry an unchecked box (writer defect)"
    if body.count(line) != 1:
        return (
            "refused",
            f"the selected line occurs {body.count(line)} times in the dashboard; a replace would be "
            f"ambiguous and could tick the wrong box",
        )
    new_body = body.replace(line, ticked_line, 1)

    status, response = api(f"/repos/{repo}/issues/{number}", method="PATCH", payload={"body": new_body})
    if status in REFUSED_STATUSES:
        return "refused", (
            f"PATCH /repos/{repo}/issues/{number} returned {status}. The App installation cannot "
            f"write issues here, so this release cannot tell {repo} anything"
        )
    if status != 200 or not isinstance(response, dict):
        raise Retryable(f"{repo}: PATCH returned {status}")

    written = str(response.get("body") or "")
    if written == body:
        return "refused", (
            f"PATCH /repos/{repo}/issues/{number} returned 200 and the stored body is BYTE-IDENTICAL "
            f"to the one read before it. Nothing was written. A 200 is not evidence"
        )
    if ticked_line not in written:
        return "refused", (
            f"PATCH /repos/{repo}/issues/{number} returned 200 but the stored body does not contain "
            f"the ticked box. The write did not land as sent"
        )
    return "ok", ""


def annotate(level: str, message: str) -> None:
    print(f"::{level}::dashboard-tick: {message}")


def run_probe(registry: str | None) -> int:
    reached, undetermined, refused = [], [], []
    for repo in receivers(registry):
        try:
            number, kind, why = find_dashboard(repo)
        except Retryable as error:
            # ONE receiver's transport failure is not a verdict about the other six. Before this was
            # caught here it propagated out of the loop and every remaining receiver went uncontacted.
            undetermined.append(repo)
            annotate("warning", f"{repo}: {error}")
            continue
        if kind == "ok":
            reached.append(f"{repo}#{number}")
            print(f"  ok           {repo}: Dependency Dashboard #{number} is readable with this token")
        elif kind == "refused":
            refused.append(repo)
            annotate("error", f"{repo}: {why}")
        else:
            undetermined.append(repo)
            annotate("warning", f"{repo}: {why}")

    print(f"\nreached {len(reached)} · undetermined {len(undetermined)} · refused {len(refused)}")
    if refused:
        annotate(
            "error",
            "the installation token cannot reach "
            + ", ".join(refused)
            + " — #1923 AC1 is NOT satisfied and the push half must not ship over these receivers",
        )
        return 1
    if not reached:
        annotate("error", "no receiver dashboard was readable at all; nothing was proven")
        return 1
    if undetermined:
        return 2
    return 0


def run_tick(package: str, version: str, registry: str | None, dry_run: bool) -> int:
    rows = receivers(registry)
    print(f"telling {len(rows)} receiver(s) about {package} {version}\n")

    reached, undetermined, findings = 0, [], []
    for repo in rows:
        # ONE receiver, ONE verdict. A transport failure against receiver 1 used to propagate out of
        # this loop, so a release stopped after a single HTTP request and the other six — including
        # any carrying a live hold — were never contacted, while the package was already published.
        # A failure to reach one receiver is undetermined for THAT receiver and nothing more.
        try:
            number, kind, why = find_dashboard(repo)
            if kind == "refused":
                findings.append(f"{repo}: {why}")
                annotate("error", f"{repo}: {why}")
                continue
            if kind != "ok":
                undetermined.append(f"{repo}: {why}")
                annotate("warning", f"{repo}: {why}")
                continue

            status, issue = api(f"/repos/{repo}/issues/{number}")
            if status in REFUSED_STATUSES:
                findings.append(f"{repo}#{number}: reading the dashboard returned {status}")
                annotate("error", f"{repo}#{number}: reading the dashboard returned {status}")
                continue
            if status != 200 or not isinstance(issue, dict):
                undetermined.append(f"{repo}#{number}: reading the dashboard returned {status}")
                annotate("warning", f"{repo}#{number}: reading the dashboard returned {status}")
                continue
            body = str(issue.get("body") or "")
            outcome, line, note = choose(body, package, version)
        except Retryable as error:
            undetermined.append(f"{repo}: {error}")
            annotate("warning", f"{repo}: {error}")
            continue

        if outcome == "undetermined":
            undetermined.append(f"{repo}#{number}: {note}")
            annotate("warning", f"{repo}#{number}: {note}")
            continue
        if outcome == "already-offered":
            reached += 1
            print(f"  no-op        {repo}#{number}: {note}")
            continue
        if dry_run:
            reached += 1
            print(f"  would tick   {repo}#{number}: {note}\n               {line.strip()}")
            continue

        try:
            state, detail = tick(repo, number, body, line)
        except Retryable as error:
            undetermined.append(f"{repo}#{number}: {error}")
            annotate("warning", f"{repo}#{number}: {error}")
            continue
        if state != "ok":
            findings.append(f"{repo}#{number}: {detail}")
            annotate("error", f"{repo}#{number}: {detail}")
            continue
        reached += 1
        print(f"  ticked       {repo}#{number}: {note}\n               {line.strip()}")

    print(f"\nreached {reached} · undetermined {len(undetermined)} · findings {len(findings)}")

    if findings:
        annotate(
            "error",
            f"{len(findings)} receiver(s) refused or silently dropped the write; {package} "
            f"{version} did not reach them",
        )
        return 1
    if reached == 0:
        annotate(
            "error",
            f"{package} {version} was published and reached ZERO receivers. A release that tells "
            f"nobody is the failure #1923 exists to make visible; it is not a quiet success",
        )
        return 1
    if undetermined:
        annotate(
            "warning",
            f"{len(undetermined)} receiver(s) could not be graded, so this run is a NO VERDICT for "
            f"them — never an ok (#1923 AC6)",
        )
        return 2
    return 0


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--package", help="the published NuGet package id, e.g. FS.GG.Kit")
    parser.add_argument("--version", help="the version just published, e.g. 0.21.0")
    parser.add_argument("--registry", help="override registry/repos.yml (the fixture uses this)")
    parser.add_argument("--probe", action="store_true", help="read-only credential/reach assertion")
    parser.add_argument("--dry-run", action="store_true", help="decide and report; write nothing")
    args = parser.parse_args(argv)

    try:
        if args.probe:
            if args.package or args.version:
                raise Permanent("--probe writes nothing and takes no --package/--version")
            return run_probe(args.registry)
        if not args.package or not args.version:
            raise Permanent("--package and --version are both required unless --probe is given")
        return run_tick(args.package, args.version, args.registry, args.dry_run)
    except Permanent as error:
        annotate("error", f"no verdict (permanent): {error}")
        return 3
    except Retryable as error:
        annotate("error", f"no verdict (retryable): {error}")
        return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
