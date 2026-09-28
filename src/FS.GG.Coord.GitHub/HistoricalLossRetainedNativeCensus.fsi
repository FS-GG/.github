namespace FS.GG.Coord.GitHub

/// Direct, read-only GitHub evidence capture for the retained historical receipt census.
///
/// Collection yields an untrusted draft. Binding requires replay of both retained raw
/// passes and the independently approved v3 registry and v2 approval envelope.
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
            ApiVersionRequested: string
            ApiVersionSelected: string
            Path: string
            Query: (string * string) list
            Status: int
            Resource: string
            Body: string
            RawSha256: string
            LinkHeader: string option
            ObservedAt: string
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

    /// A row derived by this module from native response bodies. The binder recomputes these rows.
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
            PayloadBlobSha: string
            SessionOperationId: string option
        }

    /// Engine-derived, non-authoritative input for the repaired v3 binder.
    type UntrustedDraft =
        {
            ObservationHorizon: string
            Repositories: RepositoryIdentity list
            Subjects: DraftSubject list
            EvidenceFingerprint: string
            EligibleInventoryDigest: string
        }

    type PassCapture =
        {
            Number: int
            Pages: RawPage list
            Draft: UntrustedDraft
            RawEvidenceDigest: string
        }

    type Capture =
        {
            First: PassCapture
            Second: PassCapture
        }

    /// Bounded-memory readback summary for a private two-pass capture.
    type PrivateCaptureSummary =
        {
            ObservationHorizon: string
            FirstPageCount: int
            FirstRawEvidenceDigest: string
            SecondPageCount: int
            SecondRawEvidenceDigest: string
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
    /// transport seam. Raw pages may differ while the eligible native roster and typed receipt set remain stable.
    val collectTwoPass:
        transport: IVersionedSinglePageGitHubTransport ->
        apiBase: string ->
        observationHorizon: string ->
        Result<Capture, CollectorError>

    /// Capture directly into a new private 0600 file. Each raw page is persisted and discarded
    /// before the next request; the complete artifact is scanned once before atomic publication.
    val collectTwoPassPrivate:
        transport: IVersionedSinglePageGitHubTransport ->
        apiBase: string ->
        observationHorizon: string ->
        path: string ->
        Result<PrivateCaptureSummary, string>

    /// Replays the exact two retained raw passes through the production decoder before binding.
    /// Caller-supplied typed drafts are ignored; missing or altered raw pages refuse.
    val bindV3Captured:
        apiBase: string ->
        capture: Capture ->
        expectedFamily: string ->
        expectedScope: string ->
        expectedObservationHorizon: string ->
        registryBytes: byte array ->
        entry: FS.GG.Coord.HistoricalLossRegistry.EntryV3 ->
        approval: FS.GG.Coord.HistoricalLossRegistry.NativeApprovalReadbackV2 ->
        Result<FS.GG.Coord.HistoricalLossRegistry.BoundLossV3, string list>

    /// Atomically create a private 0600 capture file. Raw bodies are stored only in that file;
    /// errors contain no response body. An existing path is never replaced.
    val savePrivate: path: string -> capture: Capture -> Result<unit, string>

    /// Load a private 0600 capture file and verify its exact raw hashes. The returned Draft values
    /// are deliberately empty placeholders; bindV3Captured replays and recomputes them from raw pages.
    val loadPrivate: path: string -> Result<Capture, string>
