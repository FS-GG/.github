namespace FS.GG.Coord.Cli

open FS.GG.Coord

module TelemetryStoreApplication =
    type DrainHooks =
        { BeforeCommit: unit -> unit
          AfterCommitBeforeDelete: unit -> unit }
    type DashboardSnapshotHooks = { AfterFirstRead: unit -> unit }
    val databaseFileName: string
    val assessProductionRoot: path: string -> TelemetryStore.DurabilityAssessment
    val initialize: path: string -> assessment: TelemetryStore.DurabilityAssessment -> Result<string, string list>
    val status: path: string -> assessment: TelemetryStore.DurabilityAssessment -> Result<string, string list>
    val publish: path: string -> assessment: TelemetryStore.DurabilityAssessment -> bytes: byte array -> Result<string, string list>
    val drain: path: string -> assessment: TelemetryStore.DurabilityAssessment -> Result<string, string list>
    val drainWithHooks: path: string -> assessment: TelemetryStore.DurabilityAssessment -> hooks: DrainHooks -> Result<string, string list>
    val ingest: path: string -> assessment: TelemetryStore.DurabilityAssessment -> bytes: byte array -> Result<string, string list>
    val summary: path: string -> assessment: TelemetryStore.DurabilityAssessment -> itemId: string -> Result<string, string list>
    val reconcile: path: string -> assessment: TelemetryStore.DurabilityAssessment -> itemId: string -> Result<string, string list>
    val ciSummary: path: string -> assessment: TelemetryStore.DurabilityAssessment -> itemId: string -> Result<string, string list>
    val ciPopulationAdmissionExists: path: string -> assessment: TelemetryStore.DurabilityAssessment -> itemId: string -> repository: string -> pullRequest: int -> baseRef: string -> baseSha: string -> head: string -> Result<bool, string list>
    val budgetSummary: path: string -> assessment: TelemetryStore.DurabilityAssessment -> itemId: string -> Result<string, string list>
    val budgetStatus: path: string -> assessment: TelemetryStore.DurabilityAssessment -> Result<string, string list>
    val budgetHealth: path: string -> assessment: TelemetryStore.DurabilityAssessment -> itemId: string -> Result<string, string list>
    val reviewSummary: path: string -> assessment: TelemetryStore.DurabilityAssessment -> itemId: string -> Result<string, string list>
    val itemDetail: path: string -> assessment: TelemetryStore.DurabilityAssessment -> itemId: string -> Result<string, string list>
    val dashboardSnapshot: path: string -> assessment: TelemetryStore.DurabilityAssessment -> itemId: string option -> Result<string, string list>
    val dashboardSnapshotWithHooks: path: string -> assessment: TelemetryStore.DurabilityAssessment -> hooks: DashboardSnapshotHooks -> itemId: string option -> Result<string, string list>
    val exportPublic: path: string -> assessment: TelemetryStore.DurabilityAssessment -> itemId: string option -> outputPath: string -> Result<string, string list>
    val run: action: string -> args: string list -> int
    val runBudget: action: string -> args: string list -> int
    val runReview: action: string -> args: string list -> int
    val runItemDetail: args: string list -> int
