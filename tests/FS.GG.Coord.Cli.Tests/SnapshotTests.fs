namespace FS.GG.Coord.Cli.Tests

open System.Text.Json
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.Cli

/// THE CODEC IS A FAIL-OPEN SURFACE, so it gets the same suspicion as everything else in this domain.
///
/// Every test here is the same question asked of a different field: can a snapshot that was never
/// properly READ still produce a confident answer? In bash it always could — `jq -r .status` on a
/// missing key yields the string "null", and the caller compares it to something. That is epic #266's
/// 51 children, and it does not stop being that bug because it moved into a JSON reader.
///
/// The rule these tests enforce: absence is an ERROR unless absence is a modelled FACT.
module SnapshotTests =

    let private snapshot items =
        $"""{{ "schema": "fsgg.coord.snapshot/1", "allowBacklog": false, "items": [ %s{items} ] }}"""

    let private ok =
        function
        | Ok r -> r
        | Error(e: Snapshot.Error list) -> failwithf "expected a snapshot, got errors: %A" e

    let private errors =
        function
        | Ok _ -> failwith "expected the snapshot to be REFUSED, but it parsed"
        | Error(e: Snapshot.Error list) -> e

    let private paths (e: Snapshot.Error list) = e |> List.map (fun x -> x.Path)

    // ================================================================================================
    // THE SCHEMA TAG IS A REFUSAL, not decoration.
    // ================================================================================================
    // The bash shim and this engine are distributed on different clocks — the shim is a digest-pinned
    // kit row, the engine is a package. They WILL be out of step at some point. When they are, the
    // engine must refuse a shape it half-recognises rather than decide from it.

    [<Fact>]
    let ``a snapshot with no schema tag is refused`` () =
        let e = Snapshot.parse """{ "allowBacklog": false, "items": [] }""" |> errors
        Assert.Contains("$.schema", paths e)

    [<Fact>]
    let ``a snapshot from a FUTURE schema is refused, not guessed at`` () =
        let e =
            Snapshot.parse """{ "schema": "fsgg.coord.snapshot/2", "allowBacklog": false, "items": [] }"""
            |> errors

        Assert.Contains("$.schema", paths e)

    [<Fact>]
    let ``a snapshot that is not JSON at all is refused with a diagnosable error`` () =
        let e = Snapshot.parse "nothing schedulable right now." |> errors
        Assert.NotEmpty(e)

    // ================================================================================================
    // ABSENCE IS AN ERROR — unless absence is a modelled fact.
    // ================================================================================================

    [<Fact>]
    let ``a missing 'state' is an ERROR — it is not "probably open"`` () =
        // The difference between "the issue is open" and "we did not look at the issue" is the whole of
        // #520, and defaulting this field would erase it.
        let e =
            snapshot """{ "owner":"o","repo":"r","number":1,"status":"Ready","body":"Paths: a" }"""
            |> Snapshot.parse
            |> errors

        Assert.Contains("items[0].state", paths e)

    [<Fact>]
    let ``a missing 'body' is an ERROR — it is not an empty touch-set`` () =
        // An absent body would parse to `Undeclared`, which reads as "somebody forgot to declare a
        // touch-set" — a statement about the ITEM. It would in fact be a statement about US: we never
        // read the body. Those are different facts and only one of them is the author's problem.
        let e =
            snapshot """{ "owner":"o","repo":"r","number":1,"status":"Ready","state":"OPEN" }"""
            |> Snapshot.parse
            |> errors

        Assert.Contains("items[0].body", paths e)

    [<Fact>]
    let ``a missing 'status' KEY is an error, but an explicitly null status is NoStatus`` () =
        // #437. The two are not the same fact: a null Status is an item somebody filed and never
        // placed — invisible to every scheduler, and its own bug. A missing KEY means the client did
        // not tell us, which is ours.
        let missing =
            snapshot """{ "owner":"o","repo":"r","number":1,"state":"OPEN","body":"Paths: a" }"""
            |> Snapshot.parse
            |> errors

        Assert.Contains("items[0].status", paths missing)

        let explicit =
            snapshot """{ "owner":"o","repo":"r","number":1,"status":null,"state":"OPEN","body":"Paths: a" }"""
            |> Snapshot.parse
            |> ok

        Assert.Equal(NoStatus, (List.head explicit.Candidates).Item.Status)

    [<Fact>]
    let ``a claim with no liveness is refused — "held" and "we did not check" are different`` () =
        // #581. A lease that lapsed is not proof of abandonment, and the code that decides so must be
        // TOLD what was observed. A defaulted liveness is a claim about the world nobody made.
        let e =
            snapshot
                """{ "owner":"o","repo":"r","number":1,"status":"Ready","state":"OPEN","body":"Paths: a",
                     "claim": { "worker":"w-a","ageSeconds":60 } }"""
            |> Snapshot.parse
            |> errors

        Assert.Contains("items[0].claim.liveness", paths e)

    [<Fact>]
    let ``'lease-expired-pr-open' without the PR number is refused — that is proof without the proof`` () =
        let e =
            snapshot
                """{ "owner":"o","repo":"r","number":1,"status":"Ready","state":"OPEN","body":"Paths: a",
                     "claim": { "worker":"w-a","ageSeconds":60,"liveness":{"kind":"lease-expired-pr-open"} } }"""
            |> Snapshot.parse
            |> errors

        Assert.Contains("items[0].claim.liveness.pr", paths e)

    [<Fact>]
    let ``NO claim at all is a FACT, not an omission — the item is simply unheld`` () =
        // The counterweight. If every absence were an error, the common case would be unrepresentable.
        let r =
            snapshot """{ "owner":"o","repo":"r","number":1,"status":"Ready","state":"OPEN","body":"Paths: a" }"""
            |> Snapshot.parse
            |> ok

        Assert.Equal(None, (List.head r.Candidates).Item.Claim)

    // ================================================================================================
    // A VOCABULARY THE ENGINE WAS NEVER TAUGHT IS REFUSED, NEVER COERCED.
    // ================================================================================================

    [<Fact>]
    let ``an unknown board Status is refused — a board-schema change must be LOUD`` () =
        // Coercing it to `NoStatus` would be SAFE (the item reads as unschedulable) and that is exactly
        // what makes it dangerous: the fleet would quietly stop scheduling a whole column and nothing
        // would say why. Silence in the safe direction is still silence.
        let e =
            snapshot """{ "owner":"o","repo":"r","number":1,"status":"Icebox","state":"OPEN","body":"Paths: a" }"""
            |> Snapshot.parse
            |> errors

        Assert.Contains("items[0].status", paths e)

    [<Fact>]
    let ``an unknown blocker state is refused`` () =
        let e =
            snapshot
                """{ "owner":"o","repo":"r","number":1,"status":"Ready","state":"OPEN","body":"Paths: a",
                     "blockers":[{"owner":"o","repo":"r","number":9,"state":"draft"}] }"""
            |> Snapshot.parse
            |> errors

        Assert.Contains("items[0].blockers[0].state", paths e)

    // ================================================================================================
    // EVERY error, not the first.
    // ================================================================================================

    [<Fact>]
    let ``a snapshot with several faults reports them ALL in one pass`` () =
        // A shadow that has to be debugged one field per round-trip, across six repos, does not get
        // debugged — it gets switched off.
        let e =
            snapshot """{ "owner":"o","repo":"r","number":1 }"""
            |> Snapshot.parse
            |> errors

        Assert.Contains("items[0].status", paths e)
        Assert.Contains("items[0].state", paths e)
        Assert.Contains("items[0].body", paths e)

    // ================================================================================================
    // THE ENGINE PARSES THE BODY. That is the point of putting it on the wire.
    // ================================================================================================

    [<Fact>]
    let ``#277 a fenced 'Paths:' line is a QUOTATION — the engine's own parser must see that`` () =
        // If the shadow re-used bash's parse, the touch-set grammar — its own family of incidents —
        // would never be compared at all, and we would call it proven on the strength of never having
        // tested it.
        let r =
            snapshot
                """{ "owner":"o","repo":"r","number":1,"status":"Ready","state":"OPEN",
                     "body":"Example:\n\n```\nPaths: src/A.fs\n```\n" }"""
            |> Snapshot.parse
            |> ok

        Assert.Equal(Undeclared, (List.head r.Candidates).Item.TouchSet)

    [<Fact>]
    let ``#496 'Paths: none' parses to the SENTINEL, not to an absent declaration`` () =
        let r =
            snapshot """{ "owner":"o","repo":"r","number":1,"status":"Ready","state":"OPEN","body":"Paths: none" }"""
            |> Snapshot.parse
            |> ok

        Assert.Equal(DeclaredNone, (List.head r.Candidates).Item.TouchSet)

    [<Fact>]
    let ``#1103 leg 8 'Paths: any' parses to the CHORE sentinel, distinct from none`` () =
        let r =
            snapshot """{ "owner":"o","repo":"r","number":1,"status":"Ready","state":"OPEN","body":"Paths: any" }"""
            |> Snapshot.parse
            |> ok

        Assert.Equal(DeclaredChore, (List.head r.Candidates).Item.TouchSet)

    [<Fact>]
    let ``#1103 leg 2 'Blocked on: human/decision' parses onto Item.HumanBlock, keeping the touch-set`` () =
        // #918's shape end-to-end: the sentinel and a REAL touch-set coexist on one item.
        let r =
            snapshot
                """{ "owner":"o","repo":"r","number":1,"status":"Ready","state":"OPEN",
                     "body":"Blocked on: human/decision\n\nPaths: src/A" }"""
            |> Snapshot.parse
            |> ok

        let item = (List.head r.Candidates).Item
        Assert.Equal(Some AwaitingHumanDecision, item.HumanBlock)
        Assert.Equal(Declared [ Matchable "src/A" ], item.TouchSet)

    [<Fact>]
    let ``an item with no sentinel has HumanBlock None`` () =
        let r =
            snapshot """{ "owner":"o","repo":"r","number":1,"status":"Ready","state":"OPEN","body":"Paths: src/A" }"""
            |> Snapshot.parse
            |> ok

        Assert.Equal(None, (List.head r.Candidates).Item.HumanBlock)

    [<Fact>]
    let ``ADR-0050: the id/field/value assertion parses onto Candidate.DeclaredPredicate (.github#1213)`` () =
        // The PURE half of the flip-time gate: the body's `### Asserted registry ...` form sections are read
        // into the assertion here; the owner VERDICT is resolved later at the offer path. `Item.Predicate`
        // stays None — resolution is impure and never runs in `parse`.
        // `Paths:` FIRST, then the assertion sections: `sectionValue` collects the value section up to the
        // next `### ` heading or end-of-body, so a trailing `Paths:` line would be swallowed into the value.
        // In a real declaration the assertion sits in its own bounded form sections; ordering it last here
        // keeps the value clean. (A polluted value only ever fails CLOSED — the gate holds — never a flip.)
        let r =
            snapshot
                """{ "owner":"o","repo":"r","number":1,"status":"Blocked","state":"OPEN",
                     "body":"Paths: src/A\n\n### Asserted registry id\n\nfs-gg-playtest\n\n### Asserted registry field\n\nmirrored\n\n### Asserted registry value\n\ntrue" }"""
            |> Snapshot.parse
            |> ok

        let c = List.head r.Candidates

        let expected: RegistryPredicate.Assertion =
            { Id = "fs-gg-playtest"
              Field = "mirrored"
              Value = "true" }

        Assert.Equal(Some expected, c.DeclaredPredicate)

        Assert.Equal(None, c.Item.Predicate)
        Assert.Equal(Declared [ Matchable "src/A" ], c.Item.TouchSet)

    [<Fact>]
    let ``an item whose body declares no assertion has DeclaredPredicate None`` () =
        let r =
            snapshot """{ "owner":"o","repo":"r","number":1,"status":"Blocked","state":"OPEN","body":"Paths: src/A" }"""
            |> Snapshot.parse
            |> ok

        Assert.Equal(None, (List.head r.Candidates).DeclaredPredicate)

    [<Fact>]
    let ``bashPaths is carried through untouched — the engine decides from its OWN parse`` () =
        // The field exists so a divergence can show both parses side by side. It must never feed the
        // decision, or the shadow would be comparing bash against itself.
        let r =
            snapshot
                """{ "owner":"o","repo":"r","number":1,"status":"Ready","state":"OPEN",
                     "body":"Paths: src/A.fs","bashPaths":["src/WRONG.fs"] }"""
            |> Snapshot.parse
            |> ok

        let c = List.head r.Candidates

        Assert.Equal(Declared [ Matchable "src/A.fs" ], c.Item.TouchSet)
        Assert.Equal(Some [ "src/WRONG.fs" ], c.BashPaths)

    // ================================================================================================
    // `batch -n 0` is bash's "unlimited". It must not read as "choose nothing".
    // ================================================================================================

    [<Fact>]
    let ``limit 0 means UNLIMITED, not a cap of zero`` () =
        let r =
            """{ "schema":"fsgg.coord.snapshot/1","allowBacklog":false,"limit":0,"items":[] }"""
            |> Snapshot.parse
            |> ok

        Assert.Equal(None, r.Limit)

    // ================================================================================================
    // .github#2399: `facts --json`'s `reviewPolicy` object must keep every byte it emitted before this
    // item (existing keys, including `quotedMarkerRule`'s STRING VALUE) while additively carrying the
    // new `markerAnchors`/`markerFieldGrammar` keys #2369 asked for. This is the `facts --json`
    // before/after check as a test rather than only a PR-body diff.
    // ================================================================================================

    [<Fact>]
    let ``facts --json's reviewPolicy keeps quotedMarkerRule's value and additively carries markerAnchors/markerFieldGrammar`` () =
        let json = Snapshot.renderFacts Protocol.factsDocument
        use document = JsonDocument.Parse json
        let reviewPolicy = document.RootElement.GetProperty("reviewPolicy")

        // Unchanged existing keys/values — the "protocol bytes did not move" half of the check.
        Assert.Equal(Protocol.reviewPolicy.InitialMarker, reviewPolicy.GetProperty("initialMarker").GetString())
        Assert.Equal(Protocol.reviewPolicy.RepairPhaseMarker, reviewPolicy.GetProperty("repairPhaseMarker").GetString())
        Assert.Equal(
            Protocol.renderMarkerAnchorRule Protocol.LeadingBlock,
            reviewPolicy.GetProperty("quotedMarkerRule").GetString())

        // Additive: every MarkerAnchors entry appears, keyed by id and anchor.
        let markerAnchors = reviewPolicy.GetProperty("markerAnchors").EnumerateArray() |> Seq.toList
        Assert.Equal(List.length Protocol.reviewPolicy.MarkerAnchors, markerAnchors.Length)

        for marker in Protocol.reviewPolicy.MarkerAnchors do
            let entry =
                markerAnchors
                |> List.find (fun e -> e.GetProperty("id").GetString() = marker.Id)

            Assert.Equal("leading-block", entry.GetProperty("anchor").GetString())

        // Additive: every markerFieldGrammar entry appears, with its required fields in order.
        let fieldGrammar = reviewPolicy.GetProperty("markerFieldGrammar").EnumerateArray() |> Seq.toList
        Assert.Equal(List.length Protocol.markerFieldGrammar, fieldGrammar.Length)

        let initialEntry =
            fieldGrammar
            |> List.find (fun e -> e.GetProperty("id").GetString() = Protocol.reviewPolicy.InitialMarker)

        let initialFields =
            initialEntry.GetProperty("requiredFields").EnumerateArray()
            |> Seq.map (fun v -> v.GetString())
            |> Seq.toList

        Assert.Equal<string list>([ "critic"; "reviewed-head"; "verdict" ], initialFields)
