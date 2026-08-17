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

    // ---- #1646: WHO THIS PROCESS IS, as opposed to who it was told to be ----------------------------
    //
    // `Id` is what the caller asked to BE. `Derived` is what it IS — rules 2-4 with `--worker` taken away.
    // They differ on exactly one shape, and that shape was a working impersonation: `verifyHeld` opened the
    // door to `Held` when the marker's id matched ours and `twinSession` did not say twin, and under one
    // Claude Code session BOTH match for a caller that copied another worker's id off the board.
    //
    // The lock boundary asks the question; this module ANSWERS it, and these pin the answer. The refusal
    // itself is pinned in `FS.GG.Coord.GitHub.Tests.WriteTests` (#1646), where the capability lives.

    /// Run `f` with a named `CLAUDE_CODE_SESSION_ID`, restoring it afterwards. Same licence as
    /// `withWorkerEnv`: process-wide state, xUnit runs a class's tests sequentially, and the restore is
    /// what keeps that true for the next one.
    let private withSessionEnv (value: string option) (f: unit -> unit) =
        let key = "CLAUDE_CODE_SESSION_ID"
        let saved = System.Environment.GetEnvironmentVariable key

        try
            System.Environment.SetEnvironmentVariable(key, Option.toObj value)
            f ()
        finally
            System.Environment.SetEnvironmentVariable(key, saved)

    [<Fact>]
    let ``#1646 --worker naming ANOTHER worker still resolves, and carries this process's own id beside it`` () =
        // IT DOES NOT REFUSE HERE, and that is the design. `resolve` reports; the LOCK decides. A worker may
        // legitimately run read-only verbs under another id (`inbox --worker`, `whoami --worker`), and the
        // harm this closes is specifically taking somebody's LOCK — so the disagreement is carried to the
        // boundary that can weigh it, rather than turned into a blanket refusal here.
        withSessionEnv None (fun () ->
            withWorkerEnv (Some "kite-461") (fun () ->
                match Identity.resolve (Some "vole-418") with
                | Ok w ->
                    Assert.Equal("vole-418", w.Id)
                    Assert.Equal(Some "kite-461", w.Derived)
                | Error msg -> failwith $"--worker must still resolve — got: %s{msg}"))

    [<Fact>]
    let ``#1646 the ordinary worker AGREES with itself - Derived is the id in use`` () =
        // Every prescribed invocation: `eval "$(… whoami --mint)"` exports `$FSGG_WORKER`, and the verb runs
        // with no flag. `Derived` must equal `Id` here, or the common path would read as an impersonation.
        withSessionEnv None (fun () ->
            withWorkerEnv (Some "kite-461") (fun () ->
                match Identity.resolve None with
                | Ok w -> Assert.Equal(Some w.Id, w.Derived)
                | Error msg -> failwith $"the ordinary worker must resolve — got: %s{msg}"))

    [<Fact>]
    let ``#1646 --worker naming ITSELF agrees too - a script that spells its own id out loud is not impersonating`` () =
        // A fan-out that passes `--worker "$FSGG_WORKER"` rather than relying on the export is doing the
        // same thing more explicitly. The two spellings must resolve to the same identity, or the refusal
        // would fire on a caller that named itself correctly.
        withSessionEnv None (fun () ->
            withWorkerEnv (Some "kite-461") (fun () ->
                match Identity.resolve (Some "kite-461") with
                | Ok w -> Assert.Equal(Some "kite-461", w.Derived)
                | Error msg -> failwith $"naming yourself must resolve — got: %s{msg}"))

    [<Fact>]
    let ``#1646 a SHARED session id is still an identity to be measured against`` () =
        // THE PRECONDITION OF THE WHOLE ISSUE. A worker that never minted an id derives one from the shared
        // session — a poor identity (it names the SESSION, and `whoami` warns exactly that), but "poor
        // identity" and "no identity" are different. Excluding it would leave the hole wide open to any
        // caller that simply skipped the mint, which is the commonest way to arrive at a shared id at all.
        withWorkerEnv None (fun () ->
            withSessionEnv (Some "e0555648-5ee1-469a-b00e-760ffb555d41") (fun () ->
                match Identity.resolve (Some "vole-418") with
                | Ok w ->
                    Assert.Equal("vole-418", w.Id)
                    Assert.NotEqual(Some "vole-418", w.Derived)
                    Assert.True(w.Derived.IsSome, "a shared session still derives an id — it is the id the fan-out COLLIDES on")
                | Error msg -> failwith $"--worker over a shared session must resolve — got: %s{msg}"))

    [<Fact>]
    let ``#1646 a caller that derives NOTHING derives nothing - the human operator --worker exists for`` () =
        // No `$FSGG_WORKER`, no session: `--worker` is the ONLY way this caller can say who it is, so there
        // is nothing to measure it against. `None` is UNASKABLE, not "no impersonation here" — the residue
        // #1646 records rather than pretends away.
        withWorkerEnv None (fun () ->
            withSessionEnv None (fun () ->
                match Identity.resolve (Some "vole-418") with
                | Ok w ->
                    Assert.Equal("vole-418", w.Id)
                    Assert.Equal(None, w.Derived)
                | Error msg -> failwith $"a human operator must still resolve — got: %s{msg}"))

    [<Fact>]
    let ``#1646 a $FSGG_WORKER that slugs to NOTHING derives NOTHING - a malformed variable must not lock a worker out`` () =
        // #1070's input, asked of the NEW field. An empty id is not an identity — it is the one every
        // annihilating input shares — so deriving `""` would make it disagree with every `--worker` and read
        // as an impersonation. That would turn a malformed variable into a lockout, which is the opposite of
        // what this refusal is for. `resolve` still refuses that value when it is the id in USE (above).
        withSessionEnv None (fun () ->
            withWorkerEnv (Some "///") (fun () ->
                match Identity.resolve (Some "vole-418") with
                | Ok w -> Assert.Equal(None, w.Derived)
                | Error msg -> failwith $"a broken FSGG_WORKER must not refuse an explicit --worker — got: %s{msg}"))

    [<Fact>]
    let ``#1646 whoami reports the disagreement, and ONLY when there is one`` () =
        // `whoami` is where a worker checks its identity BEFORE a lock verb refuses it. A refusal this
        // report cannot reproduce is one the worker has to guess at — and a line printed on every ordinary
        // invocation is noise that trains people to skip the report.
        withSessionEnv None (fun () ->
            withWorkerEnv (Some "kite-461") (fun () ->
                let impersonating =
                    Identity.resolve (Some "vole-418") |> Result.defaultWith failwith |> Identity.explain

                let ordinary = Identity.resolve None |> Result.defaultWith failwith |> Identity.explain

                Assert.Contains(impersonating, fun (line: string) -> line.StartsWith "self: kite-461")
                Assert.DoesNotContain(ordinary, fun (line: string) -> line.StartsWith "self:")))
