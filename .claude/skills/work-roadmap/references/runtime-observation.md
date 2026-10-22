# Runtime observation

Read this reference at driver entry. Native collaboration uses repository-owned dispatch observation;
load the opt-in normal-root route only for a prospectively selected item. Observation is advisory
and never a source-delivery gate.

## Opt in one item to observed normal-root execution


Native collaboration remains the default roadmap route. For one assigned item selected prospectively, the
driver may instead launch its normal root through the packaged `fsgg-coord-engine telemetry runtime codex-exec`
entrypoint. This changes the execution route for that item; ordinary delivery authority and all existing safety,
permission and operation controls still apply. Do not activate a workspace, change generated defaults or Host data,
or use this route to claim historical usage.

Create one absolute, regular, non-symlink private root assignment with mode `0600`:

```json
{"schema":"fsgg.telemetry.codex-assignment/1","featureId":"<feature>","itemId":"<item>","attemptId":"<root-attempt>","parentAttemptId":null,"producerStream":"<producer>"}
```

For a member whose original item differs from its item ID, first merge that exact mapping into the protected
`.github` assignment registry. Before starting the observed root, run the copied adapter's
`population-only --feature <feature> --item <item> --original-item <original>` command with the selected
receipt-scoped workspace config. It reads the protected mapping at an immutable revision and emits only an open
population fact. A successful result requires an `applied` receiver receipt; a merely durable receipt or lost
response retains the exact private batch for retry. Use the same member item ID in this command, the root
assignment and routine delivery. Do not call `begin`, `started` or `finish` for this observed root: the launcher
already owns its sole expected dispatch, lineage, time and native turn usage. Missing usage remains a gap.

Select the existing private host configuration and repository explicitly, and preserve Codex's machine-readable,
non-persistent launch flags:

```console
fsgg-coord-engine telemetry runtime codex-exec \
  --assignment /private/root-assignment.json \
  --config /private/telemetry.json --repository OWNER/REPO -- \
  --json --ephemeral -m MODEL -c model_reasoning_effort=EFFORT "assigned item"
```

For a remote workspace, invoke the launcher through that host's approved credential-loading wrapper. An explicit
`--config` selects the association but does not supply the receiver credential. On Main's development container,
use `fdev-telemetry exec` before `fsgg-coord-engine`; keep the credential out of prompts, logs and arguments.

Any descendant must call the same launcher from the inherited invocation context, without copying the assignment;
use the default child relation or explicit `--relation follow-up` as applicable. The inherited context preserves
the selected store, repository association and lineage. If any root or descendant invokes unobserved native
collaboration, the whole-item token total remains unknown.

Call the item total proven only after every expected invocation is present, every invocation has a terminal
outcome and final completed-turn counters, and every submitted batch has an applied Host receipt. Missing,
pending or conflicting evidence remains an explicit coverage gap; never infer a total from partial counters.

## Claude Code usage

Claude Code has no observed launcher or App Server join. Its native usage reaches the same local path from
an explicitly selected transcript: `fsgg-coord-engine telemetry usage collect claude --session-file` for a
phase receipt, or `telemetry passive capture` with a `fsgg.telemetry.passive-selection/1` file naming
`claudeHome` and the selected transcripts below its `projects` directory. Passive rows use provenance
`claude-local-transcript/1`, partial population and unknown attribution. Subagent transcripts stay separate
files and threads; item totals and attribution remain unknown.

## Record repository-owned dispatches by default

This native collaboration route applies when the opt-in `codex-exec` route above was not selected. The launcher
owns expected dispatches for its root and descendants; do not also call this section's `begin`, `started` or
`finish` commands for those invocations.

At driver entry, start one root observation with the installed
`fsgg-coord-engine skill roadmap-telemetry begin` command, supplying the roadmap
feature/item/attempt and selected model/effort. For distinct member items under one canonical original, pass
the same `--original-item` at each root; child and follow-up observations inherit it. For non-self originals,
first merge the exact member/original mapping into the protected
`docs/coordination/telemetry-original-item-assignments.json` registry. The adapter reads its protected GitHub
revision. Immediately
after every `spawn_agent`, bind the returned native agent id with `started`. Before each child or `followup_task`, start another observation with its stable attempt,
the parent's token and the `child` or `follow-up` relation. When an agent becomes terminal, call `finish` with its
real outcome before accepting its handoff; finish the root observation before returning. Pass the same stable
identities as `--telemetry-feature`, `--telemetry-item` and `--telemetry-attempt` to routine delivery so it
discovers the private host configuration and creates its CI assignment automatically.

Observation is asynchronous and never adds delivery ceremony. Missing configuration, publication, start or
terminal evidence is reported once as a coverage gap. With a Codex parent thread identity, the adapter joins
native child thread and turn records to the dispatch and verifies their final usage from private host records.
After late usage or a follow-up, call
`fsgg-coord-engine skill roadmap-telemetry usage-reconcile --token <private-token>` for the affected terminal attempt.
Until all turns are verified, report native usage as unknown; hosts without a joinable parent retain
`native-collaboration-usage-unsupported`. Expected population, lineage, requested model/effort and terminal
outcome alone do not establish complete usage.

The adapter associates an ordinary local invocation with `FSGG_TELEMETRY_REPOSITORY` first,
`GITHUB_REPOSITORY` second, then one credential-free canonical GitHub `origin` from the current checkout.
Malformed, non-GitHub, missing or ambiguous origins refuse; never guess an association or print remote credentials.
For workspace mutations, it loads a missing selected credential only through the owner-controlled telemetry client.
It persists each exact batch and next phase before submission; a retry replays those bytes and never regenerates
timestamps or identities after an unacknowledged or later-applied delivery.

Record typed activity spans for planning, implementation, review, validation, delivery, repair and operations;
use other/unclassified when necessary, and record complications with evidence as they occur. Attribute usage only
to an exact native usage observation—never by elapsed time—and keep mixed/unclassified explicit. After each
attempt terminal, author one bounded private process review through `review`; author the item review only after
the expected population is terminal. Reviews are advisory, evidence-linked observations: missing evidence is
unknown, and they never change delivery authority.

When the private host has an explicit event-publication activation receipt, the canonical adapter requests one bounded
dashboard refresh after a completed root attempt drains successfully and after each successful post-terminal root
observation drain. Publication and its returned health are advisory: failure, missing credentials, contention or an
unchanged snapshot never blocks delivery or changes the recorded terminal state. Do not assume or require a daemon.
