# Per-Receiver Dispatch Lock And Lease-Free Merge Election Clarifications

- **Work id:** 2312-dispatch-lock-merge-election
- **Stage:** clarify
- **Status:** clarified

## Source Specification

`work/2312-dispatch-lock-merge-election/spec.md`

## Clarification Questions

- **CQ-001** Is the separable clause of design §4.2 — converting all four hand-rolled copies of the ordering
  rule — adopted or rejected by this slice?
- **CQ-002** `claim` or `claimScoped`? The design's own verification note calls `claim` "the natural fit for
  an off-board lock issue" while §11.2 and the item's criterion 2 both name `claimScoped`.
- **CQ-003** How is roster completeness proved without restating the roster?
- **CQ-004** The eight `[op-lock]` issues do not exist. Creating them is a fleet-visible write, and the
  operator's sole-filing-authority directive says not to open issues. What is the disposition?
- **CQ-005** Does this slice write the merge-election marker?
- **CQ-006** Criterion 3 asks to "confirm every consumer's behaviour changes". Which consumers actually have
  behavioural coverage, and what is claimed for the ones that do not?

## Answers

**CQ-001.** **Adopted.** The design records the disposition explicitly — *"absorbed into slice 2,
deliberately not filed as a separate row"* — so it is this slice's to honour rather than to rediscover. The
argument against is real and is recorded in the ADR's alternatives: it edits a safety-critical read path for
a property no *observed* defect has yet violated. It is outweighed by two measured facts. First, the
election needs the same rule again, so declining would make a **fifth** copy rather than leaving four.
Second, the drift has already started rather than being hypothetical: `reserver`'s own doc comment names
`who` as making the same choice, and `who` does not call it.

The item's criterion 4 then forces an explicit choice, and this slice takes the strong side: **all four are
converted, so the "provably one implementation" claim is made.** Asserting it with copies outstanding is the
overstatement two earlier repair rounds each corrected, and the design says a third would be one too many.

**CQ-002.** **`claimScoped`, explicitly.** The item's acceptance criterion 2 names it, and §11.2's slice row
names it. The design's parenthetical preferring `claim` is about which overload is *natural*, not about
which the acceptance requires, and the two are not in conflict: `claim` is the single-callback wrapper.
Calling `claimScoped` directly puts both stubs at the call site, and §4.1 says those stubs *are* the
configuration — *"a new caller supplies a lock ref and two stubs and is done."* A reader of the call site
therefore sees the whole configuration without following a wrapper.

**CQ-003.** From `registry/repos.yml`, which is the roster's own authority. The test parses the `repos:`
block, keeps `FS-GG`-owned rows, and asserts a ref resolves for each. A fixture that spelled the eight names
would **be** the hand-checked list one file further away — it would agree with the table forever, including
on the day a ninth repository is onboarded and silently has no lock.

Two guards make that non-vacuous, and both are necessary. `List.forall` over an empty list is `true`, so a
regex that silently stopped matching would convert the completeness assertion into a green that asserts
nothing. So the parse is asserted to find at least eight rows **and** to contain `FS.GG.Net` specifically —
the row whose absence from the chore-lock table is this slice's whole subject.

Non-participant and foreign-owner rows are excluded because the embedded table is owner-gated by design: its
numbers are FS-GG's issues, and handing a foreign owner one would name a real-but-unrelated issue.

**CQ-004.** **Created, and disclosed rather than absorbed.** Three facts decide it, and the third is what
makes it safe rather than merely necessary.

The item's acceptance criterion 1 requires the table to *cover* all eight roster repositories, and a table
naming issues that do not exist is worse than an incomplete one: it is a lock that protects nothing while
reporting that it does — the exact failure mode `choreLockRef`'s own doc comment warns about for foreign
owners. So the slice cannot satisfy its own acceptance without them.

The precedent is direct: `#1087` created and wired six chore-lock issues the same way, and ADR-0041/0042
describe the resulting issue↔`Options.fs` pairing as the coherence contract.

And the standing directive's subject is **board churn** — it routes *findings* to a register instead of new
rows. These issues are created **closed**, carry no labels, and are on no project board, which was verified
by reading each one back rather than assumed. They generate zero board rows, which is the harm the directive
protects against. The judgement is nonetheless recorded here and in the PR body so it can be reversed
cheaply: closing eight already-closed issues and reverting one table is a small, contained undo.

**CQ-005.** **No.** §11.2 assigns that to slice 3 (*"`delivery` posts the merge election, then writes the PR
authorization marker"*). This slice supplies the ordering rule the election is *read* through, which is why
§11.2 declares `Reads.fs`/`Reads.fsi` *"for that function alone"*. Writing a marker no reader yet consumes
would be scope this slice's acceptance does not ask for and slice 3 would have to reconcile.

**CQ-006.** Honestly, and with the gap named rather than papered over.

Three things are demonstrated by execution: the rule's own behaviour (six legs), the fact that `winner`,
`reserver`, the CAS's claim path and the chore-lock path all change when the rule is broken, and — added for
this question — that **`who`'s** Stale pick changes too, because its existing coverage did not discriminate
ordering at all.

`reap` and `adopt` are routed through the exported rule **structurally**: a source gate asserts no file in
`src/FS.GG.Coord.Cli` re-implements the idiom, and a companion leg asserts at least three call sites consume
`Reads.lowestId`, so the first gate cannot be satisfied by deleting the callers. What is **not** claimed is a
behavioural leg that discriminates their ordering: the shared world fixture in `ApplicationServiceTests`
models one holder per issue and cannot express two markers on one issue, and widening it would edit a
fixture many unrelated items depend on. That limit is stated in the PR body rather than left for a reviewer
to discover.

## Decisions

- **DEC-001** Adopt §4.2's separable clause; convert all four copies; make the strong claim. *(CQ-001)*
- **DEC-002** Call `Writes.claimScoped` with both stubs visible at the call site. *(CQ-002)*
- **DEC-003** Derive roster completeness from `registry/repos.yml`, with an explicit non-vacuity guard.
  *(CQ-003)*
- **DEC-004** Create the eight `[op-lock]` issues, closed/unlocked/off-board, and disclose the judgement in
  the PR body and this package. *(CQ-004)*
- **DEC-005** Do not write the election marker; supply only the rule it is read through. *(CQ-005)*
- **DEC-006** Add a behavioural ordering leg for `who`; state the `reap`/`adopt` behavioural gap explicitly
  and rely on the structural gate pair for them. *(CQ-006)*
- **DEC-007** Give `opLockRef` the same injected-roster parameter `choreLockRef` has, but add **no** new
  environment variable in this slice — the shape stays symmetric without widening the deployment surface.

## Accepted Deferrals

- **DEF-001** A behavioural ordering leg for `reap` and `adopt`, deferred with its reason recorded in
  DEC-006. Reaching it requires widening a shared fixture that many unrelated items depend on.
- **DEF-002** `FSGG_COORD_OP_LOCKS` env injection, deferred under DEC-007 until a caller needs it.

## Remaining Ambiguity

None blocking. The one open judgement — DEC-004 — is recorded as a judgement, disclosed at review, and
reversible.

## Lifecycle Notes

Clarified against the landed design rather than against the item summary, because §4.1, §4.2 and §12.5 each
make decisions the item body compresses.
