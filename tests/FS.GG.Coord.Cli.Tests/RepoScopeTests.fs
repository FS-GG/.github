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
    [<InlineData("net", "FS.GG.Net")>]
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
    [<InlineData("net")>]
    [<InlineData(".github")>]
    let ``resolveRepo is idempotent`` (raw: string) =
        let once = Options.resolveRepo raw
        Assert.Equal(once, Options.resolveRepo once)

    // ---- ADR-0042 / #1026: the chore-lock ref is EMBEDDED, and absent means SHUT ------------------------

    /// The lock resolves through the SAME map as `--repo`, so every documented spelling of the one repo that
    /// has a lock names the one issue (#1033). A caller that spells it `sdd`-style must not get a different
    /// answer from one that spells it out.
    [<Theory>]
    [<InlineData(".github")>]
    [<InlineData(".GitHub")>] // never a casing bug — the map is case-insensitive, like the roster
    [<InlineData("FS-GG/.github")>]
    let ``.github's chore lock resolves to the closed lock issue`` (repo: string) =
        let expected: FS.GG.Coord.Types.Ref =
            { Owner = "FS-GG"
              Repo = ".github"
              Number = 1033 }

        Assert.Equal(Some expected, Options.choreLockRef [] "FS-GG" repo)

    /// ALL SEVEN REPOS RESOLVE (#1087). The six receivers gained closed `[chore-lock]` issues and this map
    /// gained their numbers, so the queue drains in every repo rather than only `.github`. Each is asserted
    /// through a short-id spelling AND its canonical spelling, because both reach `choreLockRef` — a worker
    /// types `--repo game`, `offer` is handed `FS.GG.Game`. The NUMBER is pinned here because this map is the
    /// only place the engine records it (ADR-0042: no YAML reader), so a silent renumber must red a test, not
    /// a live lock on the wrong subject.
    [<Theory>]
    [<InlineData("sdd", "FS.GG.SDD", 518)>]
    [<InlineData("FS.GG.SDD", "FS.GG.SDD", 518)>]
    [<InlineData("rendering", "FS.GG.Rendering", 878)>]
    [<InlineData("FS.GG.Rendering", "FS.GG.Rendering", 878)>]
    [<InlineData("governance", "FS.GG.Governance", 268)>]
    [<InlineData("FS.GG.Governance", "FS.GG.Governance", 268)>]
    [<InlineData("templates", "FS.GG.Templates", 252)>]
    [<InlineData("game", "FS.GG.Game", 406)>]
    [<InlineData("audio", "FS.GG.Audio", 183)>]
    let ``every receiver's chore lock resolves to its closed lock issue`` (repo: string, canonicalRepo: string, number: int) =
        let expected: FS.GG.Coord.Types.Ref =
            { Owner = "FS-GG"
              Repo = canonicalRepo
              Number = number }

        Assert.Equal(Some expected, Options.choreLockRef [] "FS-GG" repo)

    /// FAIL CLOSED remains the rule for a repo NOBODY rostered: ADR-0041 — "Absent ⇒ `offer` refuses. A chore
    /// queue that cannot find its lock must offer nothing, never broadcast." The seven known repos now resolve;
    /// an eighth the map does not know is `None`, exactly as the six receivers were before #1087.
    [<Theory>]
    [<InlineData("FS.GG.Nonexistent")>]
    [<InlineData("some-fork")>]
    [<InlineData("")>]
    let ``a repo with no lock issue has no lock`` (repo: string) =
        Assert.Equal(None, Options.choreLockRef [] "FS-GG" repo)

    /// THE FAIL-OPEN THIS KEYING EXISTS TO REFUSE. The owner is CONFIGURABLE (`FSGG_COORD_OWNER`), and the
    /// embedded numbers are FS-GG's issues. Keyed on the repo alone, a caller under any other owner would be
    /// handed `<their-owner>/.github#1033` — a WELL-FORMED ref naming an unrelated issue in a repo that has
    /// nothing to do with this org, i.e. a lock that protects nothing while reporting that it does. That is
    /// #266's shape exactly, so an unknown owner has no lock rather than a wrong one.
    [<Theory>]
    [<InlineData("acme")>]
    [<InlineData("FS-GG-fork")>]
    [<InlineData("")>]
    let ``an owner the map does not know has no lock, never a foreign one`` (owner: string) =
        Assert.Equal(None, Options.choreLockRef [] owner ".github")

    /// CANONICAL OUT, CASE-INSENSITIVE IN — `resolveRepo`'s contract, applied to the owner as well. Echoing
    /// the caller's casing back would mint a Ref structurally UNEQUAL to the canonical one while `Short`
    /// renders both `.github#1033`: two locks that compare different and print the same. A CAS whose subject
    /// can silently split that way is not a lock, and no log line would ever show it.
    [<Theory>]
    [<InlineData("FS-GG")>]
    [<InlineData("fs-gg")>]
    [<InlineData("Fs-Gg")>]
    let ``the lock ref is canonical however the owner was spelled`` (owner: string) =
        let expected: FS.GG.Coord.Types.Ref =
            { Owner = "FS-GG"
              Repo = ".github"
              Number = 1033 }

        Assert.Equal(Some expected, Options.choreLockRef [] owner ".github")

    /// The lock issue must never be confused with WORK. ADR-0041 puts three properties on it and the ref is
    /// only sound while all three hold — closed, unlocked, and never on the board. Only the number is
    /// checkable from here (the live issue's state is asserted by the PR that created it), but pinning the
    /// number is what makes a silent renumber a red test rather than a lock on the wrong subject.
    [<Fact>]
    let ``the embedded lock number is pinned`` () =
        match Options.choreLockRef [] "FS-GG" ".github" with
        | Some r ->
            Assert.Equal(1033, r.Number)
            Assert.Equal(".github", r.Repo)
            Assert.Equal("FS-GG", r.Owner)
            Assert.Equal(".github#1033", r.Short) // `Short` drops the owner — Types.Ref
        | None -> failwith "the .github chore lock must resolve — ADR-0042 embeds it"

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
