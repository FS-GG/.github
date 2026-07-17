namespace FS.GG.Coord.Cli

open System
open System.IO
open System.Text
open FS.GG.Coord.Types
open FS.GG.Coord.GitHub
open FS.GG.Coord.Cli.Identity

module Followups =

    type Action =
        | Add of raw: string
        | Peek
        | Pop
        | List

    type Outcome =
        | Added of Ref
        | Head of Ref
        | Popped of Ref
        | Listed of Ref list
        | Empty
        | Refused of why: string
        | Unreadable of why: string

    // ---- parsing the action ------------------------------------------------------------------------

    /// The verbs, spelled once. A refusal names them, so the remedy is in the message that refused (#611).
    let private verbs = "add <ref> | peek | pop | list"

    let parse (args: string list) : Result<Action, string> =
        match args with
        | [ "add"; raw ] -> Ok(Add raw)
        | "add" :: [] -> Error $"followup add needs a ref (%s{verbs})."
        // NAMED, not swallowed. `followup add a b` is a caller who thinks they queued two things; a parser
        // that took the first and shrugged at the rest would be `Options`' own residue rule broken one
        // level down — an argument that is ignored is indistinguishable from one that was honoured.
        | "add" :: _ :: extra -> Error $"followup add takes exactly one ref (got %d{List.length extra + 1}). Queue them one at a time: %s{verbs}."
        | [ "peek" ] -> Ok Peek
        | [ "pop" ] -> Ok Pop
        | [ "list" ] -> Ok List
        | [] -> Error $"followup needs a subcommand: %s{verbs}."
        | other :: _ -> Error $"unknown followup subcommand '%s{other}' (%s{verbs})."

    // ---- the queue file ----------------------------------------------------------------------------

    let path (worker: Worker) : Result<string, string> =
        // AN ID THAT CANNOT NAME A WORKER MUST NOT NAME A QUEUE. `Identity.resolve` slugs `--worker`/
        // `$FSGG_WORKER` and returns Ok for a slug that trims to nothing — `--worker '///'` is `Ok ""` —
        // so without this every such caller shares ONE file, which is property 1 defeated by the id
        // itself. Refuse rather than key on it (#419's fail-closed read).
        if String.IsNullOrWhiteSpace worker.Id then
            Error
                "the resolved worker id is EMPTY, so it cannot key a queue — every worker with an empty id would share one file, which is the collision the per-worker path exists to prevent. Mint one: eval \"$(scripts/fsgg-coord whoami --mint)\"."
        else
            Ok(Path.Combine(Cache.root (), "followups", worker.Id + ".txt"))

    /// The STORED form — fully qualified, always.
    ///
    /// NOT `Ref.Short`, which renders `repo#n` and drops the owner. The queue is read back by a later
    /// process, whose `$FSGG_COORD_OWNER` is not guaranteed to be the one that wrote the line; storing the
    /// owner is what makes the entry mean the same thing then as now. This is the on-disk contract, so it
    /// gets a name rather than an inline interpolation.
    let private qualified (r: Ref) = $"%s{r.Owner}/%s{r.Repo}#%d{r.Number}"

    /// The owner a bare ref would be resolved against, matching `scan`'s default (`Program.fs`).
    let private owner () =
        match Environment.GetEnvironmentVariable "FSGG_COORD_OWNER" with
        | null
        | "" -> "FS-GG"
        | v -> v

    /// A raw token → a ref, REFUSING one that does not name its own repo.
    ///
    /// The two probes tell "bare" from "junk" WITHOUT a second copy of the ref grammar (#485): parsing with
    /// no default repo fails for both, parsing with one succeeds only for the bare form. `parseRefIn`'s own
    /// no-default refusal cannot be reused verbatim — it says "this is not a FS-GG checkout", which inside
    /// `.github` is FALSE, and #611's lesson is that a diagnostic naming the wrong cause sends the reader
    /// somewhere there is nothing to find. The cause here is not where you are standing; it is that the
    /// queue outlives where you are standing.
    let private parseQualified (raw: string) : Result<Ref, string> =
        let ownr = owner ()

        // Any repo will do — the result is DISCARDED. This probe only asks "would a default have rescued
        // it?", i.e. "is this the bare form?", and the answer never reaches the queue.
        let probe = ".github"

        match Client.parseRefIn ownr None raw, Client.parseRefIn ownr (Some probe) raw with
        | Ok r, _ -> Ok r
        | Error _, Ok _ ->
            Error
                $"'%s{raw}' is a BARE issue number, and a queued ref must name its repo. The queue outlives the worktree that wrote it — that is what it is FOR — so by the time this is popped, a bare number resolves against whatever checkout you are standing in THEN. It would not fail: every target repo's numbering sits inside %s{ownr}/.github's, so a bare number resolves onto a real, unrelated, usually-closed row. Name the repo: owner/repo#n, or repo#n."
        | Error msg, Error _ -> Error msg

    /// A stored line → a ref. A line that does not parse is a REFUSAL, never a skip.
    ///
    /// Silently dropping an unreadable entry would be this whole item's bug, rebuilt inside its own fix: a
    /// promise discarded by a machine nobody asked. Wedging the queue is the fail-closed answer — it names
    /// the file and the line, so the operator can repair the one thing that is wrong.
    let private parseLine (file: string) (line: string) : Result<Ref, string> =
        match parseQualified line with
        | Ok r -> Ok r
        | Error msg ->
            Error
                $"the queue's entry '%s{line}' is not a usable ref: %s{msg} It was not dropped — a follow-up you cannot read is not a follow-up you do not owe. Fix or delete the line in %s{file}."

    let private splitLines (text: string) =
        text.Split('\n')
        |> Array.map (fun l -> l.Trim())
        |> Array.filter (fun l -> l <> "")
        |> Array.toList

    // ---- the IO ------------------------------------------------------------------------------------

    /// Read the queue under an EXCLUSIVE handle, optionally rewriting it, in ONE open.
    ///
    /// `FileShare.None` is the atomicity (property 4): a concurrent `pop` gets an IOException and reports
    /// `Unreadable` rather than racing us to the same head. `f` returns the outcome and, when it wants the
    /// file rewritten, the lines to keep.
    let private withQueue (file: string) (f: string list -> Outcome * string list option) : Outcome =
        try
            use fs = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)

            let buf = Array.zeroCreate<byte> (int fs.Length)
            fs.ReadExactly(buf, 0, buf.Length)

            let lines = splitLines (Encoding.UTF8.GetString buf)
            let outcome, keep = f lines

            match keep with
            | None -> outcome
            | Some remaining ->
                fs.SetLength 0L
                fs.Position <- 0L

                if not (List.isEmpty remaining) then
                    let bytes = Encoding.UTF8.GetBytes(String.concat "\n" remaining + "\n")
                    fs.Write(bytes, 0, bytes.Length)

                fs.Flush()
                fs.Dispose()

                // UNLINK, RATHER THAN LEAVE A ZERO-BYTE FILE — `Cache.clearPending`'s rule, and its reason:
                // an empty file and an absent file are different facts, and "there is a queue and it is
                // empty" is a claim about state nobody made. When the last promise drains, the queue
                // ceases to exist.
                if List.isEmpty remaining then
                    try
                        File.Delete file
                    with _ ->
                        ()

                outcome
        with
        | :? FileNotFoundException
        | :? DirectoryNotFoundException ->
            // NO QUEUE IS NOT AN UNREADABLE QUEUE. You have never queued anything, or you drained it.
            Empty
        | :? IOException as e ->
            Unreadable $"could not open the follow-up queue %s{file}: %s{e.Message} Another `followup` may hold it — retry. This is NOT an empty queue (#266): it is a promise that may still be there."
        | :? UnauthorizedAccessException as e -> Unreadable $"could not open the follow-up queue %s{file}: %s{e.Message}"

    let private addTo (file: string) (r: Ref) : Outcome =
        try
            Directory.CreateDirectory(Path.GetDirectoryName file: string) |> ignore
            // Append under an exclusive handle, for property 4's reason on the write side.
            use fs = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.None)
            let bytes = Encoding.UTF8.GetBytes(qualified r + "\n")
            fs.Write(bytes, 0, bytes.Length)
            Added r
        with
        | :? IOException as e -> Unreadable $"could not write the follow-up queue %s{file}: %s{e.Message}"
        | :? UnauthorizedAccessException as e -> Unreadable $"could not write the follow-up queue %s{file}: %s{e.Message}"

    let apply (worker: Worker) (action: Action) : Outcome =
        match path worker with
        | Error why -> Refused why
        | Ok file ->

        match action with
        | Add raw ->
            match parseQualified raw with
            | Error why -> Refused why
            | Ok r -> addTo file r

        | Peek ->
            withQueue file (fun lines ->
                match lines with
                | [] -> Empty, None
                | head :: _ ->
                    match parseLine file head with
                    | Ok r -> Head r, None
                    | Error why -> Unreadable why, None)

        | Pop ->
            withQueue file (fun lines ->
                match lines with
                | [] -> Empty, None
                | head :: tail ->
                    match parseLine file head with
                    // The rewrite happens ONLY on a head we could read. A pop that cannot name what it
                    // removed is a promise deleted by a machine that never knew what it was.
                    | Ok r -> Popped r, Some tail
                    | Error why -> Unreadable why, None)

        | List ->
            withQueue file (fun lines ->
                match lines with
                | [] -> Empty, None
                | _ ->
                    let parsed = lines |> List.map (parseLine file)

                    match parsed |> List.tryPick (function
                              | Error why -> Some why
                              | Ok _ -> None) with
                    | Some why -> Unreadable why, None
                    | None ->
                        let refs =
                            parsed
                            |> List.choose (function
                                | Ok r -> Some r
                                | Error _ -> None)

                        Listed refs, None)

    // ---- the projection ----------------------------------------------------------------------------

    let render (outcome: Outcome) : string list * string list =
        match outcome with
        // STDOUT CARRIES THE REF AND NOTHING ELSE, so `followup pop` composes: the recipe's next step is
        // `widen <that ref>`, and a commentary line on stdout would be handed to it as a path.
        | Added r -> [ qualified r ], [ $"queued %s{qualified r} — you owe yourself this one." ]
        | Head r -> [ qualified r ], []
        | Popped r -> [ qualified r ], []
        | Listed refs -> refs |> List.map qualified, [ $"%d{List.length refs} follow-up(s) owed." ]
        | Empty -> [], [ "the follow-up queue is empty — nothing owed. Back to the board." ]
        | Refused why -> [], [ $"fsgg-coord-engine: %s{why}" ]
        | Unreadable why -> [], [ $"fsgg-coord-engine: %s{why}" ]

    let exitCode (outcome: Outcome) : int =
        match outcome with
        | Added _
        | Head _
        | Popped _
        | Listed _ -> Client.ExitGreen
        // EX_NONE, and `Client`'s own constant rather than a 5 typed here — "I looked and there is nothing"
        // has exactly one meaning across this engine, and `take` already owns it (#585).
        | Empty -> Client.ExitNone
        | Refused _
        | Unreadable _ -> Client.ExitError

    let run (opts: Options.Options) : int =
        match Identity.resolve opts.Worker with
        | Error msg ->
            Console.Error.WriteLine $"fsgg-coord-engine: %s{msg}"
            Client.ExitError
        | Ok worker ->
            match parse opts.Args with
            | Error msg ->
                Console.Error.WriteLine $"fsgg-coord-engine: %s{msg}"
                Client.ExitError
            | Ok action ->
                let outcome = apply worker action
                let out, err = render outcome

                for line in out do
                    Console.Out.WriteLine(line: string)

                for line in err do
                    Console.Error.WriteLine(line: string)

                exitCode outcome
