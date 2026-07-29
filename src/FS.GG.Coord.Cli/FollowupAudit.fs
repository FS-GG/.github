namespace FS.GG.Coord.Cli

open System
open System.IO
open System.Text
open FS.GG.Coord.Cli.Identity
open FS.GG.Coord.GitHub

/// The one local observation shared by the terminal `done` boundary and the follow-up verb.
module FollowupAudit =

    type Outcome =
        | Empty
        | Owed of count: int
        | Unreadable of why: string

    let path (worker: Worker) : Result<string, string> =
        if String.IsNullOrWhiteSpace worker.Id then
            Error
                "the resolved worker id is EMPTY, so it cannot key a queue — every worker with an empty id would share one file, which is the collision the per-worker path exists to prevent. Mint one: eval \"$(scripts/fsgg-coord whoami --mint)\"."
        else
            Ok(Path.Combine(Cache.root (), "followups", worker.Id + ".txt"))

    let private countLines (text: string) =
        text.Split '\n'
        |> Array.map (fun line -> line.Trim())
        |> Array.filter (fun line -> line <> "")
        |> Array.length

    /// A read-only exclusive open preserves the queue while making contention a distinct, fail-closed fact.
    let inspect (worker: Worker) : Outcome =
        match path worker with
        | Error why -> Unreadable why
        | Ok file ->
            try
                use stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None)
                let bytes = Array.zeroCreate<byte> (int stream.Length)
                stream.ReadExactly(bytes, 0, bytes.Length)

                match countLines (Encoding.UTF8.GetString bytes) with
                | 0 -> Empty
                | count -> Owed count
            with
            | :? FileNotFoundException
            | :? DirectoryNotFoundException -> Empty
            | :? IOException as error ->
                Unreadable $"could not open the follow-up queue %s{file}: %s{error.Message} Another `followup` may hold it — retry. This is NOT an empty queue: it is a promise that may still be there."
            | :? UnauthorizedAccessException as error ->
                Unreadable $"could not open the follow-up queue %s{file}: %s{error.Message}"
