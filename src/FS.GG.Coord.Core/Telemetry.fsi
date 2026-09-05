namespace FS.GG.Coord

open System

module CanonicalJson =
    val sha256: bytes: byte array -> string
    val canonicalize: bytes: byte array -> Result<string, string>

module RuntimeUsage =
    type TokenCounts =
        { Input: int64
          CachedInput: int64
          CacheWriteInput: int64
          Output: int64
          Reasoning: int64 option
          Total: int64 }

    type UsageRow =
        { Timestamp: string
          Task: string
          SessionId: string
          ThreadId: string
          TurnId: string
          ResponseId: string
          Provider: string
          Model: string
          Effort: string
          RuntimeVersion: string
          CoordinationVersion: string
          SddVersion: string
          ContractsVersion: string
          LedgerSchema: int
          Response: TokenCounts
          Turn: TokenCounts
          Thread: TokenCounts option
          Source: string }

    type Collection =
        { Rows: UsageRow list
          SourceDigest: string }

    val collectCodex: task: string -> turnId: string option -> allResponses: bool -> sinceUtc: string option -> untilUtc: string option -> coordinationVersion: string -> sddVersion: string -> contractsVersion: string -> bytes: byte array -> Result<Collection, string list>
    val collectClaude: task: string -> coordinationVersion: string -> sddVersion: string -> contractsVersion: string -> bytes: byte array -> Result<Collection, string list>
    val renderCsv: UsageRow list -> string
    val renderJsonLines: UsageRow list -> string
    val parseJsonLines: string -> Result<UsageRow list, string list>
    val parseCsvReceipt: byte array -> Result<string * UsageRow list, string list>

module LifecycleTelemetry =
    type Transition = Started | Completed | Blocked | Resumed
    type Finding =
        | InvalidEvent of line: int * reason: string
        | InvalidTransition of phase: string * reason: string
        | EditedAuthorityComment of commentId: int64
        | RejectedFork of winningCommentId: int64 * rejectedCommentId: int64

    type Validation =
        { EventCount: int
          CompletedPhases: string list
          ActivePhases: string list
          BlockedPhases: string list }

    type HistoryRow =
        { Phase: string
          ToolingFingerprint: string
          ActualMinutes: int
          Source: string }

    val sealSuccessor: runId: string -> unitId: string -> existingJsonLines: string -> draftJson: string -> Result<string, Finding list>
    val sealSuccessorWithEvidence: runId: string -> unitId: string -> usageReports: (string * RuntimeUsage.UsageRow list) list -> history: HistoryRow list -> existingJsonLines: string -> draftJson: string -> Result<string, Finding list>
    val validate: runId: string -> unitId: string -> requireTerminal: bool -> requiredPhases: string list -> jsonLines: string -> Result<Validation, Finding list>
    val validateWithEvidence: runId: string -> unitId: string -> requireTerminal: bool -> requiredPhases: string list -> usageReports: (string * RuntimeUsage.UsageRow list) list -> history: HistoryRow list -> jsonLines: string -> Result<Validation, Finding list>
    val validateReconciledWithEvidence: runId: string -> unitId: string -> requireTerminal: bool -> requiredPhases: string list -> usageReports: (string * RuntimeUsage.UsageRow list) list -> history: HistoryRow list -> jsonLines: string -> Result<Validation, Finding list>
    val parseHistoryCsv: string -> Result<HistoryRow list, string list>
    val exportComments: runId: string -> unitId: string -> commentsJson: string -> Result<string * Finding list, Finding list>

module TelemetrySummary =
    type Summary =
        { Responses: int
          Sessions: int
          Turns: int
          Input: int64
          CachedInput: int64
          CacheWriteInput: int64
          FreshInput: int64
          Output: int64
          Reasoning: int64 option
          Total: int64 }
    val summarize: RuntimeUsage.UsageRow list -> Summary

module CritiqueReceipt =
    type Receipt =
        { CycleId: string
          ReviewedCommit: string
          RepairRounds: int
          GameFunctionality: bool
          PlayerJourneyPassed: bool
          ArtifactDigest: string }
    val validate: expectedCycle: string -> expectedHead: string option -> bytes: byte array -> Result<Receipt, string list>

module FeedbackReceipt =
    type Receipt =
        { CycleId: string
          Phases: string list
          MaterialEvents: int
          ReportDigest: string }
    val validate: expectedCycle: string -> expectedPhases: string list -> reportPath: string -> reportBytes: byte array -> auditBytes: byte array -> checkpointJsonLines: string option -> Result<Receipt, string list>

module RoadmapClosure =
    type Check = { Name: string; Required: bool; Passed: bool; Owner: string option }
    type Evidence =
        { UnitId: string
          Title: string
          RoadmapSourceDigest: string
          AcceptedReceiptDigest: string
          CandidateHead: string
          ImplementationMergeHead: string
          AcceptanceMergeHead: string
          ReviewHead: string
          FeedbackHead: string
          CycleId: string
          CycleUpdateDigest: string
          CritiqueVerdict: string
          RepairRounds: int
          IssueUrl: string
          PullRequestUrl: string
          ClaimsRemaining: int
          Checks: Check list }
    type ExternalObligation = { Check: string; Owner: string; Reason: string }
    type Closed = { Evidence: Evidence; ExternalObligations: ExternalObligation list }
    type Inputs =
        { UnitId: string
          Title: string
          RoadmapSourceDigest: string
          AcceptedReceipt: byte array
          DeliveryReceipt: byte array
          Critique: byte array
          FeedbackReportPath: string
          FeedbackReport: byte array
          FeedbackAudit: byte array
          FeedbackPhases: string list
          FeedbackCheckpoint: string option
          FeedbackBinding: byte array
          CycleUpdate: byte array
          CheckReceipts: byte array list }
    val inspect: Inputs -> Result<Closed, string list>

module RoadmapProjection =
    val renderBlock: RoadmapClosure.Closed -> string
    val render: expectedSourceDigest: string -> roadmapBytes: byte array -> RoadmapClosure.Closed -> Result<string, string list>
    val verify: expectedSourceDigest: string -> sourceRoadmapBytes: byte array -> candidateRoadmapBytes: byte array -> RoadmapClosure.Closed -> Result<unit, string list>
