namespace FS.GG.Coord.GitHub

/// Fixed canonical composition; no caller-supplied currentness, verifier, URL or executable recipe.
module V2ProjectionSource =
    open Errors
    open Transport
    open System
    open V2Projection

    type NativeObservation =
        { Issue: IssueRef; NodeId: string; UpdatedAt: string; State: string; Url: string; BodySha256: string }

    /// Native closure, delivery, publication and operation acceptance remain distinct observations.
    /// Failed reads retain the project's prior value and report Unknown/Stale outside the board.
    type ItemReport =
        { Issue: IssueRef
          Native: NativeObservation option
          DependencyObservations: NativeObservation list
          DependencyReadComplete: bool
          PlanObservation: NativeObservation option
          Delivery: string
          Publication: string
          NativeAcceptance: string
          Health: string
          Reads: int
          MutationAttempts: int
          MembershipPages: int option
          Projection: Report option
          Gap: IoError option
          LastVerified: Report option }

    type BatchReport =
        { ProjectId: string
          RecipeRevision: string
          PopulationRevision: string
          ImportRecipeRevision: string
          ImportArtifactSha256: string
          ArtifactSha256: string
          NativeDispatch: V2ObservationTransport.DispatchReceipt list
          ProtectedInputs: (string * string * string) list
          Selected: int option
          Attempted: int
          Verified: int
          Items: ItemReport list
          PopulationGap: IoError option
          Cleanup: string }

    /// The single hard-coded programme issue URL the installed reader admits as an owning plan.
    val programmePlanIssueUrl: string

    /// Pure grammar of a declared owning-plan blob URL as the installed reader applies it: exact
    /// https://github.com blob URL in a bound repository, ref `main` or a 40-hex commit, and a `docs/`
    /// path or one of the maintained observation references. Returns (repository, ref, path) or the
    /// reader's own refusal detail. No transport, no native read, no authority; the installed reader
    /// calls this same function so a planner cannot drift from it.
    val programmePlanBlobGrammar: repositories: Set<string> -> url: string -> Result<string * string * string, string>

    /// Verify the loaded adapter assembly hash before IO. Read the fixed canonical manifest at the
    /// admitted population commit, validate the complete three-to-five organization pilot or one-to-five
    /// receiver repository cohort and exact binding, then
    /// freshly read each native issue, complete blocked_by snapshot (including empty) and finite owning plan.
    /// This selected pilot admits only reviewed empty dependency snapshots; any new edge is Stale.
    /// An issue revision differing from the reviewed manifest is Stale. Unknown reads license no write.
    /// Verified means complete/current native observation, never acceptance of the owning outcome.
    /// Version 2 retains the organization manifest. Version 3 reads the same import/v1 manifest path
    /// from its sole selected receiver repository, checks its pinned/current main blob and reads only
    /// that repository's canonical docs/native plan. Product metadata never comes from the organization
    /// manifest. Version 4 authenticates exact private payload identities through the protected enrollment/v3 commitment and current native closure/dependencies/
    /// plan facts. Exact protected enrollment may declare HTTPS GitHub blob URLs in the bound
    /// repositories: docs paths or the maintained programme observation reference. Main resolves to
    /// a fresh native commit/blob each observation; immutable 40-hex plan refs remain pinned.
    /// It rechecks enrollment and source before writes and Verified no-ops.
    /// Complete/current observation health is independent of runtime capacity/Ready and outcome acceptance.
    /// Previous reports are historical display evidence only and cannot make a write current.
    val runFixed:
        transport: IGitHubTransport ->
        binding: Binding ->
        previous: BatchReport option ->
            BatchReport

    /// Purpose is set at the native read site, never inferred from diagnostic strings.
    /// Missing, inconsistent or unclassified gap evidence cannot establish eligibility.
    type NativeReadPurpose =
        | Unclassified = 0
        | ScopeEvidence = 1
        | ReviewAvailability = 2
        | CheckAvailability = 3

    type NativeReadGap = { Purpose: NativeReadPurpose; Detail: string }

    /// Read evidence and human intent remain distinct. No new verification timestamp or write is minted.
    /// Native head-bound observations; none of these facts establishes deployment acceptance.
    /// ScopeCurrent requires complete associations/files and a final matching head barrier.
    /// Review/check availability stays visible and prevents aggregate completeness. Source observation
    /// and implementation can retain eligibility with exact scope/head/association/files and all other
    /// prerequisites; review, qualification and activation remain conservative. Unclassified gaps refuse.
    type PullRequestFacts =
        { Id: string; Repository: string; Number: int; Url: string; State: string; Head: string; UpdatedAt: string
          Author: string option; Issues: string list; Files: string list; ScopeCurrent: bool; ReviewDecision: string option
          Reviews: string list; Checks: string list; Gaps: string list; GapDetails: NativeReadGap list }

    /// ObservedAt is the conservative collection-start time, including failed collection attempts.
    /// MinValue means collection was never attempted; it is an absent-observation sentinel and is
    /// never fresh, even when a caller supplies MinValue as its evaluation time. The v4 wire emits null.
    type NativeIntegratorFacts =
        { PullRequests: PullRequestFacts list; RepositoryGaps: Map<string, string>; ObservedAt: DateTimeOffset }

    /// Attributed caller observations for one bounded invocation. Supplier/evidence text and the
    /// input digest are not authentication. Native issue/body/plan/PR joins are re-read independently.
    /// Stable slice/attempt identity separates independent work under one enrolled outcome.
    /// Operation defines consumed demand: implementation, review or hosted-CI uses one matching
    /// remaining-after-active slot; existing-PR observation uses none. Activation stays owner-gated.
    type Action = Source | Activation
    type Operation = ObserveExisting | Implement | ReviewWork | QualifyWork | ActivateWork
    type AttributedObservation =
        { Supplier: string; ObservedAt: DateTimeOffset; ValidUntil: DateTimeOffset; Evidence: string list }
    type SliceJoin =
        { NativeId: string; UpdatedAt: string; BodySha256: string; SourceRevision: string; HierarchySha256: string option }
    /// Non-None declared work requires exactly one proposed-action reference "current-deliverable".
    /// True reconciles scoped nonduplication under the fresh exact body/plan join, never completion or authority.
    type Prerequisite = { Action: Action; Reference: string; Satisfied: bool option; SupplyingOwner: string }
    type CurrentSlice =
        { Id: string; Attempt: string; Operation: Operation; Issue: IssueRef
          AccountableOwner: string; Owner: string; Repository: string; Paths: string list; Action: Action
          Join: SliceJoin; Observation: AttributedObservation; PullRequest: string option; PullRequestId: string option
          PullRequestUpdatedAt: string option; Head: string option
          Prerequisites: Prerequisite list; PrerequisitesComplete: bool; IntakeAuthorized: bool option }
    type Reservation =
        { SliceId: string; Attempt: string; Operation: Operation; Owner: string; Repository: string; Paths: string list
          PullRequest: string option; Head: string option; CustodyReference: string }
    type SelectionContext =
        { Invocation: string; ProjectId: string; SchemaVersion: int; RecipeRevision: string; ArtifactSha256: string
          PopulationRevision: string; EnrollmentBlob: string; EnrollmentCommitment: string; InputSha256: string
          Slices: CurrentSlice list; ImplementationSlots: int option; ReviewSlots: int option; HostedCiSlots: int option
          CapacityMeaning: string; CapacityObservation: AttributedObservation option
          Reservations: Reservation list option; ReservationObservation: AttributedObservation option
          ObservedAt: DateTimeOffset; ValidUntil: DateTimeOffset; Evidence: string }
    type Selection =
        { SliceId: string option; Attempt: string option; Operation: Operation option
          Issue: IssueRef; Action: Action; Decision: string; Owner: string option; PullRequest: string option
          Head: string option; Reasons: string list; Evidence: string list }

    type NativeIntakeObservation =
        { Admission: BoardIntake.Admission option; Allowed: bool option; ObservedAt: DateTimeOffset; Gap: IoError option }

    /// CurrentDeliverable retains bounded native field text (16KiB UTF-8); exact "None" declares absence.
    /// Deliverables contains only canonical selected-repository PR discovery hints, joined independently.
    /// Native hierarchy observations never widen enrollment or establish acceptance.
    type HierarchyIdentity = { Issue: IssueRef; NodeId: string }
    type HierarchyChild =
        { Issue: IssueRef; NodeId: string; State: string; StateReason: string option; ClosedAt: string option }
    type WorkItemObservation =
        { WorkLevel: string; WorkLevelOptionId: string option; Parent: HierarchyIdentity option
          Children: HierarchyChild list; Complete: bool; Gap: string option; ObservedAt: DateTimeOffset
          Sha256: string option; StateReason: string option; ClosedAt: string option
          DoneWhen: string option; BlockedOn: string option; AcceptanceEvidence: string option; ClosureGaps: string list }

    type InspectionItem =
        { Evidence: ItemReport; ExpectedNodeId: string; SourceCurrentness: string
          Planning: PlanningObservation option; PlanningGap: IoError option; Discrepancies: string list; Owner: string option; SourceRevision: string option
          CurrentDeliverable: string option; Deliverables: string list; DeliverableGap: string option; Intake: NativeIntakeObservation
          WorkItem: WorkItemObservation option }

    type InspectionReport =
        { Binding: Binding; Evidence: BatchReport; Items: InspectionItem list
          Memberships: Membership list; MembershipGap: IoError option; NativeIntegrator: NativeIntegratorFacts }

    /// Facts supplied by the existing integrator; None means unknown and grants no authority.
    type IntegratorFacts =
        { OpenPullRequests: string list option; TouchSets: Map<string, string list> option; AvailableSlots: int option }
    type PlanningCandidate =
        { Issue: IssueRef; HumanStatus: string option; Track: string option; Roadmap: string option
          SourceCurrentness: string; Observation: string option; UnmetOrUnknown: string list; Integrator: IntegratorFacts }
    /// Programme v4 enrollment/v2 separates exact native/plan identities from current issue, closure,
    /// dependencies and protected-main plan revisions. Native PR observations include non-enrolled
    /// in-flight work, bounded full connection reads, head-bound reviews/checks and final head barriers.
    /// Fixed queries/selected GET reads only. No writer, mutation or durable retry composition.
    val createInspectionTransport: binding: Binding -> token: string -> IoResult<IGitHubTransport * System.IDisposable>
    /// Reuse admitted source collection and exact planning readers. Never calls runOneShot or any writer.
    val inspectFixed: transport: IGitHubTransport -> binding: Binding -> previous: BatchReport option -> InspectionReport
    /// Internal causal qualification control; never selected by a production caller.
    val internal inspectUnbatched: transport: IGitHubTransport -> binding: Binding -> previous: BatchReport option -> InspectionReport

    /// Deterministic display of selected human intent and known/unknown evidence. No scheduling decision.
    val planningCandidates: report: InspectionReport -> facts: IntegratorFacts -> PlanningCandidate list

    /// Join attributed observations to exact native issue/body/plan/enrollment/PR facts and fold
    /// explicit integrator order into per-slice selected/deferred results. Unselected alternatives
    /// are not reservations. Repository gaps affect relevant slices; binding/membership gaps are global.
    /// Expiry never releases original holds; self exemption requires exact slice/attempt/operation/PR/head.
    /// Read-only existing-PR observation consumes no new slot. Never allocates, dispatches or accepts effects.
    val selectActions: report: InspectionReport -> context: SelectionContext option -> now: System.DateTimeOffset -> Selection list
