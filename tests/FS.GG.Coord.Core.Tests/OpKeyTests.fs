namespace FS.GG.Coord.Core.Tests

open System
open System.Security.Cryptography
open System.Text
open FSharp.Reflection
open Xunit
open FS.GG.Coord

/// The operation key (.github#2311, slice 1 of the 2026-08-04 executor-fencing design §11.2).
///
/// Acceptance criterion 3 of that item is the one that shapes this file: "Prove the second half by
/// construction, not by example — a test that only asserts equality cannot distinguish a real digest
/// from a constant." So the distinctness claim is carried by three things and not by a handful of
/// pairs: an INDEPENDENT recomputation of the digest from the documented pre-image (a constant would
/// fail it), a whole-set injectivity check over ninety tuples rather than a chosen pair, and — the
/// load-bearing ones — the demonstrations that the tuples which WOULD collapse are refused before
/// anything is hashed. There are TWO of those, because the key is `sha256(UTF8(concat …))` and each
/// stage needs its own clause: the separator clause for `String.concat`, and the well-formed-UTF-16
/// clause for `Encoding.UTF8.GetBytes`, whose replacement fallback maps every unpaired surrogate to
/// one byte sequence. The second was missing from this module's first head and was found in
/// independent review; the test that closes it is
/// `…an unpaired surrogate is refused, because UTF-8 would collapse it`.
module OpKeyTests =

    let private item = "FS-GG/.github#2311"
    let private generation = "5304654156"
    let private receiver = "FS-GG/FS.GG.Net"

    let private keyOf i g r o =
        match Operation.compose i g r o with
        | Ok key -> key.Value
        | Error refusals -> failwith (refusals |> List.map Operation.describe |> String.concat "; ")

    let private preimageOf i g r o =
        match Operation.preimage i g r o with
        | Ok rendered -> rendered
        | Error refusals -> failwith (refusals |> List.map Operation.describe |> String.concat "; ")

    let private refusalsOf i g r o =
        match Operation.compose i g r o with
        | Ok key -> failwithf "expected a refusal, composed %s" key.Value
        | Error refusals -> refusals

    /// The whole outcome as one legible line, never throwing. Used where several legs must ALL be
    /// evaluated before a single assertion compares them: a helper that throws on an unexpected `Ok`
    /// would abandon the remaining legs exactly when they matter most, which is the gap an independent
    /// critic found in this file's first evidence.
    ///
    /// It renders to STRING rather than returning the typed outcome for a reason that only shows up
    /// when the assertion fails: xUnit's collection formatter prints a nested
    /// `Result<string, Refusal list>` as its .NET type name, so the diff of a failing run said nothing
    /// at all. A gate whose red is unreadable is most of the way back to a gate that cannot fail.
    let private outcomeOf i g r o =
        match Operation.compose i g r o with
        | Ok key -> $"composed %s{key.Value}"
        | Error refusals -> refusals |> List.map (sprintf "%A") |> String.concat "; "

    /// Ninety tuples: every component varied over several values, and every case of the vocabulary
    /// represented. The count is asserted so the set cannot silently shrink under a later edit.
    let private tuples =
        [ for i in [ "FS-GG/.github#2311"; "FS-GG/.github#2312"; "FS-GG/FS.GG.Net#58" ] do
            for g in [ "1"; "5304654156"; "5304654157" ] do
                for r in [ "FS-GG/.github"; "FS-GG/FS.GG.Net" ] do
                    for o in
                        [ Operation.Merge
                          Operation.Dispatch "kit-materialize"
                          Operation.Dispatch "kit-publish"
                          Operation.Publish "FS.GG.Kit"
                          Operation.Publish "FS.GG.Drivers" ] do
                        yield (i, g, r, o) ]

    [<Fact>]
    let ``#2311 the same tuple composes the same lowercase 64-hex key every time`` () =
        let first = keyOf item generation receiver Operation.Merge
        let second = keyOf item generation receiver Operation.Merge
        Assert.Equal(first, second)
        Assert.Equal(64, first.Length)
        Assert.True(first |> Seq.forall (fun c -> Char.IsAsciiDigit c || (c >= 'a' && c <= 'f')), first)

    [<Fact>]
    let ``#2311 the key is exactly sha256 of the documented pre-image, recomputed independently`` () =
        // This is the assertion a CONSTANT digest cannot survive, and it also pins the pre-image's
        // field order and separator to §3.3's `sha256(item \n gen \n receiver \n op)` rather than to
        // whatever the implementation happens to do.
        let expectedPreimage = "FS-GG/.github#2311\n5304654156\nFS-GG/FS.GG.Net\ndispatch:kit-materialize"
        Assert.Equal(expectedPreimage, preimageOf item generation receiver (Operation.Dispatch "kit-materialize"))

        let expectedKey =
            expectedPreimage
            |> Encoding.UTF8.GetBytes
            |> SHA256.HashData
            |> Convert.ToHexString
            |> fun digest -> digest.ToLowerInvariant()

        Assert.Equal(expectedKey, keyOf item generation receiver (Operation.Dispatch "kit-materialize"))

    [<Fact>]
    let ``#2311 one differing component gives a differing key, for each of the four in turn`` () =
        let baseline = keyOf item generation receiver (Operation.Dispatch "kit-materialize")

        Assert.NotEqual<string>(
            baseline,
            keyOf "FS-GG/FS.GG.Net#2311" generation receiver (Operation.Dispatch "kit-materialize")
        )

        Assert.NotEqual<string>(baseline, keyOf item "5304654157" receiver (Operation.Dispatch "kit-materialize"))
        Assert.NotEqual<string>(baseline, keyOf item generation "FS-GG/.github" (Operation.Dispatch "kit-materialize"))
        Assert.NotEqual<string>(baseline, keyOf item generation receiver (Operation.Dispatch "kit-publish"))
        Assert.NotEqual<string>(baseline, keyOf item generation receiver (Operation.Publish "kit-materialize"))
        Assert.NotEqual<string>(baseline, keyOf item generation receiver Operation.Merge)

    [<Fact>]
    let ``#2311 pre-images and keys are pairwise distinct across the whole varied set`` () =
        Assert.Equal(90, List.length tuples)

        let preimages = tuples |> List.map (fun (i, g, r, o) -> preimageOf i g r o)
        Assert.Equal(90, preimages |> List.distinct |> List.length)

        let keys = tuples |> List.map (fun (i, g, r, o) -> keyOf i g r o)
        Assert.Equal(90, keys |> List.distinct |> List.length)

    [<Fact>]
    let ``#2311 separator injection cannot forge a colliding pre-image, because the domain excludes it`` () =
        // THE FIRST HALF OF THE CONSTRUCTION — the `String.concat` stage. The encoder stage has its own
        // test, `…an unpaired surrogate is refused…`, and neither clause is sufficient alone.
        //
        // Stated as the two tuples it rules out. These differ, yet
        // String.concat "\n" would render ONE pre-image for both if any component could carry the
        // separator:
        //   ("FS-GG/.github#2311\n7", "8",    "FS-GG/FS.GG.Net", Merge)
        //   ("FS-GG/.github#2311",    "7\n8", "FS-GG/FS.GG.Net", Merge)
        // Both render "FS-GG/.github#2311\n7\n8\nFS-GG/FS.GG.Net\nmerge". Both are refused before any
        // hashing happens, which is exactly why the concatenation is injective over the ADMITTED
        // domain and why the distinctness above is a consequence rather than a sample.
        //
        // WHAT THIS REFUSAL IS AND IS NOT LOAD-BEARING FOR, stated precisely because the mutation that
        // inverts it showed the difference. Dropping `separatorFree` does not by itself open the
        // collision above: `item`, `generation` and `receiver` each independently exclude a newline
        // through their own format rules (`owner/repo#N`, decimal id, `owner/repo`), so under that
        // mutation those two tuples are still refused — with a DIFFERENT refusal. That is defence in
        // depth, not redundancy to delete. The component where this rule is the only guard is the
        // operation payload, and there the rule is forward-looking rather than currently exploitable:
        // `op` renders LAST, so a newline in it cannot forge a different four-field pre-image today,
        // but it would the moment a fifth field is appended. The property is therefore stated over the
        // DOMAIN — no admitted component carries the separator — rather than over the field order that
        // happens to hold now.
        // ALL FOUR LEGS ARE EVALUATED BEFORE THE ASSERT, and that shape is deliberate. Written as four
        // consecutive `Assert.Equal` calls, xUnit stops at the first failure — so under the mutation
        // that inverts this gate only the FIRST leg was ever measured, and the payload leg, which is
        // the one the paragraph above turns on, was reported by nobody. An independent critic caught
        // exactly that on this module's first head. Collecting the observations first and comparing
        // once means every leg is computed, and a failure shows the whole list rather than its head.
        let observed =
            [ outcomeOf "FS-GG/.github#2311\n7" "8" receiver Operation.Merge
              outcomeOf item "7\n8" receiver Operation.Merge
              outcomeOf item generation "FS-GG/FS.GG.Net\r" Operation.Merge
              outcomeOf item generation receiver (Operation.Dispatch "kit\tmaterialize") ]

        Assert.Equal<string list>(
            [ "ControlCharacter Item"
              "ControlCharacter Generation"
              "ControlCharacter Receiver"
              "ControlCharacter OperationPayload" ],
            observed
        )

    [<Fact>]
    let ``#2311 an unpaired surrogate is refused, because UTF-8 would collapse it into one key`` () =
        // THE SECOND HALF OF THE CONSTRUCTION, and the one this module's first head was missing.
        // `Encoding.UTF8.GetBytes` uses the REPLACEMENT fallback, so UTF8("r\uD800") and
        // UTF8("r\uDC00") are both `72 EF BF BD`. Nothing else in the chain refuses a surrogate —
        // Char.IsControl, Char.IsWhiteSpace and the owner/repo rules are all false for one — so these
        // two items were admitted, rendered genuinely DIFFERENT pre-images, and composed ONE key.
        // The surrogates are built at RUNTIME, and that is load-bearing rather than stylistic: the F#
        // compiler folds a lone `\uD800` escape in a string literal to U+FFFD before it ever reaches
        // the runtime — measured, `"r\uD800" |> Seq.map int` yields `[114; 65533]` — so a test written
        // with literals would compare two identical replacement characters and pass vacuously while
        // asserting nothing. `char 0xD800` survives.
        let highOnly = "FS-GG/r" + string (char 0xD800) + "#2311"
        let lowOnly = "FS-GG/r" + string (char 0xDC00) + "#2311"

        Assert.NotEqual<string>(highOnly, lowOnly)
        Assert.Equal(0xD800, int highOnly[7])
        Assert.Equal(0xDC00, int lowOnly[7])

        // The collapse itself, measured rather than asserted: two different strings, one byte sequence.
        Assert.Equal<byte[]>(Encoding.UTF8.GetBytes highOnly, Encoding.UTF8.GetBytes lowOnly)

        // Every component, not only the item — a lone surrogate anywhere reaches the same encoder — and
        // a REVERSED pair (low before high) is unpaired too. All six legs are evaluated before the
        // single comparison, for the reason the separator test states.
        let observed =
            [ outcomeOf highOnly generation receiver Operation.Merge
              outcomeOf lowOnly generation receiver Operation.Merge
              outcomeOf item ("530" + string (char 0xD800) + "4654156") receiver Operation.Merge
              outcomeOf item generation ("FS-GG/FS.GG.Net" + string (char 0xDC00)) Operation.Merge
              outcomeOf item generation receiver (Operation.Dispatch("kit" + string (char 0xD800) + "materialize"))
              outcomeOf item generation receiver (Operation.Publish(string (char 0xDC00) + string (char 0xD800))) ]

        Assert.Equal<string list>(
            [ "UnpairedSurrogate Item"
              "UnpairedSurrogate Item"
              "UnpairedSurrogate Generation"
              "UnpairedSurrogate Receiver"
              "UnpairedSurrogate OperationPayload"
              "UnpairedSurrogate OperationPayload" ],
            observed
        )

    [<Fact>]
    let ``#2311 a WELL-FORMED astral character is admitted, and keys distinctly`` () =
        // The refusal above is exactly as wide as the guarantee needs and no wider. A well-formed
        // surrogate PAIR is UTF-8-encodable and distinguishable, so banning `Char.IsSurrogate`
        // outright would shrink the admitted domain for nothing. This test is what stops the fix
        // drifting into that blanket ban: it fails if the predicate ever refuses a valid pair.
        let astral = "🚀" // U+1F680
        Assert.Equal(2, astral.Length)

        let withAstral = keyOf item generation receiver (Operation.Publish astral)
        let withoutAstral = keyOf item generation receiver (Operation.Publish "rocket")
        Assert.NotEqual<string>(withAstral, withoutAstral)

        // And its own high half ALONE is refused, so admission is precision rather than indifference.
        Assert.Equal<Operation.Refusal list>(
            [ Operation.UnpairedSurrogate Operation.OperationPayload ],
            refusalsOf item generation receiver (Operation.Publish(string astral[0]))
        )

    [<Fact>]
    let ``#2311 the operation vocabulary is closed at exactly the three cases the design names`` () =
        // A TRIPWIRE, described as one rather than as a proof. The real closedness is the compiler's:
        // `Operation.wire` matches without a wildcard, and both projects set TreatWarningsAsErrors with
        // an empty WarningsNotAsErrors, so FS0025 is an error and a fourth case breaks the build at
        // every consumer that does not handle it. A test cannot compile a hypothetical fourth case, so
        // what this one guarantees is narrower and still worth having: a case added without revisiting
        // this contract reds a named test instead of passing silently.
        let cases =
            FSharpType.GetUnionCases typeof<Operation.Op>
            |> Array.map (fun case -> case.Name)
            |> Array.sort

        Assert.Equal<string[]>([| "Dispatch"; "Merge"; "Publish" |], cases)

    [<Fact>]
    let ``#2311 wire spells the vocabulary exactly as the design's component table gives it`` () =
        Assert.Equal("merge", Operation.wire Operation.Merge)
        Assert.Equal("dispatch:kit-materialize", Operation.wire (Operation.Dispatch "kit-materialize"))
        Assert.Equal("publish:FS.GG.Kit", Operation.wire (Operation.Publish "FS.GG.Kit"))

        // Injective over the vocabulary: `merge` carries no colon and the other two carry different
        // prefixes, so a Dispatch payload can never spell a Publish and neither can spell a Merge.
        let spellings =
            [ Operation.Merge
              Operation.Dispatch "merge"
              Operation.Dispatch "FS.GG.Kit"
              Operation.Publish "merge"
              Operation.Publish "FS.GG.Kit" ]
            |> List.map Operation.wire

        Assert.Equal(5, spellings |> List.distinct |> List.length)

    [<Fact>]
    let ``#2311 composition refuses the board's repo-hash shorthand for the item`` () =
        // `.github#2107`: the board's `<repo>#N` shorthand is not GitHub grammar, and the design
        // requires `item` to be spelled `owner/repo#N` for exactly that reason.
        Assert.Equal<Operation.Refusal list>(
            [ Operation.ItemNotFullyQualified ".github#2311" ],
            refusalsOf ".github#2311" generation receiver Operation.Merge
        )

        Assert.Equal<Operation.Refusal list>(
            [ Operation.ItemNotFullyQualified "FS-GG/.github" ],
            refusalsOf "FS-GG/.github" generation receiver Operation.Merge
        )

        Assert.Equal<Operation.Refusal list>(
            [ Operation.ItemNotFullyQualified "FS-GG/.github#0" ],
            refusalsOf "FS-GG/.github#0" generation receiver Operation.Merge
        )

    [<Fact>]
    let ``#2311 composition refuses a generation that is not a server-assigned comment id`` () =
        // `released` is the engine's ABSENCE sentinel — what ClaimGeneration holds when no marker is
        // held — so a key composed on it would name a tenancy that does not exist. Refusing it is the
        // `#266` rule applied here: a failed or absent read must not become a legitimate answer.
        for candidate in [ "released"; "abc"; "0012"; "-1"; "53046 54156" ] do
            Assert.Equal<Operation.Refusal list>(
                [ Operation.GenerationNotServerAssigned candidate ],
                refusalsOf item candidate receiver Operation.Merge
            )

    [<Fact>]
    let ``#2311 composition refuses a receiver that is not owner-slash-repo`` () =
        for candidate in [ "FS.GG.Net"; "FS-GG/FS.GG.Net#58"; "FS-GG/a/b" ] do
            Assert.Equal<Operation.Refusal list>(
                [ Operation.ReceiverNotFullyQualified candidate ],
                refusalsOf item generation candidate Operation.Merge
            )

    [<Fact>]
    let ``#2311 every blank component is refused, and all of them are reported rather than the first`` () =
        Assert.Equal<Operation.Refusal list>(
            [ Operation.Blank Operation.Item
              Operation.Blank Operation.Generation
              Operation.Blank Operation.Receiver
              Operation.Blank Operation.OperationPayload ],
            refusalsOf "" "  " "" (Operation.Dispatch "")
        )

        // Merge carries no payload, so it can never produce an OperationPayload refusal.
        Assert.Equal<Operation.Refusal list>(
            [ Operation.Blank Operation.Item ],
            refusalsOf "" generation receiver Operation.Merge
        )

        Assert.Equal<Operation.Refusal list>(
            [ Operation.Blank Operation.OperationPayload ],
            refusalsOf item generation receiver (Operation.Publish " ")
        )

    [<Fact>]
    let ``#2311 every refusal names the component it is about`` () =
        Assert.Contains("item", Operation.describe (Operation.Blank Operation.Item))
        Assert.Contains("generation", Operation.describe (Operation.Blank Operation.Generation))
        Assert.Contains("receiver", Operation.describe (Operation.Blank Operation.Receiver))
        Assert.Contains("operation payload", Operation.describe (Operation.Blank Operation.OperationPayload))
        Assert.Contains("control character", Operation.describe (Operation.ControlCharacter Operation.Item))
        Assert.Contains("receiver", Operation.describe (Operation.UnpairedSurrogate Operation.Receiver))
        Assert.Contains("unpaired surrogate", Operation.describe (Operation.UnpairedSurrogate Operation.Receiver))
        Assert.Contains(".github#2311", Operation.describe (Operation.ItemNotFullyQualified ".github#2311"))
        Assert.Contains("FS.GG.Net", Operation.describe (Operation.ReceiverNotFullyQualified "FS.GG.Net"))
        Assert.Contains("released", Operation.describe (Operation.GenerationNotServerAssigned "released"))

    [<Fact>]
    let ``#2311 the pure core's compiled reference graph names no transport or GitHub assembly`` () =
        // Acceptance criterion 4 says "Demonstrate by the project's own reference graph, not by
        // inspection", so this reads the COMPILED assembly rather than the source text.
        //
        // The boundary this cannot cross, stated rather than implied: FS.GG.Coord.GitHub REFERENCES
        // FS.GG.Coord.Core, so the reverse edge would be a compile-time circularity and could never
        // appear here. What this assertion actually guards is a transport or HTTP client pulled in
        // DIRECTLY by a later edit to this project.
        //
        // The FS.GG. clause is a rule rather than a name list, so a sibling assembly that does not
        // exist yet is excluded without editing this test.
        let referenced =
            typeof<Operation.OpKey>.Assembly.GetReferencedAssemblies()
            |> Array.map (fun name -> name.Name)
            |> Array.map (fun name -> if isNull name then "" else name)

        Assert.NotEmpty referenced

        let forbidden =
            referenced
            |> Array.filter (fun name ->
                name.StartsWith("FS.GG.", StringComparison.OrdinalIgnoreCase)
                || [ "Http"; "Octokit"; "GitHub"; "WebSocket" ]
                   |> List.exists (fun token -> name.Contains(token, StringComparison.OrdinalIgnoreCase)))

        Assert.Equal<string[]>([||], forbidden)
