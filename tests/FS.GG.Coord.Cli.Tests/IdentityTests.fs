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

    // ---- resolve checks the SLUG, not the argument (#1070) -------------------------------------------
    //
    // THE FACT DIRECTLY ABOVE WAS ALREADY KNOWN, AND NOBODY ASKED `resolve` ABOUT IT. `slug "!!!" = ""` was
    // pinned for `say --to`'s benefit — the addressing surface, which refuses an id that slugs to nothing.
    // The CREATION surface asked the same question of the wrong value: it guarded the INPUT
    // (`IsNullOrWhiteSpace`) and returned the OUTPUT of `slug`, which nothing re-read. `.` is not
    // whitespace, so it passed, and `resolve` answered Ok with an EMPTY id — every worker whose id
    // annihilates getting the same one. That is rule 4's shared id (#419's collapse), manufactured by the
    // resolver rather than invented by an agent, and `whoami` reported it without a warning (#266).

    /// Run `f` with `FSGG_WORKER` set, and put the environment back whatever happens. Process-wide state:
    /// nothing else in this assembly reads this variable, and xUnit runs a class's tests sequentially, so
    /// the restore is what keeps that true for the next test rather than a lock.
    let private withWorkerEnv (value: string option) (f: unit -> unit) =
        let key = "FSGG_WORKER"
        let saved = System.Environment.GetEnvironmentVariable key

        try
            System.Environment.SetEnvironmentVariable(key, Option.toObj value)
            f ()
        finally
            System.Environment.SetEnvironmentVariable(key, saved)

    [<Fact>]
    let ``#1070 an id that slugs to NOTHING is REFUSED, not resolved to the empty id`` () =
        // The regression: Ok, exit 0, empty id, no warning. An empty id is one every caller whose input
        // annihilates SHARES — the exact fan-out collapse rule 4 refuses to invent.
        match Identity.resolve (Some "///") with
        | Error _ -> ()
        | Ok w -> failwith $"an id that slugs to nothing must be refused — got Ok with Id = '%s{w.Id}'"

    [<Fact>]
    let ``#1070 the refusal NAMES the offending input, so the reader is not sent to look at a variable they did set`` () =
        // #611's rule: a diagnostic that names the wrong cause sends the reader somewhere there is nothing
        // to find. "could not derive a worker id" is TRUE here and useless — the flag was passed.
        match Identity.resolve (Some "///") with
        | Error msg ->
            Assert.Contains("///", msg)
            Assert.Contains("--worker", msg)
            // ...and offers the mint, as rule 4's own refusal does. A COMMAND, never a literal id (#551).
            Assert.Contains("whoami --mint", msg)
        | Ok _ -> failwith "expected a refusal"

    [<Fact>]
    let ``#1070 the boundary - an id with ONE id character still resolves`` () =
        // The rule is "slugs to nothing", not "looks like punctuation". `-x-` trims to `x`, which is a
        // usable id; refusing it would lock out a worker over an id that is merely ugly.
        match Identity.resolve (Some "-x-") with
        | Ok w -> Assert.Equal("x", w.Id)
        | Error msg -> failwith $"a slug with an id character must resolve — got: %s{msg}"

    [<Fact>]
    let ``#1070 $FSGG_WORKER is the REACHABLE branch - it is checked too, and named as itself`` () =
        // The env branch is the one a fan-out uses ("set FSGG_WORKER per worker"), and a launcher building
        // one by interpolation is how `.` gets there — one bug giving the WHOLE fleet one id.
        withWorkerEnv (Some ".") (fun () ->
            match Identity.resolve None with
            | Error msg ->
                Assert.Contains("$FSGG_WORKER", msg)
                Assert.Contains("'.'", msg)
            | Ok w -> failwith $"$FSGG_WORKER='.' must be refused — got Ok with Id = '%s{w.Id}'")

    [<Fact>]
    let ``#1070 a REFUSAL, not a fallback - it must not quietly derive from the session instead`` () =
        // The tempting repair, and it trades one shared id for another: on Claude Code every subagent
        // shares one session id, so falling through would re-introduce #419 by a different door AND leave
        // `whoami` reporting success over an identity that cannot hold a lock.
        let key = "CLAUDE_CODE_SESSION_ID"
        let saved = System.Environment.GetEnvironmentVariable key

        try
            System.Environment.SetEnvironmentVariable(key, "e0555648-5ee1-469a-b00e-760ffb555d41")

            withWorkerEnv (Some "...") (fun () ->
                match Identity.resolve None with
                | Error _ -> ()
                | Ok w ->
                    failwith $"a broken id must REFUSE, never fall back to the shared session id — got '%s{w.Id}'")
        finally
            System.Environment.SetEnvironmentVariable(key, saved)

    [<Fact>]
    let ``#1070 a legitimate $FSGG_WORKER is untouched by the guard`` () =
        withWorkerEnv (Some "Merlin-C402") (fun () ->
            match Identity.resolve None with
            | Ok w ->
                Assert.Equal("merlin-c402", w.Id)
                Assert.Equal(Identity.FromEnv "FSGG_WORKER", w.Provenance)
            | Error msg -> failwith $"a real id must still resolve — got: %s{msg}")
