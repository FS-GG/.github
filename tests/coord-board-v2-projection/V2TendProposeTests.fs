/// Proposals to #4985 for board-v2 tend (FS-GG/.github#5136 phase 2 PR 5; design §6).
module FS.GG.Coord.GitHub.Tests.V2TendProposeTests

open System
open System.IO
open System.Text
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.GitHub.Tests.TendFakeGitHub

let private key = Encoding.UTF8.GetBytes "0123456789abcdef0123456789abcdef"

let private temporary () =
    let path = Path.Combine(Path.GetTempPath(), "tend-propose-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    path

let private context (board: Board) (root: string) (run: string) : V2TendWrite.Context =
    let closable = Collections.Generic.HashSet<string>()
    { Reads = board.Transport
      Send = V2TendWrite.allowlisted projectId closable.Contains board.Mutate
      Custody = V2TendWrite.Custody(Path.Combine(root, "tend"), run, ignore)
      Gate = V2TendWrite.GateCustody(Path.Combine(root, "gate"), ignore)
      Clock = TimeProvider.System
      Pacer = V2TendWrite.Pacer(TimeProvider.System, ignore, TimeSpan.Zero)
      Closable = closable
      Settings = V2TendWrite.defaultSettings projectId (set [ "EHotwagner" ]) }

let private proposal rule ref reason suggested : V2Tend.Proposal =
    { Id = rule + ":" + ref + ":" + reason; Ref = ref; Rule = rule; Reason = reason; Suggested = suggested }

let private ledgerOf (root: string) =
    match V2TendPropose.decodeLedger (File.ReadAllText(Path.Combine(root, "proposals.json"))) with
    | Ok ledger -> ledger
    | Error e -> failwith e

let private run (board: Board) (root: string) (runId: string) (proposals: V2Tend.Proposal list) =
    V2TendPropose.run (context board root runId) ignore (Path.Combine(root, "proposals.json")) (Some key) proposals

let private proposalPosts (board: Board) = board.Mutations |> List.filter (fun m -> m.Document = V2TendWrite.proposalsPath)

// ---------------------------------------------------------------------------------------------------

[<Fact>]
let ``the placeholder is byte for byte the observer's`` () =
    // scripts/board-drift-observer.py opaque("FS-GG/private-repo#7", b"0123456789abcdef0123456789abcdef")
    Assert.Equal("private:23478d2bf0d1", V2TendPropose.placeholder key "FS-GG/private-repo#7")

[<Fact>]
let ``entries publish a private or unknown subject only as its placeholder, and only Status suggestions`` () =
    let entries =
        V2TendPropose.entries key (fun repo -> repo = "FS-GG/public")
            [ proposal "P-READY" "FS-GG/public#1" "blocked-on-nothing" (Some "Ready")
              proposal "P-READY" "FS-GG/private-repo#7" "blocked-on-nothing" (Some "Ready")
              proposal "P-INTENT" "FS-GG/public#2" "intended-x" (Some "https://example.invalid/private-plan")
              proposal "P-LINK" "" "loose-link:no-delivery" None ]
    Assert.Equal<string list>(
        [ "P:P-READY:FS-GG/public#1:blocked-on-nothing"; "P:P-READY:private:23478d2bf0d1:blocked-on-nothing"; "P:P-INTENT:FS-GG/public#2:intended-x" ],
        entries |> List.map (fun e -> e.Id))
    Assert.Equal<string option list>([ Some "Ready"; Some "Ready"; None ], entries |> List.map (fun e -> e.Suggested))
    Assert.DoesNotContain(entries, fun e -> e.Subject.Contains "private-repo")

[<Fact>]
let ``each ID is posted once; a cleared ID that fires again is not reposted`` () =
    let root = temporary ()
    try
        let board = Board()
        board.PrivateRepos.Add "FS-GG/private-repo" |> ignore
        let a = proposal "P-READY" "FS-GG/x#1" "blocked-on-nothing" (Some "Ready")
        let b = proposal "P-NOBLOCKER" "FS-GG/private-repo#7" "no-blocker" None
        let first = run board root "run1" [ a; b ]
        Assert.Equal<string list>([ "written" ], first |> List.map (fun o -> o.Result))
        Assert.Single(proposalPosts board) |> ignore
        let _, body, _, _ = board.ProposalComments.[0]
        Assert.Contains("<!-- fsgg:board-tend-proposals/v1 run=run1 -->", body)
        Assert.Contains("P:P-READY:FS-GG/x#1:blocked-on-nothing", body)
        Assert.Contains("private:23478d2bf0d1", body)
        Assert.DoesNotContain("private-repo", body)
        let ledger = ledgerOf root
        Assert.Equal(2, ledger.Ids.Count)
        Assert.True(ledger.Ids |> Map.forall (fun _ e -> e.State = "live" && e.Comment = Some "IC_proposal_1"))
        // The same proposals: nothing new, nothing posted.
        Assert.Equal("no-new-ids", (run board root "run2" [ a; b ]).Head.Detail)
        // a clears, then fires again: marked cleared, then live, never reposted.
        run board root "run3" [ b ] |> ignore
        Assert.Equal("cleared", (ledgerOf root).Ids.["P:P-READY:FS-GG/x#1:blocked-on-nothing"].State)
        run board root "run4" [ a; b ] |> ignore
        Assert.Equal("live", (ledgerOf root).Ids.["P:P-READY:FS-GG/x#1:blocked-on-nothing"].State)
        Assert.Single(proposalPosts board) |> ignore
        // A genuinely new ID is posted in a second comment with only that ID.
        let c = proposal "S5" "FS-GG/x#3" "stale-active" None
        run board root "run5" [ a; b; c ] |> ignore
        Assert.Equal(2, (proposalPosts board).Length)
        let _, second, _, _ = board.ProposalComments.[1]
        Assert.Contains("P:S5:FS-GG/x#3:stale-active", second)
        Assert.DoesNotContain("P-READY", second)
    finally Directory.Delete(root, true)

[<Fact>]
let ``an uncertain post is never repeated, and the next run finds it by its run marker`` () =
    let root = temporary ()
    try
        let board = Board()
        let a = proposal "P-READY" "FS-GG/x#1" "blocked-on-nothing" (Some "Ready")
        board.LoseProposalResponse <- true
        let first = run board root "run1" [ a ]
        Assert.Equal(("uncertain", "post-unconfirmed"), (first.Head.Result, first.Head.Detail))
        Assert.True((ledgerOf root).Pending.IsSome)
        board.LoseProposalResponse <- false
        let second = run board root "run2" [ a; proposal "S5" "FS-GG/x#3" "stale-active" None ]
        Assert.Equal("verified", second.Head.Result)
        let ledger = ledgerOf root
        Assert.True(ledger.Pending.IsNone)
        Assert.Equal(Some "IC_proposal_1", ledger.Ids.["P:P-READY:FS-GG/x#1:blocked-on-nothing"].Comment)
        // The pending ID was never reposted; only the new one was posted.
        Assert.Equal(2, (proposalPosts board).Length)
        let _, second, _, _ = board.ProposalComments.[1]
        Assert.DoesNotContain("P-READY", second)
    finally Directory.Delete(root, true)

[<Fact>]
let ``a pending post that cannot be found blocks new posts rather than risk a duplicate`` () =
    let root = temporary ()
    try
        let board = Board()
        board.LoseProposalResponse <- true
        run board root "run1" [ proposal "P-READY" "FS-GG/x#1" "blocked-on-nothing" None ] |> ignore
        board.LoseProposalResponse <- false
        board.ProposalComments.Clear() // the post never reached GitHub
        let second = run board root "run2" [ proposal "S5" "FS-GG/x#3" "stale-active" None ]
        Assert.Equal<string list>([ "pending-post-not-found"; "pending-post-unresolved" ], second |> List.map (fun o -> o.Detail))
        Assert.Single(proposalPosts board) |> ignore
        // A comment from someone else, or from before the custody time, never resolves it.
        board.ProposalComments.Add(("IC_other", "<!-- fsgg:board-tend-proposals/v1 run=run1 -->", "2026-11-01T12:00:00Z", "someone-else"))
        Assert.Equal("pending-post-not-found", (run board root "run3" []).Head.Detail)
    finally Directory.Delete(root, true)

[<Fact>]
let ``without a key nothing is posted and the ledger is untouched`` () =
    let root = temporary ()
    try
        let board = Board()
        let ps = [ proposal "P-READY" "FS-GG/x#1" "blocked-on-nothing" None ]
        Assert.Equal("proposals-key-unavailable", (V2TendPropose.run (context board root "r") ignore (Path.Combine(root, "proposals.json")) None ps).Head.Detail)
        Assert.Equal("proposals-key-too-short", (V2TendPropose.run (context board root "r") ignore (Path.Combine(root, "proposals.json")) (Some [| 1uy |]) ps).Head.Detail)
        Assert.Empty(board.Calls)
        Assert.False(File.Exists(Path.Combine(root, "proposals.json")))
    finally Directory.Delete(root, true)

[<Fact>]
let ``the allowlist admits the proposals route only as exactly one marked comment on 4985`` () =
    let request path (json: string) : Request =
        { Method = "POST"; Path = path; Query = []; Body = Json json; Budget = Rest; IfNoneMatch = None; Subject = "tend proposals" }
    let marked = "{\"body\":\"<!-- fsgg:board-tend-proposals/v1 run=r -->\\nrows\"}"
    Assert.Equal(Ok(), V2TendWrite.admits projectId (fun _ -> false) (request V2TendWrite.proposalsPath marked))
    for path, json in
        [ "repos/FS-GG/.github/issues/4986/comments", marked
          "repos/FS-GG/.github/issues/4985/comments/1", marked
          V2TendWrite.proposalsPath, "{\"body\":\"no marker\"}"
          V2TendWrite.proposalsPath, "{\"body\":\"<!-- fsgg:board-tend-proposals/v1 run=a --> <!-- fsgg:board-tend-proposals/v1 run=b -->\"}"
          V2TendWrite.proposalsPath, "{\"body\":\"<!-- fsgg:board-tend-proposals/v1 run=r -->\",\"labels\":[\"x\"]}"
          V2TendWrite.proposalsPath, "not json" ] do
        Assert.True(V2TendWrite.admits projectId (fun _ -> false) (request path json) |> Result.isError, path + " " + json)
