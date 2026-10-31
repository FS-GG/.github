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

    /// Private invocation-only enrollment bytes. Never serialize this value into reports or public source.
    type ProgrammeEnrollment = { NonceHex: string; PayloadBase64: string }

    /// Immutable identities copied from an independently reviewed board binding.
    /// BindingVersion 2 retains historical organization pilot controls; version 3 selects one
    /// organization-owned product project and one owner-local repository with one-to-five issues.
    /// Version 4 is the source-only Project 3 successor, schema 2, with one-to-100 exact native issues
    /// and In review. It never falls back to an older binding. Observation-only refresh requires
    /// the exact successor native schema, selected issue identity and current source joins.
    /// Version 5 explicitly selects schema 3 and Work level Outcome/Delivery; it retains exact private enrollment.
    /// Versions 4 and 5 require private programmeEnrollment; version 3 keeps its exact wire contract.
    type Binding =
        {
            ProgrammeEnrollment: ProgrammeEnrollment option
            BindingVersion: int
            ImportRecipeRevision: string
            ImportArtifactSha256: string
            SelectedIssues: Map<string, string>
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
            WorkLevel: SelectFieldBinding option
            Repositories: Set<string>
        }

    /// Explicit supported organization modes; never treats future versions as admitted.
    val isProgrammeBinding: binding: Binding -> bool

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

    /// The programme enrollment row ceiling the reader enforces, for both the private payload rows and the binding's
    /// selected issues: 500 (#5119). Readers before #4941 refuse more than 100; coherent 0.114.0 readers refuse more than 250.
    val maximumProgrammeRows: int

    /// The Coordination board item limit (500, #5119). The complete project-membership read refuses a board that reports more.
    val maximumBoardItems: int

    /// The ceiling before the first lift. Any population at or below it is readable by every reader of the line.
    val legacyProgrammeRowCap: int

    /// The ceiling of the coherent 0.114.0 readers (250). A population above it is readable only by a 500-row reader.
    val previousProgrammeRowCap: int

    /// The CLI bounded transport's request ceiling (shared by inspect and refresh, scaled from inspect measurements
    /// only; an unbatched refresh does not fit it above about 80 rows, #5131) for a binding that selects this many rows: never below the
    /// historical 500, and scaled by the selected population up to `maximumProgrammeRows`.
    val refreshRequestCeiling: selectedRows: int -> int

    /// The CLI bounded transport's deadline in seconds (scaled from inspect measurements only) for a binding that selects this many rows: never below the
    /// historical 270, scaled by the selected population up to `maximumProgrammeRows` (410 seconds at 500 rows).
    val refreshDeadlineSeconds: selectedRows: int -> float

    /// Validate the reviewed binding without performing IO.
    val validateBinding: binding: Binding -> IoResult<unit>

    /// Decode closed bounded UTF-8 binding JSON (256KiB, depth16, no duplicate keys).
    /// Version4 requires its private envelope; historical/product shapes reject it. Errors are sanitized.
    val decodeBinding: json: string -> Result<Binding, string>

    /// Hash exact private bytes with the fixed NUL domain,32-byte nonce and u64 big-endian length.
    /// Decoded payload is strict canonical Base64/UTF-8 JSON, at most128KiB/depth16 without duplicate keys.
    val programmeCommitment: value: ProgrammeEnrollment -> IoResult<string>

    /// Validate the complete closed private payload against the selected exact binding; grants no enrollment.
    val programmeRows: binding: Binding -> IoResult<(string * string * string) list>

    /// Validate only the closed bounded public v3 document and return its digest.
    /// This offline shape check grants no authentication, enrollment or native authority.
    val decodeProgrammePublicCommitment: publicJson: string -> IoResult<string>

    /// Authenticate private enrollment against the fixed protected public two-field v3 commitment.
    /// Only native source acquisition and freshness establish authority; a local digest grants none.
    val authenticateProgrammeEnrollment: binding: Binding -> publicJson: string -> IoResult<(string * string * string) list>

    /// Pure offline preparation seam. Template is the v4 binding WITHOUT programmeEnrollment.
    /// Validate payload/template before invoking nonceFactory exactly once; production supplies CSPRNG32.
    /// Returns private binding/public commitment JSON; caller owns exclusive durable custody and readback.
    /// Invalid inputs return sanitized errors and never qualify executable bytes or native acceptance.
    val prepareProgrammeBinding:
        bindingTemplateJson: string -> payload: byte array -> nonceFactory: (unit -> byte array) -> IoResult<string * string>


    /// Run one bounded projection pass. Every run first consumes the required authoritative source
    /// verifier, then freshly verifies the exact target and field schema.
    /// The source snapshot is revalidated immediately before a mutation and, for v4, a Verified no-op.
    /// Only Observation is written; Status/Track/Roadmap and action/resource selection are independent.
    /// A successful field update
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

    /// Complete exact-project population. Unexpected members remain visible but unenrolled.
    type Membership =
        { ItemId: string; ContentId: string; Kind: string; Issue: IssueRef option; Enrolled: bool }
    val readMemberships: transport: IGitHubTransport -> binding: Binding -> IoResult<Membership list>

    /// Fresh immutable owner/schema/native membership and four planning values. Performs queries only;
    /// unseeded fields remain None, incomplete or mismatched identities remain errors. No projection.
    /// `WorkLevel` is read in the same membership query for a binding-5 successor with a bound Work level field
    /// (#5119): `Some("Unknown", None)` when unset, `Some(name, Some optionId)` when set, `None` when not read.
    type PlanningObservation =
        { ItemId: string; Status: string option; Roadmap: string option; Track: string option
          Observation: string option; MembershipPages: int; WorkLevel: (string * string option) option }
    val readPlanning: transport: IGitHubTransport -> binding: Binding -> request: Request -> IoResult<PlanningObservation>
