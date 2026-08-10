namespace FS.GG.Coord

/// Pure, fail-closed material-transition and active-inventory projection over live board, claim, PR,
/// review, and delivery-obligation facts (.github#2135).
///
/// Board drivers used to DETECT transitions and RECONSTRUCT the active-item inventory from host
/// memory — late, sometimes omitting externally claimed work, sometimes reporting a still-live claim
/// as terminal. This module makes both a deterministic READ: given a prior cursor and a fresh live
/// fact snapshot, it emits exactly the transitions that changed and renders the complete active set
/// every time, independent of whether anything changed. It performs no IO — as with `Schedulability`
/// and `Batch`, a failed read is an explicit INPUT (`ItemFacts.ReadOk = false`), never inferred from
/// what is missing, so it cannot be mistaken for "nothing is active".
module DriverEvents =

    /// The typed material states a driver-observable item can be in (issue #2135 acceptance #2).
    /// Reuses `Delivery.Stage`'s and `Driver.ReviewChain`'s existing vocabulary rather than inventing a
    /// second one (clarify DEC-001/DEC-002): review sub-states come from `Driver.ReviewChain`, and the
    /// post-merge split comes from `Delivery.Obligation` receipts.
    type MaterialState =
        | Ready
        | Claimed of worker: string
        | ReviewHandoff of critic: string option
        | ReviewRepair of round: int
        | CiLandable
        | MergedAwaitingObligations of pr: int
        | Released
        | HumanBlocked of reason: string
        | Done
        /// A live read that failed or returned incomplete data for this item. Distinct from every other
        /// case so it can never be mistaken for a legitimate "no active state" (issue acceptance #7).
        | Unreadable of reason: string

    /// One item's live fact snapshot, assembled by the caller from the SAME board/claim/PR/review/
    /// delivery reads `driver`/`delivery` already perform. Never mutated; classification derives the
    /// NEXT state from this, the cursor supplies the LAST one.
    type ItemFacts =
        { Ref: string
          ReadOk: bool
          UnreadableReason: string option
          BoardStatus: Types.BoardStatus option
          IssueState: Types.IssueState option
          ClaimWorker: string option
          HumanBlock: Types.HumanBlock option
          Pr: int option
          Review: Driver.ReviewChain option
          Merged: bool
          ObligationsDeclared: bool
          Obligations: Delivery.Obligation list
          /// A short human-readable pointer to the fact(s) that produced the classification — a claim
          /// marker id, PR head SHA, commit SHA, or similar. Carried through to the emitted event
          /// unchanged; this module never invents evidence.
          Evidence: string
          ObservedAt: int64
          SourceSha: string }

    /// One item, classified: its live facts plus the `MaterialState` and reason `classify` derived from
    /// them.
    type Classified =
        { Ref: string
          State: MaterialState
          Reason: string
          Evidence: string
          ObservedAt: int64
          SourceSha: string }

    /// The durable, versioned per-item cursor — the LAST material state observed for each ref. A ref
    /// absent from the cursor has never been observed; its first classification is always a transition.
    type Cursor = Map<string, MaterialState>

    /// A material transition: strictly a CHANGE from the cursor's last-known state for this ref.
    type TransitionEvent =
        { Ref: string
          Previous: MaterialState option
          New: MaterialState
          Reason: string
          Evidence: string
          ObservedAt: int64
          SourceSha: string }

    /// The rendered projection for one refresh: the transitions since the cursor, PLUS the complete
    /// active inventory — rendered unconditionally, because "nothing transitioned" and "nothing is
    /// active" are different facts and must never collapse into each other (issue acceptance #3/#7).
    type Projection =
        { Transitions: TransitionEvent list
          Active: Classified list
          Cursor: Cursor
          RenderedAt: int64 }

    /// Derive one item's typed material state and a short reason from its live facts. Pure and total.
    val classify: ItemFacts -> Classified

    /// True exactly for the states issue #2135 acceptance #4 names active: claimed, in review (handoff
    /// or repair), CI-landable, or merged with unverified obligations. `Ready`, `Released`, `Done`,
    /// `HumanBlocked`, and `Unreadable` are never active.
    val isActive: MaterialState -> bool

    /// Diff a fresh classification against the cursor, emitting one `TransitionEvent` per ref whose
    /// state differs from `Map.tryFind ref cursor`, and returning the updated cursor. Re-deriving over
    /// an UNCHANGED classification is idempotent: applying the returned cursor as the next call's input
    /// over the same facts yields zero events (issue acceptance #5) — with ONE deliberate exception:
    /// `Unreadable` re-emits on EVERY read for as long as it persists, even against an unchanged
    /// cursor entry (fsgg:independent-review:v1 round 1). A stable `Unreadable` is not "no news" the
    /// way a stable `Claimed` is — it is a standing failure, and idempotency-by-default would let a
    /// broken item go completely silent after its first announcement, which is indistinguishable from
    /// "recovered" or "never broken" to anything reading only the transition line (issue acceptance #7:
    /// a failed read must never become an empty or successful result).
    val deriveEvents: cursor: Cursor -> classified: Classified list -> TransitionEvent list * Cursor

    /// Classify every item's facts, diff against the cursor, and render the complete projection.
    val project: cursor: Cursor -> facts: ItemFacts list -> observedAt: int64 -> Projection

    /// A short, stable machine label for a material state — the vocabulary the cursor file and the text
    /// renderer both use. Round-trips through `decodeState`.
    val encodeState: MaterialState -> string

    /// The inverse of `encodeState`. `None` for any string this module did not itself produce — a
    /// cursor file from a newer/foreign encoding is read as "unknown", never guessed at.
    val decodeState: string -> MaterialState option

    /// The stable two-line text projection (issue acceptance #3): line one names the material
    /// transitions since the cursor (or "no material transitions"), line two names the complete active
    /// inventory (or "no active items").
    val renderText: Projection -> string
