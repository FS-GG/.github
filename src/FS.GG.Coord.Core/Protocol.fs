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

    let verdicts: VerdictDoc list =
        [ { Kind = "startable"
            Meaning = "Nothing holds it. It can be claimed now." }
          { Kind = "issue-closed"
            Meaning =
              "The issue is CLOSED while the board still shows it open. The issue's state is the WORK; the board column is a PROJECTION of it. When they disagree, the issue wins — run /check-board." }
          { Kind = "wrong-status"
            Meaning =
              "Its board Status is not one a scheduler hands out (or it has none at all, which makes it invisible to every scheduler and is a bug, not a decision)." }
          { Kind = "blocked-by"
            Meaning =
              "A `Blocked by` entry is unresolved. CLOSED and MERGED resolve; OPEN, unverifiable and unparseable all BLOCK." }
          { Kind = "no-touch-set"
            Meaning =
              "No `Paths:` line at all — an OMISSION. The item is real work and it is invisible to every worker who asks for work. Declare one, or `Paths: none` if it truly has no touch-set." }
          { Kind = "deliberately-no-touch-set"
            Meaning =
              "`Paths: none` — a decision somebody made. An epic, a decision item, an investigation whose scope IS the question. Unschedulable BY DESIGN, and correct." }
          { Kind = "unusable-touch-set"
            Meaning =
              "The declaration contains token(s) that can match no file, so they reserve NOTHING — and files nobody reserved are invisible to every other worker's overlap check." }
          { Kind = "held"
            Meaning = "A live claim marker holds it. Wait out the lease, or talk to the worker." }
          { Kind = "held-by-live-work"
            Meaning =
              "The lease EXPIRED but the work did not: an open `item/<n>-*` PR is the worktree protocol's own artifact, and it outranks a timer. Not offered; its touch-set stays reserved." }
          { Kind = "overlaps-in-flight"
            Meaning =
              "Its files collide with work already in flight. The holder and its lease window are named, because \"nothing schedulable\" and \"queued behind a claim that frees in ~96m\" are the same fact and two completely different instructions." }
          { Kind = "undetermined"
            Meaning =
              "WE COULD NOT DECIDE — and that is never a silent no. An unreachable answer is not a negative one. This is the case whose absence made every other case a lie waiting to happen." } ]

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
              "A rate budget is exhausted. The message names WHICH one (#897): REST takes `claim`/`take`/`who` with it, because the lock lives there (ADR-0034 §3); GraphQL takes the board reads."
            Action =
              "Back off until the reset it names — do not loop. Then `flush --dry-run`: a board write you made on an exhausted budget is QUEUED, and nothing replays it for you." }
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
            "ADR-0027. The lock lives on REST deliberately: GraphQL is the first budget to die under fan-out (#418), and a lock may never live on the budget that dies first." }

    let leaseRule: Rule =
        { Id = "claim-lease"
          Title = "The lease is a WINDOW, and an unknown age says so"
          Statement =
            "A claim's lease is 120 minutes by default (`FSGG_CLAIM_LEASE_MIN`). Past it the claim is REAPABLE — not free: only `reap` may break a lock, and an item's touch-set stays reserved until it does. A claim whose age cannot be read reports `lease unknown`, never a window."
          Because =
            "#428 (\"nothing schedulable\" and \"queued behind a claim held by <w>, lease frees in ~96m\" are the same fact and two completely different operator instructions — the first reads as an empty queue and sends a worker home) and #440/#488 (inventing \"frees in ~120m\" from a missing timestamp is a confident-but-unfounded sentence, which is the class both were closed for)." }

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
