namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.Cli

/// #2298: `sdd-required`'s SDD-package evidence (`work/<id>/spec.md`, `readiness/<id>/analysis.json` at
/// `implementationReady`) used to gate BOTH `delivery-route record` (the coordinator's write) and every
/// scheduling read (`show`, `claim`, `take`, `batch`/`next`, via the shared `bindSddEvidence`) on the
/// SAME filesystem fact — a fact only a CLAIMED WORKER is positioned to produce, inside a worktree, via
/// `fsgg-sdd`. That made an honest `sdd-required` route permanently unrecordable, and therefore the item
/// permanently unclaimable, for anything that did not already carry a package (`work/2137-delivery-route`
/// predated the gate and is the only reason `#2137` itself could ever record one).
///
/// This file pins the command boundary the fix lands on: `record` and `show` now REPORT the package's
/// on-disk readiness as advisory (`sddPackageReady` / `sddPackageNotes`) rather than refuse the write or
/// the read on it — for exactly the three ways that evidence can be incomplete AC5 names (no package at
/// all, an existing-but-not-`implementationReady` analysis, a workId substitution) — while a genuinely
/// missing or malformed ROUTE RECEIPT, the agent's own explicit decision, still refuses (AC5's negative
/// case, and AC2's "stays closed").
///
/// The command boundary itself is `Client.deliveryRouteCmd`, driven directly against a scripted
/// transport exactly as `ForceStealTests` already drives `Client.claim` — `record`/`show` need no board
/// or GraphQL fixture at all, only the issue body and its comment ledger.
module DeliveryRouteCliTests =

    let private ok (body: string) : Errors.IoResult<Response> =
        Ok
            { Status = 200
              Body = body
              ETag = None
              NextLink = None
              Headers = Map.empty }

    let private revision (body: string) =
        SHA256.HashData(Encoding.UTF8.GetBytes body) |> Convert.ToHexString |> _.ToLowerInvariant()

    let private issueBodyText = "Paths: src/Thing.fs"
    let private issueRevision = revision issueBodyText

    [<Literal>]
    let private DeliveryRouteMarker = "<!-- fsgg:delivery-route/v1 -->"

    let private sddRequiredReceipt workId =
        $"""{{"schema":"fsgg.coord.delivery-route/v1","subject":"FS-GG/FS.GG.SDD#42","subjectRevision":"%s{issueRevision}","route":"sdd-required","agent":"fixture-2298","timestamp":"2026-01-01T00:00:00Z","reasonCodes":["fixture"],"rationale":"fixture sdd-required receipt for #2298","declaredImpacts":["internal"],"observedFacts":["localized"],"sddWorkId":"%s{workId}","specHome":"work/%s{workId}/spec.md","requiredGates":["implementationReady","analyze","verify","ship"]}}"""

    /// A live comment thread, mutated only the way `record` would mutate it (append). Bodies round-trip
    /// through `JsonSerializer` rather than hand-escaped string interpolation, so a marker's embedded
    /// `\n` and quotes cannot corrupt the fixture's own JSON.
    type private Thread(initial: string list) =
        let comments = ResizeArray<int64 * string>(initial |> List.mapi (fun i b -> int64 (7000 + i), b))
        let mutable nextId = 9000L

        member _.Json() =
            comments
            |> Seq.map (fun (id, body) ->
                {| id = id
                   body = body
                   user = {| login = "EHotwagner" |}
                   created_at = "2026-01-01T00:00:00Z"
                   updated_at = "2026-01-01T00:00:00Z" |})
            |> List.ofSeq
            |> JsonSerializer.Serialize

        member _.Add(body: string) =
            nextId <- nextId + 1L
            comments.Add(nextId, body)
            nextId

        member _.Bodies = comments |> Seq.map snd |> List.ofSeq

    let private world (thread: Thread) =
        Fake.Recorder(fun (req: Request) ->
            let path = req.Path.Trim '/'

            match req.Method, path with
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/42" -> ok (JsonSerializer.Serialize {| number = 42; body = issueBodyText |})
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/42/comments" -> ok (thread.Json())
            | "POST", "repos/FS-GG/FS.GG.SDD/issues/42/comments" ->
                let body =
                    match req.Body with
                    | Json payload -> JsonDocument.Parse(payload).RootElement.GetProperty("body").GetString()
                    | _ -> ""

                ok (sprintf """{"id":%d}""" (thread.Add body))
            | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

    let private context (transport: Fake.Recorder) : Client.Context =
        { Transport = transport
          Owner = "FS-GG"
          Title = "Coordination"
          DefaultRepo = Some "FS.GG.SDD"
          ChoreLocks = [] }

    /// Every leg pins `FSGG_COORD_SDD_ROOT` to a fixture-owned directory, explicitly — never `None`, or
    /// `sddEvidenceErrors`' upward directory search could walk out of a throwaway temp dir and find this
    /// CHECKOUT's own real `work/`/`readiness/` trees, silently laundering a live fact into a fixture.
    let private runRoute (transport: Fake.Recorder) (sddRoot: string) (args: string list) : int * string =
        let previousRoot = Environment.GetEnvironmentVariable "FSGG_COORD_SDD_ROOT"
        let stdout = Console.Out
        use captured = new StringWriter()

        try
            Environment.SetEnvironmentVariable("FSGG_COORD_SDD_ROOT", sddRoot)
            Console.SetOut captured

            let opts =
                match Options.parse args with
                | Ok o -> o
                | Error e -> failwithf "the fixture's own argv did not parse: %s" e

            let code = Client.deliveryRouteCmd (context transport) opts
            Console.Out.Flush()
            code, captured.ToString()
        finally
            Console.SetOut stdout
            Environment.SetEnvironmentVariable("FSGG_COORD_SDD_ROOT", previousRoot)

    let private tempSddRoot () =
        let dir = Path.Combine(Path.GetTempPath(), "fsgg-2298-" + Guid.NewGuid().ToString "n")
        Directory.CreateDirectory dir |> ignore
        dir

    let private withPackage (root: string) workId (analysis: string option) =
        Directory.CreateDirectory(Path.Combine(root, "work", workId)) |> ignore
        File.WriteAllText(Path.Combine(root, "work", workId, "spec.md"), "# fixture spec\n")

        match analysis with
        | Some json ->
            Directory.CreateDirectory(Path.Combine(root, "readiness", workId)) |> ignore
            File.WriteAllText(Path.Combine(root, "readiness", workId, "analysis.json"), json)
        | None -> ()

    // ---- AC5's three incomplete-evidence cases: all now RECORD, none of them refuse -----------------

    [<Fact>]
    let ``#2298 record posts an sdd-required receipt with no SDD package on disk at all`` () =
        let root = tempSddRoot ()

        try
            let thread = Thread []
            let transport = world thread
            let path = Path.Combine(root, "receipt.json")
            File.WriteAllText(path, sddRequiredReceipt "no-package-2298")

            let code, out = runRoute transport root [ "delivery-route"; "record"; "FS.GG.SDD#42"; path ]

            Assert.Equal(0, code)
            Assert.Equal<string list>([ DeliveryRouteMarker + "\n" + (sddRequiredReceipt "no-package-2298") ], thread.Bodies)

            let result = JsonDocument.Parse(out.Trim()).RootElement
            Assert.Equal("recorded", result.GetProperty("kind").GetString())
            Assert.False(result.GetProperty("sddPackageReady").GetBoolean())

            let notes = result.GetProperty("sddPackageNotes").EnumerateArray() |> Seq.map (fun v -> v.GetString()) |> List.ofSeq
            Assert.Contains(notes, fun (n: string) -> n.Contains "sdd spec does not exist")
        finally
            Directory.Delete(root, true)

    [<Fact>]
    let ``#2298 record posts an sdd-required receipt whose readiness analysis is not implementationReady`` () =
        let root = tempSddRoot ()

        try
            withPackage root "not-ready-2298" (Some """{"workId":"not-ready-2298","status":"analyzing"}""")
            let thread = Thread []
            let transport = world thread
            let path = Path.Combine(root, "receipt.json")
            File.WriteAllText(path, sddRequiredReceipt "not-ready-2298")

            let code, out = runRoute transport root [ "delivery-route"; "record"; "FS.GG.SDD#42"; path ]

            Assert.Equal(0, code)
            Assert.Equal(1, thread.Bodies.Length)

            let result = JsonDocument.Parse(out.Trim()).RootElement
            Assert.Equal("recorded", result.GetProperty("kind").GetString())
            Assert.False(result.GetProperty("sddPackageReady").GetBoolean())

            let notes = result.GetProperty("sddPackageNotes").EnumerateArray() |> Seq.map (fun v -> v.GetString()) |> List.ofSeq
            Assert.Contains(notes, fun (n: string) -> n.Contains "not implementationReady")
        finally
            Directory.Delete(root, true)

    [<Fact>]
    let ``#2298 record posts an sdd-required receipt whose readiness analysis names a substituted workId`` () =
        let root = tempSddRoot ()

        try
            withPackage root "substituted-2298" (Some """{"workId":"other-work","status":"implementationReady"}""")
            let thread = Thread []
            let transport = world thread
            let path = Path.Combine(root, "receipt.json")
            File.WriteAllText(path, sddRequiredReceipt "substituted-2298")

            let code, out = runRoute transport root [ "delivery-route"; "record"; "FS.GG.SDD#42"; path ]

            Assert.Equal(0, code)
            Assert.Equal(1, thread.Bodies.Length)

            let result = JsonDocument.Parse(out.Trim()).RootElement
            Assert.False(result.GetProperty("sddPackageReady").GetBoolean())

            let notes = result.GetProperty("sddPackageNotes").EnumerateArray() |> Seq.map (fun v -> v.GetString()) |> List.ofSeq
            Assert.Contains(notes, fun (n: string) -> n.Contains "workId does not match")
        finally
            Directory.Delete(root, true)

    [<Fact>]
    let ``#2298 record and show both report a ready SDD package as ready, once its analysis is implementationReady`` () =
        let root = tempSddRoot ()

        try
            withPackage root "ready-2298" (Some """{"workId":"ready-2298","status":"implementationReady"}""")
            let thread = Thread []
            let transport = world thread
            let path = Path.Combine(root, "receipt.json")
            File.WriteAllText(path, sddRequiredReceipt "ready-2298")

            let recordCode, recordOut = runRoute transport root [ "delivery-route"; "record"; "FS.GG.SDD#42"; path ]
            Assert.Equal(0, recordCode)
            Assert.True(JsonDocument.Parse(recordOut.Trim()).RootElement.GetProperty("sddPackageReady").GetBoolean())

            let showCode, showOut = runRoute transport root [ "delivery-route"; "show"; "FS.GG.SDD#42" ]
            Assert.Equal(0, showCode)

            let shown = JsonDocument.Parse(showOut.Trim()).RootElement
            Assert.Equal("sdd-required", shown.GetProperty("route").GetString())
            Assert.True(shown.GetProperty("sddPackageReady").GetBoolean())
            Assert.Empty(shown.GetProperty("sddPackageNotes").EnumerateArray())
        finally
            Directory.Delete(root, true)

    [<Fact>]
    let ``#2298 show reports a missing SDD package as advisory rather than refusing the read`` () =
        let root = tempSddRoot ()

        try
            let thread = Thread [ DeliveryRouteMarker + "\n" + (sddRequiredReceipt "shown-missing-2298") ]
            let transport = world thread

            let code, out = runRoute transport root [ "delivery-route"; "show"; "FS.GG.SDD#42" ]

            Assert.Equal(0, code)

            let shown = JsonDocument.Parse(out.Trim()).RootElement
            Assert.Equal("current", shown.GetProperty("kind").GetString())
            Assert.Equal("sdd-required", shown.GetProperty("route").GetString())
            Assert.False(shown.GetProperty("sddPackageReady").GetBoolean())
        finally
            Directory.Delete(root, true)

    // ---- AC5's negative case: the receipt gate itself stays closed -----------------------------------

    [<Fact>]
    let ``#2298 a missing route receipt still refuses show — the decision gate that stays closed`` () =
        let root = tempSddRoot ()

        try
            let thread = Thread [] // no `fsgg:delivery-route/v1` comment posted at all
            let transport = world thread

            let code, _ = runRoute transport root [ "delivery-route"; "show"; "FS.GG.SDD#42" ]

            Assert.NotEqual(0, code)
        finally
            Directory.Delete(root, true)

    [<Fact>]
    let ``#2298 an incomplete route receipt still refuses record with zero writes — SDD leniency never widens this`` () =
        let root = tempSddRoot ()

        try
            let thread = Thread []
            let transport = world thread
            // `sdd-required` with neither `sddWorkId` nor `specHome` — an incomplete AGENT DECISION, the
            // thing #2298 leaves refused. Only the SDD PACKAGE'S readiness became advisory, never this.
            let malformed =
                $"""{{"schema":"fsgg.coord.delivery-route/v1","subject":"FS-GG/FS.GG.SDD#42","subjectRevision":"%s{issueRevision}","route":"sdd-required","agent":"fixture-2298","timestamp":"2026-01-01T00:00:00Z","reasonCodes":["fixture"],"rationale":"incomplete sdd-required receipt","declaredImpacts":["internal"],"observedFacts":["localized"],"sddWorkId":null,"specHome":null,"requiredGates":[]}}"""

            let path = Path.Combine(root, "malformed.json")
            File.WriteAllText(path, malformed)

            let code, _ = runRoute transport root [ "delivery-route"; "record"; "FS.GG.SDD#42"; path ]

            Assert.NotEqual(0, code)
            Assert.Empty(thread.Bodies)
        finally
            Directory.Delete(root, true)
