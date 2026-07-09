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

WHAT IT ASSERTS. Two things, which together mean "this pin can move, and it has moved":

  1. FRESHNESS. For every annotated pin, the literal equals the newest version live on the org feed.
  2. MECHANISM. This repo's own Renovate config carries the `hostRules` feed token, without which
     Renovate cannot enumerate FS.GG.* versions and therefore cannot bump ANY of these pins.

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

# Renovate reads the first config file it finds, in this order. `.github` keeps its config at the
# repo root; the product repos keep theirs under `.github/`. Both are valid, so look for both —
# and finding NONE is an error, not a skip (a repo with pins and no config cannot bump them).
RENOVATE_CONFIG_NAMES = (
    "renovate.json",
    "renovate.json5",
    ".renovaterc",
    ".renovaterc.json",
    ".github/renovate.json",
    ".github/renovate.json5",
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


def check_pin(pin: Pin, resolve) -> str | None:
    """None if the pin is at feed-newest, else the reason it is not."""
    top = _resolve_newest(pin, resolve)
    have, want = parse_version(pin.current_value), parse_version(top)
    if have == want:
        return None
    if have < want:
        return (
            f"{pin.file}:{pin.line}: {pin.dep_name} is pinned at {pin.current_value!r} but the org "
            f"feed's newest is {top!r}. The pin has frozen — the annotation manager did not bump it "
            f"(.github#127 / .github#263). Advance the literal, and check the bot can see the feed."
        )
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

        def resolve(pkg: str) -> list[str]:
            if pkg not in table:
                raise GateError(f"package {pkg!r} is not on the org feed (fixture: absent)")
            if not table[pkg]:
                raise GateError(f"the feed served zero versions for {pkg!r}")
            return list(table[pkg])
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

        def resolve(pkg: str) -> list[str]:
            return feed_versions(pkg, token)

    try:
        config_path = check_bump_mechanism(root)
        print(f"ok: {config_path} carries the {FEED_HOST} hostRules token (pins can be bumped).")

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
            problem = check_pin(pin, resolve)
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
            print(f"::error::check-pin-coherence: {p}", file=sys.stderr)
        print(f"\ncheck-pin-coherence: {len(problems)} problem(s).", file=sys.stderr)
        return 1

    print("\nok: every annotated pin equals the newest version live on the org feed.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
