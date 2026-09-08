namespace FS.GG.Coord

module TelemetryBudget =
    type Usability =
        | Unknown of reason: string
        | NotApplicable of reason: string
        | Usable of numerator: int64 * denominator: int64

    type Verdict =
        | UnknownVerdict of reason: string
        | NotApplicableVerdict of reason: string
        | Pass of numerator: int64 * denominator: int64
        | Breach of numerator: int64 * denominator: int64 * severe: bool

    type Interval =
        { StartNanoseconds: int64
          EndNanoseconds: int64 }

    val assess: Usability -> Verdict
    val unionNanoseconds: intervals: Interval list -> int64 option
    val subtractNanoseconds: source: Interval list -> excluded: Interval list -> int64 option
    val interventionDue: distinctBreachingItems: int -> hasSevereBreach: bool -> bool
