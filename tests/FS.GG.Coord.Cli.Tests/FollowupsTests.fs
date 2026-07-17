namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Threading.Tasks
open Xunit
open FS.GG.Coord.Types
open FS.GG.Coord.Cli
open FS.GG.Coord.Cli.Followups

/// THE FOLLOW-UP QUEUE'S LEGS — the ones the recipe could not have (#1063).
///
/// Every assertion here corresponds to a way #1061's ten lines of shell were wrong, and the reason this
/// file exists is that not one of them could have been written against the shell: nothing executes a
/// recipe, so nothing tests one. The four bugs were found by a review reading prose, and fixed by hand.
/// A one-off manual drive is not a gate — this is.
///
/// The queue is redirected with `FSGG_COORD_CACHE`, the SAME env var every other cache assertion in the
/// corpus isolates with (`Cache.root`). That is deliberate and is part of the design being tested: a new
/// env var would be a second thing to remember to set, and the fixture that forgot it would be measuring
/// the developer's real queue.
module FollowupsTests =

    /// A worker id is `Identity.resolve`'s output, so build it the way the engine does rather than by
    /// hand — a literal record here would let the id and the resolver drift.
    let private workerNamed (id: string) =
        match Identity.resolve (Some id) with
        | Ok w -> w
        | Error e -> failwith $"the fixture's own worker id did not resolve: %s{e}"

    /// Run `f` against a THROWAWAY cache root.
    ///
    /// `FSGG_COORD_CACHE` is process-global, so this is only safe because `AssemblyInfo.fs` disables
    /// xUnit's cross-class parallelism — the assembly-wide guard the sibling `FS.GG.Coord.GitHub.Tests`
    /// has carried for the same reason. A `lock` here would be a class-scoped answer to a process-scoped
    /// hazard: it would serialise this class against itself and do nothing about the next class that
    /// stands up a cache dir, which is precisely the "nobody else is looking" defence `Followups.fsi`
    /// argues a component must not rely on.
    let private withCache (f: string -> 'a) : 'a =
        let dir = Path.Combine(Path.GetTempPath(), "fsgg-followups-" + Guid.NewGuid().ToString("n"))
        let prior = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)

        try
            f dir
        finally
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", prior)

            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    let private ref' owner repo n : Ref = { Owner = owner; Repo = repo; Number = n }

    // ---- property 2: a queued ref is QUALIFIED, always ---------------------------------------------

    [<Theory>]
    [<InlineData("1063")>]
    [<InlineData("#1063")>]
    let ``a BARE ref is refused, and the refusal explains the queue's own reason`` (raw: string) =
        withCache (fun _ ->
            match apply (workerNamed "rook-test") (Add raw) with
            | Refused why ->
                // ASSERT THE MESSAGE, not just the refusal (RefParseTests' rule). `parseRefIn`'s own
                // no-default refusal says "this is not a FS-GG checkout" — which, run from inside
                // `.github`, is FALSE. #611: a diagnostic naming the wrong cause sends the reader to
                // check the one thing that was never wrong. The cause is the QUEUE outliving the
                // checkout, so the message must say that and not something about where we are standing.
                Assert.Contains("BARE", why)
                Assert.Contains("outlives the worktree", why)
                Assert.DoesNotContain("not a FS-GG checkout", why)
                // ...and it names the remedy, so it is actionable without opening the source.
                Assert.Contains("owner/repo#n", why)
            | other -> failwith $"expected a refusal of the bare ref, got %A{other}")

    [<Theory>]
    [<InlineData("FS-GG/FS.GG.Game#171", "FS-GG", "FS.GG.Game", 171)>]
    [<InlineData("FS.GG.Audio#12", "FS-GG", "FS.GG.Audio", 12)>]
    [<InlineData("https://github.com/FS-GG/FS.GG.SDD/issues/393", "FS-GG", "FS.GG.SDD", 393)>]
    let ``every QUALIFIED form is accepted`` (raw: string) (owner: string) (repo: string) (n: int) =
        withCache (fun _ ->
            Assert.Equal(Added(ref' owner repo n), apply (workerNamed "rook-test") (Add raw)))

    [<Fact>]
    let ``junk is refused with the canonical parser message, not the queue's bare-ref one`` () =
        withCache (fun _ ->
            // The two-probe trick must tell "bare" from "not a ref at all". If it collapsed them, every
            // typo would be told to name its repo — advice that cannot help.
            match apply (workerNamed "rook-test") (Add "nonsense") with
            | Refused why ->
                Assert.Contains("unrecognised issue ref", why)
                Assert.DoesNotContain("BARE", why)
            | other -> failwith $"expected a refusal, got %A{other}")

    [<Fact>]
    let ``the STORED form carries the owner, so it round-trips from another checkout`` () =
        withCache (fun _ ->
            let w = workerNamed "rook-test"
            apply w (Add "FS.GG.Game#171") |> ignore

            let file =
                match path w with
                | Ok p -> p
                | Error e -> failwith e

            // `Ref.Short` renders `repo#n` and DROPS the owner. Storing that would make the entry mean
            // whatever `$FSGG_COORD_OWNER` says when it is POPPED — which is a different process, by
            // construction. The owner on disk is what makes the promise mean one thing.
            Assert.Equal("FS-GG/FS.GG.Game#171", (File.ReadAllText file).Trim()))

    // ---- property 3: EMPTY is not UNREADABLE -------------------------------------------------------

    [<Fact>]
    let ``an ABSENT queue is Empty — a look that succeeded, exiting EX_NONE`` () =
        withCache (fun _ ->
            let w = workerNamed "rook-never-queued"
            Assert.Equal(Empty, apply w Peek)
            Assert.Equal(Empty, apply w Pop)
            Assert.Equal(Empty, apply w List)
            // The CONSTANT, not the digit 5: `Empty` must BE `take`'s EX_NONE, so a caller keying on
            // "nothing to do" reads one number across the engine (#585/#485).
            Assert.Equal(Client.ExitNone, exitCode Empty)
            Assert.NotEqual(Client.ExitGreen, exitCode Empty))

    [<Fact>]
    let ``Empty and Unreadable are DIFFERENT outcomes and different codes`` () =
        // #266's whole lesson, as an assertion: "I looked and there is nothing" must never be reachable
        // from "I could not look". A worker who reads the second as the first walks away from a promise.
        Assert.NotEqual(exitCode (Unreadable "the disk is on fire"), exitCode Empty)
        Assert.Equal(Client.ExitError, exitCode (Unreadable "the disk is on fire"))

    [<Fact>]
    let ``a CORRUPT head wedges the queue rather than silently dropping the promise`` () =
        withCache (fun _ ->
            let w = workerNamed "rook-test"
            apply w (Add "FS.GG.Game#171") |> ignore

            let file =
                match path w with
                | Ok p -> p
                | Error e -> failwith e

            File.WriteAllText(file, "not-a-ref\nFS-GG/FS.GG.Game#171\n")

            // Skipping the bad line would be THIS ITEM'S BUG rebuilt inside its own fix: a promise
            // discarded by a machine nobody asked. Fail closed, name the file, let the operator repair it.
            match apply w Pop with
            | Unreadable why ->
                Assert.Contains("not-a-ref", why)
                Assert.Contains(file, why)
            | other -> failwith $"expected the corrupt head to wedge, got %A{other}"

            // ...and the queue is UNTOUCHED — a pop that cannot name what it removed must remove nothing.
            Assert.Equal("not-a-ref\nFS-GG/FS.GG.Game#171\n", File.ReadAllText file))

    // ---- property 1: per-worker by construction ----------------------------------------------------

    [<Fact>]
    let ``two workers do not share a queue`` () =
        withCache (fun _ ->
            let a = workerNamed "rook-aaaa"
            let b = workerNamed "wren-bbbb"

            apply a (Add "FS.GG.Game#1") |> ignore
            apply b (Add "FS.GG.Audio#2") |> ignore

            // The shared-file bug #1061 shipped: both pops read one head, one ref is handed out twice and
            // the other is deleted having been handed to nobody.
            Assert.Equal(Popped(ref' "FS-GG" "FS.GG.Game" 1), apply a Pop)
            Assert.Equal(Popped(ref' "FS-GG" "FS.GG.Audio" 2), apply b Pop)
            Assert.Equal(Empty, apply a Pop)
            Assert.Equal(Empty, apply b Pop))

    [<Theory>]
    [<InlineData("///")>]
    [<InlineData("!!!")>]
    let ``an id that SLUGS TO NOTHING is refused rather than keyed on`` (id: string) =
        withCache (fun _ ->
            // `Identity.resolve` slugs its input and returns Ok for a slug that trims to empty, so every
            // such caller would key onto ONE file — property 1 defeated by the id itself, which is #419's
            // shape. The queue refuses; the underlying resolver defect is filed separately.
            let w = workerNamed id
            Assert.Equal("", w.Id)

            match path w with
            | Error why -> Assert.Contains("EMPTY", why)
            | Ok p -> failwith $"expected a refusal, got a queue path %s{p}"

            match apply w (Add "FS.GG.Game#1") with
            | Refused why -> Assert.Contains("EMPTY", why)
            | other -> failwith $"expected a refusal, got %A{other}")

    // ---- property 4: a pop is atomic ---------------------------------------------------------------

    [<Fact>]
    let ``pop is FIFO — the promise you made first is the one you keep first`` () =
        withCache (fun _ ->
            let w = workerNamed "rook-test"
            apply w (Add "FS.GG.Game#1") |> ignore
            apply w (Add "FS.GG.Audio#2") |> ignore
            apply w (Add "FS.GG.SDD#3") |> ignore

            Assert.Equal(Listed [ ref' "FS-GG" "FS.GG.Game" 1; ref' "FS-GG" "FS.GG.Audio" 2; ref' "FS-GG" "FS.GG.SDD" 3 ], apply w List)
            Assert.Equal(Popped(ref' "FS-GG" "FS.GG.Game" 1), apply w Pop)
            Assert.Equal(Popped(ref' "FS-GG" "FS.GG.Audio" 2), apply w Pop)
            Assert.Equal(Popped(ref' "FS-GG" "FS.GG.SDD" 3), apply w Pop)
            Assert.Equal(Empty, apply w Pop))

    [<Fact>]
    let ``peek does NOT remove, and pop does`` () =
        withCache (fun _ ->
            let w = workerNamed "rook-test"
            apply w (Add "FS.GG.Game#1") |> ignore

            Assert.Equal(Head(ref' "FS-GG" "FS.GG.Game" 1), apply w Peek)
            Assert.Equal(Head(ref' "FS-GG" "FS.GG.Game" 1), apply w Peek)
            Assert.Equal(Popped(ref' "FS-GG" "FS.GG.Game" 1), apply w Pop)
            Assert.Equal(Empty, apply w Peek))

    [<Fact>]
    let ``a drained queue is UNLINKED, not left zero-byte`` () =
        withCache (fun _ ->
            let w = workerNamed "rook-test"
            apply w (Add "FS.GG.Game#1") |> ignore

            let file =
                match path w with
                | Ok p -> p
                | Error e -> failwith e

            apply w Pop |> ignore

            // `Cache.clearPending`'s rule and its reason: an empty file and an absent file are different
            // facts, and "there is a queue and it is empty" is a claim about state nobody made.
            Assert.False(File.Exists file, "a drained queue must cease to exist, not linger zero-byte"))

    [<Fact>]
    let ``concurrent pops hand each ref to exactly ONE caller`` () =
        withCache (fun _ ->
            let w = workerNamed "rook-test"
            let n = 20

            for i in 1..n do
                apply w (Add $"FS.GG.Game#%d{i}") |> ignore

            // The read-then-delete shell could hand one head to two readers. Under contention this must
            // either pop a DISTINCT ref or report Unreadable (the lock was held) — never the same ref
            // twice, and never a ref deleted without being handed to anybody.
            let popped =
                [| for _ in 1..n ->
                       Task.Run(fun () ->
                           let rec attempt tries =
                               match apply w Pop with
                               | Unreadable _ when tries > 0 -> attempt (tries - 1)
                               | o -> o

                           attempt 50) |]
                |> Task.WhenAll
                |> fun t -> t.Result

            let refs =
                popped
                |> Array.choose (function
                    | Popped r -> Some r
                    | _ -> None)

            Assert.Equal(n, refs.Length)
            Assert.Equal(n, refs |> Array.distinct |> Array.length)
            Assert.Equal(Empty, apply w Pop))

    // ---- the action parser -------------------------------------------------------------------------

    [<Fact>]
    let ``the subcommands parse`` () =
        Assert.Equal<Result<Action, string>>(Ok(Add "FS.GG.Game#1"), parse [ "add"; "FS.GG.Game#1" ])
        Assert.Equal<Result<Action, string>>(Ok Peek, parse [ "peek" ])
        Assert.Equal<Result<Action, string>>(Ok Pop, parse [ "pop" ])
        Assert.Equal<Result<Action, string>>(Ok List, parse [ "list" ])

    [<Theory>]
    [<InlineData("")>]
    [<InlineData("nonsense")>]
    let ``an unknown or missing subcommand is NAMED and refused`` (verb: string) =
        let args = if verb = "" then [] else [ verb ]

        match parse args with
        | Error msg ->
            // `Options`' residue rule, one level down: a parser that shrugs is a parser whose caller
            // cannot tell "honoured" from "ignored".
            Assert.Contains("add <ref> | peek | pop | list", msg)
        | Ok a -> failwith $"expected a refusal, got %A{a}"

    [<Fact>]
    let ``extra args to add are refused, not silently dropped`` () =
        match parse [ "add"; "FS.GG.Game#1"; "FS.GG.Game#2" ] with
        | Error msg -> Assert.Contains("exactly one ref", msg)
        | Ok a -> failwith $"expected a refusal, got %A{a}"

    // ---- the projection ----------------------------------------------------------------------------

    [<Fact>]
    let ``stdout carries the REF and nothing else, so pop composes`` () =
        // The recipe's next step is `widen <that ref>`. A commentary line on stdout would be handed to it
        // as a path — so the split is a contract, not a formatting preference.
        let out, err = render (Popped(ref' "FS-GG" "FS.GG.Game" 171))
        Assert.Equal<string list>([ "FS-GG/FS.GG.Game#171" ], out)
        Assert.Empty(err)

        let out, err = render Empty
        Assert.Empty(out)
        Assert.NotEmpty(err)
