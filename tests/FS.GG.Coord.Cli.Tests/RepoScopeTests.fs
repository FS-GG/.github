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

    // ---- #962: `--repo` RESOLVES, at the parser, for every verb ------------------------------------------

    /// The three documented `--repo` spellings (skill Setup: "a registry short-id, `owner/repo`, or a bare
    /// repo name") all reduce to the repo NAME board rows carry.
    [<Theory>]
    [<InlineData("sdd", "FS.GG.SDD")>]
    [<InlineData("rendering", "FS.GG.Rendering")>]
    [<InlineData("governance", "FS.GG.Governance")>]
    [<InlineData("templates", "FS.GG.Templates")>]
    [<InlineData("game", "FS.GG.Game")>]
    [<InlineData("audio", "FS.GG.Audio")>]
    [<InlineData("Governance", "FS.GG.Governance")>] // never a casing bug — the map is case-insensitive
    [<InlineData("FS-GG/FS.GG.SDD", "FS.GG.SDD")>] // owner/repo -> the repo part
    [<InlineData("FS.GG.SDD", "FS.GG.SDD")>] // a literal name passes through
    [<InlineData(".github", ".github")>] // the repo whose short-id and name coincide
    let ``every documented --repo spelling resolves to the board's repo name`` (raw: string) (expected: string) =
        Assert.Equal(expected, Options.resolveRepo raw)

    /// IDEMPOTENT, and that is load-bearing rather than incidental: `Client` still resolves a GIT REMOTE and
    /// `issues`'s POSITIONAL repo arg through the same map, and those sites may be handed an already-resolved
    /// name. If resolving twice moved, every one of them would be a bug.
    [<Theory>]
    [<InlineData("sdd")>]
    [<InlineData("FS-GG/FS.GG.SDD")>]
    [<InlineData(".github")>]
    let ``resolveRepo is idempotent`` (raw: string) =
        let once = Options.resolveRepo raw
        Assert.Equal(once, Options.resolveRepo once)

    /// THE REGRESSION THAT MATTERS, and the one no per-verb test can give you: `--repo` is resolved by the
    /// PARSER, so it is resolved for EVERY verb — including any verb written after this test.
    ///
    /// This bug has been filed three times, each time as a different verb left out of a downstream
    /// resolution list: #381 (`--repo game` matched nothing), #446 (`issues` never called it), #962
    /// (`ready`, in the F# port — `[]` and exit 0 over a full board). A list is what regenerates it, so the
    /// assertion is over the VERB SURFACE rather than any one verb. `scan` is here deliberately: it is
    /// dispatched straight from `Program` and never reaches `Client.run`, so it is the verb that proves the
    /// resolution cannot live there.
    [<Theory>]
    [<InlineData("ready")>]
    [<InlineData("scan")>]
    [<InlineData("next")>]
    [<InlineData("batch")>]
    [<InlineData("who")>]
    [<InlineData("reap")>]
    [<InlineData("inbox")>]
    [<InlineData("lint")>]
    [<InlineData("take")>]
    let ``--repo is resolved for every repo-taking verb, not a list of them`` (verb: string) =
        let repoOf argv =
            match Options.parse argv with
            | Ok o -> o.Repo
            | Error e -> failwith $"%s{verb}: parse refused a documented invocation: %s{e}"

        // A short-id and the bare name it maps to must name ONE queue — for this verb, whatever it is.
        Assert.Equal(Some "FS.GG.SDD", repoOf [ verb; "--repo"; "sdd" ])
        Assert.Equal(repoOf [ verb; "--repo"; "FS.GG.SDD" ], repoOf [ verb; "--repo"; "sdd" ])
        Assert.Equal(repoOf [ verb; "--repo"; "FS.GG.SDD" ], repoOf [ verb; "--repo"; "FS-GG/FS.GG.SDD" ])
