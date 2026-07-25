namespace FS.GG.Coord.Cli.Tests

open Xunit
open FS.GG.Coord.Types
open FS.GG.Coord.GitHub
open FS.GG.Coord.Cli

module ApplicationServiceTests =

    let private row number repo title status state isPullRequest : Scan.Row =
        { Ref =
            { Owner = "FS-GG"
              Repo = repo
              Number = number }
          Title = title
          Status = status
          BlockedByRaw = ""
          State = state
          IsPullRequest = isPullRequest }

    [<Fact>]
    let ``ready application service preserves the exact JSON projection contract`` () =
        let rows =
            [ row 1 ".github" "quote: \"kept\"" BoardStatus.Ready IssueState.Open false
              row 2 ".github" "done" BoardStatus.Done IssueState.Closed false
              row 3 ".github" "pull request" BoardStatus.Ready IssueState.Open true
              row 4 "FS.GG.Game" "other repo" BoardStatus.Backlog IssueState.Open false ]

        let selected = ReadyApplication.select (Some ".github") None false rows

        Assert.Equal(1, List.length selected.Rows)
        Assert.Equal(
            """[{"number":1,"repo":"FS-GG/.github","title":"quote: \u0022kept\u0022","status":"Ready","state":"OPEN"}]""",
            Render.renderReadyJson selected.Rows
        )

    [<Fact>]
    let ``ready status selection is case-insensitive and can explicitly include Done`` () =
        let rows =
            [ row 1 ".github" "ready" BoardStatus.Ready IssueState.Open false
              row 2 ".github" "done" BoardStatus.Done IssueState.Closed false ]

        let selected = ReadyApplication.select None (Some "done") false rows

        Assert.Equal<int list>([ 2 ], selected.Rows |> List.map (fun item -> item.Ref.Number))

    [<Fact>]
    let ``lint summary owns strict gate semantics`` () =
        let permissive = LintApplication.summarize false [ "note" ]
        let strict = LintApplication.summarize true [ "note" ]
        let error = LintApplication.summarize false [ "error" ]

        Assert.False(permissive.Fails)
        Assert.True(strict.Fails)
        Assert.True(error.Fails)
        Assert.Equal(1, error.Errors)
