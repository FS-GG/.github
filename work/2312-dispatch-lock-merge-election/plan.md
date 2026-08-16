# Per-Receiver Dispatch Lock And Lease-Free Merge Election Plan

- **Work id:** 2312-dispatch-lock-merge-election
- **Stage:** plan
- **Status:** planned

## Source Snapshot

`spec.md`, `clarifications.md`, `checklist.md` of this package, plus the landed design's §4.1/§4.2/§11.2.

## Plan Scope

Five edits and one operational step, ordered so that each is verifiable before the next depends on it:

1. `Reads.fs`/`Reads.fsi` — add `lowestId`; re-express `winner` and `reserver` through it.
2. `Client.fs` — convert `who`, `reap` and `adopt` onto it, each keeping its own arm logic.
3. Create the eight `[op-lock]` issues; verify each closed/unlocked/off-board.
4. `Options.fs`/`Options.fsi` — add `opLockNumbers` and `opLockRef`.
5. `Client.fs` — add `OpLock`: lease, typed refusal, `acquire`, `release`.
6. `docs/adr/` — ADR-0075 plus both reciprocal ends and two README tables.

## Plan Decisions

- **PD-001** `lowestId` names the RULE, not a role, because four callers with four different roles share it.
- **PD-002** `winner` is re-expressed as `lowestId` composed with the staleness filter, so their
  relationship is stated in code rather than in a comment.
- **PD-003** Each converted site keeps its own surrounding arm logic. The shared thing is the ordering, not
  `reserver` — substituting `reserver` would hand `reap` a live winner.
- **PD-004** `OpLock` is a nested module in `Client.fs` rather than a new file, because `Client.fs` is the
  declared path and a new file would need an fsproj compile-order edit the item did not ask for.
- **PD-005** The refusal type is a sum, not `option`, so an unroutable receiver and a busy one are
  distinguishable — they need opposite responses.
- **PD-006** `Stolen` is not folded into the success arm. It is unreachable under `RefuseLiveHolder`, and if
  it ever arrives the force policy has changed underneath this composition's argument.
- **PD-007** No new CLI verb, so `renderCommandContract` is byte-identical and `CommandSurfaceTests` needs
  no edit.
- **PD-008** The source-scan gate strips comment lines before counting, because `lowestId`'s own doc comment
  quotes the idiom deliberately — count parses, not text.

## Contract Impact

- **PC-001** `Reads.fsi` gains `val lowestId`. Internal library signature; `IsPackable=false`.
- **PC-002** `Options.fsi` gains `val opLockRef`. Same class.

## Verification Obligations

- **VO-001** Both engine suites green, plus the Core suite.
- **VO-002** Every added gate inverted, with the mutation and the observed red recorded.
- **VO-003** The exported rule broken, and the resulting consumer reds enumerated by name.
- **VO-004** The eight lock issues read back from GitHub for state, lock, labels and board membership.
- **VO-005** `adr-coherence` corpus and fixture.
- **VO-006** `scripts/test --list` consulted so the pre-push set is derived from the workflows rather than
  guessed, then run.

## Performance Intent

None. `lowestId` sorts a list that is already sorted in every production caller, which costs nothing on the
list sizes involved (markers on one issue).

## Migration Posture

- **PM-001** No migration. Nothing reads `opLockRef` yet except the tests and `OpLock.acquire`; slices 3–6
  are its callers.
- **PM-002** No rollback step is needed beyond reverting the commit. The eight issues are inert while
  unreferenced.

## Generated View Impact

- **GV-001** None. No registry, manifest or generated projection changes.

## Accepted Deferrals

DEF-001 (behavioural `reap`/`adopt` ordering legs) and DEF-002 (`FSGG_COORD_OP_LOCKS`).

## Planning Findings

None.

## Advisory Notes

`tests/coord-engine-mutation/specs.yml` anchors on exact source text in `Client.fs`; the anchor it uses
(`match Reads.reserver opts.LeaseMinutes markers with`) is a different call site from the three converted
here, and was confirmed still present after the edits.

## Lifecycle Notes

Ordering 3-before-4 is deliberate: the table cannot be honest until the issues it names exist.
