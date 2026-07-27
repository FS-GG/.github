namespace FS.GG.Coord.Cli

/// THE WIRE-JSON RENDERERS — the machine contracts (`ready`, `who`, `claim`/`take`, `inbox`, `lint`),
/// extracted out of `Client` as ADR-0047's second Client.fs decomposition seam. Each hand-writes its
/// array with a real `Utf8JsonWriter`, so a worker id, path, branch, or title carrying a quote cannot
/// forge the shape a consumer parses. The `who`/`lint` presentation DTOs (`WhoState`, `WhoRow`,
/// `LintFinding`) travel WITH their renderers — they are the renderers' input shape and nothing else's
/// — so the wire contract and the type it serialises live behind one `.fsi`.
///
/// BYTE-IDENTICAL is the contract, not an aspiration: cases 20/25 certify `who --json`, and the
/// renderers are moved verbatim. `Client`'s command handlers gather the data and call these.
module Render =

    open System
    open System.IO
    open System.Text.Json
    open FS.GG.Coord
    open FS.GG.Coord.Types
    open FS.GG.Coord.GitHub

    /// A `who` row's lock state. "In flight" is a LOCK fact, not a column fact: an item is in flight when a
    /// marker holds it — LIVE (`Held`) or past its lease (`Stale`, still a lock only `reap` may break) — or
    /// when the board column says In progress with NO marker at all (`Unclaimed` — someone working outside
    /// the protocol, which `who` exists to surface). A Ready/Backlog item nobody claimed is simply not in
    /// flight, and `who` does not list it. This is cases 20/25's certified `who`, and the JSON `.state` a
    /// consumer keys on (held/stale/unclaimed).
    type WhoState =
        | Held of Reads.Marker
        | Stale of Reads.Marker
        | Unclaimed

    let whoStateName =
        function
        | Held _ -> "held"
        | Stale _ -> "stale"
        | Unclaimed -> "unclaimed"

    /// A classified in-flight row: the item, its lock state, the touch-set it reserves, and — on a STALE
    /// row only — the #581 proof of life that turns a bare `STALE` into `STALE (#NNN OPEN)`.
    type WhoRow =
        { Ref: Ref
          State: WhoState
          Paths: string list
          /// The item's own OPEN `item/<n>-*` PR, as (number, headRef), when the lease lapsed but the WORK
          /// did not (#581). Populated ONLY on a Stale row; `None` on held/unclaimed and on a genuinely dead
          /// stale claim a reaper may collect.
          LivePr: (int * string) option
          /// #1055: the lease lapsed and there is NO open PR, but a pushed `item/<n>-*` branch exists — proof
          /// of life during §3, before §5 opens the PR. `true` only on a Stale row whose probe found a branch
          /// and no PR; it turns a bare `STALE` (which reads as reapable) into `STALE (item/<n>-* pushed)`.
          /// Mutually exclusive with `LivePr` by construction: a PR-open row is `LeaseExpiredPrOpen`, not this.
          BranchPushed: bool
          /// WHAT that PR says (#697), when there is one — is the finished work landable? `Some` exactly when
          /// `LivePr` is `Some`; `None` otherwise. It turns `STALE (#NNN OPEN)` into `STALE (#NNN OPEN —
          /// GREEN: LAND IT)` and points a human at `adopt` instead of `reap`. A held/unclaimed row has no
          /// PR to read, so it carries none.
          PrState: PrState option
          /// `who --local` — the local git worktree this item is checked out in, if any (#959). `None` when
          /// `--local` was not asked, or when no local worktree is on this item's `item/<n>-*` branch. It is
          /// informational: a claim with no local worktree is normal (another worker holds it, elsewhere).
          Worktree: string option }

    type ClaimReceipt =
        { Ref: Ref
          Worker: string
          Kind: string
          MarkerObserved: bool
          MarkerId: int64 option
          AssigneeObserved: string option
          Status: string option
          StatusRead: string
          StatusWrite: string
          PendingBoardWrites: int option
          Converged: bool }

    type LintFinding =
        { Code: string
          Severity: string
          Id: string
          Short: string
          Status: string
          Url: string
          Detail: string }

    /// A `predicate --json` result — the ADR-0050 oracle verdict, structured. `verdict` is the word
    /// (`agrees`/`contradicts`/`unknown`); `ownerValue`/`note` are non-null on `contradicts` (the
    /// owner-declared value and the governing note the filing-time check auto-comments), `reason` on
    /// `unknown`. A real JSON writer, so a note carrying a quote cannot forge the object.
    type PredicateResult =
        { Verdict: string
          Id: string
          Field: string
          Value: string
          OwnerValue: string option
          Note: string option
          Reason: string option }

    /// ONE claim a path update now collides with (.github#1517). The human OVERLAP branch prints these same
    /// facts across two stderr lines and then a THIRD naming whether the courtesy notice landed; the machine
    /// form carries all of it in one element, because a notice that FAILED still leaves a standing collision
    /// and a consumer must not have to infer that from an absent log line.
    type PathCollision =
        { Ref: Ref
          Worker: string
          SharedTokens: string list
          Notified: bool
          NotifyError: string option }

    /// The `widen --json` / `set-paths --json` receipt (.github#1517) — the ref, the RESULTING declaration,
    /// and the #353 overlap verdict in one object. `Kind` is the past-tense verb, mirroring `ClaimReceipt`.
    type PathUpdateReceipt =
        { Ref: Ref
          Worker: string
          Kind: string
          Paths: string list
          Collisions: PathCollision list }

    /// `ready --json` — THE MACHINE CONTRACT a reconciler (`/check-board`) and `next` read, an array of
    /// board rows. The field set is bash's `board_items` projection, the fields a consumer keys on: the
    /// `number`/`repo` that name the item, the board `status` (null when the column is unset — a modelled
    /// fact, #437), the issue `state` (which is not the column — when they disagree the issue wins, #520),
    /// and the `title`. Written with a real JSON writer so a title carrying a quote cannot forge the array.
    let renderReadyJson (rows: Scan.Row list) : string =
        use stream = new MemoryStream()
        use w = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = false, SkipValidation = false))

        w.WriteStartArray()

        for row in rows do
            w.WriteStartObject()
            w.WriteNumber("number", row.Ref.Number)
            w.WriteString("repo", $"%s{row.Ref.Owner}/%s{row.Ref.Repo}")
            w.WriteString("title", row.Title)

            match statusWireName row.Status with
            | "" -> w.WriteNull("status")
            | s -> w.WriteString("status", s)

            w.WriteString(
                "state",
                match row.State with
                | Open -> "OPEN"
                | Closed -> "CLOSED"
            )

            w.WriteEndObject()

        w.WriteEndArray()
        w.Flush()
        Text.Encoding.UTF8.GetString(stream.ToArray())

    /// `who --json` — the machine contract cases 20/25 certify: a JSON array of the in-flight items, each
    /// carrying its `number`, `repo`, `state` (held/stale/unclaimed), the `worker` holding it (`null` when
    /// unclaimed — only the column, not a marker, says so), and the `paths` it reserves. A STALE row also
    /// carries `livePr` (#581): `null` when the lease lapsed and NO open PR was found (a bare stale a reaper
    /// may collect), the `#NNN item/<n>-*` ref where the work is demonstrably still alive. A real JSON
    /// writer, so a worker id, path, or branch name carrying a quote cannot forge the array.
    /// `includeWorktree` is `who --local` (#959): the `worktree` field is emitted ONLY when it was asked for,
    /// so the machine contract cases 20/25 certify is byte-identical without `--local`. When asked, every row
    /// carries it — a string path, or `null` where no local worktree is on this item's branch.
    let renderWhoJson (includeWorktree: bool) (rows: WhoRow list) : string =
        use stream = new MemoryStream()
        use w = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = false, SkipValidation = false))

        w.WriteStartArray()

        for row in rows do
            w.WriteStartObject()
            w.WriteNumber("number", row.Ref.Number)
            w.WriteString("repo", $"%s{row.Ref.Owner}/%s{row.Ref.Repo}")
            w.WriteString("state", whoStateName row.State)

            match row.State with
            | Held m
            | Stale m -> w.WriteString("worker", m.Worker.Value)
            | Unclaimed -> w.WriteNull("worker")

            w.WriteStartArray("paths")

            for p in row.Paths do
                w.WriteStringValue p

            w.WriteEndArray()

            // #581 proof of life rides the STALE row only — a held claim needs no proof, an unclaimed item
            // has nothing to prove. So the field appears exactly where a human is about to decide to reap.
            match row.State with
            | Stale _ ->
                match row.LivePr with
                | Some(pr, headRef) -> w.WriteString("livePr", $"#%d{pr} %s{headRef}")
                | None -> w.WriteNull("livePr")

                // ...and WHAT the PR says (#697). `null` when there is no PR (a bare stale a reaper may
                // collect), the landable verdict (`green`/`conflicted`/`pending`/`red`/`unknown`) when
                // there is — so a machine consumer can tell finished work from an abandoned branch.
                match row.PrState with
                | Some st -> w.WriteString("prState", Landable.name st)
                | None -> w.WriteNull("prState")

                // #1055: a pushed `item/<n>-*` branch with no PR — proof of life short of a PR. Emitted ONLY
                // when true, so every existing stale-row shape (livePr null, no branch) stays byte-identical
                // and a machine consumer can still tell this apart from a genuinely dead claim.
                if row.BranchPushed then
                    w.WriteBoolean("branchPushed", true)
            | Held _
            | Unclaimed -> ()

            if includeWorktree then
                match row.Worktree with
                | Some path -> w.WriteString("worktree", path)
                | None -> w.WriteNull("worktree")

            w.WriteEndObject()

        w.WriteEndArray()
        w.Flush()
        Text.Encoding.UTF8.GetString(stream.ToArray())

    let renderClaimReceiptJson (receipt: ClaimReceipt) : string =
        use stream = new MemoryStream()
        use w = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = false, SkipValidation = false))
        w.WriteStartObject()
        w.WriteString("ref", receipt.Ref.Short)
        w.WriteString("repo", $"%s{receipt.Ref.Owner}/%s{receipt.Ref.Repo}")
        w.WriteNumber("number", receipt.Ref.Number)
        w.WriteString("worker", receipt.Worker)
        w.WriteString("kind", receipt.Kind)
        w.WriteBoolean("markerObserved", receipt.MarkerObserved)
        match receipt.MarkerId with
        | Some id -> w.WriteNumber("markerId", id)
        | None -> w.WriteNull("markerId")
        // The assignee is an advisory account-level projection, never the worker lock. The current client
        // does not mutate it; null makes that non-observation explicit instead of laundering it as success.
        match receipt.AssigneeObserved with
        | Some a -> w.WriteString("assigneeObserved", a)
        | None -> w.WriteNull("assigneeObserved")
        match receipt.Status with
        | Some s -> w.WriteString("status", s)
        | None -> w.WriteNull("status")
        w.WriteString("statusRead", receipt.StatusRead)
        w.WriteString("statusWrite", receipt.StatusWrite)
        match receipt.PendingBoardWrites with
        | Some n -> w.WriteNumber("pendingBoardWrites", n)
        | None -> w.WriteNull("pendingBoardWrites")
        w.WriteBoolean("converged", receipt.Converged)
        w.WriteEndObject()
        w.Flush()
        Text.Encoding.UTF8.GetString(stream.ToArray())

    let renderInboxJson (msgs: (string * Reads.Message) list) : string =
        use stream = new MemoryStream()
        use w = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = false, SkipValidation = false))

        w.WriteStartArray()

        for (item, m) in msgs do
            w.WriteStartObject()
            w.WriteString("item", item)
            w.WriteNumber("id", m.Id)
            w.WriteString("from", m.From)
            w.WriteString("to", m.To)
            w.WriteString("at", m.At)
            w.WriteString("text", m.Text)
            w.WriteEndObject()

        w.WriteEndArray()
        w.Flush()
        Text.Encoding.UTF8.GetString(stream.ToArray())

    let renderLintJson (findings: LintFinding list) : string =
        use stream = new MemoryStream()
        use w = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = false, SkipValidation = false))

        w.WriteStartArray()

        for f in findings do
            w.WriteStartObject()
            w.WriteString("code", f.Code)
            w.WriteString("severity", f.Severity)
            w.WriteString("id", f.Id)
            w.WriteString("status", f.Status)
            w.WriteString("url", f.Url)
            w.WriteString("detail", f.Detail)
            w.WriteEndObject()

        w.WriteEndArray()
        w.Flush()
        Text.Encoding.UTF8.GetString(stream.ToArray())

    let renderPredicateJson (result: PredicateResult) : string =
        use stream = new MemoryStream()
        use w = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = false, SkipValidation = false))

        let writeOpt (name: string) (v: string option) =
            match v with
            | Some s -> w.WriteString(name, s)
            | None -> w.WriteNull(name)

        w.WriteStartObject()
        w.WriteString("verdict", result.Verdict)
        w.WriteString("id", result.Id)
        w.WriteString("field", result.Field)
        w.WriteString("value", result.Value)
        writeOpt "ownerValue" result.OwnerValue
        writeOpt "note" result.Note
        writeOpt "reason" result.Reason
        w.WriteEndObject()
        w.Flush()
        Text.Encoding.UTF8.GetString(stream.ToArray())

    /// `widen --json` / `set-paths --json` (.github#1517). The field order and the `ref`/`repo`/`number`/
    /// `worker`/`kind` head are `renderClaimReceiptJson`'s, because this is the same KIND of thing — a
    /// receipt for one mutation this worker just made to one item — and a second dialect for it would be a
    /// new thing for every consumer to learn.
    ///
    /// `verdict` is DERIVED from `Collisions` rather than passed in beside it. The two cannot then disagree,
    /// which is the whole failure the human form has: it prints `DISJOINT` on stdout and the OVERLAP detail
    /// on stderr, so a consumer reading one stream can be told the opposite of what the other says.
    let renderPathUpdateJson (receipt: PathUpdateReceipt) : string =
        use stream = new MemoryStream()
        use w = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = false, SkipValidation = false))

        w.WriteStartObject()
        w.WriteString("ref", receipt.Ref.Short)
        w.WriteString("repo", $"%s{receipt.Ref.Owner}/%s{receipt.Ref.Repo}")
        w.WriteNumber("number", receipt.Ref.Number)
        w.WriteString("worker", receipt.Worker)
        w.WriteString("kind", receipt.Kind)

        w.WriteStartArray("paths")

        for p in receipt.Paths do
            w.WriteStringValue p

        w.WriteEndArray()

        w.WriteString("verdict", (if List.isEmpty receipt.Collisions then "disjoint" else "overlap"))

        w.WriteStartArray("collisions")

        for c in receipt.Collisions do
            w.WriteStartObject()
            w.WriteString("ref", c.Ref.Short)
            w.WriteString("repo", $"%s{c.Ref.Owner}/%s{c.Ref.Repo}")
            w.WriteNumber("number", c.Ref.Number)
            w.WriteString("worker", c.Worker)

            // An ARRAY, like the `paths` beside it and like `renderWhoJson`'s. The human form joins these
            // stems into one stderr line; shaping the machine field to that line would hand a consumer a
            // string to split on ", " — a human's formatting choice promoted to a wire contract.
            w.WriteStartArray("sharedTokens")

            for t in c.SharedTokens do
                w.WriteStringValue t

            w.WriteEndArray()

            w.WriteBoolean("notified", c.Notified)

            match c.NotifyError with
            | Some e -> w.WriteString("notifyError", e)
            | None -> w.WriteNull("notifyError")

            w.WriteEndObject()

        w.WriteEndArray()

        w.WriteEndObject()
        w.Flush()
        Text.Encoding.UTF8.GetString(stream.ToArray())
