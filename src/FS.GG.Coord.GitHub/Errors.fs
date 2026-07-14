namespace FS.GG.Coord.GitHub

module Errors =

    open System

    type IoError =
        | RateLimited of resetAt: DateTimeOffset option
        | NotFound of subject: string
        | Unauthorized of subject: string
        | Malformed of subject: string * detail: string
        | GraphQlErrors of messages: string list
        | Partial of applied: string list * failed: (string * string) list
        | Transport of detail: string
        | Http of status: int * body: string

    type IoResult<'a> = Result<'a, IoError>

    [<Literal>]
    let ExRate = 75

    [<Literal>]
    let ExOffboard = 3

    [<Literal>]
    let ExPartial = 4

    let exitCode (error: IoError) =
        match error with
        | RateLimited _ -> ExRate
        | Partial _ -> ExPartial
        // NOTE a `NotFound` is NOT `EX_OFFBOARD`. `EX_OFFBOARD` means "this issue is not an item on the
        // BOARD" — a fact about the project, discovered on a successful read. A 404 from the issues API
        // means the ISSUE is not there. Collapsing them would let a mistyped repo masquerade as an
        // un-boarded item and trigger the `item-add` remediation, which is #421's duplicate-creating
        // failure wearing a different hat.
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
        | RateLimited resetAt ->
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

            $"GraphQL budget EXHAUSTED.%s{waitFor} This is not a protocol error and it is not a lost race — REST-only work still runs."

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
