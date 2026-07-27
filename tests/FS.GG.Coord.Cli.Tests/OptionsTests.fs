namespace FS.GG.Coord.Cli.Tests

open Xunit
open FS.GG.Coord.Cli
open FS.GG.Coord.Cli.Options

/// THE RESIDUE RULE. An argument that is ignored is indistinguishable, from the caller's side, from an
/// argument that was honoured — and the caller then acts on an answer to a question it did not ask.
///
/// SDD paid for this once: `init --project-root /tmp/b` silently seeded the CURRENT directory and
/// reported success. The parser had simply not been taught the flag, so it dropped it and carried on
/// confidently. Same shape as a gate reporting green over a subject it never read (#266) — one layer
/// down, in argv.
module OptionsTests =

    let private ok =
        function
        | Ok o -> o
        | Error(e: string) -> failwithf "expected the args to parse, got: %s" e

    let private rejected =
        function
        | Ok(o: Options) -> failwithf "expected the args to be REJECTED, but they parsed to %A" o
        | Error e -> e

    [<Fact>]
    let ``an unknown flag is NAMED and refused, never shrugged off`` () =
        let e = parse [ "decide"; "--engine=fs" ] |> rejected
        Assert.Contains("--engine=fs", e)

    [<Fact>]
    let ``an unknown command is named and refused`` () =
        let e = parse [ "schedule" ] |> rejected
        Assert.Contains("schedule", e)

    [<Fact>]
    let ``a flag given without its value does NOT swallow the next flag`` () =
        // The subtle one. `--snapshot --json` must be "you forgot the filename", not "the snapshot
        // lives in a file called --json" — and certainly not a silently-unset option plus a silently
        // consumed one.
        let e = parse [ "decide"; "--snapshot"; "--json" ] |> rejected
        Assert.Contains("--snapshot", e)

    [<Fact>]
    let ``a trailing flag with no value at all is refused`` () =
        let e = parse [ "decide"; "--snapshot" ] |> rejected
        Assert.Contains("--snapshot", e)

    [<Fact>]
    let ``JSON is the default projection — it is the contract, and the client parses it`` () =
        Assert.Equal(Json, (parse [ "decide" ] |> ok).Render)

    [<Fact>]
    let ``the text projection is opt-in`` () =
        Assert.Equal(Text, (parse [ "decide"; "--text" ] |> ok).Render)

    [<Fact>]
    let ``a snapshot file is accepted for debugging`` () =
        Assert.Equal(Some "/tmp/s.json", (parse [ "decide"; "--snapshot"; "/tmp/s.json" ] |> ok).SnapshotFile)

    [<Fact>]
    let ``no arguments prints help rather than deciding over an empty board`` () =
        Assert.Equal(Help, (parse [] |> ok).Command)

    [<Fact>]
    let ``ready --status carries the column name`` () =
        Assert.Equal(Some "Done", (parse [ "ready"; "--status"; "Done" ] |> ok).Status)

    [<Fact>]
    let ``ready --status without a value is refused, not left silently unset`` () =
        // The residue rule again: a `--status` that swallowed nothing would filter on `None` and quietly
        // show the not-Done default, answering a question the caller did not ask.
        let e = parse [ "ready"; "--status" ] |> rejected
        Assert.Contains("--status", e)

    [<Fact>]
    let ``ready --status does NOT swallow the following flag as its value`` () =
        let e = parse [ "ready"; "--status"; "--json" ] |> rejected
        Assert.Contains("--status", e)

    [<Fact>]
    let ``release --status carries the column name`` () =
        // #867: the flag PARSED all along — it was `release` that never read it. This asserts the half the
        // parser owns; `Client`'s precedence is asserted at the HTTP layer by the e2e restore fixture.
        Assert.Equal(Some "Blocked", (parse [ "release"; ".github#574"; "--status"; "Blocked" ] |> ok).Status)

    [<Fact>]
    let ``release --status without a value is refused, not left silently unset`` () =
        let e = parse [ "release"; ".github#574"; "--status" ] |> rejected
        Assert.Contains("--status", e)

    [<Fact>]
    let ``--status is REFUSED by a command that does not implement it, never swallowed`` () =
        // #867's silence had two mechanisms, and this is the one that made it survive: `--status` is a GLOBAL
        // parser flag, so `unknown argument` — the refusal that catches every other typo instantly — never
        // fired. Every command accepted it, and all but `ready` ignored it. A flag accepted and ignored tells
        // the caller, via a green exit, that something happened which did not.
        for verb in [ "claim"; "take"; "heartbeat"; "done"; "widen"; "who"; "next"; "reap" ] do
            let e = parse [ verb; "--status"; "Blocked" ] |> rejected
            Assert.Contains("--status", e)

    [<Fact>]
    let ``--status stays accepted by the two commands that DO read it`` () =
        // The mirror of the refusal: a gate that also refused the real users would just move the defect.
        Assert.Equal(Some "Done", (parse [ "ready"; "--status"; "Done" ] |> ok).Status)
        Assert.Equal(Some "Blocked", (parse [ "release"; ".github#574"; "--status"; "Blocked" ] |> ok).Status)

    [<Fact>]
    let ``ready --all is a boolean widen with no value`` () =
        Assert.True((parse [ "ready"; "--all" ] |> ok).All)

    [<Fact>]
    let ``reconcile is a text dry-run by default and apply is explicit`` () =
        let dry = parse [ "reconcile"; "--repo"; ".github" ] |> ok
        Assert.Equal(Reconcile, dry.Command)
        Assert.Equal(Text, dry.Render)
        Assert.False(dry.Apply)

        let apply = parse [ "reconcile"; "--apply" ] |> ok
        Assert.True(apply.Apply)
        Assert.Equal(Text, apply.Render)

        // .github#1541 — THE MUTATING FORM HAS A MACHINE PROJECTION. #1429 refused this pair, which
        // deleted the machine projection of the one verb that WRITES to the board: a caller applying a
        // reconciliation could not learn which writes landed and which QUEUED against an exhausted budget
        // without scraping prose. #1541 restored it. Asserted as a parsed `Options`, not merely as "not an
        // error", because both fields have to survive the funnel together.
        let mixed = parse [ "reconcile"; "--apply"; "--json" ] |> ok
        Assert.Equal(Reconcile, mixed.Command)
        Assert.True(mixed.Apply)
        Assert.Equal(Json, mixed.Render)

    // ================================================================================================
    // #991 — THE RESIDUE RULE, GENERALISED. The rule was always general; its enforcement was one arm.
    // ================================================================================================
    // #867 added the `--status` guard by hand and every other flag stayed unguarded — 38 flags, one of
    // them checked. These pin the table BOTH ways, because a guard that also refused the real users would
    // just move the defect somewhere quieter.

    /// (flag argv, a command that READS it, a command that does NOT)
    let private surface =
        [ [ "--mint" ], "whoami", "ready"
          [ "--all" ], "ready", "next"
          [ "--active" ], "overlap", "who"
          [ "--apply" ], "reap", "batch"
          [ "--peek" ], "inbox", "who"
          [ "--local" ], "who", "next"
          [ "--all-repos" ], "who", "next"
          [ "--dry-run" ], "flush", "reap"
          [ "--strict" ], "lint", "ready"
          [ "--batch" ], "set-field", "widen"
          [ "--flip" ], "done", "claim"
          [ "--force" ], "claim", "release"
          [ "--include-backlog" ], "take", "who"
          [ "--explain" ], "batch", "take"
          [ "--fresh" ], "scan", "batch"
          [ "--wait" ], "landable", "next"
          [ "--paths"; "src/A.fs" ], "widen", "claim"
          [ "--to"; "w-x" ], "say", "inbox"
          [ "--evidence"; "x" ], "done", "claim"
          [ "--partial"; "x" ], "done", "widen"
          [ "--issue"; ".github#1" ], "verify-paths", "done"
          [ "--sha"; "abc" ], "landable", "take"
          [ "--require"; "ci" ], "landable", "who"
          [ "--tries"; "3" ], "landable", "next"
          [ "--interval"; "5" ], "landable", "next"
          [ "--label"; "bug" ], "issues", "ready"
          [ "--state"; "all" ], "issues", "ready"
          [ "-n"; "5" ], "scan", "next"
          [ "--status"; "Blocked" ], "release", "claim" ]

    [<Fact>]
    let ``#991 every flag is REFUSED by a command that does not read it`` () =
        for (flag, _reader, nonReader) in surface do
            let e = parse (nonReader :: flag) |> rejected

            Assert.True(
                e.Contains(List.head flag),
                $"`%s{nonReader} %s{List.head flag}` must be refused BY NAME, got: %s{e}"
            )

    [<Fact>]
    let ``#991 every flag stays accepted by a command that DOES read it`` () =
        // The half that makes the table honest rather than merely strict.
        for (flag, reader, _) in surface do
            match parse (reader :: flag) with
            | Ok _ -> ()
            | Error e -> failwithf "`%s %s` READS this flag and must still parse, got: %s" reader (List.head flag) e

    [<Fact>]
    let ``#991 the refusal names the commands that DO read the flag`` () =
        // A refusal that only says no sends the caller looking. #867's message named `ready`/`release`;
        // every flag's now does, because the table knows the readers by construction.
        let e = parse [ "ready"; "--flip" ] |> rejected
        Assert.Contains("--flip", e)
        Assert.Contains("`done`", e)
        Assert.Contains("`ready`", e)

    [<Fact>]
    let ``#991 release --force was a DOCUMENTED no-op — the usage advertised it and nothing read it`` () =
        // The find that came out of building the table, and #867's exact defect in the very command #867
        // repaired: `release <ref> [--worker W] [--force]` sat in the usage block for the life of the port
        // while `release` never consulted `opts.Force`. `claim` is the only reader (it bypasses the #516
        // one-item-per-worker check). Refusing it breaks no working behaviour — there was none to break.
        let e = parse [ "release"; ".github#574"; "--force" ] |> rejected
        Assert.Contains("--force", e)
        Assert.Contains("`claim`", e)

        Assert.True((parse [ "claim"; ".github#574"; "--force" ] |> ok).Force)

    [<Fact>]
    let ``#991 --lease has a non-optional default and no record of the act, so it stays Global`` () =
        // WHY THIS TEST WAS RENAMED (#1523). It used to say `--json`/`--text` were Global for the same
        // reason `--lease` is, and that every command kept taking them. Both halves are now false: the
        // render flags carry a `RenderGiven` record of having been GIVEN, which is exactly what "given
        // and defaulted are the same state" claimed was impossible, and they are scoped off `Global` on
        // the strength of it. `--lease` is the one that still has no such record — it lands in
        // `LeaseMinutes`, nothing observes the act, and so there is genuinely nothing here to refuse.
        // That is the same defect one flag along, and it is filed rather than guessed at.
        Assert.Equal(Json, (parse [ "who"; "--json" ] |> ok).Render)
        Assert.Equal(Text, (parse [ "lint"; "--text" ] |> ok).Render)
        Assert.Equal(30, (parse [ "take"; "--lease"; "30" ] |> ok).LeaseMinutes)
        Assert.Equal(Some "FS.GG.SDD", (parse [ "next"; "--repo"; "sdd" ] |> ok).Repo)

    [<Fact>]
    let ``#1369 claim and take keep text defaults while json opts into typed receipts`` () =
        Assert.Equal(Text, (parse [ "claim"; ".github#1369" ] |> ok).Render)
        Assert.Equal(Json, (parse [ "claim"; ".github#1369"; "--json" ] |> ok).Render)
        Assert.Equal(Text, (parse [ "take"; "--repo"; ".github" ] |> ok).Render)
        Assert.Equal(Json, (parse [ "take"; "--repo"; ".github"; "--json" ] |> ok).Render)

    [<Fact>]
    let ``#1369 who all-repos is explicit and cannot be combined with a repo slice`` () =
        Assert.True((parse [ "who"; "--all-repos" ] |> ok).AllRepos)
        let e = parse [ "who"; "--repo"; "sdd"; "--all-repos" ] |> rejected
        Assert.Contains("mutually exclusive", e)
        Assert.StartsWith("who:", e)

    [<Fact>]
    let ``.github#1541 the all-repos refusal names the command it refused, not the word who`` () =
        // THE ARM BEHIND THE DELETED GUARD. `--all-repos` is `Only [ Who ]`, so this combination check is
        // the one refusal a verb it does not describe can reach — and it runs BEFORE the residue rule, so
        // it shadows the "not a flag of `reconcile`" sentence that names the real culprit. While #1429's
        // `--apply --json` arm sat ahead of it, this line was answered first and the shadowing was
        // invisible; #1541 removed that arm, so a `reconcile` command line now reaches it. Hardcoding one
        // verb's name into a refusal every verb can trigger sends the reader to audit the wrong command.
        let e = parse [ "reconcile"; "--apply"; "--json"; "--all-repos"; "--repo"; "FS.GG.SDD" ] |> rejected
        Assert.StartsWith("reconcile:", e)
        Assert.Contains("mutually exclusive", e)

        // ...and the legal pair on its own is still ACCEPTED — this arm refuses the `--all-repos` slice,
        // not the machine projection #1541 restored.
        Assert.Equal(Json, (parse [ "reconcile"; "--apply"; "--json" ] |> ok).Render)

    [<Fact>]
    let ``#636 take --include-backlog is READ, not merely tolerated`` () =
        // THE USAGE BLOCK IS A PRESCRIBING SITE, and it is the one #919's gate cannot see: that gate scans the
        // corpus (`docs/coordination`, `.claude/skills`), so the engine's own `--help` — the text a worker reads
        // at the moment the tool refuses them — is checked by nothing.
        //
        // `take --include-backlog` has always worked, purely because `--include-backlog` is a GLOBAL parser flag
        // and `take` threads `opts` into `scanAndDecide`. Nothing pinned either half. #636 documented the flag on
        // `take`; this is what keeps that line from becoming the next `release --status` — a flag the usage
        // advertises, the parser accepts, and the command drops on the floor.
        Assert.True((parse [ "take"; "--include-backlog" ] |> ok).AllowBacklog)

        // The reads that were already true, pinned beside it: one flag, one meaning, across the three verbs
        // whose usage advertises it.
        Assert.True((parse [ "batch"; "--include-backlog" ] |> ok).AllowBacklog)
        Assert.True((parse [ "scan"; "--include-backlog" ] |> ok).AllowBacklog)

        // And the default is Ready-only — the premise the banner's "passed over AT THE COLUMN" rests on.
        Assert.False((parse [ "take" ] |> ok).AllowBacklog)

    [<Fact>]
    let ``ready defaults leave the not-Done filter on — no status, not --all`` () =
        let o = parse [ "ready" ] |> ok
        Assert.Equal(None, o.Status)
        Assert.False(o.All)

    [<Fact>]
    let ``set-field --batch is a boolean flag; the Field=Value pairs stay in Args`` () =
        // #448: `--batch` opts the remaining args into the aliased-mutation path. A `Field=Value` pair
        // begins with a field name (not `-`), so it is an ordinary Arg — the ref first, then the pairs.
        let o = parse [ "set-field"; "--batch"; "FS.GG.SDD#42"; "Phase=P2 SDD"; "Target=2026-08-01" ] |> ok
        Assert.True(o.Batch)
        Assert.Equal<string list>([ "FS.GG.SDD#42"; "Phase=P2 SDD"; "Target=2026-08-01" ], o.Args)

    [<Fact>]
    let ``set-field without --batch leaves Batch off`` () =
        Assert.False((parse [ "set-field"; "FS.GG.SDD#42"; "Phase"; "P2 SDD" ] |> ok).Batch)

    [<Fact>]
    let ``verify-paths --issue carries the named issue ref`` () =
        // #479: `--issue` names the issue the PR implements explicitly. Its VALUE (an issue ref) is not
        // parsed here — that is verifyPaths' job — but it must be captured, not dropped.
        let o = parse [ "verify-paths"; "--pr"; "7"; "--repo"; "sdd"; "--issue"; "FS-GG/FS.GG.SDD#70" ] |> ok
        Assert.Equal(Some "FS-GG/FS.GG.SDD#70", o.Issue)
        Assert.Equal(Some 7, o.Pr)

    [<Fact>]
    let ``verify-paths without --issue leaves Issue unset`` () =
        Assert.Equal(None, (parse [ "verify-paths"; "--pr"; "7"; "--repo"; "sdd" ] |> ok).Issue)

    [<Fact>]
    let ``verify-paths --issue without a value is refused, not left silently unset`` () =
        // The residue rule (as for --status/--snapshot): a `--issue` that swallowed the next flag would
        // resolve a straddle against a ref named `--warn`. It must be "you forgot the issue".
        let e = parse [ "verify-paths"; "--pr"; "7"; "--issue"; "--warn" ] |> rejected
        Assert.Contains("--issue", e)

    // ---- the plumbing commands (#418 cache, case 10) ----------------------------------------------

    [<Fact>]
    let ``the board-map plumbing commands parse to their own commands`` () =
        Assert.Equal(Bootstrap, (parse [ "bootstrap" ] |> ok).Command)
        Assert.Equal(BoardCmd, (parse [ "board" ] |> ok).Command)
        Assert.Equal(FieldId, (parse [ "field-id"; "Phase" ] |> ok).Command)
        Assert.Equal(OptionId, (parse [ "option-id"; "Phase"; "P2 SDD" ] |> ok).Command)
        Assert.Equal(ItemId, (parse [ "item-id"; "FS.GG.SDD#42" ] |> ok).Command)

    [<Fact>]
    let ``field-id / option-id / item-id carry their operands as Args`` () =
        Assert.Equal<string list>([ "Phase" ], (parse [ "field-id"; "Phase" ] |> ok).Args)
        Assert.Equal<string list>([ "Phase"; "P2 SDD" ], (parse [ "option-id"; "Phase"; "P2 SDD" ] |> ok).Args)
        Assert.Equal<string list>([ "FS.GG.SDD#42" ], (parse [ "item-id"; "FS.GG.SDD#42" ] |> ok).Args)

    [<Fact>]
    let ``bootstrap --refresh sets the fresh flag - the drop-the-day-cache remedy`` () =
        Assert.True((parse [ "bootstrap"; "--refresh" ] |> ok).Fresh)
        Assert.False((parse [ "bootstrap" ] |> ok).Fresh)

    // ---- lint (the board-health gate, #496) -------------------------------------------------------

    [<Fact>]
    let ``lint parses to its command and defaults to the text projection`` () =
        let o = parse [ "lint" ] |> ok
        Assert.Equal(LintCmd, o.Command)
        Assert.Equal(Text, o.Render)   // FSGG-LINT lines by default; --json opts into the array

    [<Fact>]
    let ``lint --json / --repo / --strict are all captured`` () =
        let o = parse [ "lint"; "--repo"; "sdd"; "--json"; "--strict" ] |> ok
        // RESOLVED, not the raw token (#962): the parser owns `--repo`'s meaning, so every verb gets the
        // repo NAME board rows carry. This asserted `Some "sdd"` for as long as resolution was a per-verb
        // opt-in downstream — which is exactly what let `ready` be left out of it.
        Assert.Equal(Some "FS.GG.SDD", o.Repo)
        Assert.Equal(Json, o.Render)
        Assert.True(o.Strict)

    [<Fact>]
    let ``--strict is off by default - a note is advisory unless asked otherwise`` () =
        Assert.False((parse [ "lint" ] |> ok).Strict)

    // ---- overlap (the #353 repo-scoped touch-set diagnostic) --------------------------------------

    [<Fact>]
    let ``overlap parses to its command and defaults to the text projection`` () =
        let o = parse [ "overlap"; "FS.GG.SDD#401"; "--active" ] |> ok
        Assert.Equal(Overlap, o.Command)
        Assert.Equal(Text, o.Render)
        Assert.True(o.Active)
        Assert.Equal<string list>([ "FS.GG.SDD#401" ], o.Args)

    [<Fact>]
    let ``overlap takes two positional refs for the pairwise form`` () =
        let o = parse [ "overlap"; "FS.GG.SDD#401"; "FS-GG/FS.GG.Rendering#402" ] |> ok
        Assert.Equal(Overlap, o.Command)
        Assert.False(o.Active)
        Assert.Equal<string list>([ "FS.GG.SDD#401"; "FS-GG/FS.GG.Rendering#402" ], o.Args)

    [<Fact>]
    let ``--active is off by default`` () =
        Assert.False((parse [ "overlap"; "FS.GG.SDD#401"; "FS.GG.SDD#403" ] |> ok).Active)

    // ---- who --local (#959: the flag bash shipped and the port dropped) ---------------------------
    [<Fact>]
    let ``who --local parses and sets Local`` () =
        Assert.True((parse [ "who"; "--repo"; ".github"; "--local" ] |> ok).Local)

    [<Fact>]
    let ``--local is off by default`` () =
        Assert.False((parse [ "who"; "--repo"; ".github" ] |> ok).Local)

    [<Fact>]
    let ``who --local composes with --json`` () =
        let o = parse [ "who"; "--local"; "--json" ] |> ok
        Assert.True(o.Local)
        Assert.Equal(Json, o.Render)

    // ---- adopt (the #697 land-the-orphan command) -------------------------------------------------

    [<Fact>]
    let ``adopt parses to its command, defaults to text, and carries the ref`` () =
        let o = parse [ "adopt"; "FS.GG.SDD#970"; "--worker"; "heron-697" ] |> ok
        Assert.Equal(Adopt, o.Command)
        Assert.Equal(Text, o.Render)
        Assert.Equal<string list>([ "FS.GG.SDD#970" ], o.Args)
        Assert.Equal(Some "heron-697", o.Worker)

    // ---- landable (the #697/#720 verdict as a first-class query) -----------------------------------

    [<Fact>]
    let ``landable parses to its command and carries the PR arg and repo`` () =
        // A QUERY, not a table — no Render flip (the verdict is one word on stdout, the decision in the exit
        // code). The PR is a positional arg (`landable 801`), the repo an explicit --repo.
        let o = parse [ "landable"; "801"; "--repo"; "FS-GG/FS.GG.SDD" ] |> ok
        Assert.Equal(Landable, o.Command)
        Assert.Equal<string list>([ "801" ], o.Args)
        // The owner is dropped and the repo part kept (#962) — `owner/repo` is one of the three documented
        // `--repo` spellings, and all three reduce to the name board rows carry.
        Assert.Equal(Some "FS.GG.SDD", o.Repo)
        // Without --wait, the poll knobs are unset (the single-shot verdict).
        Assert.False(o.Wait)
        Assert.Equal(None, o.Tries)
        Assert.Equal(None, o.Interval)
        // ...and no caller assertions: an empty --require and no --sha score exactly as before (#737).
        Assert.Empty(o.Require)
        Assert.Equal(None, o.Sha)

    [<Fact>]
    let ``landable --wait carries the poll knobs, and --interval permits 0 (#724)`` () =
        let o =
            parse [ "landable"; "810"; "--repo"; "FS.GG.SDD"; "--wait"; "--tries"; "4"; "--interval"; "0" ]
            |> ok
        Assert.True(o.Wait)
        Assert.Equal(Some 4, o.Tries)
        // A delay, not a count — 0 is meaningful (the test harness drives the poll with no wall-clock).
        Assert.Equal(Some 0, o.Interval)

    [<Fact>]
    let ``landable --tries must be positive`` () =
        let e = parse [ "landable"; "801"; "--repo"; "R"; "--wait"; "--tries"; "0" ] |> rejected
        Assert.Contains("--tries", e)

    [<Fact>]
    let ``landable --interval refuses a negative delay`` () =
        let e = parse [ "landable"; "801"; "--repo"; "R"; "--wait"; "--interval"; "-3" ] |> rejected
        Assert.Contains("--interval", e)

    [<Fact>]
    let ``landable --require is REPEATABLE and APPENDS — a set is not its last element (#737)`` () =
        // Last-wins would silently drop a required check, which is the fail-open direction the flag exists
        // to close. Order is preserved so the diagnostic names them as the caller wrote them.
        let o =
            parse [ "landable"; "9"; "--repo"; "R"; "--require"; "registry-coherence"; "--require"; "drift" ]
            |> ok

        Assert.Equal<string list>([ "registry-coherence"; "drift" ], o.Require)

    [<Fact>]
    let ``landable --require refuses an empty check name`` () =
        // An empty name matches no check, so it could only ever hold the PR pending forever. A typo, not a
        // requirement.
        let e = parse [ "landable"; "9"; "--repo"; "R"; "--require"; "" ] |> rejected
        Assert.Contains("--require", e)

    [<Fact>]
    let ``landable --require refuses a bare flag at the end, and will not eat the next flag`` () =
        Assert.Contains("--require", parse [ "landable"; "9"; "--repo"; "R"; "--require" ] |> rejected)
        // `--require --wait` must not silently require a check called "--wait".
        Assert.Contains("--require", parse [ "landable"; "9"; "--repo"; "R"; "--require"; "--wait" ] |> rejected)

    [<Fact>]
    let ``landable --sha carries the head the caller MEANS to gate (#737)`` () =
        let o = parse [ "landable"; "9"; "--repo"; "R"; "--sha"; "deadbeef" ] |> ok
        Assert.Equal(Some "deadbeef", o.Sha)

    [<Fact>]
    let ``landable --sha refuses an empty or missing value`` () =
        Assert.Contains("--sha", parse [ "landable"; "9"; "--repo"; "R"; "--sha"; "" ] |> rejected)
        Assert.Contains("--sha", parse [ "landable"; "9"; "--repo"; "R"; "--sha" ] |> rejected)

    // ---- issues: the ETag-revalidated REST list (#446) --------------------------------------------

    [<Fact>]
    let ``issues takes a repo positional and defaults its state and label`` () =
        let o = parse [ "issues"; "sdd" ] |> ok
        Assert.Equal(Issues, o.Command)
        Assert.Equal<string list>([ "sdd" ], o.Args)
        // No --state / --label ⇒ None (the command applies bash's `open` default itself).
        Assert.Equal(None, o.IssueState)
        Assert.Equal(None, o.Label)
        // `issues` emits the raw JSON array — the default projection, so the caller jq's it.
        Assert.Equal(Json, o.Render)

    [<Fact>]
    let ``issues --state and --label are carried through`` () =
        let o = parse [ "issues"; "FS-GG/FS.GG.Game"; "--state"; "closed"; "--label"; "bug" ] |> ok
        Assert.Equal(Some "closed", o.IssueState)
        Assert.Equal(Some "bug", o.Label)

    [<Fact>]
    let ``issues --state refuses a value that is not open, closed, or all`` () =
        let e = parse [ "issues"; "sdd"; "--state"; "reopened" ] |> rejected
        Assert.Contains("--state", e)

    [<Fact>]
    let ``issues --refresh drops the cache (an alias of the fresh flag)`` () =
        Assert.True((parse [ "issues"; "sdd"; "--refresh" ] |> ok).Fresh)

    [<Fact>]
    let ``#614 done --flip --partial captures the reason and keeps --flip`` () =
        let o = parse [ "done"; "FS.GG.SDD#62"; "--flip"; "--partial"; "callers migration is a separate child" ] |> ok
        Assert.True(o.Flip)
        Assert.Equal(Some "callers migration is a separate child", o.Partial)

    [<Fact>]
    let ``#614 a bare done --flip leaves Partial None — the child completes its parent by default`` () =
        let o = parse [ "done"; "FS.GG.SDD#62"; "--flip" ] |> ok
        Assert.Equal(None, o.Partial)

    [<Fact>]
    let ``#614 --partial with no value is rejected — a partial fix must SAY why`` () =
        let e = parse [ "done"; "FS.GG.SDD#62"; "--flip"; "--partial" ] |> rejected
        Assert.Contains("--partial", e)

    // ---- choreLockRef: FS-GG's table stays gated; an env-injected roster is universal (.github#1140) ----
    // The last FS-GG hard-wire. `extra` is the per-deployment roster a VENDORED tenant injects by env
    // (`FSGG_COORD_CHORE_LOCKS`), so `pnext-item`/`check-board` can run `offer` on a non-FS-GG board — while
    // the embedded FS-GG numbers are still never handed to another owner (#1087's invariant, kept).

    let private mkRef (owner: string) (repo: string) (n: int) : FS.GG.Coord.Types.Ref =
        { FS.GG.Coord.Types.Owner = owner
          FS.GG.Coord.Types.Repo = repo
          FS.GG.Coord.Types.Number = n }

    [<Fact>]
    let ``choreLockRef with no injected roster resolves the embedded FS-GG lock, unchanged`` () =
        Assert.Equal(Some(mkRef "FS-GG" ".github" 1033), choreLockRef [] "FS-GG" ".github")
        // the short id is canonicalised on the way out, exactly as before
        Assert.Equal(Some(mkRef "FS-GG" "FS.GG.SDD" 518), choreLockRef [] "FS-GG" "sdd")

    [<Fact>]
    let ``choreLockRef stays fail-closed for a non-FS-GG owner with no injected roster`` () =
        // #1087's invariant: FS-GG's issue numbers are never handed to another owner.
        Assert.Equal(None, choreLockRef [] "acme" "Product.X")
        Assert.Equal(None, choreLockRef [] "acme" ".github")

    [<Fact>]
    let ``choreLockRef resolves an env-injected lock for a vendored tenant under its OWN owner`` () =
        let extra = [ mkRef "acme" "Product.X" 42 ]
        Assert.Equal(Some(mkRef "acme" "Product.X" 42), choreLockRef extra "acme" "Product.X")
        // nothing for a repo the roster does not name, and no leak of the FS-GG table to `acme`
        Assert.Equal(None, choreLockRef extra "acme" "Product.Y")
        Assert.Equal(None, choreLockRef extra "acme" ".github")

    [<Fact>]
    let ``an injected lock is consulted FIRST — a deployment can repoint one without a code change`` () =
        let extra = [ mkRef "FS-GG" ".github" 9999 ]
        Assert.Equal(Some(mkRef "FS-GG" ".github" 9999), choreLockRef extra "FS-GG" ".github")
        // a repo the override does not name still falls through to the embedded table
        Assert.Equal(Some(mkRef "FS-GG" "FS.GG.SDD" 518), choreLockRef extra "FS-GG" "sdd")

    [<Fact>]
    let ``choreLockRef matches owner and repo case-insensitively`` () =
        let extra = [ mkRef "Acme" "Product.X" 42 ]
        Assert.Equal(Some(mkRef "Acme" "Product.X" 42), choreLockRef extra "acme" "product.x")

    [<Fact>]
    let ``parseChoreLocks reads a comma-separated roster, canonicalises the repo, and DROPS junk`` () =
        // A malformed token degrades to the fail-closed default (a chore not offered), never a throw that
        // would take down the caller's real command — the same answer an absent lock already gives.
        let got = Client.parseChoreLocks "acme/Product.X#42, FS-GG/sdd#7 , garbage, /bad#1, a/b#x"
        Assert.Equal<FS.GG.Coord.Types.Ref list>(
            // `FS-GG/sdd` is canonicalised to `FS.GG.SDD` on the way in, so the stored ref is CAS-comparable.
            [ mkRef "acme" "Product.X" 42; mkRef "FS-GG" "FS.GG.SDD" 7 ],
            got
        )
        // an unset / empty env is an empty roster, not an error
        Assert.Equal<FS.GG.Coord.Types.Ref list>([], Client.parseChoreLocks "")

    // ---- `room open` (ADR-0051, #1215) ----------------------------------------------------------------

    [<Fact>]
    let ``room open --over parses the two-word verb and its comma list`` () =
        let o = parse [ "room"; "open"; "--over"; "12,13" ] |> ok
        Assert.Equal(RoomOpen, o.Command)
        Assert.Equal<string list>([ "12"; "13" ], o.Over)

    [<Fact>]
    let ``--over trims around commas and drops empties`` () =
        // `--over 12, 13,` is one honest list of two, not three-with-a-blank.
        let o = parse [ "room"; "open"; "--over"; "12, 13," ] |> ok
        Assert.Equal<string list>([ "12"; "13" ], o.Over)

    [<Fact>]
    let ``an --over with no real ref is refused`` () =
        let e = parse [ "room"; "open"; "--over"; "," ] |> rejected
        Assert.Contains("--over", e)

    [<Fact>]
    let ``an unknown room subcommand is NAMED and refused`` () =
        let e = parse [ "room"; "close" ] |> rejected
        Assert.Contains("close", e)

    [<Fact>]
    let ``a bare room needs a subcommand`` () =
        let e = parse [ "room" ] |> rejected
        Assert.Contains("subcommand", e)

    [<Fact>]
    let ``--over is refused on a command that does not read it (the residue rule)`` () =
        // `FOver -> Only [ RoomOpen ]`: any other verb accepting `--over` would swallow it silently.
        let e = parse [ "claim"; "1"; "--over"; "2" ] |> rejected
        Assert.Contains("--over", e)

    // ================================================================================================
    // #1507 — `--paths` SWALLOWED A FOLLOWING FLAG INTO THE TOUCH-SET.
    // ================================================================================================
    // `widen FS.GG.Governance#326 --paths <five real paths> --json` declared SIX paths, the sixth being
    // `--json`, and exited 0 under a receipt whose `DISJOINT` line read like success.
    //
    // This is the residue rule at the top of this file arriving in the one argument where it costs the
    // most. `--snapshot --json` has been "you forgot the filename" since forever; `--paths` was the one
    // arm that opted out of the guard every other multi-token flag in the parser already had — and it
    // opted out for the value that lands in the declaration the SCHEDULER reserves files by.
    //
    // It was also not an unknown flag being tolerated: `--json` is in `widen`'s own advertised surface
    // (`command-contract` prints it). A DECLARED flag of the command was parsed as a value of the
    // preceding one.

    /// Both `--paths`-taking commands. The fix is one parser arm, so a test that only exercised `widen`
    /// would pass for `set-paths` by luck rather than by construction — and `set-paths` is the recovery
    /// command, the one a worker reaches for while cleaning up exactly this corruption.
    let private pathsVerbs = [ "widen"; "set-paths" ]

    [<Fact>]
    let ``#1507 --paths LAST does not swallow the trailing flag`` () =
        for verb in pathsVerbs do
            let o = parse [ verb; ".github#1507"; "--paths"; "src/A.fs"; "src/B/"; "--json" ] |> ok
            Assert.Equal<string list>([ "src/A.fs"; "src/B/" ], o.Paths)

            // The other half of acceptance criterion 1: the flag is not merely absent from the touch-set,
            // it is HONOURED. Dropping it silently would trade one ignored argument for another.
            //
            // ASSERT THAT WITH THE SPELLING THAT IS NOT THE DEFAULT, WHICHEVER ONE THAT IS. This leg was
            // written against `--text` because both verbs then defaulted to `Render = Json`, so asserting
            // `Json` after a trailing `--json` would have passed whether the flag was read or thrown away
            // — a vacuous assertion of exactly the kind #266 is about, sitting inside the regression test
            // for a flag that was being thrown away.
            //
            // #1517 INVERTED THE PREMISE and this leg had to move with it. Honouring `--json` in the
            // renderer meant pinning `Render = Text` on both parse arms (the module `defaults` are `Json`,
            // and the BARE `widen` is the form every recipe runs), so `Text` is now the default and
            // `--text` is the spelling that proves nothing. `--json` is. The rule this leg encodes is not
            // "use `--text`" — it is "assert the flag whose effect differs from the default", and the
            // default is a thing that moves.
            Assert.Equal(Text, (parse [ verb; ".github#1507"; "--paths"; "src/A.fs" ] |> ok).Render)

            let t = parse [ verb; ".github#1507"; "--paths"; "src/A.fs"; "src/B/"; "--json" ] |> ok
            Assert.Equal<string list>([ "src/A.fs"; "src/B/" ], t.Paths)
            Assert.Equal(Json, t.Render)

    [<Fact>]
    let ``#1507 --paths MID-ARGLIST keeps parsing the flags after it`` () =
        // The order the recipes actually document is flags-then-paths, which is why the bug survived so
        // long: `widen --json --paths ...` was always fine. Both orders must now mean the same thing.
        for verb in pathsVerbs do
            let after =
                parse [ verb; ".github#1507"; "--paths"; "src/A.fs"; "--worker"; "w-1"; "--lease"; "30" ]
                |> ok

            Assert.Equal<string list>([ "src/A.fs" ], after.Paths)
            Assert.Equal(Some "w-1", after.Worker)
            Assert.Equal(30, after.LeaseMinutes)

            let before =
                parse [ verb; ".github#1507"; "--worker"; "w-1"; "--lease"; "30"; "--paths"; "src/A.fs" ]
                |> ok

            Assert.Equal<string list>(after.Paths, before.Paths)
            Assert.Equal(after.Worker, before.Worker)
            Assert.Equal(after.LeaseMinutes, before.LeaseMinutes)

    [<Fact>]
    let ``#1507 the positional ref survives a --paths that no longer eats the rest of argv`` () =
        // `--paths` used to recurse on `[]`, discarding everything after it — positionals included. The ref
        // is what `widen` resolves the claim by, so losing it is not a cosmetic difference.
        for verb in pathsVerbs do
            let o = parse [ verb; ".github#1507"; "--paths"; "src/A.fs"; "--json" ] |> ok
            Assert.Equal<string list>([ ".github#1507" ], o.Args)

    [<Fact>]
    let ``#1507 --paths with nothing but a flag after it is REFUSED, not silently satisfied`` () =
        // Acceptance criterion 3. The dangerous near-miss: with the stop rule in place but no empty check,
        // `--paths --json` would parse to an EMPTY touch-set and a green exit — the same silence one step
        // along. The refusal names the flag, so the caller sees what they typed.
        for verb in pathsVerbs do
            let e = parse [ verb; ".github#1507"; "--paths"; "--json" ] |> rejected
            Assert.Contains("--paths", e)
            Assert.Contains("--json", e)

    [<Fact>]
    let ``#1507 a trailing --paths with no tokens at all is still refused`` () =
        for verb in pathsVerbs do
            let e = parse [ verb; ".github#1507"; "--paths" ] |> rejected
            Assert.Contains("--paths", e)

    [<Fact>]
    let ``#1507 a flag-shaped token after --paths is never DECLARED, whatever flag it is`` () =
        // THE DRIFT ARGUMENT, pinned. The stop rule asks `TouchSet.isFlagShaped` — the grammar — not a copy
        // of the flag table, so it holds for flags this test was never taught:
        //
        //  * `--json`  a Global flag `widen` advertises  -> parsed as the flag
        //  * `--status` a flag of OTHER commands         -> refused by the #991 residue rule, BY NAME
        //  * `--nonesuch` no flag at all                 -> refused as `unknown argument`
        //
        // All three are loud. None of them ends up in a `Paths:` line, which is the only property that
        // matters, and none of them required this arm to know the flag's name.
        let residue = parse [ "widen"; ".github#1507"; "--paths"; "src/A.fs"; "--status"; "Blocked" ] |> rejected
        Assert.Contains("--status", residue)

        let unknown = parse [ "widen"; ".github#1507"; "--paths"; "src/A.fs"; "--nonesuch" ] |> rejected
        Assert.Contains("--nonesuch", unknown)

    [<Fact>]
    let ``#1507 a multi-token touch-set is still exactly what was typed`` () =
        // The mirror test. A stop rule that also truncated honest declarations would be a worse bug than
        // the one it replaced: silently reserving FEWER files than the worker asked for.
        let o =
            parse
                [ "widen"
                  ".github#1507"
                  "--paths"
                  ".claude/skills/"
                  ".codex/skills/"
                  ".agents/skills/"
                  ".github/workflows/skill-union.yml"
                  "scripts/materialize-skill-roots.sh" ]
            |> ok

        Assert.Equal<string list>(
            [ ".claude/skills/"
              ".codex/skills/"
              ".agents/skills/"
              ".github/workflows/skill-union.yml"
              "scripts/materialize-skill-roots.sh" ],
            o.Paths
        )

    // ---- #1523 — `--json` is HONOURED or REFUSED, and there is no third state ----------------------
    //
    // `--json` was `Global` in `scopeOf`, so `command-contract` advertised it on all 40 commands and the
    // #991 residue rule refused it nowhere. Fourteen commands branched on `opts.Render`; four printed a
    // machine document regardless; the other TWENTY printed the same prose with the flag as without and
    // exited 0. #991 exempted it by construction — `Render` has a non-optional default, so "given" and
    // "defaulted" were the same state and there was nothing to detect — and that exemption was true of
    // the PARSER and silent about the RENDERER.
    //
    // THE REPAIR IS SCOPING, NOT HONOURING, and the twenty are the argument. Teaching twenty handlers to
    // read `opts.Render` is twenty chances to flip a bare form, with no way to prove none of them did; and
    // two of the twenty decide whether this fleet runs at all. `whoami --mint` prints
    // `export FSGG_WORKER=…` for `eval`, and `done` prints the `FSGG-DONE` stamp every driver greps.
    // A JSON projection nobody asked for, on either, is an outage bought for nothing. Refusal is also
    // #991's own remedy: it makes `scopeOf` the gate rather than adding a new checker beside it.

    /// The render mode a BARE invocation of every verb parses to — criterion 3's pin.
    ///
    /// THE ASSERTION THAT MATTERS IS THIS ONE, not "`--json` works". The regression risk in scoping a
    /// render flag is entirely in the NO-FLAG path: #1517 measured that honouring `opts.Render` in
    /// `widen` while its parse arm still inherited the module default of `Json` would have flipped the
    /// bare `widen` — the form every recipe, skill and driver runs — from a human receipt to a JSON
    /// object, and sixteen more arms were sitting in that same configuration.
    ///
    /// FIFTEEN OF THESE ROWS CHANGED VALUE IN #1523, from `Json` to `Text`: `whoami`, `next`, `landable`,
    /// `release`, `heartbeat`, `set-field`, `child`, `say`, `done`, `verify-paths`, `bootstrap`,
    /// `field-id`, `option-id`, `item-id`, `add`. Every one of them is a handler that reads `opts.Render`
    /// NOWHERE, so the field it was left at could not be observed — which is exactly why they were left
    /// wrong. Their stdout is byte-identical before and after; what changed is that the declared default
    /// now states what the handler has always printed, so the next edit that teaches one of them to
    /// honour the field finds it already set to the right mode. That is the trap being disarmed rather
    /// than re-armed.
    ///
    /// EVERY ROW WHOSE COMMAND *DOES* BRANCH ON `Render` IS UNCHANGED. That is the non-regression claim,
    /// and it is the one this table exists to keep true.
    let private bareRender: (string * Render) list =
        [
          // Both projections — the handler branches. NONE of these values moved in #1523.
          "decide", Json
          "lanes", Json
          "facts", Json
          "batch", Json
          "ready", Json
          "reconcile", Text
          "who", Text
          "budget", Text
          "claim", Text
          "adopt", Text
          "take", Text
          "widen", Text // #1517
          "set-paths", Text // #1517
          "inbox", Text
          "predicate", Text
          "lint", Text

          // JSON only — stdout is a machine document whatever the flag says. Unchanged.
          "scan", Json
          "command-contract", Json
          "board", Json
          "issues", Json

          // TEXT only — prose, a bare id, or one verdict word. These are the fifteen VERBS that moved
          // (`--help`/`--version` moved too, but are reached by flag and have no bare form to pin), plus
          // the five (`reap`, `overlap`, `room open`, `followup`, `flush`) already pinned `Text` by hand.
          "whoami", Text
          "next", Text
          "reap", Text
          "landable", Text
          "release", Text
          "heartbeat", Text
          "set-field", Text
          "child", Text
          "overlap", Text
          "say", Text
          "room open", Text
          "done", Text
          "verify-paths", Text
          "followup", Text
          "bootstrap", Text
          "field-id", Text
          "option-id", Text
          "item-id", Text
          "add", Text
          "flush", Text ]

    [<Fact>]
    let ``#1523 the BARE form of every verb parses to the mode that verb actually prints in`` () =
        let wrong =
            bareRender
            |> List.choose (fun (verb, expected) ->
                match parse (verb.Split(' ') |> Array.toList) with
                | Ok o when o.Render = expected -> None
                | Ok o -> Some $"%s{verb}: bare render is %A{o.Render}, expected %A{expected}"
                | Error e -> Some $"%s{verb}: REFUSED: %s{e}")

        Assert.True(
            List.isEmpty wrong,
            "a BARE invocation changed render mode — this is the #1517 trap, and every caller that parses "
            + "the un-flagged output is downstream of it (#1523):\n  "
            + String.concat "\n  " wrong
        )

    [<Fact>]
    let ``#1523 the pinned bare form IS the declared one — one fact, not two`` () =
        // The table above is a snapshot, and a snapshot beside a declaration is two copies. This binds
        // them: the pin must equal what `renderSupport` says, so a `Both` default edited in `Options.fs`
        // goes RED here and costs a line in the diff rather than costing nothing. It is also what makes
        // the table above a REGRESSION test rather than a restatement — the value is asserted twice, from
        // the parser and from the declaration, and only agreement passes.
        let declared c =
            match renderSupport c with
            | Both d -> d
            | JsonOnly -> Json
            | TextOnly -> Text

        let wrong =
            bareRender
            |> List.choose (fun (verb, expected) ->
                match parse (verb.Split(' ') |> Array.toList) with
                | Ok o when declared o.Command = expected -> None
                | Ok o -> Some $"%s{verb}: declared %A{declared o.Command}, pinned %A{expected}"
                | Error e -> Some $"%s{verb}: REFUSED: %s{e}")

        Assert.True(List.isEmpty wrong, "the pin and the declaration disagree:\n  " + String.concat "\n  " wrong)

    [<Fact>]
    let ``#1523 the pin covers every verb — a new one cannot slip past it`` () =
        // Same argument `CommandSurfaceTests` makes about the verb inventory: a table nobody is forced to
        // extend describes last year's engine. `Command` is the only source of truth this table cannot
        // drift from, and it is reached through the PARSER — the pin names verbs, and what a verb means
        // is the parser's answer, not a second mapping kept here.
        let pinned =
            bareRender
            |> List.map (fun (verb, _) -> (parse (verb.Split(' ') |> Array.toList) |> ok).Command)
            |> Set.ofList

        let dispatched =
            Microsoft.FSharp.Reflection.FSharpType.GetUnionCases typeof<Command>
            |> Array.toList
            |> List.choose (fun case ->
                match Microsoft.FSharp.Reflection.FSharpValue.MakeUnion(case, [||]) :?> Command with
                // `--help`/`--version` are reached by flag, not by a verb, so there is no bare form to pin.
                | Help
                | Version -> None
                | c -> Some c)
            |> Set.ofList

        Assert.Equal<Set<Command>>(dispatched, pinned)

    [<Fact>]
    let ``#1523 --json is REFUSED on a command with no machine projection`` () =
        // Including the two that decide whether this fleet can run. A refusal is loud and fixable; the
        // silence it replaces is neither.
        for verb, args in
            [ "whoami", [ "--mint" ]
              "done", [ ".github#1523"; "--flip" ]
              "next", []
              "landable", [ "801"; "--repo"; ".github" ]
              "release", [ ".github#1523" ]
              "heartbeat", [ ".github#1523" ]
              "add", [ ".github#1523" ]
              "flush", []
              "reap", []
              "verify-paths", [ "--pr"; "801" ] ] do
            let e = parse ([ verb ] @ args @ [ "--json" ]) |> rejected
            Assert.Contains("--json is not a flag of", e)
            Assert.Contains(verb, e)
            // The message must say WHY, not just no. The caller reached for a machine projection; the
            // useful answer is that there is not one, and where the ones that exist are listed.
            Assert.Contains("no machine projection", e.Replace("NO machine projection", "no machine projection"))
            Assert.Contains("command-contract", e)

    [<Fact>]
    let ``#1523 --text is REFUSED on a command whose stdout is always a machine document`` () =
        // The other half of the same rule, and the reason `--json`/`--text` are two `Flag` cases rather
        // than two spellings of one: `issues` and `board` keep the JSON promise and cannot keep the text
        // one, and a single row could not have said both.
        for verb, args in [ "issues", [ "sdd" ]; "board", []; "command-contract", []; "scan", [] ] do
            let e = parse ([ verb ] @ args @ [ "--text" ]) |> rejected
            Assert.Contains("--text is not a flag of", e)
            Assert.Contains(verb, e)

    [<Fact>]
    let ``#1523 the fourteen commands that BRANCH on Render still take both spellings`` () =
        // The non-regression leg. Scoping a flag is only correct if it stays where it is honoured, and
        // every one of these is a live caller in this repo's own recipes and CI (`ready --json`,
        // `lint --json`, `batch --json`, `take --json`, `who --json`, `reconcile --json`, `budget --json`,
        // `lanes --text`, `decide --text`, `ready --text`).
        for verb, args in
            [ "decide", []
              "lanes", []
              "facts", []
              "batch", []
              "ready", []
              "who", []
              "budget", []
              "claim", [ ".github#1523" ]
              "adopt", [ ".github#1523" ]
              "take", []
              "widen", [ ".github#1523"; "--paths"; "src/A.fs" ]
              "set-paths", [ ".github#1523"; "--paths"; "src/A.fs" ]
              "inbox", []
              "lint", [] ] do
            Assert.Equal(Json, (parse ([ verb ] @ args @ [ "--json" ]) |> ok).Render)
            Assert.Equal(Text, (parse ([ verb ] @ args @ [ "--text" ]) |> ok).Render)

        // `reconcile` honours both too, and since .github#1541 `--apply` does not take that away: the
        // scoping table is what makes the pair legal, so this is the row that would notice if scoping
        // ever narrowed `--json` back off the mutating verb.
        Assert.Equal(Json, (parse [ "reconcile"; "--json" ] |> ok).Render)
        Assert.Equal(Text, (parse [ "reconcile"; "--text" ] |> ok).Render)
        Assert.Equal(Json, (parse [ "reconcile"; "--apply"; "--json" ] |> ok).Render)

    [<Fact>]
    let ``#1523 RenderGiven separates the ACT from the EFFECT`` () =
        // The modelling criterion, and the whole reason the flag can be scoped at all. `Render` alone
        // cannot answer "was it given?" — it has a non-optional default, so a defaulted `Text` and an
        // explicit `--text` are the same value. `RenderGiven` is the missing bit, and `flagsGiven` reads
        // it. Without this field the residue rule has nothing to name and `--json` stays `Global` by
        // necessity rather than by choice, which is exactly the state #991 recorded and #1523 measured.
        let bare = parse [ "who" ] |> ok
        Assert.Equal(Text, bare.Render)
        Assert.Empty(bare.RenderGiven)

        let explicitText = parse [ "who"; "--text" ] |> ok
        Assert.Equal(Text, explicitText.Render)
        Assert.Equal<Set<Render>>(Set.ofList [ Text ], explicitText.RenderGiven)

        let explicitJson = parse [ "who"; "--json" ] |> ok
        Assert.Equal<Set<Render>>(Set.ofList [ Json ], explicitJson.RenderGiven)

        // `Render` is LAST WINS. The RECORD is not — it is every spelling that was typed, and the next
        // test is why that distinction is load-bearing rather than tidy.
        let both = parse [ "who"; "--json"; "--text" ] |> ok
        Assert.Equal(Text, both.Render)
        Assert.Equal<Set<Render>>(Set.ofList [ Json; Text ], both.RenderGiven)

    [<Fact>]
    let ``#1523 giving BOTH spellings does not smuggle the out-of-scope one past the guard`` () =
        // THE HOLE THIS CLOSES, found by review of the first cut of #1523 and reproduced against the built
        // binary before it was fixed:
        //
        //   $ fsgg-coord-engine done .github#1 --json --text   -> ACCEPTED, and ran
        //   $ fsgg-coord-engine board --text --json            -> ACCEPTED, and printed JSON
        //
        // `RenderGiven` was `Render option` and held the WINNER. `Render` is last-wins, so `--json --text`
        // resolved to `Text`, `flagsGiven` reported only `--text` — which `done` legitimately takes — and
        // the `--json` that `done` cannot honour went through unnamed. That is the accepted-and-ignored
        // silence this entire change exists to end, rebuilt inside the guard against it, and reachable by
        // any caller that appends a flag to a template that already carries the other one.
        //
        // A command holding one legal render flag and one illegal one is the ONLY case that separates
        // "remember the winner" from "remember what was typed", which is why it gets its own test.
        for verb, args, offending in
            [ "done", [ ".github#1523"; "--flip" ], "--json"
              "whoami", [ "--mint" ], "--json"
              "next", [], "--json"
              "board", [], "--text"
              "issues", [ "sdd" ], "--text"
              "scan", [], "--text" ] do
            for order in [ [ "--json"; "--text" ]; [ "--text"; "--json" ] ] do
                let e = parse ([ verb ] @ args @ order) |> rejected
                Assert.Contains($"%s{offending} is not a flag of", e)

    [<Fact>]
    let ``#1598 batch --explain is READ, and it is scoped to the ONE verb that can answer it`` () =
        // Same shape as #636's `--include-backlog` pin above and for the same reason: the usage block is a
        // prescribing site nothing else gates, so a flag it advertises must be provably threaded.
        Assert.True((parse [ "batch"; "--explain" ] |> ok).Explain)
        Assert.False((parse [ "batch" ] |> ok).Explain)

        // `next` and `take` are `batch` capped at one and print a single ref — a ranking of one candidate
        // answers nothing — and `decide`'s snapshot carries no `Phase` and no age to rank on. So the flag
        // is refused there BY NAME rather than silently accepted and dropped, which is the `release
        // --status` defect (#867/#991) this table exists to prevent.
        for nonReader in [ "next"; "take"; "decide"; "scan" ] do
            let e = parse [ nonReader; "--explain" ] |> rejected
            Assert.Contains("--explain", e)
            Assert.Contains("batch", e)

    [<Fact>]
    let ``#1598 --explain composes with both projections — it is not a third rendering`` () =
        // `--explain` writes to STDERR, so it is orthogonal to `--json`/`--text` rather than a competing
        // output mode. Both spellings must parse, or `batch --json --explain` — the spelling a driver
        // consuming the array actually wants — would be refused for no reason.
        Assert.True((parse [ "batch"; "--json"; "--explain" ] |> ok).Explain)
        Assert.True((parse [ "batch"; "--text"; "--explain" ] |> ok).Explain)
        Assert.Equal(Json, (parse [ "batch"; "--json"; "--explain" ] |> ok).Render)
