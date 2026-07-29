namespace FS.GG.Coord.GitHub

module Errors =

    open System
    open FS.GG.Coord

    type RateLimitResource =
        | GraphQlBudget
        | RestBudget of resource: string option
        | SecondaryLimit of resource: string option * retryAfter: TimeSpan option
        | UnknownBudget

    type IoError =
        | RateLimited of resource: RateLimitResource * resetAt: DateTimeOffset option
        | NotFound of subject: string
        | Unauthorized of subject: string
        | Malformed of subject: string * detail: string
        | GraphQlErrors of messages: string list
        | Partial of applied: string list * failed: (string * string) list
        | Transport of detail: string
        | Http of status: int * body: string

    type IoResult<'a> = Result<'a, IoError>

    // The numbers live in ONE place now — `FS.GG.Coord.ExitCode.toInt` (#918, ADR-0046). These are the
    // GitHub layer's three, derived from the union rather than re-declared, so the collision that put
    // `ExOffboard` on `3` (a RED verdict) and `ExPartial` on `4` (a NO-VERDICT) cannot be reintroduced
    // here without the compiler seeing it in `toInt`. They are no longer `[<Literal>]` — nothing pattern-
    // matches or attributes them, only compares and returns them — which is what lets them derive.
    let ExRate = ExitCode.toInt ExitCode.Rate

    let ExOffboard = ExitCode.toInt ExitCode.Offboard

    let ExPartial = ExitCode.toInt ExitCode.Partial

    let exitCode (error: IoError) =
        match error with
        | RateLimited _ -> ExRate
        | Partial _ -> ExPartial
        // NOTE a `NotFound` is NOT `EX_OFFBOARD`. `EX_OFFBOARD` means "this issue is not an item on the
        // BOARD" — a fact about the project, discovered on a successful read. A 404 from the issues API
        // means the ISSUE is not there. Collapsing them would let a mistyped repo masquerade as an
        // un-boarded item and trigger the `item-add` remediation — #421's failure wearing a different hat:
        // a definite answer about the board, derived from a read that never established anything about it.
        // (Not a duplicate row; that mechanism does not reproduce — #871.)
        | NotFound _
        | Unauthorized _
        | Malformed _
        | GraphQlErrors _
        | Transport _
        | Http _ -> 1

    let isQueueable (error: IoError) =
        match error with
        | RateLimited _ -> true
        // EVERY OTHER FAILURE IS PERMANENT, AND A PERMANENT FAILURE MAY NOT BE QUEUED. `flush` would
        // replay it forever, and each replay would fail identically — so the queue never drains, the
        // refusal is never surfaced, and the tool reports success over a write it will never make. That
        // is #510: the promise ("the write is QUEUED") was printed on failures that could not be kept.
        | Partial _
        | NotFound _
        | Unauthorized _
        | Malformed _
        | GraphQlErrors _
        | Transport _
        | Http _ -> false

    let explain (error: IoError) =
        match error with
        | RateLimited(resource, resetAt) ->
            let waitFor =
                match resetAt with
                | Some at ->
                    let mins = int (Math.Ceiling((at - DateTimeOffset.UtcNow).TotalMinutes))
                    // A NEGATIVE or zero wait is a reset that has already passed — say "now", never
                    // "in -3 minutes", which reads as a bug and teaches the operator to distrust the
                    // number.
                    if mins <= 0 then
                        " The window has already reset — retry now."
                    else
                        $" The budget resets in ~%d{mins}m."
                | None ->
                    // We could not read the reset. Say so — an invented number is worse than an absent
                    // one, because it will be believed.
                    " The reset time could not be read."

            // NAME THE BUDGET THAT DIED, AND ONLY IT. The sentence used to open "GraphQL budget
            // EXHAUSTED" unconditionally and close with "REST-only work still runs" — advice that is
            // actively harmful on a REST limit, because it sends the worker to the budget that is gone.
            // Each arm now states what is exhausted and says nothing about the other budget beyond what
            // we actually observed.
            //
            // "MAY still run", not "still runs", and the hedge is the point. A 403 is evidence about the
            // bucket that refused THIS call and about nothing else — we did not observe the other budget,
            // so we do not get to certify it. The old text asserted REST was up while holding no evidence
            // either way, which is how it came to promise REST at the exact moment REST was gone. Both
            // budgets CAN be dead at once; a sentence that rules that out by construction is the same
            // defect, merely pointing the other way.
            // NAME THE BUCKET GITHUB NAMED. `X-RateLimit-Resource` can say `search`, `code_search`,
            // `integration_manifest` — not just `core`. Collapsing all of them into the words "REST budget"
            // sent every operator to `gh api rate_limit`'s top-level `core` figure, which was untouched, and
            // from that they concluded the tool was lying about a counter of its own (#1666). It was not: a
            // different bucket refused the call. Name it, and that reading is available to them.
            //
            // SANITISED, because this is header-sourced text going straight into operator output. A bucket
            // name is a short identifier (`core`, `code_search`); anything else is not a name we should be
            // quoting back. Unfiltered, a backtick in the header breaks out of the code span it is rendered
            // in — a small thing, but this is the sentence people read when nothing else works.
            let onResource (r: string option) =
                match r with
                | Some name when
                    not (String.IsNullOrWhiteSpace name)
                    && name.Length <= 40
                    && name |> Seq.forall (fun c -> Char.IsAsciiLetterOrDigit c || c = '_' || c = '-')
                    ->
                    $" GitHub named the resource `%s{name}`."
                | _ -> ""

            match resource with
            | GraphQlBudget ->
                $"GraphQL budget EXHAUSTED.%s{waitFor} This is not a protocol error and it is not a lost race — REST-only work may still run."

            | RestBudget r ->
                // THE LOCK LIVES HERE (ADR-0034 §3), so this arm is the one that stops the protocol dead:
                // `claim`/`take`/`who` read and write the marker over REST. Say that, rather than let a
                // worker read "rate limited" and assume the GraphQL back-off advice applies.
                $"REST budget EXHAUSTED.%s{onResource r}%s{waitFor} This is not a protocol error and it is not a lost race — but the claim lock lives on REST (ADR-0034 §3), so `claim`/`take`/`who` cannot run until it resets. GraphQL-only work may still run."

            | SecondaryLimit(r, retryAfter) ->
                // A DIFFERENT MECHANISM WITH A DIFFERENT REMEDY, and collapsing it into the primary budget
                // is #1666 itself. A secondary/abuse-detection limit is triggered by BURST CONCURRENCY, not
                // by cumulative quota, so:
                //   * the primary counter is typically almost untouched — every operator who checked
                //     `gh api rate_limit` saw 62% / 87% / 88% headroom and concluded the refusal was phantom;
                //   * it does NOT appear in `/rate_limit` at all, so a healthy reading there is not evidence
                //     against it;
                //   * waiting for `X-RateLimit-Reset` is the WRONG action — that is the PRIMARY window and
                //     it does not describe this limit. `resetAt` is `None` here BY CONSTRUCTION, so the
                //     wrong number cannot be printed even by accident.
                // The remedy is to REDUCE CONCURRENCY and back off, which is why this must not render as a
                // plain fleet-wide "wait for the budget to reset".
                let wait =
                    match retryAfter with
                    | Some(after: TimeSpan) ->
                        let secs = int (Math.Ceiling after.TotalSeconds)
                        $" GitHub sent `Retry-After: %d{secs}s` — honour THAT, not the primary reset."
                    | None ->
                        " GitHub sent no `Retry-After`, and the primary `X-RateLimit-Reset` does NOT describe this limit — back off with increasing delay rather than to a named instant."

                $"GitHub SECONDARY rate limit (abuse detection) — NOT the primary budget.%s{onResource r}%s{wait} This is not a protocol error and it is not a lost race. It is triggered by CONCURRENT REQUEST BURSTS, so the remedy is to REDUCE CONCURRENCY and retry with backoff; the primary budget may look almost untouched and does not appear in `/rate_limit`, which is not evidence that this refusal was false."

            | UnknownBudget ->
                // WHAT THIS ARM ACTUALLY KNOWS, stated exactly. It is reached when the body matched a
                // PRIMARY wording and GitHub named no resource — so the mechanism reads as primary and it
                // is the BUCKET that is unknown. An earlier draft of this sentence also claimed "the
                // wording did not say whether this was primary or secondary", which was false in every
                // reachable case: over-claiming uncertainty is the same defect as over-claiming certainty,
                // and this change is not entitled to either.
                //
                // Which budget is still not guessed (#266) — `core` and `graphql` are waited out the same
                // way, so the missing name costs advice, not correctness.
                $"A GitHub rate limit is EXHAUSTED — the wording reads as the PRIMARY budget, but GitHub named no resource, so WHICH bucket is unknown.%s{waitFor} This is not a protocol error and it is not a lost race. Wait it out; if it recurs immediately on resuming, suspect a secondary limit instead and reduce concurrency."

        | NotFound subject -> $"not found: %s{subject}. The server said so — this is an absence, not a failed read."

        | Unauthorized subject ->
            $"not authorised to read %s{subject}. The token cannot see it, which is not the same fact as it not being there."

        | Malformed(subject, detail) ->
            // The sentence the corpus greps for is "malformed" (case 42, #461). It is load-bearing: the
            // operator must be able to tell a failed read from an empty answer, and this is the word that
            // tells them.
            $"malformed response reading %s{subject}: %s{detail}. That is a FAILED READ, not an empty answer — refusing to decide from it."

        | GraphQlErrors messages -> "GraphQL refused the query: " + String.Join("; ", messages)

        | Partial(applied, failed) ->
            let landed = String.Join(", ", applied)

            let broke =
                failed |> List.map (fun (alias, msg) -> $"%s{alias}: %s{msg}") |> String.concat "; "

            $"PARTIAL WRITE — %d{List.length applied} of %d{List.length applied + List.length failed} aliases landed (%s{landed}) and the rest did not (%s{broke}). This is NOT queued: replaying the document would rewrite the half that already landed."

        | Transport detail -> $"could not reach GitHub: %s{detail}. We did not observe anything — this is not an empty answer."

        | Http(status, body) ->
            let trimmed = if body.Length > 400 then body.Substring(0, 400) + "…" else body
            $"HTTP %d{status}: %s{trimmed}"
