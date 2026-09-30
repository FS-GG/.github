namespace FS.GG.Coord.GitHub

/// Restricted, opt-in projection onto a reviewed Coordination V2 binding.
///
/// This adapter is deliberately separate from the legacy board bootstrap and write paths. It performs
/// one bounded target read, one complete membership read, one field read and at most one mutation. It
/// never adds an item or changes issue, claim, dependency, scheduling or settlement state.
module V2Projection =

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

    /// Only a complete, current source observation may be projected as Verified.
    type SourceEvidence =
        | Fresh of observedRevision: string
        | Stale of lastVerifiedRevision: string option
        | Unknown of reason: string

    type Request =
        {
            Issue: IssueRef
            Source: SourceEvidence
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
            Reads: int
            Mutations: int
        }

    /// Validate the reviewed binding without performing IO.
    val validateBinding: binding: Binding -> IoResult<unit>

    /// Run one bounded projection pass. Every run freshly verifies the exact target and field schema.
    /// A retry after a lost mutation response repeats those reads; if the desired value landed, it emits
    /// no second mutation. Any failed or incomplete read remains an error and cannot license a write.
    val runOneShot: transport: IGitHubTransport -> binding: Binding -> request: Request -> IoResult<Report>
