namespace FS.GG.Coord.Cli.Tests

open System.Text.Json
open System.Text.RegularExpressions
open Microsoft.FSharp.Reflection
open Xunit
open FS.GG.Coord.Cli
open FS.GG.Coord.Cli.Options

/// THE SURFACE INVENTORY (#869).
///
/// `OptionsTests` asserts the engine REFUSES what it does not know. Nothing asserted what it must
/// KNOW — so deleting a verb made the engine *more* compliant with its own suite. `fsgg-coord add`
/// was refused cleanly, exit 1, precisely as specified, while 470 tests stayed green and the
/// protocol's boarding verb did not exist (#846/#861). That is #266's class — a gate that fails open
/// — landing on the port built to end it.
///
/// Nothing upstream could catch it either, because THERE WAS NO LIST. ADR-0034 and ADR-0040 do not
/// enumerate the surface; #767 enumerated its own scope by hand (fifteen verbs, against bash's
/// thirty-two) with no artifact to diff against; and the corpus that was the de facto inventory was
/// retired by #835.
///
/// So this file IS the missing artifact. The point is NOT to freeze the surface — it is to make
/// shrinking it cost a line in a diff instead of costing nothing.
module CommandSurfaceTests =

    /// The declared command surface: every verb the engine dispatches, and what it dispatches to.
    ///
    /// A verb leaves the surface only by leaving THIS LIST — a decision in the diff, reviewable,
    /// exactly the standard ADR-0040's D.4 disposition manifest set for the five assertions it did
    /// dispose of on the record. Adding a verb to the engine without adding it here is caught by the
    /// DU cross-check below, so the list cannot silently rot in either direction.
    let private surface: (string * Command) list =
        [
          // DECISION — pure; read state on stdin and touch no network (ADR-0034).
          "decide", Decide
          "delivery", DeliveryCmd
          "review", ReviewCmd
          "driver", DriverCmd
          "cycle", CycleCmd
          "scan", Scan
          "lanes", LanesView
          "facts", Facts
          "command-contract", CommandContractCmd
          "intake", IntakeCmd
          "delivery-route", RouteCmd

          // IO — the client surface the shim execs in place of bash (ADR-0040 Phase D).
          "whoami", WhoAmI
          "budget", Budget
          "next", Next
          "batch", BatchCmd
          "ready", Ready
          "reconcile", Reconcile
          "who", Who
          "reap", Reap
          "claim", Claim
          "adopt", Adopt
          "landable", Landable
          "take", Take
          "release", Release
          "heartbeat", Heartbeat
          "add", Add
          "set-field", SetField
          "child", Child
          "widen", Widen
          "set-paths", SetPaths
          "overlap", Overlap
          "say", Say
          "inbox", Inbox
          "done", DoneCmd
          "verify-paths", VerifyPaths
          // #862/#878, landed on main while this gate was being written — and caught BY it: CI builds the
          // merge ref, so the DU cross-check went red naming `Flush` on this PR's first run. The verb
          // entered the surface, so it cost a line here. That is the whole point (#869).
          "flush", Flush

          // The #418 board-map plumbing.
          "bootstrap", Bootstrap
          "board", BoardCmd
          "field-id", FieldId
          "option-id", OptionId
          "item-id", ItemId
          // `.github#2477`: the metered `userContentEdits` read the independent-review contract's
          // body-edit provenance check names as authoritative.
          "body-edits", BodyEdits

          // The board-health gate (#496) and the REST listing (#446).
          "lint", LintCmd
          "issues", Issues

          // The follow-up queue (#1063) — LOCAL, like `whoami`: a file, no board, no token.
          "followup", Followup

          // The ADR-0050 registry-predicate oracle (#1202) — LOCAL: reads registry + producer manifests,
          // no board, no token.
          "predicate", Predicate
          "diff-audit", DiffAudit

          // Coordination rooms (ADR-0051, #1215). The ONE two-word verb — a `room` namespace so
          // `room close`/`room list` have a home; the dispatch check below splits on whitespace for it.
          "room open", RoomOpen ]

    /// The two commands with no verb form — they are reached by flag (`--help`, `--version`), so the
    /// verb inventory cannot account for them and the DU cross-check must be told so explicitly.
    let private flagDispatched = [ Help; Version ]

    /// Verbs that are DISPATCHED but absent from `usage` — a verb that works and is invisible. Each entry
    /// is a LIVE DEFECT, not an exemption: the test below asserts this set is EXACTLY the undocumented
    /// one, so documenting a verb goes red until its line here is deleted, and a newly-undocumented verb
    /// goes red on arrival.
    ///
    /// EMPTY, and it earned that. `adopt` (#697) sat here when #869 landed: dispatched since #697,
    /// reasoned about by #712, and absent from `usage` — but the one-line fix lived in `Options.fs`,
    /// held by a live claim on #862, so #869 could only record it. #891 documented it, and this set going
    /// red is what made the second half of that fix non-optional (the PR could not merge until the entry
    /// came out). Empty is the honest resting state: every verb the engine dispatches, `--help` names.
    let private knownUndocumented: Set<string> = Set.empty

    let private caseName (c: Command) =
        FSharpValue.GetUnionFields(c, typeof<Command>) |> fst |> fun i -> i.Name

    /// Does the usage block document this verb? Anchored to the two-space command column, NOT a
    /// substring search: `usage.Contains "board"` is true of "the board as a reconciler sees it", and a
    /// documentation gate that matches prose is a gate that reports green over a subject it never read.
    let private documented (verb: string) =
        Regex.IsMatch(usage, $@"^  {Regex.Escape verb}(\s|$)", RegexOptions.Multiline)

    [<Fact>]
    let ``every verb on the declared surface is DISPATCHED — the assertion the suite never had`` () =
        // The inverse of `an unknown command is named and refused`. Bare verbs: arity is each command's
        // own business (Program/Client refuse the wrong count), so dispatch is a parse-level property —
        // which is what keeps this sub-second and offline, per OptionsTests' existing shape.
        let missing =
            surface
            |> List.choose (fun (verb, expected) ->
                // Split on whitespace: a multi-word verb (`room open`, ADR-0051) is dispatched by its
                // token sequence, not by a single argv element. Single-word verbs are unaffected.
                match parse (verb.Split(' ') |> Array.toList) with
                | Ok o when o.Command = expected -> None
                | Ok o -> Some $"%s{verb} → %A{o.Command} (expected %A{expected})"
                | Error e -> Some $"%s{verb} → REFUSED: %s{e}")

        // Reported together: one red test naming every dropped verb beats N runs of find-the-next-one.
        Assert.True(
            List.isEmpty missing,
            "the engine no longer dispatches every verb it declares — a verb leaves the surface only by "
            + "leaving `surface` (#869):\n  "
            + String.concat "\n  " missing
        )

    [<Fact>]
    let ``the inventory covers the Command DU — a verb ADDED to the engine must be added to the list`` () =
        // The other direction, and the reason the list cannot rot: an inventory nobody is forced to
        // extend is one that describes last year's engine. Reflection over the DU is the only source of
        // truth here that the inventory itself cannot drift from.
        let declared =
            (surface |> List.map (snd >> caseName)) @ (flagDispatched |> List.map caseName) |> Set.ofList

        let actual =
            FSharpType.GetUnionCases typeof<Command> |> Array.map (fun c -> c.Name) |> Set.ofArray

        // (declared, actual) in that order: the inventory is the SPEC, the DU is what the engine really
        // has, so a verb added without a line here reads as "Actual: [… Flush …]" — the name of the thing
        // to go declare.
        Assert.Equal<Set<string>>(declared, actual)

    [<Fact>]
    let ``--help documents every verb on the surface — a command that works and is invisible is a gap`` () =
        // The `adopt` class. An undocumented verb is not a surface gap, but it is the same failure one
        // layer out: the caller cannot ask for what nothing tells them exists.
        let gaps = surface |> List.map fst |> List.filter (documented >> not) |> Set.ofList

        Assert.Equal<Set<string>>(knownUndocumented, gaps)

    [<Fact>]
    let ``no verb is declared twice — a duplicated line is a merge artifact, not a surface`` () =
        // Cheap, and it keeps the two set-equality tests above honest: List/Set conversions swallow a
        // duplicate silently, so a botched rebase could grow the list without growing the surface.
        let verbs = surface |> List.map fst
        Assert.Equal<string list>(List.distinct verbs, verbs)

    [<Fact>]
    let ``the machine-readable command contract covers the declared surface`` () =
        use doc = JsonDocument.Parse(renderCommandContract ())
        Assert.Equal("fsgg.coord.commands/1", doc.RootElement.GetProperty("schema").GetString())

        let emitted =
            doc.RootElement.GetProperty("commands").EnumerateArray()
            |> Seq.map (fun row -> row.GetProperty("name").GetString())
            |> Set.ofSeq

        Assert.Equal<Set<string>>(surface |> List.map fst |> Set.ofList, emitted)

    /// The emitted contract, as `{command -> flag set}` — the shape every gate downstream reads it in
    /// (`scripts/check-skill-quality.py` builds exactly this to audit documented invocations).
    let private emittedFlags () =
        let doc = JsonDocument.Parse(renderCommandContract ())

        doc.RootElement.GetProperty("commands").EnumerateArray()
        |> Seq.map (fun row ->
            row.GetProperty("name").GetString(),
            (row.GetProperty("flags").EnumerateArray() |> Seq.map _.GetString() |> Set.ofSeq))
        |> Map.ofSeq

    /// The render flags named on a command's anchored usage line. This intentionally reads the human
    /// surface rather than `scopeOf`: the emitted contract is the typed source of truth, and this test is
    /// the boundary that makes its promises visible to someone running `--help`.
    let private usageRenderFlags (verb: string) =
        let line =
            Regex.Match(usage, $@"^  {Regex.Escape verb}(?:\s|$)[^\r\n]*", RegexOptions.Multiline)

        Assert.True(line.Success, $"%s{verb}: no anchored usage line to compare with its contract")

        Regex.Matches(line.Value, @"--(?:json|text)")
        |> Seq.map _.Value
        |> Set.ofSeq

    /// Render-capable commands whose established usage form intentionally does not spell every projection.
    /// This is an explicit migration boundary, not a second contract: the assertion below requires this
    /// list to equal the observed residue, so a new omission cannot hide here and documenting one makes
    /// the exemption itself fail until it is removed. #1548's four commands are deliberately absent.
    let private renderUsageExemptions =
        set
            [ "scan"
              "whoami"
              "budget"
              "next"
              "reconcile"
              "who"
              "reap"
              "claim"
              "landable"
              "take"
              "release"
              "heartbeat"
              "add"
              "set-field"
              "child"
              "widen"
              "set-paths"
              "overlap"
              "say"
              "inbox"
              "done"
              "verify-paths"
              "flush"
              "bootstrap"
              "board"
              "field-id"
              "option-id"
              "item-id"
              "lint"
              "issues"
              "followup"
              "room open" ]

    /// Every command the contract is emitted for, paired with its declared render support.
    let private contractCommands =
        surface |> List.map (fun (verb, command) -> verb, command, renderSupport command)

    [<Fact>]
    let ``#1523 the contract advertises --json exactly where a JSON projection EXISTS`` () =
        // THE DEFECT THIS PINS, and it is the one nothing in this file could see. `--json` was `Global` in
        // `scopeOf` and `renderCommandContract` spliced it onto EVERY row unconditionally, so the emitted
        // surface promised a machine projection on all 40 commands. Fourteen branched on `opts.Render`;
        // four printed JSON regardless; the other TWENTY printed the same prose with the flag as without,
        // exited 0, and told the caller by that exit that a thing had happened which had not. That is
        // #867/#991's "accepted and ignored" — the very defect the residue rule exists to end — surviving
        // inside the one flag the rule exempted itself from.
        //
        // WHY THIS IS DERIVED AND NOT A LIST. `renderSupport` is the single hand-written fact about the
        // renderers; `scopeOf`, the contract emitter and the bare-form default all read it. A second copy
        // of the honouring set here — the obvious way to write this test — would be free to drift from the
        // engine exactly as the three copies it replaces drifted from each other, which is the shape five
        // separate items on this board have now been filed against (#1507, #1510, #1515, #1528). So the
        // assertion compares the EMITTED contract against the DECLARATION, and the only way to change what
        // is advertised is to change what the engine says it can do.
        let emitted = emittedFlags ()

        let wrong =
            contractCommands
            |> List.choose (fun (verb, _, support) ->
                let advertised = emitted.[verb].Contains "--json"
                let hasProjection = support <> TextOnly

                if advertised = hasProjection then
                    None
                elif advertised then
                    Some $"%s{verb}: advertises --json and has NO JSON projection (%A{support})"
                else
                    Some $"%s{verb}: has a JSON projection (%A{support}) and does not advertise --json")

        Assert.True(
            List.isEmpty wrong,
            "the emitted surface and the renderers disagree about `--json` (#1523):\n  "
            + String.concat "\n  " wrong
        )

    [<Fact>]
    let ``#1523 the contract advertises --text exactly where a HUMAN projection EXISTS`` () =
        // The mirror, and it is not symmetry for its own sake. `--json` and `--text` are two promises, and
        // a command can keep one without the other: `issues` and `board` emit a raw machine document
        // whatever you ask for, so `--text` on them was the same broken promise pointing the other way.
        // Modelling them as one flag would have forced one of the two halves to keep lying.
        let emitted = emittedFlags ()

        let wrong =
            contractCommands
            |> List.choose (fun (verb, _, support) ->
                let advertised = emitted.[verb].Contains "--text"
                let hasProjection = support <> JsonOnly

                if advertised = hasProjection then
                    None
                else
                    Some $"%s{verb}: --text advertised=%b{advertised}, projection exists=%b{hasProjection} (%A{support})")

        Assert.True(
            List.isEmpty wrong,
            "the emitted surface and the renderers disagree about `--text` (#1523):\n  "
            + String.concat "\n  " wrong
        )

    [<Fact>]
    let ``#1548 usage names exactly the render flags emitted in the command contract`` () =
        // The contract is deliberately derived from the parser/render declaration, not from this prose.
        // Comparing both directions catches the two real failure modes: a flag callers can use but cannot
        // discover, and a help promise that the engine does not honour. No command needs an exemption:
        // each emitted row has an anchored usage line, including the one multi-word verb.
        let emitted = emittedFlags ()

        let disagreements =
            contractCommands
            |> List.choose (fun (verb, _, _) ->
                let expected = emitted.[verb] |> Set.filter (fun flag -> flag = "--json" || flag = "--text")
                let actual = usageRenderFlags verb

                if actual = expected then
                    None
                else
                    Some(verb, $"%s{verb}: usage has %A{Set.toList actual}; contract emits %A{Set.toList expected}"))

        let exempted = disagreements |> List.map fst |> Set.ofList

        Assert.Equal<Set<string>>(renderUsageExemptions, exempted)

        let unexpected =
            disagreements
            |> List.filter (fst >> renderUsageExemptions.Contains >> not)
            |> List.map snd

        Assert.True(
            List.isEmpty unexpected,
            "usage and the emitted render contract disagree outside explicit exemptions (#1548):\n  "
            + String.concat "\n  " unexpected
        )

    [<Fact>]
    let ``#1523 what the contract advertises is exactly what the parser ACCEPTS`` () =
        // The round trip, and the reason the two tests above are worth anything. They compare the emitted
        // document to `renderSupport`; this one compares it to the PARSER, so a contract that agreed with
        // the declaration while the residue rule refused something else — an emitter and a gate reading
        // two different tables, which is precisely the state #1523 found them in — cannot pass.
        //
        // Every command, both spellings, both directions: advertised ⇒ parses, unadvertised ⇒ REFUSED.
        let emitted = emittedFlags ()

        // STRICT IN BOTH DIRECTIONS, and deliberately not "any error that is not the residue message
        // counts as acceptance". That weaker predicate is sound today only because `parse` checks arity
        // nowhere in the bare form — so the moment a command gains one (the natural next step for
        // `landable`, `field-id`, `issues`, all of which currently parse bare with no positionals), that
        // command's row would silently stop testing anything in the advertised direction. A gate that
        // quietly narrows its own subject is the #266 shape. So: advertised MUST parse `Ok`, and
        // unadvertised MUST produce the residue refusal by name. Anything else is a finding.
        let disagreements =
            [ for verb, _, _ in contractCommands do
                  for flag in [ "--json"; "--text" ] do
                      let advertised = emitted.[verb].Contains flag
                      let result = parse ((verb.Split(' ') |> Array.toList) @ [ flag ])

                      match advertised, result with
                      | true, Ok _ -> ()
                      | true, Error e -> yield $"%s{verb} %s{flag}: advertised, but parse REFUSED it: %s{e}"
                      | false, Error e when e.Contains $"%s{flag} is not a flag of" -> ()
                      | false, Ok _ -> yield $"%s{verb} %s{flag}: NOT advertised, and the parser accepted it"
                      | false, Error e ->
                          yield
                              $"%s{verb} %s{flag}: NOT advertised, and the refusal is about something else "
                              + $"— the flag went unjudged: %s{e}" ]

        Assert.True(
            List.isEmpty disagreements,
            "the emitted contract and the parser disagree about the render flags (#1523):\n  "
            + String.concat "\n  " disagreements
        )

    /// The emitted rows, as `{command -> the whole row}` — the write-ness assertions below need `writes`
    /// and `writesWhen` together, and reading the row once keeps them talking about the same object.
    let private emittedRows () =
        let doc = JsonDocument.Parse(renderCommandContract ())
        let rows = doc.RootElement.GetProperty("commands").EnumerateArray() |> Seq.toList

        let byName =
            rows |> List.map (fun row -> row.GetProperty("name").GetString(), row.Clone()) |> Map.ofList

        // `Map.ofList` KEEPS THE LAST OF A DUPLICATE KEY AND SAYS NOTHING, so two rows named `widen` — a
        // botched rebase in the emitter, or a `commandName` collision — would be invisible to every
        // assertion built on this. That is the same swallow `no verb is declared twice` above guards
        // `surface` against; the emitted document deserves it too.
        Assert.Equal(List.length rows, Map.count byName)
        byName

    [<Fact>]
    let ``#1534 every verb on the Command union declares write-ness — no verb defaults to READ`` () =
        // TOTAL OVER THE DU. The emitter reflects over the `Command` union itself and excludes only
        // `Help`/`Version`, so the row set is the union's — which is why this walks the union rather than
        // `surface`. The two outer arms below are belt-and-braces and are unreachable today: the DU
        // cross-check above already pins `surface ∪ flagDispatched == the union`, and the coverage test
        // already pins the emitted names against `surface`. They are kept because they cost a line and
        // name the right thing if either of those is ever relaxed; the LIVE assertion is the `writes` key.
        //
        // WHAT ENFORCES THE "no verb defaults to READ" HALF IS THE COMPILER, NOT THIS TEST, and the
        // distinction is worth stating rather than blurring. `writeSurface` is a total match with NO
        // wildcard arm and this project sets `TreatWarningsAsErrors`, so a verb added to the union with no
        // row is FS0025 — a build error. A future `| _ -> Reads` arm would give that away, and this test
        // would NOT catch it: the new verb would still emit a `writes` key, valued `never`, and every
        // assertion below would pass. Nothing in a test project can see the shape of a match arm. So the
        // guarantee is structural and its custodian is the diff — which is why the arm carries a comment
        // in `Options.fs` naming itself the most damaging edit that file accepts.
        //
        // What this DOES buy, and it is the half the compiler cannot: the emitted DOCUMENT is total. A
        // missing `writes` key and a `writes` the engine cannot spell are the same finding — a consumer
        // that cannot read the field falls back to its own default, and "no answer" being read as "does
        // not write" is the exact failure this field exists to end.
        let rows = emittedRows ()

        let expected =
            FSharpType.GetUnionCases typeof<Command>
            |> Array.toList
            |> List.map (fun c -> c.Name)
            |> List.except (flagDispatched |> List.map caseName)

        let verbOf = surface |> List.map (fun (verb, c) -> caseName c, verb) |> Map.ofList

        let bad =
            expected
            |> List.choose (fun case ->
                match Map.tryFind case verbOf with
                | None -> Some $"%s{case}: on the Command union and not on `surface` — nothing emits it"
                | Some verb ->
                    match Map.tryFind verb rows with
                    | None -> Some $"%s{verb}: no row in the emitted contract"
                    | Some row ->
                        match row.TryGetProperty "writes" with
                        | false, _ -> Some $"%s{verb}: emitted with NO `writes` field"
                        | true, v ->
                            match v.GetString() with
                            | "always"
                            | "never"
                            | "conditional" -> None
                            | other -> Some $"%s{verb}: `writes` is %s{other}, which is not a value of the field")

        Assert.True(
            List.isEmpty bad,
            "the emitted contract does not declare write-ness for every verb (#1534):\n  "
            + String.concat "\n  " bad
        )

    [<Fact>]
    let ``#1534 a conditional write names its gate and an unconditional one names none`` () =
        // `writes == "conditional"` and `has("writesWhen")` must be ONE fact asked two ways. Two independent
        // keys are two things that can disagree, and a consumer reading the wrong one of a disagreeing pair
        // is the shape every item this field descends from was filed against.
        let rows = emittedRows ()

        // A SUITE THAT CANNOT FAIL IS NOT A SUITE (#266/#436, and `coord-engine.yml` runs a non-vacuity
        // step for exactly this). Every assertion below is a fold over `rows`, so an empty `rows` — an
        // emitter that stopped emitting — would report GREEN over nothing.
        Assert.NotEmpty rows

        let bad =
            [ for verb, row in Map.toList rows do
                  let writes = row.GetProperty("writes").GetString()
                  let hasGate = fst (row.TryGetProperty "writesWhen")

                  match writes, hasGate with
                  | "conditional", false -> yield $"%s{verb}: conditional, and says nothing about WHEN"
                  | "conditional", true ->
                      // Exactly one gate key, and it must be one this contract defines. A row carrying two
                      // would be read by whichever key a consumer happened to look for first.
                      let gate = row.GetProperty "writesWhen"
                      let keys = gate.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq

                      if keys.Count <> 1 then
                          yield $"%s{verb}: writesWhen carries %d{keys.Count} keys (%A{keys}), not exactly one"
                      elif not (Set.isSubset keys (set [ "flagGiven"; "flagAbsent"; "argvCannotSay" ])) then
                          yield $"%s{verb}: writesWhen names an unknown condition %A{keys}"
                      elif System.String.IsNullOrWhiteSpace(gate.EnumerateObject() |> Seq.head |> _.Value.GetString()) then
                          yield $"%s{verb}: writesWhen's value is blank — a condition nobody can act on"
                  | _, true -> yield $"%s{verb}: writes=%s{writes} and yet carries a writesWhen gate"
                  | _, false -> () ]

        Assert.True(
            List.isEmpty bad,
            "the emitted contract's write-ness and its condition disagree (#1534):\n  "
            + String.concat "\n  " bad
        )

    [<Fact>]
    let ``#1534 a flag-gated write names a flag the command can actually be GIVEN`` () =
        // THE CROSS-CHECK THAT MAKES THE GATE MORE THAN PROSE. The condition is declared as a typed `Flag`
        // and spelled through the one `scopedFlags` table, so this compares it against `scopeOf` — the
        // already-derived, compiler-total flag surface — by way of the row's own `flags` array.
        //
        // Without it, `reap` could be declared gated on `--dry-run` and read perfectly plausibly: a guard
        // would scan argv for a flag `reap` refuses, never find it, and permit every `reap --apply` on a
        // stale engine. The shim's #1528 header calls exactly this "a guard whose correctness depends on
        // argv SHAPE"; if the engine is going to hand out that shape, the shape has to be real.
        let rows = emittedRows ()

        // THE SUBJECT MUST EXIST, AND THIS TEST IS THE ONE THAT NEEDED SAYING SO. Everything below is
        // conditional on a row HAVING a `writesWhen` with a flag key — so a contract that emitted no gates
        // at all would satisfy it perfectly, over an empty subject. That is not hypothetical: while this
        // change was being written, an emitter deliberately broken to drop every `writesWhen` object turned
        // the test above red and left THIS one green. A gate that survives its own incident is the shape
        // #266 names. So the flag-gated verbs are named: `reap` and `reconcile` are the pair #1534 was filed
        // about, `flush` is the opposite polarity, and `delivery` is the head-SHA-guarded merge path.
        let flagGated =
            rows
            |> Map.filter (fun _ row ->
                match row.TryGetProperty "writesWhen" with
                | false, _ -> false
                | true, gate -> fst (gate.TryGetProperty "flagGiven") || fst (gate.TryGetProperty "flagAbsent"))
            |> Map.keys
            |> Set.ofSeq

        Assert.Equal<Set<string>>(set [ "delivery"; "flush"; "reap"; "reconcile" ], flagGated)

        let bad =
            [ for verb, row in Map.toList rows do
                  match row.TryGetProperty "writesWhen" with
                  | false, _ -> ()
                  | true, gate ->
                      let flags = row.GetProperty("flags").EnumerateArray() |> Seq.map _.GetString() |> Set.ofSeq

                      for key in [ "flagGiven"; "flagAbsent" ] do
                          match gate.TryGetProperty key with
                          | false, _ -> ()
                          | true, spelling ->
                              let f = spelling.GetString()

                              if not (flags.Contains f) then
                                  yield
                                      $"%s{verb}: gated on %s{f}, which is NOT a flag the command takes "
                                      + $"(it takes %A{Set.toList flags}) — `scopeOf` and `writeSurface` disagree" ]

        Assert.True(
            List.isEmpty bad,
            "a conditional write is gated on a flag its command refuses (#1534):\n  "
            + String.concat "\n  " bad
        )

    [<Fact>]
    let ``#1534 the five classifications #1528 found WRONG stay decided`` () =
        // NOT a restatement of the emitter. FIVE of these are the exact rows a person got wrong in the
        // shim's bash copy, unnoticed for months (#1528): `set-paths` reached the same `Writes.widen` PATCH
        // as `widen` through the same `updateTouchSet` helper and was called a read; `room` and `reconcile`
        // were called reads; `next` was called a read, and takes the #733 chore lock AFTER printing, so the
        // name is not the evidence; and `bootstrap` was called a WRITE, which would have refused a pair of
        // GraphQL queries on a stale engine for nothing. Moving one now costs a line in a diff.
        //
        // `widen`, `reap` and `batch` are CONTRAST ANCHORS rather than corrections, and each is one edit
        // away from its neighbour's wrong answer: `widen` is `set-paths`'s twin, `reap` is the dry-run
        // polarity `reconcile` shares, and `batch` is `next` uncapped — the pair that proves "scheduler
        // verbs read" is not the rule `next` breaks.
        let rows = emittedRows ()
        let writes verb = rows.[verb].GetProperty("writes").GetString()

        Assert.Equal("always", writes "set-paths")
        Assert.Equal("always", writes "widen")
        Assert.Equal("always", writes "room open")
        Assert.Equal("conditional", writes "reconcile")
        Assert.Equal("conditional", writes "next")
        Assert.Equal("conditional", writes "reap")
        Assert.Equal("never", writes "bootstrap")
        // `batch` is `next` uncapped and makes NO offer — the pair is the whole reason `next` needs a row
        // of its own rather than "scheduler verbs write".
        Assert.Equal("never", writes "batch")

    [<Fact>]
    let ``the machine contract preserves dangerous option polarities`` () =
        use doc = JsonDocument.Parse(renderCommandContract ())

        let flags command =
            doc.RootElement.GetProperty("commands").EnumerateArray()
            |> Seq.find (fun row -> row.GetProperty("name").GetString() = command)
            |> fun row ->
                row.GetProperty("flags").EnumerateArray()
                |> Seq.map _.GetString()
                |> Set.ofSeq

        Assert.Contains("--paths", flags "widen")
        Assert.Contains("--paths", flags "set-paths")
        Assert.DoesNotContain("--apply", flags "widen")
        Assert.DoesNotContain("--apply", flags "set-paths")
        Assert.Contains("--apply", flags "reap")
        Assert.DoesNotContain("--dry-run", flags "reap")
        Assert.Contains("--dry-run", flags "flush")
        Assert.DoesNotContain("--apply", flags "flush")
