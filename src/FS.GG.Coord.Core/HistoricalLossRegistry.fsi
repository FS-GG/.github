namespace FS.GG.Coord

/// A fail-closed source contract for independently approved historical provenance loss.
/// It validates evidence only; it cannot author a disposition or grant canonical authority.
module HistoricalLossRegistry =
    [<Literal>]
    val Schema: string = "fsgg.coord.historical-loss-registry/v1"

    [<Literal>]
    val SchemaV2: string = "fsgg.coord.historical-loss-registry/v2"

    [<Literal>]
    val SchemaV3: string = "fsgg.coord.historical-loss-registry/v3"

    [<Literal>]
    val ApprovalEnvelopeSchemaV2: string = "fsgg.coord.historical-loss-approval/v2"

    [<Literal>]
    val RequiredConsequence: string = "exclude-unverifiable-history-and-block-positive-provenance"

    type SourceRole =
        | ProtectedParserOnly
        | LocalCacheOnly

    type AuditedSource =
        {
            ProducerId: string
            Revision: string
            Path: string
            BlobSha: string
            BytesSha256: string
            Role: SourceRole
        }

    type NativeSurvivor =
        {
            NativeId: string
            Family: string
            PayloadBlobSha: string
            SessionOperationId: string option
        }

    type NativeCensus =
        {
            Scope: string
            ObservedThrough: string
            Revision: string
            Complete: bool
            Terminal: bool
            DeclaredCount: int
            RawBlobShas: string list
            Survivors: NativeSurvivor list
            Digest: string
        }

    type ApprovalBinding =
        {
            Subject: string
            PullRequest: int
            BaseSha: string
            HeadSha: string
            MergeCommitSha: string
            RegistryPath: string
        }

    type Entry =
        {
            Family: string
            Scope: string
            Cutoff: string
            AuditedSources: AuditedSource list
            CensusFirst: NativeCensus
            CensusSecond: NativeCensus
            Consequence: string
            Approval: ApprovalBinding
        }

    type Registry = { Schema: string; Entries: Entry list }

    /// Stable facts known before the content-bearing merge. Head, merge and
    /// content identities are deliberately supplied by the native v2 envelope.
    type ApprovalBindingV2 =
        {
            Subject: string
            PullRequest: int
            BaseSha: string
            RegistryPath: string
        }

    type EntryV2 =
        {
            Family: string
            Scope: string
            Cutoff: string
            AuditedSources: AuditedSource list
            CensusFirst: NativeCensus
            CensusSecond: NativeCensus
            Consequence: string
            Approval: ApprovalBindingV2
        }

    type RegistryV2 = { Schema: string; Entries: EntryV2 list }

    type RecoverySourceRole =
        | RecoveredWriterSource
        | ProtocolAuthoringSource

    type RecoverySource =
        {
            ProducerId: string
            Revision: string
            Path: string
            BlobSha: string
            BytesSha256: string
            Role: RecoverySourceRole
        }

    type RetainedEnumerationKind =
        | DirectRepositoryEnumeration
        | SearchOnly
        | AuditNotFoundInference

    type RepositoryIdentityV3 =
        {
            FullName: string
            DatabaseId: int64
            NodeId: string
        }

    type RetainedSubjectV3 =
        {
            Repository: RepositoryIdentityV3
            NativeId: string
            Family: string
            CreatedAt: string
            PayloadBlobSha: string
            SessionOperationId: string option
            LiveClaim: bool
        }

    type RetainedPageV3 =
        {
            Repository: RepositoryIdentityV3
            Index: int
            ItemCount: int
            RawSha256: string
            Terminal: bool
        }

    /// A census of subjects retained now. The four historical fields must remain
    /// the literal `unknown`; this shape cannot turn a present-day inventory into history.
    type RetainedNativeCensusV3 =
        {
            SelectedRepositories: RepositoryIdentityV3 list
            ObservationHorizon: string
            Revision: string
            Enumeration: RetainedEnumerationKind
            Complete: bool
            DeclaredCount: int
            Pages: RetainedPageV3 list
            Subjects: RetainedSubjectV3 list
            HistoricalEmissions: string
            HistoricalDeletions: string
            LostCount: string
            ProducerDeploymentEnd: string
            Digest: string
        }

    type EntryV3 =
        {
            Family: string
            Scope: string
            ObservationHorizon: string
            RecoverySources: RecoverySource list
            KnownSurvivorIds: string list
            CensusFirst: RetainedNativeCensusV3
            CensusSecond: RetainedNativeCensusV3
            ExclusionAppliesToLiveClaims: bool
            Consequence: string
            Approval: ApprovalBindingV2
        }

    type RegistryV3 = { Schema: string; Entries: EntryV3 list }

    type NativePullRequest =
        {
            Repository: string
            PullRequest: int
            State: string
            Merged: bool
            BaseSha: string
            HeadSha: string
            MergeCommitSha: string
        }

    type NativeReviewComment =
        {
            DatabaseId: int64
            NodeId: string
            Url: string
            Body: string
            BodySha256: string
        }

    type NativeFileReadback =
        {
            Repository: string
            Path: string
            Revision: string
            BlobSha: string
            Bytes: byte array
            BytesSha256: string
        }

    type NativeBlobReadback =
        {
            Repository: string
            BlobSha: string
            Bytes: byte array
            BytesSha256: string
        }

    type NativeApprovalReadback =
        {
            PullRequestFirst: NativePullRequest
            PullRequestSecond: NativePullRequest
            ReviewCommentsFirst: NativeReviewComment list
            ReviewCommentsSecond: NativeReviewComment list
            ReviewCommentsComplete: bool
            ReviewCommentsTerminal: bool
            File: NativeFileReadback
            Blob: NativeBlobReadback
        }

    type NativeMergedPullRequestV2 =
        {
            PullRequest: NativePullRequest
            MergedAt: string
        }

    type NativeApprovalEnvelopeCommentV2 =
        {
            DatabaseId: int64
            NodeId: string
            Url: string
            CreatedAt: string
            Body: string
            BodySha256: string
        }

    type NativeApprovalReadbackV2 =
        {
            PullRequestFirst: NativeMergedPullRequestV2
            PullRequestSecond: NativeMergedPullRequestV2
            ReviewCommentsFirst: NativeReviewComment list
            ReviewCommentsSecond: NativeReviewComment list
            ReviewCommentsComplete: bool
            ReviewCommentsTerminal: bool
            ApprovalEnvelopeFirst: NativeApprovalEnvelopeCommentV2
            ApprovalEnvelopeSecond: NativeApprovalEnvelopeCommentV2
            File: NativeFileReadback
            Blob: NativeBlobReadback
        }

    type BoundLoss =
        {
            Family: string
            Scope: string
            Cutoff: string
            CensusDigest: string
            ApprovalDigest: string
            Consequence: string
        }

    type BoundLossV3 =
        {
            Family: string
            Scope: string
            ObservationHorizon: string
            SelectedRepositories: RepositoryIdentityV3 list
            RetainedCount: int
            CensusDigest: string
            ApprovalDigest: string
            Consequence: string
        }

    val parse: raw: string -> Result<Registry, string list>
    val parseV2: raw: string -> Result<RegistryV2, string list>
    val parseV3: raw: string -> Result<RegistryV3, string list>
    val censusDigest: census: NativeCensus -> string
    val retainedCensusDigestV3: census: RetainedNativeCensusV3 -> string

    /// Binds one exact entry to an independently supplied, two-pass native readback.
    /// Unknown families, sessionless claims, incomplete/drifting evidence, historical review
    /// shapes without claim/base bindings, and any mismatch refuse.
    val bind:
        expectedFamily: string ->
        expectedScope: string ->
        expectedCutoff: string ->
        registryBytes: byte array ->
        entry: Entry ->
        native: NativeApprovalReadback ->
            Result<BoundLoss, string list>

    /// Binds v2 registry bytes to a separately captured native envelope posted
    /// after the exact content-bearing merge. The registry itself contains no
    /// reviewed-head, merge-commit, blob or content digest self-reference.
    val bindV2:
        expectedFamily: string ->
        expectedScope: string ->
        expectedCutoff: string ->
        registryBytes: byte array ->
        entry: EntryV2 ->
        native: NativeApprovalReadbackV2 ->
            Result<BoundLoss, string list>

    /// Binds a v3 present-day retained-subject census to the existing detached v2 approval.
    /// Search results, inferred absence, historical estimates, incomplete pages, live claims,
    /// and any attempt to treat the observation horizon as producer history refuse.
    val bindV3:
        expectedFamily: string ->
        expectedScope: string ->
        expectedObservationHorizon: string ->
        expectedRepositories: RepositoryIdentityV3 list ->
        registryBytes: byte array ->
        entry: EntryV3 ->
        native: NativeApprovalReadbackV2 ->
            Result<BoundLossV3, string list>
