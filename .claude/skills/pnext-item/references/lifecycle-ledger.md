# FS.GG item lifecycle ledger

Every claimed item has one tracked, append-only JSONL phase ledger and one append-only runtime-usage
report. Normal items use:

```text
logs/items/<owner>.<repo>/<issue-number>/lifecycle.jsonl
logs/items/<owner>.<repo>/<issue-number>/usage.csv
```

A roadmap item uses its roadmap path instead:

```text
logs/roadmap/<roadmap-slug>/<run-id>/<unit-id>.jsonl
logs/roadmap/<roadmap-slug>/<run-id>/<unit-id>.usage.csv
```

Create the ledger when work begins. Never rewrite, delete, reorder, or renumber accepted rows. A status
reply is a projection of the ledger; prose and a GitHub timestamp are evidence, not substitutes.

## Phase events

Each JSONL line is one phase transition. The shared v1 fields are `schema_version`, `run_id`, `unit_id`,
`item`, `sequence`, `phase_order`, `phase`, `event`, `at`, `actor`, `model`, `source`, `evidence`,
`actual_minutes`, `historical_durations_minutes`, `historical_average_minutes`, and `token_usage`.

- `item` contains the canonical GitHub `repo`, positive issue `number`, and exact HTTPS issue `url`.
- `phase` is a lowercase stable identifier. Every numbered review, repair, recovery, merge, verification,
  projection, and cleanup pass is distinct.
- `event` is `started`, `completed`, `blocked`, or `resumed`; exactly one phase is active.
- `at` is canonical UTC. Terminal duration is elapsed wall time from the first start, rounded to the
  nearest whole minute. Historical averages use prior completed durations for the same canonical phase
  and the same rounding rule.
- `actor` is the minted worker, critic, or host identity.
- `model` records `provider`, exact `name`/variant, optional `effort`, and authoritative `source`. A model
  switch begins a new phase. Never derive a model from an agent nickname.
- `token_usage` on a start/resume is `pending`. On a terminal event it is `measured`, `pending`, or
  `unavailable`. `pending` means the response containing the transition has not finished yet and must be
  reconciled by the post-response collector. `unavailable` is allowed only after the named authoritative
  source was checked and had no usable record.

Measured phase usage records total input, cached input, cache-write input, output, reasoning output, and
total tokens. Cached/cache-write counts are subsets or components of input according to the provider;
`total = input + output`, and reasoning output is already included in output. Bind the measurement to one
or more `session_id`/`turn_id` rows in the usage report. When a phase spans turns, sum completed-turn
records. When a turn spans phases, do not invent a split: keep the affected phase pending until a boundary
receipt exists, or mark it unavailable with that exact limitation.

## Runtime collection

The collector emits this stable report header:

```text
timestamp,task,session_id,thread_id,turn_id,provider,model,effort,input,cached_input,cache_write_input,output,reasoning,total,thread_input,thread_cached_input,thread_cache_write_input,thread_output,thread_reasoning,thread_total,source
```

Codex persists `token_usage_record` rows under `~/.codex/sessions/YYYY/MM/DD/`. Use the final record for
the selected `turn_id`: its `turn_token_usage` is the completed-turn total, `thread_token_usage` is the
full-thread total, and the matching `turn_context` supplies the exact model variant and effort. Run:

```sh
python3 .agents/skills/pnext-item/scripts/collect-runtime-usage.py codex \
  --session-file <rollout.jsonl> --task <item/phase> --turn-id <turn-id> --append <usage.csv>
```

The local Codex JSONL is an internal interface, so the collector validates every required key and fails
closed on shape drift. For direct API work, prefer the stable response `usage` object.

Claude Code exposes `session_id`, `prompt_id`, `model.id`, effort, and the latest API response's input,
cache-creation, cache-read, and output usage to a status-line command. Persist those snapshots after each
assistant message. A `Stop` hook runs after the main response finishes and receives `session_id` and
`transcript_path`; use it to append the saved snapshot. `SubagentStop` supplies a separate
`agent_transcript_path`, so subagent usage must remain separate. The status-line value is one latest API
response, not a whole multi-call turn; aggregate only captured requests. Claude's ordinary usage does not
separate reasoning from output, so the report leaves `reasoning` empty rather than guessing. For
non-interactive work, `--output-format json`/`stream-json` or the Agent SDK result is preferred.

## Validation and completion

Run the validator at each handoff and gate. A normal item uses any stable lowercase `run_id` and path-safe
`unit_id`; a roadmap supplies its cycle values:

```sh
python3 .agents/skills/pnext-item/scripts/validate-lifecycle-log.py \
  --root . --run <run-id> --unit <unit-id> --log <lifecycle.jsonl>
```

Use `--require-terminal --require-reconciled` before a done stamp or roadmap roll-up. The first rejects an
active or blocked phase; the second rejects terminal `pending` token usage. Run both scripts' `--self-test`
after changing them.
