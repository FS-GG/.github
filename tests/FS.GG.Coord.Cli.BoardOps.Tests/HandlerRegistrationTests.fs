namespace FS.GG.Coord.Cli.BoardOps.Tests

open Xunit
open FS.GG.Coord.Cli
open FS.GG.Coord.Cli.BoardOps

module HandlerRegistrationTests =
    let private inert _ _ = 0

    let private implementations: HandlerRegistration.Implementations =
        { Add = inert
          Flush = inert
          SetField = inert
          Child = inert
          BodyEdits = inert
          FieldId = inert
          OptionId = inert
          ItemId = inert
          Board = inert
          Bootstrap = inert
          Issues = inert
          Intake = inert
          Say = inert
          Inbox = inert
          RoomOpen = inert }

    [<Fact>]
    let ``BoardOps registers each owned command exactly once`` () =
        let registrations = HandlerRegistration.handlers implementations
        let result = HandlerRegistration.validate HandlerRegistration.commands registrations
        Assert.True(Result.isOk result)
        Assert.Equal(HandlerRegistration.commands.Length, registrations.Length)

    [<Fact>]
    let ``registration validation rejects a duplicate handler`` () =
        let registrations = HandlerRegistration.handlers implementations
        let result = HandlerRegistration.validate HandlerRegistration.commands (registrations.Head :: registrations)
        Assert.True(Result.isError result)

    [<Fact>]
    let ``registration validation rejects a missing handler`` () =
        let registrations = HandlerRegistration.handlers implementations |> List.tail
        let result = HandlerRegistration.validate HandlerRegistration.commands registrations
        Assert.True(Result.isError result)

    [<Fact>]
    let ``registration validation rejects an unexpected handler`` () =
        let registrations = HandlerRegistration.handlers implementations
        let unexpected = Options.Help, inert
        let result = HandlerRegistration.validate HandlerRegistration.commands (unexpected :: registrations)
        Assert.True(Result.isError result)

    [<Fact>]
    let ``production producers agree with every command union case exactly once`` () =
        let registrations = Program.commandRegistrations
        let result = HandlerRegistration.validate Options.allCommands registrations
        Assert.True(Result.isOk result)
        Assert.Equal(Options.allCommands.Length, registrations.Length)

    [<Fact>]
    let ``production inventory rejects a parsed command with no registered producer`` () =
        let registrations =
            Program.commandRegistrations
            |> List.filter (fst >> ((<>) Options.Help))

        let result = HandlerRegistration.validate Options.allCommands registrations

        match result with
        | Error errors -> Assert.Contains("missing handlers: [Help]", errors)
        | Ok _ -> failwith "a parsed command with no production registration was admitted"
