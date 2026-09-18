# ADR-0085: FsQuint owns reusable Quint–F# correspondence

- **Status:** Accepted
- **Date:** 2026-09-18
- **Affects:** FsQuint, FS.GG.SDD, FS.GG.Coordination, .github

## Decision

[FsQuint](https://github.com/FS-GG/FsQuint) owns generic ITF decoding, canonical
identities, validation, comparison and replay. Its optional Tooling package owns
bounded, explicitly pinned Quint process invocation. Both packages form one coherent
release set, published to GitHub Packages and nuget.org. Ordinary F# consumers need
only public NuGet, .NET 10 and their own model/driver; organization fabrics are optional.

SDD preserves its existing public CLR types and delegates through data mappings.
It retains compiler, profile and lifecycle policies. Coordination removes linked
replay source and consumes the public package, retaining Choreo provenance guards,
projection, actor/PostgreSQL drivers, formal budgets and raw trace fixtures. Generic
fixes land upstream and propagate through pinned, tested dependency PRs. No consumer
maintains a second generic implementation or takes a cross-repository project reference.

Package pins and Quint tool pins are independent. A package update does not authorize
new projection semantics, fixture refresh, evidence reuse, or installed activation.
Coordination binds package pins, locks and correspondence sources into reuse decisions.
Incompatible candidates fail qualification; rollback restores an immutable package pin
and qualifies its matching evidence. Published archives and tags are never replaced.

## Rollout and compatibility

The canonical [FSQUINT-01 roadmap](https://github.com/FS-GG/FsQuint/blob/main/docs/roadmaps/fsquint.md)
records actual releases and acceptance. Public previews precede consumer migrations;
a substantive Unicode fingerprint fix then exercises both consumer updates before
stable publication. Valid schema-v1 canonical identities and SDD binary consumers
must remain compatible. Retiring the facade requires a separate SDD major API decision.

FsQuint maintainers own generic API/schema compatibility, regression fixtures and
release integrity. Consumers own their models, projections and adoption. FsQuint is
explicitly declared in `outside-fabric`: package ownership does not require board
scope, coordination-kit or build-config participation. Registry closure accepts a
contract owner only if rostered or explicitly exempt with a reason. Org closure still
rejects missing repositories, stale exemptions and contradictory declarations. This
changes the former rule that every dependency-graph participant must join the roster;
existing receiver and board policies remain unchanged. Existing organization issue-intake policy
still applies. The support policy promises no SLA or automatic older-line backports.

## Consequences

One implementation and semantic corpus receive generic fixes. Consumers still spend
qualification time on upgrades; source reuse does not prove domain correspondence.
The bounded queue example establishes independence from FS-GG. Production actor and
PostgreSQL replay establish the actual Coordination use case. Neither expands the
claim to arbitrary F# verification or changes Unified V2 operational gates.
