# ADR-0074: Runtime skill identity is an end-to-end content chain

Status: Accepted

Date: 2026-08-24

## Context

A skill can exist as producer source, a published package entry, materialized receiver bytes, and
one or more runtime-visible roots. Paths and line numbers do not identify which copy an agent read.
This became observable when two checked-in copies differed by 96 lines and when a runtime-loaded
`cross-repo-coordination` skill disagreed with the copy used to verify its behavior.

The existing layers already content-address most boundaries, but their answers were separate:
`registry/skills.yml` binds a producer `source` to its `sha256`, producer manifests inventory the
whole skill tree, FS.GG.Kit's manifest binds package files to receiver destinations, and
`skill-view` validates runtime visibility. None emitted a common identity report naming the
authority and every artifact compared.

## Decision

Use `fsgg.skill-identity/v1` as the common projection. It names:

- the skill id and authoritative producer source or pinned package;
- every compared artifact path, expected sha256, actual sha256, and verdict;
- each runtime root's declared disposition (`live` or `view`); and
- one closed verdict: `coherent`, `drift`, or `inconclusive`.

Producer authority is explicit. `fsgg-skill-registry-check --identity <id>` joins the central
registry row to the owning producer manifest and its complete file inventory. Package transport is
explicit: `coordination-sync --check --against-pin --identity <id>` reads the restored package's
own manifest and compares the receiver bytes without consulting a moving hub checkout. Runtime
identity is explicit: `skill-view identity --skill <id>` reads either a source tree or manifest and
compares every file in every declared runtime root. A receiver project declaration, never traversal
order, says which root is live and which is a generated view.

`cross-repo-coordination` is the production reference subject. Its central catalog row uses the
existing `process` scope and its `.github` delivery-class declaration names FS.GG.Kit. The
generated coordination-kit manifest carries the same canonical source/body digest alongside its
closed raw-file inventory, so it is directly usable as an identity authority as well as package
input. `.github`'s driver and coordination manifests remain disjoint producer inventories; identity
selects the unique manifest entry by skill id and refuses duplicates across them.

Absent, unreadable, malformed, empty, or duplicate authority data is `inconclusive` and red. A
digest mismatch or undeclared runtime file is `drift` and red. Existing check, generate,
materialization, and parameter-selection behavior is unchanged.

## Consequences

Agents can cite a digest plus an authoritative path instead of assuming a line number identifies a
distributed copy. Gates can compose producer, package, materialized, and runtime reports without
inventing authority between them. Identity work hashes only the selected skill inventory and adds no
network request to an existing check.

The package report proves agreement with the pinned package, not that the pin is current. Registry
coherence and published-package coherence remain separate required links in the end-to-end claim.
Materialization predicates remain out of scope: they decide whether a skill should exist, while this
ADR decides which bytes exist and were loaded.
