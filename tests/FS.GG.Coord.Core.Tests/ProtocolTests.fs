namespace FS.GG.Coord.Tests

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
