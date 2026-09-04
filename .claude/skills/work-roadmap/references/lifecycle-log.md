# Roadmap lifecycle log

Each roadmap item has one externally durable append-only ledger on its canonical GitHub issue. Every
event is a `fsgg:item-lifecycle/v1` structured comment posted through the verified `fsgg-coord comment`
boundary. An optional immutable export uses:

```text
logs/roadmap/<roadmap-slug>/<run-id>/<unit-id>.jsonl
```

Create it when the item begins. Never edit, delete, reorder, or renumber accepted comments. Append an
event immediately when a phase starts, completes, blocks, or resumes. A status reply is a projection of
this ledger; prose is not a substitute for it.

The candidate branch cannot be the live authority: critique, merge, protected-main, projection, and
cleanup happen after its exact head is fixed, and appending those future facts would invalidate that
head forever. Tracked JSONL is a later immutable projection under a distinct source-bound item and never
gates the PR that carries it. Raw request-level usage and machine-local paths remain private/untracked.

## Event contract

Every line is one JSON object with these fields:

- `schema_version`: integer `1`.
- `revision`, `previous_digest`, `digest`: contiguous digest chain over canonical event JSON.
- `authority`: `github_issue_comment`, canonical item subject, and exact claim generation.
- `run_id`, `unit_id`: lowercase identifiers matching the validator arguments.
- `item`: `repo`, positive `number`, and exact HTTPS `url` for that issue.
- `sequence`: positive contiguous integer, starting at `1`.
- `phase_order`: positive contiguous integer in first-seen phase order.
- `phase`: lowercase identifier; use a distinct identifier for every numbered repair.
- `event`: `started`, `completed`, `blocked`, or `resumed`.
- `at`: canonical UTC `YYYY-MM-DDTHH:MM:SSZ`, nondecreasing across the ledger.
- `actor`: minted worker, critic, or accountable host identity.
- `model`: either `{"status":"recorded","provider":"...","name":"...","effort":"...","source":"..."}`
  from an authoritative host/runtime observation, or
  `{"status":"unavailable","reason":"...","source":"..."}`.
  Never infer a model from an agent label. One phase binds one model; a model change starts a distinct
  continuation/recovery phase so its duration and tokens remain attributable.
- `tooling`: `ledger_schema` plus versioned `runtime`, `coordination`, `sdd`, and `contracts`
  components. Record exact versions and sources or an explicit unavailable/not-applicable reason.
  Historical duration/token comparisons use only rows with the same canonical tooling fingerprint.
- `source`: object containing `repository` and either a 40-hex `revision` or an
  `unavailable_reason` explaining why no source revision exists yet.
- `evidence`: non-empty array of reproducible commands, paths, API calls, or URLs.
- `actual_minutes`: `null` on `started`/`resumed`; on `blocked`/`completed`, elapsed wall time from the
  phase's first `started` event, rounded to the nearest whole minute.
- `historical_durations_minutes`: empty on non-`completed` events; on `completed`, the prior completed
  durations supplied by a validated history report for the same canonical phase and tooling fingerprint.
- `historical_average_minutes`: `null` when the basis is empty; otherwise the nearest whole-minute
  arithmetic mean of `historical_durations_minutes`.
- `token_usage`: `{"status":"pending"}` on `started`/`resumed`. A terminal event may remain pending only
  until the response that closed it finishes. Reconcile it from the runtime record to
  `{"status":"measured","input":N,"cached_input":N,"cache_write_input":N,"output":N,"reasoning":N,
  "total":N,"source":"...","session_ids":[...],"turn_ids":[...]}` with non-negative integers and
  `total = input + output`, or
  `{"status":"unavailable","reason":"...","source":"..."}`. The validator joins measured rows to
  the private usage report and verifies counts, model, runtime, coordination, SDD, and contracts versions.
  Counts are phase-local deltas from an
  authoritative host/provider receipt, never estimates or context-window sizes.

Only one phase may be active. A new phase starts only after the preceding phase completed. `blocked`
leaves no active phase; only that same phase may `resume`, and it may block again or complete. A completed
phase is terminal. The final item ledger has no active or blocked phase.

## Required phases and ownership

Use the actual lifecycle, without collapsing work that has distinct authority or evidence. At minimum,
represent intake/route/claim, SDD or planning, implementation and tests, initial critique, every numbered
repair/confirmation, host acceptance, guarded merge, protected-main verification, receipt/projection, and
cleanup when applicable. A critic appends its own phase events under its minted identity; the implementer
must not attribute critic token usage to itself. Cross-repository projection phases remain in the same item
ledger and bind their repository/revision in `source`.

Codex's completed `token_usage_record` rows provide request, turn, and thread totals; matching
`turn_context` rows provide the exact model variant and effort. Claude Code's status-line input provides
the exact model ID and latest-response input/cache/output usage, while `Stop` and `SubagentStop` hooks
provide the post-response lifecycle point and separate transcript paths. The `pnext-item` lifecycle-ledger
contract carries the equivalent normal-item collection rules. If turn-level usage spans multiple phases,
do not allocate or estimate shares.

## Validation

Run at every handoff and gate named by the skill:

```sh
python3 .agents/skills/work-roadmap/scripts/validate-lifecycle-log.py \
  --root . --run <run-id> --unit <unit-id> \
  --log <exported-lifecycle.jsonl> --usage <private-untracked-usage.csv> \
  [--history-report <validated-history.csv>]
```

Use `--require-terminal --require-reconciled` before cycle completion and final roll-up. The host exports
the issue-comment chain, joins it to the private usage report, and checks the command's exit status directly;
never mask it through a pipeline. Run
`--self-test` after changing the validator.
