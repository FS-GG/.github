namespace FS.GG.Coord.Core.Tests

open System
open System.IO
open Xunit
open FS.GG.Coord

/// .github#2739 — the churn reading has six required elements and, until this module, no way to
/// confirm a pass emitted them.
///
/// EVERY TEST BELOW NAMES THE MUTATION IT REDS ON. That is not a style preference: .github#266 is at
/// instance 7 with a fifth admitted mechanism (.github#2752) — *a verification artifact's efficacy is
/// proven against its author's own model, so a wrong model is invariant under the proof* — and this
/// module is a validator, which is exactly the shape that class arrives in. Two consequences are
/// visible throughout:
///
///   * every acceptance assertion has a REFUSAL twin and every refusal assertion has an ACCEPTANCE
///     control, so "the validator rejected it" can never be confused with "the validator rejects
///     everything", and "the validator accepted it" can never be confused with "the validator accepts
///     everything";
///   * where a rule has a boundary (a window's end against now, a coupling's row count, an interval's
///     start against its end) the test drives the BOUNDARY and not merely the branch. Reaching a
///     branch is not covering its predicate.
///
/// The fixture is the real 2026-08-15 worked reading from
/// `.claude/skills/board-analyst/references/churn-reading.md`, lifted field by field, so the schema is
/// proven against a pass that actually happened rather than one constructed to fit it (AC5).
module ChurnReadingTests =

    // The instant every `validate` call below judges against. A FIXED value, not `DateTimeOffset.Now`:
    // a suite whose verdict moves with the wall clock is an instrument fault, and the window rule is
    // precisely the rule a drifting `now` would silently stop testing.
    let private now = DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero)

    let private fixture name =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "churn-readings", name))

    // ------------------------------------------------------------------ document construction

    // A HEALTHY pass: every census looked and found nothing, so `remedy` is legitimately null. Every
    // probe below is this document with exactly one field changed, added or removed, which is what lets
    // each test name the single mutation it reds on.
    let private healthy =
        [ "schema", "\"fsgg.coord.churn-reading/v1\""
          "window", """{"start":"2026-08-14T17:00:00Z","end":"2026-08-15T17:00:00Z"}"""
          "rowsOpened", "17"
          "rowsClosed", "24"
          "netRowDelta", "-7"
          "itemsLanded", """{"count":24,"unit":"issues closed"}"""
          "causeInstances",
          """{"searchedNotFound":"folded every open row title and body against the 28 open rows; no two share a cause"}"""
          "successorGeneratingCauses",
          """{"searchedNotFound":"walked every row filed by an independent review in the window back to the PR that produced it; no chain exceeds one generation"}"""
          "alreadyDerived",
          """{"searchedNotFound":"grepped the scripts/check-*.py corpus for each open row's condition; no row restates a derived one"}"""
          "jointlyRedCandidates",
          """{"searchedNotFound":"listed the gate corpora every open row adds a file to; no corpus is touched by two live rows"}"""
          "remedy", "null"
          "prose", "\"No pathology. Net -7 against 24 issues closed; the board is absorbing discovery.\"" ]

    let private render (fields: (string * string) list) =
        fields
        |> List.map (fun (name, value) -> $"\"%s{name}\":%s{value}")
        |> String.concat ","
        |> sprintf "{%s}"

    // A mutation helper that cannot silently mutate nothing. A typo in a field name would otherwise
    // produce a probe identical to the control, which passes for the wrong reason forever.
    let private replacing name value =
        if healthy |> List.forall (fun (key, _) -> key <> name) then
            failwith $"the probe names '%s{name}', which is not a field of the healthy document"

        healthy
        |> List.map (fun (key, existing) -> key, (if key = name then value else existing))
        |> render

    let private without name =
        if healthy |> List.forall (fun (key, _) -> key <> name) then
            failwith $"the probe drops '%s{name}', which is not a field of the healthy document"

        healthy |> List.filter (fun (key, _) -> key <> name) |> render

    let private plus name value = render (healthy @ [ name, value ])

    // ------------------------------------------------------------------ result helpers

    let private findings (result: Result<'a, ChurnReading.Finding list>) : ChurnReading.Finding list =
        match result with
        | Error findings -> findings
        | Ok _ -> failwith "expected findings, but the reading was accepted"

    let private accepted (result: Result<'a, ChurnReading.Finding list>) =
        match result with
        | Ok value -> value
        | Error findings ->
            let rendered =
                findings
                |> List.map (fun finding -> $"%s{finding.Field}: %s{finding.Detail}")
                |> String.concat "; "

            failwith $"expected acceptance, but got findings: %s{rendered}"

    let private parsed document = accepted (ChurnReading.parse document)

    let private validated document =
        accepted (ChurnReading.validate now (parsed document))

    /// Parse, then validate, collapsing both stages' findings — the order an analyst meets them in.
    let private judge document =
        match ChurnReading.parse document with
        | Error findings -> Error findings
        | Ok reading -> ChurnReading.validate now reading

    let private fields result =
        findings result
        |> List.map (fun finding -> finding.Field)
        |> List.distinct
        |> List.sort

    let private mentions (result: Result<'a, ChurnReading.Finding list>) (field: string) (fragment: string) =
        Assert.Contains(
            findings result,
            fun finding -> finding.Field = field && finding.Detail.Contains(fragment: string)
        )

    // ================================================================== the two standing controls
    //
    // .github#2752's mechanism is that a verification artifact is proven against its author's own
    // model. These two tests are the cheapest available check on the instrument itself, and every
    // assertion in this file is read against them: one proves the validator can say no, the other
    // proves it can say yes.

    [<Fact>]
    let ``#2739 CONTROL the validator is not hardwired to accept - an empty document names every required field`` () =
        let result = ChurnReading.parse "{}"

        Assert.Equal<string list>(
            [ "alreadyDerived"
              "causeInstances"
              "itemsLanded"
              "jointlyRedCandidates"
              "netRowDelta"
              "prose"
              "remedy"
              "rowsClosed"
              "rowsOpened"
              "schema"
              "successorGeneratingCauses"
              "window" ],
            fields result
        )

    [<Fact>]
    let ``#2739 CONTROL the validator is not hardwired to refuse - the healthy document validates unchanged`` () =
        let reading = validated (render healthy)
        Assert.Equal(ChurnReading.Schema, reading.Schema)
        Assert.Equal(-7, reading.NetRowDelta)
        Assert.Equal(None, reading.Remedy)

    // ================================================================== AC1/AC2: the closed field set

    [<Fact>]
    let ``#2739 an unknown ROOT field is rejected BY NAME`` () =
        let result = ChurnReading.parse (plus "severity" "\"high\"")
        mentions result "severity" "is not a field of"

    [<Fact>]
    let ``#2739 the twelve documented root fields are exactly the accepted set`` () =
        // The control for the test above: every documented name is ACCEPTED where the unknown one was
        // refused, or "unknown field rejected" would be indistinguishable from "all fields rejected".
        Assert.Equal(12, List.length healthy)
        let reading = validated (render healthy)
        Assert.Equal("issues closed", reading.ItemsLanded.Unit)

    [<Fact>]
    let ``#2739 an unknown field INSIDE window is rejected by its qualified name`` () =
        let result =
            ChurnReading.parse (
                replacing "window" """{"start":"2026-08-14T17:00:00Z","end":"2026-08-15T17:00:00Z","label":"today"}"""
            )

        mentions result "window.label" "is not a field of"

    [<Fact>]
    let ``#2739 an unknown field INSIDE a found entry is rejected by its qualified name`` () =
        let result =
            ChurnReading.parse (
                replacing "alreadyDerived" """{"found":[{"row":".github#2648","gate":"g","severity":"high"}]}"""
            )

        mentions result "alreadyDerived.found[0].severity" "is not a field of"

    // ================================================================== AC2: the whole point of the row
    //
    // An OMITTED element and an element that was considered and found empty must not look the same.
    // .github#2760 states the same defect one layer up: `gate.report_ok` takes a prose summary rather
    // than a subject census, so the harness cannot tell "looked and found nothing" from "did not look".

    [<Fact>]
    let ``#2739 an ABSENT jointlyRedCandidates is a validation error, never a silent pass`` () =
        let result = ChurnReading.parse (without "jointlyRedCandidates")
        mentions result "jointlyRedCandidates" "is required"

    [<Fact>]
    let ``#2739 an ABSENT alreadyDerived is a validation error, never a silent pass`` () =
        let result = ChurnReading.parse (without "alreadyDerived")
        mentions result "alreadyDerived" "is required"

    [<Fact>]
    let ``#2739 searchedNotFound is a valid claim of looked-and-found-none, and it CARRIES the search`` () =
        let reading = validated (render healthy)

        match reading.AlreadyDerived with
        | ChurnReading.SearchedNotFound search -> Assert.Contains("scripts/check-*.py", search)
        | other -> failwith $"expected SearchedNotFound, got %A{other}"

    [<Fact>]
    let ``#2739 a BLANK searchedNotFound search is refused - the claim has to be checkable, not trusted`` () =
        // The distinction between this and the test above is the entire value of the union: a census
        // that says "none" without saying what was searched is the sentinel wearing a new shape.
        let result = judge (replacing "alreadyDerived" """{"searchedNotFound":"   "}""")
        mentions result "alreadyDerived" "must carry the search performed"

    [<Fact>]
    let ``#2739 notSearched DECODES but never validates - a skipped element is an INCOMPLETE pass, named`` () =
        let document =
            replacing "jointlyRedCandidates" """{"notSearched":"ran out of time before the gate-corpus sweep"}"""

        // It must decode: an analyst who genuinely did not look needs an honest shape to write, or the
        // shape they reach for instead is a false `searchedNotFound`.
        match (parsed document).JointlyRedCandidates with
        | ChurnReading.NotSearched why -> Assert.Contains("ran out of time", why)
        | other -> failwith $"expected NotSearched, got %A{other}"

        // And it must not validate: all six elements are required.
        let result = judge document
        mentions result "jointlyRedCandidates" "is one of the six REQUIRED elements"
        mentions result "jointlyRedCandidates" "INCOMPLETE"

    [<Fact>]
    let ``#2739 a BARE ARRAY census is refused because it cannot distinguish found-none from did-not-look`` () =
        // This is the shape .github#2739's own body first proposed. It is better than prose and still
        // collapses the pair .github#2760 names, so it is refused with that reason quoted at the analyst.
        let result = ChurnReading.parse (replacing "alreadyDerived" "[]")
        mentions result "alreadyDerived" "cannot distinguish"

    [<Fact>]
    let ``#2739 the STRING none as a census is refused, with the collapse named`` () =
        let result = ChurnReading.parse (replacing "causeInstances" "\"none\"")
        mentions result "causeInstances" "is the string 'none'"
        mentions result "causeInstances" "cannot distinguish"

    [<Fact>]
    let ``#2739 NULL as a census is refused, with the collapse named`` () =
        let result = ChurnReading.parse (replacing "causeInstances" "null")
        mentions result "causeInstances" "is null"
        mentions result "causeInstances" "cannot distinguish"

    [<Fact>]
    let ``#2739 a census naming two cases at once is refused, naming both`` () =
        let result =
            ChurnReading.parse (replacing "alreadyDerived" """{"searchedNotFound":"s","notSearched":"n"}""")

        mentions result "alreadyDerived" "names more than one case"

    [<Fact>]
    let ``#2739 found with an EMPTY array is refused - looked-and-found-none is said with searchedNotFound`` () =
        let result = judge (replacing "alreadyDerived" """{"found":[]}""")
        mentions result "alreadyDerived" "must carry at least one row"

    // ================================================================== AC3: remedy, both directions

    [<Fact>]
    let ``#2739 a pathology with a NULL remedy is refused, naming which censuses reported found`` () =
        let document =
            healthy
            |> List.map (fun (key, value) ->
                key,
                match key with
                | "alreadyDerived" -> """{"found":[{"row":".github#2648","gate":"scripts/check-engine-freshness.py"}]}"""
                | _ -> value)
            |> render

        let result = judge document
        mentions result "remedy" "is required to be non-null whenever a pathology is present"
        mentions result "remedy" "alreadyDerived"

    [<Fact>]
    let ``#2739 the SAME pathology WITH a remedy validates - the control for the refusal above`` () =
        let document =
            healthy
            |> List.map (fun (key, value) ->
                key,
                match key with
                | "alreadyDerived" -> """{"found":[{"row":".github#2648","gate":"scripts/check-engine-freshness.py"}]}"""
                | "remedy" ->
                    "\"As at 2026-08-15T17:00:00Z: rewrite .github#2648 into the operator decision the gate cannot make, rather than closing it.\""
                | _ -> value)
            |> render

        let reading = validated document
        Assert.True(Option.isSome reading.Remedy)

    [<Fact>]
    let ``#2739 no pathology and a null remedy validates - a clean pass owes no mechanism`` () =
        let reading = validated (render healthy)
        Assert.Equal(None, reading.Remedy)

    [<Fact>]
    let ``#2739 the STRING none as a remedy is refused - explicit null is the shape`` () =
        let result = judge (replacing "remedy" "\"none\"")
        mentions result "remedy" "is the string 'none'"

    [<Fact>]
    let ``#2739 an ABSENT remedy key is refused - null has to be written, not omitted`` () =
        let result = ChurnReading.parse (without "remedy")
        mentions result "remedy" "is required"

    // ================================================================== element 1: the window

    [<Fact>]
    let ``#2739 a window whose end has NOT HAPPENED YET is refused`` () =
        let result =
            judge (replacing "window" """{"start":"2026-08-14T17:00:00Z","end":"2026-08-16T00:00:01Z"}""")

        mentions result "window.end" "must already have happened"

    [<Fact>]
    let ``#2739 BOUNDARY a window ending exactly at now validates, one second later does not`` () =
        // Reaching the branch is not covering its predicate: this drives `end <= now` and `end > now`
        // one second apart, so an off-by-one in the comparison reds here rather than passing silently.
        let at instant =
            judge (replacing "window" $"""{{"start":"2026-08-14T17:00:00Z","end":"%s{instant}"}}""")

        Assert.True(Result.isOk (at "2026-08-16T00:00:00Z"))
        mentions (at "2026-08-16T00:00:01Z") "window.end" "must already have happened"

    [<Fact>]
    let ``#2739 today is not a window endpoint`` () =
        let result =
            ChurnReading.parse (replacing "window" """{"start":"today","end":"2026-08-15T17:00:00Z"}""")

        mentions result "window.start" "'today', 'this session' and a"

    [<Fact>]
    let ``#2739 a window endpoint without an explicit UTC Z is refused`` () =
        let result =
            ChurnReading.parse (
                replacing "window" """{"start":"2026-08-14T17:00:00+02:00","end":"2026-08-15T17:00:00Z"}"""
            )

        mentions result "window.start" "must be an explicit UTC instant"

    [<Fact>]
    let ``#2739 a bare date is not a window endpoint`` () =
        let result =
            ChurnReading.parse (replacing "window" """{"start":"2026-08-14","end":"2026-08-15T17:00:00Z"}""")

        mentions result "window.start" "must be an explicit UTC instant"

    [<Fact>]
    let ``#2739 a decoded window endpoint carries a ZERO offset, so the verdict cannot depend on the host timezone`` () =
        let reading = parsed (render healthy)
        Assert.Equal(TimeSpan.Zero, reading.Window.Start.Offset)
        Assert.Equal(TimeSpan.Zero, reading.Window.End.Offset)

    [<Fact>]
    let ``#2739 BOUNDARY a window whose end equals its start is refused, one second wide is not`` () =
        let at instant =
            judge (replacing "window" $"""{{"start":"2026-08-14T17:00:00Z","end":"%s{instant}"}}""")

        mentions (at "2026-08-14T17:00:00Z") "window" "strictly after its start"
        Assert.True(Result.isOk (at "2026-08-14T17:00:01Z"))

    // ================================================================== element 1: the arithmetic

    [<Fact>]
    let ``#2739 a netRowDelta disagreeing with rowsOpened minus rowsClosed is refused, with BOTH numbers`` () =
        let result = judge (replacing "netRowDelta" "-1")
        mentions result "netRowDelta" "is -1, but rowsOpened 17 minus rowsClosed 24 is -7"

    [<Fact>]
    let ``#2739 the worked reading's own arithmetic - 17 opened, 24 closed, net -7 - validates`` () =
        // The control for the refusal above. Both windows in the reference document's "Two windows, one
        // board" section are internally consistent; it is the transcription slip this catches.
        let reading = validated (render healthy)
        Assert.Equal(reading.RowsOpened - reading.RowsClosed, reading.NetRowDelta)

    [<Fact>]
    let ``#2739 a negative rowsClosed is refused`` () =
        let document =
            healthy
            |> List.map (fun (key, value) ->
                key,
                match key with
                | "rowsClosed" -> "-1"
                | "netRowDelta" -> "18"
                | _ -> value)
            |> render

        mentions (judge document) "rowsClosed" "cannot be negative"

    // ================================================================== element 1: the unit

    [<Fact>]
    let ``#2739 an itemsLanded with a BLANK unit is refused - 25 items landed names no unit`` () =
        // `references/churn-reading.md` measures this exact failure: at the row's own endpoint no
        // candidate unit produces 25 — issues closed, PRs merged and PRs closed were all measured.
        let result = judge (replacing "itemsLanded" """{"count":25,"unit":"  "}""")
        mentions result "itemsLanded.unit" "must name what 'landed' counts"

    [<Fact>]
    let ``#2739 an itemsLanded CARRYING its unit validates - the control for the refusal above`` () =
        let reading = validated (replacing "itemsLanded" """{"count":25,"unit":"pull requests merged"}""")
        Assert.Equal(25, reading.ItemsLanded.Count)
        Assert.Equal("pull requests merged", reading.ItemsLanded.Unit)

    [<Fact>]
    let ``#2739 an itemsLanded with no unit KEY at all is refused`` () =
        let result = ChurnReading.parse (replacing "itemsLanded" """{"count":25}""")
        mentions result "itemsLanded.unit" "is required"

    // ================================================================== keep the prose

    [<Fact>]
    let ``#2739 a BLANK prose field is refused - the skeleton never replaces the reading`` () =
        let result = judge (replacing "prose" "\"   \"")
        mentions result "prose" "never a"

    [<Fact>]
    let ``#2739 prose is free-form - the validator never inspects what the reading says`` () =
        // The control for the refusal above, and the row's hardest constraint stated as a test: any
        // non-blank prose is accepted, because the analysis is judgement and this module owns none of it.
        let reading = validated (replacing "prose" "\"x\"")
        Assert.Equal("x", reading.Prose)

    // ================================================================== element 6: the coupling

    [<Fact>]
    let ``#2739 a jointlyRedCandidates entry naming ONE row is refused - the element is a COUPLING`` () =
        let document =
            healthy
            |> List.map (fun (key, value) ->
                key,
                match key with
                | "jointlyRedCandidates" -> """{"found":[{"rows":[".github#2666"],"gate":"tests/skill-quality/recency-comment-edit.py"}]}"""
                | "remedy" -> "\"As at 2026-08-15T17:00:00Z: the later row adopts the corpus requirement.\""
                | _ -> value)
            |> render

        mentions (judge document) "jointlyRedCandidates.found[0].rows" "at least two rows"

    [<Fact>]
    let ``#2739 BOUNDARY a two-row coupling validates where a one-row entry did not`` () =
        let document =
            healthy
            |> List.map (fun (key, value) ->
                key,
                match key with
                | "jointlyRedCandidates" -> """{"found":[{"rows":[".github#2666",".github#2584"],"gate":"tests/skill-quality/recency-comment-edit.py"}]}"""
                | "remedy" -> "\"As at 2026-08-15T17:00:00Z: the later row adopts the corpus requirement.\""
                | _ -> value)
            |> render

        let reading = validated document

        match reading.JointlyRedCandidates with
        | ChurnReading.Found [ coupling ] -> Assert.Equal(2, List.length coupling.Rows)
        | other -> failwith $"expected one coupling, got %A{other}"

    // ================================================================== AC5: the real worked reading

    [<Fact>]
    let ``#2739 the 2026-08-15 worked reading from references churn-reading md parses and validates`` () =
        let reading = validated (fixture "worked-2026-08-15.json")

        Assert.Equal(DateTimeOffset(2026, 8, 14, 17, 0, 0, TimeSpan.Zero), reading.Window.Start)
        Assert.Equal(DateTimeOffset(2026, 8, 15, 17, 0, 0, TimeSpan.Zero), reading.Window.End)
        Assert.Equal(17, reading.RowsOpened)
        Assert.Equal(24, reading.RowsClosed)
        Assert.Equal(-7, reading.NetRowDelta)

        match reading.CauseInstances with
        | ChurnReading.Found [ instance ] ->
            Assert.Equal<string list>([ ".github#2381"; ".github#2648" ], instance.Rows)
        | other -> failwith $"expected one cause instance, got %A{other}"

        match reading.AlreadyDerived with
        | ChurnReading.Found [ derived ] -> Assert.Equal("scripts/check-engine-freshness.py", derived.Gate)
        | other -> failwith $"expected one derived row, got %A{other}"

        match reading.JointlyRedCandidates with
        | ChurnReading.Found [ coupling ] ->
            Assert.Equal("tests/skill-quality/recency-comment-edit.py", coupling.Gate)
        | other -> failwith $"expected one coupling, got %A{other}"

        Assert.True(Option.isSome reading.Remedy)

    [<Fact>]
    let ``#2739 the worked reading is a PATHOLOGICAL pass, so dropping its remedy reds`` () =
        // Proof the fixture exercises the remedy rule rather than sitting on its easy side: the real
        // reading found all four pathologies, so it is the strongest available witness for AC3.
        let document = fixture "worked-2026-08-15.json"
        let reading = parsed document
        let stripped = { reading with Remedy = None }
        let result = ChurnReading.validate now stripped
        mentions result "remedy" "is required to be non-null whenever a pathology is present"

    // ================================================================== the receipt

    [<Fact>]
    let ``#2739 renderResult reports each census CASE, so the receipt itself distinguishes the answers`` () =
        let mixed =
            healthy
            |> List.map (fun (key, value) ->
                key,
                match key with
                | "alreadyDerived" -> """{"found":[{"row":".github#2648","gate":"scripts/check-engine-freshness.py"}]}"""
                | "remedy" -> "\"As at 2026-08-15T17:00:00Z: rewrite the row into the operator decision.\""
                | _ -> value)
            |> render

        let rendered = ChurnReading.renderResult (validated mixed)

        Assert.Contains("\"schema\":\"fsgg.coord.churn-reading-result/v1\"", rendered)
        Assert.Contains("\"alreadyDerived\":{\"case\":\"found\",\"count\":1}", rendered)
        Assert.Contains("\"causeInstances\":{\"case\":\"searchedNotFound\",\"count\":0}", rendered)
        Assert.Contains("\"jointlyRedCandidates\":{\"case\":\"searchedNotFound\",\"count\":0}", rendered)
        Assert.Contains("\"remedy\":\"present\"", rendered)
        Assert.Contains("\"window\":{\"start\":\"2026-08-14T17:00:00Z\",\"end\":\"2026-08-15T17:00:00Z\"}", rendered)
        Assert.Contains("\"writes\":0", rendered)

    [<Fact>]
    let ``#2739 renderResult on a clean pass reports null remedy and four searchedNotFound censuses`` () =
        let rendered = ChurnReading.renderResult (validated (render healthy))
        Assert.Contains("\"remedy\":\"null\"", rendered)
        Assert.Equal(4, (rendered.Split("\"case\":\"searchedNotFound\"")).Length - 1)

    // ================================================================== totality

    [<Fact>]
    let ``#2739 a malformed document is a VALUE, never an exception`` () =
        mentions (ChurnReading.parse "{ not json") "document" "is not valid JSON"
        mentions (ChurnReading.parse "[]") "document" "must be a JSON object"
        mentions (ChurnReading.parse "\"a reading\"") "document" "must be a JSON object"
