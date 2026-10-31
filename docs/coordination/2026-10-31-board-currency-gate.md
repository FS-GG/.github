# BOARD-CURRENCY-01: make "the board is current and triaged" a merge-time gate

Design, 2026-10-31. Owning repository `FS-GG/.github`; owning Outcome
[#4730](https://github.com/FS-GG/.github/issues/4730) (COORD-EFF-02). Root (Programme/root) owns
approval, integration, the guarded Status writes and every acceptance decision. Phase 1 (the rule
evaluator and the shadow report) is PR #5035. Root's recorded decisions on this design
([issuecomment-6540490608](https://github.com/FS-GG/.github/issues/4730#issuecomment-6540490608)) are
folded in below and listed in §6.

## Why

The user asked for this directly:

- "we need to make check board that everything is current and triaged mandatory and deterministic.
  maybe automatic part of merge process?"
- "why is the work of the last day not put down as deliveries on the coord board"
- "more honestly reflect the amount of work still open … most outcomes have all their deliveries done
  yet still are open, that is very uninformative."

Today the board is current only if Root remembers each guarded Status write
([lifecycle contract](../../.agents/skills/work-board/references/lifecycle-contract.md), same-turn
rule). The hourly [board-drift observer](../../.github/workflows/board-drift-observer.yml) (#4973,
issue #4985) detects some misses, but its rules are mostly heuristics. Nothing at merge time checks that
the work being merged is on the board.

### Baseline (ad hoc, point in time)

These counts are one ad hoc observation, not a reproducible fixture: the board has changed since, and
the snapshot holds private-repository text, so it is not published. The observation was made at
2026-10-31T06:45Z (98 cards, 145 PRs: 20 open and 108 merged in the previous 24 hours carry the routine
marker) with the phase-1 code at #5035 head `e6bed0b2`. Repeating the same commands later gives a new
observation, not these numbers:

```sh
python3 scripts/board-currency-fetch.py --out "$SCRATCH/snapshot.json"
python3 scripts/board-currency-rules.py check --snapshot "$SCRATCH/snapshot.json" --out "$SCRATCH/findings.json"
# R1 by reason and PR state:
python3 -c 'import json,sys,collections; r=json.load(open(sys.argv[1])); print(collections.Counter((f["reason"], f.get("prState")) for f in r["findings"] if f["rule"]=="R1"))' "$SCRATCH/findings.json"
```

| Rule | Findings | Breakdown |
|---|---:|---|
| R1 | 109 | 94 of the 108 merged routine PRs: 80 `no-delivery`, 12 `no-parent-outcome`, 2 `multiple-deliveries`. 15 of the 20 open routine PRs: 14 `no-delivery`, 1 `no-parent-outcome`. 94 + 15 = 109. Nearly every `no-delivery` PR says only `Refs #4730`, which is an Outcome. |
| R3 | 9 | all `no-blocker` |
| R5 | 9 | open Outcomes with every sub-issue closed |
| R6 | 3 | open Outcomes with no open Delivery on the board |
| R7 | 4 | missing fields |
| R9 | 5 | all `orphan-delivery` |
| R2, R4, R8, U | 0 | |

Ten open Outcomes were In progress, and nine of them had every sub-issue closed: the "uninformative"
board the user describes. Root's Delivery backfill
([issuecomment-6540490595](https://github.com/FS-GG/.github/issues/4730#issuecomment-6540490595))
has since created 98 Delivery issues as native sub-issues, which changes the R5 and R6 picture (§5).

The cause is structural, not a lapse of attention: the merge path never asks which Delivery a PR
delivers, and nothing turns "every child is closed" into a decision. This design fixes both
deterministically and leaves judgement with Root.

## 1. Rule set `fsgg:board-currency-rules/v1`

Inputs are native state only:

- issue and PR state and `stateReason`;
- the PR body;
- native parent, sub-issue and blocked-by links;
- the Project 3 fields Status, Work level and Track;
- Root-login marker comments, a closed grammar listed below.

The evaluator's clock is the snapshot's own `fetchedAt`, so one snapshot always gives the same
findings. A finding ID is `<rule>:<subject>:<reason>`. It never contains an age or a count, so an
unchanged situation keeps its ID from one run to the next. The parameters are part of the version:
changing any of them means a new rule-set version.

| Parameter | v1 value | Why |
|---|---|---|
| `activityHours` | 48 | Twice a working day. The observer runs hourly, so a stall is visible within two days, not seven. |
| `mergedWindowHours` | 24 | The window in which every merged routine PR is evaluated by R1. Unlinked and overridden merged PRs are also carried past it (v2, R1 persistence). |
| `carryDays` (v2) | 7 | How long an unlinked merged routine PR stays under R1 after the 24-hour window. About 110 routine merges a day keeps the carry search under GitHub's 1,000-result cap. |
| `carryFrom` (v2) | 2026-11-01T00:00Z | Carrying starts here, so the pre-phase-2 backlog (addressed by the Delivery backfill) is not carried. Root may move it to the actual phase-2 start before merge. |
| `effectiveFrom` | 2026-10-31T00:00Z | R2's acceptance-marker leg does not condemn Outcomes closed before the marker existed. |
| `rootLogins` | `EHotwagner` | The only login trusted for markers. Every agent authenticates as this one account, so a marker proves "Root's account", not "Root's hands". |

### The rules

| ID | Applies to | Exact inputs | Fails when | Reasons (finding ID suffix) |
|---|---|---|---|---|
| R1 | Routine PR (body carries `fsgg:routine-development/v1`): open and non-draft; merged inside the window; or, from phase 2, carried (see persistence below) | PR body refs (`Refs` and closing keywords, ignoring code spans and fences); each ref's Project 3 item; that item's Work level, state, Status and native parent; the override marker and the PR's author and body editor | The PR references zero or more than one P3 Delivery. Or the Delivery has no parent that is a P3 Outcome. Or, while the PR is open, the Delivery is closed or Done. Or the PR carries a valid override. Referencing the parent Outcome as well is allowed. | `no-delivery`, `multiple-deliveries`, `no-parent-outcome`, `delivery-closed`, `delivery-done`, `overridden` (phase 2) |
| R2 | Every issue card | state, `stateReason`, `closedAt`, Status, Work level, Root comments | A closed card is not Done. A Done card is open. An Outcome closed as COMPLETED on or after `effectiveFrom` lacks `fsgg:outcome-accepted/v1`. | `closed-not-done`, `done-open`, `outcome-unaccepted` |
| R3 | Open card with Status Blocked | native `blockedBy` (with its total), newest Root `fsgg:blocked-on/v1` marker, those blockers' states; for an Outcome, its open child Deliveries | No open blocker is named. An Outcome passes if one of its open child Deliveries is Blocked. | `no-blocker`, `blockers-closed` |
| R4 | Open card In progress or In review | Delivery: PRs referencing it, and the activity set below. Outcome: open children's Status. | A Delivery In progress has no open PR (a draft counts) and no activity within `activityHours`. A Delivery In review has no open non-draft PR and no such activity. An Outcome In progress has no open child In progress or In review. An Outcome In review has an open child that is not In review. The Outcome legs apply only when the Outcome has an open Delivery; otherwise R5 or R6 already names the cause. | `stale-active`, `outcome-no-active-child`, `outcome-review-open-child` |
| R5 | Open Outcome | native sub-issues (complete list) | It has at least one sub-issue, and every one is closed. | `children-complete` |
| R6 | Open Outcome that is not already R5 | native sub-issues, their P3 Work level | No open sub-issue is a P3 Delivery. | `no-open-delivery` |
| R7 | Every card | Status, Work level, Track, content type | A field is missing. Or the card is a draft, which is not a native issue, so the other rules cannot reason about it. | `missing-<fields>`, `draft` |
| R8 | Delivery | PRs referencing it | The Delivery is open with an open non-draft PR but is Backlog, Ready or Blocked. Or it is closed and an open non-draft PR still references it. | `open-pr-pre-action`, `pr-open-on-closed` |
| R9 | Open issue card on Project 3 with Work level Delivery; issue card on Project 3 with Work level Outcome | native parent and its P3 Work level | A Delivery has no parent, its parent is off the board, or its parent is not an Outcome. Or an Outcome is a sub-issue of a Delivery. Closed cards, and sub-issues that have no Project 3 card, are out of scope: R9 cannot see them, and a missing Delivery card shows up as R6 on its Outcome. | `orphan-delivery`, `parent-off-board`, `parent-not-outcome`, `outcome-under-delivery` |
| U | Any of the above | n/a | An input needed for a verdict could not be read: the card content; the comments (not read, or a Root comment truncated by the fetch with no marker in its visible part); a blocker's state; a truncated sub-issue or blocked-by list; or a parent card that R1 or R9 needs and that is unreadable. When the board holds an unreadable card with no identity (null content), any reference, parent or open-child lookup that misses the board is Unknown too, because the miss could be that card (v2). Unknown is never a pass ([.github#266](https://github.com/FS-GG/.github/issues/266)). | `unreadable`, `r1-reference-unreadable`, `r1-parent-unreadable`, `r2-comments-unread`, `r3-blockers-unread`, `r4-comments-unread`, `r6-children-unread`, `r9-parent-unreadable` |

**R4 activity set, exactly.** A Delivery's last activity is the latest of: the issue's `updatedAt`;
the Status field value's `updatedAt`; the `updatedAt` of every PR in the snapshot that references the
issue (open, or merged inside the window); and the `createdAt` of every comment on the issue, by any
author. Nothing else counts. In particular the project item's own `updatedAt` does not count: it moves
on any field edit, including an edit that only moves the card. If the comments were not read, the
verdict is `U r4-comments-unread`, never a guess from the remaining signals.

**R1 persistence (rule-set v2).** A merged PR leaves the 24-hour window, but an unlinked or
overridden one must not drop off the report. The carried set is derived from native state on every run,
not from the tracking issue: the issue is public, and a private repository's PR reference cannot be
stored there in a form the fetch could re-read. The fetch adds two searches:

- routine PRs (body contains `fsgg:routine-development/v1`) merged on or after the later of `carryFrom`
  and `fetchedAt − carryDays`;
- every merged PR whose body contains `fsgg:board-gate-override/v1`, at any age (overrides are rare).

R1 evaluates a merged PR outside the 24-hour window when it is inside the carry window, or when it
carries a valid override, and marks the finding `carried`. The finding clears only when the PR body
references exactly one Delivery with a parent Outcome. An unlinked PR without an override drops off
after `carryDays`; an overridden one stays until linked. For each PR whose body matches the override
regex, the fetch reads `author`, `lastEditedAt` and `editor` (one batched GraphQL read) so the authorship
rule can be checked. If that read fails or is incomplete, the whole fetch fails and writes no snapshot,
so an override is never silently dropped and reported as an ordinary R1 failure. The carry search runs
one day at a time (about 110 routine merges a day), so GitHub's 1,000-result cap cannot wipe a run; a
single day at the cap still refuses rather than reading a truncated day. The evaluator stays a pure
function of the snapshot. `overridden`, carrying and the identity-less Unknown change verdicts, so they
are rule-set version 2.

### Why each rule, and how it refines the brief

- **R1 is the rule the merge gate enforces.** "Exactly one Delivery" makes each merge attributable,
  and that is what puts "the work of the last day" on the board. Outcome refs stay allowed beside the
  Delivery, because PRs legitimately cite context. A Delivery with no Outcome parent fails, because
  otherwise the work exists but rolls up nowhere.
- **R2 records acceptance differently per level.** A Delivery's acceptance is its native closure
  (pnext-item: "close only its fulfilled promise with the correct native reason"), so no new Delivery
  marker is needed. An Outcome's closure has repeatedly happened without the whole promise being
  checked, so it needs an explicit Root marker. The marker leg is not retroactive.
- **R3 uses native blocked-by first.** GitHub's issue dependencies are native, machine-readable and
  writable (`addBlockedBy`). The marker is a transition aid for blockers outside GitHub, and even
  then the blocker must be an issue. "Every blocker is closed" is the actionable signal: the card
  should move.
- **R4 is the deterministic form of drift rule (c),** with 48 hours instead of 7 days. A Delivery
  whose PR merged and that waits for acceptance becomes `stale-active` 48 hours after the last
  activity. **That is intended:** it is exactly the "deliveries done yet still open" case, and the
  finding says acceptance is owed. It clears when Root closes the Delivery (acceptance) or anyone
  records activity.
- **R5 and R6 are disjoint,** so one cause gives one finding. R5 cannot decide between "propose
  closure" and "remaining Deliveries are missing"; that decision is Root's. The finding names both
  resolutions. A Root `fsgg:outcome-closure-proposed/v1` marker keeps the finding but tags it
  `pending: closure-proposed`, which separates findings waiting on Root from findings nobody has
  triaged.
- **R7 adds drafts,** because a draft has no state, parent or links and cannot be held to any other
  rule.
- **R8 keeps only the deterministic parts of the brief's R8.** "All PRs merged means Done" is not
  deterministic: a Delivery may need another PR, or a consumer effect after the merge. Closure decides
  Done (R2). The merge-time transition in §2(b) writes the deterministic part.
- **R9 is new.** Without hierarchy integrity, R5 and R6 undercount: a Delivery that is not linked to
  its Outcome makes the Outcome look empty.

### Marker grammar

Markers are HTML comments and count only outside a code span or fence. Issue markers count only in
a comment by a `rootLogins` author. PR-body markers count only under the authorship rule given for the
override below.

| Marker | Meaning |
|---|---|
| `<!-- fsgg:outcome-accepted/v1 -->` | Root accepts the Outcome's whole promise (R2). |
| `<!-- fsgg:outcome-closure-proposed/v1 -->` | Root or a worker proposes closure; the finding stays, tagged pending (R5, R6). |
| `<!-- fsgg:blocked-on/v1 OWNER/REPO#N … -->` | The blockers that are not native links. The newest marker wins, and an empty marker clears (R3). |
| `<!-- fsgg:delivery-continues/v1 -->` | In a PR body: after this merge the Delivery stays In progress instead of In review (§2b). |
| `<!-- fsgg:board-gate-override/v1 rule=R1 reason="…" -->` | In a PR body: a recorded override of the R1 gate. Exact grammar below. |

**Override grammar, exactly.** The PR body, with CRLF and lone CR line endings first normalised to LF
and then code spans and fences removed, must match this regular expression (Python `re`, `MULTILINE`)
**exactly once**:

```text
^<!-- fsgg:board-gate-override/v1 rule=R1 reason="(?P<reason>[^"\n]{1,200})" -->$
```

The reason must also contain at least one non-whitespace character. Two or more matches, a reason that
is empty or only whitespace, any other `rule=` value, or any other spelling is not an override; the PR
is then judged as if no marker were present. The marker counts only when the PR's `author.login` is in
`rootLogins` and, if the body has ever been edited, the GraphQL `PullRequest.editor.login` (the last
body editor) is also in `rootLogins`. Every agent shares the Root account, so this proves "Root's
account", which is the repository's stated trust model: prevent mistakes and drift, not adversarial
writers.

## 2. Enforcement points

### Which control binds

**The binding control is the merge-time gate in `tools/routine-delivery.py` (b).** Every Root merge
goes through it, and it already makes `routine-eligibility` binding the same way: `routine_contract`
reads that check run from the API and refuses without it. Neither `routine-eligibility` nor the PR
check (a) is a required status context on `main`. Main's protection, read 2026-10-31, requires eight
contexts, all from app 15368 (GitHub Actions): `contract-coherence / coherence`, `projection`,
`roster-closure`, `drift`, `claim-generation`, `Lint every shell file in the repo (pinned shellcheck)`,
`claim-fence` and `architecture-map reconcile`. So a merge made outside `routine-delivery.py` is not
stopped by (a) in any phase unless Root adds it to protection.

**Required mode for (a) is a Root settings step,** not a code change. Root adds
`{"context": "board-linkage", "app_id": 15368}` to `main`'s `required_status_checks.checks` and reads
it back with `GET repos/FS-GG/.github/branches/main/protection/required_status_checks`, which must list
it with that `app_id`. Pinning `app_id` stops another app from satisfying the context by name.
`enforce_admins` is true, so once `board-linkage` is required a red `board-linkage` blocks every merge,
admins included, through any route. That is why the override must make (a) itself pass. There is no
bypass to fall back on.

### Evaluation order (both (a) and (b))

1. Read the PR: body, author, last body editor, head. This read is already part of both paths.
2. **Override first.** If the body carries a valid override (grammar and authorship in §1), the verdict
   is `overridden`. Project 3 is not read for the verdict, so an unreadable board cannot block an
   override. (b) merges and records `boardGate: overridden`; (a) passes with a notice.
3. Otherwise evaluate R1 from one GraphQL query that resolves each ref's P3 item, Work level, state,
   Status and parent.
4. If that read fails or is incomplete, the verdict is `unknown`. (b) returns `outcome: indeterminate`
   with reason `board-linkage unknown` and makes no merge (fail-closed). (a) fails in required mode and
   writes a notice in advisory mode.
5. If R1 fails, (b) returns `outcome: refused` with reason `board-linkage R1 <reason>`, makes no merge
   attempt and leaves `attempts` unchanged. (a) fails in required mode and writes a notice in advisory
   mode.
6. Otherwise the verdict is `linked`.

### (a) PR check `board-linkage`

This is the evaluation order above for one PR, in `FS-GG/.github` only, implemented by
`scripts/board-linkage.py` and run by `.github/workflows/board-linkage.yml`.

- **Its own light job.** The job is named `board-linkage`, because only a job publishes a context that
  branch protection can require. It is a separate workflow, not a job in `routine-eligibility.yml`:
  that file is a producer file distributed to receiver workspaces
  (`src/FS.GG.Drivers/workspace-inventory.json`). Receivers have no App secret, and changing producer
  bytes reopens the producer-installation contract. The workflow uses `pull_request_target`
  (`opened`, `synchronize`, `reopened`, `edited`, so fixing the body re-runs it) with PR-keyed
  concurrency. It checks out only the default branch, never the candidate, and reads the PR body
  through the API as data. It reads Project 3 with a read-only App token: organization-projects,
  issues and pull-requests read, the same mint as `board-drift-read`.
- **Non-routine PRs pass.** A PR whose body lacks `fsgg:routine-development/v1` is reported `skip` and
  the job succeeds, in every mode. Otherwise a required `board-linkage` would block every non-routine
  merge under `enforce_admins`. The skip is decided from the event payload in the first step, before
  the App token is minted; the token mint and the evaluation run only for a routine PR. An App outage
  therefore cannot block a non-routine merge. An unreadable payload is treated as routine, so the full
  check (and its Unknown) decides rather than a silent skip.
- **Rule-set v2.** The check imports the observer's own `override_of()` and `linkage_of()`, so the
  check, the gate and the observer cannot disagree. A valid override gives `overridden` without
  reading Project 3. The carry of unlinked and overridden PRs past the 24-hour window is the observer's
  side of the same v2 rules (§1, R1 persistence): the verdict this check gives an open PR is the verdict
  the observer keeps reporting after the merge until the PR is linked.
- **Advisory in phase 2.** It runs `--mode advisory`: every verdict exits 0, and `refused` or `unknown`
  writes a warning annotation. Required mode (`--mode required`: exit 1 on `refused` or `unknown`) is
  used only after the Root settings step above.
- **Privacy.** Output names the PR, the verdict and the reason, never a referenced issue (it may be
  private) and never body text.

Product repositories do not get this check in this design, because they have no App secret. They are
covered by (b), which every Root merge passes through.

### (b) Merge-time gate in `tools/routine-delivery.py`

`summarize()` already re-reads the live PR before it merges. The gate runs the order above before the
merge effect, only when `--apply` is given and the PR carries the routine marker.

After the existing merged-PR and merge-commit readback, make exactly one deterministic Delivery
Status transition through the guarded-write semantics of the
[Status lifecycle](../../.agents/skills/work-board/references/status-lifecycle.md):

| After merge | Target Status |
|---|---|
| The Delivery is closed (for example by a closing keyword) | **No write.** Done has one writer: the built-in "Item closed → Done" project workflow (below). |
| Open, Refs-only, no `delivery-continues` marker | In review (implementation merged; acceptance or consumer effects remain) |
| Open, Refs-only, with `fsgg:delivery-continues/v1` | In progress |

The merge-time write never sets Done. Root decided that closing the issue is the acceptance act and
that the built-in workflow moves a closed item to Done
([issuecomment-6540490608](https://github.com/FS-GG/.github/issues/4730#issuecomment-6540490608),
decisions 2 and 4). One writer for Done means no race between the two.

The write follows these steps:

1. Take the preimage from a fresh snapshot.
2. Treat an equal value as a no-op.
3. Write only from a preimage of Backlog, Ready, In progress or In review. Refuse to write, and report
   `boardTransition: refused-preimage`, when the preimage is Done, empty or unreadable, **or Blocked**.
   Blocked records Root's judgement that a named prerequisite is missing; a merge does not prove that
   prerequisite was met, so the gate does not overturn it. The merge stands, the merge result names
   `refused-preimage` for Root to decide, and the observer reports the card under R3 if its blockers are
   closed or unnamed.
4. Record possible-send in the result, then send one `gh project item-edit`.
5. Read back independently and verify with `require_preimage` and `verify_field_transition` from
   `scripts/board_v2_native_results.py`.
6. If the send or readback fails or is uncertain, report `boardTransition: uncertain`. Never resend.

This makes the same-turn Status rule automatic for the one transition a merge determines. The parent
Outcome is never rolled up automatically.

**As implemented** (`--board-gate`, #5094). The mode is `advisory` by default and `enforce` from phase
3; advisory computes the same verdict and preimage decision but never refuses and never writes. The
result carries `boardGate` (`linked`, `overridden`, `refused`, `unknown`, `unavailable` or
`not-applicable`), a fixed-vocabulary `boardGateReason`, and `boardTransition` (`noop`, `written`,
`refused-preimage`, `uncertain`, `not-applicable`, or advisory `would-write:<Status>` and
`would-refuse-preimage`, plus `refused-custody` in enforce). These points refine the steps above:

- `scripts/board-currency-rules.py` is not distributed to receiver workspaces. Where it is absent the
  gate reports `unavailable`: advisory records it, and enforce returns `indeterminate` (fail-closed).
- A Delivery that the PR names with a closing keyword is treated as closed: GitHub closes it
  asynchronously after the merge, so writing to it could race the built-in Done workflow. Like a closed
  Delivery, it gets no write (`not-applicable`).
- `require_preimage` and `verify_field_transition` decode the whole-project `migration-prepare`
  snapshot, which is far more than one merge needs. The gate applies the same guard to the selected item
  alone. The compared state is the item's complete reported field-value set, its item and project
  identities, and the live Status field with its options (the option ID is resolved from that schema).
  The gate requires an adjacent re-read to equal the preimage before the single send. It reports
  `written` only when the readback equals the preimage with Status alone moved to the target.
  **Root accepted this item-scoped substitute guard for this writer** (#5106 review): for the selected
  item it is at least as strict as the whole-project helpers, and its scope is disclosed here.
- **Durable possible-send custody.** Before the send, enforce writes one JSON record per (repository,
  PR) to a private directory (`--board-gate-custody`, default `$XDG_STATE_HOME/fsgg/board-gate`). The
  record carries the Delivery, item, preimage and target, and is keyed by repository and PR alone so a
  later run can find it before it knows anything else. The directory must be owned by the running user and
  is tightened to mode 0700. The record is written atomically and fsynced, with the directory fsynced too.
  If it cannot be written, nothing is sent (`refused-custody`). After a readback that proves the target,
  the record is atomically rewritten to `verified`. The record holds identities and Status names only.
- **Crash recovery is read-only.** A run that died after the send leaves a `possible-send` record on a PR
  that is now merged. The next enforce run takes the already-merged path, finds the record, and reads back
  without sending: `boardGateReason: custody-readback` with `boardTransition: written` (Status equals the
  recorded target, record rewritten to `verified`) or `uncertain` (anything else, including a failed or
  malformed read; the record stays). A `verified` record reports `written` with no read. As defence in
  depth, the write path also refuses to overwrite an existing record (`uncertain`, no send).
- **Enforce runs only from the FS-GG/.github checkout.** `--board-gate enforce` is refused, by `main()` and
  again by `summarize()` itself, unless the running copy sits in a checkout that holds
  `scripts/board-currency-rules.py` and whose `origin` is exactly FS-GG/.github on github.com. The URL is
  parsed, not suffix-matched: `https://github.com/FS-GG/.github[.git][/]` with no userinfo, port, query or
  fragment, `ssh://git@github.com/FS-GG/.github[.git]`, or `git@github.com:FS-GG/.github[.git]`; host and
  path compare case-insensitively. The receiver copy distributed by the drivers package can therefore never
  enforce, and the default in the distributed bytes stays `advisory` (a test and a mutant pin it).
  Flipping that default is not how phase 3 starts: Root runs the `.github` copy with `--board-gate enforce`.
- **A PR is never a Delivery.** A reference that resolves to a pull request counts as off the board in
  all three places: the merge gate, the board-linkage check and the observer's `linkage_of()`.
- **An overridden PR gets no Status transition.** Only a `linked` verdict names a verified Delivery to
  move. An override records `boardGate: overridden` and `boardTransition: not-applicable`.

### (c) The hourly observer

It evaluates every rule over a full snapshot and maintains one tracking issue holding the current
finding set. In phase 1 it only wrote a job summary (`board-currency-shadow.yml`).

**Phase 2 (converged).** The board-drift observer is the single writer. Its read job takes a second,
board-currency snapshot (`board-currency-fetch.py`), and its keyed check step evaluates the rules
(`board-currency-rules.py check`). `board-drift-observer.py check --currency` merges the findings through
the same private-placeholder gate as drift findings, so a subject in a private or unknown-visibility
repository appears only as `private:<HMAC>`. The report job keeps one issue, **#4985, renamed "Board
findings: Project 3"** (Root decision 3). The report still recognises the legacy title "Board drift:
Project 3" and renames it on its next run, even when nothing else changed. Drift and currency findings
share one body and one digest. A comment is posted only when the drift set changes or a new non-R1
currency finding appears (Root decision on the #5103 review). R1 changes with almost every merge, so R1
is shown in the body only, as a count plus a list. A change that touches nothing else rewrites the body
without a comment and without reopening a closed issue. Currency findings have no ack route: each
clears when the native state it reads is fixed. A failed currency read leaves the run red and the issue
untouched, as a failed drift read does. The shadow workflow is retired, so the net change is one job
fewer per hour. Failing the run on new finding IDs is phase 3.

### Which writes are automatic

| Write | Who | Automatic? |
|---|---|---|
| Delivery Status after merge (table above) | `routine-delivery.py` as the merging credential | Yes: deterministic, guarded, read back |
| Status Done when an issue closes | GitHub's built-in project workflow "Item closed → Done", the **only** writer of Done | Yes. Enabled by the owner (decision 2); read back `enabled: true` on 2026-10-31 |
| Sub-issue added to the project | GitHub's built-in "Auto-add sub-issues to project" | **No.** Turned off by the owner and read back `enabled: false` on 2026-10-31 ([issuecomment-6540378933](https://github.com/FS-GG/.github/issues/4730#issuecomment-6540378933)); cards come from batched enrollment |
| Accepting an Outcome (marker plus close) | Root | **No.** This is a Root decision |
| Closing a Delivery | Root, or a closing keyword the PR author chose | No, except through the closing keyword |
| Any other Status (Blocked, Ready, Backlog, Outcome Status) | Root | No. Findings tell Root; Root writes |
| Creating the remaining Deliveries (R5, R6) | Root, through canonical intake and enrollment | No |
| Tracking issue | Observer, using `GITHUB_TOKEN` with `issues: write` only | Yes (report only) |

## 3. Authority and safety

- **Project field writes.** Phase 3 uses the credential that already performs the merge: Root's PAT
  through `gh`, which already makes Root's guarded Status writes. No new writer identity is introduced.
  The App `fs-gg-cross-repo-dispatch` holds `organization_projects: write` (pinned in
  `permission-coherence.yml`) but stays read-only for this design. Its board use here is read-only
  mints for the PR check and the observer, so a CI job never writes a board field.
- **Guarded writes.** Use the existing guard semantics (preimage, then send, then independent
  readback), the existing cooperative single-writer window, and no automatic retry.
- **Rate limits.**
  - Merge gate: about 1 GraphQL point before the merge, plus 1 read, 1 mutation and 1 readback after
    it. That is about 4 points of the PAT's 5,000 per hour per merge, or about 450 points a day at the
    current rate of about 110 merges a day.
  - Observer: runs on the App installation budget. Per hour it costs 2 to 3 item pages of about 2
    points, 2 search calls, about 25 comment reads and at most one batched state read.
  - PR check: about 1 point per `routine-eligibility` run.
- **Failed reads.**
  - Gate: an Unknown refuses the merge, which is fail-closed.
  - Observer: a failed read is a red run that leaves the tracking issue untouched (the board-drift
    precedent).
  - PR check: an Unknown is a notice while advisory and a failure once required.
- **Override.** `enforce_admins: true` means there is no admin bypass, so the override is data, not
  privilege: the exact marker, non-empty reason and Root authorship defined in §1. Its effects:
  - (a) and (b) evaluate it first, before any Project 3 read (§2, evaluation order).
  - The gate records `boardGate: overridden`.
  - The observer reports `R1:<pr>:overridden`, at any age, until the PR body references exactly one
    Delivery with a parent Outcome (§1, R1 persistence).

  An override is therefore never silent and never expires on its own.

## 4. Rollout

| Phase | What runs | Entry | Exit |
|---|---|---|---|
| 1, shadow (#5035) | Pure evaluator; `board-currency-shadow` runs after each `board-drift-observer` completion and writes only its job summary | Rule set v1 approved (decision 1) | Root reviews about 3 days of counts and false positives; batched enrollment lands |
| 1a, follow-up | The phase-1 Unknown corrections (§1, U row) and their tests | #5035 merged | Before phase 2 |
| 2, advisory | (a) as the `board-linkage` job, which only writes notices; (b) logs the would-refuse verdict without refusing; (c) moves to tracking issue #4985; R1 rule-set v2 (`overridden`, carrying) | Root approval on #4730 | Open-PR R1 near zero for 2 days; the override path exercised once in a test |
| 3, required | (a) fails on R1 or Unknown, and Root adds `board-linkage` (app 15368) to `main` protection with readback; (b) refuses and performs the post-merge transition; (c) fails the run on new IDs | Root approval | n/a |

Drift rules map onto these rules as follows:

| board-drift rule | board-currency rule |
|---|---|
| a | R8 `open-pr-pre-action` |
| b | R2 |
| c | R4 |
| d | R7 |
| x | U |

Rule e, the ruling heuristic, has no deterministic counterpart. In phase 2 both observers report to
one tracking issue: #4985, renamed to the board-currency findings issue (decision 3). Drift rule s from
#4990 (schedule watch; open at the time of writing, not merged) stays with the drift workflow.

**Retiring the duplicated conditions.** In phase 2 the mapped pairs report the same card twice in one
issue. The pairs are not identical:

| Pair | How they differ |
|---|---|
| a / R8 | a covers any card a fresh PR refers to; R8 covers only Deliveries |
| b / R2 | b also covers PR cards; R2 also covers Outcome acceptance |
| c / R4 | c fires after 7 days; R4 fires after 48 hours, with Outcome legs |
| d / R7 | the same condition |
| x / U | U is the wider of the two |

Both rules in each pair stay until Root retires them. The plan:

1. **Observe.** Run phase 2 for at least seven days. For each pair, compare the IDs from the same run:
   every card a drift rule names should also be named by its currency counterpart.
2. **Decide.** Root rules on each pair separately. For each card a drift rule names that its counterpart
   does not, Root either records the reason (for example, a or b on a PR card, which the R rules do not
   cover by design) or asks for a currency rule change. A rule change is a new rule-set version.
3. **Retire.** One PR per approved pair removes the drift rule, its ack route and its tests, keeping the
   rest of the drift observer. Rule d and rule x go first, because their pairs are identical or the
   currency rule is wider. Rules e and s are not duplicates and stay.

Until a pair is retired, its drift findings keep their ack route and the currency findings keep none.

## 5. Interaction with existing pieces

- **Auto-add (built-in, now off).** The owner turned it off on 2026-10-31 because unattended cards
  broke `migration-prepare` and `inspect`. New sub-issues therefore have no card until batched
  enrollment (the #4941 planner, after the coordination CLI release that lifts the row cap). Until then
  the rules cannot see the 98 backfilled Delivery issues: an Outcome whose only open sub-issues lack
  cards is R6 `no-open-delivery`, and an Outcome whose carded children are all closed is not R5 while
  card-less open sub-issues exist. Expect R5 to fall and R6 to rise until enrollment lands. The rules
  never add or remove membership.
- **Board-drift observer.** It is unchanged in phase 1. The shadow workflow chains on its completion
  through `workflow_run`. GitHub drops scheduled runs and fdev dispatches the observer hourly, so this
  gets the same cadence with no second schedule or dispatcher.
- **`routine-eligibility`.** Unchanged. (a) is its own workflow (`board-linkage.yml`), so the
  distributed producer file and its envelope are untouched. Their refusals stay independent.
- **`routine-delivery.py`.** It hosts (b). The gate runs before the merge effect, and the transition
  runs after the existing readback. Neither changes publication, telemetry or evidence behaviour.
- **CI load.** Phase 2 retires the phase-1 shadow job: the currency read runs as one more step in
  `board-drift-read`. Phase 1 added one light job per observer run and one step in the existing
  `board-drift-observer-selftest` fixture job. Phase 2 adds one light PR-triggered job, `board-linkage`
  (no candidate checkout, one GraphQL read, PR-keyed concurrency). That is the gate's only PR-triggered
  cost, and it has to be a job to be requirable.

## 6. Root's recorded decisions

From [issuecomment-6540490608](https://github.com/FS-GG/.github/issues/4730#issuecomment-6540490608),
2026-10-31:

1. Rule set `fsgg:board-currency-rules/v1` is approved as the phase-1 basis; phase 1 goes to
   independent review before merge. The phase-2 R1 additions (`overridden`, carrying, identity-less Unknown) are version 2
   and need their own approval with phase 2.
2. The built-in "Item closed → Done" project workflow is turned on (owner UI action; read back
   `enabled: true` on 2026-10-31). It is the only writer of Done (§2b).
3. Tracking issue: reuse #4985, renamed to the board-currency findings issue.
4. Post-merge Status: In review by default, or In progress when the PR body carries
   `fsgg:delivery-continues/v1`. Done follows Root's acceptance, which is closing the issue.
5. This work's own Delivery issue is created as a native sub-issue of #4730 and enrolled with the batch
   after the coordination CLI 0.114.0 release lifts the row cap. Until then this work's PRs say
   `Refs #4730` and are themselves R1 `no-delivery` findings.

Still open for Root: required mode for (a), which is the settings step in §2 and is taken only at
phase 3.
