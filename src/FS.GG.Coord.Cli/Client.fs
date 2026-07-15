namespace FS.GG.Coord.Cli

/// THE CLIENT COMMAND SURFACE — the bash client's commands, re-expressed over the typed IO layer.
///
/// This is what the ADR-0034 §4.4 shim execs in place of `scripts/fsgg-coord`. Each command composes the
/// already-built, already-tested pieces — `Scan` (the board read), `Batch`/`Schedulability` (the pure
/// decision), `Writes` (the claim CAS and the capability-typed writes), `Board` (the field writes), `Done`
/// (the done-stamp) — into a CLI verb with the fail-closed exit contract the recipes and the corpus depend
/// on.
///
/// THE ONE RULE THIS FILE ADDS TO THE ONES BELOW IT: every command that touches a lock takes a WORKER (via
/// `Identity`), because the lock is keyed on the worker, not the account (ADR-0027). And every command
/// fails CLOSED — a read it could not make is never an empty answer, and an exhausted budget is EX_RATE
/// (75), the back-off signal, never a generic error.
module Client =

    open System
    open FS.GG.Coord
    open FS.GG.Coord.Types
    open FS.GG.Coord.GitHub
    open FS.GG.Coord.GitHub.Transport
    open FS.GG.Coord.Cli.Options

    [<Literal>]
    let ExitGreen = 0

    [<Literal>]
    let ExitError = 1

    [<Literal>]
    let ExitRed = 3

    [<Literal>]
    let ExitNoVerdict = 4

    // #585 — `take`'s exit code must tell "I claimed you an item" (0) apart from the ways it can claim
    // NOTHING, so a worker loop (`take && work_it`) never proceeds on nothing. EX_NONE (5) is "looked,
    // nothing startable" (empty or all-blocked); EX_CONTENDED (6) is "lost every race — the board is
    // contended, back off and retry"; a read failure keeps its own non-zero (`fail`, never EX_NONE, so
    // "could not look" ≠ "empty" — #266); EX_RATE (75) is the budget. The values dodge the engine's
    // reserved codes (1 error, 2 defect, 3 red, 4 no-verdict). This reverses #480's "the empty queue
    // exits cleanly (0)", by decision on #585.
    [<Literal>]
    let ExitNone = 5

    [<Literal>]
    let ExitContended = 6

    /// Board status → its name, qualified against BoardStatus (bare `Ready` would resolve to the
    /// `Command.Ready` opened below). One place, so every render agrees.
    let private statusName (s: BoardStatus) =
        match s with
        | BoardStatus.NoStatus -> ""
        | BoardStatus.Backlog -> "Backlog"
        | BoardStatus.Ready -> "Ready"
        | BoardStatus.InProgress -> "In progress"
        | BoardStatus.Blocked -> "Blocked"
        | BoardStatus.InReview -> "In review"
        | BoardStatus.Done -> "Done"

    let private eprint (s: string) = Console.Error.WriteLine(s: string)

    /// An IO failure → its exit code and a printed reason. `RateLimited` becomes EX_RATE (75), the back-off
    /// signal; a caller that saw a generic 1 would treat a temporary condition as permanent.
    let private fail (e: Errors.IoError) : int =
        eprint $"fsgg-coord-engine: %s{Errors.explain e}"
        Errors.exitCode e

    // ---- shared context --------------------------------------------------------------------------------

    let private env name fallback =
        match Environment.GetEnvironmentVariable(name: string) with
        | null
        | "" -> fallback
        | v -> v

    type Context =
        { Transport: IGitHubTransport
          Owner: string
          Title: string
          /// The board's default repo scope, for a bare `repo#n` ref and the candidate filter.
          DefaultRepo: string option }

    /// Parse a `<ref>` — a URL, `owner/repo#n`, or `repo#n` (owner defaulting to the board owner).
    let private parseRef (ctx: Context) (raw: string) : Result<Ref, string> =
        let url =
            Text.RegularExpressions.Regex.Match(raw, @"github\.com/([\w.-]+)/([\w.-]+)/issues/(\d+)")

        if url.Success then
            Ok
                { Owner = url.Groups.[1].Value
                  Repo = url.Groups.[2].Value
                  Number = int url.Groups.[3].Value }
        else
            let full = Text.RegularExpressions.Regex.Match(raw, @"^([\w.-]+)/([\w.-]+)#(\d+)$")

            if full.Success then
                Ok
                    { Owner = full.Groups.[1].Value
                      Repo = full.Groups.[2].Value
                      Number = int full.Groups.[3].Value }
            else
                let short = Text.RegularExpressions.Regex.Match(raw, @"^([\w.-]+)#(\d+)$")

                if short.Success then
                    Ok
                        { Owner = ctx.Owner
                          Repo = short.Groups.[1].Value
                          Number = int short.Groups.[2].Value }
                else
                    Error $"unrecognised issue ref '%s{raw}' (use a URL, owner/repo#n, or repo#n)."

    /// Resolve the worker, printing the shared-session warning to stderr but proceeding — the id is still
    /// this worker's in the common single-worker case; the warning is for the fan-out that needs to know.
    let private worker (opts: Options) : Result<Identity.Worker, int> =
        match Identity.resolve opts.Worker with
        | Error msg ->
            eprint $"fsgg-coord-engine: %s{msg}"
            Result.Error ExitError
        | Ok w ->
            match w.Provenance with
            | Identity.FromSharedSession(_, _, why) ->
                eprint $"fsgg-coord-engine: WARNING — worker id '%s{w.Id}' was derived from a session where %s{why}. Pass --worker to be certain."
            | _ -> ()

            Ok w

    let private oneArg (opts: Options) (what: string) : Result<string, int> =
        match opts.Args with
        | [ a ] -> Ok a
        | [] ->
            eprint $"fsgg-coord-engine: %s{what} required."
            Result.Error ExitError
        | _ ->
            eprint $"fsgg-coord-engine: %s{what} takes exactly one argument (got %d{List.length opts.Args})."
            Result.Error ExitError

    // ---- the read / schedule commands ------------------------------------------------------------------

    /// Scan the board and decide. The shared body of `next`/`batch`/`take` — one board read, one decision,
    /// so the three can never disagree about which items exist (#485).
    let private scanAndDecide (ctx: Context) (opts: Options) (intent: Cache.ReadIntent) =
        Board.bootstrap ctx.Transport ctx.Owner ctx.Title
        |> Result.bind (fun board -> Scan.board ctx.Transport intent ctx.Owner ctx.Title board.Number)
        |> Result.bind (fun rows ->
            Scan.snapshot ctx.Transport rows opts.Repo opts.AllowBacklog opts.Limit opts.LeaseMinutes
            |> Result.map (fun (doc, receipt) -> rows, doc, receipt))

    let private renderDecision (opts: Options) (doc: string) : Result<Batch.BatchResult, int> =
        match Snapshot.parse doc with
        | Error errors ->
            for e in errors do
                eprint $"fsgg-coord-engine: %s{e.Path}: %s{e.Message}"

            Result.Error ExitError
        | Ok request ->
            match
                Batch.schedule
                    request.AllowBacklog
                    request.Limit
                    request.InFlight
                    (request.Candidates |> List.map (fun c -> c.Item))
            with
            | Green result -> Ok result
            | Red reasons ->
                eprint "REFUSED — the batch cannot be scheduled:"

                for r in reasons do
                    eprint $"  %s{r}"

                Result.Error ExitRed
            | Verdict.NoVerdict reason ->
                eprint $"UNDETERMINED — %s{reason}"
                Result.Error ExitNoVerdict

    let private printChosen (leaseMinutes: int) (result: Batch.BatchResult) =
        if List.isEmpty result.Chosen then
            printfn "nothing schedulable right now."
        else
            for item in result.Chosen do
                printfn "  → %s" item.Ref.Short

        let passed =
            result.Decisions |> List.filter (fun d -> d.Result <> Schedulability.Startable)

        if not (List.isEmpty passed) then
            eprint "passed over:"

            for d in passed do
                eprint $"  %s{Batch.explainDecision leaseMinutes d}"

    let batch (ctx: Context) (opts: Options) : int =
        match scanAndDecide ctx opts Cache.Scheduling with
        | Error e -> fail e
        | Ok(_, doc, _) ->
            match renderDecision opts doc with
            | Error code -> code
            | Ok result ->
                match opts.Render with
                | Json ->
                    // THE MACHINE CONTRACT — the array of chosen ids `take` consumes, byte-identical to the
                    // bash client's `batch --json`: `["FS.GG.SDD#70","FS.GG.SDD#74"]`, short form, sorted as
                    // the scheduler chose them. This is the one output where byte-parity is not a nicety: a
                    // `take` that parses it must read the same array from either engine.
                    let ids =
                        result.Chosen
                        |> List.map (fun item -> "\"" + item.Ref.Short + "\"")
                        |> String.concat ","

                    printfn "[%s]" ids
                    // The skip reasons still go to stderr — a caller reads the array on stdout, the "why
                    // nothing / why less" on stderr, exactly as bash does.
                    let passed =
                        result.Decisions |> List.filter (fun d -> d.Result <> Schedulability.Startable)

                    for d in passed do
                        eprint $"  %s{Batch.explainDecision opts.LeaseMinutes d}"
                | Text ->
                    if not (List.isEmpty result.Chosen) then
                        printfn "schedulable in parallel (%d):" (List.length result.Chosen)

                    printChosen opts.LeaseMinutes result

                ExitGreen

    let next (ctx: Context) (opts: Options) : int =
        // `next` is `batch` capped at one. The cap is the ONLY difference — the decision is identical, so
        // they cannot disagree.
        let opts = { opts with Limit = Some 1 }

        match scanAndDecide ctx opts Cache.Scheduling with
        | Error e -> fail e
        | Ok(_, doc, _) ->
            match renderDecision opts doc with
            | Error code -> code
            | Ok result ->
                match result.Chosen with
                | item :: _ -> printfn "%s" item.Ref.Short
                | [] ->
                    printfn "no schedulable item — every candidate is blocked, claimed, overlapping, or undeclared"
                    printChosen opts.LeaseMinutes result

                ExitGreen

    let ready (ctx: Context) (opts: Options) : int =
        // A RECONCILER read — always fresh, never the cache. Its whole job is to say what is true right now.
        match Board.bootstrap ctx.Transport ctx.Owner ctx.Title with
        | Error e -> fail e
        | Ok board ->
            match Scan.board ctx.Transport Cache.Reconciling ctx.Owner ctx.Title board.Number with
            | Error e -> fail e
            | Ok rows ->
                let rows =
                    match opts.Repo with
                    | Some r -> rows |> List.filter (fun row -> String.Equals(row.Ref.Repo, r, StringComparison.OrdinalIgnoreCase))
                    | None -> rows

                for row in rows |> List.filter (fun r -> not r.IsPullRequest && r.State = Open) do
                    let status =
                        match statusName row.Status with
                        | "" -> "(no status)"
                        | s -> s

                    printfn "  %-14s %s  %s" status row.Ref.Short row.Title

                ExitGreen

    let who (ctx: Context) (opts: Options) : int =
        // A truth read — fresh, and it reads the LOCK, which is never cached.
        match Board.bootstrap ctx.Transport ctx.Owner ctx.Title with
        | Error e -> fail e
        | Ok board ->
            match Scan.board ctx.Transport Cache.Reconciling ctx.Owner ctx.Title board.Number with
            | Error e -> fail e
            | Ok rows ->
                let candidates =
                    rows
                    |> List.filter (fun r ->
                        not r.IsPullRequest
                        && (match opts.Repo with
                            | Some name -> String.Equals(r.Ref.Repo, name, StringComparison.OrdinalIgnoreCase)
                            | None -> true))

                let mutable anyHeld = false
                let mutable failure = None

                for row in candidates do
                    if failure.IsNone then
                        match Reads.markers ctx.Transport row.Ref.Owner row.Ref.Repo row.Ref.Number with
                        | Error e -> failure <- Some e
                        | Ok markers ->
                            match Reads.winner opts.LeaseMinutes markers with
                            | Some m ->
                                anyHeld <- true
                                let age = Schedulability.leaseWindow opts.LeaseMinutes m.AgeSeconds
                                printfn "  %s  held by %s  (%s)" row.Ref.Short m.Worker.Value age
                            | None -> ()

                match failure with
                | Some e -> fail e
                | None ->
                    if not anyHeld then
                        printfn "nothing is in flight."

                    ExitGreen

    let budget (ctx: Context) : int =
        match Reads.rateLimit ctx.Transport with
        | Error e -> fail e
        | Ok meter ->
            printfn "GraphQL budget: %d / %d remaining" meter.Remaining meter.Limit

            if meter.Remaining < Budget.WarnBelow then
                eprint $"fsgg-coord-engine: WARNING — only %d{meter.Remaining} GraphQL points remain (< %d{Budget.WarnBelow}); the fleet shares one 5,000/hr budget (#418)."

            ExitGreen

    // ---- the lock lifecycle ----------------------------------------------------------------------------

    /// #516 — at most ONE item per worker. The CAS is keyed on the ITEM, so it guarantees at most one
    /// worker per item; NOTHING guaranteed the converse, and the cost model assumes it. A second,
    /// unattended claim RESERVES A TOUCH-SET on files nobody is editing for the whole lease, and `batch`
    /// then refuses every item that overlaps it. This scans the TARGET repo's in-flight items for a live
    /// claim held by THIS worker on a DIFFERENT item, returning the ones they already hold.
    ///
    /// It rides the 90s scan cache (`Cache.Scheduling`), exactly as bash's guard rides `CACHED=1`: under
    /// `take` the board scan is already paid, and a bare `claim` rides the window like `next` — paying a
    /// fresh board scan per claim is the burn #418 exists to stop. A stale-by-90s set cannot cause a
    /// double-hold: it can only miss OUR OWN very recent second claim, and the item's own CAS still holds.
    /// A held item's markers are read fresh (`Reads.markers` — the lock is never cached), and `winner`
    /// applies the lease, so a lapsed claim of ours does not count.
    let private heldElsewhere (ctx: Context) (leaseMinutes: int) (workerId: string) (ref: Ref) =
        match Board.bootstrap ctx.Transport ctx.Owner ctx.Title with
        | Error e -> Error e
        | Ok board ->
            match Scan.board ctx.Transport Cache.Scheduling ctx.Owner ctx.Title board.Number with
            | Error e -> Error e
            | Ok rows ->
                let inFlight =
                    rows
                    |> List.filter (fun r ->
                        not r.IsPullRequest
                        && r.Status = InProgress
                        && r.Ref.Number <> ref.Number
                        && String.Equals(r.Ref.Repo, ref.Repo, StringComparison.OrdinalIgnoreCase)
                        && String.Equals(r.Ref.Owner, ref.Owner, StringComparison.OrdinalIgnoreCase))

                let rec scan acc rows =
                    match rows with
                    | [] -> Ok(List.rev acc)
                    | (row: Scan.Row) :: rest ->
                        match Reads.markers ctx.Transport row.Ref.Owner row.Ref.Repo row.Ref.Number with
                        | Error e -> Error e
                        | Ok markers ->
                            match Reads.winner leaseMinutes markers with
                            | Some m when m.Worker.Value = workerId -> scan (row.Ref.Short :: acc) rest
                            | _ -> scan acc rest

                scan [] inFlight

    let claim (ctx: Context) (opts: Options) : int =
        match oneArg opts "claim: an issue ref", worker opts with
        | Error c, _
        | _, Error c -> c
        | Ok arg, Ok w ->
            match parseRef ctx arg with
            | Error msg ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | Ok ref ->
                let session = w.Session |> Option.map SessionId

                // #516: refuse a SECOND live hold before the CAS. `--force` is the deliberate override — a
                // rule with no escape hatch gets worked around, not obeyed. Re-claiming the SAME item is not
                // caught (the scan excludes `ref` itself), so `take` retries stay idempotent.
                let heldCheck =
                    if opts.Force then Ok [] else heldElsewhere ctx opts.LeaseMinutes w.Id ref

                match heldCheck with
                | Error e -> fail e
                | Ok(_ :: _ as heldRefs) ->
                    let names = String.Join(", ", heldRefs)

                    eprint
                        $"fsgg-coord-engine: worker '%s{w.Id}' ALREADY HOLDS %s{names}. A claim reserves a touch-set, so a second one locks files nobody is editing for the rest of the lease (%d{opts.LeaseMinutes}m) — and `batch` will refuse every item that overlaps it (#516)."

                    eprint "  Finish or drop the item you hold:  fsgg-coord-engine done <issue> --flip   (or: release <issue>)"
                    eprint "  If you genuinely mean to hold two, say so:  fsgg-coord-engine claim <issue> --force"
                    ExitRed
                | Ok [] ->
                    // The claim records the column it OVERWRITES, so `release` can restore it (#481). Read
                    // the current board Status for this item first — from the fresh reconciler scan is
                    // overkill for one item, so we pass None and let the CAS record what the marker's own
                    // prev carries on a re-claim.
                    match Writes.claim ctx.Transport opts.LeaseMinutes (WorkerId w.Id) session ref None with
                    | Error e -> fail e
                    | Ok(Writes.Won held) ->
                        // Move the board column to In progress — the ONE board write, through the
                        // queue-aware path so an exhausted budget defers rather than drops (#510).
                        match Board.bootstrap ctx.Transport ctx.Owner ctx.Title with
                        | Error e ->
                            // The LOCK is held (the marker is posted); a board-write failure does not
                            // un-hold it. Report the claim, note the board.
                            printfn "claimed %s by worker %s" ref.Short w.Id
                            eprint $"fsgg-coord-engine: note — the lock is held, but the board column could not be moved: %s{Errors.explain e}"
                            ExitGreen
                        | Ok board ->
                            match
                                Board.boardWrite ctx.Transport board ref.Owner ref.Repo ref.Number "Status" (Board.Set "In progress") w.Id
                            with
                            | Ok _
                            | Error _ ->
                                ignore held
                                printfn "claimed %s by worker %s" ref.Short w.Id
                                ExitGreen
                    | Ok(Writes.Lost holder) ->
                        eprint $"fsgg-coord-engine: %s{ref.Short} is already held by %s{holder.Value}. Pick another, or wait for the lease."
                        ExitRed
                    | Ok(Writes.Undecided reason) ->
                        eprint $"fsgg-coord-engine: could not take %s{ref.Short}: %s{reason}. This is a LOSS, not a win — retry."
                        ExitRed
                    | Ok Writes.BlockedByUnparseableMarker ->
                        eprint $"fsgg-coord-engine: %s{ref.Short} carries a marker held by nobody (an unparseable lock). It blocks until reaped."
                        ExitRed

    let release (ctx: Context) (opts: Options) : int =
        match oneArg opts "release: an issue ref", worker opts with
        | Error c, _
        | _, Error c -> c
        | Ok arg, Ok w ->
            match parseRef ctx arg with
            | Error msg ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | Ok ref ->
                match Writes.verifyHeld ctx.Transport opts.LeaseMinutes (WorkerId w.Id) ref with
                | Error e -> fail e
                | Ok None ->
                    eprint $"fsgg-coord-engine: %s{w.Id} does not hold %s{ref.Short} — nothing to release."
                    ExitError
                | Ok(Some held) ->
                    match Writes.release ctx.Transport held with
                    | Error e -> fail e
                    | Ok previousStatus ->
                        // The marker is gone; the lease is dropped. Restore the column it overwrote — or
                        // `Ready` if it recorded none (#481). A board failure here leaves the column alone
                        // and is reported, never fatal: the lock is already released.
                        let restoreTo =
                            match previousStatus with
                            | Some s -> s
                            | None -> BoardStatus.Ready

                        let name = statusName restoreTo

                        match Board.bootstrap ctx.Transport ctx.Owner ctx.Title with
                        | Ok board when name <> "" ->
                            Board.boardWrite ctx.Transport board ref.Owner ref.Repo ref.Number "Status" (Board.Set name) w.Id
                            |> ignore
                        | _ -> ()

                        printfn "released %s" ref.Short
                        ExitGreen

    let heartbeat (ctx: Context) (opts: Options) : int =
        match oneArg opts "heartbeat: an issue ref", worker opts with
        | Error c, _
        | _, Error c -> c
        | Ok arg, Ok w ->
            match parseRef ctx arg with
            | Error msg ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | Ok ref ->
                match Writes.verifyHeld ctx.Transport opts.LeaseMinutes (WorkerId w.Id) ref with
                | Error e -> fail e
                | Ok None ->
                    // Either someone else holds it, or the lease expired. Read the markers to say which —
                    // "a non-holder cannot renew" and "the lease expired" are different remedies.
                    match Reads.markers ctx.Transport ref.Owner ref.Repo ref.Number with
                    | Ok markers ->
                        match Reads.winner opts.LeaseMinutes markers with
                        | Some m when m.Worker <> WorkerId w.Id ->
                            eprint $"fsgg-coord-engine: %s{ref.Short} is held by %s{m.Worker.Value}, not %s{w.Id} — STOP working it, or reap it."
                        | _ ->
                            eprint $"fsgg-coord-engine: %s{w.Id}'s lease on %s{ref.Short} has EXPIRED and cannot be renewed in place — re-claim it (claim --force if its work is gone)."

                        ExitError
                    | Error e -> fail e
                | Ok(Some held) ->
                    match Writes.heartbeat ctx.Transport opts.LeaseMinutes held with
                    | Error e -> fail e
                    | Ok _ ->
                        printfn "heartbeat %s by worker %s" ref.Short w.Id
                        ExitGreen

    let take (ctx: Context) (opts: Options) : int =
        match worker opts with
        | Error c -> c
        | Ok w ->
            match scanAndDecide ctx { opts with Limit = Some 1 } Cache.Scheduling with
            // #585: a board we could not read is NOT an empty queue — but that distinction is already
            // carried by the code `fail` returns (EX_RATE for a budget, a non-zero read error otherwise),
            // and it is never EX_NONE, so "I could not look" and "I looked, and it is empty" keep
            // different codes (#266). bash's hard board-read failure exits the same way (#344's fatal
            // die), so the two engines agree.
            | Error e -> fail e
            | Ok(_, doc, _) ->
                match renderDecision { opts with Limit = Some 1 } doc with
                | Error code -> code
                | Ok result ->
                    match result.Chosen with
                    | [] ->
                        // #585: looked, nothing startable — NOT a claim. Exit EX_NONE so `take && work_it`
                        // does not proceed on nothing. (`printChosen` still prints the per-item WHY.)
                        printfn "no schedulable item — every candidate is blocked, claimed, overlapping, or undeclared"
                        printChosen opts.LeaseMinutes result
                        ExitNone
                    | item :: _ ->
                        // Claim the chosen item. `claim` re-reads and runs the CAS, so a stale scan cannot
                        // cost a double-claim: the loser backs off and the caller retries.
                        // #585: translate the claim's verdict into `take`'s contract — a win is 0, an
                        // exhausted budget passes through as EX_RATE (back off until reset), and any other
                        // failure is a LOST RACE (EX_CONTENDED): the item was startable when we picked it,
                        // so a failure to take it means someone else got there first.
                        match claim ctx { opts with Args = [ item.Ref.Short ] } with
                        | code when code = ExitGreen -> ExitGreen
                        | code when code = Errors.ExRate -> code
                        | _ -> ExitContended

    // ---- the writes ------------------------------------------------------------------------------------

    let setField (ctx: Context) (opts: Options) : int =
        match opts.Args with
        | [ refArg; field; value ] ->
            match parseRef ctx refArg, worker opts with
            | Error msg, _ ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | _, Error c -> c
            | Ok ref, Ok w ->
                match Board.bootstrap ctx.Transport ctx.Owner ctx.Title with
                | Error e -> fail e
                | Ok board ->
                    let write =
                        if value = "" then Board.Clear else Board.Set value

                    match Board.boardWrite ctx.Transport board ref.Owner ref.Repo ref.Number field write w.Id with
                    | Error e -> fail e
                    | Ok Board.Written ->
                        printfn "set %s %s = %s" ref.Short field (if value = "" then "<cleared>" else value)
                        ExitGreen
                    | Ok Board.Deferred ->
                        printfn "set %s %s = %s — QUEUED (budget exhausted; flush replays it)" ref.Short field value
                        Errors.ExRate
                    | Ok Board.NotOnBoard ->
                        eprint $"fsgg-coord-engine: %s{ref.Short} is not an item on this board — nothing written."
                        ExitError
        | _ ->
            eprint "fsgg-coord-engine: set-field takes <ref> <field> <value> (an empty value clears)."
            ExitError

    let child (ctx: Context) (opts: Options) : int =
        match opts.Args with
        | [ parentArg; childArg ] ->
            match parseRef ctx parentArg, parseRef ctx childArg with
            | Error msg, _
            | _, Error msg ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | Ok parent, Ok childRef ->
                match Reads.restId ctx.Transport childRef.Owner childRef.Repo childRef.Number with
                | Error e -> fail e
                | Ok childId ->
                    match Writes.child ctx.Transport parent childId with
                    | Error e -> fail e
                    | Ok() ->
                        printfn "attached %s as a child of %s" childRef.Short parent.Short
                        ExitGreen
        | _ ->
            eprint "fsgg-coord-engine: child takes <parent-ref> <child-ref>."
            ExitError

    let widen (ctx: Context) (opts: Options) : int =
        match oneArg opts "widen: an issue ref", worker opts with
        | Error c, _
        | _, Error c -> c
        | Ok arg, Ok w ->
            if List.isEmpty opts.Paths then
                eprint "fsgg-coord-engine: widen needs --paths <token>..."
                ExitError
            else
                match parseRef ctx arg with
                | Error msg ->
                    eprint $"fsgg-coord-engine: %s{msg}"
                    ExitError
                | Ok ref ->
                    // #706 — widen takes the HELD claim. verifyHeld is the only door to it that this command
                    // has, and it fails closed: no capability from a failed read.
                    match Writes.verifyHeld ctx.Transport opts.LeaseMinutes (WorkerId w.Id) ref with
                    | Error e -> fail e
                    | Ok None ->
                        eprint $"fsgg-coord-engine: %s{w.Id} does not hold %s{ref.Short} — widen rewrites the touch-set of a lock you must be holding (#706)."
                        ExitError
                    | Ok(Some held) ->
                        // #523 — validate BEFORE the read of the body, and rewrite BEFORE the PATCH. A bad
                        // token cannot reach the write, because it cannot produce the value the write takes.
                        match Writes.validate opts.Paths with
                        | Error msg ->
                            eprint $"fsgg-coord-engine: %s{msg}"
                            ExitError
                        | Ok validated ->
                            match Reads.issueBody ctx.Transport ref.Owner ref.Repo ref.Number with
                            | Error e -> fail e
                            | Ok body ->
                                let rewritten = Writes.rewrite body validated

                                match Writes.widen ctx.Transport held rewritten with
                                | Error e -> fail e
                                | Ok() ->
                                    printfn "widened %s → Paths: %s" ref.Short (String.Join(", ", opts.Paths))
                                    ExitGreen

    let say (ctx: Context) (opts: Options) : int =
        match oneArg opts "say: an issue ref", worker opts with
        | Error c, _
        | _, Error c -> c
        | Ok arg, Ok w ->
            match opts.ToWorker, opts.Message with
            | None, _ ->
                eprint "fsgg-coord-engine: say needs --to <worker>."
                ExitError
            | _, None ->
                eprint "fsgg-coord-engine: say needs --message <text>."
                ExitError
            | Some toW, Some msg ->
                match parseRef ctx arg with
                | Error m ->
                    eprint $"fsgg-coord-engine: %s{m}"
                    ExitError
                | Ok ref ->
                    // No lock required — the worker who most needs to speak is the one who just lost a race.
                    match Writes.say ctx.Transport (WorkerId w.Id) (WorkerId toW) ref msg with
                    | Error e -> fail e
                    | Ok() ->
                        printfn "said to %s on %s" toW ref.Short
                        ExitGreen

    let doneCmd (ctx: Context) (opts: Options) : int =
        match oneArg opts "done: an issue ref", worker opts with
        | Error c, _
        | _, Error c -> c
        | Ok arg, Ok w ->
            match parseRef ctx arg with
            | Error msg ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | Ok ref ->
                match Board.bootstrap ctx.Transport ctx.Owner ctx.Title with
                | Error e -> fail e
                | Ok board ->
                    match Done.facts ctx.Transport board ref with
                    | Error e -> fail e
                    | Ok facts ->
                        let verdict = Done.verify opts.Evidence facts
                        printfn "%s" (Done.render ref verdict)

                        match verdict with
                        | Verdict.NoVerdict _ -> ExitNoVerdict
                        | Red _ -> ExitRed
                        | Green _ ->
                            // Stamp the board Done. A board-write failure leaves the stamp GREEN (the work
                            // IS done) and reports the note — the same rule as the bash client.
                            Board.boardWrite ctx.Transport board ref.Owner ref.Repo ref.Number "Status" (Board.Set "Done") w.Id
                            |> ignore

                            // --flip: roll the parent up, if the child completes it. `Completes` is asserted
                            // by the caller running `done --flip`; a partial fix would pass through the CLI
                            // as a plain `done` (no --flip), which never climbs.
                            if opts.Flip then
                                match facts.Parent with
                                | Some parent ->
                                    match Done.rollUp ctx.Transport board w.Id parent Done.Completes with
                                    | Error e ->
                                        eprint $"fsgg-coord-engine: the stamp is GREEN, but the roll-up to %s{parent.Short} did not complete: %s{Errors.explain e}"
                                    | Ok results ->
                                        for r in results do
                                            match r with
                                            | Done.ParentClosed p -> printfn "  ↑ %s stamped Done and closed" p.Short
                                            | Done.ParentLeftOpen(p, reasons) ->
                                                eprint $"  ↑ %s{p.Short} left OPEN:"

                                                for reason in reasons do
                                                    eprint $"      %s{reason}"
                                            | Done.NoParent -> ()
                                | None -> ()

                            // #533 — A FINISHED ITEM MUST NOT KEEP ITS LOCK. `done` verified the merge, set
                            // the column Done, and rolled the parent up — and, until here, left the claim
                            // marker live for the rest of the 120m lease. A live marker's `Paths:` keep
                            // reserving its touch-set, so the item most likely to overlap a just-finished one
                            // — its own follow-up findings, filed BECAUSE you were standing in those files —
                            // is the one its own author is locked out of. This is the port's half of #533:
                            // `done --flip` set Status and never touched the marker, and `release` was the
                            // only path that dropped it — but `release` REWRITES Status, so running it on an
                            // item you just stamped clobbers the stamp you just earned.
                            //
                            // Drop OUR OWN lock, and only ours. A `Held` is obtainable only by confirming the
                            // live winner is us (`verifyHeld`), so `release` here CANNOT touch another
                            // worker's marker — deleting a claim that is not ours is `reap`'s job, and the
                            // "only your own" rule is the capability type, not a forgettable `if`. And unlike
                            // the `release` command, we do NOT restore the column: the item is Done, and Done
                            // is what stands.
                            match Writes.verifyHeld ctx.Transport opts.LeaseMinutes (WorkerId w.Id) ref with
                            | Ok(Some held) ->
                                match Writes.release ctx.Transport held with
                                | Ok _ -> ()
                                | Error e ->
                                    eprint
                                        $"fsgg-coord-engine: the stamp is GREEN, but %s{w.Id}'s claim on %s{ref.Short} could not be dropped: %s{Errors.explain e}. Run `release` (or `reap`) so it stops reserving its touch-set (#533)."
                            | Ok None ->
                                // We do not hold it. If ANOTHER worker's lock is live, this engine leaves it
                                // alone and says so — it never silently deletes a claim that is not ours.
                                match Reads.markers ctx.Transport ref.Owner ref.Repo ref.Number with
                                | Ok markers ->
                                    match Reads.winner opts.LeaseMinutes markers with
                                    | Some m when m.Worker <> WorkerId w.Id ->
                                        eprint
                                            $"fsgg-coord-engine: %s{ref.Short} is stamped Done, but %s{m.Worker.Value} still holds its claim — `done` drops only your own lock; run `reap` to clear another worker's (#533)."
                                    | _ -> ()
                                | Error _ -> ()
                            | Error e ->
                                eprint
                                    $"fsgg-coord-engine: the stamp is GREEN, but %s{w.Id}'s claim on %s{ref.Short} could not be checked: %s{Errors.explain e}. If it is still held, run `release` so it stops reserving its touch-set (#533)."

                            ExitGreen

    // ---- verify-paths ----------------------------------------------------------------------------------

    /// Check a PR's changed files against the touch-set declared by the issue it implements.
    ///
    /// THE VERDICT VOCABULARY IS THE BASH CLIENT'S, because the shim will run one where the other ran:
    ///   OK      — every changed file is inside the declared touch-set.
    ///   DRIFT   — a file falls outside it (named), and the PR should widen or split.
    ///   SKIP    — nothing to verify against (no touch-set, or the issue can't be identified). Green.
    ///   INVALID — the declared touch-set has only unmatchable tokens (#273).
    ///
    /// "I COULD NOT CHECK" IS NEVER A VERDICT (#322). An unreadable head ref, body, or file list is an
    /// ERROR — even under --warn, which downgrades a real DRIFT/INVALID to advisory but cannot downgrade a
    /// read that never happened. Stamping "stays inside its touch-set" on a subject nobody looked at is the
    /// exact fail-open this command exists to prevent.
    let verifyPaths (ctx: Context) (opts: Options) : int =
        match opts.Pr with
        | None ->
            eprint "fsgg-coord-engine: verify-paths needs --pr <n>."
            ExitError
        | Some pr ->
            let owner = ctx.Owner

            let repo =
                match opts.Repo with
                | Some r -> r
                | None ->
                    eprint "fsgg-coord-engine: verify-paths needs --repo <name> (the repo the PR is in)."
                    ""

            if repo = "" then
                ExitError
            else

            // The issue a PR implements: its `item/<n>-*` branch, else what it declares it closes.
            let resolveIssue () : Result<Ref option, Errors.IoError> =
                match Reads.prHeadRef ctx.Transport owner repo pr with
                | Error e -> Result.Error e
                | Ok head ->
                    let m = Text.RegularExpressions.Regex.Match(head, @"^item/(\d+)-")

                    if m.Success then
                        Ok(Some { Owner = owner; Repo = repo; Number = int m.Groups.[1].Value })
                    else
                        // Not an item branch — ask what it closes.
                        Reads.prClosingRef ctx.Transport owner repo pr

            match resolveIssue () with
            | Error e -> fail e
            | Ok None ->
                // Can't tell which issue this PR implements. SKIP — not a verdict, and green: a PR that
                // implements no tracked item has no touch-set to drift from.
                printfn
                    "FSGG-PATHS SKIP — cannot tell which issue PR #%d implements (branch is not item/<n>-…, and it closes no issue)."
                    pr

                ExitGreen
            | Ok(Some issue) ->
                // Repo-relative touch-sets: a PR in repo A that closes an issue in repo B cannot be checked
                // against B's paths — those say nothing about A's files (#353).
                if not (String.Equals(issue.Repo, repo, StringComparison.OrdinalIgnoreCase)) then
                    printfn
                        "FSGG-PATHS SKIP — PR #%d is in %s/%s but implements %s, in another repo — a touch-set there says nothing about the files changed here."
                        pr
                        owner
                        repo
                        issue.Short

                    ExitGreen
                else

                match Reads.issueBody ctx.Transport issue.Owner issue.Repo issue.Number with
                | Error e -> fail e
                | Ok body ->
                    match TouchSet.parse body with
                    | Undeclared
                    | DeclaredNone ->
                        printfn "FSGG-PATHS SKIP — %s declares no 'Paths:' touch-set; nothing to verify against." issue.Short
                        ExitGreen
                    | Unreadable reason ->
                        // Should not happen (we just read the body), but the type demands it be handled, and
                        // "I could not read the body" is an error, never a SKIP.
                        eprint $"fsgg-coord-engine: could not read %s{issue.Short}'s touch-set: %s{reason}"
                        ExitError
                    | Declared tokens ->
                        let unmatchable =
                            tokens
                            |> List.choose (function
                                | Unmatchable u -> Some u
                                | Matchable _ -> None)

                        if List.length unmatchable = List.length tokens then
                            // EVERY token is unmatchable — the declaration reserves nothing (#273). That is
                            // INVALID, not "everything drifts": the touch-set is the broken thing.
                            let bad = String.Join(", ", unmatchable)
                            printfn "FSGG-PATHS INVALID — %s declares only unmatchable tokens: %s" issue.Short bad
                            eprint $"  %s{Schedulability.TouchSetGrammar}"
                            if opts.Warn then ExitGreen else ExitRed
                        else

                        match Reads.prFiles ctx.Transport owner repo pr with
                        | Error e -> fail e
                        | Ok files ->
                            let drift =
                                files
                                |> List.filter (fun f -> not (tokens |> List.exists (fun t -> TouchSet.covers t f)))

                            if List.isEmpty drift then
                                printfn "FSGG-PATHS OK — PR #%d stays inside the touch-set declared by %s." pr issue.Short
                                ExitGreen
                            else
                                printfn "FSGG-PATHS DRIFT — PR #%d changes files outside the touch-set declared by %s:" pr issue.Short

                                for f in drift do
                                    printfn "    %s" f

                                eprint "  Widen the touch-set (fsgg-coord-engine widen), or split the PR."
                                if opts.Warn then ExitGreen else ExitRed

    // ---- identity --------------------------------------------------------------------------------------

    let whoami (opts: Options) : int =
        if opts.Mint then
            printfn "export FSGG_WORKER=%s" (Identity.mint ())
            eprint "fsgg-coord-engine: minted a worker id — eval this line, or export it, in EACH worker's shell."
            ExitGreen
        else
            match Identity.resolve opts.Worker with
            | Error msg ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | Ok w ->
                for line in Identity.explain w do
                    printfn "%s" line

                ExitGreen

    // ---- the dispatcher for the IO commands ------------------------------------------------------------

    /// Build the context — the transport, the board coordinates, the token check. `Error` is a printed
    /// message and an exit code (a missing token is a refusal, never an empty board).
    let context () : Result<Context * IDisposable, int> =
        let token =
            match env "GITHUB_TOKEN" (env "GH_TOKEN" "") with
            | "" -> None
            | t -> Some t

        match token with
        | None ->
            eprint
                "fsgg-coord-engine: this command needs a GitHub token ($GITHUB_TOKEN or $GH_TOKEN). An unauthenticated read returns an empty organization, and an empty board is exactly the answer this engine refuses to invent."

            Result.Error ExitError
        | Some token ->
            let transport = new Transport.HttpTransport(Transport.apiBaseFromEnv (), token)

            Ok(
                { Transport = transport
                  Owner = env "FSGG_COORD_OWNER" "FS-GG"
                  Title = env "FSGG_COORD_PROJECT" "Coordination"
                  DefaultRepo = None },
                transport :> IDisposable
            )

    /// Run an IO command. Every one goes through here so the token check, the transport lifetime, and the
    /// defect boundary are in one place.
    let run (opts: Options) : int =
        match context () with
        | Error code -> code
        | Ok(ctx, disposable) ->
            use _ = disposable

            match opts.Command with
            | Next -> next ctx opts
            | BatchCmd -> batch ctx opts
            | Ready -> ready ctx opts
            | Who -> who ctx opts
            | Budget -> budget ctx
            | Claim -> claim ctx opts
            | Take -> take ctx opts
            | Release -> release ctx opts
            | Heartbeat -> heartbeat ctx opts
            | SetField -> setField ctx opts
            | Child -> child ctx opts
            | Widen -> widen ctx opts
            | Say -> say ctx opts
            | DoneCmd -> doneCmd ctx opts
            | VerifyPaths -> verifyPaths ctx opts
            | other -> failwith $"Client.run received a non-IO command: %A{other}"
