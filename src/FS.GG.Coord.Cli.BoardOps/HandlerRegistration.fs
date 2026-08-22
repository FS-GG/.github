namespace FS.GG.Coord.Cli.BoardOps

open FS.GG.Coord.Cli
open FS.GG.Coord.Cli.Kernel

module HandlerRegistration =
    type Handler = Context -> Options.Options -> int

    let commands =
        Options.commandCatalogue
        |> List.choose (fun descriptor ->
            if descriptor.HandlerOwner = Options.BoardOps then Some descriptor.Command else None)

    let validate<'handler>
        (allCommands: Options.Command list)
        (registrations: (Options.Command * 'handler) list)
        =
        let grouped = registrations |> List.groupBy fst
        let duplicates = grouped |> List.choose (fun (command, entries) -> if entries.Length = 1 then None else Some command)
        let registered = grouped |> List.map fst |> Set.ofList
        let expected = allCommands |> Set.ofList
        let missing = Set.difference expected registered |> Set.toList
        let unexpected = Set.difference registered expected |> Set.toList
        let errors =
            [ if not duplicates.IsEmpty then yield $"duplicate handlers: %A{duplicates}"
              if not missing.IsEmpty then yield $"missing handlers: %A{missing}"
              if not unexpected.IsEmpty then yield $"unexpected handlers: %A{unexpected}" ]

        if errors.IsEmpty then Ok(Map.ofList registrations) else Error errors
