namespace FS.GG.Coord

/// THE PROTOCOL, AS DATA — the source every projection is emitted FROM (ADR-0034 §4.5).
///
/// A coordination rule is currently stated in up to six places: the ADR, the canonical doc, the tool,
/// and four `SKILL.md` bodies across two skill roots — then content-addressed into `repos.lock` and
/// byte-copied into six receivers. **54 vendored copies of the protocol.** The propagation edge is a
/// second issue and a second PR, every time (#309 → #502, #481 → #531), and the collision attractor was
/// removed BY HAND twice (#532, #551) before #570 gated it.
///
/// The inversion is the whole of §4.5: `fsgg-coord` was ALREADY the model — it was simply not the
/// SOURCE. In every drift that can be dated, the tool was right and the prose was wrong.
/// `check-worker-id-attractor.py` even calls `parallel-work.md` *"the document those skills are a
/// projection OF"*, and exists only because that projection is copied by hand.
///
/// So the rules live HERE, once, in the typed core that already enforces them, and the prose is
/// GENERATED. A rule then cannot land in one tier and not the others, because there are no tiers — it
/// is `repos.lock`'s discipline applied to the protocol itself: a generated, CI-gated artifact that
/// nobody authors, where a collision is a rebase rather than a decision.
///
/// The proof this was needed arrived while the flip was being written: `TouchSetGrammar` was typed into
/// F# by hand, byte-identical to bash's `TOUCHSET_GRAMMAR` purely by luck, with NOTHING holding the two
/// in step. That is a seventh copy, added by the change that was fixing the copies.
module Protocol =

    open Types

    /// One rule. `Id` is the anchor a projection references, so a doc can cite a rule without restating
    /// it, and a reader can grep the id back to the code that enforces it.
    type Rule =
        { Id: string
          Title: string
          /// The rule itself, in one paragraph. This is the text that lands in every projection.
          Statement: string
          /// Why it is this way — the incident that bought it. Emitted into the canonical doc, and
          /// omitted from the terse skill projections.
          Because: string }

    /// A schedulability verdict, as the worker meets it.
    type VerdictDoc =
        { Kind: string
          Meaning: string }

    /// One exit code, as the CALLER's contract — the fact a shell script reads without parsing prose.
    type ExitCodeDoc =
        { Code: int
          /// The `EX_*` spelling, where the code has one a worker would recognise; `""` where it does
          /// not. The name is a label on the number, never a second source for it.
          Name: string
          /// What the code means the engine OBSERVED.
          Meaning: string
          /// What the caller should DO about it. A code whose remedy is unstated is a code a worker
          /// invents a remedy for.
          Action: string }

    // ---- the verdicts ------------------------------------------------------------------------------
    // ONE TOTAL FUNCTION, ONE UNION. Fourteen of the scheduler family's issues were a missing case in
    // it; #485 named the cause — startability was computed in five places and agreed in none. These are
    // those cases, and the projection cannot drift from them because it is emitted from them.
    //
    // IT SAID THAT, AND IT WAS A TWELFTH COPY (#865). `verdicts` was a hand-typed list of Kind/Meaning
    // pairs that referenced the union NOWHERE, so "emitted from them" was an aspiration the code did not
    // implement — and it had already drifted, in both directions at once:
    //
    //   * `ItemPrOpen` (#651) was returned by `schedulable`, emitted as `item-pr-open` on the wire, and
    //     documented NOWHERE. A worker handed a verdict grepped it into the doc that explains it —
    //     the promise `Protocol.fsi` opens by making — and found nothing.
    //   * `held` was documented where the wire has always emitted `held-by`. Worse than a miss: `held`
    //     IS a live token in `who`'s claim-state vocabulary, so the grep SUCCEEDS, in the wrong
    //     vocabulary, and answers a question the reader did not ask.
    //
    // And the guard was green over both, because it counted entries and compared them to a THIRD
    // hand-written set of the same strings (`ProtocolTests`), whose comment claimed a compiler property
    // F# does not give a list literal. A generator whose source is hand-maintained just moves the drift
    // upstream and makes it authoritative — which is precisely what ADR-0034 §4.5 was about to lean on.
    //
    // So the Kind is no longer WRITTEN here. It is `Schedulability.kind`, applied to each case: an
    // exhaustive match the compiler checks. What is left here is the one thing the union genuinely does
    // not carry — the MEANING — and it is a total match too, so a new case cannot reach the docs
    // undocumented. The build fails; nobody has to notice.

    /// One value of each `Schedulability` case, as the subject the two matches below are applied to.
    ///
    /// THE ONE PART THE COMPILER CANNOT CHECK, and it is named rather than hidden. F# gives no
    /// exhaustiveness property for a list literal — that was #865's defect, asserted in a comment as
    /// though it were a guarantee. The samples' FIELDS are irrelevant (`kind` and `meaning` both ignore
    /// them); only the set of CASES matters, and `ProtocolTests` pins that against the union by
    /// reflection, which does not depend on anybody remembering this list.
    let private everyCase: Schedulability.Schedulability list =
        [ Schedulability.Startable
          Schedulability.IssueClosed
          Schedulability.WrongStatus Backlog
          Schedulability.BlockedBy []
          Schedulability.NoTouchSet
          Schedulability.DeliberatelyNoTouchSet
          Schedulability.UnusableTouchSet []
          Schedulability.HeldBy(WorkerId "")
          Schedulability.HeldByLiveWork(WorkerId "", 0)
          Schedulability.ItemPrOpen 0
          Schedulability.OverlapsInFlight []
          Schedulability.Undetermined "" ]

    /// What the verdict MEANS to the worker who is handed it — the one fact the union does not carry.
    /// A total match: a new case fails the build here rather than reaching a projection undocumented.
    let private meaning =
        function
        | Schedulability.Startable -> "Nothing holds it. It can be claimed now."
        | Schedulability.IssueClosed ->
            "The issue is CLOSED while the board still shows it open. The issue's state is the WORK; the board column is a PROJECTION of it. When they disagree, the issue wins — run /check-board."
        | Schedulability.WrongStatus _ ->
            "Its board Status is not one a scheduler hands out (or it has none at all, which makes it invisible to every scheduler and is a bug, not a decision)."
        | Schedulability.BlockedBy _ ->
            "A `Blocked by` entry is unresolved. CLOSED and MERGED resolve; OPEN, unverifiable and unparseable all BLOCK."
        | Schedulability.NoTouchSet ->
            "No `Paths:` line at all — an OMISSION. The item is real work and it is invisible to every worker who asks for work. Declare one, or `Paths: none` if it truly has no touch-set."
        | Schedulability.DeliberatelyNoTouchSet ->
            "`Paths: none` — a decision somebody made. An epic, a decision item, an investigation whose scope IS the question. Unschedulable BY DESIGN, and correct."
        | Schedulability.UnusableTouchSet _ ->
            "The declaration contains token(s) that can match no file, so they reserve NOTHING — and files nobody reserved are invisible to every other worker's overlap check."
        | Schedulability.HeldBy _ -> "A live claim marker holds it. Wait out the lease, or talk to the worker."
        | Schedulability.HeldByLiveWork _ ->
            "The lease EXPIRED but the work did not: an open `item/<n>-*` PR is the worktree protocol's own artifact, and it outranks a timer. Not offered; its touch-set stays reserved."
        | Schedulability.ItemPrOpen _ ->
            "No claim marker governs it, but an `item/<n>-*` PR is already OPEN on its branch — an implementation is in flight whether or not anyone claimed it. Not offered: claiming it would duplicate work that is already written (#651)."
        | Schedulability.OverlapsInFlight _ ->
            "Its files collide with work already in flight. The holder and its lease window are named, because \"nothing schedulable\" and \"queued behind a claim that frees in ~96m\" are the same fact and two completely different instructions."
        | Schedulability.Undetermined _ ->
            "WE COULD NOT DECIDE — and that is never a silent no. An unreachable answer is not a negative one. This is the case whose absence made every other case a lie waiting to happen."

    let verdicts: VerdictDoc list =
        everyCase
        |> List.map (fun c ->
            { Kind = Schedulability.kind c
              Meaning = meaning c })

    // ---- take's exit contract ----------------------------------------------------------------------
    // #585 gave `take` codes that tell "I claimed you an item" apart from the four ways it can claim
    // NOTHING, so `take && work_it` cannot fire on nothing. The codes were then RESTATED by hand in
    // /pnext-item §1 — and the restatement was wrong in three ways at once, which is the case for
    // generating it rather than proof-reading it harder:
    //
    //   * it documented `EX_PARTIAL` as "could not read the board — a no-verdict". `Errors.ExPartial`
    //     is a WRITE that half-landed (a `set-field --batch` outcome). `take` never returns it, and the
    //     code a failed READ actually carries is 1 (or EX_RATE for a budget).
    //   * its "≠0, ≠2" row swallowed rows 5, 6 and 75 — every one of them is also "≠0, ≠2", so read
    //     top-down the table contradicted itself. It gets one thing right that a naive replacement
    //     loses, though, and the first draft of THIS list lost it: a catch-all covers 3 (the batch was
    //     REFUSED), which is reachable and easy to forget. Enumerating beats a catch-all only if the
    //     enumeration is complete.
    //   * it read EX_RATE as "the GraphQL budget". Since #897 the engine names the budget that ACTUALLY
    //     died, and REST is the one that takes `claim`/`take`/`who` down with it (ADR-0034 §3).
    //
    // The list is ordered as a worker meets it: the one success first, then the failures by how likely
    // they are to be the reason a loop stopped.
    let takeExitCodes: ExitCodeDoc list =
        [ { Code = 0
            Name = ""
            Meaning = "An item was CLAIMED. This is the ONLY code that means you hold one."
            Action = "Go work it — and only here." }
          { Code = 5
            Name = "EX_NONE"
            Meaning =
              "Looked, and nothing was startable — an empty or all-blocked queue. A LOOK THAT SUCCEEDED and found nothing, which is why it is not 0 and not a read failure."
            Action =
              "Nothing to do: stop, or wait for the board to free up. Diagnose before you idle — `batch --include-backlog`, `who`, `next` each name a different reason a full board looks empty." }
          { Code = 6
            Name = "EX_CONTENDED"
            Meaning =
              "The item was startable when it was picked and the claim CAS lost every race for it — somebody else got there first."
            Action = "Back off briefly and retry. The board is busy, not empty." }
          { Code = 75
            Name = "EX_RATE"
            Meaning =
              "A rate budget is exhausted. The message names WHICH one (#897): REST takes `claim`/`take`/`who` with it, because the lock lives there (ADR-0034 §3); GraphQL takes the board reads. When it is REST, the fleet STANDING DOWN is the designed behaviour, not an outage (#976): answering \"is this item takeable?\" costs the very budget that is gone, and a lock you cannot verify is not a lock. So this is a stop, and it is meant to be."
            Action =
              "Back off until the reset it names — do not loop. Then `flush --dry-run`: a board write you made on an exhausted budget is QUEUED, and nothing replays it for you. AND IF YOU ARE HOLDING AN ITEM, `heartbeat` is REST too — an outage that outlives your lease cannot be renewed through, and the moment REST returns your item is startable again and the next `take` hands it to somebody else. Two things save you and neither is the timer: an OPEN `item/<n>-*` PR (#581 — the lease lapsed, the work did not), or a liveness probe that itself fails (which fails closed, #266). Push the branch and open the PR EARLY: it is the only proof of life that does not depend on the budget you just lost." }
          { Code = 3
            Name = ""
            Meaning =
              "REFUSED — the batch cannot be scheduled at all. Some in-flight claim declares a touch-set that matches no file, so it reserves NOTHING, and scheduling against it would hand its files to a second worker. The message names the item and the offending tokens."
            Action =
              "Do NOT retry — it will refuse identically until the declaration is fixed. Fix the claim it names (`widen <issue> --paths '<paths>'`), or talk to its holder." }
          { Code = 1
            Name = ""
            Meaning =
              "No verdict was reached, for one of two reasons the message tells apart: the engine refused your INPUT before it looked (no worker id resolves; the board document does not parse), or the board READ failed. A read failure is never an empty queue and never EX_NONE (#266) — \"I could not look\" and \"I looked, and it is empty\" keep different codes on purpose."
            Action =
              "Read the message. A refused input is not retryable — it names its own remedy. Retry only a read failure, and investigate one that persists." }
          { Code = 2
            Name = ""
            Meaning =
              "The ENGINE broke — an unhandled defect, with a stack trace. Its own code, so a broken engine cannot hide behind a stream of what look like bad inputs."
            Action = "Report it. Do not retry, and do not work an item you were not handed." } ]

    // ---- landable's exit contract ------------------------------------------------------------------
    // #900, and it is #889 one command over: /pnext-item §5 restated `landable`'s codes BY HAND, and
    // restated BASH's — "green (0), pending (3), red / conflicted / unknown (1)". The engine's are
    // 0/7/3/4, and the divergence is deliberate and on the record (`Client.fs`, ADR-0040 §5): bash
    // numbered the poll loop 0/3/1, where the engine keeps 3 == red across every verdict command
    // (`done`/`decide`/`adopt`) and gives pending its own 7. The LITERALS differ; the PROPERTY does not.
    //
    // The table was therefore wrong in BOTH directions on the two codes that matter, and `landable`'s
    // exit code exists precisely so a poll loop need not parse the verdict word:
    //
    //   * 3 means RED, and the table said PENDING — so `until landable "$pr"; do sleep 30; done` waits
    //     FOREVER on a PR that will never go green. Not a wrong answer: a hang, on the failure case, so
    //     it survives every green rehearsal.
    //   * 7 means PENDING, and the table had no 7 — so a loop reads it as an unrecognised failure and
    //     stops waiting on a PR that is merely still running.
    //
    // ENUMERATING BEATS A CATCH-ALL ONLY IF THE ENUMERATION IS COMPLETE (#889's lesson, paid for by a
    // first draft of `takeExitCodes` that dropped `ExitRed`). The four-row match `Client.landable` ends
    // on is NOT the whole contract — #900's own issue body lists those four and stops. Two more are
    // reachable, and they are the easy ones to forget:
    //
    //   * 1, from the REFUSED-INPUT arms ahead of the read (`--repo` absent, a PR ref that is not a
    //     number, `oneArg`'s arity check). Never retryable.
    //   * 2, from `Program.main`'s top-level defect handler, which wraps every command.
    //
    // AND THERE IS NO 75 HERE, WHICH IS THE ROW A READER OF `take`'S TABLE WILL EXPECT. `landable` is
    // fail-closed BY CONSTRUCTION: `Reads.prLandableRequire` returns a bare `PrState` with no error
    // channel, so a rate limit, a 404 and a `null` mergeability all resolve to `PrUnknown` — exit 4, an
    // honest no-verdict — rather than to a budget code. A `landable` that exited 75 would be a defect.
    //
    // Ordered as a poll loop meets it: the one green, then the one code worth retrying, then the ways
    // to stop.
    let landableExitCodes: ExitCodeDoc list =
        [ { Code = 0
            Name = ""
            Meaning =
              "GREEN — the PR is finished work: it merges cleanly, and every workflow run and check-run scored on its head SHA passed. The ONLY code that means merge it."
            Action = "Merge it. This is the only code that says so." }
          { Code = 7
            Name = ""
            Meaning =
              "PENDING — the verdict has not SETTLED: checks are still running, none have registered yet, the run set is still growing, GitHub has not finished computing the PR's mergeability (it does so in a BACKGROUND job, and `null` is the normal first answer for a PR you just opened — #950), or an assertion you added (`--require`, `--sha`) is not yet met. The ONE retryable verdict, which is why it has a code of its own rather than sharing one with a way to stop."
            Action =
              "Keep waiting — this is the only code that says wait. Prefer `--wait`, which polls until the verdict settles rather than believing an early green. A `pending` that NEVER resolves is a finding: the job was RENAMED, its workflow's `paths:` filter no longer matches, `--sha` named the wrong commit, or GitHub never finished computing mergeability (rare, and not something waiting longer fixes — read the PR yourself)." }
          { Code = 3
            Name = ""
            Meaning =
              "RED or CONFLICTED — two words, one code, because both mean STOP and neither improves by waiting. Red: a run or check-run failed. Conflicted: the PR does not merge cleanly, so GitHub cannot build `refs/pull/N/merge` and gives it NO CI at all — which is why it is returned immediately rather than polled."
            Action =
              "Stop. Do NOT wait — 3 is the code the recipe used to call `pending`, and a loop that waits on it never terminates. A red check is a finding; a conflicted PR needs a rebase, which is AUTHORING, not landing." }
          { Code = 4
            Name = ""
            Meaning =
              "UNKNOWN — no verdict, and this is the FAIL-CLOSED one (#266). The read could not be made or its answer was not conclusive: a rate limit, a 404, a PR whose `mergeable` field is ABSENT entirely. Note what it is NOT. A `mergeable` GitHub has not computed YET is PENDING (7), not this — it is guaranteed to change, and calling it unknown made `--wait` settle at once and abandon a seconds-old PR (#950). And there is no EX_RATE (75) here, unlike `take`: an exhausted budget arrives as this code, because `landable` has no error channel to carry a budget on."
            Action =
              "Do not merge, and do not treat it as a red. An unreachable answer is not a negative one. Look at why the read failed — check `budget` if you suspect a rate limit — and ask again." }
          { Code = 1
            Name = ""
            Meaning =
              "REFUSED — the engine rejected your INPUT before it ever looked at the PR: no `--repo` (so which repo the PR is in is undefined), a ref that is not a PR number, or the wrong number of arguments. It is not a verdict about the PR, and no word is printed."
            Action = "Read the message and fix the call. Not retryable — it will refuse identically." }
          { Code = 2
            Name = ""
            Meaning =
              "The ENGINE broke — an unhandled defect, with a stack trace. Its own code, so a broken engine cannot hide behind a stream of what look like bad inputs."
            Action = "Report it. Do not retry, and do not merge a PR you have no verdict on." } ]

    // ---- the rules ---------------------------------------------------------------------------------

    let touchSetGrammar: Rule =
        { Id = "touch-set-grammar"
          Title = "The touch-set grammar — it is NOT a glob language"
          Statement = Schedulability.TouchSetGrammar
          Because =
            "#273. Four hand-copied forms of the unmatchable-token predicate existed across two engines. A token that matches no file conflicts with nothing — so an item declaring only such tokens reserves NOTHING, clears every overlap check, and the lock succeeds under exactly the conditions it exists to prevent." }

    let touchSetDeclaration: Rule =
        { Id = "touch-set-declaration"
          Title = "`Paths:` is a declaration, and a fenced one is a QUOTATION"
          Statement =
            "Declare the touch-set as a `Paths:` line at up to three leading spaces. A `Paths:` line INSIDE a fenced code block is a quotation of the grammar, not a use of it — the protocol docs quote it constantly. `Paths: none` is a SENTINEL meaning \"this item deliberately has no touch-set\", and it is not the same fact as having forgotten one."
          Because =
            "#277 (a fenced line read as a declaration would let a doc reserve files) and #496 (an epic and a forgotten touch-set rendered identically, so no gate could be written at all — nine items of real work went invisible, and the surface whose job is board health reported `0 error(s)` over a dead queue)." }

    let blockerResolution: Rule =
        { Id = "blocker-resolution"
          Title = "A MERGED blocker is RESOLVED; an unreadable one BLOCKS"
          Statement =
            "`Blocked by` clears on CLOSED **or MERGED**. It does not clear on OPEN, on a blocker whose state could not be read (unverifiable), or on prose that is not an issue ref at all (unparseable) — all three BLOCK."
          Because =
            "#476: `Blocked by` may name a PULL REQUEST, whose state is OPEN | CLOSED | MERGED. A rule clearing only on CLOSED unblocks when the blocking work is ABANDONED and blocks forever once it is FINISHED — the gate opened precisely when the work was thrown away and shut precisely when it was done. And #266/#421: \"I could not look\" is not \"I looked and it is fine\"; prose in a dependency field is not permission." }

    let checkOrder: Rule =
        { Id = "check-order"
          Title = "Blockers are checked BEFORE the touch-set"
          Statement =
            "The scheduler asks, in order: is the issue closed? is its Status one we hand out? is it BLOCKED? is its touch-set usable? is it HELD? does it overlap work in flight? The first answer that is not \"no\" is the verdict, and it is the one sentence the worker reads."
          Because =
            "ADR-0038. A blocked item cannot be started whatever its touch-set says, so reporting \"no `Paths:` declared\" sends a worker to fix something that leaves them exactly where they were. And blockers are FREE — they are board facts already in the scan — where a touch-set costs a body READ per item, on the budget that dies first (#418). That is why bash never fetched a blocked item's body, and how an unreadable one could silently cease to exist." }

    let claimLock: Rule =
        { Id = "claim-lock"
          Title = "The claim lock is a comment-order CAS, and the ASSIGNEE cannot hold it"
          Statement =
            "A claim is an `fsgg:claim` marker COMMENT, and the lowest live marker id wins. GitHub issues comment ids from one server-side sequence, so \"lowest live marker\" is a total order every racer observes identically. The GitHub ASSIGNEE cannot be the lock, because N agents share one account."
          Because =
            "ADR-0027. The lock lives on REST, and the invariant it serves — a lock may never live on the budget that dies first — is unamended. What inverted is WHICH budget that is, so this rule no longer asserts a standing answer. #418 measured GraphQL dying first (five workers looping `take` drained 5,000 pt/hr in ~15 minutes), and REST was chosen as the survivor. #895 measured the reverse, twice on 2026-07-16: REST core hit 0/5,000 and took `claim`/`take`/`who` down with it, while GraphQL stayed healthy through both — 3,639/5,000 at the first of them. This rule used to state \"GraphQL is the first budget to die\" as standing fact, and that premise is what kept regenerating the doctrine that caused the inversion — a recipe steering every worker's reads onto REST to save GraphQL points, on one shared account, spending the lock's own budget to save 7 points of 5,000. #895 decided (2026-07-17) that the lock STAYS and the DOCTRINE moves (#968): REST is metered per request and cannot be batched, so under fan-out it is structurally the scarcer budget with no lever to pull, where GraphQL batches 100 nodes to a query. Discretionary reads belong on GraphQL; REST carries the lock, which has no alternative." }

    let leaseRule: Rule =
        { Id = "claim-lease"
          Title = "The lease is a WINDOW, and an unknown age says so"
          Statement =
            "A claim's lease is 120 minutes by default (`FSGG_CLAIM_LEASE_MIN`). Past it the claim is REAPABLE — not free: only `reap` may break a lock, and an item's touch-set stays reserved until it does. A claim whose age cannot be read reports `lease unknown`, never a window."
          Because =
            "#428 (\"nothing schedulable\" and \"queued behind a claim held by <w>, lease frees in ~96m\" are the same fact and two completely different operator instructions — the first reads as an empty queue and sends a worker home) and #440/#488 (inventing \"frees in ~120m\" from a missing timestamp is a confident-but-unfounded sentence, which is the class both were closed for). And the lease is a TIMER, which is why it never decides alone: it cannot see a REST outage, and `heartbeat` is REST, so an outage on the lock's budget spends a lease nobody can renew and silently reads as abandonment (#976, ratifying that the fleet stops there rather than making the clock outage-aware). What answers instead is evidence — an open `item/<n>-*` PR (#581), or a liveness probe that failed and therefore fails closed (#266). Expiry is EVIDENCE of abandonment, never proof." }

    let failClosed: Rule =
        { Id = "fail-closed"
          Title = "A read that did not happen may never render as a confident answer"
          Statement =
            "An error, an empty result, and a legitimate \"no\" are three different facts. A failed board scan is not an empty board; a failed marker read is not an unheld item; an unread issue body is not an undeclared touch-set. Every one of them fails CLOSED and says which it was."
          Because =
            "Epic #266, which has 51 children. #461: a failed claim scan read as \"nothing is claimed\", so `take` handed a held item to a second worker. #344: a rate-limited scan exited 0 with no verdict, and a worker read \"nothing to do\" off a board it never managed to read." }

    /// Every rule, in the order a projection presents them.
    let rules: Rule list =
        [ touchSetDeclaration
          touchSetGrammar
          checkOrder
          blockerResolution
          claimLock
          leaseRule
          failClosed ]

    /// The rules a worker FILING an item must satisfy — the subset `cross-repo-coordination` restates
    /// (#889).
    ///
    /// A SUBSET OF `rules`, NEVER A SECOND LIST. Every member is the same value the canonical list holds,
    /// so the two cannot disagree about what a rule SAYS — which is the only thing #731's mechanism was
    /// built to guarantee. `ProtocolTests` pins the containment, because the failure this invites is a
    /// rule authored straight into here and reaching a projection while the canonical doc never states it.
    ///
    /// WHY A SUBSET AT ALL, rather than emitting `rules`. `cross-repo-coordination` files work into
    /// ANOTHER repo; it does not schedule, claim, or hold a lease, and it links to
    /// `intra-repo-parallel-work` for all of that. Emitting the full block would bury the four lines it
    /// needs under `check-order`, `claim-lock`, `claim-lease` and `fail-closed` — seventy lines of
    /// scheduler internals a filer does not act on. That is the trap #916 named: a region carries what its
    /// document is FOR, which is why the kinds exist.
    ///
    /// What a FILER acts on: how to declare a touch-set (`touch-set-declaration`), what the grammar will
    /// actually accept (`touch-set-grammar`), and what `Blocked by` does once the edge is recorded
    /// (`blocker-resolution`). Nothing else on this list is a decision they make.
    let filingRules: Rule list =
        [ touchSetDeclaration; touchSetGrammar; blockerResolution ]
