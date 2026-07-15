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
    open System.Diagnostics
    open System.IO
    open System.Text.Json
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
                // #419: point at the mint COMMAND, not a literal — the same remedy `whoami` gives, so the
                // command path agents actually run most does not re-introduce the copy-a-literal attractor.
                // The id is named as DIAGNOSIS (which id you are using now), never as one to invent.
                eprint $"fsgg-coord-engine: WARNING — worker id '%s{w.Id}' was derived from a session where %s{why}. Give EACH worker a unique id (do NOT invent one):  eval \"$(fsgg-coord-engine whoami --mint)\""
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
        Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title
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
                    // #440: name the OBSERVED reason, never a GUESSED list of causes — `printChosen` prints the
                    // honest "nothing schedulable right now." plus the per-item passed-over reasons, the shape
                    // `batch` already uses. The guessed list asserted causes `next` never observed (case 41).
                    printChosen opts.LeaseMinutes result

                ExitGreen

    /// `ready --json` — THE MACHINE CONTRACT a reconciler (`/check-board`) and `next` read, an array of
    /// board rows. The field set is bash's `board_items` projection, the fields a consumer keys on: the
    /// `number`/`repo` that name the item, the board `status` (null when the column is unset — a modelled
    /// fact, #437), the issue `state` (which is not the column — when they disagree the issue wins, #520),
    /// and the `title`. Written with a real JSON writer so a title carrying a quote cannot forge the array.
    let private renderReadyJson (rows: Scan.Row list) : string =
        use stream = new MemoryStream()
        use w = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = false, SkipValidation = false))

        w.WriteStartArray()

        for row in rows do
            w.WriteStartObject()
            w.WriteNumber("number", row.Ref.Number)
            w.WriteString("repo", $"%s{row.Ref.Owner}/%s{row.Ref.Repo}")
            w.WriteString("title", row.Title)

            match statusName row.Status with
            | "" -> w.WriteNull("status")
            | s -> w.WriteString("status", s)

            w.WriteString(
                "state",
                match row.State with
                | Open -> "OPEN"
                | Closed -> "CLOSED"
            )

            w.WriteEndObject()

        w.WriteEndArray()
        w.Flush()
        Text.Encoding.UTF8.GetString(stream.ToArray())

    let ready (ctx: Context) (opts: Options) : int =
        // A RECONCILER read — always fresh, never the cache. Its whole job is to say what is true right now.
        match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
        | Error e -> fail e
        | Ok board ->
            match Scan.board ctx.Transport Cache.Reconciling ctx.Owner ctx.Title board.Number with
            | Error e -> fail e
            | Ok rows ->
                // #520 — `ready` is a TRUTH read: it shows the BOARD, including items the SCHEDULER refuses
                // (a closed-but-Ready issue, an item whose only touch-set is unmatchable). So it filters on
                // the board Status COLUMN, and NEVER on the issue's OPEN/CLOSED state — the column is the
                // projection the reconciler exists to reconcile, and hiding a closed-but-Ready row by its
                // state is exactly the disagreement `/check-board` is run to find. A PR is not an item of
                // work (#641), so it is the one thing dropped unconditionally.
                let wanted =
                    rows
                    |> List.filter (fun r -> not r.IsPullRequest)
                    |> List.filter (fun r ->
                        match opts.Repo with
                        | Some name -> String.Equals(r.Ref.Repo, name, StringComparison.OrdinalIgnoreCase)
                        | None -> true)
                    |> List.filter (fun r ->
                        // The DEFAULT excludes Done and nothing else (bash's `board_filter` xd=1). Naming a
                        // column (`--status S`) or `--all` widens past it — asking for a column, Done
                        // included, is asking to SEE it. `--status` matches the column NAME, case-insensitive,
                        // the way bash matches it.
                        match opts.Status with
                        | Some s -> String.Equals(statusName r.Status, s, StringComparison.OrdinalIgnoreCase)
                        | None -> opts.All || r.Status <> BoardStatus.Done)

                match opts.Render with
                | Json -> printfn "%s" (renderReadyJson wanted)
                | Text ->
                    for row in wanted do
                        let status =
                            match statusName row.Status with
                            | "" -> "(no status)"
                            | s -> s

                        printfn "  %-14s %s  %s" status row.Ref.Short row.Title

                ExitGreen

    /// A `who` row's lock state. "In flight" is a LOCK fact, not a column fact: an item is in flight when a
    /// marker holds it — LIVE (`Held`) or past its lease (`Stale`, still a lock only `reap` may break) — or
    /// when the board column says In progress with NO marker at all (`Unclaimed` — someone working outside
    /// the protocol, which `who` exists to surface). A Ready/Backlog item nobody claimed is simply not in
    /// flight, and `who` does not list it. This is cases 20/25's certified `who`, and the JSON `.state` a
    /// consumer keys on (held/stale/unclaimed).
    type private WhoState =
        | Held of Reads.Marker
        | Stale of Reads.Marker
        | Unclaimed

    let private whoStateName =
        function
        | Held _ -> "held"
        | Stale _ -> "stale"
        | Unclaimed -> "unclaimed"

    /// The touch-set a claim reserves (or an In-progress item declares) — the `paths` a consumer keys on
    /// (case 25). Read from the issue body; an undeclared or unreadable one is an empty list, because `who`
    /// reports what is reserved, and nothing is reserved on a surface nobody declared.
    let private pathNames (ts: TouchSet) : string list =
        match ts with
        | Declared tokens ->
            tokens
            |> List.map (fun t ->
                match t with
                | Matchable s -> s
                | Unmatchable s -> s)
        | Undeclared
        | DeclaredNone
        | Unreadable _ -> []

    /// `who --json` — the machine contract cases 20/25 certify: a JSON array of the in-flight items, each
    /// carrying its `number`, `repo`, `state` (held/stale/unclaimed), the `worker` holding it (`null` when
    /// unclaimed — only the column, not a marker, says so), and the `paths` it reserves. A real JSON writer,
    /// so a worker id or path carrying a quote cannot forge the array.
    let private renderWhoJson (rows: (Scan.Row * WhoState * string list) list) : string =
        use stream = new MemoryStream()
        use w = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = false, SkipValidation = false))

        w.WriteStartArray()

        for (row, state, paths) in rows do
            w.WriteStartObject()
            w.WriteNumber("number", row.Ref.Number)
            w.WriteString("repo", $"%s{row.Ref.Owner}/%s{row.Ref.Repo}")
            w.WriteString("state", whoStateName state)

            match state with
            | Held m
            | Stale m -> w.WriteString("worker", m.Worker.Value)
            | Unclaimed -> w.WriteNull("worker")

            w.WriteStartArray("paths")

            for p in paths do
                w.WriteStringValue p

            w.WriteEndArray()
            w.WriteEndObject()

        w.WriteEndArray()
        w.Flush()
        Text.Encoding.UTF8.GetString(stream.ToArray())

    let who (ctx: Context) (opts: Options) : int =
        // A truth read — fresh, and it reads the LOCK, which is never cached.
        match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
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

                // Classify a candidate by its LOCK. The lowest-id marker in the CAS's own total order names
                // the holder; the lease decides whether it is live (`Held`) or past its window (`Stale`).
                let classify (row: Scan.Row) (markers: Reads.Marker list) : WhoState option =
                    match Reads.winner opts.LeaseMinutes markers with
                    | Some m -> Some(Held m)
                    | None ->
                        match markers |> List.sortBy (fun m -> m.Id) |> List.tryHead with
                        | Some m -> Some(Stale m)
                        | None ->
                            // No marker at all: only an In-progress COLUMN makes it in flight — work with no
                            // lock behind it. Ready/Backlog with no marker is nobody's claim, and not listed.
                            if row.Status = BoardStatus.InProgress then Some Unclaimed else None

                let mutable failure = None
                let results = ResizeArray<Scan.Row * WhoState * string list>()

                for row in candidates do
                    if failure.IsNone then
                        // A FAILED MARKER READ IS FATAL (#461): a claim set we could not read is never an
                        // empty one, so `who` fails closed rather than reporting a live lock as absent.
                        match Reads.markers ctx.Transport row.Ref.Owner row.Ref.Repo row.Ref.Number with
                        | Error e -> failure <- Some e
                        | Ok markers ->
                            match classify row markers with
                            | None -> ()
                            | Some state ->
                                // The reserved touch-set is informational, so a body we could not read is an
                                // empty list, not a failed `who` — a body is not a lock. The one read that
                                // must fail closed (the marker) is the one above. Only `--json` reports paths,
                                // so the human table pays no body read: the text render never shows them.
                                let paths =
                                    match opts.Render with
                                    | Text -> []
                                    | Json ->
                                        match Reads.issueBody ctx.Transport row.Ref.Owner row.Ref.Repo row.Ref.Number with
                                        | Ok body -> pathNames (TouchSet.parse body)
                                        | Error _ -> []

                                results.Add(row, state, paths)

                match failure with
                | Some e -> fail e
                | None ->
                    let inFlight = List.ofSeq results

                    match opts.Render with
                    | Json -> printfn "%s" (renderWhoJson inFlight)
                    | Text ->
                        if List.isEmpty inFlight then
                            printfn "nothing is in flight."
                        else
                            for (row, state, _) in inFlight do
                                match state with
                                | Held m ->
                                    printfn
                                        "  %-16s held by %s  (%s)"
                                        row.Ref.Short
                                        m.Worker.Value
                                        (Schedulability.leaseWindow opts.LeaseMinutes m.AgeSeconds)
                                | Stale m ->
                                    printfn
                                        "  %-16s held by %s  STALE (%s)"
                                        row.Ref.Short
                                        m.Worker.Value
                                        (Schedulability.leaseWindow opts.LeaseMinutes m.AgeSeconds)
                                | Unclaimed ->
                                    printfn
                                        "  %-16s UNCLAIMED — In progress with NO claim marker"
                                        row.Ref.Short

                    // A markerless In-progress item is work happening OUTSIDE the protocol — warned on
                    // stderr (where case 20 looks for it) regardless of the stdout format, so a `who` piped
                    // to a machine consumer still shouts about the one thing no reconciler can fix by itself.
                    for (row, state, _) in inFlight do
                        match state with
                        | Unclaimed ->
                            eprint
                                $"fsgg-coord-engine: WARNING — %s{row.Ref.Short} is In progress with NO claim marker (someone is working outside the protocol)."
                        | _ -> ()

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
        match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
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
                    // #481: the claim records the column it OVERWRITES, so `release` (and `reap`) can put it
                    // back rather than guess `Ready`. The pre-claim column is knowable at exactly one instant
                    // — before the `In progress` write below overwrites it — so it rides into the marker here.
                    //
                    // The board is read at most ONCE: `board` is a `lazy` shared between the pre-claim column
                    // read and the `In progress` write, so a winning claim bootstraps a single time. And the
                    // pre-claim read is spent ONLY when we win and post: `readPreviousStatus` is the CAS's
                    // post-path thunk, never called on a lost race or an idempotent re-claim. That is what
                    // keeps this off the losing side of the hottest path in the org, on the budget that dies
                    // first under fan-out (#418).
                    let board = lazy (Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title)

                    let readPreviousStatus () =
                        // BEST-EFFORT. A pre-claim column we cannot read is recorded as NONE — the same as a
                        // marker minted before #481 — and it NEVER blocks the lock: the lease matters more
                        // than the courtesy of a restorable column, and `release` falling back to `Ready` is
                        // the safe, pre-existing behaviour for "nothing recorded".
                        match board.Force() with
                        | Ok b ->
                            match Board.itemStatus ctx.Transport b ref.Owner ref.Repo ref.Number with
                            | Ok s -> s
                            | Error _ -> None
                        | Error _ -> None

                    match Writes.claim ctx.Transport opts.LeaseMinutes (WorkerId w.Id) session ref readPreviousStatus with
                    | Error e -> fail e
                    | Ok(Writes.Won held) ->
                        // Move the board column to In progress — the ONE board write, through the
                        // queue-aware path so an exhausted budget defers rather than drops (#510).
                        match board.Force() with
                        | Error e ->
                            // The LOCK is held (the marker is posted); a board-write failure does not
                            // un-hold it. Report the claim, note the board.
                            printfn "claimed %s by worker %s" ref.Short w.Id
                            eprint $"fsgg-coord-engine: note — the lock is held, but the board column could not be moved: %s{Errors.explain e}"
                            ExitGreen
                        | Ok b ->
                            match
                                Board.boardWrite ctx.Transport b ref.Owner ref.Repo ref.Number "Status" (Board.Set "In progress") w.Id
                            with
                            | Ok _
                            | Error _ ->
                                ignore held
                                printfn "claimed %s by worker %s" ref.Short w.Id
                                ExitGreen
                    | Ok(Writes.Lost holder) ->
                        eprint $"fsgg-coord-engine: %s{ref.Short} is already held by %s{holder.Value}. Pick another, or wait for the lease."
                        ExitRed
                    | Ok(Writes.Twin theirs) ->
                        // #419: the marker is ours by id but a DIFFERENT session — two workers share one id.
                        // This is a broken IDENTITY, not a contested item, and the fix for a broken identity
                        // is a NEW identity, so `--force` (which steals contested items) must NOT override it:
                        // forcing here would delete a lock our twin is working behind. The remedy is a command,
                        // not a literal — an id an agent copies is an id agents collide on.
                        eprint
                            $"fsgg-coord-engine: %s{ref.Short} carries a live marker with YOUR worker id '%s{w.Id}' but a DIFFERENT session (%s{theirs.Value}) — two workers share one id (#419). Adopting it would put both of you on this item, which is the double-claim ADR-0027 exists to prevent."

                        eprint "  Mint a fresh, unique id in THIS shell (do NOT invent one):  eval \"$(fsgg-coord-engine whoami --mint)\""
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
                            // A recorded `In progress` is the claim's OWN footprint, not a column anybody
                            // chose — restoring it would leave the item looking claimed with no claim on it.
                            // It falls back to `Ready`, exactly as an unrecorded column does (#481).
                            | Some InProgress
                            | None -> BoardStatus.Ready
                            | Some s -> s

                        let name = statusName restoreTo

                        match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
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
                        // does not proceed on nothing.
                        // #440: name the OBSERVED reason, never a GUESSED list of causes. `printChosen` prints
                        // the honest "nothing schedulable right now." to stdout and the per-item passed-over
                        // reasons to stderr — the same shape `batch`/`decide` already use. Reciting "every
                        // candidate is blocked, claimed, overlapping, or undeclared" asserts causes we did not
                        // observe (case 41); over a starved queue half of them are false, which is the #440
                        // defect wearing a headline.
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

    /// `set-field --batch <ref> Field=Value ...` — N fields in ONE aliased mutation (#448).
    ///
    /// The whole point is the call count: three separate writes are three GraphQL points; the same three
    /// aliased into one document is one. So EVERYTHING is resolved before a single mutation is emitted (a
    /// pair that fails validation costs zero — a refused value must not spend the budget that dies first),
    /// and the two failure arms are told apart because they carry opposite promises: a rate limit refused
    /// the whole document, so every pair is QUEUED; a per-alias failure means the board is half-written, so
    /// nothing is queued and the caller is told, field by field, what landed and what did not.
    let private setFieldBatchCmd (ctx: Context) (opts: Options) : int =
        match opts.Args with
        | refArg :: (_ :: _ as pairs) ->
            match parseRef ctx refArg, worker opts with
            | Error msg, _ ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | _, Error c -> c
            | Ok ref, Ok w ->
                // Split each pair on the FIRST '=' — a value may legitimately contain one (`Contract=a=b`),
                // and an empty value clears, exactly as the single path's empty `<value>` does.
                let parsePair (s: string) : Result<string * Board.FieldWrite, string> =
                    match s.IndexOf '=' with
                    | -1 -> Error s
                    | i ->
                        let field = s.Substring(0, i)
                        let value = s.Substring(i + 1)

                        if field = "" then Error s
                        else Ok(field, (if value = "" then Board.Clear else Board.Set value))

                let parsed = pairs |> List.map parsePair

                match parsed |> List.tryPick (function | Error s -> Some s | Ok _ -> None) with
                | Some bad ->
                    eprint
                        $"fsgg-coord-engine: set-field --batch takes Field=Value pairs (an empty value clears); '%s{bad}' is not one."
                    ExitError
                | None ->
                    let writes = parsed |> List.choose (function | Ok p -> Some p | Error _ -> None)

                    match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
                    | Error e -> fail e
                    | Ok board ->
                        // Map an alias ("f2") back to the pair it wrote, so a partial write can be reported
                        // in the caller's OWN vocabulary — `Field='value'` — not "f2".
                        let describe (alias: string) : string =
                            let idx =
                                match Int32.TryParse(alias.TrimStart 'f') with
                                | true, n -> Some n
                                | _ -> None

                            match idx with
                            | Some i when i >= 0 && i < List.length writes ->
                                match List.item i writes with
                                | field, Board.Set v -> $"%s{field}='%s{v}'"
                                | field, Board.Clear -> $"%s{field}=<cleared>"
                            | _ -> alias

                        match Board.boardWriteBatch ctx.Transport board ref.Owner ref.Repo ref.Number writes w.Id with
                        // THE PARTIAL ARM IS ITS OWN ANSWER — matched BEFORE the generic failure. Some aliases
                        // landed; reporting nothing happened would be a lie, and reporting success is the bug
                        // #448 forbade by name. EX_PARTIAL (4), and the board is half-written on the record.
                        | Error(Errors.Partial(applied, failed)) ->
                            eprint
                                "fsgg-coord-engine: PARTIALLY APPLIED — the board is now half-written. This is NOT queued: replaying the document would rewrite the aliases that already landed."

                            for alias in applied do
                                eprint $"  APPLIED  %s{describe alias}"

                            for alias, msg in failed do
                                eprint $"  FAILED   %s{describe alias} — %s{msg}"

                            Errors.ExPartial
                        | Error e -> fail e
                        | Ok Board.Written ->
                            printfn "set %d field(s) on %s in one aliased mutation:" (List.length writes) ref.Short

                            for field, write in writes do
                                printfn "  %s = %s" field (match write with | Board.Set v -> v | Board.Clear -> "<cleared>")

                            ExitGreen
                        | Ok Board.Deferred ->
                            printfn
                                "set-field --batch %s — QUEUED all %d field(s) (budget exhausted; flush replays the batch)"
                                ref.Short
                                (List.length writes)

                            Errors.ExRate
                        | Ok Board.NotOnBoard ->
                            eprint $"fsgg-coord-engine: %s{ref.Short} is not an item on this board — nothing written."
                            ExitError
        | _ ->
            eprint "fsgg-coord-engine: set-field --batch takes <ref> followed by one or more Field=Value pairs."
            ExitError

    let setField (ctx: Context) (opts: Options) : int =
        if opts.Batch then
            setFieldBatchCmd ctx opts
        else

        match opts.Args with
        | [ refArg; field; value ] ->
            match parseRef ctx refArg, worker opts with
            | Error msg, _ ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | _, Error c -> c
            | Ok ref, Ok w ->
                match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
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
                    // #320: the existing-links read must FAIL CLOSED. Swallowing it would make "I could not
                    // reach the API" look exactly like "the edge is not there" — `child` would POST, collect
                    // a 422, and blame the token. Idempotency is BY ID, not by number: two repos can each
                    // have an issue #7, so a re-run of a worker's close-out never has to reason about the edge.
                    match Reads.subIssueIds ctx.Transport parent.Owner parent.Repo parent.Number with
                    | Error e ->
                        eprint
                            $"fsgg-coord-engine: child: cannot read %s{parent.Short}'s sub-issues (%s{Errors.explain e}) — refusing to guess whether %s{childRef.Short} is already linked."
                        ExitError
                    | Ok existing when List.contains childId existing ->
                        printfn "%s is already a sub-issue of %s — nothing to do" childRef.Short parent.Short
                        ExitGreen
                    | Ok _ ->
                        match Writes.child ctx.Transport parent childId with
                        // A failed link surfaces the API's OWN diagnosis — a 422 (already linked, or a
                        // cross-repo link GitHub refuses) and a 403 (no `issues: write`) are different
                        // problems with different fixes, and guessing one would send the worker at the wrong.
                        | Error e -> fail e
                        | Ok() ->
                            printfn "linked %s as a sub-issue of %s" childRef.Short parent.Short
                            ExitGreen
        | _ ->
            eprint "fsgg-coord-engine: child takes <parent-ref> <child-ref>."
            ExitError

    // #353 — two refs name the same repo. `Paths:` tokens are repo-relative, so a touch-set comparison
    // is only meaningful within a repo; both `overlap` and `widen`'s re-check narrow the live set to this.
    let private sameRepo (a: Ref) (b: Ref) =
        String.Equals(a.Owner, b.Owner, StringComparison.OrdinalIgnoreCase)
        && String.Equals(a.Repo, b.Repo, StringComparison.OrdinalIgnoreCase)

    // The tokens a collision is named IN — the stem (`src/Scene/**` and `src/Scene` are one subtree, so
    // the raw suffix beside a reservation that has none reads as two different things), deduped.
    let private sharedTokens (pairs: (string * string) list) =
        pairs
        |> List.collect (fun (a, b) -> [ TouchSet.stem a; TouchSet.stem b ])
        |> List.distinct
        |> String.concat ", "

    /// The #353 collision scan, shared by `overlap --active` and `widen`'s re-check: given an item's ref
    /// and touch-set, every LIVE claim in the SAME repo whose touch-set collides with it, named with its
    /// holder and the shared token stems. The repo scope IS the fix — a same-named repo-relative token in
    /// another repo names a different file, so it is never a collision and its holder is never named. A
    /// claim we could not read propagates as an Error: a claim we could not check is never a silent DISJOINT.
    let private activeCollisions
        (ctx: Context)
        (opts: Options)
        (ref: Ref)
        (ts: TouchSet)
        : Errors.IoResult<(Ref * string * string) list> =
        match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
        | Error e -> Error e
        | Ok board ->
            match Scan.board ctx.Transport Cache.Scheduling ctx.Owner ctx.Title board.Number with
            | Error e -> Error e
            | Ok rows ->
                // SAME REPO, in flight, not this item, not a PR — the repo scope IS the #353 fix.
                let candidates =
                    rows
                    |> List.filter (fun r ->
                        not r.IsPullRequest
                        && r.Status = InProgress
                        && r.Ref.Number <> ref.Number
                        && sameRepo r.Ref ref)

                // Only a LIVE claim reserves anything (`winner` applies the lease); read its touch-set and
                // compare. A read that fails propagates — a claim we could not check is never a silent DISJOINT.
                let rec scan acc rows =
                    match rows with
                    | [] -> Ok(List.rev acc)
                    | (row: Scan.Row) :: rest ->
                        match Reads.markers ctx.Transport row.Ref.Owner row.Ref.Repo row.Ref.Number with
                        | Error e -> Error e
                        | Ok markers ->
                            match Reads.winner opts.LeaseMinutes markers with
                            | None -> scan acc rest
                            | Some m ->
                                match Reads.issueBody ctx.Transport row.Ref.Owner row.Ref.Repo row.Ref.Number with
                                | Error e -> Error e
                                | Ok body ->
                                    match TouchSet.conflicts ts (TouchSet.parse body) with
                                    | [] -> scan acc rest
                                    | pairs -> scan ((row.Ref, m.Worker.Value, sharedTokens pairs) :: acc) rest

                scan [] candidates

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

                                    // #353 — ADR-0021's "re-declare AND re-check overlap before continuing", the
                                    // half a worker cannot do alone. The widen has LANDED (that is its job, and how
                                    // a bad declaration gets fixed); now re-check the NEW touch-set against the live
                                    // claims in THIS item's repo, and notify each worker it now collides with — on
                                    // their own issue. The scan compares the rewritten body's touch-set, never the
                                    // old one. Scoped to the same repo: a cross-repo namesake is a phantom (#353),
                                    // and its innocent holder is left uncommented.
                                    match activeCollisions ctx opts ref (TouchSet.parse rewritten.Body) with
                                    | Error e -> fail e
                                    | Ok [] ->
                                        printfn "DISJOINT — the widened touch-set clears every live claim in %s/%s (#353)." ref.Owner ref.Repo
                                        ExitGreen
                                    | Ok collisions ->
                                        // The notify is the part a worker cannot do alone. A post that fails is
                                        // reported, but the collision still stands — it does not become a DISJOINT.
                                        let paths = String.Join(", ", opts.Paths)

                                        for other, holder, toks in collisions do
                                            eprint $"OVERLAP — now collides with %s{other.Short} (worker %s{holder})"
                                            eprint $"  %s{toks}"

                                            let msg =
                                                $"heads up: I widened %s{ref.Short} to `Paths: %s{paths}`, which now overlaps your touch-set here (%s{toks}). We cannot run these in parallel as declared — sequence one behind the other (`Blocked by`) or split the touch-set, and reply here."

                                            match Writes.say ctx.Transport (WorkerId w.Id) (WorkerId holder) other msg with
                                            | Error e ->
                                                eprint $"  could NOT notify worker %s{holder} on %s{other.Short}: %s{Errors.explain e}"
                                            | Ok() -> eprint $"  notified worker %s{holder} on %s{other.Short}"

                                        eprint "fsgg-coord-engine: widening introduced a collision — do NOT keep editing the shared paths until it is resolved."
                                        // A real same-repo collision exits non-zero (engine ExitContended=6; bash's
                                        // literal 1 disposed on the record, ADR-0040 §5).
                                        ExitContended

    /// #353 — DOES THIS ITEM'S TOUCH-SET COLLIDE WITH ANOTHER'S, and NOTHING outside its own repo counts.
    ///
    /// `Paths:` tokens are repo-relative: `scripts/fsgg-coord` names a file in whichever repo the item lives.
    /// So comparing two items' token lists is only meaningful WITHIN a repo — `TouchSet.conflicts` says so in
    /// its own contract. `overlap` used to hand `active_claims` no repo, so a token collided with the same
    /// string in every OTHER repo — a phantom that never hands two workers one file (the dangerous
    /// direction), but stops a worker who has nothing to stop for and is INCOHERENT with the scheduler, which
    /// would run the pair in parallel. Two shapes, both repo-scoped:
    ///   `overlap <ref> --active`     — the item vs the LIVE claims in its own repo.
    ///   `overlap <ref-a> <ref-b>`    — the two items, or DISJOINT-by-construction if they are in different repos.
    let overlapCmd (ctx: Context) (opts: Options) : int =
        let touchSetOf (ref: Ref) =
            Reads.issueBody ctx.Transport ref.Owner ref.Repo ref.Number
            |> Result.map TouchSet.parse

        match opts.Args with
        | [ a ] when opts.Active ->
            match parseRef ctx a with
            | Error m ->
                eprint $"fsgg-coord-engine: %s{m}"
                ExitError
            | Ok ref ->
                match touchSetOf ref with
                | Error e -> fail e
                | Ok ts ->
                    match activeCollisions ctx opts ref ts with
                    | Error e -> fail e
                    | Ok [] ->
                        printfn "DISJOINT — %s overlaps no live claim in %s/%s (#353)." ref.Short ref.Owner ref.Repo
                        ExitGreen
                    | Ok collisions ->
                        for other, holder, toks in collisions do
                            printfn "OVERLAP — %s collides with %s held by %s on %s" ref.Short other.Short holder toks

                        ExitContended

        | [ a; b ] when not opts.Active ->
            match parseRef ctx a, parseRef ctx b with
            | Error m, _
            | _, Error m ->
                eprint $"fsgg-coord-engine: %s{m}"
                ExitError
            | Ok ra, Ok rb ->
                if not (sameRepo ra rb) then
                    // #353 — DISJOINT BY CONSTRUCTION. Repo-relative tokens in two different repos can never
                    // name the same file, so this needs no body read at all.
                    printfn
                        "DISJOINT — %s and %s are in different repos; repo-relative touch-sets can never name the same file (#353)."
                        ra.Short
                        rb.Short

                    ExitGreen
                else
                    match touchSetOf ra with
                    | Error e -> fail e
                    | Ok tsa ->
                        match touchSetOf rb with
                        | Error e -> fail e
                        | Ok tsb ->
                            match TouchSet.conflicts tsa tsb with
                            | [] ->
                                printfn "DISJOINT — %s and %s share no touch-set token; they may run in parallel." ra.Short rb.Short
                                ExitGreen
                            | pairs ->
                                printfn "OVERLAP — %s and %s share %s" ra.Short rb.Short (sharedTokens pairs)
                                ExitContended

        | _ ->
            eprint "fsgg-coord-engine: overlap needs <ref> --active, or two refs: overlap <ref-a> <ref-b>."
            ExitError

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
                match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
                | Error e -> fail e
                | Ok board ->
                    match Done.facts ctx.Transport board ref with
                    | Error e -> fail e
                    | Ok facts ->
                        let verdict = Done.verify opts.Pr opts.Evidence facts
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

    /// resolve_repo (bash): a `--repo` value is a registry short-id (`sdd`), an `owner/repo`, or a literal
    /// repo name. It reduces to the repo NAME board rows carry — a short-id maps, an `owner/repo` keeps its
    /// repo part, a literal name passes through — so `--repo sdd`, `--repo FS-GG/FS.GG.SDD` and
    /// `--repo FS.GG.SDD` all name one queue. (The roster map is embedded, not read from repos.yml, because
    /// the shim ships as a `kind: client` kit item WITHOUT the roster — case 13 §6c / #381.) Shared by the
    /// #480 worker-command scoping below and by verify-paths' `--repo`/`--issue` boundary (#479).
    let private resolveRepo (raw: string) : string =
        match raw.ToLowerInvariant() with
        | "sdd" -> "FS.GG.SDD"
        | "rendering" -> "FS.GG.Rendering"
        | "governance" -> "FS.GG.Governance"
        | "templates" -> "FS.GG.Templates"
        | "game" -> "FS.GG.Game"
        | "audio" -> "FS.GG.Audio"
        | _ ->
            // owner/repo -> the repo part (bash's `${1#*/}`); a literal name -> itself.
            match raw.IndexOf('/') with
            | -1 -> raw
            | i -> raw.Substring(i + 1)

    // ---- #480/#430: the repo a command scopes to — the checkout you are STANDING IN ------------------
    // Defined ABOVE verify-paths (and the worker-command `scopedRepo` below) because BOTH read the same
    // signal: the git remote of the checkout. verify-paths' #430 default (neither `--repo` nor `--issue`)
    // resolves the repo exactly the way `next`/`take`/`batch`/`who` do — one shared reader, one behaviour.

    /// A GitHub remote URL → its `owner/repo`, or `None` when the URL is not a GitHub remote naming
    /// exactly one owner/repo. Handles every form `git config remote.origin.url` yields:
    /// `https://github.com/FS-GG/x(.git)`, `git@github.com:FS-GG/x(.git)`, `ssh://…/FS-GG/x.git`. A bare
    /// host or a nested path is NOT a scope — bash's `*/*/*|*/` guard, held here as an exact one-slash
    /// requirement — so a malformed remote can never be silently read as a repo.
    let parseGitHubSlug (url: string) : string option =
        match url.IndexOf("github.com", StringComparison.OrdinalIgnoreCase) with
        | -1 -> None
        | idx ->
            let mutable s = url.Substring(idx + "github.com".Length).TrimStart(':', '/')

            if s.EndsWith(".git", StringComparison.OrdinalIgnoreCase) then
                s <- s.Substring(0, s.Length - 4)

            s <- s.TrimEnd('/')
            // owner/repo EXACTLY — one slash, both sides non-empty.
            let parts = s.Split('/')

            if parts.Length = 2 && parts.[0] <> "" && parts.[1] <> "" then
                Some s
            else
                None

    /// scope_repo (#480): the current checkout's `owner/repo`, read from `git config --get
    /// remote.origin.url`. FREE and offline — deliberately NOT `gh repo view` — so resolving the scope
    /// cannot burn GraphQL budget, and an exhausted budget can never be misreported as "you are not in a
    /// checkout" (#430). `None` when there is no readable `origin` that parses as `owner/repo`.
    let private gitRemoteRepo () : string option =
        try
            let psi = ProcessStartInfo("git", "config --get remote.origin.url")
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError <- true
            psi.UseShellExecute <- false
            use p = Process.Start psi
            let url = p.StandardOutput.ReadToEnd().Trim()
            p.WaitForExit()

            if p.ExitCode <> 0 then None else parseGitHubSlug url
        with _ ->
            None

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

            // An explicit `--issue` (owner/repo#n, repo#n, or a URL) names the issue the PR implements —
            // no branch or closing-ref resolution needed. It is parsed up front because its repo is
            // authoritative: it decides the repo when `--repo` is absent, and a `--issue` in a DIFFERENT
            // repo than `--repo` is a straddle the tool refuses (#479).
            let issueRef =
                match opts.Issue with
                | None -> Ok None
                | Some raw ->
                    match parseRef ctx raw with
                    | Ok r -> Ok(Some r)
                    | Error m -> Result.Error m

            match issueRef with
            | Result.Error m ->
                eprint $"fsgg-coord-engine: verify-paths --issue: %s{m}"
                ExitError
            | Ok issueRef ->

            // The repo the PR is in: `--repo` (a registry short-id / owner/repo / literal name, reduced the
            // way every worker command reduces it — case 13's resolve_repo), else the `--issue`'s repo (the
            // issue decides when no `--repo` is given), else #430's git-remote default — the repo of the
            // checkout you are standing in, read FREE and offline from `git config remote.origin.url`, the
            // same signal `next`/`take`/`batch`/`who` scope to (#480). Deliberately NOT `gh repo view`
            // (bash's fallback): repo resolution must never spend GraphQL, so an exhausted budget can never
            // be dressed up as "not inside a checkout" — the exact fail this whole command guards against.
            // With no remote either, there is no subject to check, so it refuses (an earned verdict, since
            // `git config` failing is not a rate limit dressed up as one).
            let repo =
                match opts.Repo with
                | Some r -> Ok(resolveRepo r)
                | None ->
                    match issueRef with
                    | Some ir -> Ok ir.Repo
                    | None ->
                        match gitRemoteRepo () with
                        | Some slug -> Ok(resolveRepo slug)
                        | None ->
                            eprint
                                "fsgg-coord-engine: verify-paths is not inside a GitHub checkout (no git remote), and neither --repo nor --issue names the repo the PR is in. Name it with --repo FS-GG/<repo>, or the issue with --issue <ref>."

                            Result.Error ExitError

            match repo with
            | Result.Error rc -> rc
            | Ok repo ->

            // #479: `--repo` and `--issue` naming DIFFERENT repos is a straddle — a touch-set in one repo
            // says nothing about the files changed in the other, and printing a verdict on the wrong subject
            // is the exact fail-open this command exists to prevent (#266). It fails CLOSED both by default
            // AND under --warn: --warn downgrades a real DRIFT to advisory, but it cannot license a verdict on
            // a subject that was never compared. (Only reachable when BOTH flags are present — with `--repo`
            // absent, `repo` IS the issue's repo and they agree by construction.)
            match issueRef with
            | Some ir when opts.Repo.IsSome && not (String.Equals(ir.Repo, repo, StringComparison.OrdinalIgnoreCase)) ->
                // No FSGG-PATHS verdict — the touch-set drift gate greps stdout for one, and a straddle
                // produces none; it exits non-zero and the gate reads that as the failure it is.
                eprint (
                    sprintf
                        "fsgg-coord-engine: verify-paths refuses to straddle a repo boundary — PR #%d in %s/%s vs the touch-set of %s/%s#%d, in another repo. The touch-set was NOT checked (a touch-set there says nothing about the files changed here). Name the PR's own issue with --issue, or drop --issue to resolve it from the branch."
                        pr
                        owner
                        repo
                        ir.Owner
                        ir.Repo
                        ir.Number
                )

                ExitNoVerdict
            | _ ->

            // The issue a PR implements: an explicit `--issue` (which bypasses the head-ref read entirely —
            // #322, an unreadable head ref must not drag down a run that named its issue), else its
            // `item/<n>-*` branch, else what it declares it closes.
            let resolveIssue () : Result<Ref option, Errors.IoError> =
                match issueRef with
                | Some ir -> Ok(Some ir)
                | None ->
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
                        "FSGG-PATHS SKIP — PR #%d is in %s/%s but implements %s/%s#%d, in another repo — a touch-set there says nothing about the files changed here."
                        pr
                        owner
                        repo
                        issue.Owner
                        issue.Repo
                        issue.Number

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

                // #419: when the id was DERIVED from a session that shares one id across every subagent, a
                // fan-out would hand N workers the same id — the exact collision ADR-0027 moved the lock off
                // the shared account to avoid. Warn to STDERR (stdout is the id itself), point at the mint
                // COMMAND, and offer NO literal to copy: a warning that named an example id is one agents
                // pattern-match on and paste, which is how #419 happened.
                match w.Provenance with
                | Identity.FromSharedSession _ ->
                    eprint
                        "fsgg-coord-engine: WARNING — this id was derived from a session that shares one id across every subagent, so a fan-out of workers would all draw it and collide on each other's locks (#419)."

                    eprint "  Give EACH worker a unique id (do NOT invent one):  eval \"$(fsgg-coord-engine whoami --mint)\""
                | _ -> ()

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

    // ---- #480: the repo a WORKER command scopes to — the one you are STANDING IN --------------------
    // `parseGitHubSlug` + `gitRemoteRepo` are defined above verify-paths (its #430 default reads the same
    // remote); only the worker-command wrapper lives here.

    /// The repo a worker command (`next`/`take`/`batch`/`who`) scopes to. An explicit `--repo` wins and is
    /// resolved through `resolveRepo`; otherwise the scope defaults to the checkout you are standing in
    /// (#480). Reconcilers (`ready`) do NOT call this — /check-board runs a bare `ready --all` to reconcile
    /// the WHOLE board, so narrowing it to the checkout would silently shrink the reconciler to one repo,
    /// trading this scope bug for a strictly worse one in the very tool that exists to catch it.
    let private scopedRepo (opts: Options) : string option =
        match opts.Repo with
        | Some r -> Some(resolveRepo r)
        | None -> gitRemoteRepo () |> Option.map resolveRepo

    /// Run an IO command. Every one goes through here so the token check, the transport lifetime, and the
    /// defect boundary are in one place.
    // ---- the plumbing commands (#418 board/item cache, case 10) ---------------------------------------
    //
    // These expose the resolver cache the corpus counts: `bootstrap` pays the two GraphQL points once and
    // day-caches the field/option id map; `board`/`field-id`/`option-id` read it back for ZERO; `item-id`
    // resolves an issue's board item id in ONE call and then keeps it forever. They are board-global — no
    // #480 repo scoping — because the board is one board and its ids are the same from any checkout.

    let bootstrapCmd (ctx: Context) (opts: Options) : int =
        // `--refresh` drops the day-cache so the next resolve is a real one — the remedy Snapshot.fs points
        // a worker at when the board schema changed under a warm cache.
        if opts.Fresh then
            Cache.dropBoardMap ctx.Owner ctx.Title

        match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
        | Error e -> fail e
        | Ok board ->
            printfn "bootstrapped board #%d '%s' in %s (%d fields)" board.Number board.Title board.Owner (Map.count board.Fields)
            ExitGreen

    let boardCmd (ctx: Context) : int =
        match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
        | Error e -> fail e
        | Ok board ->
            printfn "%s" (Board.boardToJson board)
            ExitGreen

    let fieldId (ctx: Context) (opts: Options) : int =
        match opts.Args with
        | [ field ] ->
            match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
            | Error e -> fail e
            | Ok board ->
                match Map.tryFind field board.Fields with
                | Some f ->
                    printfn "%s" f.Id
                    ExitGreen
                | None ->
                    eprint $"fsgg-coord-engine: no board field named '%s{field}'."
                    ExitError
        | _ ->
            eprint "fsgg-coord-engine: field-id takes <field>."
            ExitError

    let optionId (ctx: Context) (opts: Options) : int =
        match opts.Args with
        | [ field; option ] ->
            match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
            | Error e -> fail e
            | Ok board ->
                match Map.tryFind field board.Fields with
                | Some f ->
                    match f.Type with
                    | Board.SingleSelect options ->
                        match Map.tryFind option options with
                        | Some id ->
                            printfn "%s" id
                            ExitGreen
                        | None ->
                            eprint $"fsgg-coord-engine: field '%s{field}' has no option '%s{option}'."
                            ExitError
                    | _ ->
                        eprint $"fsgg-coord-engine: field '%s{field}' is not a single-select field."
                        ExitError
                | None ->
                    eprint $"fsgg-coord-engine: no board field named '%s{field}'."
                    ExitError
        | _ ->
            eprint "fsgg-coord-engine: option-id takes <field> <option>."
            ExitError

    let itemIdCmd (ctx: Context) (opts: Options) : int =
        match opts.Args with
        | [ refArg ] ->
            match parseRef ctx refArg with
            | Error msg ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | Ok ref ->
                match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
                | Error e -> fail e
                | Ok board ->
                    match Board.itemIdCached ctx.Transport board ref.Owner ref.Repo ref.Number with
                    | Error e -> fail e
                    | Ok(Some id) ->
                        printfn "%s" id
                        ExitGreen
                    // #421: a SUCCESSFUL read that found nothing is the only path that says "not on board".
                    // It is a definite answer, not a failure — but it is not an id, so it exits non-zero.
                    | Ok None ->
                        eprint $"fsgg-coord-engine: %s{ref.Short} is not an item on this board."
                        ExitError
        | _ ->
            eprint "fsgg-coord-engine: item-id takes <ref>."
            ExitError

    // ---- lint: the board-health gate (#496) -----------------------------------------------------------
    //
    // The rule whose absence let `lint` report `0 error(s)` over a DEAD queue. A Ready/Backlog item that
    // declares no schedulable touch-set is refused by `batch`/`take` — correctly, an undeclared touch-set
    // cannot be proven disjoint — and then sits on the board looking like work no worker can ever pick up.
    // NO-TOUCH-SET names the omission; BAD-TOUCH-SET names the item that DECLARED a touch-set the scheduler
    // cannot use (every token unmatchable). Both are the same condition — "no worker can ever pick this up"
    // — so both are errors. `Paths: none` is the deliberate out (an epic, a decision item), and it is the
    // whole point: "deliberately undeclared" must be sayable, or no gate can tell it from "somebody forgot".
    // A RECONCILER read: always fresh, never the cache.
    //
    // The epic ROLL-UP-graph rules (EPIC-*, DONE-STATUS-OPEN-ISSUE, the PR-probing EPIC-UNLINKED-CHILD) are
    // deferred to a later slice — they need the sub-issue graph + body-child-ref parsing this one does not.

    type private LintFinding =
        { Code: string
          Severity: string
          Id: string
          Short: string
          Status: string
          Url: string
          Detail: string }

    let private renderLintJson (findings: LintFinding list) : string =
        use stream = new MemoryStream()
        use w = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = false, SkipValidation = false))

        w.WriteStartArray()

        for f in findings do
            w.WriteStartObject()
            w.WriteString("code", f.Code)
            w.WriteString("severity", f.Severity)
            w.WriteString("id", f.Id)
            w.WriteString("status", f.Status)
            w.WriteString("url", f.Url)
            w.WriteString("detail", f.Detail)
            w.WriteEndObject()

        w.WriteEndArray()
        w.Flush()
        Text.Encoding.UTF8.GetString(stream.ToArray())

    // Strip the owner from a `owner/repo#n` ref → `repo#n` (bash's `sub("^[^/]+/";"")`), the form a finding
    // NAMES a ref in.
    let private shortRef (ref: string) =
        match ref.IndexOf '/' with
        | i when i >= 0 -> ref.Substring(i + 1)
        | _ -> ref

    let lint (ctx: Context) (opts: Options) : int =
        match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
        | Error e -> fail e
        | Ok board ->
            match Scan.board ctx.Transport Cache.Reconciling ctx.Owner ctx.Title board.Number with
            | Error e -> fail e
            | Ok rows ->
                let repoFilter = opts.Repo |> Option.map resolveRepo
                // A PR is not an item of work (#641), and `--repo` scopes the pass (short-ids resolved).
                let items =
                    rows
                    |> List.filter (fun r -> not r.IsPullRequest)
                    |> List.filter (fun r ->
                        match repoFilter with
                        | Some name -> String.Equals(r.Ref.Repo, name, StringComparison.OrdinalIgnoreCase)
                        | None -> true)

                let mk code severity (r: Scan.Row) detail =
                    { Code = code
                      Severity = severity
                      Id = $"%s{r.Ref.Owner}/%s{r.Ref.Repo}#%d{r.Ref.Number}"
                      Short = r.Ref.Short
                      Status = statusName r.Status
                      Url = $"https://github.com/%s{r.Ref.Owner}/%s{r.Ref.Repo}/issues/%d{r.Ref.Number}"
                      Detail = detail }

                // The schedulability rules (#496): a Ready/Backlog OPEN item no worker can pick up.
                let touchSetFindings (r: Scan.Row) (body: string) : LintFinding list =
                    if r.State = IssueState.Open && (r.Status = BoardStatus.Ready || r.Status = BoardStatus.Backlog) then
                        match TouchSet.parse body with
                        | Undeclared ->
                            [ mk
                                  "NO-TOUCH-SET"
                                  "error"
                                  r
                                  $"%s{statusName r.Status} but declares no `Paths:` — `batch`/`take` cannot schedule it, so no worker can ever pick it up. Declare a touch-set, or `Paths: none` if it genuinely has none (an epic, a decision item)." ]
                        | Declared tokens when
                            (not (List.isEmpty tokens))
                            && tokens
                               |> List.forall (function
                                   | Unmatchable _ -> true
                                   | Matchable _ -> false)
                            ->
                            let bad =
                                tokens
                                |> List.map (function
                                    | Unmatchable t -> t
                                    | Matchable t -> t)
                                |> String.concat ", "

                            [ mk
                                  "BAD-TOUCH-SET"
                                  "error"
                                  r
                                  $"%s{statusName r.Status}, and EVERY declared `Paths:` token is unmatchable: %s{bad}. A token that matches no file conflicts with nothing, so `batch` refuses it and no worker can ever pick this up — the item is as dead as one with no touch-set at all. Not a glob language: exact paths, directory prefixes, and a TRAILING `/**` or `/*`." ]
                        | _ -> []
                    else
                        []

                // The epic ROLL-UP-graph rules. Only epics pay the sub-issue read.
                let epicFindings (r: Scan.Row) (body: string) : Errors.IoResult<LintFinding list> =
                    match Reads.subIssues ctx.Transport r.Ref.Owner r.Ref.Repo r.Ref.Number with
                    | Error e -> Error e
                    | Ok graph ->
                        let visible = List.length graph.Children

                        let noChildren =
                            if r.State = IssueState.Open && graph.Total = 0 then
                                [ mk "EPIC-NO-CHILDREN" "error" r "open [epic] with zero sub-issues — nothing rolls up" ]
                            else
                                []

                        let truncated =
                            if graph.Total > visible then
                                [ mk
                                      "EPIC-CHILDREN-TRUNCATED"
                                      "error"
                                      r
                                      $"%d{graph.Total} sub-issues, only %d{visible} visible — cannot verify rollup" ]
                            else
                                []

                        let doneOpenChild =
                            if r.Status = BoardStatus.Done && graph.Children |> List.exists (fun c -> c.Open) then
                                let openRefs =
                                    graph.Children
                                    |> List.filter (fun c -> c.Open)
                                    |> List.map (fun c -> shortRef c.Ref)
                                    |> String.concat ", "

                                [ mk "EPIC-DONE-OPEN-CHILD" "error" r $"board says Done, but open child: %s{openRefs}" ]
                            else
                                []

                        // EPIC-UNLINKED-CHILD may only reason when the graph is WHOLE (Total == visible) — a
                        // truncated graph makes "this declared child is unlinked" a claim about a set already
                        // known to be short (#266); EPIC-CHILDREN-TRUNCATED covers that case instead.
                        let unlinkedResult =
                            if graph.Total = visible then
                                // The shared EPIC-UNLINKED-CHILD set (#485: same definition the `done --flip`
                                // rollup uses). Only reason when the graph is WHOLE (Total == visible).
                                match
                                    Done.bodyUnlinkedChildren
                                        ctx.Transport
                                        r.Ref.Owner
                                        r.Ref.Repo
                                        body
                                        (graph.Children |> List.map (fun c -> c.Ref))
                                with
                                | Error e -> Error e
                                | Ok [] -> Ok []
                                | Ok kept ->
                                    let named = kept |> List.map shortRef |> String.concat ", "

                                    Ok
                                        [ mk
                                              "EPIC-UNLINKED-CHILD"
                                              "error"
                                              r
                                              $"body declares child(ren) absent from the sub-issue graph, so rollup cannot see them: %s{named}" ]
                            else
                                Ok []

                        match unlinkedResult with
                        | Error e -> Error e
                        | Ok unlinked -> Ok(noChildren @ truncated @ doneOpenChild @ unlinked)

                // Per item, in rule order: touch-set rules, then a Done-but-open NOTE, then the epic rules
                // (only epics read their graph/body-child-refs). FAIL CLOSED (#266): an unreadable body or
                // graph fails the whole pass — a gate that could not read an item must not report it clean.
                let rec classify (acc: LintFinding list) (rows: Scan.Row list) : Errors.IoResult<LintFinding list> =
                    match rows with
                    | [] -> Ok acc
                    | r :: rest ->
                        let isEpic =
                            r.Title.IndexOf("[epic]", StringComparison.OrdinalIgnoreCase) >= 0

                        let isTouchSetCandidate =
                            r.State = IssueState.Open && (r.Status = BoardStatus.Ready || r.Status = BoardStatus.Backlog)

                        let doneOpenNote =
                            if r.Status = BoardStatus.Done && r.State = IssueState.Open then
                                [ mk "DONE-STATUS-OPEN-ISSUE" "note" r "board Status is Done but the issue is still open" ]
                            else
                                []

                        // One body read serves BOTH the touch-set rules and the epic body-child-refs.
                        let bodyNeeded = isTouchSetCandidate || isEpic

                        let bodyResult =
                            if bodyNeeded then
                                Reads.issueBody ctx.Transport r.Ref.Owner r.Ref.Repo r.Ref.Number
                            else
                                Ok ""

                        match bodyResult with
                        | Error e -> Error e
                        | Ok body ->
                            let tsFindings = touchSetFindings r body

                            let epicResult =
                                if isEpic then epicFindings r body else Ok []

                            match epicResult with
                            | Error e -> Error e
                            | Ok epic -> classify (acc @ tsFindings @ doneOpenNote @ epic) rest

                match classify [] items with
                | Error e -> fail e
                | Ok findings ->
                    let errors = findings |> List.filter (fun f -> f.Severity = "error") |> List.length
                    let notes = findings |> List.filter (fun f -> f.Severity = "note") |> List.length

                    match opts.Render with
                    | Json -> printfn "%s" (renderLintJson findings)
                    | Text ->
                        for f in findings do
                            printfn "FSGG-LINT %s  %s  %s  — %s" (f.Severity.ToUpperInvariant()) f.Code f.Short f.Detail

                        let repoSuffix =
                            match repoFilter with
                            | Some r -> $" for repo '%s{r}'"
                            | None -> ""

                        eprint $"fsgg-coord-engine: %d{errors} error(s), %d{notes} note(s)%s{repoSuffix}"

                    // A gate: any error fails it; `--strict` makes a note fatal too. (No note rule ships in
                    // this slice — the epic-graph rules that emit them are deferred — so `notes` is 0 here.)
                    if errors > 0 || (opts.Strict && notes > 0) then
                        ExitError
                    else
                        ExitGreen

    let run (opts: Options) : int =
        // #480: a WORKER command scopes to the repo you are standing in when no `--repo` spells it out; a
        // reconciler stays org-wide. `take` ACTS — it claims an item and prints a worktree command against
        // THIS checkout's origin — so an undetectable scope is a hard error, not a quiet fall-back to the
        // whole org, which is what once handed a `.github` worker another repo's item and a worktree
        // command that would have built it in the wrong repository.
        let opts =
            match opts.Command with
            | Next
            | BatchCmd
            | Who
            | Take -> { opts with Repo = scopedRepo opts }
            | _ -> opts

        match opts.Command with
        | Take when Option.isNone opts.Repo ->
            eprint
                "fsgg-coord-engine: take: --repo required (no git remote here, so 'the repo you are standing in' is undefined)."

            ExitError
        | _ ->

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
            | Overlap -> overlapCmd ctx opts
            | Say -> say ctx opts
            | DoneCmd -> doneCmd ctx opts
            | VerifyPaths -> verifyPaths ctx opts
            | Bootstrap -> bootstrapCmd ctx opts
            | BoardCmd -> boardCmd ctx
            | FieldId -> fieldId ctx opts
            | OptionId -> optionId ctx opts
            | ItemId -> itemIdCmd ctx opts
            | LintCmd -> lint ctx opts
            | other -> failwith $"Client.run received a non-IO command: %A{other}"
