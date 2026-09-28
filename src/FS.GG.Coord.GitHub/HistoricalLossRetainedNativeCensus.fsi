namespace FS.GG.Coord.GitHub

/// Direct, read-only GitHub evidence capture for the retained historical receipt census.
///
/// This module deliberately stops at an untrusted draft. It neither constructs a
/// `HistoricalLossRegistry` value nor grants approval or bounded-loss authority.
module HistoricalLossRetainedNativeCensus =

    open Transport

    type RepositoryIdentity =
        {
            FullName: string
            DatabaseId: int64
            NodeId: string
        }

    type Stream =
        | Identity
        | Issues
        | Pulls
        | IssueComments
        | IssueEvents
        | Timeline of issueNumber: int

    /// Exact request and response bytes for one provider page. `Terminal` is derived only from a
    /// verified continuation or a short/empty final page; a full page without `Link: rel="next"`
    /// causes an explicit next-page probe.
    type RawPage =
        {
            Pass: int
            Repository: RepositoryIdentity
            Stream: Stream
            Index: int
            Method: string
            Path: string
            Query: (string * string) list
            Status: int
            Resource: string
            Body: string
            RawSha256: string
            NextLink: string option
            ItemCount: int
            Terminal: bool
        }

    type Family =
        | DeliveryReceipt
        | IntakeReceipt
        | LegacyDoneReceipt

    type Origin =
        | IssueBody
        | IssueComment

    /// A row derived by this module from native response bodies. Callers cannot supply these rows.
    type DraftSubject =
        {
            Repository: RepositoryIdentity
            SubjectNumber: int
            SubjectIsPullRequest: bool
            NativeId: string
            Family: Family
            Origin: Origin
            CreatedAt: string
            PayloadSha256: string
            SessionOperationId: string option
        }

    /// Engine-derived, non-authoritative input for the repaired v3 binder.
    type UntrustedDraft =
        {
            ObservationHorizon: string
            Repositories: RepositoryIdentity list
            Subjects: DraftSubject list
            EvidenceFingerprint: string
        }

    type PassCapture =
        {
            Number: int
            Pages: RawPage list
            Draft: UntrustedDraft
        }

    type Capture =
        {
            First: PassCapture
            Second: PassCapture
        }

    type CollectorError =
        | TransportFailure of subject: string * detail: string
        | Unauthorized of subject: string
        | Forbidden of subject: string
        | NotFound of subject: string
        | ProviderStatus of subject: string * status: int
        | InvalidResponse of subject: string * detail: string
        | UnsafeContinuation of subject: string * detail: string
        | RosterDrift of detail: string
        | PassDrift of detail: string
        | DuplicateNativeId of nativeId: string
        | MalformedCandidate of subject: string * detail: string

    /// The exact accepted GS2-06.8 repository roster, including live database and node identities.
    val repositories: RepositoryIdentity list

    /// Capture two complete direct-enumeration passes. Every request is a GET through the single-page
    /// transport seam. The result remains non-authoritative until a separate repaired registry binder
    /// recomputes it from `RawPage` and joins independent approval evidence.
    val collectTwoPass:
        transport: ISinglePageGitHubTransport ->
        apiBase: string ->
        observationHorizon: string ->
        Result<Capture, CollectorError>
