---
schemaVersion: 1
workId: 2730-doc-comments-to-signatures
title: Doc Comments To Signatures
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2730-doc-comments-to-signatures/spec.md
sourceClarifications: work/2730-doc-comments-to-signatures/clarifications.md
sourceChecklist: work/2730-doc-comments-to-signatures/checklist.md
publicOrToolFacingImpact: true
---

# Doc Comments To Signatures Plan

Prose status: planned

## Source Snapshot
- spec: work/2730-doc-comments-to-signatures/spec.md sha256:d2c1fe3260ad43d46896c6f720ab2ba86d0b2a3a0ee4eca4222d84bac1a01fb9 schemaVersion:1
- clarifications: work/2730-doc-comments-to-signatures/clarifications.md sha256:182664054a3c65d66fae7d748388ed60e24ece7fcee4f939d5ede5bf3f5a8852 schemaVersion:1
- checklist: work/2730-doc-comments-to-signatures/checklist.md sha256:68b192fb0c1c6381ab61396285a75fa1d6f21e6feed6d4690f9b0671e4e86c42 schemaVersion:1

## Plan Scope
- Work item 2730-doc-comments-to-signatures is planned from the current specification, clarification, and checklist facts.
- Requirement count: 9.
- Clarification decision count: 7.
- Checklist result count: 11.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Sweep all 37 implementation files under `src/FS.GG.Coord.Core` and `src/FS.GG.Coord.GitHub` that have a sibling `.fsi` — 2,970 doc-comment lines in 459 blocks, counted by the gate's own lexer at `0ddd4b88` — to zero XML documentation comments, one file per commit-sized unit so the diff is readable file by file.
- PD-002 [AC-002] [AC-003] [FR-002] complete: Classify every block by the audience test in DEC-001, exploiting one structural shortcut that removes judgement rather than guessing at it — a block attached to a declaration the `.fsi` does not export cannot be contract prose, because no caller can name that declaration, so all 185 such blocks demote to `//` by derivation; the remaining 274 are read against the sibling signature and each is moved, demoted, or dropped as a duplicate, with the dropped set enumerated in the pull-request body.
- PD-003 [AC-005] [FR-003] complete: Build `scripts/check-signature-doc-siting.py` as a pure-stdlib, no-network gate whose subject is every `*.fs` under `<root>/src/` outside `obj/` and `bin/` that has a sibling `.fsi`, reporting `<path>:<line>` for each XML documentation comment it finds and naming the reason in one sentence.
- PD-004 [AC-006] [AC-007] [FR-004] complete: Give the gate three outcomes and no fourth — exit 0 when every file matches its baseline exactly, exit 1 for a finding, exit 3 for **no verdict** when discovery yields zero `.fs` files under `src/`, zero files with a sibling `.fsi`, or an unreadable or malformed baseline — and print the discovered file/subject/hit counts on every run, including green ones, so a subject that silently shrank is visible in the log rather than inferred from a pass.
- PD-005 [AC-008] [FR-005] complete: Ship `tests/signature-doc-siting/baseline.txt` as `<count> <path>` lines with exact-match semantics in both directions, carrying only the 12 `src/FS.GG.Coord.Cli` files and their 943 lines, with a header stating why they are there and what decrements them.
- PD-006 [AC-004] [FR-006] complete: Capture `bin/Release/net10.0/FS.GG.Coord.Core.xml` and `FS.GG.Coord.GitHub.xml` before the sweep, rebuild after it, and assert one-directional containment — every `<member name=…>` and every documentation text node present before is present after — since an undocumented member emits no element at all and a two-directional equality would forbid the gain this work exists to make.
- PD-007 [AC-009] [AC-010] [FR-007] complete: Make the gate lex rather than grep — track `(* … *)` nesting and `"`, `@"`, `"""` literals, require exactly three slashes with a fourth disqualifying, and accept a doc comment anywhere on a line rather than only line-leading — and restrict the subject to files with a sibling `.fsi` so a `.fs` whose `///` the compiler keeps is never reported.
- PD-008 [AC-011] [FR-008] complete: Invert every assertion this work adds at authoring time — the gate's three exit codes, each fixture leg, and the two workflow classification legs — and record the exact mutation and the observed red in the pull-request body rather than leaving the critic to discover them.
- PD-009 [AC-012] [FR-009] complete: Make `tests/signature-doc-siting/run.sh` exercise the gate over throwaway synthetic trees for every leg the real tree cannot express — a `///` inside a block comment, inside each of the three string forms, a `////` line, a `.fs` with no sibling — and then run it over the **real** tree twice, once as shipped and once with an offender planted into an already-swept file, so a fixture that passes on synthetic strings while the shipped baseline rots is not a reachable state.
- PD-010 [DEC-008] acceptedDeferral: Accepted deferral DEC-008 remains visible to task generation.
- PD-011 [DEC-009] acceptedDeferral: Accepted deferral DEC-009 remains visible to task generation.
- PD-012 [CR-010] acceptedDeferral: Accepted deferral CR-010 remains visible to task generation.
- PD-013 [CR-011] acceptedDeferral: Accepted deferral CR-011 remains visible to task generation.

## Contract Impact
- PC-001 [PD-001] command report: No `.fsi` is narrowed, widened, or re-typed and no executable line moves, so `FS.GG.Coord.Core` and `FS.GG.Coord.GitHub` keep their exact public surface; the only compiled artifact that may differ is the generated XML documentation, and only by gaining. Neither project is a `kit:` source in `registry/repos.yml`, so no published payload and no coherent-set version follow.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: `dotnet build src/FS.GG.Coord.GitHub -c Release` (which builds Core transitively) is green with zero warnings; `dotnet test` over the three coordination test projects is green; `scripts/check-signature-doc-siting.py --root .` exits 0 over the swept tree; `bash tests/signature-doc-siting/run.sh` is green; the XML containment comparison of PD-006 passes in both projects; and the pull request carries the recorded red for every inverted assertion.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: The baseline file is the only durable state this work introduces, and it is diagnose-only in both directions — a count that disagrees with the tree reds and prints the number to write rather than rewriting it, so the file cannot drift into a list nobody maintains.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/2730-doc-comments-to-signatures/work-model.json` and `analysis.json` refresh from the current plan sources; no other generated view in the repository reads doc comments, and the projections gate's generated regions are emitted from `Protocol.fs`'s data rather than from its comments, so a demoted comment cannot move a projection.

## Accepted Deferrals
- DEC-008 acceptedDeferral: `src/FS.GG.Coord.Cli`'s 943 doc-comment lines across 12 files stay where they are and enter the gate baseline as exact counts; the extraction programme (`.github#2724`, `.github#2731` onward) decrements them as each module gains its `.fsi`.
- DEC-009 acceptedDeferral: Adding `signature-doc-siting` to `main`'s required status contexts stays with the repository owner; this work ships the gate unrequired, as `pipefail-assertions` did.
- CR-010 acceptedDeferral: Mirrors DEC-008 — the `Cli` residue is recorded rather than swept, and the checklist carries it forward so tasks and evidence keep it visible.
- CR-011 acceptedDeferral: Mirrors DEC-009 — branch protection is out of this work item's reach, and the checklist carries that forward to evidence.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.
- `.github#2724` and `.github#2731` move this work's denominator. Whichever of the three lands second recomputes `baseline.txt`. That is the baseline mechanism working and no `Blocked by` edge is added for it.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2730-doc-comments-to-signatures`.
