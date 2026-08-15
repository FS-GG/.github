---
schemaVersion: 1
workId: 2311-operation-key-core
title: Operation Key And Closed Vocabulary In The Pure Core
stage: charter
changeTier: tier1
status: chartered
policyPointers:
  - .fsgg/sdd.yml
  - .fsgg/agents.yml
  - .fsgg/policy.yml
  - .fsgg/capabilities.yml
  - .fsgg/tooling.yml
---

# Operation Key And Closed Vocabulary In The Pure Core Charter

## Identity
- Work id: `2311-operation-key-core`
- Coordination item: `FS-GG/.github#2311`
- Parent epic: `FS-GG/.github#1858`
- Governing design: `docs/reports/2026-08-04-github-native-executor-fencing-design.md` §3, §4.1, §11.2 slice 1
- Lifecycle stage: charter
- Status: chartered
- Delivery route: `sdd-required` (route-decision revision 1, digest
  `c76b49e3ba25ffd5c1c50b32063d6551ab9d57d86b5bdc8463011d28f8837042`)

## Principles
- **Exclusion and idempotence are different questions with different answers.** The design is
  explicit that mutual exclusion is answered by the *subject* — one lock issue per receiver (§4.1)
  — and idempotence by the *opkey*, recorded in the effect receipt and checked by the receiver.
  A type that blurs the two invites the next reader to use one where the other belongs, so the
  signature file must say, for each exported thing, which question it answers.
- **A vocabulary is a sum, not a string.** An open vocabulary is how a fourth operation gets added
  at one call site and silently fails to dedupe at another. Closedness here means the compiler
  refuses an unhandled case at every consumer, not that a validator rejects an unknown literal at
  runtime.
- **The key is a total function of its inputs, and its distinctness is proved by construction.**
  A test that only asserts `key x = key x` cannot distinguish a real digest from a constant
  (acceptance criterion 3). The claim to establish is injectivity of the pre-image over each of the
  four components, at the pre-image level where it is decidable, and then distinctness of the
  digests that follow.
- **This slice is pure by construction, and the proof is the reference graph.** Acceptance
  criterion 4 constrains the project's references, not its source text; "I read the file and saw no
  `open`" is inspection, and inspection is what criterion 4 rules out.
- **A gate ships with evidence it can fail.** Every assertion this slice adds is a gate over the
  new surface; each one is inverted at authoring time and its red recorded.

## Scope Boundaries
- **In scope:** `src/FS.GG.Coord.Core/Operation.fs` and `Operation.fsi` — the `Operation` closed
  vocabulary, its wire spelling, the `OpKey` type, and the digest composition over
  `(item, gen, receiver, op)`; the two project files that must list them (`FS.GG.Coord.Core.fsproj`,
  `FS.GG.Coord.Core.Tests.fsproj`, both of which enumerate every source file explicitly and carry no
  globs); and `tests/FS.GG.Coord.Core.Tests/OpKeyTests.fs`.
- **Out of scope, and each for a stated reason:**
  - **Writing the opkey into any CAS marker.** §4.1: the write path *"gains no code, no prefix, no
    field, no parameter"*, and `pathRepo=` is deliberately not reused to smuggle it.
  - **Any IO, transport, GitHub type, or board contact.** This slice is ordered first precisely
    because it is pure.
  - **Any fencing behaviour.** Slices 2–6 fence; this is the vocabulary they share. Nothing here
    changes an existing decision.
  - **`Delivery.fs`/`Delivery.fsi` and every other existing Core module.** They are outside the
    declared touch-set, so the SHA-256-hex primitive is not consolidated here; see the
    clarification record for the decision and its cost.
- Keep SDD lifecycle ownership separate from optional Governance enforcement.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2311-operation-key-core`.
