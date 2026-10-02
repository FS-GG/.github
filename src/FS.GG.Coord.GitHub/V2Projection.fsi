namespace FS.GG.Coord.GitHub

/// Restricted, opt-in projection onto a reviewed Coordination V2 binding.
///
/// This adapter is deliberately separate from the legacy board bootstrap and write paths. It invokes
/// one required source verifier, verifies the immutable owner and target, drains at most ten pages of
/// fifty native memberships, and validates item/content identity on each Observation read. It performs
/// at most one mutation and requires independent post-write readback. It never adds an item or changes
/// issue, claim, dependency, scheduling or settlement state.
module V2Projection =

    open System
    open Errors
    open Transport

    type SelectFieldBinding =
        {
            Id: string
            Options: Map<string, string>
        }

    /// Immutable identities copied from an independently reviewed board binding.
    type Binding =
        {
            SchemaVersion: int
            RecipeRevision: string
            PopulationRevision: string
            OrganizationId: string
            ArtifactSha256: string
            OwnerKind: OwnerKind
            Owner: string
            ProjectNumber: int
            ProjectTitle: string
            ProjectId: string
            Status: SelectFieldBinding
            RoadmapFieldId: string
            Track: SelectFieldBinding
            Observation: SelectFieldBinding
            Repositories: Set<string>
        }

    type IssueRef =
        {
            Owner: string
            Repository: string
            Number: int
        }

    /// Result returned by the required authoritative source verifier.
    type SourceVerification =
        | Current of observedRevision: string
        | Stale of lastVerifiedRevision: string option
        | Refused of reason: string

    /// Read-only authority boundary. A production composition root must inject its reviewed canonical
    /// source reader; a projection request cannot supply or mint currentness itself. This module cannot
    /// authenticate an arbitrary function value, so a future live route must keep verifier construction
    /// internal and must not accept a verifier or `SourceVerification` from command/request input.
    type SourceVerifier = IssueRef -> IoResult<SourceVerification>

    type Request =
        {
            Issue: IssueRef
            ExpectedNodeId: string
        }

    type Outcome =
        | AlreadyCurrent of itemId: string
        | Updated of itemId: string

    type Report =
        {
            ProjectId: string
            RecipeRevision: string
            Issue: IssueRef
            ObservedRevision: string
            Observation: string
            Outcome: Outcome
            SourceChecks: int
            ProjectReads: int
            Mutations: int
            VerifiedAt: DateTimeOffset
        }

    /// Validate the reviewed binding without performing IO.
    val validateBinding: binding: Binding -> IoResult<unit>

    /// Run one bounded projection pass. Every run first consumes the required authoritative source
    /// verifier, then freshly verifies the exact target and field schema.
    /// The source snapshot is revalidated immediately before a mutation. A successful field update
    /// requires independent readback. A retry after a lost mutation response repeats those reads;
    /// if the desired value landed, it emits
    /// no second mutation. Any failed or incomplete read remains an error and cannot license a write.
    /// Production commands must use V2ProjectionSource.runFixed; verifier injection is the fixture seam.
    val runOneShot:
        verifySource: SourceVerifier ->
        transport: IGitHubTransport ->
        binding: Binding ->
        request: Request ->
            IoResult<Report>
