namespace FS.GG.Coord.Cli.Tests

open Xunit
open FS.GG.Coord.Types
open FS.GG.Coord.GitHub
open FS.GG.Coord.Cli

/// #2378: a worker paused at the review handoff cannot heartbeat between turns (it is not running), so a
/// review cycle longer than the lease silently expires its claim — and `review`/`delivery` then refuse
/// with "no live claim marker can authorize …", even though nothing has actually been abandoned: the
/// item's own `item/<n>-*` PR is still open, exactly the proof of life `Schedulability.HeldByLiveWork`
/// already trusts for the scheduling question (#581).
///
/// `Client.authorizedMarker` is the shared repair both `review` and `delivery` route through. It is
/// tested directly, against a stub `liveness` THUNK, rather than through a live-transport fixture: the
/// function is deliberately transport-agnostic (the thunk is the one seam), so driving it here exercises
/// the exact decision `review`/`delivery` make without restating GitHub's `pulls`/`matching-refs`
/// response shapes a second time — those already have their own coverage in `ReadTests.fs`.
module AuthorizedMarkerTests =

    let private marker (id: int64) (worker: string) (ageSeconds: int) : Reads.Marker =
        { Id = id
          Worker = WorkerId worker
          Session = None
          AgeSeconds = ageSeconds
          PreviousStatus = None
          PathRepo = None
          Raw = $"<!-- fsgg:claim worker=%s{worker} lease=120 -->" }

    /// A lease of 120 minutes throughout — matches the production default and #2378's own incident
    /// timeline (a 120-minute lease, a ~2h review cycle).
    let private leaseMinutes = 120

    /// A thunk that FAILS THE TEST if it is ever invoked — proves the "not an eager read" claim in
    /// `authorizedMarker`'s doc comment: when `Reads.winner` already finds a live marker, the PR probe
    /// must never be paid for.
    let private mustNotBeCalled () : Errors.IoResult<Liveness> =
        failwith "authorizedMarker must not consult liveness when a live marker already answers the question"

    [<Fact>]
    let ``a live marker authorizes without consulting liveness at all`` () =
        let m = marker 1L "heron-b71" 0
        match Client.authorizedMarker leaseMinutes [ m ] mustNotBeCalled with
        | Ok(Some found) -> Assert.Equal(m, found)
        | other -> failwith $"a live marker must authorize on its own; got %A{other}"

    [<Fact>]
    let ``#2378 an expired lease backed by an OPEN item PR still authorizes — the worker is paused, not gone`` () =
        // 8000 seconds > 120-minute (7200s) lease: genuinely expired.
        let stale = marker 1L "smew-1ae8" 8000
        let liveness () = Ok(LeaseExpiredPrOpen 2370)

        match Client.authorizedMarker leaseMinutes [ stale ] liveness with
        | Ok(Some found) -> Assert.Equal(stale, found)
        | other -> failwith $"an expired lease with an open item PR must still authorize; got %A{other}"

    [<Fact>]
    let ``#2378 negative control — an expired lease with NO open PR does not authorize`` () =
        // Without this, the assertion above is satisfied by a function that always authorizes.
        let stale = marker 1L "ghost-000" 8000
        let liveness () = Ok LeaseExpiredNoPr

        match Client.authorizedMarker leaseMinutes [ stale ] liveness with
        | Ok None -> ()
        | other -> failwith $"an expired lease with no open PR must not authorize; got %A{other}"

    [<Fact>]
    let ``#2378 an expired lease with only a pushed branch does not authorize — weaker evidence than a PR`` () =
        let stale = marker 1L "ghost-000" 8000
        let liveness () = Ok LeaseExpiredBranchPushed

        match Client.authorizedMarker leaseMinutes [ stale ] liveness with
        | Ok None -> ()
        | other -> failwith $"a pushed-branch-only claim must not authorize; got %A{other}"

    [<Fact>]
    let ``#2378 a failed liveness probe propagates as an error, never a silent refusal`` () =
        let stale = marker 1L "ghost-000" 8000
        let boom = Errors.Malformed("FS-GG/.github#2378", "fixture: the PR probe failed")
        let liveness () = Error boom

        match Client.authorizedMarker leaseMinutes [ stale ] (fun () -> liveness ()) with
        | Error e -> Assert.Equal(boom, e)
        | other -> failwith $"a failed liveness read must propagate as an Error, not a verdict; got %A{other}"

    [<Fact>]
    let ``no markers at all authorizes nobody, and never asks about liveness`` () =
        match Client.authorizedMarker leaseMinutes [] mustNotBeCalled with
        | Ok None -> ()
        | other -> failwith $"an empty marker list must authorize nobody; got %A{other}"
