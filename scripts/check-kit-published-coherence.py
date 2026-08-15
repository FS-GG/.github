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

`--remote` is an explicit spelling of the same repository, not a substitute for its tag subject.
Before reading refs, the tag arm normalizes its host and repository path and refuses any remote that
is not this repository on GitHub. That accepts the normal HTTPS and SSH spellings while preventing a
canned or foreign repository from making matching tags look like evidence. The live workflow leaves
it unset, so its default remains the repository that invoked the gate.

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

THE OBLIGATION ARM (`--obligation-arm`, .github#2533). A third PR-time subject, and it is about the
OTHER half of a release: not the version this PR ships, but the post-merge obligation its author
DECLARES for shipping it.

`.github#2512` declared obligation 1, `coherent-set-0.50.6-release`, as a manual act — tag
`coord-engine/v0.50.6`, `kit/v0.50.6`, `drivers/v0.50.6` at the merge commit, then verify both feeds.
It was reviewed by two independent critics across three rounds, accumulated two sub-obligations, and
was explicitly host-gated ("merge, then STOP; do NOT begin obligation 1") on the grounds that it was
the session's only irreversible act. THE MERGE ITSELF PERFORMED IT: `kit-auto-publish.yml` is
`on: push: branches: [main]`, and 8 seconds after the merge landed it had cut all three tags at
`8de950c3`. A worker verified ZERO `0.50.6` tags at 15:43:56Z; six refs existed by 15:46:48Z.

Two things were wrong at once, and only one of them is about wasted review attention:

  * A GATE THAT CANNOT FIRE. Sub-obligation `1b` was written as a stop-DO-NOT-TAG condition to be
    evaluated immediately before tagging. There was no such moment. Its condition happened to hold,
    so nothing shipped that it would have refused — luck, not process.
  * A DOUBLE ACT. A worker that DOES obey a manual release obligation after the automation has
    already run re-tags or re-publishes. `.github#2240` measured that two packs of an identical
    checkout produce different sha256, and this repo carries two permanent two-of-three sets
    (`0.50.1`, `0.50.5`) as the standing cost of release paths going wrong.

Nothing bound the declaration to whether its subject was automated. `fsgg:delivery-obligation` is
free-text prose describing an act, and `kind=package-release` in a PR comment gives no hint that
`on: push: branches: [main]` in a workflow file will fire on that same merge.

THE MECHANISM, AND WHY IT IS THIS ONE OF THE THREE .github#2533 NAMED. The row offered a typed `kind`
that names the automation, a check that cross-references `.github/workflows/*` triggers, or an
explicit `automated: true|false` field the declaration must carry. This arm is the SECOND, joined to
the declaration by its typed `kind`:

  * A typed kind naming the automation, and an `automated:` field, both take THE AUTHOR'S WORD for
    exactly the fact the author got wrong. `.github#2512`'s declaration was honest and confident and
    would have said `automated: false` just as confidently; two critics and a host read it and
    agreed. A field that records a belief cannot detect that the belief is false.
  * An `automated:` field additionally changes the declaration grammar the engine already parses
    (`DeliveryApplication.fs`'s `obligationDeclaration`), so every existing and in-flight declaration
    becomes non-conforming — a protocol break bought for a detection that still would not detect.
  * Cross-referencing the trigger derives the verdict from the repository's own live workflow files.
    It stays true when a trigger changes: if `kit-auto-publish` ever stops running on push to `main`,
    the same declaration stops being flagged, and correctly so, with nothing to update here.

THE CONTROLLED COUNTERPART IS THE POINT (.github#2533 AC3). The fix is NOT "warn on every obligation".
An obligation that genuinely requires manual action — a registry record needing post-publish evidence,
a downstream repo's bump — is not flagged, and the fixture asserts that leg beside the flagged one. A
warning that fires on everything is read as noise, and #266's whole thesis is that noise is how a
control stops being a control.

WHAT IT MEASURES. `MERGE_AUTOMATION` below declares, per automated act, the obligation `kind`s that
NAME that act and the workflow that PERFORMS it. The kinds are not this file's invention: they are the
same tokens `pnext-item`'s `merge-and-release.md` tells a worker to use for those acts, so the two
halves of the join are one contract rather than two lists that can drift. The workflow half is never
restated — the arm opens the workflow file and reads its `on:` block, and a declaration is flagged
only if that trigger really does fire on a merge into `main`.

A TRIGGER IS NOT AN ACT (.github#2571), AND THAT WAS THIS ARM'S FIRST DEFECT. The paragraph above was
the whole rule until 2026-08-15, and it is only half of one. `kit-auto-publish.yml` does fire on every
merge into `main` — and the program it runs, `kit-auto-publish.py`, deliberately admits ONLY a same-line
PATCH bump (.github#2442) and terminally refuses a MINOR with `candidate-not-next-patch`. Every
coherent-set release is a minor by design (.github#2402). So on exactly the release that needs the most
care, the workflow fires and cuts nothing: the release is manual, it is genuinely owed, and this arm
refused every token that named it. The three ways out were all bad — mislabel the `kind` to an unmapped
token (which is choosing a word to evade a control, and .github#2527 / PR #2532 shipped that route with
no row and no disclosure), declare nothing (and red `main` until someone notices), or leave the PR red.

So the arm now asks BOTH halves, and the second half is READ, exactly like the first: it loads
`kit-auto-publish.py` and calls its own `decide()`. Nothing about .github#2442's rail is restated here,
so the rail can move with nothing to update in the join table — the same property the workflow half was
built for.

WHAT IT ASKS `decide()` ABOUT IS A SET OF WORLDS, NOT ONE, and the first draft of this repair got that
wrong in a way worth recording (round-1 repair on .github#2571). It supplied the `<Version>` this merge
would publish and the frontier measured NOW, pinned every unknowable post-merge fact to its most
permissive value, and claimed the resulting verdict held on "any post-merge state of the world". The
frontier is not a constant: it advances between this gate running and the merge landing, `decide()`'s
rail is `candidate.patch == frontier.patch + 1`, and a forward move flips `candidate-not-next-patch`
into `tag`. Candidate `0.58.3` refuses against an observed `0.58.0` and tags against `0.58.2`. A false
one-directional invariant, inside the fail-closed control guarding an irreversible double-publish, is
.github#2533's own defect one level down.

The arm therefore ENUMERATES the reachable worlds and takes the disjunction: the act is manual only if
`decide()` declines in every one of them. `kit_auto_publish_completions` owns that set — the observed
frontier and, when the current rail has one and the feed has not already passed it, the frontier that
would admit this candidate — and states why each unvaried fact is uniquely permissive. THAT THE SET IS
COMPLETE IS MEASURED, NOT CLAIMED: `tests/kit-published-coherence/run.sh` sweeps a grid of candidates and
observed frontiers, brute-forces over every reachable frontier and over the facts this builder pins, and
asserts the two agree on every pair — with an inversion leg that re-pins the frontier and proves the
sweep reds. The sentence this replaced was a claim no test could falsify, which is why it survived.

THE CONTROLLED COUNTERPART OF THE COUNTERPART. A PATCH-line candidate declaring a performance
obligation is still flagged — the fixture asserts the minor and patch legs side by side against one
another, because a change that merely stopped flagging things would have disarmed .github#2533 rather
than corrected it.

FAILS CLOSED, and the map cannot rot in silence. Every mapped workflow is opened and its triggers
parsed on EVERY run, even when this PR declares nothing: a renamed, deleted, or unparseable workflow
is a no-verdict RED, not an arm that quietly matches nothing. Every mapped DECISION PROGRAM is loaded
on every run too, for the identical reason — a renamed or broken `kit-auto-publish.py` must not leave
the arm silently back on the trigger-only rule. Loading is local and free; the observation the decision
needs (an MSBuild evaluation and one nuget.org read) is deferred until a mapped kind is actually
declared, so an unrelated PR pays neither. A candidate whose decision cannot be evaluated — an
unreadable version, an unreachable feed, a `decide()` that raises or returns an action this file does
not classify — is a no-verdict RED, never a pass. A comment that opens with the declaration marker but
does not parse is a no-verdict too, rather than a second opinion about why — `DeliveryApplication.fs`
owns that diagnosis.

WHAT IT CANNOT ASSERT, stated here rather than discovered later (#266):

  * A kind nobody mapped is not flagged. The join is a declared table, which is why `merge-and-release.md`
    carries the authoring-side half: a worker choosing a kind is told which acts are automated. This
    arm is the backstop for the mapped ones, not a classifier for arbitrary prose.
  * A mapped workflow whose `push:` trigger carries a `paths:`/`paths-ignore:` filter is a NO-VERDICT
    when a mapped kind is declared, never a pass: whether THIS merge fires it then depends on the
    diff, and "cannot tell" must not merge. No mapped workflow carries one today.
  * It does not check that a declared obligation is CORRECT, complete, or discharged. That is the
    engine's `delivery` path. Its one question is whether the act named is one the merge performs.
  * IT DOES NOT PREDICT WHAT `decide()` WILL ANSWER AFTER THE MERGE. It answers the strictly weaker
    question that is sound in the direction that matters: COULD the merge cut this, on any world still
    reachable from the one observed. A "not performed" verdict therefore holds whatever happens between
    now and the merge — including a frontier that advances, which the first draft of this arm's
    decision half did NOT hold for. A "performed" verdict does not promise the tag will actually be
    cut, only that this arm cannot rule it out, and flagging is the safe side of that: a candidate that
    clears the rail here can still be refused later by a fact that moved (someone republishes, a
    sibling tag appears), and the obligation the author was told to write as VERIFICATION is exactly
    what surfaces that.
  * ITS COMPLETENESS IS PROVED AGAINST THE RAIL AS WRITTEN, NOT AGAINST EVERY RAIL. The enumerated set
    of reachable worlds is exact for the frontier rule `kit-auto-publish.py` implements today, and the
    fixture's brute-force sweep is what establishes that rather than a comment. Reshape the rail so
    that some other frontier admits a candidate and the sweep goes RED naming the pair — which is the
    point of grading it there rather than asserting it here.
  * IT CANNOT TELL "THIS PR HAS NO COMMENTS" FROM "I READ NOTHING", and no arrangement of this file
    can. An empty comment list is a legal state, so `[]` is a legal subject; an unreadable,
    unparsable or non-list payload is a no-verdict RED, but an empty one is a green. The guarantee
    that the comments were actually FETCHED therefore belongs to the caller, and it is a real
    guarantee, not a disclaimer: the first draft of this arm's step piped `gh api` into `jq` under
    GitHub's default `bash -e` (no pipefail), so a transport failure fabricated `[]` and greened
    this arm. `kit-published-coherence.yml` now takes the pipe out and sets `pipefail`, and the
    fixture EXECUTES that step under a failing stub `gh` and asserts it exits non-zero — a
    behaviour assertion, because the substring check that stood there first passed while the fetch
    was replaced by `echo '[]'`.

Usage:  scripts/check-kit-published-coherence.py [--lock registry/repos.lock]
        scripts/check-kit-published-coherence.py --pr-arm [--base <ref-or-sha>]
        scripts/check-kit-published-coherence.py --tag-arm [--remote <url>] [--namespace kit/v]
        scripts/check-kit-published-coherence.py --obligation-arm --obligations <comments.json>

`--fixture-manifest <tsv> --canonical-manifest <tsv>` compares canned manifests and refuses to run
unless FSGG_KIT_COHERENCE_FIXTURE_OK=1 — which only tests/kit-published-coherence/ sets. A test hook
that can silently turn the gate into a no-op is the very defect class above. The PR arm's canned
inputs (`--changed-files`, `--kit-sources`, `--published-version`) and the obligation arm's
(`--obligation-candidate-version`, `--obligation-published-version`) are locked behind the same switch,
for the same reason: each one of them, left open, is a way to make the arm answer without reading its
subject.

Exit 0 = the newest published FS.GG.Kit carries the same coordination-kit bytes canonical derives
(default arm), this PR incurs no republish obligation it has not already met (`--pr-arm`), or every
obligation this PR declares names an act the merge will NOT perform for it (`--obligation-arm`).
"""
from __future__ import annotations

import argparse
import fnmatch
import http.client
import io
import json
import os
import re
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from collections.abc import Callable
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
REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STAGE_KIT = os.path.join(REPO_ROOT, "src", "FS.GG.Kit", "stage-kit.sh")
REPOS_TOOL = os.path.join(REPO_ROOT, "scripts", "repos.sh")

# coordination-kit members carry a repos.lock digest; build-config members deliberately do not
# (ADR-0036), exactly as verify-package.sh partitions them.
COORDINATION_KINDS = frozenset({"skill", "client", "config"})
_HEX64 = re.compile(r"\A[0-9a-f]{64}\Z")

# --- the obligation arm (.github#2533) ------------------------------------------------------------
# The branch a merged pull request lands on, and therefore the branch whose `push:` trigger decides
# whether an act is performed BY the merge.
DEFAULT_BRANCH = "main"


@dataclass(frozen=True)
class MergeAutomation:
    """One act this repository performs AUTOMATICALLY when a pull request merges into `main`.

    `kinds` are the `fsgg:delivery-obligation` `kind` tokens that NAME this act. They are the same
    tokens `.claude/skills/pnext-item/references/merge-and-release.md` tells a worker to use, so the
    join is one contract with two halves rather than two lists free to drift.

    `workflow` is READ, never restated: this arm opens the file and parses its `on:` block, so a
    trigger that stops being a merge trigger stops flagging declarations with nothing to update here.
    A workflow that cannot be opened or parsed is a no-verdict RED on every run, which is what stops
    this table rotting silently when a file is renamed or deleted.

    `decision` and `facts` are the SECOND half of that read, and .github#2571 is why they exist. A
    TRIGGER is not an ACT. `kit-auto-publish.yml` fires on every merge into `main`, but the program it
    runs admits ONLY a same-line patch bump (.github#2442) and terminally refuses a coherent-set MINOR
    with `candidate-not-next-patch` — a version line every coherent-set release produces. On such a
    candidate the workflow fires and cuts nothing: the release is genuinely manual, genuinely owed, and
    under the trigger-only rule its author had no declarable token for it, because every kind that
    NAMES the act was flagged. `decision` names the program whose `decide()` answers "does the act
    HAPPEN", and `completions` turns the two observable candidate facts into the post-merge states of
    the world to ask it about. Like `workflow`, `decision` is opened on every run and never restated,
    so .github#2442's rail can move with nothing to update in this table.
    """

    workflow: str
    kinds: frozenset[str]
    performs: str
    decision: str | None = None
    completions: Callable[[object, str, str], "list[Completion]"] | None = None


@dataclass(frozen=True)
class Completion:
    """One post-merge state of the world this arm asks the mapped `decide()` about.

    `frontier` is the feed frontier this state assumes and `hypothetical` says whether that is the one
    measured now or one the feed could still reach before the merge. Carrying both means a finding can
    say WHICH world it is talking about instead of quoting a number that was never observed.
    """

    frontier: str
    hypothetical: bool
    facts: dict


def _kit_auto_publish_facts(candidate: str, frontier: str) -> dict:
    """The permissive completion of everything EXCEPT the frontier, which the caller varies.

    Only two of `kit-auto-publish.decide()`'s inputs are knowable while the PR is open: the `<Version>`
    this merge would publish, and the feed frontier it would publish above. Everything else is a POST-
    merge observation — was the commit merge-reachable, did the PR arm pass, are the feeds absent, does
    a tag already exist, do the sibling tags agree. A fact set that guessed one of those wrong in the
    permissive direction would report an automated act as manual, which is the .github#2533 fail-open
    this arm exists to prevent.

    So it does not guess: each is pinned to the value that MOST FAVOURS "the merge performs the act",
    and each choice is uniquely permissive rather than merely plausible —

      * `provenance` — the only shape that clears `merged-pr-provenance-missing` and `pr-arm-not-passed`.
      * `orgFeed`/`nugetFeed` both `absent` — the ONLY equal pair that reaches the frontier comparison
        and can end in a tag. `present`/`present` ends at `openEvidencePr` or a sticky escalation
        (nothing is cut), and an unequal pair is `partial-publish-stop-no-retry`.
      * `tagExists: false` and both `siblingTags` empty — reaches `tag`. (The `tagExists: true` branch
        with a sibling missing reaches `tagSiblings`, which is also an act; both are permissive, so the
        choice between them changes no verdict. A NON-empty mismatching sibling would not be.)
      * `sourceSha` — any non-empty string; the sibling comparison it feeds is vacuous when every
        sibling is empty. It is deliberately a legible synthetic token rather than a plausible commit,
        so a reader of a fact dump cannot mistake it for something that was measured.

    `version` is supplied as measured and is NOT varied, because it is not free: it is a property of
    this PR's own head, and it cannot move without a push that re-runs this gate against the new one.
    The frontier is the opposite — see `kit_auto_publish_completions`.
    """
    return {
        "version": candidate,
        "provenance": {
            "mergedReachable": True,
            "introducedVersion": candidate,
            "prArm": "pass",
        },
        "orgFeed": "absent",
        "nugetFeed": "absent",
        "orgLatest": frontier,
        "nugetLatest": frontier,
        "tagExists": False,
        "siblingTags": {"drivers": "", "coordEngine": ""},
        "sourceSha": "0000000000000000000000000000000000000000",
    }


def kit_auto_publish_completions(module, candidate: str, observed: str) -> list[Completion]:
    """Every post-merge world this arm must rule out before it calls an act MANUAL.

    THE FRONTIER IS NOT A CONSTANT, AND PINNING IT TO THE OBSERVED VALUE WAS THIS FUNCTION'S OWN
    .github#2533 (round-1 repair on .github#2571). The first draft supplied the measured frontier and
    nothing else, and claimed the resulting verdict held on "ANY post-merge state of the world". It did
    not. `decide()`'s rail is `candidate.patch == frontier.patch + 1`, the feed frontier moves FORWARD
    between this gate running and the merge landing, and a forward move can therefore flip a candidate
    from `candidate-not-next-patch` into `tag`. Measured on this file's own head: candidate `0.58.3`
    against observed frontier `0.58.0` refuses, and the same candidate against `0.58.2` — which any
    intervening patch release produces — tags. Over a bounded sweep of monotone-forward completions the
    observed-only builder was unsound on 78 of 225 (candidate, observed) pairs. A false one-directional
    invariant inside the control guarding an irreversible double-publish is the exact shape .github#2533
    measured, one level down.

    So the frontier is not pinned; it is ENUMERATED, and `merge_performs_act` takes the disjunction. A
    state is included when the feed can still reach it, which for a monotonically-forward frontier means
    `>= observed`:

      * the OBSERVED frontier — the world as it stands, and deliberately FIRST so that a "not performed"
        finding quotes the reason an author can check against the live feed rather than a hypothetical.
      * the ADMITTING frontier `(minor, patch - 1)`, when the current rail has one (a candidate with
        `patch == 0` — every coherent-set MINOR — has none, which is why the minor line is unperformed
        on every reachable frontier rather than merely on this one) and when it is still reachable
        (`>= observed`; a frontier already past it cannot come back).

    THAT SET IS COMPLETE FOR THE RAIL AS WRITTEN, AND THAT COMPLETENESS IS MEASURED RATHER THAN CLAIMED
    — which is the part that matters, because the sentence this repair replaced was a claim no test
    could falsify. `tests/kit-published-coherence/run.sh` sweeps a bounded grid of candidates and
    observed frontiers, computes by BRUTE FORCE over every reachable frontier (and over `tagExists` and
    both feed-presence states, which this builder pins) whether ANY of them performs, and asserts this
    function's disjunction agrees on every pair. If .github#2442's rail is ever reshaped so that some
    OTHER frontier admits a candidate, that sweep reds and names the pair — it does not quietly go on
    being wrong. Its own inversion leg re-pins the frontier to the observed value and proves the sweep
    reds when the completion is un-pinned again.

    `patch_tuple` is read from the mapped program rather than restated, for the same reason `decide()`
    is: the set of reachable frontiers has to be expressed in the rail's OWN notion of a version, or the
    two could disagree about what "forward" means.
    """
    completions = [Completion(observed, False, _kit_auto_publish_facts(candidate, observed))]
    patch_tuple = getattr(module, "patch_tuple", None)
    if not callable(patch_tuple):
        raise GateError(
            f"the mapped decision program exposes no callable `patch_tuple` — this arm reads it to "
            f"enumerate the frontiers the feed can still reach, and will not substitute its own copy "
            f"of the rail's version grammar."
        )
    here, there = patch_tuple(candidate), patch_tuple(observed)
    # A candidate or frontier the rail's own grammar refuses needs no enumeration: `decide()` ends at
    # `version-not-stable-0x-patch` or `feed-frontier-unknown` on the observed world, and a frontier
    # that moves FORWARD from an unparsable one stays unparsable, so that refusal is stable.
    if here and there and here[1] >= 1:
        admitting = (here[0], here[1] - 1)
        if admitting > there:
            frontier = f"0.{admitting[0]}.{admitting[1]}"
            completions.append(
                Completion(frontier, True, _kit_auto_publish_facts(candidate, frontier))
            )
    return completions


MERGE_AUTOMATION: tuple[MergeAutomation, ...] = (
    MergeAutomation(
        workflow=".github/workflows/kit-auto-publish.yml",
        # `package-release` is the token `.github#2512` actually used, and it is the reason this arm
        # exists; the four namespace-specific spellings are the ones `merge-and-release.md` offers a
        # worker who wants to name the artifact rather than the act. All five reach the same workflow
        # because .github#2409's coherent set is published from three sibling tags at ONE commit —
        # naming one package does not make the act a different act.
        kinds=frozenset(
            {
                "package-release",
                "coherent-set-release",
                "kit-release",
                "coord-engine-release",
                "drivers-release",
            }
        ),
        performs=(
            "cuts kit/v<version>, coord-engine/v<version> and drivers/v<version> at the merge commit "
            "and starts release-kit / release-coord-engine / release-drivers"
        ),
        decision="scripts/kit-auto-publish.py",
        completions=kit_auto_publish_completions,
    ),
)

# `kit-auto-publish.decide()`'s actions that CUT NOTHING, listed as an allow-list rather than naming
# the two that do (.github#2571). The direction is the whole point. Reading an act as PERFORMED when it
# is not costs a legitimate obligation its declarable token — annoying, visible, and repaired in one
# edit. Reading it as NOT PERFORMED when it is restores .github#2533's defect: a manual release
# obligation sails through, a worker discharges it after the automation already ran, and .github#2240's
# two-of-three sets are the standing cost. So the safe default for an action nobody here anticipated is
# "the merge performs it", and an action that is neither in this set nor a known act is a NO-VERDICT
# (see `merge_performs_act`) rather than a silent verdict in either direction.
_DECISION_CUTS_NOTHING = frozenset({"refuse", "stickyEscalate", "openEvidencePr"})
# The actions that DO write a tag, and therefore perform the act a declaration must not claim.
_DECISION_PERFORMS = frozenset({"tag", "tagSiblings"})

# The engine's own filter, restated here because this arm must agree with it rather than hold a
# second opinion about what a declaration is — and since .github#2563 that agreement is MECHANICAL
# rather than a promise: both sides are graded against `tests/delivery-leading-line/corpus.json`, so
# this file cannot drift from the engine without one of the two suites going red. See `_leading_line`
# below for what that replaced and why a docstring was not enough.
# `DeliveryApplication.fs`'s `obligationsFromComments`
# selects comments whose LEADING LINE — the first line of the trimmed body — starts with
# `<!-- fsgg:delivery-obligation`. A comment that opens with prose and carries a perfectly-formed
# marker on line 3 parses as ABSENT there, so it must parse as absent here too; an arm that were more
# generous would flag declarations the engine cannot see, and stay quiet about the trap that made a
# fully-written declaration inert.
#
# THIS SAID "AT BYTE 0" UNTIL .github#2544, AND THAT IS THE POINT OF THE ROW. The engine's candidate
# pre-filter really was a raw `Body.StartsWith` while the parser it fed trimmed first, so a body
# opening with a newline — what heredocs and `gh api --field` payloads add for free — was discarded
# before the parser ran. The engine now asks the leading-line question once, and this arm follows it
# rather than preserving the byte-0 reading it was written to mirror: an arm left stricter than the
# engine would call a live declaration absent, which is the same invisibility one layer down.
_DECLARATION_PREFIX = "<!-- fsgg:delivery-obligation"
# `<!-- fsgg:delivery-obligations none head=<sha> -->` shares that prefix (plural, then a space), and
# is the assertion that NOTHING is owed — not an obligation. It is skipped, not parsed.
_NONE_PREFIX = "<!-- fsgg:delivery-obligations "
# `DeliveryApplication.fs`'s `obligationDeclaration`, character for character: the same id and kind
# grammars, the same single-line shape, anchored the same way.
_OBLIGATION_ID = r"[a-z0-9][a-z0-9_.-]*"
_OBLIGATION_KIND = r"[a-z0-9][a-z0-9_-]*"
_DELIVERY_HEAD = r"[0-9A-Za-z._-]+"
_OBLIGATION_DECLARATION = re.compile(
    r"\A<!-- fsgg:delivery-obligation"
    rf" id=(?P<id>{_OBLIGATION_ID})"
    rf" kind=(?P<kind>{_OBLIGATION_KIND})"
    rf" head=(?P<head>{_DELIVERY_HEAD}) -->\Z"
)


def _leading_line(body: str) -> str:
    """`DeliveryApplication.leadingLine`, restated — including its CommonMark indent limit.

    Leading blank lines and up to THREE spaces of indentation render invisibly, so a marker behind
    them is still the comment's leading line. FOUR spaces, or a tab (one tab stop), opens a CommonMark
    INDENTED CODE BLOCK: the marker is then a visible code sample, and reading it as a declaration is
    the fail-open the round-1 critic measured on `.github#2544` (a bystander's code sample destroying
    somebody else's valid declaration, and an indented declaration+receipt pair reading `verified`).
    The engine returns the line AS WRITTEN in that case so nothing can match it; so does this.

    THE SENTENCE ABOVE USED TO BE THE WHOLE COUPLING, AND THAT WAS THE DEFECT (.github#2563). "Restated
    here" was a claim no test could falsify: the engine held one copy of the limit, this function held
    another, each side pinned its own copy with its own fixture legs, and a COORDINATED one-sided edit —
    moving one constant AND updating that same side's legs — passed both. It is now enforced instead of
    asserted. `tests/delivery-leading-line/corpus.json` is the single statement of this boundary;
    `tests/kit-published-coherence/run.sh` grades THIS function's real entry point
    (`obligation_declarations`) against it, and `tests/FS.GG.Coord.Cli.Tests/DeliveryApplicationTests.fs`
    grades the engine against the same verdicts. Neither side keeps a private leg asserting a SINGLE
    COMMENT BODY's declares/inert verdict, so changing the limit here reds the corpus, and editing the
    corpus to restore it reds the engine suite. If you are about to change the test below, that corpus
    is the file to change.

    EXPECT THE ENGINE SUITE TO RED IN MORE PLACES THAN THE CORPUS, and do not read that as the corpus
    being wrong. `DeliveryApplicationTests.fs` retains four `.github#2544` legs carrying four-space
    declaration-form bodies (`:304`/`:307`/`:318`/`:492`) that the corpus cannot subsume: two are
    MULTI-COMMENT scenarios — one of them turning on a `fsgg:delivery-receipt` marker this arm never
    parses — and one asserts the engine's diagnostic wording, which this arm does not emit. A
    coordinated one-sided edit reds those too: `Failed: 4, Passed: 801`, alongside the corpus legs.
    They make the engine side stricter, never more permissive.
    """
    normalized = body.replace("\r\n", "\n")
    first = next((line for line in normalized.split("\n") if line.strip()), None)
    if first is None:
        return ""
    indent = first[: len(first) - len(first.lstrip(" \t"))]
    if "\t" in indent or len(indent) >= 4:
        return first
    return body.strip().replace("\r\n", "\n").split("\n", 1)[0]

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


def staging_owned_inputs(roster_path: str, csproj_path: str) -> list[str]:
    """Read every exact source whose bytes (or staging recipe) can change FS.GG.Kit.

    The registry owns staged coordination content; the pack project owns its explicit packed
    members.  Reading both owners makes additions and removals visible to this arm without a
    second hand-maintained source list (#1692).
    """
    inputs = set(kit_sources(roster_path))
    try:
        project = ET.parse(csproj_path).getroot()
    except (OSError, ET.ParseError) as e:
        raise GateError(f"cannot read the kit pack project {csproj_path!r}: {e}") from e
    project_dir = os.path.dirname(os.path.abspath(csproj_path))
    for element in project.iter():
        if element.tag.rsplit("}", 1)[-1] != "None" or element.attrib.get("Pack", "").lower() != "true":
            continue
        include = element.attrib.get("Include", "").strip()
        if not include or any(token in include for token in ("*", "?")):
            raise GateError(
                f"{csproj_path} has a packed None item with no exact Include path ({include!r}); "
                "the PR arm cannot enumerate an approximate package subject."
            )
        inputs.add(os.path.relpath(os.path.normpath(os.path.join(project_dir, include)), REPO_ROOT).replace(os.sep, "/"))
    inputs.add(os.path.relpath(os.path.abspath(csproj_path), REPO_ROOT).replace(os.sep, "/"))
    inputs.add("src/FS.GG.Kit/stage-kit.sh")
    return sorted(inputs)


def changed_paths(base: str) -> list[str]:
    """Repo-relative paths this PR changes, resolved from HEAD and the current base ref.

    Resolve the merge base explicitly, then diff that commit against HEAD. The workflow passes the
    fetched remote-tracking base branch, not `pull_request.base.sha`: GitHub can deliver a stale event
    SHA while actions/checkout has checked out a merge ref recomputed against a newer base. Mixing
    those two snapshots attributes the base branch's own commits to the PR (.github#1910).

    Any failure to resolve either side or compute the diff is a GateError. A PR whose diff cannot be
    read has an UNKNOWN obligation, and an unknown obligation must not merge (#266) — an empty list
    here would read as "touched nothing".
    """
    if not base or not base.strip():
        raise GateError(
            "the PR arm has no base ref to diff against (pass --base, or set GITHUB_BASE_REF) — "
            "without one there is no diff, and no diff is not 'no kit sources touched'."
        )
    try:
        base_commit = subprocess.run(
            ["git", "rev-parse", "--verify", f"{base.strip()}^{{commit}}"],
            cwd=REPO_ROOT,
            text=True,
            capture_output=True,
            check=False,
        )
        merge_base = subprocess.run(
            ["git", "merge-base", "HEAD", base.strip()],
            cwd=REPO_ROOT,
            text=True,
            capture_output=True,
            check=False,
        )
    except OSError as e:
        raise GateError(f"cannot run git to read this PR's diff: {e}") from e
    if base_commit.returncode != 0:
        detail = (base_commit.stderr or base_commit.stdout).strip()
        raise GateError(
            f"cannot resolve base {base!r} to a commit"
            + (f": {detail}" if detail else "")
            + " — fetch the current base branch; an unreadable base is a no-verdict, not a green."
        )
    if merge_base.returncode != 0:
        detail = (merge_base.stderr or merge_base.stdout).strip()
        raise GateError(
            f"cannot resolve a merge base between HEAD and {base!r}"
            + (f": {detail}" if detail else "")
            + " — fetch the current base branch and enough history (actions/checkout "
            "`fetch-depth: 0`), then rebase if the histories genuinely do not meet. A diff this arm "
            "cannot compute is a no-verdict, not a green."
        )
    resolved = merge_base.stdout.strip()
    if not re.fullmatch(r"[0-9a-fA-F]{40}", resolved):
        raise GateError(
            f"git merge-base returned no single 40-hex commit for HEAD and {base!r}: "
            f"{resolved!r} — the PR's changed-file subject is unresolved, not empty."
        )

    # On GitHub's pull_request merge ref, HEAD^1 is the base commit used to construct the checked-out
    # tree. If the caller supplied an older ancestor, mixing it with that newer merge ref is exactly
    # #1910's false attribution. This is a different remedy from a real PR-owned kit edit: refresh or
    # rebase; do not bump and publish a package for somebody else's base commit.
    first_parent = subprocess.run(
        ["git", "rev-parse", "--verify", "HEAD^1"],
        cwd=REPO_ROOT,
        text=True,
        capture_output=True,
        check=False,
    )
    second_parent = subprocess.run(
        ["git", "rev-parse", "--verify", "HEAD^2"],
        cwd=REPO_ROOT,
        text=True,
        capture_output=True,
        check=False,
    )
    supplied = base_commit.stdout.strip()
    parent = first_parent.stdout.strip()
    if first_parent.returncode == 0 and second_parent.returncode == 0 and supplied != parent:
        ancestor = subprocess.run(
            ["git", "merge-base", "--is-ancestor", supplied, parent],
            cwd=REPO_ROOT,
            text=True,
            capture_output=True,
            check=False,
        )
        if ancestor.returncode == 0:
            raise GateError(
                f"base {base!r} resolves to {supplied}, behind the checked-out merge ref's base "
                f"parent {parent}. Refresh or rebase the PR and rerun this arm; do not bump "
                f"FS.GG.Kit for commits that belong to the base branch."
            )
        if ancestor.returncode not in (0, 1):
            detail = (ancestor.stderr or ancestor.stdout).strip()
            raise GateError(
                f"cannot compare supplied base {supplied} with HEAD's first parent {parent}"
                + (f": {detail}" if detail else "")
                + " — base currency is unresolved, not green."
            )

    result = subprocess.run(
        ["git", "diff", "--name-only", resolved, "HEAD"],
        cwd=REPO_ROOT,
        text=True,
        capture_output=True,
        check=False,
    )
    if result.returncode != 0:
        detail = (result.stderr or result.stdout).strip()
        raise GateError(
            f"cannot diff resolved merge base {resolved} against HEAD"
            + (f": {detail}" if detail else "")
            + " — this arm has no changed-file verdict, not a green."
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
    """The EVALUATED `<Version>` FS.GG.Kit.csproj publishes. Never a grep (.github#2402).

    FS.GG.Kit, FS.GG.Drivers and coord-engine became a coherent set in .github#2402: `<Version>`
    resolves from a shared `$(FsggCoherentSetVersion)` MSBuild property (Directory.Build.props)
    rather than a literal in this file, exactly the same shape `check-engine-freshness.py` and
    `check-engine-release-notes.py` already read via `dotnet msbuild -getProperty:Version` — "never
    a grep" is `release-coord-engine.yml`'s own header text for the identical reason. A regex over
    the raw XML would capture the literal token `$(FsggCoherentSetVersion)`, not a version, and
    silently mis-parse every comparison downstream.
    """
    # A SINGLE `-getProperty:` prints the bare value as plain text; `dotnet msbuild` only switches to
    # the `{"Properties": {...}}` JSON document (what check-engine-release-notes.py's two-property
    # call receives) once TWO OR MORE are requested (verified directly: `-getProperty:Version` alone
    # on this project prints `0.49.0`, no braces). One property is all this arm needs, so this reads
    # the plain-text form rather than requesting an unused second property just to get JSON.
    try:
        run = subprocess.run(
            ["dotnet", "msbuild", csproj_path, "-getProperty:Version"],
            capture_output=True,
            text=True,
            check=False,
        )
    except OSError as e:
        raise GateError(f"cannot run dotnet msbuild to evaluate {csproj_path!r}: {e}") from e
    if run.returncode != 0:
        detail = run.stderr.strip() or run.stdout.strip() or "(no diagnostic)"
        raise GateError(f"dotnet msbuild could not evaluate {csproj_path!r}: {detail}")
    version = run.stdout.strip()
    if not version:
        raise GateError(
            f"{csproj_path} evaluates to an empty Version; this arm needs exactly one to know what a "
            f"merge would publish."
        )
    return version


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
        else staging_owned_inputs(roster_path, csproj_path)
    )
    changed = (
        canned_lines(canned_changed, "changed-file list")
        if canned_changed
        else changed_paths(base)
    )

    hits = touched_kit_sources(changed, sources)
    if not hits:
        print(
            f"ok: this PR changes {len(changed)} file(s), none of them a staging-owned package input "
            f"declared by {roster_path} and {csproj_path} ({len(sources)} input(s) considered). No republish obligation, and the "
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
            f"ok: this PR touches {len(hits)} staging-owned package input file(s), and "
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


def workflow_triggers(path: str) -> dict:
    """One workflow's `on:` block, with all three legal spellings normalised.

    PyYAML resolves the bare key `on` to the boolean True (YAML 1.1), so a plain `doc["on"]` misses
    it entirely and EVERY workflow would look like it triggers on nothing — which here would mean
    silently deciding no act is automated, the fail-open this file exists to refuse. Same trap, same
    handling, as scripts/check-paths-coherence.py, check-workflow-timeouts.py and
    check-workflow-permissions.py; nothing about it is specific to this arm.

    `on: push` and `on: [push, pull_request]` are as legal as the mapping form. Anything that is none
    of the three spellings is refused, not guessed.
    """
    import yaml  # lazy: only this arm and the pr-arm's roster read parse YAML.

    try:
        text = open(path, encoding="utf-8").read()
    except OSError as e:
        raise GateError(
            f"cannot read the mapped workflow {path!r}: {e}. MERGE_AUTOMATION names it as the actor "
            f"that performs a declarable obligation, so a workflow this arm cannot open is a "
            f"no-verdict — never an arm that quietly matches nothing (renamed? deleted? update "
            f"MERGE_AUTOMATION and merge-and-release.md's table together)."
        ) from e
    try:
        doc = yaml.safe_load(text)
    except yaml.YAMLError as e:
        raise GateError(f"{path} is not parsable as YAML: {e}") from e
    if not isinstance(doc, dict):
        raise GateError(f"{path} is not a YAML mapping")
    for key in ("on", True):
        if key in doc:
            got = doc[key]
            if isinstance(got, dict):
                return got
            if isinstance(got, list):
                return {str(k): None for k in got}
            if isinstance(got, str):
                return {got: None}
            raise GateError(
                f"{path}: `on:` is {type(got).__name__}, not a string, list, or mapping — this arm "
                f"cannot tell what triggers the workflow, and guessing would silently decide the act "
                f"is manual (#266)."
            )
    raise GateError(f"{path}: no `on:` block — this arm cannot tell what triggers the workflow")


def merge_triggers_workflow(triggers: dict, path: str) -> bool:
    """Does a merge into `main` start this workflow?

    True only for a `push:` trigger that reaches `main` UNCONDITIONALLY. A `paths:`/`paths-ignore:`
    filter makes the answer a function of the diff, which this arm does not have; that is a
    GateError (no-verdict), never a False that would read as "manual, carry on".
    """
    if "push" not in triggers:
        return False
    push = triggers["push"]
    if push is None:  # bare `on: push` / `on: [push]` — every branch, every path.
        return True
    if not isinstance(push, dict):
        raise GateError(
            f"{path}: `on.push` is {type(push).__name__}, not a mapping — this arm cannot tell "
            f"which branches it fires on"
        )
    if "paths" in push or "paths-ignore" in push:
        raise GateError(
            f"{path}: `on.push` carries a path filter, so whether THIS merge starts it depends on "
            f"the diff — a question this arm does not answer. 'Cannot tell' must not merge (#266). "
            f"Either evaluate the filter here, or say in merge-and-release.md's table that the act "
            f"is conditional."
        )
    if "branches" in push and "branches-ignore" in push:
        raise GateError(
            f"{path}: `on.push` declares both `branches:` and `branches-ignore:`, which GitHub "
            f"rejects — this arm will not invent a precedence between them"
        )

    def patterns(key: str) -> list[str]:
        value = push[key]
        if not isinstance(value, list):
            raise GateError(
                f"{path}: `on.push.{key}` is {type(value).__name__}, not a list — this arm cannot "
                f"tell whether it covers {DEFAULT_BRANCH!r}"
            )
        return [str(item) for item in value]

    # GitHub matches these as GLOBS, not literals, so `ma*` and `**` reach `main` exactly as `main`
    # does. Comparing literals would read a glob-filtered trigger as not covering `main` and report
    # an automated act as manual — the fail-open direction.
    if "branches" in push:
        return any(fnmatch.fnmatch(DEFAULT_BRANCH, pattern) for pattern in patterns("branches"))
    if "branches-ignore" in push:
        return not any(
            fnmatch.fnmatch(DEFAULT_BRANCH, pattern) for pattern in patterns("branches-ignore")
        )
    return True  # `push:` with no branch filter at all — every branch, `main` included.


def decision_function(path: str):
    """One mapped automation's decision program, LOADED from its own source file (.github#2571).

    Loaded, never reimplemented, and never summarised in a table here: the rule this arm needs is
    "would the merge actually cut anything for THIS candidate", and the only correct statement of that
    rule is the program the workflow runs. `.github#2442`'s frontier rail is a maintainer decision that
    is expected to be revisited; a copy of it here would be a second opinion that goes stale the day it
    changes, which is the drift `MERGE_AUTOMATION`'s workflow half already refuses to have.

    The file name carries a hyphen, so it is not importable by module name; it is loaded by path. The
    module is registered in `sys.modules` before execution because a module object absent from there
    breaks `dataclasses`' own type resolution on 3.12+ if the loaded file ever grows a dataclass.

    Unreadable, unparsable, or missing a callable `decide` is a GateError — the SAME fail-closed
    posture, for the same reason, as a mapped workflow that cannot be opened. This half of the map must
    not be able to rot into silence either.
    """
    import importlib.util

    if not os.path.exists(path):
        raise GateError(
            f"cannot read the mapped decision program {path!r}: no such file. MERGE_AUTOMATION names "
            f"it as the program that decides whether the merge actually performs a declarable act, so "
            f"a program this arm cannot load is a no-verdict — never an arm that assumes the act "
            f"happens or that it does not (renamed? deleted? update MERGE_AUTOMATION and "
            f"merge-and-release.md's table together)."
        )
    spec = importlib.util.spec_from_file_location(
        "fsgg_merge_decision_" + re.sub(r"\W", "_", path), path
    )
    if spec is None or spec.loader is None:
        raise GateError(f"cannot load the mapped decision program {path!r} as a Python module")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    try:
        spec.loader.exec_module(module)
    except BaseException as e:  # noqa: BLE001
        # BaseException, not Exception, and that is not defensive breadth. `SystemExit` is a
        # BaseException: a decision program that calls `sys.exit(0)` at import — or is edited into one
        # by accident, a stray `exit()` in a debug session — would otherwise propagate straight out of
        # this gate and EXIT IT ZERO, greening the arm on a program it never actually loaded. That is
        # the fail-open this whole file is written against, so every way the load can end that is not
        # "the module is now usable" ends here instead.
        raise GateError(f"the mapped decision program {path!r} failed to load: {e!r}") from e
    if not callable(getattr(module, "decide", None)):
        raise GateError(
            f"{path} exposes no callable `decide` — this arm reads that function to learn whether the "
            f"merge performs the act, and cannot substitute its own copy of the rule."
        )
    return module


def merge_performs_act(
    automation: MergeAutomation, *, candidate: str, frontier: str
) -> tuple[bool, Completion, dict]:
    """Can merging this PR CUT the act, on any post-merge world still reachable? (.github#2571)

    Returns (performed, completion, decision) — the world the answer came from and `decide()`'s own
    verdict in it, carried out so a finding can quote the action, reason and scope note it produced
    rather than paraphrase them, and can name the frontier it actually scored against.

    THE ANSWER IS A DISJUNCTION, NOT A SINGLE EVALUATION, and that is this function's round-1 repair on
    .github#2571. "The merge performs this act" has to mean "the merge COULD perform this act", because
    the world moves between a PR-time gate and a merge: the feed frontier advances, and the rail
    `decide()` applies is a function of it. Ruling the act manual therefore means ruling out every
    reachable world, so the first performing completion wins and only an all-declining sweep returns
    False. `kit_auto_publish_completions` owns which worlds those are and why the set is complete.

    A "not performed" answer is reported from the FIRST completion — the observed world — so the reason
    an author reads is the one they can check against the live feed.

    An action this file recognises as neither cutting nor not-cutting is a GateError. That is not
    pedantry: `decide()` gaining a sixth action is exactly when a silent assumption here would be
    wrong, and 'cannot tell' must not merge (#266).
    """
    assert automation.decision and automation.completions  # guarded at load; see `_assert_map`
    module = decision_function(automation.decision)
    completions = automation.completions(module, candidate, frontier)
    if not completions:
        raise GateError(
            f"{automation.decision}'s completion builder produced NO post-merge world to score. An "
            f"empty sweep declines by vacuity, which is a pass this arm must never reach by accident."
        )
    declined: list[tuple[Completion, dict]] = []
    for completion in completions:
        try:
            decision = module.decide(completion.facts)
        except BaseException as e:  # noqa: BLE001 — `SystemExit` too; see `decision_function`'s note.
            raise GateError(
                f"{automation.decision}'s decide() raised on the candidate fact set "
                f"(frontier {completion.frontier}): {e!r}"
            ) from e
        if not isinstance(decision, dict) or not isinstance(decision.get("action"), str):
            raise GateError(
                f"{automation.decision}'s decide() returned {decision!r}, which carries no string "
                f"`action` — this arm cannot tell whether the merge performs the act."
            )
        action = decision["action"]
        if action in _DECISION_PERFORMS:
            return True, completion, decision
        if action not in _DECISION_CUTS_NOTHING:
            raise GateError(
                f"{automation.decision}'s decide() returned the action {action!r}, which this arm "
                f"classifies as neither performing the act nor declining to. Add it to "
                f"_DECISION_CUTS_NOTHING or _DECISION_PERFORMS in this file — deliberately, because "
                f"guessing in the permissive direction is how .github#2533's defect comes back."
            )
        declined.append((completion, decision))
    return (False, *declined[0])


def _assert_map() -> None:
    """`decision` and `completions` are both-or-neither: a half-declared row would skip the check."""
    for automation in MERGE_AUTOMATION:
        if bool(automation.decision) != bool(automation.completions):
            raise GateError(
                f"MERGE_AUTOMATION row for {automation.workflow} declares only one of "
                f"`decision`/`completions`; a row with a decision program but no completion builder "
                f"(or the reverse) would silently fall back to the trigger-only rule .github#2571 "
                f"replaced."
            )


@dataclass(frozen=True)
class Declaration:
    """One parsed `fsgg:delivery-obligation` declaration from this PR's comments."""

    id: str
    kind: str
    head: str


def obligation_declarations(comments_path: str) -> tuple[list[Declaration], int]:
    """Parse this PR's comment bodies into declarations, exactly as the engine selects them.

    Returns the declarations and the number of comments considered. Accepts either the raw
    `gh api .../issues/<n>/comments` shape (a list of objects carrying `body`) or a plain list of
    bodies, because the workflow may reasonably hand over either and a shape this arm cannot read
    must be an error rather than an empty set.
    """
    try:
        raw = open(comments_path, encoding="utf-8").read()
    except OSError as e:
        raise GateError(f"cannot read the PR comments {comments_path!r}: {e}") from e
    try:
        payload = json.loads(raw)
    except json.JSONDecodeError as e:
        raise GateError(f"{comments_path} is not parsable as JSON: {e}") from e
    if not isinstance(payload, list):
        raise GateError(
            f"{comments_path} is {type(payload).__name__}, not a list of comments — an unreadable "
            f"subject is a no-verdict, not 'this PR declared nothing'."
        )

    bodies: list[str] = []
    for index, comment in enumerate(payload):
        if isinstance(comment, str):
            bodies.append(comment)
        elif isinstance(comment, dict) and isinstance(comment.get("body"), str):
            bodies.append(comment["body"])
        else:
            raise GateError(
                f"{comments_path}[{index}] is neither a string nor an object with a string `body`"
            )

    declarations: list[Declaration] = []
    for body in bodies:
        # ONE reading of "leading", used by the filter and the parse alike (.github#2544). Testing the
        # raw body here and the trimmed leading line below is exactly the disagreement that row fixed
        # in the engine; repeating it here would have re-created it in the gate that mirrors it.
        leading = _leading_line(body)
        if not leading.startswith(_DECLARATION_PREFIX):
            continue  # Not the comment's leading line. Not generosity — agreement with the engine.
        if leading.startswith(_NONE_PREFIX):
            continue  # `fsgg:delivery-obligations none head=…`: the assertion that nothing is owed.
        matched = _OBLIGATION_DECLARATION.match(leading)
        if not matched:
            raise GateError(
                f"a comment opens with {_DECLARATION_PREFIX!r} but its leading line does not parse "
                f"as a declaration: {leading!r}. This arm reports that as a no-verdict rather than "
                f"guessing a kind; `DeliveryApplication.fs` owns the diagnosis of WHY (malformed id, "
                f"kind, or head), and `scripts/fsgg-coord delivery` will report it."
            )
        declarations.append(
            Declaration(
                id=matched.group("id"), kind=matched.group("kind"), head=matched.group("head")
            )
        )
    return declarations, len(bodies)


def run_obligation_arm(
    *,
    comments_path: str,
    csproj_path: str,
    canned_candidate: str | None = None,
    canned_published: str | None = None,
) -> int:
    """The .github#2533 rule as .github#2571 corrected it.

    Exit 0 = no declared obligation names an act that merging THIS PR performs. Two conditions have to
    hold for an act to be performed, and only the first of them used to be asked: the merge must START
    the automation (its `on:` block), and the automation must then CUT something for this candidate
    (its own `decide()`). A coherent-set MINOR fires `kit-auto-publish.yml` and is terminally refused
    by `kit-auto-publish.py`, so the release is real, manual and owed — and under the trigger-only rule
    every token naming it was flagged, leaving its author the three bad options .github#2571 lists.
    """
    _assert_map()
    # Read the whole map FIRST, on every run, declarations or not: a mapped workflow that has been
    # renamed away is the way this table rots into silence, and silence is the defect. Parsing is
    # unconditional; only the VERDICT on an unanswerable trigger is deferred, so an unrelated PR is
    # not held on a question it never asked. The DECISION half is read unconditionally for exactly the
    # same reason (.github#2571) — a renamed or broken `kit-auto-publish.py` must be a no-verdict, not
    # an arm that silently falls back to trusting the trigger. Loading it is local and free; only the
    # observation it needs (msbuild + the feed) is deferred until a declaration makes it load-bearing.
    parsed: dict[str, dict] = {}
    triggered: dict[str, bool] = {}
    decided: list[str] = []
    for automation in MERGE_AUTOMATION:
        parsed[automation.workflow] = workflow_triggers(automation.workflow)
        try:
            triggered[automation.workflow] = merge_triggers_workflow(
                parsed[automation.workflow], automation.workflow
            )
        except GateError:
            triggered[automation.workflow] = True
        if automation.decision:
            decision_function(automation.decision)
            decided.append(automation.decision)

    declarations, considered = obligation_declarations(comments_path)
    mapped = {kind: a for a in MERGE_AUTOMATION for kind in a.kinds}

    # The candidate's two observable facts, read AT MOST ONCE and only when a declaration actually
    # turns on them. `dotnet msbuild` and a nuget.org round-trip are not costs an unrelated PR should
    # pay, and the pr-arm applies the same discipline ("the feed was not read").
    observed: dict[str, str] = {}

    def observe() -> tuple[str, str]:
        if not observed:
            observed["candidate"] = canned_candidate or declared_kit_version(csproj_path)
            observed["frontier"] = canned_published or newest_published_stable()
        return observed["candidate"], observed["frontier"]

    findings: list[str] = []
    declined: list[str] = []
    for declaration in declarations:
        automation = mapped.get(declaration.kind)
        if automation is None:
            continue
        # Re-ask the deferred question now that it is load-bearing: this is the leg where "cannot
        # tell" must red rather than pass, so the GateError is deliberately NOT caught here.
        if not merge_triggers_workflow(parsed[automation.workflow], automation.workflow):
            continue
        decision = None
        if automation.decision:
            candidate, frontier = observe()
            performs, completion, decision = merge_performs_act(
                automation, candidate=candidate, frontier=frontier
            )
            # The world the verdict came from, named exactly. A hypothetical frontier quoted as if it
            # were measured would send an author to the feed to check a number that is not there.
            world = (
                f"feed frontier {completion.frontier}"
                if not completion.hypothetical
                else (
                    f"frontier {completion.frontier} — not the {frontier} on the feed now, but one it "
                    f"can still reach before this merges, and the feed only moves forward"
                )
            )
            if not performs:
                note = decision.get("note")
                declined.append(
                    f"    obligation id={declaration.id} kind={declaration.kind}\n"
                    f"      {automation.workflow} IS merge-triggered, but "
                    f"{automation.decision} decides `{decision['action']}` "
                    f"({decision.get('reason', '(no reason)')}) for {PACKAGE} {candidate} against "
                    f"{world} — and against every other frontier the feed can still reach. It fires "
                    f"and cuts nothing, so this act is yours."
                    + (f"\n      {note}" if note else "")
                )
                continue
        findings.append(
            f"    obligation id={declaration.id} kind={declaration.kind}\n"
            f"      performed by {automation.workflow}, which {automation.performs}"
            + (
                ""
                if decision is None
                else (
                    f"\n      {automation.decision} decides `{decision['action']}` "
                    f"({decision.get('reason', '(no reason)')}) for {PACKAGE} "
                    f"{observed['candidate']} against {world} — the merge can cut this one."
                )
            )
        )

    if not findings:
        return _obligation_ok(declarations, considered, mapped, triggered, decided, declined)

    print(
        f"::error::check-kit-published-coherence: this PR declares {len(findings)} post-merge "
        f"obligation(s) for an act THE MERGE ITSELF PERFORMS.\n"
        + "\n".join(findings)
        + "\n"
        f"This is .github#2533. `.github#2512` declared exactly this — a manual coherent-set "
        f"release, reviewed by two independent critics across three rounds and explicitly host-gated "
        f"— and the merge had cut all three tags 8 seconds later. The declaration looked like a "
        f"control and was inert.\n"
        f"REWRITE THE OBLIGATION AS VERIFICATION, NOT PERFORMANCE: 'verify the automatic release' — "
        f"read the published bytes against canonical, which is what a green release workflow does "
        f"NOT prove. Discharging it by hand after the automation has run re-tags or re-publishes "
        f"(.github#2240: two packs of an identical checkout differ), and this repo already carries "
        f"two permanent two-of-three sets as the cost of that.\n"
        f"AND IF THE OBLIGATION CARRIES A PRE-ACT STOP CONDITION, IT IS IN THE WRONG PLACE. A "
        f"condition readable only AFTER the act is not a stop condition; put it in a PRE-MERGE gate "
        f"— see `pnext-item`'s merge-and-release.md, 'Which post-merge acts are automated'.",
        file=sys.stderr,
    )
    return 1


def _obligation_ok(
    declarations: list[Declaration],
    considered: int,
    mapped: dict[str, MergeAutomation],
    triggered: dict[str, bool],
    decided: list[str],
    declined: list[str],
) -> int:
    """The green line, which must say what was MEASURED — not merely that nothing was found."""
    automated = sorted(w for w, fires in triggered.items() if fires)
    manual = sorted(w for w, fires in triggered.items() if not fires)
    kinds = ", ".join(sorted(mapped)) or "(none)"
    note = (
        ""
        if not manual
        else (
            f" {len(manual)} mapped workflow(s) no longer trigger on a merge into "
            f"{DEFAULT_BRANCH} and therefore flag nothing: {', '.join(manual)}."
        )
    )
    read = (
        f" {len(triggered)} workflow(s) were opened and their `on:` blocks parsed on this run"
        + (
            f", and {len(decided)} decision program(s) ({', '.join(sorted(decided))}) were loaded"
            if decided
            else ""
        )
        + "."
    )
    if not declarations:
        print(
            f"ok: this PR's {considered} comment(s) carry no `fsgg:delivery-obligation` declaration, "
            f"so there is no declared act to compare against the {len(automated)} merge-triggered "
            f"automation(s) this repository runs. Whether obligations are REQUIRED here is "
            f"`scripts/fsgg-coord delivery`'s question, not this arm's.{note}"
        )
        return 0
    # .github#2571's leg, and it is a DIFFERENT green from the one below: the kind DOES name a mapped
    # act and the workflow IS merge-triggered — the obligation survives because the program that
    # workflow runs declines to cut anything for this candidate. Saying so is the whole point. An
    # author who reads "name no act that merging this PR performs" for a `coherent-set-release` would
    # reasonably conclude the gate had stopped looking.
    if declined:
        print(
            f"ok: {len(declined)} declared obligation(s) name a mapped act that merging this PR "
            f"does NOT perform for the version it ships, so they are declarable and owed:\n"
            + "\n".join(declined)
            + f"\nThis is .github#2571. A TRIGGER IS NOT AN ACT: the workflow fires on every merge "
            f"into {DEFAULT_BRANCH}, and what it then does is that program's decision, read here "
            f"rather than assumed.{read}{note}"
        )
        return 0
    print(
        f"ok: {len(declarations)} declared obligation(s) "
        f"({', '.join(sorted(d.kind for d in declarations))}) name no act that merging this PR "
        f"performs. The kinds that WOULD be flagged are: {kinds} — read from MERGE_AUTOMATION, whose"
        f"{read}{note}"
    )
    return 0


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
        help="this repository's git remote whose release tags are the anchor (default: its GitHub HTTPS URL)",
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
    ap.add_argument(
        "--obligation-arm",
        action="store_true",
        help="the obligation rule (.github#2533): does this PR declare a post-merge obligation for "
        "an act the merge itself performs?",
    )
    ap.add_argument(
        "--obligations",
        help="this PR's comments, as `gh api repos/<slug>/issues/<n>/comments` emits them. This is "
        "how the arm's SUBJECT is delivered, not a canned answer — the same shape as "
        "check-claim-generation.py's --body — so it is not locked behind the fixture switch. An "
        "unreadable, unparsable or non-list file is a no-verdict RED. An EMPTY LIST is not: a PR "
        "really can have no comments, so `[]` is a legal subject and this arm cannot tell it from "
        "a fetch that read nothing. Whoever produces this file owes the guarantee that it was "
        "actually read — see the caller's own note in kit-published-coherence.yml.",
    )
    ap.add_argument(
        "--obligation-candidate-version",
        help="the <Version> this PR would publish, canned (tests only). Live, the obligation arm "
        "evaluates it from --csproj exactly as the PR arm does.",
    )
    ap.add_argument(
        "--obligation-published-version",
        help="the newest published kit the candidate would publish above, canned (tests only). Live, "
        "the obligation arm reads nuget.org — and only when a mapped kind is actually declared.",
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
    # The obligation arm's two canned inputs (.github#2571) are locked by the SAME switch: each
    # replaces a read of an observed subject — the version this merge would publish, and the frontier
    # it would publish above — with an answer typed on the command line, and either one left open is a
    # way to choose the arm's verdict rather than measure it.
    obligation_arm_canned = {
        "--obligation-candidate-version": args.obligation_candidate_version,
        "--obligation-published-version": args.obligation_published_version,
    }
    supplied = sorted(
        flag
        for flag, value in {**pr_arm_canned, **tag_arm_canned, **obligation_arm_canned}.items()
        if value
    )
    if supplied and os.environ.get("FSGG_KIT_COHERENCE_FIXTURE_OK") != "1":
        print(
            f"::error::check-kit-published-coherence: {', '.join(supplied)} read canned input and are "
            f"NOT a coherence signal. They are available only to tests/kit-published-coherence/, which "
            f"sets FSGG_KIT_COHERENCE_FIXTURE_OK=1. Refusing to run.",
            file=sys.stderr,
        )
        return 1
    selected = [
        flag
        for flag, on in (
            ("--pr-arm", args.pr_arm),
            ("--tag-arm", args.tag_arm),
            ("--obligation-arm", args.obligation_arm),
        )
        if on
    ]
    if len(selected) > 1:
        print(
            f"::error::check-kit-published-coherence: {', '.join(selected)} are different arms with "
            f"different subjects; run one of them.",
            file=sys.stderr,
        )
        return 1
    # A flag that is silently ignored is a caller who believes they configured a run they did not
    # get — the same rule the per-arm canned-input checks below apply.
    if args.obligations and not args.obligation_arm:
        print(
            "::error::check-kit-published-coherence: --obligations is an --obligation-arm input and "
            "means nothing to the other arms. Refusing to run rather than ignoring it.",
            file=sys.stderr,
        )
        return 1

    # An arm's canned inputs mean nothing to the other arms, and a flag that is silently ignored is a
    # caller who believes they configured a run they did not get. The arm's NAME is derived from the
    # single selection above rather than restated per branch, so adding a fourth arm cannot leave one
    # of these messages naming the wrong one.
    arm_names = {
        "--pr-arm": "the PR arm",
        "--tag-arm": "the tag arm",
        "--obligation-arm": "the obligation arm",
    }
    selected_name = arm_names[selected[0]] if selected else "the published-package arm"
    misdirected = sorted(flag for flag, value in pr_arm_canned.items() if value) if not args.pr_arm else []
    if misdirected:
        print(
            f"::error::check-kit-published-coherence: {', '.join(misdirected)} are --pr-arm inputs and "
            f"mean nothing to {selected_name}. Refusing to run rather than ignoring them.",
            file=sys.stderr,
        )
        return 1
    misdirected = sorted(flag for flag, value in tag_arm_canned.items() if value) if not args.tag_arm else []
    if misdirected:
        print(
            f"::error::check-kit-published-coherence: {', '.join(misdirected)} are --tag-arm inputs and "
            f"mean nothing to {selected_name}. Refusing to run rather than ignoring them.",
            file=sys.stderr,
        )
        return 1
    misdirected = (
        sorted(flag for flag, value in obligation_arm_canned.items() if value)
        if not args.obligation_arm
        else []
    )
    if misdirected:
        print(
            f"::error::check-kit-published-coherence: {', '.join(misdirected)} are --obligation-arm "
            f"inputs and mean nothing to {selected_name}. Refusing to run rather than ignoring them.",
            file=sys.stderr,
        )
        return 1

    if args.obligation_arm:
        if args.fixture_manifest or args.canonical_manifest:
            print(
                "::error::check-kit-published-coherence: --obligation-arm and the manifest fixture "
                "flags are different arms with different subjects; run one or the other.",
                file=sys.stderr,
            )
            return 1
        if not args.obligations:
            print(
                "::error::check-kit-published-coherence: --obligation-arm requires --obligations. "
                "An arm handed no subject AT ALL must refuse, not report that nothing was declared "
                "(#266). Note this is the only 'no subject' case this arm can detect: a supplied "
                "file holding `[]` is a legal PR with no comments, and the guarantee that the file "
                "was actually fetched belongs to the caller.",
                file=sys.stderr,
            )
            return 1
        try:
            return run_obligation_arm(
                comments_path=args.obligations,
                csproj_path=args.csproj,
                canned_candidate=args.obligation_candidate_version,
                canned_published=args.obligation_published_version,
            )
        except GateError as e:
            # Same rule as the other arms: a no-verdict is RED. We cannot tell whether this PR
            # declares a manual obligation for an automated act, and "cannot tell" must not merge.
            print(f"::error::check-kit-published-coherence (obligation-arm): {e}", file=sys.stderr)
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
            remote = args.remote.strip() or f"https://github.com/{repository}.git"
            if _repository_origin(remote) != (FORGE_HOST, repository.lower()):
                raise GateError(
                    f"--remote {remote!r} is not {FORGE_HOST}/{repository}; refusing tags from a different repository"
                )
            return run_tag_arm(
                remote=remote,
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
