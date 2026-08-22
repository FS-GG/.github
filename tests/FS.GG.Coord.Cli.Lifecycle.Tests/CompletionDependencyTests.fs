namespace FS.GG.Coord.Cli.Lifecycle.Tests

open System.IO
open Xunit

module CompletionDependencyTests =
    let private repositoryRoot =
        Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))

    [<Fact>]
    let ``delivery completion is an explicit dependency with no mutable backpatch`` () =
        let client = File.ReadAllText(Path.Combine(repositoryRoot, "src", "FS.GG.Coord.Cli", "Client.fs"))
        let lifecycle = File.ReadAllText(Path.Combine(repositoryRoot, "src", "FS.GG.Coord.Cli.Lifecycle", "LiveHandlers.fs"))
        Assert.Contains("(completeDelivery: Context -> Options -> int)", lifecycle)
        Assert.DoesNotContain("let mutable private completeDelivery", client + lifecycle)
        Assert.DoesNotContain("delivery completion is not initialized", client + lifecycle)
        Assert.DoesNotContain("do completeDelivery <-", client + lifecycle)
