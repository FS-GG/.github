module FS.GG.Coord.Cli.Tests.OpLockDispatchVerbTests

open System
open System.IO
open System.Text.RegularExpressions
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.Cli

// THE PRODUCTION CALLER OF THE PER-RECEIVER OPERATION LOCK — the half of `.github#2312` that did not land.
//
// `d1632c4e` landed `Options.opLockRef` (all eight roster repositories, `FS.GG.Net` included) and
// `Client.OpLock.acquire` (the item CAS, unchanged, on a third subject) correct and complete, and landed
// them reachable from NOTHING BUT THEIR OWN UNIT TESTS. The board analyst measured it on 2026-08-17:
// `grep -rn "OpLock\.acquire" src/ --include=*.fs` returned 0 while the same expression over `tests/`
// returned 4, and the row was reopened for exactly that. `OpLock.release` was worse than uncalled — it was
// defined in `Client.fs` and omitted from `Client.fsi`, so F# made it private and no caller, production or
// test, could have reached it even by trying.
//
// The consequence was not stylistic. `.github/workflows/fsgg-dispatch-broker.yml` takes a `grant` input —
// "the comment id of the dispatch grant on the receiver's operation-lock issue" — and refuses at step 5
// unless that grant is the live CAS winner on that issue. With no writer, no `fsgg:claim` marker could
// ever appear on an op-lock issue, so that refusal was unreachable BY CONSTRUCTION rather than by policy
// and no caller could ever supply a non-empty `grant`. A lock nobody can take is not a fence.
//
// These legs are about the WIRING, and each is named for the mutation it reds on. The lock's own
// mechanics — the eight-row table, the CAS reuse, the off-board property, the one exported ordering rule —
// are `OpLockTests`' and are not re-litigated here.

let private me = WorkerId "finch-6929"
let private them = WorkerId "kite-461"
let private now = DateTimeOffset.UtcNow.ToString("o")

/// `FS.GG.Net#72` — the receiver every leg below dispatches at, chosen because `FS.GG.Net` is the row the
/// chore-lock table omits and `FS.GG.Net#58` is one of the two pull requests `.github#1858` measured as
/// merged by the unlocked executor. If the wiring works anywhere, it has to work here.
let private receiver = "FS-GG/FS.GG.Net"
let private lockIssue = 72

/// A well-formed request, in `Operation.compose`'s own argument order.
let private item = "FS-GG/FS.GG.Net#58"
let private generation = "5319401108"
let private dispatchOp = "dispatch:coordination-kit"

let private ok (body: string) =
    Ok
        { Status = 200
          Body = body
          ETag = None
          NextLink = None
          Headers = Map.empty }

let private scripted (responses: IoResult<Response> list) =
    let queue = System.Collections.Generic.Queue<IoResult<Response>>(responses)

    Fake.Recorder(fun _ ->
        if queue.Count = 0 then
            failwith "the transport was called more times than the test scripted"
        else
            queue.Dequeue())

/// A transport that must never be reached. Any call at all is a `failwith` rather than a bad assertion:
/// "it refused" and "it refused WITHOUT paying" are different facts, and only the second is what an
/// order-of-operations claim asserts.
let private unreachable () = scripted []

let private marker (id: int) (worker: string) =
    let body = $"<!-- fsgg:claim worker=%s{worker} lease=10 -->"
    $"""{{"id":%d{id},"body":"%s{body}","updated_at":"%s{now}"}}"""

/// The same marker, with its lease long lapsed. `lease=10` and an `updated_at` an hour old, so `isStale`
/// is decided by the FIELDS the engine reads rather than by anything this fixture asserts.
let private staleMarker (id: int) (worker: string) =
    let old = DateTimeOffset.UtcNow.AddHours(-1.0).ToString("o")
    let body = $"<!-- fsgg:claim worker=%s{worker} lease=10 -->"
    $"""{{"id":%d{id},"body":"%s{body}","updated_at":"%s{old}"}}"""

let private comments (ms: string list) = "[" + String.concat "," ms + "]"

let private contextOn (transport: IGitHubTransport) : Kernel.Context =
    { Transport = transport
      Owner = "FS-GG"
      Title = "Coordination"
      DefaultRepo = Some ".github"
      ChoreLocks = [] }

/// Walk up to the repository root, anchored on a file only the root has — `OpLockTests`' idiom, so both
/// files find the tree the same way whatever directory the runner starts in.
let rec private repoRoot (dir: string) =
    if File.Exists(Path.Combine(dir, "src/FS.GG.Coord.Cli/Client.fs")) then
        dir
    else
        repoRoot (Directory.GetParent(dir).FullName)

let private root = repoRoot (Directory.GetCurrentDirectory())

/// Drive one `op-lock` invocation through the SAME argv the parser sees in production, capturing stdout
/// and stderr separately.
///
/// THROUGH `Options.parse`, NEVER BY CONSTRUCTING AN `Options` RECORD. The defect this file exists for was
/// a reachability defect, so a fixture that handed the handler a record it built itself would skip the
/// exact link that was missing. Everything from the argv token `op-lock` to the marker on GitHub is under
/// test here, including the parser arm and the command-to-handler dispatch.
///
/// `AssemblyInfo.fs` disables cross-class parallelism, so pointing the process-global cache root somewhere
/// private per call is safe — the same licence `DoneStderrTests.runDone` and `ForceStealTests.runClaim`
/// take.
let private runOpLock (transport: IGitHubTransport) (argv: string list) : int * string * string =
    let dir = Path.Combine(Path.GetTempPath(), "fsgg-2312-" + Guid.NewGuid().ToString "n")
    let previousCache = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
    let previousWorker = Environment.GetEnvironmentVariable "FSGG_WORKER"
    let stdout = Console.Out
    let stderr = Console.Error
    use capturedOut = new StringWriter()
    use capturedErr = new StringWriter()

    try
        Directory.CreateDirectory dir |> ignore
        Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)

        // THE IDENTITY IS EXPORTED, NOT ASSERTED ON ARGV, and discovering why cost this fixture a round.
        // `--worker <id>` NAMES an id; `selfOf` DERIVES one from the environment; and `#1646` makes the
        // second the third factor the first cannot restate — a live marker for a `worker` the environment
        // does not derive is refused as `ImpersonatesHolder` whatever the sessions say. A fixture that
        // passed `--worker finch-6929` into a runner whose own environment derived something else was
        // therefore testing the impersonation guard, not the lock. Exporting the id is what a real
        // executor does (`eval "$(fsgg-coord whoami --mint)"`), so it is also the honest fixture.
        Environment.SetEnvironmentVariable("FSGG_WORKER", me.Value)
        Console.SetOut capturedOut
        Console.SetError capturedErr

        let opts =
            match Options.parse argv with
            | Ok o -> o
            | Error e -> failwithf "the fixture's own argv did not parse: %s" e

        let code =
            match opts.Command with
            | Options.OpLockAcquire -> Client.opLockAcquire (contextOn transport) opts
            | Options.OpLockRelease -> Client.opLockRelease (contextOn transport) opts
            | other -> failwithf "argv %A parsed to %A, which is not an op-lock command" argv other

        Console.Out.Flush()
        Console.Error.Flush()
        code, capturedOut.ToString(), capturedErr.ToString()
    finally
        Console.SetOut stdout
        Console.SetError stderr
        Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)
        Environment.SetEnvironmentVariable("FSGG_WORKER", previousWorker)

        try
            Directory.Delete(dir, true)
        with _ ->
            ()

let private acquireArgv =
    [ "op-lock"; "acquire"; item; generation; receiver; dispatchOp ]

// ---- The reachability defect itself, and a control that shows the instrument can say the opposite -----

/// Source with `//` and `///` comment lines removed, so a doc comment that NAMES a symbol is not counted
/// as a call to it. `OpLockTests`' own `code` helper, for its own stated reason.
let private code (relativePath: string) =
    File.ReadAllLines(Path.Combine(root, relativePath))
    |> Array.filter (fun line -> not ((line.TrimStart()).StartsWith "//"))
    |> String.concat "\n"

/// Every non-signature F# file under `src/`, with comments stripped. Signature files are excluded because
/// a `val` declaration is not a call site — a symbol can be declared in fifty `.fsi` files and reachable
/// from none.
let private productionSources =
    Directory.GetFiles(Path.Combine(root, "src"), "*.fs", SearchOption.AllDirectories)
    |> Array.filter (fun p -> not (p.EndsWith ".fsi"))
    |> Array.map (fun p -> p, code (Path.GetRelativePath(root, p)))

/// How many production files contain a CALL to this qualified name — an occurrence not followed by more
/// name characters, so `acquire` does not match `acquireSomethingElse`.
let private productionCallSites (qualifiedName: string) =
    let pattern = Regex.Escape qualifiedName + @"(?![\w.])"

    productionSources
    |> Array.filter (fun (_, text) -> Regex.IsMatch(text, pattern))
    |> Array.map (fun (path, _) -> Path.GetRelativePath(root, path))
    |> Array.toList

[<Fact>]
let ``CONTROL - the reachability scan can report ZERO, so a non-zero answer is evidence`` () =
    // WITHOUT THIS LEG, EVERY OTHER REACHABILITY CLAIM IN THIS FILE IS UNFALSIFIABLE. A scan that matched
    // everything — a regex that silently degenerated, a source list that accidentally included the tests,
    // a `Path.GetRelativePath` that returned something no filter could reject — would report every symbol
    // as reachable and turn the leg below into a green that asserts nothing. That is the exact shape
    // `.github#266` names: "the subject is missing or empty, and absence reads as pass", which is also the
    // shape the row under repair shipped.
    //
    // Three readings, one instrument, and they must DISAGREE with each other:

    //   1. A symbol that exists nowhere at all. If this is non-empty the scan is not reading source.
    Assert.Empty(productionCallSites "Client.OpLock.thisSymbolDoesNotExist")

    //   2. A symbol that exists and is genuinely consumed — the analyst's own control, made executable.
    //      `Chores.offer` is the chore lock's production caller, and if the scan cannot see it the scan is
    //      broken rather than the code.
    Assert.NotEmpty(productionCallSites "Chores.offerWithLifecycle")

    //   3. And the source set is not empty or accidentally tiny, which would make (1) vacuous.
    Assert.True(
        Array.length productionSources > 20,
        $"scanned only %d{Array.length productionSources} production sources — the scan, not the code, is what broke"
    )

[<Fact>]
let ``Client.OpLock.acquire has a PRODUCTION call site - it landed with none, and that is why the row reopened`` () =
    // THE HEADLINE ASSERTION OF THIS ROW'S REPAIR, and it is deliberately a source fact rather than a
    // behavioural one, because the defect was a source fact: every behavioural test of `acquire` passed
    // green throughout the whole period in which no production code could reach it.
    //
    // The CONTROL above is what makes this leg mean something: the same instrument returns [] for an
    // absent symbol and non-[] for a consumed one, so a non-empty answer here is a measurement rather than
    // a hardwired pass.
    let sites = productionCallSites "OpLock.acquire"

    Assert.True(
        not (List.isEmpty sites),
        "Client.OpLock.acquire is reachable from no production file — the lock cannot be taken, so fsgg-dispatch-broker.yml's `grant` input can never be non-empty and its step-5 refusal is unreachable by construction (.github#2312)"
    )

    // The same for the release half, which was not merely uncalled: `d1632c4e` left it out of
    // `Client.fsi`, so F# made it private and NO caller could have reached it.
    Assert.True(
        not (List.isEmpty (productionCallSites "OpLock.release")),
        "Client.OpLock.release is reachable from no production file — a grant that cannot be dropped is a receiver serialised for its whole lease"
    )

[<Fact>]
let ``the CLI dispatch actually routes op-lock to its handlers - the link that was missing`` () =
    // A SOURCE GATE, AND IT IS DECLARED AS ONE rather than dressed up as behaviour. `Client.run` builds
    // its own transport from `context ()` — a real HTTP client against a real token — so no unit fixture
    // can drive the dispatch arm itself; every leg above therefore calls `Client.opLockAcquire` /
    // `Client.opLockRelease` directly, which is precisely the link a reachability defect hides behind.
    // `.github#2312` shipped a correct `OpLock.acquire` that nothing dispatched to, so leaving the one
    // remaining unexercised hop unasserted would repeat the row's own mistake one level up.
    //
    // The honest thing is to gate it on source and say so, which is what `OpLockTests` does for its own
    // ordering scan for the same reason. `Program.fs` needs no leg of its own: its command list feeds one
    // `-> Client.run opts` arm, and a `Command` case missing from it is an incomplete-match warning the
    // build turns into an error — the compiler is that gate, and a stronger one than this file could be.
    let client = code "src/FS.GG.Coord.Cli/Client.fs"

    Assert.Matches(@"\|\s*OpLockAcquire\s*->\s*opLockAcquire\s+ctx\s+opts", client)
    Assert.Matches(@"\|\s*OpLockRelease\s*->\s*opLockRelease\s+ctx\s+opts", client)

// ---- The verb exists on argv, and it is the parser that says so -------------------------------------

[<Fact>]
let ``op-lock acquire and release PARSE from argv, and an unknown subcommand is named rather than swallowed`` () =
    match Options.parse [ "op-lock"; "acquire"; item; generation; receiver; dispatchOp ] with
    | Ok o ->
        Assert.Equal(Options.OpLockAcquire, o.Command)
        Assert.Equal<string list>([ item; generation; receiver; dispatchOp ], o.Args)
    | Error e -> failwith $"`op-lock acquire` must parse — got %s{e}"

    match Options.parse [ "op-lock"; "release"; receiver ] with
    | Ok o ->
        Assert.Equal(Options.OpLockRelease, o.Command)
        Assert.Equal<string list>([ receiver ], o.Args)
    | Error e -> failwith $"`op-lock release` must parse — got %s{e}"

    // A NAMESPACE, NOT A PREFIX. Were the third word swallowed into `acquire`'s positional list, a typo'd
    // `op-lock aquire FS-GG/FS.GG.Net …` would be read as an acquire whose first component is "aquire" —
    // and `Operation.compose` would refuse it for the wrong reason, telling the caller their ITEM is
    // malformed. `room`'s arm has the same shape for the same reason.
    match Options.parse [ "op-lock"; "aquire"; receiver ] with
    | Error e -> Assert.Contains("aquire", e)
    | Ok o -> failwith $"an unknown op-lock subcommand must be REFUSED by name — it parsed to %A{o.Command}"

    match Options.parse [ "op-lock" ] with
    | Error e -> Assert.Contains("subcommand", e)
    | Ok o -> failwith $"a bare `op-lock` must be refused — it parsed to %A{o.Command}"

[<Fact>]
let ``both op-lock commands are BOARD WRITES in the engine and in the shim's partition`` () =
    // TWO HALVES OF ONE FACT, and the second is not implied by the first. The engine's `writes` answer is
    // what `command-contract` advertises; `scripts/fsgg-coord-guards.sh` is what actually refuses the verb
    // when the engine is stale, and it is a hand-written set. `tests/coord-engine-parity/shim.sh` §3b holds
    // them in bijection at CI time — this leg is the unit-level half, so a `dotnet test` run alone catches
    // the omission rather than deferring every such mistake to the shell suite.
    // READ THROUGH `command-contract`, which is the projection §3b actually consumes, rather than through
    // the private `WriteSurface` union: a row that classified correctly internally and rendered wrongly
    // would still leave the shim comparing against the wrong answer.
    let contract = System.Text.Json.JsonDocument.Parse(Options.renderCommandContract ()).RootElement

    let writesOf (name: string) =
        contract.GetProperty("commands").EnumerateArray()
        |> Seq.tryFind (fun c -> c.GetProperty("name").GetString() = name)
        |> Option.map (fun c -> c.GetProperty("writes").GetString())

    Assert.Equal(Some "always", writesOf "op-lock acquire")
    Assert.Equal(Some "always", writesOf "op-lock release")

    // ONE WORD IN THE SHIM, because the shim dispatches on `$1` and §3b projects the engine's surface
    // through `awk '{print $1}'`. Both commands must therefore share a first token, and that token must be
    // the one classified.
    Assert.Equal("op-lock acquire", Options.commandName Options.OpLockAcquire)
    Assert.Equal("op-lock release", Options.commandName Options.OpLockRelease)

    let guards = File.ReadAllText(Path.Combine(root, "scripts", "fsgg-coord-guards.sh"))
    let writesLine = Regex.Match(guards, @"(?m)^BOARD_WRITES=""([^""]*)""$")
    Assert.True(writesLine.Success, "scripts/fsgg-coord-guards.sh no longer declares a literal BOARD_WRITES set")

    let classified = writesLine.Groups.[1].Value.Split(' ') |> Array.map (fun s -> s.Trim())
    Assert.Contains("op-lock", classified)

// ---- Acquire: the production path, end to end -------------------------------------------------------

/// The three calls the CAS makes to win a free lock: read, post, re-read.
let private winsTheLock () =
    scripted
        [ ok "[]"
          ok """{"id":901}"""
          ok (comments [ marker 901 "finch-6929" ]) ]

[<Fact>]
let ``op-lock acquire takes the grant and prints the broker's whole input tuple`` () =
    let transport = winsTheLock ()
    let code, out, err = runOpLock transport (acquireArgv @ [ "--json" ])

    Assert.True(0 = code, $"acquire did not succeed (exit %d{code}); stderr was: %s{err}")

    let doc = System.Text.Json.JsonDocument.Parse(out.Trim()).RootElement

    // THE GRANT IS THE SERVER-ASSIGNED COMMENT ID AND NOTHING ELSE. Nobody can mint one locally, nobody
    // can choose its value, and nobody can forge its ordering (design §3.2) — which is why the broker's
    // step 5 is the one check a requester cannot satisfy by typing. A grant equal to anything the caller
    // supplied would be a grant the caller could have supplied.
    Assert.Equal("901", doc.GetProperty("grant").GetString())

    // AND IT IS ADDRESSED AT THE RECEIVER'S OWN LOCK ISSUE. `scripted` answers a queue and ignores the
    // request, so every assertion above would hold identically had the verb written to some other issue.
    Assert.True(
        transport.Logged $"comment-list FS-GG/FS.GG.Net %d{lockIssue}",
        "the verb did not act on FS.GG.Net's operation-lock issue"
    )

    // THE OPKEY IS `Operation.compose`'s, WHICH IS WHY THE BROKER'S RECOMPUTATION AGREES. The broker
    // recomputes the key from the same four components and refuses a mismatch; deriving it here any second
    // way is how the two answers come to disagree. Recomputed independently in this assertion, from the
    // engine's own function, so a handler that printed a different string reds.
    let expected =
        match Operation.compose item generation receiver (Operation.Dispatch "coordination-kit") with
        | Ok(Operation.OpKey k) -> k
        | Result.Error refusals -> failwithf "the fixture's own request did not compose: %A" refusals

    Assert.Equal(expected, doc.GetProperty("opkey").GetString())

    // The rest of the tuple, echoed so a caller can hand the whole object to `gh workflow run` without
    // re-typing components it would then have to keep in step by hand.
    Assert.Equal(item, doc.GetProperty("item").GetString())
    Assert.Equal(generation, doc.GetProperty("generation").GetString())
    Assert.Equal(receiver, doc.GetProperty("receiver").GetString())
    Assert.Equal(dispatchOp, doc.GetProperty("op").GetString())
    Assert.Equal(10, doc.GetProperty("leaseMinutes").GetInt32())

[<Fact>]
let ``the text projection prints grant and opkey on stdout, and the release reminder on stderr`` () =
    // STREAM DISCIPLINE, for `.github#1562`'s reason: a caller doing `grant="$(… | grep ^grant= …)"` must
    // not have prose land in the value it captures. The reminder is advice, so it is stderr.
    let transport = winsTheLock ()
    let code, out, err = runOpLock transport acquireArgv

    Assert.True(0 = code, $"acquire did not succeed (exit %d{code}); stderr was: %s{err}")
    Assert.Contains("grant=901", out)
    Assert.Contains("opkey=", out)
    Assert.DoesNotContain("op-lock release", out)
    Assert.Contains("op-lock release", err)

[<Fact>]
let ``a live holder REFUSES the dispatch with exit 6 - a contended receiver is the fence working`` () =
    // EXIT 6, NOT 1, AND THE DIFFERENCE IS THE REMEDY. `ExitContended` documents "back off briefly and
    // retry — the board is busy, not empty"; `ExitError` documents an input the caller must change. A
    // caller handed 1 for a busy receiver would treat a working fence as a misconfiguration and stop
    // retrying; a caller handed 6 for an unrostered receiver would retry for ever.
    let transport = scripted [ ok (comments [ marker 700 "kite-461" ]) ]
    let code, out, err = runOpLock transport acquireArgv

    Assert.True(6 = code, $"a live holder must be exit 6 (got %d{code}); stderr was: %s{err}")
    Assert.Equal("", out.Trim())
    Assert.Contains("kite-461", err)

[<Fact>]
let ``an unrostered receiver REFUSES with exit 1 and spends no network call`` () =
    let transport = unreachable ()

    let code, _, err =
        runOpLock
            transport
            [ "op-lock"; "acquire"; item; generation; "FS-GG/FS.GG.NotARepo"; dispatchOp ]

    Assert.Equal(1, code)
    Assert.Contains("refusing to dispatch unfenced", err)
    Assert.Equal(0, transport.RestCalls)
    Assert.Equal(0, transport.GraphQlCalls)

[<Fact>]
let ``the key is composed BEFORE the lock is taken - a malformed request never strands a receiver`` () =
    // ORDER OF OPERATIONS, ASSERTED STRUCTURALLY. If the grant were taken first, a request that
    // `Operation.compose` then refused would leave a live marker on the receiver for a dispatch that was
    // never going to be authorized — stalling that receiver for the whole ten-minute lease on a typo, and
    // stalling it in the one repository the `.github#1858` incident reached.
    //
    // `unreachable` is what makes this structural rather than hopeful: the assertion is not "it refused"
    // but "it refused WITHOUT PAYING", and only the second distinguishes the two orderings.
    let transport = unreachable ()

    let code, _, err =
        runOpLock
            transport
            // The board's `<repo>#N` shorthand, which is not GitHub grammar (.github#2107) — `compose`
            // refuses it, and it must do so before any write.
            [ "op-lock"; "acquire"; ".github#2312"; generation; receiver; dispatchOp ]

    Assert.Equal(1, code)
    Assert.Contains("owner/repo#N", err)
    Assert.Equal(0, transport.RestCalls)

[<Fact>]
let ``the dispatch prefix is DERIVED from Operation.wire, so a non-dispatch operation is refused unpaid`` () =
    // A LEASE-BASED LOCK IS VERIFIABLE ONLY BY A READER RUNNING INSIDE THE LEASE, and every operation but
    // dispatch is verified by a queued CI job (design §4.2) — so `merge` is fenced by the lease-free
    // election, not by this. Brokering it here would hand a merge a grant that has expired by the time
    // anything checks it.
    //
    // The prefix this arm tests against is `Operation.wire (Operation.Dispatch "")`, computed by the one
    // function that defines the wire vocabulary rather than typed into `Client.fs` — the second copy §12.5
    // forbids, which slice 3 declined to write for the ordering rule and which this row declines to write
    // for the operation vocabulary. This leg pins the derivation by requiring the refusal to QUOTE it.
    let transport = unreachable ()

    let code, _, err =
        runOpLock
            transport
            [ "op-lock"; "acquire"; item; generation; receiver; "merge" ]

    Assert.Equal(1, code)
    Assert.Contains(Operation.wire (Operation.Dispatch ""), err)
    Assert.Equal(0, transport.RestCalls)

// ---- Release: only our own grant, and only through verifyHeld ---------------------------------------

[<Fact>]
let ``op-lock release drops OUR grant, through verifyHeld rather than lowest id`` () =
    // TWO MARKERS, AND OURS IS NOT THE LOWEST — the one arrangement that tells the two rules apart.
    //
    // `kite-461`'s marker is id 700 with a LAPSED lease; ours is 901 and live. `Reads.lowestId` — the very
    // function this slice exported, and the one three CLI arms correctly use — answers 700, because it
    // applies no liveness judgement at all and is documented as answering only "which marker is first".
    // `Writes.verifyHeld` answers 901, because the CAS winner is the lowest LIVE marker and that is us.
    //
    // A release wired to the first would DELETE ANOTHER WORKER'S MARKER while leaving our own grant
    // standing on the receiver — collecting a lock we do not hold, and stalling the receiver for our whole
    // lease into the bargain. Collecting a stale foreign marker is `claim`'s own job on the next acquire
    // (ADR-0041), never a release's. This is `Reads.fsi`'s stated warning — "IT IS NOT `reserver`, AND
    // SUBSTITUTING ONE FOR THE OTHER IS A DEFECT" — reaching the one verb where getting it wrong deletes
    // something.
    let transport =
        scripted
            [ ok (comments [ staleMarker 700 "kite-461"; marker 901 "finch-6929" ])
              ok "" ]

    let code, out, err = runOpLock transport [ "op-lock"; "release"; receiver; "--json" ]

    Assert.True(0 = code, $"release did not succeed (exit %d{code}); stderr was: %s{err}")

    let doc = System.Text.Json.JsonDocument.Parse(out.Trim()).RootElement
    Assert.Equal("901", doc.GetProperty("grant").GetString())
    Assert.True(doc.GetProperty("released").GetBoolean())

    // THE DELETE NAMED OUR MARKER, not the lowest one. Without this the assertions above would hold for a
    // handler that reported 901 and deleted 700.
    Assert.True(
        transport.Logged $"comment-delete FS-GG/FS.GG.Net 901",
        "release did not delete OUR marker — a release that finds the marker by anything but the capability is #550"
    )

    // AND IT LEFT THE LOWEST-ID MARKER ALONE. Without this the leg above would pass for a handler that
    // deleted both, or that reported 901 and deleted 700 as well.
    Assert.False(
        transport.Logged $"comment-delete FS-GG/FS.GG.Net 700",
        "release deleted the lowest-id marker, which belongs to another worker — that is `Reads.lowestId`'s answer, not `verifyHeld`'s"
    )

    // THE CONTROL FOR THIS LEG, and it is not decoration: it establishes that the fixture really does put
    // the two rules in disagreement. If `lowestId` and the CAS winner agreed on this input, every
    // assertion above would hold for a handler wired to either one.
    let scanned: Reads.Marker list =
        [ { Id = 700L
            Worker = them
            Session = None
            AgeSeconds = 3600
            PreviousStatus = None
            PathRepo = None
            Raw = "" }
          { Id = 901L
            Worker = me
            Session = None
            AgeSeconds = 1
            PreviousStatus = None
            PathRepo = None
            Raw = "" } ]

    Assert.Equal(Some 700L, Reads.lowestId scanned |> Option.map (fun m -> m.Id))
    Assert.Equal(Some 901L, Reads.winner Client.OpLock.LeaseMinutes scanned |> Option.map (fun m -> m.Id))

[<Fact>]
let ``op-lock release REFUSES when this worker holds no grant - nothing to drop is not permission to delete`` () =
    let transport = scripted [ ok (comments [ marker 700 "kite-461" ]) ]

    let code, out, err = runOpLock transport [ "op-lock"; "release"; receiver ]

    Assert.Equal(1, code)
    Assert.Equal("", out.Trim())
    Assert.Contains("no grant of ours to drop", err)

[<Fact>]
let ``op-lock release on an unrostered receiver REFUSES and spends no network call`` () =
    let transport = unreachable ()

    let code, _, err =
        runOpLock transport [ "op-lock"; "release"; "FS-GG/FS.GG.NotARepo" ]

    Assert.Equal(1, code)
    Assert.Contains("refusing to dispatch unfenced", err)
    Assert.Equal(0, transport.RestCalls)

// ---- The injected roster reaches production ---------------------------------------------------------

[<Fact>]
let ``FSGG_COORD_OP_LOCKS reaches opLockRef's extra parameter, and is NOT the chore lock's variable`` () =
    // `opLockRef`'s `extra` parameter is documented as "the per-deployment roster a vendored tenant may
    // inject". Until this row it had no production reader at all: the only production caller would have
    // passed `[]`, which is the same reader-without-writer shape the row is reopened for, one level down.
    let previousOp = Environment.GetEnvironmentVariable "FSGG_COORD_OP_LOCKS"
    let previousChore = Environment.GetEnvironmentVariable "FSGG_COORD_CHORE_LOCKS"

    try
        Environment.SetEnvironmentVariable("FSGG_COORD_OP_LOCKS", "acme/Product.X#42")
        Environment.SetEnvironmentVariable("FSGG_COORD_CHORE_LOCKS", "acme/Product.Y#77")

        let injected = Client.OpLock.roster ()

        Assert.Equal<Ref list>(
            [ { Owner = "acme"
                Repo = "Product.X"
                Number = 42 } ],
            injected
        )

        // TWO SUBJECTS, TWO VARIABLES. Sharing one would make a chore drain and a dispatch operation
        // serialise against each other — "two questions answered in one colour" (design §4.1) — and a
        // tenant repointing one lock would silently repoint the other.
        Assert.DoesNotContain({ Owner = "acme"; Repo = "Product.Y"; Number = 77 }, injected)

        // And it resolves through the lookup the acquire path uses, under a foreign owner, which is the
        // whole point of an injected roster.
        Assert.Equal(Some(List.head injected), Options.opLockRef injected "acme" "Product.X")
    finally
        Environment.SetEnvironmentVariable("FSGG_COORD_OP_LOCKS", previousOp)
        Environment.SetEnvironmentVariable("FSGG_COORD_CHORE_LOCKS", previousChore)

[<Fact>]
let ``the NotHeld refusal describes itself and is distinct from every other arm`` () =
    // The release path's arm. Distinct from `HeldByAnother`: "my grant already lapsed" and "another
    // executor took this receiver while I was dispatching" need opposite responses, and a caller that
    // cannot tell them apart will retry the wrong one.
    let lines =
        [ Client.OpLock.NoLockRef("FS-GG", "FS.GG.NotARepo")
          Client.OpLock.HeldByAnother them
          Client.OpLock.Twin(SessionId "other-session")
          Client.OpLock.Impersonates(me, them)
          Client.OpLock.NotHeld("FS-GG", "FS.GG.Net")
          Client.OpLock.Undetermined "the re-read failed" ]
        |> List.map Client.OpLock.describe

    for line in lines do
        Assert.False(String.IsNullOrWhiteSpace line, "a refusal must say something")

    Assert.Equal(List.length lines, lines |> List.distinct |> List.length)
