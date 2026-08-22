namespace FS.GG.Coord.Cli.Lifecycle.Tests

open Xunit
open FS.GG.Coord.Cli
open FS.GG.Coord.Cli.Lifecycle

module HandlerRegistrationTests =
    let private inert _ = 0

    let private dependencies: Handlers.Dependencies =
        { Delivery = inert
          Review = inert
          Route = inert
          Landable = inert
          Done = inert
          VerifyPaths = inert
          Followup = inert }

    [<Fact>]
    let ``Lifecycle registers every owned command exactly once`` () =
        let registrations = Handlers.handlers dependencies
        let result = HandlerRegistration.validate HandlerRegistration.commands registrations
        Assert.True(Result.isOk result)
        Assert.Equal(HandlerRegistration.commands.Length, registrations.Length)

    [<Fact>]
    let ``Lifecycle registration rejects duplicate missing and unexpected handlers`` () =
        let registrations = Handlers.handlers dependencies
        Assert.True(Result.isError (HandlerRegistration.validate HandlerRegistration.commands (registrations.Head :: registrations)))
        Assert.True(Result.isError (HandlerRegistration.validate HandlerRegistration.commands registrations.Tail))
        Assert.True(Result.isError (HandlerRegistration.validate HandlerRegistration.commands ((Options.Help, inert) :: registrations)))

    [<Fact>]
    let ``production composition owns every parsed command exactly once`` () =
        let registrations = Program.commandRegistrations
        let result = FS.GG.Coord.Cli.BoardOps.HandlerRegistration.validate Options.allCommands registrations
        Assert.True(Result.isOk result)
        Assert.Equal(Options.allCommands.Length, registrations.Length)
