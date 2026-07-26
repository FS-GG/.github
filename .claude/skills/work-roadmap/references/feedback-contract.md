# Development-feedback completion gate

The worker must explicitly invoke `fs-gg-feedback-report` and keep one stable lowercase cycle id:
`roadmap-<roadmap-slug>-m<milestone>-<slug>`. Give that id to the worker in its initial brief; do not
derive a different id after work begins.

At onboarding/first build, lifecycle authoring, the first implementation-test-evidence loop, and
verify/ship/PR orchestration, invoke the feedback skill and decide whether a material checkpoint
qualifies. Append qualifying friction, rework, capability gaps, documentation defects, orchestration
failures, and unexpectedly effective patterns with its documented `checkpoint` command. Routine green
commands are not findings.

Before handoff, finalize one schema-v2 report for the cycle. In §1 include this activation envelope:

```markdown
- **activation:** active
- **phases:** onboarding-first-build, lifecycle-authoring, implementation-test-evidence, verify-ship-pr
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
python3 .agents/skills/work-roadmap/scripts/validate-feedback-state.py \
  --root . --cycle <cycle-id> --report feedback/<report>.md \
  --audit feedback/audits/<report-stem>.audit.json \
  --phases onboarding-first-build,lifecycle-authoring,implementation-test-evidence,verify-ship-pr
```

The host repeats all applicable commands against the worker's exact merged paths before accepting the
milestone. An `incomplete` or `unsupported` audit finding is unresolved and blocks actionable handoff.
Missing, unreadable, malformed, wrong-cycle, count-mismatched, unbound-audit, or unvalidated state fails
closed and the host reports the exact path plus the command above that repairs or verifies it.

A zero-event cycle has no checkpoint JSONL: the validated activation envelope proves capture ran at
the named phases and explains why nothing qualified. Do not create a fake defect or positive pattern.
The schema-v2 report remains mandatory.

Before roadmap completion, the host verifies every completed milestone has a validated cycle report,
then lands a final roll-up that names every cycle/report and dispositions every checkpoint as a
structured finding, positive pattern, accepted observation, or deduplicated existing issue. A final
roadmap report without this coverage is incomplete.
