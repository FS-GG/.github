namespace FS.GG.Coord.Cli.Tests

open Xunit
open FS.GG.Coord.Cli

/// #480: a worker command scopes to the repo you are STANDING IN, read from the git remote. The
/// end-to-end behaviour (the git-remote default, `take`'s refusal, `ready` staying org-wide, short-id
/// resolution) is held by the coord-engine parity harness against a live checkout. These cover the ONE
/// piece parity cannot cheaply reach: the URL parser's REFUSALS. A remote that does not name exactly one
/// `owner/repo` must yield `None`, never a half-parsed scope — the same fail-shut posture the reads have.
module RepoScopeTests =

    [<Theory>]
    [<InlineData("https://github.com/FS-GG/.github", "FS-GG/.github")>]
    [<InlineData("https://github.com/FS-GG/.github.git", "FS-GG/.github")>]
    [<InlineData("https://github.com/FS-GG/FS.GG.SDD.git", "FS-GG/FS.GG.SDD")>]
    [<InlineData("git@github.com:FS-GG/FS.GG.Templates.git", "FS-GG/FS.GG.Templates")>]
    [<InlineData("git@github.com:FS-GG/FS.GG.Templates", "FS-GG/FS.GG.Templates")>]
    [<InlineData("ssh://git@github.com/FS-GG/FS.GG.Audio.git", "FS-GG/FS.GG.Audio")>]
    [<InlineData("https://github.com/FS-GG/FS.GG.SDD/", "FS-GG/FS.GG.SDD")>]
    let ``every git remote form yields owner/repo`` (url: string) (expected: string) =
        Assert.Equal(Some expected, Client.parseGitHubSlug url)

    [<Theory>]
    [<InlineData("")>] // no remote at all
    [<InlineData("https://gitlab.com/FS-GG/x.git")>] // not GitHub
    [<InlineData("https://github.com/FS-GG")>] // a bare owner — no repo
    [<InlineData("https://github.com/")>] // a bare host
    [<InlineData("https://github.com/FS-GG/group/repo")>] // a nested path is not a scope (bash's */*/* )
    [<InlineData("git@github.com:FS-GG/")>] // owner with an empty repo
    let ``a remote that does not name exactly one owner/repo is refused`` (url: string) =
        Assert.Equal(None, Client.parseGitHubSlug url)
