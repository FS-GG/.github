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
        | [ baseSha; headSha; oldToken; newToken ] when not (List.isEmpty opts.Paths) ->
            let root = opts.Repo |> Option.defaultValue "."
            let occurrences =
                opts.Paths
                |> List.map (fun path ->
                    match git root $"show %s{baseSha}:%s{path}", git root $"show %s{headSha}:%s{path}" with
                    | Ok before, Ok after -> Ok(inventory path before after oldToken newToken)
                    | Error error, _ | _, Error error -> Error $"cannot audit %s{path}: %s{error.Trim()}")
                |> List.fold (fun state next -> Result.bind (fun all -> Result.map (fun xs -> all @ xs) next) state) (Ok [])
            match occurrences with
            | Error error -> eprintfn "fsgg-coord-engine: %s" error; 1
            | Ok rows ->
                let receipt = receipt root baseSha headSha opts.Paths (not (List.isEmpty rows)) rows
                printfn "{\"schemaVersion\":%d,\"repository\":\"%s\",\"baseSha\":\"%s\",\"headSha\":\"%s\",\"required\":%b,\"occurrenceCount\":%d,\"allResolved\":false}" receipt.SchemaVersion receipt.Repository receipt.BaseSha receipt.HeadSha receipt.Required receipt.Occurrences.Length
                0
        | _ -> eprintfn "fsgg-coord-engine: diff-audit needs <base> <head> <old-token> <new-token> and --paths P..."; 1
