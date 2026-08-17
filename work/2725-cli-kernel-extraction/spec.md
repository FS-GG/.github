---
schemaVersion: 1
workId: 2725-cli-kernel-extraction
title: Cli Kernel Extraction
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Cli Kernel Extraction Specification

Prose status: specified

## User Value

A worker extracting one CLI command family can do so without contending on
`src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj`, because the shared base every family needs lives in its
own project with its own touch-set token.

**And that is the whole case, stated against this work's own interest.** The split is **not**
justified by build time: measured on the row, touching `Client.fs` (11,090 lines) and touching
`Json.fs` (72 lines) both rebuild in ~3.9s. Lane concurrency is the entire justification. If the
boundary drawn here does not create disjoint lanes for `.github#2726`–`#2729`, this work has not
delivered its reason for existing, and every other criterion below passing does not change that.

**The obstacle is not what it was assumed to be.** `.github#2724` measured at `479d185a` that of the
77 surviving exports on `Client`, exactly four are required by production code — `run`, `whoami`,
`followupAudit`, `predicate`, all from `Program.fs` — and the other 73 are held open by
`tests/FS.GG.Coord.Cli.Tests` alone. So what holds `Client` together is not production coupling
between command families; it is that the tests reach into internals. That is why SB-004 and FR-003
are the load-bearing criteria here and not tidiness notes.

## Scope

- SB-001: A new `src/FS.GG.Coord.Cli.Kernel` project carrying a `.fsi` for every module, referenced by
  `FS.GG.Coord.Cli`.
- SB-002: `Json`, `Options`, `Identity`, `RefParsing` and `Render` relocated as whole files, verbatim,
  with their existing signature files. This half is a project move, not an extraction, and is
  deliberately priced lower: each of these files already existed separately and already carried a
  signature before this work began.
- SB-003: One new `Kernel` module extracted from `Client.fs` holding the exit-code vocabulary, the
  `Context` record, the stderr and refusal helpers, worker resolution, the context-bound ref parsers
  and the checkout-scope readers. This is the only extraction; it is where the risk is.
- SB-004: A new `tests/FS.GG.Coord.Cli.Kernel.Tests` project holding the tests that cover the moved
  modules, **moved** out of `tests/FS.GG.Coord.Cli.Tests` rather than duplicated.
- SB-005: A stated and uniformly applied rule for where a relocated doc comment is sited, because a
  signature file discards the implementation file's comments. The rule is stated in full under
  *Documentation Siting Rule* below and is binding on every block this work moves.
- SB-006: Execution-confirmed evidence that the release payload tolerates the two new packed entries.
- SB-008: The namespace `FS.GG.Coord.Cli` is preserved across the assembly boundary, so no consumer of
  a relocated module is re-spelled. .NET permits a namespace to span assemblies; using that is what
  keeps SB-002 a verbatim move and keeps FR-004 cheap to establish.

## Non-Goals

- SB-007: Do not implement later lifecycle commands or Governance enforcement in this specification.
- SB-009: The three `let mutable private` forward declarations in `Client.fs` —
  `generatedPathCollector`, `completeDelivery`, `followupAuditContextOverride` — are **not** touched.
  They are the evidence motivating the extraction programme, and `.github#2727` owns replacing
  `completeDelivery` with a real dependency inversion. Beginning that here would turn a
  compiler-checked move into a design change.
- SB-010: A general documentation sweep of `Client.fs` is **not** in scope. `.github#2730` owns
  detecting and repairing the discarded-`///` condition across that file and is live in its repair
  phase. Only prose attached to blocks this work relocates is this work's.
- SB-011: No behaviour changes. No verb gains, loses or alters an exit code, an output line, a flag, a
  read or a write. `FS.GG.Coord.Core` and `FS.GG.Coord.GitHub` are not modified at all.
- SB-012: The Kernel is not published as its own NuGet package. It is `IsPackable=false` and ships
  only as a payload entry inside the `FS.GG.Coord.Cli` tool package, exactly as `FS.GG.Coord.Core` and
  `FS.GG.Coord.GitHub` already do.

## Documentation Siting Rule

`Client.fsi` exists as of `.github#2724`, and an F# signature file **discards every `///` comment in
the implementation file it fronts** — 1,714 such lines in `Client.fs`, measured. This work is the
first to move code out from under that signature, so for every block it relocates it decides whether
that block's prose reaches a consumer again or stays discarded. The decision is made once, here, and
applied uniformly:

1. **Consumer-visible prose is preserved byte-for-byte.** Where a relocated declaration has a doc
   comment in `Client.fsi` today, that text — not the implementation file's — is what reaches
   consumers, and it moves verbatim into `Kernel.fsi`. Nothing that documents a binding today stops
   documenting it after this work.
2. **Discarded implementation prose travels with its implementation and stays discarded.** A
   relocated declaration's `///` prose in `Client.fs` moves into `Kernel.fs` unchanged. It is still
   discarded there, which is correct: this work is a move, and silently promoting thousands of lines
   of unreviewed prose into a public signature would be the sweep SB-010 excludes.
3. **Where (1) and (2) are byte-identical, the text is not duplicated.** The `.fsi` copy survives and
   the `.fs` declaration is left bare, because two copies of one paragraph in two files is two texts
   that will drift.
4. **A declaration whose visibility this work changes gains a signature entry authored from its own
   discarded prose.** The bindings that were `private` to `Client` and must become public on the
   Kernel — an assembly boundary has no `private`-to-a-friend — are the one place where prose that was
   discarded starts reaching consumers. That is confined to exactly the declarations whose visibility
   changed, which is a set this work can enumerate and a reviewer can check, and it happens nowhere
   else.
5. **Nothing that is not moving is touched**, including in the files this work moves whole (SB-002),
   which move byte-identical.

## User Stories

- US-001 (P1): As a worker on `.github#2726`–`#2729`, I can extract one command family into its own
  project while another worker extracts a different one, without either of us editing a file the other
  has reserved.
- US-002 (P1): As a reviewer of any later extraction, I can read the Kernel's signature files and see
  the shared surface those extractions depend on, because the compiler holds it rather than the
  implementation happening to expose it.
- US-003 (P1): As a release operator, I know before `.github#2726` proceeds whether adding an assembly
  to the packed tool output is tolerated by the release machinery, because it was confirmed by running
  it rather than by reading it.
- US-004 (P2): As a consumer of the coordination engine, I observe no change whatsoever: every verb
  exits as it did, prints what it did, and reads and writes what it did.

## Acceptance Scenarios

- AC-001 [US-004] [FR-001]: Given the whole repository, when `dotnet build` runs with
  `TreatWarningsAsErrors` on, then it succeeds for every project including the two new ones.
- AC-002 [US-004] [FR-002]: Given the test totals recorded before the change, when the full suite runs
  after it, then the totals reconcile exactly — the count the CLI test project loses is the count the
  Kernel test project gains, and nothing fails.
- AC-003 [US-001] [FR-003]: Given `tests/FS.GG.Coord.Cli.Kernel.Tests`, when its project references
  are read, then it references `FS.GG.Coord.Cli.Kernel` and does **not** reference
  `FS.GG.Coord.Cli` — so the Kernel is proven to stand up without the module it was cut from, which is
  the only mechanical evidence that the cut is real.
- AC-004 [US-004] [FR-004]: Given the exit-contract suite, when it runs unmodified except for the
  module qualifier on the moved literals, then every verb's exit contract is observed unchanged.
- AC-005 [US-003] [FR-005]: Given a clean `dotnet pack` of `FS.GG.Coord.Cli`, when the packed payload
  entries are enumerated and the release saga's own payload comparison is executed against them, then
  `FS.GG.Coord.Cli.Kernel.dll` and `FS.GG.Coord.Cli.Kernel.pdb` are present and tolerated. If they are
  not, that is a blocking finding on its own row before `.github#2726` proceeds, and this work stops
  rather than working around it.
- AC-006 [US-001] [FR-006]: Given the boundary as built, when the file set each of
  `.github#2726`–`#2729` would need to edit is enumerated, then no two of them require editing the
  same file, and none of them requires editing a Kernel file to add a family.
  **AMENDED AT IMPLEMENTATION, AND THE FIRST CONJUNCT IS NOT MET.** Both halves were enumerated by
  execution and they answer differently, so they are recorded separately rather than averaged.
  (i) *"none of them requires editing a Kernel file to add a family"* — **MET**, and compiler-held.
  (ii) *"no two of them require editing the same file"* — **NOT MET**, and **not achievable by any
  module boundary**, which is why this is an amendment to the criterion and not a defect in the
  boundary. Nineteen files are shared. Sixteen are hand-kept per-project enumerations — four gate
  scripts, five workflow trigger lists, two committed `packages.lock.json` files, four gate fixtures and
  the consuming `.fsproj` — which any *n*th project must join wherever the module seam is drawn; a seam
  cannot make a literal list stop being a literal list. The remaining three are the departing family's
  own surface (`Client.fs`, `Client.fsi`, the CLI test project's `.fsproj`), shared until the last
  family leaves. The enumeration is published in the pull-request body per PD-006, and the reproducible
  method for it is stated there.
  (iii) The criterion as authored was therefore unsatisfiable when it was written, and CHK-006's `pass`
  graded its testability and its linkage to AC-006 — not its achievability. Recording it as unmet with
  the measurement is worth more than reporting a boundary that met it, because no boundary can.
  (iv) The **cause** — that per-project registries in this repository are hand-kept enumerations rather
  than derived from the project graph — is a finding on its own row, filed as a packet against
  `.github#2691`. It is deliberately **not** repaired here: repairing it would rewrite sixteen gates
  from inside a row whose subject is one module boundary.
- AC-007 [US-002] [FR-007]: Given every module in the Kernel project, when the project's compile list
  is read, then each `.fs` is preceded by its own `.fsi`, so the Kernel's public surface cannot widen
  except by an edit somebody reviews.
- AC-008 [US-002] [FR-008]: Given the relocated declarations, when their documentation is compared
  before and after, then every doc comment that reached a consumer before still does, with the same
  bytes, and any prose newly reaching a consumer belongs to a declaration whose visibility this work
  changed.

## Functional Requirements

- FR-001: `dotnet build` MUST succeed with `TreatWarningsAsErrors` on for every project in the repository. (Stories: US-004; Acceptance: AC-001)
- FR-002: The full test suite MUST pass with no test lost — pre-change and post-change totals reconcile exactly, and the Kernel test project contributes the tests removed from the CLI test project. (Stories: US-004; Acceptance: AC-002)
- FR-003: `tests/FS.GG.Coord.Cli.Kernel.Tests` MUST reference `FS.GG.Coord.Cli.Kernel` and MUST NOT reference `FS.GG.Coord.Cli`. (Stories: US-001; Acceptance: AC-003)
- FR-004: Every CLI verb's exit contract MUST be unchanged, evidenced by the exit-contract suite passing with no edit other than the module qualifier on the relocated literals. (Stories: US-004; Acceptance: AC-004)
- FR-005: The packed tool payload MUST be produced and its entry set enumerated by execution, confirming the release saga tolerates `FS.GG.Coord.Cli.Kernel.dll` and `.pdb`. (Stories: US-003; Acceptance: AC-005)
- FR-006: The boundary MUST create disjoint lanes for `.github#2726`–`#2729`, demonstrated by enumerating the file set each would edit and showing the sets are pairwise disjoint. (Stories: US-001; Acceptance: AC-006) **AMENDED AT IMPLEMENTATION — see AC-006. The enumeration was performed and the sets are NOT pairwise disjoint: nineteen files are shared, sixteen of them hand-kept per-project registries that no module boundary can remove from the path of an nth project. What FR-006 asked for cannot be demonstrated by any boundary in this repository, so the requirement is recorded UNMET with the measured residue published where the next four rows read it, rather than restated until it reads as discharged. The source and test DIRECTORIES are genuinely disjoint and compiler-held; that is the part of FR-006's intent this boundary does deliver, and it is stated as the narrower claim it is.**
- FR-007: Every module in `src/FS.GG.Coord.Cli.Kernel` MUST be fronted by its own signature file. (Stories: US-002; Acceptance: AC-007)
- FR-008: Relocated documentation MUST be sited by the Documentation Siting Rule above, and the outcome MUST be reported as counts a reviewer can check. (Stories: US-002; Acceptance: AC-008)

## Ambiguities

- AMB-001: Whether `Snapshot` belongs in the Kernel. It is a pure model depending only on `Json`, and
  three test files covering it would then move cleanly — but the row's scope names five modules and
  does not name it, and `Snapshot` is consumed by the scheduling verbs rather than by every family.
- AMB-002: Whether the bindings that were `private` in `Client` should become `public` on the Kernel
  or `internal` with `InternalsVisibleTo`. `internal` would keep the surface narrow; `public` states
  the shared base honestly and is what the four downstream rows will consume.
- AMB-003: Whether `Client` should retain re-export aliases (`let ExitGreen = Kernel.ExitGreen`,
  `type Context = Kernel.Context`) so no call site is re-spelled.

## Public Or Tool-Facing Impact

- This specification is an SDD lifecycle artifact and command-report contract input.
- The packed `FS.GG.Coord.Cli` tool gains two payload entries. Package identity, tool command name,
  version and every CLI surface are unchanged; the added entries are the whole of the tool-facing
  delta, and FR-005 is the criterion that decides whether they are acceptable.
- `Client`'s public surface shrinks by the declarations that move to the Kernel. The four bindings
  production code outside the module actually requires — `run`, `whoami`, `followupAudit`, `predicate`
  — are unaffected.

## Lifecycle Notes

- Next lifecycle action: `fsgg-sdd clarify --work 2725-cli-kernel-extraction`.
