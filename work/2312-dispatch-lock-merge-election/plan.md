# Per-Receiver Dispatch Lock And Lease-Free Merge Election Plan

- **Work id:** 2312-dispatch-lock-merge-election
- **Stage:** plan
- **Status:** planned (revision 2 — reopened 2026-08-17)

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

*Revision 2, on the 2026-08-17 reopen — the production caller the first pass declared out of scope:*

7. `Options.fs`/`Options.fsi` (now under `src/FS.GG.Coord.Cli.Kernel/`) — two `Command` cases, the parser
   arms, and rows in `renderSupport`, the write surface, `commandName` and the usage block.
8. `Client.fs`/`Client.fsi` — `OpLock.held`, `OpLock.roster`, `OpLock.splitReceiver`, `OpLock.parseDispatch`,
   the `NotHeld` refusal arm, the two verb handlers, the `run` dispatch, and the EXPORT of `OpLock.release`.
9. `Program.fs` — both cases routed to `Client.run`.
10. `scripts/fsgg-coord-guards.sh` — `op-lock` classified in `BOARD_WRITES`.
11. The four hand-kept inventories that refuse a silently added verb: `CommandSurfaceTests.surface`,
    `OptionsTests.bareRender`, and `ApplicationServiceTests`' `sweptArms` plus its `runJsonArm` dispatch.

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
- **PD-007** ~~No new CLI verb~~ — **REVERSED on the reopen.** The claim it rested on (that the item asked
  for none) was true of the item text and false of the design: there was never going to be a caller of this
  lock that was not a verb, because the broker is a workflow and the executor is a shell. `renderCommandContract`
  gains two rows and four hand-kept inventories cost a line each, which is what those inventories are for.
- **PD-009** TWO `Command` cases under one `op-lock` namespace, not one case with an `Args`-read subcommand.
  `room open`'s precedent, and it buys the same two things: `commandName` can say which verb ran, and an
  unknown third word is named and refused rather than swallowed into `acquire`'s four positionals.
- **PD-010** POSITIONAL arguments, no new flags. The four are `Operation.compose`'s own, in its own order,
  so the argv reads as the key it composes; and adding four flags would touch the flag-scope table, the
  emitted flag surface and the flag-narrative gate for no gain in clarity.
- **PD-011** The `dispatch:` prefix is DERIVED as `Operation.wire (Operation.Dispatch "")`, never typed.
  Typing it would be a second copy of the wire vocabulary in the CLI layer — §12.5's forbidden second copy,
  which slice 3 declined to write for the ordering rule.
- **PD-012** `OpLock.held` re-obtains the capability through `Writes.verifyHeld`, not through
  `Reads.lowestId`. Release DELETES, and `verifyHeld` is the only door to a `Held` that applies `claim`'s
  twin and impersonation predicates.
- **PD-013** `parseChoreLocks` is REUSED to read `FSGG_COORD_OP_LOCKS`. Its name is chore-flavoured and its
  behaviour is not: it is a comma-separated `owner/repo#n` reader that drops an unparseable token rather
  than throwing, which is this roster's grammar and fail-closed polarity exactly. A second parser for a
  better name is #485's defect bought with a word.
- **PD-008** The source-scan gate strips comment lines before counting, because `lowestId`'s own doc comment
  quotes the idiom deliberately — count parses, not text.

## Contract Impact

- **PC-001** `Reads.fsi` gains `val lowestId`. Internal library signature; `IsPackable=false`.
- **PC-002** `Options.fsi` gains `val opLockRef`. Same class.
- **PC-003** *(revision 2)* `renderCommandContract` gains two rows, `op-lock acquire` and `op-lock release`,
  both `writes: always`, both admitting `--json`/`--text`. NO new flag. The one in-repository consumer of
  that surface, `scripts/fsgg-coord-guards.sh`, is updated in the same change and
  `tests/coord-engine-parity/shim.sh` §3b holds the two in bijection.
- **PC-004** *(revision 2)* `Client.fsi` gains `val held`, `val release`, `val roster`, `val LeaseMinutes`,
  the `NotHeld` refusal case, and the two verb handlers. `release` is an EXPORT of an existing binding that
  was private by omission, not a new function.

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

- **PM-001** ~~No migration. Nothing reads `opLockRef` yet except the tests and `OpLock.acquire`; slices 3–6
  are its callers.~~ **This sentence is the defect, written down at planning time.** "Slices 3–6 are its
  callers" was a forecast, not a fact, and slice 5 landed by transcribing the lock read into inline Python
  rather than consuming anything this row exported. Revision 2 supplies the caller here.
- **PM-003** *(revision 2)* Still no migration. Both verbs are additive; no existing invocation changes
  behaviour, and `FSGG_COORD_OP_LOCKS` unset is the default FS-GG deployment.
- **PM-002** No rollback step is needed beyond reverting the commit. The eight issues are inert while
  unreferenced.

## Generated View Impact

- **GV-001** None. No registry, manifest or generated projection changes.

## Accepted Deferrals

DEF-001 (behavioural `reap`/`adopt` ordering legs) remains accepted and untouched by revision 2.

**DEF-002 is DISCHARGED by revision 2.** It deferred `FSGG_COORD_OP_LOCKS` on the reasoning that the
variable "is not added until a caller needs it". A caller now exists, and leaving it at `[]` would have
reproduced this row's own defect one level down: a documented injection point no production path can reach.

## Planning Findings

None.

## Advisory Notes

`tests/coord-engine-mutation/specs.yml` anchors on exact source text in `Client.fs`; the anchor it uses
(`match Reads.reserver opts.LeaseMinutes markers with`) is a different call site from the three converted
here, and was confirmed still present after the edits.

## Lifecycle Notes

Ordering 3-before-4 is deliberate: the table cannot be honest until the issues it names exist.
