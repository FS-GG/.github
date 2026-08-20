namespace FS.GG.Coord.Cli.BoardOps

open FS.GG.Coord.Cli
open FS.GG.Coord.Cli.Kernel

module HandlerRegistration =
    type Handler = Context -> Options.Options -> int

    type Implementations =
        { Add: Handler
          Flush: Handler
          SetField: Handler
          Child: Handler
          BodyEdits: Handler
          FieldId: Handler
          OptionId: Handler
          ItemId: Handler
          Board: Handler
          Bootstrap: Handler
          Issues: Handler
          Intake: Handler
          Say: Handler
          Inbox: Handler
          RoomOpen: Handler }

    let commands =
        [ Options.Add
          Options.Flush
          Options.SetField
          Options.Child
          Options.BodyEdits
          Options.FieldId
          Options.OptionId
          Options.ItemId
          Options.BoardCmd
          Options.Bootstrap
          Options.Issues
          Options.IntakeCmd
          Options.Say
          Options.Inbox
          Options.RoomOpen ]

    let handlers implementations =
        [ Options.Add, implementations.Add
          Options.Flush, implementations.Flush
          Options.SetField, implementations.SetField
          Options.Child, implementations.Child
          Options.BodyEdits, implementations.BodyEdits
          Options.FieldId, implementations.FieldId
          Options.OptionId, implementations.OptionId
          Options.ItemId, implementations.ItemId
          Options.BoardCmd, implementations.Board
          Options.Bootstrap, implementations.Bootstrap
          Options.Issues, implementations.Issues
          Options.IntakeCmd, implementations.Intake
          Options.Say, implementations.Say
          Options.Inbox, implementations.Inbox
          Options.RoomOpen, implementations.RoomOpen ]

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
