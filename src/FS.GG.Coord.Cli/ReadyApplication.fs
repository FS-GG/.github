namespace FS.GG.Coord.Cli

module ReadyApplication =

    open System
    open FS.GG.Coord.Types
    open FS.GG.Coord.GitHub
    open FS.GG.Coord.Cli.Options

    let select (repo: string option) (status: string option) (all: bool) (rows: Scan.Row list) : Scan.Scoped =
        let scoped =
            rows
            |> List.filter (fun row -> not row.IsPullRequest)
            |> Scan.scope repo

        let selected =
            scoped.Rows
            |> List.filter (fun row ->
                match status with
                | Some wanted ->
                    String.Equals(statusWireName row.Status, wanted, StringComparison.OrdinalIgnoreCase)
                | None -> all || row.Status <> BoardStatus.Done)

        { scoped with Rows = selected }
