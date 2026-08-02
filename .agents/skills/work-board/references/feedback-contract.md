# Development-feedback completion gate

The host gives every claimed item worker one stable lowercase cycle id:
`item-<issue-number>-<slug>`. The id is part of the initial worker brief and remains unchanged through
review and merge.

At onboarding/first build, lifecycle authoring when used, the first implementation-test-evidence loop,
and verify/ship/PR orchestration, invoke `fs-gg-feedback-report` and decide whether a material
checkpoint qualifies. Append qualifying friction, rework, capability gaps, documentation defects,
orchestration failures, and unexpectedly effective patterns with its documented `checkpoint` command.
Routine green commands are not findings.

Before handoff, finalize one schema-v2 report for the cycle. In §1 include this activation envelope:

```markdown
- **activation:** active
- **phases:** onboarding-first-build, lifecycle-authoring-or-not-used, implementation-test-evidence, verify-ship-pr
- **material events:** <non-negative checkpoint count>
- **zero-event reason:** <n/a, or evidence-based explanation when the count is zero>
```

When the count is non-zero, validate the checkpoint file:

```sh
dotnet fsi .agents/skills/fs-gg-feedback-report/scripts/feedback-tool.fsx -- \
  validate-checkpoints feedback/checkpoints/<cycle-id>.jsonl
```

Always validate the report with its exact actionability audit, then validate the cycle activation
envelope and the same audit/report binding:

```sh
dotnet fsi .agents/skills/fs-gg-feedback-report/scripts/feedback-tool.fsx -- \
  validate feedback/<report>.md --audit feedback/audits/<report-stem>.audit.json
python3 .agents/skills/work-board/scripts/validate-feedback-state.py \
  --root . --cycle <cycle-id> --report feedback/<report>.md \
  --audit feedback/audits/<report-stem>.audit.json \
  --phases onboarding-first-build,lifecycle-authoring-or-not-used,implementation-test-evidence,verify-ship-pr
```

The host repeats all applicable commands against the worker's exact merged paths before accepting the
item. A command passes only when the validator itself exits `0`; capture its output, then capture and
test that exit status immediately. For example:

```sh
validation_output="$(python3 .agents/skills/work-board/scripts/validate-feedback-state.py \
  --root . --cycle <cycle-id> --report feedback/<report>.md \
  --audit feedback/audits/<report-stem>.audit.json \
  --phases onboarding-first-build,lifecycle-authoring-or-not-used,implementation-test-evidence,verify-ship-pr)"
validation_status=$?
printf '%s\n' "$validation_output"
[ "$validation_status" -eq 0 ] || exit "$validation_status"
```

Never pipe a validator through `tail`, `tee`, or another command when deciding whether it passed: a
pipeline reports the last element's status, so a validator exit `1` can be replaced by exit `0` and turn
a failed validation into a false pass. An `incomplete` or `unsupported` audit finding is unresolved and blocks actionable handoff.
Missing, unreadable, malformed, wrong-cycle, count-mismatched, unbound-audit, or unvalidated state fails
closed and the host reports the exact path plus the command above that repairs or verifies it.

A zero-event cycle has no checkpoint JSONL: the validated activation envelope proves capture ran at
the named phases and explains why nothing qualified. Do not create a fake defect or positive pattern.
The schema-v2 report remains mandatory.

The board cannot terminate until each completed cycle has a validated report and the workspace report
names every completed cycle/report and dispositions every checkpoint as a structured finding,
positive pattern, accepted observation, or deduplicated existing issue. Eleven ship-ready items plus a
final report but no cycle feedback artifacts is incomplete, not a zero-event run.
