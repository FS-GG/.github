# ADR-0076: Agent-authored F# specification kernel, piloted in S.I.R.

- **Status:** Superseded for future canonical authoring by [ADR-0077](0077-quint-first-typed-specification-authority.md); retained as the Typed SDD P0–P4 decision and delivery record
- **Date:** 2026-08-24
- **Decision owners:** FS-GG/.github, FS.GG.SDD, and S.I.R. maintainers
- **Affects:** FS-GG/.github, FS-GG/FS.GG.SDD, future FS-GG consumers, and the EHotwagner/S.I.R. pilot
- **Related design:** [Agent-authored F# specification kernel and canonical mutation algebra](../coordination/2026-08-24-typed-protocol-kernel-design.md)
- **Amended:** 2026-08-24 — name the consumer lifecycle `typed-sdd` and establish its staged default direction

> **Successor (2026-08-25):** [ADR-0077](0077-quint-first-typed-specification-authority.md) selects
> canonical Quint source plus a small generated FS-GG compiled contract for future Typed SDD work. This
> does not reinterpret the F# backend, packages, migrations, or P0–P4 evidence delivered under this ADR.
> They remain current production behavior until the successor is implemented, published, and adopted.

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

## Amendment (2026-08-24): Typed SDD lifecycle and future default

The consumer process is named **Typed SDD** and its stable machine identifier is `typed-sdd`. It is the third
durable lifecycle posture beside Standard SDD (`sdd`) and Freeform (`none`); during transition, the retiring
`spec-kit` lane remains a fourth legacy value. `freeform` is not introduced as a competing wire token.

Typed SDD uses the existing SDD lifecycle stages and evidence semantics with a different canonical
representation and authoring backend. Every supported workspace provider and product profile will gain an
explicit `typed-sdd` option after the S.I.R. pilot, kernel publication, and re-adoption prove the contract.
The option must propagate through provider descriptors, scaffolding, provenance, skills, readiness,
refresh/upgrade, and compatibility receipts without coercion or fallback.

Typed SDD is the intended future workspace default, but this amendment does **not** flip the current
default. ADR-0056 continues to make `sdd` authoritative for omitted selection. Typed SDD first ships as an
additive opt-in and completes migration, all-provider/profile, installed-artifact, failure-mode, and
non-S.I.R. soak evidence. A later cross-repo ADR must amend ADR-0056 and move every default-bearing surface
coherently. Standard SDD and Freeform remain explicit supported choices after that flip.

## Consequences

S.I.R. can improve its current provisional builder and authoring loop without waiting for a platform-wide
framework. Extraction follows measured use, so FS.GG.SDD does not design a hypothetical universal DSL.
Other FS-GG repositories gain one specification identity/evolution contract while keeping their domain
semantics local.

The platform must maintain extension compatibility, normalized codecs, migrations, provenance, and
projection freshness. Agent-only canonical authoring also requires an emergency path with an expiring
exemption; it may never prevent a new finding from being recorded. Until the S.I.R. pilot and extraction
milestones land, existing SDD Markdown and coordination contracts remain authoritative.

Naming the lifecycle creates a stable contract value before provider work begins and prevents each
consumer from inventing `ast`, `edsl`, `agent-sdd`, or `executable-sdd` aliases. Delaying the default prevents
an aspirational design from producing lifecycle-less or unauthorable workspaces. The cost is a deliberate
opt-in period and a second, evidence-bearing decision before the default can move.

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
