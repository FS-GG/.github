# ADR-0069: Fable lockstep is a profiled Game.Core package contract

- **Status:** Accepted
- **Date:** 2026-07-28
- **Affects:** FS.GG.Game (producer), EHotwagner/S.I.R (consumer), FS-GG/.github
  (contract registry and Coordination board)

## Context

S.I.R. intends to compile one authoritative F# gameplay kernel for .NET and
Fable. It also intends to reuse the integer/grid substrate in
`FS.GG.Game.Core`. The current `game-sim-core` contract promises a
deterministic, BCL-only .NET package. It does not promise that the package
contains Fable-consumable source or that any result is equal between .NET and
JavaScript.

Treating “deterministic on .NET”, “compiles with Fable”, and “produces
byte-identical cross-runtime results” as synonyms would silently widen an
existing contract. The assembly also mixes integer grid operations with
floating-point continuous simulation, wall-clock accumulation, sequential
random streams, and larger modules that have not been inventoried for Fable.
A truthful promise cannot grade that assembly as one unit.

The consumer cannot solve this by linking a sibling checkout or copying the
functions it needs. Either route creates a second source-delivery contract
outside the published package, and copying creates a second algorithm body.
The producer must own the source view and the evidence.

[FS.GG.Game#526](https://github.com/FS-GG/FS.GG.Game/issues/526) is the
receiving cross-repository request. The accepted producer proposal is
[`docs/reports/2026-07-28-fable-lockstep-compatibility-proposal.md`](https://github.com/FS-GG/FS.GG.Game/blob/main/docs/reports/2026-07-28-fable-lockstep-compatibility-proposal.md).
The request is scheduled on the Coordination board under contract identity
`fs-gg-game-fable-lockstep`.

## Decision

FS.GG.Game will establish Fable compatibility as a versioned, profiled facet
of the existing `FS.GG.Game.Core` NuGet package.

The first bounded spike uses the supported `Fable.Package.SDK` package-author
path to add a Fable source view to that package. The source view compiles the
same producer-owned implementation files as .NET. A producer-owned internal
packing project may link those canonical files if the package needs a smaller
compile graph; it may not contain copied algorithms or publish as a second
public package without a new decision.

Compatibility is declared at the smallest stable public surface with one
honest numeric contract:

- `LockstepExact` means the same canonical input bytes produce byte-identical
  canonical output bytes under the pinned .NET and Fable/Node profiles.
- `Portable` means the function is available on both targets but only a
  documented semantic or tolerance contract is promised.
- `DotNetOnly` means the supported Fable package path does not expose it.
- an unclassified function carries no cross-runtime promise.

Only `LockstepExact` surfaces may enter S.I.R.'s shared authoritative path, and
only behind S.I.R.'s own gameplay-semantic adapters. A grade is valid only
when producer-owned fixtures execute the same decoded inputs on .NET and
Fable/Node and compare canonical binary output, including boundary, ordering,
degenerate-input, and restore cases.

The bounded spike begins with `Cell`, one canonical edge relation, one integer
LOS operation, one bounded pathfinding operation, and a clean consumer that
restores only the packed artifact. This is a falsifiable package/compiler
spike, not certification of the full assembly.

### Version and rollout rule

The package version, compatibility-profile identity, fixture-schema version,
compiler/FSharp.Core/Node profile, and source commit are recorded together.
Changing the output of a `LockstepExact` function changes the compatibility
identity even when its .NET signature is unchanged.

Producer publication precedes registry activation and consumer adoption:

1. FS.GG.Game proves the package shape and cross-runtime fixtures.
2. FS.GG.Game publishes a versioned package carrying the profile.
3. `.github` registers the live profile/package identity.
4. S.I.R. pins and qualifies the published artifact.

The registry is deliberately **not** flipped by this decision-only ADR. No
Fable-bearing package or executable profile is live yet. Advertising one now
would violate publish-before-flip; the open `contract-change` request remains
the tracking item that must update the registry when the producer work is
resolved.

## Consequences

- Existing .NET consumers keep one package identity and are not asked to adopt
  a Fable-specific companion package.
- `api-surface/*.fsi` remains an inert documentation/scaffolding payload. The
  supported Fable source view has separate package metadata and validation.
- An assembly-level “Fable compatible” badge is insufficient. The profile and
  executable vectors are the authority for exact claims.
- Floating-point, wall-clock, sequential-RNG, and unclassified surfaces cannot
  enter S.I.R. authority merely because they compile.
- The package consumer test must execute outside the producer checkout and
  fail on project references, repository-relative includes, or undeclared
  feeds.
- Historical replay engines bind to a compatibility identity; a current
  bundle cannot silently reinterpret an old exact profile.
- FS.GG.Game owns package/source and fixture changes. S.I.R. owns its semantic
  adapters and downstream conformance scenarios.
- A failed bounded spike returns to this decision with a reduced reproduction.
  It does not authorize a consumer fork or silent companion package.

## Alternatives considered

- **A second public `FS.GG.Game.Core.Fable` package.** Rejected for the first
  spike because it creates two independently publishable identities for one
  source contract and adds version-coherence work. It remains a decision-level
  fallback only if the selected package shape fails with evidence.
- **A permanent project reference or linked files in S.I.R.** Rejected because
  the consumer would qualify local checkout state rather than the artifact it
  releases against.
- **A copied Fable subset.** Rejected because two algorithm bodies cannot prove
  one lockstep substrate.
- **Generated JavaScript as the library contract.** Rejected because it cannot
  support a consumer compiling and inspecting the shared F# source. Generated
  JavaScript remains a valid versioned deployment artifact.
- **Certify all of `FS.GG.Game.Core` at once.** Rejected because its public
  modules do not share one numeric/runtime contract; the broad label would be
  weaker and less truthful than function-level grades.
