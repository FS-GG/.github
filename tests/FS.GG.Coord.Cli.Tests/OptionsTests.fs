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
          [ "--dry-run" ], "flush", "reap"
          [ "--strict" ], "lint", "ready"
          [ "--batch" ], "set-field", "widen"
          [ "--flip" ], "done", "claim"
          [ "--force" ], "claim", "release"
          [ "--include-backlog" ], "take", "who"
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
    let ``#991 a flag whose field has a non-optional default is Global — there is nothing to refuse`` () =
        // `--json`/`--text` land in `Render` and `--lease` in `LeaseMinutes`, both non-optional. "Given" and
        // "defaulted" are the same state, so they cannot be detected and must not be guessed at. They are
        // declared Global deliberately, and every command keeps taking them.
        Assert.Equal(Json, (parse [ "who"; "--json" ] |> ok).Render)
        Assert.Equal(Text, (parse [ "lint"; "--text" ] |> ok).Render)
        Assert.Equal(30, (parse [ "take"; "--lease"; "30" ] |> ok).LeaseMinutes)
        Assert.Equal(Some "FS.GG.SDD", (parse [ "next"; "--repo"; "sdd" ] |> ok).Repo)

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
