namespace FS.GG.Coord.GitHub

open FS.GG.Coord

/// GET-only capture of the native evidence consumed by HistoricalLossRegistry.bindV2.
module HistoricalLossApprovalRead =
    type RawResponse =
        {
            Resource: string
            Path: string
            Query: (string * string) list
            Status: int
            Body: string
            BodySha256: string
            NextLink: string option
        }

    type Capture =
        {
            BoundLoss: HistoricalLossRegistry.BoundLoss
            Readback: HistoricalLossRegistry.NativeApprovalReadbackV2
            FirstPass: RawResponse list
            SecondPass: RawResponse list
            Fingerprint: string
        }

    /// Raw provider evidence paired with a typed retained-subject census. Endpoint-specific
    /// enumeration remains a separate source; this type cannot manufacture terminal pages.
    type V3CensusCapture =
        {
            First: HistoricalLossRegistry.RetainedNativeCensusV3
            Second: HistoricalLossRegistry.RetainedNativeCensusV3
            FirstPass: RawResponse list
            SecondPass: RawResponse list
        }

    /// Checks structural raw/typed stability for a direct v3 census, then refuses because
    /// no production raw-to-typed decoder and exact endpoint/roster proof exists yet.
    val validateV3CensusCapture:
        expectedRepositories: HistoricalLossRegistry.RepositoryIdentityV3 list ->
        capture: V3CensusCapture ->
            Errors.IoResult<HistoricalLossRegistry.RetainedNativeCensusV3>

    /// Reads the merged pull request, its complete issue-comment population, and the exact
    /// registry contents and Git blob at the merge commit twice. It returns authority only
    /// after both raw and typed passes agree and the pure v2 binder accepts them.
    val collectAndBind:
        transport: Transport.ISinglePageGitHubTransport ->
        apiBase: string ->
        owner: string ->
        repositoryName: string ->
        expectedFamily: string ->
        expectedScope: string ->
        expectedCutoff: string ->
        registryBytes: byte array ->
        entry: HistoricalLossRegistry.EntryV2 ->
            Errors.IoResult<Capture>
