---
name: p-add
description: Use when explicitly asked to turn a short natural-language work request into a clean FS-GG Coordination-board item. Reconcile and triage backlog first, infer ownership, deduplicate, file, add, and verify.
---

# PAdd

Turn the text after `$p-add` into one durable, schedulable issue. This command files work; it does not implement the item.

## 1. Clean and triage before filing

Run the complete [check-board](../check-board/SKILL.md) pass:

```bash
scripts/fsgg-coord budget
scripts/fsgg-coord reconcile --json
scripts/fsgg-coord lint --json
```

Review every mechanical finding, apply only typed safe repairs with `reconcile --apply`, flush queued
writes, and repeat the fresh reconcile/lint pass. Stop on unreadable state or an unconfirmed write.

Then read `scripts/fsgg-coord ready --status Backlog --all`. Read each issue and its relevant comments
and classify it exactly as the `drive-board` backlog-triage contract requires: promote evidenced
actionable work, retain only an explicitly parked item, set Blocked only for a live parseable dependency,
and surface genuine missing judgement. Finish this whole pass before describing the new item as Ready.

## 2. Resolve the described work

Treat the invocation text as the source request. Infer the owning repository from live evidence:

- inspect `registry/repos.yml`, capability ownership, sibling repositories, and relevant source/docs;
- choose the repository that owns the behavior or contract, not the repository that merely noticed it;
- search that repository's open issues and pull requests for semantic duplicates using
  `scripts/fsgg-coord issues <repo> --state open --refresh` plus GitHub search when needed;
- reuse an existing matching issue rather than creating a duplicate.

Decide autonomously when evidence selects one owner and scope. Ask one concise question before any
mutation only when materially different owners or scopes remain plausible after inspection.

## 3. Construct the intake draft

Create a concise outcome-oriented title and put every structured field in an intake draft, not in a
hand-authored issue body. Start from this complete shape and replace every placeholder with evidence:

```json
{
  "schema": "fsgg.coord.intake/v1",
  "id": "<stable-local-id>",
  "owner": "FS-GG",
  "repository": "<owning-repo>",
  "title": "<outcome-oriented title>",
  "observed": "<requested or observed behavior and why it matters>",
  "rootCause": "<established cause, or what remains unestablished>",
  "acceptance": "<testable acceptance criteria>",
  "verification": "<commands, evidence links, or intended proof>",
  "paths": ["<exact path or directory prefix>"],
  "class": "<defect|hardening|capability|decision>",
  "severity": "<low|medium|high|critical>",
  "status": "Backlog",
  "backlogReason": "not-yet-actionable",
  "disposition": "create"
}
```

The draft must carry:

- the observed or requested behavior and why it matters;
- testable acceptance criteria;
- `paths` with the narrowest evidence-backed exact paths or directory prefixes;
- `blockedBy` only for a real sequencing dependency (omit the property otherwise);
- relevant evidence links or identifiers.

Never guess a path or use unsupported glob syntax. The intake contract requires at least one existing
path; do not substitute an empty array or `none`. If a valid touch-set cannot be established, investigate
further or surface the missing judgement before filing.

`paths`, `class`, `severity`, `blockedBy`, and the projected body lines they produce belong to the
draft. Hand-authoring `Paths:` or `Class:` in a created issue body is a defect, not a style choice.

## 4. Validate, file, and project

For a new issue, validate the exact draft before any creation, then apply that same file:

```bash
scripts/fsgg-coord intake validate intake.json --json
intake_result="$(scripts/fsgg-coord intake apply intake.json --json)"
issue_ref="$(jq -r .issue <<<"$intake_result")"
scripts/fsgg-coord add "$issue_ref"
```

`intake apply` creates or reuses the issue according to `disposition`, composes the body from the
validated fields, and projects it onto the board. Retain the returned canonical issue ref. A direct
`gh issue create` or `gh api ... POST .../issues` is not the ordinary filing path.
The following idempotent `add` is a live projection assertion for the returned ref; it is not a second
filing path and never replaces the transaction.

For a deduplicated existing issue, set `disposition` to `reuse` and use the same validate/apply pair;
the transaction refuses a draft whose claimed disposition does not match live dedupe evidence.

Set only fields supported by evidence:

- `Ready` for open, actionable work with valid paths, no unresolved dependency, no active claim, and no
  open implementation PR **and a current agent-authored delivery-route receipt**. Every implementation
  draft first enters `AwaitingDeliveryRouteDecision`; the fixed checklist records evidence (multi-repo,
  public contracts, migration/release/security/recovery, coordinated phases/providers, and independent
  evidence classes) but never selects lightweight or SDD on the agent's behalf;
- `Blocked` only when `Blocked by:` names a live implementation dependency;
- `Backlog` for a parked decision **and** for anything not yet evidenced as `Ready` — it is the default
  `add` writes, and it means "visible to triage, not startable", not "deliberately shelved";
- `In review` only when an existing matching implementation PR is already open.

Never set `In progress` or `Done` by hand. Claims and verified done stamps own those transitions.
If budget exhaustion queues a write, run `flush` when available and do not claim success until a fresh
read confirms it.

## 5. Verify and report

Re-read the issue, its board row, status, paths, blockers, and `scripts/fsgg-coord budget`. Run a final
`reconcile --json` and `lint --json`. Success requires `pendingBoardWrites: 0` and the intended live row.

Whenever filing or triage changes an item's material status, immediately emit:

`<item> — <new status>: <one-line summary>`

Then emit:

`Active: <item> — <current activity/gate>; ...`

Finish with the issue link/ref, owning repository, deduplication result, chosen status, and any surfaced
human judgement. Do not begin implementation unless the user separately asks to work the board or item.
