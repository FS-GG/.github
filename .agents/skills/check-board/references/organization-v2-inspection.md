# Organization Coordination V2 inspection

Apply this route only to an explicitly selected FS-GG organization planning scope. Product boards,
local-only workspaces and separately selected historical Project 1 scopes retain their own bindings.
Neither a title match nor an organization ID found in a product tree selects this route.

## Adoption gate

Source preparation is not a live consumer switch. Root must first record protected source and
actual executable custody, the exact successor population/binding, complete four-target import and
planning-field readback, an actual qualified four-target inspection, and its selected consumer
adoption decision in the [canonical carryover evidence](https://github.com/FS-GG/.github/blob/main/docs/coordination/board-v2-import-preparation.md#four-target-successor-and-consumer-adoption--source-preparation).
The successor contains SDD#928, Templates#441, .github#3010 and .github#3009, with their exact native
node IDs. SDD#935 stays unapproved; an observed membership or native child is not selection.
An original-three pilot receipt, a prepared manifest, local fixture or synthetic report does not
qualify this successor. No adoption decision is recorded by this prepared contract.

Once root selects adoption for this scope, invoke its independently authenticated protected
`fsgg-coord-engine` executable, with the reviewed private binding and a new output path:

```sh
fsgg-coord-engine board-v2 inspect --binding-file reviewed-binding.json --report-file new-inspection.json
```

The command is structurally read-only. It performs fixed source/native/planning queries and complete
bounded native dependency reads, including empty snapshots; stale early source branches retain
incomplete dependency/plan coverage explicitly. It sends no mutation, retry or claim and never takes
the refresh writer lock. The optional `--previous-report-file` accepts historical refresh/v2 evidence
for display only. Use the selected artifact directly until a separately qualified publication and
receiver installs this command; an older shared shim or package is not assumed to support it.

## Read the result before selecting work

Require `fsgg.coord.board-v2-inspection/1`, exact organization/project/native cohort, repository scope,
field IDs/options, adapter and constructor provenance, protected population revision/blob, and actual
item/read coverage. Validate against root's selected binding, not values copied from an untrusted
report. `selected: null`, a population gap, a failed item/planning read, incomplete dependencies or
Stale/Unknown source currentness is unavailable evidence. Exit 3 reports such gaps; exit 0 still
supplies neither delivery acceptance nor scheduling permission. Never call this an empty backlog,
fall back to Project 1, silently reuse previous freshness or block otherwise valid source delivery.

Read human Status, Track and Roadmap independently of Observation/source currentness. Verified is
observation health, not Ready or Done. Closed native issue plus an unaccepted outcome and human Done
with missing acceptance are discrepancies. Owning plans retain body/source/publication prerequisites;
an empty native blocked_by list does not clear those separate prerequisites. Preserve owner edits.

The existing integrator combines only selected candidates with current open PRs, disjoint touch sets,
worker/CI capacity and owning-plan evidence. CLI candidate PR/touch-set/slot facts default Unknown.
Before dispatch, run the existing single batch intake authorization for the selected native issue refs,
check actual author/repository permission and continue the existing routine/native-delivery route.
An inspection row, human Ready or candidate entry cannot grant intake, merge, effect or publication
permission. Work without a represented issue continues through its owning roadmap; do not invent a row.

For this admitted organization scope, use inspection instead of legacy reconcile/apply/flush,
Class/Phase claim ranking, batch/driver events or automatic status repair. Do not send V2 planning
items into legacy lifecycle adapters. Scheduling remains the current integrator's bounded decision;
no second scheduler, durable ledger or generic queue is introduced. Report selected scope, gaps,
owner disagreements and the actual selection; no worker dispatch is required merely to prove a dry-run.

Project 1 is a retained legacy reference. Its immutable exact-project1 behavior and separately scoped
scheduled archive remain unchanged. The hosted V2 refresh job stays dormant and unenrolled. This
organization contract changes no product board ID, template, provider/lifecycle default or credential.
