# ADR-0076: Agent-authored F# specification kernel, piloted in S.I.R.

- **Status:** Accepted
- **Date:** 2026-08-24
- **Decision owners:** FS-GG/.github, FS.GG.SDD, and S.I.R. maintainers
- **Affects:** FS-GG/.github, FS-GG/FS.GG.SDD, future FS-GG consumers, and the EHotwagner/S.I.R. pilot
- **Related design:** [Agent-authored F# specification kernel and canonical mutation algebra](../coordination/2026-08-24-typed-protocol-kernel-design.md)

## Context

S.I.R. already represents executable gameplay rules through a layered F# EDSL/AST and exposes agent-facing
authoring and coherence skills. FS.GG.SDD owns lifecycle specifications but currently seeds and parses
Markdown sections into typed facts. The coordination engine separately needs inspectable process, evidence,
and mutation models. All three efforts need stable identity, vocabulary, references, evidence obligations,
provenance, canonical normalization, derived views, compatibility, and an iterative human/agent authoring
loop.

Duplicating those mechanisms would create three specification frameworks. Combining every domain case into
one universal grammar would instead couple lifecycle, gameplay, and coordination semantics and make the
shared layer impossible to evolve safely.

## Decision

FS-GG will establish one small, extensible F# specification kernel. Canonical specification sources are
created and modified by agents through repository-owned skills during iterative sessions with humans. The
EDSL produces inspectable data; a canonical compiler validates and normalizes the AST, renders semantic
diffs, records provenance, and derives human-readable and machine-readable projections. Arbitrary closures
are not inspectable semantics. Ordinary F# algorithms are allowed only as explicitly registered opaque
nodes with declared contracts, evidence, and implementation identity.

S.I.R. is the first pilot because its accepted executable-rules corpus already exercises formulas,
transitions, algorithms, canonical encodings, replay, projections, and agent workflows. S.I.R. continues to
own gameplay types, interpreters, and policy. The pilot extracts only demonstrated generic concepts.

After the pilot, FS.GG.SDD owns and publishes the reusable specification contracts, compiler, normalized
representation, authoring protocol, and base skills. Consumer repositories own typed extension packages and
domain-specific skills. Producer publication and compatibility registration precede consumer adoption.
The `.github` coordination engine adopts the kernel through process and protocol extensions, including the
canonical observation and mutation algebras described by the related design.

The shared kernel is not one closed union of all platform semantics. Extensions compose through stable IDs,
versioned typed payload contracts, codecs, compiler rules, semantic-diff renderers, and evidence validators.
Markdown, JSON, schemas, diagrams, and skill guidance are projections or external evidence, never parallel
semantic authorities. XML may be generated for interchange but is not a canonical input.

“Agents only” is enforced by the sanctioned authoring/compiler capability and its receipts, normalization,
and freshness checks—not by commit author identity. Humans retain readable projections, semantic diffs, and
the final decision in each conversational iteration.

## Consequences

S.I.R. can improve its current provisional builder and authoring loop without waiting for a platform-wide
framework. Extraction follows measured use, so FS.GG.SDD does not design a hypothetical universal DSL.
Other FS-GG repositories gain one specification identity/evolution contract while keeping their domain
semantics local.

The platform must maintain extension compatibility, normalized codecs, migrations, provenance, and
projection freshness. Agent-only canonical authoring also requires an emergency path with an expiring
exemption; it may never prevent a new finding from being recorded. Until the S.I.R. pilot and extraction
milestones land, existing SDD Markdown and coordination contracts remain authoritative.

## Alternatives considered

1. **Keep S.I.R., SDD, and coordination EDSLs independent.** Rejected because identity, normalization,
   evidence, projections, evolution, and authoring workflow would drift independently.
2. **Move the whole S.I.R. rules corpus into FS.GG.SDD.** Rejected because gameplay vocabulary and
   execution belong to S.I.R.; only proven generic substrate is shared.
3. **Create one universal closed specification grammar.** Rejected because it couples unrelated domains
   and forces opaque escape hatches as soon as one consumer evolves.
4. **Keep Markdown canonical and parse it into typed facts.** Retained only as a migration bridge; it leaves
   semantic structure dependent on prose grammar and permits hand-authored shadow representations.
5. **Use XML as the common source format.** Rejected because serialization does not supply domain typing,
   authority, freshness, mutation legality, or F# execution semantics.
