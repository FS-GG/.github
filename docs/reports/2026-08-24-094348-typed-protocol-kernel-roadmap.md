---
title: "Roadmap: Agent-authored F# specification and protocol kernel"
category: Design
categoryindex: 4
index: 24
description: "A S.I.R.-first roadmap to a reusable F# specification AST and the coordination process/mutation extension."
---

# Roadmap: Agent-authored F# specification and protocol kernel

| Field | Value |
|---|---|
| Created | 2026-08-24T09:43:48+02:00 |
| Updated | 2026-08-24T10:19:38+02:00 — add Typed SDD lifecycle adoption and default preparation |
| Status | Planned; no implementation milestone is implied complete |
| Design authority | [Agent-authored F# specification kernel and canonical mutation algebra](../coordination/2026-08-24-typed-protocol-kernel-design.md) |
| Starting point | `main` at `0d56bb104da22478dfef72825f8cf19425635ed0` |
| Evidence window | 2026-08-21T07:26:29Z through 2026-08-24T07:26:29Z |

## Outcome

First prove the agent-authored specification EDSL/AST against S.I.R.'s live executable rules corpus. Then
extract only the reusable specification substrate into an FS.GG.SDD-owned, published contract. Finally,
move the FS-GG coordination protocol from strong but locally typed subsystems to process and protocol
extensions whose facts, commands, events, mutations, receipts, schemas, and projections have exactly one
authority. Preserve all current external contracts until each bounded surface has a proved replacement.

The consumer-facing process is **Typed SDD**, machine value `typed-sdd`. It will become an additive option
beside Standard SDD (`sdd`) and Freeform (`none`) before it is eligible to replace `sdd` as the workspace
default. The roadmap treats option introduction and default selection as separate contract changes.

The roadmap is intentionally incremental. Each milestone must land independently, keep `main` green,
and leave the old path removable rather than creating a permanent dual authority.

## Baseline and success measures

The starting 72-hour board window measured:

- 54 issues opened;
- 32 issues closed;
- net row growth of 22;
- 34 newly opened rows still open; and
- 156 repository commits.

Track these measures from milestone 0 onward:

| Measure | Baseline action | Target after final cutover |
|---|---|---|
| Independently authored representations per protocol fact | Census every registered surface | One authority; all remaining representations generated or external observations |
| Remote mutation entry points outside interpreters | Static/project-reference census | Zero |
| Protocol string comparisons outside codecs | Static census | Zero, excluding named expiring exemptions |
| Structural omissions discovered after independent review starts | Record per PR | Zero for modelled surfaces |
| Successor rows from one missing model concept | Churn reading every pass | Zero over a closed 30-day window |
| Partial-operation retries requiring human reconstruction | Receipt/recovery audit | Zero |
| Retained histories replayable by current engine | Replay corpus | 100% or an explicit versioned migration |
| Mutation controls caught | Named control inventory | 100% of required controls red |
| Agent-authoring iteration cost | P0 records questions, revisions, diagnostics, and elapsed work per S.I.R. session | No repeated question caused by missing model vocabulary; semantic diff reviewed each iteration |
| Shared-kernel consumer copies | Census local shared substrate after P2 | Zero after P3 and each later adoption |
| Provider/profile Typed SDD coverage | P4 derives the supported matrix from provider descriptors | Every supported row proves explicit `none`, `sdd`, and `typed-sdd` |
| Default-bearing surfaces | P4 derives the census while `sdd` remains default | One P5 flip changes the complete census; zero omitted or divergent defaults |
| Migration ambiguities | Record typed reason and source location per Standard SDD import | Zero silent guesses; every unresolved fact remains explicit |

Counts never substitute for the qualitative churn reading. A lower issue count achieved by suppressing
findings is failure.

## Delivery principles

1. **Strangle, do not rewrite.** Put a new typed seam beside one live path, compare, switch, then delete
   the old path before starting the next broad migration.
2. **One source at cutover.** Shadow comparison is temporary evidence, never a permanent second
   authority.
3. **Compatibility before elegance.** Existing CLI paths, JSON schemas, exit codes, issue markers, and
   receipts remain stable until a versioned migration is proved.
4. **Cheap closure first.** Model compilation and protocol-surface checks run before expensive CI and
   review.
5. **No automatic row explosion.** Milestones use existing class rows and bounded children. Findings
   become evidence on the relevant model surface unless they establish a new cause.
6. **Every milestone has deletion criteria.** Adding a model without retiring a shadow representation
   makes the root problem worse.

## Dependency map

```text
P0 S.I.R. baseline and pilot charter
 |
 v
P1 S.I.R. canonical authoring pilot
 |
 v
P2 extract shared specification kernel into FS.GG.SDD
 |
 v
P3 publish, register, and re-adopt from S.I.R.
 |
 +--> P4 all-provider Typed SDD opt-in
 |      |
 |      v
 |    P5 evidence-gated workspace default flip
 |
 +--> M0 coordination census and vocabulary
 |
 +--> M1 observation/evidence kernel
 |      |
 |      +--> M2 mutation algebra + dependency-field pilot
 |              |
 |              +--> M3 durable operation plans + intake pilot
 |
 +--> M4 process event model + delivery/review pilots
          |
          +--> M5 coordination compiler extensions
                    |
             +------+------+
             |             |
             v             v
        M6 schemas and   M7 protocol-surface gate
        fingerprints       |
             |             |
             +------+------+
                    v
          M8 model/replay/formal verification
                    |
                    v
          M9 retire shadows and measure
```

P0–P3 are ordered producer/consumer work: no cross-repository PR assumes unpublished contracts. P4 begins
only after the published boundary is proved by P3; P5 is independent of the coordination M-series and
cannot use coordination progress as a substitute for consumer readiness. M2 and M4 may proceed in parallel
only when their touch-sets and shared extension types are separated. M5 must
incorporate what both coordination pilots actually needed; it extends the already-proven specification
compiler and must not design a universal DSL from hypothetical requirements.

## P0 — S.I.R. baseline and pilot charter

### Deliverables

- Freeze representative S.I.R. corpus slices covering a fact, predicate, formula, transition, registered
  algorithm, supersession, evidence, generated documentation, and historical replay binding.
- Measure the current explicit-record and provisional-builder authoring cost, semantic diff quality,
  coherence runtime, and failure diagnostics.
- Define the smallest candidate shared concepts: stable node/specification IDs, vocabulary, references,
  supersession, provenance, evidence obligations, schema version, normalization, and extension contracts.
- Record S.I.R.'s gameplay types and interpreters as explicitly non-transferable domain ownership.

### Acceptance

- Every proposed shared concept has at least two concrete uses across S.I.R. rules, lifecycle
  specifications, or coordination; hypothetical abstractions are removed.
- The fixtures are content-addressed and reproduce under .NET and Fable where the current corpus promises
  parity.
- No authoritative S.I.R. behavior changes.

### Exit criterion

The pilot can distinguish reusable specification substrate from S.I.R.-owned rule semantics using checked
examples rather than naming intuition.

## P1 — Agent-authored S.I.R. specification pilot

### Deliverables

- Implement the candidate inspectable AST and try direct records, computation expressions, and a hybrid
  authoring surface against the frozen corpus.
- Extend `sir-author-rule` into the inspect → intent → one material question → typed proposal plus human
  projection → edit → validate → semantic diff → evidence/coherence → revise loop.
- Add canonical normalization, provenance/authoring receipt, stable fingerprint, derived Markdown/manifest
  views, and deliberate direct-edit/emergency-exemption controls.
- Keep registered algorithms explicit and opaque, with inputs, outputs, reads, writes, evidence, and
  implementation fingerprint visible to the AST.

### Acceptance

- The migrated rule slices execute, render, replay, and fingerprint identically except for accepted,
  versioned changes.
- Two syntactic authoring forms that mean the same thing normalize to byte-identical ASTs.
- A human can review the semantic diff without reading builder mechanics.
- The gate proves capability-mediated authoring without relying on commit identity and cannot block a new
  finding from being recorded.

### Exit criterion

At least three real iterative human/agent rule sessions complete without a second semantic authority or an
untyped escape hatch, and their friction report selects the authoring surface.

## P2 — Extract the specification kernel into FS.GG.SDD

### Deliverables

- Move the proven shared AST, compiler, normal form, versioned codecs, semantic-diff protocol, provenance,
  evidence contracts, and base authoring skill contract into FS.GG.SDD.
- Define typed extension registration without `obj`, reflection discovery, or a platform-wide closed union.
- Add a requirements extension covering current SDD scope, user value, requirements, acceptance,
  ambiguities/decisions, and evidence obligations.
- Provide a versioned Markdown migration adapter and generated human projection; do not silently reinterpret
  legacy prose.
- Register the package/contracts and compatibility policy under publish-before-flip sequencing.

### Acceptance

- Existing supported SDD artifacts either migrate losslessly or produce a stable, actionable ambiguity.
- Extension compiler, codec, semantic-diff, projection, and evidence-validator fixtures are public contract
  tests.
- The package is independently consumable without S.I.R. or coordination dependencies.

### Exit criterion

FS.GG.SDD publishes a stable preview containing only concepts proven by the pilot and its own requirements
extension.

## P3 — Re-adopt the published kernel in S.I.R.

### Deliverables

- Replace the pilot's local shared substrate with the published FS.GG.SDD-owned package while retaining the
  S.I.R.-owned rule extension.
- Re-run the frozen corpus, .NET/Fable parity, generated views, coherence, replay, and agent-authoring
  sessions through the package boundary.
- Delete the local shadow substrate and publish compatibility/consumer receipts.

### Acceptance

- S.I.R. has one rule authority and no vendored or locally forked copy of the shared kernel.
- The package boundary does not expose gameplay semantics or require S.I.R. at runtime.
- A negative source/semantic/package identity mismatch fails before rule execution or projection.

### Exit criterion

The producer/consumer cycle is proven end to end; other FS.GG repositories and coordination may adopt the
kernel without treating the S.I.R. pilot as a package source.

## P4 — Make Typed SDD an additive option for every consumer

### Deliverables

- Add `typed-sdd` to the lifecycle choice contract while retaining current machine values `sdd`, `none`,
  and the separately retiring `spec-kit`; keep the omitted-value default at `sdd`.
- Publish FS.GG.SDD support first, then update provider descriptors, template parameters, workspace wizard,
  scaffold provenance, registry projections, generated guidance, and consumer pins in dependency order.
- Route the existing SDD stage skills through a representation backend selected from provenance. Add
  Typed SDD authoring/inspection operations without copying the lifecycle stage instructions.
- Add Standard SDD → Typed SDD analysis/migration with explicit `Migrated | Ambiguous | Unsupported`
  outcomes, semantic diff, rollback boundary, and no writes before acceptance.
- Add refresh, upgrade, doctor, readiness, and ship checks for compiler/package identity, extension
  compatibility, canonical source, normalized AST, authoring receipt, and projection freshness.
- Exercise explicit `none`, `sdd`, and `typed-sdd` across every supported provider/profile, including clean
  creation, restore, agent authoring, build/test, lifecycle completion, refresh, and upgrade.

### Acceptance

- No supported provider rejects, drops, aliases, or silently downgrades `typed-sdd`.
- Omitted lifecycle selection still resolves to `sdd` on every default-bearing surface.
- A fresh consumer installs published artifacts only; no source checkout or S.I.R. dependency is required.
- Wrong lifecycle, missing compiler, stale projection, unsupported extension, direct edit, and agent-
  unavailable controls all produce distinct actionable failures.
- Standard SDD and Freeform behavior remain compatible, and `spec-kit` retirement is neither delayed nor
  widened.

### Exit criterion

Typed SDD is a fully supported opt-in lane for every workspace/product shape, with a published migration
path and derived compatibility receipt; it is not yet the default.

## P5 — Evidence-gated Typed SDD workspace default

### Deliverables

- Run representative non-S.I.R. opt-in work through complete Typed SDD lifecycles and publish the authoring
  friction, ambiguity, failure-recovery, and semantic-authority results.
- Freeze default-path fixtures for every provider/profile immediately before the flip.
- Write the separate cross-repo ADR that amends ADR-0056 and names the exact package, template, provider,
  registry, scaffolder, and wizard versions carrying the new default.
- In the ordered producer/consumer rollout, flip omitted lifecycle selection from `sdd` to `typed-sdd`
  everywhere; retain explicit `sdd` and `none` choices.
- Publish migration and operator guidance, then verify installed artifacts and fresh workspaces from both
  feeds rather than source-project references.
- Observe 7-, 14-, and 30-day default cohorts and retain a versioned rollback plan that restores selection
  semantics without rewriting canonical specifications.

### Acceptance

- All nine default-readiness conditions in the design hold at the exact release identities being flipped.
- Raw template, every provider, scaffolder, wizard, provenance, registry, docs, and tests agree that omitted
  lifecycle means `typed-sdd`.
- Explicit `sdd` remains Standard SDD and explicit `none` remains Freeform; neither is an alias or fallback.
- A wrong-default mutation makes every default-bearing contract test red.
- No default-created workspace is silently lifecycle-less or unable to author its first specification.
- The post-flip cohorts show no second authority, silent migration, or recurring missing-vocabulary chain.

### Exit criterion

Typed SDD is the coherent workspace default across all consumer entry points. Standard SDD and Freeform
remain explicit supported choices, and any later retirement decision is outside this roadmap.

## M0 — Ratify vocabulary and produce the protocol census

### Deliverables

- Add the canonical terms from the design to a small kernel namespace without behavior changes.
- Produce a machine-readable census of:
  - external authorities and reads;
  - decoders/codecs;
  - process decisions;
  - remote writes;
  - durable ledgers and receipts;
  - projections and generated documents; and
  - raw protocol string comparisons.
- Map every current open protocol issue to an existing model surface, a new proposed surface, or a
  non-protocol defect.
- Record the baseline measures above with reproducible commands.

### Acceptance

- The census is derived from source/project structure where possible and labels unavoidable manual
  entries explicitly.
- Zero subjects is a refusal, not a clean result.
- Every census row names authority, subject shape, freshness/revision source, and current owner module.
- No runtime behavior or wire output changes.

### Exit criterion

The team can answer “where is this fact decided and where can it be mutated?” for every live protocol
surface using one command.

## M1 — Observation and evidence kernel

### Deliverables

- Implement `AuthorityId`, `SubjectId`, `Revision`, `Evidence`, and `Observation<'a>` in Core.
- Add constructors that prevent `ConfirmedAbsent` without complete-read evidence.
- Define strict adapter contracts for REST, GraphQL, Actions, git, filesystem, and feed observations.
- Migrate two representative reads:
  - a paginated GitHub board/issue fact; and
  - an exact-head Actions/check-run fact.
- Emit authority, subject, revision, and evidence identity in structured diagnostic output.

### Acceptance

- Rate limit, truncated pagination, malformed response, permissions failure, and legitimate absence are
  five distinguishable outcomes.
- Existing JSON/plain/rich results remain compatible or change behind an explicit schema version.
- Mutation controls coercing each failure to absence are caught.
- Adapter tests preserve raw evidence by bytes or digest.

### Exit criterion

No migrated decision accepts a naked source value or performs its own external read.

## M2 — Mutation algebra and `Blocked by` pilot

### Deliverables

- Implement the closed mutation DU, typed outcomes, interpreter capability, and receipt envelope.
- Extend board field metadata to distinguish scalar and set-valued semantics.
- Migrate `.github#2907`:
  - atomic revision-bound `AddMember`/`RemoveMember` for `Blocked by`;
  - explicit replace retained only under a separately named administrative command;
  - body-only inert dependency declarations reported by lint.
- Make every existing board write compile through the algebra, initially via compatibility adapters.

### Acceptance

- Concurrent adds preserve both edges or one fails stale; neither silently overwrites.
- Removing one member preserves every other member.
- Duplicate add/remove is idempotent and reports `AlreadyApplied` distinctly.
- `SetScalar` cannot be constructed for a registered set field.
- Existing callers see compatible outputs until migrated.

### Exit criterion

There is one implementation of board-field mutation semantics and no generic path can masquerade as
set membership intent.

## M3 — Durable operation plans and intake pilot

### Deliverables

- Implement `OperationId`, `MutationPlan`, step dependencies, step receipts, and resumption.
- Define `Applied`, `AlreadyApplied`, `RefusedBeforeWrite`, `Stale`, and `Indeterminate` outcomes.
- Migrate intake creation from `.github#2835` to a resumable plan:
  issue creation → labels → board placement → field projection → completion receipt.
- Add failure injection before and after every remote call.
- Define roll-forward versus compensation rules for irreversible GitHub effects.

### Acceptance

- Killing the process at every injected boundary and retrying reaches one identical final state.
- No retry creates a second issue or appends a duplicate receipt.
- An indeterminate create is re-observed before any repeat write.
- A rejected field value leaves a durable partial-state receipt and a correct next action.
- Plans and receipts carry model/schema version and protocol fingerprint.

### Exit criterion

No human must inspect remote state to reconstruct where a migrated operation stopped.

## M4 — Process events and lifecycle pilots

### Deliverables

- Define state/intent/event/decision/evolve modules for Delivery and Review.
- Make `Merged`, `AwaitingPostMergeVerification`, `Verified`, and `Completed` distinct delivery states.
- Migrate `.github#2905` and the complete transition decisions identified by the 2026-08-22 design.
- Represent review generation, wait, repair, succession, and acceptance as events rather than
  independently authored tokens.
- Compile authorized events into M2/M3 mutation plans.

### Acceptance

- No event sequence reaches `Completed` without an exact-merge protected/default-branch receipt.
- Red, unreadable, or absent post-merge verification stays visible and recoverable.
- Review head movement and claim reacquisition have one total transition decision consumed unchanged by
  projection and writer.
- Retained histories from the named defect corpus replay to their expected state.
- A writer cannot add local preconditions around a shared decision without a gate failure.

### Exit criterion

Delivery and Review have one executable process authority each; issue state and Projects status are
projections only.

## M5 — Coordination extensions for the specification compiler

### Deliverables

- Reuse the P1-selected authoring conventions and deviate only where the M2/M4 evidence requires it.
- Implement process and protocol extensions plus compiler validation for:
  - stable/duplicate IDs;
  - state/event reachability;
  - authority and codec ownership;
  - mutation interpreter coverage;
  - projection sources and cycles;
  - schema versions; and
  - model-test dimensions.
- Compose process models without introducing a single monolithic process union.

### Acceptance

- Equivalent builder and direct AST construction produce byte-identical normalized specification models.
- Model fingerprint is independent of declaration order but changes on semantic structure.
- The compiler rejects named negative fixtures for every validation class.
- No arbitrary closure is treated as inspectable transition structure.
- Existing `Protocol.fs` documentation data can be derived from or associated with model IDs.

### Exit criterion

One compiled specification model can enumerate all migrated authorities, processes, mutations,
projections, and schemas without scanning prose, while S.I.R. and requirements extensions remain absent
from the coordination dependency closure.

## M6 — Versioned schemas, envelopes, and protocol fingerprint

### Deliverables

- Implement versioned event/receipt envelopes with source, id, subject, schema, correlation,
  causation, revision, and model version.
- Generate JSON Schema 2020-12 for losslessly representable contracts.
- Add explicit schema fragments and codecs where F# unions require custom encoding.
- Report package version, model version, and protocol fingerprint from the engine and wrapper.
- Stamp generated docs/skills and structured outputs with model identity where compatible.
- Provide upcasters for every retained old event/receipt version.

### Acceptance

- Old retained documents decode or fail with an explicit unsupported-version result.
- Unknown fields follow the declared compatibility policy; they never disappear accidentally.
- Encode/decode and old→new→encode fixtures are deterministic.
- A conflicting wrapper manifest/runtime artifact is named before any protocol decision.
- XML/SCXML, if prototyped, is generated only and absent from production reads.

### Exit criterion

Every durable protocol document says which semantics interpret it, and runtime output proves which
model produced the answer.

## M7 — Protocol-surface gate and architectural enforcement

### Deliverables

- Add the early `protocol-surface` required context.
- Enforce project-reference boundaries around Core, adapters, model, and interpreters.
- Add AST/source checks for direct mutations, raw registered-state comparisons, duplicate codecs,
  unrevisioned writes, unowned projections, and unclassified wire changes.
- Add `MODELLED | EXEMPT | UNMODELLED` reporting.
- Define an exemption record with reason, issue, owner surface, and enforced expiry.
- Generate the census from the normalized specification model's protocol extension and interpreter
  registration.

### Acceptance

- One deliberate violation of every rule makes a named control red.
- An empty source census returns no-verdict/refusal.
- The gate completes before expensive suites under the repository's bounded target.
- A finding packet can still be recorded when no model case exists.
- An expired exemption fails without relying on a human reminder.
- Existing independent property gates remain and prove their own non-vacuity.

### Exit criterion

A new protocol behavior cannot merge merely because its raw implementation compiles.

## M8 — Model-based, replay, and bounded formal verification

### Deliverables

- Add FsCheck state-machine suites comparing the pure model with fixture-backed interpreters.
- Turn every shrunk sequence failure into a deterministic corpus fixture.
- Replay retained production-safe histories on every model/codec change.
- Add mutation controls for guards, revisions, subjects, authorities, idempotency keys, event ordering,
  and projection ownership.
- Model at least these bounded protocols in TLA+ or an equivalently checkable state specification:
  - comment-order claim CAS and lease/reacquisition;
  - set-valued concurrent add/remove; and
  - mutation-plan retry around indeterminate effects.

### Acceptance

- Model and interpreter agree on state, events, calls, receipts, and refusals for generated sequences.
- Historical replay is deterministic under each supported model version.
- The formal checks find injected lost-update, double-apply, stale-authorize, and deadlock controls.
- Formal artifacts are derived from or explicitly mapped to model IDs and cannot execute production
  writes.

### Exit criterion

The highest-risk concurrency and recovery invariants have both executable tests and state-space
evidence.

## M9 — Retire shadows, complete migration, and measure

### Deliverables

- Migrate remaining claim, dependency, intake, review, delivery, finding, and release surfaces.
- Delete compatibility parsers, generic mutation routes, duplicate predicates, and hand-authored
  projections immediately after each cutover.
- Remove every temporary exemption or convert it into a separately accepted permanent external boundary.
- Publish the final protocol-surface census and deletion ledger.
- Run closed-window churn readings at 7, 14, and 30 days after the final cutover.

### Acceptance

- All architecture acceptance conditions in the design document hold.
- No remote mutation bypass exists.
- No registered fact has more than one semantic codec or decision authority.
- Every remaining representation is labelled as authority, observation, or projection.
- The 30-day reading finds no successor chain caused by hand-authored second representations.
- Any remaining churn is named by a different measured mechanism rather than absorbed into this claim.

### Exit criterion

The protocol kernel is ordinary infrastructure: new concepts extend one model and the old drift
surfaces no longer exist.

## Existing issue alignment

This table is a routing aid, not a replacement for live board state.

| Current row/class | Roadmap home |
|---|---|
| `.github#2903` second representations | Class anchor across M0–M9 |
| `.github#2905` merged versus verified | M4 |
| `.github#2906` red verdict provenance | M1 and M4 |
| `.github#2907` dependency set mutations | M2 |
| `.github#2908` engine/manifest identity | M1 and M6 |
| `.github#2841` touch-set representations | M0, M5, M7 |
| `.github#2842` evidence-result ambiguity | M1 and M6 |
| `.github#2848` status vocabulary | M5 and M6 |
| `.github#2850` duplicate actor ownership | M0 and M5 |
| `.github#2852` runtime skill identity | M1 and M6 |
| `.github#2862` false chain-validation name | M4 and M6 |
| `.github#2835` non-atomic intake | M3 |
| `.github#2846`, `#2853`, `#2867` ledger/retry chains | M3 and M4 |
| `.github#2893`, `#2896` merge-election/recovery order | M3, M4, M8 |

Before implementation, re-read each row. A mapping here does not freeze its title, state, scope, or
acceptance criteria.

## Cutover protocol for each migrated surface

1. Freeze representative input/output fixtures from the old authority.
2. Introduce the typed model and interpreter behind a compatibility adapter.
3. Run old and new decisions against the defect corpus and fixture traffic.
4. Classify every divergence as old defect, new defect, or intentional versioned change.
5. Add a mutation control proving the new invariant can fail.
6. Switch the one production caller set to the new authority.
7. Delete the old predicate/parser/writer in the same PR or a pre-declared immediately following PR
   that blocks further surface changes.
8. Regenerate projections and record the model fingerprint.
9. Add the surface to replay and model-based sequence tests.

There is no indefinite shadow mode. A shadow that remains callable is another authority.

## Stop conditions

Pause the roadmap and return to design if any milestone demonstrates that:

- the algebra needs a generic untyped mutation escape hatch;
- model compilation depends on executing arbitrary external IO;
- the AST cannot represent a migrated pilot without embedding opaque workflow closures;
- retained histories cannot be versioned without rewriting immutable evidence;
- early enforcement blocks finding intake or honest `Unreadable`/`Unknown` outcomes;
- generated projections would require removing an independent property gate; or
- the kernel must become a network service to satisfy correctness.

These are evidence that the proposed boundary is wrong, not implementation inconvenience to hide.

## Completion report

After M9, write a timestamped report containing:

- the final derived protocol census;
- every retired representation and mutation bypass;
- schema/model versions and supported migration window;
- model/replay/formal verification results;
- the 7-, 14-, and 30-day churn readings;
- counter-evidence and remaining failure classes; and
- a decision on whether SCXML export or a richer visual process projection is now worth adding.

P5 additionally produces a Typed SDD default-transition report containing the derived provider/profile and
default-bearing-surface censuses, exact release identities, migration results, opt-in soak evidence, wrong-
default controls, rollback boundary, and 7-, 14-, and 30-day default-cohort readings.
