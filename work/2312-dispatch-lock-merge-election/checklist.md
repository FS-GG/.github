# Per-Receiver Dispatch Lock And Lease-Free Merge Election Checklist

- **Work id:** 2312-dispatch-lock-merge-election
- **Stage:** checklist
- **Status:** checklistReady (revision 2 — reopened 2026-08-17)

## Source Specification

`work/2312-dispatch-lock-merge-election/spec.md`

## Source Clarifications

`work/2312-dispatch-lock-merge-election/clarifications.md`

## Source Snapshot

Design §3, §4.1, §4.2, §6.3, §11.2, §11.3, §12.5 read in full before any edit. Slice 1's landed
`Operation.fsi` read for the vocabulary this slice's election is keyed on.

## Checklist Items

- **CR-001** The eight roster repositories are enumerated from `registry/repos.yml`, not from memory.
- **CR-002** Every acquire path refuses when no lock ref resolves, and refuses before spending a request.
- **CR-003** The CAS write path is untouched — no new function, prefix, field or parameter.
- **CR-004** The ordering rule has exactly one implementation, and every consumer calls it.
- **CR-005** Each added gate has recorded inversion evidence: the exact mutation and the observed red.
- **CR-006** The eight lock issues are closed, unlocked, unlabelled and off-board, verified by reading back.
- **CR-007** The ADR carries both ends of both amendment links and passes `adr-coherence`.
- **CR-008** Every claim in the PR body is either verified with a command or marked `unverified`.

## Review Results

- CR-001 — met. Test derives the roster and reds if the parse finds fewer than eight rows.
- CR-002 — met. `NoLockRef` arm asserted against an `unreachable` transport (0 REST, 0 GraphQL).
- CR-003 — met. `Writes.fs`/`Writes.fsi` are absent from the diff; the CAS's own suites pass unmodified.
- CR-004 — met for all four sites. Consumer *behaviour* coverage is partial and named (DEF-001), and the
  structural gate that stands in for it was found evadable at round-1 review and repaired (CQ-007).
- CR-005 — met, and STRENGTHENED at round-1 review. Six inversions originally; a seventh (the critic's
  `_.Id` shorthand evasion) proved the CLI-layer gate did not bind and is now reproduced as evidence, and
  two further inversions prove the new binding leg fails in both directions — narrowing the regex reds its
  match half, over-widening it to `sortByDescending` reds its no-match half. Nine in total.
- CR-006 — met. All eight read back `state=closed locked=false labels=0`, `projectItems` empty.
- CR-007 — met. `check-adr-coherence.py` OK; fixture 17/17.
- CR-008 — carried into the PR body and the worker's report.

## Reopen Checklist — 2026-08-17

- **CR-009** — met. The reopen's finding was RE-MEASURED on this branch before any edit rather than taken
  from the analyst's comment: `OpLock.acquire` had 0 production call sites and 3 in tests, and the control
  (`Chores.offerWithLifecycle`) had real production sites, so the instrument could report both answers.
- **CR-010** — met. A second finding beyond the reopen comment: `OpLock.release` was absent from
  `Client.fsi` and therefore private, unreachable by any caller. Recorded in the spec's Reopen section and
  in ADR-0075 §5.
- **CR-011** — met. Every gate added by revision 2 carries a recorded inversion: nine legs, each naming the
  exact mutation, the artifact whose SHA-256 moved, and the named test observed red. Written to
  `readiness/2312-dispatch-lock-merge-election/inversion-evidence.json` by
  `tests/FS.GG.Coord.Cli.Tests/inversions-2312.py`.
- **CR-012** — met. One of those nine is a CONTROL that must red exactly ONE named leg and leave the other
  fifteen green; it did. Without it, "the gate reds" is unfalsifiable.
- **CR-013** — met, as a MEASUREMENT that contradicted this checklist's own first attempt. The inversion
  harness originally verified each revert by requiring the rebuilt artifact to hash back to its baseline.
  That assertion FAILED on byte-identical source: a local Release build of this tree is not byte-reproducible
  (`fsgg-coord-engine.dll` alternated between two hashes). The revert is therefore verified on source bytes
  and on the suite, and the hashes are recorded as observations. Reported as a finding-packet candidate.
- **CR-014** — met. `src/FS.GG.Coord.Cli/Options.fs` and `Options.fsi`, two of this row's declared `Paths:`,
  no longer exist — they moved to `src/FS.GG.Coord.Cli.Kernel/`. The declaration was widened (verdict
  `disjoint`, no collisions) rather than assumed, and the two dead tokens are reported to the host rather
  than silently rewritten.
- **CR-015** — met. `tests/receiver-validate/run.sh:15` states in a comment that "slice 2 landed
  `Client.OpLock.acquire` with no reachable caller, so no dispatch grant can exist". Revision 2 makes that
  sentence false. It is a comment, not an assertion, so the fixture stays green (74/74); the file is outside
  this row's lane and is reported to the host rather than edited.

## Accepted Deferrals

Both deferrals are recorded in `clarifications.md` (DEF-001, DEF-002) with their dispositions in `tasks.yml`.

## Blocking Findings

None.

## Advisory Notes

`FS.GG.SDD`'s operation-lock number (878) coincides with `FS.GG.Rendering`'s chore-lock number (878). They
are different issues in different repositories and nothing compares them, because a `Ref` carries its repo.
Noted in the table so a later reader does not "fix" one of them.

## Lifecycle Notes

The checklist was reviewed after implementation rather than before, because two of its items (CR-005, CR-006)
are statements about evidence that only exists once the work is done.
