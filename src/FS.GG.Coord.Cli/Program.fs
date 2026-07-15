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
let private renderText (leaseMinutes: int) (decision: Verdict<Batch.BatchResult>) =
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
                eprint $"  %s{Batch.explainDecision leaseMinutes d}"

        if result.Truncated then
            eprint "note: the batch was capped, so the candidates after the last one chosen were never evaluated."

let private readInput (opts: Options) =
    match opts.SnapshotFile with
    | Some path -> File.ReadAllText path
    | None -> Console.In.ReadToEnd()

/// THE PROTOCOL, EMITTED (ADR-0034 §4.5). It reads nothing and decides nothing — it states what the
/// engine already enforces, so `scripts/generate-projections` can render the canonical doc and the four
/// `SKILL.md` bodies FROM it rather than from somebody's memory of it.
///
/// This is the inversion the design doc asks for. `fsgg-coord` was always the model; it just was not the
/// SOURCE, so every rule was re-typed into six documents and byte-copied into six repos — 54 vendored
/// copies, and a propagation edge that cost a second issue and a second PR every single time. Now the
/// rule exists once, and the prose is a build artifact.
let private facts (opts: Options) =
    match opts.Render with
    | Json -> printfn "%s" (Snapshot.renderFacts Protocol.rules Protocol.verdicts)
    | Text ->
        for r in Protocol.rules do
            printfn "## %s" r.Title
            printfn ""
            printfn "%s" r.Statement
            printfn ""
            printfn "> %s" r.Because
            printfn ""

        printfn "## The verdicts — what a worker can be told, and nothing else"
        printfn ""

        for v in Protocol.verdicts do
            printfn "- **%s** — %s" v.Kind v.Meaning

    ExitGreen

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

/// Partition the board into lanes (#428). Reads the SAME snapshot `decide` does, so lanes and the
/// scheduler can never disagree about which items exist or what they reserve — that divergence is #485
/// (one question, five implementations, agreeing in none) and it is not being rebuilt here.
let private lanes (opts: Options) =
    let json = readInput opts

    if String.IsNullOrWhiteSpace json then
        eprint "fsgg-coord-engine: the snapshot is empty. That is a failed read, not an empty board — refusing to decide."
        ExitError
    else

    match Snapshot.parse json with
    | Error errors ->
        eprint "fsgg-coord-engine: the snapshot is malformed, so no partition was computed:"

        for e in errors do
            eprint $"  %s{e.Path}: %s{e.Message}"

        ExitError

    | Ok request ->
        let items = request.Candidates |> List.map (fun c -> c.Item)
        let partition = Lanes.partition items

        // STARTABILITY IS THE SCHEDULER'S CALL, NOT THE LANE MODULE'S. `Lanes.free` takes the predicate
        // rather than deriving one, so "is this item startable?" keeps exactly one implementation.
        let startable (item: Item) =
            Schedulability.schedulable request.AllowBacklog [] item = Schedulability.Startable

        match opts.Render with
        | Json -> printfn "%s" (Snapshot.renderLanes startable partition)
        | Text ->
            for line in Lanes.explain startable partition do
                printfn "%s" line

        ExitGreen

/// THE ONE COMMAND THAT PERFORMS IO — and the thing ADR-0034 said the IO layer was FOR.
///
/// > `FS.GG.Coord.GitHub` … is required only for the Phase 3 flip, **when the engine must fetch its own
/// > state**.
///
/// Until now it could not. `decide` reads a snapshot on stdin, and bash was the only thing that could
/// produce one — so the typed engine was a decision procedure with no way to observe the thing it decides
/// about, which is precisely why bash could not be deleted. `scan | decide` is now a complete scheduling
/// pass with no bash anywhere in it.
///
/// IT FAILS CLOSED, EVERYWHERE. A board it cannot read is not an empty board (#344); a marker set it cannot
/// read is not an unheld item (#461); an exhausted budget is not an absence (#421). Every one of those is an
/// `Error` here and a non-zero exit — never an empty snapshot, which `decide` would faithfully report as
/// "nothing schedulable" over a queue full of work.
let private scan (opts: Options) =
    let env name fallback =
        match Environment.GetEnvironmentVariable(name: string) with
        | null
        | "" -> fallback
        | v -> v

    let owner = env "FSGG_COORD_OWNER" "FS-GG"
    let title = env "FSGG_COORD_PROJECT" "Coordination"

    // The token is REQUIRED, and its absence is said out loud. An unauthenticated read of an org Projects v2
    // board does not fail cleanly — it returns `organization: null`, which would walk straight into a
    // "malformed response" that tells the operator nothing about the actual cause.
    let token =
        match env "GITHUB_TOKEN" (env "GH_TOKEN" "") with
        | "" -> None
        | t -> Some t

    match token with
    | None ->
        eprint
            "fsgg-coord-engine: scan needs a GitHub token ($GITHUB_TOKEN or $GH_TOKEN). An unauthenticated read of an org board does not fail cleanly — it returns an empty organization, and an empty board is exactly the answer this engine exists to refuse to invent."

        ExitError

    | Some token ->

    use transport =
        new GitHub.Transport.HttpTransport(GitHub.Transport.apiBaseFromEnv (), token)

    let github = transport :> GitHub.Transport.IGitHubTransport

    // SCHEDULING vs RECONCILING is a TYPE (`Cache.ReadIntent`), and `scan` is a SCHEDULING read: it may be
    // served a stale board, because the worst a stale scan can do is offer an item somebody just claimed —
    // and the claim CAS, which reads markers over REST and never from this cache, is what actually decides
    // who holds it. Staleness costs a retry; it cannot cost a double-claim. `--fresh` opts out.
    let intent =
        if opts.Fresh then
            GitHub.Cache.Reconciling
        else
            GitHub.Cache.Scheduling

    let result =
        GitHub.Board.bootstrap github owner title
        |> Result.bind (fun board -> GitHub.Scan.board github intent owner title board.Number)
        |> Result.bind (fun rows ->
            GitHub.Scan.snapshot github rows opts.Repo opts.AllowBacklog opts.Limit opts.LeaseMinutes)

    match result with
    | Error e ->
        eprint $"fsgg-coord-engine: scan: %s{GitHub.Errors.explain e}"

        // THE EXIT CODE IS THE BACK-OFF SIGNAL. `EX_RATE` (75) means "try again later", and a caller that
        // saw a generic 1 would treat a temporary condition as a permanent one — retrying a budget failure
        // three times just spends three more calls confirming the same 403.
        GitHub.Errors.exitCode e

    | Ok(document, receipt) ->
        match opts.Render with
        | Json -> printfn "%s" document
        | Text ->
            // The DOCUMENT still goes to stdout — it is the contract, and `scan --text | decide` must keep
            // working. The receipt is commentary, and commentary goes to stderr.
            printfn "%s" document

        // WHAT THE SCAN COULD NOT DO IS SAID, NOT IMPLIED. A number that only ever reports what it looked at
        // is how "we agreed" and "we never checked" come to print the same sentence — which is the sentence
        // ADR-0034 opens with.
        eprint
            $"scan: %d{receipt.Candidates} candidate(s); %d{receipt.OffBoardResolved} off-board blocker(s) resolved"

        if receipt.OffBoardSkipped > 0 then
            // THE CAP IS ANNOUNCED, NEVER SILENT. A silent cap leaves the overflow blocked-forever with no
            // trace — reported blocked by something nobody looked up, which is indistinguishable from being
            // genuinely blocked.
            eprint
                $"scan: WARNING — %d{receipt.OffBoardSkipped} off-board blocker(s) were NOT resolved (cap: %d{GitHub.Scan.OffBoardCap}). They are reported UNKNOWN, which BLOCKS. This is a cap, not a verdict."

        if receipt.BodiesUnreadable > 0 then
            eprint
                $"scan: WARNING — %d{receipt.BodiesUnreadable} candidate body(ies) could not be read. They carry `bodyUnreadable` and will be UNDETERMINED, never 'no touch-set declared'."

        ExitGreen

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
        | Json -> printfn "%s" (Snapshot.render request.LeaseMinutes request.Candidates decision)
        | Text -> renderText request.LeaseMinutes decision

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

            | Scan -> scan opts

            | Decide -> decide opts

            | FleetVerdict -> fleet opts

            | LanesView -> lanes opts

            | Facts -> facts opts

            // `whoami` reads no board — identity is local, and `--mint` needs no token.
            | WhoAmI -> Client.whoami opts

            // The client command surface — the shim's targets. Each reads/writes GitHub through the typed
            // IO layer; `Client.run` owns the token check, the transport lifetime, and the exit contract.
            | Next
            | BatchCmd
            | Ready
            | Who
            | Budget
            | Claim
            | Take
            | Release
            | Heartbeat
            | SetField
            | Child
            | Widen
            | Overlap
            | Say
            | DoneCmd
            | VerifyPaths
            | Bootstrap
            | BoardCmd
            | FieldId
            | OptionId
            | ItemId
            | LintCmd -> Client.run opts

    with e ->
        // A DEFECT IS ITS OWN EXIT CODE, and it is not `1`. The client must be able to tell "the engine
        // is broken" from "the caller is wrong" — because the first means the shadow is untrustworthy
        // and the second means the snapshot is. Collapsing them would hide a broken engine behind a
        // stream of what look like bad inputs.
        eprint $"fsgg-coord-engine: DEFECT — %s{e.GetType().Name}: %s{e.Message}"
        eprint (string e.StackTrace)
        ExitDefect
