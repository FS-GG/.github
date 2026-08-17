namespace FS.GG.Coord.Core.Tests

open System
open System.IO
open Xunit
open FS.GG.Coord

/// .github#2737 — the finding packet is the sole input to the sole filing authority (.github#2695),
/// and until this module nothing parsed it.
///
/// The fixtures under `fixtures/finding-packets/` are REAL packets, lifted field by field from the
/// live registers `.github#2691` (pending) and `.github#2687` (rejected). Each file is named for the
/// GitHub comment id it was lifted from, so any reader can fetch the original and check the lift.
/// Nothing in them is invented: where a comment supplies no answer for a field, the field is ABSENT
/// from the lift, and the validator's verdict on that absence is the point of the fixture.
module FindingPacketTests =

    let private fixture name =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "finding-packets", name))

    let private packet: FindingPacket.Packet =
        { Schema = FindingPacket.Schema
          Surface = "src/FS.GG.Coord.Core/Review.fs:314"
          Cause = FindingPacket.Established "the conjunct pair is unreachable"
          RedToday = FindingPacket.SearchedNotFound "nothing fails; the condition is latent"
          DerivedBy = FindingPacket.SearchedNotFound "no scripts/check-*.py computes it"
          ClassRow = FindingPacket.Found ".github#2557"
          WhyNotHere = "outside the declared Paths:"
          Paths = [ "src/FS.GG.Coord.Core/Review.fs" ]
          Finder = "plover-ce15" }

    let private findings (result: Result<'a, FindingPacket.Finding list>) : FindingPacket.Finding list =
        match result with
        | Error findings -> findings
        | Ok _ -> failwith "expected findings, but the packet was accepted"

    let private accepted result =
        match result with
        | Ok value -> value
        | Error (findings: FindingPacket.Finding list) ->
            let rendered =
                findings |> List.map (fun f -> $"%s{f.Field}: %s{f.Detail}") |> String.concat "; "

            failwith $"expected acceptance, but got findings: %s{rendered}"

    let private fieldsOf result =
        findings result |> List.map (fun finding -> finding.Field) |> List.distinct |> List.sort

    // ---------------------------------------------------------------- AC-001: the closed field set

    [<Fact>]
    let ``#2737 an unknown field is rejected BY NAME, not merely rejected`` () =
        // Naming the field is the assertion. A test that checked only "parsing failed" would also pass
        // against a parser that rejects everything, which is the control failure VO-004 guards.
        let document =
            """{"schema":"fsgg.coord.finding-packet/v1","surface":"s","cause":{"established":"c"},
                "redToday":{"found":"r"},"derivedBy":{"found":"d"},"classRow":{"found":"k"},
                "whyNotHere":"w","paths":["p"],"finder":"plover-ce15","severity":"high"}"""

        let findings = findings (FindingPacket.parse document)
        Assert.Contains(findings, fun finding -> finding.Field = "severity")
        Assert.Contains(findings, fun finding -> finding.Detail.Contains "is not a field of")

    [<Fact>]
    let ``#2737 the nine documented fields are exactly the accepted set`` () =
        // The control for the test above: each documented name must be ACCEPTED where the unknown one
        // was refused, or "unknown field rejected" would be indistinguishable from "all fields rejected".
        let document = fixture "accept-5304198465.json"
        let packet = accepted (FindingPacket.parse document)
        Assert.Equal(FindingPacket.Schema, packet.Schema)
        Assert.Equal("merlin-efd3", packet.Finder)

    // -------------------------------------------- AC-002 / FR-002: the sentinels, and why not a null

    [<Theory>]
    [<InlineData("redToday")>]
    [<InlineData("derivedBy")>]
    [<InlineData("classRow")>]
    let ``#2737 the string none is refused with a message naming the three-case shape`` (field: string) =
        let document =
            $$"""{"schema":"fsgg.coord.finding-packet/v1","surface":"s","cause":{"established":"c"},
                 "redToday":{"found":"r"},"derivedBy":{"found":"d"},"classRow":{"found":"k"},
                 "whyNotHere":"w","paths":["p"],"finder":"plover-ce15","{{field}}":"none"}"""

        let finding =
            findings (FindingPacket.parse document)
            |> List.find (fun finding -> finding.Field = field)

        Assert.Contains("'none'", finding.Detail)
        Assert.Contains("found", finding.Detail)
        Assert.Contains("searchedNotFound", finding.Detail)
        Assert.Contains("notSearched", finding.Detail)

    [<Theory>]
    [<InlineData("redToday")>]
    [<InlineData("derivedBy")>]
    [<InlineData("classRow")>]
    let ``#2737 an explicit null is refused too, because it collapses the same two states`` (field: string) =
        // .github#2737's body proposed a nullable. A null cannot say whether the finder searched and
        // found nothing or did not search, which is the distinction tests 2 and 3 of the bar rest on.
        let document =
            $$"""{"schema":"fsgg.coord.finding-packet/v1","surface":"s","cause":{"established":"c"},
                 "redToday":{"found":"r"},"derivedBy":{"found":"d"},"classRow":{"found":"k"},
                 "whyNotHere":"w","paths":["p"],"finder":"plover-ce15","{{field}}":null}"""

        let finding =
            findings (FindingPacket.parse document)
            |> List.find (fun finding -> finding.Field = field)

        Assert.Contains("null", finding.Detail)
        Assert.Contains("notSearched", finding.Detail)

    [<Fact>]
    let ``#2737 all three answer cases are distinguishable after a round trip`` () =
        let document = fixture "accept-5307639382.json"
        let value = accepted (FindingPacket.parse document)

        // The whole point of DEC-002: these are three states, not two.
        match value.RedToday, value.DerivedBy, value.ClassRow with
        | FindingPacket.SearchedNotFound red, FindingPacket.SearchedNotFound derived, FindingPacket.Found row ->
            Assert.Contains("check-engine-release-notes", red)
            Assert.Contains("No scripts/check-*.py", derived)
            Assert.Contains(".github#2648", row)
        | _ -> failwith "5307639382 answers redToday and derivedBy as searched-not-found and classRow as found"

    [<Fact>]
    let ``#2737 I did not look is a different value from I looked and found nothing`` () =
        let value = accepted (FindingPacket.parse (fixture "accept-5304198465.json"))

        match value.DerivedBy, value.ClassRow with
        | FindingPacket.NotSearched _, FindingPacket.NotSearched _ -> ()
        | _ -> failwith "5304198465's finder said an adjudicator SHOULD CHECK — that is not-searched"

        Assert.NotEqual<FindingPacket.Answer>(
            FindingPacket.NotSearched "an adjudicator should check",
            FindingPacket.SearchedNotFound "an adjudicator should check"
        )

    [<Fact>]
    let ``#2737 a union case carrying a blank string is refused`` () =
        // FR-008. A sentinel that carries no evidence is exactly what `none` was, wearing a new shape.
        let value = { packet with DerivedBy = FindingPacket.NotSearched "   " }
        Assert.Contains(fieldsOf (FindingPacket.validate value), fun field -> field = "derivedBy")

    [<Fact>]
    let ``#2737 a union naming two cases at once is refused`` () =
        let document =
            """{"schema":"fsgg.coord.finding-packet/v1","surface":"s","cause":{"established":"c"},
                "redToday":{"found":"r","notSearched":"also"},"derivedBy":{"found":"d"},
                "classRow":{"found":"k"},"whyNotHere":"w","paths":["p"],"finder":"plover-ce15"}"""

        let finding =
            findings (FindingPacket.parse document)
            |> List.find (fun finding -> finding.Field = "redToday")

        Assert.Contains("more than one case", finding.Detail)

    // ------------------------------------------------------------- AC-003 / FR-003: it never throws

    [<Theory>]
    [<InlineData("")>]
    [<InlineData("   ")>]
    [<InlineData("not json at all")>]
    [<InlineData("{\"schema\":")>]
    [<InlineData("[1,2,3]")>]
    [<InlineData("42")>]
    [<InlineData("null")>]
    [<InlineData("\"a bare string\"")>]
    let ``#2737 parse returns typed findings and never throws`` (document: string) =
        // The gate is that no exception escapes. `findings` itself fails the test on an Ok.
        let findings = findings (FindingPacket.parse document)
        Assert.NotEmpty findings
        Assert.All(findings, fun finding -> Assert.False(String.IsNullOrWhiteSpace finding.Field))
        Assert.All(findings, fun finding -> Assert.False(String.IsNullOrWhiteSpace finding.Detail))

    // ---------------------------------------------- AC-004 / FR-004: the REAL corpus, accepted side

    [<Theory>]
    [<InlineData("accept-5304198465.json")>]
    [<InlineData("accept-5307639382.json")>]
    [<InlineData("accept-5307153964.json")>]
    let ``#2737 a real filed packet that answers all nine fields is accepted`` (name: string) =
        let value = accepted (FindingPacket.parse (fixture name))
        accepted (FindingPacket.validate value) |> ignore

    // ---------------------------------------------- AC-004 / FR-004: the REAL corpus, rejected side
    //
    // Each row: the fixture, and the field the real comment does not answer. These are the packets
    // .github#2737's route decision required this specification to name, and the set is not empty.

    [<Theory>]
    // 5304189944 — "Pending packet 1". Declares itself "the address, not a copy" and proposes no
    // declaration at all, so a faithful lift cannot invent one.
    [<InlineData("reject-5304189944-no-paths.json", "paths")>]
    // 5309266535 — "Release debt with no destination". A request for a release act, not a finding:
    // nothing is red and it never says so, and it proposes no paths of its own.
    [<InlineData("reject-5309266535-not-a-finding.json", "surface")>]
    [<InlineData("reject-5309266535-not-a-finding.json", "redToday")>]
    [<InlineData("reject-5309266535-not-a-finding.json", "paths")>]
    // 5306816009 — its own first line reads "Increment on an EXISTING packet — not a new finding",
    // and it is one of the increments and adjudications that any search for the anchor returns.
    [<InlineData("reject-5306816009-increment.json", "surface")>]
    [<InlineData("reject-5306816009-increment.json", "whyNotHere")>]
    [<InlineData("reject-5306816009-increment.json", "paths")>]
    [<InlineData("reject-5306816009-increment.json", "derivedBy")>]
    // 5311301051 — the corpus's best packet by a prose reading, and it still never says whether a
    // gate already derives its condition. That is test 2 of the filing bar, unanswered.
    [<InlineData("reject-5311301051-no-derived-by.json", "derivedBy")>]
    // 5304198465's own content, with its sentinels written the way the whole corpus writes them.
    [<InlineData("reject-5304198465-sentinels-as-none.json", "redToday")>]
    [<InlineData("reject-5304198465-sentinels-as-none.json", "derivedBy")>]
    [<InlineData("reject-5304198465-sentinels-as-none.json", "classRow")>]
    let ``#2737 a real filed packet is rejected on the field it does not answer`` (name: string, field: string) =
        let reported =
            match FindingPacket.parse (fixture name) with
            | Error findings -> findings |> List.map (fun finding -> finding.Field)
            | Ok value -> fieldsOf (FindingPacket.validate value)

        Assert.Contains(field, reported)

    [<Fact>]
    let ``#2737 the rejected corpus is not empty, and the accepted corpus is not either`` () =
        // A validator that accepts everything cannot fail; one that rejects everything cannot
        // discriminate. Both halves are asserted, as one fact, so neither can quietly become vacuous.
        let verdicts name =
            match FindingPacket.parse (fixture name) with
            | Error _ -> false
            | Ok value -> (FindingPacket.validate value) |> Result.isOk

        let accepted =
            [ "accept-5304198465.json"; "accept-5307639382.json"; "accept-5307153964.json" ]

        let rejected =
            [ "reject-5304189944-no-paths.json"
              "reject-5309266535-not-a-finding.json"
              "reject-5306816009-increment.json"
              "reject-5311301051-no-derived-by.json"
              "reject-5304198465-sentinels-as-none.json" ]

        Assert.All(accepted, fun name -> Assert.True(verdicts name, $"%s{name} must be accepted"))
        Assert.All(rejected, fun name -> Assert.False(verdicts name, $"%s{name} must be rejected"))
        Assert.NotEmpty accepted
        Assert.NotEmpty rejected

    [<Fact>]
    let ``#2737 an accepted fixture stops being accepted when its content is damaged`` () =
        // VO-004: the accepted assertions above must be shown able to FAIL, or they prove nothing about
        // the validator's discrimination. One field is emptied and the same fixture must now be refused.
        let value = accepted (FindingPacket.parse (fixture "accept-5307153964.json"))
        Assert.True((FindingPacket.validate value) |> Result.isOk)
        Assert.True((FindingPacket.validate { value with Paths = [] }) |> Result.isError)
        Assert.True((FindingPacket.validate { value with Surface = "" }) |> Result.isError)

        Assert.True(
            (FindingPacket.validate { value with DerivedBy = FindingPacket.NotSearched "" })
            |> Result.isError
        )

    // ------------------------------------------------------------------------- FR-009: the finder id

    [<Theory>]
    [<InlineData("the implementer of .github#2557 (avocet-e787)")>]
    [<InlineData("worker `swift-6a33`")>]
    [<InlineData("board analyst dunlin-c152")>]
    [<InlineData("Plover-CE15")>]
    [<InlineData("")>]
    let ``#2737 a finder that is a sentence naming an id is not an id`` (finder: string) =
        // `scripts/fsgg-coord say <ref> --to <worker>` is how an analyst replies, and it takes an id.
        Assert.Contains(fieldsOf (FindingPacket.validate { packet with Finder = finder }), fun f -> f = "finder")

    [<Theory>]
    [<InlineData("plover-ce15")>]
    [<InlineData("merlin-efd3")>]
    [<InlineData("dunlin-c152")>]
    [<InlineData("plover-227")>]
    let ``#2737 a real minted worker id is accepted`` (finder: string) =
        // The control for the test above: real ids minted by `whoami --mint` must pass, or the finder
        // rule would be "reject everything" rather than "reject prose".
        accepted (FindingPacket.validate { packet with Finder = finder }) |> ignore

    // --------------------------------------------------------- AC-005 / FR-005: the intake round trip

    [<Fact>]
    let ``#2737 a validated packet lifts into an intake draft that Intake validate accepts`` () =
        let value = accepted (FindingPacket.parse (fixture "accept-5307153964.json"))
        let seed = FindingPacket.toIntakeSeed value

        let draft: Intake.Draft =
            { Schema = Intake.Schema
              Id = "packet-5307153964"
              Owner = "FS-GG"
              Repository = ".github"
              Title = "intake apply can never file a row in FS-GG/.github"
              Observed = seed.Observed
              RootCause = seed.RootCause
              Acceptance = "duplicateCandidates follows pagination to exhaustion"
              Verification = "a paginated fixture transport returns every candidate"
              Paths = seed.Paths
              Class = "defect"
              Status = "Ready"
              Disposition = Some Intake.Create
              Phase = None
              Severity = None
              BlockedBy = None
              BlockedOn = None
              BacklogReason = None
              JudgementQuestion = None }

        match Intake.validate draft with
        | Ok _ -> ()
        | Error findings ->
            let rendered =
                findings |> List.map (fun f -> $"%s{f.Field} %s{f.Detail}") |> String.concat "; "

            failwith $"the lifted draft must validate, but: %s{rendered}"

        Assert.Equal<string list>([ "src/FS.GG.Coord.GitHub/Reads.fs"; "tests/FS.GG.Coord.GitHub.Tests" ], seed.Paths)

    [<Fact>]
    let ``#2737 an unestablished cause lifts in .github#1858's form rather than vanishing`` () =
        let seed =
            FindingPacket.toIntakeSeed { packet with Cause = FindingPacket.NotEstablished "three runs, two outcomes" }

        Assert.Contains("not established", seed.RootCause)
        Assert.Contains("three runs, two outcomes", seed.RootCause)

    // ------------------------------------------------------------------------------ the result document

    [<Fact>]
    let ``#2737 the success document names the schema and which cases were answered`` () =
        let value = accepted (FindingPacket.parse (fixture "accept-5304198465.json"))
        let rendered = FindingPacket.renderResult value
        Assert.Contains(FindingPacket.ResultSchema, rendered)
        // The analyst can see at a glance which bar tests the finder actually searched.
        Assert.Contains("\"derivedBy\":\"notSearched\"", rendered)
        Assert.Contains("\"redToday\":\"found\"", rendered)
        Assert.Contains("\"writes\":0", rendered)

    [<Fact>]
    let ``#2737 an unrecognised schema version is refused rather than best-effort decoded`` () =
        let value = { packet with Schema = "fsgg.coord.finding-packet/v2" }
        Assert.Contains(fieldsOf (FindingPacket.validate value), fun field -> field = "schema")
