namespace FS.GG.Coord.Cli.BoardOps

module Handlers =

    open System
    open System.Diagnostics
    open System.IO
    open System.Text.Json
    open FS.GG.Coord
    open FS.GG.Coord.Types
    open FS.GG.Coord.GitHub
    open FS.GG.Coord.GitHub.Transport
    open FS.GG.Coord.Cli
    open FS.GG.Coord.Cli.Options
    open FS.GG.Coord.Cli.Render
    open FS.GG.Coord.Cli.Kernel

    [<Literal>]
    let private StructuredRouteMarker = "<!-- fsgg:route-decision/v2 -->"

    // Everything below spells the three legal values EXACTLY ZERO times. `Class.legalClasses` is the
    // union by reflection and `Types.itemClassWireName` is the one function that renders a case to a
    // word; the only thing this module adds is the GLOSS, which the union genuinely does not carry —
    // and that is a TOTAL match, so a fourth `ItemClass` fails the build here rather than reaching a
    // filer's diagnostic unexplained. It is `Protocol.meaning`'s shape, for `Protocol.meaning`'s reason.

    let private gloss =
        function
        | Defect -> "something is broken now"
        | Hardening -> "nothing is broken; this removes a way it could break"
        | Decision -> "a human must choose first"

    // The menu a filer picks from: every legal value, spelled as the body line they must write.
    let classMenu: string =
        Class.legalClasses
        |> List.map (fun c -> $"`Class: %s{itemClassWireName c}` (%s{gloss c})")
        |> String.concat ", "

    // The sentence `lint`'s `CLASS-INVALID` and `add`'s refusal BOTH render, spelled once.
    //
    // `None` when every `Class:` line in the body resolves — including when there is no line at all,
    // which is `CLASS-UNSET`'s business and not this one's.
    let outOfVocabularyClass (body: string) : string option =
        match Class.unrecognised body with
        | [] -> None
        | bad ->
            let quoted =
                bad
                |> List.map (fun v -> if v = "" then "(empty)" else $"\"%s{v}\"")
                |> String.concat ", "

            // QUOTE THE OFFENDING VALUE BACK AND LIST THE LEGAL SET (AC3). Both measured authors believed
            // they were classing the row correctly; `docs` and `enhancement` are exactly the words a
            // reasonable person picks from the general vocabulary of issue triage when nothing says the
            // set is closed. A diagnostic that said only "invalid" would send them to the ADR to find out
            // what it wanted, which is the cost this rule exists to remove.
            Some
                $"its text DOES declare a `Class:` line and the value is not one this engine speaks: %s{quoted}. The vocabulary is CLOSED and has exactly these values — %s{classMenu}. Anything else is not a class at all: the row reads as untriaged, counts as a POSSIBLE defect under ADR-0066's stopping rule, and blocks a clean termination read for the whole board until a human reads the body (.github#1651). Value case and surrounding space are normalised, so `Class: Defect` and `class: defect` are fine; an unlisted WORD is not."

    // The CLASS axis's whole verdict over one candidate row: `Some(code, detail)`, or `None` when the
    // row's own text classes it.
    //
    // **THE TWO CAUSES ARE SEPARATE FINDINGS, AND THE INVALID ONE WINS.** They were one rule, and a row
    // carrying `Class: docs` was told it *"records no `Class:`"* — false, and false in the direction
    // that costs the most: the reader goes looking for a missing line, finds a present one, and has to

    let filingLaneOfOne (candidate: Ref) (paths: TouchSet) (items: Item list) : Ref list =
        items
        |> List.filter (fun item ->
            item.Ref <> candidate
            && String.Equals(item.Ref.Owner, candidate.Owner, StringComparison.OrdinalIgnoreCase)
            // repo-filter-monopoly: exempt — REF-to-REF identity comparison, not a `--repo` filter.
            && String.Equals(item.Ref.Repo, candidate.Repo, StringComparison.OrdinalIgnoreCase)
            && TouchSet.strictlyContains paths item.TouchSet)
        |> List.map _.Ref

    // Structured decisions are append-only ledgers, so every effective read must see revision 1 and
    // every predecessor. A bounded tail is unsafe: a buried record could otherwise disappear. Use the complete, paginated identity read
    // for reads and writes; the pagination guard fails closed rather than truncating authorization.
    let private completeDeliveryRouteComments (ctx: Context) (target: Ref) =
        Reads.commentBodies ctx.Transport target.Owner target.Repo target.Number

    let private readDeliveryRouteComments (ctx: Context) (target: Ref) =
        completeDeliveryRouteComments ctx target

    // The validated delivery-route decision in the complete comment ledger — one search, in one place.
    //
    // It was written out three times (scheduling, the claim/take mutation boundary, and
    // `delivery-route show`/`record`) and .github#2324 needed a fourth caller in `verifyPaths`. A rule
    // copied a fourth time is #485's shape arriving by addition rather than by drift, so the copies are
    // collapsed here first. Every caller still owns what it does with `None` — the three existing ones
    // deliberately differ (`Unreadable`/`Stale` vs. a raw IO error), and that judgement is theirs, not
    // this function's.
    //
    // Read every structured record in comment order. Missing, malformed, gapped, or tampered evidence
    // is an explicit refusal; there is no alternate authority or prose fallback.
    let private structuredRouteLedger (subject: string) (comments: string list) =
        let marked =
            comments
            |> List.choose (fun comment ->
                if comment.StartsWith(StructuredRouteMarker + "\n", StringComparison.Ordinal) then
                    Some(comment.Substring(StructuredRouteMarker.Length).Trim())
                else None)

        if List.isEmpty marked then Ok None
        else
            let decoded = marked |> List.map DeliveryRouteApplication.decodeStructured
            let failures = decoded |> List.choose (function Error error -> Some error | Ok _ -> None)
            if not (List.isEmpty failures) then Error failures
            else
                let records = decoded |> List.choose Result.toOption
                StructuredDecision.validateRouteLedger subject records |> Result.map (fun latest -> Some(records, latest))

    let private routeEvidence (subject: string) (comments: string list) : DeliveryRoute.Verdict =
        match structuredRouteLedger subject comments with
        | Error errors -> DeliveryRoute.Stale errors
        | Ok(Some(_, latest)) ->
            DeliveryRoute.Current(StructuredDecision.toEffectiveRoute latest)
        | Ok None ->
            DeliveryRoute.Stale [ "structured route ledger is missing" ]

    // The route decision is an impure receipt: both the source item and its append-only receipt ledger
    // are read immediately before the pure scheduler sees the item.  An unreadable read stays typed as
    // unreadable, rather than collapsing into a missing/lightweight decision.
    let private readDeliveryRouteVerdict (ctx: Context) (target: Ref) =
        match readDeliveryRouteComments ctx target with
        | Error error -> DeliveryRoute.Unreadable [ Errors.explain error ]
        | Ok comments -> routeEvidence target.Canonical comments

    // Mutation boundaries need the underlying IO error as well as the fail-closed route verdict.  In
    // particular, a rate-limited receipt read must remain EX_RATE for a JSON worker, not be flattened into

    let private requireCoherentBlockedWrite (ctx: Context) (ref: Ref) (status: BoardStatus option) : Result<unit, int> =
        if status <> Some BoardStatus.Blocked then Ok()
        else
            match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
            | Error e -> eprint $"fsgg-coord-engine: Status=Blocked: board unreadable ({Errors.explain e})"; Error ExitError
            | Ok board ->
                match Board.itemBlockedBy ctx.Transport board ref.Owner ref.Repo ref.Number with
                | Ok(Some value) when not (String.IsNullOrWhiteSpace value) -> Ok()
                | Error e -> eprint $"fsgg-coord-engine: Status=Blocked: Blocked by unreadable ({Errors.explain e})"; Error ExitError
                | Ok _ ->
                    match Reads.issueBody ctx.Transport ref.Owner ref.Repo ref.Number with
                    | Ok body when HumanBlock.parse body |> Option.isSome -> Ok()
                    | Ok _ -> eprint "fsgg-coord-engine: Status=Blocked refuses an incoherent park (.github#2079)."; Error ExitError
                    | Error e -> eprint $"fsgg-coord-engine: Status=Blocked: body unreadable ({Errors.explain e})"; Error ExitError

    // THE ONE RESOLVED-STATUS BOUNDARY FOR A `Ready` WRITE (.github#2698) — the deliberate mirror of
    // `requireCoherentBlockedWrite` directly above, and placed beside it so the two lifecycle columns
    // that carry a precondition carry it in one shape rather than two.
    //
    // A row boarded `Ready` with no current delivery-route receipt is UNSCHEDULABLE FROM BIRTH and says
    // nothing about it. `Schedulability.schedulable` maps a `Stale`/`Unreadable` route to
    // `AwaitingDeliveryRouteDecision`, `Batch.scheduleWith` then skips the row and reserves no lane —
    // while every board projection keeps reporting it as available work. That is the same shape
    // `.github#2220` closed for `Paths: none`, reached through a different missing precondition.
    //
    // IT IS A REFUSAL, NOT A CORRECTION, and never an authoring path. `DeliveryRoute.fs`'s own validator
    // says the route *"is required and must be agent-authored"*: an engine that picked `lightweight`
    // here to keep the promotion moving would be minting the one judgement the receipt exists to record.
    // So this converts a SILENTLY unschedulable row into a LOUD refusal at the moment the promoting
    // agent still holds the context a route decision needs, and names the command that authors one.
    //
    // IT IS SHARED BECAUSE ONE SEAM WAS NEVER THE WHOLE OF IT. The filed acceptance criterion named
    // `add --status Ready` alone; a host then measured seven rows reaching `Ready` on 2026-08-16 and NOT
    // ONE of them went through that seam (`.github#2698#issuecomment-5309155317`). `add` with no
    // `--status` defaults to `Backlog` (#1823), so the real paths were `set-field Status Ready` and —
    // for five of the seven — `reconcile --apply`, which derived `Ready` from policy and promoted rows
    // their operator had deliberately parked in `Backlog`, with no operator action at all. A rule
    // enforced only where a human types it is a rule a scheduled job walks around, so every caller that
    // RESOLVES a `Status=Ready` mutation passes through here, the reducer included.
    //
    // IT FAILS CLOSED ON AN UNREAD LEDGER (#266): a receipt we could not READ is not a receipt we may
    // declare absent — nor one we may declare present. The underlying IO error's own exit code is
    // preserved rather than flattened to 1, so a rate-limited read stays EX_RATE and keeps its back-off
    // contract instead of reading to a JSON worker as a permanent refusal.
    let private requireCurrentRouteIfReady (ctx: Context) (ref: Ref) (status: BoardStatus option) : Result<unit, int> =
        if status <> Some BoardStatus.Ready then Ok()
        else
            match readDeliveryRouteComments ctx ref with
            | Error e ->
                eprint
                    $"fsgg-coord-engine: refusing Status=Ready on %s{ref.Short} — its delivery-route receipt ledger could NOT BE READ (%s{Errors.explain e}). That is a failed read, not an absent receipt, and it is not a present one either (#266). Nothing was written."

                Error(Errors.exitCode e)
            | Ok comments ->
                match routeEvidence ref.Canonical comments with
                | DeliveryRoute.Current _ -> Ok()
                | DeliveryRoute.Stale reasons
                | DeliveryRoute.Unreadable reasons ->
                    let why = String.concat "; " reasons

                    eprint
                        $"fsgg-coord-engine: refusing Status=Ready on %s{ref.Short} — no current delivery-route receipt (%s{why}). Nothing was written."

                    eprint
                        "fsgg-coord-engine:   A row boarded Ready without one is UNSCHEDULABLE FROM BIRTH: `batch`/`take` pass it over as `awaiting an explicit current delivery-route decision`, while `ready` and every other projection keep reporting it as available work (.github#2698)."

                    eprint
                        $"fsgg-coord-engine:   The route is an agent judgement and this engine may not mint one. Author it, then re-run:  scripts/fsgg-coord delivery-route record %s{ref.Short} <receipt.json>"

                    Error ExitError

    // True only for the one shape `Board.bootstrap` emits when the configured Projects v2 board itself
    // could not be resolved — a credential/visibility gap, not a real reconcile finding (round-2 review
    // repair, .github#2264 PR #2271; the org-level remedy is tracked at .github#2332, not here).
    //
    // SCOPED TO `reconcile` ALONE, deliberately not a change to `Errors.exitCode` — that shared table
    // already explains, in its own comment, why `NotFound` stays a real exit-1 finding for every other

    let private requireSentinelIfBlockedByCleared (ctx: Context) (ref: Ref) : Result<unit, int> =
        match Reads.issueBody ctx.Transport ref.Owner ref.Repo ref.Number with
        | Error e ->
            eprint
                $"fsgg-coord-engine: set-field --batch: the body could not be read to check for a 'Blocked on:' sentinel (%s{Errors.explain e}) — nothing written."

            Error ExitError
        | Ok body ->
            match HumanBlock.parse body with
            | Some _ -> Ok()
            | None ->
                eprint
                    $"fsgg-coord-engine: set-field --batch refuses: this call clears 'Blocked by' in the SAME batch as 'Status=Blocked' — the row would land with an EMPTY field and no 'Blocked on: human/...' sentinel (.github#2079). Pair 'Status=Blocked' with a non-empty 'Blocked by=<refs>' instead, or record a human park first: add a body line 'Blocked on: human/decision' or 'Blocked on: human/action'. Nothing written."

                Error ExitError

    let requireCoherentParkIfBlocked (ctx: Context) (ref: Ref) (requested: BoardStatus option) : Result<unit, int> =
        requireCoherentBlockedWrite ctx ref requested

    // #2098: `requireCoherentParkIfBlocked` alone is the wrong gate for `set-field --batch`. `release`
    // and single `set-field` write the `Blocked by` field as a SEPARATE step BEFORE calling it
    // (`writeBlockedByIfRequested`, `--blocked-by` above; the single-field write itself for the other
    // door), so by the time the gate's live read runs, a same-call edge has already landed and the read
    // sees it. `--batch` cannot borrow that trick: AC1 requires the WHOLE document to validate before
    // ANY alias is emitted (`setFieldBatchCmd`'s "one aliased mutation" contract — a pair that fails
    // validation must cost nothing), so there is no live write to read back. Calling the live-only gate
    // as-is would refuse the exact call the docs steer callers toward: `Status=Blocked` paired with a
    // non-empty `Blocked by=<ref>` in the SAME batch, because the live board has not seen that pair yet.
    //
    // So this batch-only wrapper is handed the batch's OWN pending `Blocked by` write and judges it
    // BEFORE ever touching the live field: a non-empty pending `Set` makes the park coherent on its own
    // (no live read needed); a pending `Clear` is judged on the sentinel ALONE (the live field is about
    // to be obsolete — see `requireSentinelIfBlockedByCleared` above); only the ABSENCE of a `Blocked by`
    // pair in this batch defers to the live-read gate, so a batch that never touches `Blocked by` behaves
    // exactly as it did before this issue.
    let requireCoherentParkIfBlockedForBatch
        (ctx: Context)
        (ref: Ref)
        (requested: BoardStatus option)
        (pendingBlockedBy: Board.FieldWrite option)
        : Result<unit, int> =
        if requested <> Some BoardStatus.Blocked then
            Ok()
        else
            match pendingBlockedBy with
            | Some(Board.Set v) when not (String.IsNullOrWhiteSpace v) -> Ok()
            | Some Board.Clear -> requireSentinelIfBlockedByCleared ctx ref
            | _ -> requireCoherentParkIfBlocked ctx ref requested

    // THE OPERATOR-WRITABLE INTENT CHANNEL (.github#2690) — the WRITE half, shared by every verb that
    // lands a Status column somebody explicitly named.
    //
    // `LifecycleProjection.explicitStatusWatermark` owns the rule (which columns record an intent, and why
    // the rest deliberately do not); this owns the IO and the failure vocabulary. Callers pass the column
    // they actually landed, never the one they hoped to.
    //
    // CALL IT ONLY AFTER THE COLUMN WRITE LANDED — `Ok Board.Written`, never `Deferred`. That is
    // `Writes.lifecycleWatermark`'s own stated contract (*"Persist the ordering receipt only after the
    // caller has freshly verified its board mutation"*): a deferred write has not happened, and `flush`
    // replays the column, not this.
    //
    // IT DOES NOT ADD A VERIFICATION READ, and that is a decision rather than an omission. `reconcile`
    // re-reads the row before persisting its watermark because the receipt it is about to write could
    // SUPPRESS a later event under `advance`'s ordering rule. Here the failure runs the other way and is
    // self-correcting: a watermark for a column write that somehow did not land makes the very next
    // reconcile pass compute the operator's intended column and WRITE it, which is the outcome the
    // operator asked for. Spending a GraphQL point per `set-field` to prevent a self-healing outcome is
    // not the trade `#418` asks for.
    //
    // A FAILURE HERE IS A FAILURE, and the caller must say so rather than exiting green. `reconcile`
    // already settled this vocabulary — a verified status whose watermark could not be persisted is
    // `Failed "verified status has no durable lifecycle watermark"`, not a warning — and the reason is the
    // whole of this row: a column with no intent behind it is reverted on the next pass, silently. The
    // caller that swallows this reproduces the defect it is fixing.
    let private recordExplicitStatusIntent
        (ctx: Context)
        (ref: Ref)
        (landed: BoardStatus)
        (reason: string)
        : Result<unit, string> =
        let observedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

        match LifecycleProjection.explicitStatusWatermark observedAt reason landed with
        | None -> Ok()
        | Some watermark ->
            Writes.lifecycleWatermark ctx.Transport ref (LifecycleProjection.watermarkMarker watermark)
            |> Result.mapError Errors.explain

    // The one sentence every caller of the above prints when it fails, so three verbs cannot drift into
    // three different accounts of the same consequence.
    let private explicitStatusIntentFailure (ref: Ref) (landed: BoardStatus) (why: string) : string =
        $"fsgg-coord-engine: %s{ref.Short} Status=%s{statusWireName landed} LANDED on the board, but its scheduling intent could NOT be recorded (%s{why}) — so the next `reconcile --apply` pass will recompute this row from inputs you never touched and REVERT it (.github#2690). Nothing is queued and nothing replays it. Re-run this command once the transport recovers."

    let private declaredPathTokens (touchSet: TouchSet) : string list =
        match touchSet with
        | Undeclared
        | Unreadable _ -> []
        | DeclaredNone -> [ "none" ]
        | DeclaredChore -> [ "any" ]
        | Declared tokens ->
            tokens
            |> List.map (function
                | Matchable token
                | Unmatchable token -> token)


    let private generatedPaths (root: string) : Set<string> =
        let script = Path.Combine(root, "scripts", "generated-paths")

        if not (File.Exists script) then
            Set.empty
        else
            try
                let psi = ProcessStartInfo(script)
                psi.WorkingDirectory <- root
                psi.RedirectStandardOutput <- true
                psi.RedirectStandardError <- true
                psi.UseShellExecute <- false
                use p = Process.Start psi

                // BOTH pipes async, and the ordering here is the whole point rather than a style choice.
                //
                // A blocking `StandardOutput.ReadToEnd()` cannot be bounded by a later `WaitForExit t`: a
                // stuck generator never closes stdout, so the read never returns and the timeout below is
                // simply never reached. (Measured, on the first cut of this function: a `sleep 600`
                // generator hung `verify-paths` for as long as it was allowed to run, and left the child
                // alive — the timeout was decoration, and a comment promising a bound that does not exist
                // is worse than no bound, because it invites the trust it cannot earn.)
                //
                // Async also removes the symmetric deadlock: draining one pipe while the child fills the
                // other's buffer blocks them both.
                let stdout = Text.StringBuilder()
                let sync = obj ()

                p.OutputDataReceived.Add(fun e ->
                    if not (isNull e.Data) then
                        lock sync (fun () -> stdout.AppendLine e.Data |> ignore))

                // The generator's OWN reason, forwarded rather than swallowed — see the doc comment.
                p.ErrorDataReceived.Add(fun e ->
                    if not (isNull e.Data) then
                        eprint $"  | %s{e.Data}")

                p.BeginOutputReadLine()
                p.BeginErrorReadLine()

                // BOUNDED, because a HANG is the one failure this design otherwise had no answer for.
                // Everything else fails closed — absent, broken, silent all subtract nothing and say so. A
                // generator that blocks (waits on stdin, a hung git, a stalled network mount) instead hangs
                // `verify-paths` itself, which IS the merge gate: no verdict, no diagnostic, a job burning
                // to its workflow timeout. That is not fail-closed, it is fail-SILENT. Nothing else in the
                // chain bounds it — `generated-paths` does not time its own generators out — so the bound
                // goes at the edge that has a verdict to protect. 30s is three orders of magnitude past the
                // measured cost of the whole roster (~40ms): only a genuinely stuck generator reaches it.
                // `Kill true` takes the process TREE — killing the script and orphaning the generator it is
                // blocked on would leak the actual hang.
                //
                // Tunable ONLY so the gate can prove the bound without costing CI 30 idle seconds — the same
                // reason `FSGG_COORD_SCAN_TTL_SEC` exists, and the e2e suite already sets that. An untested
                // safety net is one that rots quietly (#724); a knob nothing but the test turns is cheaper
                // than not testing it. A malformed or non-positive value falls back to the default rather
                // than disabling the bound: "0" must not silently mean "wait forever".
                let timeoutMs =
                    match Environment.GetEnvironmentVariable "FSGG_GENERATED_PATHS_TIMEOUT_MS" with
                    | null
                    | "" -> 30_000
                    | v ->
                        match Int32.TryParse v with
                        | true, n when n > 0 -> n
                        | _ -> 30_000

                if not (p.WaitForExit timeoutMs) then
                    (try p.Kill true with _ -> ())

                    eprint
                        $"fsgg-coord-engine: scripts/generated-paths did not finish within %d{timeoutMs}ms and was killed — NOTHING is subtracted, so a regenerated artifact will be reported as drift below."

                    Set.empty
                else

                // The child is gone; this second, unbounded wait is the documented way to let the async
                // handlers flush what it wrote before exiting. It cannot hang — the process has exited.
                p.WaitForExit()
                let out = lock sync (fun () -> stdout.ToString())

                if p.ExitCode <> 0 then
                    eprint
                        $"fsgg-coord-engine: scripts/generated-paths exited %d{p.ExitCode} — NOTHING is subtracted, so a regenerated artifact will be reported as drift below."

                    Set.empty
                else
                    out.Split('\n')
                    |> Array.map (fun l -> l.Trim())
                    |> Array.filter (fun l -> l <> "")
                    |> Set.ofArray
            with ex ->
                eprint
                    $"fsgg-coord-engine: could not run scripts/generated-paths (%s{ex.Message}) — NOTHING is subtracted, so a regenerated artifact will be reported as drift below."

                Set.empty

    let private generatedPathCollector = generatedPaths


    let private gateField (ref: Ref) (field: string) (raw: string) : Result<Board.FieldWrite, int> =
        if field <> "Blocked by" then
            Ok(if raw = "" then Board.Clear else Board.Set raw)
        else
            match Blockers.canonicalizeBlockedBy ref.Owner ref.Repo raw with
            | Ok None -> Ok Board.Clear
            | Ok(Some canonical) -> Ok(Board.Set canonical)
            | Error Blockers.Placeholder ->
                // A placeholder is not a value — it is the caller trying to say "no blocker" with a token
                // rather than by clearing. Point at the clear (`'Blocked by' ''`), not at Status.
                eprint
                    $"fsgg-coord-engine: 'Blocked by' does not take a placeholder ('%s{raw.Trim()}'). To say there is no blocker, CLEAR the field:  set-field <issue> 'Blocked by' ''"

                Error ExitError
            | Error Blockers.NotIssueRefs ->
                // Prose in a dependency field. If the caller means the item ITSELF is blocked, that is a
                // Status — name it, so the refusal is a redirection, not just a rejection.
                eprint
                    $"fsgg-coord-engine: 'Blocked by' takes issue refs (owner/repo#n), not prose: '%s{raw}'. If the item ITSELF is blocked, that is a Status:  set-field <issue> Status Blocked"

                Error ExitError

    // `set-field --batch <ref> Field=Value ...` — N fields in ONE aliased mutation (#448).
    //
    // The whole point is the call count: three separate writes are three GraphQL points; the same three
    // aliased into one document is one. So EVERYTHING is resolved before a single mutation is emitted (a
    // pair that fails validation costs zero — a refused value must not spend the budget that dies first),
    // and the two failure arms are told apart because they carry opposite promises: a rate limit refused
    // the whole document, so every pair is QUEUED; a per-alias failure means the board is half-written, so
    // nothing is queued and the caller is told, field by field, what landed and what did not.
    let private setFieldBatchCmd (ctx: Context) (opts: Options) : int =
        match opts.Args with
        | refArg :: (_ :: _ as pairs) ->
            match parseRef ctx refArg, worker opts with
            | Error msg, _ ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | _, Error c -> c
            | Ok ref, Ok w ->
                // Split each pair on the FIRST '=' — a value may legitimately contain one (`Contract=a=b`),
                // and an empty value clears, exactly as the single path's empty `<value>` does.
                let parsePair (s: string) : Result<string * Board.FieldWrite, string> =
                    match s.IndexOf '=' with
                    | -1 -> Error s
                    | i ->
                        let field = s.Substring(0, i)
                        let value = s.Substring(i + 1)

                        if field = "" then Error s
                        else Ok(field, (if value = "" then Board.Clear else Board.Set value))

                let parsed = pairs |> List.map parsePair

                match parsed |> List.tryPick (function | Error s -> Some s | Ok _ -> None) with
                | Some bad ->
                    eprint
                        $"fsgg-coord-engine: set-field --batch takes Field=Value pairs (an empty value clears); '%s{bad}' is not one."
                    ExitError
                | None ->
                    let rawWrites = parsed |> List.choose (function | Ok p -> Some p | Error _ -> None)

                    // The `Blocked by` gate applies to `--batch` too — the same one home the single write
                    // uses — so a prose dependency cannot slip in through the aliased document. It runs
                    // BEFORE bootstrap, so a refused pair spends no GraphQL and queues nothing.
                    let gated =
                        (Ok [], rawWrites)
                        ||> List.fold (fun acc (field, write) ->
                            match acc with
                            | Error _ -> acc
                            | Ok done' ->
                                let raw =
                                    match write with
                                    | Board.Set v -> v
                                    | Board.Clear -> ""

                                match gateField ref field raw with
                                | Error rc -> Error rc
                                | Ok w -> Ok((field, w) :: done'))
                        |> Result.map List.rev

                    match gated with
                    | Error rc -> rc
                    | Ok writes ->

                    // #2098 AC1: the SAME coherent-park invariant `release`/single `set-field` already
                    // enforce, reached through the batch door — a `Status=Blocked` write must not land
                    // with an empty `Blocked by` field and no `Blocked on: human/...` sentinel. Judged
                    // against THIS batch's own pending writes (`requireCoherentParkIfBlockedForBatch`),
                    // so a call pairing `Status=Blocked` with a non-empty `Blocked by=<ref>` in the SAME
                    // document is coherent without a live read racing its own not-yet-emitted mutation.
                    // Runs BEFORE any alias is emitted, same as `gateField` above — a refused batch must
                    // cost nothing.
                    let requestedStatus =
                        writes
                        |> List.tryPick (fun (field, write) ->
                            if field = "Status" then
                                match write with
                                | Board.Set v -> Reads.statusOfName v
                                | Board.Clear -> None
                            else
                                None)

                    let pendingBlockedBy =
                        writes
                        |> List.tryPick (fun (field, write) -> if field = "Blocked by" then Some write else None)

                    match requireCoherentParkIfBlockedForBatch ctx ref requestedStatus pendingBlockedBy with
                    | Error rc -> rc
                    | Ok() ->

                    // .github#2698, reached through the batch door — the same seam `set-field <ref> Status
                    // Ready` uses, and gated from the same `requestedStatus` the park gate above already
                    // resolved off the CANONICAL `write` pairs rather than off raw argv. Before any alias
                    // is emitted, so a refused batch costs nothing and leaves the row exactly as it was.
                    //
                    // No batch-local variant is needed the way `Blocked` needed one: a route receipt is a
                    // comment ledger on the issue, and no `set-field` document can write one, so there is
                    // no pending-write-in-this-same-document case for the live read to race.
                    match requireCurrentRouteIfReady ctx ref requestedStatus with
                    | Error rc -> rc
                    | Ok() ->

                    match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
                    | Error e -> fail e
                    | Ok board ->
                        // Map an alias ("f2") back to the pair it wrote, so a partial write can be reported
                        // in the caller's OWN vocabulary — `Field='value'` — not "f2".
                        let describe (alias: string) : string =
                            let idx =
                                match Int32.TryParse(alias.TrimStart 'f') with
                                | true, n -> Some n
                                | _ -> None

                            match idx with
                            | Some i when i >= 0 && i < List.length writes ->
                                match List.item i writes with
                                | field, Board.Set v -> $"%s{field}='%s{v}'"
                                | field, Board.Clear -> $"%s{field}=<cleared>"
                            | _ -> alias

                        match Board.boardWriteBatch ctx.Transport board ref.Owner ref.Repo ref.Number writes w.Id with
                        // THE PARTIAL ARM IS ITS OWN ANSWER — matched BEFORE the generic failure. Some aliases
                        // landed; reporting nothing happened would be a lie, and reporting success is the bug
                        // #448 forbade by name. EX_PARTIAL (4), and the board is half-written on the record.
                        | Error(Errors.Partial(applied, failed)) ->
                            eprint
                                "fsgg-coord-engine: PARTIALLY APPLIED — the board is now half-written. This is NOT queued: replaying the document would rewrite the aliases that already landed."

                            for alias in applied do
                                eprint $"  APPLIED  %s{describe alias}"

                            for alias, msg in failed do
                                eprint $"  FAILED   %s{describe alias} — %s{msg}"

                            Errors.ExPartial
                        | Error e -> fail e
                        | Ok Board.Written ->
                            printfn "set %d field(s) on %s in one aliased mutation:" (List.length writes) ref.Canonical

                            for field, write in writes do
                                printfn "  %s = %s" field (match write with | Board.Set v -> v | Board.Clear -> "<cleared>")

                            // .github#2690: the batch door onto the same intent channel. `requestedStatus`
                            // above is already exactly the landed column — the batch is all-or-nothing at
                            // this arm (a partial write is `Errors.Partial`, matched before it), so the
                            // pair this records really did land together.
                            match requestedStatus with
                            | None -> ExitGreen
                            | Some status ->
                                match
                                    recordExplicitStatusIntent ctx ref status $"explicit set-field --batch by %s{w.Id}"
                                with
                                | Ok() -> ExitGreen
                                | Error why ->
                                    eprint (explicitStatusIntentFailure ref status why)
                                    ExitError
                        | Ok Board.Deferred ->
                            printfn
                                "set-field --batch %s — QUEUED all %d field(s) (budget exhausted; flush replays the batch)"
                                ref.Canonical
                                (List.length writes)

                            Errors.ExRate
                        | Ok Board.NotOnBoard ->
                            eprint $"fsgg-coord-engine: %s{ref.Canonical} is not an item on this board — nothing written."
                            ExitError
        | _ ->
            eprint "fsgg-coord-engine: set-field --batch takes <ref> followed by one or more Field=Value pairs."
            ExitError

    let setField (ctx: Context) (opts: Options) : int =
        if opts.Batch then
            setFieldBatchCmd ctx opts
        else

        match opts.Args with
        | [ refArg; field; value ] ->
            match parseRef ctx refArg, worker opts with
            | Error msg, _ ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | _, Error c -> c
            | Ok ref, Ok w ->
                // The `Blocked by` gate runs FIRST — before any board read — so a refused value spends no
                // GraphQL, and it produces the canonical value (or `Clear`) the write below emits.
                match gateField ref field value with
                | Error rc -> rc
                | Ok write ->

                // AC1 (.github#2079): `set-field <ref> Status Blocked` is `release --status Blocked`'s
                // other door onto the same park invariant — refused BEFORE any write when the row would
                // land with neither a non-empty `Blocked by` field nor a `Blocked on: human/...` sentinel.
                // A no-op for every other field/value pair (`requireCoherentParkIfBlocked` itself is a
                // no-op unless the resolved status is `Blocked`).
                match requireCoherentParkIfBlocked ctx ref (if field = "Status" then Reads.statusOfName value else None) with
                | Error rc -> rc
                | Ok() ->

                // .github#2698 — THE SEAM THE FILED AC MISSED AND THE ONE OPERATORS ACTUALLY USE. `add`
                // with no `--status` defaults to `Backlog` (#1823), so `set-field <ref> Status Ready` is
                // how a triaging host promotes a row; three of the seven instances the host measured on
                // 2026-08-16 came through this exact command. Refused BEFORE any board read or write.
                //
                // Resolved off `write` — the canonical pair `gateField` produced — and not off raw argv,
                // which is `.github#2690`'s own rule for the intent recorded further down this function:
                // the column the board will actually hold is the one whose precondition must be checked.
                let promotedStatus =
                    if field = "Status" then
                        match write with
                        | Board.Set v -> Reads.statusOfName v
                        | Board.Clear -> None
                    else
                        None

                match requireCurrentRouteIfReady ctx ref promotedStatus with
                | Error rc -> rc
                | Ok() ->

                match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
                | Error e -> fail e
                | Ok board ->
                    match Board.boardWrite ctx.Transport board ref.Owner ref.Repo ref.Number field write w.Id with
                    | Error e -> fail e
                    | Ok Board.Written ->
                        printfn
                            "set %s %s = %s"
                            ref.Canonical
                            field
                            (match write with
                             | Board.Set v -> v
                             | Board.Clear -> "<cleared>")

                        // .github#2690: THE COLUMN IS NOT THE DECISION. Read the landed value off `write`
                        // — the canonical pair `gateField` produced — rather than off raw argv, exactly as
                        // the batch arm does, so the intent recorded is the one the board actually holds.
                        // A `Clear` records nothing: an emptied column is the absence of a choice.
                        let landed =
                            if field = "Status" then
                                match write with
                                | Board.Set v -> Reads.statusOfName v
                                | Board.Clear -> None
                            else
                                None

                        match landed with
                        | None -> ExitGreen
                        | Some status ->
                            match
                                recordExplicitStatusIntent ctx ref status $"explicit set-field by %s{w.Id}"
                            with
                            | Ok() -> ExitGreen
                            | Error why ->
                                eprint (explicitStatusIntentFailure ref status why)
                                ExitError
                    | Ok Board.Deferred ->
                        printfn
                            "set %s %s = %s — QUEUED (budget exhausted; flush replays it)"
                            ref.Canonical
                            field
                            (match write with
                             | Board.Set v -> v
                             | Board.Clear -> "<cleared>")

                        Errors.ExRate
                    | Ok Board.NotOnBoard ->
                        eprint $"fsgg-coord-engine: %s{ref.Canonical} is not an item on this board — nothing written."
                        ExitError
        | _ ->
            eprint "fsgg-coord-engine: set-field takes <ref> <field> <value> (an empty value clears)."
            ExitError

    let child (ctx: Context) (opts: Options) : int =
        match opts.Args with
        | [ parentArg; childArg ] ->
            match parseRef ctx parentArg, parseRef ctx childArg with
            | Error msg, _
            | _, Error msg ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | Ok parent, Ok childRef ->
                match Reads.restId ctx.Transport childRef.Owner childRef.Repo childRef.Number with
                | Error e -> fail e
                | Ok childId ->
                    // #320: the existing-links read must FAIL CLOSED. Swallowing it would make "I could not
                    // reach the API" look exactly like "the edge is not there" — `child` would POST, collect
                    // a 422, and blame the token. Idempotency is BY ID, not by number: two repos can each
                    // have an issue #7, so a re-run of a worker's close-out never has to reason about the edge.
                    match Reads.subIssueIds ctx.Transport parent.Owner parent.Repo parent.Number with
                    | Error e ->
                        eprint
                            $"fsgg-coord-engine: child: cannot read %s{parent.Short}'s sub-issues (%s{Errors.explain e}) — refusing to guess whether %s{childRef.Short} is already linked."
                        ExitError
                    | Ok existing when List.contains childId existing ->
                        printfn "%s is already a sub-issue of %s — nothing to do" childRef.Short parent.Short
                        ExitGreen
                    | Ok _ ->
                        match Writes.child ctx.Transport parent childId with
                        // A failed link surfaces the API's OWN diagnosis — a 422 (already linked, or a
                        // cross-repo link GitHub refuses) and a 403 (no `issues: write`) are different
                        // problems with different fixes, and guessing one would send the worker at the wrong.
                        | Error e -> fail e
                        | Ok() ->
                            printfn "linked %s as a sub-issue of %s" childRef.Short parent.Short
                            ExitGreen
        | _ ->
            eprint "fsgg-coord-engine: child takes <parent-ref> <child-ref>."
            ExitError



    let say (ctx: Context) (opts: Options) : int =
        match oneArg opts "say: an issue ref", worker opts with
        | Error c, _
        | _, Error c -> c
        | Ok arg, Ok w ->
            match opts.ToWorker, opts.Message with
            | None, _ ->
                eprint "fsgg-coord-engine: say needs --to <worker>."
                ExitError
            | _, None ->
                eprint "fsgg-coord-engine: say needs --message <text>."
                ExitError
            | Some toW, Some msg ->
                // NORMALIZE the target to a worker id. Ids are slug()'d at creation and `inbox` matches
                // `.to` by EXACT string, so an unslugged `--to Heron-B71` posts a message its recipient
                // (heron-b71) can never see. `*` — anyone holding the item — is the one literal that is
                // not a worker id. Slug via Identity.slug, the SAME normalization that creates ids (#485).
                let normalizedTo =
                    if toW = "*" then Ok "*"
                    else
                        match Identity.slug toW with
                        | "" -> Error $"say: --to '%s{toW}' is not a usable worker id."
                        | s -> Ok s

                match normalizedTo with
                | Error e ->
                    eprint $"fsgg-coord-engine: %s{e}"
                    ExitError
                | Ok toSlug ->
                    match parseRef ctx arg with
                    | Error m ->
                        eprint $"fsgg-coord-engine: %s{m}"
                        ExitError
                    | Ok ref ->
                        // An unslugged target reached the wrong id silently; say we changed it.
                        if toSlug <> toW then
                            eprint $"fsgg-coord-engine: addressing worker '%s{toSlug}' (normalized from '%s{toW}')."
                        // No lock required — the worker who most needs to speak is the one who just lost a race.
                        match Writes.say ctx.Transport (WorkerId w.Id) (WorkerId toSlug) ref msg with
                        | Error e -> fail e
                        | Ok() ->
                            printfn "said to %s on %s" toSlug ref.Short
                            ExitGreen


    // This worker's mailbox: every message addressed to it (or broadcast) across the in-flight claims.
    //
    // `say` posts a message on the ITEM it concerns, and both parties to a collision are In progress, so the
    // in-flight set is exactly where cross-work talk lives. A per-worker cursor makes `inbox` show only
    // what is new; `--peek` leaves the cursor alone.
    //
    // IT RUNS THE SAME OFF-BOARD SCAN `who`/`reap`/`batch` run (case 25). A claim — and the message riding
    // it — can sit on an issue the board never listed (a failed column flip, or an item that never reached
    // the board), so a mailbox that read only the board's In-progress column would silently drop a message
    // posted on an off-board claim. The candidate set is the board's In-progress rows (arm A) UNION every
    // open issue in the repos in scope (arm B — paginated, and never conditional, so a 304 cannot hide a
    // message the way it must never hide a lock).
    let inbox (ctx: Context) (opts: Options) : int =
        match worker opts with
        | Error c -> c
        | Ok w ->
            match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
            | Error e -> fail e
            | Ok board ->
                match Scan.board ctx.Transport Cache.Reconciling ctx.Owner ctx.Title board.Number with
                | Error e -> fail e
                | Ok rows ->
                    let scoped =
                        rows |> List.filter (fun r -> not r.IsPullRequest) |> Scan.scope opts.Repo

                    // #979 — a mailbox that reports "no new messages" over a repo it never found is the
                    // one failure this verb must not have. Like `who`, the fallback below fails closed on
                    // a repo that does not exist; the advisory is what names the cause as a spelling
                    // rather than an outage.
                    scoped.Advisory |> Option.iter eprint

                    let boardRows = scoped.Rows

                    // The repos an off-board claim could live in — derived from the board scan in hand, with
                    // the same `--repo`-names-no-board-item fallback `who` uses, so a message on an issue that
                    // never reached the board is still found.
                    let repos =
                        let fromBoard =
                            boardRows |> List.map (fun r -> r.Ref.Owner, r.Ref.Repo) |> List.distinct

                        match fromBoard, opts.Repo with
                        | [], Some name when name.Contains "/" ->
                            let parts = name.Split('/')
                            [ parts.[0], parts.[1] ]
                        | [], Some name -> [ ctx.Owner, name ]
                        | rs, _ -> rs

                    let mutable failure: Errors.IoError option = None

                    // Off-board open issues, WITH their bodies (as `who` keeps them): one list read serves both
                    // the candidate scan and the `Rooms:` extraction below, so widening the subject set to
                    // coordination rooms (ADR-0051) costs no extra body read — only the message read per room.
                    let offBoard =
                        System.Collections.Generic.Dictionary<string * string * int, Reads.IssueBodyRead>()

                    for (o, r) in repos do
                        if failure.IsNone then
                            // FAILS CLOSED (#461): an unreadable scan is never an empty one. A mailbox that
                            // swallowed a failed issue-list read would report "no new mail" over a claim whose
                            // messages it never looked for.
                            match Reads.openIssues ctx.Transport o r with
                            | Error e -> failure <- Some e
                            | Ok issues ->
                                for issue in issues do
                                    offBoard.[(o, r, issue.Number)] <- issue.Body

                    let inProgressRefs =
                        boardRows
                        |> List.filter (fun r -> r.Status = BoardStatus.InProgress)
                        |> List.map (fun r -> r.Ref.Owner, r.Ref.Repo, r.Ref.Number)
                        |> Set.ofList

                    // ADR-0051 — the subject set WIDENS to the coordination ROOMS the in-flight items
                    // reference. A worker is in a room by holding a claim on an item that carries `Rooms: #R`,
                    // and folding #R in here — behind the SAME per-worker cursor — is the whole mechanism the
                    // ADR calls "the one real code delta". Derived from the bodies already in hand, so the
                    // added cost is one message read per DISTINCT room, the bound ADR-0051 sets. A room may
                    // live in a repo outside `--repo` scope; `Reads.messages` takes owner/repo/number
                    // directly, so a cross-repo room is still reached. The recipient filter below is unchanged,
                    // so a worker still sees only messages addressed to it or broadcast (`*`) — exactly as for
                    // items, which is why this widens the SUBJECTS without broadening delivery.
                    let rooms =
                        offBoard
                        |> Seq.collect (fun kv ->
                            let (o, r, _) = kv.Key

                            match kv.Value with
                            // .github#1794: a body we could not read names no rooms we know of — which is
                            // not the claim that it names none. It costs a subject, never a delivery: the
                            // ITEM itself stays in `candidates` (its key is in `offBoard.Keys` regardless),
                            // so no message on the item is lost; only a room it might have referenced is
                            // unreachable through this row, and any OTHER item referencing that room still
                            // folds it in.
                            | Reads.BodyUnread _ -> []
                            | Reads.BodyRead body -> Rooms.parse o r body)
                        |> Seq.map (fun rf -> rf.Owner, rf.Repo, rf.Number)
                        |> Set.ofSeq

                    let candidates =
                        if failure.IsSome then
                            []
                        else
                            Seq.append (Seq.append offBoard.Keys inProgressRefs) rooms
                            |> Seq.distinct
                            |> Seq.sortBy (fun (o, r, n) -> o, r, n)
                            |> List.ofSeq

                    // The cursor is the high-water mark. `maxId` advances past EVERY message seen — even one
                    // not addressed to us, even a broadcast we sent — so mail already read never re-surfaces;
                    // `delivered` is the subset the cursor's advance does NOT gate on: new (> the old cursor),
                    // not our own, and addressed to us or to everyone.
                    let since = Cache.inboxCursor w.Id
                    let mutable maxId = since
                    let delivered = ResizeArray<string * Reads.Message>()

                    for (o, r, n) in candidates do
                        if failure.IsNone then
                            // A FAILED READ IS A LOST MESSAGE, not an empty mailbox — fail closed over the
                            // in-flight set exactly as `who` does, so an unread warning is never reported as
                            // "no new mail".
                            match Reads.messages ctx.Transport o r n with
                            | Error e -> failure <- Some e
                            | Ok msgs ->
                                for m in msgs do
                                    if m.Id > maxId then
                                        maxId <- m.Id

                                    if m.Id > since && m.From <> w.Id && (m.To = w.Id || m.To = "*") then
                                        delivered.Add($"%s{r}#%d{n}", m)

                    match failure with
                    | Some e -> fail e
                    | None ->
                        // The default consumes what it shows (advance the cursor); `--peek` shows it and
                        // leaves the cursor, so the same mail is still new next time.
                        if not opts.Peek then
                            Cache.putInboxCursor w.Id maxId

                        let delivered = List.ofSeq delivered

                        match opts.Render with
                        | Json -> printfn "%s" (renderInboxJson delivered)
                        | Text ->
                            if List.isEmpty delivered then
                                printfn "no new messages for worker %s." w.Id
                            else
                                for (item, m) in delivered do
                                    printfn "── %s  %s → %s  (%s)" item m.From m.To m.At
                                    printfn "%s" m.Text
                                    printfn ""

                                // Say the cursor was NOT advanced, on stderr, so a `--peek` piped to a machine
                                // consumer still shouts that this read did not consume the mail it showed.
                                if opts.Peek then
                                    eprint "fsgg-coord-engine: --peek — cursor not advanced."

                        ExitGreen

    // `room open --over N,M` — open a coordination room over a contended cluster (ADR-0051). Creates the
    // room ISSUE (off the board — coordination scaffolding, not deliverable work) and writes a `Rooms:`
    // back-reference onto each named item, so their holders share the room's channel via `say`/`inbox`.
    // No lock is taken or required: a room is opened over other workers' items, exactly as `say` speaks
    // to them.
    let roomOpen (ctx: Context) (opts: Options) : int =
        match worker opts with
        | Error c -> c
        | Ok w ->
            if List.isEmpty opts.Over then
                eprint "fsgg-coord-engine: room open needs --over N,M (the items to open the room over)"
                ExitError
            else
                let parsed = opts.Over |> List.map (fun t -> t, parseRef ctx t)
                let bad = parsed |> List.choose (fun (t, r) -> match r with Error m -> Some(t, m) | Ok _ -> None)

                match bad with
                | _ :: _ ->
                    for (t, msg) in bad do
                        eprint $"fsgg-coord-engine: --over '%s{t}': %s{msg}"

                    ExitError
                | [] ->
                    let members =
                        parsed
                        |> List.choose (fun (_, r) ->
                            match r with
                            | Ok ref -> Some ref
                            | Error _ -> None)
                        |> List.distinct

                    match members with
                    | [] ->
                        eprint "fsgg-coord-engine: room open needs at least one item"
                        ExitError
                    | first :: _ ->
                        // A room is INTRA-REPO (ADR-0027 §5, which ADR-0051 amends): every member shares one
                        // repo, and the room lives there with them. This is not a mere convenience — it is what
                        // makes the derived close (ADR-0051 §4) SOUND. That close scans the room's own repo for
                        // surviving referrers; a member in another repo would be invisible to it, so completing
                        // the last same-repo member would close the room while the cross-repo member is still
                        // open (the one error §4 must never make). Refusing a cross-repo `--over` here is what
                        // keeps every tool-created room within a single repo, so the repo-local scan is exact.
                        // A cross-repo knot is cross-repo-coordination's domain (ADR-0001), not a room's.
                        let owner, repo = first.Owner, first.Repo

                        let strangers =
                            members |> List.filter (fun m -> m.Owner <> owner || m.Repo <> repo)

                        if not (List.isEmpty strangers) then
                            let named = strangers |> List.map (fun m -> $"%s{m.Owner}/%s{m.Repo}#%d{m.Number}") |> String.concat ", "

                            eprint
                                $"fsgg-coord-engine: room open is intra-repo (ADR-0027 §5) — these members are outside %s{owner}/%s{repo}: %s{named}. A cross-repo knot is cross-repo-coordination's domain (ADR-0001), not a room's."

                            ExitError
                        else

                        let memberList = members |> List.map (fun r -> r.Short) |> String.concat ", "
                        let title = $"coordination room over %s{memberList}"

                        let bodyText =
                            $"Coordination room (ADR-0051), opened by worker %s{w.Id} over %s{memberList}.\n\n"
                            + "Workers holding these items share this room's channel — reach it with `say` on this "
                            + "issue and read it with `inbox`. Membership and lifecycle are DERIVED from the "
                            + "`Rooms:` back-references on the items: this room closes itself when every referenced "
                            + "item is done. Record any touch-set agreement as a `widen` on the real items, not here.\n\n"
                            + "Paths: none"

                        match Writes.createRoom ctx.Transport owner repo title bodyText with
                        | Error e -> fail e
                        | Ok room ->
                            // Every member shares the room's repo (enforced above), so the back-reference is a
                            // bare `#n` — which `Rooms.parse`, defaulting a bare ref to the member's own repo,
                            // resolves to exactly this room.
                            let roomToken = $"#%d{room.Number}"

                            let mutable failure: Errors.IoError option = None

                            for m in members do
                                if failure.IsNone then
                                    match Reads.issueBody ctx.Transport m.Owner m.Repo m.Number with
                                    | Error e -> failure <- Some e
                                    | Ok mbody ->
                                        // Idempotent: a member already referencing the room keeps its one line
                                        // rather than growing a duplicate (the union in `Rooms.parse` would
                                        // collapse it anyway, but a clean body is worth the read).
                                        if Rooms.parse m.Owner m.Repo mbody |> List.contains room then
                                            ()
                                        else
                                            match Writes.writeRoomRef ctx.Transport m mbody roomToken with
                                            | Error e -> failure <- Some e
                                            | Ok() -> ()

                            match failure with
                            | Some e ->
                                eprint
                                    $"fsgg-coord-engine: room %s{room.Short} was created, but a `Rooms:` back-reference could not be written: %s{Errors.explain e}. The room exists; wire the remaining members by hand or re-run."

                                Errors.exitCode e
                            | None ->
                                printfn "opened room %s over %s" room.Short memberList
                                ExitGreen


    let bootstrapCmd (ctx: Context) (opts: Options) : int =
        // `--refresh` drops the day-cache so the next resolve is a real one — the remedy Snapshot.fs points
        // a worker at when the board schema changed under a warm cache.
        if opts.Fresh then
            Cache.dropBoardMap ctx.Owner ctx.Title

        match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
        | Error e -> fail e
        | Ok board ->
            printfn "bootstrapped board #%d '%s' in %s (%d fields)" board.Number board.Title board.Owner (Map.count board.Fields)
            ExitGreen

    let boardCmd (ctx: Context) : int =
        match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
        | Error e -> fail e
        | Ok board ->
            printfn "%s" (Board.boardToJson board)
            ExitGreen

    let fieldId (ctx: Context) (opts: Options) : int =
        match opts.Args with
        | [ field ] ->
            match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
            | Error e -> fail e
            | Ok board ->
                match Map.tryFind field board.Fields with
                | Some f ->
                    printfn "%s" f.Id
                    ExitGreen
                | None ->
                    eprint $"fsgg-coord-engine: no board field named '%s{field}'."
                    ExitError
        | _ ->
            eprint "fsgg-coord-engine: field-id takes <field>."
            ExitError

    let optionId (ctx: Context) (opts: Options) : int =
        match opts.Args with
        | [ field; option ] ->
            match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
            | Error e -> fail e
            | Ok board ->
                match Map.tryFind field board.Fields with
                | Some f ->
                    match f.Type with
                    | Board.SingleSelect options ->
                        match Map.tryFind option options with
                        | Some id ->
                            printfn "%s" id
                            ExitGreen
                        | None ->
                            eprint $"fsgg-coord-engine: field '%s{field}' has no option '%s{option}'."
                            ExitError
                    | _ ->
                        eprint $"fsgg-coord-engine: field '%s{field}' is not a single-select field."
                        ExitError
                | None ->
                    eprint $"fsgg-coord-engine: no board field named '%s{field}'."
                    ExitError
        | _ ->
            eprint "fsgg-coord-engine: option-id takes <field> <option>."
            ExitError

    let itemIdCmd (ctx: Context) (opts: Options) : int =
        match opts.Args with
        | [ refArg ] ->
            match parseRef ctx refArg with
            | Error msg ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | Ok ref ->
                match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
                | Error e -> fail e
                | Ok board ->
                    match Board.itemIdCached ctx.Transport board ref.Owner ref.Repo ref.Number with
                    | Error e -> fail e
                    | Ok(Some id) ->
                        printfn "%s" id
                        ExitGreen
                    // #421: a SUCCESSFUL read that found nothing is the only path that says "not on board".
                    // It is a definite answer, not a failure — but it is not an id, so it exits non-zero.
                    | Ok None ->
                        eprint $"fsgg-coord-engine: %s{ref.Short} is not an item on this board."
                        ExitError
        | _ ->
            eprint "fsgg-coord-engine: item-id takes <ref>."
            ExitError

    // `body-edits <ref>` — "has this issue/PR body changed since X" (`.github#2477`).
    //
    // `.github#2456`'s independent-review contract names GraphQL's `userContentEdits` connection as the
    // authoritative source for this question — REST's timeline carries no body-edit event at all, only
    // `renamed` for titles — and warns a critic off a hand-built `gh api graphql` call, which
    // `graphql-monopoly` refuses as an unmetered principal on the shared budget. This is the sanctioned,
    // metered way to ask the contract's own question: one `Reads.contentEditProvenance` call through the
    // existing client path, so it is budget-attributed exactly like every other read.
    //
    // FAILS CLOSED ON BOTH PROJECTIONS. `Reads.contentEditProvenance` never degrades a read it could not
    // complete into an empty connection; this handler carries that all the way to the exit code and both
    // renderers via `failWith opts.Render`, so a rate-limited or unauthorized read prints as a FAILED
    // READ — non-zero exit, an error on stderr (`--text`) or in the failure document (`--json`) — and
    // NEVER as "0 edits". Silently reporting a failed read as a negative is exactly the false negative
    // `.github#2456` was written to prevent; this command exists so a critic never has to choose between
    // asking the authoritative question and obeying `graphql-monopoly`.
    let bodyEditsCmd (ctx: Context) (opts: Options) : int =
        match opts.Args with
        | [ refArg ] ->
            match parseRef ctx refArg with
            | Error msg ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | Ok ref ->
                match Reads.contentEditProvenance ctx.Transport ref.Owner ref.Repo ref.Number with
                | Error e -> failWith opts.Render e
                | Ok provenance ->
                    match opts.Render with
                    | Json ->
                        let doc =
                            JsonSerializer.Serialize(
                                {| ref = ref.Short
                                   totalEdits = provenance.Total
                                   edits =
                                    provenance.Edits
                                    |> List.map (fun edit ->
                                        {| editedAt = edit.EditedAt.ToString "o"
                                           editor = edit.EditorLogin |> Option.toObj |}) |}
                            )

                        printfn "%s" doc
                    | Text ->
                        if provenance.Total = 0 then
                            printfn "%s: no body edits recorded (GraphQL userContentEdits totalCount 0)." ref.Short
                        else
                            printfn
                                "%s: %d body edit(s) recorded (GraphQL userContentEdits totalCount):"
                                ref.Short
                                provenance.Total

                            for edit in provenance.Edits do
                                let who = edit.EditorLogin |> Option.defaultValue "(editor unknown)"
                                printfn "  %s by %s" (edit.EditedAt.ToString "o") who

                            // TRUNCATED, LIKE `subIssues`'s graph — the connection is capped at 100, and a
                            // caller must be able to tell "5 edits, all listed" from "127 edits, 100 shown".
                            if provenance.Total > List.length provenance.Edits then
                                printfn
                                    "  (only %d of %d shown — the connection is capped at 100)"
                                    (List.length provenance.Edits)
                                    provenance.Total

                    ExitGreen
        | _ ->
            eprint "fsgg-coord-engine: body-edits takes <ref>."
            ExitError

    // The column `add` writes when the caller names none (.github#1823).
    //
    // **`Backlog`, and not `Ready`.** `Backlog` is visible to triage and NOT startable without a
    // deliberate promotion, which is `drive-board`'s existing backlog-triage contract — it promotes only
    // evidenced actionable work to `Ready`. Defaulting to `Ready` would auto-schedule work nobody has
    // read; defaulting to NOTHING was the defect (14 rows in one day, every one found by accident).
    [<Literal>]
    let private AddDefaultStatus = "Backlog"

    // `add <ref>` — put an issue on the board (#861).
    //
    // The verb `check-graphql-monopoly` (#586) names as the compliant alternative to `gh project
    // item-add`, and the one the port dropped — so the rule spent its life with no path that obeyed it.
    // The raw `gh` call spends the shared 5,000 pt/hr fleet budget with nothing to meter, cache or refuse
    // it; this goes through the one transport, which does all three.
    //
    // Idempotent by #421's rule and not by a `try`: see `Board.addItem`. Re-running it is free of a write.
    //
    // ---- THE STATUS DEFAULT (.github#1823) --------------------------------------------------------
    //
    // `add` used to leave `Status` UNSET, and a row with no `Status` is invisible to every scheduler —
    // `Schedulability` says so in as many words: *"no Status on the board: invisible to every scheduler,
    // and nobody set it."* Fourteen rows were filed that way on 2026-07-28, in three batches, and EVERY
    // instance was found by accident by a driver reading `batch` output for an unrelated reason. Nothing
    // reported any of them. Each was filed in good faith by a worker discharging a real item and
    // following the documented flow — file the finding, `add` it to the board. The step that made the row
    // schedulable was undocumented at the point of use and silently optional, which is #1644's subject
    // arriving one layer down: no scheduler reads prose, and an unscheduled row is prose.
    //
    // **THE DEFAULT ONLY EVER FILLS AN EMPTY COLUMN, AND THAT IS THE WHOLE RISK OF THIS CHANGE.** `add`
    // is idempotent (#861) — a close-out pass, a retry, or two workers racing the same follow-up all
    // reach it — so a naive "set Status on add" would walk a live `In progress` row back to `Backlog` and
    // DESTROY information rather than add it. That is the one direction this can be wrong, and it is why
    // the already-on-board arm READS the column before it decides, and why an unreadable column is left
    // alone rather than defaulted (#266: a column we could not read is not a column we may call empty).
    //
    // **THERE IS ONE ARM, NOT TWO, AND THAT IS DELIBERATE.** A freshly-added row looks like it needs no
    // read — but `AddedToBoard` does not mean "the item is new". `Board.addItem`'s own docstring records
    // that `addProjectV2ItemById` is idempotent SERVER-side and returns the EXISTING item's id for an
    // issue already on the board, so the case really means "the lookup did not find it" — and that
    // lookup is `projectItems(first: 20)`, unpaginated. A successful read can therefore miss a row that
    // IS on the board carrying a live column, and the skipped read would have overwritten it. One
    // GraphQL point on a once-per-filing verb (#418) buys that away.
    //
    // **AND IT SAYS SO.** Silence is how the defect worked: a filer who is told nothing assumes the row
    // is schedulable. The stderr note names the column and states that the row is NOT startable yet.
    //
    // **AN EXPLICIT `--status` IS VALIDATED BEFORE THE ADD**, against the options `bootstrapCached` has
    // already resolved — zero GraphQL, and no mutation spent on a value that cannot land. The Status
    // write is non-fatal (the row is boarded, so a red would send a filer back to re-run `add`), which
    // is right for a default nobody asked for and wrong for an instruction: a bad `--status` would
    // otherwise board the row, exit 0, and leave it with no column at all — this change's own flag
    // producing the very row it exists to prevent.
    //
    // COST, over `Board.addItem`'s own (3 GraphQL on a real add, 1 when it is already there): **+3
    // measured**, on every path — the `itemStatus` read, the item-id lookup `boardWrite` re-resolves for
    // itself, and the mutation. (`boardWrite` takes owner/repo/number, not the id `addItem` just
    // returned, so that lookup is redundant and is charged anyway; retiring it is a change to
    // `Board.boardWrite`'s signature and belongs to whoever owns that file.) Two of the three are not
    // spent when the column is already set — the read answers, and nothing is written.
    let addCmd (ctx: Context) (opts: Options) : int =
        match opts.Args with
        | [ refArg ] ->
            match parseRef ctx refArg with
            | Error msg ->
                eprint $"fsgg-coord-engine: %s{msg}"
                ExitError
            | Ok ref ->
                // THE CLASS GATE, BEFORE THE ROW EXISTS (.github#1651 AC1).
                //
                // `add` is the engine's FIRST sight of an item: the issue itself is written with `gh`, so
                // there is no earlier place a closed vocabulary can be enforced. Two workers in one run,
                // in two repos, wrote `Class: docs` and `Class: enhancement` — both believed they were
                // classing the row, nothing told them the set was closed, and each row then sat on the
                // board reading as UNCLASSED, which ADR-0066 counts as a POSSIBLE defect and which
                // therefore blocked a clean termination read until a human read the body by hand.
                //
                // IT REFUSES; IT DOES NOT CORRECT. Mapping `docs` onto the nearest of three would be the
                // guess #1588's AC3 forbids, wearing a writer's authority instead of a parser's.
                //
                // AND IT FAILS CLOSED (#266): a body we could not READ is not a body we may declare
                // clean. `add` costs one REST read it did not cost before, which is the price of the
                // check being at the only moment the author is still standing there. Nothing has been
                // written when this refuses, so the remedy is: fix the line, re-run `add`. `add` is
                // idempotent (already-on-board is a success), so a retry is free.
                //
                // IT DOES NOT REQUIRE A CLASS. An item with no `Class:` line boards, and `lint`'s
                // `CLASS-UNSET` reports it — refusing THAT would turn "filed but never boarded" into a
                // routine outcome, and an unboarded row is invisible to every scheduler rather than
                // merely untriaged.
                match Reads.issueBody ctx.Transport ref.Owner ref.Repo ref.Number with
                | Error e -> fail e
                | Ok body ->

                match outOfVocabularyClass body with
                | Some detail ->
                    eprint $"fsgg-coord-engine: refusing to board %s{ref.Short} — %s{detail} Fix the body line and re-run `add`."
                    ExitError
                | None ->

                let laneOfOneWarning =
                    match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
                    | Error e -> Some($"could not inspect sibling declarations ({Errors.explain e})")
                    | Ok board ->
                        match Scan.board ctx.Transport Cache.Reconciling ctx.Owner ctx.Title board.Number with
                        | Error e -> Some($"could not inspect sibling declarations ({Errors.explain e})")
                        | Ok rows ->
                            match Scan.snapshot ctx.Transport rows (Some ref.Repo) true None opts.LeaseMinutes with
                            | Error e -> Some($"could not inspect sibling declarations ({Errors.explain e})")
                            | Ok(doc, _) ->
                                match Snapshot.parse doc with
                                | Error errors ->
                                    let detail = sprintf "%A" errors
                                    Some($"could not inspect sibling declarations ({detail})")
                                | Ok request ->
                                    let siblings = filingLaneOfOne ref (TouchSet.parse body) (request.Candidates |> List.map _.Item)
                                    if List.isEmpty siblings then None
                                    else
                                        let names = siblings |> List.map _.Short |> String.concat ", "
                                        Some($"its Paths: declaration strictly contains {names}. A directory token reserves future files beneath it too, so this is a lane of one; narrow the holding declaration or sequence the work.")

                laneOfOneWarning |> Option.iter (fun warning -> eprint $"fsgg-coord-engine: filing advisory for %s{ref.Short} — %s{warning}")

                // .github#2305/ADR-0044 — ADVISORY, not a refusal, and DELIBERATELY not the same shape as
                // `updateTouchSet`'s hard refusal. `add` boards a body ALREADY WRITTEN on GitHub (`gh issue
                // create`, or a human/host editor); there is no `--paths` write for this command to refuse
                // — the declaration to react to already exists. Refusing to board it entirely would strand
                // an authored issue off every scheduler until somebody edits the live issue body to fix
                // it, and an issue-body edit invalidates that item's delivery-route receipt `subjectRevision`
                // (`pnext-item`'s own binding rule) — a strictly worse remedy than a stderr note for what
                // is, today, silent: `.github#2216` is open right now with both
                // `registry/driver-skill-manifest.json` and `registry/coordination-kit-skill-manifest.json`
                // verbatim in its live `Paths:`, filed before this warning existed and unnoticed until the
                // critic's repair-1 review found it. This closes that half of the gap — a filer/host now
                // learns immediately, on the same stderr surface `laneOfOneWarning` already uses — while
                // `widen`/`set-paths` (`updateTouchSet` above) remain the hard gate against a NEW
                // declaration, which is the door this engine actually controls.
                let generatedTokenWarning =
                    let generated =
                        match KitDigest.kitRoot () with
                        | Some root -> generatedPathCollector root
                        | None -> Set.empty

                    match TouchSet.generatedTokens generated (declaredPathTokens (TouchSet.parse body)) with
                    | [] -> None
                    | tokens ->
                        let named = String.concat ", " tokens

                        Some(
                            $"its Paths: declaration names {named} — a generated, CI-gated artifact (ADR-0044): nobody authors it, so declaring it reserves nothing, and a later widen naming it will be refused for the same reason. Narrow the Paths: line on the issue."
                        )

                generatedTokenWarning
                |> Option.iter (fun warning -> eprint $"fsgg-coord-engine: filing advisory for %s{ref.Short} — %s{warning}")

                match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
                | Error e -> fail e
                | Ok board ->

                // AN EXPLICIT `--status` IS CHECKED BEFORE ANYTHING IS WRITTEN, and it costs zero
                // GraphQL: `bootstrapCached` has already resolved the column's options, so the check is
                // free and it precedes the add.
                //
                // Checked here, and not left to the write, because of what the write does with a refusal.
                // The Status write is deliberately NON-FATAL — the row is boarded, so a red would send a
                // filer back to re-run `add` rather than to the field write actually owed — and that is
                // right for a DEFAULT nobody asked for. It is wrong for an instruction: `add --status Redy`
                // would board the row, print the id, note the refusal, and exit 0, leaving a row with NO
                // column at all. The flag added to close "a boarded row invisible to every scheduler"
                // would itself be a way to produce one, on a green exit. `set-field` exits non-zero for
                // the same value, and this must not be the weaker verb it says it is.
                //
                // NOTHING HAS BEEN WRITTEN when this refuses, and the remedy is: fix the value, re-run.
                // `add` is idempotent, so the retry is free — `outOfVocabularyClass` above, exactly.
                let explicitStatus =
                    match opts.Status with
                    | None -> Ok None
                    | Some name ->
                        match Map.tryFind "Status" board.Fields with
                        | Some { Type = Board.SingleSelect options } when Map.containsKey name options -> Ok(Some name)
                        | Some { Type = Board.SingleSelect options } ->
                            let known = options |> Map.keys |> String.concat ", "

                            Error
                                $"'%s{name}' is not a column on this board's `Status` field. Known columns: %s{known}. Nothing was written — fix the value and re-run `add` (it is idempotent, so the retry is free)."
                        | Some _ -> Error "this board's `Status` field is not a single-select, so `--status` cannot name a column on it."
                        | None ->
                            let known = board.Fields |> Map.keys |> String.concat ", "

                            Error $"this board has no `Status` field at all, so `--status` names nothing. Known fields: %s{known}."

                match explicitStatus with
                | Error detail ->
                    eprint $"fsgg-coord-engine: refusing to board %s{ref.Short} — %s{detail}"
                    ExitError
                | Ok explicitStatus ->

                // #2109: `add --status Blocked` is a Status writer, not merely an add with a
                // convenient flag. Establish the coherent-park invariant BEFORE item-add: after it,
                // the otherwise-invalid board row already exists. Reuse the shared gate so the live
                // `Blocked by` field and the human sentinel keep exactly the same meaning as the
                // other explicit Status=Blocked doors.
                match requireCoherentParkIfBlocked ctx ref (if explicitStatus = Some "Blocked" then Some BoardStatus.Blocked else None) with
                | Error c -> c
                | Ok() ->

                // .github#2698 AC1 — `add --status Ready` is a Status writer, exactly as `--status
                // Blocked` is directly above, and it establishes its precondition in the same place and
                // for the same reason: BEFORE item-add, because after it the otherwise-invisible board row
                // already exists and `add` is documented as green once the row is boarded, so a later
                // refusal could not undo it.
                //
                // AC2 — THE OTHER COLUMNS ARE UNTOUCHED. `explicitStatus` is `None` for a bare `add`, so
                // the #1823 `Backlog` default below never reaches this gate, and neither does `--status
                // Backlog` or `--status Blocked`. A row that is deliberately not yet schedulable owes no
                // route decision; that is the whole point of parking it.
                match requireCurrentRouteIfReady ctx ref (if explicitStatus = Some "Ready" then Some BoardStatus.Ready else None) with
                | Error c -> c
                | Ok() ->

                match Board.addItem ctx.Transport board ref.Owner ref.Repo ref.Number with
                | Error e -> fail e
                | Ok outcome ->

                // ALREADY THERE IS A SUCCESS, and it exits 0. `add` is the second line of the recipe's
                // filing procedure, so a close-out pass, a retry, or two workers racing the same
                // follow-up all reach it — and none of them is an error. It says so on stderr and puts
                // the id on stdout, so a caller piping it gets an id either way.
                let itemId =
                    match outcome with
                    | Board.AlreadyOnBoard id ->
                        eprint $"fsgg-coord-engine: %s{ref.Short} is already on board '%s{ctx.Title}'."
                        id
                    | Board.AddedToBoard id ->
                        eprint $"added %s{ref.Short} to board '%s{ctx.Title}'."
                        id

                // THE ID GOES OUT BEFORE THE COLUMN IS SETTLED, unconditionally. `add`'s promise is
                // "this issue is on the board, here is its item id", and that promise is already kept
                // by the time we get here. Every Status outcome below is a note ABOUT a row that is
                // boarded; none of them may swallow the id a caller is piping.
                printfn "%s" itemId

                // THE IDENTITY IS RESOLVED HERE, AFTER THE ROW IS BOARDED, AND NEVER BEFORE IT.
                //
                // The deferral queue is keyed on the worker id, so a board write needs one — but #1823
                // explicitly REFUSED to make `add` refuse: *"`add` is called mid-item by a worker who
                // has just found something outside its touch-set, and a refusal at that moment is how a
                // finding ends up in a report instead of on the board."* Resolving before the add would
                // turn a working `add` into a refusal for any caller with no identity ladder, which is
                // that same failure wearing this change's badge. So the column is what degrades, loudly
                // and by name, and `add`'s own promise is untouched.
                //
                // Through `worker`, not `Identity.resolve`, so the #419 shared-session WARNING fires here
                // as it does on every other verb that writes. `add` is now one of them, and a write verb
                // that alone stays silent about a shared id is the one place the warning is missing.
                let write (value: string) (why: string) : int =
                    match worker opts with
                    | Error _ ->
                        eprint
                            $"fsgg-coord-engine: %s{ref.Short} IS on the board (its id is on stdout), but Status was NOT set to '%s{value}' — a board write is queued against a worker id and this process could not derive one. The refusal above says how to fix it; then:  scripts/fsgg-coord set-field %s{ref.Short} Status %s{value}"

                        ExitGreen

                    | Ok w ->

                    let writeOutcome =
                        Board.boardWrite ctx.Transport board ref.Owner ref.Repo ref.Number "Status" (Board.Set value) w.Id

                    match writeOutcome with
                    | Ok Board.Written ->
                        eprint $"fsgg-coord-engine: Status=%s{value} on %s{ref.Short} — %s{why}"

                        // .github#2690 direction C — THE ONE WITH NO OPERATOR IN IT AT ALL, and the reason
                        // this arm covers the #1823 DEFAULT and not only `--status`. The stderr line
                        // immediately above promises a freshly filed row is *"VISIBLE to triage, but NOT
                        // startable … promoting it there is a deliberate act"*. That sentence was false:
                        // nothing recorded the park, so the next `reconcile --apply` pass derived `Auto`
                        // from the row's own declared paths and promoted it, with no operator anywhere in
                        // the loop. `#2678`, `#2679`, `#2683`, `#2684` and `#2688` all read `Ready` within
                        // the hour of being filed to `Backlog`. Recording the intent is what makes the
                        // promise above true.
                        //
                        // `add`'s verdict stays GREEN for the reason its own `| _ ->` arm gives: the row IS
                        // boarded, which is what `add` was asked to do, and reddening here would send a
                        // filer back to re-run `add` rather than to the one write that is owed. The
                        // consequence is on stderr, named, with the command that finishes it.
                        // A SHORT, STABLE REASON — never `why`. `why` is the paragraph this verb prints to a
                        // human; the reason is `Uri.EscapeDataString`d into a wire marker that every later
                        // reconcile pass re-reads, and splicing four hundred characters of prose into it
                        // would make the row's own receipt unreadable for no gain.
                        let intentReason =
                            match explicitStatus with
                            | Some _ -> "explicit add --status"
                            | None -> "add filed this row to Backlog pending triage (#1823)"

                        match Reads.statusOfName value with
                        | None -> ExitGreen
                        | Some status ->
                            match recordExplicitStatusIntent ctx ref status intentReason with
                            | Ok() -> ExitGreen
                            | Error reason ->
                                eprint (explicitStatusIntentFailure ref status reason)
                                eprint
                                    $"fsgg-coord-engine: the row IS boarded and its Status IS set, so `add` is green — but re-run the column write to record the intent:  scripts/fsgg-coord set-field %s{ref.Short} Status %s{value}"

                                ExitGreen
                    | _ ->
                        // Deferred / NotOnBoard / a failed mutation. `boardWriteNote` names what did
                        // NOT land and the exact command that finishes it. The verdict stays green:
                        // the ROW IS BOARDED, which is what `add` was asked to do and what its stdout
                        // now says — and reporting a red here would send a filer back to re-run `add`
                        // rather than to the one-field write that is actually owed. (An explicit
                        // `--status` cannot reach here on a bad VALUE — that was refused above, before
                        // the add — so what remains is genuinely a transport or budget condition.)
                        boardWriteNote ref "Status" value writeOutcome
                        ExitGreen

                match explicitStatus with
                // AC2 — AN EXPLICIT STATUS STILL WINS. `--status` is the caller naming the column, so
                // it is written whatever is there: this is `set-field <ref> Status <S>` reached from
                // `add`, and #1823 makes only the DEFAULT conditional. A flag accepted and then
                // silently declined would be #867's defect, on the flag #867 is about.
                | Some explicit ->
                    write explicit "you named it with --status (an explicit column always wins over the #1823 default)."

                | None ->

                // AC4 — THE IDEMPOTENCE ARM, AND IT IS THE ONLY ARM. Read the column, then prefer
                // whatever is already there. This is what a "just set Status on add" change gets wrong.
                //
                // THE FRESHLY-ADDED CASE USED TO SKIP THIS READ and it was wrong to. The justification was
                // "a new project item has no field values, and #421's guard means `AddedToBoard` only
                // follows a definite not-on-board read" — but `Board.addItem`'s own docstring records the
                // opposite about the mutation: `addProjectV2ItemById` is idempotent SERVER-side and
                // returns the EXISTING item's id for an issue already on the board. So `AddedToBoard`
                // means "the lookup did not find it", never "the item is new" — and that lookup is
                // `projectItems(first: 20)` with no pagination, so a successful read can miss a row that
                // is on the board carrying a live column. One unpaginated miss and the default would have
                // overwritten it. That is the ONE direction this change destroys information, asserted to
                // be impossible rather than made so. It costs one GraphQL point on a once-per-filing verb
                // (#418) to stop asserting it.
                match Board.itemStatus ctx.Transport board ref.Owner ref.Repo ref.Number with
                | Error e ->
                    // #266. NOT MEASURED is not `Ok None`. We could not read the column, so we may
                    // not assert it is empty, and defaulting on an unread column is exactly how
                    // this change would destroy information instead of adding it.
                    eprint
                        $"fsgg-coord-engine: %s{ref.Short} is on the board, but its Status could NOT BE READ (%s{Errors.explain e}) — so the #1823 default was NOT applied. That is a read that did not happen, not an empty column, and defaulting over one would overwrite whatever is really there. Check it:  scripts/fsgg-coord ready --all --repo %s{ref.Repo}"

                    ExitGreen

                | Ok(Some existing) ->
                    eprint
                        $"fsgg-coord-engine: %s{ref.Short} already has Status='%s{statusWireName existing}' — LEFT AS IT IS. The #1823 default only ever fills an EMPTY column, so re-running `add` never walks a row somebody set back to Backlog."

                    ExitGreen

                | Ok None ->
                    write
                        AddDefaultStatus
                        $"the #1823 default, because you named none. The row is ON the board and VISIBLE to triage, but NOT startable: a scheduler takes `Ready`, and promoting it there is a deliberate act. Use `--status <column>` to choose, or `set-field %s{ref.Short} Status Ready` once it is triaged."
        | _ ->
            eprint "fsgg-coord-engine: add takes <ref> (a URL, owner/repo#n, or repo#n)."
            ExitError

    // ---- flush: the replay every deferral already promised (#862) --------------------------------------
    //
    // `Board.boardWrite` QUEUES a field write that meets an exhausted budget and returns `Deferred`; the
    // verb then prints "QUEUED — flush replays it" and exits `EX_RATE`. That message was true of the
    // LIBRARY and false of this CLI. The queue (`Cache.defer`/`pending`/`dropPending`) and the replay
    // (`Board.flush`) were both here, complete and tested — nothing exposed the verb, so `flush` was an
    // unknown command and the promise could not be kept by anyone.
    //
    // THAT IS THE WORST DIRECTION FOR IT TO BE WRONG, and it is why this is a bug and not a missing
    // feature. `EX_RATE` means back off and retry; "QUEUED, flush replays it" means the retry is already
    // arranged. A worker who reads both correctly concludes there is NOTHING TO DO — and the write is
    // gone. The board is a projection of issue state, so a dropped `set-field`/`done --flip` leaves the
    // projection lying, and the cost lands on a later reconcile pass rather than on the write (#510).
    //
    // The rendering rule (#266): "3 queued" and "3 replayed" are DIFFERENT SENTENCES. A dry run must never
    // be readable as a flush, which is the whole reason `--dry-run` says NOT replayed in those words.

    let flushCmd (ctx: Context) (opts: Options) : int =
        // READ THE QUEUE FIRST, AND BEFORE ANY BOARD READ. An empty queue needs no board map, and
        // `bootstrapCached` can spend GraphQL — which is precisely what we do not have when a flush is
        // called for. The overwhelmingly common case (nothing pending) must cost nothing.
        match Cache.pending () with
        | Error e -> fail e

        // AN EMPTY QUEUE IS A SUCCESS, and it exits 0. `flush` is a recovery verb: a close-out pass, a
        // retry, or a worker following the `EX_RATE` advice on a budget that recovered before they got
        // here all reach it, and none of them is an error.
        | Ok [] ->
            printfn "flush — nothing pending."
            ExitGreen

        | Ok entries when opts.DryRun ->
            // WHICH BOARD IS THIS OWED TO? A dry run is the read that must work when NO board read can — an
            // exhausted budget is the only reason a queue exists — so "which board?" has to be answerable
            // exactly here (#966). It still costs zero GraphQL: the target is recorded ON the entry (#882),
            // and `ctx.Owner`/`ctx.Title` are the environment's, so this compares two things already in hand.
            let here = ctx.Owner, ctx.Title

            let elsewhere (e: Cache.Deferred) =
                match e.Board with
                | Some b when not (Cache.sameBoard b here) -> Some b
                | _ -> None

            printfn
                "flush --dry-run — %d write(s) queued, NOT replayed. A flush here writes %s/%s:"
                (List.length entries)
                ctx.Owner
                ctx.Title

            for e in entries do
                // A SKIP AND A REPLAY MUST NOT RENDER ALIKE, which is the whole of this item: the engine has
                // told a skip from a drop since #963, and every entry printed here looked identical anyway.
                let note =
                    match elsewhere e with
                    | Some(o, t) -> sprintf "  -> queued against %s/%s; a flush HERE would SKIP it" o t
                    | None ->
                        match e.Board with
                        // Pre-#882: no board recorded, replayed against the current one — the behaviour it
                        // was queued under. Said out loud, because it is the one entry whose target is a
                        // default rather than a fact.
                        | None -> "  -> no board recorded (pre-#882); would replay against this board"
                        | Some _ -> ""

                printfn
                    "  %s %s = %s  (queued %s by %s)%s"
                    e.Ref
                    e.Field
                    (if e.Value = "" then "<cleared>" else e.Value)
                    e.At
                    e.Worker
                    note

            // The remedy that WORKS, next to the count it explains. Re-running flush here can never land
            // another board's entry, so "re-run after the reset" would be advice that does not fix the
            // number it names.
            match entries |> List.choose elsewhere |> List.distinct with
            | [] -> ()
            | boards ->
                let n = entries |> List.filter (elsewhere >> Option.isSome) |> List.length

                eprint (
                    sprintf
                        "fsgg-coord-engine: %d of these write(s) are queued against another board (%s) and CANNOT land here — they are still owed, not lost. Point FSGG_COORD_OWNER/FSGG_COORD_PROJECT at that board and flush again."
                        n
                        (boards |> List.map (fun (o, t) -> $"%s{o}/%s{t}") |> String.concat ", ")
                )

            ExitGreen

        | Ok _ ->
            match Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title with
            | Error e -> fail e
            | Ok board ->

                // EVERY COUNT BELOW COMES FROM THE PASS ITSELF. An earlier cut of this read the queue here
                // and again afterwards to infer what had landed — and the deferral queue is ONE file shared
                // by every worker on the machine, so a concurrent `defer` in that window made the inference
                // wrong in the worst direction: "nothing replayed" over writes that HAD landed. Nothing is
                // reconstructed now.
                match Board.flush ctx.Transport board with
                | Error e -> fail e
                | Ok r ->
                    printfn "flush — replayed %d of %d queued write(s)." r.Written r.Queued

                    // A DROP IS NOT A WRITE, AND IT IS NOT AN ERROR EITHER. `Board.flush` drops what it can
                    // never land — an unparseable ref, an item no longer on the board — rather than
                    // retrying it forever. Saying so is the difference between a queue that drained and a
                    // queue that gave up quietly.
                    if r.Dropped > 0 then
                        eprint
                            $"fsgg-coord-engine: %d{r.Dropped} queued write(s) were DROPPED, not replayed — permanently un-writable (an unparseable ref, or an item no longer on this board)."

                    // A SKIP IS THE OPPOSITE OF A DROP, and until now the CLI told the worker neither (#966).
                    // `replayed 0 of 1` and nothing else is exactly the "my write did not replay and nothing
                    // said why" that #882 felt like from the outside — the impression this count exists to
                    // end. A dropped write is gone; a skipped one is still owed and still landable, just not
                    // by this pass against this board, so it gets its own line and its own remedy.
                    if r.Skipped > 0 then
                        eprint
                            $"fsgg-coord-engine: %d{r.Skipped} queued write(s) were SKIPPED, not replayed — they are queued against a DIFFERENT board, so this pass left them alone. They are still owed and still landable: point FSGG_COORD_OWNER/FSGG_COORD_PROJECT at that board and flush again (`flush --dry-run` names it)."

                    match r.Stopped with
                    | None -> ExitGreen

                    // A PARTIAL IS REPORTED AS A PARTIAL. A bare `fail e` here would say "budget exhausted"
                    // over a flush that DID land writes, and the worker could not tell "nothing landed"
                    // from "most landed" — the same could-not-tell that #862 is about.
                    | Some e ->
                        // SUBTRACT THE SKIPS (#966). `Queued - Written - Dropped` stays TRUE — a skipped
                        // entry does remain queued — but it is the wrong number to attach to "re-run flush
                        // after the reset", because a re-run HERE can never land another board's entry. The
                        // advice would not fix the count it names. The skips are reported above, with the
                        // remedy that does work; this number is the one the reset actually clears.
                        let remaining = r.Queued - r.Written - r.Dropped - r.Skipped

                        eprint
                            $"fsgg-coord-engine: the budget ran out mid-flush; %d{remaining} write(s) REMAIN QUEUED — re-run flush after the reset."

                        fail e

    // ---- lint: the board-health gate (#496) -----------------------------------------------------------
    //
    // The rule whose absence let `lint` report `0 error(s)` over a DEAD queue. A Ready/Backlog item that
    // declares no schedulable touch-set is refused by `batch`/`take` — correctly, an undeclared touch-set
    // cannot be proven disjoint — and then sits on the board looking like work no worker can ever pick up.
    // NO-TOUCH-SET names the omission; BAD-TOUCH-SET names the item that DECLARED a touch-set the scheduler
    // cannot use (every token unmatchable). Both are the same condition — "no worker can ever pick this up"
    // — so both are errors. `Paths: none` is the deliberate out (an epic, a decision item), and it is the
    // whole point: "deliberately undeclared" must be sayable, or no gate can tell it from "somebody forgot".
    // A RECONCILER read: always fresh, never the cache.
    //
    // The epic ROLL-UP-graph rules (EPIC-*, DONE-STATUS-OPEN-ISSUE, the PR-probing EPIC-UNLINKED-CHILD) SHIP —
    // they read the sub-issue graph and parse body child-refs, which the touch-set rules do not.
    //
    // They divide into two kinds, and the split is the thing to hold onto. The `error` rules name an epic that
    // CANNOT roll up (no children, truncated graph, an unlinked declared child, acceptance delegated to nobody
    // or stated nowhere) — each is a defect with a mechanical remedy. `EPIC-ROLLUP-READY` is a `note` naming
    // the opposite: every mechanical precondition holds. It is still not a verdict that the epic is done,
    // because that verdict is `Discharge` (#614) and no read can reach it.
    // Compatibility seams retained for existing callers while the lint command delegates its
    // deterministic verdicts to the extracted application service.

    let issues (ctx: Context) (opts: Options) : int =
        match opts.Args with
        | [] ->
            eprint "fsgg-coord-engine: issues: a repo is required (a registry short-id, owner/repo, or a repo name)."
            ExitError
        | raw :: _ ->
            // bash's `issues` split: a slashed token is an explicit owner/repo (kept whole), a bare token is
            // a short-id resolved against the board owner. Note this does NOT run an owner/repo back through
            // resolveRepo — an explicit owner is authoritative, exactly as bash's `${1%%/*}`/`${1#*/}` reads.
            let owner, repo =
                match raw.IndexOf('/') with
                | -1 -> ctx.Owner, resolveRepo raw
                | i -> raw.Substring(0, i), raw.Substring(i + 1)

            let state = opts.IssueState |> Option.defaultValue "open"

            match Reads.issues ctx.Transport owner repo state opts.Label opts.Fresh with
            | Ok body ->
                printfn "%s" (body.TrimEnd())
                ExitGreen
            // `fail`, NOT a hand-rolled `explain` + `ExitError`. This arm was the latter, and the two are
            // not the same: `fail` returns the ERROR'S OWN code, so a rate limit exits EX_RATE (75) — the
            // back-off signal — while `ExitError` flattened it to a generic 1, the code a caller reads as a
            // PERMANENT protocol error. `fail`'s own docstring names the failure exactly: "a caller that
            // saw a generic 1 would treat a temporary condition as permanent."
            //
            // It bit hardest here of all places. `issues` is the REST-only read `/pnext-item` §4 sends
            // every worker to for its dedupe step *because* REST survives a GraphQL outage — so it is the
            // one command still standing when GraphQL dies, and the one that dies alone when REST goes
            // instead (2026-07-16: REST core 0/5000, GraphQL 3,639 spare). Either way it reported the one
            // condition worth retrying as the one condition never worth retrying.
            | Error e -> fail e

    // #2134's receipt-first intake transaction. The receipt is persisted immediately after the only
    // REST create, so a retry repairs the same issue's projection rather than issuing a second POST.
    // Execute the live receipt-first intake transaction.  This is public so the transaction can be
    // driven over a recording transport: the tests must count the issue-create POST, not infer it
    // from a later board result.
    let intakeCmd (ctx: Context) (opts: Options) : int =
        match opts.Args with
        | [ action; path ] ->
            match IntakeApplication.readDraft path, action with
            | Error reason, _ -> eprint $"fsgg-coord-engine: intake: %s{reason}"; ExitError
            | Ok draft, "validate" ->
                match Intake.validate draft with
                | Ok _ ->
                    printfn "{\"schema\":\"fsgg.coord.intake-result/v1\",\"kind\":\"validated\",\"draftId\":%s,\"writes\":0}" (JsonSerializer.Serialize draft.Id)
                    ExitGreen
                | Error findings -> eprint (findings |> List.map (fun f -> $"%s{f.Field} %s{f.Detail}") |> String.concat "; "); ExitError
            | Ok draft, "apply" ->
                match Intake.validate draft |> Result.bind (fun valid -> IntakeApplication.validateLivePaths valid |> Result.map (fun () -> valid) |> Result.mapError (fun reason -> [ { Intake.Finding.Field = "paths"; Detail = reason } ])) with
                | Error findings -> eprint (findings |> List.map (fun f -> $"%s{f.Field} %s{f.Detail}") |> String.concat "; "); ExitError
                | Ok _ ->
                    let dependencyGuard =
                        match draft.BlockedBy with
                        | None -> Ok()
                        | Some raw ->
                            match parseRefIn draft.Owner (Some draft.Repository) raw with
                            | Error reason -> Error(Errors.Malformed(draft.Id, $"Blocked dependency is not canonical: %s{reason}"))
                            | Ok dependency ->
                                Reads.blockerState ctx.Transport dependency.Owner dependency.Repo dependency.Number
                                |> Result.bind (function
                                    | Types.BlockerOpen -> Ok()
                                    | Types.BlockerClosed | Types.BlockerMerged -> Error(Errors.Malformed(draft.Id, $"Blocked dependency %s{dependency.Canonical} is already resolved"))
                                    | Types.BlockerUnknown | Types.BlockerUnparseable -> Error(Errors.Malformed(draft.Id, $"Blocked dependency %s{dependency.Canonical} is not live/readable")))
                    let receiptTransactionCore () =
                        match Cache.getIntakeReceipt draft.Id with
                        | Error e -> Error(Errors.Malformed(draft.Id, e))
                        | Ok(Some receipt) ->
                            IntakeReceipt.validate draft receipt
                            |> Result.mapError (fun message -> Errors.Malformed(draft.Id, message))
                            |> Result.map (fun r -> { Owner = r.Owner; Repo = r.Repository; Number = r.IssueNumber })
                        | Ok None ->
                            let digest = IntakeReceipt.digest draft
                            let intent =
                                Cache.getIntakeIntent draft.Id
                                |> Result.mapError (fun message -> Errors.Malformed(draft.Id, message))
                                |> Result.bind (function
                                    | None -> Ok None
                                    | Some stored when stored.Owner = draft.Owner && stored.Repository = draft.Repository && stored.DraftDigest = digest -> Ok(Some stored)
                                    | Some _ -> Error(Errors.Malformed(draft.Id, "intake intent does not match this draft")))
                            intent |> Result.bind (fun intent ->
                              Reads.duplicateCandidates ctx.Transport draft.Owner draft.Repository
                              |> Result.bind (fun candidates ->
                                let matches = candidates |> List.filter (fun c -> c.Title = draft.Title)
                                let provenance = IntakeReceipt.marker draft
                                let persist number =
                                    let receipt: IntakeReceipt.Receipt = { DraftId = draft.Id; Owner = draft.Owner; Repository = draft.Repository; IssueNumber = number; DraftDigest = digest }
                                    Cache.putIntakeReceipt receipt |> Result.mapError (fun message -> Errors.Malformed(draft.Id, message))
                                    |> Result.map (fun () -> { Owner = draft.Owner; Repo = draft.Repository; Number = number })
                                match draft.Disposition, matches, intent with
                                | Some Intake.Reuse, [ c ], _ -> persist c.Number
                                | Some Intake.Reuse, [], _ -> Error(Errors.Malformed(draft.Id, "reuse was selected but no duplicate candidate matches the title"))
                                | Some Intake.Reuse, _, _ -> Error(Errors.Malformed(draft.Id, "reuse is ambiguous because multiple duplicate candidates match the title"))
                                | Some Intake.Create, [ c ], Some _ when not c.IsPullRequest && c.Body.Contains(provenance, StringComparison.Ordinal) -> persist c.Number
                                | Some Intake.Create, _ :: _, _ ->
                                    Error(Errors.Malformed(draft.Id, "a duplicate candidate matches the title; select reuse or revise the draft"))
                                | Some Intake.Create, [], _ ->
                                    let intent: Cache.IntakeIntent = { DraftId = draft.Id; Owner = draft.Owner; Repository = draft.Repository; DraftDigest = digest }
                                    Cache.putIntakeIntent intent
                                    |> Result.mapError (fun message -> Errors.Malformed(draft.Id, message))
                                    |> Result.bind (fun () -> Writes.createIntake ctx.Transport draft)
                                    |> Result.bind (fun created -> persist created.Number)
                                | None, _, _ -> Error(Errors.Malformed(draft.Id, "draft disposition is missing"))))
                    let receiptTransaction () = dependencyGuard |> Result.bind (fun () -> receiptTransactionCore ())
                    let receiptResult =
                        Cache.withIntakeLock draft.Id receiptTransaction
                        |> Result.mapError (fun message -> Errors.Malformed(draft.Id, message))
                        |> Result.bind id
                    match receiptResult with
                    | Error e -> fail e
                    | Ok issue ->
                        let preProjectionReadyGuard =
                            if draft.Status <> "Ready" then Ok()
                            else
                                Reads.issueBody ctx.Transport issue.Owner issue.Repo issue.Number
                                |> Result.bind (fun body ->
                                    if body.Contains("Blocked by:", StringComparison.OrdinalIgnoreCase)
                                       || body.Contains("Blocked on: human/", StringComparison.OrdinalIgnoreCase)
                                       || body.Contains("Judgement question:", StringComparison.OrdinalIgnoreCase) then
                                        Error(Errors.Malformed(draft.Id, "Ready is refused while the reused/live issue still declares a dependency or human choice"))
                                    else
                                        match readDeliveryRouteVerdict ctx issue with
                                        | DeliveryRoute.Current _ -> Ok()
                                        | DeliveryRoute.Stale reasons
                                        | DeliveryRoute.Unreadable reasons ->
                                            Error(Errors.Malformed(draft.Id, "Ready is refused until a current delivery-route receipt exists: " + String.concat "; " reasons)))
                        match preProjectionReadyGuard, Reads.issueState ctx.Transport issue.Owner issue.Repo issue.Number, Board.bootstrapCached ctx.Transport ctx.Owner ctx.Title, worker opts with
                        | Error e, _, _, _ -> fail e
                        | _, Error e, _, _ -> fail e
                        | _, _, Error e, _ -> fail e
                        | _, _, _, Error code -> code
                        | Ok(), Ok issueState, Ok board, Ok w ->
                            let readyGuard =
                                if draft.Status <> "Ready" then Ok()
                                elif issueState = IssueState.Closed then Error(Errors.Malformed(draft.Id, "Ready is refused for a closed issue"))
                                else
                                    Reads.markerScan ctx.Transport issue.Owner issue.Repo issue.Number
                                    |> Result.bind (Reads.requireCompleteMarkerScan issue.Canonical)
                                    |> Result.bind (fun markers ->
                                        if not (List.isEmpty markers) then Error(Errors.Malformed(draft.Id, "Ready is refused while a claim is live"))
                                        else Reads.prAlive ctx.Transport issue.Owner issue.Repo issue.Number
                                             |> Result.bind (function
                                                 | Types.LeaseExpiredNoPr -> Ok()
                                                 | Types.LivenessUnknown -> Error(Errors.Malformed(draft.Id, "Ready eligibility could not verify implementation PR/branch absence"))
                                                 | _ -> Error(Errors.Malformed(draft.Id, "Ready is refused while an implementation PR or branch is live"))))
                            match readyGuard with
                            | Error e -> fail e
                            | Ok() ->
                            // Intake's validated draft carries the dependency/sentinel and createIntake
                            // persists it in the issue body. The atomic batch below establishes Status and
                            // Blocked by together; the legacy single-write preflight cannot require the
                            // not-yet-written board field without making coherent creation unreachable.
                            match (Ok(): Result<unit, int>) with
                            | Error code -> code
                            | Ok() ->
                                match Board.addItem ctx.Transport board issue.Owner issue.Repo issue.Number with
                                | Error e -> fail e
                                | Ok _ ->
                                    let writes =
                                        [ yield "Status", Board.Set draft.Status
                                          yield "Class", Board.Set draft.Class
                                          match draft.Phase with Some value -> yield "Phase", Board.Set value | None -> ()
                                          match draft.Severity with Some value -> yield "Severity", Board.Set value | None -> ()
                                          match draft.BlockedBy with Some value -> yield "Blocked by", Board.Set value | None -> () ]
                                    match Board.boardWriteBatch ctx.Transport board issue.Owner issue.Repo issue.Number writes w.Id with
                                    | Error e -> fail e
                                    | Ok Board.Deferred -> eprint "fsgg-coord-engine: intake projection is queued; retry after flush (no second POST)."; Errors.ExRate
                                    | Ok Board.NotOnBoard -> eprint "fsgg-coord-engine: intake add did not produce a readable board item."; ExitError
                                    | Ok Board.Written ->
                                        for field, write in writes do
                                            let value = match write with Board.Set value -> value | Board.Clear -> ""
                                            let queued: Cache.Deferred =
                                                { Ref = issue.Canonical; Field = field; Value = value; At = ""; Worker = w.Id
                                                  Board = Some(board.Owner, board.Title) }
                                            Cache.dropPending queued
                                        let readback =
                                            writes
                                            |> List.fold (fun state (field, write) ->
                                                state |> Result.bind (fun () ->
                                                    let expected = match write with Board.Set value -> Some value | Board.Clear -> None
                                                    Board.itemFieldValue ctx.Transport board issue.Owner issue.Repo issue.Number field
                                                    |> Result.bind (fun actual ->
                                                        if actual = expected then Ok()
                                                        else Error(Errors.Malformed(draft.Id, $"fresh %s{field} readback did not match the requested projection"))))) (Ok())
                                        match readback with
                                        | Error e -> fail e
                                        | Ok() ->
                                            let disposition = match draft.Disposition with Some Intake.Create -> "create" | Some Intake.Reuse -> "reuse" | None -> "unknown"
                                            let fields = writes |> List.map fst |> JsonSerializer.Serialize
                                            match Cache.pending () with
                                            | Error e -> fail e
                                            | Ok pending ->
                                                let boardIdentity = $"{{\"owner\":{JsonSerializer.Serialize board.Owner},\"title\":{JsonSerializer.Serialize board.Title},\"number\":%d{board.Number},\"id\":{JsonSerializer.Serialize board.Id}}}"
                                                let issueUrl = $"https://github.com/%s{issue.Owner}/%s{issue.Repo}/issues/%d{issue.Number}"
                                                printfn "{\"schema\":\"fsgg.coord.intake-result/v1\",\"kind\":\"applied\",\"draftId\":%s,\"issue\":%s,\"issueUrl\":%s,\"dedupeDisposition\":%s,\"board\":%s,\"status\":%s,\"fields\":%s,\"projectionFresh\":true,\"pendingWrites\":%d,\"judgementQuestion\":%s}" (JsonSerializer.Serialize draft.Id) (JsonSerializer.Serialize issue.Canonical) (JsonSerializer.Serialize issueUrl) (JsonSerializer.Serialize disposition) boardIdentity (JsonSerializer.Serialize draft.Status) fields pending.Length (JsonSerializer.Serialize draft.JudgementQuestion)
                                                ExitGreen
            | Ok _, _ -> eprint "fsgg-coord-engine: intake: expected validate or apply"; ExitError
        | _ -> eprint "fsgg-coord-engine: intake: usage intake <validate|apply> <draft.json>"; ExitError

    // Read the full evidence pair from GitHub.  The issue body is the source-bound subject and comments
    // are the append-only receipt ledger: a failure in either direction is not a missing decision.
    // `show` renders only the current receipt (`kind = "current"`, never a history), but validates the
    // complete append-only ledger before selecting it. This is the same fail-closed read used at the
    // scheduling and mutation boundaries.

    let handlers =
        HandlerRegistration.handlers
            { Add = addCmd
              Flush = flushCmd
              SetField = setField
              Child = child
              BodyEdits = bodyEditsCmd
              FieldId = fieldId
              OptionId = optionId
              ItemId = itemIdCmd
              Board = fun ctx _ -> boardCmd ctx
              Bootstrap = bootstrapCmd
              Issues = issues
              Intake = intakeCmd
              Say = say
              Inbox = inbox
              RoomOpen = roomOpen }
