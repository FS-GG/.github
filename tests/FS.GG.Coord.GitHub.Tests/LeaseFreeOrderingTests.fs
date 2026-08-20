module FS.GG.Coord.GitHub.Tests.LeaseFreeOrderingTests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.GitHub

// `Reads.lowestId` is the one exported lease-free ordering rule — "lowest id wins", expressed once, where
// callers can reach it (executor-fencing design §4.2, `.github#2312`).
//
// WHAT THESE LEGS ARE FOR, stated because the obvious reading is the wrong one. `lowestId` is three tokens
// of code, so a suite that only checked "it returns the smallest id" would be testing `List.sortBy`. The
// property that matters is not the arithmetic; it is that this function is the SUBJECT every consumer's
// answer flows through — so that changing the rule here changes `winner`, `reserver`, `who`, `reap` and
// `adopt` together, and cannot be changed in one of them alone. The compositional legs below are that
// claim, and `tests/coord-engine-mutation` plus `OpLockTests`' source-scan gate are the inversions that
// keep it honest.

let private at (id: int64) (worker: string) (ageSeconds: int) : Reads.Marker =
    { Reads.Id = id
      Reads.Worker = WorkerId worker
      Reads.Session = None
      Reads.AgeSeconds = ageSeconds
      Reads.PreviousStatus = None
      Reads.PathRepo = None
      Reads.AgentContract = None
      Reads.Raw = "" }

/// Live: well inside a 120-minute lease.
let private live (id: int64) (worker: string) = at id worker 60

/// Lapsed: far past a 120-minute lease.
let private lapsed (id: int64) (worker: string) = at id worker 99999

[<Fact>]
let ``lowestId returns the lowest comment id, and SORTS rather than trusting input order`` () =
    // It does not assume its input arrives ordered even though `markerScan` returns it that way. A rule that
    // depends on an invariant it does not enforce has a silent failure mode, and this one's is two racers
    // computing two different winners and both believing they hold the lock — the precise outcome the CAS
    // exists to make impossible. So the input here is deliberately SHUFFLED.
    let shuffled = [ live 903L "late"; live 901L "first"; live 902L "middle" ]

    match Reads.lowestId shuffled with
    | Some m -> Assert.Equal(WorkerId "first", m.Worker)
    | None -> failwith "a non-empty marker list has a lowest id"

    // ...and the answer does not depend on which order it was handed. Every permutation agrees, which is
    // what "every racer observes the same total order" means operationally.
    let permutations =
        [ [ live 901L "first"; live 902L "middle"; live 903L "late" ]
          [ live 903L "late"; live 902L "middle"; live 901L "first" ]
          [ live 902L "middle"; live 901L "first"; live 903L "late" ] ]

    for permutation in permutations do
        match Reads.lowestId permutation with
        | Some m -> Assert.Equal(WorkerId "first", m.Worker)
        | None -> failwith "a non-empty marker list has a lowest id"

[<Fact>]
let ``lowestId is LEASE-FREE - a wholly lapsed set still elects, which is the whole point`` () =
    // This is the difference from `winner`, and it is why the function exists separately. `reap` and
    // `adopt` are reached ONLY when no live winner exists; if the ordering rule filtered on liveness they
    // would be handed `None` on exactly the input they are for, and a stale lock would become unbreakable.
    let allLapsed = [ lapsed 903L "late"; lapsed 901L "first" ]

    Assert.Equal(None, Reads.winner 120 allLapsed)

    match Reads.lowestId allLapsed with
    | Some m -> Assert.Equal(WorkerId "first", m.Worker)
    | None -> failwith "lowestId applies no lease filter — a lapsed set still has a lowest id"

[<Fact>]
let ``lowestId on an empty list is None - an absence, never an invented answer`` () =
    Assert.Equal(None, Reads.lowestId [])

[<Fact>]
let ``winner IS lowestId composed with the staleness filter - one rule with one parameter`` () =
    // The relationship stated as an executable equation rather than as a comment. If someone re-implements
    // either side independently, this leg is what notices.
    let markers =
        [ lapsed 901L "dead"; live 902L "alive"; live 903L "also-alive"; lapsed 904L "also-dead" ]

    let composed = markers |> List.filter (Reads.isStale 120 >> not) |> Reads.lowestId

    Assert.Equal(Reads.winner 120 markers, composed)

    match Reads.winner 120 markers with
    | Some m -> Assert.Equal(WorkerId "alive", m.Worker)
    | None -> failwith "a live marker must win"

[<Fact>]
let ``reserver falls back to lowestId - and returns the LIVE winner when there is one`` () =
    // §4.2's second constraint, as a test: `reserver` is NOT a drop-in for `lowestId`. When a live winner
    // exists it returns THAT, so a caller wanting "lowest id, lease irrespective" must not reach for it.
    // `reap` and `adopt` are the sites where getting this backwards breaks a lock somebody is standing in.
    let mixed = [ lapsed 901L "dead"; live 902L "alive" ]

    match Reads.reserver 120 mixed with
    | Some m -> Assert.Equal(WorkerId "alive", m.Worker)
    | None -> failwith "a live winner reserves"

    // ...and they DISAGREE on this input, which is exactly why substituting one for the other is a defect
    // rather than a refactor.
    match Reads.lowestId mixed with
    | Some m -> Assert.Equal(WorkerId "dead", m.Worker)
    | None -> failwith "lowestId ignores the lease"

    // With no live winner, `reserver` IS `lowestId`.
    let allLapsed = [ lapsed 903L "late"; lapsed 901L "first" ]
    Assert.Equal(Reads.reserver 120 allLapsed, Reads.lowestId allLapsed)

[<Fact>]
let ``a single-element list elects that element whatever its lease says`` () =
    // The degenerate case, pinned because the election (§4.2) reads it constantly: the FIRST executor to
    // post an election marker is the only candidate, and it must elect itself rather than return `None`
    // until a second one arrives.
    Assert.Equal(Some(live 5L "only"), Reads.lowestId [ live 5L "only" ])
    Assert.Equal(Some(lapsed 5L "only"), Reads.lowestId [ lapsed 5L "only" ])
