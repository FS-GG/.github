# Read-only CLI contract

```sh
fsgg-coord-engine board-v2 inspect --binding-file reviewed-binding.json --report-file new-inspection.json
```

The binding must identify the loaded adapter, protected population blob and exact
selected native cohort. The output is `fsgg.coord.board-v2-inspection/1`. It records
current native issues, complete dependency reads, owning plans and human Status,
Roadmap and Track separately from Observation and source currentness.

An unavailable population produces `selected: null`, a population gap and no
candidates (exit 3). This is unknown coverage. Missing planning values remain null;
Unknown Observation remains Unknown. Native closure or human Done never supplies
outcome acceptance. Open PRs, touch sets, available slots and intake authorization
remain unknown unless supplied independently to the pure candidate function by the
current integrator. Candidates do not dispatch or authorize work.

Inspection performs queries and bounded dependency GETs only. It never sends or
retries mutations, changes board fields, creates projection timestamps or takes the
refresh writer lock. Optional `--previous-report-file` reads a refresh/v2 report
only to retain historical LastVerified; history never proves currentness.


## Programme successor source window

Organization `bindingVersion: 4` requires exact Project 3 identity, `schemaVersion: 2`
and six Status options including In review. Product binding version 3 retains its
separate five-status contract. Historical organization v2 controls grant no successor
selection or fallback. Programme refresh and every writer composition refuse before IO.

The fixed manifest path holds `fsgg.coordination-board-v2-enrollment/v2`: a closed
`target` (owner, organizationId, projectId, projectNumber, schemaVersion), closed
`binding` (recipeRevision, artifactSha256, repositories), and exact selected `items`
(issue, nodeId, roadmap). No issue revision, open state or empty-dependency snapshot
belongs to enrollment. The pinned enrollment blob must still match protected main;
selected plan paths are stable while their current protected-main blobs are observed.

Inspection/v2 adds complete membership reconciliation and actual repository-wide open
PR inventories, including non-enrolled work, native closing-issue associations, head,
files, reviews and checks. Connections drain within existing bounds; denied/partial
reads and final issue/dependency/plan/planning/membership/head changes remain unknown.
Unexpected cards stay visible and unenrolled. No human planning field is written.

This is the first useful source portion of .7/.8, **not .8 completion**. The maintained
`selectActions` and CLI `encodeInspectionWithContext` APIs accept trusted typed current
slice, action-specific prerequisite/intake and attributed resource/reservation facts.
The ordinary CLI has no authenticated supplier for those facts yet and reports the
missing owning integrator/runtime inputs explicitly. It recovers the actual existing
PR/head without claiming continuation eligibility when action prerequisites are unknown.
Native `closingIssuesReferences` supplies an observed association, not the complete
owning-issue Current deliverable, accountable owner and proposed-action join. Routine
whole-outcome PRs may use Related instead of Closes. An absent closing edge therefore
does not prove that no current deliverable exists. The production Current deliverable/
owner/action join remains open with the integrator alongside the other missing suppliers;
new dispatch cannot infer an empty existing-work population from closing edges alone.
With complete current action evidence, continuation preserves that PR/owner without
requiring new-dispatch capacity. New source readiness separately requires fresh
implementation, review, CI and original/uncertain reservation coverage. Canonical
TouchSet rules compare actual current slices/files, never broad parent scope.

Later publication/installed/native acceptance can block activation without blocking
independent source work. PR checks cannot establish those acceptance facts. Selection
never grants dispatch authority. Production prerequisite/resource supplier integration,
installed qualification, migration/Observation writer, UI and consumer cutover remain
open under the owning plan. Source version 0.103 and original operation holds remain.

The focused source and actual CLI tests share an inert transport fixture. They require
hosted CI: no local CLR/native/browser execution was performed in this source window.
