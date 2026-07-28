#!/usr/bin/env python3
"""Gate the PUBLISHED FS.GG.Kit against canonical (.github#1291, epic #266).

THE DEFECT THIS CLOSES. FS.GG.Kit (ADR-0062) ships the coordination kit — the shared skills, the
`fsgg-coord` client, and the engine tool manifest `.config/dotnet-tools.json` — as ONE versioned
package. A receiver on `kit-delivery: package` MATERIALIZES that kit onto disk from its pinned
version, so *the package's content is what the fleet actually restores*. `verify-package.sh` proves
the kit is derived-correct AT PACK TIME (its manifest digests == registry/repos.lock). Nothing
rechecked it AFTER publish, as canonical moves.

So the published package is a scalar no gate looked at — the same shape as the engine pin before
`engine-pin-coherence` (.github#1196): a bump to a kit source lands on main, `repos.lock` advances,
every PACK-TIME gate stays green, and the PUBLISHED kit silently carries the old bytes until someone
republishes. That is exactly .github#1291: the `fs.gg.coord.cli` engine pin advanced 0.6.0 -> 0.7.0
(#660) in the manifest, but the published FS.GG.Kit 0.1.0 still carried the 0.6.0 manifest, so the
first receiver to MATERIALIZE FROM SCRATCH (FS.GG.Net) got a kit that drifted from canonical and
`coordination-coherence` red — while every other gate was green.

`engine-pin-coherence` guards the PIN the fleet copies; this guards the PACKAGE the fleet
materializes. They are the same #266 lesson one layer apart: a subject nothing watches goes stale in
silence.

WHAT IT ASSERTS.

The same `src/FS.GG.Kit/stage-kit.sh` used at pack time derives a canonical `kit-manifest.tsv` from
the current kit tree. The gate compares every published coordination-kit row to that manifest by
kind, package path, receiver destination, content digest, and executable bit. Missing, extra,
changed, or wrong-mode members are all drift. `scripts/repos.sh validate` runs first, so
`registry/repos.lock` remains the declared-source integrity gate; the tree manifest complements the
scalar lock for multi-file skill auxiliaries rather than replacing or weakening it.

build-config members are EXCLUDED, exactly as in `verify-package.sh`: they carry no repos.lock row
(ADR-0036 pin model — their sha256 is a self-consistent integrity record, checked at materialize),
so there is nothing in repos.lock to match them against.

THE COMPARISON POINT IS nuget.org, AND THAT IS DELIBERATE. Five of the six kit receivers restore
FS.GG.* from PUBLIC nuget.org (ADR-0039); it is the registry the fleet actually materializes from,
so it is the registry the published kit must be measured on — the same "measure against what a
receiver can actually restore" reasoning as `engine-pin-coherence`. Reading is anonymous, so this
gate needs no token.

WHY THIS IS NOT A pull_request live gate (it mirrors engine-pin-coherence / engine-freshness). The
staleness is a property of MAIN (repos.lock) plus the FEED (the published kit), not of any PR's
diff: #660 changed the manifest and touched nothing under src/FS.GG.Kit/, so no path-filtered PR
trigger could ever see it, and it is fixed by REPUBLISHING (release-kit.yml), not by editing some
other PR. So the live check runs on main, on a schedule, and on demand; a PR runs the fixture, whose
subject IS the diff.

FAILS CLOSED, the whole point of epic #266. "Nothing to check" and "checked, and it's fine" must not
share an exit code. Every one of these is an ERROR, never a skip and never "coherent":

  * registry/repos.lock is unreadable, empty, or stale against its declared sources;
  * the canonical kit cannot be staged or its manifest is malformed/empty;
  * the feed is unreachable / unauthorised / returns an unrecognised shape, or serves zero stable
    versions (the kit ships no prerelease — the shared Renovate preset sets ignoreUnstable=true, so
    a prerelease kit would be invisible to the fabric meant to carry it);
  * the published nupkg cannot be downloaded, is not a zip, or carries no `kit/kit-manifest.tsv`;
  * that manifest is empty, malformed, or names zero coordination-kit members — the subject this
    gate measures has vanished, the #266 unwatched-subject shape;
  * a manifest row is not the fixed 5-field
    `kind<TAB>pkgrel<TAB>dest<TAB>sha<TAB>executable` shape.

Comparison is by exact 64-hex digest, never by substring.

THE PR ARM (`--pr-arm`, .github#1597). Everything above is the verdict of record that the release
ACTUALLY HAPPENED, and it stays exactly as it is: it is the only thing that observes the published
package, and only `main` plus the feed can answer that. But it is also, by construction, a verdict
that arrives too late to act on. A PR that edits a `kit:` source merges GREEN — this workflow's live
job is `if: github.event_name != 'pull_request'` — and the repo learns the published kit went stale
on the POST-MERGE run, by which time every `coordination-kit` receiver is already carrying bytes that
disagree with canonical. That happened twice in one morning: `edc8404` (#1581) and `0e1c5d0` (#1591),
the second 40 minutes after a release greened the first, on a different kit source.

So this arm moves the AUTHORING obligation to the moment it can still be met — the PR — without
touching the arm that observes the release. Two arms, two subjects, one file, because they share the
kit-source list, the feed reader and the NuGet ordering, and a second copy of any of those is how two
gates end up disagreeing about what "newest" means (#263).

THE NAIVE RULE DOES NOT WORK, AND IT IS WORTH BEING PRECISE ABOUT WHY. The obvious PR check is *"a PR
touching a kit source must bump `<Version>`"*. **That rule greens `edc8404`, the first incident**: it
bumped `0.8.0 -> 0.8.1` and `main` was still RED afterwards, because a bump is not a publish. Nobody
had run the release. A rule that scores the first of two incidents green is not a gate, it is a
formality.

The rule that separates all three cases compares against THE FEED, not the tree's own history — the
same comparand discipline `scripts/repos-audit.sh` already spells out for the pin sweep ("THE
COMPARAND IS THE FEED, NOT THIS TREE"):

    if a PR's diff touches any `kit:` source in registry/repos.yml,
    then src/FS.GG.Kit/FS.GG.Kit.csproj <Version> must be STRICTLY GREATER
    than the newest STABLE FS.GG.Kit on nuget.org.

  * `0e1c5d0` / #1591 — touches `scripts/fsgg-coord`; `<Version>` 0.8.1, published 0.8.1.
    `0.8.1 > 0.8.1` is false -> RED at PR time. Caught, which is the whole point.
  * `edc8404` / #1581 — touches two kit sources; `<Version>` 0.8.1, published 0.8.0 -> green, and
    CORRECTLY so: the bump was real and the release followed it.
  * a bump already landed and not yet released, then a second kit-touching PR — green, and correctly
    so. Between a bump and its release the tree is legitimately ahead of the feed, and the second PR
    simply rides into the pending release. The naive rule would demand a pointless second bump.
  * a PR touching no kit source -> NOT EVALUATED, and it never reads the network.

WHAT THIS ARM DELIBERATELY DOES NOT DO: pick the version. `0e1c5d0` was receiver-visible BEHAVIOUR, so
`0.9.0` was the right answer and `0.8.2` was not, and no gate can tell those apart. The arithmetic
stays human (#1597 review). This says only "the number you are shipping is not ahead of the one the
fleet can already restore", which is a fact, not a judgement.

THE KIT-SOURCE LIST IS READ FROM registry/repos.yml, NEVER RESTATED — not here, and not in the
workflow's trigger. A restated list is stale the day a `kit:` row lands, and this workflow's PR
trigger USED to carry a `paths:` filter naming only this gate's own files, which is why a kit-source
PR did not even start it. That is `.github#1606`'s shape inverted: a gate whose subject is not in its
trigger set. The trigger is now UNFILTERED on `pull_request`, exactly as the `push` trigger already
is and for the same reason — the subject is not any one path — and the arm no-ops (exit 0, no network)
on a PR that touches nothing.

FAILS CLOSED, like everything else here (#266). No feed verdict is RED, never green: an unreachable
or rate-limited nuget.org means we cannot tell whether the bump is sufficient, and "cannot tell" must
not merge. The network is only reached once a kit source IS touched, so an outage cannot block PRs
that had no obligation in the first place.

THE TAG ARM (`--tag-arm`, .github#1784). `#1772` promoted `kit/v*` from decoration to a TRUST
ANCHOR. The receiver-side `materialize / kit-bump-shape` reporter resolves the rule it runs like
this:

    dotnet restore  ->  project.assets.json names the resolved FS.GG.Kit version
    that version    ->  the tag `kit/v<version>`
    that tag        ->  peeled to a 40-hex COMMIT; the rule is checked out THERE

That is the right fix — the verdict became a function of what the receiver actually restores rather
than of the hub's moving `main` (ADR-0067 §2, #1584). But it makes a **mutable ref** load-bearing.
This file's header used to say, in as many words, that a `kit/v*` tag "is a COHERENCE CHECK against
the csproj `<Version>`, never the source of truth". It is the source of truth for a rule now, and a
tag deleted or force-moved AFTER publication silently recreates the exact defect `#1772` closed:
the reporter resolves a rule out of a tree that is not that release, or refuses on a version that is
on the feed. `release-kit.yml`'s gate is PUBLISH-TIME ONLY and cannot see that.

THE COMPARAND IS THE ARTIFACT, NOT A LIST. The interesting question is not "does the tag exist" —
that is checkable against memory, and memory is what went wrong. It is **"does the tag still resolve
to the commit that produced the published package?"**, and the published package answers it itself.
Every FS.GG.Kit `.nuspec` on nuget.org carries SourceLink's repository binding:

    <repository type="git" url="https://github.com/FS-GG/.github" commit="<40-hex>" />

A published package is IMMUTABLE; a tag is not. So the nuspec is a fixed point the mutable ref can
be measured against, and the measurement needs no record anyone has to maintain. Measured across all
23 published versions on 2026-07-28: every one binds a commit, and every one of the 21 that had a
tag agreed with it exactly. That is why this arm can assert equality rather than mere existence.

    for every STABLE version nuget.org serves:
        the nuspec must bind a 40-hex commit in THIS repository,
        the tag `kit/v<version>` must exist,
        and `git ls-remote` (peeled) must resolve it to exactly that commit.

WHAT THIS ARM CANNOT ASSERT, stated because a check whose limits are unwritten gets trusted past
them (#266):

  * It does not prove the nuspec's commit is HONEST. The same pack that produced the package wrote
    it, so a publish from a compromised or hand-crafted tree could name any sha. What it proves is
    that the tag and the artifact still AGREE — which is exactly the post-publish mutation this
    issue is about, because the artifact can no longer change and the tag can.
  * It says nothing about versions the feed has unlisted or deleted. Its subject is what a receiver
    can restore today.
  * It does not check the tag's TREE against the package's bytes. That is the default arm's job for
    the newest version, and it is not re-derivable for old ones (stage-kit.sh has itself moved).
  * A `kit/v*` tag with NO published version is reported, never red. Between `release-kit.yml`
    pushing a tag and nuget.org indexing the package there is a window in which precisely that is
    true, and a red there would make every release red `main` on its way through.

THE ASYMMETRY IS DELIBERATE, and it is the only direction that can be a defect: `#1772` already made
the tag a PRECONDITION of publishing (`release-kit.yml` refuses unless `kit/v<version>` exists AND
points at the commit being packed), so any release that publishes at all satisfies this arm on the
way in. A violation therefore means the tag moved AFTERWARDS — which is the whole subject.

Usage:  scripts/check-kit-published-coherence.py [--lock registry/repos.lock]
        scripts/check-kit-published-coherence.py --pr-arm [--base <ref-or-sha>]
        scripts/check-kit-published-coherence.py --tag-arm [--remote <url>]

`--fixture-manifest <tsv> --canonical-manifest <tsv>` compares canned manifests and refuses to run
unless FSGG_KIT_COHERENCE_FIXTURE_OK=1 — which only tests/kit-published-coherence/ sets. A test hook
that can silently turn the gate into a no-op is the very defect class above. The PR arm's canned
inputs (`--changed-files`, `--kit-sources`, `--published-version`) are locked behind the same switch,
for the same reason: each one of them, left open, is a way to make the arm answer without reading its
subject.

Exit 0 = the newest published FS.GG.Kit carries the same coordination-kit bytes canonical derives
(default arm), or this PR incurs no republish obligation it has not already met (`--pr-arm`).
"""
from __future__ import annotations

import argparse
import io
import json
import os
import re
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request
import zipfile
from dataclasses import dataclass

# Shared feed reader + NuGet version ordering (.github#263) — one implementation of "what does the
# feed serve", so the gates cannot drift into disagreeing about version order. `scripts/` is not a
# package, so put this file's own directory on the path (the test harness loads this gate by path).
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from fsgg_feed import (  # noqa: E402  (path shim above must run first)
    NUGET_ORG,
    GateError,
    is_prerelease,
    newest,
    nuget_org_versions,
    parse_version,
)

# The package the fleet materializes (ADR-0062). Its content — not just its version — is the subject.
PACKAGE = "FS.GG.Kit"
LOCK = "registry/repos.lock"
# The PR arm's two authored subjects: where the kit sources are DECLARED, and where the version a PR
# proposes to ship is authored. Neither is restated anywhere in this file or in the workflow.
ROSTER = "registry/repos.yml"
KIT_CSPROJ = "src/FS.GG.Kit/FS.GG.Kit.csproj"
_VERSION_ELEMENT = re.compile(r"<Version>\s*([^<\s][^<]*?)\s*</Version>")
REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STAGE_KIT = os.path.join(REPO_ROOT, "src", "FS.GG.Kit", "stage-kit.sh")
REPOS_TOOL = os.path.join(REPO_ROOT, "scripts", "repos.sh")

# coordination-kit members carry a repos.lock digest; build-config members deliberately do not
# (ADR-0036), exactly as verify-package.sh partitions them.
COORDINATION_KINDS = frozenset({"skill", "client", "config"})
_HEX64 = re.compile(r"\A[0-9a-f]{64}\Z")

# --- the tag arm (.github#1784) -------------------------------------------------------------------
# The tag scheme the #1772 resolver uses. Written once here; the workflow restates nothing.
TAG_PREFIX = "kit/v"
# The repository a published kit must name. `GITHUB_REPOSITORY` is authoritative in CI; the literal is
# the fallback for a local run. This is asserted AGAINST the nuspec rather than read FROM it: taking
# the remote from the artifact would let a package published out of a fork redirect its own check.
DEFAULT_REPOSITORY = "FS-GG/.github"
_HEX40 = re.compile(r"\A[0-9a-f]{40}\Z")
# The #1772 resolver accepts a bare `x.y.z` and nothing else, so that is the tag grammar this arm
# matches. A `kit/v*` ref outside it (`kit/vnext`, `kit/v1.2`) can never be resolved from a pin and is
# reported as unmatched rather than parsed into a version this arm would then invent.
_KIT_TAG_REF = re.compile(r"\Arefs/tags/" + re.escape(TAG_PREFIX) + r"(\d+\.\d+\.\d+)(\^\{\})?\Z")


def _repository_slug() -> str:
    """`owner/name` of the repository whose tags are the anchor. Never read from the artifact."""
    slug = (os.environ.get("GITHUB_REPOSITORY") or "").strip()
    return slug or DEFAULT_REPOSITORY


def _fetch_nuspec(version: str) -> bytes:
    """The published .nuspec for FS.GG.Kit@version, from the flat container.

    The nuspec is served as its own ~1 KB document, so this arm does not pay for 23 .nupkg
    downloads to read 23 one-line bindings. Any failure raises — an unreadable nuspec is a version
    whose tag CANNOT BE CHECKED, and #266 is precisely that "I could not evaluate this" must never
    be reported as "I evaluated it and it passed".
    """
    lid = PACKAGE.lower()
    url = f"{NUGET_ORG}/{lid}/{version}/{lid}.nuspec"
    req = urllib.request.Request(url, headers={"User-Agent": "fsgg-check-kit-coherence"})
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            return resp.read()
    except urllib.error.HTTPError as e:
        raise GateError(
            f"cannot read the published {PACKAGE} {version} .nuspec from nuget.org "
            f"(HTTP {e.code} {e.reason}) — the feed serves this version, so its tag binding is a "
            f"question this gate must answer, and an unanswerable one is not a pass."
        ) from e
    except urllib.error.URLError as e:
        raise GateError(
            f"nuget.org unreachable while reading the {PACKAGE} {version} .nuspec: {e.reason}"
        ) from e


def nuspec_repository_commit(version: str, nuspec: bytes, *, repository: str) -> str:
    """The 40-hex commit the PUBLISHED nuspec binds `version` to.

    Parsed as XML, not grepped: the nuspec's namespace has changed across schema versions and a
    regex over markup is how a gate ends up matching a commented-out element. Every absence is a
    GateError — a version whose artifact names no commit has no fixed point to measure its mutable
    tag against, and that is an unresolved verdict, not a green one.
    """
    import xml.etree.ElementTree as ET  # lazy: only the tag arm parses XML.

    try:
        root = ET.fromstring(nuspec)
    except ET.ParseError as e:
        raise GateError(f"the published {PACKAGE} {version} .nuspec is not parsable XML: {e}") from e
    repo_elements = [el for el in root.iter() if el.tag.rsplit("}", 1)[-1] == "repository"]
    if len(repo_elements) != 1:
        raise GateError(
            f"the published {PACKAGE} {version} .nuspec carries {len(repo_elements)} <repository> "
            f"element(s); this arm needs exactly one to know which commit produced the artifact. "
            f"Without it the tag kit/v{version} can only be compared to a list someone maintains, "
            f"which is the failure mode .github#1784 exists to remove."
        )
    element = repo_elements[0]
    commit = (element.get("commit") or "").strip().lower()
    if not _HEX40.match(commit):
        raise GateError(
            f"the published {PACKAGE} {version} .nuspec <repository> names no 40-hex commit "
            f"(commit={element.get('commit')!r}). SourceLink writes this at pack time; a package "
            f"without it cannot anchor its own tag."
        )
    url = (element.get("url") or "").strip()
    normalized = url.rstrip("/").removesuffix(".git").lower()
    if not normalized.endswith("/" + repository.lower()):
        raise GateError(
            f"the published {PACKAGE} {version} .nuspec was packed from {url!r}, not {repository} — "
            f"its commit names a history whose tags are not the ones the fleet resolves against."
        )
    return commit


def remote_kit_tags(remote: str) -> dict[str, str]:
    """`version -> the commit kit/v<version> resolves to`, read from the remote, PEELED.

    Peeled on purpose and exactly as the #1772 resolver does it: an annotated tag's own object id is
    not the commit the rule would be checked out at, and comparing it to the nuspec's commit would
    red every annotated release. `refs/tags/X^{}` therefore always wins over `refs/tags/X`.

    A git failure raises. An empty answer does NOT: a repository with no kit tags at all is a real
    (catastrophic) state this arm must report per-version, not a read error to be confused with it.
    """
    try:
        result = subprocess.run(
            ["git", "ls-remote", "--tags", remote, f"refs/tags/{TAG_PREFIX}*"],
            text=True,
            capture_output=True,
            check=False,
            timeout=120,
        )
    except (OSError, subprocess.SubprocessError) as e:
        raise GateError(f"cannot list {TAG_PREFIX}* tags on {remote!r}: {e}") from e
    if result.returncode != 0:
        detail = (result.stderr or result.stdout).strip()
        raise GateError(
            f"cannot list {TAG_PREFIX}* tags on {remote!r}"
            + (f": {detail}" if detail else "")
            + " — a tag set this gate cannot read is an UNRESOLVED verdict for every published "
            "version, never a passing one (#266)."
        )
    return parse_ls_remote_tags(result.stdout)


def parse_ls_remote_tags(text: str) -> dict[str, str]:
    """Parse `git ls-remote` output into `version -> peeled commit`. Malformed rows are errors."""
    direct: dict[str, str] = {}
    peeled: dict[str, str] = {}
    for lineno, raw in enumerate(text.splitlines(), 1):
        if not raw.strip():
            continue
        parts = raw.split("\t")
        if len(parts) != 2:
            raise GateError(f"ls-remote line {lineno} is not `<sha>\\t<ref>`: {raw!r}")
        sha, ref = parts[0].strip().lower(), parts[1].strip()
        if not _HEX40.match(sha):
            raise GateError(f"ls-remote line {lineno} carries a non-sha object id {sha!r}")
        match = _KIT_TAG_REF.match(ref)
        if not match:
            continue  # not a `kit/v<x.y.z>` ref — no pin can resolve to it; reported, not parsed.
        (peeled if match.group(2) else direct)[match.group(1)] = sha
    return {**direct, **peeled}


def run_tag_arm(
    *,
    remote: str,
    repository: str,
    canned_published: str | None,
    canned_tags: str | None,
) -> int:
    """.github#1784. Exit 0 = every published version's tag still resolves to its artifact's commit."""

    def read_canned(path: str, what: str) -> str:
        try:
            return open(path, encoding="utf-8").read()
        except OSError as e:
            raise GateError(f"cannot read the canned {what} {path!r}: {e}") from e

    # `version<TAB>commit`, one per line — the nuspec read, canned. `-` means the artifact binds no
    # commit, so the fixture can exercise the unanchorable case without a network.
    bindings: dict[str, str] = {}
    if canned_published:
        for lineno, raw in enumerate(read_canned(canned_published, "published-version list").splitlines(), 1):
            if not raw.strip():
                continue
            parts = raw.split("\t")
            if len(parts) != 2:
                raise GateError(f"canned published line {lineno} is not `<version>\\t<commit>`: {raw!r}")
            version, commit = parts[0].strip(), parts[1].strip().lower()
            # Constrained EXACTLY as the live nuspec read is, and refused with the same words. A
            # canned input the gate validates more loosely than its real subject is a fixture that
            # can green a shape production would red.
            if not _HEX40.match(commit):
                raise GateError(
                    f"the published {PACKAGE} {version} .nuspec <repository> names no 40-hex commit "
                    f"(commit={None if commit == '-' else commit!r}). SourceLink writes this at pack "
                    f"time; a package without it cannot anchor its own tag."
                )
            bindings[version] = commit
    else:
        live = nuget_org_versions(PACKAGE)  # raises on 404/unreachable/empty — never []
        stable = [v for v in live if not is_prerelease(v)]
        if not stable:
            raise GateError(
                f"nuget.org serves no stable version of {PACKAGE} — only prereleases {sorted(live)}. "
                f"There is nothing whose tag this arm can anchor, and an empty subject is not a pass."
            )
        for version in sorted(stable, key=parse_version):
            bindings[version] = nuspec_repository_commit(
                version, _fetch_nuspec(version), repository=repository
            )

    tags = parse_ls_remote_tags(read_canned(canned_tags, "ls-remote tag list")) if canned_tags \
        else remote_kit_tags(remote)

    missing: list[str] = []
    moved: list[tuple[str, str, str]] = []
    for version, commit in bindings.items():
        resolved = tags.get(version)
        if resolved is None:
            missing.append(version)
        elif resolved != commit:
            moved.append((version, resolved, commit))

    untagged_versions = sorted(set(tags) - set(bindings), key=parse_version)

    if not missing and not moved:
        note = ""
        if untagged_versions:
            # NEVER an error. release-kit.yml pushes the tag BEFORE nuget.org indexes the package, so
            # this is the normal state of a release in flight; reddening it would make every release
            # red main on its way through.
            note = (
                f"\n  note: {len(untagged_versions)} {TAG_PREFIX}* tag(s) name no published version "
                f"({', '.join(untagged_versions)}). Not an error — release-kit.yml pushes the tag "
                f"before the feed indexes the package, and no receiver can pin a version that was "
                f"never published."
            )
        print(
            f"ok: all {len(bindings)} stable {PACKAGE} version(s) on nuget.org carry a "
            f"{TAG_PREFIX}<version> tag in {repository}, and every tag still resolves (peeled) to "
            f"the exact commit its published .nuspec was packed from. The ref .github#1772's "
            f"bump-shape resolver trusts has not moved since publication.{note}"
        )
        return 0

    details: list[str] = []
    for version in sorted(missing, key=parse_version):
        details.append(
            f"    MISSING  {TAG_PREFIX}{version} — published, but no such tag. Its artifact was "
            f"packed from {bindings[version]}; create it with:\n"
            f"        git tag {TAG_PREFIX}{version} {bindings[version]} && "
            f"git push origin {TAG_PREFIX}{version}"
        )
    for version, resolved, commit in sorted(moved, key=lambda row: parse_version(row[0])):
        details.append(
            f"    MOVED    {TAG_PREFIX}{version} resolves to {resolved}, but the published .nuspec "
            f"was packed from {commit}. The tag was changed after publication."
        )
    print(
        f"::error::check-kit-published-coherence (tag-arm): the {TAG_PREFIX}* tags .github#1772's "
        f"bump-shape resolver trusts no longer agree with the packages the fleet restores "
        f"({len(missing)} missing, {len(moved)} moved):\n" + "\n".join(details) + "\n"
        f"A receiver whose pin names a MISSING tag gets a REFUSED from `materialize / "
        f"kit-bump-shape` and cannot be graded at all. A receiver whose pin names a MOVED tag is "
        f"graded by a rule from a tree that is not its release — the exact defect .github#1772 "
        f"closed, reopened through a mutable ref. The published .nuspec is the fixed point here: it "
        f"is immutable and the tag is not, so the tag is what to repair.",
        file=sys.stderr,
    )
    return 1


def read_lock_digests(lock_path: str) -> set[str]:
    """Every declared-source digest in repos.lock. Absence/emptiness is never a green baseline."""
    try:
        text = open(lock_path, encoding="utf-8").read()
    except OSError as e:
        raise GateError(f"cannot read the canonical lock {lock_path!r}: {e}") from e
    digests = set()
    for line in text.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        # repos.lock rows are `<sha256>  <path>` (two-space separated, sha256sum style).
        sha = line.split(None, 1)[0].lower()
        if _HEX64.match(sha):
            digests.add(sha)
    if not digests:
        raise GateError(
            f"registry/repos.lock ({lock_path!r}) yielded no digests — the canonical set this gate "
            f"compares against is unreadable, and an empty baseline must not read as 'coherent'."
        )
    return digests


def validate_live_lock() -> None:
    """Keep repos.lock authoritative before deriving the multi-file canonical manifest."""
    try:
        result = subprocess.run(
            ["bash", REPOS_TOOL, "validate"],
            cwd=REPO_ROOT,
            text=True,
            capture_output=True,
            check=False,
        )
    except OSError as e:
        raise GateError(f"cannot run the repos.lock integrity gate: {e}") from e
    if result.returncode != 0:
        detail = (result.stderr or result.stdout).strip()
        raise GateError(
            "registry/repos.lock is not valid against the declared kit sources"
            + (f": {detail}" if detail else "")
        )


def stage_canonical_manifest() -> str:
    """Derive the exact pack-time kit manifest from the current tree."""
    try:
        with tempfile.TemporaryDirectory(prefix="fsgg-kit-coherence-") as work:
            out = os.path.join(work, "kit")
            result = subprocess.run(
                ["bash", STAGE_KIT, out],
                cwd=REPO_ROOT,
                text=True,
                capture_output=True,
                check=False,
            )
            if result.returncode != 0:
                detail = (result.stderr or result.stdout).strip()
                raise GateError(
                    "cannot derive the canonical kit manifest with stage-kit.sh"
                    + (f": {detail}" if detail else "")
                )
            try:
                return open(
                    os.path.join(out, "kit-manifest.tsv"), encoding="utf-8"
                ).read()
            except OSError as e:
                raise GateError(f"canonical stage emitted no readable kit-manifest.tsv: {e}") from e
    except OSError as e:
        raise GateError(f"cannot create the canonical staging directory: {e}") from e


def newest_published_stable() -> str:
    """The newest STABLE FS.GG.Kit on nuget.org. Raises on any unreadable/empty/prerelease-only feed."""
    live = nuget_org_versions(PACKAGE)  # raises GateError on 404/unreachable/empty — never []
    stable = [v for v in live if not is_prerelease(v)]
    if not stable:
        raise GateError(
            f"nuget.org serves no stable version of {PACKAGE} — only prereleases {sorted(live)}. "
            f"release-kit.yml refuses to publish a prerelease (the shared Renovate preset sets "
            f"ignoreUnstable=true, so receivers would never see it), so the fleet's kit cannot be "
            f"one and the comparison point is unknown."
        )
    return newest(stable)


def _download_nupkg(version: str) -> bytes:
    """The published FS.GG.Kit@version .nupkg bytes from nuget.org. Any failure raises — never b''."""
    lid = PACKAGE.lower()
    url = f"{NUGET_ORG}/{lid}/{version}/{lid}.{version}.nupkg"
    req = urllib.request.Request(url, headers={"User-Agent": "fsgg-check-kit-coherence"})
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            return resp.read()
    except urllib.error.HTTPError as e:
        raise GateError(
            f"cannot download {PACKAGE} {version} from nuget.org (HTTP {e.code} {e.reason}) — the "
            f"feed named it but will not serve it; a package this gate cannot read must fail, not skip."
        ) from e
    except urllib.error.URLError as e:
        raise GateError(f"nuget.org unreachable while downloading {PACKAGE} {version}: {e.reason}") from e


def manifest_from_nupkg(nupkg: bytes) -> str:
    """The `kit/kit-manifest.tsv` text inside a FS.GG.Kit .nupkg. Any absence is a GateError."""
    try:
        with zipfile.ZipFile(io.BytesIO(nupkg)) as z:
            try:
                return z.read("kit/kit-manifest.tsv").decode("utf-8")
            except KeyError as e:
                raise GateError(
                    "the published FS.GG.Kit carries no kit/kit-manifest.tsv — the manifest this "
                    "gate reads is gone, and a package with no manifest is not 'coherent' (#266)."
                ) from e
    except zipfile.BadZipFile as e:
        raise GateError(f"the downloaded FS.GG.Kit is not a valid .nupkg (zip): {e}") from e


@dataclass(frozen=True)
class ManifestEntry:
    kind: str
    package_path: str
    destination: str
    sha256: str
    executable: bool


def coordination_entries(manifest_tsv: str, subject: str) -> dict[str, ManifestEntry]:
    """Map receiver destination to each exact coordination-kit manifest row."""
    out: dict[str, ManifestEntry] = {}
    for lineno, raw in enumerate(manifest_tsv.splitlines(), 1):
        if not raw.strip():
            continue
        parts = raw.split("\t")
        if len(parts) != 5:
            raise GateError(
                f"{subject} kit-manifest.tsv line {lineno} is not the 5-field "
                "kind<TAB>pkgrel<TAB>dest<TAB>sha<TAB>executable "
                f"shape (got {len(parts)} field(s)): {raw!r}"
            )
        kind, package_path, dest, sha, executable_raw = parts
        if kind not in COORDINATION_KINDS:
            continue  # build-config etc. — no repos.lock row to match against (verify-package.sh §1)
        sha = sha.strip().lower()
        if not _HEX64.match(sha):
            raise GateError(
                f"{subject} kit-manifest.tsv line {lineno} carries a non-sha256 digest {sha!r}"
            )
        if executable_raw not in ("true", "false"):
            raise GateError(
                f"{subject} kit-manifest.tsv line {lineno} carries an invalid executable bit "
                f"{executable_raw!r}"
            )
        if dest in out:
            raise GateError(
                f"{subject} kit-manifest.tsv names receiver destination {dest!r} more than once"
            )
        out[dest] = ManifestEntry(
            kind=kind,
            package_path=package_path,
            destination=dest,
            sha256=sha,
            executable=executable_raw == "true",
        )
    if not out:
        raise GateError(
            f"the {subject} manifest names zero coordination-kit members (skill/client/config) — "
            "the subject this gate measures has vanished; that is an ERROR, not 'coherent'."
        )
    return out


def kit_sources(roster_path: str) -> list[str]:
    """Every `source:` in repos.yml's `kit:` block. Read, never restated (.github#1597 AC2).

    A new kit row is therefore covered the day it lands — including the four non-client skill rows,
    which #1586's shim-shrink does not remove. An absent, non-list, or empty `kit:` block is a
    GateError rather than an empty set: an empty set silently switches the whole arm off, and a check
    that disables itself on a bad read is the fail-open (#266) this file exists to refuse.
    """
    import yaml  # lazy: the fixture arm never parses YAML, and so need not depend on PyYAML.

    try:
        text = open(roster_path, encoding="utf-8").read()
    except OSError as e:
        raise GateError(f"cannot read the kit roster {roster_path!r}: {e}") from e
    try:
        roster = yaml.safe_load(text)
    except yaml.YAMLError as e:
        raise GateError(f"{roster_path} is not parsable as YAML: {e}") from e
    if not isinstance(roster, dict):
        raise GateError(f"{roster_path} is not a YAML mapping")

    kit = roster.get("kit")
    if not isinstance(kit, list) or not kit:
        raise GateError(
            f"{roster_path}: `kit:` is missing or not a non-empty list — this arm cannot tell which "
            f"sources oblige a republish, and 'cannot tell' is not 'nothing to do'."
        )
    sources: list[str] = []
    for index, row in enumerate(kit):
        if not isinstance(row, dict):
            raise GateError(f"{roster_path}: kit[{index}] is not a mapping")
        source = row.get("source")
        if not isinstance(source, str) or not source.strip():
            raise GateError(
                f"{roster_path}: kit[{index}] has no usable `source` ({source!r}) — a kit row whose "
                f"source cannot be read would silently drop out of this arm's subject "
                f"(run: scripts/repos.sh validate)."
            )
        sources.append(source.strip().rstrip("/"))
    return sources


def changed_paths(base: str) -> list[str]:
    """Repo-relative paths this PR changes, as `git diff --name-only <base>...HEAD`.

    Three-dot on purpose: it diffs the merge base against HEAD, so changes that landed on the BASE
    branch after the PR forked are not attributed to the PR. Two-dot would blame a kit-source commit
    from someone else's merge on whichever PR happened to be open, and demand a bump from a diff that
    does not contain one.

    Any git failure is a GateError. A PR whose diff cannot be read has an UNKNOWN obligation, and an
    unknown obligation must not merge (#266) — an empty list here would read as "touched nothing".
    """
    if not base or not base.strip():
        raise GateError(
            "the PR arm has no base ref to diff against (pass --base, or set GITHUB_BASE_REF) — "
            "without one there is no diff, and no diff is not 'no kit sources touched'."
        )
    try:
        result = subprocess.run(
            ["git", "diff", "--name-only", f"{base.strip()}...HEAD"],
            cwd=REPO_ROOT,
            text=True,
            capture_output=True,
            check=False,
        )
    except OSError as e:
        raise GateError(f"cannot run git to read this PR's diff: {e}") from e
    if result.returncode != 0:
        detail = (result.stderr or result.stdout).strip()
        raise GateError(
            f"cannot diff against base {base!r}"
            + (f": {detail}" if detail else "")
            + " — check out enough history (actions/checkout `fetch-depth: 0`) so the merge base "
            "resolves; a diff this arm cannot compute is a no-verdict, not a green."
        )
    return [line for line in result.stdout.splitlines() if line.strip()]


def touched_kit_sources(changed: list[str], sources: list[str]) -> list[tuple[str, str]]:
    """(changed path, kit source) for every changed path that IS or lives UNDER a kit source.

    Kit sources are a mix of files (`scripts/fsgg-coord`) and directories (a skill root), so the test
    is exact-match OR prefix-with-separator. The separator is not optional: a bare `startswith` would
    make `.claude/skills/check-board-notes` match the `check-board` skill, and this arm would demand a
    republish for a file the kit does not ship.
    """
    hits: list[tuple[str, str]] = []
    for path in changed:
        for source in sources:
            if path == source or path.startswith(source + "/"):
                hits.append((path, source))
                break
    return hits


def declared_kit_version(csproj_path: str) -> str:
    """The single `<Version>` FS.GG.Kit.csproj authors. Zero or many is a GateError, not a guess."""
    try:
        text = open(csproj_path, encoding="utf-8").read()
    except OSError as e:
        raise GateError(f"cannot read {csproj_path!r} to learn the version this PR ships: {e}") from e
    found = _VERSION_ELEMENT.findall(text)
    if len(found) != 1:
        raise GateError(
            f"{csproj_path} declares {len(found)} <Version> element(s); this arm needs exactly one to "
            f"know what a merge would publish."
        )
    return found[0]


def run_pr_arm(
    *,
    roster_path: str,
    csproj_path: str,
    base: str,
    canned_changed: str | None,
    canned_sources: str | None,
    canned_published: str | None,
) -> int:
    """The .github#1597 rule. Exit 0 = no unmet republish obligation; 1 = RED (including no-verdict)."""

    def canned_lines(path: str, what: str) -> list[str]:
        try:
            raw = open(path, encoding="utf-8").read()
        except OSError as e:
            raise GateError(f"cannot read the canned {what} {path!r}: {e}") from e
        return [line.strip() for line in raw.splitlines() if line.strip()]

    sources = (
        canned_lines(canned_sources, "kit-source list")
        if canned_sources
        else kit_sources(roster_path)
    )
    changed = (
        canned_lines(canned_changed, "changed-file list")
        if canned_changed
        else changed_paths(base)
    )

    hits = touched_kit_sources(changed, sources)
    if not hits:
        print(
            f"ok: this PR changes {len(changed)} file(s), none of them a `kit:` source declared in "
            f"{roster_path} ({len(sources)} source(s) considered). No republish obligation, and the "
            f"feed was not read."
        )
        return 0

    declared = declared_kit_version(csproj_path)
    if is_prerelease(declared):
        raise GateError(
            f"{csproj_path} declares the prerelease <Version> {declared!r}. release-kit.yml refuses "
            f"to publish a prerelease and the shared Renovate preset sets ignoreUnstable=true, so no "
            f"receiver could ever restore it — a prerelease cannot discharge a republish obligation."
        )
    published = canned_published or newest_published_stable()

    touched_list = "\n".join(
        f"    {path}  (kit source: {source})" for path, source in sorted(hits)
    )
    if parse_version(declared) > parse_version(published):
        print(
            f"ok: this PR touches {len(hits)} `kit:` source file(s), and "
            f"{csproj_path} <Version> {declared} is ahead of the newest published {PACKAGE} "
            f"({published}) — merging it rides into a release that has not happened yet.\n"
            f"{touched_list}"
        )
        return 0

    print(
        f"::error::check-kit-published-coherence: this PR edits the coordination kit but ships a "
        f"version the fleet can ALREADY restore. {csproj_path} <Version> is {declared} and the newest "
        f"published {PACKAGE} on nuget.org is {published}; the rule is STRICTLY GREATER "
        f"({declared} > {published} is false).\n{touched_list}\n"
        f"Merging this leaves every `coordination-kit` receiver materializing {published}, whose bytes "
        f"no longer match canonical — `kit-published-coherence` reds on main immediately afterwards "
        f"and `coordination-coherence` reds in the receivers (.github#1291, #1591).\n"
        f"Bump <Version> in {csproj_path} to a version above {published}. Choose it yourself: patch "
        f"for a comment or doc edit, MINOR when the change is receiver-visible behaviour — a gate "
        f"cannot tell those apart, and #1591 needed the minor. Then release (tag kit/v<version> -> "
        f"release-kit.yml) after this merges; the main-only arm above stays the verdict that the "
        f"release actually happened.",
        file=sys.stderr,
    )
    return 1


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    ap.add_argument("--lock", default=LOCK, help=f"canonical digest lock (default: {LOCK})")
    ap.add_argument(
        "--fixture-manifest",
        help="read the published kit-manifest.tsv from a file (tests only, never in CI)",
    )
    ap.add_argument(
        "--canonical-manifest",
        help="read the canonical kit-manifest.tsv from a file (tests only; requires --fixture-manifest)",
    )
    ap.add_argument(
        "--pr-arm",
        action="store_true",
        help="the PR-time authoring rule (.github#1597): does this diff owe a kit republish?",
    )
    ap.add_argument("--base", default=os.environ.get("GITHUB_BASE_REF", ""), help="PR base ref/sha")
    ap.add_argument("--roster", default=ROSTER, help=f"kit-source declaration (default: {ROSTER})")
    ap.add_argument("--csproj", default=KIT_CSPROJ, help=f"kit project (default: {KIT_CSPROJ})")
    ap.add_argument("--changed-files", help="read this PR's changed paths from a file (tests only)")
    ap.add_argument("--kit-sources", help="read the kit-source list from a file (tests only)")
    ap.add_argument("--published-version", help="the newest published kit, canned (tests only)")
    ap.add_argument(
        "--tag-arm",
        action="store_true",
        help="the tag-integrity rule (.github#1784): does every published version's kit/v* tag still "
        "resolve to the commit its artifact was packed from?",
    )
    ap.add_argument(
        "--remote",
        default="",
        help="the git remote whose kit/v* tags are the anchor (default: this repository on github.com)",
    )
    ap.add_argument(
        "--tag-arm-published",
        help="read `<version>\\t<nuspec commit>` rows instead of the feed (tests only)",
    )
    ap.add_argument(
        "--tag-arm-tags",
        help="read canned `git ls-remote` output instead of the remote (tests only)",
    )
    args = ap.parse_args(argv)

    # EVERY canned input is locked behind the SAME switch as --fixture-manifest, and for the same
    # reason: each is a way to make a gate answer without reading its subject. --base/--roster/--csproj
    # are NOT locked — they redirect the read, they do not replace it, so a wrong one still fails.
    pr_arm_canned = {
        "--changed-files": args.changed_files,
        "--kit-sources": args.kit_sources,
        "--published-version": args.published_version,
    }
    # The tag arm's canned inputs are locked by the SAME switch, for the same reason: each replaces a
    # read of the arm's subject (the feed's nuspecs, the remote's refs) with an answer supplied on the
    # command line, and an unlocked one is a way to green a gate without measuring anything.
    tag_arm_canned = {
        "--tag-arm-published": args.tag_arm_published,
        "--tag-arm-tags": args.tag_arm_tags,
    }
    supplied = sorted(
        flag for flag, value in {**pr_arm_canned, **tag_arm_canned}.items() if value
    )
    if supplied and os.environ.get("FSGG_KIT_COHERENCE_FIXTURE_OK") != "1":
        print(
            f"::error::check-kit-published-coherence: {', '.join(supplied)} read canned input and are "
            f"NOT a coherence signal. They are available only to tests/kit-published-coherence/, which "
            f"sets FSGG_KIT_COHERENCE_FIXTURE_OK=1. Refusing to run.",
            file=sys.stderr,
        )
        return 1
    if args.pr_arm and args.tag_arm:
        print(
            "::error::check-kit-published-coherence: --pr-arm and --tag-arm are different arms with "
            "different subjects; run one or the other.",
            file=sys.stderr,
        )
        return 1
    # An arm's canned inputs mean nothing to the other arms, and a flag that is silently ignored is a
    # caller who believes they configured a run they did not get.
    running = "the tag arm" if args.tag_arm else "the published-package arm"
    misdirected = sorted(flag for flag, value in pr_arm_canned.items() if value) if not args.pr_arm else []
    if misdirected:
        print(
            f"::error::check-kit-published-coherence: {', '.join(misdirected)} are --pr-arm inputs and "
            f"mean nothing to {running}. Refusing to run rather than ignoring them.",
            file=sys.stderr,
        )
        return 1
    running = "the PR arm" if args.pr_arm else "the published-package arm"
    misdirected = sorted(flag for flag, value in tag_arm_canned.items() if value) if not args.tag_arm else []
    if misdirected:
        print(
            f"::error::check-kit-published-coherence: {', '.join(misdirected)} are --tag-arm inputs and "
            f"mean nothing to {running}. Refusing to run rather than ignoring them.",
            file=sys.stderr,
        )
        return 1

    if args.tag_arm:
        if args.fixture_manifest or args.canonical_manifest:
            print(
                "::error::check-kit-published-coherence: --tag-arm and the manifest fixture flags are "
                "different arms with different subjects; run one or the other.",
                file=sys.stderr,
            )
            return 1
        repository = _repository_slug()
        try:
            return run_tag_arm(
                remote=args.remote.strip() or f"https://github.com/{repository}.git",
                repository=repository,
                canned_published=args.tag_arm_published,
                canned_tags=args.tag_arm_tags,
            )
        except GateError as e:
            # #266: a tag this arm could not resolve is reported UNRESOLVED, never as valid.
            print(f"::error::check-kit-published-coherence (tag-arm): {e}", file=sys.stderr)
            return 1

    if args.pr_arm:
        if args.fixture_manifest or args.canonical_manifest:
            print(
                "::error::check-kit-published-coherence: --pr-arm and the manifest fixture flags are "
                "different arms with different subjects; run one or the other.",
                file=sys.stderr,
            )
            return 1
        try:
            return run_pr_arm(
                roster_path=args.roster,
                csproj_path=args.csproj,
                base=args.base,
                canned_changed=args.changed_files,
                canned_sources=args.kit_sources,
                canned_published=args.published_version,
            )
        except GateError as e:
            # AC3: a no-verdict is RED. We cannot tell whether the bump is sufficient, and "cannot
            # tell" must not merge.
            print(f"::error::check-kit-published-coherence (pr-arm): {e}", file=sys.stderr)
            return 1

    try:
        lock_digests = read_lock_digests(args.lock)

        if args.fixture_manifest:
            # A flag that lets the gate pass without reading the live package is the fails-open shape
            # epic #266 is about, so it is LOCKED, not merely documented as test-only.
            if os.environ.get("FSGG_KIT_COHERENCE_FIXTURE_OK") != "1":
                print(
                    "::error::check-kit-published-coherence: --fixture-manifest reads a canned "
                    "manifest and is NOT a coherence signal. It is available only to "
                    "tests/kit-published-coherence/, which sets FSGG_KIT_COHERENCE_FIXTURE_OK=1. "
                    "Refusing to run.",
                    file=sys.stderr,
                )
                return 1
            print(
                f"FIXTURE MODE — reading {args.fixture_manifest}, NOT the live package. "
                f"Not a coherence signal."
            )
            try:
                manifest_tsv = open(args.fixture_manifest, encoding="utf-8").read()
            except OSError as e:
                raise GateError(f"cannot read fixture manifest: {e}") from e
            if not args.canonical_manifest:
                raise GateError(
                    "--fixture-manifest requires --canonical-manifest; a published manifest without "
                    "its exact canonical comparison point is not a coherence signal"
                )
            try:
                canonical_tsv = open(args.canonical_manifest, encoding="utf-8").read()
            except OSError as e:
                raise GateError(f"cannot read canonical fixture manifest: {e}") from e
            version = "(fixture)"
        else:
            if args.canonical_manifest:
                raise GateError("--canonical-manifest is test-only and requires --fixture-manifest")
            validate_live_lock()
            canonical_tsv = stage_canonical_manifest()
            version = newest_published_stable()
            manifest_tsv = manifest_from_nupkg(_download_nupkg(version))

        canonical = coordination_entries(canonical_tsv, "canonical")
        shipped = coordination_entries(manifest_tsv, "published")
        canonical_digests = {entry.sha256 for entry in canonical.values()}
        absent_lock_digests = lock_digests - canonical_digests
        if absent_lock_digests:
            raise GateError(
                "canonical kit-manifest.tsv does not contain every declared-source digest from "
                f"registry/repos.lock ({len(absent_lock_digests)} missing)"
            )
    except GateError as e:
        print(f"::error::check-kit-published-coherence: {e}", file=sys.stderr)
        return 1

    missing = sorted(set(canonical) - set(shipped))
    extra = sorted(set(shipped) - set(canonical))
    changed = sorted(dest for dest in set(canonical) & set(shipped) if canonical[dest] != shipped[dest])
    if not missing and not extra and not changed:
        print(
            f"ok: the newest published {PACKAGE} ({version}) carries {len(shipped)} coordination-kit "
            "member(s), with the exact canonical destinations, bytes, modes, and closed file set. "
            "registry/repos.lock is valid and a fresh materialize is coherent."
        )
        return 0

    details: list[str] = []
    details.extend(f"    missing: {dest}" for dest in missing)
    details.extend(f"    extra: {dest}" for dest in extra)
    for dest in changed:
        want, got = canonical[dest], shipped[dest]
        fields = [
            name
            for name in ("kind", "package_path", "sha256", "executable")
            if getattr(want, name) != getattr(got, name)
        ]
        details.append(f"    changed ({', '.join(fields)}): {dest}")
    lines = "\n".join(details)
    print(
        f"::error::check-kit-published-coherence: the newest published {PACKAGE} ({version}) is "
        "STALE — its coordination-kit manifest differs from the canonical staged manifest, so a "
        "receiver that materializes it drifts from canonical and coordination-coherence reds "
        f"(.github#1291):\n{lines}\n"
        f"A kit source changed on main without a republish. Bump <Version> in "
        f"src/FS.GG.Kit/FS.GG.Kit.csproj and release (tag kit/v<version> -> release-kit.yml), so "
        f"the published kit carries current canonical.",
        file=sys.stderr,
    )
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
