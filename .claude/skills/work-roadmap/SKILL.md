---
name: work-roadmap
description: Use when explicitly asked to complete a markdown roadmap milestone by milestone. Run each in a fresh worker through SDD and independent critique, merge it, update the roadmap, and finish with a report.
---

# work-roadmap

Burn down a markdown roadmap milestone by milestone. The roadmap—not a project board—is the ledger.

1. Read the complete roadmap and select the next unchecked, dependency-ready milestone.
2. Spawn one fresh disposable worker from current default branch and give it only that milestone.
   During worker setup, interactive/game work must explicitly invoke the `pnext-item` performance-first
   planning gate before implementation begins. A milestone that ships or claims reachable game
   functionality must record `game_functionality: true` and a passing bot-driven headless player
   journey (`.github#2087`) — a bot driving the product through the same control messages a real
   player emits, booted at the product's real entry point, not seeded into a mid-game state. No such
   milestone may reach a `shipReady`-equivalent verdict without it.
3. Give the worker a stable feedback cycle id. It follows the repository's SDD lifecycle and item/PR
   merge discipline and invokes `fs-gg-feedback-report` at every required checkpoint boundary.
4. After the first green implementation/test/evidence loop, have the worker start one fresh independent
   critic. The critic reviews requirements, diff, tests, architecture, and roadmap evidence without
   editing the implementation. The worker repairs blocker/major findings, and the same critic confirms
   them before the worker updates the roadmap and lands the milestone. For a work-roadmap milestone,
   this milestone critique loop owns the review/repair count and supersedes `$pnext-item`'s normal
   three-round cap; all other applicable `$pnext-item` planning, review-evidence, exact-SHA, merge,
   release, and escalation discipline remains in force. Permit at most ten numbered
   repair/confirmation rounds. If round ten remains red, record the terminal human escalation, stop
   the milestone, and never start round eleven or merge.
5. Verify the merge, tests, roadmap checkbox/evidence, release obligations, critique artifact,
   checkpoint state, and schema-v2 report externally. Missing, invalid, or unreadable critique or
   feedback state fails closed.
6. Discard the worker and critic, refresh default branch, re-read the roadmap, and select again.
   Persist a typed cycle envelope beside the roadmap: fresh-read Markdown into its source revision
   and units, run `fsgg-coord cycle inspect`, then `register` (or exact-id resume) for the milestone.
   Bind each unit's stable roadmap feedback/critique identity as `providerCycleId`. Pass the actual
   generated SDD verification, validated schema-v3 critique, and validated schema-v2 feedback artifacts
   to `advance`: each provider input names `rootPath` and `artifactPath`, while feedback also names its
   `auditPath` and ordered `phases`. The engine reruns `fsgg-sdd verify` and the canonical critique and
   feedback validators itself; normalized or minimally shaped caller-authored envelopes are not
   provider evidence, and journey applicability comes from the critique artifact. Persist the
   `updateReceipt` emitted and durably journaled by the guarded merged-head/checkpoint `update`;
   `complete` consumes that exact receipt and revalidates its bound
   evidence, followed by another fresh inspection.
   Parallel milestones additionally require recorded disjoint touch-sets and explicit operator
   authorization. Missing, stale, or wrong-cycle evidence fails closed.
7. After no unchecked milestone remains, validate every completed cycle and land the final report with
   cross-cycle critique and feedback roll-ups; a report that omits a cycle, critique disposition, or
   checkpoint disposition cannot finish.

## Lifecycle log

Create the item's tracked append-only lifecycle log before its first phase transition and append every
phase start, completion, block, and resume. Include numbered critique/repair rounds, guarded landing,
protected-main verification, receipt/projection, and cleanup as distinct phases. Validate the log at each
worker/critic handoff, before host acceptance, before cycle completion, and in the final cross-cycle
roll-up. A missing phase, sequence gap, invalid transition, or unresolved active phase fails closed.

Record the exact provider/model variant and effort used for each phase and authoritative input, cached
input, cache-write input, output, reasoning, and total token usage. Token accounting is reconciled after
the response finishes from the runtime session record or stable provider response; a terminal phase may
not turn a temporarily unavailable in-turn counter into permanent `unavailable`. When a runtime truly
provides no authoritative record, retain the concrete host/source reason; never infer or estimate.
Durations and historical averages are derived from recorded UTC timestamps and prior duration evidence,
using only whole discrete minutes. Read [lifecycle-log](references/lifecycle-log.md) for the canonical path,
schema, transition rules, token accounting, and exact validation command.

## Status position line

Every intermediate roadmap-driver, worker, or critic-handoff status reply must start with:

`Roadmap item: **<unit-id> — <name>** · GitHub: <linked owner/repo#number>`

Make the issue label a Markdown hyperlink to its canonical GitHub issue URL.

Follow it with one current, compact, ordered process-position line using:

- `✅` completed
- `🟢` active (exactly one while work is progressing)
- `⚪` pending
- `🔴` blocked (and no `🟢` while blocked)

Name active repair rounds. Show each completed step's actual duration and historical average for that
canonical step; show active elapsed time. Use only whole minutes: floor active elapsed; round completed
actuals and arithmetic averages to nearest. Average prior completed occurrences in this roadmap run,
excluding the current one; use `avg n/a` without a prior observation. Use recorded lifecycle timestamps,
never invented timing evidence. Give each concurrently reported item its own header and position line.

Example:

`✅ Intake (actual 2 min · avg 3 min) → ✅ Claim (actual 1 min · avg n/a) → 🟢 Repair round 3 (active · elapsed 4 min) → ⚪ Host acceptance → ⚪ Guarded merge`

The header and line complement, not replace, prose and evidence.

Milestones are sequential unless the roadmap explicitly establishes disjoint parallel milestones and
the user authorized parallel execution. Load [host-loop](references/host-loop.md) for shared
fresh-worker and verification rules and [roadmap-ledger](references/roadmap-ledger.md) for markdown
state transitions.
Load [feedback-contract](references/feedback-contract.md) for the worker activation, exact validation
commands, zero-event representation, host acceptance gate, and final roll-up contract.
Load [critique-contract](references/critique-contract.md) for critic isolation, severity and repair
rules, the artifact schema, exact validation command, and host acceptance gate.
Load [deep detail](references/deep-detail.md) only for recovery paths and extended rationale.
