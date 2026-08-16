module FS.GG.Coord.Cli.Tests.OpLockTests

open System.IO
open System.Text.RegularExpressions
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.Cli

// The per-receiver OPERATION lock and the one exported lease-free ordering rule — slice 2 of the
// GitHub-native executor fencing design (`.github#2312` under `.github#1858`).
//
// These legs pin the two things the design says must not be lost, and both are stated as MECHANICAL
// assertions rather than as reviewed lists, because a hand-checked list is exactly what rotted the first
// time: `Options.choreLockNumbers` lists seven repositories and omits `FS.GG.Net`, and `FS.GG.Net#58` is
// one of the two pull requests the incident measured as merged by the unlocked executor.

let private me = WorkerId "snipe-383c"
let private them = WorkerId "kite-461"
let private itsMe = Writes.Derives me
let private now = System.DateTimeOffset.UtcNow.ToString("o")

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
/// "it refused" and "it refused WITHOUT paying" are different facts, and only the second is what a
/// fail-closed arm claims.
let private unreachable = scripted []

let private marker (id: int) (worker: string) =
    let body = $"<!-- fsgg:claim worker=%s{worker} lease=10 -->"
    $"""{{"id":%d{id},"body":"%s{body}","updated_at":"%s{now}"}}"""

let private comments (ms: string list) = "[" + String.concat "," ms + "]"

/// Walk up to the repository root. The same idiom `BlockerLintTests` uses — anchored on a file that only
/// the root has, so it works from whatever directory the runner happens to start in.
let rec private repoRoot (dir: string) =
    if File.Exists(Path.Combine(dir, "src/FS.GG.Coord.Cli/Client.fs")) then
        dir
    else
        repoRoot (Directory.GetParent(dir).FullName)

let private root = repoRoot (Directory.GetCurrentDirectory())

// ---- Criterion 1: the table covers ALL EIGHT roster repositories, proved mechanically ---------------

/// Every `FS-GG`-owned row of `registry/repos.yml`'s `repos:` block, read from the roster itself.
///
/// DERIVED, NEVER RESTATED, AND THAT IS THE ENTIRE POINT OF THIS FIXTURE. The acceptance criterion asks
/// for "a test that fails when a roster repo has no lock ref, not a hand-checked list that will rot the
/// same way the seven-row table did". A fixture that spelled the eight names here would BE that
/// hand-checked list, one file further away — it would agree with the table forever, including on the day
/// the org onboards a ninth repository and the new receiver silently has no lock.
///
/// `registry/repos.yml` is the roster's own authority ("the single authoritative list of the framework
/// repos the org-level fabrics iterate"), so a repository that exists for the fleet is a row here, and a
/// row here with no operation lock is precisely the defect this asserts against.
///
/// Rows under another owner are excluded because the embedded table is owner-gated by design: its numbers
/// are FS-GG's issues, and handing a foreign owner one would be a lock naming an unrelated issue. The one
/// such row today is `EHotwagner/S.I.R.`, a `non-participant`.
let private rosterRepos: string list =
    let text = File.ReadAllLines(Path.Combine(root, "registry", "repos.yml"))

    let block =
        text
        |> Array.skipWhile (fun line -> line.TrimEnd() <> "repos:")
        |> Array.skip 1
        // The block ends at the next top-level key — a line starting in column 0 that is not a comment.
        |> Array.takeWhile (fun line ->
            let trimmed = line.TrimStart()
            line.StartsWith " " || line.StartsWith "\t" || trimmed = "" || trimmed.StartsWith "#")

    block
    |> Array.choose (fun line ->
        let m = Regex.Match(line, @"^\s*-\s*\{[^}]*?full:\s*([\w.-]+)/([\w.-]+)")

        if m.Success && m.Groups.[1].Value.ToLowerInvariant() = "fs-gg" then
            Some m.Groups.[2].Value
        else
            None)
    |> Array.toList

[<Fact>]
let ``the roster parse is NON-VACUOUS - a fixture that reads nothing proves nothing`` () =
    // THE GUARD THAT MAKES EVERY LEG BELOW MEAN SOMETHING. `List.forall` over an empty list is `true`, so a
    // regex that silently stopped matching — a reformatted roster, a renamed key, a `repos:` block that
    // moved — would turn the completeness assertion into a green that asserts nothing at all. That is the
    // failure mode this whole file exists to prevent, arriving through the test rather than the table.
    //
    // Eight is the roster as measured today; the assertion is `>=` because onboarding a ninth repository
    // must not red THIS leg — it must red the completeness leg below, which is the one that means "the new
    // receiver has no lock".
    Assert.True(
        List.length rosterRepos >= 8,
        $"parsed only %d{List.length rosterRepos} FS-GG roster rows from registry/repos.yml — the parse, not the roster, is what broke"
    )

    // And it found the specific row whose ABSENCE from the chore-lock table is this slice's subject.
    Assert.Contains("FS.GG.Net", rosterRepos)

[<Fact>]
let ``opLockRef covers EVERY roster repository - including FS.GG.Net, the eighth row`` () =
    // Criterion 1. The chore-lock table inherited a hole in exactly the repository the incident reached;
    // this asserts the operation-lock table did not inherit it, and keeps asserting it for every repository
    // the org adds later.
    let missing =
        rosterRepos |> List.filter (fun repo -> (Options.opLockRef [] "FS-GG" repo).IsNone)

    Assert.True(
        List.isEmpty missing,
        $"""roster repositories with NO operation-lock ref: %s{String.concat ", " missing}. A receiver with no lock cannot be fenced, and design §4.1 makes that a refusal rather than a licence to dispatch."""
    )

[<Fact>]
let ``each op-lock ref is CANONICAL and names its own repository`` () =
    // A ref built from the caller's spelling rather than the table's would be structurally unequal to the
    // canonical one while `Short` rendered both alike — two locks that compare different and print the same,
    // which is the split a CAS cannot survive and no log would show.
    for repo in rosterRepos do
        match Options.opLockRef [] "FS-GG" repo with
        | Some r ->
            Assert.Equal("FS-GG", r.Owner)
            Assert.Equal(repo, r.Repo)
            Assert.True(r.Number > 0, $"%s{repo}'s operation-lock ref must name a real issue number")
        | None -> failwith $"%s{repo} has no operation-lock ref"

[<Fact>]
let ``the operation lock and the chore lock are DIFFERENT subjects wherever both exist`` () =
    // Mutual exclusion is answered by the SUBJECT (design §4.1), so sharing an issue with the chore lock
    // would make a chore drain and a dispatch operation serialise against each other — two questions
    // answered in one colour. `FS.GG.Net` legitimately has no chore lock and is skipped rather than
    // special-cased into a pass.
    for repo in rosterRepos do
        match Options.opLockRef [] "FS-GG" repo, Options.choreLockRef [] "FS-GG" repo with
        | Some op, Some chore ->
            Assert.True(
                op <> chore,
                $"%s{repo}'s operation lock and chore lock name the SAME issue — one lock cannot serialise two independent questions"
            )
        | _ -> ()

[<Fact>]
let ``opLockRef resolves through the same map as --repo - short ids and casing agree`` () =
    // The lookup canonicalises its input, so a caller who typed a short id gets the same lock as one who
    // typed the canonical spelling. A lock that answered differently by spelling would be two locks.
    let canonical = Options.opLockRef [] "FS-GG" "FS.GG.Net"
    Assert.Equal(canonical, Options.opLockRef [] "FS-GG" "net")
    Assert.Equal(canonical, Options.opLockRef [] "FS-GG" "NET")
    Assert.Equal(canonical, Options.opLockRef [] "fs-gg" "FS-GG/FS.GG.Net")

// ---- Criterion 6: absent ref ⇒ REFUSE, and the refusal costs nothing --------------------------------

[<Fact>]
let ``an unrostered repo and a foreign owner both get NO ref - fail closed`` () =
    // "Absent ref ⇒ refuse, never proceed" (#266, #421). The owner gate is the part that is easy to get
    // wrong: keyed on the repo alone, a caller under another owner would be handed `<their-owner>/.github`
    // number 2714 — a real ref naming an unrelated issue, which is a lock that protects nothing while
    // reporting that it does.
    Assert.Equal(None, Options.opLockRef [] "FS-GG" "FS.GG.NotARepo")
    Assert.Equal(None, Options.opLockRef [] "acme" ".github")
    Assert.Equal(None, Options.opLockRef [] "acme" "FS.GG.Net")

[<Fact>]
let ``acquire REFUSES a receiver with no lock ref - and spends nothing doing it`` () =
    // The refusal is asked FIRST, before any network call, because a receiver with no lock is a guaranteed
    // refusal and the budget it would spend is REST — the meter the item CAS itself lives on. `unreachable`
    // makes that a structural claim rather than a hopeful one.
    match Client.OpLock.acquire unreachable me itsMe None [] "FS-GG" "FS.GG.NotARepo" with
    | Error(Client.OpLock.NoLockRef(owner, receiver)) ->
        Assert.Equal("FS-GG", owner)
        Assert.Equal("FS.GG.NotARepo", receiver)
    | other -> failwith $"a receiver with no lock must REFUSE, never proceed unfenced — got %A{other}"

    Assert.Equal(0, unreachable.RestCalls)
    Assert.Equal(0, unreachable.GraphQlCalls)

[<Fact>]
let ``a vendored deployment may inject its own lock, and it is consulted FIRST`` () =
    let injected =
        { Owner = "acme"
          Repo = "Product.X"
          Number = 42 }

    Assert.Equal(Some injected, Options.opLockRef [ injected ] "acme" "Product.X")
    // ...and it does not leak to a repo it does not name.
    Assert.Equal(None, Options.opLockRef [ injected ] "acme" "Product.Y")

// ---- Criterion 2 / 7: the CAS unchanged, on a subject that is off the board -------------------------

[<Fact>]
let ``acquire is the item CAS on the op-lock subject - and NEVER touches the board`` () =
    // Criterion 7, and it is verified rather than assumed. `#516`'s one-item-per-worker refusal scans BOARD
    // items for a live claim held by this worker on a different item; the lock issue is off-board by
    // construction, so taking a grant while holding an item cannot trip it.
    //
    // The board is Projects v2, which this engine reaches ONLY over GraphQL. So "off-board" is not a claim
    // about intent here — it is the assertion that acquiring the lock bills the GraphQL meter zero times.
    // A future edit that reached for a board read to resolve, validate or record the grant would red this.
    let transport =
        scripted
            [ ok "[]" // 1. read: the lock is free
              ok """{"id":901}""" // 2. post our marker
              ok (comments [ marker 901 "snipe-383c" ]) ] // 3. re-read: we hold it

    match Client.OpLock.acquire transport me itsMe None [] "FS-GG" "FS.GG.Net" with
    | Ok held ->
        Assert.Equal(901L, held.MarkerId)
        Assert.Equal(me, held.Worker)
        // #481: the column nobody recorded is not restored. An off-board issue never had one, and that
        // declined coupling IS the chore/operation-lock configuration (§4.1).
        Assert.Equal(None, held.PreviousStatus)
    | other -> failwith $"the operation lock must be winnable with the CAS as it stands — got %A{other}"

    // IT RAN ON THE OPERATION-LOCK SUBJECT. `scripted` answers a queue and ignores the request, so every
    // assertion above would hold identically had `acquire` addressed some other issue. The design's whole
    // argument for reusing the CAS verbatim is that *the subject disambiguates*, so a test that cannot see
    // the subject is not testing that argument.
    Assert.True(
        transport.Logged "comment-list FS-GG/FS.GG.Net 72",
        "the CAS did not read FS.GG.Net's operation-lock issue"
    )

    // OFF-BOARD, MECHANICALLY.
    Assert.Equal(0, transport.GraphQlCalls)

[<Fact>]
let ``a live holder is REFUSED, never stolen - a busy receiver is the lock working`` () =
    // `RefuseLiveHolder`, with no flag that changes it. The steal is a recovery route for an item whose
    // holder died with written work stranded on it; an operation lock holds no work, so a live holder means
    // another executor is already dispatching against this receiver. Forcing it would put two executors on
    // one receiver, which is the incident rather than the remedy.
    let transport = scripted [ ok (comments [ marker 700 "kite-461" ]) ]

    match Client.OpLock.acquire transport me itsMe None [] "FS-GG" "FS.GG.Net" with
    | Error(Client.OpLock.HeldByAnother holder) -> Assert.Equal(them, holder)
    | other -> failwith $"a live holder must refuse the grant — got %A{other}"

    Assert.Equal(0, transport.GraphQlCalls)

[<Fact>]
let ``every refusal describes itself without deciding anything`` () =
    // A caller that cannot dispatch must be able to say WHY. Collapsing these into one `None` is what makes
    // an unroutable receiver indistinguishable from a busy one, and those need opposite responses.
    let lines =
        [ Client.OpLock.NoLockRef("FS-GG", "FS.GG.NotARepo")
          Client.OpLock.HeldByAnother them
          Client.OpLock.Twin(SessionId "other-session")
          Client.OpLock.Impersonates(me, them)
          Client.OpLock.Undetermined "the re-read failed" ]
        |> List.map Client.OpLock.describe

    for line in lines do
        Assert.False(System.String.IsNullOrWhiteSpace line, "a refusal must say something")

    Assert.Equal(List.length lines, lines |> List.distinct |> List.length)

// ---- Criterion 3: ONE ordering rule, and no second implementation in the CLI layer ------------------

/// Source with `//` and `///` comment lines removed.
///
/// COUNT PARSES, NOT TEXT. `Reads.lowestId`'s own doc comment quotes the very expression this gate hunts
/// for — deliberately, because that comment is where the reason it must not be written twice is recorded.
/// A scanner that counted raw text would either red on the explanation or force the explanation to be
/// deleted, and both are worse than stripping comments first.
let private code (relativePath: string) =
    File.ReadAllLines(Path.Combine(root, relativePath))
    |> Array.filter (fun line -> not ((line.TrimStart()).StartsWith "//"))
    |> String.concat "\n"

/// "Lowest id wins, lease-free", in the spellings F# makes available for it.
let private orderingIdiom =
    Regex(
        @"List\.sortBy\s*\(\s*fun\s+\w+\s*->\s*\w+\.Id\s*\)\s*\|>\s*List\.tryHead"
        + @"|List\.minBy\s*\(\s*fun\s+\w+\s*->\s*\w+\.Id\s*\)"
        + @"|List\.sortBy\s*\(\s*fun\s+\w+\s*->\s*\w+\.Id\s*\)\s*\|>\s*List\.head"
    )

[<Fact>]
let ``the lease-free ordering rule is written EXACTLY ONCE, inside Reads`` () =
    // Criterion 3, and #485's defect stated as a gate. Before this slice the rule was written FOUR times and
    // three of them decided locks: `reserver`'s lapsed-claim fallback, `who`'s Held/Stale classification,
    // `reap`'s stale-lock candidate, and `adopt`'s expired-claim selection. `reserver`'s own doc comment
    // named `who` as making the same choice while `who` did not call it.
    let inReads = orderingIdiom.Matches(code "src/FS.GG.Coord.GitHub/Reads.fs").Count

    Assert.True(
        (inReads = 1),
        $"the ordering rule must be implemented exactly once in Reads.fs — found %d{inReads}. Two copies of 'lowest id wins' agree at first and drift later (#485)."
    )

[<Fact>]
let ``the CLI layer contains NO second implementation of lowest-id-wins`` () =
    // The CLI must reach the rule through `Reads.lowestId`, never re-express it. This scans the WHOLE Cli
    // project rather than only `Client.fs`, so a new copy cannot arrive in a neighbouring file.
    let offenders =
        Directory.GetFiles(Path.Combine(root, "src/FS.GG.Coord.Cli"), "*.fs")
        |> Array.map (fun full -> Path.GetFileName full)
        |> Array.filter (fun name ->
            orderingIdiom.IsMatch(code (Path.Combine("src/FS.GG.Coord.Cli", name))))
        |> Array.toList

    Assert.True(
        List.isEmpty offenders,
        $"""the CLI layer re-implements the lease-free ordering rule in: %s{String.concat ", " offenders}. It must call `Reads.lowestId` — design §4.2 and §12.5."""
    )

[<Fact>]
let ``the CLI consumers actually CALL the exported rule - the gate is not vacuous`` () =
    // The leg above is satisfiable by deleting the callers, which would be a green meaning the opposite of
    // what it claims. This pins that `who`, `reap` and `adopt` reach the rule through `Reads.lowestId`, so
    // "no second copy" means "routed through the one copy" rather than "no longer computed".
    let client = code "src/FS.GG.Coord.Cli/Client.fs"
    let calls = Regex.Matches(client, @"Reads\.lowestId\b").Count

    Assert.True(
        calls >= 3,
        $"expected `who`, `reap` and `adopt` to consume `Reads.lowestId` — found %d{calls} call site(s)"
    )
