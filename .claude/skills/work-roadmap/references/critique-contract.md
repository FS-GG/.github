# Independent milestone critique gate

Run one bounded qualitative review per milestone after the first green implementation/test/evidence
loop and before verify/ship/PR orchestration. The implementation worker starts a fresh critic with the
milestone text, roadmap path, cycle id, base ref, current head, diff, lifecycle artifacts, tests, and
evidence. Do not give it the worker's conclusions or ask it to approve a predetermined result.

The critic reviews only:

- milestone and acceptance-requirement coverage;
- correctness and regression risk in the diff;
- test quality and missing negative/boundary coverage;
- architectural fit and avoidable coupling;
- sufficiency and accuracy of roadmap completion evidence.

The critic must cite concrete repository evidence. Style preferences and hypothetical concerns without
an observable failure mode are not findings. It may write `reviews/roadmap/<cycle-id>.json` and nothing
else. It must not edit implementation, tests, SDD artifacts, feedback, or roadmap state.

Use `blocker` when the milestone cannot safely ship, `major` when acceptance, correctness, regression
protection, or architecture materially fails, and `minor` for bounded non-acceptance debt. The worker
must resolve every blocker/major finding in the milestone branch. A minor finding may be resolved or
routed to a durable issue or unchecked roadmap item; never bury it in prose.

Allow at most ten numbered worker repair rounds, each followed by confirmation of the exact repaired
head by the same critic. Before routing a repair, validate the ordered commit chain and permit the
repair only when the latest round is less than ten. This count-before-routing gate prevents a failed
tenth confirmation from racing into an eleventh repair. If the tenth confirmation still reports a
blocker/major finding, record the human escalation described below, stop the milestone, and do not
check its roadmap box, merge it, or start round eleven. A human must decide the required action or
change the acceptance boundary before work resumes. Self-approval by the implementation worker is
invalid. A no-finding review is valid when the critic records the reviewed scope and both verdicts.

The JSON artifact uses schema version 2:

```json
{
  "schema_version": 2,
  "cycle_id": "roadmap-example-m1-example",
  "milestone": "M1 — example",
  "critic": "fresh critic identity",
  "initial_reviewed_commit": "0000000000000000000000000000000000000000",
  "scope": ["requirements", "diff", "tests", "architecture", "roadmap-evidence"],
  "initial_verdict": "pass",
  "repair_rounds": 0,
  "reviewed_commits": ["0000000000000000000000000000000000000000"],
  "findings": [],
  "confirmation": {
    "reviewed_commit": "0000000000000000000000000000000000000000",
    "verdict": "pass",
    "unresolved_blocker_major": []
  },
  "human_escalation": null
}
```

Each finding has a unique `id`, `severity`, `summary`, non-empty `evidence` array, `disposition`, and
non-empty `resolution_evidence` array. `disposition` is normally `resolved` or `follow-up`.
Blocker/major findings must be `resolved`; minor follow-ups must cite the durable issue or roadmap item
in `resolution_evidence`. The only exception is `unresolved` for a blocker/major named by both matching
tenth-round confirmation and human-escalation ID lists. Those lists must exactly equal the set of
`unresolved` finding IDs. `reviewed_commits` is the unique ordered exact-SHA chain: the initial reviewed
commit followed by exactly one new commit per repair round. The confirmation SHA must equal its final
entry.

After a failed tenth confirmation, set `human_escalation` to an object containing the final
`reviewed_commit`, a non-empty `unresolved_blocker_major` list, and a concrete non-empty
`action_required` string. Leave the confirmation verdict and unresolved list truthful. The validator
then fails closed with `human escalation is terminal and cannot satisfy milestone acceptance`; this is
the durable stop signal, not an acceptance artifact. No automated worker may clear it or reset the
round count. Only an explicit human decision may start separately scoped renewed work.

Before handoff, the worker validates the artifact:

```sh
python3 .agents/skills/work-roadmap/scripts/validate-critique-state.py \
  --root . --cycle <cycle-id> --artifact reviews/roadmap/<cycle-id>.json
```

The host repeats that exact command against the merged artifact. Missing, unreadable, malformed,
wrong-cycle, incomplete-scope, invalid-severity, unevidenced, unresolved blocker/major, excessive
repair-round, or non-passing confirmation state fails closed. The roadmap evidence must point to the
validated artifact. The final completion report names every milestone's critique artifact and rolls up
resolved blocker/major counts plus outstanding minor follow-ups.
