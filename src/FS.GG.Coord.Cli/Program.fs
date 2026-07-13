module FS.GG.Coord.Cli.Program

open System
open System.IO
open System.Reflection
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.Cli
open FS.GG.Coord.Cli.Options

[<Literal>]
let private ExitGreen = 0

[<Literal>]
let private ExitError = 1

[<Literal>]
let private ExitDefect = 2

[<Literal>]
let private ExitRed = 3

[<Literal>]
let private ExitNoVerdict = 4

let private eprint (s: string) = Console.Error.WriteLine(s: string)

/// The `--text` projection. A rendering of the SAME answer the JSON carries — it may not add a fact
/// the contract lacks, and nothing may parse it.
let private renderText (decision: Verdict<Batch.BatchResult>) =
    match decision with
    | Red reasons ->
        eprint "REFUSED — the batch cannot be scheduled:"

        for r in reasons do
            eprint $"  %s{r}"

    | NoVerdict reason -> eprint $"UNDETERMINED — %s{reason}"

    | Green result ->
        if List.isEmpty result.Chosen then
            printfn "nothing schedulable right now."
        else
            printfn "schedulable in parallel (%d):" (List.length result.Chosen)

            for item in result.Chosen do
                printfn "  → %s" item.Ref.Short

        // The passed-over reasons go to stderr, and they ARE the answer to "why is there nothing to
        // do". A queue that shrinks without explanation is #440: `take` reported "no schedulable
        // item" over a board full of work, and the worker went home.
        let passed =
            result.Decisions
            |> List.filter (fun d -> d.Result <> Schedulability.Startable)

        if not (List.isEmpty passed) then
            eprint "passed over:"

            for d in passed do
                eprint $"  %s{Schedulability.explain d.Item d.Result}"

        if result.Truncated then
            eprint "note: the batch was capped, so the candidates after the last one chosen were never evaluated."

let private readInput (opts: Options) =
    match opts.SnapshotFile with
    | Some path -> File.ReadAllText path
    | None -> Console.In.ReadToEnd()

/// Fold the fleet divergence ledger into the cut-over verdict (#634).
///
/// The exit code IS the gate. `no-verdict` is 4 and `red` is 3 — neither is 0, so a caller that only
/// checks `if engine fleet; then flip; fi` cannot flip on an empty ledger, a one-worker ledger, or a
/// ledger full of somebody else's engine build. That is the whole point: the criterion had no
/// implementation at all, so it could only ever be met by a human deciding it had been.
let private fleet (opts: Options) =
    let json = readInput opts

    if String.IsNullOrWhiteSpace json then
        // An EMPTY document is not an empty ledger. The client is meant to hand us what it read; if it
        // handed us nothing, we did not observe a fleet that never diverged — we failed to observe
        // anything, and the difference between those two is this entire module.
        eprint
            "fsgg-coord-engine: the ledger document is empty. That is a failed read, not an empty ledger — refusing to decide."

        ExitError
    else

    match Fleet.parse json with
    | Error errors ->
        eprint "fsgg-coord-engine: the ledger is malformed, so no verdict was reached:"

        for e in errors do
            eprint $"  %s{e.Path}: %s{e.Message}"

        ExitError

    | Ok query ->
        let verdict =
            Divergence.evaluate query.Engine query.RequiredDays query.MinWorkers query.Today query.Reports

        match opts.Render with
        | Json -> printfn "%s" (Fleet.render verdict)
        | Text ->
            for line in Divergence.explain verdict do
                match verdict with
                | Green _ -> printfn "%s" line
                | _ -> eprint line

        match verdict with
        | Green _ -> ExitGreen
        | Red _ -> ExitRed
        | NoVerdict _ -> ExitNoVerdict

let private decide (opts: Options) =
    let json = readInput opts

    if String.IsNullOrWhiteSpace json then
        // An EMPTY snapshot is not an empty board. The client is meant to hand us the state it read;
        // if it handed us nothing, we did not observe an empty queue — we failed to observe anything.
        // Deciding "nothing is schedulable" from that is the exact substitution this engine exists to
        // make impossible.
        eprint "fsgg-coord-engine: the snapshot is empty. That is a failed read, not an empty board — refusing to decide."
        ExitError
    else

    match Snapshot.parse json with
    | Error errors ->
        eprint "fsgg-coord-engine: the snapshot is malformed, so no decision was reached:"

        for e in errors do
            eprint $"  %s{e.Path}: %s{e.Message}"

        ExitError

    | Ok request ->
        let decision =
            Batch.schedule
                request.AllowBacklog
                request.Limit
                request.InFlight
                (request.Candidates |> List.map (fun c -> c.Item))

        match opts.Render with
        | Json -> printfn "%s" (Snapshot.render request.Candidates decision)
        | Text -> renderText decision

        match decision with
        | Green _ -> ExitGreen
        | Red _ -> ExitRed
        | NoVerdict _ -> ExitNoVerdict

[<EntryPoint>]
let main argv =
    try
        match Options.parse (List.ofArray argv) with
        | Error message ->
            eprint $"fsgg-coord-engine: %s{message}"
            eprint ""
            eprint Options.usage
            ExitError

        | Ok opts ->
            match opts.Command with
            | Help ->
                printfn "%s" Options.usage
                ExitGreen

            | Version ->
                let v =
                    Assembly.GetExecutingAssembly().GetName().Version
                    |> Option.ofObj
                    |> Option.map string
                    |> Option.defaultValue "0.0.0"

                printfn "%s" v
                ExitGreen

            | Decide -> decide opts

            | FleetVerdict -> fleet opts

    with e ->
        // A DEFECT IS ITS OWN EXIT CODE, and it is not `1`. The client must be able to tell "the engine
        // is broken" from "the caller is wrong" — because the first means the shadow is untrustworthy
        // and the second means the snapshot is. Collapsing them would hide a broken engine behind a
        // stream of what look like bad inputs.
        eprint $"fsgg-coord-engine: DEFECT — %s{e.GetType().Name}: %s{e.Message}"
        eprint (string e.StackTrace)
        ExitDefect
