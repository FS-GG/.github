---
title: "Design: Reducing coordination change amplification"
category: Design
categoryindex: 4
index: 22
description: "A staged design for making command, review, delivery, CI, self-hosting, and release changes fail closed at one typed authority boundary."
---

# Design: Reducing coordination change amplification

| Field | Value |
|---|---|
| Status | Proposed |
| Authored | 2026-08-22 |
| Snapshot time | 2026-08-22T12:56:41Z |
| Scope | `FS-GG/.github` coordination engine, its tests, and its hosted lifecycle |
| Primary incident | [`.github#2753`](https://github.com/FS-GG/.github/issues/2753) |

## Summary

Small coordination-engine changes are expensive when one semantic fact is copied across command
registration, parser metadata, tests, review projection, mutation authorization, GitHub project
automation, and release recovery. The copies normally agree. When one is missed, the defect appears
late, often after a review record or release asset has become immutable.

This design reduces that amplification by making each lifecycle question have one typed authority and
many derived projections. It does **not** remove independent tests. It changes those tests from copied
inventories into generated conformance checks against observable behavior.

The seven decisions are:

1. introduce one command catalogue for command metadata and handler ownership;
2. require one complete typed predicate for each review or delivery transition;
3. make `Done` a receipt-gated transition rather than a projection of issue closure;
4. formalize candidate-engine use with a self-host bootstrap receipt;
5. run a bounded change-completeness gate before expensive CI and independent review;
6. model-test lifecycle state machines and require consumer parity; and
7. keep release recovery state and receiver receipts usable after release immutability.

The work should be delivered in small compatibility-preserving slices. No big-bang engine rewrite is
required.

## Context

### The measured failure chain

The difficulty of [`.github#2753`](https://github.com/FS-GG/.github/issues/2753) was not primarily the
size of its production change. The change added a verified comment-mutation command, including an
ownership read before `PATCH`. The costly part was the number and ordering of surrounding contracts:

- the BoardOps `Implementations` record, command list, and command-to-handler list each needed a new
  entry;
- the kernel command-surface inventory and bare-render pin each needed a new entry;
- independent review found those omissions on successive heads, consuming bounded review rounds;
- the SDD generator and the repository provenance fixer had a deliberate two-step relationship that
  initially looked contradictory;
- an exhausted review chain required a closed predecessor PR, fresh claim turnover, host grant,
  structured escalation, and a separate repair PR;
- publication succeeded, but receiver delivery initially failed because recovery attempted to mutate an
  immutable release asset; and
- issue closure and Projects automation could project `Done` before the typed delivery obligation was
  satisfied.

The individual safeguards were reasonable. Their **independent authored representations** and their
late ordering made the change hard.

### Related evidence

The same class is visible in other work:

| Item | Observed seam | Design lesson |
|---|---|---|
| [`.github#2773`](https://github.com/FS-GG/.github/issues/2773) | `delivery` and `verify-paths` computed path admission separately | One classifier must feed every projection. The landed `Delivery.classifyPaths`/`pathsVerified` shape is the reference pattern. |
| [`.github#2819`](https://github.com/FS-GG/.github/issues/2819) | review projection and escalation writer admitted different terminal histories | A predicate is not shared if callers still add different preconditions around it. |
| [`.github#2820`](https://github.com/FS-GG/.github/issues/2820) | recovery mutated a journal after promotion made the release immutable | Recovery must read and verify immutable evidence, then resume downstream work without rewriting it. |
| [`.github#2723`](https://github.com/FS-GG/.github/issues/2723) | a merged change retains a post-merge arming obligation | Merge, delivery, and completion are separate durable transitions. |
| [`.github#643`](https://github.com/FS-GG/.github/issues/643) | a narrative closing phrase closed and stamped an unfinished issue | GitHub issue state and Projects status are projections, not completion authority. |

### Existing good foundations

This design builds on mechanisms already present:

- `Delivery.classifyPaths` returns an exhaustive `PathAdmission` union, and both delivery and
  `verify-paths` consume `Delivery.pathsVerified`.
- `Delivery.inspect` is a pure reducer over a complete `Snapshot` and returns one action with freshness
  and idempotency keys.
- structured route and review decisions are append-only and digest-linked, as documented in
  [Structured route and review decisions](structured-decisions.md).
- the [coherent-set release saga](release-saga.md) binds package identity to exact source and artifact
  digests.
- immutable release recovery now reads the existing package journal, proves its identity and artifacts,
  and performs no asset write.

The target architecture extends these patterns instead of introducing another control plane.

## Risk model

Change amplification is the product of four factors:

| Factor | Meaning | Typical symptom |
|---|---|---|
| Authored copies | Independent places where the same fact must be repeated | A new command compiles in one layer and is absent in another. |
| Temporal boundaries | Facts become immutable or stale between stages | A review passes, a required check later reds, and the ledger cannot be rewritten. |
| Authority splits | Read and write paths answer the same question differently | Inspection says proceed while the writer refuses, or conversely. |
| Late discovery | Cheap completeness errors run after costly tests or review | A missing inventory row consumes a CI cycle and an independent-review round. |

A mitigation is successful only if it reduces one of these factors without weakening fail-closed
behavior. Deleting a check is not a mitigation. Deriving that check from one authority is.

## Design principles

1. **One question, one typed authority.** A projection may render or transport a decision. It must not
   reconstruct the decision with its own match tree.
2. **Unknown is not absent.** Unread or incomplete authority remains an explicit refusal state.
3. **Independent tests observe behavior.** They do not become a second writable catalogue.
4. **Durable transitions precede projections.** Board fields, issue state, comments, and dashboards are
   updated only after the typed transition is authorized.
5. **Immutable evidence is read, never repaired in place.** A correction appends a successor receipt or
   opens a bounded repair phase.
6. **Cheap completeness precedes expensive confidence.** Compilation and structural closure run before
   mutation sweeps and before a critic is dispatched.
7. **Self-hosting is an explicit trust transition.** A candidate engine may prove a change to the engine,
   but only through a receipt that binds its bytes and evidence.

## Decision 1: one command catalogue

### Problem

Command facts are currently split across the `Command` union, `renderSupport`, parser arms, BoardOps
handler ownership, BoardOps implementation fields, the command-surface table, and the bare-render table.
Reflection catches some omissions, but it catches them after an author has already had to discover which
tables exist.

### Decision

Add a kernel-owned catalogue containing the metadata that is truly declarative:

```fsharp
type HandlerOwner =
    | KernelProgram
    | BoardOps

type MutationKind =
    | ReadOnly
    | WritesRemoteState

type CommandDescriptor =
    { Command: Command
      Verb: string
      Render: RenderSupport
      Mutation: MutationKind
      HandlerOwner: HandlerOwner
      Documented: bool }

val commandCatalogue: CommandDescriptor list
```

The catalogue is the authored source for:

- command-contract rows;
- render-flag availability and bare render defaults;
- write-ness classification;
- documentation coverage;
- expected handler ownership; and
- generated test cases.

BoardOps should expose a single list of `(Command * Handler)` bindings. Validation compares its keys with
the catalogue rows owned by `BoardOps`. The `Implementations` record and the separate BoardOps `commands`
list then become unnecessary.

Parser arms remain explicit where commands have different arguments. A generated conformance test parses
every catalogue verb and proves that it yields the descriptor's command and default render mode. That is
an independent observation, not a copied inventory.

Reflection over `Command` remains as a guard proving that every nullary union case has exactly one
descriptor. A duplicate descriptor, unowned command, undocumented exception, or missing handler fails in
the bounded completeness gate.

### Why not generate the `Command` union or parser

The commands have heterogeneous arguments and validation rules. Generating the whole parser would hide
useful domain code behind a new generator and make diagnostics harder. The catalogue centralizes stable
metadata while leaving behavioral parsing explicit and testable.

## Decision 2: complete shared lifecycle predicates

### Problem

Moving a partial condition into Core does not create one authority if a projection and a writer still add
different surrounding checks. The relevant unit is the **complete transition question**, including ledger
prefix, current head, wait receipt, check settlement, claim generation, and repair bounds.

### Decision

Represent the full input and output of every disputed transition:

```fsharp
type OrdinaryExhaustionFacts =
    { HeadSha: string
      Initial: ReviewDecision
      Confirmations: ReviewDecision list
      CompletedWait: ReviewWaitReceipt
      RequiredChecks: RequiredCheckVerdict
      OriginalClaimGeneration: string
      CurrentClaimGeneration: string }

type OrdinaryExhaustionDecision =
    | NotExhausted of reason: string
    | AwaitChecks
    | HostAcceptanceEligible
    | EnterRepairPhase of terminalDigest: string

val decideOrdinaryExhaustion:
    OrdinaryExhaustionFacts -> OrdinaryExhaustionDecision
```

Both the read-side projection and the live escalation writer must consume this decision. Neither may
re-test ledger shape, check color, head equality, or claim turnover. The writer may add only mutation
mechanics: freshness re-read, idempotency, and append execution.

Apply the same rule to delivery path admission, host acceptance, merge authorization, completion, and
release recovery. A source gate should reject consumer-local matches over the underlying fact union when
a shared decision type exists.

## Decision 3: receipt-gated `Done`

### Problem

`Delivery.inspect` already distinguishes `MergedAwaitingObligations` from `Done`, but GitHub issue closure
and Projects automation can independently project completion. A merge-closing keyword can therefore make
an item look done before receiver, publication, arming, or cleanup receipts exist.

### Decision

Define one completion receipt:

```fsharp
type DeliveryCompletionReceipt =
    { Item: string
      PullRequest: int
      MergeSha: string
      MergeReachable: bool
      ObligationReceipts: VerifiedObligationReceipt list
      PendingBoardWrites: int
      CompletedAt: DateTimeOffset
      Digest: string }
```

`Complete` is authorized only when:

1. the merged head is reachable from the default branch;
2. every exact-head obligation declaration has one verified receipt;
3. no undeclared obligation or contradictory receipt is present;
4. pending board writes are zero; and
5. the transition still matches its freshness and action keys.

The completion writer appends the receipt first, then projects issue closure, `Status=Done`, claim release,
and cleanup. A partial projection is reconciled from the receipt. Without a valid receipt, an auto-closed
issue is restored to open and the board remains `In review` or `Blocked` according to the reducer.

Projects close-event automation must stop being completion authority. It may request reconciliation, but
it may not write `Done` directly.

## Decision 4: typed self-host bootstrap

### Problem

An engine change can make the shared engine unable to inspect or authorize the PR that fixes it. Running
the candidate binary is sometimes necessary, but an ad hoc candidate run weakens the trust boundary and
is difficult to audit later.

### Decision

Add a `SelfHostBootstrapReceipt` that binds:

- base and candidate head SHAs;
- candidate binary SHA-256 and reported version;
- the shared engine's exact refusal and its classified bootstrap reason;
- build, unit, focused production-route, provenance, and inversion evidence;
- the candidate engine's decision and action keys; and
- accountable host acceptance.

Only a small enumerated set of bootstrap reasons is allowed, such as a new schema case or a relocated
decision boundary. Business-rule disagreement is not a bootstrap reason.

The candidate may inspect and produce a proposed transition. A host-owned bootstrap command verifies the
receipt with the shared engine's stable verifier before performing a write. After merge, CI rebuilds the
shared engine and replays the same snapshot; disagreement blocks completion and release.

## Decision 5: a bounded change-completeness gate

### Problem

Inventory, body, path, and provenance failures are cheap but have repeatedly appeared after long CI runs
or after a critic was dispatched.

### Decision

Add a required `change-completeness` context, targeted to finish within five minutes for ordinary changes.
It runs before independent-review dispatch and is a prerequisite of expensive mutation jobs.

The gate includes:

1. restore and compile of affected engine projects;
2. command-catalogue closure and handler ownership;
3. parser, render, write-ness, contract, and help conformance generated from the catalogue;
4. delivery/verify-paths and review projection/writer parity fixtures;
5. declared-path verification;
6. PR closing-keyword and commit-message checks;
7. SDD ship-verdict provenance normalization and validation; and
8. focused production-route tests selected from the changed decision boundary.

Long mutation sweeps, full derived suites, and cross-repository release checks remain required where
applicable. They start only after change completeness is green. This preserves confidence while avoiding
known structural failures at the end of the queue.

## Decision 6: model-based lifecycle conformance

### Problem

Example tests cover known histories but do not systematically prove that projection, writer admission,
and transition execution agree across every verdict, check state, round, head, and claim turnover.

### Decision

Define a small reference model for `Review` and `Delivery` and generate bounded histories. At minimum,
cover the cross-product of:

- initial and confirmation verdicts;
- rounds zero through the ordinary ceiling plus an invalid overflow;
- pending, green, red, and unreadable required checks;
- matching and changed heads;
- waiting, completed, cancelled, expired, and malformed waits;
- same, renewed, and fresh claim generations; and
- absent, verified, stale, and contradictory delivery obligations.

For every generated history, assert:

```text
projection decision == writer admission == reducer transition
```

Then run mutation controls that remove one predicate clause or fork one consumer. The model suite must
red on every such divergence.

Production-shaped HTTP tests remain for ownership reads, zero-mutation refusals, pagination, and GitHub
transport behavior. The model tests prove state-space agreement; the HTTP tests prove adapter fidelity.

## Decision 7: immutable release recovery and receiver receipts

### Problem

Publication, promotion, and receiver delivery are different durable effects. If receiver delivery is
sequenced after an asset mutation, promotion can make the recovery step impossible even though package
bytes are already correct.

### Decision

Retain these release invariants:

1. package journals bind exact source and artifact identity before promotion;
2. promotion is the sole transition that makes the coherent release immutable;
3. an immutable retry downloads and verifies the existing journal and performs zero asset writes;
4. receiver delivery resumes from verified journal state; and
5. each receiver result is recorded as an append-only delivery receipt outside the immutable asset set.

The coherent-set obligation is complete only when all package and receiver receipts named by its
declaration are present and verified. Feed publication alone is not sufficient.

## Target flow

```text
authored change
     |
     v
command catalogue / typed lifecycle facts
     |
     v
change-completeness (<5 min target)
     |
     +---- refusal ----> repair before review
     |
     v
full tests + mutation controls
     |
     v
independent review ledger
     |
     v
typed host acceptance ---- self-host receipt if required
     |
     v
merge receipt
     |
     v
verified post-merge obligation receipts
     |
     v
delivery completion receipt
     |
     v
issue/board/claim/cleanup projections
```

No downstream box recomputes the decision made by the box above it. It validates the bound receipt and
performs its own effect.

## Rollout

### Phase 0: measurement and compatibility fixtures

- Record current command-addition edit count, pre-review rejection rate, review rounds consumed by
  structural omissions, and time-to-first-actionable-red.
- Freeze representative snapshots from #2753, #2773, #2819, #2820, and the closing-keyword incident.
- Add no new authority yet.

### Phase 1: command catalogue

- Introduce descriptors alongside existing tables.
- Prove byte-identical command contract and help output.
- Convert tests to generated descriptor cases.
- Remove the BoardOps implementation record and duplicate command list only after parity is green.

### Phase 2: lifecycle decision records

- Introduce complete fact and decision types for ordinary exhaustion and completion.
- Route read and write consumers through them.
- Add model and source-level divergence gates.
- Keep existing JSON schemas stable; translate new internal decisions at the adapter boundary.

### Phase 3: receipt-gated completion

- Add completion receipt parsing and dry-run reconciliation.
- Change Projects automation from direct `Done` mutation to reconciliation dispatch.
- Observe would-correct output before enabling writes.
- Enable fail-closed correction, then make the completion receipt required.

### Phase 4: self-host and CI staging

- Add candidate binary hashing and bootstrap receipts.
- Introduce `change-completeness` as advisory, measure false positives and duration, then require it.
- Make expensive jobs depend on it without removing their path-sensitive execution.

### Phase 5: release and receiver closure

- Persist append-only receiver receipts.
- Require them in coherent-set obligation verification.
- Rehearse mutable, promoted-immutable, partial-feed, and partial-receiver recovery.

Each phase must be independently mergeable and reversible. A phase may not delete its predecessor path
until parity and an effective inversion have both passed.

## Acceptance criteria

The design is implemented when all of the following are true:

1. Adding a nullary command requires one authored descriptor and one handler binding; all other metadata
   and positive test cases are derived.
2. Removing a descriptor, handler, parser arm, render implementation, or write-ness declaration produces
   a named red in `change-completeness`.
3. Review projection and escalation writer consume the same complete decision value, with model tests over
   every verdict/check/round/head/claim class.
4. No item reaches `Done` without a valid exact-merge completion receipt covering every declared
   obligation and zero pending board writes.
5. A GitHub close event without that receipt is corrected rather than treated as authority.
6. Candidate-engine use is impossible without a digest-bound bootstrap receipt and post-merge replay.
7. Structural errors reach an actionable red before critic dispatch and within the five-minute target.
8. Immutable release recovery performs zero release-asset mutation and still produces every required
   receiver receipt.
9. Every new shared decision boundary has an effective mutation that makes all of its consumers red.

## Success metrics

Measure monthly and over at least twenty engine PRs:

- median files and authored inventory rows needed to add a command;
- median time to first actionable CI failure;
- percentage of critic findings that are structural completeness omissions;
- review rounds consumed before a semantically reviewable head;
- projection/writer divergence incidents;
- items corrected from premature `Done`;
- self-host transitions using a complete bootstrap receipt; and
- releases requiring manual journal or receiver-state intervention.

Targets are zero projection/writer divergences, zero premature `Done` states, zero unreceipted self-host
writes, and at least a 50% reduction in command-addition authored inventory edits.

## Alternatives considered

### Keep the current copies and improve the checklist

Rejected. #2753 already had extensive guidance and independent review. Checklists help discovery but do
not establish authority or prevent drift.

### Generate all CLI code

Rejected. Parser behavior and validation are heterogeneous. Generating them would replace visible domain
logic with a complex generator. The catalogue should own metadata, not behavior.

### Trust GitHub issue closure as completion

Rejected. GitHub closing-keyword parsing and Projects automation cannot see exact-head obligations or
pending writes.

### Allow host waivers for self-hosting and baseline-red CI

Rejected as a normal route. A typed bootstrap receipt can encode the narrow self-host exception without
making unrelated red checks waivable.

### Put mutable recovery state back into release assets

Rejected. Promotion intentionally makes those assets immutable. Recovery must validate them and persist
new downstream receipts elsewhere.

## Consequences

Positive consequences:

- fewer authored copies and earlier diagnostics;
- review effort shifts from inventory archaeology to semantics;
- read and write paths cannot silently define different state machines;
- board completion becomes auditable and repairable; and
- self-host and release recovery become normal typed routes rather than exceptional operator knowledge.

Costs and trade-offs:

- the command catalogue becomes a high-value compatibility surface and needs strict review;
- model tests add abstraction and require careful generators to avoid impossible histories;
- completion reconciliation needs a staged migration from existing Projects automation;
- bootstrap receipts add ceremony to the uncommon self-host path; and
- CI dependency changes must preserve required context names and path-sensitive execution.

These costs are deliberate. They are paid once at an authority boundary instead of repeatedly by every
command, review round, and release recovery.

## Non-goals

- replacing GitHub Projects, pull requests, or issue comments as transports;
- weakening independent review or reducing the ordinary review ceiling;
- making unknown or unreadable state permissive;
- replacing production-shaped transport tests with only pure model tests;
- combining merge, publication, receiver delivery, and completion into one remote transaction; or
- retroactively rewriting existing append-only review or release evidence.

## Operational rule while this design is evaluated

Do not create additional instance-specific backlog rows for a missing inventory or projection/writer
copy until the corresponding shared-authority migration has been checked for coverage. A new incident
should be folded into the relevant migration evidence when it has the same cause. New rows remain
appropriate for materially different causes or independently releasable blockers.
