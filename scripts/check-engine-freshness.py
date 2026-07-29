#!/usr/bin/env python3
"""Gate the engine's SOURCE against the version the fleet can actually restore (.github#1075, epic #266).

THE DEFECT THIS CLOSES. `feed-coherence` asserts `registry.package-version == feed(newest)` —
registry vs feed. `source-coherence` asserts `registry.version == source` — registry vs source.
Neither compares SOURCE to FEED, and that is the direction this engine has failed in four times
(.github#844, #846, #964, #1067): the fix merges, nobody releases, and every receiver keeps
`exec`ing an engine that predates it.

WHY NO VERSION COMPARISON CAN SEE IT. Every other producer's source version moves with the CHANGE
(SDD#387 reds SDD's own PR when a surface edit skips the bump), so `version` drifting ahead of
`package-version` is their legible "merged, not yet published" signal. THIS engine's `<Version>`
moves only at RELEASE time, by hand — `release-coord-engine.yml` evaluates the fsproj property and
requires the tag to match it, so the bump IS the release act. While the source outruns the feed,
`<Version>` is CONSTANT, and `version == package-version` is precisely the state the bug lives in.
The scalars are equal BECAUSE the defect is happening. So this gate counts COMMITS, not scalars.

WHAT IT ASSERTS.

    tag   = coord-engine/v<the newest version LIVE ON THE FEED>
    drift = git log <tag>..HEAD -- <the engine's source trees>

A TAG IS NOT A PUBLISH, which is why the comparison point is derived from the FEED and the tag is
then resolved from it — never "the newest tag, assumed shipped". The `fs-gg-ui-template` PHANTOM
0.9.1 in registry/dependencies.yml is the precedent: all three tags cut, zero packages exist.

THE THRESHOLD, AND WHY IT IS THE WIRE SURFACE. `drift > 0` is the obvious bar and it is the wrong
one: measured on this repo, v0.3.0 was cut at 07:01 and 33 engine commits landed in the 8 hours
after it — more than the 28 that justified v0.3.0 itself. A gate that reds the moment any engine
commit merges is red ~always, and a gate that is red by design on the happy path teaches exactly one
lesson: "FAILED is noise, merge anyway" (the #698 trade, made explicit in pnext-item §5). Age fails
for the same reason from the other side — at this velocity every unreleased commit is hours old, so
an age bar is GREEN on the very drift this issue was filed about.

So the bar is RELEVANCE, and it has two principled definitions rather than a magic number:

  * `src/FS.GG.Coord.Core/Protocol.fs` is the engine's WIRE SURFACE — the exit codes and verb
    contract. It is ALSO the file `scripts/generate-projections` emits the exit-code tables into
    every worker-facing SKILL.md from (`source_of()`; 17 regions across both skill roots).
  * So when Protocol.fs has drifted unreleased, main's OWN DOCUMENTS describe an engine the fleet
    cannot run. The org contradicts itself, in the one place a worker looks to find out what a verb
    does. That is not a latent risk; it is the reported experience — a `take` exit table the engine
    disagrees with, a `say` that demands a flag the recipe says is optional, a bare issue ref the
    docs accept and the binary refuses.
  * A drift commit whose merged PR closes an issue declaring `Class: defect` carries a measured fix
    for something broken NOW. Receivers cannot detect that they are still running the broken
    behavior, and unlike ordinary development drift there is no safe latency to budget: any
    unreleased defect-class engine commit is RED (.github#1671). The closing-issue relation comes
    from GitHub's structural `closingIssuesReferences`, never from parsing `#123` prose out of a
    commit subject; the class comes from the issue's unfenced `Class:` declaration.
  * Drift that is internal-only and closes no defect (a refactor or hardening behind the wire) does
    NOT degrade a worker's ability to read the docs and be right. It is REPORTED, never red.

That is "loud exactly when the fleet is degraded, silent otherwise", keyed on facts rather than a
threshold. The wire leg catches three of the four occurrences above (#844 and #846 are literally
"next/take/done/add are all unknown command"; #1067 is the landable exit codes). The defect leg
catches #964's class too: a BEHAVIOUR regression behind an unchanged wire contract is red when its
closing issue declares `Class: defect`, while a hardening/refactor remains below the bar. That
distinction is the board's closed vocabulary, not a subject-line heuristic.

FAILS CLOSED, which is the whole point of epic #266. "I could not look" and "I looked, and it is
current" must never share an exit code. Every one of these is an ERROR, not a skip and never
"no drift":

  * the feed is unreachable, unauthorised, returns unparsable JSON, or serves zero versions;
  * the feed's newest version has NO matching `coord-engine/v<version>` tag (a publish with no tag,
    or a tag scheme that moved — either way the comparison point is unknown, not "current");
  * GitHub's associated-PR / closing-issue metadata is unreadable, partial, or malformed;
  * the tag exists but git cannot read it, or the repo has no commits;
  * the wire-surface file does not exist at the path this gate names (the protocol moved, and a
    hard-coded path that silently checks nothing is the #266 shape this gate exists to refuse);
  * an engine source tree named below does not exist.

Usage:  scripts/check-engine-freshness.py [--repo <dir>] [--ref HEAD]

`--fixture <feed.json>` serves a canned feed instead of the live one. It is NOT a freshness signal,
and it refuses to run unless FSGG_ENGINE_FIXTURE_OK=1 — which only tests/engine-freshness/ sets. A
test hook that can silently turn the gate into a no-op is the very defect class above.

Exit 0 = the fleet's engine carries every wire-surface and defect-class commit on this ref.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import urllib.error
import urllib.request

# The feed reader and NuGet version ordering are SHARED with check-feed-coherence.py and
# check-pin-coherence.py (.github#263) — one implementation of "what does the feed serve", so the
# gates cannot drift into disagreeing about version order. `scripts/` is not a package, so put this
# file's own directory on the path: the test harness loads this gate by path via importlib, which
# sets sys.path[0] to the TEST's directory, not to scripts/.
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from fsgg_feed import (  # noqa: E402  (path shim above must run first)
    ORG,
    GateError,
    feed_versions,
    is_prerelease,
    newest,
)

# The package the fleet restores and `scripts/fsgg-coord` execs (ADR-0034 §4.4).
PACKAGE = "FS.GG.Coord.Cli"

# `release-coord-engine.yml` releases on `coord-engine/v<version>` matching the fsproj <Version>.
TAG_PREFIX = "coord-engine/v"

# The trees whose commits ship in that package. A commit outside them cannot change the engine the
# fleet runs, so counting it would be noise with a gate's authority behind it.
ENGINE_SOURCE = (
    "src/FS.GG.Coord.Cli",
    "src/FS.GG.Coord.Core",
    "src/FS.GG.Coord.GitHub",
)

# The engine's wire surface — see the threshold discussion in the module docstring.
#
# THIS PATH IS COUPLED TO `scripts/generate-projections`' `source_of()`, which names the same file as
# the source of every `fsgg-protocol:*` region. The coupling is deliberate and it is checked rather
# than assumed: `_assert_exists` below reds the gate if this path stops existing, so a protocol that
# MOVES cannot leave this gate quietly measuring nothing. It cannot be read off the generator today —
# `--list` emits a kind's OUTPUTS (kind/path/marker, ADR-0044), not its source — and widening that
# contract to carry the source would reach `scripts/generated-paths`, `check-generator-list.py` and
# their fixture. Filed as the honest way to close it (.github#1075's follow-up).
WIRE_SURFACE = "src/FS.GG.Coord.Core/Protocol.fs"


def git(repo: str, *args: str) -> str:
    """Run git in `repo`, returning stdout. Any failure is a GateError — never a silent empty."""
    try:
        p = subprocess.run(
            ("git", "-C", repo, *args),
            capture_output=True,
            text=True,
            check=False,
        )
    except OSError as e:
        raise GateError(f"cannot run git: {e}") from e
    if p.returncode != 0:
        raise GateError(
            f"`git {' '.join(args)}` failed (exit {p.returncode}): {p.stderr.strip() or '(no stderr)'}"
        )
    return p.stdout


def _assert_exists(repo: str, ref: str, path: str, what: str) -> None:
    """A path this gate MEASURES must exist on the ref, or the gate is measuring nothing."""
    out = git(repo, "ls-tree", "-r", "--name-only", ref, "--", path)
    if not out.strip():
        raise GateError(
            f"{what} {path!r} does not exist at {ref}. This gate is hard-coded to measure it, so a "
            f"path that has moved would leave the gate reporting green over an unwatched subject — "
            f"the exact fails-open shape epic #266 is about. Update {os.path.basename(__file__)} to "
            f"name the new path (and check `scripts/generate-projections`' source_of(), which names "
            f"the same file)."
        )


def resolve_tag(repo: str, version: str) -> str:
    """The tag for `version`. An absent tag is an ERROR: a publish whose commit we cannot name."""
    tag = f"{TAG_PREFIX}{version}"
    try:
        git(repo, "rev-parse", "--verify", f"refs/tags/{tag}^{{commit}}")
    except GateError as e:
        raise GateError(
            f"the feed's newest {PACKAGE} is {version!r}, but this repo has no tag {tag!r} — so the "
            f"commit that produced the package the fleet restores cannot be named, and the drift "
            f"cannot be measured. This is an ERROR, not 'no drift': either the release published "
            f"without its tag, the tag was deleted, or the tag scheme moved. ({e})"
        ) from e
    return tag


def drift_commits(repo: str, tag: str, ref: str, paths: tuple[str, ...]) -> list[tuple[str, str]]:
    """`git log tag..ref -- paths`, as `(full sha, 'short-sha subject')` rows."""
    out = git(repo, "log", "--format=%H%x00%h %s", f"{tag}..{ref}", "--", *paths)
    rows = []
    for line in out.splitlines():
        if not line.strip():
            continue
        try:
            sha, display = line.split("\0", 1)
        except ValueError as e:
            raise GateError(f"git emitted an unreadable commit row {line!r}") from e
        rows.append((sha, display))
    return rows


def _issue_rows(raw: object, where: str) -> list[dict]:
    """Validate issue metadata before policy reads it. An unreadable class is never 'not defect'."""
    if not isinstance(raw, list):
        raise GateError(f"{where} is not an issue list")
    rows = []
    for issue in raw:
        if not isinstance(issue, dict) or not isinstance(issue.get("number"), int):
            raise GateError(f"{where} contains an issue without an integer `number`")
        body = issue.get("body")
        if body is not None and not isinstance(body, str):
            raise GateError(f"{where} issue #{issue['number']} has a non-text `body`")
        rows.append({"number": issue["number"], "body": body or ""})
    return rows


def closing_issues(
    commits: list[tuple[str, str]], token: str, fixture_table: dict | None
) -> dict[str, list[dict]]:
    """Closing issues for each drift commit, structurally through its merged associated PR."""
    if not commits:
        return {}
    if fixture_table is not None:
        raw = fixture_table.get("_closingIssues", {})
        if not isinstance(raw, dict):
            raise GateError("fixture `_closingIssues` is not an object keyed by commit sha")
        return {
            sha: _issue_rows(raw.get(sha, []), f"fixture `_closingIssues[{sha}]`")
            for sha, _ in commits
        }

    fields = []
    for i, (sha, _) in enumerate(commits):
        fields.append(
            f"""c{i}: object(expression: "{sha}") {{
              ... on Commit {{
                associatedPullRequests(first: 20) {{
                  pageInfo {{ hasNextPage }}
                  nodes {{
                    merged
                    mergeCommit {{ oid }}
                    closingIssuesReferences(first: 50) {{
                      pageInfo {{ hasNextPage }}
                      nodes {{ number body }}
                    }}
                  }}
                }}
              }}
            }}"""
        )
    query = (
        'query { repository(owner: "FS-GG", name: ".github") {\n'
        + "\n".join(fields)
        + "\n} }"
    )
    req = urllib.request.Request(
        "https://api.github.com/graphql",
        data=json.dumps({"query": query}).encode("utf-8"),
        headers={
            "Authorization": f"Bearer {token}",
            "Accept": "application/vnd.github+json",
            "Content-Type": "application/json",
            "User-Agent": "fsgg-check-engine-freshness",
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            payload = json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        raise GateError(f"GitHub rejected the closing-issue read (HTTP {e.code} {e.reason})") from e
    except urllib.error.URLError as e:
        raise GateError(f"GitHub closing-issue read was unreachable: {e.reason}") from e
    except ValueError as e:
        raise GateError(f"GitHub closing-issue read returned unparsable JSON: {e}") from e

    if not isinstance(payload, dict) or payload.get("errors"):
        raise GateError(f"GitHub closing-issue GraphQL read failed: {payload.get('errors')!r}")
    repository = ((payload.get("data") or {}).get("repository"))
    if not isinstance(repository, dict):
        raise GateError("GitHub closing-issue read returned no FS-GG/.github repository")

    answer: dict[str, list[dict]] = {}
    for i, (sha, _) in enumerate(commits):
        commit = repository.get(f"c{i}")
        if not isinstance(commit, dict):
            raise GateError(f"GitHub could not resolve drift commit {sha}")
        prs = commit.get("associatedPullRequests")
        if not isinstance(prs, dict) or not isinstance(prs.get("nodes"), list):
            raise GateError(f"GitHub returned no associated-PR connection for drift commit {sha}")
        if (prs.get("pageInfo") or {}).get("hasNextPage"):
            raise GateError(f"drift commit {sha} has more than 20 associated PRs; refusing a partial read")

        issues = []
        for pr in prs["nodes"]:
            if not isinstance(pr, dict) or not pr.get("merged"):
                continue
            if (pr.get("mergeCommit") or {}).get("oid") != sha:
                continue
            closing = pr.get("closingIssuesReferences")
            if not isinstance(closing, dict) or not isinstance(closing.get("nodes"), list):
                raise GateError(f"GitHub returned no closing-issue connection for drift commit {sha}")
            if (closing.get("pageInfo") or {}).get("hasNextPage"):
                raise GateError(f"drift commit {sha} closes more than 50 issues; refusing a partial read")
            issues.extend(_issue_rows(closing["nodes"], f"GitHub closing issues for {sha}"))

        # A PR may be reachable through more than one association edge. Classify each issue once.
        answer[sha] = list({row["number"]: row for row in issues}.values())
    return answer


def declares_defect(body: str) -> bool:
    """The defect leg of Class.fromBody's grammar: unfenced, 0-3 spaces, case-insensitive."""
    fence = None
    for line in body.splitlines():
        if fence is not None:
            if re.match(rf"^ {{0,3}}{re.escape(fence[0])}{{{fence[1]},}}\s*$", line):
                fence = None
            continue

        marker = re.match(r"^ {0,3}(`{3,}|~{3,})", line)
        if marker:
            token = marker.group(1)
            fence = (token[0], len(token))
            continue
        declaration = re.match(r"^ {0,3}[Cc]lass:\s*(.*?)\s*$", line)
        if declaration and declaration.group(1).casefold() == "defect":
            return True
    return False


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    ap.add_argument("--repo", default=".", help="the git repo to measure (default: cwd)")
    ap.add_argument("--ref", default="HEAD", help="the ref to measure drift up to (default: HEAD)")
    ap.add_argument("--fixture", help="read the feed from a JSON file (tests only, never in CI)")
    args = ap.parse_args(argv)
    fixture_table = None
    token = ""

    if args.fixture:
        # A flag that makes the gate report green without reading the feed is precisely the
        # fails-open shape epic #266 is about, so it is not merely documented as test-only — it is
        # locked. A stray `--fixture` anywhere else (a copy-pasted CI step, a debugging line left
        # behind) fails the gate instead of quietly turning it into a no-op.
        if os.environ.get("FSGG_ENGINE_FIXTURE_OK") != "1":
            print(
                "::error::check-engine-freshness: --fixture reads a canned feed and is NOT a "
                "freshness signal. It is available only to tests/engine-freshness/, which sets "
                "FSGG_ENGINE_FIXTURE_OK=1. Refusing to run.",
                file=sys.stderr,
            )
            return 1
        # Loud on purpose: a fixture run must never be mistaken for a live-feed run in a log.
        print(f"FIXTURE MODE — reading {args.fixture}, NOT the live feed. Not a freshness signal.")

        def resolve() -> list[str]:
            nonlocal fixture_table
            try:
                fixture_table = json.load(open(args.fixture, encoding="utf-8"))
            except (OSError, ValueError) as e:
                raise GateError(f"cannot read fixture: {e}") from e
            if not isinstance(fixture_table, dict):
                raise GateError("fixture is not a JSON object")
            if PACKAGE not in fixture_table:
                raise GateError(f"package {PACKAGE!r} is not on the org feed (fixture: absent)")
            vs = fixture_table[PACKAGE]
            if not vs:
                raise GateError(f"the feed served zero versions for {PACKAGE!r}")
            return list(vs)
    else:
        token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN") or ""
        if not token:
            print(
                "::error::check-engine-freshness: no GITHUB_TOKEN/GH_TOKEN in the environment. The "
                "org feed cannot be read without one, and an unreadable feed must fail the gate, "
                "not skip it.",
                file=sys.stderr,
            )
            return 1

        def resolve() -> list[str]:
            return feed_versions(PACKAGE, token)

    try:
        live = resolve()
        # The engine may not ship a prerelease at all: release-coord-engine.yml REFUSES one,
        # because the org's Renovate preset sets ignoreUnstable=true and receivers would never see
        # it. So the fleet's version is the newest STABLE, and a stray prerelease on the feed must
        # not become the comparison point.
        stable = [v for v in live if not is_prerelease(v)]
        if not stable:
            raise GateError(
                f"the feed serves no stable version of {PACKAGE} — only prereleases {sorted(live)}. "
                f"release-coord-engine.yml refuses to publish a prerelease, so this feed cannot be "
                f"the fleet's engine and the comparison point is unknown."
            )
        version = newest(stable)

        # Fail closed on the subjects this gate is hard-coded to measure, BEFORE reporting anything
        # about them.
        for tree in ENGINE_SOURCE:
            _assert_exists(args.repo, args.ref, tree, "engine source tree")
        _assert_exists(args.repo, args.ref, WIRE_SURFACE, "the wire-surface file")

        tag = resolve_tag(args.repo, version)
        all_drift = drift_commits(args.repo, tag, args.ref, ENGINE_SOURCE)
        wire_drift = drift_commits(args.repo, tag, args.ref, (WIRE_SURFACE,))
        issues_by_commit = closing_issues(all_drift, token, fixture_table)
    except GateError as e:
        print(f"::error::check-engine-freshness: {e}", file=sys.stderr)
        return 1

    print(
        f"the fleet restores {PACKAGE} {version} (newest on the {ORG} feed), cut at {tag}.\n"
        f"engine commits on {args.ref} since {tag}: {len(all_drift)} "
        f"({len(wire_drift)} touching the wire surface {WIRE_SURFACE})"
    )

    # ALWAYS report the drift, whatever the verdict. Drift below the red bar is a subject the gate
    # CAN see, so it is printed in full rather than collapsed into a green — "checked, and it is
    # below the bar" must be legible as such, not indistinguishable from "there is nothing here".
    if all_drift:
        print("\nunreleased engine commits (oldest first):")
        wire_shas = {sha for sha, _ in wire_drift}
        for sha, display in reversed(all_drift):
            mark = "  WIRE " if sha in wire_shas else "       "
            issue_refs = ", ".join(f"#{row['number']}" for row in issues_by_commit[sha])
            closes = f" — closes {issue_refs}" if issue_refs else ""
            print(f"{mark}{display}{closes}")

    defect_drift = []
    for sha, display in all_drift:
        defect_issues = [
            issue["number"] for issue in issues_by_commit[sha] if declares_defect(issue["body"])
        ]
        if defect_issues:
            defect_drift.append((sha, display, defect_issues))

    if defect_drift:
        print("\nunreleased defect-class engine commits (oldest first):")
        for _, display, issues in reversed(defect_drift):
            refs = ", ".join(f"#{number}" for number in issues)
            print(f"  DEFECT {display} — closes {refs}")

    if wire_drift:
        print()
        print(
            f"::error::check-engine-freshness: the engine's WIRE SURFACE has outrun the feed — "
            f"{len(wire_drift)} commit(s) have changed {WIRE_SURFACE} since {tag}, the release that "
            f"produced the {version} every receiver restores. `scripts/generate-projections` emits "
            f"this repo's OWN exit-code tables into every worker-facing SKILL.md from that file, so "
            f"main now documents a verb contract the fleet's engine does not implement: a worker "
            f"reading the recipe and being refused by the binary is this state. Release the engine "
            f"— bump <Version> in src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj and push the matching "
            f"{TAG_PREFIX}<version> tag (release-coord-engine.yml does the rest).",
            file=sys.stderr,
        )
    if defect_drift:
        print(
            f"::error::check-engine-freshness: {len(defect_drift)} unreleased engine commit(s) "
            f"close an issue declaring `Class: defect`. Receivers still restore {version}, so they "
            f"still run behavior the merged defect fixes proved wrong. Defect-class drift has no "
            f"green latency budget: create the separate release item and cut the next "
            f"FS.GG.Coord.Cli version through release-coord-engine.yml; do not fold an unplanned "
            f"publish into the source PR that exposed this signal.",
            file=sys.stderr,
        )

    if wire_drift or defect_drift:
        return 1

    if all_drift:
        print(
            f"\nok: {len(all_drift)} unreleased engine commit(s), none touching the wire surface. "
            f"Reported, not red: the fleet's engine implements the verb contract this repo "
            f"documents. A release is still owed."
        )
        return 0

    print(f"\nok: no engine commits since {tag} — the fleet runs this ref's engine.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
