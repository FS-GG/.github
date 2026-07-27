namespace FS.GG.Coord.Cli

/// THE KIT-DIGEST OBLIGATION'S IO EDGE (#469/#563/#588/#509) — the file/tree reads under the pure
/// comparisons in `Core.Kit`, extracted out of `Client` as the first Client.fs decomposition seam
/// (ADR-0047). `Client` composes these two advisories into the verbs that need them; this module is
/// that IO and nothing else, `.fsi`-fronted so its only surface is the two `warn` entry points.
///
/// Editing a kit source obliges a re-digest of `registry/repos.lock` (ADR-0019), and the cheap moment
/// to learn that is BEFORE a red `main` after the merge — which is how #469 was found. The pure
/// comparisons live in `Core.Kit`; this is the file IO under them. Silent whenever there is no tree or
/// no lock to read: a receiver mirrors the kit but not the registry, and must not be nagged about a
/// file it has not got.
///
/// THERE ARE TWO QUESTIONS HERE, AND #563 IS ONLY THE ANSWER TO ONE OF THEM.
///
///   * *Is the lock stale?* — `digestWarn`, OBSERVED off the tree. Never inferred from what was
///     declared: #563 pinned the fail-open where declaring `registry/repos.yml` marked the obligation
///     met while the lock went stale. A declaration cannot prove a digest matches, so it is never asked
///     to.
///   * *Will this worker owe a relock?* — `declaredWarn`, read off the DECLARATION, because at claim
///     time nothing is edited yet and there is no digest to compare. `staleSources` is silent by
///     construction on a clean tree, which is exactly why the obligation went unnamed until CI (#509).
///
/// The second is not a re-run of the inference the first replaced: it can only ADD advice, never
/// suppress it, so it cannot fail open the way its ancestor did. `Kit.declaredSources` carries that
/// argument.
///
/// BOTH ARE ADVISORY AND NEITHER IS A GATE. CI is authoritative; over-warning mis-gates nothing.
module KitDigest =

    open System
    open System.Diagnostics
    open System.IO
    open FS.GG.Coord
    open FS.GG.Coord.Types
    open FS.GG.Coord.GitHub
    open FS.GG.Coord.GitHub.Transport

    let private eprint (s: string) = Console.Error.WriteLine(s: string)

    /// The kit root: `FSGG_KIT_ROOT` (the fixture stands a throwaway tree up here), else the git toplevel.
    /// `git rev-parse --show-toplevel` is FREE and offline; `None` outside a checkout, which is silent.
    ///
    /// Public because `verify-paths` needs the same root to locate its generated-paths subtraction — the
    /// one place outside the two advisories that resolves "where is the tree?" the same way (ADR-0047).
    let kitRoot () : string option =
        match Environment.GetEnvironmentVariable "FSGG_KIT_ROOT" with
        | null
        | "" ->
            try
                let psi = ProcessStartInfo("git", "rev-parse --show-toplevel")
                psi.RedirectStandardOutput <- true
                psi.RedirectStandardError <- true
                psi.UseShellExecute <- false
                use p = Process.Start psi
                let top = p.StandardOutput.ReadToEnd().Trim()
                p.WaitForExit()
                if p.ExitCode <> 0 || top = "" then None else Some top
            with _ ->
                None
        | v -> Some v

    /// The ACTUAL digest of a kit source, off the tree. A skill kit is content-addressed on its `SKILL.md`,
    /// a client kit on the file itself. `None` when the entry names a file this tree does not carry (or one
    /// we could not read) — `Kit.staleSources` reads that as "not here", never a staleness.
    let private kitDigestOf (root: string) (src: string) : string option =
        let path0 = Path.Combine(root, src)
        let path = if Directory.Exists path0 then Path.Combine(path0, "SKILL.md") else path0

        if File.Exists path then
            try
                use sha = System.Security.Cryptography.SHA256.Create()

                Some(
                    File.ReadAllBytes path
                    |> sha.ComputeHash
                    |> Array.map (fun b -> b.ToString "x2")
                    |> String.concat ""
                )
            with _ ->
                None
        else
            None

    /// The universal skill roots — `coordination-sync`, FS.GG.Kit and fsgg-sdd share this default
    /// under ADR-0065. Repos with an intentional override simply lack the other root directories;
    /// `kitDivergedRoots` treats an absent root as outside that tree's declared runtime surface.
    let private kitSkillRoots = [ ".claude/skills"; ".agents/skills" ]

    let private skillCopies (roots: string list) (src: string) : (string * string) list =
        let trim (s: string) = s.TrimEnd([| '/' |])
        let source = trim src
        let lane = roots |> List.map trim

        match lane |> List.tryFind (fun root -> source.StartsWith(root + "/", StringComparison.Ordinal)) with
        | None -> []
        | Some sourceRoot ->
            let name = source.Substring(sourceRoot.Length + 1)

            if name = "" || name.Contains "/" then
                []
            else
                lane
                |> List.filter ((<>) sourceRoot)
                |> List.map (fun targetRoot -> source, targetRoot + "/" + name)

    /// The KIT's skills whose roots have diverged, as `(source, mirror)` directory pairs — read off the
    /// lock's declared sources, never off a directory listing (#647). A client kit yields no mirror, so a
    /// client-only staleness never nags about roots; a repo's OWN skills are not the kit's business and are
    /// not in the lock, so they are not policed here at all.
    ///
    /// A source this tree does not carry is SILENT, not diverged — the same rule `Kit.staleSources` keeps for
    /// the same reason: a receiver that mirrors the kit but not a given source must not be nagged about a
    /// file it has not got. A source present while its mirror is MISSING FROM AN EXISTING ROOT is diverged:
    /// the kit really does owe both roots.
    ///
    /// **A ROOT THIS TREE HAS NOT GOT AT ALL IS SILENT TOO, AND THAT IS NOT THE SAME RULE.** A receiver may
    /// materialize the kit into ONE root (`AGENT_SKILL_ROOTS` is configurable, and `coordination-sync`'s two
    /// are only its DEFAULT), and telling it to `cp` a skill into a root it deliberately does not have would
    /// be #647's own bug in a narrower coat: a warning, on a green tree, about a layout that is nobody's
    /// mistake. An absent root is a repo's decision; an absent FILE inside a root it does keep is drift.
    let private kitDivergedRoots (root: string) (lock: (string * string) list) : (string * string) list =
        let abs (p: string) =
            Path.Combine(root, p.Replace('/', Path.DirectorySeparatorChar))

        let readBytes p =
            if File.Exists p then
                try
                    Some(File.ReadAllBytes p)
                with _ ->
                    None
            else
                None

        let skillMd (dir: string) = Path.Combine(abs dir, "SKILL.md")
        let rootExists (r: string) = Directory.Exists(abs r)

        lock
        |> List.collect (fun (_want, src) -> skillCopies kitSkillRoots src)
        |> List.choose (fun (source, mirror) ->
            // The mirror's ROOT, not the skill's own directory: `.agents/skills` for `.agents/skills/x`.
            let mirrorRoot = Path.GetDirectoryName(mirror.Replace('/', Path.DirectorySeparatorChar))

            match readBytes (skillMd source) with
            | None -> None // the kit's source is not in this tree — "not here", never a divergence
            | Some _ when not (rootExists mirrorRoot) -> None // this tree keeps no such root — its choice
            | Some bytes -> Some((source, mirror), Some bytes, readBytes (skillMd mirror)))
        |> Kit.divergedRoots

    /// OBSERVE the kit-digest obligation and warn on stderr (advisory, never fatal — `repos-registry-selftest`
    /// is the authority). Silent when there is no root or no lock to read.
    let digestWarn () : unit =
        match kitRoot () with
        | None -> ()
        | Some root ->
            let lockPath =
                match Environment.GetEnvironmentVariable "FSGG_REPOS_LOCK" with
                | null
                | "" -> Path.Combine(root, "registry", "repos.lock")
                | v -> v

            if File.Exists lockPath then
                let lock =
                    try
                        Kit.parseLock (File.ReadAllText lockPath)
                    with _ ->
                        []

                match Kit.staleSources (kitDigestOf root) lock with
                | [] -> ()
                | stale ->
                    eprint ""
                    eprint "fsgg-coord-engine: KIT DIGEST — a content-addressed kit source is STALE in `registry/repos.lock`:"

                    for s in stale do
                        eprint $"  %s{s}"

                    eprint "`repos-registry-selftest` reds `main` until it is regenerated (ADR-0019, #469). Run:"
                    eprint "  scripts/repos.sh relock"
                    eprint "Then name `registry/repos.lock` as EXPECTED DRIFT in the PR — do NOT reserve it in the"
                    eprint "touch-set. It is a GENERATED, CI-GATED artifact, so a collision in it is a REBASE, not a"
                    eprint "decision (#309). Reserving it is the three-worker deadlock #527 removed (#428)."

                match kitDivergedRoots root lock with
                | [] -> ()
                | roots ->
                    eprint ""
                    eprint "fsgg-coord-engine: SKILL ROOTS — a KIT skill must stay BYTE-IDENTICAL across every root"
                    eprint "(ADR-0011/0014), or the `roots` gate reds `main`. These have diverged:"

                    // FROM the registry-declared source, TO its mirror. Never the reverse: the source is the
                    // copy the lock digests and `coordination-sync` fans out (#647).
                    for source, mirror in roots do
                        eprint $"  cp %s{source}/SKILL.md %s{mirror}/SKILL.md"

    /// Name the relock a worker has just signed up for, off the touch-set they claimed (#509).
    ///
    /// **THE READ IS ON THE WIN PATH ONLY, AND THAT IS NOT AN OPTIMISATION.** `claim` is the hottest command
    /// in the org and REST is the budget that dies first under fan-out (#895, measured: core at 0/5,000 while
    /// GraphQL had 3,639 spare). So this costs ONE issue read, once, and only when a worker actually took the
    /// item — never on a lost race, never on an idempotent re-claim. It is the same discipline the `board`
    /// thunk keeps below, for the same reason (#418).
    ///
    /// A read we could not make is SILENT. This is advice, not a gate: refusing a claim whose lock is held
    /// and whose marker is posted, because an advisory read failed, would trade a real lock for a courtesy.
    ///
    /// Takes the `IGitHubTransport` directly rather than `Client.Context`: this module is compiled before
    /// `Client`, and `ctx.Transport` is all it ever read (ADR-0047). The caller passes `ctx.Transport`.
    let declaredWarn (transport: IGitHubTransport) (ref: Ref) : unit =
        match kitRoot () with
        | None -> ()
        | Some root ->
            let lockPath =
                match Environment.GetEnvironmentVariable "FSGG_REPOS_LOCK" with
                | null
                | "" -> Path.Combine(root, "registry", "repos.lock")
                | v -> v

            if File.Exists lockPath then
                let lock =
                    try
                        Kit.parseLock (File.ReadAllText lockPath)
                    with _ ->
                        []

                match Reads.issueBody transport ref.Owner ref.Repo ref.Number with
                | Error _ -> () // an advisory read that failed says nothing, and must not say something
                | Ok body ->
                    let declared =
                        match TouchSet.parse body with
                        // MATCHABLE TOKENS ONLY. An unmatchable token reserves nothing and covers nothing
                        // (#273), so it cannot name a kit source either — the same asymmetry `TouchSet.covers`
                        // keeps. Reading one as a declaration here would warn about an obligation the worker
                        // has provably not taken on.
                        | Declared tokens ->
                            tokens
                            |> List.choose (function
                                | Matchable t -> Some t
                                | Unmatchable _ -> None)
                        // Undeclared / `Paths: none` / `Paths: any` — no path declaration to read an
                        // obligation off (a chore reserves nothing, so it names no kit source either).
                        | Undeclared
                        | DeclaredNone
                        | DeclaredChore -> []
                        // WE DID NOT READ THE BODY — unknown, not absent, and the two are not one fact
                        // (#266). Here that distinction costs nothing to honour and is still worth honouring:
                        // this is ADVICE, and advice can only ever ADD. An unread body yields no obligation to
                        // name, so it names none. What it must NOT do is what the caller of a SCHEDULING read
                        // must never do — coerce this to `Undeclared` and make a confident claim about an item
                        // nobody looked at.
                        | Unreadable _ -> []

                    match Kit.declaredSources lock declared with
                    | [] -> ()
                    | sources ->
                        eprint ""
                        eprint "fsgg-coord-engine: KIT DIGEST — the touch-set you just claimed names a content-addressed kit source:"

                        for s in sources do
                            eprint $"  %s{s}"

                        eprint "If you EDIT it, `registry/repos.lock` goes stale and `repos-registry-selftest` reds `main`"
                        eprint "(ADR-0019, #469). Before you open the PR:"
                        eprint "  scripts/repos.sh relock"
                        eprint "Then name `registry/repos.lock` as EXPECTED DRIFT in the PR — do NOT reserve it in the"
                        eprint "touch-set. It is a GENERATED, CI-GATED artifact, so a collision in it is a REBASE, not a"
                        eprint "decision (#309). Reserving it is the three-worker deadlock #527 removed (#428)."
                        eprint "If you only READ it, ignore this — and give the path back (`widen`), so it stops"
                        eprint "reserving files nobody is editing (#601)."
