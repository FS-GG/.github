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
        | RuntimeAdmission of invocationId: string * featureId: string * attemptId: string * parentAttemptId: string option * producerStream: string * requestedModel: string option * requestedEffort: string option * backend: string option
        | RuntimeStart of invocationId: string * threadId: string option * turnId: string option * turnSequence: int64 option * processId: int64 * phase: string
        | RuntimeTurnUsage of invocationId: string * threadId: string * turnId: string option * turnSequence: int64 * provider: string option * requestedModel: string option * observedModel: string option * requestedEffort: string option * observedEffort: string option * backend: string option * scope: string * provenance: string * input: int64 * cachedInput: int64 * output: int64 * reasoning: int64 option * total: int64
        | RuntimeTerminal of invocationId: string * threadId: string option * outcome: string * exitCode: int64
        | RuntimeGap of invocationId: string * code: string
        | CiBinding of collectionId: string * repository: string * head: string * pullRequest: int64 * workflow: string * featureId: string * attemptId: string * parentAttemptId: string option * producerStream: string * binding: string
        | CiPage of collectionId: string * resource: string * page: int64 * count: int64 * total: int64
        | CiRun of repository: string * runId: int64 * attempt: int64 * workflow: string * event: string * head: string * status: string * conclusion: string option * createdAt: string option * startedAt: string option * updatedAt: string option
        | CiJob of repository: string * runId: int64 * attempt: int64 * jobId: int64 * name: string * status: string * conclusion: string option * createdAt: string option * startedAt: string option * completedAt: string option
        | CiStep of repository: string * runId: int64 * attempt: int64 * jobId: int64 * number: int64 * name: string * status: string * conclusion: string option * startedAt: string option * completedAt: string option * classification: string * rationale: string
        | CiCoverage of collectionId: string * inventory: string * attempts: string * jobPages: string * terminal: string * timestamps: string * lineage: string * classification: string * criticalPath: string
        | CiPopulationAdmission of collectionId: string * repository: string * pullRequest: int64 * baseRef: string * baseSha: string * head: string * witness: string
        | CiCheck of repository: string * checkId: int64 * name: string * appSlug: string option * status: string * conclusion: string option * startedAt: string option * completedAt: string option
        | CiPopulationCoverage of collectionId: string * actions: string * checks: string * attempts: string * jobs: string * terminal: string * timestamps: string * continuation: string * externalChecks: int64 * gaps: string
        | NativeItemOutcome of repository: string * pullRequest: int64 * baseRef: string * baseSha: string * head: string * outcome: string * codeDelivery: string * mergeCommit: string option * occurredAt: string option * observedAt: string * sourceKind: string * sourceRef: string
        | BudgetPopulation of originalItemId: string * state: string * sourceKind: string * sourceRef: string
        | BudgetAttribution of dimension: string * provider: string * accountingScope: string * numerator: int64 option * denominator: int64 option * coverage: string * attribution: string * sourceKind: string * sourceRef: string
        | BudgetInterval of dimension: string * classification: string * startNanoseconds: int64 * endNanoseconds: int64 * witnessed: bool * sourceKind: string * sourceRef: string
        | BudgetIntervention of interventionId: string * transition: string * sequence: int64 * result: string * coverage: string * sourceRef: string
        | OperationalActivation of activationId: string * scope: string * runtime: string * activatedAt: string * clockProvenance: string * lateAfterSeconds: int64
        | ExpectedDispatch of dispatchId: string * activationId: string * relation: string * parentDispatchId: string option * runtime: string * expectedAt: string * clockProvenance: string
        | InvocationLineage of dispatchId: string * invocationId: string * relation: string * parentInvocationId: string option * rootInvocationId: string * runtime: string
        | EventTime of invocationId: string * event: string * occurredAt: string option * occurredClockProvenance: string option * observedAt: string option * observedClockProvenance: string option

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
          Admitted: int64
          Started: int64
          Terminal: int64
          RuntimeUsage: int64
          MissingAdmission: int64
          MissingStart: int64
          MissingTerminal: int64
          MissingUsage: int64
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
