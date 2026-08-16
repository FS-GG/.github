# Per-Receiver Dispatch Lock And Lease-Free Merge Election Checklist

- **Work id:** 2312-dispatch-lock-merge-election
- **Stage:** checklist
- **Status:** checklistReady

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
