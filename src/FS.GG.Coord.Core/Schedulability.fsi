namespace FS.GG.Coord

/// "Is this item startable?" — ONCE.
///
/// This is #485. In the bash client that question was computed in five places and agreed in none, and
/// it took 34 issues to notice. Consolidating them helped only because the survivor was then made
/// right: three disagreements outlived the merge, and one of them was handing CLOSED issues to live
/// workers (#520).
///
/// The function is TOTAL and PURE. It cannot read anything, so it cannot mistake a failed read for a
/// legitimate "no" — the mistake behind epic #266's 51 children. Where the caller could not learn a
/// fact, it must say so in the input (`BlockerUnknown`, `LivenessUnknown`), and the answer comes back
/// as `Undetermined` rather than as a confident skip.
module Schedulability =

    open Types

    /// Whether the scheduler hands an item out in a given board column — the answer `columnStartability`
    /// gives, and the one `Protocol.boardStatuses` publishes.
    ///
    /// THREE CASES, NOT A BOOL, because the truth has three states. See `columnStartability`.
    type ColumnStartability =
        /// The scheduler offers this column unconditionally. `Ready`, and only `Ready`.
        | AlwaysStartable

        /// Offered ONLY when the caller opts in — `batch/take/next --include-backlog`. `Backlog`, and
        /// only `Backlog`. This case is the reason the type is not a `bool`.
        | WithBacklogOptIn

        /// Never offered, whatever the flags. The column is not a queue.
        | NeverStartable

    /// Why an item can or cannot be started.
    ///
    /// NOT A BOOL. Each case is a distinct fact a worker acts on differently, and collapsing them is
    /// what let real work go invisible: an epic and a forgotten touch-set rendered identically, so
    /// nine items sat unschedulable while `lint` reported `0 error(s)` over a dead queue (#496); an
    /// empty queue and a fully-blocked one printed the same sentence, so "nothing to do" was
    /// indistinguishable from "everything is blocked" (#440, #488).
    type Schedulability =
        /// Startable now.
        | Startable

        /// The board column says it is not ready. Carries which column, because `NoStatus` is its own
        /// bug (#437) and must not read as `Backlog`.
        | WrongStatus of BoardStatus

        /// The ISSUE is closed, whatever the board column says (#520). The column is a projection of
        /// the work; the issue IS the work. One was handed to a worker two hours after it was closed
        /// as completed, and then PROMOTED back to Ready on release, re-arming it for the next.
        | IssueClosed

        /// No `Paths:` line at all — an OMISSION. Nobody can pick this up, and somebody should fix it.
        | NoTouchSet

        /// `Paths: none` — a DECISION (an epic, a decision item). Unschedulable BY DESIGN, and not a
        /// bug. Telling this apart from the case above is the whole of #496.
        | DeliberatelyNoTouchSet

        /// A touch-set was declared and AT LEAST ONE of its tokens is unmatchable — `tokens` names them.
        /// A token that matches no file conflicts with nothing, and `lint` was green over that for as
        /// long as it was conflated with declaring nothing at all.
        ///
        /// ANY unmatchable token, not every one of them: a PARTLY-dead declaration is the worse case, not
        /// a lesser one. The item looks declared, and the dead tokens reserve nothing — so the files they
        /// name are invisible to every other worker's overlap check. The rule is `TouchSet.usability`'s,
        /// stated once and asked here (#864); this doc-comment used to say "EVERY token", which is a rule
        /// the code has never implemented and which `Lanes` believed (#864).
        | UnusableTouchSet of tokens: string list

        /// Blockers still hold it. Resolved means CLOSED **or MERGED** (#476).
        | BlockedBy of Blocker list

        /// The item's own `Class: decision` declaration or a `Blocked on: human/...` sentinel refuses it:
        /// a HUMAN must choose or act first (#1103/#1887). This governs REGARDLESS of the touch-set, so a
        /// decision item may keep the real `Paths:` that records where its eventual fix will land (#918)
        /// and still be refused. The distinction the empty-`Blocked by` park flattened — a DECISION nobody
        /// can start until a human chooses, versus an ACTION that makes it startable the moment it lands —
        /// rides on the `HumanBlock`; `Class: decision` maps to the decision case from the item's own text,
        /// never from the lag-prone board `Class` projection.
        | AwaitingHuman of HumanBlock

        /// A mandatory route receipt is missing, stale, malformed, or its live evidence could not be read.
        | AwaitingDeliveryRouteDecision of reasons: string list

        /// Another worker holds it, and the lock is live.
        | HeldBy of WorkerId

        /// Its lease lapsed, but its `item/<n>-*` PR is OPEN — the worker is demonstrably still
        /// working (#581). Lease expiry is EVIDENCE of abandonment, never proof, and its false
        /// positive is systematic: work that takes longer than the lease. This item is NOT free, and
        /// its touch-set stays reserved.
        | HeldByLiveWork of WorkerId * pr: int

        /// No claim marker governs it, but its own `item/<n>-*` PR is OPEN — an implementation is already
        /// in flight, whether or not anyone claimed it. #581 read this proof-of-life only THROUGH a marker,
        /// so a markerless item with an open PR fell straight through to `Startable` and was handed out a
        /// second time, costing a duplicate implementation (#651).
        | ItemPrOpen of pr: int

        /// It collides with work already in flight.
        | OverlapsInFlight of (string * string) list

        /// WE COULD NOT DECIDE. Never green, never a silent skip. This is the case whose absence made
        /// every other case a lie waiting to happen.
        | Undetermined of reason: string

    /// **WHETHER THE SCHEDULER HANDS AN ITEM OUT IN A COLUMN — the `Status` half of `schedulable`,
    /// NAMED.**
    ///
    /// NOT A BOOL, and that is the whole reason this type exists rather than a `bool` return. `Backlog`
    /// is neither startable nor not: it is startable *iff the caller opted in* (`--include-backlog`),
    /// and flattening that to either answer is a lie in the case it is most often asked about. `Backlog`
    /// is the most common park on this board, so "is a parked item startable?" is the question, and both
    /// bare answers are wrong — `false` hides `take --include-backlog`, `true` hides that plain `take`
    /// will never offer it.
    ///
    /// It exists because this fact had NO OWNER. It was three legs of a `match` INLINE in `schedulable`
    /// — reachable only by running the scheduler against a synthetic `Item` — so a document that wanted
    /// to state it had no way to READ it, and the only remaining option was to type the six answers out
    /// again. That is #865's defect and #916's trap 1: a generator whose source is hand-maintained just
    /// moves the drift upstream and hands it a generator's authority. `Protocol.boardStatuses` derives
    /// the published `startable?` column from this function, so the table a filer reads is the predicate
    /// that schedules, not a copy that agrees with it today (#983's move for `statusWireName`, #1012's
    /// for `blockerStateWireName`).
    ///
    /// `schedulable` CALLS this — it is the same decision, not a second one kept in step.
    val columnStartability: status: BoardStatus -> ColumnStartability

    /// **THE STARTABILITY WIRE VOCABULARY: a `ColumnStartability` as `facts --json` spells it, ONCE.**
    ///
    /// `Protocol.boardStatuses` publishes this string and `scripts/generate-projections` selects on it to
    /// render the `startable?` column. It lives HERE, in `Core`, beside the union it names — on exactly
    /// the terms `Types.statusWireName` (#983) and `Types.blockerStateWireName` (#1012) live there, and
    /// `kind` below.
    ///
    /// The first draft of #1057 spelled these three strings in `Snapshot.fs` instead, inside the writer.
    /// That is the defect this whole change is about, one level down: a wire vocabulary owned by a
    /// projection rather than by `Core`. MEASURED — renaming `with-backlog-opt-in` there compiled with
    /// ZERO F# errors, and only a `jq` filter in a shell script noticed. A vocabulary the compiler cannot
    /// check is a vocabulary that drifts.
    ///
    /// A TOTAL match: a fourth `ColumnStartability` case fails the BUILD here rather than reaching the
    /// wire unnamed. (The `jq` on the far side of the JSON boundary cannot be type-checked at all, so it
    /// ERRORS on a string it does not know rather than rendering it as "not startable" — #266.)
    val columnStartabilityWireName: c: ColumnStartability -> string

    /// Is `item` startable, given what is already in flight?
    ///
    /// `inFlight` is every touch-set currently held by a live claim IN THE SAME REPO. Tokens are
    /// repo-relative, so mixing repos here invents collisions (#353) — the caller owns that.
    ///
    /// `allowBacklog` mirrors `batch --include-backlog`: `next`/`take` fall back to Backlog when no
    /// Ready item is startable, and pretending otherwise is how a full queue read as an empty one
    /// (#440). It is the opt-in `ColumnStartability.WithBacklogOptIn` names.
    ///
    /// `generated` is `.github#2305`/ADR-0044's repo-relative set of generated, CI-gated artifact paths
    /// (`scripts/generated-paths`'s subtractable set). A disjointness hit (step 6) attributable SOLELY to
    /// a shared entry of `generated` is excluded — see `TouchSet.excludeGenerated`'s doc for the
    /// exact-stem-only rule that keeps a directory-prefix declaration colliding exactly as before.
    /// `Set.empty` reproduces the pre-#2305 answer exactly: passing it is always safe, and it is what a
    /// caller with no filesystem to ask (a pure snapshot-only decision path) must pass, since it has no
    /// roster to hand — see the callers in `Program.fs`'s `lanes` command.
    val schedulable: generated: Set<string> -> allowBacklog: bool -> inFlight: TouchSet list -> item: Item -> Schedulability

    /// The verdict's WIRE KIND — the token the divergence log speaks and `facts` documents, spelled ONCE
    /// (#865).
    ///
    /// Every projection of the vocabulary is emitted from this function: `Snapshot`'s `verdict` field and
    /// `Protocol.verdicts` both call it, so the log a worker greps and the doc that explains it cannot
    /// disagree BY CONSTRUCTION. They had, in both directions at once: `ItemPrOpen` was on the wire and
    /// in no doc, and the doc said `held` where the wire says `held-by`.
    ///
    /// It is a total `function` match, which is the entire point — the compiler refuses a new union case
    /// with no name here (FS0025 under warnings-as-errors). A list literal gives NO such property, and
    /// the test that claimed it in a comment was counting entries instead.
    ///
    /// `held-by`, not `held`: `held` belongs to `who`'s claim-state vocabulary and is a different
    /// question.
    val kind: Schedulability -> string

    /// The touch-set grammar, stated once. A refusal that does not say what WOULD have been accepted
    /// only moves the worker's confusion one step later.
    val TouchSetGrammar: string

    /// "Should I wait?" — as a NUMBER (#428).
    ///
    /// "nothing schedulable" and "queued behind a claim held by <w>, lease frees in ~96m" are the same
    /// fact and two completely different instructions: the first reads as an empty queue and sends a
    /// worker home.
    ///
    /// A NEGATIVE age means the age is UNKNOWN and renders as "lease unknown". A marker may be hand-
    /// written or truncated and carry no timestamp, and inventing "frees in ~120m" out of a missing
    /// field is the confident-but-unfounded sentence #440 and #488 were both closed for.
    val leaseWindow: leaseMinutes: int -> ageSeconds: int -> string

    /// The path-collision pairs, as one string.
    val collisionText: hits: (string * string) list -> string

    /// A one-line reason, for the "passed over:" list a worker reads when nothing is startable.
    ///
    /// A queue that shrinks without explanation is #440: `take` reported "no schedulable item" over a
    /// board full of work, and the worker went home.
    ///
    /// HOLDER-BLIND. It sees the item and the verdict, but a collision's HOLDER is a fact about the
    /// batch, not the item — so an `OverlapsInFlight` renders here without naming who holds the files.
    /// `Batch.explainDecision` is the one to call when a decision is in hand; it is the operator-facing
    /// renderer, and this is its fallback.
    val explain: leaseMinutes: int -> item: Item -> result: Schedulability -> string
