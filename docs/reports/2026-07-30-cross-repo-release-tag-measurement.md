# Cross-repository release-tag measurement — 2026-07-30

**Issue:** `.github#1820`.  This is a measurement, not a tag repair and not a new
required check.  It extends `.github#1790`'s comparison: the immutable NuGet
`.nuspec` SourceLink `repository/@commit` is compared with the peeled commit of
the corresponding live Git tag.  A package without that 40-hex anchor is
**UNCOVERED**, never clean (`#266`).  Prerelease versions are included.

**Reproduce:** for each row, obtain the complete version list from
`https://api.nuget.org/v3-flatcontainer/<lower-package-id>/index.json`, read the
`.nuspec` from every listed `.nupkg`, and compare its `repository/@commit` with
`git ls-remote --tags origin refs/tags/<prefix><version>^{}`.  The measurements
below were made against the eight live, non-archived repositories returned by
`gh repo list FS-GG --limit 100` on 2026-07-30.

## Result

Every repository publishes at least one package.  Every selected anchor had a
SourceLink commit, but the release tags are not generally at the commit that
packed the artifact.  This is not a null result: it is the same release-order
shape that `.github#1790` found twice, now visible across the fleet.

| repository | release namespace | immutable package anchor | anchored versions | agree | disagree | missing | unresolved |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |
| `.github` | `kit/v*`, `coord-engine/v*`, `drivers/v*`, `new-sdd-workspace/v*`, `new-sdd-fullstack/v*` | the five `FS.GG.*` packages | 57 | 54 | 2 | 1 | 0 |
| `FS.GG.Game` | `v*` | `FS.GG.Game.Core` | 17 | 9 | 8 | 0 | 0 |
| `FS.GG.Game` | `skills/v*` | `FS.GG.Game.Skills` | 5 | 5 | 0 | 0 | 0 |
| `FS.GG.Governance` | `v*` | `FS.GG.Governance.ReferenceGateSet` | 4 | 0 | 3 | 1 | 0 |
| `FS.GG.Rendering` | `v*` | `FS.GG.UI` | 30 | 0 | 30 | 0 | 0 |
| `FS.GG.Audio` | `v*` | `FS.GG.Audio.Core` | 6 | 2 | 4 | 0 | 0 |
| `FS.GG.Templates` | `fs-gg-templates/v*` | `FS.GG.Templates` | 8 | 2 | 6 | 0 | 0 |
| `FS.GG.Net` | `v*` | `FS.GG.Net.Core` | 7 | 0 | 7 | 0 | 0 |
| `FS.GG.SDD` | `v*` | `FS.GG.SDD.Cli` and `FS.GG.Contracts` | **unresolved in this run** | — | — | — | **yes** |

The table deliberately distinguishes the SDD result from a pass: the package
version lists and live tags were read, and a current `FS.GG.SDD.Cli 0.31.1`
artifact anchors `f419f0e`, which agrees with peeled `v0.31.1`; the complete
per-version sweep did not complete before this report.  It is therefore not
included in any clean total and must be re-run by the follow-up.

`FS.GG.Rendering` also has `fs-gg-ui/v*` and `fs-gg-ui-template/v*` namespaces.
They are live release namespaces, but this first sweep did not establish a
one-to-one artifact anchor for each independently of the shared UI release
versions.  They are **UNCOVERED**, not omitted or called clean.  No other
namespace was inferred from a name alone.

## What the disagreements mean

They are not evidence that a recent tag was force-moved.  Sampling the concrete
records shows the tag points at a release/version-bump commit while the
immutable package names a later commit that performed the NuGet dual-publish
wiring.  For example, `.github#1790` recorded this exact cause for
`coord-engine/v0.1.0` and `new-sdd-fullstack/v0.1.1-preview.1`; the Game,
Governance, Rendering, Audio, Templates, and Net counts show that it was a
fleet-wide retrofit pattern, not a `.github` exception.

The report does **not** recommend moving those historical tags.  Moving a tag
would mutate a cited release marker, and it would erase rather than explain the
provenance disagreement.  The live disagreement must instead be recorded on
both commits by the detector, as `.github#1790` does.

## Decision: one hub detector, not eight divergent readers

The right next step is a hub detector in `.github`, driven by the authoritative
roster and a declarative `(repository, namespace, package, grammar)` table.
It can reuse the SourceLink/NuGet/peeled-tag reader already proven by
`scripts/check-kit-published-coherence.py`; each producer-specific copy would
reimplement the same feed and tag semantics and would drift.  The hub must:

1. enumerate every rostered publisher and explicitly emit `UNCOVERED` where an
   anchor or mapping has not been established;
2. include prereleases, preserve `agree` / `disagree` / `missing` / `unresolved`
   as separate outcomes, and pin known historical disagreements rather than
   making them disappear;
3. add the resulting assertion as detection even where a repository later adds
   a tag-immutability ruleset, because a mutable setting is not historical
   evidence; and
4. add the two Rendering namespaces and complete the SDD sweep before treating
   any fleet total as complete.

Follow-up **`.github#2033`** was filed from this measurement and added to the
Coordination board as **Backlog**; it owns that implementation.  No sibling
repository setting or tag was changed by this report.
