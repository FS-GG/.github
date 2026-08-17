namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open Xunit
open FS.GG.Coord
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.Cli

/// THE `add` STATUS DEFAULT, AND THE IDEMPOTENCE IT MUST NOT BREAK (.github#1823).
///
/// `add` put an issue on the board and left `Status` UNSET. A row with no `Status` is invisible to every
/// scheduler — `Schedulability` says so in as many words: *"no Status on the board: invisible to every
/// scheduler, and nobody set it."* Fourteen rows were filed that way on 2026-07-28, in three batches, and
/// EVERY instance was found by accident by a driver reading `batch` output for an unrelated reason.
/// Nothing reported any of them. Each was filed in good faith by a worker discharging a real item and
/// following the documented flow: file the finding, `add` it to the board.
///
/// **THE PAIR IS THE GATE, exactly as `ForceStealTests` is.** One board, two argv lines is not enough
/// here, because the risk is not that the default fails to fire — it is that it fires TOO OFTEN. `add` is
/// idempotent (#861), so a close-out pass, a retry, or two workers racing the same follow-up all reach it.
/// A naive "set Status on add" walks a live `In progress` row back to `Backlog` and DESTROYS information
/// rather than adding it. So every leg below comes in two halves: the column the engine WRITES, and the
/// column it must leave alone.
///
/// **THE ANCHOR IS THE MUTATION, NEVER THE PROSE.** Every assertion here reads `Fake.Recorder`'s
/// `item-edit --id … --field-id … --single-select-option-id …` line, which is the board write as the
/// transport saw it. Nothing asserts on the stderr sentence this change introduces: an anchor made of the
/// guard's own output passes whenever the guard emits words, which is #1808's third point and the shape
/// `#1772` recorded (a fixture testing a hand-written mirror of its subject rather than the subject).
module AddStatusDefaultTests =

    let private ok (body: string) : Errors.IoResult<Response> =
        Ok
            { Status = 200
              Body = body
              ETag = None
              NextLink = None; Headers = Map.empty }

    /// What the board's `Status` column holds for FS.GG.SDD#42 before `add` runs.
    type private Column =
        /// The issue is not on this board at all — `add` will create the item.
        | NotOnBoard
        /// On the board, and its `Status` is genuinely empty. The fourteen rows' condition.
        | OnBoardUnset
        /// On the board with a column somebody set. The one `add` may never overwrite.
        | OnBoardSet of string
        /// On the board with a column somebody set, and `Board.itemId` DOES NOT SEE IT.
        ///
        /// Not exotic: that lookup is `projectItems(first: 20)` with no pagination and no `pageInfo`
        /// check, so a wholly successful read returns "not on board" for an issue whose item sits past
        /// the twentieth project. `addProjectV2ItemById` is idempotent server-side and hands back the
        /// EXISTING item, so `AddedToBoard` means "the lookup did not find it" — never "the item is new".
        /// The design that skipped the column read on that arm would have overwritten a live column here.
        | OnBoardHiddenFromLookup of string
        /// On the board, and the column read FAILS. NOT the same as empty (#266).
        | OnBoardUnreadable

    let private ItemId = "PVTI_item42"
    let private NewItemId = "PVTI_added42"

    /// The board: one `Status` single-select carrying the three columns these legs name.
    let private FieldsAnswerWithoutBlocked =
        """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_backlog","name":"Backlog"},{"id":"opt_ready","name":"Ready"},{"id":"opt_wip","name":"In progress"}]}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""

    let private FieldsAnswer =
        FieldsAnswerWithoutBlocked.Replace("\"In progress\"", "\"In progress\"},{\"id\":\"opt_blocked\",\"name\":\"Blocked\"")

    let private ProjectAnswer =
        """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""

    /// THE FIXTURE IS A BOARD, NOT A SCRIPT OF ANSWERS. `addProjectV2ItemById` MUTATES it — after the add,
    /// the issue IS on the board and every later read must say so. A canned sequence would answer the
    /// post-add item lookup with "not on board" and the Status write would silently become a no-op, which
    /// is precisely the outcome these legs exist to catch; the board has to be a thing that changes.
    ///
    /// `Added()` MODELS THE SERVER, NOT THE HAPPY PATH. `addProjectV2ItemById` is idempotent server-side
    /// (`Board.addItem`'s own docstring, measured against the live board in #861): for an issue already
    /// on the board it returns THAT item's id and adds nothing. So this only creates a fresh, field-less
    /// item when the board really has none — and `OnBoardHiddenFromLookup` below drives the case where it
    /// does, which the `AddedToBoard`-skips-the-read design could not survive and no longer attempts.
    type private Board(start: Column) =
        let mutable column = start

        let mutable itemId =
            match start with
            | NotOnBoard
            | OnBoardHiddenFromLookup _ -> None
            | _ -> Some ItemId

        member _.Column = column
        member _.ItemId = itemId

        /// What `add`'s mutation does. Server-side idempotent: an issue already on the board keeps its
        /// item and its column, and only a genuinely absent one becomes a new, field-less item.
        member _.Added() =
            match column with
            | OnBoardHiddenFromLookup existing ->
                // The lookup missed it (`projectItems(first: 20)`, unpaginated) but the server has it.
                itemId <- Some ItemId
                column <- OnBoardSet existing
            | _ ->
                itemId <- Some NewItemId
                column <- OnBoardUnset

            match itemId with
            | Some id -> id
            | None -> failwith "the fixture's own add produced no item id"

    /// The two `projectItems` reads are told apart by `fieldValueByName`, which only `Board.itemStatus`
    /// selects. `Board.itemId` asks for `nodes { id project { number } }` on the same connection, so a
    /// fixture keying on `projectItems` alone would answer one query with the other's shape.
    let private graphqlAnswer (board: Board) (blockedBy: string option) (document: string) : Errors.IoResult<Response> =
        if document.Contains "projectsV2" then
            ok ProjectAnswer
        elif document.Contains "fields(first" then
            ok FieldsAnswer
        elif document.Contains "\"Blocked by\"" then
            match blockedBy with
            | Some value ->
                ok $"""{{"data":{{"repository":{{"issue":{{"projectItems":{{"nodes":[{{"project":{{"number":12}},"fieldValueByName":{{"text":"%s{value}"}}}}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
            | None -> ok """{"data":{"repository":{"issue":{"projectItems":{"nodes":[]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
        elif document.Contains "fieldValueByName" then
            // `Board.itemStatus` — the read that decides whether the default may fire.
            match board.Column with
            | OnBoardUnreadable -> Error(Errors.Http(502, "the Status column could not be read"))
            | NotOnBoard
            | OnBoardHiddenFromLookup _ ->
                ok """{"data":{"repository":{"issue":{"projectItems":{"nodes":[]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
            | OnBoardUnset ->
                ok """{"data":{"repository":{"issue":{"projectItems":{"nodes":[{"project":{"number":12},"fieldValueByName":null}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
            | OnBoardSet name ->
                ok
                    $"""{{"data":{{"repository":{{"issue":{{"projectItems":{{"nodes":[{{"project":{{"number":12}},"fieldValueByName":{{"name":"%s{name}"}}}}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
        elif document.Contains "projectItems" then
            // `Board.itemId` — presence on THIS board, and the whole of #421's guard.
            match board.ItemId with
            | None -> ok """{"data":{"repository":{"issue":{"projectItems":{"nodes":[]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
            | Some id ->
                ok
                    $"""{{"data":{{"repository":{{"issue":{{"projectItems":{{"nodes":[{{"id":"%s{id}","project":{{"number":12}}}}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
        elif document.Contains "addProjectV2ItemById" then
            let id = board.Added()
            ok $"""{{"data":{{"addProjectV2ItemById":{{"item":{{"id":"%s{id}"}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
        elif document.Contains "issue(number: $number) { id }" then
            ok """{"data":{"repository":{"issue":{"id":"I_issue42"}}},"rateLimit":{"cost":1,"remaining":4977}}"""
        elif document.Contains "updateProjectV2ItemFieldValue" then
            ok """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}},"rateLimit":{"cost":1,"remaining":4977}}"""
        else
            Error(Errors.NotFound $"the fixture serves no such document: {document.Substring(0, min 80 document.Length)}")

    /// The issue's own text. `Class: defect` keeps the #1651 vocabulary gate quiet — that gate is a
    /// different rule and a refusal there would stop these legs before they reached the board at all.
    let private IssueBody = """{"number":42,"body":"Paths: src/Thing.fs\n\nClass: defect"}"""

    /// .github#2690: `add`'s Status write now also records a lifecycle intent, and that receipt is a
    /// comment POST on the row. `posted` is where this fixture keeps it, so a leg can anchor on the
    /// RECEIPT — the bytes that reach GitHub — rather than on the stderr sentence about it, which is the
    /// same rule the board-write assertions above already follow.
    /// The row's own comment ledger, as `Reads.commentBodies` reads it — a JSON array of `{"body": …}`.
    ///
    /// .github#2698 needs this to be a PARAMETER rather than the hardcoded `[]` it was, because the
    /// refusal it adds is a refusal about an ABSENCE, and a gate asserting an absence is satisfied by a
    /// reader that matches NOTHING. `.github#2312` shipped exactly that: an ordering gate whose check
    /// never matched, green forever. So every refusal leg below is paired with a PRESENCE leg driven
    /// through this same seam, and the presence corpus is deliberately more than one spelling.
    let private commentsJson (bodies: string list) =
        bodies
        |> List.mapi (fun i body ->
            System.Text.Json.JsonSerializer.Serialize {| id = 9000 + i; body = body |})
        |> String.concat ","
        |> sprintf "[%s]"

    let private worldCapturingWithComments (posted: ResizeArray<string>) (column: Column) issueBody blockedBy (comments: string list) =
        let board = Board column

        Fake.Recorder(fun (req: Request) ->
            let path = req.Path.Trim '/'

            match req.Method, path with
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, _) when document.Contains "items(first: 100" ->
                    ok """{"data":{"organization":{"projectV2":{"items":{"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[{"status":{"name":"Ready"},"blockedBy":null,"class":null,"severity":null,"phase":null,"repoScope":null,"content":{"__typename":"Issue","number":9,"title":"narrow sibling","state":"OPEN","createdAt":"2026-07-30T00:00:00Z","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                | Query(document, _) -> graphqlAnswer board blockedBy document
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/42" -> ok issueBody
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/9" -> ok """{"number":9,"body":"Paths: docs/reports/new-file.md"}"""
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/9/comments" -> ok "[]"
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/42/comments" -> ok (commentsJson comments)
            | "POST", "repos/FS-GG/FS.GG.SDD/issues/42/comments" ->
                match req.Body with
                | Json payload ->
                    use doc = System.Text.Json.JsonDocument.Parse payload

                    match doc.RootElement.TryGetProperty "body" with
                    | true, value -> posted.Add(value.GetString())
                    | _ -> failwith "the engine posted a comment with no body"

                    ok """{"id":9042}"""
                | _ -> Error(Errors.NotFound "a comment POST with no JSON payload")
            | "GET", "repos/FS-GG/FS.GG.SDD/issues" -> ok "[]"
            | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

    let private worldCapturing (posted: ResizeArray<string>) (column: Column) issueBody blockedBy =
        worldCapturingWithComments posted column issueBody blockedBy []

    let private worldWithBodyAndBlockedBy (column: Column) issueBody blockedBy =
        worldCapturing (ResizeArray()) column issueBody blockedBy

    let private worldWithBody (column: Column) issueBody = worldWithBodyAndBlockedBy column issueBody None

    let private world (column: Column) = worldWithBody column IssueBody

    // ---- .github#2698 — THE ROUTE-RECEIPT CORPUS -----------------------------------------------------
    //
    // `.github#2312`'s repair is the pattern being copied: a gate that asserts an absence needs N
    // spellings that MUST be recognised and M lookalikes that must NOT be, or a reader that recognises
    // nothing satisfies every refusal leg at once.

    /// The row's canonical subject, which is what `routeEvidence` validates the ledger against.
    let private Subject = "FS-GG/FS.GG.SDD#42"

    /// SPELLING 1 — an ordinary `lightweight` decision. The overwhelmingly common real receipt.
    let private LightweightReceipt =
        StructuredFixtures.routeComment Subject (Some DeliveryRoute.Lightweight) "fixture-rook" None

    /// SPELLING 2 — an `sdd-required` decision, carrying the SDD bindings that route demands. Here so the
    /// presence path cannot be satisfied by a reader that only ever recognises the word `lightweight`:
    /// the gate's question is "is there a CURRENT decision", never "which route did it pick".
    let private SddRequiredReceipt =
        StructuredFixtures.routeComment Subject (Some DeliveryRoute.SddRequired) "fixture-rook" (Some "2698-route")

    /// LOOKALIKE 1 — a well-formed, valid receipt for a DIFFERENT row. `validateRouteLedger` binds the
    /// record to its subject, and this is the failure a copy-pasted receipt actually produces.
    let private OtherSubjectReceipt =
        StructuredFixtures.routeComment "FS-GG/FS.GG.SDD#43" (Some DeliveryRoute.Lightweight) "fixture-rook" None

    /// LOOKALIKE 2 — the receipt's exact JSON with NO marker. The ledger is found by its marker; naked
    /// payload is not evidence, and a reader that scanned for `"route"` anywhere in a comment would
    /// wrongly accept this.
    let private UnmarkedReceiptJson =
        StructuredFixtures.routeJson Subject (Some DeliveryRoute.Lightweight) "fixture-rook" None

    /// LOOKALIKE 3 — the marker, verbatim, with prose in front of it. `structuredRouteLedger` requires
    /// the marker to OPEN the comment; a human quoting the protocol in a discussion must not board a row.
    let private QuotedMarkerComment =
        "As discussed, the receipt would read:\n\n" + LightweightReceipt

    /// LOOKALIKE 4 — a different protocol marker entirely, on the same row. The claim lock lives in this
    /// same ledger, so every real row has one of these beside its receipt.
    let private ClaimMarkerComment =
        "<!-- fsgg:claim worker=vole-418 lease=120 renewed=1 session=s prev=Ready pathRepo=FS.GG.SDD -->"

    let private context (transport: Fake.Recorder) : Kernel.Context =
        { Transport = transport
          Owner = "FS-GG"
          Title = "Coordination"
          DefaultRepo = Some "FS.GG.SDD"
          ChoreLocks = [] }

    /// The identity ladder is pinned for `ForceStealTests`' reason: `Identity.resolve` reads the harness's
    /// session id out of the environment, so a test that says nothing about it asserts something different
    /// inside an agent shell than in CI. `add` takes no lock, so nothing here turns on WHICH worker we are
    /// — only that the answer is the same on both machines.
    let private sessionVars =
        [ "CLAUDE_CODE_SESSION_ID"; "OPENCODE_SESSION_ID"; "FSGG_AGENT_SESSION_ID"; "FSGG_WORKER" ]

    /// Drive ONE CLI verb as a real command line, isolated on its own cache and pinned identity.
    ///
    /// `runAddWithStderr` was this function with `Client.addCmd` welded in. .github#2698 needs the same
    /// scaffolding for `set-field` and `release`, because the refusal it adds is ONE shared gate reached
    /// through four doors, and a gate proven at one door is a gate a scheduled job walks around — which is
    /// exactly what the host measured on 2026-08-16, seven times, at doors this module did not drive.
    let private runVerbWithStderr (invoke: Kernel.Context -> Options.Options -> int) (transport: Fake.Recorder) (args: string list) : int * string * string =
        let dir = Path.Combine(Path.GetTempPath(), "fsgg-1823-" + Guid.NewGuid().ToString "n")
        let previousCache = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        let previousKitRoot = Environment.GetEnvironmentVariable "FSGG_KIT_ROOT"
        let previousSessions = sessionVars |> List.map (fun v -> v, Environment.GetEnvironmentVariable v)
        let stdout = Console.Out
        let stderr = Console.Error
        use captured = new StringWriter()
        use capturedErr = new StringWriter()

        try
            Directory.CreateDirectory dir |> ignore
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", dir)
            Environment.SetEnvironmentVariable("CLAUDE_CODE_SESSION_ID", null)
            Environment.SetEnvironmentVariable("OPENCODE_SESSION_ID", null)
            Environment.SetEnvironmentVariable("FSGG_AGENT_SESSION_ID", "ed60050b")
            Environment.SetEnvironmentVariable("FSGG_WORKER", "vole-418")
            Console.SetOut captured
            Console.SetError capturedErr

            let opts =
                match Options.parse args with
                | Ok o -> o
                | Error e -> failwithf "the fixture's own argv did not parse: %s" e

            let code = invoke (context transport) opts
            Console.Out.Flush()
            Console.Error.Flush()
            code, captured.ToString(), capturedErr.ToString()
        finally
            Console.SetOut stdout
            Console.SetError stderr
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", previousKitRoot)

            for name, value in previousSessions do
                Environment.SetEnvironmentVariable(name, value)

            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    let private runAddWithStderr (transport: Fake.Recorder) (args: string list) = runVerbWithStderr Client.addCmd transport args

    let private runAdd transport args =
        let code, stdout, _ = runAddWithStderr transport args
        code, stdout

    /// The board write, as the transport saw it. THIS is the anchor — the bytes that reach GitHub, not
    /// the sentence the CLI prints about them.
    let private statusWrite (itemId: string) (optionId: string) =
        $"item-edit --id %s{itemId} --project-id PVT_coord --field-id PVTSSF_status --single-select-option-id %s{optionId}"

    // ---- THE DEFAULT FIRES ------------------------------------------------------------------------

    [<Fact>]
    let ``#1823 add with no --status puts a NEWLY boarded row in Backlog`` () =
        let transport = world NotOnBoard

        let code, out = runAdd transport [ "add"; "FS.GG.SDD#42" ]

        Assert.Equal(0, code)
        // The id still goes to stdout — a caller piping `add` gets one either way, and the column is a
        // note ABOUT a row that is already boarded.
        Assert.Equal(NewItemId, out.Trim())

        Assert.True(
            transport.Logged(statusWrite NewItemId "opt_backlog"),
            $"the #1823 default must WRITE Backlog on a newly boarded row — log: %A{transport.Log}"
        )

    [<Fact>]
    let ``#1843 add scans a narrow sibling, warns, and still boards the broad declaration`` () =
        let transport = worldWithBody NotOnBoard """{"number":42,"body":"Paths: docs/reports\n\nClass: defect"}"""

        let code, out, err = runAddWithStderr transport [ "add"; "FS.GG.SDD#42" ]

        Assert.Equal(0, code)
        Assert.Equal(NewItemId, out.Trim())
        Assert.True(transport.Logged("item-add"), $"advisory must not suppress the board mutation: %A{transport.Log}")
        Assert.True(transport.Logged("issue-get FS-GG/FS.GG.SDD 9"), $"the real sibling body must be scanned: %A{transport.Log}")
        Assert.True(err.Contains("FS.GG.SDD#9"), err)
        Assert.Contains("lane of one", err)
        Assert.Contains("holding declaration", err)

    [<Fact>]
    let ``#1823 add REPAIRS a row that is already on the board with NO Status`` () =
        // The fourteen rows' condition, and the reason the already-on-board arm reads the column instead
        // of skipping it: those rows are ON the board, so the `AddedToBoard` arm never sees them.
        let transport = world OnBoardUnset

        let code, out = runAdd transport [ "add"; "FS.GG.SDD#42" ]

        Assert.Equal(0, code)
        Assert.Equal(ItemId, out.Trim())

        Assert.True(
            transport.Logged(statusWrite ItemId "opt_backlog"),
            $"an on-board row with an EMPTY Status must be defaulted to Backlog — log: %A{transport.Log}"
        )

    // ---- AND IT MUST NOT (AC4 — THE ONE WAY THIS CHANGE DESTROYS INFORMATION) ----------------------

    [<Fact>]
    let ``#1823 AC4 add does NOT overwrite a Status somebody already set`` () =
        // THE LEG THIS ITEM IS MOST AFRAID OF. `add` is idempotent (#861) and is re-run routinely, so a
        // default that did not read first would roll a live `In progress` row back to `Backlog`. Anchored
        // on the ABSENCE of any Status mutation at all, which is a fact about the board rather than about
        // the sentence the CLI chose to print.
        let transport = world (OnBoardSet "In progress")

        let code, out = runAdd transport [ "add"; "FS.GG.SDD#42" ]

        Assert.Equal(0, code)
        Assert.Equal(ItemId, out.Trim())

        Assert.Equal(0, transport.Count "item-edit")

    [<Fact>]
    let ``#1823 AC4 a Status of Ready is preserved too - not only the in-flight ones`` () =
        // `In progress` alone would pass against an engine that special-cased the claim column. The
        // property is "any column somebody set", so a second, ordinary one is asserted as well.
        let transport = world (OnBoardSet "Ready")

        let code, _ = runAdd transport [ "add"; "FS.GG.SDD#42" ]

        Assert.Equal(0, code)
        Assert.Equal(0, transport.Count "item-edit")

    [<Fact>]
    let ``#1823 a Status that could NOT BE READ is left alone, never defaulted (#266)`` () =
        // "I could not evaluate this" is NEVER "I evaluated it and it is empty". A failed read that
        // defaulted would overwrite whatever is really there — the same destruction as AC4's, reached
        // through a fabricated absence instead of a missing check.
        let transport = world OnBoardUnreadable

        let code, out = runAdd transport [ "add"; "FS.GG.SDD#42" ]

        // Still green: the ROW IS BOARDED, and that is what `add` promised and what stdout says.
        Assert.Equal(0, code)
        Assert.Equal(ItemId, out.Trim())
        Assert.Equal(0, transport.Count "item-edit")

    // ---- AC2 — AN EXPLICIT STATUS STILL WINS -------------------------------------------------------

    [<Fact>]
    let ``#1823 AC2 --status names the column instead of the default`` () =
        // .github#2698: `--status Ready` now requires a current delivery-route receipt, so this leg — and
        // every other `--status Ready` leg in this module — supplies one. That is not fixture upkeep: it
        // is the PRESENCE half of the new gate's corpus, and it reds if the receipt reader stops reading.
        let transport = worldCapturingWithComments (ResizeArray()) NotOnBoard IssueBody None [ LightweightReceipt ]

        let code, _ = runAdd transport [ "add"; "FS.GG.SDD#42"; "--status"; "Ready" ]

        Assert.Equal(0, code)
        Assert.True(transport.Logged(statusWrite NewItemId "opt_ready"), $"log: %A{transport.Log}")
        // And the default did NOT also fire. A flag that merely added a second write would leave the
        // board in whichever order the mutations happened to land.
        Assert.False(transport.Logged(statusWrite NewItemId "opt_backlog"))

    [<Fact>]
    let ``#1823 an item the LOOKUP missed keeps its column - AddedToBoard is not 'the item is new'`` () =
        // THE ARM THAT USED TO SKIP THE READ, AND WHY IT MAY NOT. `addProjectV2ItemById` is idempotent
        // server-side and returns the EXISTING item for an issue already on the board, and `Board.itemId`
        // is `projectItems(first: 20)` with no pagination — so a wholly successful read can answer "not on
        // board" for a row that is on it, carrying a column somebody set. The old fresh-add arm reasoned
        // "a new item has no field values, so no read is owed" and would have defaulted straight over it.
        //
        // This is the leg that reds if that shortcut ever comes back.
        let transport = world (OnBoardHiddenFromLookup "In progress")

        let code, _ = runAdd transport [ "add"; "FS.GG.SDD#42" ]

        Assert.Equal(0, code)
        Assert.Equal(0, transport.Count "item-edit")

    // ---- A BAD --status IS REFUSED BEFORE THE ADD -------------------------------------------------

    [<Fact>]
    let ``#1823 add --status naming no column is REFUSED, and spends no write`` () =
        // The Status write is non-fatal by design — the row is boarded, so a red would send a filer back
        // to re-run `add` rather than to the field write actually owed. Right for a default nobody asked
        // for; WRONG for an instruction. Unvalidated, `--status Redy` boards the row, notes a 422, and
        // exits 0 — leaving a row with NO column at all, which is the exact thing #1823 exists to stop,
        // produced by #1823's own flag. `set-field` exits non-zero for the same value.
        let transport = world OnBoardUnset

        let code, out = runAdd transport [ "add"; "FS.GG.SDD#42"; "--status"; "Redy" ]

        Assert.NotEqual(0, code)
        // Refused BEFORE the add: nothing on stdout, no mutation of any kind, not even the board item add.
        Assert.Equal("", out.Trim())
        Assert.Equal(0, transport.Count "item-edit")
        Assert.Equal(0, transport.Count "item-add")

    [<Fact>]
    let ``#1823 AC2 --status is an instruction, so it wins over a column already set`` () =
        // The distinction the item draws: the DEFAULT defers to what is there, an EXPLICIT column does
        // not. `add --status X` is `set-field <ref> Status X` reached from `add`, and a flag accepted and
        // then silently declined is #867's defect on #867's own flag.
        let transport =
            worldCapturingWithComments (ResizeArray()) (OnBoardSet "In progress") IssueBody None [ LightweightReceipt ]

        let code, _ = runAdd transport [ "add"; "FS.GG.SDD#42"; "--status"; "Ready" ]

        Assert.Equal(0, code)
        Assert.True(transport.Logged(statusWrite ItemId "opt_ready"), $"log: %A{transport.Log}")

    // ---- #2109 — `add --status Blocked` IS A COHERENT-PARK WRITE -------------------------------

    [<Fact>]
    let ``#2109 add --status Blocked refuses an incoherent new park before item-add`` () =
        let transport = world NotOnBoard

        let code, out = runAdd transport [ "add"; "FS.GG.SDD#42"; "--status"; "Blocked" ]

        Assert.NotEqual(0, code)
        Assert.Equal("", out.Trim())
        Assert.Equal(0, transport.Count "item-add")
        Assert.Equal(0, transport.Count "item-edit")

    [<Fact>]
    let ``#2109 add --status Blocked proceeds for a human sentinel park`` () =
        let body = """{"number":42,"body":"Paths: src/Thing.fs\n\nBlocked on: human/action\n\nClass: defect"}"""
        let transport = worldWithBody NotOnBoard body

        let code, out = runAdd transport [ "add"; "FS.GG.SDD#42"; "--status"; "Blocked" ]

        Assert.Equal(0, code)
        Assert.Equal(NewItemId, out.Trim())
        Assert.True(transport.Logged(statusWrite NewItemId "opt_blocked"), $"log: %A{transport.Log}")

    [<Fact>]
    let ``#2109 add --status Blocked proceeds for a live Blocked by edge without reading the body twice`` () =
        let body = """{"number":42,"body":"Paths: src/Thing.fs\n\nClass: defect"}"""
        // A live board field exists only on an already-boarded item.  This is the explicit override
        // route that previously held `In progress`, so the assertion proves both coherence and that
        // `add --status Blocked` still wins over an existing column once the reason is real.
        let transport = worldWithBodyAndBlockedBy (OnBoardSet "In progress") body (Some "FS-GG/FS.GG.SDD#9")

        let code, out = runAdd transport [ "add"; "FS.GG.SDD#42"; "--status"; "Blocked" ]

        Assert.Equal(0, code)
        Assert.Equal(ItemId, out.Trim())
        Assert.True(transport.Logged(statusWrite ItemId "opt_blocked"), $"log: %A{transport.Log}")
        Assert.Equal(1, transport.Count "issue-get FS-GG/FS.GG.SDD 42")

    // ---- AC1 — AND IT SAYS SO ----------------------------------------------------------------------

    [<Fact>]
    let ``#1823 AC1 the usage block states the default and that the row is not startable`` () =
        // Silence is how the defect worked: fourteen filers were told nothing and assumed the row was
        // schedulable. This is the weaker, prose half of AC1 — the behaviour is pinned by the mutation
        // legs above — and it is here so that softening the default costs an edit to the sentence that
        // promises it, in the same diff.
        let usage = Options.usage

        Assert.Contains("Status DEFAULTS TO `Backlog`", usage)
        Assert.Contains("NOT startable", usage)
        Assert.Contains("only ever fills an EMPTY column", usage)

    [<Fact>]
    let ``#1823 --status is accepted by add and still refused by a command that ignores it`` () =
        // The scope table's two halves. Widening `FStatus` to a third command must not widen it to all of
        // them — that was #867's mechanism, where `--status` was global and every command swallowed it.
        Assert.Equal(Some "Ready", (Options.parse [ "add"; "FS.GG.SDD#42"; "--status"; "Ready" ] |> Result.toOption |> Option.get).Status)

        match Options.parse [ "heartbeat"; "FS.GG.SDD#42"; "--status"; "Ready" ] with
        | Ok _ -> failwith "`heartbeat --status` must still be REFUSED — widening the scope table by one command may not widen it to all"
        | Error e -> Assert.Contains("--status", e)

    // ---- .github#2690 DIRECTION C: THE #1823 DEFAULT HAD TO BE RECORDED, NOT ONLY WRITTEN -------------
    //
    // The stderr line this default already printed promised the row was *"VISIBLE to triage, but NOT
    // startable … promoting it there is a deliberate act"*. That sentence was false for the whole of
    // #1823's life. `Backlog` records no intent, and the next `reconcile --apply` pass derived `Auto` from
    // the row's own declared paths and promoted it — with no operator anywhere in the loop, which is why
    // .github#2690 calls this the direction that needs no deliberate park to go wrong. `#2678`, `#2679`,
    // `#2683`, `#2684` and `#2688` all read `Ready` within the hour of being filed to `Backlog`.
    //
    // THE PAIR IS THE GATE HERE TOO, for this module's own stated reason: the risk is not that the receipt
    // fails to fire, it is that it fires when `add` did NOT write a column. A receipt on the idempotence
    // arm would record an intent for a row somebody else's `In progress` claim owns, and `tryWatermark`
    // takes the NEWEST receipt — so it would outrank the real one.

    let private watermarksIn (posted: ResizeArray<string>) =
        posted
        |> Seq.map (fun b -> b.Trim())
        |> Seq.filter (fun b -> b.StartsWith "<!-- fsgg:lifecycle-watermark" && b.EndsWith "-->")
        |> List.ofSeq

    [<Fact>]
    let ``.github#2690 the #1823 default records the Backlog intent that keeps the row parked`` () =
        let posted = ResizeArray<string>()
        let transport = worldCapturing posted NotOnBoard IssueBody None

        let code, out = runAdd transport [ "add"; "FS.GG.SDD#42" ]

        Assert.Equal(0, code)
        Assert.Equal(NewItemId, out.Trim())
        Assert.True(transport.Logged(statusWrite NewItemId "opt_backlog"), $"%A{transport.Log}")

        // `IssueBody` declares `Paths: src/Thing.fs`, so `lifecyclePolicyIntent` answers `Auto` for this
        // row and `Auto` projects `Ready`. The receipt below is the only thing that outranks it.
        let recorded = List.exactlyOne (watermarksIn posted)
        Assert.Contains("intent=backlog", recorded)
        Assert.Contains("status=Backlog", recorded)

    [<Fact>]
    let ``.github#2690 add records NOTHING on the arm where it writes no column`` () =
        // AC4's row, from the intent side. `add` is idempotent and re-run routinely; a receipt minted here
        // would assert a scheduling decision nobody made, on a row already claimed and `In progress`.
        let posted = ResizeArray<string>()
        let transport = worldCapturing posted (OnBoardSet "In progress") IssueBody None

        let code, _ = runAdd transport [ "add"; "FS.GG.SDD#42" ]

        Assert.Equal(0, code)
        Assert.False(transport.Logged "single-select-option-id", $"no column may be written here: %A{transport.Log}")
        Assert.Empty(watermarksIn posted)

    [<Fact>]
    let ``.github#2690 an explicit add --status Ready records the Auto intent`` () =
        // `--status` is the caller naming the column — `set-field <ref> Status <S>` reached from `add`
        // (#1823 AC2) — so it carries the channel for the same reason the default does.
        let posted = ResizeArray<string>()
        let transport = worldCapturingWithComments posted NotOnBoard IssueBody None [ LightweightReceipt ]

        let code, _ = runAdd transport [ "add"; "FS.GG.SDD#42"; "--status"; "Ready" ]

        Assert.Equal(0, code)
        Assert.True(transport.Logged(statusWrite NewItemId "opt_ready"), $"%A{transport.Log}")

        let recorded = List.exactlyOne (watermarksIn posted)
        Assert.Contains("intent=auto", recorded)
        Assert.Contains("status=Ready", recorded)

    // ---- .github#2698 — `Ready` REQUIRES A CURRENT DELIVERY-ROUTE RECEIPT ---------------------------
    //
    // A row boarded `Ready` with no receipt is UNSCHEDULABLE FROM BIRTH: `Schedulability` maps a
    // stale/unreadable route to `AwaitingDeliveryRouteDecision`, `Batch` then skips it and reserves no
    // lane, and every board projection goes on reporting it as available work. Two rows were in that state
    // when the item was filed; a re-measurement on 2026-08-16T19:45Z over the live board found 23 of 31
    // open rows there, so the population is not a backlog — it regenerates at the rate the board files.
    //
    // WHY THESE LEGS LIVE IN THIS MODULE. The filed acceptance criterion named `add --status Ready` and
    // this file. The seam count then changed under it: a host boarded seven rows on 2026-08-16 and NOT ONE
    // reached `Ready` through `add --status Ready` (`.github#2698#issuecomment-5309155317`). `add` with no
    // `--status` defaults to `Backlog` (#1823), so the real doors were `set-field Status Ready` and —
    // for five of the seven — `reconcile --apply`, which derived `Ready` from policy and promoted rows
    // that had been deliberately parked, with no operator action at all. The refusal is therefore ONE
    // shared function reached through four doors, and it is proven at each of them here: a gate proven at
    // one door is a gate a scheduled job walks around.
    //
    // AND THE CORPUS IS TWO-SIDED, WHICH IS THE POINT. This gate asserts an ABSENCE, and `.github#2312`
    // measured what that costs when it is tested from one side only: an ordering gate whose check matched
    // NOTHING shipped green and evadable, and its repair added a corpus of N spellings that must match and
    // M lookalikes that must not. So every refusal leg below has a PRESENCE partner driven through the
    // same seam. A receipt reader that returned "stale" for everything would satisfy every refusal here
    // and red every presence leg; one that returned "current" for everything does the opposite.

    let private ClaimedAt = DateTimeOffset.UtcNow.UtcTicks

    /// A LIVE claim marker for the identity `runVerbWithStderr` pins, so `release` can reach its own
    /// `--status` gate. It doubles as lookalike 4: a real row's ledger always has one of these beside the
    /// receipt, and the route reader must not mistake it for one.
    let private LiveClaimMarker =
        $"<!-- fsgg:claim worker=vole-418 lease=120 renewed=%d{ClaimedAt} session=ed60050b prev=Backlog pathRepo=FS.GG.SDD -->"

    /// The same world, with the receipt ledger read FAILING rather than answering empty (#266).
    ///
    /// `failure` is a PARAMETER because `Http 502` and `RateLimited` are not the same finding and the
    /// fixture could not tell them apart when it served only the first: `Errors.exitCode` maps `Http _`
    /// to 1, which is the value `ExitError` already carries, so flattening the gate's
    /// `Error(Errors.exitCode e)` to `Error ExitError` left the whole suite green. The claim about
    /// EX_RATE was true and ungated — the fixture, not the subject, was what the inversion measured.
    let private worldWithLedgerFailure (column: Column) (failure: Errors.IoError) =
        let board = Board column

        Fake.Recorder(fun (req: Request) ->
            let path = req.Path.Trim '/'

            match req.Method, path with
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, _) when document.Contains "items(first: 100" ->
                    ok """{"data":{"organization":{"projectV2":{"items":{"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                | Query(document, _) -> graphqlAnswer board None document
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/42" -> ok IssueBody
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/42/comments" -> Error failure
            | "GET", "repos/FS-GG/FS.GG.SDD/issues" -> ok "[]"
            | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

    let private worldWithUnreadableLedger (column: Column) =
        worldWithLedgerFailure column (Errors.Http(502, "the receipt ledger could not be read"))

    let private runSetField transport args = runVerbWithStderr Client.setField transport args
    let private runRelease transport args = runVerbWithStderr Client.release transport args

    let private addReady (comments: string list) =
        worldCapturingWithComments (ResizeArray()) NotOnBoard IssueBody None comments

    /// `set-field` writes a column on a row that is ALREADY BOARDED — it never adds one. Driving it
    /// against `NotOnBoard` would red for "not an item on this board", which is a different refusal, and a
    /// leg that reds for the wrong reason is not evidence about the gate under test.
    let private setFieldWorld (comments: string list) =
        worldCapturingWithComments (ResizeArray()) OnBoardUnset IssueBody None comments

    // ---- DOOR 1: `add --status Ready` (AC1) --------------------------------------------------------

    [<Fact>]
    let ``.github#2698 AC1 add --status Ready is REFUSED when the row has no route receipt`` () =
        let transport = addReady []

        let code, out, err = runAddWithStderr transport [ "add"; "FS.GG.SDD#42"; "--status"; "Ready" ]

        Assert.NotEqual(0, code)
        // BEFORE ANY WRITE, exactly as `--status Redy` and `--status Blocked` already refuse: nothing on
        // stdout, no board item, no column. A refusal that boarded the row first would leave behind the
        // very thing it is refusing to create.
        Assert.Equal("", out.Trim())
        Assert.Equal(0, transport.Count "item-add")
        Assert.Equal(0, transport.Count "item-edit")
        // The row cannot be promoted until an AGENT authors the decision, so the refusal has to name the
        // command that does it — a refusal a reader cannot act on stalls triage where `batch` used to.
        Assert.Contains("delivery-route record", err)

    [<Fact>]
    let ``.github#2698 add --status Ready PROCEEDS on a current lightweight receipt`` () =
        // THE PRESENCE HALF. Without this leg a receipt reader that recognised nothing would satisfy every
        // refusal in this section and ship the gate evadable — `.github#2312`'s exact failure.
        let transport = addReady [ LightweightReceipt ]

        let code, out, err = runAddWithStderr transport [ "add"; "FS.GG.SDD#42"; "--status"; "Ready" ]

        Assert.Equal(0, code)
        Assert.Equal(NewItemId, out.Trim())
        Assert.True(transport.Logged(statusWrite NewItemId "opt_ready"), $"log: %A{transport.Log}")
        Assert.DoesNotContain("delivery-route record", err)

    [<Fact>]
    let ``.github#2698 an sdd-required receipt is just as current as a lightweight one`` () =
        // The gate's question is "is there a CURRENT decision", never "which route did it pick". A reader
        // that only recognised the word `lightweight` would pass the leg above and fail here.
        let transport = addReady [ SddRequiredReceipt ]

        let code, _ = runAdd transport [ "add"; "FS.GG.SDD#42"; "--status"; "Ready" ]

        Assert.Equal(0, code)
        Assert.True(transport.Logged(statusWrite NewItemId "opt_ready"), $"log: %A{transport.Log}")

    [<Fact>]
    let ``.github#2698 the receipt is found among the protocol traffic that surrounds it`` () =
        // A real row's ledger is mostly claim markers, messages and review records. A reader that only
        // inspected the first (or the last) comment would pass every other presence leg here and fail on
        // every live row, which is the shape `structuredRouteLedger`'s complete paginated read exists for.
        let transport = addReady [ ClaimMarkerComment; "an ordinary human comment"; LightweightReceipt; "a later reply" ]

        let code, _ = runAdd transport [ "add"; "FS.GG.SDD#42"; "--status"; "Ready" ]

        Assert.Equal(0, code)
        Assert.True(transport.Logged(statusWrite NewItemId "opt_ready"), $"log: %A{transport.Log}")

    // ---- THE LOOKALIKES — M SPELLINGS THAT MUST NOT COUNT AS A RECEIPT -----------------------------

    [<Theory>]
    [<InlineData("other-subject")>]
    [<InlineData("unmarked")>]
    [<InlineData("quoted")>]
    [<InlineData("claim-marker")>]
    let ``.github#2698 a lookalike ledger does not authorize the promotion`` (shape: string) =
        let comment =
            match shape with
            // A valid, well-formed receipt — for a DIFFERENT row. What a copy-paste actually produces, and
            // the one `validateRouteLedger`'s subject binding exists to catch.
            | "other-subject" -> OtherSubjectReceipt
            // The receipt payload with no marker. A reader scanning for `"route"` anywhere would take it.
            | "unmarked" -> UnmarkedReceiptJson
            // The marker verbatim, with prose in front. Quoting the protocol in a discussion must not board
            // a row; the marker has to OPEN the comment.
            | "quoted" -> QuotedMarkerComment
            // Another protocol marker entirely, on the same row.
            | "claim-marker" -> ClaimMarkerComment
            | other -> failwithf "the fixture's own case list does not cover '%s'" other

        let transport = addReady [ comment ]

        let code, out, _ = runAddWithStderr transport [ "add"; "FS.GG.SDD#42"; "--status"; "Ready" ]

        Assert.NotEqual(0, code)
        Assert.Equal("", out.Trim())
        Assert.Equal(0, transport.Count "item-add")
        Assert.Equal(0, transport.Count "item-edit")

    // ---- AC2 — THE COLUMNS THAT OWE NO ROUTE DECISION ARE UNTOUCHED --------------------------------

    [<Fact>]
    let ``.github#2698 AC2 a bare add still defaults to Backlog and never reads the receipt ledger`` () =
        // A row deliberately not yet schedulable owes no route decision — that is what parking it means.
        // The read-count assertion is the load-bearing half: it pins that the #1823 default costs no new
        // REST call, so the gate cannot become a per-filing tax on the one verb #1823 made unconditional.
        let transport = addReady []

        let code, out = runAdd transport [ "add"; "FS.GG.SDD#42" ]

        Assert.Equal(0, code)
        Assert.Equal(NewItemId, out.Trim())
        Assert.True(transport.Logged(statusWrite NewItemId "opt_backlog"), $"log: %A{transport.Log}")
        Assert.Equal(0, transport.Count "comment-list FS-GG/FS.GG.SDD 42")

    [<Fact>]
    let ``.github#2698 AC2 an explicit --status Backlog is unaffected by the route gate`` () =
        let transport = addReady []

        let code, _ = runAdd transport [ "add"; "FS.GG.SDD#42"; "--status"; "Backlog" ]

        Assert.Equal(0, code)
        Assert.True(transport.Logged(statusWrite NewItemId "opt_backlog"), $"log: %A{transport.Log}")

    // ---- #266 — AN UNREAD LEDGER IS NOT AN ABSENT ONE, AND NOT A PRESENT ONE -----------------------

    [<Fact>]
    let ``.github#2698 a receipt ledger that could not be READ refuses the promotion, fail-closed`` () =
        // The direction that would be silent: a transport fault answered as "no receipt, carry on" would
        // board an unschedulable row on a green exit, and one answered as "receipt present, carry on"
        // would board it without any decision at all. Neither is an answer this engine may give.
        let transport = worldWithUnreadableLedger NotOnBoard

        let code, out, _ = runAddWithStderr transport [ "add"; "FS.GG.SDD#42"; "--status"; "Ready" ]

        Assert.NotEqual(0, code)
        Assert.Equal("", out.Trim())
        Assert.Equal(0, transport.Count "item-add")
        Assert.Equal(0, transport.Count "item-edit")

    [<Fact>]
    let ``.github#2698 a RATE-LIMITED receipt read refuses as EX_RATE, not as a permanent error`` () =
        // THE CLAIM THE CHANGE MAKES, NOW HELD. `requireCurrentRouteIfReady` returns the underlying read's
        // OWN exit code rather than a flat `ExitError`, so a rate-limited ledger read keeps its back-off
        // contract instead of reading to a JSON worker as a permanent refusal it should stop retrying.
        //
        // THE FIXTURE HAD TO CHANGE TO SAY THIS. The only failing-ledger world served `Http 502`, and
        // `Errors.exitCode` maps `Http _` to 1 — the same integer `ExitError` carries — so no assertion
        // over the exit code could separate the two, and flattening `Error(Errors.exitCode e)` to
        // `Error ExitError` left the whole suite green. A gate whose inversion survives is not a gate; the
        // production shape the claim is about was simply absent from the corpus.
        let transport =
            worldWithLedgerFailure NotOnBoard (Errors.RateLimited(Errors.RestBudget(Some "core"), None))

        let code, out, _ = runAddWithStderr transport [ "add"; "FS.GG.SDD#42"; "--status"; "Ready" ]

        Assert.Equal(Errors.ExRate, code)
        // And it is still a refusal before any write — the back-off classification must not cost the
        // fail-closed property it rides on.
        Assert.Equal("", out.Trim())
        Assert.Equal(0, transport.Count "item-add")
        Assert.Equal(0, transport.Count "item-edit")

    // ---- DOOR 2: `set-field <ref> Status Ready` (AC5 — COVERED, NOT DEFERRED) -----------------------

    [<Fact>]
    let ``.github#2698 AC5 set-field Status Ready is REFUSED with no receipt`` () =
        // THE DOOR THE FILED AC DID NOT NAME AND OPERATORS ACTUALLY USE: three of the seven rows the host
        // measured reached `Ready` through exactly this command.
        let transport = setFieldWorld []

        let code, out, err = runSetField transport [ "set-field"; "FS.GG.SDD#42"; "Status"; "Ready" ]

        Assert.NotEqual(0, code)
        Assert.Equal("", out.Trim())
        Assert.Equal(0, transport.Count "item-edit")
        Assert.Contains("delivery-route record", err)

    [<Fact>]
    let ``.github#2698 AC5 set-field Status Ready PROCEEDS on a current receipt`` () =
        let transport = setFieldWorld [ LightweightReceipt ]

        let code, out, _ = runSetField transport [ "set-field"; "FS.GG.SDD#42"; "Status"; "Ready" ]

        Assert.Equal(0, code)
        Assert.Contains("Status", out)
        Assert.True(transport.Logged(statusWrite ItemId "opt_ready"), $"log: %A{transport.Log}")

    [<Fact>]
    let ``.github#2698 AC5 set-field --batch Status=Ready is REFUSED with no receipt`` () =
        // THE FIFTH DOOR, AND IT HAD NO RED LEG. The change's own inversion table proved four doors
        // independently load-bearing and omitted this one, so deleting its gate call left 848/848 green —
        // `.github#2312`'s exact shape, applied rigorously at four seams and not at the fifth. The two
        // pre-existing `batch: true` legs in `BlockerLintTests` are PRESENCE legs: they serve a receipt and
        // expect success, so they pass with the gate and without it.
        //
        // BEFORE ANY ALIAS IS EMITTED. `set-field --batch` writes one aliased document, so a refusal that
        // arrived mid-document would half-write the row — the `Partial` outcome the batch path treats as
        // its worst answer, reached by a gate meant to prevent a write.
        let transport = setFieldWorld []

        let code, out, err = runSetField transport [ "set-field"; "--batch"; "FS.GG.SDD#42"; "Status=Ready" ]

        Assert.NotEqual(0, code)
        Assert.Equal("", out.Trim())
        Assert.Equal(0, transport.Count "batch-mutation")
        Assert.Equal(0, transport.Count "item-edit")
        Assert.Contains("delivery-route record", err)

    [<Fact>]
    let ``.github#2698 AC5 set-field --batch Status=Ready PROCEEDS on a current receipt`` () =
        let transport = setFieldWorld [ LightweightReceipt ]

        let code, _, _ = runSetField transport [ "set-field"; "--batch"; "FS.GG.SDD#42"; "Status=Ready" ]

        Assert.Equal(0, code)
        Assert.True(transport.Logged "opt_ready", $"log: %A{transport.Log}")

    [<Fact>]
    let ``.github#2698 AC5 set-field Status Backlog is untouched by the route gate`` () =
        // The same command, one column over. A gate written as "refuse a Status write without a receipt"
        // rather than "refuse a READY write without one" would red here — and would make parking a row
        // require the very decision parking it defers.
        let transport = setFieldWorld []

        let code, _, _ = runSetField transport [ "set-field"; "FS.GG.SDD#42"; "Status"; "Backlog" ]

        Assert.Equal(0, code)
        Assert.True(transport.Logged(statusWrite ItemId "opt_backlog"), $"log: %A{transport.Log}")

    // ---- DOOR 3: `release <ref> --status Ready` -----------------------------------------------------

    [<Fact>]
    let ``.github#2698 release --status Ready is refused BEFORE the lock is dropped`` () =
        // THE ORDERING IS THE ASSERTION. A refusal that arrived after `Writes.release` deleted the marker
        // would leave the holder with no lock and no way to retry — strictly worse than the row it was
        // protecting. Anchored on the ABSENCE of the marker delete, which is a fact about the lock rather
        // than about the sentence the CLI printed.
        let transport =
            worldCapturingWithComments (ResizeArray()) (OnBoardSet "In progress") IssueBody None [ LiveClaimMarker ]

        let code, _, _ = runRelease transport [ "release"; "FS.GG.SDD#42"; "--status"; "Ready" ]

        Assert.NotEqual(0, code)
        Assert.Equal(0, transport.Count "comment-delete")
        Assert.Equal(0, transport.Count "item-edit")

    [<Fact>]
    let ``.github#2698 release --status Ready proceeds, and drops the lock, on a current receipt`` () =
        let transport =
            worldCapturingWithComments
                (ResizeArray())
                (OnBoardSet "In progress")
                IssueBody
                None
                [ LiveClaimMarker; LightweightReceipt ]

        let code, _, _ = runRelease transport [ "release"; "FS.GG.SDD#42"; "--status"; "Ready" ]

        Assert.Equal(0, code)
        Assert.True(transport.Logged "comment-delete", $"the lock must actually drop here: %A{transport.Log}")
        Assert.True(transport.Logged(statusWrite ItemId "opt_ready"), $"log: %A{transport.Log}")

    // ---- DOOR 4: `reconcile --apply` — THE SEAM WITH NO OPERATOR IN IT ------------------------------
    //
    // THE PART THE FILED ITEM DID NOT CONTAIN. `.github#2721`, `#2722` and `#2723` were deliberately set
    // to `Backlog` by a host honouring a design's ordering. The next `reconcile --apply` reported
    // `LIFECYCLE-PROJECTION-LAG … Status=Ready` for all three and PROMOTED them — no `add`, no
    // `set-field`, no human in the loop; the reducer derived `Ready` from policy and applied it. Every one
    // landed with no receipt and was then found unschedulable by `batch --explain`.
    //
    // So a refusal that stops at the operator doors is a refusal a scheduled job walks around, and this
    // pair is what says so. It does NOT assert that the reducer stops DERIVING `Ready` — that projection
    // has a purpose this row did not study, and `.github#2690` may change its shape — only that a derived
    // `Ready` is not WRITTEN onto a row that cannot be scheduled once it lands.
    module private ReducerPromotionFixture =

        /// `#42` is `Blocked`; its `Blocked by` field names `#8`, which is CLOSED. That satisfies the
        /// reducer's precondition on the field alone, so the lifecycle projection computes `Ready` — the
        /// exact chore the host watched promote three parked rows.
        let private itemJson (n: int) (status: string) (blockedBy: string option) (state: string) (body: string option) =
            let blockedByJson =
                match blockedBy with
                | Some b -> $"""{{"text":"%s{b}"}}"""
                | None -> "null"

            let bodyJson =
                body |> Option.map System.Text.Json.JsonSerializer.Serialize |> Option.defaultValue "null"

            $"""{{"status":{{"name":"%s{status}"}},"blockedBy":%s{blockedByJson},"content":{{"__typename":"Issue","number":%d{n},"title":"item %d{n}","body":%s{bodyJson},"state":"%s{state}","repository":{{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}}}}"""

        /// Which chore the reducer derives, and therefore which gate is asked.
        type Mode =
            /// `#42` is `Blocked`, its one blocker is CLOSED -> the projection computes `Ready`. The
            /// route gate is the one that answers.
            | PromoteReady
            /// `#42` is `Ready`, its `Blocked by` names an OPEN blocker -> the projection computes
            /// `Blocked`; but the LIVE `Blocked by` read at mutation time comes back EMPTY and the body
            /// carries no `Blocked on:` sentinel, so the coherence gate refuses.
            ///
            /// THIS IS THE RACE `requireCoherentBlockedWrite`'S OWN CALL SITE DESCRIBES — *"the scan that
            /// derived this chore is stale by definition once another actor can clear `Blocked by`"* — and
            /// it is the only way that gate refuses inside the reducer. It had no behavioural leg
            /// anywhere: the sole match was a `Regex.Matches` occurrence count over source text.
            | StaleBlockedPark

        let transport (comments: string list) (mode: Mode) =
            let body42 = "Paths: src/A.fs"

            let items =
                match mode with
                | PromoteReady ->
                    [ itemJson 42 "Blocked" (Some "FS-GG/FS.GG.SDD#8") "OPEN" (Some body42)
                      itemJson 8 "Done" None "CLOSED" None ]
                | StaleBlockedPark ->
                    [ itemJson 42 "Ready" (Some "FS-GG/FS.GG.SDD#8") "OPEN" (Some body42)
                      itemJson 8 "Ready" None "OPEN" None ]
                |> String.concat ","

            Fake.Recorder(fun (req: Request) ->
                match req.Method, req.Path.Trim '/' with
                | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
                | "POST", "graphql" ->
                    match req.Body with
                    | Query(document, variables) ->
                        if document.Contains "projectsV2" then
                            ok ProjectAnswer
                        elif document.Contains "fields(first" then
                            // ITS OWN FIELD MAP, not the module's. The lifecycle chore writes BOTH
                            // `Status=Ready` and an emptied `Blocked by` in one aliased document, so this
                            // board must carry the text field; the `add` legs' board deliberately does not,
                            // and widening theirs would change what their own #2109 legs measure.
                            ok """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_backlog","name":"Backlog"},{"id":"opt_ready","name":"Ready"},{"id":"opt_blocked","name":"Blocked"},{"id":"opt_wip","name":"In progress"}]},{"id":"PVTF_blocked","name":"Blocked by","dataType":"TEXT"}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                        elif document.Contains "items(first" then
                            ok
                                $"""{{"data":{{"organization":{{"projectV2":{{"items":{{"pageInfo":{{"hasNextPage":false,"endCursor":null}},"nodes":[%s{items}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
                        elif document.Contains "updateProjectV2ItemFieldValue" || document.Contains "clearProjectV2ItemFieldValue" then
                            if document.Contains "f0:" then
                                ok """{"data":{"f0":{"clientMutationId":null},"f1":{"clientMutationId":null}},"rateLimit":{"cost":1,"remaining":4977}}"""
                            else
                                ok """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}},"rateLimit":{"cost":1,"remaining":4977}}"""
                        elif document.Contains "\"Blocked by\"" then
                            // `Board.itemBlockedBy`, the LIVE re-read the coherence gate makes immediately
                            // before the mutation. EMPTY in both modes: in `StaleBlockedPark` that is the
                            // whole point, and in `PromoteReady` the gate never asks (the resolved status
                            // is not `Blocked`), so one answer serves both without ambiguity.
                            ok """{"data":{"repository":{"issue":{"projectItems":{"nodes":[]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                        elif document.Contains "projectItems" then
                            ok
                                $"""{{"data":{{"repository":{{"issue":{{"projectItems":{{"nodes":[{{"id":"%s{ItemId}","project":{{"number":12}}}}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
                        elif document.Contains "node(id: $itemId)" then
                            // `verifyWrites` — the fresh read-back that must prove the mutation LANDED
                            // before the pass persists its ordering watermark. The board here answers as a
                            // board that took the write, so a promotion that reaches the wire is reported
                            // `written` rather than `failed`, and the two legs differ in ONE input.
                            // PER FIELD, because `verifyWrites` reads back BOTH of them and a board that
                            // answered `Ready` to the `Blocked by` probe would report the emptied text
                            // field as unverified and sink the whole row to `failed`.
                            let field =
                                variables
                                |> List.tryPick (fun (k, v) ->
                                    match k, v with
                                    | "field", VString name -> Some name
                                    | _ -> None)

                            match field with
                            | Some "Status" -> ok """{"data":{"node":{"fieldValueByName":{"name":"Ready"}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                            | _ -> ok """{"data":{"node":{"fieldValueByName":null}},"rateLimit":{"cost":1,"remaining":4977}}"""
                        else
                            Error(Errors.NotFound $"the reducer fixture serves no answer for: %s{document}")
                    | _ -> Error(Errors.NotFound "a graphql call with no document")
                | "GET", "repos/FS-GG/FS.GG.SDD/issues/42" ->
                    ok (System.Text.Json.JsonSerializer.Serialize {| number = 42; body = body42 |})
                | "GET", "repos/FS-GG/FS.GG.SDD/issues/42/comments" -> ok (commentsJson comments)
                | "POST", "repos/FS-GG/FS.GG.SDD/issues/42/comments" -> ok """{"id":9042}"""
                | "GET", "repos/FS-GG/FS.GG.SDD/issues/8" ->
                    ok (System.Text.Json.JsonSerializer.Serialize {| number = 8; body = "Paths: src/B.fs" |})
                | "GET", "repos/FS-GG/FS.GG.SDD/issues/8/comments" -> ok "[]"
                | "POST", "repos/FS-GG/FS.GG.SDD/issues/8/comments" -> ok """{"id":9008}"""
                | "GET", "repos/FS-GG/FS.GG.SDD/issues" -> ok "[]"
                | "GET", "repos/FS-GG/FS.GG.SDD/pulls" -> ok "[]"
                | "GET", "repos/FS-GG/FS.GG.SDD/git/matching-refs/heads/item/42-" -> ok "[]"
                | "GET", "repos/FS-GG/FS.GG.SDD/git/matching-refs/heads/item/8-" -> ok "[]"
                | m, p -> Error(Errors.NotFound $"the reducer fixture serves no %s{m} %s{p}"))

    let private runReconcileMode (comments: string list) (mode: ReducerPromotionFixture.Mode) =
        let transport = ReducerPromotionFixture.transport comments mode
        let code, out, err = runVerbWithStderr Client.reconcile transport [ "reconcile"; "--repo"; "FS.GG.SDD"; "--apply"; "--json" ]
        transport, code, out, err

    let private runReconcileApply (comments: string list) =
        runReconcileMode comments ReducerPromotionFixture.PromoteReady

    [<Fact>]
    let ``.github#2698 reconcile --apply does NOT promote a receipt-less row it derived Ready for`` () =
        let transport, code, out, _ = runReconcileApply []

        // THE EXIT CODE IS PART OF THE ANSWER, and discarding it is what hid a scheduled-workflow red.
        // `coord-board-reconcile.yml` ends `exit "$rc"` on this exact command, and nothing recurring
        // authors a receipt — so a refusal classified as a failure would red that workflow until a human
        // authored receipts by hand. The pass ran to completion and the board is not wrong; ONE derived
        // remedy was declined as outside this pass's authority, which is `reconcile`'s own stated
        // report-only boundary and the class `NotAttempted` already carries.
        Assert.Equal(0, code)
        Assert.Contains("\"outcome\":\"not-attempted\"", out)
        // The row is not merely absent from the failures — it names what is owed and how to discharge it.
        Assert.Contains("delivery-route record", out)
        // The chore IS still derived — the reducer is untouched. What must not happen is the WRITE.
        Assert.Contains("LIFECYCLE-PROJECTION-LAG", out)
        // ANCHORED ON THE OPTION ID THE CONTROL BELOW PRODUCES, deliberately. The lifecycle chore writes
        // two fields in ONE aliased document, which the recorder logs as `batch-mutation`, not
        // `item-edit` — so an absence asserted against `item-edit` would be an absence of something this
        // pass never emits in either direction, and would hold just as well against a gate that did
        // nothing at all. `opt_ready` is the byte that differs.
        Assert.False(transport.Logged "opt_ready", $"no Ready promotion may reach the wire: %A{transport.Log}")
        Assert.DoesNotContain("\"outcome\":\"written\"", out)

    [<Fact>]
    let ``.github#2698 reconcile --apply promotes the SAME row once it carries a receipt`` () =
        // THE CONTROL, and it is what makes the leg above evidence about the gate rather than about the
        // fixture. One input differs — the ledger — and the mutation appears.
        let receipt = StructuredFixtures.routeComment Subject (Some DeliveryRoute.Lightweight) "fixture-rook" None
        let transport, code, out, err = runReconcileApply [ receipt ]

        Assert.Equal(0, code)
        Assert.Contains("LIFECYCLE-PROJECTION-LAG", out)
        Assert.True(transport.Logged "opt_ready", $"out: %s{out}\nerr: %s{err}\nlog: %A{transport.Log}")
        Assert.Contains("\"outcome\":\"written\"", out)

    [<Fact>]
    let ``.github#2698 a Status=Blocked coherence refusal still FAILS the reducer pass, and says so`` () =
        // THE OTHER SIDE OF THE DISCRIMINATOR THIS CHANGE INTRODUCED (round-1 F5).
        //
        // `routeRefused` is a NEW two-sided condition, and this change is what makes the `Status=Blocked`
        // arm reachable only when it is false. The route side is pinned by the two legs above; without
        // this leg, dropping the `&& Result.isOk blockedGate` conjunct left 851/851 green — and that
        // mutation is not cosmetic. It routes an INCOHERENT PARK into the `NotAttempted` arm: `#2079`'s
        // boundary stops failing the pass, `rc` falls 1 -> 0, and the operator is told the row "has no
        // current delivery-route receipt" when the route gate returned `Ok`. That is exactly the
        // mis-attribution this round repaired, with the arms swapped.
        //
        // THE LESSON, RECORDED WHERE IT COST SOMETHING: when a repair introduces a condition, BOTH arms
        // are new subjects — including the arm whose behaviour is unchanged. Preserving old behaviour
        // through a new guard is still a new claim about that behaviour, and this file's own two-sided
        // discipline ran over the DECISION that had just been made rather than over the BOOLEAN that had
        // just been written.
        let transport, code, out, err = runReconcileMode [] ReducerPromotionFixture.StaleBlockedPark

        // The projection really did compute `Blocked` — otherwise this leg would be asserting a refusal
        // that never happened, and would pass against a fixture that derived nothing at all.
        Assert.Contains("\"value\":\"Blocked\"", out)

        // THE PASS FAILS. An incoherent park IS the board being wrong, and it keeps the non-zero exit
        // that `coord-board-reconcile` is meant to see.
        Assert.NotEqual(0, code)
        Assert.Contains("\"outcome\":\"failed\"", out)

        // AND THE MESSAGE IS THE BLOCKED ONE. This is the assertion that pins the DISCRIMINATOR rather
        // than merely the arm: an implementation that reached the right exit code by the wrong branch
        // would still misname the gate to whoever reads the receipt, which is the defect this round found
        // in the mirror direction.
        Assert.Contains("Status=Blocked coherence gate", out)
        Assert.DoesNotContain("has no current delivery-route receipt", out)
        Assert.DoesNotContain("\"outcome\":\"not-attempted\"", out)

        // Nothing was written on either field, and the refusal named the park rather than the route.
        Assert.Equal(0, transport.Count "batch-mutation")
        Assert.Equal(0, transport.Count "item-edit")
        Assert.Contains("incoherent park", err)
