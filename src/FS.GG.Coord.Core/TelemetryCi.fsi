namespace FS.GG.Coord

module TelemetryCi =
    [<Literal>]
    val AssignmentSchema: string = "fsgg.telemetry.ci-assignment/1"

    type Assignment =
        { FeatureId: string
          ItemId: string
          AttemptId: string
          ParentAttemptId: string option
          ProducerStream: string }

    type Interval = { StartUtc: System.DateTimeOffset; EndUtc: System.DateTimeOffset }
    type Timing =
        { RunnerSeconds: int64 option
          WallSeconds: int64 option
          AdministrativeSeconds: int64 option
          CriticalPathSeconds: int64 option }

    val parseAssignment: bytes: byte array -> Result<Assignment, string list>
    val interval: startUtc: string option -> endUtc: string option -> Interval option
    val unionSeconds: intervals: Interval list -> int64 option
    val subtractSeconds: source: Interval list -> excluded: Interval list -> int64 option
