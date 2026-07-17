namespace FS.GG.Coord.Cli.Tests

open System.Reflection
open System.Text.Json
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Cli

/// A RULE SUBSET IS A SUBSET OF `rules`, AND THAT IS THE ONLY THING HOLDING IT HONEST (#889).
///
/// The driver skills restated the protocol by hand; #889 folds those restatements into generated
/// regions, one kind per skill, each emitted from a SUBSET of `Protocol.rules` — `filingRules` for
/// `cross-repo-coordination`, `reconcileRules` for `check-board`. But generation only makes copies
/// AGREE — it does not make them TRUE (#916), and a subset invites a failure the full `rules` region
/// cannot have: a rule authored straight into a subset would reach that skill's projection while the
/// canonical doc — which projects `rules` — never states it. That is the six-documents-disagreeing
/// problem #731 was built to end, rebuilt inside the mechanism that ends it, and it would be invisible:
/// both regions would regenerate green, because each is faithful to the list it reads.
///
/// So the containment is the assertion. A subset may hold FEWER rules than `rules`; it may never hold
/// one `rules` does not, and it may never hold a rule whose text has drifted from the canonical one.
///
/// THE INVARIANTS ATTACH TO THE TYPE, NOT TO A ROSTER — and that is the whole difference from what this
/// module replaced. `FilingRulesTests` named `filingRules` in every assertion, so `reconcileRules`
/// arrived with NO pin and nothing said so; the second subset would have been unguarded precisely
/// because the first one's guard was hand-written. A roster of subsets here would be the defect one
/// level up, wearing a test's clothes — the same reasoning `ProtocolTests.exitTables` already states
/// for the exit-code tables, applied one type over. A third subset is covered the moment it is
/// declared.
///
/// WHAT THIS DOES NOT DO. It does not assert that any SUBSET is the RIGHT one — that a filer needs
/// exactly these three rules and no others is a judgement, not a fact a test can hold. It pins the
/// property that a wrong judgement cannot ALSO become a contradiction: whatever a subset states, the
/// canonical doc states identically.
module RuleSubsetTests =

    /// Every `Rule list` the protocol declares, EXCEPT the canonical `rules` itself.
    ///
    /// Found by TYPE, so declaring a new subset in `Protocol.fs` enrols it here with no edit — and
    /// forgetting to enrol it is not a thing that can happen.
    let private subsets: (string * Protocol.Rule list) list =
        let m = typeof<Protocol.Rule>.Assembly.GetType "FS.GG.Coord.Protocol"

        // The module type is found by NAME, so a rename would otherwise silently yield zero subsets and
        // pass every invariant vacuously. That is the exact failure this reflection exists to refuse.
        Assert.True(not (isNull m), "FS.GG.Coord.Protocol not found — reflection cannot see the subsets it is gating")

        m.GetProperties(BindingFlags.Public ||| BindingFlags.Static)
        |> Array.filter (fun p -> p.PropertyType = typeof<Protocol.Rule list>)
        |> Array.filter (fun p -> p.Name <> "rules")
        |> Array.map (fun p -> p.Name, p.GetValue null :?> Protocol.Rule list)
        |> List.ofArray

    /// THE FLOOR (#266, #436). Reflection finding NOTHING would make every invariant below iterate an
    /// empty list and pass — the vacuity these gates exist to refuse. This fails when a subset is
    /// DELETED or renamed out of view; a subset that is ADDED needs no edit here.
    [<Fact>]
    let ``#889 every rule subset the protocol declares is gated`` () =
        let names = subsets |> List.map fst
        Assert.NotEmpty subsets
        Assert.Contains("filingRules", names)
        Assert.Contains("reconcileRules", names)

    [<Fact>]
    let ``#889 every rule in every subset is one of the canonical rules, by VALUE`` () =
        // BY VALUE, not by id. Comparing ids would pass while `statement` drifted — and the statement is
        // the entire payload of the projection. F# structural equality over the record is what makes
        // "the same rule" mean the same text, not merely the same name.
        for name, rs in subsets do
            for r in rs do
                Assert.True(
                    List.contains r Protocol.rules,
                    $"%s{name} holds rule '%s{r.Id}' that `rules` does not state identically — the skill's region would carry protocol the canonical doc never states"
                )

    [<Fact>]
    let ``#889 every subset is STRICT - one that grows into all of rules has no reason to be a kind`` () =
        // The kinds exist to keep a driver skill from carrying seventy lines of scheduler internals it
        // links elsewhere for (#916). A subset that had drifted into every rule would render the full
        // block under a name that promises a subset — the region would be honest and the SPLIT would be
        // a lie, which is the harder one to notice.
        for name, rs in subsets do
            Assert.True(
                rs.Length < Protocol.rules.Length,
                $"%s{name} covers every rule — either the subset is wrong, or that document should project `rules`"
            )

    [<Fact>]
    let ``#889 no subset is EMPTY - a region rendering nothing still regenerates green`` () =
        // An empty subset emits a region with a header and no rules. `--check` compares it against the
        // generator's own output and passes: the block agrees with itself and says nothing at all. That
        // is #266's signature — a projection reporting success over a subject it does not carry.
        for name, rs in subsets do
            Assert.True(not rs.IsEmpty, $"%s{name} is empty — its region would render no rules and still pass `--check`")

    [<Fact>]
    let ``#889 no subset renders a rule twice - a document arguing with itself`` () =
        for name, rs in subsets do
            let ids = rs |> List.map _.Id

            Assert.True(
                (List.distinct ids) = ids,
                $"%s{name} states a rule more than once — its region would render the same rule twice"
            )

    [<Fact>]
    let ``#889 facts --json states every subset by name, because the generator selects nothing`` () =
        // THE PAYLOAD IS THE CONTRACT, and this is what makes "the generator selects nothing" enforceable
        // rather than a convention. `scripts/generate-projections` renders `.filingRules[]` /
        // `.reconcileRules[]` and picks no ids of its own — deliberately, so the membership is a decision
        // in `Protocol.fs` that this suite can reach, rather than a `jq` filter in a shell script that
        // nothing tests. If a key ever stops being emitted, the region silently renders EMPTY and
        // `--check` still passes.
        //
        // Keyed on the PROPERTY NAME, so a new subset must be emitted under its own name to pass. That is
        // the coupling that keeps a third kind from being wired up with a shell-side filter.
        let json =
            Snapshot.renderFacts
                Protocol.rules
                Protocol.filingRules
                Protocol.reconcileRules
                Protocol.verdicts
                Protocol.takeExitCodes
                Protocol.landableExitCodes

        use doc = JsonDocument.Parse json

        for name, rs in subsets do
            match doc.RootElement.TryGetProperty name with
            | false, _ ->
                Assert.Fail
                    $"facts --json does not state '%s{name}' — its region would render empty and `--check` would still pass"
            | true, emitted ->
                Assert.Equal(JsonValueKind.Array, emitted.ValueKind)
                Assert.Equal(rs.Length, emitted.GetArrayLength())

                let ids =
                    emitted.EnumerateArray() |> Seq.map (fun e -> e.GetProperty("id").GetString()) |> List.ofSeq

                Assert.Equal<string list>(rs |> List.map _.Id, ids)
