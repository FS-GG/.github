namespace FS.GG.Coord

/// Pure, fail-closed cycle state for roadmap and workspace-board drivers.
module CycleLedger =
    type Unit = { Id: string; Dependencies: string list; Completed: bool; Evidence: string list }
    type Ledger = { SourceRevision: string; Units: Unit list }
    type Cycle = { Id: string; UnitId: string; Executor: string; Repository: string; BaseCommit: string }
    type ProviderReceipt =
        { Schema: string; Provider: string; WorkId: string; CycleId: string; SourceRevision: string
          CandidateHead: string; Verdict: string; Round: int; PlayerJourney: bool option; JourneyRequired: bool
          GeneratorId: string; GeneratorVersion: string; ArtifactDigest: string }
    type Evidence =
        { ImplementationHead: string; ReviewHead: string; FeedbackCycle: string; FeedbackActive: bool
          MergedPr: int option; MergeHead: string option; EvidencePaths: string list; Dispositions: string list }
    type UpdateReceipt = { CycleId: string; EvidenceDigest: string }
    type Action = | Resume of Cycle | Register of Cycle | Advance of Cycle | Update of Cycle | Escalate of Cycle | Complete

    val cycleId: unitId: string -> executor: string -> repository: string -> baseCommit: string -> string
    val inspect: Ledger -> Result<Unit list, string list>
    val register: Ledger -> executor: string -> repository: string -> baseCommit: string -> selectedUnit: string option -> parallelAuthorized: bool -> disjointTouchSets: bool -> live: Cycle list -> Result<Action, string list>
    val validateProvider: expectedWorkId: string -> expectedCycle: Cycle -> expectedSourceRevision: string -> expectedHead: string -> expectedProvider: string -> expectedSchema: string -> expectedGenerator: string -> ProviderReceipt -> Result<unit, string list>
    val advance: Ledger -> Cycle -> ProviderReceipt -> ProviderReceipt -> ProviderReceipt -> Evidence -> Result<Action, string list>
    val update: Ledger -> Cycle -> Evidence -> Result<Action, string list>
    val complete: Ledger -> accepted: Cycle list -> guardedUpdates: UpdateReceipt list -> rollupCycleIds: string list -> Result<Action, string list>
