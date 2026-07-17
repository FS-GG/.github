namespace FS.GG.Coord.Cli.Tests

open System.Text.Json
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Cli

/// `filingRules` IS A SUBSET OF `rules`, AND THAT IS THE ONLY THING HOLDING IT HONEST (#889).
///
/// `cross-repo-coordination` restated the touch-set rules by hand; #889 folds them into a generated
/// region. But generation only makes copies AGREE — it does not make them TRUE (#916), and a SUBSET
/// invites a failure the full `rules` region cannot have: a rule authored straight into `filingRules`
/// would reach the skill's projection while the canonical doc — which projects `rules` — never states
/// it. That is the six-documents-disagreeing problem #731 was built to end, rebuilt inside the
/// mechanism that ends it, and it would be invisible: both regions would regenerate green, because each
/// is faithful to the list it reads.
///
/// So the containment is the assertion. `filingRules` may hold FEWER rules than `rules`; it may never
/// hold one `rules` does not, and it may never hold a rule whose text has drifted from the canonical
/// one.
///
/// WHAT THIS DOES NOT DO. It does not assert that the SUBSET is the right one — that a filer needs
/// exactly these three and no others is a judgement, not a fact a test can hold. It pins the property
/// that a wrong judgement here cannot ALSO become a contradiction: whatever `filingRules` states, the
/// canonical doc states identically.
module FilingRulesTests =

    [<Fact>]
    let ``#889 every filing rule is one of the canonical rules, by VALUE`` () =
        // BY VALUE, not by id. Comparing ids would pass while `statement` drifted — and the statement is
        // the entire payload of the projection. F# structural equality over the record is what makes
        // "the same rule" mean the same text, not merely the same name.
        for r in Protocol.filingRules do
            Assert.Contains(r, Protocol.rules)

    [<Fact>]
    let ``#889 filingRules is a strict subset - if it grows into all of rules, the kind has no reason to exist``
        ()
        =
        // The kind exists to keep a driver skill from carrying seventy lines of scheduler internals it
        // links elsewhere for (#916). A `filingRules` that had drifted into every rule would render the
        // full block under a name that promises a subset — the region would be honest and the SPLIT
        // would be a lie, which is the harder one to notice.
        Assert.True(
            Protocol.filingRules.Length < Protocol.rules.Length,
            "filingRules covers every rule — either the subset is wrong, or this document should project `rules`"
        )

    [<Fact>]
    let ``#889 filingRules has no duplicates - a rule rendered twice is a document arguing with itself`` () =
        Assert.Equal<string list>(
            Protocol.filingRules |> List.map _.Id |> List.distinct,
            Protocol.filingRules |> List.map _.Id
        )

    [<Fact>]
    let ``#889 facts --json states filingRules, because the generator selects nothing`` () =
        // THE PAYLOAD IS THE CONTRACT. `scripts/generate-projections` renders `.filingRules[]` and picks
        // no ids of its own — deliberately, so the membership is a decision in `Protocol.fs` that this
        // suite can reach, rather than a `jq` filter in a shell script that nothing tests. If the key
        // ever stops being emitted, the region silently renders EMPTY and `--check` still passes: the
        // generated block would agree with itself, and say nothing at all.
        let json =
            Snapshot.renderFacts
                Protocol.rules
                Protocol.filingRules
                Protocol.verdicts
                Protocol.takeExitCodes
                Protocol.landableExitCodes

        use doc = JsonDocument.Parse json
        let emitted = doc.RootElement.GetProperty("filingRules")

        Assert.Equal(JsonValueKind.Array, emitted.ValueKind)
        Assert.NotEmpty(emitted.EnumerateArray())
        Assert.Equal(Protocol.filingRules.Length, emitted.GetArrayLength())

        let ids =
            emitted.EnumerateArray() |> Seq.map (fun e -> e.GetProperty("id").GetString()) |> List.ofSeq

        Assert.Equal<string list>(Protocol.filingRules |> List.map _.Id, ids)
