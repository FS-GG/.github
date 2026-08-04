namespace FS.GG.Coord.Cli

open System
open System.Diagnostics
open FS.GG.Coord.SemanticDiff

module SemanticDiffApplication =
    let private git (root: string) (args: string) =
        let start: ProcessStartInfo = ProcessStartInfo("git", args)
        start.WorkingDirectory <- root
        start.RedirectStandardOutput <- true
        start.RedirectStandardError <- true
        use child = Process.Start start
        let output = child.StandardOutput.ReadToEnd()
        let error = child.StandardError.ReadToEnd()
        child.WaitForExit()
        if child.ExitCode = 0 then Ok output else Error error

    /// `diff-audit <base> <head> <old-token> <new-token> --paths P... [--repo ROOT]` reads each
    /// declared path from the two immutable commits. A failed git read is an error, never an empty audit.
    let run (opts: Options.Options) =
        match opts.Args with
        | [ baseSha; headSha; oldToken; newToken ]
        | [ baseSha; headSha; oldToken; newToken; _ ]
        | [ baseSha; headSha; oldToken; newToken; _; _ ] when not (List.isEmpty opts.Paths) ->
            // `--repo` is shared with board commands and normalizes a leading slash as a repository
            // spelling. Restore it only when that exact absolute directory exists; otherwise retain
            // the normal relative repository root.
            let requestedRoot = opts.Repo |> Option.defaultValue "."

            let root =
                if IO.Directory.Exists("/" + requestedRoot) then
                    "/" + requestedRoot
                else
                    requestedRoot

            let occurrences =
                opts.Paths
                |> List.map (fun path ->
                    match git root $"show %s{baseSha}:%s{path}", git root $"show %s{headSha}:%s{path}" with
                    | Ok before, Ok after -> Ok(inventory path before after oldToken newToken)
                    | Error error, _
                    | _, Error error -> Error $"cannot audit %s{path}: %s{error.Trim()}")
                |> List.fold
                    (fun state next -> Result.bind (fun all -> Result.map (fun xs -> all @ xs) next) state)
                    (Ok [])

            match occurrences with
            | Error error ->
                eprintfn "fsgg-coord-engine: %s" error
                1
            | Ok rows ->
                let threshold =
                    match Environment.GetEnvironmentVariable "FSGG_DIFF_AUDIT_THRESHOLD" with
                    | null
                    | "" -> 5
                    | value ->
                        match Int32.TryParse value with
                        | true, n when n >= 0 -> n
                        | _ -> -1

                let commitMessage =
                    git root $"log -1 --format=%%B %s{headSha}" |> Result.defaultValue ""
                // The item arm consumes a captured issue body and derives the declaration from its bytes;
                // caller process memory is not evidence.  The driver/skill owns fetching that body from the
                // named live item before invoking this local git-object command.
                let itemBody =
                    match opts.Args with
                    | [ _; _; _; _; _; itemBodyPath ] -> IO.File.ReadAllText itemBodyPath |> Some
                    | _ -> None

                if threshold < 0 then
                    eprintfn "fsgg-coord-engine: FSGG_DIFF_AUDIT_THRESHOLD must be a non-negative integer"
                    1
                else
                    let required = activationRequired threshold rows.Length commitMessage itemBody

                    let inventoried =
                        receipt root baseSha headSha oldToken newToken opts.Paths required rows

                    let resolved =
                        match opts.Args with
                        | [ _; _; _; _; "-"; _ ] -> Ok inventoried
                        | [ _; _; _; _; receiptPath ]
                        | [ _; _; _; _; receiptPath; _ ] ->
                            match ofJson (IO.File.ReadAllText receiptPath) with
                            | Error errors -> Error errors
                            | Ok supplied ->
                                let suppliedById =
                                    supplied.Occurrences |> List.map (fun row -> row.Id, row) |> Map.ofList

                                if
                                    supplied.BaseSha <> baseSha
                                    || supplied.HeadSha <> headSha
                                    || supplied.DeclaredPaths <> inventoried.DeclaredPaths
                                then
                                    Error [ "diff-audit disposition receipt is stale for this base/head/path scope" ]
                                elif
                                    supplied.Occurrences.Length <> suppliedById.Count
                                    || suppliedById.Count <> rows.Length
                                then
                                    Error [ "diff-audit dispositions are missing or duplicated" ]
                                else
                                    Ok
                                        { inventoried with
                                            Occurrences =
                                                rows
                                                |> List.map (fun row ->
                                                    match Map.tryFind row.Id suppliedById with
                                                    | Some supplied ->
                                                        { row with
                                                            Disposition = supplied.Disposition }
                                                    | None -> row) }
                        | _ -> Ok inventoried

                    match resolved with
                    | Error errors ->
                        errors |> List.iter (eprintfn "fsgg-coord-engine: %s")
                        1
                    | Ok result ->
                        printfn "%s" (toJson result)

                        if required && not (validate baseSha headSha result |> List.isEmpty) then
                            3
                        else
                            0
        | _ ->
            eprintfn
                "fsgg-coord-engine: diff-audit needs <base> <head> <old-token> <new-token> [receipt.json|-] [item-body.md] and --paths P..."

            1
