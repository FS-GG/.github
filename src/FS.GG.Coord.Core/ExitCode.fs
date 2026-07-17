namespace FS.GG.Coord

module ExitCode =

    type ExitCode =
        | Green
        | Error
        | Defect
        | Red
        | NoVerdict
        | NoneStartable
        | Contended
        | Pending
        | Offboard
        | Partial
        | Rate

    let toInt (code: ExitCode) : int =
        match code with
        | Green -> 0
        | Error -> 1
        | Defect -> 2
        | Red -> 3
        | NoVerdict -> 4
        | NoneStartable -> 5
        | Contended -> 6
        | Pending -> 7
        | Offboard -> 8
        | Partial -> 9
        | Rate -> 75

    // Ordered as a worker meets them: the one success first, then the failures by how likely they are
    // to be the reason a loop stopped. This is the same ordering `Protocol.takeExitCodes` renders, and
    // the completeness test pins the two to the same SET.
    let takeCodes: ExitCode list =
        [ Green
          NoneStartable
          Contended
          Rate
          Red
          Error
          Defect ]

    // Ordered as a poll loop meets it: green, then the one retryable code, then the ways to stop.
    let landableCodes: ExitCode list =
        [ Green
          Pending
          Red
          NoVerdict
          Error
          Defect ]
