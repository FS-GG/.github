---
schemaVersion: 1
workId: 2563-cross-language-indent-limit
title: Cross Language Indent Limit
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2563-cross-language-indent-limit/spec.md
sourceClarifications: work/2563-cross-language-indent-limit/clarifications.md
sourceChecklist: work/2563-cross-language-indent-limit/checklist.md
publicOrToolFacingImpact: true
---

# Cross Language Indent Limit Plan

Prose status: planned

## Source Snapshot
- spec: work/2563-cross-language-indent-limit/spec.md sha256:91b30031534f2c636f88b3ed532b72a3f4b999b65c152cfe5b973c3705a06b07 schemaVersion:1
- clarifications: work/2563-cross-language-indent-limit/clarifications.md sha256:8310e1a0f0d4fd46bee10b27352a3d7c3711f39c0ccd6e9da59c0d63238ed90e schemaVersion:1
- checklist: work/2563-cross-language-indent-limit/checklist.md sha256:7fa349146e288fb4f9a06ab6f8c692b05f72a7e952b19b32b196263948bf5106 schemaVersion:1

## Plan Scope
- Work item 2563-cross-language-indent-limit is planned from the current specification, clarification, and checklist facts.
- Requirement count: 7.
- Clarification decision count: 2.
- Checklist result count: 7.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Author `tests/delivery-leading-line/corpus.json` as the single
  statement of the delivery-marker leading-line boundary — one entry per comment body, each carrying
  `name`, `body` and the verdict `declares` or `inert` — and delete the per-language single-body indent
  legs it replaces, so that no language retains a private leg asserting a SINGLE COMMENT BODY's
  declares/inert verdict. The four `#2544` engine-only legs carrying four-space declaration-form bodies
  (`DeliveryApplicationTests.fs:304`/`:307`/`:318`/`:492`) are NOT among them and stay — the
  engine-only retention decision below, and DEC-001, record why the corpus cannot subsume them. Reproduce the pre-change 18/18 baseline first; it is
  both the corpus's content and the regression bar.
- PD-002 [AC-002] [FR-002] complete: Add the F# consumer to
  `tests/FS.GG.Coord.Cli.Tests/DeliveryApplicationTests.fs`. It walks up from `AppContext.BaseDirectory`
  to the repository root — the idiom already used by `RuleSubsetTests`, `DocumentedInvocationTests` and
  `SkillProgressiveDisclosureTests`, since no `.fsproj` in this repo copies content to output — reads
  the corpus with `System.Text.Json`, and drives `DeliveryApplication.obligationsFromComments` once per
  entry.
- PD-003 [AC-002] [FR-005] complete: The F# consumer asserts BOTH halves of the item's criterion 5:
  `declares` must yield `Ok` with exactly one obligation whose id is the entry name, and `inert` must
  yield `Error` whose reason names the leading-line rule. Asserting only `Ok`/`Error` would let the
  named-ness regress into silent invisibility, which is the `#2544` failure mode this row must not
  re-open.
- PD-004 [AC-001] [FR-001] complete: Add the Python consumer to `tests/kit-published-coherence/run.sh`,
  driving the gate's real CLI entry point `--obligation-arm` once per corpus entry via the existing
  `obl`/`must_pass`/`must_fail` helpers, so the leg exercises `obligation_declarations` rather than
  `_leading_line` in isolation. `declares` entries assert the arm names the obligation; `inert` entries
  assert it reports carrying no `fsgg:delivery-obligation`.
- PD-005 [AC-004] [FR-004] complete: Give each consumer its own independent non-vacuity floor: a stated
  literal entry count, an assertion that both verdict classes are present, and an assertion that the
  number of entries actually executed equals the number read. `.github#2534` measured an empty-corpus
  green and `.github#1768` measured 157 passing legs while the script was dying mid-run, so a computed
  count would let the corpus shrink silently. On the shell side this folds into the existing
  `EXPECTED_LEGS` discipline and its accounting comment.
- PD-006 [AC-003] [FR-003] complete: Declare `tests/delivery-leading-line/**` in BOTH `paths:` copies of
  `.github/workflows/coord-engine.yml`. The two copies must stay identical or `paths-coherence` reds
  (`.github#880`). `kit-published-coherence.yml` is left alone: it is deliberately unfiltered on
  `pull_request` (`.github#1597`) and already starts on every PR.
- PD-007 [AC-006] [FR-006] complete: Record DEC-001's chosen shape and its three rejections in the
  corpus file's own header and in both implementations' comments, so a reader who arrives at either
  implementation is pointed at the artifact that now enforces what the prose used to only assert
  (`check-kit-published-coherence.py:459`, `_leading_line`'s docstring, and the comment above
  `DeliveryApplication.leadingLine`).
- PD-008 [AC-007] [FR-007] complete: Leave the `#2544` round-1 engine-only legs in place unchanged —
  bystander destruction, the indented declaration+receipt pair reading `Verified`, and the conditional
  advice. They are multi-comment engine behaviours a one-body-one-verdict corpus cannot express, so
  retiring them would trade coverage for tidiness.
- PD-009 [AC-001] [AC-002] [FR-001] [FR-002] complete: Produce gate-inversion evidence in BOTH
  directions before handoff, as `.github#2551` requires: mutate the F# limit plus the corpus and observe
  the shell fixture red; mutate the Python limit plus the corpus and observe the F# suite red; and
  revert both. A check that has only ever been green is not evidence.

## Contract Impact
- PC-001 [PD-001] dataContract: `tests/delivery-leading-line/corpus.json` becomes a contract surface —
  the single statement of the leading-line boundary, graded by two consumers in two languages. Its
  entry count is asserted by each consumer separately, so adding or removing an entry is a deliberate
  two-file edit.
- PC-002 [PD-006] workflowTrigger: `.github/workflows/coord-engine.yml` gains one `paths:` entry in each
  of its two copies. Compatibility-preserving: it widens the trigger set and narrows nothing.
- PC-003 [PD-003] behaviourPreserving: No runtime behaviour of `scripts/fsgg-coord`, the published kit,
  or `check-kit-published-coherence.py`'s verdicts changes. What declares is byte-for-byte what declared
  before.

## Verification Obligations
- VO-001 [PD-001] [PC-003] semanticTest: Reproduce the `.github#2544` 18-shape agreement through BOTH
  real entry points BEFORE any edit — `dotnet fsi` against the shared checkout's Release assemblies for
  the engine, and an `importlib` load of the gate for `obligation_declarations` — and record it as the
  baseline.
- VO-002 [PD-002] [PD-004] [PC-001] semanticTest: Re-run the same measurement after the change and
  assert the verdicts are identical to the recorded pre-change baseline: 7 declare, 11 inert, every inert case an
  `Error` naming the leading-line rule.
- VO-003 [PD-009] [PC-001] gateInversion: Mutate the F# limit to `>= 8` together with the corpus
  entries that move, leave Python untouched, run `tests/kit-published-coherence/run.sh`, and record the
  exact red. Then the mirror image: mutate the Python limit plus the corpus, leave F# untouched, run the
  Cli test suite, and record the exact red. Revert both and re-confirm green.
- VO-004 [PD-005] [PC-001] gateInversion: Delete the corpus, then truncate it below its declared entry
  count, and record that EACH consumer fails rather than passing over a smaller corpus than it claims.
- VO-005 [PD-006] [PC-002] reachability: Evidence that a change confined to the corpus starts both
  workflows — the declared `paths:` entry in both copies of `coord-engine.yml`, the absence of any
  `paths:` filter on `kit-published-coherence.yml`'s `pull_request` trigger, and the live check runs on
  the PR.
- VO-006 [PD-008] [PC-003] semanticTest: The full `FS.GG.Coord.Cli.Tests` suite and the full
  `kit-published-coherence` fixture pass, including the untouched `#2544` round-1 engine-only legs and
  the fixture's `EXPECTED_LEGS` floor.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-002] additiveOnly: There is nothing to migrate. The corpus is new, so no reader can hold a
  previous version of it; the workflow change only widens a trigger set; and no marker grammar, wire
  shape, or published artifact moves. A checkout that predates this change and one that follows it
  classify every comment body identically, which is the property VO-002 measures rather than asserts.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/2563-cross-language-indent-limit/work-model.json` and
  `analysis.json` refresh from these plan sources; no other generated view is affected. In particular
  the corpus is AUTHORED, not generated — nothing derives it from either implementation, and neither
  implementation derives anything from it. That is deliberate: a generated coupling artifact would be a
  stale-artifact surface of the `.github#2551` kind, which is the specific property that disqualified
  the shared-generated-constant shape in DEC-001.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2563-cross-language-indent-limit`.
