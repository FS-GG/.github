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
    has never heard of is readable).

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


# The receiver-side path of every file scripts/sync-build-config.sh copies byte-for-byte out of
# dist/dotnet/. A receiver's copy is SYNCED, not authored (ADR-0006, sync-not-fork), and the
# build-config drift gate — a REQUIRED check in adopting repos — fails any PR that changes one.
#
# .config/dotnet-tools.json stays FIRST: _offence() reports the first source path an ignorePaths
# entry is a substring of, and the #678 legs assert that message names dist/dotnet/.config/... .
SYNCED_RECEIVER_FILES = (
    ".config/dotnet-tools.json",
    "Directory.Build.props",
    "Directory.Packages.props",
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
_SANCTIONED_MATCH_REPOSITORIES = [f"!{SOURCE_OF_TRUTH_REPO}"]

# The org source of truth for each synced file: the copy that lives HERE and must stay MANAGED, so
# that Renovate keeps bumping the baseline (#660, #677). Every check below is ultimately about
# protecting these paths, not about the receiver copy.
SYNCED_SOURCE_PATHS = tuple(f"dist/dotnet/{f}" for f in SYNCED_RECEIVER_FILES)

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
        if d:
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


def assert_synced_files_unmanaged(preset_path: str) -> None:
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

    # (1) No ignorePaths entry may reach a synced file — tested in RENOVATE'S direction.
    offenders: list[str] = []

    def _offence(entry: str) -> str | None:
        # Renovate's own substring branch, verbatim: `file.includes(ignorePath)`. Anything that
        # occurs inside the source-of-truth path un-manages it, however short.
        for src in SYNCED_SOURCE_PATHS:
            if entry in src:
                return f"it is a SUBSTRING of {src}, which Renovate would therefore stop managing"
        # The minimatch branch is not modelled (see the docstring). Refuse conservatively: an entry
        # naming a synced file is either the literal trap above or a glob that works by coincidence.
        for rel in SYNCED_RECEIVER_FILES:
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
            f"{preset_path} uses `ignorePaths` to reach a synced build-config file, at: "
            f"{'; '.join(offenders)}. ignorePaths matches by SUBSTRING "
            f"(`file.includes(ignorePath)`), so it cannot separate a receiver's copy from THIS "
            f"repo's dist/dotnet/ source of truth — the one pin Renovate actually bumps here "
            f"(#660). Use a packageRule with `matchFileNames` + `enabled: false`, which anchors "
            f"(.github#678)."
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
                f"gate stayed green. The only sanctioned narrowing is "
                f"`matchRepositories: {_SANCTIONED_MATCH_REPOSITORIES!r}` (.github#678, #794)."
            )

        # The one sanctioned narrowing, asserted by VALUE. Both directions are silent failures that
        # renovate-config-validator calls valid:
        #   absent      -> the rule applies HERE too, freezing this repo's own authored pins and the
        #                  org baseline that every receiver is synced from (#576, #753).
        #   any other   -> "!FS-GG/.github, !FS-GG/FS.GG.Game" quietly hands Game back the
        #                  un-mergeable PR; a POSITIVE entry inverts the rule to apply nowhere else.
        got = rule.get("matchRepositories")
        if got != _SANCTIONED_MATCH_REPOSITORIES:
            raise GateError(
                f"{preset_path} packageRules[{i}] disables {rel} with "
                f"`matchRepositories = {got!r}`, not {_SANCTIONED_MATCH_REPOSITORIES!r}. That path "
                f"is a SYNCED copy in a receiver and this repo's OWN authored build config in "
                f"{SOURCE_OF_TRUTH_REPO}, which dogfoods this preset — so the rule must exclude the "
                f"source of truth, and exclude nothing else. Omit it and Renovate stops proposing "
                f"bumps for this repo's own pins (FSharp.Core, Spectre.Console, xunit) AND for the "
                f"dist/dotnet/ baseline every receiver is synced from — the #576 freeze, silent and "
                f"valid. Name any other repo and that receiver gets the un-mergeable PR back "
                f"(.github#794)."
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


def diagnose_stale_pin(pin: Pin, top: str, ev: BotEvidence) -> str:
    """WHY is the pin stale? Answered from evidence, never asserted."""
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
        f"of the registry's newest {top!r}. The pin names a version no consumer can restore."
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
        assert_synced_files_unmanaged(preset)
        print(
            f"ok: {', '.join(SYNCED_RECEIVER_FILES)} is disabled for receivers via matchFileNames "
            f"+ matchRepositories {_SANCTIONED_MATCH_REPOSITORIES}, leaving dist/dotnet/ AND this "
            f"repo's own authored copies managed here (#678, #794)."
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

    problems: list[str] = []

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
            # No `cap.where` prefix here: every GateError check_cap raises already carries one, and
            # re-prefixing printed the locator twice in the same sentence.
            problems.append(str(e))
            continue
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

    print(
        "\nok: every annotated pin equals the newest version on the registry Renovate reads, and "
        "every allowedVersions cap still has the reason it was written for."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
