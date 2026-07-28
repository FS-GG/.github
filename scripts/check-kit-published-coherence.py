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

THE TAG ARM (`--tag-arm`, .github#1784, WIDENED TO EVERY RELEASE NAMESPACE BY .github#1790).

ITS SUBJECT IS THE WHOLE REPOSITORY'S RELEASE TAGS, NOT THE KIT'S — read that before the rest of
this section, because the file it lives in is named for the kit and that is now a name that
under-describes one of its three arms. It lives here anyway, and deliberately: it shares the feed
reader, the NuGet ordering, the nuspec parse and the `ls-remote` parse with the arms above, and
`#263` is the standing lesson that two implementations of "what does the feed serve" is how two
gates end up disagreeing. `#1790` AC2 says so in as many words — *"prefer generalising it to writing
a second one"*. The namespace table below is the whole of the widening; nothing else about the arm
changed shape.

`#1772` promoted `kit/v*` from decoration to a TRUST ANCHOR. The receiver-side
`materialize / kit-bump-shape` reporter resolves the rule it runs like this:

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
be measured against, and the measurement needs no record anyone has to maintain.

    for every version nuget.org serves, in every release namespace below:
        the nuspec must bind a 40-hex commit in THIS repository,
        the tag `<prefix><version>` must exist,
        and `git ls-remote` (peeled) must resolve it to exactly that commit.

WHY THE KIT'S CLEAN RESULT IS WHAT MAKES THE WIDENING WORTH ANYTHING (.github#1790). Measured on
2026-07-28, `kit/v*` was 21 of 21 correct and is 24 of 24 today: on its own subject `#1784` caught
NOTHING. That is not a wasted gate, it is the control. The same method one namespace over found a
disagreement nobody had ever looked for, and a clean control is what makes it a finding rather than
noise. `#1790` therefore extends the method to every namespace this repository publishes from, and
the extension immediately paid twice more. Measured live, all five:

    namespace                 pkg                     feed  anchored  verdict
    kit/v*                    FS.GG.Kit                 24     24/24  all agree
    coord-engine/v*           FS.GG.Coord.Cli           15     15/15  0.1.0 DISAGREES (#1790)
    drivers/v*                FS.GG.Drivers             11     11/11  0.5.0 had NO TAG (#1790)
    new-sdd-workspace/v*      FS.GG.NewSddWorkspace      6       6/6   all agree
    new-sdd-fullstack/v*      FS.GG.NewSddFullstack      1       1/1   0.1.1-preview.1 DISAGREES

EVERY VERSION THE FEED SERVES, PRERELEASE INCLUDED — and that widening is not tidiness. `#1784`
filtered to STABLE, inherited from the arms above where stable-ness decides what "newest" means. For
tag integrity it decides nothing: "does this tag still name the commit that produced this artifact?"
is exactly as well-posed for a prerelease. The filter was load-bearing in the wrong direction —
`new-sdd-fullstack/v0.1.1-preview.1` is the ONLY version that package ever published, so a
stable-only subject would have reported that namespace as having nothing to check, and the
disagreement sitting in it would have stayed invisible a second time.

THE ANCHOR IS VERIFIED PER NAMESPACE, NEVER ASSUMED. Each row below DECLARES that its package binds
a commit; the arm then reads the artifact and reds if it does not. A namespace whose packages carry
no `<repository commit=…>` has no fixed point at all, and the answer to that is to record it as
UNCOVERED (`anchor=None` below) — never to substitute a weaker comparand such as a maintained list,
which is the exact failure mode this arm exists to remove. All five namespaces are anchored today,
57 of 57 published versions; the uncovered branch is declared, tested, and currently unused.

WHAT THIS ARM CANNOT ASSERT, stated because a check whose limits are unwritten gets trusted past
them (#266). This is `#1784`'s list and it transfers verbatim — a widened check inherits its limits
as well as its method:

  * It does not prove the nuspec's commit is HONEST. The same pack that produced the package wrote
    it, so a publish from a compromised or hand-crafted tree could name any sha. What it proves is
    that the tag and the artifact still AGREE — which is exactly the post-publish mutation this
    issue is about, because the artifact can no longer change and the tag can.
  * It says nothing about versions the feed has unlisted or deleted. Its subject is what a receiver
    can restore today.
  * It does not check the tag's TREE against the package's bytes. That is the default arm's job for
    the newest version, and it is not re-derivable for old ones (stage-kit.sh has itself moved).
  * A release tag with NO published version is reported, never red. Between a release workflow
    pushing a tag and nuget.org indexing the package there is a window in which precisely that is
    true — today's releases needed 14 and 18 cache-busted feed polls — and a red there would make
    every release red `main` on its way through. Two such tags exist right now for reasons that are
    not a window at all (`drivers/v0.4.0`, `new-sdd-fullstack/v0.1.0-preview.1`: cut, never served),
    and they are reported in exactly the same words.

THE ASYMMETRY IS DELIBERATE for `kit/v*`, and only there: `#1772` made the tag a PRECONDITION of
publishing (`release-kit.yml` refuses unless `kit/v<version>` exists AND points at the commit being
packed), so any kit release that publishes at all satisfies this arm on the way in, and a violation
means the tag moved AFTERWARDS. The other four release workflows check only that the tag's VERSION
STRING equals the evaluated project `<Version>` — which is why `coord-engine/v0.1.0` and
`new-sdd-fullstack/v0.1.1-preview.1` could be wrong from birth: at both tagged commits the project
already declared the right version, and a later re-run packed the artifact from a different commit.
So in those namespaces a disagreement is "the tag moved, OR the tag was never the pack source", and
the arm reports the fact rather than a cause it cannot know.

A VERSION IS JOINED ON ITS CANONICAL FORM, NOT ITS LITERAL. nuget.org normalises the versions it
serves (`1.0` becomes `1.0.0`, prerelease case is folded); a tag carries whatever the release author
typed. An exact string compare therefore reports `MISSING` for a `drivers/v1.0` tag against a
published `1.0.0` — fail-CLOSED, so not a #266 violation, but a false red standing on `main`, which
is its own defect class. The join key is `parse_version`, which already IS that canonicalisation, so
there is no second notion of "same version" (#263). Two tags that canonicalise to one version but
resolve to DIFFERENT commits are an ambiguity, not a join: nothing can say which a pin resolves, so
that namespace is UNRESOLVED.

NOTHING MEASURED IS NOT A PASS, and it is asserted at the aggregate as well as per namespace. A run
in which every namespace was UNCOVERED, or every one was narrowed away, printed `ok: … 0 measured
over 0 published version(s)` and exited 0 — a check reporting a measurement it never took, which is
the whole of epic #266. Found in review of this change rather than in testing, which is why the
fixture now asserts the EXIT CODE and not only the words.

`--namespace` NARROWS THE LIVE SUBJECT AND IS NOT LOCKED, because it redirects nothing: every
namespace it selects is still read from the feed and the remote. It does REFUSE an unknown prefix,
because ignoring one means a typo (`--namespace drviers/v`) quietly measures fewer namespaces and
exits 0, which is indistinguishable from a full run — the shape of the gap #1790 closed. The live
workflow runs unscoped, and the fixture asserts that it does.

`--remote` IS NOT LOCKED EITHER, AND THAT IS THE WEAKEST POINT IN THIS ARM. Unlike `--base`,
`--roster` and `--csproj`, which redirect a read so that a wrong one still fails, `--remote`
SUBSTITUTES half the subject: a local repository carrying tags that match the feed's nuspecs greens
the arm. It is inherited from #1784 and left as it is because the arm must be runnable against a
mirror, and because the live workflow passes no `--remote` at all — but it is a canned input in
everything but name, and anyone tightening this file should start there.

RECORDED DISAGREEMENTS (`RECORDED_DISAGREEMENTS`, .github#1790), and why this is not the maintained
list the arm refuses to have. Two tags are wrong TODAY and were wrong before anyone looked. Moving
them is available and was declined — see the record on each entry — so without something the arm
reds forever, and a gate that is red by design teaches exactly one lesson: "FAILED is noise, merge
anyway" (`check-engine-freshness.py` spells the same trade out). The record is therefore PINNED, not
an exemption: it names the namespace, the version, the commit the tag resolves to today AND the
commit the artifact names, and it applies only when ALL FOUR still hold. The comparand is unchanged
— still the artifact, never the record. Move either tag again, in either direction, and the record
stops matching and the arm reds. A record that does not match is reported SPENT — which covers the
version falling off the feed AND the case review had to point out, the disagreement being REPAIRED:
that takes the success path, so a suppression entry could otherwise outlive the thing it suppressed,
forever, unlooked-at. The list cannot rot into cover for a defect it does not already describe,
which is the only property that makes it different in kind from "compare against memory". The
fixture pins the SET of records as well as their shape, so adding one fails until someone updates a
line — a suppression that can be added without a reviewer noticing is not pinned at all.

Usage:  scripts/check-kit-published-coherence.py [--lock registry/repos.lock]
        scripts/check-kit-published-coherence.py --pr-arm [--base <ref-or-sha>]
        scripts/check-kit-published-coherence.py --tag-arm [--remote <url>] [--namespace kit/v]

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
import http.client
import io
import json
import os
import re
import subprocess
import sys
import tempfile
import urllib.error
import urllib.parse
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

# --- the tag arm (.github#1784, widened to every release namespace by .github#1790) ----------------
# The tag scheme the #1772 resolver uses. Written once here; the workflow restates nothing.
TAG_PREFIX = "kit/v"
# The repository a published kit must name. `GITHUB_REPOSITORY` is authoritative in CI; the literal is
# the fallback for a local run. This is asserted AGAINST the nuspec rather than read FROM it: taking
# the remote from the artifact would let a package published out of a fork redirect its own check.
DEFAULT_REPOSITORY = "FS-GG/.github"
# The forge whose refs the #1772 resolver reads. Asserted as part of the repository IDENTITY, because
# a slug match alone accepts that slug on any host.
FORGE_HOST = "github.com"
_HEX40 = re.compile(r"\A[0-9a-f]{40}\Z")

# THE TWO TAG GRAMMARS, and why there are two rather than one.
#
# `BARE_TRIPLE` is the #1772 resolver's grammar: it accepts a bare `x.y.z` and nothing else, so a
# `kit/v*` ref outside it (`kit/vnext`, `kit/v1.2`) can never be selected by a receiver's pin. Such a
# ref is skipped rather than parsed into a version this arm would then demand a package for.
#
# `NUGET_VERSION` is the full NuGet version literal, and it is the right grammar everywhere else
# because nothing resolves those namespaces FROM A PIN. Their consumers derive the ref by string
# concatenation from a version the FEED served (`check-engine-freshness.py` does exactly
# `coord-engine/v` + newest-on-feed), so every literal the feed can serve is a ref that must be
# checkable — including the prerelease literals `new-sdd-workspace` and `new-sdd-fullstack` shipped.
# Narrowing them to `BARE_TRIPLE` would silently drop `new-sdd-fullstack/v0.1.1-preview.1`, which is
# where one of the two live disagreements lives.
#
# THE COST OF KEEPING `BARE_TRIPLE` FOR THE KIT, stated because it is a real one: if FS.GG.Kit ever
# published a prerelease, this arm would report `MISSING kit/v<that version>` no matter what tag
# exists, because the grammar cannot match the ref. That is the CORRECT verdict for the #1772
# resolver — a receiver pinned there genuinely cannot resolve a rule — but it means the header's
# "every version the feed serves" holds for four namespaces and not for this one. It is unreachable
# today: `release-kit.yml` refuses to publish a prerelease and the shared Renovate preset sets
# ignoreUnstable=true, so no receiver could restore one. If that ever changes, the fix is #1772's
# resolver, not this grammar.
BARE_TRIPLE = r"\d+\.\d+\.\d+"
NUGET_VERSION = r"\d+(?:\.\d+){0,3}(?:-[0-9A-Za-z.-]+)?"


@dataclass(frozen=True)
class TagNamespace:
    """One release-tag namespace and the artifact that anchors it (.github#1790).

    `package` is the nuget.org id whose published `.nuspec` is the immutable comparand. `None` means
    THIS NAMESPACE HAS NO ARTIFACT ANCHOR: it is reported UNCOVERED, loudly, on every run, and no
    weaker comparand is substituted for it. That branch is declared and tested; no namespace uses it
    today, because all five were measured and all five bind a commit on every published version.
    """

    prefix: str          # the ref prefix, e.g. "kit/v"
    package: str | None  # the nuget.org id whose nuspec anchors it, or None = no anchor
    grammar: str         # BARE_TRIPLE or NUGET_VERSION — which version literals are refs at all
    note: str            # what resolves this namespace, i.e. what a moved tag would break

    @property
    def ref_pattern(self) -> re.Pattern[str]:
        return re.compile(
            r"\Arefs/tags/" + re.escape(self.prefix) + r"(" + self.grammar + r")(\^\{\})?\Z"
        )


# EVERY release-tag namespace this repository publishes from. A namespace absent from this table is
# the gap .github#1790 is: `#1784` shut `kit/v*` and nothing reached the other four, so the one real
# disagreement in the repository sat one namespace over from a check that was 21-for-21 clean.
RELEASE_NAMESPACES: tuple[TagNamespace, ...] = (
    TagNamespace(
        prefix="kit/v",
        package="FS.GG.Kit",
        grammar=BARE_TRIPLE,
        note="the receiver-side `materialize / kit-bump-shape` reporter resolves the RULE it runs "
        "from this tag, peeled to a commit (.github#1772)",
    ),
    TagNamespace(
        prefix="coord-engine/v",
        package="FS.GG.Coord.Cli",
        grammar=NUGET_VERSION,
        note="scripts/check-engine-freshness.py resolves `coord-engine/v<newest on the feed>` and "
        "counts wire-surface commits since it — a moved tag moves that baseline (.github#1075)",
    ),
    TagNamespace(
        prefix="drivers/v",
        package="FS.GG.Drivers",
        grammar=NUGET_VERSION,
        note="the driver skill bytes the SDD CLI pins and materializes at scaffold time (ADR-0054); "
        "the tag is the only record of which tree produced a given driver payload",
    ),
    TagNamespace(
        prefix="new-sdd-workspace/v",
        package="FS.GG.NewSddWorkspace",
        grammar=NUGET_VERSION,
        note="the workspace scaffolder tool (ADR-0016); the tag is the only record of which tree "
        "produced a published scaffolder",
    ),
    TagNamespace(
        prefix="new-sdd-fullstack/v",
        package="FS.GG.NewSddFullstack",
        grammar=NUGET_VERSION,
        note="the RETIRED predecessor of new-sdd-workspace (ADR-0016 update 2026-07-04b). Retired is "
        "not exempt: its tags still assert which tree produced the packages nuget.org still serves",
    ),
)


@dataclass(frozen=True)
class RecordedDisagreement:
    """A tag that is wrong TODAY, recorded rather than moved (.github#1790).

    PINNED ON BOTH COMMITS. It suppresses the red only while the tag still resolves to
    `tag_commit` AND the artifact still names `nuspec_commit`. Move the tag anywhere — including to
    the commit the artifact names — and the record stops matching, so the arm reds and someone has to
    say what changed. That is what keeps it from becoming the maintained list this arm exists to
    replace: it cannot describe a state it was not written against.
    """

    prefix: str
    version: str
    tag_commit: str
    nuspec_commit: str
    issue: str
    why: str


# The two disagreements .github#1790 measured, and the decision taken on each. Both were WRONG FROM
# BIRTH rather than moved afterwards: at each tagged commit the project already declared the version
# in question, and a later re-run packed the artifact from a different commit — in both cases the
# commit that wired the nuget.org dual-publish. Re-pointing either would make the record true at the
# cost of mutating a marker that has been cited for a year, using precisely the force-move the
# `release-tags-are-immutable` ruleset now refuses; excluding them would scope the check away from
# versions whose anchor is perfectly trustworthy. Recording keeps history honest and keeps the gate
# able to fail. See the GitHub Release attached to each tag for the same statement where a reader of
# the tag will find it.
RECORDED_DISAGREEMENTS: tuple[RecordedDisagreement, ...] = (
    RecordedDisagreement(
        prefix="coord-engine/v",
        version="0.1.0",
        tag_commit="94b044b1e575fc9da0105c32bd063b0f387a5eef",
        nuspec_commit="78c3b5492263a33016e9e3bcac7816a14e9bb237",
        issue=".github#1790",
        why="the tag was pushed at the 0.1.0 release commit; the package nuget.org serves was packed "
        "ten minutes and one merge later, by the run that wired the nuget.org dual-publish (#624/"
        "#625). Both commits declare <Version>0.1.0</Version>, so the double-bind #1784 requires "
        "cannot tell them apart and only the artifact can. NOT re-pointed: it is cited in "
        "docs/2026-07-15-phase-d-corpus-through-shim-plan.md and moving it is the exact mutation "
        "the ruleset created for this issue exists to refuse.",
    ),
    RecordedDisagreement(
        prefix="new-sdd-fullstack/v",
        version="0.1.1-preview.1",
        tag_commit="2e73e5a02099947108663a1edace1214c56647a6",
        nuspec_commit="775a11eec882e2184ea9a18a5f759bb54a9ba143",
        issue=".github#1790",
        why="the same shape, in a namespace nothing had ever checked: tag at the version-bump commit, "
        "artifact packed 5h29m later by the commit that wired the nuget.org dual-publish (#157). "
        "Both commits declare <Version>0.1.1-preview.1</Version>. NOT re-pointed: the package is "
        "RETIRED (renamed to FS.GG.NewSddWorkspace), nothing resolves the tag, and mutating a dead "
        "namespace's history buys nothing that recording does not.",
    ),
)


def _repository_slug() -> str:
    """`owner/name` of the repository whose tags are the anchor. Never read from the artifact."""
    slug = (os.environ.get("GITHUB_REPOSITORY") or "").strip()
    return slug or DEFAULT_REPOSITORY


def _fetch_nuspec(package: str, version: str) -> bytes:
    """The published .nuspec for `package`@`version`, from the flat container.

    The nuspec is served as its own ~1 KB document, so this arm does not pay for 57 .nupkg
    downloads to read 57 one-line bindings. Any failure raises — an unreadable nuspec is a version
    whose tag CANNOT BE CHECKED, and #266 is precisely that "I could not evaluate this" must never
    be reported as "I evaluated it and it passed".
    """
    lid = package.lower()
    url = f"{NUGET_ORG}/{lid}/{version.lower()}/{lid}.nuspec"
    req = urllib.request.Request(url, headers={"User-Agent": "fsgg-check-kit-coherence"})
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            return resp.read()
    except urllib.error.HTTPError as e:
        raise GateError(
            f"cannot read the published {package} {version} .nuspec from nuget.org "
            f"(HTTP {e.code} {e.reason}) — the feed serves this version, so its tag binding is a "
            f"question this gate must answer, and an unanswerable one is not a pass."
        ) from e
    except urllib.error.URLError as e:
        raise GateError(
            f"nuget.org unreachable while reading the {package} {version} .nuspec: {e.reason}"
        ) from e
    except (TimeoutError, OSError, http.client.HTTPException) as e:
        # `resp.read()` raises these DIRECTLY — a socket timeout or a truncated body is neither an
        # HTTPError nor a URLError, so without this clause the most likely failure of 57 sequential
        # fetches on a flaky feed escapes as a traceback instead of the module's stated GateError.
        # Still red either way; this makes it red with a reason.
        raise GateError(
            f"the {package} {version} .nuspec download from nuget.org failed mid-read: {e!r}"
        ) from e


def nuspec_repository_commit(
    version: str, nuspec: bytes, *, repository: str, package: str, prefix: str
) -> str:
    """The 40-hex commit the PUBLISHED nuspec binds `package`@`version` to.

    `package` and `prefix` are REQUIRED, for the reason `parse_ls_remote_tags` states: a default
    naming the kit makes every other namespace's failure message name the wrong package, and makes
    the fixture look as though it covers a path it only covers for the kit (#1790 review).

    Parsed as XML, not grepped: the nuspec's namespace has changed across schema versions and a
    regex over markup is how a gate ends up matching a commented-out element. Every absence is a
    GateError — a version whose artifact names no commit has no fixed point to measure its mutable
    tag against, and that is an unresolved verdict, not a green one.
    """
    import xml.etree.ElementTree as ET  # lazy: only the tag arm parses XML.

    try:
        root = ET.fromstring(nuspec)
    except ET.ParseError as e:
        raise GateError(f"the published {package} {version} .nuspec is not parsable XML: {e}") from e
    repo_elements = [el for el in root.iter() if el.tag.rsplit("}", 1)[-1] == "repository"]
    if len(repo_elements) != 1:
        raise GateError(
            f"the published {package} {version} .nuspec carries {len(repo_elements)} <repository> "
            f"element(s); this arm needs exactly one to know which commit produced the artifact. "
            f"Without it the tag {prefix}{version} can only be compared to a list someone maintains, "
            f"which is the failure mode .github#1784 exists to remove."
        )
    element = repo_elements[0]
    commit = (element.get("commit") or "").strip().lower()
    if not _HEX40.match(commit):
        raise GateError(
            f"the published {package} {version} .nuspec <repository> names no 40-hex commit "
            f"(commit={element.get('commit')!r}). SourceLink writes this at pack time; a package "
            f"without it cannot anchor its own tag."
        )
    if (origin := _repository_origin(element.get("url") or "")) != (FORGE_HOST, repository.lower()):
        raise GateError(
            f"the published {package} {version} .nuspec was packed from "
            f"{element.get('url')!r} (host {origin[0]!r}, repository {origin[1]!r}), not "
            f"{FORGE_HOST}/{repository} — its commit names a history whose tags are not the ones "
            f"the fleet resolves against."
        )
    return commit


def _repository_origin(url: str) -> tuple[str, str]:
    """`(host, owner/name)` for a nuspec repository url. Compared WHOLE, never by suffix.

    A bare `endswith("/" + slug)` test was the first draft and it is not an identity check: it
    accepts `https://evil.example.com/FS-GG/.github` (any host) and
    `https://github.com/attacker/mirror#https://github.com/FS-GG/.github` (the real slug in a
    fragment). Both would let a package assert a commit against tags it has nothing to do with,
    which is the one thing this function exists to prevent. Lowercasing happens BEFORE the `.git`
    strip so a `.GIT` suffix is not left behind to fail a real url.
    """
    lowered = url.strip().lower().rstrip("/")
    if lowered.endswith(".git"):
        lowered = lowered[: -len(".git")]
    if scp := re.match(r"\Agit@([^:/]+):(.+)\Z", lowered):  # git@github.com:owner/name
        return scp.group(1), scp.group(2).strip("/")
    split = urllib.parse.urlsplit(lowered)
    host = split.netloc.rsplit("@", 1)[-1].split(":", 1)[0]  # drop userinfo and port
    return host, split.path.strip("/")


def remote_release_tags(remote: str, ns: TagNamespace) -> dict[str, str]:
    """`version -> the commit <prefix><version> resolves to`, read from the remote, PEELED.

    Peeled on purpose and exactly as the #1772 resolver does it: an annotated tag's own object id is
    not the commit the rule would be checked out at, and comparing it to the nuspec's commit would
    red every annotated release. `refs/tags/X^{}` therefore always wins over `refs/tags/X`.

    A git failure raises. An empty answer does NOT: a namespace with no tags at all is a real
    (catastrophic) state this arm must report per-version, not a read error to be confused with it.
    """
    try:
        result = subprocess.run(
            ["git", "ls-remote", "--tags", remote, f"refs/tags/{ns.prefix}*"],
            text=True,
            capture_output=True,
            check=False,
            timeout=120,
        )
    except (OSError, subprocess.SubprocessError) as e:
        raise GateError(f"cannot list {ns.prefix}* tags on {remote!r}: {e}") from e
    if result.returncode != 0:
        detail = (result.stderr or result.stdout).strip()
        raise GateError(
            f"cannot list {ns.prefix}* tags on {remote!r}"
            + (f": {detail}" if detail else "")
            + " — a tag set this gate cannot read is an UNRESOLVED verdict for every published "
            "version, never a passing one (#266)."
        )
    return parse_ls_remote_tags(result.stdout, ns)


def parse_ls_remote_tags(text: str, ns: TagNamespace) -> dict[str, str]:
    """Parse `git ls-remote` output into `version -> peeled commit` for ONE namespace.

    `ns` is REQUIRED — it used to default to `RELEASE_NAMESPACES[0]`, which is a footgun rather than
    a convenience: a caller who omits it silently gets the kit's prefix and its narrower grammar, so
    every row of any other namespace is skipped and the answer is `{}` — an empty tag set, which
    reads as "every version is MISSING" (#1790 review).

    Rows outside `ns`'s prefix-and-grammar are skipped, not parsed into a version this arm would then
    demand a package for. The canned fixture feeds one file per namespace, exactly as `ls-remote`
    with that namespace's refspec would answer, so a row for a DIFFERENT namespace is skipped here
    for the same reason the live read never sees it.
    """
    pattern = ns.ref_pattern
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
        match = pattern.match(ref)
        if not match:
            continue  # outside this namespace's grammar — no consumer can resolve it.
        (peeled if match.group(2) else direct)[match.group(1)] = sha
    return {**direct, **peeled}


def _read_canned(path: str, what: str) -> str:
    try:
        return open(path, encoding="utf-8").read()
    except OSError as e:
        raise GateError(f"cannot read the canned {what} {path!r}: {e}") from e


def parse_canned_bindings(text: str, ns: TagNamespace) -> dict[str, str]:
    """`version<TAB>commit` rows — the nuspec read, canned. `-` means the artifact binds no commit.

    Constrained EXACTLY as the live read is, and refused with the same words. A canned input the gate
    validates more loosely than its real subject is a fixture that can green a shape production would
    red, so every narrowing the live branch performs is performed here too: the version parse and
    uniqueness. Parsed HERE, before anything else looks at it, and NOT at report time —
    `sorted(..., key=parse_version)` inside the failure report would raise while rendering a real
    verdict and throw the diagnosis away.
    """
    package = ns.package or "(no anchor)"
    bindings: dict[str, str] = {}
    for lineno, raw in enumerate(text.splitlines(), 1):
        if not raw.strip():
            continue
        parts = raw.split("\t")
        if len(parts) != 2:
            raise GateError(f"canned published line {lineno} is not `<version>\\t<commit>`: {raw!r}")
        version, commit = parts[0].strip(), parts[1].strip().lower()
        try:
            parse_version(version)
        except GateError as e:
            raise GateError(
                f"canned published line {lineno} names {version!r}, which is not a NuGet "
                f"version: {e}"
            ) from e
        if version in bindings:
            raise GateError(
                f"canned published line {lineno} repeats version {version!r}; the feed's "
                f"versions are unique, so a duplicate would silently overwrite its own subject."
            )
        if not _HEX40.match(commit):
            raise GateError(
                f"the published {package} {version} .nuspec <repository> names no 40-hex commit "
                f"(commit={None if commit == '-' else commit!r}). SourceLink writes this at pack "
                f"time; a package without it cannot anchor its own tag."
            )
        bindings[version] = commit
    return bindings


@dataclass(frozen=True)
class NamespaceVerdict:
    """One namespace's measured answer. `unresolved` is the #266 state: NOT MEASURED, never clean."""

    ns: TagNamespace
    published: int = 0
    missing: tuple[tuple[str, str], ...] = ()             # version, the commit its artifact names
    moved: tuple[tuple[str, str, str], ...] = ()          # version, tag commit, nuspec commit
    recorded: tuple[tuple[RecordedDisagreement, str], ...] = ()   # record, the sha the tag holds
    spent: tuple[RecordedDisagreement, ...] = ()
    untagged: tuple[str, ...] = ()
    unresolved: str | None = None
    uncovered: bool = False

    @property
    def red(self) -> bool:
        return bool(self.unresolved) or bool(self.missing) or bool(self.moved)


def classify_namespace(
    ns: TagNamespace, bindings: dict[str, str], tags: dict[str, str]
) -> NamespaceVerdict:
    """Compare one namespace's artifact bindings to its tags. PURE — no network, no git.

    Split out from the reading so the fixture can drive the whole decision, including the
    RECORDED_DISAGREEMENTS pinning, against real inputs rather than a mirror of them.
    """
    records: dict[tuple, RecordedDisagreement] = {}
    for r in RECORDED_DISAGREEMENTS:
        if r.prefix != ns.prefix:
            continue
        key = version_key(r.version)
        if key in records:
            # Two records for one version silently collapse into whichever the loop saw last, and the
            # survivor then speaks for a state nobody reviewed. The list's whole claim is that it
            # cannot describe anything it was not written against.
            raise GateError(
                f"RECORDED_DISAGREEMENTS names {ns.prefix}{r.version} more than once; one entry "
                f"would silently overwrite the other and speak for a state nobody wrote."
            )
        records[key] = r

    # COMPARED ON A CANONICAL KEY, not on the literal string. nuget.org normalises the versions it
    # serves (`1.0` -> `1.0.0`, prerelease case folded); a tag carries whatever the release author
    # typed. An exact string compare therefore reds a `drivers/v1.0` tag for a published `1.0.0` —
    # fail-CLOSED, so not a #266 violation, but a false red on `main`, which is its own defect class.
    keyed_tags: dict[tuple, tuple[str, str]] = {}   # key -> (tag's version literal, commit)
    for literal, sha in tags.items():
        key = version_key(literal)
        if key in keyed_tags and keyed_tags[key][1] != sha:
            # Two DIFFERENT commits under one canonical version is not a normalisation question, it
            # is an ambiguity: nothing can say which one a pin would resolve. Unresolved, never a pass.
            raise GateError(
                f"{ns.prefix}{literal} and {ns.prefix}{keyed_tags[key][0]} are the same NuGet "
                f"version but resolve to different commits ({sha} vs {keyed_tags[key][1]}). Nothing "
                f"can say which one a pin resolves, so this is unresolved, not a pass."
            )
        keyed_tags.setdefault(key, (literal, sha))

    missing: list[tuple[str, str]] = []
    moved: list[tuple[str, str, str]] = []
    recorded: list[tuple[RecordedDisagreement, str]] = []
    seen: set[tuple] = set()
    for version, commit in bindings.items():
        key = version_key(version)
        seen.add(key)
        entry = keyed_tags.get(key)
        if entry is None:
            missing.append((version, commit))
        elif entry[1] == commit:
            continue
        else:
            record = records.get(key)
            # PINNED ON BOTH COMMITS. A record only speaks for the exact state it was written
            # against; any movement in either direction falls through to MOVED and reds.
            if record and record.tag_commit == entry[1] and record.nuspec_commit == commit:
                recorded.append((record, entry[1]))
            else:
                moved.append((version, entry[1], commit))

    # A RECORD THAT DESCRIBES NOTHING IS SPENT, never silently kept — the list must not be able to
    # accumulate entries nobody re-reads. ONE RULE: a record that did not match is spent. It covers
    # every way a record can stop describing reality, including the two that are easy to miss:
    #
    #   * the version leaves the feed, so it is no longer a subject at all;
    #   * the disagreement is REPAIRED and tag and artifact now agree. That is the SUCCESS case, so
    #     it takes the `continue` above and would otherwise be invisible forever — a suppression
    #     entry outliving the thing it suppressed, which is exactly the rot this record claims it
    #     cannot have. Review caught this; the rule above is the shape that cannot miss a case.
    #
    # Never red on its own: an unpublished version is never red here, and a repaired disagreement is
    # not a defect. When the record failed to match because the tag MOVED, the MOVED red is already
    # raised above and this simply says the record no longer speaks.
    active = {id(record) for record, _ in recorded}
    spent = tuple(r for _, r in sorted(records.items()) if id(r) not in active)
    return NamespaceVerdict(
        ns=ns,
        published=len(bindings),
        missing=tuple(sorted(missing, key=lambda row: parse_version(row[0]))),
        moved=tuple(sorted(moved, key=lambda row: parse_version(row[0]))),
        recorded=tuple(sorted(recorded, key=lambda row: parse_version(row[0].version))),
        spent=spent,
        untagged=tuple(
            sorted(
                (literal for key, (literal, _) in keyed_tags.items() if key not in seen),
                key=parse_version,
            )
        ),
    )


def version_key(literal: str) -> tuple:
    """The canonical identity of a NuGet version literal, shared by feed strings and tag strings.

    `parse_version` already IS that canonicalisation — it pads numeric segments to four and folds
    prerelease case — so this reuses it rather than growing a second notion of "same version"
    (#263). It is only ever used to JOIN two names for one version; ordering still goes through
    `parse_version` directly.
    """
    return parse_version(literal)




def measure_namespace(
    ns: TagNamespace,
    *,
    remote: str,
    repository: str,
    canned_published: str | None,
    canned_tags: str | None,
) -> NamespaceVerdict:
    """Read one namespace's subject and classify it. Any unreadable subject is UNRESOLVED, not clean."""
    if ns.package is None:
        # DECLARED as having no artifact anchor. Reported UNCOVERED on every run and measured by
        # nothing — the alternative is a comparand this arm does not trust, which is the whole point.
        return NamespaceVerdict(ns=ns, uncovered=True)
    try:
        if canned_published is not None:
            bindings = parse_canned_bindings(
                _read_canned(canned_published, "published-version list"), ns
            )
        else:
            live = nuget_org_versions(ns.package)  # raises on 404/unreachable/empty — never []
            bindings = {
                version: nuspec_repository_commit(
                    version,
                    _fetch_nuspec(ns.package, version),
                    repository=repository,
                    package=ns.package,
                    prefix=ns.prefix,
                )
                for version in sorted(live, key=parse_version)
            }

        # AN EMPTY SUBJECT IS NOT A PASS, and this must be said ONCE, below both branches. Stating it
        # only inside the live branch is exactly how the first draft of this arm shipped a fail-open:
        # an empty canned list produced zero comparisons and then printed "ok: all 0 ... version(s)
        # ... has not moved since publication" — a check reporting a measurement it never took (#266).
        if not bindings:
            raise GateError(
                f"this arm resolved ZERO published {ns.package} versions to check {ns.prefix}* tags "
                f"for. That is not 'every tag is fine' — it is a subject that could not be read, and "
                f"the two must never share an exit code."
            )

        tags = (
            parse_ls_remote_tags(_read_canned(canned_tags, "ls-remote tag list"), ns)
            if canned_tags is not None
            else remote_release_tags(remote, ns)
        )
        # INSIDE the try, deliberately. `classify_namespace` parses versions and refuses an ambiguous
        # or duplicated record, so it raises GateError too — and outside the try that escapes
        # `run_tag_arm`'s comprehension and aborts EVERY namespace on one error, destroying the
        # aggregation property this arm exists to have (#1790 review).
        return classify_namespace(ns, bindings, tags)
    except GateError as e:
        return NamespaceVerdict(ns=ns, unresolved=str(e))


def render_tag_arm(verdicts: list[NamespaceVerdict], repository: str) -> tuple[int, str, str]:
    """`(exit code, stdout text, stderr text)` for a measured set of namespaces. PURE."""
    out: list[str] = []
    problems: list[str] = []
    red = any(v.red for v in verdicts)

    for v in verdicts:
        ns = v.ns
        if v.uncovered:
            # NEVER "clean". #266: "I could not evaluate this" is not "I evaluated it and it passed",
            # and a namespace with no immutable artifact to measure against is exactly that.
            out.append(
                f"  {ns.prefix}*  UNCOVERED — NOT MEASURED. No published artifact anchors this "
                f"namespace, so there is no immutable fixed point its mutable tags can be compared "
                f"to. Its tags are unchecked; treat them as unverified, not as verified-good. "
                f"({ns.note})"
            )
            continue
        if v.unresolved:
            out.append(f"  {ns.prefix}*  UNRESOLVED — NOT MEASURED: {v.unresolved}")
            problems.append(f"  {ns.prefix}*  UNRESOLVED: {v.unresolved}")
            continue

        # "N published, N anchored" said the same number twice and could never show a discrepancy:
        # a version whose artifact binds no commit RAISES, so it lands in `unresolved` above and
        # never reaches here. Say what is actually true instead of printing a reassuring ratio that
        # is a tautology (#1790 review).
        headline = (
            f"{v.published} published version(s), every one anchored by its own .nuspec"
        )
        if v.missing or v.moved:
            verdict = f"{len(v.missing)} MISSING, {len(v.moved)} MOVED"
        elif v.recorded:
            verdict = f"{len(v.recorded)} recorded disagreement(s), everything else agrees"
        else:
            verdict = "every tag resolves (peeled) to its artifact's commit"
        out.append(f"  {ns.prefix}*  {ns.package}: {headline} — {verdict}")

        for record, resolved in v.recorded:
            out.append(
                f"      RECORDED  {ns.prefix}{record.version} resolves to {resolved}, its artifact "
                f"was packed from {record.nuspec_commit} ({record.issue}). NOT a pass and not a "
                f"regression: a known, pinned disagreement. {record.why}"
            )
        for record in v.spent:
            out.append(
                f"      SPENT     the recorded disagreement for {ns.prefix}{record.version} "
                f"({record.issue}) names a version the feed no longer serves, so it now describes "
                f"nothing. Delete it from RECORDED_DISAGREEMENTS, or say what replaced it."
            )
        if v.untagged:
            # NEVER an error. A release workflow pushes the tag BEFORE nuget.org indexes the package,
            # so this is the normal state of a release in flight; reddening it would make every
            # release red main on its way through.
            out.append(
                f"      note: {len(v.untagged)} {ns.prefix}* tag(s) name no published version "
                f"({', '.join(v.untagged)}). Not an error — the release workflow pushes the tag "
                f"before the feed indexes the package, and nothing can pin a version that was "
                f"never published."
            )

        for version, commit in v.missing:
            problems.append(
                f"    MISSING  {ns.prefix}{version} — published, but no such tag. Its artifact was "
                f"packed from {commit}; create it with:\n"
                f"        git tag {ns.prefix}{version} {commit} && "
                f"git push origin {ns.prefix}{version}"
            )
        for version, resolved, commit in v.moved:
            problems.append(
                f"    MOVED    {ns.prefix}{version} resolves to {resolved}, but the published "
                f".nuspec was packed from {commit}. The tag was changed after publication."
            )

    measured = [v for v in verdicts if not v.uncovered and not v.unresolved]
    total = sum(v.published for v in measured)
    # ZERO MEASURED IS NOT A PASS, and it is the aggregate twin of the per-namespace guard above.
    # Without it a run in which every namespace was UNCOVERED (or every one was narrowed away) prints
    # `ok: … 0 measured over 0 published version(s)` and exits 0 — a check reporting a measurement it
    # never took, which is the one thing epic #266 is about. Found in review, not in testing.
    if not measured:
        problems.insert(
            0,
            f"    NOTHING WAS MEASURED. {len(verdicts)} namespace(s) were selected and none of them "
            f"yielded a single published version to compare a tag against. That is not 'every tag is "
            f"fine'; it is a subject that could not be read, and the two must never share an exit "
            f"code.",
        )
        red = True
    header = (
        f"{'FAILED' if red else 'ok'}: {len(verdicts)} of {len(RELEASE_NAMESPACES)} declared "
        f"release-tag namespace(s) selected in {repository}, {len(measured)} measured over {total} "
        f"published version(s). Each tag is compared to the commit its own published .nuspec was "
        f"packed from — an immutable artifact, not a maintained list (.github#1784, widened by "
        f".github#1790)."
    )
    stdout = header + "\n" + "\n".join(out)
    if not red:
        return 0, stdout, ""
    stderr = (
        "::error::check-kit-published-coherence (tag-arm): release tags no longer agree with the "
        "packages the fleet restores:\n" + "\n".join(problems) + "\n"
        "A consumer whose pin names a MISSING tag cannot resolve it at all. A consumer whose pin "
        "names a MOVED tag resolves a tree that is not that release — the defect .github#1772 "
        "closed, reopened through a mutable ref. The published .nuspec is the fixed point here: it "
        "is immutable and the tag is not, so the tag is what to repair. If a tag CANNOT be repaired, "
        "record it in RECORDED_DISAGREEMENTS with both commits and the reason, as .github#1790 did "
        "for coord-engine/v0.1.0."
    )
    return 1, stdout, stderr


def _canned_by_namespace(values: list[str], flag: str) -> dict[str, str]:
    """`PREFIX=FILE` pairs -> `{prefix: file}`. Every prefix must be a DECLARED namespace.

    Repeatable and prefix-qualified so the fixture can drive several namespaces in one run and prove
    the aggregation — that one namespace's red does not abort the others, and that every namespace is
    named in the report. An unknown prefix is refused rather than ignored: a fixture whose canned
    input silently applies to nothing is a leg that measures nothing while appearing to pass.
    """
    out: dict[str, str] = {}
    known = {ns.prefix for ns in RELEASE_NAMESPACES}
    for value in values:
        prefix, sep, path = value.partition("=")
        if not sep or not prefix.strip() or not path.strip():
            raise GateError(
                f"{flag} takes `PREFIX=FILE` (e.g. `kit/v=/tmp/tags.txt`); got {value!r}."
            )
        prefix, path = prefix.strip(), path.strip()
        if prefix not in known:
            raise GateError(
                f"{flag} names the unknown release namespace {prefix!r}. Known: "
                f"{', '.join(sorted(known))}. Refusing rather than silently applying it to nothing."
            )
        if prefix in out:
            raise GateError(f"{flag} names {prefix!r} twice; the second would silently win.")
        out[prefix] = path
    return out


def run_tag_arm(
    *,
    remote: str,
    repository: str,
    only: list[str],
    canned_published: dict[str, str],
    canned_tags: dict[str, str],
) -> int:
    """.github#1784/#1790. Exit 0 = every release tag still resolves to its artifact's commit."""
    # AN UNKNOWN --namespace IS REFUSED, not ignored. Ignoring it means `--namespace kit/v
    # --namespace drviers/v` (typo) quietly measures ONE namespace and exits 0, which is
    # indistinguishable from a full run — the same "silently applies to nothing" defect
    # `_canned_by_namespace` already refuses, and the same shape as the gap #1790 closed.
    known = {ns.prefix for ns in RELEASE_NAMESPACES}
    if unknown := sorted(set(only) - known):
        raise GateError(
            f"--namespace names {unknown}, which are not declared release namespaces. Known: "
            f"{', '.join(sorted(known))}. Refusing rather than silently measuring fewer namespaces "
            f"than the caller asked for."
        )
    # A canned namespace must supply BOTH halves. One half canned and the other read live is a
    # fixture that reaches the network from a leg whose author believed it could not — and whose
    # DNS failure would then read as the leg passing.
    if set(canned_published) ^ set(canned_tags):
        raise GateError(
            "every canned namespace needs both --tag-arm-published and --tag-arm-tags; "
            f"{sorted(set(canned_published) ^ set(canned_tags))} has only one half, so the other "
            "would be read live from a run that believes it is offline."
        )
    # When anything is canned, the subject IS the canned namespaces. Otherwise a fixture would fall
    # through to a live read of the four it did not mention.
    selected = [
        ns
        for ns in RELEASE_NAMESPACES
        if (ns.prefix in canned_published if canned_published else True)
        and (not only or ns.prefix in only)
    ]
    if not selected:
        raise GateError(
            f"no release namespace matches {only!r}. Known: "
            f"{', '.join(ns.prefix for ns in RELEASE_NAMESPACES)}."
        )
    verdicts = [
        measure_namespace(
            ns,
            remote=remote,
            repository=repository,
            canned_published=canned_published.get(ns.prefix),
            canned_tags=canned_tags.get(ns.prefix),
        )
        for ns in selected
    ]
    code, stdout, stderr = render_tag_arm(verdicts, repository)
    print(stdout)
    if stderr:
        print(stderr, file=sys.stderr)
    return code


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
        help="the tag-integrity rule (.github#1784, every release namespace since #1790): does every "
        "published version's release tag still resolve to the commit its artifact was packed from?",
    )
    ap.add_argument(
        "--remote",
        default="",
        help="the git remote whose release tags are the anchor (default: this repository on github.com)",
    )
    ap.add_argument(
        "--namespace",
        action="append",
        default=[],
        metavar="PREFIX",
        help="measure only these release-tag namespaces (repeatable; default: all of "
        + ", ".join(ns.prefix for ns in RELEASE_NAMESPACES)
        + "). Narrows the subject, it does not replace a read, so it is not a canned input.",
    )
    ap.add_argument(
        "--tag-arm-published",
        action="append",
        default=[],
        metavar="PREFIX=FILE",
        help="read `<version>\\t<nuspec commit>` rows for one namespace instead of the feed "
        "(tests only; repeatable)",
    )
    ap.add_argument(
        "--tag-arm-tags",
        action="append",
        default=[],
        metavar="PREFIX=FILE",
        help="read canned `git ls-remote` output for one namespace instead of the remote "
        "(tests only; repeatable)",
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
        "--tag-arm-published": args.tag_arm_published or None,
        "--tag-arm-tags": args.tag_arm_tags or None,
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
                only=args.namespace,
                canned_published=_canned_by_namespace(
                    args.tag_arm_published, "--tag-arm-published"
                ),
                canned_tags=_canned_by_namespace(args.tag_arm_tags, "--tag-arm-tags"),
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
