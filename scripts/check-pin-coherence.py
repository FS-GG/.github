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
    newest,
    nuget_org_versions,
    parse_version,
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

        def resolve(pkg: str) -> list[str]:
            if pkg not in table:
                raise GateError(f"package {pkg!r} is not on the registry (fixture: absent)")
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

        # Read the registry RENOVATE reads (nuget.org, anonymous), not the org feed. Comparing a pin
        # against a registry the bot cannot see is how a gate demands a bump that can never open —
        # the token is still needed below, but for the GitHub API (dashboard + PRs), not for this.
        def resolve(pkg: str) -> list[str]:
            return nuget_org_versions(pkg)

        _items: list = []  # the dep-independent search, fetched at most once per run

        def evidence(dep: str) -> BotEvidence:
            if not _items:
                _items.extend(_search_all(repo, token))
            return gather_bot_evidence(repo, token, dep, items=_items)

    try:
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

    print(f"comparing {len(pins)} annotated pin(s) against {', '.join(hosts)} (what Renovate reads):")
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

    print("\nok: every annotated pin equals the newest version on the registry Renovate reads.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
