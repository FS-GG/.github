namespace FS.GG.Coord

/// Exact, pure metric primitives. Callers supply admitted canonical selections; this module
/// cannot admit records, classify prose, change attribution or establish completion eligibility.
module ProcessEfficiency =
    type Fraction = { Numerator: bigint; Denominator: bigint }
    type Purpose =
        | DirectProduct | UsefulAssurance | NecessaryCoordination
        | ProcessImprovement | AvoidableProcess | UnknownPurpose
    type ResourceKey = { Identity: string; Dimension: string; Provider: string; AccountingScope: string }
    type Share = { ItemId: string; Purpose: Purpose; Fraction: Fraction }
    type Resource = { Key: ResourceKey; Amount: bigint; Shares: Share list }
    type Allocation =
        { Total: bigint; Allocated: Fraction; Unallocated: Fraction
          ByItem: Map<string, Fraction>; ByPurpose: Map<Purpose, Fraction> }
    type SourceRevision =
        { Identity: string; Revision: int64; ContentDigest: string; EffectiveItemId: string }
    type TimeSummary =
        { ElapsedNanoseconds: bigint; ObservedSpanNanoseconds: bigint
          TouchUnionNanoseconds: int64 option; WaitUnionNanoseconds: int64 option
          TouchWaitOverlapNanoseconds: int64 option; FlowRatio: Fraction option }
    type Attempt = { Identity: string; Ordinal: int; InputDigest: string; Usage: bigint }
    type Operation = { Identity: string; PopulationComplete: bool; Attempts: Attempt list }
    type RetrySummary =
        { ObservedUsage: bigint; KnownOperations: int; RetriedOperations: int
          AdditionalUsage: bigint; SameInputUsage: bigint; ChangedInputUsage: bigint
          Incidence: Fraction option; Burden: Fraction option }
    type NativePopulation =
        { ExpectedInvocations: Set<string> option; TerminalInvocations: Set<string>
          UsageInvocations: Set<string>; NativeEligible: bool }
    type PopulationSummary =
        { SourceDeliveries: int; NativeCompletions: int; ObservedUsage: bigint
          WholeItemUsage: bigint option; AssessmentScope: string }

    type ItemState = Accepted | Abandoned | Open | Unsuccessful
    type FirstPassItem = { Identity: string; PopulationComplete: bool; State: ItemState; AcceptedAttempt: int option }
    type FirstPassSummary =
        { EligibleItems: int; FirstPassItems: int; Rate: Fraction option
          ExcludedItems: int; OpenItems: int; AbandonedItems: int }
    type Freshness =
        { SourceAgeSeconds: decimal option; IngestionAgeSeconds: decimal option
          PublicationAgeSeconds: decimal option; ProducerCoverage: Fraction option }

    /// Normalize an exact nonnegative fraction; invalid denominators fail closed.
    val fraction: numerator: bigint -> denominator: bigint -> Result<Fraction, string>
    /// Deduplicate native resource identities before allocation; over-allocation and replay
    /// conflicts fail closed. This calculation requires one dimension/provider/accounting scope.
    val allocate: Resource list -> Result<Allocation, string>
    /// Select greatest canonical source revisions; same-revision conflicts remain errors.
    /// The caller applies its cutoff and the supported correction's effective selection first.
    val selectCurrent: SourceRevision list -> Result<SourceRevision list, string>
    /// Clip witnessed touch/wait unions to a witnessed window; empty observations remain unknown.
    val timeSummary: window: TelemetryBudget.Interval -> touch: TelemetryBudget.Interval list -> wait: TelemetryBudget.Interval list -> Result<TimeSummary, string>
    /// Executed attempts have explicit ordinals; identical replay adds no usage or retry.
    /// Incomplete operations retain observed exposure but cannot establish full retry burden.
    val retries: Operation list -> Result<RetrySummary, string>
    /// A source outcome is independent of native eligibility and complete joined populations.
    val population: sourceOutcomeIds: Set<string> -> observedUsage: bigint -> NativePopulation -> Result<PopulationSummary, string>
    /// Zero accepted items preserves observed cost and has no applicable quotient.
    val costPerAccepted: observedCost: bigint -> acceptedItems: int -> Result<Fraction option, string>
    /// A content-sensitive metric source fingerprint includes effective attribution and calculation version.
    val sourceFingerprint: calculationVersion: string -> SourceRevision list -> Result<string, string>
    /// Complete terminal item populations form the denominator; open/incomplete exposure stays visible.
    val firstPass: FirstPassItem list -> Result<FirstPassSummary, string>
    /// Producer, actual receipt ingestion and publication ages remain independently witnessed.
    val freshness: cutoff: System.DateTimeOffset -> producerTime: System.DateTimeOffset option -> ingestionTime: System.DateTimeOffset option -> publicationTime: System.DateTimeOffset option -> expectedProducers: Set<string> option -> observedProducers: Set<string> -> Result<Freshness, string>
