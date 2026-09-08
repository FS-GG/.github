namespace FS.GG.Coord

module TelemetryRuntime =
    [<Literal>]
    val AssignmentSchema: string = "fsgg.telemetry.codex-assignment/1"

    type Assignment =
        { FeatureId: string
          ItemId: string
          AttemptId: string
          ParentAttemptId: string option
          ProducerStream: string }

    type TurnUsage =
        { ThreadId: string
          TurnId: string option
          TurnSequence: int64
          Provider: string option
          ObservedModel: string option
          ObservedEffort: string option
          Backend: string option
          Input: int64
          CachedInput: int64
          Output: int64
          Reasoning: int64 option
          Total: int64 }

    type Projection =
        | ThreadStarted of threadId: string
        | TurnStarted of threadId: string * turnId: string option * turnSequence: int64
        | TurnUsageCompleted of TurnUsage
        | Gap of code: string

    val parseAssignment: bytes: byte array -> Result<Assignment, string list>
    val projectLine: currentThreadId: string option -> turnSequence: int64 -> raw: string -> Projection option
