#!/usr/bin/env python3
"""Gate every Renovate-annotated version pin against the live org feed (.github#263, epic #266).

THE DEFECT THIS CLOSES. `.github/workflows/contract-coherence.yml` installs the typed registry
validator at a pinned version:

    # renovate: datasource=nuget depName=FS.GG.SDD.Cli
    dotnet tool install --global FS.GG.SDD.Cli --version 0.9.0

The pin is supposed to track feed-newest — the registry row `registry-validator-typed` asserts
`coherent: true` on exactly that strength, because under the registry's additive tolerance a frozen
typed validator silently degrades toward a "does the YAML parse" check. Nothing enforced it. The
literal sat at 0.2.1 while the org shipped 0.5.0 (.github#127, "H2"), was fixed by hand, then sat at
0.5.0 while the org shipped 0.6.0, 0.7.0 and 0.8.0 (.github#263). Both times a human found it.

The mechanism the row depends on — the org preset's annotation manager auto-bumping the literal —
was never proven. This gate proves it, every day, by checking its OUTPUT.

WHAT IT ASSERTS. Three things, which together mean "this pin can move, and it has moved":

  1. FRESHNESS. For every annotated pin, the literal equals the newest version live on the org feed.
  2. MECHANISM (config). This repo's own Renovate config carries the `hostRules` feed token, without
     which Renovate cannot enumerate FS.GG.* versions and therefore cannot bump ANY of these pins.
  3. MECHANISM (behaviour) — .github#566. When a pin IS stale, the gate goes and finds out WHY,
     from Renovate's own artifacts, instead of asserting a cause. (2) proves the token block is
     PRESENT; it cannot prove the token RESOLVES, and those came apart: the block has been present
     since #263 and the pin froze anyway, because `{{ secrets.FSGG_PACKAGES_READ_TOKEN }}` is a Mend
     App Secret that nothing in CI can read — so nobody ever verified it. This gate used to print
     "the annotation manager did not bump it", which is a cause it never checked and which reads
     identically whether the bot is BLIND or merely hasn't run yet. It now discriminates:

       * the dep is missing from Renovate's Dependency Dashboard  -> the manager's regex broke;
       * the dep is detected AND a bump PR exists                 -> benign, merge that PR;
       * the dep is detected, the feed has a newer version, and    -> THE BOT IS BLIND. It 401s on
         Renovate opened no PR — ever                                 the feed. Fix the token; do NOT
                                                                      hand-bump (that is what #127
                                                                      and #263 did, and it froze
                                                                      again both times).

(2) is the root cause of the .github#263 recurrence. `FS.GG.*` resolves from the private org
GitHub Packages feed, and Renovate does not substitute `{{ secrets }}` inside a preset pulled via
`extends` — so the token must live in each repo's OWN config. Every product repo has it. `.github`,
which authors the preset and dogfoods it, never did: its FS.GG.* lookups 401'd, silently, so no bump
PR could ever open. Only third-party bumps (nuget.org, no auth) were ever observed, which is exactly
what the compatibility projection recorded without anyone reading it as a symptom.

THE SUBJECT IS THE MANAGER'S OWN REGEX. This gate does not hard-code what a pin looks like. It reads
`default.json`, finds the annotation-driven custom manager, and scans with THAT regex over THOSE file
patterns, skipping the paths Renovate's `ignorePaths` excludes. So the gate and the bot cannot
disagree about what a pin is: if the manager's regex stops matching the pin (a reformat, a moved
literal, a renamed file), the bot goes silent AND the gate goes red, instead of the bot going silent
alone — and the gate never reds over a pin the bot was never going to bump.

FAILS CLOSED, which is the point of epic #266. "Nothing to check" and "checked, and it's fine" must
not share an exit code. Every one of these is an ERROR, not a skip:

  * `default.json` is unreadable, or declares no annotation-driven manager;
  * the manager's regex matches ZERO pins repo-wide (it has stopped seeing its subject);
  * a pin named in REQUIRED_PINS has become invisible to that regex — the bot has gone silent on a
    pin we know exists, which a scan alone can never detect, because it scans with the same regex;
  * a pin names a datasource or a package this gate cannot resolve (it must not guess);
  * the feed is unreachable, 401s, 404s, serves zero versions, or returns an unrecognised shape;
  * a version literal (pin or feed) does not parse;
  * this repo's Renovate config is absent, or lacks the feed `hostRules` token.

Comparison is by NuGet version ORDER, never by substring — the .github#268 defect class, where
`0.4.0` matches inside `0.4.0-preview.1`. Ordering and feed reads are shared with
scripts/check-feed-coherence.py via scripts/fsgg_feed.py, so the two gates cannot drift.

Usage:  scripts/check-pin-coherence.py [--root .]

`--fixture <feed.json>` serves a canned feed instead of the live one. It is NOT a coherence signal,
and it refuses to run unless FSGG_PIN_FIXTURE_OK=1 — which only tests/pin-coherence/ sets. A test
hook that can silently turn the gate into a no-op is the very defect class above.

Exit 0 = every annotated pin is at feed-newest, and the bot is configured to keep it there.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
from fnmatch import fnmatch
from typing import NamedTuple

# Shared with scripts/check-feed-coherence.py — one implementation of NuGet ordering + feed reads.
# `scripts/` is not a package, and the test harness loads this gate by path via importlib (which
# sets sys.path[0] to the TEST's directory), so put this file's own directory on the path.
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from fsgg_feed import (  # noqa: E402  (path shim above must run first)
    GateError,
    feed_versions,
    newest,
    parse_version,
)

# The host every FS.GG.* package resolves from. The preset routes them here with `registryUrls`;
# Renovate needs a `hostRules` token for the same host or every lookup 401s.
FEED_HOST = "nuget.pkg.github.com"

# Renovate reads the FIRST config file it finds, and this tuple reproduces its documented resolution
# order exactly. `.github` keeps its config at the repo root; the product repos keep theirs under
# `.github/`. Both are valid, and finding NONE is an error, not a skip (a repo with pins and no
# config cannot bump them).
#
# The order is load-bearing, not decorative. Read the wrong file and this gate answers a question
# about a config Renovate never uses: a token-bearing `.renovaterc` alongside a token-less
# `.github/renovate.json` would report green while the bot goes on 401'ing — the exact fails-open
# shape of epic #266, rebuilt inside the gate meant to close it.
RENOVATE_CONFIG_NAMES = (
    "renovate.json",
    "renovate.json5",
    ".github/renovate.json",
    ".github/renovate.json5",
    ".renovaterc",
    ".renovaterc.json",
    ".renovaterc.json5",
)

# Pins this repo is KNOWN to carry. Scanning alone cannot detect cause (1) of .github#263 — that the
# manager's regex silently stopped matching a pin — because a gate that scans with the very regex
# under suspicion sees exactly the nothing the bot sees. So the expected subjects are named here, and
# a missing one is an ERROR. This is the sibling of check-feed-coherence.py's CONTRACT_PACKAGES: an
# explicit inventory, where forgetting to add an entry fails loudly instead of shrinking the gate.
#
# It is a MINIMUM, not an allow-list. A new annotated pin is discovered by the scan and checked for
# freshness like any other; it need not be listed. Listing buys detection of DISAPPEARANCE.
#
# Deliberately NOT inferred from a "does this file mention `# renovate:`" heuristic. This repo's
# registry and preset describe the annotation format in prose (registry/dependencies.yml, and
# default.json's own matchStrings + description), and a gate that reddened on documentation would be
# switched off — which is how a gate ends up failing open for real.
REQUIRED_PINS: frozenset[tuple[str, str]] = frozenset({
    (".github/workflows/contract-coherence.yml", "FS.GG.SDD.Cli"),
})

# Renovate/RE2 spells named groups `(?<name>...)`; Python spells them `(?P<name>...)`. Rewrite only
# real named groups — never the lookbehinds `(?<=` and `(?<!`.
_NAMED_GROUP = re.compile(r"\(\?<(?![=!])")

# Directories Renovate does not scan, so neither does this gate. `default.json` extends
# `config:recommended`, which pulls in `:ignoreModulesAndTests` — an `ignorePaths` list of
# `**/<dir>/**` globs. A `**/<dir>/**` minimatch means "any path with <dir> as a segment", including
# at the root, so this compares path SEGMENTS rather than using fnmatch (whose `*` crosses `/` and
# whose `**/tests/**` would therefore miss `tests/x`).
#
# This is not a convenience skip. The gate's whole premise is that it sees exactly what the bot sees:
# scanning a file Renovate ignores would red the build over a pin Renovate was never going to bump.
# That is precisely what happened when this gate first ran over its own fixture — tests/pin-coherence/
# run.sh is a `.sh` file, matched by the preset's managerFilePatterns, and its heredocs carry
# annotation-shaped pins (`Expecto`, `FS.GG.Contracts`) that exist only as test data.
#
# The converse — an OPERATIVE pin parked under one of these directories — would be invisible to the
# bot and so freeze silently. REQUIRED_PINS is the guard: it names the real pins by path, and any of
# them landing somewhere unscanned reads as "gone invisible to the manager", which is red.
_IGNORED_SEGMENTS = frozenset({
    "node_modules", "bower_components", "vendor", "examples",
    "__tests__", "test", "tests", "__fixtures__",
})


class Pin(NamedTuple):
    file: str
    line: int
    datasource: str
    dep_name: str
    current_value: str


def _to_python_regex(renovate_regex: str) -> re.Pattern:
    try:
        return re.compile(_NAMED_GROUP.sub("(?P<", renovate_regex))
    except re.error as e:
        raise GateError(f"cannot compile the manager's matchString as a regex: {e}") from e


def _file_matcher(patterns: list[str]):
    """Renovate's `managerFilePatterns`: `/regex/` if slash-delimited, else a minimatch glob."""
    if not patterns:
        raise GateError("the annotation manager declares no `managerFilePatterns`")
    regexes, globs = [], []
    for p in patterns:
        if len(p) > 1 and p.startswith("/") and p.endswith("/"):
            regexes.append(_to_python_regex(p[1:-1]))
        else:
            globs.append(p)

    def matches(path: str) -> bool:
        return any(r.search(path) for r in regexes) or any(fnmatch(path, g) for g in globs)

    return matches


def load_annotation_manager(config_path: str) -> tuple[list[re.Pattern], object]:
    """The org preset's annotation-driven custom manager: its regexes and its file matcher.

    Identified structurally — the manager whose matchStrings capture both `depName` and
    `currentValue` — rather than by its description, which is prose and may be reworded.
    """
    try:
        with open(config_path, encoding="utf-8") as fh:
            preset = json.load(fh)
    except OSError as e:
        raise GateError(f"cannot read the org Renovate preset {config_path!r}: {e}") from e
    except ValueError as e:
        raise GateError(f"the org Renovate preset {config_path!r} is not valid JSON: {e}") from e

    managers = preset.get("customManagers")
    if not isinstance(managers, list) or not managers:
        raise GateError(
            f"{config_path} declares no `customManagers`. The annotation-driven manager is what "
            f"bumps every embedded pin; without it the pins below are unmanaged."
        )

    for m in managers:
        strings = m.get("matchStrings") or []
        if not all(isinstance(s, str) for s in strings):
            continue
        if any("(?<depName>" in s and "(?<currentValue>" in s for s in strings):
            return (
                [_to_python_regex(s) for s in strings],
                _file_matcher(m.get("managerFilePatterns") or []),
            )

    raise GateError(
        f"{config_path} declares no annotation-driven custom manager (none captures both "
        f"`depName` and `currentValue`). The `# renovate: datasource=.. depName=..` pins in this "
        f"repo are therefore bumped by nothing."
    )


def _ignored(rel: str) -> bool:
    """Is this path inside a directory Renovate's `ignorePaths` excludes?"""
    return any(seg in _IGNORED_SEGMENTS for seg in rel.split("/")[:-1])


def tracked_files(root: str) -> list[str]:
    """Exactly the files Renovate sees: tracked, minus the paths its `ignorePaths` excludes."""
    try:
        out = subprocess.run(
            ["git", "-C", root, "ls-files", "-z"],
            check=True, capture_output=True, text=True,
        ).stdout
    except (OSError, subprocess.CalledProcessError) as e:
        raise GateError(f"cannot list tracked files under {root!r}: {e}") from e
    return [p for p in out.split("\0") if p and not _ignored(p)]


def scan_pins(root: str, regexes: list[re.Pattern], matches_path) -> list[Pin]:
    """Every pin the org preset's annotation manager can see — scanned with the manager's own regex.

    Scanning with the manager's regex, over the manager's file patterns, is the point: the gate sees
    exactly what the bot sees. Anything the bot would silently ignore, this ignores too — and
    REQUIRED_PINS is what turns that silence into a failure.
    """
    pins: list[Pin] = []
    for rel in tracked_files(root):
        if not matches_path(rel):
            continue
        try:
            with open(os.path.join(root, rel), encoding="utf-8") as fh:
                text = fh.read()
        except (OSError, UnicodeDecodeError):
            continue  # not a text file the manager could read either

        for rx in regexes:
            for m in rx.finditer(text):
                g = m.groupdict()
                pins.append(
                    Pin(
                        file=rel,
                        line=text.count("\n", 0, m.start()) + 1,
                        datasource=(g.get("datasource") or "").strip(),
                        dep_name=(g.get("depName") or "").strip(),
                        current_value=(g.get("currentValue") or "").strip(),
                    )
                )

    # A manager may declare several matchStrings (Renovate's default strategy is `any`), and two of
    # them can match the same literal. Report such a pin once rather than twice — deduplicating the
    # IDENTICAL tuple only, so two genuinely different pins on one line still both surface.
    return list(dict.fromkeys(pins))


def assert_required_pins(pins: list[Pin]) -> None:
    """Every pin this repo is known to carry must still be visible to the manager's regex."""
    seen = {(p.file, p.dep_name) for p in pins}
    missing = sorted(REQUIRED_PINS - seen)
    if not missing:
        return
    # Report every missing pin, not just the first: if a reformat blinded the manager, it likely
    # blinded it for all of them, and fixing them one red run at a time is how a gate gets muted.
    detail = "; ".join(f"{dep} in {path}" for path, dep in missing)
    raise GateError(
        f"the org preset's annotation manager no longer sees {len(missing)} known pin(s): {detail}. "
        f"Either they were removed (drop them from REQUIRED_PINS), or the annotation/manager regex "
        f"stopped matching them — in which case the bot has gone silent on them exactly as in "
        f".github#263, and the literals will freeze without anything noticing."
    )


def check_bump_mechanism(root: str) -> str:
    """This repo's Renovate config must carry the feed token, or no FS.GG.* pin can ever bump."""
    present = [n for n in RENOVATE_CONFIG_NAMES if os.path.isfile(os.path.join(root, n))]
    if not present:
        raise GateError(
            "this repo has no Renovate configuration "
            f"({', '.join(RENOVATE_CONFIG_NAMES)}), so nothing bumps its pins."
        )
    config_path = present[0]

    if config_path.endswith(".json5"):
        raise GateError(
            f"{config_path} is JSON5, which this gate cannot parse. Refusing to report green on a "
            f"config it did not read."
        )
    try:
        with open(os.path.join(root, config_path), encoding="utf-8") as fh:
            cfg = json.load(fh)
    except (OSError, ValueError) as e:
        raise GateError(f"cannot read this repo's Renovate config {config_path!r}: {e}") from e

    rules = cfg.get("hostRules")
    if not isinstance(rules, list) or not any(
        isinstance(r, dict)
        and str(r.get("matchHost", "")).strip() == FEED_HOST
        and str(r.get("token", "")).strip()
        for r in rules
    ):
        raise GateError(
            f"{config_path} declares no `hostRules` token for {FEED_HOST}. FS.GG.* packages resolve "
            f"only from the private org feed, and Renovate does NOT substitute {{{{ secrets }}}} "
            f"inside a preset pulled via `extends` — so the token must live in THIS repo's own "
            f"config. Without it every FS.GG.* lookup 401s, no bump PR is ever opened, and the pins "
            f"below freeze silently. This is the .github#263 root cause; do not delete this check."
        )
    return config_path


# ---- Is the bot ACTUALLY bumping, or only configured to? (.github#566) ---------------------------
#
# check_bump_mechanism above proves the `hostRules` block is PRESENT. It cannot prove the token in it
# RESOLVES, and those are different facts: the block has been present since #263, and the pin froze
# anyway. When the bot goes blind to the private feed — a Mend App Secret that was never set, an
# expired or rotated token, a renamed secret — the presence check stays GREEN and only freshness goes
# red. The gate then printed "the annotation manager did not bump it", which is a CAUSE IT NEVER
# CHECKED, and is indistinguishable from the benign "0.10.0 shipped an hour ago and the bot has not
# run since". That conflation is epic #266's subject exactly: "I could not check" must never share a
# verdict with "I checked, and it is broken".
#
# The token itself is unreachable from here — it is a Mend App Secret, and CI's GITHUB_TOKEN is a
# different credential on a different path, so a lookup that succeeds here proves nothing about the
# bot. What IS reachable is the bot's OWN OUTPUT, and it discriminates:
#
#   * Renovate's Dependency Dashboard lists every dependency it DETECTED. If the pin is there, the
#     manager's regex matched and the bot knows the dependency exists.
#   * Renovate's PRs say what it managed to BUMP.
#
# Detected + an update is due on the feed + it opened no PR  ⇒  it could not enumerate the feed.
# There is no other explanation: it saw the dependency, a newer version exists, and it did nothing.
# That is the #127 / #263 root cause, and naming it is the difference between fixing the token and
# hand-bumping the literal a third time.
GITHUB_API = "https://api.github.com"
DASHBOARD_TITLE = "Dependency Dashboard"


class BotEvidence(NamedTuple):
    """What Renovate itself says it saw and did. Gathered from its OWN artifacts, not inferred."""

    detected: bool  # does the Dependency Dashboard list this dep? (i.e. did the manager match it?)
    bump_prs: list  # (number, state, title) of Renovate PRs naming THIS dep — any state
    dashboard: int | None
    # Renovate PRs naming ANY FS.GG.* package. This is the FEED-ACCESS signal, and it is deliberately
    # separate from bump_prs, because the two answer different questions and conflating them produces
    # the exact wrong verdict this gate exists to prevent:
    #
    #   bump_prs   — "did the bot bump THIS pin?"        (empty is normal; the pin may be current)
    #   feed_prs   — "can the bot see the private feed?" (empty, with a due update, means it CANNOT)
    #
    # Matching only on the dep name would call a WORKING bot blind, because the org preset GROUPS
    # some FS.GG.* packages: `groupName: "FS.GG.UI coherent set"` opens ONE PR titled "update fs.gg.ui
    # coherent set", whose title contains no member's name. A grouped bump is still proof the token
    # resolves — so it must count here even though it does not count as a bump of this pin.
    feed_prs: list


def _gh_api(path: str, token: str):
    req = urllib.request.Request(
        f"{GITHUB_API}{path}",
        headers={
            "Authorization": f"Bearer {token}",
            "Accept": "application/vnd.github+json",
            "User-Agent": "fsgg-check-pin-coherence",
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except (urllib.error.URLError, OSError, ValueError) as e:
        # Fails CLOSED. An unreadable API is "I could not check", and this gate must never let that
        # wear the same green as "I checked". The caller turns this into a GateError.
        raise GateError(
            f"cannot read {path} from the GitHub API ({e}). The bot-mechanism check needs it to tell "
            f"a BLIND bot from a merely-late one, and a check that cannot see its subject must not "
            f"report green (epic #266). Ensure the job grants `issues: read` and `pull-requests: read`."
        ) from e


def _search_all(repo: str, token: str) -> list:
    """EVERY Renovate-authored issue/PR in the repo. Paginated — the first page is not the answer.

    The Dependency Dashboard is one OLD issue (it is created once, then edited forever), so on any
    repo with a busy bot it sinks below the first page. Reading only `per_page=100` and concluding
    "no dashboard ⇒ the bot is not running here" would be a loud, false claim on a perfectly healthy
    repo — and would report a WRONG CAUSE, which is the one thing this whole check exists to stop.
    """
    items: list = []
    for page in range(1, 11):  # search caps at 1000 results; 10 pages is that ceiling, not a guess
        q = urllib.parse.quote(f"repo:{repo} author:app/renovate", safe="")
        got = _gh_api(f"/search/issues?q={q}&per_page=100&page={page}", token)
        batch = got.get("items") or []
        items.extend(batch)
        if len(items) >= int(got.get("total_count") or 0) or not batch:
            break
    return items


def gather_bot_evidence(repo: str, token: str, dep_name: str, items=None) -> BotEvidence:
    """Read Renovate's own dashboard + PRs. Its artifacts, not our inference.

    `items` may be passed in pre-fetched: the search is dep-INDEPENDENT, so N stale pins must not
    cost N identical searches (the search API allows 30/min, and a gate that rate-limits itself into
    a 403 fails on its own redundancy).
    """
    if items is None:
        items = _search_all(repo, token)

    dashboard = next(
        (i["number"] for i in items if str(i.get("title", "")).strip() == DASHBOARD_TITLE), None
    )
    if dashboard is None:
        raise GateError(
            f"Renovate has opened no {DASHBOARD_TITLE!r} issue in {repo}, so there is no record of "
            f"what it detected. The org preset enables `:dependencyDashboard`, so its absence means "
            f"the bot is not running here at all — which is a bigger finding than a stale pin, and "
            f"not something this gate may paper over by reporting green."
        )

    body = str(_gh_api(f"/repos/{repo}/issues/{dashboard}", token).get("body") or "")
    detected = dep_name.lower() in body.lower()

    needle = dep_name.lower()
    prs = [i for i in items if i.get("pull_request")]
    bump_prs = [
        (i["number"], i.get("state", "?"), i.get("title", ""))
        for i in prs
        if needle in str(i.get("title", "")).lower()
    ]
    # Any FS.GG.* bump at all — including a GROUPED one, whose title names the group and not the
    # member. See BotEvidence.feed_prs: this is the feed-access signal, not the per-pin one.
    feed_prs = [
        (i["number"], i.get("state", "?"), i.get("title", ""))
        for i in prs
        if "fs.gg" in str(i.get("title", "")).lower()
    ]
    return BotEvidence(
        detected=detected, bump_prs=bump_prs, dashboard=dashboard, feed_prs=feed_prs
    )


def diagnose_stale_pin(pin: Pin, top: str, ev: BotEvidence) -> str:
    """WHY is the pin stale? Answered from evidence, never asserted."""
    where = f"{pin.file}:{pin.line}: {pin.dep_name} is pinned at {pin.current_value!r} but the org feed's newest is {top!r}."

    if not ev.detected:
        return (
            f"{where} Renovate's Dependency Dashboard (#{ev.dashboard}) does NOT list this "
            f"dependency, so the annotation manager's regex is no longer matching it — the bot is not "
            f"even looking at this pin. Fix the manager/annotation; advancing the literal would leave "
            f"the pin unmanaged and it would freeze again on the next release."
        )

    if ev.bump_prs:
        prs = ", ".join(f"#{n} ({s})" for n, s, _ in ev.bump_prs)
        return (
            f"{where} The bot IS working — it detected the dependency and opened {prs}. This is the "
            f"BENIGN case: merge that PR rather than hand-editing the literal. (Do not 'fix' this by "
            f"advancing the pin; you would close a bump PR the bot will simply reopen.)"
        )

    if ev.feed_prs:
        # It opened NO PR for this dep — but it HAS opened FS.GG.* PRs, so it can plainly reach the
        # private feed. Whatever is wrong, it is not the credential, and saying "THE BOT IS BLIND"
        # here would send someone to chase a token that works. The likeliest cause is the org
        # preset's GROUPING (`groupName: "FS.GG.UI coherent set"`), whose PR title names the group,
        # not the member.
        prs = ", ".join(f"#{n} ({s})" for n, s, _ in ev.feed_prs[:3])
        return (
            f"{where} The bot can SEE the feed — it has opened FS.GG.* bump PRs here ({prs}) — but it "
            f"opened none for {pin.dep_name} specifically. So this is NOT the #263 blind-token "
            f"failure, and the credential is not the thing to go and fix.\n"
            f"    Look instead at why THIS pin is exempt: a `groupName` packageRule in default.json "
            f"can fold it into a grouped PR whose title names the group and not the member; a "
            f"`matchPackageNames` rule can have disabled it; or the update may simply be newer than "
            f"the bot's last run. Confirm against Dependency Dashboard #{ev.dashboard} before "
            f"touching the literal."
        )

    return (
        f"{where} THE BOT IS BLIND TO THE ORG FEED — do NOT hand-bump this literal.\n"
        f"    Evidence: Renovate's Dependency Dashboard (#{ev.dashboard}) DOES list {pin.dep_name} "
        f"(so the manager's regex matches and the bot knows the pin exists), the feed DOES serve "
        f"{top!r}, and Renovate has opened NO bump PR for it — nor for ANY other FS.GG.* package, "
        f"ever, grouped or otherwise. Third-party bumps (nuget.org, no auth) it opens fine. A bot "
        f"that can see the dependency, and can see nothing to update, is a bot whose feed lookup "
        f"returned nothing — i.e. it 401'd on {FEED_HOST}.\n"
        f"    `renovate.json` carries the `hostRules` block (that was #263's fix), so the CONFIG is "
        f"right and the CREDENTIAL is not: `{{{{ secrets.FSGG_PACKAGES_READ_TOKEN }}}}` is a Mend App "
        f"Secret, and nothing in CI can read it — which is exactly why nobody has ever verified it. "
        f"Set/repair that secret in the Mend dashboard for this repo.\n"
        f"    Advancing the literal is what was done in .github#127 and .github#263, and the pin "
        f"froze again both times. It is the paper-over, not the fix."
    )


def _resolve_newest(pin: Pin, resolve) -> str:
    if pin.datasource != "nuget":
        raise GateError(
            f"datasource {pin.datasource!r} is not one this gate can resolve. It must not guess a "
            f"feed — extend the gate, or the pin goes unchecked."
        )
    if not pin.dep_name.startswith("FS.GG."):
        raise GateError(
            f"{pin.dep_name!r} is not an FS.GG.* package, so it does not resolve from the org feed. "
            f"This gate only reads that feed — extend it rather than reporting green."
        )
    return newest(resolve(pin.dep_name))


def check_pin(pin: Pin, resolve, evidence) -> str | None:
    """None if the pin is at feed-newest, else the reason it is not — WITH the cause, evidenced.

    `evidence` is a callable (dep_name -> BotEvidence). It is consulted only when a pin is actually
    stale, so the happy path costs no extra API calls.
    """
    top = _resolve_newest(pin, resolve)
    have, want = parse_version(pin.current_value), parse_version(top)
    if have == want:
        return None
    if have < want:
        # The old code asserted "the annotation manager did not bump it" here — a cause it never
        # verified (.github#566). Go and look instead.
        return diagnose_stale_pin(pin, top, evidence(pin.dep_name))
    return (
        f"{pin.file}:{pin.line}: {pin.dep_name} is pinned at {pin.current_value!r}, which is AHEAD "
        f"of the org feed's newest {top!r}. The pin names a version no consumer can restore."
    )


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    ap.add_argument("--root", default=".", help="repo root to scan (default: cwd)")
    ap.add_argument("--preset", help="path to the org Renovate preset (default: <root>/default.json)")
    ap.add_argument("--fixture", help="read the feed from a JSON file (tests only, never in CI)")
    ap.add_argument(
        "--repo",
        help="owner/repo whose Renovate dashboard + PRs prove the bot can see the feed "
        "(default: $GITHUB_REPOSITORY)",
    )
    args = ap.parse_args(argv)

    root = args.root
    preset = args.preset or os.path.join(root, "default.json")

    if args.fixture:
        # A flag that makes the gate report green without reading the feed is precisely the
        # fails-open shape epic #266 is about, so it is locked rather than merely documented.
        if os.environ.get("FSGG_PIN_FIXTURE_OK") != "1":
            print(
                "::error::check-pin-coherence: --fixture reads a canned feed and is NOT a coherence "
                "signal. It is available only to tests/pin-coherence/, which sets "
                "FSGG_PIN_FIXTURE_OK=1. Refusing to run.",
                file=sys.stderr,
            )
            return 1
        print(f"FIXTURE MODE — reading {args.fixture}, NOT the live feed. Not a coherence signal.")
        try:
            with open(args.fixture, encoding="utf-8") as fh:
                table = json.load(fh)
        except (OSError, ValueError) as e:
            print(f"::error::check-pin-coherence: cannot read fixture: {e}", file=sys.stderr)
            return 1

        # A fixture feed may carry a canned `_renovate` block so the harness can exercise the
        # bot-evidence legs (blind / benign / manager-broke) with no network. Absent it, the gate is
        # explicit that the cause is UNVERIFIED rather than quietly asserting one — which is the
        # whole defect #566 is about, and it would be absurd to rebuild it inside the fix.
        canned = table.pop("_renovate", None)

        def resolve(pkg: str) -> list[str]:
            if pkg not in table:
                raise GateError(f"package {pkg!r} is not on the org feed (fixture: absent)")
            if not table[pkg]:
                raise GateError(f"the feed served zero versions for {pkg!r}")
            return list(table[pkg])

        def evidence(dep: str) -> BotEvidence:
            if canned is None:
                raise GateError(
                    f"{dep} is stale, and this fixture carries no `_renovate` block, so the CAUSE is "
                    f"UNVERIFIED. The gate will not name one it did not check (#566)."
                )
            e = canned.get(dep, canned)
            bump = [tuple(p) for p in (e.get("bump_prs") or [])]
            # A bump PR for THIS dep is also, trivially, an FS.GG.* PR — so a fixture that names one
            # without restating it under feed_prs must not read as "the bot never touched the feed".
            return BotEvidence(
                detected=bool(e.get("detected")),
                bump_prs=bump,
                dashboard=e.get("dashboard"),
                feed_prs=[tuple(p) for p in (e.get("feed_prs") or [])] or bump,
            )
    else:
        token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN") or ""
        if not token:
            print(
                "::error::check-pin-coherence: no GITHUB_TOKEN/GH_TOKEN in the environment. The org "
                "feed cannot be read without one, and an unreadable feed must fail the gate, not "
                "skip it.",
                file=sys.stderr,
            )
            return 1

        repo = args.repo or os.environ.get("GITHUB_REPOSITORY") or ""
        if not repo:
            print(
                "::error::check-pin-coherence: no --repo and no GITHUB_REPOSITORY. The gate needs to "
                "know which repo's Renovate dashboard and PRs to read in order to tell a BLIND bot "
                "from a merely-late one (#566). It will not guess, and it will not skip the check.",
                file=sys.stderr,
            )
            return 1

        def resolve(pkg: str) -> list[str]:
            return feed_versions(pkg, token)

        _items: list = []  # the dep-independent search, fetched at most once per run

        def evidence(dep: str) -> BotEvidence:
            if not _items:
                _items.extend(_search_all(repo, token))
            return gather_bot_evidence(repo, token, dep, items=_items)

    try:
        config_path = check_bump_mechanism(root)
        # Deliberately NOT "pins can be bumped" — that was the overclaim (#566). Presence of the
        # block is not resolution of the token, and the gate has no way to prove the latter from
        # here. If a pin turns out to be stale, diagnose_stale_pin() goes and gets the evidence.
        print(
            f"ok: {config_path} declares the {FEED_HOST} hostRules token. NOTE: this proves the "
            f"block is PRESENT, not that the token RESOLVES — the bot's behaviour is checked below, "
            f"and only if a pin is actually stale."
        )

        regexes, matches_path = load_annotation_manager(preset)
        pins = scan_pins(root, regexes, matches_path)
        if not pins:
            raise GateError(
                "the org preset's annotation manager matched ZERO pins in this repo. Either every "
                "annotated pin was removed, or the manager's regex/managerFilePatterns stopped "
                "seeing them — in which case the bot is silently bumping nothing. A gate with no "
                "subject must not report green."
            )
        assert_required_pins(pins)
    except GateError as e:
        print(f"::error::check-pin-coherence: {e}", file=sys.stderr)
        return 1

    print(f"comparing {len(pins)} annotated pin(s) against the {FEED_HOST}/FS-GG feed:")
    problems: list[str] = []
    for pin in sorted(pins):
        try:
            problem = check_pin(pin, resolve, evidence)
        except GateError as e:
            problems.append(f"{pin.file}:{pin.line}: {e}")
            continue
        if problem:
            problems.append(problem)
        else:
            print(f"  ok   {pin.file}:{pin.line:<4} {pin.dep_name:24} == {pin.current_value}")

    if problems:
        print()
        for p in problems:
            # `::error::` is a LINE-ORIENTED workflow command: GitHub ends the annotation at the
            # first newline, so a multi-line diagnosis would lose every line after the first — and
            # the lost lines are the evidence, which is the entire point (#566). Newlines must be
            # sent as the %0A escape to survive into the annotation. Print the readable form to the
            # log too, since the log is where anyone reading a failed run actually looks.
            print(f"::error::check-pin-coherence: {p.replace('%', '%25').replace(chr(13), '')
                                                    .replace(chr(10), '%0A')}", file=sys.stderr)
            print(f"check-pin-coherence: {p}", file=sys.stderr)
        print(f"\ncheck-pin-coherence: {len(problems)} problem(s).", file=sys.stderr)
        return 1

    print("\nok: every annotated pin equals the newest version live on the org feed.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
