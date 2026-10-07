namespace FS.GG.Telemetry.Host

/// Pure semantic preparation; these inputs and output declarations confer no admission authority.
module internal NativeResponsesAssessment =
    type Input =
        { OutputText: string
          PacketBytes: byte array
          CanonicalRequestBytes: byte array
          RequestId: string
          AnalysisUsageRef: string
          ClaimedAt: string
          ObservedAt: string
          OperationId: string
          Producer: string }

    /// Validates exact retained evidence and all prose/reference/classification obligations,
    /// then emits a private partial assessment. Metrics and then-policy alternative witnesses
    /// are absent from this caller route, so references to them are refused rather than inferred.
    val prepare: input: Input -> Result<byte array, string list>
