namespace FS.GG.Coord.GitHub

/// Bounded organization Project 3 migration preparation. This is separate from the
/// normal v4 binding: unresolved native option/view IDs never grant consumer authority.
module V2Migration =
    open System
    open Errors
    open Transport

    type SelectedIssue =
        { Id: string; Repository: string; Number: int; Slice: string }

    type DesiredView =
        { Id: string option; Name: string; Layout: string; Filter: string;
          VisibleFieldIds: string list }

    type PlanningOption =
        { Id: string; Name: string }

    /// One selected issue's explicit initial human planning values. Absent native values may
    /// be initialized within a cooperative owner-controlled window; equal values are no-ops,
    /// conflicting human/automation values refuse. Omitted Status remains an explicit owner action.
    type InitialPlanning =
        { IssueId: string; Track: PlanningOption; Roadmap: string; Status: PlanningOption option }

    type PlanningValue =
        | PlanningText of string
        | PlanningSelect of PlanningOption

    type Scope =
        { SourceRevision: string; ArtifactSha256: string; CliArtifactSha256: string; PrincipalId: string;
          PrincipalLogin: string; SelectedIssues: SelectedIssue list;
          StatusFieldId: string; RoadmapFieldId: string; TrackFieldId: string;
          ObservationFieldId: string; InReviewColor: string; InReviewDescription: string;
          Views: DesiredView list; InitialPlanning: InitialPlanning list }

    type SelectOption =
        { Id: string; Name: string; Color: string; Description: string }

    type Field =
        { Id: string; Name: string; DataType: string; Options: SelectOption list }

    /// Full selected scalar planning payloads; native Text/SingleSelect IDs must be nonempty and
    /// their timestamps must match the invariant UTC provider rule. The collection key remains field ID.
    /// Untouched system values retain kind/field identity; nested labels/users/repositories are not written.
    type ItemValue =
        { Id: string; FieldId: string; Kind: string; Value: string }

    type Item =
        { Id: string; ContentId: string; ContentKind: string; Repository: string option;
          Number: int option; UpdatedAt: string; Archived: bool; Values: ItemValue list }

    type NativeIssue =
        { Id: string; Repository: string; Number: int; UpdatedAt: string; State: string }

    type View =
        { Id: string; Name: string; Layout: string; Filter: string;
          VisibleFieldIds: string list; UnsupportedConfiguration: string }

    type Snapshot =
        { ObservedAt: DateTimeOffset; PrincipalId: string; PrincipalLogin: string;
          OrganizationId: string; ProjectId: string; ProjectTitle: string;
          ProjectNumber: int; Private: bool; CanUpdate: bool; UpdatedAt: string;
          Fields: Field list; Items: Item list; Views: View list; Issues: NativeIssue list }

    type Change =
        | AddInReview of fieldId: string * retained: SelectOption list * color: string * description: string
        | AddMembership of issueId: string
        | InitializePlanningValue of issueId: string * itemId: string * fieldId: string * value: PlanningValue
        | CreateView of DesiredView
        | UpdateView of viewId: string * DesiredView

    /// A complete read proves the exact effect or still shows its exact pre-effect state.
    type ReadbackObservation =
        | ObservedEffect of Scope
        | PendingVisibility

    type Prepared =
        { Scope: Scope; Snapshot: Snapshot; Changes: Change list; OwnerActions: string list;
          Digest: string; PreparedPath: string option }

    /// Validate planning input; it is scope data, never operation admission.
    val validateScope: scope: Scope -> IoResult<unit>

    /// Read complete native schema, membership, selected planning values, view configuration and issues.
    /// Schema, item, view and selected issue first pages share one bounded native query. Complete
    /// nested first pages are observation-local; genuine continuations retain their exact parent identity.
    /// An independent final read rechecks project/principal metadata; this is not an atomic snapshot.
    /// A later effect always invalidates the prior determining snapshot and triggers fresh native reads.
    /// All reads share one 500-request/270-second invocation budget supplied by the caller.
    val collect: transport: IGitHubTransport -> scope: Scope -> IoResult<Snapshot>

    /// Derive the exact allowlisted delta without IO. Existing human planning values are retained.
    val prepare: scope: Scope -> snapshot: Snapshot -> IoResult<Prepared>

    /// Reconstruct a request only if the change occurs in the freshly derived prepared delta.
    /// This is an operation-specific boundary, not a generic GraphQL input route.
    val request: prepared: Prepared -> change: Change -> IoResult<Transport.Request>

    /// Closed bounded planning input. Unknown approval/authority properties are refused.
    val parseScope: json: string -> IoResult<Scope>

    /// Preparation report retains exact stable native tokens, unresolved semantic changes and its
    /// canonical original prepared-file/custody path. An unbound pure plan cannot become apply input.
    val encodePrepared: prepared: Prepared -> string

    /// Decode only scope and selected prepared identity; apply must freshly reproduce all native facts.
    val decodePrepared: json: string -> IoResult<Scope * string * DateTimeOffset * string>

    /// Resolve a newly created view only from one independently observed exact identity. The
    /// original semantic filter remains selected; a dependent exact-ID update stays in the same invocation.
    val resolveReadback: scope: Scope -> before: Snapshot -> after: Snapshot -> change: Change -> IoResult<Scope>

    /// Check one independent readback. Only the selected effect may differ; retained human values,
    /// option identities, unrelated cards/fields/views and determining native issue facts must agree.
    val validateTransition: scope: Scope -> before: Snapshot -> after: Snapshot -> change: Change -> IoResult<unit>

    /// Classify one complete read after a clear correlated native success. An observed effect
    /// binds its returned identity and exact transition in this same snapshot. Pending visibility
    /// permits read-only observation of the exact pre-effect state, never another send or NoSend.
    /// Malformed/partial/uncorrelated responses must not enter this path; the caller retains its
    /// original evidence and enforces the shared request/time/planning window without renewal.
    val observeTransition:
        scope: Scope -> before: Snapshot -> after: Snapshot -> change: Change -> returnedId: string -> IoResult<ReadbackObservation>

    /// Stable determining native identity excluding collection time.
    val snapshotDigest: snapshot: Snapshot -> string

    /// Recovery evidence only; never accepted as prepared apply input or renewed approval.
    val encodeReadback: scope: Scope -> snapshot: Snapshot -> string
