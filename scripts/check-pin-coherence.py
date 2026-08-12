#!/usr/bin/env python3
"""Gate every Renovate-annotated version pin against the registry Renovate reads (#263, #576, epic #266).

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

  1. FRESHNESS. For every annotated pin, the literal equals the newest version on the registry
     Renovate resolves it from (nuget.org — see PUBLIC_HOSTS/#576), not merely on some feed.
  2. MECHANISM (routing). Every host the org preset routes FS.GG.* to is one Renovate can actually
     READ — a public registry (no credential), or an auth-required one for which THIS repo's own
     config carries a `hostRules` token. A routing the bot cannot read means no pin can ever bump.
  3. MECHANISM (behaviour) — .github#566. When a pin IS stale, the gate goes and finds out WHY,
     from Renovate's own artifacts, instead of asserting a cause. It discriminates:

       * the dep is missing from Renovate's Dependency Dashboard  -> the manager's regex broke;
       * the dep is detected AND a bump PR exists                 -> benign, merge that PR;
       * the dep is detected, the registry has a newer version,   -> THE BOT IS BLIND. What to do
         and Renovate opened no PR — ever                            about it depends on the ROUTE,
                                                                     and the gate says which (below).
  4. MECHANISM (scheme) — .github#576/#1122. Every pin's literal must be a SINGLE version under the
     versioning scheme Renovate resolves for it, not an open-ended `>=` range. This is the blind spot
     that hid #576 for four rounds: under `nuget` versioning a bare literal `0.10.0` is `>=0.10.0` —
     satisfied by every newer release — so the bot proposes nothing, while the pin sits at exactly
     newest and passes (1), (2) and (3) green. The annotation manager's versioningTemplate DEFAULT is
     now `loose` (#1135, the sibling of #1131's FsGgUiVersion fix), so a bare literal at the default is
     SINGLE and bumpable; the range this gate reds is now reachable only by an explicit
     `versioning=nuget`. The gate resolves the scheme (the manager's template, rendered with any
     `versioning=` capture) and reds a literal that is a range: "this pin can never bump".
     `versioning=loose` was the per-pin fix (#1119) and is now belt-and-suspenders over the loose
     default; this keeps the check itself from being dropped. isSingleVersion is a transcription of
     renovate's own, driven not reasoned (see the block above check_pin_bumpable).

THE #576 CORRECTION — READ THIS BEFORE YOU GO LOOKING FOR A TOKEN.

This gate used to assert (2) as "renovate.json declares a hostRules token for nuget.pkg.github.com",
on the premise, written into the config itself, that `FS.GG.* are not on nuget.org`. THE PREMISE WAS
FALSE. All 32 of the 32 packages the org publishes to GitHub Packages are ALSO public on nuget.org,
anonymously readable, at the same latest version. Routing FS.GG.* to the org feed forced every
Renovate lookup through the one host that needs a credential — a Mend App Secret nothing in CI can
read or verify — those lookups 401'd, and a 401 on a datasource is not an error, it is an EMPTY
VERSION LIST. The bot saw the dep, enumerated nothing, and opened no PR.

So the FS.GG.SDD.Cli pin froze four times — 0.2.1 (#127), 0.5.0 (#263), 0.9.0 (#566), 0.10.0 — and
each time it was closed by hand-advancing the literal and chasing the credential. The fix was to
stop routing FS.GG.* to an auth-required host at all (default.json now routes to nuget.org), after
which no credential is in the path and there is nothing left that can silently fail.

The tell, for the record, because it is the thing that ought to have been noticed years earlier:
`matchPackageNames` regexes are CASE-SENSITIVE. The coordination tool is pinned by its LOWERCASE id
`fs.gg.coord.cli`, so it MISSED the routing rule, escaped the override, fell back to nuget.org, and
bumped fine (#660) — while properly-cased `FS.GG.SDD.Cli` matched, was forced onto the dead feed,
and froze. One package bumping and another freezing, from the same config, is not a credential
problem. And note what that did to THIS gate: #660 populated `feed_prs`, so the blind-detector read
the one accidental escape as proof the feed was reachable and refused to diagnose blindness at all.

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
  * this repo's Renovate config is absent;
  * the preset routes FS.GG.* to an auth-required host and this repo declares no token for it;
  * the preset routes FS.GG.* to a host this gate cannot classify (it must not guess that a host it
    has never heard of is readable);
  * the annotation manager declares no `versioningTemplate`, or one this gate cannot render, so the
    scheme a pin resolves to is unknown (#1122);
  * a pin resolves to a versioning scheme this gate has not verified against renovate's own
    `isSingleVersion` — an unverified scheme must not read as "single version, fine" (#1122).

Comparison is by NuGet version ORDER, never by substring — the .github#268 defect class, where
`0.4.0` matches inside `0.4.0-preview.1`. Ordering and feed reads are shared with
scripts/check-feed-coherence.py via scripts/fsgg_feed.py, so the two gates cannot drift.

Usage:  scripts/check-pin-coherence.py [--root .]

`--fixture <feed.json>` serves a canned feed instead of the live one. It is NOT a coherence signal,
and it refuses to run unless FSGG_PIN_FIXTURE_OK=1 — which only tests/pin-coherence/ sets. A test
hook that can silently turn the gate into a no-op is the very defect class above.

Exit 0 = every annotated pin is at registry-newest, and the bot is routed so it can keep it there.
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
    is_prerelease,
    newest,
    nuget_org_nuspec,
    nuget_org_versions,
    nuspec_dependency_ranges,
    parse_version,
    range_admits,
)

# The shared gate harness (#1159, #1158 D2). `run()` guarantees the process exit code is a VERDICT and
# never an accident: an uncaught exception exits Python 1, which is this gate's FINDING code, so a bug
# in the gate would otherwise surface to CI as a confident wrong finding about its subject. `ExitCode`
# is the one exit-code contract. `fsgg_feed.GateError` is a DISTINCT class from `lib.gate.GateError`
# (they predate the harness and are shared with check-feed-coherence.py); this gate keeps raising the
# feed one and maps it to a no-verdict code in `main`, so `run()` only has to backstop a genuine crash.
from lib.gate import ExitCode, run  # noqa: E402  (path shim above must run first)

# Hosts a Renovate `registryUrls` may route FS.GG.* to, and what each one COSTS to read.
#
# This used to be a single constant, FEED_HOST = "nuget.pkg.github.com", carrying the comment "the
# host every FS.GG.* package resolves from". That was false, and the falsehood is the whole of
# .github#576: every FS.GG.* package is ALSO public on nuget.org — all 32 of the 32 ids the org
# publishes, at the same latest version. Routing them to the org feed forced every Renovate lookup
# through the one host needing a credential (a Mend App Secret no CI job can read or verify), the
# lookups 401'd, and a 401 on a datasource is not an error — it is an empty version list. So the
# bot detected the dep, enumerated nothing, opened no PR, and the FS.GG.SDD.Cli pin froze four
# times (#127 at 0.2.1, #263 at 0.5.0, #566 at 0.9.0, and again at 0.10.0), each hand-advanced.
#
# The gate must therefore stop asserting "a token is present for THE feed" — a declaration — and
# assert the thing that actually has to be true: EVERY HOST THE PRESET ROUTES FS.GG.* TO MUST BE
# ONE RENOVATE CAN READ. A public host needs no token. An auth-required host needs a `hostRules`
# token in THIS repo's own config (Renovate does not substitute {{ secrets }} inside a preset
# pulled via `extends`), which is the #263 protection and is preserved below, not deleted.
PUBLIC_HOSTS = frozenset({"api.nuget.org"})
AUTH_HOSTS = frozenset({"nuget.pkg.github.com"})

# The hosts the preset was ACTUALLY found to route to on this run, filled in by check_bump_mechanism
# before any pin is judged. diagnose_stale_pin() reads it, because the right remediation for a blind
# bot depends entirely on whether a credential is even in the path: with an auth host, "the token
# never resolved" is the live hypothesis; with a public one it is not available as an excuse, and
# saying it anyway would be naming a cause the gate did not check (#566).
ROUTED_HOSTS: list[str] = []

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
    versioning: str | None  # the annotation's own `versioning=<v>` capture, or None (→ manager default)
    value_line: int = 0     # the line the `currentValue` was CAPTURED on — the annotation line + the
    #                         manager's small look-ahead window; equal to `line` only when the pin sits
    #                         on the annotation line itself.
    from_comment: bool = False  # True when that captured line is a COMMENT, not a pin literal — the
    #                             annotation has drifted from its pin and the manager is reading a
    #                             version-shaped string out of prose (#1236).


# The comment marker per file type the annotation manager scans (its managerFilePatterns): `#` for
# the line-comment families, `<!-- -->` for the XML families. JSON has no comments, so a capture
# there is never prose. Kept conservative on purpose: a real pin literal is NEVER on a comment-only
# line, so "the captured line is a comment" has no false positives — and a gate that cried wolf on a
# good pin would teach exactly the "reds are noise" habit the #1236 class needs broken.
def _capture_from_comment(rel: str, text: str, value_start: int) -> bool:
    ext = os.path.splitext(rel)[1].lower()
    line_start = text.rfind("\n", 0, value_start) + 1
    if ext in (".yml", ".yaml", ".sh"):
        return text[line_start:value_start].lstrip().startswith("#")
    if ext in (".props", ".targets", ".fsproj"):
        # inside an XML comment iff the nearest `<!--` before the capture is not yet closed by a `-->`
        return text.rfind("<!--", 0, value_start) > text.rfind("-->", 0, value_start)
    return False  # .json and anything else: no line-comment syntax to hide a phantom in


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


def load_annotation_manager(config_path: str) -> tuple[list[re.Pattern], object, str]:
    """The org preset's annotation-driven custom manager: its regexes, file matcher, and versioningTemplate.

    Identified structurally — the manager whose matchStrings capture both `depName` and
    `currentValue` — rather than by its description, which is prose and may be reworded.

    The `versioningTemplate` is returned because the SCHEME Renovate applies to a pin is what
    decides whether its literal is a single, bumpable version or an open-ended `>=` range — the
    exact #576 blind spot. A manager that declares no versioningTemplate is refused rather than
    guessed at: the gate cannot make the single-version assertion without knowing the scheme, and
    "I could not tell the scheme" must not wear the same green as "the pin is fine" (epic #266).
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
            template = m.get("versioningTemplate")
            if not isinstance(template, str) or not template.strip():
                raise GateError(
                    f"{config_path}'s annotation-driven custom manager declares no "
                    f"`versioningTemplate`, so the gate cannot tell which versioning scheme Renovate "
                    f"applies to its pins — and that scheme is what decides whether a bare literal is "
                    f"a single, bumpable version or a `>=` floor that never bumps (#576). The gate "
                    f"will not guess: declare a versioningTemplate (Renovate defaults an unset one to "
                    f"the datasource's scheme — for nuget that is `nuget`, which is the very default "
                    f"that froze the SDD.Cli pin)."
                )
            return (
                [_to_python_regex(s) for s in strings],
                _file_matcher(m.get("managerFilePatterns") or []),
                template.strip(),
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
                # `versioning` is OPTIONAL in the manager's matchString, so an absent capture is a
                # legitimate None (→ manager default), NOT the empty string. Keep the two apart:
                # `versioning=` written empty would be a different, broken, thing from omitting it.
                versioning = g.get("versioning")
                value_start = m.start("currentValue")
                pins.append(
                    Pin(
                        file=rel,
                        line=text.count("\n", 0, m.start()) + 1,
                        datasource=(g.get("datasource") or "").strip(),
                        dep_name=(g.get("depName") or "").strip(),
                        current_value=(g.get("currentValue") or "").strip(),
                        versioning=versioning.strip() if versioning is not None else None,
                        value_line=text.count("\n", 0, value_start) + 1,
                        from_comment=_capture_from_comment(rel, text, value_start),
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


def prose_capture_problem(pin: Pin) -> str | None:
    """A FINDING if the pin's version was captured from a COMMENT rather than a pin literal.

    The org preset's custom manager captures the version a line or two below the `# renovate:`
    annotation. When the annotation drifts away from its pin — an explanatory comment slips in
    between — the manager's look-ahead lands on that comment and reads a version-shaped string out
    of the PROSE instead of the pin. Renovate and this gate then both track the phantom: a bump PR
    would rewrite the comment while the real pin freezes silently. That is the exact mechanism of
    #1236, and every symptom of the recurring freeze family (#263/#576/#1121) it belongs to.

    This is a FINDING, not a no-verdict: the repo is misconfigured whether or not the feed is
    reachable, and it is checked BEFORE the feed comparison so the phantom cannot masquerade as a
    merely-stale pin with a "merge the bump PR" remedy — the bump PR here targets the comment.
    """
    if not pin.from_comment:
        return None
    return (
        f"{pin.file}:{pin.line}: the `# renovate:` annotation for {pin.dep_name} captured its "
        f"version from a COMMENT at line {pin.value_line} (`{pin.current_value}`), not from the pin "
        f"literal it is meant to bump. The manager captures the version a line or two below the "
        f"annotation, so a comment carrying a version-shaped string in that gap SHADOWS the real "
        f"pin — Renovate and this gate both read the phantom, and a bump PR would rewrite the "
        f"comment while the pin freezes (#1236). Move the annotation to sit immediately above the "
        f"pin, with NO comment between the two."
    )


def routed_hosts(preset_path: str) -> list[str]:
    """The hosts the org preset routes FS.GG.* to, via the `registryUrls` of its FS.GG.* rule.

    An empty `registryUrls` is NOT an error: with no override, Renovate uses its default nuget
    registry, which is nuget.org — the public one. That is a routing this gate is happy with, so it
    is reported as such rather than guessed at.
    """
    try:
        with open(preset_path, encoding="utf-8") as fh:
            preset = json.load(fh)
    except OSError as e:
        raise GateError(f"cannot read the org Renovate preset {preset_path!r}: {e}") from e
    except ValueError as e:
        raise GateError(f"the org Renovate preset {preset_path!r} is not valid JSON: {e}") from e

    hosts: list[str] = []
    for rule in preset.get("packageRules") or []:
        if not isinstance(rule, dict):
            continue
        urls = rule.get("registryUrls") or []
        if not urls:
            continue

        names = [str(n) for n in (rule.get("matchPackageNames") or [])]
        datasources = [str(d) for d in (rule.get("matchDatasources") or [])]

        # Does this rule's routing reach FS.GG.* ? TWO ways it can, and missing either is a hole:
        #
        #  (a) it NAMES FS.GG — the obvious one. Matched loosely (substring, not by compiling the
        #      regex), because the rule's own regex is part of what this gate is checking and must
        #      not be the thing the gate trusts to find it.
        #
        #  (b) it names NO packages at all — in which case it applies to EVERY package of its
        #      datasource, FS.GG.* included. An unnamed nuget-wide `registryUrls` silently reroutes
        #      the whole org, and a gate that only looked for (a) would report green over it while
        #      the bot went blind. That is the #266 fails-open shape rebuilt inside the fix for it,
        #      so it is closed here rather than left to be discovered a fifth time.
        names_fsgg = any("FS\\.GG" in n or "FS.GG" in n for n in names)
        applies_to_all_nuget = not names and (not datasources or "nuget" in datasources)
        if not (names_fsgg or applies_to_all_nuget):
            continue

        for url in urls:
            host = urllib.parse.urlparse(str(url)).hostname
            if host and host not in hosts:
                hosts.append(host)

    # No override at all => Renovate's default nuget registry, which IS nuget.org. That is a fact
    # about Renovate, not a guess about this preset, so it is safe to state rather than fail on.
    return hosts or ["api.nuget.org"]


SECRET_TEMPLATE = re.compile(r"\{\{\s*secrets\.")


def assert_no_stray_secret_templates(cfg: dict, config_path: str) -> None:
    """A `{{ secrets.X }}` template is only ever legitimate as a hostRules token VALUE.

    Renovate interpolates `{{ }}` in EVERY config value — `description` prose included. Prose in a
    Renovate config is not inert; it is a template. A `{{ secrets.X }}` sitting in a description
    therefore either (a) fails config-validation with "Unknown secrets name" and takes the WHOLE
    repo config down, so Renovate silently does nothing at all, or (b) interpolates a live secret
    into a string that is not a credential.

    This is not hypothetical. The #576 fix removed the hostRules token and wrote an explanation of
    WHY into `description` — quoting the very template it had just deleted. The explanation
    re-introduced a worse version of the bug it was explaining, and `renovate-config-validator`
    reported the config as valid, because it does not interpolate secrets. Only running Renovate
    caught it. Hence this check: the declaration is not the subject, and a validator that never
    interpolates cannot see an interpolation defect.
    """
    offenders: list[str] = []

    def walk(node, path: str) -> None:
        if isinstance(node, dict):
            for k, v in node.items():
                walk(v, f"{path}.{k}" if path else str(k))
        elif isinstance(node, list):
            for i, v in enumerate(node):
                walk(v, f"{path}[{i}]")
        elif isinstance(node, str) and SECRET_TEMPLATE.search(node):
            # The one legitimate home: hostRules[N].token
            if re.fullmatch(r"hostRules\[\d+\]\.token", path):
                return
            offenders.append(path)

    walk(cfg, "")
    if offenders:
        raise GateError(
            f"{config_path} contains a `{{{{ secrets.* }}}}` template outside a hostRules token, at: "
            f"{', '.join(offenders)}. Renovate interpolates {{{{ }}}} in EVERY config value — "
            f"`description` prose included — so this is not documentation, it is an interpolation "
            f"target. It will either fail config-validation with 'Unknown secrets name' (taking the "
            f"whole repo config down, so Renovate silently does NOTHING), or splice a live secret "
            f"into a field that is not a credential. Name the secret WITHOUT the braces."
        )


# The receiver-side path of every file the AUTHORITY syncs byte-for-byte into a receiver's tree. A
# receiver's copy is SYNCED, not authored (ADR-0006, sync-not-fork), and the build-config drift gate
# — a REQUIRED check in adopting repos — fails any PR that changes one. So Renovate must be disabled
# against these paths IN RECEIVERS: a bump is a PR that structurally cannot merge, forever (#794).
#
# `.config/dotnet-tools.json` WAS THE FIRST ENTRY HERE AND IS NOT ANY MORE (#1798, ADR-0068). It was
# correct for as long as it was true: #1077 made the engine tool manifest a `kind: config` kit row,
# so all seven coordination-kit receivers held a materialized copy. #1615 took it back OFF the kit —
# each receiver now OWNS its manifest, coordination-sync compares it against nothing, and the ONLY
# delivery path the engine pin has left is a Renovate bump in the receiver's own file. Keeping it
# here did not merely become pointless: this tuple is what `assert_synced_files_unmanaged` REQUIRES a
# `matchFileNames` + `enabled: false` preset rule for, so its presence here was actively holding the
# preset rule that switched ADR-0068's delivery mechanism off in all seven receivers.
#
# THIS TUPLE IS DERIVED, NOT DECIDED — `assert_synced_list_is_complete` reds unless it equals its
# owners' union — so the way to change it is to change an owner, and the way to re-add the manifest
# is to make it an authority-synced file again.
SYNCED_RECEIVER_FILES = (
    "Directory.Build.props",
    "Directory.Packages.props",
)

# THE OTHER ROLE THE TUPLE ABOVE USED TO PLAY, AND IT DOES NOT FOLLOW FROM IT (#1798).
#
# `SYNCED_SOURCE_PATHS` below was `dist/dotnet/` + each synced file, and it is what `_offence()`
# tests `ignorePaths` entries against. That fused two different claims onto one list:
#
#   (A) "this file is synced INTO receivers, so Renovate must be disabled there" — the tuple above;
#   (B) "this repo's own copy under dist/dotnet/ is MANAGED and an ignorePaths substring must never
#       reach it" — which is a fact about THIS repo and has nothing to do with syncing.
#
# `dist/dotnet/.config/dotnet-tools.json` left (A) and did NOT leave (B): ADR-0068 says in as many
# words that it "REMAINS in this repo as `.github`'s own canonical manifest", it is
# `engine-pin-coherence`'s subject, and it is #660 — the one FS.GG.* bump PR Renovate has ever opened
# here. Had (B) been left derived from (A), removing the manifest from the synced set would have
# silently withdrawn the #678 substring protection from the very baseline #678 was written to
# protect, in the same commit that made that baseline the fleet's only delivery path. That is the
# failure this split exists to make impossible, and it is why (B) is derived from the TREE — every
# package file actually present under dist/dotnet/ — rather than from (A).
BASELINE_DIR = "dist/dotnet"

# ---------------------------------------------------------------------------------------------------
# THE SURFACE THIS GATE READS (#1802). `check-paths-coherence.py` rule (c) folds this by AST and reds
# `pin-coherence.yml` if its `paths:` filter does not select every entry — so widening what we read
# widens the filter that decides whether we run at all.
#
# WHY IT EXISTS. #1615's roster edit touched `registry/repos.yml`, which this gate READS and the
# workflow filter did not list, so the gate went red on a tree whose change could not re-run it. A
# hand-kept filter beside a hand-kept read-set is two lists that agree only while someone remembers,
# and they had stopped: FOUR reads were missing from the filter, not the one #1802 was filed about —
# `registry/repos.yml`, `scripts/sync-build-config.sh`, `dist/dotnet/**`, and every `renovate.*` name
# other than `renovate.json`.
#
# TWO ENTRIES ARE SPELLED LITERALLY RATHER THAN COMPOSED, and that is forced: the folder adds
# SEQUENCES and cannot evaluate `BASELINE_DIR + "/**"` — a string concatenation folds to two
# non-sequences and the gate REFUSES (exit 3, measured while writing this). Where the folder makes
# composition unwritable, the retype is made safe by the assertions below rather than by hoping.
PATHS_SUBJECT = (
    (
        "registry/repos.yml",
        "scripts/sync-build-config.sh",
        "default.json",
        "dist/dotnet/**",
        ".github/workflows/contract-coherence.yml",
    )
    + RENOVATE_CONFIG_NAMES
)

assert f"{BASELINE_DIR}/**" in PATHS_SUBJECT, (
    "PATHS_SUBJECT restates BASELINE_DIR as a glob and they have drifted: "
    f"BASELINE_DIR={BASELINE_DIR!r}"
)
assert {f for f, _pkg in REQUIRED_PINS} <= set(PATHS_SUBJECT), (
    "PATHS_SUBJECT must name every file REQUIRED_PINS asserts a pin in; missing: "
    f"{sorted({f for f, _pkg in REQUIRED_PINS} - set(PATHS_SUBJECT))}"
)

# Renovate's nuget manager's four SHIPPED managerFilePatterns, read out of 43.281.1's own
# dist/modules/manager/nuget/index.js `defaultConfig` rather than the docs. They decide which files
# under dist/dotnet/ Renovate can propose a bump against at all — the only ones (B) can be about, and
# the only ones a `kind: config` kit row could ever have needed a preset rule for.
#
# HARD-CODED HERE AND RE-DERIVED IN THE FIXTURE: this gate is offline Python and must not depend on
# npm, but a hand-copied list of somebody else's constant is exactly the thing that rots silently. So
# tests/preset-repo-scope-coherence/drive-package-rules.mjs reads the array out of the pinned
# renovate and reds if it has moved. The list is written by hand; forgetting it is not quiet.
_NUGET_MANAGER_FILE_RE = re.compile(
    r"(?:^|/)(?:[^/]+\.(?:cs|fs|vb|sql)proj|[^/]+\.(?:props|targets)|dotnet-tools\.json|global\.json)$"
)

# The repo the synced files are AUTHORED in. Every path above means the opposite thing here: in a
# receiver it is a synced copy Renovate must not touch, and here it is this repo's own build config
# — .github does NOT adopt the org baseline (its root Directory.Packages.props is hand-authored,
# because the baseline's FSharp.Core pin would collide, NU1506). Since .github dogfoods this preset
# (renovate.json extends github>FS-GG/.github), a rule that named those paths unconditionally would
# freeze this repo's own engine pins — FSharp.Core, Spectre.Console, xunit, FsCheck.Xunit (#739 and
# #753 are Renovate bumping the root file) — which is #576 exactly.
#
# #925 read that collision as proof the .props half was INEXPRESSIBLE in the shared preset, and
# routed it to a re-enable in this repo's own renovate.json (.github#794). It is expressible:
# Renovate's matchRegexOrGlobList reads a leading `!` as a negation, so ONE rule can apply to every
# repo except the source of truth. Measured against renovate 43.265.3's own matcher, not the docs.
SOURCE_OF_TRUTH_REPO = "FS-GG/.github"

# THIS GATE ASSERTS ONE THING ABOUT `matchRepositories`: THE AUTHORITY IS NEVER CAUGHT BY IT.
#
# It used to assert the exact literal `["!FS-GG/.github"]`, and #1552 is why that had to change. The
# negation says "every repo except the author", which is NOT the same set as "every repo that
# RECEIVES these files" — and the two stopped being equal when FS.GG.Templates, FS.GG.Audio and
# FS.GG.Net were onboarded WITHOUT build-config. The preset now carries a POSITIVE allow-list per
# fabric, derived from registry/repos.yml and gated by check-preset-repo-scope-coherence.py.
#
# THE SPLIT IS DELIBERATE, and it is #1538's rule: one red must not carry two unrelated meanings.
#   * THIS gate's subject is the ORG SOURCE OF TRUTH — that `.github`'s own authored pins and the
#     dist/dotnet/ baseline stay managed here. That is the #576/#753 freeze, and it is a fact about
#     ONE repo, checkable with no registry at all.
#   * WHICH RECEIVERS the rule must name is a fact about the ROSTER, and it belongs to the gate that
#     derives it. Re-spelling the receiver set here would put the same predicate in two files, which
#     is the "computed in N places, agrees in N-1 at best" disease (#485) — and it is exactly how
#     this gate came to block #1552's fix while the fix was correct.
#
# So the assertion below is a PREDICATE, not a literal: every entry must be a plain positive
# `owner/repo`, and `FS-GG/.github` must not be among them. That refuses each way the authority gets
# caught — an absent key (the rule applies everywhere, including here), a `*` or other glob that
# matches everything, a `!`-negation of some OTHER repo (which matches `.github`), and the authority
# listed outright — without pretending to know the roster.
#
# The repo half admits a LEADING DOT, because `.github` is a real repository name — this repo's. It
# must be spellable here so that naming the authority outright is caught by the authority check and
# reported as "you named the source of truth", rather than by the shape check as "that is not a
# plain repo name". A finding that names the wrong cause is barely better than no finding.
_AUTHORITY_SAFE_ENTRY = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]*/[A-Za-z0-9.][A-Za-z0-9._-]*$")

def baseline_managed_paths(root: str) -> tuple:
    """Every package file this repo keeps under dist/dotnet/ — role (B) above, derived from the TREE.

    These are the copies that live HERE and must stay MANAGED, so that Renovate keeps bumping the
    baseline (#660, #677). This used to be `dist/dotnet/` + SYNCED_RECEIVER_FILES, which made it a
    COROLLARY of the synced set; #1798 is what that cost. When ADR-0068 took the engine tool manifest
    off the kit, the manifest correctly left the synced set — and would have taken its own baseline
    copy's substring protection with it, in the same change that made that baseline the fleet's only
    delivery path for the engine pin. The two facts are independent, so they are read independently.

    Reading the tree rather than a list is the same move `assert_synced_list_is_complete` makes for
    the other role, and for the same reason (#902's census rot): add a package file to dist/dotnet/
    and it is protected without an edit here. Filtered by the nuget manager's own file patterns
    because an `ignorePaths` entry reaching a file no manager extracts costs nothing — the gate must
    report what Renovate would actually stop bumping, not every path that happens to be in there.

    Fail CLOSED. `dist/dotnet/` is this repo's baseline and cannot legitimately be empty of package
    files; "I could not find the baseline" must never read as "the baseline is safe" (#266).
    """
    base = os.path.join(root, BASELINE_DIR)
    found: list[str] = []
    for dirpath, _dirnames, filenames in os.walk(base):
        for name in filenames:
            rel = os.path.relpath(os.path.join(dirpath, name), root).replace(os.sep, "/")
            if _NUGET_MANAGER_FILE_RE.search(rel):
                found.append(rel)
    if not found:
        raise GateError(
            f"{BASELINE_DIR}/ holds no file Renovate's nuget manager would extract. That directory is "
            f"this repo's dependency BASELINE — the source of truth every receiver's build config is "
            f"materialized from, and the one place Renovate actually opens FS.GG.* bumps (#660). An "
            f"empty answer here is a gate that cannot see its subject, not a repo with nothing to "
            f"protect, so it refuses rather than reporting green over it (#266)."
        )
    # Sorted, so the offence message below names a stable path. `.config/dotnet-tools.json` sorts
    # first under `dist/dotnet/`, which is what the #678 legs assert the message names.
    return tuple(sorted(found))

# The only keys the disabling rule may carry. Any other key is a MATCHER, and a matcher narrows the
# rule — `matchUpdateTypes: ["major"]` would leave every minor bump proposing the un-mergeable PR
# again, with the gate still green.
#
# matchRepositories is the ONE sanctioned narrowing, and it is REQUIRED rather than merely allowed
# (see SOURCE_OF_TRUTH_REPO): without it the rule freezes this repo's own pins, and with any value
# other than the exact negation it silently un-disables a receiver. Its VALUE is asserted below —
# membership here only gets it past the "no matchers" check.
_DISABLE_RULE_KEYS = {"description", "matchFileNames", "enabled", "matchRepositories"}


def _kit_config_dests(root: str) -> set:
    """The receiver-side dest of every coordination-kit `kind: config` row (registry/repos.yml).

    A `kind: config` file is byte-synced to every kit receiver by scripts/coordination-sync (#1077),
    exactly as sync-build-config's FILES are — so a receiver's copy is authority-managed, not authored,
    and belongs in the disabled set for the very same reason. `.config/dotnet-tools.json` moved onto
    this fabric in #1077, so the synced set is now the UNION of two owners; this reads the second one
    the same narrow way FILES is read from its owner (a regex over the owning file, not a new YAML dep).

    ONLY THE ROWS RENOVATE COULD ACTUALLY BUMP, and that filter is not a detail (#1798). The premise
    of the union is "a synced copy Renovate would happily propose a bump against"; a `kind: config`
    row whose dest no Renovate manager extracts has no bump to suppress, so demanding a preset rule
    for it is demanding a rule that could never do anything. That premise held for as long as the
    kit's only `kind: config` row WAS the tool manifest. #1696 added `scripts/lib/roots.sh` and
    `scripts/lib/args.sh` — shell libraries `scripts/skill-view` sources — and this function reported
    them as files "Renovate will propose the un-mergeable PR against in every receiver, forever".
    Nothing of the kind: there is no manager for a `.sh`. That false demand took `pin-coherence` to a
    NO VERDICT on `main` from 2026-07-28T09:22Z (run 30346280541) — a gate blinded, on the day the
    real drift landed underneath it, by insisting on a rule for two files that never needed one.
    """
    path = os.path.join(root, "registry", "repos.yml")
    try:
        with open(path, encoding="utf-8") as fh:
            src = fh.read()
    except OSError as e:
        raise GateError(f"cannot read {path!r}, which owns the kit config-file set (#1077): {e}") from e
    dests = set()
    for row in re.finditer(r"^\s*-\s*\{[^}]*\bkind:\s*config\b[^}]*\}", src, re.MULTILINE):
        d = re.search(r"\bdest:\s*([^,}\s]+)", row.group(0))
        if d and _NUGET_MANAGER_FILE_RE.search(d.group(1)):
            dests.add(d.group(1))
    return dests


def assert_synced_list_is_complete(root: str) -> None:
    """SYNCED_RECEIVER_FILES must equal the files authority-synced to receivers — its DEFINITION.

    That set has two owners now (#1077): scripts/sync-build-config.sh's FILES (the two `.props`), and
    the coordination-kit's `kind: config` rows in registry/repos.yml (`.config/dotnet-tools.json`, moved
    off build-config so it reaches all six kit receivers, not build-config's four). Both are authority-
    synced to receivers, so both must be Renovate-disabled there; SYNCED_RECEIVER_FILES is the union.

    Add a file to either owner and every receiver starts carrying a synced copy Renovate would happily
    bump — the un-mergeable PR returns for that file, in every receiver, with the preset and this gate
    both green, because neither knows the file exists. That is the census rot #902 fixed in three copies
    at once: state the invariant, do not hand-maintain the roll-call. Deriving the roster from the owners
    (rather than the hand-kept tuple above) keeps ONE source of truth per owner and names drift the
    moment it appears — parsing each owner narrowly, which is the property that matters.
    """
    path = os.path.join(root, "scripts", "sync-build-config.sh")
    try:
        with open(path, encoding="utf-8") as fh:
            src = fh.read()
    except OSError as e:
        raise GateError(f"cannot read {path!r}, which defines the synced set: {e}") from e

    m = re.search(r"^FILES=\((.*?)^\)", src, re.MULTILINE | re.DOTALL)
    if not m:
        raise GateError(
            f"{path} has no FILES=( ... ) array. It is the definition of the synced set, and a gate "
            f"that cannot find it must not report green over the list it is supposed to check."
        )
    props = set(re.findall(r'"([^"]+)"', m.group(1)))
    if not props:
        raise GateError(f"{path} declares an EMPTY FILES=( ... ); refusing to report green.")
    # The full authority-synced set: build-config's FILES ∪ the coordination-kit's config dests (#1077).
    declared = props | _kit_config_dests(root)

    known = set(SYNCED_RECEIVER_FILES)
    if declared != known:
        missing = sorted(declared - known)
        extra = sorted(known - declared)
        raise GateError(
            f"the synced set has drifted from its owners (sync-build-config.sh FILES ∪ the kit's "
            f"kind:config rows, #1077): "
            + (f"an owner syncs {missing!r} that this gate does not disable — Renovate "
               f"will propose the un-mergeable PR against it in every receiver, forever. " if missing else "")
            + (f"this gate disables {extra!r} that no owner syncs any more — a receiver "
               f"authors that file now, and the preset is silently freezing it. " if extra else "")
            + f"SYNCED_RECEIVER_FILES must equal that union (.github#794, #1077)."
        )


def assert_synced_files_unmanaged(preset_path: str, root: str | None = None) -> None:
    """A synced receiver file must be disabled with matchFileNames — NEVER with ignorePaths.

    Renovate proposing a bump against a receiver's synced copy opens a PR that structurally cannot
    merge, in any receiver, ever, and re-opens it after every close (.github#678; FS.GG.Game#278 is
    the instance, closed unmerged). So the rule must exist. This asserts it, and asserts that the
    org SOURCE OF TRUTH survives it — which is the half that is easy to lose.

    #678 proposed `ignorePaths: [".config/dotnet-tools.json"]`, and that fix defeats its own stated
    intent. Renovate's filterIgnoredFiles ignores a file when

        file.includes(ignorePath) || minimatch(ignorePath, {dot:true}).match(file)

    The first branch is a SUBSTRING test, and it is the one that bites: any LITERAL path that occurs
    inside dist/dotnet/.config/dotnet-tools.json un-manages it. That is not only the spelling #678
    proposed — ".config", "dist/", ".json" and even "/" all freeze the baseline, and the shorter the
    entry the more it swallows. So this check runs in RENOVATE'S direction (`entry in source`), not
    the intuitive one (`basename in entry`): the intuitive form catches exactly the one spelling the
    fixture happens to write and waves through every worse one.

    Scope of the modelling, stated rather than implied. The substring branch is reproduced EXACTLY —
    Python's `in` is JavaScript's `String.includes`, the same operator, not an approximation of it.
    The minimatch branch is NOT reproduced: re-implementing minimatch here would be a hand-rolled
    copy of a matcher that has been wrong four times elsewhere (#724) and would drift from the bot
    the moment either changed. So entries carrying glob metacharacters are refused conservatively
    when they name a synced file, and a glob that reaches dist/dotnet/ by some spelling neither
    branch catches would pass. That is a known hole, not a claim of completeness.

    And the sentence this docstring used to carry — "ignorePaths honours neither regex nor
    anchoring" — was FALSE, which matters in the one file whose doctrine is that a false sentence
    froze the SDD.Cli pin four times (#576). Measured against renovate 43.265.2: the minimatch
    branch DOES anchor, so `[.]config/dotnet-tools.json` and `*.config/dotnet-tools.json` both
    ignore the receiver copy and correctly leave dist/dotnet/ managed. Working ignorePaths forms
    exist. They work by ESCAPING the substring branch on a glob metacharacter, which is a coincidence
    to rest a baseline on, and they are refused here for that reason — not because they cannot work.
    The regex form is the genuinely dead one: `/^\\.config\\/dotnet-tools\\.json$/` matches NOTHING
    (ignorePaths is not a regex surface), so it fails silently and looks exactly like a fix.

    Every failure mode here is invisible to renovate-config-validator — each is a perfectly valid
    config — and every one is silent. Hence a gate rather than a comment (#266).
    """
    # Split exactly as routed_hosts() does: "cannot read" and "is not valid JSON" are different
    # findings with different fixes, and this runs first, so collapsing them here would mis-name
    # every malformed-preset failure in the gate.
    try:
        with open(preset_path, encoding="utf-8") as fh:
            cfg = json.load(fh)
    except OSError as e:
        raise GateError(f"cannot read the org Renovate preset {preset_path!r}: {e}") from e
    except ValueError as e:
        raise GateError(f"the org Renovate preset {preset_path!r} is not valid JSON: {e}") from e

    # The baseline this preset must never un-manage (role (B), #1798). `default.json` lives at the
    # repo root, so the preset's own directory IS the root unless a caller says otherwise — and
    # baseline_managed_paths() refuses rather than returning an empty tuple, so a wrong root here
    # cannot degrade into "no paths to protect, therefore nothing to report" (#266).
    baseline_paths = baseline_managed_paths(root if root is not None else (os.path.dirname(preset_path) or "."))

    # (1) No ignorePaths entry may reach a managed baseline file — tested in RENOVATE'S direction.
    offenders: list[str] = []

    def _offence(entry: str) -> str | None:
        # Renovate's own substring branch, verbatim: `file.includes(ignorePath)`. Anything that
        # occurs inside a BASELINE path un-manages it, however short. Keyed on the baseline rather
        # than on the synced set (#1798): `dist/dotnet/.config/dotnet-tools.json` stopped being
        # synced when ADR-0068 took it off the kit and did NOT stop being the baseline Renovate
        # bumps here (#660) — it is now the ONLY way the engine pin enters the org at all.
        for src in baseline_paths:
            if entry in src:
                return f"it is a SUBSTRING of {src}, which Renovate would therefore stop managing"
        # The minimatch branch is not modelled (see the docstring). Refuse conservatively: an entry
        # naming one of these files is either the literal trap above or a glob that works by
        # coincidence. Both the receiver-side synced paths and the baseline's own basenames, because
        # a receiver copy and its baseline share a basename and either spelling is the same trap.
        for rel in (*SYNCED_RECEIVER_FILES, *baseline_paths):
            if os.path.basename(rel) in entry or rel in entry:
                return f"it names {rel}, whose only safe home is a matchFileNames packageRule"
        return None

    def walk(node, path: str) -> None:
        if isinstance(node, dict):
            for k, v in node.items():
                here = f"{path}.{k}" if path else str(k)
                if k == "ignorePaths" and isinstance(v, list):
                    for i, entry in enumerate(v):
                        if isinstance(entry, str) and (why := _offence(entry)):
                            offenders.append(f"{here}[{i}] = {entry!r} — {why}")
                walk(v, here)
        elif isinstance(node, list):
            for i, v in enumerate(node):
                walk(v, f"{path}[{i}]")

    walk(cfg, "")
    if offenders:
        raise GateError(
            f"{preset_path} uses `ignorePaths` to reach a file this repo's dist/dotnet/ BASELINE "
            f"must keep managed, at: {'; '.join(offenders)}. ignorePaths matches by SUBSTRING "
            f"(`file.includes(ignorePath)`), so it cannot separate a receiver's copy from THIS "
            f"repo's dist/dotnet/ source of truth — the one pin Renovate actually bumps here "
            f"(#660). Use a packageRule with `matchFileNames` + `enabled: false`, which anchors "
            f"(.github#678). This is keyed on the baseline, not on the synced set: since ADR-0068 "
            f"`dist/dotnet/.config/dotnet-tools.json` is no longer synced anywhere and is still the "
            f"engine pin's only entry point into the org (.github#1798)."
        )

    # (2) Each synced file must actually BE disabled, by an anchored matchFileNames rule.
    rules = cfg.get("packageRules")
    rules = rules if isinstance(rules, list) else []
    for rel in SYNCED_RECEIVER_FILES:
        base = os.path.basename(rel)
        # Every rule whose matchFileNames mentions this file, in declaration order. Renovate merges
        # packageRules in order and LAST WINS, so the rule that decides `enabled` is the last one
        # that matches — not the first, and not "the one that says enabled:false somewhere".
        mentioning = [
            (i, rule, pats)
            for i, rule in enumerate(rules)
            if isinstance(rule, dict) and isinstance(pats := rule.get("matchFileNames"), list)
            and any(isinstance(p, str) and (p == rel or base in p or p.endswith(rel)) for p in pats)
        ]
        if not mentioning:
            raise GateError(
                f"{preset_path} declares no `matchFileNames: [{rel!r}]` + `enabled: false` "
                f"packageRule. Without it Renovate proposes bumps against every receiver's SYNCED "
                f"copy of {rel} — a PR the build-config drift gate must reject, in every receiver, "
                f"forever, re-opened after every close (.github#678, FS.GG.Game#278)."
            )

        i, rule, pats = mentioning[-1]  # last wins
        if rule.get("enabled") is not False:
            raise GateError(
                f"{preset_path} packageRules[{i}] is the LAST rule matching {rel} and it does not "
                f"set `enabled: false` (enabled={rule.get('enabled')!r}). Renovate merges "
                f"packageRules in order and the last match wins, so this re-enables the receiver's "
                f"synced copy and the un-mergeable PR returns — with every earlier `enabled: false` "
                f"still sitting in the file, looking correct (.github#678)."
            )

        over_broad = [p for p in pats if isinstance(p, str) and p != rel and (base in p or p.endswith(rel))]
        if over_broad:
            raise GateError(
                f"{preset_path} packageRules[{i}].matchFileNames contains {over_broad!r}, which "
                f"reaches beyond the receiver's copy — `**/`-style patterns match "
                f"dist/dotnet/{rel} too, disabling the ORG SOURCE OF TRUTH and the one pin Renovate "
                f"actually bumps here (#660). Declare exactly {rel!r}, anchored at the repo root "
                f"(.github#678)."
            )
        if rel not in pats:
            raise GateError(
                f"{preset_path} packageRules[{i}].matchFileNames is {pats!r}, which does not "
                f"declare {rel!r} exactly (.github#678)."
            )

        extra = sorted(set(rule) - _DISABLE_RULE_KEYS)
        if extra:
            raise GateError(
                f"{preset_path} packageRules[{i}] disables {rel} but also carries {extra!r}. Every "
                f"additional key NARROWS the rule, and a narrowed rule leaves the un-mergeable "
                f"receiver PR proposing again for whatever it no longer covers — "
                f"`matchUpdateTypes: ['major']` would still propose every minor bump — while this "
                f"gate stayed green. The only sanctioned narrowing is `matchRepositories`, and it "
                f"must be a positive allow-list that does not name {SOURCE_OF_TRUTH_REPO} "
                f"(.github#678, #794, #1552)."
            )

        # The one sanctioned narrowing, asserted as a PREDICATE about the AUTHORITY (see
        # _AUTHORITY_SAFE_ENTRY). Every rejected shape is a silent failure that
        # renovate-config-validator calls valid:
        #   absent            -> the rule applies HERE too, freezing this repo's own authored pins
        #                        and the org baseline every receiver takes (#576, #753).
        #   a `!`-negation    -> matches every repo it does not name — including this one.
        #   a glob / regex    -> `*` and `/^FS-GG/.*/ ` match the authority just as happily.
        #   the authority     -> named outright.
        # WHICH receivers it must name is NOT asserted here — that is
        # check-preset-repo-scope-coherence.py's subject, derived from registry/repos.yml (#1552).
        got = rule.get("matchRepositories")
        if got is None:
            raise GateError(
                f"{preset_path} packageRules[{i}] disables {rel} with NO `matchRepositories`. That "
                f"path is a materialized copy in a receiver and this repo's OWN authored build "
                f"config in {SOURCE_OF_TRUTH_REPO}, which dogfoods this preset — so an unscoped "
                f"rule stops Renovate proposing bumps for this repo's own pins (FSharp.Core, "
                f"Spectre.Console, xunit) AND for the dist/dotnet/ baseline every receiver takes: "
                f"the #576 freeze, silent and valid. Declare a positive allow-list of the "
                f"receivers (.github#678, #794, #1552)."
            )
        if not isinstance(got, list) or not got:
            raise GateError(
                f"{preset_path} packageRules[{i}] disables {rel} with "
                f"`matchRepositories = {got!r}`, which is not a non-empty list. An empty list "
                f"matches nothing and silently re-enables the un-mergeable receiver PR (.github#794)."
            )
        unsafe = [r for r in got if not isinstance(r, str) or not _AUTHORITY_SAFE_ENTRY.match(r)]
        if unsafe:
            raise GateError(
                f"{preset_path} packageRules[{i}] disables {rel} with `matchRepositories = {got!r}`, "
                f"which contains {unsafe!r}. Every entry must be a plain positive `owner/repo`: a "
                f"`!`-negation matches every repo it does not name, and a glob or regex matches "
                f"whatever it happens to match — both of which can catch {SOURCE_OF_TRUTH_REPO}, "
                f"where these paths are AUTHORED rather than received. That is the #576 freeze, "
                f"silent and valid (.github#794, #1552)."
            )
        if SOURCE_OF_TRUTH_REPO in got:
            raise GateError(
                f"{preset_path} packageRules[{i}] disables {rel} and NAMES {SOURCE_OF_TRUTH_REPO} in "
                f"`matchRepositories`. This repo AUTHORS that path — it does not receive it — and it "
                f"dogfoods this preset, so listing it freezes this repo's own engine pins "
                f"(FSharp.Core, Spectre.Console, xunit) and the dist/dotnet/ baseline every receiver "
                f"takes (#576, #753, #1552)."
            )


# ---- A CAP's expiry condition must be EXECUTABLE, not prose (.github#943, #850) ------------------
#
# THE DEFECT. An `allowedVersions` cap is written for a reason, and the reason expires. Every cap the
# org has ever written said so, in its own description:
#
#     ACTION WHEN YoloDev.Expecto.TestSdk ships a release supporting Expecto >= 11:
#     delete this allowedVersions cap
#
# That condition came true — adapter 0.16.0 shipped, depending on Expecto 10.2.3 as a MINIMUM with no
# upper bound, so it had stopped constraining Expecto at all. Nothing re-checked it. The cap went on
# holding FS.GG.Rendering at Expecto 10.x for months after the only reason for it was gone (#850,
# FS.GG.Rendering#845). The trigger fired into a void, because a `description` is prose and nothing
# reads prose. A rule nothing re-checks is a rule that outlives its reason.
#
# THE ENCODING, AND WHY IT IS THIS ONE. The trigger is an annotation inside the rule's own
# `description`. The three alternatives were not rejected on taste:
#
#   * A STRUCTURED SIBLING FIELD (`"fsggCapTrigger": {...}`) is INEXPRESSIBLE. Renovate's own
#     validator rejects it — `Invalid configuration option: packageRules[0].fsggCapTrigger`, measured
#     against renovate-config-validator 43.265.4, not inferred — and default.json is the SHARED org
#     preset every repo extends, so a config error here is an org-wide break. `description` is a
#     legal field that Renovate ignores, and the same probe validates clean through it.
#   * A COMPANION FILE keyed by package is the shape this very preset already argues against (the
#     #925 paragraph in default.json): "one place, no ordering dependency, and no second file that
#     must be kept in step". A trigger that can drift away from the cap it governs is the defect.
#   * A HAND-WRITTEN GATE LEG PER CAP is what #942 deliberately declined. It covers the cap somebody
#     remembered to write a leg for, which is the same "somebody remembers" this exists to replace.
#
# It is also the spelling this repo already uses for exactly this job: the preset's OWN annotation
# manager reads `# renovate: datasource=… depName=…` out of a comment. A cap trigger is that idea
# aimed at the cap instead of the pin.
#
# WHAT IT ASSERTS. A cap excludes a set of published versions. It exists because those versions are
# unusable for a stated reason. The trigger states that reason as a fact about a nuspec:
#
#     fsgg-cap-expires-when: dependency=Expecto admits=11.0.0
#
# read as: "this cap expires when a version it EXCLUDES declares a dependency on Expecto whose range
# admits 11.0.0". The gate resolves that daily against api.nuget.org — the same registry Renovate
# reads — and reds when it comes true, naming the cap, the version, and the range that retired it.
# That is a literal transcription of the prose ACTION line into something that executes.
#
# THE SENTINEL, AND WHY IT IS NOT AN ESCAPE HATCH. Not every cap's expiry is a nuspec fact.
# FS.GG.Audio caps FSharp.Core at `<11.0.0` because the org majority is on 10.1.x — a COORDINATION
# decision, which api.nuget.org cannot answer and never will. So a cap may instead declare:
#
#     fsgg-cap-expires-when: manual — <why no automatic check is possible>
#
# and the gate reports it as UNCHECKED rather than pretending. What the gate refuses is a cap with
# NEITHER: silence is the state that produced #850. This is `Paths: none` from the org's own
# parallel-work protocol, one level down — a real sentinel that makes an absence DELIBERATE and
# machine-readable, where prose and an omission rendered identically. Write the predicate, or write
# the sentinel; those are the only two honest states.
#
# WHAT THE PREDICATE DOES NOT EXPRESS, stated here so the next writer meets the boundary instead of
# discovering it. It quantifies over the versions THIS cap excludes, and reads THEIR nuspecs. So it
# fits a cap whose reason is a fact about the capped package itself — which is every cap the org
# holds today. It does NOT fit a cap on A whose trigger is a fact about B's nuspec: FS.GG.Rendering's
# retired cap was exactly that shape (it capped Expecto, and its ACTION condition was about the
# ADAPTER's nuspec), and it would have to use the `manual` sentinel here. That cap is gone (#845), so
# there is no live subject to build the second quantifier against, and building one on a hypothetical
# is how a gate grows a leg nobody can test. If such a cap is written again, extend this — do not
# reach for `manual` to avoid the work.
# A trigger is a WHOLE description unit — an array entry, or a line of a string description — and not
# a substring found anywhere in the prose. That distinction is not fussiness; the looser rule was
# written first and it rebuilt #850 inside the fix for it.
#
# A cap's description is where the mechanism gets EXPLAINED, so the annotation's own spelling appears
# in the prose around it ("...write `fsgg-cap-expires-when: manual — <why>` if..."). A regex that
# searched the joined text matched that sentence too, and took whichever came FIRST. The real
# trigger won only because it happened to sit earlier in the array. Reorder the array — a harmless
# edit nobody would think twice about — and this cap silently reads as the `manual` sentinel: never
# checked again, reported as green, which is precisely the state that let Rendering's cap outlive its
# reason. Documentation that disables the thing it documents is the trap #919 hit when a gate parsed
# a quoted sample invocation as a real one.
#
# Anchoring to the unit lets the prose quote the annotation freely, and makes "is this a trigger?" a
# question about structure rather than about what else the sentence happens to contain.
_CAP_TRIGGER_PREFIX = "fsgg-cap-expires-when:"
_CAP_PREDICATE_RE = re.compile(r"^dependency=(?P<dep>\S+)\s+admits=(?P<admits>\S+)$")
# `manual`, then a separator (em dash, hyphen or colon), then a REASON. The reason is required: a
# bare `manual` is the silence this gate exists to refuse, wearing the sentinel's clothes.
_CAP_MANUAL_RE = re.compile(r"^manual\s*[—\-:]\s*(?P<why>\S.*)$")


class Cap(NamedTuple):
    where: str              # human-readable locator: the preset path + the rule's subject
    packages: tuple[str, ...]
    allowed: str            # the `allowedVersions` spec itself
    dependency: str | None  # None => the manual sentinel
    admits: str | None
    manual_why: str | None


def _description_units(rule: dict) -> list[str]:
    """A packageRule's `description`, split into the units a trigger may occupy.

    Renovate allows a string OR an array of them, and a string may itself carry newlines — so the
    unit is "an array entry, or a line within one", which is what a human means by "put it on its
    own line".
    """
    desc = rule.get("description")
    if isinstance(desc, str):
        desc = [desc]
    if not isinstance(desc, list):
        return []
    return [line for d in desc for line in str(d).split("\n")]


def _cap_triggers(rule: dict) -> list[str]:
    """Every declared trigger on `rule`: the payload of each unit that IS a trigger, in order."""
    return [
        u.strip()[len(_CAP_TRIGGER_PREFIX):].strip()
        for u in _description_units(rule)
        if u.strip().startswith(_CAP_TRIGGER_PREFIX)
    ]


def cap_allows(spec: str, version: str) -> bool:
    """True if the `allowedVersions` cap `spec` ADMITS `version` — i.e. does not exclude it.

    Only the spellings the org's house rule permits. Anything else raises rather than guessing, and
    guessing here is not a small error: mis-reading which versions a cap excludes silently changes
    the SUBJECT of the trigger check, which is the fails-open direction.

    A `/regex/` cap is refused outright. Renovate tries allowedVersions as a regex FIRST, so one is
    legal there — but a regex describes no version ORDER, so this gate cannot say which published
    versions it excludes, and a trigger whose subject cannot be enumerated cannot be checked.
    """
    spec = (spec or "").strip()
    if not spec:
        raise GateError("`allowedVersions` is empty")
    if len(spec) > 1 and spec.startswith("/") and spec.rsplit("/", 1)[-1] in ("", "i"):
        raise GateError(
            f"`allowedVersions: {spec!r}` is a REGEX, and this gate cannot enumerate the versions a "
            f"regex excludes — so its expiry trigger cannot be checked. Write the cap as a "
            f"comparator (`<1.0.0`) or a NuGet range, which is the org house rule, or declare the "
            f"`manual` sentinel and say why."
        )
    m = re.match(r"^(?P<op><=|>=|<|>|=)\s*(?P<v>\S+)$", spec)
    if m:
        op, want, have = m.group("op"), parse_version(m.group("v")), parse_version(version)
        return {
            "<": have < want, "<=": have <= want, ">": have > want,
            ">=": have >= want, "=": have == want,
        }[op]
    # Not a comparator: the remaining legal spelling is a NuGet bracket range.
    return range_admits(spec, version)


def load_caps(preset_path: str) -> list[Cap]:
    """Every `allowedVersions` cap in the preset, with its declared trigger. Raises on an UNDECLARED one.

    This is the half that makes the house rule enforceable rather than merely written down: a cap
    added without a trigger is red on the PR that adds it, which is the only moment anybody has the
    context to state one.
    """
    try:
        with open(preset_path, encoding="utf-8") as fh:
            cfg = json.load(fh)
    except (OSError, ValueError) as e:
        raise GateError(f"cannot read the org preset {preset_path}: {e}") from e

    caps: list[Cap] = []
    for i, rule in enumerate(cfg.get("packageRules") or []):
        if not isinstance(rule, dict) or "allowedVersions" not in rule:
            continue
        names = rule.get("matchPackageNames") or []
        where = f"{preset_path}: packageRules[{i}] ({', '.join(names) or 'no matchPackageNames'})"
        for n in names:
            if n.startswith("/"):
                raise GateError(
                    f"{where}: this cap matches packages by REGEX, so the gate cannot name the "
                    f"package whose nuspec its trigger is about. Caps name a package literally — "
                    f"which is the org house rule anyway (a plain string reaches minimatch with "
                    f"`nocase: true`, so it already matches a re-cased id; see this rule's own "
                    f"description)."
                )
        if not names:
            raise GateError(
                f"{where}: a cap with no `matchPackageNames` applies to EVERY package, and this "
                f"gate will not enumerate the whole registry to find out what it excludes."
            )

        triggers = _cap_triggers(rule)
        if len(triggers) > 1:
            raise GateError(
                f"{where}: this cap declares {len(triggers)} expiry triggers "
                f"({'; '.join(repr(t) for t in triggers)}). Which one is checked would come down to "
                f"their ORDER in the description, and a cap whose meaning depends on the order of "
                f"its prose is one edit away from silently going unchecked. Declare exactly one."
            )
        if not triggers:
            raise GateError(
                f"{where}: this `allowedVersions` cap declares NO expiry trigger, so nothing will "
                f"ever re-check it. That is the #850 defect exactly: FS.GG.Rendering's Expecto cap "
                f"stated its own ACTION condition in prose, the condition came true, nothing read "
                f"the prose, and the cap outlived its reason by months.\n"
                f"    Add ONE line to this rule's `description`, either:\n"
                f"      fsgg-cap-expires-when: dependency=<id> admits=<version>\n"
                f"        (the cap expires when a version it EXCLUDES declares a dependency on <id> "
                f"whose range admits <version> — checked daily against api.nuget.org)\n"
                f"      fsgg-cap-expires-when: manual — <why no automatic check is possible>\n"
                f"        (for a cap whose trigger is not a fact about a nuspec — an org "
                f"coordination decision, say. Reported as UNCHECKED, never as green.)\n"
                f"    It must be a WHOLE description entry, or a whole line within one. A mention "
                f"inside a sentence is prose, and prose is what this replaces."
            )
        rest = triggers[0]
        if manual := _CAP_MANUAL_RE.match(rest):
            caps.append(Cap(where, tuple(names), rule["allowedVersions"], None, None,
                            manual.group("why")))
            continue
        pred = _CAP_PREDICATE_RE.match(rest)
        if not pred:
            raise GateError(
                f"{where}: the expiry trigger {rest!r} is not readable. It must be either "
                f"`dependency=<id> admits=<version>` or `manual — <why>`. A trigger the gate cannot "
                f"parse is a trigger nothing checks, which is the state this annotation exists to "
                f"end — so it is an error rather than a warning."
            )
        try:
            parse_version(pred.group("admits"))
        except GateError as e:
            raise GateError(f"{where}: the trigger's `admits=` is not a version: {e}") from e
        caps.append(Cap(where, tuple(names), rule["allowedVersions"],
                        pred.group("dep"), pred.group("admits"), None))
    return caps


def check_cap(cap: Cap, resolve, dep_ranges) -> str | None:
    """None if the cap is still justified, else the reason it has EXPIRED.

    `resolve` is (package -> list[str] of published versions); `dep_ranges` is
    (package, version, dep_id -> list[str] of declared ranges). Both raise rather than return empty
    to mean "could not tell", so an unreadable registry reds the gate instead of retiring a cap.
    """
    if cap.dependency is None:
        # Callers dispatch on the sentinel; this is the backstop, and it is not decoration. A manual
        # cap carries no dependency to look up, and asking for one anyway HAPPENS not to crash
        # today only because the package it is asked about (FSharp.Core) declares no dependencies at
        # all — so the empty result reads as "declares no dependency on <None>", i.e. CAP EXPIRED.
        # A manual cap silently reported as expired is the exact wrong answer, arrived at by luck.
        raise GateError(
            f"{cap.where}: this cap declares the `manual` sentinel, so there is no automatic check "
            f"to run and its trigger must not be evaluated as though there were."
        )
    for package in cap.packages:
        stable = [v for v in resolve(package) if not is_prerelease(v)]
        excluded = sorted((v for v in stable if not cap_allows(cap.allowed, v)), key=parse_version)
        if not excluded:
            raise GateError(
                f"{cap.where}: `allowedVersions: {cap.allowed!r}` excludes NO published stable "
                f"version of {package}. Its trigger therefore has no subject, and a check with no "
                f"subject must not report green (epic #266). Either the cap is dead weight and "
                f"should be deleted, or it is capping a version line that no longer exists."
            )
        for version in excluded:
            ranges = dep_ranges(package, version, cap.dependency)
            if not ranges:
                return (
                    f"{cap.where}: CAP EXPIRED — {package} {version} is excluded by "
                    f"`allowedVersions: {cap.allowed!r}`, but its nuspec declares NO dependency on "
                    f"{cap.dependency} at all, so it does not constrain {cap.dependency} and the "
                    f"cap's stated reason no longer holds for it.\n"
                    f"    Re-read the nuspec, then delete the cap or narrow it — and update this "
                    f"rule's `fsgg-cap-expires-when:` line to whatever is true now."
                )
            if len(ranges) > 1:
                raise GateError(
                    f"{cap.where}: {package} {version} declares DISAGREEING ranges on "
                    f"{cap.dependency} across its target-framework groups ({', '.join(ranges)}). "
                    f"The gate will not guess which one the trigger is about — state the cap's "
                    f"reason per-framework, or declare the `manual` sentinel and say why."
                )
            if range_admits(ranges[0], cap.admits):
                return (
                    f"{cap.where}: CAP EXPIRED — {package} {version} is excluded by "
                    f"`allowedVersions: {cap.allowed!r}`, but its nuspec declares "
                    f"{cap.dependency} {ranges[0]!r}, which ADMITS {cap.dependency} "
                    f"{cap.admits}. The cap's own trigger — "
                    f"`dependency={cap.dependency} admits={cap.admits}` — has come true, so the "
                    f"reason it was written for is gone.\n"
                    f"    Delete the cap, or narrow it to keep excluding whatever is still bad and "
                    f"restate the trigger. Do NOT reason from the version number: this cap exists "
                    f"BECAUSE the number lies (#850)."
                )
    return None


def check_bump_mechanism(root: str, preset_path: str) -> tuple[str, list[str]]:
    """Every host the preset routes FS.GG.* to must be one Renovate can actually read.

    Public host  -> no credential needed, nothing to assert.
    Auth host    -> THIS repo's own config must carry a `hostRules` token for it, because Renovate
                    does not substitute {{ secrets }} inside a preset pulled via `extends`. This is
                    the .github#263 protection; it still fires, it is just no longer demanded of a
                    repo that routes nowhere near an auth-required feed.
    Unknown host -> fail CLOSED. A host this gate has never heard of must not read as fine.
    """
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

    assert_no_stray_secret_templates(cfg, config_path)

    hosts = routed_hosts(preset_path)
    ROUTED_HOSTS[:] = hosts  # diagnose_stale_pin() needs to know if a credential is in the path
    rules = cfg.get("hostRules")
    rules = rules if isinstance(rules, list) else []

    for host in hosts:
        if host in PUBLIC_HOSTS:
            continue
        if host not in AUTH_HOSTS:
            raise GateError(
                f"the org preset routes FS.GG.* to {host!r}, which this gate does not know how to "
                f"read. It is neither a known-public registry ({', '.join(sorted(PUBLIC_HOSTS))}) "
                f"nor a known auth-required one ({', '.join(sorted(AUTH_HOSTS))}). A gate that "
                f"cannot tell whether the bot can reach a host must not report green over it — add "
                f"the host to PUBLIC_HOSTS or AUTH_HOSTS, deliberately."
            )
        has_token = any(
            isinstance(r, dict)
            and str(r.get("matchHost", "")).strip() == host
            and str(r.get("token", "")).strip()
            for r in rules
        )
        if not has_token:
            raise GateError(
                f"the org preset routes FS.GG.* to {host}, which REQUIRES a credential, and "
                f"{config_path} declares no `hostRules` token for it. Renovate does NOT substitute "
                f"{{{{ secrets }}}} inside a preset pulled via `extends`, so the token must live in "
                f"THIS repo's own config. Without it every FS.GG.* lookup 401s, a 401 is an empty "
                f"version list rather than an error, no bump PR is ever opened, and the pins below "
                f"freeze silently. That is the .github#263 / #576 root cause.%0A"
                f"Before adding a token: check whether these packages are simply PUBLIC on "
                f"nuget.org. In #576 all 32 of them were, and the correct fix was to stop routing "
                f"them to an auth-required host at all — not to chase the credential."
            )
    return config_path, hosts


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


def diagnose_stale_pin(pin: Pin, top: str, ev: BotEvidence, cap: "Cap | None" = None) -> str:
    """WHY is the pin stale? Answered from evidence, never asserted.

    `cap` is the `allowedVersions` cap governing this pin's dep, if any (.github#2464). When present,
    `top` is already the newest version THE CAP ADMITS, not the feed's absolute newest — Renovate
    itself would never propose past the cap, so comparing against the uncapped newest would red a pin
    the bot is correctly refusing to bump. Say so, rather than let the generic "the bot is blind"
    diagnoses below send a reader chasing a credential or a manager regex that isn't the cause.
    """
    if cap is not None:
        where = (
            f"{pin.file}:{pin.line}: {pin.dep_name} is pinned at {pin.current_value!r}, but "
            f"{cap.where} caps it at `allowedVersions: {cap.allowed!r}`, whose newest ADMITTED "
            f"published version is {top!r} — this compares against that ceiling, not the feed's "
            f"absolute newest, because Renovate itself will never propose past the cap."
        )
    else:
        where = f"{pin.file}:{pin.line}: {pin.dep_name} is pinned at {pin.current_value!r} but the newest on the registry Renovate reads is {top!r}."

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

    routed = ROUTED_HOSTS or ["<unknown>"]
    auth_routed = [h for h in routed if h in AUTH_HOSTS]

    if auth_routed:
        # The pre-#576 world: the preset still routes FS.GG.* somewhere that needs a credential.
        cause = (
            f"i.e. it 401'd on {', '.join(auth_routed)}.\n"
            f"    The `hostRules` block being PRESENT is not the credential RESOLVING — that was the "
            f"#263 mistake, and the pin froze twice more after it. "
            f"`{{{{ secrets.FSGG_PACKAGES_READ_TOKEN }}}}` is a Mend App Secret; nothing in CI can "
            f"read or verify it, which is exactly why nobody ever checked.\n"
            f"    Before you go chasing that credential: CHECK WHETHER THE PACKAGE IS PUBLIC ON "
            f"nuget.org. In #576 all 32 org packages were, and the fix was to stop routing FS.GG.* "
            f"to an auth-required host at all (default.json), not to repair a token."
        )
    else:
        # Post-#576: FS.GG.* is routed somewhere public, so there is NO credential in the path and
        # "fix the token" is not an available excuse. A blind bot here means something else, and the
        # gate must not name a cause it did not check (#566).
        cause = (
            f"and it is NOT a credential problem: the preset routes FS.GG.* to "
            f"{', '.join(routed)}, which is public and needs no token (#576).\n"
            f"    So do not go looking for one. The remaining causes are: the preset's "
            f"`matchPackageNames` regex no longer matches this id (note it is CASE-SENSITIVE unless "
            f"it carries the /i flag — that is how `fs.gg.coord.cli` behaved differently from "
            f"`FS.GG.SDD.Cli` for months); a `matchPackageNames` rule has disabled the update; "
            f"Renovate is not running on this repo at all; or nuget.org itself served nothing.\n"
            f"    Confirm against Dependency Dashboard #{ev.dashboard} — and do NOT hand-bump the "
            f"literal, which is what was done in #127, #263 and #566, after which it froze again "
            f"every time."
        )

    return (
        f"{where} THE BOT IS BLIND TO THE FEED — do NOT hand-bump this literal.\n"
        f"    Evidence: Renovate's Dependency Dashboard (#{ev.dashboard}) DOES list {pin.dep_name} "
        f"(so the manager's regex matches and the bot knows the pin exists), the registry DOES serve "
        f"{top!r}, and Renovate has opened NO bump PR for it — nor for ANY other FS.GG.* package, "
        f"ever, grouped or otherwise. A bot that can see the dependency, and can see nothing to "
        f"update, is a bot whose registry lookup returned nothing — {cause}"
    )


def _resolve_newest(pin: Pin, resolve) -> str:
    if pin.datasource != "nuget":
        raise GateError(
            f"datasource {pin.datasource!r} is not one this gate can resolve. It must not guess a "
            f"feed — extend the gate, or the pin goes unchecked."
        )
    if not pin.dep_name.startswith("FS.GG."):
        raise GateError(
            f"{pin.dep_name!r} is not an FS.GG.* package, so it does not resolve from the FS.GG.* registry. "
            f"This gate only reads that feed — extend it rather than reporting green."
        )
    return newest(resolve(pin.dep_name))


# ---- A pin's coherence ceiling is CAP-ADMITTED newest, not feed-absolute newest (.github#2464) -----
#
# THE GAP THIS CLOSES. `load_caps` and `check_cap` re-check an `allowedVersions` cap's own EXPIRY
# trigger — whether the cap has outlived its reason — but nothing before this connected a cap back to
# the FRESHNESS comparison every FS.GG.* pin is judged against. `_resolve_newest` above answers "what
# is newest on the registry", unconditionally; it does not know a cap exists. The only cap this repo
# had ever carried (YoloDev.Expecto.TestSdk) never surfaced this gap, because `_resolve_newest`
# refuses any package that is not `FS.GG.*` — a THIRD-PARTY cap's subject is categorically exempt from
# the freshness check. A cap on an `FS.GG.*` pin this gate actually judges is new territory.
#
# Left unfixed, capping `FS.GG.SDD.Cli` below a real feed-newest release would make the FRESHNESS
# check red forever: the pin equals what the cap admits, never what the registry's absolute newest
# is, and those two are now permanently different by construction. That is not a residual finding to
# route around — it is the cap being unable to do the one thing it exists for, on the only pin this
# item is about.
#
# WHAT IT ASSERTS. A pin governed by an `allowedVersions` cap is coherent when it equals the newest
# PUBLISHED version the cap ADMITS — exactly what Renovate itself would propose under that same
# `allowedVersions` filter (`cap_allows`, ordering shared via `fsgg_feed.newest`). A pin with NO
# governing cap is judged as before, against the feed's absolute newest. Two or more caps naming the
# same package is refused rather than guessed at (which one bounds freshness would be undefined), and
# a cap that admits NOTHING published leaves the pin unable to ever be coherent, which is also refused
# rather than reported as a silent, permanent red with no actionable cause.
def _governing_caps(pin: Pin, caps: list) -> list:
    """Every cap in `caps` whose `matchPackageNames` names `pin.dep_name` (nocase — .github#660's own
    plain-string minimatch semantics, restated in `default.json`'s FS.GG.* rule and Cap's docstring).
    """
    return [c for c in caps if any(p.casefold() == pin.dep_name.casefold() for p in c.packages)]


def _effective_newest(pin: Pin, resolve, caps: list) -> "tuple[str, Cap | None]":
    """The version `pin` must equal to be coherent, and the cap that says so — but ONLY when that cap
    actually LOWERS the ceiling below the feed's absolute newest.

    Absent a governing cap, or with one that does not currently exclude the feed's newest release
    (e.g. a fresh cap written ahead of the release it targets), this is exactly `_resolve_newest` —
    unconditional feed-absolute newest, the pre-#2464 behaviour — and the second return value is
    `None`. A cap's admitted-newest can never exceed the feed's absolute newest (it is a filter over
    the same population), so returning `None` whenever they are EQUAL is not losing information: the
    cap is not the reason this pin is (or is not) coherent, and saying otherwise would misdirect a
    reader toward the cap for a staleness the cap has nothing to do with. Only when the cap's admitted
    newest is strictly lower does it become the operative ceiling, and Renovate itself would never
    propose past it — comparing against the feed's absolute newest instead would red a pin the bot is
    correctly refusing to advance.
    """
    top = _resolve_newest(pin, resolve)
    governing = _governing_caps(pin, caps)
    if not governing:
        return top, None
    if len(governing) > 1:
        raise GateError(
            f"{pin.file}:{pin.line}: {pin.dep_name} is named by {len(governing)} allowedVersions caps "
            f"in the preset ({'; '.join(c.where for c in governing)}). Which one bounds this pin's "
            f"coherence ceiling is undefined when more than one applies — narrow the caps' "
            f"matchPackageNames so at most one governs {pin.dep_name}."
        )
    cap = governing[0]
    admitted = [v for v in resolve(pin.dep_name) if cap_allows(cap.allowed, v)]
    if not admitted:
        raise GateError(
            f"{cap.where}: `allowedVersions: {cap.allowed!r}` admits NO published version of "
            f"{pin.dep_name} at all, so {pin.file}:{pin.line} can never be coherent under this cap. "
            f"Either the cap excludes a version line that still contains real releases, or it needs "
            f"narrowing."
        )
    admitted_top = newest(admitted)
    if parse_version(admitted_top) >= parse_version(top):
        return top, None
    return admitted_top, cap


# ---- Does the pin's versioning scheme make its literal a SINGLE version, or a range? (#576, #1122) --
#
# THE DEFECT. #576's four-round freeze was NOT, at root, a credential or a route. It was the SCHEME:
# the annotation manager's versioningTemplate defaults to `nuget`, and under `nuget` versioning a
# BARE literal `0.10.0` is an inclusive minimum — `>=0.10.0` — which every newer release already
# satisfies, so Renovate proposes nothing. The pin sat at exactly newest, so every other check in
# this gate (freshness, routing, detection) was GREEN while the pin could not move by construction.
# #1119 fixed it by writing `versioning=loose` onto the pin, under which the same bare literal is a
# single pinned version and a newer release does NOT satisfy it. This asserts that property so the
# `versioning=` token cannot be dropped again without a red.
#
# WHAT `isSingleVersion` MEANS, PROVEN not reasoned. Modelled here in Python (the house style — this
# gate already models NuGet ordering in fsgg_feed rather than shelling to a bot), but the model is a
# transcription of renovate 43.268.4's OWN `versioning.get(scheme).isSingleVersion(literal)`, driven
# directly because reasoning about this is exactly what mis-diagnosed #576 for four rounds:
#
#     scheme          '0.10.0'   '[0.10.0]'   '>=0.10.0' / '^0.10.0'
#     nuget           false      true         false
#     loose/semver/…  true       false        false
#
# So: under `nuget`, ONLY the bracketed exact form `[x]` is single; a bare literal is a floor. Under
# the semver family, a bare exact literal is single and every bracket/comparator/range is not. Any
# OTHER scheme is refused (GateError), never assumed single — a scheme this gate has not verified
# against renovate must not read as fine (epic #266). Extend the sets below only after driving the
# real `isSingleVersion`, the way #576 taught.
_SEMVER_LIKE_VERSIONINGS = frozenset({"loose", "semver", "semver-coerced", "npm"})
_NUGET_EXACT_RE = re.compile(r"^\[\s*(?P<v>[^,\[\]()]+?)\s*\]$")

# Renovate renders the manager's versioningTemplate to get a pin's scheme. Reproduce the two shapes
# this org's presets actually use, and refuse the rest rather than mis-render a Handlebars template:
#   * a PLAIN literal (`"nuget"`) — a constant scheme, and Renovate ignores a `versioning=` capture
#     against it, so the capture is ignored here too;
#   * the capture-or-default idiom `{{#if versioning}}{{{versioning}}}{{else}}<default>{{/if}}` — the
#     scheme is the pin's `versioning=` capture when it has one, else <default>.
_VERSIONING_CAPTURE_OR_DEFAULT_RE = re.compile(
    r"^\{\{#if\s+versioning\}\}\s*\{\{\{\s*versioning\s*\}\}\}\s*\{\{else\}\}"
    r"\s*(?P<default>[A-Za-z][\w.-]*)\s*\{\{/if\}\}$"
)


def resolve_versioning(versioning_template: str, captured: str | None) -> str:
    """The versioning scheme Renovate applies to a pin: the manager's template, rendered with the
    pin's own `versioning=` capture. Raises on a template shape this gate cannot render (fail closed).
    """
    t = (versioning_template or "").strip()
    if not t:
        raise GateError("the annotation manager declares no versioningTemplate")
    if "{{" not in t:
        # A constant scheme. Renovate renders the same literal regardless of any capture, so a stray
        # `versioning=` on a pin is INERT under this manager — model that rather than silently
        # honouring a capture the bot would ignore.
        return t
    m = _VERSIONING_CAPTURE_OR_DEFAULT_RE.match(t)
    if m:
        return (captured or m.group("default")).strip()
    raise GateError(
        f"the annotation manager's versioningTemplate {versioning_template!r} is neither a plain "
        f"scheme nor the `{{{{#if versioning}}}}{{{{{{versioning}}}}}}{{{{else}}}}<default>{{{{/if}}}}` "
        f"idiom, so this gate cannot tell which scheme a pin resolves to. Refusing to guess (#266) — "
        f"simplify the template or extend resolve_versioning() deliberately."
    )


def is_single_version(scheme: str, literal: str) -> bool:
    """True iff `literal` names a SINGLE version under `scheme` — i.e. one a newer release would NOT
    already satisfy, so Renovate can bump it. Raises on a scheme this gate has not verified (#266).
    """
    lit = (literal or "").strip()
    if not lit:
        raise GateError("empty version literal — nothing to classify")
    if scheme == "nuget":
        # Under nuget versioning ONLY the bracketed exact form `[x]` is single; a bare literal is a
        # `>=` floor (renovate 43.268.4: isSingleVersion('0.10.0') is false, '[0.10.0]' is true).
        m = _NUGET_EXACT_RE.match(lit)
        if not m:
            return False
        parse_version(m.group("v"))  # the bracketed form must still name a real version
        return True
    if scheme in _SEMVER_LIKE_VERSIONINGS:
        # A bare exact version is single; a comparator, caret, bracket or range is not — and
        # parse_version's anchored grammar rejects every one of those, so its success IS the test.
        try:
            parse_version(lit)
        except GateError:
            return False
        return True
    raise GateError(
        f"versioning scheme {scheme!r} is one this gate has not verified against renovate's own "
        f"`isSingleVersion`, so it cannot say whether {lit!r} is a single version or a range. A pin "
        f"whose scheme the gate cannot evaluate must not read as fine (epic #266): drive renovate's "
        f"isSingleVersion for {scheme!r} and add it to the sets in check-pin-coherence.py, or change "
        f"the pin to a scheme the gate knows ({', '.join(sorted(_SEMVER_LIKE_VERSIONINGS | {'nuget'}))})."
    )


def check_pin_bumpable(pin: Pin, scheme: str) -> str | None:
    """None if the pin's literal is a single, bumpable version under `scheme`; else the reason it is a
    range that can never bump — the #576 blind spot, which a freshness check alone cannot see because
    a `>=` floor equals newest today.
    """
    if is_single_version(scheme, pin.current_value):
        return None
    fix = (
        f"the bracketed exact form `[{pin.current_value}]`"
        if scheme == "nuget"
        else f"an explicit `versioning=loose` (or semver), under which `{pin.current_value}` IS a single version"
    )
    return (
        f"{pin.file}:{pin.line}: {pin.dep_name} is pinned at {pin.current_value!r} under "
        f"versioning={scheme!r}, which reads it as a RANGE, not a single version — so this pin can "
        f"NEVER bump: every newer release already satisfies it, and Renovate proposes nothing. This "
        f"is the #576 blind spot exactly — the literal equals newest TODAY, so freshness and every "
        f"other check here pass green while the pin is frozen by construction. Use {fix}."
    )


def check_pin(pin: Pin, resolve, evidence, caps: list | None = None) -> str | None:
    """None if the pin is at its coherence ceiling, else the reason it is not — WITH the cause, evidenced.

    `evidence` is a callable (dep_name -> BotEvidence). It is consulted only when a pin is actually
    stale, so the happy path costs no extra API calls. `caps` is every `allowedVersions` cap the
    preset declares (default `[]`); a pin one of them governs is judged against that cap's admitted
    newest, not the feed's absolute newest (.github#2464) — see `_effective_newest`.
    """
    top, cap = _effective_newest(pin, resolve, caps or [])
    have, want = parse_version(pin.current_value), parse_version(top)
    if have == want:
        return None
    if have < want:
        # The old code asserted "the annotation manager did not bump it" here — a cause it never
        # verified (.github#566). Go and look instead.
        return diagnose_stale_pin(pin, top, evidence(pin.dep_name), cap)
    ceiling = "the cap-admitted" if cap else "the registry's"
    return (
        f"{pin.file}:{pin.line}: {pin.dep_name} is pinned at {pin.current_value!r}, which is AHEAD "
        f"of {ceiling} newest {top!r}. The pin names a version no consumer can restore."
    )


def _no_verdict(reason: str) -> int:
    """Print a could-not-look framed as a NO-VERDICT, and return the no-verdict exit code (#1160).

    This gate used to return its FINDING code (1) for every unreadable feed, config, or preset, so a
    transient nuget.org / GitHub outage was indistinguishable from a stale pin — and a human, reading a
    red, would "fix" a pin that was fine by hand-advancing it. A no-verdict says the opposite: the gate
    could not complete its check, so nobody should act as if it did. Both codes are non-green, so CI
    still fails; the EXIT CODE (3 here vs 1 for a real finding) is what carries the distinction (#1158
    D2). `fsgg_feed`'s feed/GitHub reads raise a single `GateError` that conflates a permanent 404 with
    a retryable 5xx, so this uses the conservative permanent code rather than over-claiming retryable.
    """
    print(
        f"::error::check-pin-coherence: NO VERDICT — {reason} This is NOT a stale pin: the gate could "
        f"not complete its check, so do not hand-advance any pin on the strength of this red (#1160).",
        file=sys.stderr,
    )
    return int(ExitCode.NO_VERDICT_PERMANENT)


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
            return int(ExitCode.NO_VERDICT_PERMANENT)
        print(f"FIXTURE MODE — reading {args.fixture}, NOT the live feed. Not a coherence signal.")
        try:
            with open(args.fixture, encoding="utf-8") as fh:
                table = json.load(fh)
        except (OSError, ValueError) as e:
            return _no_verdict(f"cannot read fixture: {e}.")

        # A fixture feed may carry a canned `_renovate` block so the harness can exercise the
        # bot-evidence legs (blind / benign / manager-broke) with no network. Absent it, the gate is
        # explicit that the cause is UNVERIFIED rather than quietly asserting one — which is the
        # whole defect #566 is about, and it would be absurd to rebuild it inside the fix.
        canned = table.pop("_renovate", None)

        # ...and a `_nuspecs` block for the cap-trigger leg (#943): {pkg: {version: {dep: range}}}.
        # Same reasoning as `_renovate` above: absent it, the gate says the trigger is UNVERIFIED
        # rather than reading "no fixture" as "cap still justified", which would rebuild the very
        # fails-open shape the leg exists to close.
        canned_nuspecs = table.pop("_nuspecs", None)

        def resolve(pkg: str) -> list[str]:
            if pkg not in table:
                raise GateError(f"package {pkg!r} is not on the registry (fixture: absent)")
            if not table[pkg]:
                raise GateError(f"the feed served zero versions for {pkg!r}")
            return list(table[pkg])

        def dep_ranges(package: str, version: str, dep_id: str) -> list[str]:
            if canned_nuspecs is None:
                raise GateError(
                    f"the cap on {package} needs {package}@{version}'s nuspec, and this fixture "
                    f"carries no `_nuspecs` block. The gate will not read a missing nuspec as "
                    f"'declares no constraint' (#266)."
                )
            if package not in canned_nuspecs or version not in canned_nuspecs[package]:
                raise GateError(
                    f"fixture has no nuspec for {package}@{version} — an unreadable nuspec must "
                    f"not read as 'no constraint'."
                )
            # Synthesise a real nuspec and read it back with the REAL parser, rather than answering
            # from the dict directly. The canned value is a range, or a LIST of them (a nuspec
            # declares its dependencies once per target-framework group, so "pinned differently per
            # TFM" is a state the fixture must be able to express — it is the one the gate refuses
            # to guess about).
            #
            # Going through nuspec_dependency_ranges is not ceremony. Answering from the dict is a
            # SECOND implementation of "what ranges does this nuspec declare on X", and it drifted
            # from the first immediately: the real parser deduplicates identical groups, the dict
            # read did not, so the fixture called YoloDev 1.0.0's two identical Expecto entries
            # DISAGREEING while the live gate — reading the same package — deduplicated them and
            # passed. A fixture that answers differently from the thing it is a fixture for proves
            # nothing about it.
            deps = canned_nuspecs[package][version]
            decls = "".join(
                f'<dependency id="{did}" version="{s}" />'
                for did, spec in deps.items()
                for s in (spec if isinstance(spec, list) else [spec])
            )
            return nuspec_dependency_ranges(
                '<?xml version="1.0"?>'
                '<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">'
                f"<metadata><dependencies>{decls}</dependencies></metadata></package>",
                dep_id,
            )

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
            return _no_verdict(
                "no GITHUB_TOKEN/GH_TOKEN in the environment, so the feed and the bot's dashboard "
                "cannot be read at all."
            )

        repo = args.repo or os.environ.get("GITHUB_REPOSITORY") or ""
        if not repo:
            return _no_verdict(
                "no --repo and no GITHUB_REPOSITORY, so the gate cannot read the Renovate dashboard "
                "and PRs it needs to tell a BLIND bot from a merely-late one (#566)."
            )

        # Read the registry RENOVATE reads (nuget.org, anonymous), not the org feed. Comparing a pin
        # against a registry the bot cannot see is how a gate demands a bump that can never open —
        # the token is still needed below, but for the GitHub API (dashboard + PRs), not for this.
        def resolve(pkg: str) -> list[str]:
            return nuget_org_versions(pkg)

        def dep_ranges(package: str, version: str, dep_id: str) -> list[str]:
            return nuspec_dependency_ranges(nuget_org_nuspec(package, version), dep_id)

        _items: list = []  # the dep-independent search, fetched at most once per run

        def evidence(dep: str) -> BotEvidence:
            if not _items:
                _items.extend(_search_all(repo, token))
            return gather_bot_evidence(repo, token, dep, items=_items)

    try:
        # Runs before the feed is read: it needs no network, and a preset that un-manages the org
        # source of truth is a finding whether or not the feed happens to be reachable today.
        assert_synced_list_is_complete(root)
        assert_synced_files_unmanaged(preset, root)
        print(
            f"ok: {', '.join(SYNCED_RECEIVER_FILES)} is disabled for receivers via matchFileNames "
            f"+ a positive matchRepositories allow-list that does not name {SOURCE_OF_TRUTH_REPO}, "
            f"leaving dist/dotnet/ AND this repo's own authored copies managed here (#678, #794, "
            f"#1552)."
        )

        config_path, hosts = check_bump_mechanism(root, preset)
        # Deliberately NOT "pins can be bumped". For an auth-required host, presence of a token is
        # not resolution of it, and the gate cannot prove the latter from here (#566) — if a pin
        # turns out to be stale, diagnose_stale_pin() goes and gets the bot's own evidence.
        # For a PUBLIC host there is no credential to resolve, which is the point of #576: the
        # cheapest way to make a lookup provable is to remove the secret from it.
        needs_auth = [h for h in hosts if h in AUTH_HOSTS]
        print(
            f"ok: the org preset routes FS.GG.* to {', '.join(hosts)} — "
            + (
                f"{config_path} declares a hostRules token for {', '.join(needs_auth)}. NOTE: this "
                f"proves the block is PRESENT, not that the token RESOLVES."
                if needs_auth
                else "public, no credential required, so there is no token that can silently fail."
            )
        )

        # Before the feed is read: an UNDECLARED cap trigger is a finding whether or not nuget.org
        # is reachable today, and it is the half that keeps the house rule true going forward.
        caps = load_caps(preset)

        regexes, matches_path, versioning_template = load_annotation_manager(preset)
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
        return _no_verdict(str(e))

    problems: list[str] = []

    # Structural, and FIRST — no feed needed. A `# renovate:` annotation whose captured version sits
    # in a COMMENT rather than on its pin has drifted from that pin (#1236): the manager, and this
    # gate, then track a phantom out of the prose. Caught here, before the feed comparison, so the
    # phantom cannot wear the "stale pin — merge the bump PR" costume — that bump PR targets the
    # comment. If any pin is in this state the run is a FINDING and stops here: the feed comparison
    # below would be comparing the wrong literal anyway.
    drifted = [prose_capture_problem(pin) for pin in sorted(pins)]
    drifted = [p for p in drifted if p]
    if drifted:
        print()
        for p in drifted:
            print(f"::error::check-pin-coherence: {p.replace('%', '%25').replace(chr(13), '')
                                                    .replace(chr(10), '%0A')}", file=sys.stderr)
            print(f"check-pin-coherence: {p}", file=sys.stderr)
        print(f"\ncheck-pin-coherence: {len(drifted)} problem(s).", file=sys.stderr)
        return int(ExitCode.FINDING)

    # The caps, first: a cap that has outlived its reason is silently holding a whole repo back, and
    # unlike a stale pin nothing else in the org will ever notice (#943, #850).
    print(f"re-checking {len(caps)} allowedVersions cap(s) against their declared expiry triggers:")
    for cap in caps:
        subject = ", ".join(cap.packages)
        if cap.dependency is None:
            print(f"  MANUAL {subject:28} unchecked — {cap.manual_why}")
            continue
        try:
            expired = check_cap(cap, resolve, dep_ranges)
        except GateError as e:
            # A GateError here is a could-not-look (a feed/nuspec read that failed, or an unreadable
            # trigger), NOT a finding — so it is a no-verdict, and it ABORTS rather than joining the
            # findings list: an unreliable feed cannot be trusted to have proven the OTHER caps/pins
            # coherent either. This is the #1160 fix — the very confusion that let a human bump a good
            # pin. (No `cap.where` prefix: every GateError check_cap raises already carries one.)
            return _no_verdict(str(e))
        if expired:
            problems.append(expired)
        else:
            print(
                f"  ok     {subject:28} still justified — no version it excludes admits "
                f"{cap.dependency} {cap.admits}"
            )

    print(f"\ncomparing {len(pins)} annotated pin(s) against {', '.join(hosts)} (what Renovate reads):")
    for pin in sorted(pins):
        try:
            # The scheme first: a pin whose versioning makes its literal a `>=` floor can never bump,
            # and that is invisible to the freshness comparison below because such a pin equals newest
            # today (#576). So it is checked BEFORE, and short-circuits the feed read when it reds.
            scheme = resolve_versioning(versioning_template, pin.versioning)
            problem = check_pin_bumpable(pin, scheme)
            if problem is None:
                problem = check_pin(pin, resolve, evidence, caps)
        except GateError as e:
            # Could-not-look for this pin — a feed read that failed, or a literal/scheme that would
            # not parse — is a no-verdict, not a finding, and it aborts: if the feed was unreadable
            # for this pin it was unreadable for the run, and the pins already judged "ok" were judged
            # against the same unreliable feed. Returning a FINDING here is exactly what #1160 fixes.
            return _no_verdict(f"{pin.file}:{pin.line}: {e}")
        if problem:
            problems.append(problem)
        else:
            governing = _governing_caps(pin, caps)
            capped_note = f"  [capped: {governing[0].allowed!r}]" if governing else ""
            print(f"  ok   {pin.file}:{pin.line:<4} {pin.dep_name:24} == {pin.current_value}  [versioning={scheme}]{capped_note}")

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
        return int(ExitCode.FINDING)

    print(
        "\nok: every annotated pin equals the newest version on the registry Renovate reads, and "
        "every allowedVersions cap still has the reason it was written for."
    )
    return int(ExitCode.OK)


if __name__ == "__main__":
    # `run()` backstops a genuine CRASH into a no-verdict (never exit 1, this gate's finding code); the
    # deliberate no-verdicts above are already mapped by `main` via `_no_verdict` (#1160/#1159).
    sys.exit(run(main, sys.argv[1:], name="check-pin-coherence"))
