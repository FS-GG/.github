namespace FS.GG.Coord.Tests

open System.Reflection
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types

/// THE PROTOCOL IS THE SOURCE, NOT A COPY OF ONE (ADR-0034 §4.5).
///
/// `Protocol.fs` is what every projection is emitted from — the canonical doc and the `SKILL.md` bodies
/// in both skill roots. These tests guard the properties that make that safe. They are cheap and they
/// look obvious; each one is a way the projection could go quietly wrong, and "quietly" is the whole
/// problem this module exists to end.
module ProtocolTests =

    /// A RULE THAT RESTATES ITS SOURCE IS STILL A COPY. `touchSetGrammar.Statement` must BE
    /// `Schedulability.TouchSetGrammar` — not a paraphrase of it, not a version of it that was correct
    /// when it was typed. If somebody replaces the reference with a literal, the two drift the moment
    /// one is edited, and the generated docs would then faithfully publish the stale one.
    ///
    /// This is not hypothetical: the F# grammar constant was itself typed in by hand while the flip was
    /// being written, byte-identical to bash's purely by luck.
    [<Fact>]
    let ``the grammar rule IS the enforcing constant — not a copy of it`` () =
        Assert.Equal(Schedulability.TouchSetGrammar, Protocol.touchSetGrammar.Statement)

    /// EVERY VERDICT THE SCHEDULER CAN RETURN MUST BE DOCUMENTED, AND NOTHING ELSE MAY BE.
    ///
    /// Fourteen of the scheduler family's issues were a MISSING CASE in this union. A worker handed a
    /// verdict the docs never mention has no way to act on it; a doc listing a verdict the scheduler
    /// cannot return sends them looking for a state that does not exist. The union and its prose are one
    /// thing, and this asserts they stay one thing.
    [<Fact>]
    let ``the documented verdicts are exactly the verdicts the scheduler can return`` () =
        let documented = Protocol.verdicts |> List.map (fun v -> v.Kind) |> Set.ofList

        // Every case of `Schedulability`, constructed. If a case is ADDED to the union, this list fails
        // to compile until somebody adds it — which is the point: the compiler, not a reviewer, notices.
        let everyCase: Schedulability.Schedulability list =
            [ Schedulability.Startable
              Schedulability.IssueClosed
              Schedulability.WrongStatus Backlog
              Schedulability.NoTouchSet
              Schedulability.DeliberatelyNoTouchSet
              Schedulability.UnusableTouchSet [ "**/x" ]
              Schedulability.BlockedBy []
              Schedulability.HeldBy(WorkerId "w")
              Schedulability.HeldByLiveWork(WorkerId "w", 1)
              Schedulability.OverlapsInFlight []
              Schedulability.Undetermined "r" ]

        Assert.Equal(List.length everyCase, Set.count documented)

        // And the kinds match the wire vocabulary the divergence log speaks, so a reader of that log can
        // grep a verdict straight into the doc that explains it.
        let expected =
            Set.ofList
                [ "startable"
                  "issue-closed"
                  "wrong-status"
                  "no-touch-set"
                  "deliberately-no-touch-set"
                  "unusable-touch-set"
                  "blocked-by"
                  "held"
                  "held-by-live-work"
                  "overlaps-in-flight"
                  "undetermined" ]

        Assert.Equal<Set<string>>(expected, documented)

    /// A RULE WITH NO `Because` IS A RULE THAT WILL BE DELETED BY SOMEBODY WHO DOES NOT KNOW WHY.
    ///
    /// Every rule here was bought by an incident. The `Because` is what stops the next author — who is
    /// reasonably sure the rule is silly — from removing it. Half this repo's issue history is that
    /// author.
    [<Fact>]
    let ``every rule states the incident that bought it`` () =
        for r in Protocol.rules do
            Assert.False(System.String.IsNullOrWhiteSpace r.Id, $"rule '%s{r.Title}' has no id")
            Assert.False(System.String.IsNullOrWhiteSpace r.Statement, $"rule '%s{r.Id}' states nothing")
            Assert.False(System.String.IsNullOrWhiteSpace r.Because, $"rule '%s{r.Id}' has no Because")

    /// Ids are ANCHORS — a projection links to them, and a reader greps them back to the code. Two rules
    /// sharing one id would silently make one of them unreachable.
    [<Fact>]
    let ``rule ids are unique`` () =
        let ids = Protocol.rules |> List.map (fun r -> r.Id)
        Assert.Equal<string list>(List.distinct ids, ids)

    /// THE GENERATOR MUST HAVE SOMETHING TO GENERATE. An empty rule list would render an empty region,
    /// the gate would compare empty to empty, and every document would pass while stating nothing — the
    /// vacuity failure (#266, #436) in the gate built to end the vendored copies.
    [<Fact>]
    let ``the protocol is not empty`` () =
        Assert.NotEmpty Protocol.rules
        Assert.NotEmpty Protocol.verdicts
        Assert.NotEmpty Protocol.takeExitCodes
        Assert.NotEmpty Protocol.landableExitCodes

    /// EVERY `ExitCodeDoc list` the protocol declares, found by REFLECTION rather than by a list
    /// somebody remembers to update.
    ///
    /// A hand-written roster here would be the defect one level up: a table added to `Protocol.fs` and
    /// not to the roster gets NONE of the invariants below, and nothing says so — a gate that silently
    /// stops covering its subject (#266, #436). `take`'s table (#889) and `landable`'s (#900) were both
    /// hand-written copies that drifted; answering that with a hand-written roster of them would be the
    /// same mistake, wearing a test's clothes. So the invariants attach to the TYPE, and a third table
    /// is covered the moment it is declared.
    let private exitTables: (string * Protocol.ExitCodeDoc list) list =
        let m =
            typeof<Protocol.Rule>.Assembly.GetType "FS.GG.Coord.Protocol"

        // The module type is found by NAME, so a rename would otherwise silently yield zero tables and
        // pass every invariant vacuously. That is the exact failure this reflection exists to refuse.
        Assert.True(not (isNull m), "FS.GG.Coord.Protocol not found — reflection cannot see the tables it is gating")

        m.GetProperties(BindingFlags.Public ||| BindingFlags.Static)
        |> Array.filter (fun p -> p.PropertyType = typeof<Protocol.ExitCodeDoc list>)
        |> Array.map (fun p -> p.Name, p.GetValue null :?> Protocol.ExitCodeDoc list)
        |> List.ofArray

    /// THE FLOOR (#266, #436). Reflection finding NOTHING would make every invariant below iterate an
    /// empty list and pass — the vacuity these gates exist to refuse. This fails when a table is
    /// DELETED or renamed out of view; a table that is ADDED needs no edit here, because reflection has
    /// already covered it.
    [<Fact>]
    let ``every exit-code table the protocol declares is gated`` () =
        let names = exitTables |> List.map fst
        Assert.NotEmpty exitTables
        Assert.Contains("takeExitCodes", names)
        Assert.Contains("landableExitCodes", names)

    /// A CODE WITHOUT A REMEDY IS A CODE A WORKER INVENTS A REMEDY FOR — and the invented one is
    /// "retry", which is exactly wrong for `take` 2 (the engine is broken), `take` 5 (the queue is
    /// empty), and `landable` 3 (the PR is RED — the invented retry is the #900 hang itself).
    /// The `Meaning`/`Action` split is the whole reason these tables beat the number alone.
    [<Fact>]
    let ``every exit code says what it saw and what to do`` () =
        for cmd, codes in exitTables do
            for c in codes do
                Assert.False(
                    System.String.IsNullOrWhiteSpace c.Meaning,
                    $"%s{cmd} exit %d{c.Code} means nothing")

                Assert.False(
                    System.String.IsNullOrWhiteSpace c.Action,
                    $"%s{cmd} exit %d{c.Code} tells the caller to do nothing")

    /// Two rows for one code is two remedies for one observation, and the worker reads whichever it
    /// meets first. The old hand-written `take` table had exactly this defect: its `≠0, ≠2` row also
    /// matched 5, 6 and 75, so three codes carried two contradictory instructions each.
    [<Fact>]
    let ``exit codes are unique within a table`` () =
        for cmd, codes in exitTables do
            let ns = codes |> List.map (fun c -> c.Code)
            Assert.Equal<int list>(List.distinct ns, ns)
            Assert.True(not ns.IsEmpty, $"%s{cmd}'s table is empty")

    /// 0 IS THE ONLY SUCCESS, and the table's first row is what a worker copies. `take && work_it`
    /// firing on nothing is #585 itself; merging on a non-green `landable` is #900's.
    [<Fact>]
    let ``every table documents exactly one success code, and it leads`` () =
        for cmd, codes in exitTables do
            Assert.Equal(0, (List.head codes).Code)

            Assert.Equal(
                1,
                codes |> List.filter (fun c -> c.Code = 0) |> List.length)

            Assert.True(
                codes |> List.forall (fun c -> c.Code >= 0),
                $"%s{cmd} documents a negative exit code, which no shell can report")

    /// `landable`'s CONTRACT IS THE POLL LOOP, and #900 was that the recipe got the two codes the loop
    /// reads backwards: it called 3 "pending" (3 is RED — so the loop waits forever on a PR that will
    /// never go green) and had no row for 7 at all (7 is PENDING — so the loop reads it as an
    /// unrecognised failure and stops waiting on a PR that is merely still running).
    ///
    /// This pins the DOCUMENTED MEANINGS, in `Core`, where the engine's constants are not visible.
    /// `ExitContractTests` ties the same rows to `Client.ExitPending`/`ExitRed` — the two halves are
    /// both needed, because generating a table only makes the copies AGREE; it does not make them TRUE.
    [<Fact>]
    let ``landable's 3 is red and its 7 is pending, never the reverse`` () =
        let meaningOf code =
            Protocol.landableExitCodes
            |> List.tryFind (fun c -> c.Code = code)
            |> Option.map (fun c -> c.Meaning.ToUpperInvariant())

        match meaningOf 3 with
        | None -> Assert.Fail "landable exit 3 (red/conflicted) is not documented"
        | Some m ->
            Assert.True(m.Contains "RED", "landable exit 3 does not say it is RED — #900 is that it said 'pending'")
            Assert.False(m.StartsWith "PENDING", "landable exit 3 is documented as PENDING — that is #900 exactly, and a loop built on it hangs")

        match meaningOf 7 with
        | None -> Assert.Fail "landable exit 7 (pending) is not documented — the recipe's table had no 7, so a loop stops waiting on a PR that is still running"
        | Some m -> Assert.True(m.Contains "PENDING", "landable exit 7 does not say it is PENDING")

    /// THERE IS NO EX_RATE IN `landable`, and a reader of `take`'s table will expect one.
    /// `Reads.prLandableRequire` returns a bare `PrState` with no error channel, so a rate limit is
    /// `PrUnknown` — exit 4 — not 75. Documenting a 75 here would send a worker to wait out a budget
    /// reset over what is actually an unread PR.
    [<Fact>]
    let ``landable documents no rate-limit code`` () =
        Assert.DoesNotContain(75, Protocol.landableExitCodes |> List.map (fun c -> c.Code))
