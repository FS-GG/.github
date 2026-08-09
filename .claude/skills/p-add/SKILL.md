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

## 3. Construct the issue

Create a concise outcome-oriented title and a body containing:

- the observed or requested behavior and why it matters;
- testable acceptance criteria;
- `Paths:` with the narrowest evidence-backed exact paths or directory prefixes;
- `Blocked by:` only for a real sequencing dependency;
- relevant evidence links or identifiers.

Never guess a path or use unsupported glob syntax. Use `Paths: none` only when the item deliberately
changes no repository file. If a valid touch-set cannot be established, investigate further or ask
before filing.

## 4. File and project

Create the issue in its owning repository, or retain the deduplicated issue ref. Then:

```bash
scripts/fsgg-coord add FS-GG/<repo>#<number>
scripts/fsgg-coord set-field FS-GG/<repo>#<number> Status <status>
```

`add` is idempotent, and since `#1823` it **defaults `Status` to `Backlog`** when the row has no column
yet — a row with no `Status` is invisible to every scheduler, so the default is what stops a filed item
being unschedulable. It never overwrites a column somebody set. Run the `set-field` above anyway when
the evidence supports a different column; it is the explicit decision, and it wins.

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
