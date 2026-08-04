# GitHub-native operation keying and effect fencing — the design gate for `.github#1858`

- **Created:** 2026-08-04
- **Item:** [`.github#2210`](https://github.com/FS-GG/.github/issues/2210) (`Class: hardening`, `Severity: High`), the design gate for [`.github#1858`](https://github.com/FS-GG/.github/issues/1858) (`Class: defect`, `Severity: Critical`)
- **Author:** worker `godwit-fb4a`
- **Status:** design for review. **No enforcement code, broker, or merge gate lands under `#2210`** — that is its acceptance criterion 7. The deliverable is this document.
- **Deliverable location:** `docs/reports/`, not `docs/decisions/`. `#2210` leaves the choice to the author and asks it to be stated: `docs/decisions/` does not exist in this repository, and the org's decision record is `docs/adr/`, which is governed by `scripts/check-adr-coherence.py` and an index this item's touch-set does not include. Filing a pseudo-ADR outside that corpus would create exactly the second-copy drift ADR-0034 §4 names. The ADR-worthy decisions here are listed in §11 and belong to the implementation slices that make them.
  *Verification:* `ls docs/` — no `decisions` entry; `ls docs/adr | tail -8` → `0068`…`0073`, `README.md`, `template.md`; `Paths: docs/reports/ docs/decisions/` on `#2210`.

---

## 0. Evidence discipline

Every specific, checkable assertion below carries `Verification:` — a command, a `file:line`, an API call, or a URL — or the exact word `unverified`. `unverified` is first-class here and non-pejorative. A design that asserts how GitHub behaves without checking is the failure mode `#266` names, and this design is about a mechanism that failed while everyone was acting reasonably, so an unchecked premise is the most expensive thing it could contain.

Facts read live against the GitHub API on 2026-08-04 are marked as such, with the exact call. Two of them contradict statements committed in this repository; both contradictions are recorded rather than silently resolved (§12).

---

## 1. What happened, stated as mechanism

`#1858` records that two executors carried `.github#1853` to completion concurrently against seven receiver repositories while the board showed exactly one claim marker, and that two of the resulting pull requests were **merged** by the executor that held no lock.

*Verification:* [`#1858` body](https://github.com/FS-GG/.github/issues/1858) — the artifact table and the "not mine" column; `gh issue view 1858 --repo FS-GG/.github --json body,comments`.

Three separate findings on that issue constrain any remedy, and each one kills a family of otherwise-plausible designs.

### 1.1 The second executor never called a coordination verb

The self-identification comment is unambiguous: *"I held **no** claim, minted **no** id, and did not read `#1853`'s claim marker before acting… I never ran a lock verb at all — so no guard, correct or otherwise, ever got the chance to fire at me."*

*Verification:* https://github.com/FS-GG/.github/issues/1858#issuecomment-5109701014

**Consequence.** Every guard that lives inside `fsgg-coord` is unreachable by this defect. `#1938` measured the same boundary independently: *"`scripts/fsgg-coord-guards.sh` is sourced only by `scripts/fsgg-coord`, while the second context ran `git`/`gh` directly. `take`/`claim` already refuse a distinct executor that calls them; they cannot protect a process that never calls them."*

*Verification:* `gh issue view 1938 --repo FS-GG/.github --json body`

The enforcement point must therefore sit where the **effect** lands, not where the tool is invoked. There are exactly two such points that GitHub can see: the merge of a pull request, and the arrival of a cross-repo dispatch.

### 1.2 The two executors were indistinguishable by session — by construction, not by accident

The binding note on `#1858` states the root cause as the shared Claude Code session id collapsing all subagents onto one worker identity (`#419`): *"The marker recorded one worker because, as far as the protocol could tell, there **was** one."*

*Verification:* https://github.com/FS-GG/.github/issues/1858#issuecomment-5156536308

This is not a harness quirk to be worked around; it is documented as structural. Claude Code exports one `CLAUDE_CODE_SESSION_ID` per **session**, and *"Subagents spawned by the Task tool **share the parent's `session_id`**"*, confirmed directly in this org by two concurrently spawned subagents reporting an identical session id and an identical `PPID`. The per-subagent `agent_id` exists only inside hook input JSON and *"is not exported into the environment a Bash tool can read, so a script like `fsgg-coord` cannot see it."*

*Verification:* `docs/coordination/agent-session-identifiers.md` — the harness table and the "Claude Code: one session id, N subagents" section; upstream [anthropics/claude-code#7881](https://github.com/anthropics/claude-code/issues/7881).

The engine's own twin guard concedes the same boundary in code. `twinSession` returns a twin **only when both sessions are known and differ**:

```fsharp
let private twinSession (ours: SessionId option) (theirs: SessionId option) : SessionId option =
    match ours, theirs with
    | Some(SessionId o), Some(SessionId t) when o <> t -> Some(SessionId t)
    | _ -> None
```

*Verification:* `src/FS.GG.Coord.GitHub/Writes.fs`, `twinSession` (the function immediately following `claimMarker`, whose emitted marker is at `src/FS.GG.Coord.GitHub/Writes.fs:115`).

Two executors sharing one session are, to this predicate, never twins. **No amount of tightening reaches them.** A design that improves marker-writing, improves twin detection, or improves what the marker records is answering a different question.

There is also a smaller, verified gap worth not building on: `docs/coordination/agent-session-identifiers.md` states that *"the claim marker records `harness=<name> session=<id>`"*. The emitted marker records `worker=`, `lease=`, `session=`, `prev=` and `pathRepo=`; there is no `harness=` field anywhere in the engine.
*Verification:* `src/FS.GG.Coord.GitHub/Writes.fs:115`; `grep -rn "harness=" src/` → no matches. This design does not rely on `harness=` existing.

### 1.3 Detection happened, cooperation happened, and duplication happened anyway

The 2026-07-28 addendum measured both executors pressing the same four receivers minutes apart, **after** the second had read the first's status comment. Its finding is not carelessness: *"That comment's 'Still NOT proven' section listed those four receivers as having no button yet — true when written at 21:00, false by 21:02 … **The only channel the two executors had was a static issue comment, and a static comment describing live fleet state is stale within minutes.** Each of us read the other's most recent word and acted correctly on information that had already expired."* And: *"The lock is per **item**; the contention here was per **receiver repository**, which no marker on `#1853` describes."*

*Verification:* https://github.com/FS-GG/.github/issues/1858#issuecomment-5109768017

**Consequence.** Correct action on expired information is still duplication. A better *channel* does not fix this, because the failure was not a communication failure — both parties communicated, and both acted correctly on what they had. What was missing was a **lock at the granularity of the contention**, which was the receiver, not the item.

---

## 2. The design principle

Everything below follows from one inversion.

> **Stop trying to identify the executor. Make the effect prove its own authorization, using an identity only GitHub can issue.**

The claim lock separates **markers**, and markers separate **workers** only while worker ids are distinct — the engine says so in its own generated contract: *"That total order is over MARKERS, and it separates WORKERS only while their ids are DISTINCT."*
*Verification:* `src/FS.GG.Coord.Core/Protocol.fs:545`, projected into `.claude/skills/pnext-item/references/command-contracts.md`.

So the design never compares a worker id, a session id, a process, or a machine. Instead:

**Authorization is a capability GitHub issues, not a property the executor asserts.** Before a consequential effect, the acting party must obtain a **grant** — a marker comment posted through the existing comment-order CAS. The grant's identity is the **GitHub-assigned comment id**. Nobody can mint one locally, nobody can choose its value, and nobody can forge its ordering.

This is what makes §1.2 tractable. Two executors that are indistinguishable by session are still perfectly distinguishable by *which comment id GitHub gave them*, because GitHub issues comment ids from a single server-side sequence:

> *"GitHub issues comment ids from a single server-side sequence, so this is a total order that every racer observes identically — which is what makes the comment-order CAS a real compare-and-swap rather than a hopeful convention."*

*Verification:* `src/FS.GG.Coord.GitHub/Reads.fs:360-374` (the doc comment on `winner`).

The comment-id sequence is the **only identity source in this system that a shared session cannot collapse**, because it is not derived from the executor at all. Keying on it is what lets the design satisfy `#2210` AC2 without ever solving the identity problem `#1938` assigned to the harness.

And it turns §1.1's bypass into a non-event: an executor that never called a coordination verb **holds no grant**, so it has nothing to present at any fence, so every fence it reaches is red. No comparison, no detection heuristic, no twin check — an absent capability, refused.

---

## 3. Question 1 — the operation key

**Key every consequential operation by `(item, claim generation, receiver, operation)`.**

| component | value | source |
|---|---|---|
| `item` | `owner/repo#N`, fully qualified | the issue |
| `gen` | the **comment id of the winning `fsgg:claim` marker** on that item | GitHub |
| `receiver` | `owner/repo` of the repository the effect lands in | the operation |
| `op` | a literal from a closed vocabulary: `merge`, `dispatch:<event-type>`, `publish:<package>` | the operation |

`item` is spelled `owner/repo#N` and never the board's `<repo>#N` shorthand. That shorthand is not GitHub grammar, and `.github#2107` is what it costs when it reaches a field GitHub parses.
*Verification:* `.claude/skills/pnext-item/SKILL.md` §6; `tests/closing-keywords/`.

### 3.1 Claim generation is already defined, and already server-assigned

This is not new machinery. The engine's delivery path already defines the claim generation as the winning marker's comment id:

```fsharp
ClaimGeneration = marker |> Option.map (fun held -> string held.Id) |> Option.defaultValue "released"
```

*Verification:* `src/FS.GG.Coord.Cli/Client.fs:1045`.

Three properties follow, all free:

1. **Server-assigned.** No executor picks it.
2. **Monotone.** A released-then-reclaimed item gets a strictly higher id, so **ABA is closed by construction** — a stale authorization can never coincidentally match a later tenancy of the same item.
3. **Total-ordered identically for every racer.** `Reads.winner` sorts before taking the head rather than trusting its input order, precisely so *"two racers each compute a different winner and both believe they hold the lock"* cannot happen.
   *Verification:* `src/FS.GG.Coord.GitHub/Reads.fs:375-379`.

### 3.2 The three identities, kept distinct

`#1858` replacement-plan step 1 requires worker id, executor id, and claim generation to stay distinct. They are distinct here because they have three different **issuers**:

| identity | value | issued by | what it is for | what it must never be used for |
|---|---|---|---|---|
| **worker id** | `godwit-fb4a` | minted locally (`whoami --mint`) | human-readable addressing (`say --to`), provenance in `who` | any authorization decision |
| **claim generation** | winning `fsgg:claim` comment id | **GitHub** | which *tenancy* of the item an effect belongs to | identifying *who* is acting |
| **executor identity** | the **grant's** comment id (§4) | **GitHub** | the only identity a fence trusts | anything that outlives the grant's lease |

The inversion that matters: **executor identity is not an attribute of the process — it is a capability the process holds.** Two contexts of one session, one worker id, one machine, can each obtain a grant; GitHub gives them two different comment ids; exactly one is the winner. Neither had to know the other existed.

### 3.3 Wire form

`opkey = sha256(item \n gen \n receiver \n op)`, lowercase hex.

Reuse `Delivery.digest` rather than writing a second one — one rule computed in two places agrees at first and drifts later (`#485`).
*Verification:* `src/FS.GG.Coord.Core/Delivery.fs:79` (`digest`) and `:86` (`freshnessToken`), which already composes exactly this way.

---

## 4. Question 2 — the durable marker

There are three markers, and **the distinction between them is load-bearing**: exactly one of them flows through the claim CAS, and the other two must never be read by it.

| marker | read by | may it introduce a new prefix? |
|---|---|---|
| the **grant** (§4.1) | `Reads.winner`, the CAS's own arbiter | **No.** It must be a `fsgg:claim` marker or the CAS cannot see it. |
| the **effect receipt** (§4.2) | a purpose-built idempotence reader | Yes — it decides no lock. |
| the **PR authorization** (§6.3) | the merge gate | Yes — it decides no lock. |

Every one of them is an anchored HTML comment, so a marker merely *quoted* in prose is not a marker — the discipline `Reads.fs` already applies, whose comment explains that an un-anchored pattern lets a body that quotes a marker *"forge a lock on the item it is posted"* on.
*Verification:* `src/FS.GG.Coord.GitHub/Reads.fs:220-226`.

### 4.1 The grant — a `fsgg:claim` marker on the receiver's operation-lock issue

**The grant is an ordinary `fsgg:claim` marker, posted by `Writes.claim` unchanged, on a dedicated per-receiver operation-lock issue.** It introduces no prefix, no field, and no parameter.

*(An earlier draft of this section specified a distinct `fsgg:op-grant` prefix while simultaneously quoting ADR-0041's "No new marker prefix". That was incoherent, and it was also unimplementable: `markerBody` hardcodes the claim prefix with `worker=` as the first key (`src/FS.GG.Coord.GitHub/Writes.fs:91-115`), `markerRe` and `workerRe` are anchored on exactly that (`src/FS.GG.Coord.GitHub/Reads.fs:222-226`), and a body that misses `markerRe` is classified `NotAMarker` and dropped (`src/FS.GG.Coord.GitHub/Reads.fs:283`). A differently-prefixed grant would have been neither writable by `Writes.claim` nor visible to `Reads.winner`. The correction is below and it is ADR-0041's own answer.)*

The lock issue is closed, *unlocked*, off-board, one per repository. The grant **is** its comment id; the holder is `Reads.winner leaseMinutes` over that issue's markers, unchanged.

**This costs the GitHub layer no code, and that is checkable rather than asserted.** `Writes.claim` is already public, already takes an arbitrary `ref: Ref`, and already takes `readPreviousStatus` and `readPathRepo` as caller-supplied callbacks — so a new caller supplies a lock ref and two stubs and is done.
*Verification:* `src/FS.GG.Coord.GitHub/Writes.fsi:294-305` (`val claim`, with `ref: Ref` and both callbacks in its signature). Slice 2's `Paths:` in §11.2 is scoped accordingly: `Options.fs` and `Client.fs`, not `Writes.fs`/`Reads.fs`.

This is ADR-0041's decision applied a third time. That ADR's finding was that `Writes.claim` *"is **already a general comment-order CAS over an arbitrary issue ref.** It is not item-specific; it is *item-configured*, by its caller, through a callback"* — and its decision was *"a chore takes `Writes.claim` — unchanged — on a dedicated per-repo chore-lock issue, with a short lease. No new function. No new marker prefix. No new parameter."*
*Verification:* `docs/adr/0041-the-chore-lock-is-the-item-cas-on-another-subject.md`, "The finding" and "Decision".

**Where the operation key lives, given the grant cannot carry it.** It does not need to, because the grant and the key answer different questions:

- **Mutual exclusion** is answered by the *subject*: one lock issue per receiver, so holding its `fsgg:claim` winner means "I hold this receiver's operation lock right now". That is exactly ADR-0041's argument — *"`fsgg:claim` disambiguates markers **on the same issue**. A dedicated issue disambiguates **by subject**"* — and it needs no key in the marker at all.
- **Idempotence** is answered by the *opkey*, recorded in the effect receipt (§4.2) and checked by the receiver. A repeat of the same `(item, gen, receiver, op)` finds a receipt and collapses.

So the opkey is never written into a CAS marker, and the `pathRepo=` field is deliberately **not** reused to smuggle it: that field has a defined meaning (`Marker.PathRepo`, `src/FS.GG.Coord.GitHub/Reads.fsi:44`), and overloading a parsed field with a second meaning is the drift class this design refuses everywhere else.

**Compatibility with `#516` (one item per worker), checked rather than assumed.** A worker taking a grant while already holding its item must not trip the one-item-per-worker refusal. It does not: that check *"scans the TARGET repo's in-flight items for a live claim held by THIS worker on a DIFFERENT item"* — **board** items — and the lock issue is off-board by construction, exactly as the chore lock is.
*Verification:* `src/FS.GG.Coord.Cli/Client.fs:3024-3036`; ADR-0041's "Consequences" (*"`who` and `reap` do not see the chore lock… the lock issue is off-board by design"*).

Its operational clauses carry over unchanged and are **requirements**, not commentary:

- **Closed, so it never appears in an `--state open` read** and cannot be mistaken for work; **not locked**, because a locked conversation refuses comments and the marker *is* a comment — locking the lock issue would disable the lock.
- **Absent ref ⇒ refuse.** A fence that cannot find its lock must refuse, never proceed. Fail closed, like every other "could not look" in this engine (`#266`, `#421`).
- **The ref is embedded beside the roster**, not read from YAML: the engine has no YAML reader deliberately, because the shim ships as a `kind: client` kit item *without* the roster, so a `repos.yml` reader would be absent exactly where receivers run.
  *Verification:* ADR-0041's 2026-07-17 amendment note citing ADR-0042/`#1026`; `Options.choreLockRef` at `src/FS.GG.Coord.Cli/Options.fs:1309-1348`.

**A gap this inherits and must close.** The chore-lock table lists **seven** repositories and omits `FS.GG.Net`:

```fsharp
[ ".github", 1033; "FS.GG.SDD", 518; "FS.GG.Rendering", 878; "FS.GG.Governance", 268
  "FS.GG.Templates", 252; "FS.GG.Game", 406; "FS.GG.Audio", 183 ]
```

*Verification:* `src/FS.GG.Coord.Cli/Options.fs:1297-1307`.

The roster is eight, and `FS.GG.Net#58` is one of the two pull requests `#1858` measured as merged by the unlocked executor. A per-receiver table built the same way would inherit the hole in exactly the repository the incident reached. Slice 2's acceptance must include the eighth row.

### 4.2 The effect receipt — on the item

```
<!-- fsgg:op-effect v=1 opkey=<sha256> grant=<grant comment id>
     receiver=FS-GG/FS.GG.Net op=merge evidence=<url-or-run-id> -->
```

Posted on the **item** after the effect lands. A new prefix is safe here precisely because `Reads.winner` never reads it: it decides no lock, so it cannot forge one. This is `#1858` AC3's audit trail — two executions under one worker id appear as two grants with two distinct comment ids, and the loser's refusal is itself on the record, visible on GitHub rather than reconstructable only from pull-request and workflow timestamps.

**The receipt is audit for the merge path and authority for nothing.** It is written *after* the effect, so its absence understates rather than overstates. Deriving *authorization* from it would turn a failed comment POST into a second dispatch (§8.5). The receiver may consult it for **idempotence** — "has this exact opkey already been applied?" — which is a different question from "is this executor allowed to act", and answering it wrongly costs a duplicate no-op rather than an unauthorized effect.

### 4.3 Local state may cache; it is never required

Every value above is a GitHub comment or a field of one. The only local inputs a fresh process needs are `owner/repo#N` and the compiled-in lock-ref table. `fsgg-coord` is already effectively stateless between invocations, which the hold decision names as the invariant to preserve: *"Today the durable coordination state and claim CAS live on GitHub, while `fsgg-coord` is effectively stateless between invocations. That location independence is an invariant, not an incidental property to trade away."*
*Verification:* `docs/reports/2026-07-30-150617-native-collaboration-runtime-supervision-design-and-roadmap.md`, "Hold decision".

A second machine, a restarted process, or a different runtime reconstructs the entire authorization state from GitHub alone. §10 checks this claim element by element.

---

## 5. Question 3 — broker authorization

### 5.1 What exists today, measured

- `dispatch-sender.yml` is a `workflow_call` reusable sender. It builds `client_payload` as `{version, source_repo, source_sha, source_ref} + $extra`, where `$extra` is the caller's free-form `payload` input **merged last** — its own comment says *"merges it last, so a caller can add fields (and override the defaults) without changing this file."*
  *Verification:* `.github/workflows/dispatch-sender.yml:103-119`.
- It declares **no `concurrency:`**, no operation key, no idempotency, and nothing identifying the acting party.
  *Verification:* `grep -n "concurrency" .github/workflows/dispatch-sender.yml` → no match.
- **No workflow in this repository calls it.** `release-kit.yml:57` and `release-coord-engine.yml:32` each say in terms that they do not and should not.
  *Verification:* `grep -rn "dispatch-sender" .github/workflows/` — every hit is the file itself or a comment.
- The one `repository_dispatch` consumer that reasons about payload trust, `feed-autofix.yml`, deliberately refuses it: *"`client_payload.version` is deliberately NOT trusted, or even read: a payload that disagreed with the feed would be a reason to distrust the payload."*
  *Verification:* `.github/workflows/feed-autofix.yml:37-39`.

So the broker is close to greenfield, and the org has already written down the correct trust rule for it.

### 5.2 The design

The **broker** is a workflow in `FS-GG/.github` that becomes the only caller of `dispatch-sender.yml`. For each request it:

1. Recomputes `opkey` from `(item, gen, receiver, op)`.
2. Reads the receiver's op-lock issue over REST and requires that the presented grant **is the live winner** by `Reads.winner`, that its `opkey` matches, and that its `gen` still equals the live winning `fsgg:claim` id on the item. Anything else: refuse.
3. Serializes per receiver with `concurrency: { group: fsgg-dispatch-<receiver>, cancel-in-progress: false }`.
4. Passes the authorization **pointer** through the existing `payload` input, needing no edit to `dispatch-sender.yml`:
   `{"fsgg_opkey": "...", "fsgg_grant": "...", "fsgg_item": "...", "fsgg_gen": "..."}`.
5. Posts the `fsgg:op-effect` receipt on the item with the dispatch run id.

**Concurrency is serialization only — it is not the dedupe, and the reason is a verified semantic.** GitHub's default `queue: single` permits at most one *pending* run, and *"any existing `pending` job or workflow in the same concurrency group will be canceled and the new queued job or workflow will take its place."* That policy is **last-writer-wins**: it silently prefers the *later* duplicate and discards the earlier one. It gives mutual exclusion; it does not give idempotence, and it does not give "the first authorized operation wins".
*Verification:* https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#concurrency (`queue: single` default, `queue: max` up to 100 pending, `queue: max` incompatible with `cancel-in-progress: true`).

Dedupe therefore comes from the **opkey**, never from the queue: a receiver that has already applied an opkey collapses a repeat into a no-op.

### 5.3 Receiver-side validation, and how it reaches the receivers

The receiver **must not trust `client_payload`** — §5.1 shows the caller can override even `source_repo` and `source_sha`. The payload carries a *pointer*; GitHub carries the *authority*. A receiver re-reads the grant on its own op-lock issue and the claim marker on the item, both over REST, and accepts only on an exact live match. This is `feed-autofix.yml`'s existing discipline, generalized.

**Distribution is constrained, and the constraint picks the route.** The kit cannot ship workflow files at all: *"materializing a workflow FILE through the kit cannot work — the App installation token that pushes the materialize commit has `contents: write` and not `workflows: write`, so the push that carried it would be rejected"*, and hand-copying into seven repositories is the defect class `#1507`/`#1510`/`#1515`/`#1528`/`#1538` closed, which ADR-0067 §5 forbids re-opening.

The same comment names the one route that works: *"All seven receivers ALREADY call this workflow, ungated, on `pull_request` into `main`. A job added here therefore reaches all seven with ZERO receiver-side edits — the same route `kit / coordination-kit` and `lock-ranges / lock-ranges` already take."*
*Verification:* `.github/workflows/kit-materialize.yml:55-68`.

So receiver-side validation and the receiver-side merge gate are **jobs added to the reusable workflow the receivers already call**, not new files pushed into receivers. Zero receiver edits, one owner, one place to fix.

---

## 6. Question 4 — the merge gate

### 6.1 Two verified credential facts decide the shape

**(a) A check run can only be created by a GitHub App**, and this org's App cannot. The Checks API states *"To create a check run, you must use a GitHub App. OAuth apps and authenticated users are not able to create a check suite"*, and *"Write permission for the REST API to interact with checks is only available to GitHub Apps."*
*Verification:* https://docs.github.com/en/rest/checks/runs

Read live on 2026-08-04, the `fs-gg-cross-repo-dispatch` installation carries **no `checks: write` and no `statuses: write`**:

```
$ gh api /orgs/FS-GG/installations --jq '.installations[] | {app_slug, permissions}'
{"app_slug":"fs-gg-cross-repo-dispatch",
 "permissions":{"administration":"write","contents":"write","issues":"write","metadata":"read",
                "organization_administration":"read","packages":"read","pull_requests":"write"}}
{"app_slug":"renovate",
 "permissions":{"administration":"read","checks":"write", ... ,"statuses":"write", ...}}
```

*Verification:* the command above, run 2026-08-04. (Renovate is shown only to demonstrate the two scopes exist and are simply not granted to ours.)

**(b) The App is documented as not installed on `.github` at all:** *"`.github` (role: authority) is not a receiver: it OWNS the callee side, and the App is not installed on it, so it is not a subject here."*
*Verification:* `.github/workflows/required-context-coherence.yml:38-40`.

Together these say: an App-created check run is unavailable today without an org-owner grant, and would be unavailable in `.github` even then. **The gate must be an ordinary GitHub Actions job**, whose check run GitHub creates for it, authenticated by the run's own `GITHUB_TOKEN`. That is not a workaround — it is strictly better, because it keeps the App off the critical path of every merge (§8.2).

### 6.2 Shape

A **new** workflow, `on: pull_request [opened, synchronize, reopened]` **and `merge_group`**, **no `paths:` filter**, `permissions: { contents: read, issues: read }`, one job whose id gives the required context its name.

**Why `merge_group` is not optional — and what it costs.** A check run is per-SHA, and branch protection consumes the last result reported for that SHA. The `pull_request` triggers fire on *head* events only, so **nothing re-evaluates the fence when the claim marker changes**. A claim released after the final push would leave a green in place through merge. `strict: true` would not help, because it tracks the *base* branch, not the claim — and `.github` main is `strict: false` anyway (verified live, §6.4).

GitHub's merge queue is the one native mechanism that re-evaluates required checks **at merge time**: *"You **must** use the `merge_group` event to trigger your GitHub Actions workflow when a pull request is added to a merge queue"*, the queue *"creates a temporary branch… to validate pull request changes"*, and on a failing status *"the merge queue automatically removes pull request #1 from the merge queue."*
*Verification:* https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/configuring-pull-request-merges/managing-a-merge-queue

So the fence re-reads the live claim on the merge-group ref, and a claim that changed between the last push and the merge attempt ejects the PR instead of merging it.

**The cost is real and is a decision, not a detail.** No FS-GG repository has a merge queue today: `required_merge_queue` is absent from `FS-GG/.github` and `FS-GG/FS.GG.Game` main protection (verified live 2026-08-04, `gh api repos/FS-GG/<r>/branches/main/protection`); the other six are `unverified`. Enabling one changes how *every* PR in that repository merges, not only fenced ones. §9.1 therefore treats it as a distinct arming decision, and §13 states the residual for anyone who declines it.

- No `paths:` filter, because a filtered workflow produces no check run on a PR that misses the filter, and protection then waits forever. The org has this written down as an arming precondition.
  *Verification:* `docs/coordination/reusable-workflow-contract.md`, "The ordering constraint: never arm a context before its producer reports".
- The PR body is read **through the environment, never interpolated into a `run:` block** — `closing-keywords.yml` states the rule and the reason: *"A PR body is attacker-controlled text: `${{ github.event.pull_request.body }}` pasted into a `run:` block is a script-injection sink."*
  *Verification:* `.github/workflows/closing-keywords.yml:97-105`.
- The verdict contract is the org's shared one: `scripts/lib/gate.py`'s `ExitCode`, where `OK = 0` is *"the ONLY green code"* and an inability to establish the answer is a **no-verdict**, never a pass.
  *Verification:* `scripts/lib/gate.py:59-79`; `docs/reports/2026-08-02-required-context-reconciliation.md`.

### 6.3 What it checks — each failure RED

The PR carries one authorization marker in its body, bound to its head:

```
<!-- fsgg:pr-authorization v=1 item=FS-GG/.github#2210 gen=5177045416
     opkey=<sha256> grant=<comment id> head=<sha> -->
```

**The marker is attacker-controlled text, and check 4 is the only thing that makes the rest mean anything.** §6.2 already treats the PR body as untrusted for injection; it is equally untrusted for *content*. Any executor with write access can simply type an authorization marker naming a plausible item, generation, and opkey. Checks 1, 2, 3 and 5 are then all satisfiable **by typing**, because each compares the marker against facts a forger can read. Only check 4 grounds the marker in something the forger cannot choose: a server-assigned comment id whose winner is decided by a CAS. Without it the gate is decorative — and an earlier draft of this section omitted it, then claimed in §8.3 that per-receiver exclusion reached merges. It did not; it reached dispatch only.

1. **Exactly one marker.** Missing → RED. Duplicate → RED. (Missing is the `#1858` executor's case: it never obtained one.)
2. **`head=` equals the PR's current head SHA.** A force-push after authorization means the authorization is for a different artifact. This is the rule `delivery` already enforces client-side, where a merge is refused because *"the PR is no longer at the inspected head."*
   *Verification:* `src/FS.GG.Coord.Cli/Client.fs:1096`.
3. **`gen=` equals the live winning `fsgg:claim` comment id on `item`**, re-read at check time. Stale → RED. Released, so there is no live winner → RED.
4. **`grant=` is the live winner of this receiver's operation-lock issue**, re-read at check time, and the worker recorded in that winning marker equals the worker recorded in the item's winning claim marker. Not the winner → RED. Not live → RED. Nonexistent, or on the wrong issue → RED. Winner held by a different worker than the claim → RED.
   This is where a second context is refused. Two contexts sharing one live generation compute a byte-identical opkey and pass checks 1–3 and 5 identically — the critic's case, and it is real. They cannot both pass this one, because they contend on **one** lock issue and `Reads.winner` names a single lowest live marker that every reader computes identically. Forging the field does not help: a forger who invents a comment id fails the existence read, and a forger who posts a real grant marker to obtain a real id has thereby entered the CAS and can still only win it once.
   *Verification:* `src/FS.GG.Coord.GitHub/Reads.fs:375-379` (`winner` sorts before taking the head, so racers cannot disagree); `src/FS.GG.Coord.GitHub/Reads.fs:283` and `:473-486` (an unreadable or unclassifiable marker refuses the whole scan rather than shortening the list).
5. **`opkey` recomputes** from `(item, gen, this repository, merge)`. Mismatch → RED.
6. **Any read that cannot be completed** → RED via the no-verdict path (§8.2 answers the fail-always tension this creates).

**What check 4 costs, stated plainly.** A merge grant must be live when the gate runs. Holding one from authorization until a human-paced review finishes would either lapse (the lease is minutes, §7) or serialize every merge in the receiver for hours. So the grant is taken around each *head event* and around the merge attempt, not held across the PR's life — which is why `merge_group` (§6.2) matters twice over: it is the trigger that re-runs check 4 at merge time, when the grant that authorizes the merge is actually held.

### 6.4 Why a new job and not the existing required `drift` context

`.github`'s protected `main` requires five contexts today, read live:

```
$ gh api repos/FS-GG/.github/branches/main/protection
required_status_checks.contexts = ["contract-coherence / coherence","projection",
                                   "roster-closure","drift","reconcile"]
strict = false     enforce_admins = false     required_pull_request_reviews = null
```

*Verification:* the command above, run 2026-08-04.

`drift` is `touch-set-drift.yml`, which is *"the ONE workflow in the org that EXECUTES the coordination client on a CI runner"*, on `pull_request [opened, synchronize, reopened]`, with `issues: read`. It is the closest existing precedent and proves the shape is viable.
*Verification:* `.github/workflows/touch-set-drift.yml:1-15` (the `on:`/`permissions:` block) and `:40-42`.

It is nonetheless the **wrong host**, because its verdict is deliberately non-blocking: *"This reports that drift. The drift VERDICT does NOT block. ADR-0021 is explicit that the touch-set is 'a declaration, not an enforced boundary'."* Folding a blocking verdict into it would make one check run answer two questions in one colour and would silently change what its entire history of greens meant. A separate context keeps the two answers separable.

### 6.5 Two boundaries stated rather than hidden

- **This does not stop a duplicate PR being opened.** `#1858` AC1 already concedes it: *"A duplicate PR may still be opened by a principal that already has repository write access; preventing creation would require restricting that principal's credentials, which is outside this design."* What the gate removes is the thing the incident actually measured — the **merge**.
- **`enforce_admins: false` on `FS-GG/.github` main** (verified above) means a required context does not bind an admin. The fence is a fence, not a wall. Its value is still real: an admin bypass is a deliberate act that leaves a GitHub record, whereas `#1858`'s duplicate merge left none. Raising `enforce_admins` is an org-owner action, listed in §11 as a prerequisite rather than assumed. The same setting on the other seven repositories is `unverified`.

---

## 7. Per-receiver contention — the second hard constraint

§1.3's finding is that the lock was per **item** while the contention was per **receiver**, and that a static comment channel expired faster than the executors could act on it.

The answer is the per-receiver op-lock of §4.1, and it changes the question from a guess into a **read**:

- *"Is anyone dispatching against `FS.GG.SDD` right now?"* becomes `Reads.winner` over that receiver's op-lock issue — live at the moment of the read, not a sentence someone wrote seven minutes earlier.
- In the addendum's scenario, both executors would have contended on one lock issue. One gets the grant; the other is **refused at the fence**, not asked to be careful. Neither has to detect the other, and neither has to be right about stale information.
- The two jobs are split, because one marker cannot do both (§4.1). **Exclusion** is the lock issue: one live winner per receiver, so a second executor is refused rather than asked to be careful. **Idempotence** is the opkey: re-pressing the same button for the same `(item, gen, receiver, op)` finds an effect receipt and collapses, while a genuinely different operation is a distinct opkey and is correctly serialized rather than silently coalesced.

**Costs carried over from ADR-0041's consequences, because they apply unchanged.**

- *"Chores serialise to one in flight per repo, and that IS a new bound."* So do operations here, per receiver. Accepted for the same reason — it is what makes the REST spend bounded.
- *"Chores spend REST, which is the budget that dies."* Each acquire is a marker read, a post, and a re-read, plus a delete on release, and the claim lock lives on REST (ADR-0034 §3), which hit 0/5,000 twice on 2026-07-16 (`#894`, `#907`) while GraphQL stayed healthy.
- *"`who` and `reap` do not see the chore lock."* Both scan **board** items and the lock issue is off-board, so a held grant is invisible to the roster and a lapsed one is not collected by `reap`. It is still self-healing, because `claim`'s own stale collection takes the dead marker on the next acquire.

*Verification for all three:* `docs/adr/0041-the-chore-lock-is-the-item-cas-on-another-subject.md`, "Consequences".

Two mitigations this design must therefore carry explicitly: the grant lease is **minutes**, not the claim's 120-minute default; and the grant is taken **immediately before a consequential effect and released after it**, never held across an item's lifetime. A long-held per-receiver grant would convert a parallel fleet into a serial one.

---

## 8. Question 5 — failure modes, each failing closed

### 8.1 Stale generation

The item was released, reaped, stolen, or re-claimed since the authorization was written, so the live winning claim comment id differs from `gen`. **RED whenever the gate is evaluated, and refused at the broker.**

Recovery is authoring, not retry: re-take the claim and re-authorize, which mints a new opkey. ABA needs no separate defence — comment ids are monotone (§3.1), so a released-and-reclaimed item can never reproduce its old generation.

**When "RED at the merge gate" is actually true, and when it is not.** A check run is per-SHA and protection consumes the last result for that SHA, so the gate's verdict is only as fresh as the last event that ran it:

| the repository has… | the fence is re-evaluated… | a claim that changes after the last push is… |
|---|---|---|
| a merge queue (`merge_group`, §6.2) | on every head event **and at merge time**, on the merge-group ref | **caught** — the queue ejects the PR |
| no merge queue | on head events only | **not caught** — a stale green stands through merge |

Without a merge queue this weakens `#1858` AC1 from *"a pull request that does not name the current GitHub-visible claim generation cannot merge"* to *"…that did not name the current generation **as of its last head event**"*. That is a genuine gap and §13 states it as one rather than leaving the stronger sentence standing.

**What the gap does *not* reopen.** The `#1858` harm was an executor that held no claim at all merging into six repositories. That executor has no authorization marker and no grant, so it is refused at check 1 and check 4 regardless of freshness. The residual class is narrower and different: *the rightful holder's own PR* merging shortly after its own claim lapsed. That is a lease-hygiene problem, not a duplicate-execution one, and `delivery --apply` already re-reads the winning claim immediately before its merge request for every executor that uses the client (`src/FS.GG.Coord.Cli/Client.fs:1078-1088`). The uncovered case is therefore "an executor that bypasses the client **and** whose own claim lapsed mid-review" — bounded, and bounded further by any merge queue.

### 8.2 App outage, and the fail-closed / fail-always tension

These two must not be collapsed, because collapsing them is `#463` — *"A gate that failed closed on a read the fleet's token cannot make would not fail closed; it would fail ALWAYS."*
*Verification:* the `landable` exit-4 row in `.claude/skills/pnext-item/references/command-contracts.md`, generated from `src/FS.GG.Coord.Core/Protocol.fs`.

- **The merge gate cannot read GitHub.** RED, via the no-verdict path. Bounded blast radius: it blocks merges during a window in which GitHub is unreadable and merging is therefore not happening anyway.
- **The broker cannot read the grant.** Refuse the dispatch. No effect is better than an unauthorized effect, and the operation is retryable by construction because the opkey is a pure function of facts that outlive the outage.
- **What the design deliberately does *not* do** is depend on a credential the ordinary path cannot obtain. The merge gate reads only `issues: read` and `contents: read` — both valid `GITHUB_TOKEN` scopes. It does **not** need `administration`, which is not a valid `permissions:` scope in either direction and whose absence produced *"a startup validation error that produces no check run at all"* when `#1575` hit it.
  *Verification:* `.github/workflows/required-context-coherence.yml:27-30`; `scripts/repos.sh:485-490`.

This is the concrete reason §6.1's App constraint is a feature: **an App outage cannot block merges, because the App is not on the merge path.**

### 8.3 Concurrent receivers — the clause that answers the shared session id

Two executors, indistinguishable by session, reach the fence for the same receiver:

1. Both post claim markers to that receiver's operation-lock issue. GitHub assigns two **distinct** comment ids.
2. `Reads.winner` is the lowest live id, and every racer computes it identically because the function sorts rather than trusting input order (§3.1).
3. The loser's grant is not the winner, so it is refused.

**At no point is a worker id, a session id, a process, or a machine compared.** The design does not repair `twinSession` (§1.2) — it routes around the need for it.

**Both paths reach this, and they reach it at different points.** The dispatch path enforces it in the broker (§5.2 step 2) and again at the receiver (§5.3). The merge path enforces it at **check 4** of §6.3 — which is the check an earlier draft of this document omitted while still claiming here that per-receiver exclusion covered merges. It did not. Merge is where `#1858`'s harm actually landed (`FS.GG.Net#58` and `FS.GG.Audio#220` were *merged* by the unlocked executor), so a design that fenced only dispatch would have missed the measured defect. With check 4 present, `#2210` AC2 is discharged on both paths — subject to §8.1's freshness bound on when the merge-path check is evaluated.

### 8.4 The executor that never calls a coordination verb

The actual `#1858` mechanism. It holds no grant, so its PR carries no authorization marker, so the merge gate is RED at check 1 — and would still be RED at check 4 even if it typed one, because it holds no winning grant (§6.3). Its dispatch carries no grant, so the receiver refuses. It can still push branches and open PRs — §6.5, conceded.

**The honest residual.** A resumed context that *does* run `fsgg-coord` and *does* hold the claim — because the claim is still live under a shared id, or because it re-claimed — will be granted. Effect fencing cannot distinguish "the rightful holder resumed" from "a second context of the rightful holder", and it does not try. It does not need to for *correctness*: both then contend for the same receiver's operation lock, and check 4 refuses whichever one is not its winner, so at most one effect lands. What is lost is the **warning**, not the fence. The warning is `#1938`'s harness-owned boundary and is not this design's to build (§12.2).

### 8.5 Partial application

An effect that lands while its `fsgg:op-effect` receipt fails to post. Because the receipt is written *after* the effect and is audit rather than authority (§4.2), its absence understates the record; it never authorizes a second effect. A re-run recomputes the same opkey and the receiver collapses it.

The inverse — a receipt that posts while the effect fails — is also safe, for the same reason: nothing reads the receipt to decide anything.

### 8.6 An op-lock ref that cannot be resolved

Refuse. ADR-0041's clause is binding and is repeated here because it is the one that is tempting to relax: *"A chore queue that cannot find its lock must offer nothing, never broadcast: condition 1 fails **closed**."*

---

## 9. Questions 6 and 7 — migration and rollback

### 9.1 Migration: observed-only → required

The org already has an incident-derived arming sequence, and this design **obeys it rather than restating it**. Its headline hazard is measured, not hypothetical: *"`repos.sh require-context --apply` for a context nothing produces holds every pull request in every targeted repository at 'Expected — waiting for status to be reported', permanently"* — and the writer's dry run over seven receivers *"reported **6 would-add, 1 failed** at a moment when no receiver-side producer existed at all, and running it would have wedged six repositories."*
*Verification:* `docs/coordination/reusable-workflow-contract.md`, "The ordering constraint".

Applied here, step by step, with each step's evidence:

1. **Land the producer in observe-only.** The fence workflow reports `success` unconditionally and writes its real verdict to the job summary. Required in **zero** repositories. This is exactly the state `materialize / kit-bump-shape` and `kit-bump-mechanical` sit in today, deliberately.
2. **Observe it REPORT on a real pull request.** `gh api repos/<r>/commits/<sha>/check-runs` must show the context **by name**. A workflow that *should* report is not a producer.
3. **Prove producibility statically**, needing no write: `python3 scripts/check-required-contexts.py --repo FS-GG/<r> --root <checkout> --protection <payload naming the context>`.
4. **Prove the verdict is a function of the receiver, not of the clock** (ADR-0067 §2, `#1584`). This deserves a real answer rather than a wave, because on its face the fence reads *another repository's live state* — the claim marker in `.github`. The answer: the fence's input is not a moving *tip*, it is **the generation the PR itself names**, recorded immutably in its own body and bound to its own head SHA. The fence is a function of (this PR's head, this PR's declared generation, the live claim). The live claim *can* change underneath it — and when it does, RED is the **correct** answer, not an outage.
   Two residuals are real and are stated rather than argued away. **First**, a hub outage makes the fence unreadable and therefore red across every armed receiver at once. That is the price of arming, and it is why step 5 is gated on a measured red/green pair per receiver rather than on a dry run alone. **Second**, "when it does, RED is the correct answer" is only reached if something re-evaluates the fence after the claim changes — which, without a merge queue, nothing does (§6.2, §8.1). Step 4a below is what makes this clause true rather than aspirational.
4a. **Decide the merge queue, per repository, before arming.** With `merge_group` enabled the fence is re-evaluated at merge time and §8.1's stale-green window closes; without it, arming still buys the refusal of a verb-less or ungranted executor (checks 1 and 4) but leaves that window open. This is a separate decision from arming the context because it changes how every PR in the repository merges, not only fenced ones, and because no FS-GG repository has one today (verified live for two of eight; six `unverified`). Arming without it is a legitimate choice — it must simply be a recorded one, with §13's residual acknowledged, rather than a silent downgrade of `#1858` AC1.
5. **Only then `--apply`**, hub first (`.github` alone), watching one deliberate red and one deliberate green, then per receiver with a dry run before each.

**In-flight work is not stranded**, and the ordering that guarantees it is the load-bearing part: a PR opened before the gate exists carries no authorization marker and would go red the instant the context is armed. So **slice 3 — `delivery` writing the marker — must land before slice 8 arms anything.** Once it has, every in-flight PR acquires its marker on the next head push, and arming after that window is safe. The observe-only phase must run at least one full claim-lease window (120 minutes default, `FSGG_CLAIM_LEASE_MIN`), and arming must confirm zero open `item/<n>-*` PRs lacking the marker.

**One correction the arming step must carry.** `scripts/repos.sh` states *"**No credential in this org can run the APPLY path today**: raising the App's grant to `administration: write` is an org-owner action"*, measured 2026-07-28.
*Verification:* `scripts/repos.sh:485-490`.

That is no longer true. Read live 2026-08-04, the installation carries `administration: write` (§6.1's output). `#1712`'s decision was to raise it, and the follow-up comment recorded the grant as half-done — App `write`, installation `read`, pending org-owner approval — which has since been approved.
*Verification:* https://github.com/FS-GG/.github/issues/1712#issuecomment-5100812663 for the half-done state; the live `gh api /orgs/FS-GG/installations` call above for the current one.

So the apply path **is** runnable from an App token today, and `scripts/repos.sh`'s comment is premise-rot of exactly the kind `#1138` named ("fix the premise-rot in the same change that creates it"). Repairing it is part of slice 8.

### 9.2 Rollback

Three levels, cheapest first, and **the order is the design**:

1. **Neuter the producer.** Flip the job back to observe-only via a repository variable read at job level. The check keeps reporting green — it *must* keep reporting, or protection waits forever — while its verdict stops binding. Minutes, no `administration` credential, no org-owner involvement. **This is the first move for both failure directions** (fails open, or blocks legitimate work).
2. **Disarm the context.** `scripts/repos.sh unrequire-context`, a DELETE against `.../protection/required_status_checks/contexts`. Note `repos.sh`'s own deliberate under-promise: removal is from the **classic store only**, and *"a ruleset may still require the context afterwards"*. So a rollback must check both stores — `check-required-contexts.py` reads `repos/{repo}/rules/branches/{branch}` as well.
   *Verification:* `scripts/repos.sh:478-484`; `scripts/check-required-contexts.py:114` and `:761` — *"`branches/<b>/protection` (classic) does NOT report ruleset rules, and `rules/branches/<b>` does NOT"* report classic ones. The rulesets read is also the cheaper one: *"`rules/branches/<b>` needs only `metadata: read`"* (`:739`), so a rollback can check the second store without an `administration` credential.
3. **Revoke the broker** — make its authorization step advisory and let dispatches through unfenced. This is the fail-**open** direction, so it is a deliberate, recorded act and never a default.

**Never disarm before neutering.** A required context whose producer has been deleted wedges every PR permanently — the same failure §9.1's sequence exists to prevent, reached from the other end.

---

## 10. Acceptance criterion 6 — several users, several runtimes, independent PCs

`#2210` AC6 and the hold decision both make this binding: no required machine-local database, daemon, or leader. Checked element by element rather than asserted.

| element | where its state lives | local state required? | daemon or leader required? |
|---|---|---|---|
| claim generation | `fsgg:claim` comment id on the item | no | no |
| operation grant | a claim-marker comment id on the receiver's op-lock issue | no | no |
| PR authorization | a marker in the PR body | no | no |
| merge gate | an Actions job on the PR, reading GitHub | no | no |
| broker | an Actions workflow in `FS-GG/.github` | no | GitHub Actions' own `concurrency` — GitHub's, not ours |
| receiver validation | a job in the reusable workflow receivers already call | no | no |
| op-lock refs | compiled-in configuration (ADR-0042's shape) | **configuration, not state** | no |

The single centralization is that the broker workflow runs in `FS-GG/.github`. That is a GitHub-hosted serialization point, not a machine-local one; it holds no state of its own, and its authority is entirely derived from comments any party can read. **A second broker instance would be safe**, because the authority is the grant, not the broker — which is precisely the property the hold decision found missing from the supervisor's registry model.

Two independent PCs, two runtimes, and a restart all reconstruct identical authorization state from `owner/repo#N` plus the compiled table. Nothing is cached that is required; everything cached is re-derivable.

---

## 11. Acceptance criterion 4 — the implementation slices

### 11.1 Which of `#1858`'s replacement-plan steps become slices

| `#1858` step | disposition |
|---|---|
| 1 — specify a GitHub-hosted operation identity | slices **1, 2** |
| 2 — fence merges | slices **3, 4** |
| 3 — broker and deduplicate receiver effects | slices **5, 6** |
| 4 — make resumed work explicit | **not a slice under this design.** It is `#1938`'s harness-owned boundary, which has no addressable owner (§9.2 of that issue's history, and the 2026-08-01 comment on `#1858`). This design deliberately does not depend on it. Recording it as "not filed" rather than dropping it silently is the point. |
| 5 — prove the incident, not a toy case | slice **7** |
| 6 — roll out incrementally | slice **8** |

### 11.2 Proposed slices

Each `Paths:` below is a proposal for the filing worker to declare, chosen to be disjoint so the slices can run in parallel lanes where their ordering allows.

| # | slice | proposed `Paths:` | `Class:` | ordering |
|---|---|---|---|---|
| 1 | Operation key and receipt types in the pure core: `OpKey`, the closed operation vocabulary, digest composition | `src/FS.GG.Coord.Core/Operation.fs src/FS.GG.Coord.Core/Operation.fsi tests/FS.GG.Coord.Core.Tests` | hardening | first; pure, no IO |
| 2 | Per-receiver op-lock **refs and callers** — a new `opLockRef` table beside `choreLockRef` (**including the missing `FS.GG.Net` row**) plus grant acquire/release that calls `Writes.claim` with the lock ref. The CAS itself gains no code: no new prefix, no new field, no new parameter (§4.1) | `src/FS.GG.Coord.Cli/Options.fs src/FS.GG.Coord.Cli/Client.fs tests/FS.GG.Coord.Cli.Tests` | hardening | after 1 |
| 3 | `delivery` writes the PR authorization marker bound to head, and takes/releases the grant around a guarded landing | `src/FS.GG.Coord.Cli/DeliveryApplication.fs src/FS.GG.Coord.Cli/DeliveryApplication.fsi src/FS.GG.Coord.Core/Delivery.fs src/FS.GG.Coord.Core/Delivery.fsi` | hardening | after 2; **must land before 8** (§9.1) |
| 4 | The merge-gate producer, observe-only, triggered on `pull_request` **and `merge_group`** (§6.2), with all six checks of §6.3 including the grant read | `.github/workflows/fsgg-claim-fence.yml scripts/check-claim-fence.py tests/claim-fence` | hardening | after 2 — check 4 needs the op-lock refs |
| 5 | The broker workflow — the only caller of `dispatch-sender.yml`; per-receiver `concurrency`, opkey dedupe | `.github/workflows/fsgg-dispatch-broker.yml tests/dispatch-broker` | hardening | after 2 |
| 6 | Receiver-side validation as a **job added to `kit-materialize.yml`** (zero receiver edits, §5.3) | `.github/workflows/kit-materialize.yml tests/receiver-validate` | hardening | after 5 |
| 7 | The reproduction: two concurrently live executors, two PCs, restart, stale generation, App outage, concurrent receivers — `#1858` AC6 | `tests/claim-fence-e2e` | hardening | after 6 |
| 8 | Arming: observe-only → required per receiver by the five-step sequence, plus the `scripts/repos.sh` premise-rot repair (§9.1) | `scripts/repos.sh docs/coordination/reusable-workflow-contract.md` | hardening | last; after 3 and 7 |

Slice 6's `Paths:` overlaps a widely-touched file and will contend; that is a scheduling cost, not a design one, and it is still cheaper than the seven-copy alternative ADR-0067 §5 forbids.

### 11.3 Prerequisites that are not slices

These block acceptance but are not authorable as implementation work, so they belong on `#1858` as recorded prerequisites rather than as filed slices:

1. **`enforce_admins: false` on `FS-GG/.github` main** (verified live, §6.4). A required context does not bind an admin. Org-owner action. State on the same pass across the other seven repositories: `unverified`.
2. **The ADR.** §3's identity model and §4.1's reuse of the item CAS on a third subject are ADR-shaped decisions amending ADR-0027 and extending ADR-0041. `docs/adr/` is outside `#2210`'s touch-set, so the ADR is written by the slice that makes the decision — slice 2 — not here.

---

## 12. Acceptance criterion 5 — rejected alternatives

1. **The stateful collaboration-runtime supervisor (M0–M7).** On hold since 2026-07-31, and its own hold text disqualifies it: a per-installation registry *"cannot establish repository-wide exclusivity, while a repository- or organization-wide supervisor would centralize coordination that is currently GitHub-native"*, and *"Different users could therefore receive different supervision depending on which machine remained online."* It also states outright that *"`.github#1858` is **not solved or routed by M4**"*. Using it would trade the invariant AC6 makes binding. Its hold text moreover *names* this design's direction as the intended replacement investigation — *"a GitHub-visible executor or operation-generation marker, guarded PR/merge/dispatch entry points that verify the current claim immediately before their external effect, and receiver-scoped side-effect fencing stored on GitHub"* — so this is continuation, not competition.
   *Verification:* `docs/reports/2026-07-30-150617-native-collaboration-runtime-supervision-design-and-roadmap.md`, "Hold decision", consequences 3 and 4 and the closing paragraph.

2. **Fix the identity instead — a durable per-executor id.** `#1938` DECIDED that the harness owns durable executor identity and mutation-disabled resumed contexts, and its own rejected-alternatives section rejects a GitHub-only integration for *that* purpose, correctly: a GitHub integration cannot observe a context clear. But that boundary has **no addressable owner** — no repository in the roster or the sibling workspace owns the harness runtime, and `EHotwagner/speckit-fsharp-tooling` was inspected and is skill/preset tooling, not the harness.
   *Verification:* https://github.com/FS-GG/.github/issues/1938#issuecomment-5117909215; https://github.com/FS-GG/.github/issues/1858#issuecomment-5152138133.
   So identity-first is not rejected on merit; it is **unroutable today**. This design is deliberately orthogonal to it: because it never compares identities, it neither competes with `#1938` nor waits for it, and `#1938` landing later would add a warning this design does not have (§8.4) without invalidating anything here.

3. **Tighten marker-writing, or improve the twin check.** Rejected on the binding 2026-08-02 note and on `twinSession`'s own definition (§1.2): the predicate is structurally incapable of separating two executors that share a session, and the marker recorded one worker because the protocol could see one.

4. **A per-receiver coordination room (ADR-0051) as the channel.** Rejected as the *mechanism*. A room is a better channel than the static comment the addendum measured, but *"a room carries no lock and no lease"*, so it stays advisory — and §1.3's finding is precisely that both executors cooperated in good faith over a channel and duplicated anyway. Rooms remain useful **alongside** this design; they are not a substitute for a lock.
   *Verification:* `src/FS.GG.Coord.GitHub/Writes.fs:1005-1007`.

5. **A second CAS for grant markers, under a prefix of their own.** Rejected on ADR-0041's Option B and `#485`: one rule computed in two places agrees at first and drifts later. `Writes.claim` is already a general comment-order CAS over an arbitrary issue ref, and the tests have driven it that way for its whole life. The **third** option — parameterising the existing CAS's prefix — is rejected on ADR-0041's Option A: it refactors the org's most safety-critical function to obtain a generality it already has, and would parameterise off protections (stale collection, twin detection) that a lock issue positively wants. §4.1 records that an earlier draft of this document reached for a new prefix anyway, and why that was both incoherent with the ADR it cited and unimplementable against the code.

6. **GitHub Actions `concurrency` as the deduplicator.** Rejected on a verified semantic: the default `queue: single` cancels the existing *pending* run in favour of the newly queued one, so the policy is last-writer-wins. That is mutual exclusion, not idempotence, and it silently prefers the later duplicate — the opposite of what a fence wants (§5.2).

7. **An App-created check run for the merge gate.** Rejected on two verified facts: creating a check run requires a GitHub App, and this org's App installation carries neither `checks: write` nor `statuses: write`; and the App is documented as not installed on `.github` at all. It would additionally put the App on the critical path of every merge, so an App outage would block all merges — a failure mode a fence must not have (§6.1, §8.2).

8. **Extending `touch-set-drift`'s existing required `drift` context.** Rejected because that verdict is deliberately non-blocking; adding a blocking verdict would make one check run answer two questions in one colour and retroactively change what its history of greens meant (§6.4).

9. **Trusting `client_payload` for authorization.** Rejected on the sender's own shape — the caller's `payload` merges **last** and can override `source_repo`/`source_sha` — and on the discipline `feed-autofix.yml` already applies. The payload is a pointer; GitHub is the authority (§5.1, §5.3).

10. **A machine-local claim cache read at startup**, one of the two shapes the mechanism comment floated. Rejected on AC6: it would make correctness depend on machine-local state, which the hold decision forbids, and it would help only the runtime that wrote it — a second PC, a second runtime, or a fresh container gets nothing. It remains legitimate as an *advisory* cache, which is all §4.3 permits.
    *Verification:* https://github.com/FS-GG/.github/issues/1858#issuecomment-5109701014, "The remedy this points at", candidate shape 1.

---

## 13. What this design does not solve

Stated so a reader cannot mistake its boundary for a claim.

- **It does not prevent a duplicate PR from being opened**, or a branch from being pushed. `#1858` AC1 concedes this; the remedy is credential restriction, which is out of scope.
- **It does not detect a resumed context**, and does not try. It makes the resumed context's *effects* safe, not its *self-knowledge*. The warning is `#1938`'s, and `#1938` has no owner (§12.2).
- **It does not bind an admin**, because `enforce_admins` is false on `.github` main (§6.5).
- **Without a merge queue, it does not prove the generation is current *at merge time*** — only that it was current at the pull request's last head event (§6.2, §8.1). Stated as the bounded limitation it is: `#1858` AC1 as literally worded ("a pull request that does not name the current GitHub-visible claim generation cannot merge") is met **only** in a repository with `merge_group` enabled. Without one, the achieved property is the weaker "…did not name the current generation as of its last head event". The uncovered case is an executor that bypasses `fsgg-coord` **and** whose own claim lapsed mid-review; it is not `#1858`'s measured case, which fails checks 1 and 4 regardless of freshness.
- **It does not fence effects outside GitHub** — a package published to a feed, a file written outside a repository. The opkey vocabulary is extensible to `publish:<package>`, but that path is unbuilt and unverified here.
- **It does not close `#1857`.** `#1857` is CLOSED, and `#1858` AC4 — that the session-rotation refusal must not recommend a remedy whose effect is to strand a claim into a second executor — appears already satisfied. The refusal now tells the caller that this may be a rotated session rather than a twin, directs it to retry under its *existing* session and worker id, and ends with an explicit instruction **not** to mint a new id for a live claim. (Quoted only in paraphrase here: the literal line carries a pasteable identity assignment, and reproducing it in a document is the collision attractor `#419` measured — `tests/worker-id-attractor/` rejects it, correctly, even in a quotation.)
  *Verification:* `src/FS.GG.Coord.Cli/Client.fs:308-310`; `gh issue view 1857 --repo FS-GG/.github --json state` → `CLOSED`.

---

## 14. `#1858`'s record — the contradiction, and its resolution

`#2210` AC3 requires the row to stop reading two ways. The host's comment evidenced the conflict without resolving it, deliberately leaving the judgement here.
*Verification:* https://github.com/FS-GG/.github/issues/1858#issuecomment-5176968614

| source | says |
|---|---|
| body, "Board consequence" | *"this remains a Backlog design item and is deliberately unschedulable for implementation until the design gate above passes"* |
| body, `Paths:` | `none` |
| comment, 2026-08-02 | *"**Unparked from `Backlog` to `Ready`.**"* |
| live board | `Status: Backlog` |

*Verification:* `gh issue view 1858 --repo FS-GG/.github --json body,comments`; `scripts/fsgg-coord ready --json` → `{"number":1858,...,"status":"Backlog","class":"defect","severity":"Critical"}` (run 2026-08-04).

**The resolution is that the 2026-08-02 unpark never took effect and could not have.** This is a measurement, not a preference:

- `Paths: none` parses as `DeclaredNone`, which the engine's own type documents as *"a decision somebody made. An epic, a decision item, an investigation whose scope IS the question. **Unschedulable BY DESIGN.**"* — as distinct from `Undeclared`, *"An OMISSION — somebody forgot."*
  *Verification:* `src/FS.GG.Coord.Core/Types.fsi:127-137`.
- So `#1858`'s `Paths: none` is **correct and coherent** with what the row is. It is not a missing declaration, and `lint` is right not to treat it as one.
- A row that reserves nothing cannot be handed to a worker, so `Ready` would have made it a permanently unfillable lane rather than a schedulable item. The live board never recorded the flip, and the body was never edited to match it.

Therefore: `#1858` stays `Backlog` with `Paths: none`, **as the parent design row it is**, and the 2026-08-02 line is superseded rather than honoured. What *does* change is the thing that actually blocked progress — its **filing gate opens**: this document is the design its body demanded, so material implementation slices (§11.2) may now be filed against it with real `Paths:`, which is what its body asked for all along.

The mechanics of recording this on `#1858` (a superseding comment linking this document and the 2026-08-02 line, and a body amendment to the "Board consequence" paragraph naming `#2210` as the discharged gate) are performed on the issue by `#2210`'s worker. They are board writes, not file changes, and touch no path outside this item's declared touch-set.

---

## 15. Open questions for the reviewer

Named rather than resolved, because each is a decision this design should not take alone.

1. **Grant lease length.** §7 argues minutes. The exact value is a measurement nobody has taken: it must exceed the slowest legitimate `merge` or `dispatch` and stay well under the claim's 120. `unverified`.
2. **Whether the broker should also gate `.github`'s own merges** or only cross-repo dispatch. The incident merged PRs in six *receiver* repositories, so receiver coverage is the priority; hub coverage is cheap but arms the fence against every hub PR at once.
3. **Whether `enforce_admins` should be raised** as part of slice 8 or tracked separately as an org-owner errand (§11.3).
4. **Rulesets.** For `FS-GG/.github` this is now settled: it has exactly one active ruleset and its target is **tags**, not branches, so branch protection there lives wholly in the classic store and §9.2's rollback needs one call.
   *Verification:* `gh api repos/FS-GG/.github/rulesets --jq '.[] | {name,target,enforcement}'` → `{"name":"release-tags-are-immutable","target":"tag","enforcement":"active"}` (2026-08-04). Coverage across the other seven: `unverified`.
5. **The merge queue — the largest open decision in this document.** §6.2 shows it is the only GitHub-native way to re-evaluate the fence at merge time, and §8.1/§13 show exactly what is lost without it. No FS-GG repository has one (verified for two of eight; six `unverified`), and enabling one changes how every PR in that repository merges. The question is not whether it closes the gap — it does — but whether the org wants the merge-queue workflow in the repositories that need fencing, or prefers to arm the fence with §13's residual recorded. **This design does not take that decision**; §9.1 step 4a makes it explicit and required before arming, either way.
