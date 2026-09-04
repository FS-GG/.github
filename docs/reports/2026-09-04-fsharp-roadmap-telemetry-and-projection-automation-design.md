---
title: F# roadmap telemetry and projection automation
category: Design
categoryindex: 4
index: 83
description: Proposed deterministic replacement for agent-mediated roadmap telemetry, validation, and acceptance projection.
---

# F# roadmap telemetry and projection automation

**Status:** accepted for filing on 2026-09-04; implementation and release remain governed by the filed
item's claim, SDD, review, and delivery route.

This design moves lifecycle telemetry and mechanical roadmap closure out of agent instructions and Python
skill helpers into typed, deterministic F# commands. The current `.github` repository remains the initial
owner because it builds and publishes `FS.GG.Coord.Cli` and owns the `work-roadmap` and `pnext-item`
skills. `FS.GG.Coordination` becomes the eventual product owner only through a separately reviewed,
versioned contract migration. Independent semantic critique and exceptional disposition remain judgment
boundaries; collection, parsing, validation, hashing, projection, and gate aggregation do not.

## 1. Problem and measured opportunity

The GS2-07.2 product path completed in approximately 1 hour 40 minutes. Its later roadmap projection
took 53 minutes 41 seconds before its first completed delivery and consumed at least 21.12 million
measured tokens. A further 41 minutes 23 seconds and 1.74 million tokens were spent recovering an
engine-pin and registry propagation incident after the projection had already completed once.

The normal projection changed four non-runtime files and its independent projection critique reported no
findings. Most of its cost came from repeatedly reconstructing and checking facts already present in
content-addressed receipts:

| Phase | Measured tokens | Deterministic share proposed |
|---|---:|---:|
| Projection authoring | 7,290,402 | 80–90% |
| Typed-cycle validation | 534,353 | 100% |
| Projection critique | 5,242,230 | 55–70% mechanical checks; semantic verdict remains independent |
| Host acceptance | 3,360,798 | 85–95% |
| Guarded merge | 1,418,806 | 95–100% after authorization |
| Protected-main verification | 1,163,559 | 100% |
| Typed-cycle completion | 1,040,699 | 100% |
| Cleanup | 1,067,765 | 90–100% |

The skill implementation also duplicates executable policy:

- `collect-runtime-usage.py`: 281 lines, byte-identical in `work-roadmap` and `pnext-item`;
- `validate-lifecycle-log.py`: 798 lines, byte-identical in both skills;
- `validate-critique-state.py`: 319 lines;
- `validate-feedback-state.py`: 166 lines; and
- the `.agents` skill trees are mirrored again under `.claude`.

There are 2,643 Python lines in the `.agents` copies and approximately 1,564 lines of unique behavior.
Mirroring doubles the physical maintenance surface. More importantly, agents currently assemble lifecycle
events and command sequences that are completely derivable from typed inputs.

The target is to remove **80–90% of agent-mediated closure mechanics** and **95–100% of agent-mediated
telemetry mechanics**. This is an engineering target to validate on later GS2 units, not a promised
performance result. CI and GitHub latency remain wall-clock costs even when no agent supervises them.

## 2. Decision boundaries

### 2.1 Deterministic code owns

- Codex and Claude usage-record parsing without reading conversation content;
- strict token arithmetic, provider/model/tool-version binding, and phase-local aggregation;
- canonical JSON serialization and SHA-256 digest chaining;
- lifecycle transition validation, duration calculation, history grouping, and terminal-state checks;
- GitHub comment export, edit detection, canonical-child election, and rejected-fork reporting;
- critique and feedback schema validation;
- accepted-receipt, implementation-head, review-head, merge-head, check, and cycle identity joins;
- generation of the bounded GS2 roadmap acceptance projection from accepted evidence;
- deterministic feedback/audit structure and zero-event representation;
- exact-main readback, cycle completion, and machine-readable completion summary; and
- classification of unrelated failed checks as separate obligations rather than silently attaching them
  to the completed roadmap unit.

### 2.2 Independent judgment owns

- assessing whether requirements, architecture, tests, and evidence are substantively adequate;
- authoring and classifying genuinely novel findings;
- deciding whether a discovered failure is material to the unit or an independently owned obligation;
- accepting an explicit exception or changing policy; and
- approving an externally mutating merge or cross-repository contract migration.

Agents may supply judgment records as typed inputs. They must not calculate durations, token totals,
digests, cycle state, projection text, or check convergence.

## 3. Ownership

### 3.1 Initial implementation owner: `.github`

The current executable boundary is the `FS.GG.Coord.Cli` dotnet tool built from this repository. Its
existing F# code already owns:

- typed cycle transitions in `FS.GG.Coord.Core/CycleLedger.fs`;
- cycle application and provider validation in `FS.GG.Coord.Cli/CycleLedgerApplication.fs`; and
- serialized lifecycle-comment creation and server-order election in
  `FS.GG.Coord.Cli.BoardOps/Handlers.fs`.

The first implementation belongs beside those surfaces. Putting another source copy directly into
`FS.GG.Coordination` would create two authorities before a migration contract exists.

### 3.2 Eventual owner: `FS.GG.Coordination`

When the v2 substrate reaches a roadmap unit that admits this responsibility, migrate the public
telemetry/projection contract to `FS.GG.Coordination` by version, not by copied source. The migration must
state old and new semantics, producer and consumer acceptance, compatibility window, package version,
and publish-before-adopt order. `.github` retains organization policy, the roadmap document, desired
projection format, and release registry; the Coordination product owns execution.

## 4. Proposed F# architecture

```text
runtime JSONL / provider snapshot       accepted GS2 receipt + review
                 |                                  |
                 v                                  v
        RuntimeUsage adapters              RoadmapClosure inputs
                 |                                  |
                 +---------------+------------------+
                                 v
                    pure typed validation/reduction
                                 |
                  +--------------+---------------+
                  |                              |
                  v                              v
          Lifecycle ledger                Projection document
        canonical event bytes             bounded generated block
                  |                              |
                  +--------------+---------------+
                                 v
                    existing verified GitHub writes
                                 |
                                 v
                   exact-main completion receipt
```

### 4.1 Core types

Add pure types under `FS.GG.Coord.Core`:

```fsharp
type TokenCounts =
    { Input: int64
      CachedInput: int64
      CacheWriteInput: int64
      Output: int64
      Reasoning: int64 option }

type TokenUsage =
    | Pending
    | Measured of counts: TokenCounts * source: EvidenceDigest * sessions: string list * turns: string list
    | Unavailable of reason: string * source: string

type LifecycleTransition = Started | Completed | Blocked | Resumed

type LifecycleLedger =
    { RunId: string
      UnitId: string
      Events: LifecycleEvent list }

type TelemetryFinding =
    | InvalidRuntimeRecord of location: string * reason: string
    | InvalidTokenArithmetic of reason: string
    | InvalidLifecycleTransition of phase: string * reason: string
    | EvidenceMismatch of subject: string * expected: string * observed: string
    | EditedAuthorityComment of commentId: int64
    | RejectedFork of winningCommentId: int64 * rejectedCommentId: int64
```

Use explicit discriminated unions for provider records, event states, findings, and projection outcomes.
Do not carry Python-style open dictionaries into the core reducer. Parsing may use `JsonDocument`, but a
successfully parsed record must immediately become a closed typed value.

### 4.2 Modules

| Module | Responsibility |
|---|---|
| `RuntimeUsage` | Parse Codex JSONL and Claude snapshots, validate counters, select exact turns/windows, aggregate phase-local deltas, emit stable CSV or JSON. |
| `LifecycleTelemetry` | Validate event shapes and transitions, calculate durations and comparable history, canonicalize and seal successors. |
| `LifecycleCommentProjection` | Import GitHub comments, reject edits, elect the lowest-id canonical successor, retain fork findings. |
| `CritiqueReceipt` | Validate schema-v3 critique identity, rounds, confirmation, journeys, and unresolved severities. |
| `FeedbackReceipt` | Validate schema-v2 feedback and audit identity, phases, findings, and zero-event rationale. |
| `RoadmapClosure` | Join accepted product receipt, review, feedback, protected checks, and typed-cycle facts into a closed completion model. |
| `RoadmapProjection` | Render only the marker-bounded status, checkbox, and evidence paragraph for one unit; refuse ambiguous or unbounded Markdown edits. |
| `TelemetrySummary` | Produce phase totals, cached/fresh split, wall/active/wait classification, missing-data inventory, and machine-readable roll-ups. |

`CritiqueReceipt` and `FeedbackReceipt` should absorb the remaining Python validators already invoked
indirectly by `CycleLedgerApplication`. The trusted validator identity then becomes the signed tool/package
identity rather than the SHA-256 of a Python file placed beside the executable.

### 4.3 CLI surface

Add one coherent family rather than one command per former script:

```text
fsgg-coord telemetry usage collect codex ...
fsgg-coord telemetry usage collect claude ...
fsgg-coord telemetry lifecycle export-comments ...
fsgg-coord telemetry lifecycle seal-successor ...
fsgg-coord telemetry lifecycle validate ...
fsgg-coord telemetry summarize ...
fsgg-coord roadmap close inspect ...
fsgg-coord roadmap close render ...
fsgg-coord roadmap close verify ...
```

The CLI must retain the present stable CSV header, canonical compact JSON bytes, digest algorithm,
exit-code behavior, and privacy boundary. Raw runtime files and local paths stay private. Public records
contain aggregates, stable content digests, and runtime identifiers only.

`roadmap close render` is pure: it prints a candidate document or patch and never writes GitHub or the
working tree. Existing verified mutation commands remain the only remote write boundary. A later
orchestrator can compose inspect, render, review, guarded merge, and exact-main verify without recreating
their logic in prompts.

## 5. Deterministic projection contract

Roadmap prose should no longer be freely regenerated. Each unit receives a marker-bounded machine block
whose inputs are:

- unit id and title from the current roadmap;
- accepted product receipt and its canonical digest;
- implementation candidate, implementation merge, and acceptance merge identities;
- generated and independent gate counts;
- critique verdict and repair-round count;
- exact-main workflow receipts;
- issue/PR closure and claim census;
- provider cycle id and guarded update digest; and
- optional typed observations that have an owning evidence source.

The renderer refuses an unchecked unit with no matching accepted receipt, an already checked unit whose
generated bytes differ from the same inputs, multiple matching blocks, a stale roadmap source digest, or
evidence that crosses into a successor unit. Rendering the same inputs twice must produce byte-identical
output.

The feedback report becomes two layers:

1. a deterministic envelope containing provenance, exercised surfaces, outcomes, counts, and known gaps;
2. optional reviewed observations containing the genuinely interpretive `worked`, `did not`, and
   improvement statements.

An empty observation layer produces a valid, explicit zero-event report. An agent is not required merely
to paraphrase receipts.

## 6. Lifecycle behavior

The F# reducer preserves the current invariants:

- revisions and sequences are contiguous and equal;
- every digest binds the complete canonical event excluding `digest`;
- `started` and `resumed` carry pending usage and no completed duration;
- `completed` and `blocked` require measured or specifically unavailable usage;
- terminal token totals equal input plus output, cache counters cannot exceed input, and reasoning is a
  subset of output;
- one phase binds one actor, model, source, and tool fingerprint;
- events are nondecreasing within a phase while independent phases may overlap;
- blocking and resumption are phase-local;
- completed phases cannot resume;
- required phases must exist before completion;
- the final ledger has no active or blocked phase; and
- GitHub comment id, not agent observation order, elects a concurrent successor.

Durations, cached/fresh ratios, and totals are computed on read. Agents provide timestamps and evidence;
they do not provide derived numbers that code can calculate.

## 7. Migration plan

### Stage A — F# parity behind the existing skills

Implement the typed modules and CLI commands in `.github`. Build a frozen fixture corpus from both Python
implementations, including every positive self-test and rejection mutation. Run Python and F# over the
same corpus and require byte-identical successful output plus equivalent typed refusal categories.

No skill command changes in this stage. Python remains the production oracle while F# proves parity.

### Stage B — publish before flip

Publish one coherent `FS.GG.Coord.Cli`/Kit/Drivers release containing the new commands. Verify the exact
package from both feeds and update receiver pins before changing skill instructions. The package must
carry the validators; a receiver must not resolve validation code from its mutable checkout.

### Stage C — skill migration

Update `work-roadmap` and `pnext-item` references to call `fsgg-coord telemetry ...` directly. Replace the
duplicated Python scripts with temporary compatibility launchers only if an identified supported receiver
still calls their paths. A launcher contains argument translation and `exec` only; it contains no parsing,
validation, hashing, or business rules.

Update `.agents` first, regenerate the `.claude` projection through its existing generator, and assert
byte agreement. Do not hand-edit both mirrors.

### Stage D — delete Python

After the compatibility window, delete all four Python helpers and every registry/package reference to
them. Add an executable absence gate so no skill, test, manifest, or packaged artifact invokes:

```text
collect-runtime-usage.py
validate-lifecycle-log.py
validate-critique-state.py
validate-feedback-state.py
```

### Stage E — automate roadmap closure

Add `roadmap close` only after telemetry parity is accepted. First use it in read-only/render mode beside
one manually reviewed projection. Once byte stability and refusal behavior are demonstrated, make its
rendered projection the sole normal source for the bounded machine block. Keep guarded merge and policy
exceptions separately authorized.

### Stage F — optional v2 ownership migration

File a distinct cross-repository contract change when `FS.GG.Coordination` is ready to own this surface.
Publish the new owner first, migrate consumers, verify registry state, and retire the `.github` execution
copy. Do not maintain both implementations.

## 8. Acceptance criteria

The implementation is acceptable only when:

1. F# reproduces the frozen valid outputs of all four Python helpers.
2. Every existing negative self-test remains red through an independently authored F# test or black-box
   CLI fixture.
3. Provider casing mismatch, malformed counters, edited comments, digest forks, stale source, wrong model
   or tool version, invented history, and unreconciled terminal usage each produce distinct findings.
4. `work-roadmap` and `pnext-item` use the same compiled implementation with no copied logic.
5. The packaged tool validates from its own immutable bytes, not scripts selected from a candidate tree.
6. A complete lifecycle can be exported, sealed, validated, summarized, and re-exported byte-identically.
7. A GS2 accepted receipt can render the same roadmap block twice with no change.
8. Tampering with any receipt, head, gate, critique, feedback audit, or roadmap source makes closure refuse.
9. An unrelated non-required failed check becomes a separately owned obligation and cannot reopen a
   completed unit without a typed materiality decision.
10. Raw runtime content and local filesystem paths never enter public output.
11. Skill-quality, package verification, warning-as-error build, unit tests, architecture tests, and
    clean-checkout receiver smoke tests pass.
12. The Python helpers and their packaged references are absent at the end of the compatibility window.

## 9. Expected effect

For an ordinary non-runtime roadmap projection with an already accepted product receipt:

| Measure | GS2-07.2 observed | Design target |
|---|---:|---:|
| Normal projection wall time | 53m 41s | 10–20m plus CI latency |
| Agent-mediated phases | 8 after intake | 1 semantic critique, plus exception handling only when needed |
| Known projection tokens | 21.12m | 2–4m, to be measured rather than assumed |
| Manually assembled machine artifacts | 4 | 0 |
| Distinct telemetry implementations | duplicated Python skill copies | 1 compiled F# implementation |
| Post-completion unrelated-check churn | 41m 23s on GS2-07.2 | separate obligation; zero time charged to the closed unit |

The design does not weaken independent review. It removes agents from work whose correct answer is already
a pure function of receipts, timestamps, immutable bytes, and GitHub facts. The remaining agent token
budget is spent on semantic criticism and exceptional decisions, where judgment is actually valuable.

## 10. Accepted decisions

1. `.github` owns the initial implementation and coherent tool release. A later move to
   `FS.GG.Coordination` requires its own versioned migration.
2. Successful F# outputs remain byte-identical to the Python outputs during parity and migration.
3. The renderer owns a bounded machine block; optional human narrative remains outside it.
4. An unrelated failed non-required check creates or links a separate obligation. It cannot automatically
   reopen a completed unit without a typed materiality decision.
5. Logic-free compatibility launchers may remain for at most one coherent release when a measured receiver
   still requires them; otherwise the Python helpers are deleted at the skill flip.
