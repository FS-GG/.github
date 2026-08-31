---
name: padd-item
description: Use when explicitly asked to add a described item to the current product workspace board. Reconcile and triage first, then deduplicate, file, and verify.
---

# PAdd Item

Turn the text after `$padd-item` into one durable issue in the current repository and project it onto
this workspace's configured board. File the work; do not implement it.

## 1. Prove workspace wiring

Resolve the current repository from its GitHub remote. Before any mutation, require:

- `scripts/fsgg-coord`, `check-board`, and `work-board` in the workspace;
- `FSGG_COORD_PROJECT` plus either an organization/named-user owner or
  `FSGG_COORD_OWNER_TYPE=user` for the authenticated viewer's board;
- authentication that can read the repository and configured GitHub Projects v2 board.

Use the configured `FSGG_COORD_OWNER_TYPE`, `FSGG_COORD_OWNER`, and `FSGG_COORD_PROJECT` exactly.
Never silently fall back to the FS-GG organization board. On missing wiring, kit, authentication, or a
readable board, stop non-zero without mutation and name the `new-sdd-workspace retrofit <workspace>
--board owner/title` or `work-roadmap` alternative.

## 2. Clean and triage this workspace

Run the workspace-scoped `check-board` pass:

```bash
scripts/fsgg-coord budget
scripts/fsgg-coord reconcile --repo <this-repo> --json
scripts/fsgg-coord lint --repo <this-repo> --json
```

Apply only typed safe repairs with `reconcile --apply`, flush queued writes, and repeat the fresh pass.
Stop on unreadable state or an unconfirmed write.

Then read `scripts/fsgg-coord ready --repo <this-repo> --status Backlog --json`. Read every item and
relevant comment. Follow the `work-board` backlog-triage contract: promote evidenced actionable work,
retain only an explicitly parked item, set Blocked only for a live parseable dependency, and surface
genuine missing judgement. Finish this whole pass before filing.

## 3. Resolve and deduplicate the request

Treat the invocation text as the source request. Inspect current-repository source, tests, docs, issues,
and pull requests to establish behavior and scope. Search live issues with:

```bash
scripts/fsgg-coord issues <this-repo> --state open --refresh
```

Use GitHub issue/PR search when needed. Reuse an existing semantic match instead of creating a duplicate.
If evidence says another repository owns the behavior, route a request through
`cross-repo-coordination`; do not edit another checkout or put its
implementation onto this repo's local scheduling lane.

Decide autonomously when evidence selects one scope. Ask one concise question before mutation only when
materially different local scopes remain plausible after inspection.

## 4. Construct, validate, file, and project

Create a concise outcome title and put the structured fields in a draft such as:

```json
{
  "schema": "fsgg.coord.intake/v1",
  "id": "<stable-local-id>",
  "owner": "<configured-board-owner>",
  "repository": "<this-repo>",
  "title": "<outcome title>",
  "observed": "<requested or observed behavior>",
  "rootCause": "<established cause, or what remains unestablished>",
  "acceptance": "<testable criteria>",
  "verification": "<commands or evidence links>",
  "paths": ["<exact path or directory prefix>"],
  "class": "<defect|hardening|decision>",
  "severity": "<low|medium|high|critical>",
  "status": "Backlog",
  "backlogReason": "not-yet-actionable",
  "disposition": "create"
}
```

Put a real sequencing dependency in the optional `blockedBy` property; omit it otherwise. Never guess
paths or use unsupported glob syntax. The intake contract requires at least one existing path; do not
substitute an empty array or `none`. A hand-authored `Paths:` or `Class:` line in a created body is a
defect, not a style choice: those projections belong to the validated draft.

Validate the exact draft before creation, then apply that same file:

```bash
scripts/fsgg-coord intake validate intake.json --json
intake_result="$(scripts/fsgg-coord intake apply intake.json --json)"
issue_ref="$(jq -r .issue <<<"$intake_result")"
scripts/fsgg-coord add "$issue_ref"
```

`intake apply` creates or reuses the issue according to `disposition`, composes the body from validated
fields, and targets the configured board. Use `disposition: reuse` for a deduplicated existing issue and
retain the canonical ref returned by the transaction. Direct issue-creation commands are not the ordinary
filing path.
The following idempotent `add` asserts that the returned ref is on the configured board; it is not a
second filing path and never substitutes for validate/apply.

Set only evidenced fields:

- `Ready` for open, actionable work with valid paths, no unresolved dependency, no active claim, and no
  open implementation PR;
- `Blocked` only when `Blocked by:` names a live implementation dependency;
- `Backlog` for a parked decision **and** for anything not yet evidenced as `Ready` — it is the default
  `add` writes, and it means "visible to triage, not startable", not "deliberately shelved";
- `In review` only when a matching implementation PR is already open.

Never set `In progress` or `Done` by hand. Claims and verified done stamps own them. If rate exhaustion
queues a write, run `flush` and do not report success until a fresh read confirms it.

## 5. Verify and report

Re-read the issue and its workspace-scoped board row. Verify status, paths, blockers, configured board
identity, `pendingBoardWrites: 0`, final `reconcile --json`, and final `lint --json`.

For every material status change, immediately emit:

`<item> — <new status>: <one-line summary>`

Then emit:

`Active: <item> — <current activity/gate>; ...`

Finish with the issue ref/link, current repository, configured board, deduplication result, chosen
status, and any judgement requiring a human. Do not implement the item unless separately asked to run
`work-board` or `pnext-item`.
