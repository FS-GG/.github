namespace FS.GG.Coord

module TelemetryStore =
    [<Literal>]
    val BatchSchema: string = "fsgg.telemetry.ingest/1"
    [<Literal>]
    val MaxBatchBytes: int = 65536
    [<Literal>]
    val MaxEventBytes: int = 65536
    [<Literal>]
    val MaxEvents: int = 64

    type Coverage =
        { RecordValidity: string
          JoinIntegrity: string
          PopulationCoverage: string
          Qualification: string
          Eligible: int64 option
          Observed: int64 option }

    type Payload =
        | Item of featureId: string option
        | Feature of name: string
        | Attempt of parentItemId: string * status: string
        | ParentChild of parentId: string * childId: string
        | PullRequestHead of repository: string * pullRequest: int64 * head: string
        | Source of sourceIdentity: string * generation: string * cursor: string
        | Usage of provider: string * model: string * effort: string * input: int64 * cachedInput: int64 * cacheWriteInput: int64 * output: int64 * reasoning: int64 option * total: int64 * responses: int64 * sessions: int64 * turns: int64
        | Delivery of state: string * expectedHead: string option * observedHead: string option * pullRequest: int64 option
        | Evidence of digest: string * availability: string
        | Coverage of Coverage
        | Diagnostic of code: string * severity: string
        | Correction of targetIdentity: string * reason: string

    type Fact =
        { Identity: string
          ItemId: string option
          Revision: int64
          Kind: string
          Payload: Payload
          Canonical: string
          ContentDigest: string }

    type Batch =
        { IngestId: string
          SourceIdentity: string
          Generation: string
          Cursor: string
          Facts: Fact list
          ContentDigest: string }

    type Aggregate =
        { ItemId: string
          FactCount: int64
          UsageObservations: int64
          DeliveryObservations: int64
          Input: int64
          CachedInput: int64
          CacheWriteInput: int64
          Output: int64
          Reasoning: int64 option
          Total: int64
          RecordValidity: string
          JoinIntegrity: string
          PopulationCoverage: string
          Qualification: string }

    val parseBatch: bytes: byte array -> Result<Batch, string list>
    val reduce: itemId: string -> facts: Fact list -> Result<Aggregate, string list>
    val publicJson: aggregate: Aggregate -> string

    type DurabilityAssessment =
        | ApprovedLocalDurable
        | Unsafe of reason: string
        | DurabilityUnverified of reason: string

    val validateStoreRoot: path: string -> assessment: DurabilityAssessment -> Result<string, string list>
