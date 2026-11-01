namespace FS.GG.Coord.GitHub

/// Pure derivation for the Project 3 board reconciler, `board-v2 tend` (FS-GG/.github#5136 phase 2 PR 2).
///
/// The rules, their order and their parameters are those of the accepted design,
/// docs/coordination/2026-10-31-board-reconciler-design.md §3 and §4 (rule set `fsgg:board-tend-rules/v1`).
/// `derive` reads only its arguments: the snapshot's `ObservedAt` is its only clock, so one snapshot always
/// gives the same plan. Nothing here reads the network, a file or the wall clock, and nothing here writes.
///
/// Scope of this module: Work level (W1h, W1d), Track (T1d, T1i, T1f), Delivery Status (S1, S3, S3b, S4,
/// S9, S9r and the proposals), the Outcome roll-up (O1), Roadmap for enrolled cards (RM1) and Delivery
/// closure (C1, owner decision 9). Enrollment candidates (E1, E2) and archive planning (A1) are later
/// deliveries of the same design.
module V2Tend =
    open System

    type WorkLevel =
        | Outcome
        | Delivery

    type Track =
        | ActiveDelivery
        | FollowUp

    type Status =
        | Backlog
        | Ready
        | InProgress
        | InReview
        | Blocked
        | Done

    type ContentKind =
        | Issue
        | PullRequestCard
        | DraftCard
        | Unreadable

    type PrState =
        | PrOpen
        | PrClosed
        | PrMerged

    /// How a PR's body refers to this card, judged by the board-currency `linkage_of()` semantics.
    type Linkage =
        /// The verdict has no failures and names exactly this Delivery.
        | Strict
        /// The PR mentions the card but `linkage_of()` fails; the failure reason is kept for P-LINK.
        | Loose of reason: string

    type LinkedPr =
        { Id: string
          State: PrState
          Draft: bool
          Linkage: Linkage
          UpdatedAt: DateTimeOffset
          MergedAt: DateTimeOffset option
          /// The PR body carries `<!-- fsgg:delivery-continues/v1 -->` (outside code spans and fences).
          Continues: bool
          /// The PR body carries `<!-- fsgg:delivery-completes/v1 -->` (outside code spans and fences).
          Completes: bool }

    /// The intended-fields line `Work level: <v> · Track: <v> · Intended Status: <v>`; each part may be absent.
    type IntendedFields =
        { WorkLevel: WorkLevel option
          Track: Track option
          Status: Status option
          /// The issue author, and the last body editor if the body was edited, are in `rootLogins`.
          RootAuthored: bool }

    type Blocker = { Ref: string; Open: bool }

    type Card =
        { ItemId: string
          /// `OWNER/REPO#N`; an unreadable card may have none.
          Ref: string
          Kind: ContentKind
          Open: bool
          CreatedAt: DateTimeOffset
          UpdatedAt: DateTimeOffset
          ClosedAt: DateTimeOffset option
          Status: Status option
          StatusUpdatedAt: DateTimeOffset option
          WorkLevel: WorkLevel option
          Track: Track option
          Roadmap: string option
          /// Native parent reference, whether or not it is on the board.
          Parent: string option
          /// Native sub-issue references with their open state, whether or not they are on the board.
          SubIssues: (string * bool) list
          SubIssuesComplete: bool
          Blockers: Blocker list
          BlockersComplete: bool
          /// Blockers named by the newest Root `fsgg:blocked-on/v1` marker; None when comments were not read.
          MarkerBlockers: Blocker list option
          Prs: LinkedPr list
          TimelineComplete: bool
          /// None when the body has no intended-fields line in the closed grammar.
          Intended: IntendedFields option
          BlockedOn: string option
          AcceptanceEvidence: string option
          Enrolled: bool
          EnrolledRoadmap: string option
          /// The issue carries a Root `fsgg:tend-closed/v1` comment: it was closed by tend and reopened since.
          TendClosedBefore: bool }

    /// One entry of the local deferral ledger, keyed by `<rule>:<item>:<field>`.
    type Deferral =
        { Key: string
          FirstDeferredAt: DateTimeOffset
          Runs: int }

    type Snapshot =
        { ObservedAt: DateTimeOffset
          Cards: Card list
          Ledger: Deferral list }

    type Parameters =
        { ActivityHours: float
          DoneSettleMinutes: float
          ReviewSettleHours: float
          LeafSettleHours: float
          CloseSettleHours: float
          ExpiryRuns: int
          ExpiryHours: float
          /// Rule ids whose effects may be applied; every other rule's effects are listed as would-apply.
          Enabled: Set<string> }

    /// The v1 parameter values of the design (§3) with the given enabled rules.
    val v1: enabled: Set<string> -> Parameters

    /// Every Apply rule id this module can produce, including C1 (closures are listed in `Plan.Close`).
    val applyRules: Set<string>

    type Field =
        | StatusField
        | WorkLevelField
        | TrackField
        | RoadmapField

    val fieldName: field: Field -> string

    /// One field change. Successive moves on one field in one derivation collapse into one effect from the
    /// original preimage to the final target, carrying every rule in the chain.
    type Effect =
        { Id: string
          ItemId: string
          Ref: string
          Field: Field
          Preimage: string option
          Target: string
          Rules: string list
          /// Effects of this plan that must be sent first (a parent's fill, a child's move).
          DependsOn: string list }

    type DeferralReason =
        | InputPending
        | RuleDisabled
        | SettleWindow

    type Deferred =
        { Key: string
          ItemId: string
          Ref: string
          Field: Field
          Rule: string
          Reason: DeferralReason
          /// Counts toward expiry: input-pending deferrals, and disabled T1d, T1i and W1d. Settle windows and every
          /// other disabled rule never count and never become proposals.
          Counts: bool
          /// Past 3 runs or 24 hours: the field is also proposed (and still applied later if it settles).
          Expired: bool
          /// The proposal reason on expiry (`track-declared-pending`, `track-undetermined`, ...), or
          /// `rule-disabled:<rule>` / `settle-window:<rule>` for an entry that never expires.
          Label: string }

    type Proposal =
        { Id: string
          Ref: string
          Rule: string
          Reason: string
          Suggested: string option }

    type Refusal =
        { Id: string
          Ref: string
          Rule: string
          Reason: string }

    type Comparison = { Pairs: int; Disagreements: int }

    /// Rule C1 (owner decision 9, FS-GG/.github#4730 issuecomment-6551301543): close a Delivery issue when
    /// every strictly linked PR is merged, at least one is, none is open, and the last merge does not carry
    /// `fsgg:delivery-continues/v1`. Never an Outcome, never a Blocked card.
    type Closure =
        { Id: string
          ItemId: string
          Ref: string
          /// The merged PRs (`OWNER/REPO#N`), oldest merge first, for the closing comment.
          MergedPrs: string list }

    type Plan =
        { Ruleset: string
          ObservedAt: DateTimeOffset
          Apply: Effect list
          /// Effects of rules that are not enabled, shown for comparison; never sent.
          WouldApply: Effect list
          Deferred: Deferred list
          Propose: Proposal list
          Refuse: Refusal list
          /// T1i's would-be Track against people's Tracks on existing undeclared Delivery-Outcome pairs.
          T1iComparison: Comparison
          /// C1 closures (decision 9): open, non-Blocked Deliveries whose strictly linked PRs are all merged.
          Close: Closure list
          /// C1 closures that would happen if C1 were enabled; never sent.
          WouldClose: Closure list }

    val ruleset: string

    /// Derive the plan. Pure; deterministic in the order of cards, PRs, sub-issues and blockers.
    /// Liveness holds over the enabled rules: after max(k, 3) runs or 24 hours every field is filled, refused,
    /// proposed, or waiting only on a rule that is not enabled (a would-apply entry, never a proposal).
    /// A closed card without `closedAt` is refused as Unknown. An Outcome with no sub-issues and an empty Status
    /// is proposed (`no-open-delivery`) and never filled; an Outcome's intended Status is never proposed.
    val derive: parameters: Parameters -> snapshot: Snapshot -> Plan

    /// Apply a plan's Apply effects to a snapshot in memory, as a successful send and readback would.
    val applyPlan: plan: Plan -> snapshot: Snapshot -> Snapshot

    /// The next deferral ledger after a run at `now`: counting deferrals (expired or not) are carried, new
    /// ones start at one run, and keys that are no longer deferred are dropped.
    val nextLedger: plan: Plan -> previous: Deferral list -> now: DateTimeOffset -> Deferral list

    /// Every effect that depends on `effectId`, transitively; the writer defers them when it is refused.
    val dependantsOf: plan: Plan -> effectId: string -> string list

    /// Parse an intended-fields line in the closed grammar (emphasis markup removed). None for any other form.
    val parseIntendedLine: line: string -> rootAuthored: bool -> IntendedFields option

    /// Decode a `fsgg.board-tend-snapshot/1` JSON document (fixtures, and later the reader's output).
    val decodeSnapshot: json: string -> Result<Snapshot, string>

    /// Encode a plan as the `fsgg.board-tend-plan/1` JSON document (local, private custody: it names cards).
    val encodePlan: plan: Plan -> string
