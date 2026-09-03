---
feedbackSchema: 2
date: 2026-09-03
workspace: FS.GG.Coordination
cycle: roadmap-github-substrate-v2-m6-gs2-06-7-workflow-selection
lane: github-substrate-v2
toolVersion: n/a
commit: 6d3b7662ac4d9474a9976ac093ec910f55fb6087
---

## §1 Provenance and confidence

- **activation:** active
- **phases:** onboarding-first-build, lifecycle-authoring, implementation-test-evidence, verify-ship-pr
- **material events:** 0
- **zero-event reason:** `fs-gg-feedback-report` is not materialized in the Coordination product tree; all four phases were exercised, no substitute checkpoint tool was used, and the recurrence remains deduplicated to [FS-GG/.github#2366](https://github.com/FS-GG/.github/issues/2366).

This report covers GS2-06.7 from exact prerequisite registration through SDD implementation, Q3/Q7
qualification, repair confirmation, protected implementation merge, append-only acceptance, clean-checkout
provider rejection and repair, semantic repair, superseding repair acceptance, and roadmap projection. Confidence is bounded to the exact Git and GitHub
objects, retained evidence, protected runs, and validators named here.

## §2 What worked

Registration issue #260 and PR #261 pinned the canonical roadmap, accepted GS2-06.6 receipt, unit contract
`a6eb28bb64a56dc6379cefc6682336b7bface7b8ea2d29711429d05b3e507b79`, and ordered Q3/Q7 command
digests. Candidate `c10590c4fcc35fdae22435af09b5b551682910a2` passed independent review `5518639298`
and host acceptance `5518668723`, then squash-merged as
`ff65faa6697ea5835de72c1e6fa1b53af0a883e3`. Delivery `5518694233` records digest
`760d1f20cb58d67f0da1d5adf43d71025dc2e1961568701186e730ae9a4811ae`; protected Bootstrap
`33701716628` and Push-on-main `33701716127` passed.

Implementation issue #262 and PR #263 produced a sealed, mutation-free selector over the complete typed
workflow inventory, file and non-file impact graph, stable aggregates, typed `NotApplicable`, merge-group
recomputation, fleet baselines and targets, sentinels, deterministic fleet disablement, and the deletion
ledger. The initial review `5519020498` found a major root-binding survivor. Repaired candidate
`6720277cd17f96e044e5afd9b18fa71813f9cbca` passed confirmation `5519081717` (digest
`7395681afa5bbed16c632018bddc81af05e8eae5774ee1e999c406046d0974d5`) and host acceptance
`5519120654` (digest `558767721a6d425ae0f158ee54eefa43485b283207a936d2fb52c60f1777cc0e`).
Q3 passed 23 controls and Q7 passed 12 controls with seal
`43836b63dc7c0324331a270f462ace5b73c6427ff573d80e152ad4d58e3f18ab`; unit tests passed
185/185 and architecture tests 458/458. The candidate squash-merged as
`3277a7a581f9b75001a851e0b614de3c1eadf812`; delivery `5519145418` records digest
`e0e995fab8a077d82a0701d83da935a723ccce4cc26a546291c55077e4d31592`, and protected Bootstrap
`33705575802` plus CodeQL `33705575434` passed.

Acceptance issue #264 and PR #265 bound the exact implementation merge, Q3/Q7 evidence, repair and host
records, delivery, protected runs, and accepted GS2-06.6 prerequisite into receipt
`c6d1662e7df93f8b6ca8f577b5143e1e8a45eb9ac6fe55922488659ff9363036`. Candidate
`05ce70b3f05a7178bfad0af68649e8b80db6c5ac` passed review `5519238919` and host acceptance
`5519346373`, then squash-merged as `81574bc8d12d46d08d60448b96db641fc3cb0de9`. Delivery
`5519369598` records digest `bc006aeec8f4310566dfed91bdf665f079abe8801ef46a393749c3840b903234`;
protected Bootstrap `33707365752` and Push-on-main `33707365512` passed.

The first roadmap provider preflight rejected accepted main because the retained work model and verification
view came from ambient SDD 1.5.0 instead of the canonical 1.0.0 consumer. Typed issue #266 and PR #267
stabilized the exact canonical views and extended the tracked-byte guard. Candidate
`353c5a5ab9cd47cc01ea4ecfda6aee3d5bf58bfe` passed independent review `5519507655` (digest
`577255bd7f557e49051b5c6b6c95f2f23f2e46d8babc8212bed574a48a026377`) and host acceptance
`5519547792` (digest `d30d6701344676899760ff4751a17069fed70acc4a42159dc55015baf8876261`), then
squash-merged as `57305e540267f3f4696ba5a6cdfc84361de577d3`. Protected Bootstrap `33708922854` and
Push-on-main `33708922489` passed; delivery `5519583995` records digest
`fd0df37052c30cbf5bcac47c00c75b4a0f311ee8a3a0cd582b7fddf50a8e5706`.

A second fresh isolated checkout at that protected merge found all five provider paths tracked. Canonical
FS.GG.SDD.Cli 1.0.0 returned `noChange`, `coherent=true`, 38 ready obligations, 19 observed evidence
items, 19 observed tests, zero diagnostics, four `noChange` operations, and a clean Git tree.

The independent roadmap critique then correctly rejected the accepted behavior itself: the upstream tree
contained a fixed qualification corpus rather than an arbitrary-input production selector, and its ten
identical fleet rows had no observation provenance. Semantic issue #268 and PR #269 added the Core API and
CLI, composite action, reusable selection workflow, stable aggregate, scheduled full-suite sentinel, strict
typed-decision boundary, merge-group current-head authority, reviewed inventory authority, and fail-closed
inversions. Two changes-required reviews exposed the original sentinel eligibility error and five deeper
trust/aggregation gaps; the final candidate `2da51ef3a77ac826e16141418232a46d786388c6` passed independent
confirmation `5520730651` (digest `f6be2102b687b199853db43b5168addb170cf13684e97ae4ce714b021673fa30`)
and host acceptance `5520759515` (digest `e2aa1ff5e147c063d93224459194b87422ae4bf288301862fa344f9d86ccc548`).
It squash-merged as `286bde7afd607ac8e62a4ca71f6f82d363c052b4`; protected Bootstrap
`33717657519` and CodeQL `33717657228` passed. Unit tests passed 189/189, architecture tests 464/464,
Q3 passed 23 controls, and Q7 passed 12 controls over ten repositories at seal
`1a7b8201130401a703c9f65812c23412330b9092e8ffe050b945e3a8d11de868`. The retained observation set
binds 80 Actions runs and 305 unique jobs; fleet selection remains disabled and no consumer was enabled.

Separate issue #270 and PR #271 preserved the original receipt byte-for-byte while adding indexed repair
receipt `repair-GS2-06.7-268`. Candidate `31eccc62a83ad4ae15cb0338156001cbb6e6eb77` passed independent
review `5521120106` (digest `09698819fd142eb04565389501bc8873b8a213eae992e34e9d121f852c2696f9`)
and host acceptance `5521151909` (digest `18b18fc7121f5dafd73136b542a556656a7c2f5f4996516a3f2a7ed399b4190e`),
then squash-merged as `6d3b7662ac4d9474a9976ac093ec910f55fb6087`. Protected Bootstrap
`33720546807` and CodeQL `33720546536` passed. The repair receipt is 3,171 bytes, file SHA-256
`37d36961589dbcd5db2b1d9deab09932dc9204fedaddb618eea5be6dfddbfd27`, and self-digest
`f0360e50a1c94262d8c1b83a12871276c8b1c58e6efceba14b49926dca00d45d`; the original remains 2,242
bytes, file SHA-256 `9a98a13213c9a6934b362a6cb75dc3b523800205961e76cd4de984157733dc0b`, and self-digest
`c6d1662e7df93f8b6ca8f577b5143e1e8a45eb9ac6fe55922488659ff9363036`.

The final fresh isolated checkout at `6d3b7662` retained analysis, work-model, verification, both TRX files,
the original receipt, and the repair receipt. Canonical FS.GG.SDD.Cli 1.0.0 returned `noChange`,
`coherent=true`, zero diagnostics, four no-change operations, and a clean tree. Final tracked hashes are
analysis `75cad913`, work model `5d4a3f00`, verification `1a8b8aee`, unit TRX `a2746a37`, and architecture
TRX `13648d7f`.

## §3 What did not

The first implementation review proved that changed-subject labels and non-file inputs were not bound to
their typed roots: a well-formed source-to-release reclassification survived. The repair added exact bindings
for all representative inputs and an independently authored inversion before acceptance.

The first projection critique proved that the supposedly accepted implementation still stopped at fixed
fixtures and that its uniform fleet baselines were not measurements. Projection was rolled back before PR,
linked as blocked by #268, and remained unchecked until production surfaces, observation provenance, and a
separate repair receipt had completed their own review and protected-main chains.

The accepted tree retained all provider paths, but its work model and verification view were generated by
SDD 1.5.0. Canonical 1.0.0 rewrote them and reported `coherent=false`; running canonical analyze first made
verification block on stale evidence. Provider repair #266/#267 restored the consumer-defined fixed point
before any projection artifact was authored.

The GitHub repository uses squash-only transport. Four reviewed candidate identities therefore differ from
their protected merge identities: `c10590c4` → `ff65faa6`, `6720277c` → `3277a7a5`, `05ce70b3` →
`81574bc8`, and `353c5a5a` → `57305e54`. Review evidence binds candidate heads; receipts, prerequisites,
protected runs, and roadmap authority bind squash merge heads. Treating either identity as the other would
make an otherwise accurate evidence chain stale.

The first typed intake application partially created provider issue #266 before refusing its board projection
because no worker identity was present. Exact-id resume completed the transaction without duplicating the
issue. This was bounded orchestration friction, not product behavior.

Provider PR #267 also exposed a guarded-landing transport defect: `fsgg-coord delivery --apply` sent the
default merge-commit request to a squash-only repository and GitHub returned HTTP 405. The already accepted,
exact-head candidate was then squash-merged explicitly, and typed delivery resumed to its verified completion
receipt. Durable [`.github#3169`](https://github.com/FS-GG/.github/issues/3169) owns selecting an enabled
repository merge method inside guarded landing.

## §4 Findings

No checkpoint-backed feedback finding was created. The implementation root-binding defect and provider
currency defect, missing runtime selector, and unproven-baseline defects are resolved in the schema-v3 critique.
The guarded-landing merge-policy defect is routed to
`.github#3169`; the missing feedback skill remains deduplicated to `.github#2366`.

## §5 Did not exercise

No production workflow, fleet setting, ruleset, required check, merge queue, repository setting, package,
release, deployment, feed, or successor-unit behavior was mutated. This non-game offline qualification and
roadmap-projection milestone requires no player journey.

## §6 Doc-versus-behavior contradictions

The roadmap contract expects `fs-gg-feedback-report` in a fully materialized product tree, while this
Coordination checkout omits it. `.github#2366` owns that scaffold-provenance contradiction.

## §7 Workarounds still in the tree

The provider repair used one explicit exact-head squash merge after guarded delivery returned HTTP 405; the
typed completion flow then verified the same protected merge. `.github#3169` owns removal of that out-of-band
transport fallback. The retained provider files and durability test are explicit tracked evidence; no ignored
output, external feedback tool, production writer, or secret shim remains in the tree.

## §8 Friction and avoidable cost

One initial implementation repair was required for an independently reproduced root-classification survivor.
One provider repair was required because the producer used an ambient SDD version newer than the roadmap
consumer's canonical verifier. The projection critique then required the substantive #268 repair and separate
#270 acceptance. PR #269 needed two recorded changes-required review rounds plus two unrecorded superseded
exact-head reviews: those reviews found a false eligible sentinel, malformed evidence acceptance, stale
merge-group and inventory authority, static failure attribution, uncorrelated skipped aggregates, CI-only
output observability, and duplicate JSON member collapse. All were repaired and covered by inversions before
the final pass. Squash transport required explicit candidate-to-merge mappings at every layer, and the known
guarded-delivery HTTP 405 recurred for the repair merges. Exact `--match-head-commit` squash preserved the
reviewed heads while `.github#3169` continues to own the transport fix.

## §9 Skill value and gaps

`github-substrate-v2-work` preserved the accepted prerequisite, Q3/Q7 ordering, permission ceiling, and unit
boundary. `pnext-item` preserved exact-head review, host acceptance, protected squash merge, and completion
receipts. `work-roadmap` enforced the clean-checkout canonical-provider gate, schema-v3 critique,
schema-v2 feedback/audit, typed cycle, and exact four-path projection. The absent feedback skill remains the
only activation gap.

## §10 Outcome markers

- Registration: #260/#261; candidate `c10590c4`; merge `ff65faa6`; delivery `5518694233`.
- Implementation: #262/#263; repaired candidate `6720277c`; merge `3277a7a5`; Q3 23, Q7 12, seal `43836b63`.
- Implementation review: changes-required `5519020498`; confirmation `5519081717`; host `5519120654`.
- Acceptance: #264/#265; candidate `05ce70b3`; merge `81574bc8`; receipt `c6d1662e7df93f8b6ca8f577b5143e1e8a45eb9ac6fe55922488659ff9363036`.
- Provider repair: #266/#267; candidate `353c5a5a`; merge `57305e54`; review `5519507655`; host `5519547792`; delivery `5519583995`.
- Semantic repair: #268/#269; final candidate `2da51ef3`; merge `286bde7a`; review `5520730651`; host `5520759515`; protected runs `33717657519`/`33717657228`.
- Repair acceptance: #270/#271; candidate `31eccc62`; merge `6d3b7662`; receipt `f0360e50a1c94262d8c1b83a12871276c8b1c58e6efceba14b49926dca00d45d`; protected runs `33720546807`/`33720546536`.
- Final provider hashes: analysis `75cad913`, work model `5d4a3f00`, verification `1a8b8aee`, unit TRX `a2746a37`, architecture TRX `13648d7f`.
- Transport follow-up: `.github#3169` records guarded landing's HTTP 405 on squash-only repository policy.

## §11 Falsifiable improvements

Pinning the roadmap cycle's canonical SDD version at producer authoring time should make the first clean
checkout replay return `noChange`; any future ambient-version output must red the retained generator and
digest guard before acceptance. `.github#3169` should make the same exact-head guarded delivery succeed with
the repository's enabled squash method and no fallback. After `.github#2366` lands, the same product checkout
should validate real checkpoint invocations instead of the zero-event fallback.

## §12 Development-surface coverage

| Surface | Status | Evidence and result |
|---|---|---|
| onboarding-guidance | exercised | Accepted GS2-06.6 and exact roadmap bytes gated registration. |
| skills | partial | Roadmap, unit, SDD, review, and delivery skills exercised; feedback skill absent. |
| sdd-authoring | exercised | Existing lifecycle package reached implementation, evidence, verification, and ship readiness. |
| implementation-apis | exercised | Production Core/CLI arbitrary-input selection, workflow contracts, aggregate, sentinel, and Q3/Q7 contracts qualified. |
| dependencies-build | exercised | Release builds passed with warnings as errors. |
| testing | exercised | Unit 189/189; semantic architecture 464/464; receipt architecture 465/465; Q3 23; Q7 12. |
| evidence | exercised | Original and indexed repair receipts, 80-run/305-job provenance, review, protected runs, fixed provider bytes, and inversions verified. |
| runtime-playtest | not-exercised | Non-game offline qualification unit. |
| performance | exercised | Ten repository baselines and accepted fan-out/time targets qualified offline. |
| documentation | exercised | SDD, evidence, feedback, critique, and roadmap ledger are covered. |
| packaging-upgrade | not-exercised | No release or package publication occurred. |
| worker-git-pr | exercised | PRs #261, #263, #265, #267, #269, and #271 preserve candidate/merge identity pairs. |
