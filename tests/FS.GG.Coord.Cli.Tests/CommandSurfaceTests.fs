namespace FS.GG.Coord.Cli.Tests

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
          "scan", Scan
          "lanes", LanesView
          "facts", Facts

          // IO — the client surface the shim execs in place of bash (ADR-0040 Phase D).
          "whoami", WhoAmI
          "budget", Budget
          "next", Next
          "batch", BatchCmd
          "ready", Ready
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

          // The board-health gate (#496) and the REST listing (#446).
          "lint", LintCmd
          "issues", Issues

          // The follow-up queue (#1063) — LOCAL, like `whoami`: a file, no board, no token.
          "followup", Followup

          // The ADR-0050 registry-predicate oracle (#1202) — LOCAL: reads registry + producer manifests,
          // no board, no token.
          "predicate", Predicate

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
