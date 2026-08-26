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
        Assert.Equal(Some expected, Kernel.parseGitHubSlug url)

    [<Theory>]
    [<InlineData("")>] // no remote at all
    [<InlineData("https://gitlab.com/FS-GG/x.git")>] // not GitHub
    [<InlineData("https://github.com/FS-GG")>] // a bare owner — no repo
    [<InlineData("https://github.com/")>] // a bare host
    [<InlineData("https://github.com/FS-GG/group/repo")>] // a nested path is not a scope (bash's */*/* )
    [<InlineData("git@github.com:FS-GG/")>] // owner with an empty repo
    let ``a remote that does not name exactly one owner/repo is refused`` (url: string) =
        Assert.Equal(None, Kernel.parseGitHubSlug url)

    // ---- #962: `--repo` RESOLVES, at the parser, for every verb ------------------------------------------

    /// The three documented `--repo` spellings (skill Setup: "a registry short-id, `owner/repo`, or a bare
    /// repo name") all reduce to the repo NAME board rows carry — now asserted on the `RepoScope.Scope`
    /// union itself (#2398), so a roster spelling that resolved to `NonRepository` by mistake would fail
    /// here rather than compile away into a string nobody checked the tag of.
    [<Theory>]
    [<InlineData("sdd", "FS.GG.SDD")>]
    [<InlineData("rendering", "FS.GG.Rendering")>]
    [<InlineData("governance", "FS.GG.Governance")>]
    [<InlineData("templates", "FS.GG.Templates")>]
    [<InlineData("game", "FS.GG.Game")>]
    [<InlineData("audio", "FS.GG.Audio")>]
    [<InlineData("net", "FS.GG.Net")>]
    [<InlineData("coordination", "FS.GG.Coordination")>]
    [<InlineData("Governance", "FS.GG.Governance")>] // never a casing bug — the map is case-insensitive
    [<InlineData("FS-GG/FS.GG.SDD", "FS.GG.SDD")>] // owner/repo -> the repo part
    [<InlineData("FS.GG.SDD", "FS.GG.SDD")>] // a literal name passes through
    [<InlineData(".github", ".github")>] // the repo whose short-id and name coincide
    let ``every documented --repo spelling resolves to the board's repo name`` (raw: string) (expected: string) =
        Assert.Equal(FS.GG.Coord.RepoScope.Repository expected, Options.resolveRepo raw)

    /// IDEMPOTENT, and that is load-bearing rather than incidental: `Client` still resolves a GIT REMOTE and
    /// `issues`'s POSITIONAL repo arg through the same map, and those sites may be handed an already-resolved
    /// name. If resolving twice moved, every one of them would be a bug. Compared as `Scope` values
    /// directly (#2398) — a strictly stronger check than the prior string comparison, since it also pins
    /// which arm survives the second resolve.
    [<Theory>]
    [<InlineData("sdd")>]
    [<InlineData("FS-GG/FS.GG.SDD")>]
    [<InlineData("net")>]
    [<InlineData(".github")>]
    let ``resolveRepo is idempotent`` (raw: string) =
        let once = Options.resolveRepo raw

        // Re-resolving the same DISPLAY STRING a first resolve produced must land on the same arm —
        // stated by an exhaustive two-arm match (never `Option.defaultValue` or a wildcard), so a
        // hypothetical third `Scope` arm fails THIS build (FS0025, `TreatWarningsAsErrors`) rather than
        // silently falling through unhandled.
        let onceName =
            match once with
            | FS.GG.Coord.RepoScope.Repository name -> name
            | FS.GG.Coord.RepoScope.NonRepository token -> token

        Assert.Equal(once, Options.resolveRepo onceName)

    // ---- .github#2363: `sir` / `S.I.R.` spelling equivalence, on the union rather than a string --------

    /// Criterion 4's TYPE-LEVEL half: `sir` (any casing) and the canonical `S.I.R.` spelling resolve to
    /// the identical `Scope` value, so `batch --repo sir` and `batch --repo S.I.R.` compare a board row's
    /// resolved scope against the SAME `Repository "S.I.R."`, never two strings that merely print alike.
    /// The live-fixture half of criterion 4 (rows actually selected) and criteria 5-6 (claim exclusion
    /// across spellings, external-owner receiver verification) are `.github#2363`'s remaining scope —
    /// this item's acceptance carries the union-level coverage, not the receiver-fixture proof.
    [<Theory>]
    [<InlineData("sir")>]
    [<InlineData("Sir")>]
    [<InlineData("SIR")>]
    [<InlineData("S.I.R.")>]
    let ``sir and S.I.R. resolve to the identical Scope value`` (raw: string) =
        Assert.Equal(FS.GG.Coord.RepoScope.Repository "S.I.R.", Options.resolveRepo raw)

    /// Gate-inversion evidence for the spelling-equivalence assertion above: reverting `RepoScope.fs`'s
    /// `"sir" -> Repository "S.I.R."` arm to the pre-fix `"sir" -> "S.I.R."` string case (i.e. dropping
    /// the tag) does not change what this test observes, because the test already asserts the STRUCTURED
    /// value — it is the `resolveRepo`/`Options.resolveRepo` SIGNATURE change (string -> Scope) that makes
    /// this file fail to compile at all against the pre-fix engine, which is the strongest red a type-level
    /// fix can produce: not a wrong answer, a build that cannot lie about the answer having been checked.

    // ---- .github#2398: `cross-repo` is a `NonRepository`, never a `Repository` -----------------------------

    /// THE FIX ITSELF: the board's one deliberate non-roster value (`docs/coordination/board-schema.md`)
    /// comes back tagged, so a consumer cannot compare it against a repository, reserve a `Paths:` token
    /// under it, or select a claim lock with it without an explicit `NonRepository` match arm first.
    [<Theory>]
    [<InlineData("cross-repo")>]
    [<InlineData("Cross-Repo")>]
    [<InlineData("CROSS-REPO")>]
    let ``cross-repo resolves to NonRepository, never a Repository`` (raw: string) =
        match Options.resolveRepo raw with
        | FS.GG.Coord.RepoScope.NonRepository token -> Assert.Equal(raw, token)
        | FS.GG.Coord.RepoScope.Repository name ->
            failwith $"cross-repo must never resolve to a Repository — got %s{name}"

    /// Gate-inversion evidence: reverting `RepoScope.resolve`'s `"cross-repo" -> NonRepository raw` arm to
    /// fall through to the generic passthrough (`"cross-repo" -> Repository raw`, i.e. treating it like
    /// any other unrecognised token) turns this test red — the match above hits the `Repository` arm and
    /// fails with the message above, rather than passing on either shape. Observed by hand against a
    /// scratch revert of `RepoScope.fs` before this PR; restored and reconfirmed green.

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
    let ``every receiver's chore lock resolves to its closed lock issue``
        (repo: string, canonicalRepo: string, number: int)
        =
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
