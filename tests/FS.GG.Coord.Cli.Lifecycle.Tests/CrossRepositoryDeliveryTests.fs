namespace FS.GG.Coord.Cli.Tests

open System
open System.Text.Json
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.Cli
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport

module CrossRepositoryDeliveryTests =
    let private item: Ref = { Owner = "FS-GG"; Repo = ".github"; Number = 2845 }
    let private receiver = "FS.GG.SDD"
    let private pr = 908
    let private generation = 7001L
    let private electionId = 8001L
    let private worker = WorkerId "smew-f1e2"
    let private head = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
    let private files = [ "src/Delivery.fs"; "tests/DeliveryTests.fs" ]
    let private touchSet = Declared [ Matchable "src/**"; Matchable "tests/**" ]

    let private scope () =
        DeliveryApplication.deliveryScope "FS-GG" (Some receiver) item |> Result.defaultWith failwith

    let private opkey =
        Operation.compose item.Canonical (string generation) $"FS-GG/%s{receiver}" Operation.Merge
        |> Result.defaultWith (fun errors -> errors |> List.map Operation.describe |> String.concat "; " |> failwith)
        |> _.Value

    let private marker: Reads.Marker =
        {
            Id = generation
            Worker = worker
            Session = None
            AgeSeconds = 1
            PreviousStatus = None
            PathRepo = None
            AgentContract = None
            Raw = "<!-- fsgg:claim worker=smew-f1e2 lease=120 -->"
        }

    let private response body : Errors.IoResult<Response> =
        Ok { Status = 200; Body = body; ETag = None; NextLink = None; Headers = Map.empty }

    let private comment id body updatedAt =
        JsonSerializer.Serialize
            {|
                id = id
                html_url = $"https://example.test/comments/%d{id}"
                body = body
                updated_at = updatedAt
            |}

    let private comments claimId claimWorker =
        let now = DateTimeOffset.UtcNow.ToString("O")
        let claim =
            comment
                claimId
                $"<!-- fsgg:claim worker=%s{claimWorker} lease=120 -->\nheld"
                now
        let election =
            comment
                electionId
                (DeliveryApplication.electionMarker opkey item.Canonical (string generation) $"FS-GG/%s{receiver}" pr)
                now
        $"[%s{claim},%s{election}]"

    let private noClaimComments () =
        let now = DateTimeOffset.UtcNow.ToString("O")
        let election =
            comment
                electionId
                (DeliveryApplication.electionMarker opkey item.Canonical (string generation) $"FS-GG/%s{receiver}" pr)
                now
        $"[%s{election}]"

    let private prJson sha body = JsonSerializer.Serialize {| head = {| sha = sha |}; body = body |}
    let private filesJson paths = paths |> List.map (fun path -> {| filename = path |}) |> JsonSerializer.Serialize

    let private closingJson number repo =
        $"""{{"data":{{"repository":{{"pullRequest":{{"closingIssuesReferences":{{"totalCount":1,"nodes":[{{"number":%d{number},"repository":{{"nameWithOwner":"%s{repo}"}}}}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4999}}}}"""

    type private Drift =
        | Stable
        | MissingHolder
        | WrongHolder
        | ChangedGeneration
        | ChangedHead
        | ChangedBodyLinkage
        | ChangedGraphLinkage
        | ChangedPaths
        | ChangedDeclaration
        | RemovedDeclaration
        | PatchChangedHead
        | PatchChangedHolder
        | CurrentMarker

    type private World =
        {
            mutable Requests: (string * string) list
            mutable Patches: string list
            mutable Body: string
            mutable Patched: bool
            mutable CommentReads: int
        }

    let private transport drift =
        let canonicalBody = $"Implements delivery.\n\nCloses %s{item.Canonical}"
        let currentBody =
            if drift = CurrentMarker then
                canonicalBody
                + "\n\n"
                + FS.GG.Coord.Cli.Lifecycle.LiveHandlers.authorizationMarker
                    item.Canonical
                    (string generation)
                    opkey
                    (string electionId)
                    head
            else canonicalBody

        let world =
            {
                Requests = []
                Patches = []
                Body = currentBody
                Patched = false
                CommentReads = 0
            }

        let recorder =
            Fake.Recorder(fun req ->
                let path = req.Path.Trim('/')
                world.Requests <- world.Requests @ [ req.Method, path ]
                match req.Method, path, req.Body with
                | "GET", "repos/FS-GG/.github/issues/2845/comments", _ ->
                    world.CommentReads <- world.CommentReads + 1
                    match drift with
                    | MissingHolder -> response (noClaimComments ())
                    | WrongHolder -> response (comments generation "other-a1b2")
                    | ChangedGeneration -> response (comments (generation + 1L) "smew-f1e2")
                    | PatchChangedHolder when world.Patched -> response (comments generation "other-a1b2")
                    | _ -> response (comments generation "smew-f1e2")
                | "GET", "repos/FS-GG/.github/issues/2845", _ ->
                    let body =
                        match drift with
                        | ChangedDeclaration -> "Paths: docs/**"
                        | RemovedDeclaration -> "No path declaration remains."
                        | _ -> "Paths: src/**, tests/**"
                    response (JsonSerializer.Serialize {| body = body |})
                | "GET", "repos/FS-GG/FS.GG.SDD/pulls/908", _ ->
                    let liveHead =
                        if drift = ChangedHead || (drift = PatchChangedHead && world.Patched) then
                            String.replicate 40 "b"
                        else
                            head
                    let body = if drift = ChangedBodyLinkage then "Closes #2845" else world.Body
                    response (prJson liveHead body)
                | "GET", "repos/FS-GG/FS.GG.SDD/pulls/908/files", _ ->
                    let current = if drift = ChangedPaths then files @ [ "src/Unexpected.fs" ] else files
                    response (filesJson current)
                | "POST", "graphql", Query _ ->
                    if drift = ChangedGraphLinkage then
                        response (closingJson 2845 "FS-GG/FS.GG.SDD")
                    else
                        response (closingJson item.Number $"%s{item.Owner}/%s{item.Repo}")
                | "PATCH", "repos/FS-GG/FS.GG.SDD/pulls/908", Json body ->
                    world.Patches <- world.Patches @ [ body ]
                    use doc = JsonDocument.Parse body
                    world.Body <- doc.RootElement.GetProperty("body").GetString()
                    world.Patched <- true
                    response "{}"
                | method', path', _ -> Error(Errors.NotFound $"unexpected cross-repo request: %s{method'} %s{path'}"))
        recorder, world

    let private context transport: FS.GG.Coord.Cli.Kernel.Context =
        {
            Transport = transport
            Owner = "FS-GG"
            Title = "Coordination"
            DefaultRepo = Some ".github"
            ChoreLocks = []
        }

    let private authorizeWithPathsVerified drift pathsVerified =
        let recorder, world = transport drift
        let result =
            FS.GG.Coord.Cli.Lifecycle.LiveHandlers.ensureCrossRepositoryAuthorization
                (context recorder)
                (scope ())
                120
                marker
                pr
                head
                touchSet
                files
                pathsVerified
        result, recorder, world

    let private authorize drift = authorizeWithPathsVerified drift true

    [<Fact>]
    let ``#2845 scope requires a local unambiguous receiver and preserves same-repo defaults`` () =
        Assert.True((scope ()).CrossRepository)
        Assert.True(DeliveryApplication.deliveryScope "FS-GG" None item |> Result.exists (fun value -> not value.CrossRepository))
        Assert.True(DeliveryApplication.deliveryScope "FS-GG" (Some "Other/Repo") item |> Result.isError)
        let foreign = { item with Owner = "Other" }
        Assert.True(DeliveryApplication.deliveryScope "FS-GG" (Some receiver) foreign |> Result.isError)
        let nonCoordination = { item with Repo = "FS.GG.Rendering" }
        Assert.True(DeliveryApplication.deliveryScope "FS-GG" (Some receiver) nonCoordination |> Result.isError)

    [<Fact>]
    let ``#2845 cross-repo apply and flip refuse at the effect preflight`` () =
        let value = scope ()
        Assert.True(DeliveryApplication.validateDeliveryEffects value true false |> Result.isError)
        Assert.True(DeliveryApplication.validateDeliveryEffects value false true |> Result.isError)
        Assert.True(DeliveryApplication.validateDeliveryEffects value false false |> Result.isOk)

    [<Fact>]
    let ``#2845 canonical linkage rejects same-number receiver shorthand and wrong repository`` () =
        Assert.True(DeliveryApplication.hasCanonicalClosingLinkage item $"Closes %s{item.Canonical}")
        Assert.False(DeliveryApplication.hasCanonicalClosingLinkage item "Closes #2845")
        Assert.False(DeliveryApplication.hasCanonicalClosingLinkage item "Closes FS-GG/FS.GG.SDD#2845")

    [<Fact>]
    let ``#2845 shared path contract rejects ambiguous cross-repo tokens`` () =
        let touchSet = Declared [ Matchable "src/**"; Unmatchable "../other/**" ]
        let mutable sameRepoCalled = false
        let result =
            DeliveryApplication.classifyReceiverPaths (scope ()) touchSet files (fun () ->
                sameRepoCalled <- true
                [])
        Assert.True(Result.isError result)
        Assert.False(sameRepoCalled)

    [<Fact>]
    let ``#2845 authorization reads item authority and PATCHes only the receiver repository`` () =
        let result, recorder, world = authorize Stable
        Assert.True(Result.isOk result, $"%A{result}")
        Assert.Single(world.Patches) |> ignore
        Assert.Contains(("PATCH", "repos/FS-GG/FS.GG.SDD/pulls/908"), world.Requests)
        Assert.DoesNotContain(("GET", "repos/FS-GG/.github/pulls/908"), world.Requests)
        Assert.DoesNotContain(("PATCH", "repos/FS-GG/.github/pulls/908"), world.Requests)
        Assert.True(recorder.Count("comment-list FS-GG/.github 2845") >= 2)

    [<Fact>]
    let ``#2845 failed inspected path verdict refuses before election or PATCH effects`` () =
        let result, _, world = authorizeWithPathsVerified Stable false
        Assert.True(Result.isError result)
        Assert.Empty(world.Requests)
        Assert.Empty(world.Patches)

    [<Theory>]
    [<InlineData("missing")>]
    [<InlineData("wrong-holder")>]
    [<InlineData("generation")>]
    [<InlineData("head")>]
    [<InlineData("body-link")>]
    [<InlineData("graph-link")>]
    [<InlineData("paths")>]
    [<InlineData("declaration")>]
    [<InlineData("removed-declaration")>]
    let ``#2845 mutable authority drift refuses before receiver PATCH`` kind =
        let drift =
            match kind with
            | "missing" -> MissingHolder
            | "wrong-holder" -> WrongHolder
            | "generation" -> ChangedGeneration
            | "head" -> ChangedHead
            | "body-link" -> ChangedBodyLinkage
            | "graph-link" -> ChangedGraphLinkage
            | "paths" -> ChangedPaths
            | "declaration" -> ChangedDeclaration
            | "removed-declaration" -> RemovedDeclaration
            | other -> failwith other
        let result, _, world = authorize drift
        Assert.True(Result.isError result, $"%s{kind} unexpectedly authorized")
        Assert.Empty(world.Patches)

    [<Theory>]
    [<InlineData("head")>]
    [<InlineData("holder")>]
    let ``#2845 PATCH-time authority change is detected by final readback`` kind =
        let drift = if kind = "head" then PatchChangedHead else PatchChangedHolder
        let result, _, world = authorize drift
        Assert.True(Result.isError result, $"%s{kind} unexpectedly survived final readback")
        Assert.Single(world.Patches) |> ignore

    [<Fact>]
    let ``#2845 a current receiver marker is idempotent and never duplicated`` () =
        let result, _, world = authorize CurrentMarker
        Assert.True(Result.isOk result, $"%A{result}")
        Assert.Empty(world.Patches)

    [<Fact>]
    let ``#2845 election race reread refuses when another PR has the lower receiver-aware grant`` () =
        let mutable reads = 0
        let mutable posts = 0
        let contender = DeliveryApplication.electionMarker opkey item.Canonical (string generation) $"FS-GG/%s{receiver}" 907
        let mine = DeliveryApplication.electionMarker opkey item.Canonical (string generation) $"FS-GG/%s{receiver}" pr
        let contenderComment = comment 7999L contender "2026-09-27T00:00:00Z"
        let mineComment = comment electionId mine "2026-09-27T00:00:01Z"
        let recorder =
            Fake.Recorder(fun req ->
                match req.Method, req.Path.Trim('/') with
                | "GET", "repos/FS-GG/.github/issues/2845/comments" ->
                    reads <- reads + 1
                    if reads = 1 then response "[]"
                    else response ($"[%s{contenderComment},%s{mineComment}]")
                | "POST", "repos/FS-GG/.github/issues/2845/comments" ->
                    posts <- posts + 1
                    response $"{{\"id\":%d{electionId}}}"
                | method', path -> Error(Errors.NotFound $"unexpected election request %s{method'} %s{path}"))
        let result =
            FS.GG.Coord.Cli.Lifecycle.LiveHandlers.electionGroundingForReceiver
                (context recorder)
                item
                "FS-GG"
                receiver
                (string generation)
                pr
        Assert.True(Result.isError result)
        Assert.Equal(1, posts)
        Assert.Equal(2, reads)
