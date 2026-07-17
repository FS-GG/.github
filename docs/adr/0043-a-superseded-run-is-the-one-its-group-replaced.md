# ADR-0043: A superseded run is the one its group replaced — the conclusion is not part of the test

- **Status:** Accepted (2026-07-17)
- **Date:** 2026-07-17
- **Affects:** `.github` (`Landable.supersede`, the `landable` verdict every repo's workers and bots merge on), and therefore every repo that lands work through `/pnext-item` — sdd, rendering, governance, templates, game, audio
- **Decides:** [#1039](https://github.com/FS-GG/.github/issues/1039). Corrects the doctrine [#698](https://github.com/FS-GG/.github/issues/698)/[#719](https://github.com/FS-GG/.github/issues/719) established, on a premise neither of them tested.

## Context

`landable` scores an open PR by rolling up the workflow runs and check-runs on its head SHA. Runs that
`cancel-in-progress` replaced must be dropped, or the recipe's own happy path — push the branch, then edit
the PR body — reds every correct PR (#698).

`Landable.supersede` dropped a superseded run **only when it concluded `cancelled`**:

```fsharp
|> List.filter (fun r -> r.Conclusion <> Some "cancelled" || not (replaced r))
```

The `cancelled`-only clause was there to stop a failure being laundered by **re-running it until it
passed**. That is the entire reason the conclusion was in the test, and it is stated in the code as a
guarantee: *"a failed run is never dropped — so this cannot fail open."*

**Two things are wrong with that, and both are facts about GitHub rather than judgements.**

**1. A re-run creates no run, so the attack the clause defends against does not exist.** A re-run adds an
**attempt** to the existing run, keeping its id and its `run_number`, and the row's `conclusion` reads the
**latest attempt**. This is the same mechanic [#721](https://github.com/FS-GG/.github/issues/721) documented
in `/pnext-item` — the reason a re-run re-executes a stale `@main` — and it was never carried into
`Landable.fs`. Measured on the run #721 itself cites, re-run a full day after it was created:

```console
$ gh api repos/FS-GG/FS.GG.Governance/actions/runs/29236481212 \
    --jq '"num=\(.run_number) attempt=\(.run_attempt) created=\(.created_at) started=\(.run_started_at)"'
num=131 attempt=2 created=2026-07-13T08:44:13Z started=2026-07-14T09:30:07Z

$ gh api "repos/FS-GG/FS.GG.Governance/actions/runs?head_sha=aeecf6013…" \
    --jq '.workflow_runs[] | select(.path==".github/workflows/lockfile-sync.yml") | .id'
29236481212                     # ONE row — the re-run added no second one
```

So *"a real failure followed by a passing re-run"* is **one row reading `success`**, which every rule here
scores green. The clause bought no protection against re-run-until-green; there was none to buy. **Its
stated purpose was unachievable for its whole life, and no test could have caught that — the shape it
guards against cannot be constructed from the API.**

**2. What the clause actually kept was a stale verdict.** A workflow keyed on **mutable PR metadata** is not
a function of the head SHA. `architecture-map` reads the body out of the event payload
(`PR_BODY: ${{ github.event.pull_request.body }}`), and when it fails it prints its own remedy: *add
`architecture-map: unaffected` to the PR body*. Do that, and the `edited` event starts a **second run on the
same SHA**, which passes. Nothing cancelled the first — it had **completed**, and `architecture-map` declares
no `concurrency` block — so it stayed `failure`, `supersede` kept it, and the verdict was `red` **forever**.
Measured on PR #1036, head `752e95c`:

| run | conclusion | |
|---|---|---|
| `29574083099` | **failure** | read the pre-edit body |
| `29574492033` | **success** | the gate's own remedy, applied |

`landable` said `red` (exit 3, terminal); GitHub said `mergeable_state: clean` and `reconcile` is a
**required** check — so the stricter authority was the one saying merge. The only escape found was
`git commit --amend --no-edit` + force-push: a tree-identical commit whose sole purpose was to launder a
stale verdict.

## Decision

**Only the highest `run_number` in each concurrency group is scored. Every earlier run of that group is
dropped, with its check suite, whatever it concluded.**

```fsharp
let live = runs |> List.filter (fun r -> not (replaced r))
```

`cgroup` — `(Path, Event, HeadBranch, PrNumbers)`, ADR-unchanged since #703 — remains the key, and it is
what keeps this fail-closed. The conclusion is removed from the test; the group is not.

**The event is not consulted, and does not need to be.** #1039 first proposed distinguishing a re-run from a
re-evaluation by the run's `event`. That is unnecessary *and* impossible:

- **Unnecessary.** `Reads.workflowRuns` keys its read on **one head SHA**, and a `synchronize` **changes the
  head SHA by definition**. So a second run of a group on a fixed SHA is *always* a metadata
  re-evaluation, never a re-run. The SHA scoping already decides it — with no event list to go stale, which
  is the cost #1039 correctly predicted and priced (#381/#446/#962).
- **Impossible.** `edited` and `labeled` are webhook **actions**; the runs API carries only `event`, which is
  `pull_request` for both runs above. Diffing the two run objects, every differing field is an id, a URL or a
  timestamp. There is nothing to discriminate on.

## Consequences

- **`landable` and GitHub's branch protection no longer reach opposite terminal verdicts** on a PR whose only
  failure is a superseded metadata gate. That was #1039's acceptance, and it is met by agreeing with GitHub
  on this shape rather than by explaining the disagreement.
- **Every posture in the corpus survives**, each pinned by a test that passes **unmodified**:

  | shape | verdict | posture |
  |---|---|---|
  | cancelled, replaced by its group | dropped → GREEN | #698/#720 |
  | cancelled, **nobody re-ran** | live → RED | #698 — a cancelled run nobody re-ran is still a finding |
  | cancelled `pull_request` + higher `workflow_dispatch` | live → RED | #703 — different group, supersedes nothing |
  | failure, replaced by its group | **dropped → GREEN** | **this record** |
  | failure, **not replaced** | live → RED | unchanged |

- **The fail-open #698 feared is now `cgroup`'s job alone, and it always was.** A `workflow_dispatch` run
  shares the SHA, the path and a higher run number, but is a different `github.ref` — so it supersedes
  nothing, and cannot vacuously green a `pull_request` run whose gate job it skipped. Pinned for failures as
  well as cancellations.
- **A gate may keep failing on mutable metadata**, and the printed remedy now works: edit the body, the
  `edited` run passes, `landable` goes green. No force-push, no laundered SHA.
- **A cost, named:** a failure genuinely fixed by nothing but time — a flake — is now dropped if any later
  trigger re-ran its group on the same SHA. That is the same trade `cancel-in-progress` already makes, and it
  is bounded by the group key: only a re-trigger of the *same* workflow on the *same* ref can do it.
- **`Landable.fs`'s doctrine is corrected, not just its rule.** The comment asserting the re-run guarantee is
  replaced by the mechanic that falsifies it. Retiring a conclusion and leaving its premise to re-emit is
  [#968](https://github.com/FS-GG/.github/issues/968)'s defect.

## Alternatives considered

**A — latest run per workflow `path` wins.** What GitHub does. Rejected: it drops `Event`/`HeadBranch` from
the key and reopens #703's hole, letting a `workflow_dispatch` run license the drop of a `pull_request` run.
This record is *not* A — it keeps the full group key.

**B — drop a superseded failure only when the later run came from a DIFFERENT event.** Recorded as the
decision on #1039 and **withdrawn as unimplementable**: `edited`/`synchronize` are actions, not events, and
both runs read `event=pull_request`. Its own accepted cost — an embedded event list that would rot — could
not even be paid, because the list has no field to read.

**C — metadata gates must not FAIL; report `neutral`.** Keeps `landable`'s model exactly true. Rejected: a
`neutral` required check does not block, so `architecture-map` stops gating — #463's *"a gate nobody must
satisfy reports drift into a log file"*, re-created deliberately.

**D — metadata gates re-run on a new SHA only** (drop `edited`/`labeled` from their triggers). Coherent, and
needs no `landable` change. Rejected as the weaker trade: it makes the human remedy worse for every metadata
gate — an opt-out costs a commit — to avoid one narrow rule in one function.

**The A-vs-B trade that #1039 framed does not exist.** Both options were priced against re-run-until-green,
and that attack is unreachable: a re-run mutates the row it is a re-run of. Once that is corrected, the
`cancelled` clause protects nothing and the choice collapses.
