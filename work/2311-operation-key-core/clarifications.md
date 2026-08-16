---
schemaVersion: 1
workId: 2311-operation-key-core
title: Operation Key And Closed Vocabulary In The Pure Core
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2311-operation-key-core/spec.md
publicOrToolFacingImpact: true
---

# Operation Key And Closed Vocabulary In The Pure Core Clarifications

## Source Specification
- work/2311-operation-key-core/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): §3.3 says *"Reuse `Delivery.digest` rather than writing a second one"*, but that function is `private` and `Delivery.fs` is outside this slice's touch-set. What does this slice do?
- **CQ-002** (AMB-002): May a key be composed on the literal generation `released`, which the engine substitutes when no claim marker is held?
- **CQ-003** (AMB-003): What stops two distinct admitted tuples from composing one key — at the concatenation stage, and at the UTF-8 encoding stage?
- **CQ-004** (AMB-004): How is *"adding a case is a compile error at every consumer"* tested, given a test cannot compile a hypothetical fourth case?
- **CQ-005** (AMB-005): What exactly does the reference-graph check assert, and what is its inversion?
- **CQ-006** (AMB-006): Should `OpKey`'s representation be hidden so `compose` is the only producer?

## Answers

**CQ-001.** `Delivery.digest` cannot be called from here, and the reason is measured rather than
assumed. It is declared `let private digest` at `src/FS.GG.Coord.Core/Delivery.fs:99` and does not
appear in `Delivery.fsi` at all, so it is invisible outside its own module; reaching it means editing
`Delivery.fsi`, and `Delivery.fs`/`Delivery.fsi` are not in this item's declared `Paths:`. Widening to
them would put a slice whose entire premise is *"pure, no IO, lands first, changes nothing"* into the
engine's delivery decision path.

The second half of the answer is what makes the trade acceptable rather than merely convenient: the
thing §3.3 points at is **not** one rule with one home today. The primitive it names is invoked at
**ten** sites across **seven** modules inside `FS.GG.Coord.Core` alone, and every one of those modules
keeps its own `private` helper; none is shared.

*Verification:* `grep -rn 'SHA256.HashData' src/FS.GG.Coord.Core/` returns exactly ten lines —
`StructuredDecision.fs:51`, `IntakeReceipt.fs:17`, `SemanticDiff.fs:97`, `Driver.fs:654`,
`Driver.fs:675`, `Delivery.fs:102`, `Review.fs:156`, `CycleLedger.fs:74`, `CycleLedger.fs:136`,
`CycleLedger.fs:240` — and `grep -rc` over the same directory attributes them to seven files
(`IntakeReceipt` 1, `CycleLedger` 3, `Delivery` 1, `StructuredDecision` 1, `Review` 1, `SemanticDiff`
1, `Driver` 2).

So this slice cannot *avoid* being the eighth module with its own copy of the **primitive** without a consolidation whose
subject is eight existing modules, and that is a different piece of work with a different touch-set.
What §3.3 is actually protecting — the **composition rule** for the operation key, `sha256(item \n gen
\n receiver \n op)` — *is* written exactly once, here, and every later slice consumes it rather than
re-spelling it. Decision: implement the hex primitive privately in `Operation.fs`, in the identical
shape the eight neighbours use, and route the "consolidate the SHA-256-hex primitive across Core"
observation to the board analyst as a finding packet rather than absorbing it silently or filing a row
for it.

**CQ-002.** No. `gen` is defined by §3, and by the engine, as *"the comment id of the winning
`fsgg:claim` marker"* — a server-assigned integer, which is what buys the three properties §3.1 lists
(server-assigned, monotone, identically total-ordered for every racer). `released` is the engine's
*absence* sentinel (`ClaimGeneration = marker |> Option.map (fun held -> string held.Id) |>
Option.defaultValue "released"`, `src/FS.GG.Coord.Cli/Client.fs`), and a key composed on it would name
a tenancy that does not exist. Fencing an effect to a non-existent tenancy is exactly the shape `#266`
forbids — a failed or absent read becoming indistinguishable from a legitimate answer. So `compose`
requires the generation to be a non-empty run of decimal digits and refuses anything else, `released`
included, as a value the caller must handle.

**CQ-003.** Two clauses, one per stage, and stating only the first is an error this answer originally
made. The key is `sha256(UTF8(concat …))`, so injectivity has to survive **both** maps.

1. **The concatenation.** Injectivity of `String.concat "\n"` over 4-tuples holds exactly when no field
   can contain the separator, so the domain excludes it: every component is validated to contain no
   `\n`, no `\r`, and no other control character before any hashing happens.
2. **The encoder.** `Encoding.UTF8.GetBytes` is injective only on **well-formed UTF-16**: it uses the
   REPLACEMENT fallback, so every unpaired surrogate — high or low — encodes to the same `EF BF BD`.
   Nothing else in the validation chain refuses one (`Char.IsControl`, `Char.IsWhiteSpace` and the
   `owner/repo` rules are all false for a surrogate), so before this clause existed
   `"FS-GG/r\uD800#2311"` and `"FS-GG/r\uDC00#2311"` were two admitted, genuinely distinct pre-images
   that composed **one** key, `57fa828b1c3b83ebc6022180fe5eefa1fec209f23a32dfa356e64f04160e1e3a`. The
   domain therefore excludes unpaired surrogates too. It excludes only the UNPAIRED ones: a well-formed
   astral character is UTF-8-encodable and distinguishable, so refusing it would shrink the domain
   below what the guarantee needs.

Both together turn *"different tuples give different keys"* from a property sampled by examples into a
property that follows from the construction — and it is why the tests assert distinctness of the
**pre-images** as well as of the digests, and why each clause carries its own inversion evidence. A
component carrying a newline or a lone surrogate is a refusal, not an input that gets hashed into an
ambiguous key.

**Provenance of clause 2, recorded rather than absorbed:** it was missing from this item's first pushed
head and was found by independent review (critic `kite-3d80`, round 1). It is repaired here, not
excused: the finding was unreachable from GitHub-shaped input, but the `.fsi` is the contract slices
2-6 build on, and a guarantee that is false in the interface would be inherited seven more times.

**CQ-004.** In two parts, because neither part alone is honest.

1. **The compile-time half is real and is the actual mechanism**: `Op` is a discriminated union with
   no catch-all, `wire` matches it without a wildcard, and both projects set
   `TreatWarningsAsErrors=true` with an empty `WarningsNotAsErrors`, so FS0025 (incomplete pattern
   match) is an **error**. A fourth case therefore fails the build at every consumer that does not
   handle it. Its inversion evidence is a mutation: delete one arm of `wire`, build, record the red.
2. **The test half is a tripwire, and is described as one rather than as a proof.** A test asserts,
   through `FSharpType.GetUnionCases`, that the vocabulary has exactly the three cases this design
   names and that each has a distinct wire spelling. A test cannot compile a hypothetical fourth case,
   so it cannot demonstrate the compile error; what it *can* do is guarantee that adding a case
   without revisiting this contract reds a named test rather than passing silently.

**CQ-005.** It asserts over the **compiled** graph, not the source text, because criterion 4 says
*"Demonstrate by the project's own reference graph, not by inspection."* The test reads
`typeof<Operation.OpKey>.Assembly.GetReferencedAssemblies()` and requires that (a) no referenced
assembly's simple name begins with `FS.GG.` — so `FS.GG.Coord.GitHub` and any future sibling is
excluded by rule rather than by name — and (b) no referenced assembly's name contains `Http`,
`Octokit`, `GitHub`, or `WebSocket`. Note the boundary this test cannot cross and does not claim to:
`FS.GG.Coord.GitHub` *references* `FS.GG.Coord.Core`, so a reverse reference is a compile-time
circularity and could never appear; the assertion's real subject is a transport or HTTP client pulled
in directly. Its inversion is therefore a genuine transport reference: add a use of
`System.Net.Http.HttpClient` to `Operation.fs`, build, and record the red.

**CQ-006.** No. Every type in `FS.GG.Coord.Core` is representation-transparent —
`Types.WorkerId`, `DeliveryRoute.Receipt`, `Driver.ReviewChain` and the rest are all public — and this
core's stated design rule is about *unrepresentable failure states*, not about unforgeable values. An
`OpKey` is an idempotence key inside a pure library, not a capability: forging one costs a duplicate
no-op, which is precisely the cost §4.3 already assigns to answering the idempotence question wrongly.
Hiding the representation would also cost structural equality, which the distinctness tests rely on.
`OpKey` is a public single-case union whose `.fsi` states that `compose` is the only sanctioned
producer.

## Decisions

- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-003]: The SHA-256-hex primitive is implemented privately in `Operation.fs`, matching the seven existing per-module private copies in `FS.GG.Coord.Core`; the operation-key **composition rule** has exactly one home, which is what §3.3 protects. Consolidating the primitive is routed to the board analyst as a finding packet, not absorbed and not filed as a row here.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-006] [AC-009]: `compose` requires the generation to be a non-empty run of decimal digits — a server-assigned comment id — and refuses `released` and every other non-numeric spelling as a typed refusal the caller must handle.
- **DEC-003** [CQ-003] [AMB:AMB-003] [FR-003] [AC-004]: The domain excludes both collision classes, one per composition stage — every component is refused if it contains `\n`, `\r`, or any other control character (so `String.concat "\n"` is injective), and refused if it is not well-formed UTF-16 (so `Encoding.UTF8.GetBytes` is injective); unpaired surrogates only, since a well-formed astral character is encodable and distinguishable. The distinctness claim rests on those two clauses rather than on examples, and each carries its own inversion evidence.
- **DEC-004** [CQ-004] [AMB:AMB-004] [FR-002] [AC-005]: Closedness is enforced by the compiler — no catch-all arm, no `string -> Op` parse, `TreatWarningsAsErrors` making FS0025 an error — with a reflection-based case-count tripwire described as a tripwire, and a delete-an-arm mutation as its inversion evidence.
- **DEC-005** [CQ-005] [AMB:AMB-005] [FR-004] [AC-007]: Purity is asserted over `Assembly.GetReferencedAssemblies()` with a rule-shaped `FS.GG.` prefix ban plus a transport-name ban, and its inversion is a real `System.Net.Http.HttpClient` reference added to `Operation.fs`.
- **DEC-006** [CQ-006] [AMB:AMB-006] [FR-001]: `OpKey` stays a representation-transparent single-case union, consistent with every other type in this core; the `.fsi` states that `compose` is the only sanctioned producer.

## Accepted Deferrals
- **DEC-007**: Consolidating the SHA-256-hex primitive across the seven `FS.GG.Coord.Core` modules that each carry a private copy is deferred out of this slice — recorded, not dropped, and routed to the board analyst as a finding packet under DEC-001. Deferred because its subject is seven modules outside this item's declared `Paths:`, none of which this pure, first-in-order slice may touch.

## Remaining Ambiguity
- None. AMB-001 through AMB-006 are resolved above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2311-operation-key-core`.
