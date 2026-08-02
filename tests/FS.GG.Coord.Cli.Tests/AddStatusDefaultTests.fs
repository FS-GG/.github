namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open Xunit
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
              NextLink = None }

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
    let private graphqlAnswer (board: Board) (document: string) : Errors.IoResult<Response> =
        if document.Contains "projectsV2" then
            ok ProjectAnswer
        elif document.Contains "fields(first" then
            ok FieldsAnswer
        elif document.Contains "\"Blocked by\"" then
            // #2109 checks this before item-add. These fixtures deliberately have no live edge.
            ok """{"data":{"repository":{"issue":{"projectItems":{"nodes":[]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
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

    let private worldWithBody (column: Column) issueBody =
        let board = Board column

        Fake.Recorder(fun (req: Request) ->
            let path = req.Path.Trim '/'

            match req.Method, path with
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, _) when document.Contains "items(first: 100" ->
                    ok """{"data":{"organization":{"projectV2":{"items":{"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[{"status":{"name":"Ready"},"blockedBy":null,"class":null,"severity":null,"phase":null,"repoScope":null,"content":{"__typename":"Issue","number":9,"title":"narrow sibling","state":"OPEN","createdAt":"2026-07-30T00:00:00Z","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                | Query(document, _) -> graphqlAnswer board document
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/42" -> ok issueBody
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/9" -> ok """{"number":9,"body":"Paths: docs/reports/new-file.md"}"""
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/9/comments" -> ok "[]"
            | "GET", "repos/FS-GG/FS.GG.SDD/issues" -> ok "[]"
            | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

    let private world (column: Column) = worldWithBody column IssueBody

    let private context (transport: Fake.Recorder) : Client.Context =
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

    let private runAddWithStderr (transport: Fake.Recorder) (args: string list) : int * string * string =
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

            let code = Client.addCmd (context transport) opts
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
        let transport = world NotOnBoard

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
        let transport = world (OnBoardSet "In progress")

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
