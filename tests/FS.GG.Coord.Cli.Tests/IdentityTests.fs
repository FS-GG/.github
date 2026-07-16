namespace FS.GG.Coord.Cli.Tests

open Xunit
open FS.GG.Coord.Cli

/// `Identity.slug` is the ONE normalization that turns a caller-supplied name into a worker id — the same
/// one that creates ids at `whoami`/`--worker` time. Its public contract exists so that any surface which
/// ADDRESSES a worker (`say --to`) can run its target through the identical rule (#485): if the addressing
/// slug ever drifted from the creation slug, a message would be sent to an id nobody holds and `inbox`,
/// which matches `.to` by EXACT string, would silently never deliver it.
module IdentityTests =

    [<Fact>]
    let ``a mis-cased id is lowered to the worker id it round-trips to`` () =
        // `Heron-B71` is what a human types; `heron-b71` is what the marker was created with.
        Assert.Equal("heron-b71", Identity.slug "Heron-B71")

    [<Fact>]
    let ``an already-canonical id is left untouched`` () =
        // The signal `say --to` uses to decide whether to WARN: slug x = x means nothing was normalized.
        Assert.Equal("heron-b71", Identity.slug "heron-b71")

    [<Fact>]
    let ``non-id punctuation collapses to a hyphen and edges are trimmed`` () =
        Assert.Equal("finch-a3f", Identity.slug "finch.a3f")
        Assert.Equal("finch-a3f", Identity.slug "  finch a3f  ")

    [<Fact>]
    let ``a target with no id characters at all slugs to empty`` () =
        // `say --to` reads this as "not a usable worker id" and refuses rather than address the empty string.
        Assert.Equal("", Identity.slug "!!!")
