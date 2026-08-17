namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Text.Json
open Xunit
open FS.GG.Coord
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.Cli

/// .github#2351 — `overlap` reported DISJOINT BY CONSTRUCTION for two items in the SAME repository
/// whenever one of them carried the board's `cross-repo` `Repo Scope` value and the other did not, and it
/// did so WITHOUT READING EITHER TOUCH-SET. That is not a missed conflict, it is an AUTHORIZED one: the
/// claim system's one job is that two workers never hold the same path, and this defect removed that
/// guarantee silently and permissively for any pair where one side's `Repo Scope` was the sentinel.
///
/// ROOT CAUSE. `pathRepoOf`/`targetPathRepo`/`otherPathRepo` in `Client.fs` all took a raw `Repo Scope` /
/// marker `PathRepo` string and handed it straight to `Option.defaultValue <ref's own repo>`.
/// `Option.defaultValue` only ever declines a `None` — and a board Repo Scope of `cross-repo` (the ONE
/// value `docs/coordination/board-schema.md` documents as the deliberate NON-roster option; every other
/// option is a same-named `registry/repos.yml` row) is a `Some "cross-repo"`, not a `None`. So the literal
/// string `"cross-repo"` was compared against a genuine repository name, could never equal it, and the
/// engine printed a confident `DISJOINT — … are in different repos …` for two items in the SAME
/// repository. `Client.pathRepoOrFallback` is the fix: the one place that question is settled (AC5) is
/// that a scope naming no repository behaves exactly like an ABSENT one.
///
/// THE LIVE REPRODUCTION THE ISSUE NAMED. `.github#2345` (claim `rook-9dc2`) and `.github#2070` (claim
/// `smew-e1d9`) were both live claims in `FS-GG/.github`, both declaring the same four tokens, and
/// `.github#2070`'s `Repo Scope` was `cross-repo`. `overlap .github#2345 .github#2070` reported DISJOINT.
/// The fixtures below reproduce that EXACT shape — same repository, one scope the sentinel, the other not,
/// at least one shared token — for both surfaces the acceptance criteria name: the two-ref `overlap`
/// command (AC1/AC2, the reported repro) and the marker-backed `activeCollisions` scan `overlap --active`/
/// `widen`/`set-paths`/`claim` all share (AC3).
///
/// GATE-INVERSION EVIDENCE. Reverting `Client.fs`'s `pathRepoOrFallback` call sites back to a bare
/// `Option.defaultValue` reproduces the exact pre-fix behaviour, and every `Assert.Equal("OVERLAP", …)`
/// / `Assert.Equal(Kernel.ExitContended, …)` leg below turns red — the fixture reports `DISJOINT` /
/// `ExitGreen` instead, which is the confident-wrong verdict the issue is about. Recorded by hand against
/// the pre-fix source at review time; this is the reproducible mutation the gate-inversion discipline
/// asks for.
module OverlapTests =

    let private ok (body: string) : Errors.IoResult<Response> =
        Ok
            { Status = 200
              Body = body
              ETag = None
              NextLink = None
              Headers = Map.empty }

    // ---- the board: one project, one Status field, and the rows a test supplies ------------------------

    let private ProjectAnswer =
        """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""

    let private FieldsAnswer =
        """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"},{"id":"opt_wip","name":"In progress"}]}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""

    let private itemsAnswer (items: string) =
        $"""{{"data":{{"organization":{{"projectV2":{{"items":{{"pageInfo":{{"hasNextPage":false,"endCursor":null}},"nodes":[%s{items}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""

    /// One board row, as the `items(first: 100 …)` document (`Scan.fs`'s `BoardDoc`) renders it.
    /// `repoScope` is the LITERAL `Repo Scope` single-select value — `None` renders `null`, exactly an
    /// item whose scope was never set, and `Some "cross-repo"` is the sentinel this defect mishandled.
    let private boardRow (nwo: string) (number: int) (repoScope: string option) =
        let scope =
            repoScope
            |> Option.map (fun s -> $"""{{"name":"%s{s}"}}""")
            |> Option.defaultValue "null"

        $"""{{"status":{{"name":"In progress"}},"blockedBy":null,"class":null,"severity":null,"phase":null,"repoScope":%s{scope},"content":{{"__typename":"Issue","number":%d{number},"title":"item %d{number}","state":"OPEN","repository":{{"nameWithOwner":"%s{nwo}"}}}}}}"""

    let private graphqlAnswer (items: string) (document: string) : Errors.IoResult<Response> =
        if document.Contains "projectsV2" then
            ok ProjectAnswer
        elif document.Contains "fields(first" then
            ok FieldsAnswer
        elif document.Contains "items(first" then
            ok (itemsAnswer items)
        else
            Error(Errors.NotFound $"the fixture serves no answer for: %s{document}")

    // ---- AC1/AC2/AC4: the two-ref `overlap` command, `boardPathScopes`' own `pathRepoOf` -----------------

    /// The `overlap <ref-a> <ref-b>` fixture. Two board rows, each carrying its own `Repo Scope` and its
    /// own issue body — nothing else reads a network call on this path (`boardPathScopes` then
    /// `Reads.issueBody` per ref, exactly `Client.overlapCmd`'s two-ref branch).
    let private twoRefWorld
        (nwoA: string)
        (numberA: int)
        (scopeA: string option)
        (bodyA: string)
        (nwoB: string)
        (numberB: int)
        (scopeB: string option)
        (bodyB: string)
        =
        let items =
            [ boardRow nwoA numberA scopeA; boardRow nwoB numberB scopeB ]
            |> String.concat ","

        let issuePath (nwo: string) (n: int) =
            let parts = nwo.Split '/'
            $"repos/%s{parts.[0]}/%s{parts.[1]}/issues/%d{n}"

        Fake.Recorder(fun (req: Request) ->
            match req.Method, req.Path.Trim '/' with
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, _) -> graphqlAnswer items document
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
            | "GET", p when p = issuePath nwoA numberA ->
                ok (JsonSerializer.Serialize {| number = numberA; body = bodyA |})
            | "GET", p when p = issuePath nwoB numberB ->
                ok (JsonSerializer.Serialize {| number = numberB; body = bodyB |})
            | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

    let private context (transport: Fake.Recorder) : Kernel.Context =
        { Transport = transport
          Owner = "FS-GG"
          Title = "Coordination"
          DefaultRepo = Some ".github"
          ChoreLocks = [] }

    let private options (args: string list) : Options.Options =
        match Options.parse args with
        | Ok o -> o
        | Error e -> failwithf "the fixture's own argv did not parse: %s" e

    let private runOverlapIn (dir: string) (transport: Fake.Recorder) (args: string list) : int * string =
        let previousCache = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        let previousKitRoot = Environment.GetEnvironmentVariable "FSGG_KIT_ROOT"
        let stdout = Console.Out
        use captured = new StringWriter()

        try
            Directory.CreateDirectory dir |> ignore
            // A COLD CACHE PER TEST. `boardPathScopes`/`Board.bootstrapCached` and `Scan.board` both keep a
            // day cache keyed on owner/title (#418); a shared, ambient cache directory would let one test's
            // board bleed into the next one's "same repository, different scope" fixture.
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", dir)
            Console.SetOut captured

            let code = Client.overlapCmd (context transport) (options args)

            Console.Out.Flush()
            code, captured.ToString()
        finally
            Console.SetOut stdout
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", previousKitRoot)

    let private runOverlap (transport: Fake.Recorder) (args: string list) : int * string =
        let dir = Path.Combine(Path.GetTempPath(), "fsgg-2351-" + Guid.NewGuid().ToString "n")

        try
            runOverlapIn dir transport args
        finally
            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    [<Fact>]
    let ``#2351 AC1/AC2: same repo, one item's Repo Scope is cross-repo, shared token — OVERLAP, not DISJOINT`` () =
        // The exact live shape the issue reproduced: both refs `FS-GG/.github`, `.github#2070`'s scope is
        // the `cross-repo` sentinel, `.github#2345`'s is unset (defaults to its own repo, `.github`), and
        // both declare `docs/architecture.md`.
        let world =
            twoRefWorld
                "FS-GG/.github"
                2345
                None
                "Paths: docs/architecture.md"
                "FS-GG/.github"
                2070
                (Some "cross-repo")
                "Paths: docs/architecture.md"

        let code, out = runOverlap world [ "overlap"; "FS-GG/.github#2345"; "FS-GG/.github#2070" ]

        Assert.Equal(Kernel.ExitContended, code)
        Assert.Contains("OVERLAP", out)
        Assert.Contains("docs/architecture.md", out)
        Assert.DoesNotContain("DISJOINT", out)

    [<Fact>]
    let ``#2351 AC1/AC2: the same fixture with BOTH scopes cross-repo also collides`` () =
        // Neither side names a real repository, so both fall back to their own hosting repo — still the
        // SAME repository, so the tokens must still be compared. This is AC2's "never substitutes" read
        // from the other direction: the sentinel is symmetric, not merely a special case of "the OTHER
        // side is normal".
        let world =
            twoRefWorld
                "FS-GG/.github"
                2345
                (Some "cross-repo")
                "Paths: docs/architecture.md"
                "FS-GG/.github"
                2070
                (Some "cross-repo")
                "Paths: docs/architecture.md"

        let code, out = runOverlap world [ "overlap"; "FS-GG/.github#2345"; "FS-GG/.github#2070" ]

        Assert.Equal(Kernel.ExitContended, code)
        Assert.Contains("OVERLAP", out)

    [<Fact>]
    let ``#2351 AC1: genuinely different repos stay DISJOINT BY CONSTRUCTION, sentinel or not`` () =
        // The short-circuit `#353` relies on must still fire for a REAL cross-repo pair — AC1's second
        // sentence. Two different repositories, one of them scoped `cross-repo`, sharing a token that would
        // collide if they were ever compared: still DISJOINT, and no body read is implied by that (both
        // issue bodies below are never fetched, so a fixture 404 on either would fail this leg loudly).
        let world =
            twoRefWorld
                "FS-GG/.github"
                2345
                (Some "cross-repo")
                "Paths: docs/architecture.md"
                "FS-GG/FS.GG.SDD"
                74
                None
                "Paths: docs/architecture.md"

        let code, out = runOverlap world [ "overlap"; "FS-GG/.github#2345"; "FS-GG/FS.GG.SDD#74" ]

        Assert.Equal(Kernel.ExitGreen, code)
        Assert.Contains("DISJOINT", out)

    // ---- AC3: the marker-backed scan `activeCollisions` — and therefore `overlap --active`, `widen`, ----
    // ---- `set-paths`, and `claim` all share ---------------------------------------------------------

    /// The `overlap <ref> --active` fixture. `ref` (`selfNumber`) holds no claim marker of its own — its
    /// `targetPathRepo` therefore falls back to its own repo, exactly the `rook-9dc2`/`.github#2345` side
    /// of the live incident, which carried no scope override at all. `other` (`otherNumber`) is a LIVE
    /// claim whose marker carries `pathRepo=<otherScope>` — the `smew-e1d9`/`.github#2070` side, whose
    /// board `Repo Scope` was `cross-repo` and therefore embedded that literal string in its own marker at
    /// claim time (`Client.readPathRepo`).
    let private activeWorld (otherScope: string option) =
        let owner, repo = "FS-GG", ".github"
        let selfNumber = 2345
        let otherNumber = 2070
        let sharedBody = "Paths: docs/architecture.md"
        let ts = DateTime.UtcNow.ToString "yyyy-MM-ddTHH:mm:ssZ"

        let pathRepoAttr =
            otherScope
            |> Option.map (fun s -> $" pathRepo=%s{s}")
            |> Option.defaultValue ""

        Fake.Recorder(fun (req: Request) ->
            match req.Method, req.Path.Trim '/' with
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, _) -> graphqlAnswer "" document
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
            | "GET", p when p = $"repos/%s{owner}/%s{repo}/issues" ->
                [ {| number = otherNumber
                     state = "open"
                     body = sharedBody |} ]
                |> JsonSerializer.Serialize
                |> ok
            | "GET", p when p = $"repos/%s{owner}/%s{repo}/issues/%d{selfNumber}" ->
                ok (JsonSerializer.Serialize {| number = selfNumber; body = sharedBody |})
            | "GET", p when p = $"repos/%s{owner}/%s{repo}/issues/%d{selfNumber}/comments" ->
                // NO CLAIM OF ITS OWN — `targetPathRepo` reads `None` and falls back to `ref.Repo`.
                ok "[]"
            | "GET", p when p = $"repos/%s{owner}/%s{repo}/issues/%d{otherNumber}/comments" ->
                ok
                    $"""[{{"id":8070,"body":"<!-- fsgg:claim worker=smew-e1d9 lease=120%s{pathRepoAttr} -->\nheld","user":{{"login":"EHotwagner"}},"created_at":"%s{ts}","updated_at":"%s{ts}"}}]"""
            | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

    [<Fact>]
    let ``#2351 AC3: activeCollisions (overlap --active / widen / claim's shared scan) reports OVERLAP against a cross-repo-scoped holder`` () =
        let world = activeWorld (Some "cross-repo")

        let code, out = runOverlap world [ "overlap"; "FS-GG/.github#2345"; "--active" ]

        Assert.Equal(Kernel.ExitContended, code)
        Assert.Contains("OVERLAP", out)
        Assert.Contains("smew-e1d9", out)

    [<Fact>]
    let ``#2351 AC3 control: activeCollisions still reports OVERLAP when the holder's scope is unset (unaffected by the fix)`` () =
        // Not the sentinel at all — pins that `pathRepoOrFallback` changed nothing about the ordinary,
        // never-buggy case: a holder with no `pathRepo` on its marker defaults to its own repo exactly as
        // it always has, and still collides.
        let world = activeWorld None

        let code, out = runOverlap world [ "overlap"; "FS-GG/.github#2345"; "--active" ]

        Assert.Equal(Kernel.ExitContended, code)
        Assert.Contains("OVERLAP", out)
