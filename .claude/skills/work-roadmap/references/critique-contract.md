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

Allow one worker repair round followed by confirmation by the same critic. A second repair round is
allowed only when confirmation finds an original blocker still unresolved or the first repair creates
a new blocker. After that, stop the milestone and report the blocker rather than looping. Confirmation
must inspect the repaired head and record `pass`; self-approval by the implementation worker is invalid.
A no-finding review is valid when the critic records the reviewed scope and both verdicts.

The JSON artifact uses schema version 1:

```json
{
  "schema_version": 1,
  "cycle_id": "roadmap-example-m1-example",
  "milestone": "M1 — example",
  "critic": "fresh critic identity",
  "initial_reviewed_commit": "0000000000000000000000000000000000000000",
  "scope": ["requirements", "diff", "tests", "architecture", "roadmap-evidence"],
  "initial_verdict": "pass",
  "repair_rounds": 0,
  "second_round_reason": null,
  "findings": [],
  "confirmation": {
    "reviewed_commit": "0000000000000000000000000000000000000000",
    "verdict": "pass",
    "unresolved_blocker_major": []
  }
}
```

Each finding has a unique `id`, `severity`, `summary`, non-empty `evidence` array, `disposition`, and
non-empty `resolution_evidence` array. `disposition` is `resolved` or `follow-up`. Blocker/major
findings must be `resolved`; minor follow-ups must cite the durable issue or roadmap item in
`resolution_evidence`. Set `second_round_reason` to `null` unless `repair_rounds` is 2; for round 2 it
must be `original-blocker-unresolved` or `repair-created-blocker`.

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
