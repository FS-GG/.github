namespace FS.GG.Coord.Cli.Lifecycle

open FS.GG.Coord.Cli

module HandlerRegistration =
    type Handler = Options.Options -> int

    /// The lifecycle family owns both live and snapshot forms of delivery/review. Program-level
    /// handlers retain that distinction while this inventory makes ownership exhaustive.
    let commands =
        [ Options.DeliveryCmd
          Options.ReviewCmd
          Options.RouteCmd
          Options.Landable
          Options.DoneCmd
          Options.VerifyPaths
          Options.Followup ]

    let validate (expected: Options.Command list) (registrations: (Options.Command * Handler) list) =
        let expectedSet = Set.ofList expected

        let grouped =
            registrations
            |> List.groupBy fst
            |> List.map (fun (command, entries) -> command, List.length entries)
            |> Map.ofList

        let duplicates =
            grouped
            |> Map.toList
            |> List.choose (fun (command, count) ->
                if count > 1 then Some $"duplicate lifecycle handler for %A{command}" else None)

        let registeredSet = registrations |> List.map fst |> Set.ofList

        let missing =
            Set.difference expectedSet registeredSet
            |> Set.toList
            |> List.map (fun command -> $"missing lifecycle handler for %A{command}")

        let unexpected =
            Set.difference registeredSet expectedSet
            |> Set.toList
            |> List.map (fun command -> $"unexpected lifecycle handler for %A{command}")

        match duplicates @ missing @ unexpected with
        | [] -> registrations |> Map.ofList |> Ok
        | errors -> Error errors
