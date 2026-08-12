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
    ///
    /// **AND THERE IS A FOURTH ANSWER, WHICH IS NOT A LOCK STATE AT ALL** (.github#1668). `Undetermined` is
    /// "the marker read came back with comments I could not classify, so I cannot tell you whether this item
    /// is held". It exists because the other three are all ASSERTIONS about the lock, and an unreadable
    /// comment supports none of them — least of all `Unclaimed`, which is the fail-open direction and which
    /// carries an accusation ("someone is working outside the protocol") that was levelled, in the incident
    /// that opened #1668, at a worker holding a valid marker.
    type WhoState =
        | Held of Reads.Marker
        | Stale of Reads.Marker
        | Unclaimed
        /// The marker read was incomplete AND produced no marker at all, so there is no lock state to
        /// report. It carries no payload: the reasons are on `WhoRow.Incomplete`, because an incomplete
        /// read is a property of the READ and a `Held` or `Stale` row can suffer it just as badly — a
        /// `STALE` built from a short read is the one that sends a human to `reap`.
        | Undetermined

    let whoStateName =
        function
        | Held _ -> "held"
        | Stale _ -> "stale"
        | Unclaimed -> "unclaimed"
        // A NEW WORD, NOT A REUSED ONE. A consumer keying on `unclaimed` must not silently start receiving
        // rows that mean "could not determine" — that is the same substitution in a machine contract.
        | Undetermined -> "undetermined"

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
          Worktree: string option

          /// EVERY COMMENT THE MARKER READ COULD NOT CLASSIFY (.github#1668) — on EVERY row, whatever its
          /// state, because an incomplete read is a property of the READ and not of the verdict drawn from
          /// it. Empty on the overwhelmingly normal row, and empty is the load-bearing value: only an empty
          /// list licenses acting on this row's state as a fact.
          ///
          /// It is NOT redundant with `Undetermined`. That state is the case where the short read left NO
          /// marker at all; this field also fires on `Held` and `Stale`, where a marker WAS found and the
          /// hidden one may be a lower id (so the named holder is the wrong holder) or a live claim behind
          /// a lapsed one (so the `STALE` a human is about to `reap` is not free).
          Incomplete: string list }

    /// ONE claim a path update — or, since .github#2459, a `claim` itself — now collides with. The human
    /// OVERLAP branch prints these same facts across two stderr lines and then a THIRD naming whether the
    /// courtesy notice landed; the machine form carries all of it in one element, because a notice that
    /// FAILED still leaves a standing collision and a consumer must not have to infer that from an absent
    /// log line. Moved ahead of `ClaimReceipt` (.github#2459) because that receipt now carries a list of
    /// these too, and an F# record referring to a type must follow its definition.
    type PathCollision =
        { Ref: Ref
          Worker: string
          SharedTokens: string list
          Notified: bool
          NotifyError: string option }

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
          Collisions: PathCollision list
          Converged: bool }

    /// `take --json`'s other outcome (.github#1525) — see the `.fsi` for why it is not a `ClaimReceipt`
    /// with everything optional.
    type NoItemReceipt =
        { Worker: string
          PassedOver: int
          RepoAdvisory: string option }

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

    /// The `widen --json` / `set-paths --json` receipt (.github#1517) — the ref, the RESULTING declaration,
    /// and the #353 overlap verdict in one object. `Kind` is the past-tense verb, mirroring `ClaimReceipt`.
    type PathUpdateReceipt =
        { Ref: Ref
          Worker: string
          Kind: string
          Paths: string list
          Collisions: PathCollision list }

    /// HOW ONE MECHANICAL REPAIR WENT under `reconcile --apply` (.github#1524).
    ///
    /// THE WIRE WORDS ARE `renderClaimReceiptJson`'s, NOT the human line's. `ClaimReceipt.StatusWrite`
    /// already names `Board.Written`/`Board.Deferred`/`Board.NotOnBoard` as `written`/`deferred`/
    /// `not-on-board`, and those are the same three cases of the same `Board.WriteOutcome`, reported by the
    /// same CLI, about the same kind of act — a Status write this process just attempted. `reconcile`'s
    /// HUMAN line says `applied` and `queued`; minting those as wire words too would give one tool two
    /// names for one fact, which is a dialect rather than a contract. So the human form keeps its words and
    /// the machine form reuses the ones already on the wire.
    ///
    /// `Deferred` is the case that matters most and the reason .github#1524 was filed: it is a board write
    /// QUEUED against an exhausted budget, and NOTHING replays it for you. It is a distinct case of a
    /// closed union — never a substring a consumer greps a sentence for.
    ///
    /// `Reaped` is `STALE-CLAIM`, whose remedy is not a field write at all: `reconcile` re-enters its own
    /// `reap` verb once per affected repo. So this outcome is HONESTLY per-REPO — it says the reap pass
    /// covering this item exited green, not that this item's marker was read back collected. That is
    /// weaker than `Written`, and it is named separately so a consumer cannot mistake it for the stronger
    /// claim.
    ///
    /// `NotAttempted` is the apply phase never starting (no worker id resolved, the board did not
    /// bootstrap). The rows still ship — a `--json` caller gets a document describing what was found and
    /// explicitly not tried, rather than an empty stream it cannot parse.
    type ReconcileOutcome =
        | Written
        | Deferred
        | NotOnBoard
        | Reaped
        | NotAttempted of reason: string
        | Failed of reason: string

    let reconcileOutcomeName (outcome: ReconcileOutcome) =
        match outcome with
        | Written -> "written"
        | Deferred -> "deferred"
        | NotOnBoard -> "not-on-board"
        | Reaped -> "reaped"
        | NotAttempted _ -> "not-attempted"
        | Failed _ -> "failed"

    /// ONE row of `reconcile --json` — a mechanical finding, and (under `--apply`) how repairing it went.
    type ReconcileRow =
        { Id: string
          Rule: string
          Subject: Ref
          Size: string
          Remedy: string
          Statement: string
          /// The field this repair sets and the value it sets it to — `None` for `STALE-CLAIM`, whose
          /// remedy is a marker collection delegated to `reap`, not a field write. ONE option over the
          /// PAIR, so "which field" and "which value" cannot be present independently of each other.
          Write: (string * string) option
          /// All intended field values for an apply receipt.  BLOCKER-CLEARED is deliberately two writes.
          Writes: (string * string) list
          /// Values observed on the fresh verification read; absent when no fresh observation was possible.
          Observed: (string * string) list option
          /// `None` on a DRY RUN, where nothing was attempted and therefore nothing is known.
          Outcome: ReconcileOutcome option }

    /// `ready --json` — THE MACHINE CONTRACT a reconciler (`/check-board`) and `next` read, an array of
    /// board rows. The field set is bash's `board_items` projection, the fields a consumer keys on: the
    /// `number`/`repo` that name the item, the board `status` (null when the column is unset — a modelled
    /// fact, #437), the issue `state` (which is not the column — when they disagree the issue wins, #520),
    /// the `title`, and the `class` column (.github#1588 — null when unset, on `status`'s terms exactly).
    /// Written with a real JSON writer so a title carrying a quote cannot forge the array.
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

            // `class` — the board column, as scanned (.github#1588). WITHOUT IT `drive-board`'s stopping
            // rule is not executable: the contract is "no startable `defect`", and a driver reading this
            // document could learn which rows are UNCLASSED (from `lint`'s CLASS-UNSET) and nothing about
            // which are defects, so answering the question meant opening every open issue body by hand on
            // every loop. A rule nobody can evaluate is not a rule.
            //
            // `null` when unset, exactly as `status` is and for #437's reason: the absence is a modelled
            // fact, not an empty string. It is the PROJECTION, so it is only as current as the last
            // `reconcile` — which is why the driver reconciles before it reads, and why `CLASS-UNSET`
            // names the rows this column cannot speak for.
            match row.BoardClass with
            | Some c -> w.WriteString("class", itemClassWireName c)
            | None -> w.WriteNull("class")

            w.WriteString("severity", severityWireName row.Severity)

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
    /// carrying its `number`, `repo`, `state` (held/stale/unclaimed/undetermined — .github#1668 added the
    /// fourth), the `worker` holding it (`null` when unclaimed, and ALSO null when undetermined, which is
    /// why `.state` and not `worker` carries that distinction), and the `paths` it reserves. Any row whose
    /// marker read was INCOMPLETE also carries `undetermined`: a non-empty array of the reasons. That field
    /// is keyed on the READ, so it appears on `held` and `stale` rows too — a consumer must not treat a
    /// `stale` row bearing it as reapable. A STALE row also
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
            // BOTH null, and they are told apart by `.state`, never by this field. "Nobody holds it" and
            // "I cannot say who holds it" have the same empty answer HERE — which is exactly why #1668
            // needed a distinct `.state` word rather than a cleverer `worker`.
            | Unclaimed
            | Undetermined -> w.WriteNull("worker")

            // .github#1668: WHY the answer may be incomplete, on the row itself, so a machine consumer can
            // act on it without re-deriving anything.
            //
            // KEYED ON THE READ, NOT ON THE STATE. It rides a `held` or `stale` row too, and it has to: a
            // hidden marker with a LOWER id makes `worker` above the wrong holder, and a hidden LIVE marker
            // behind a lapsed one makes a `stale` row — the row a consumer reaps — not free at all. Emitted
            // only when non-empty, so every row from a complete read stays byte-identical.
            if not (List.isEmpty row.Incomplete) then
                w.WriteStartArray("undetermined")

                for reason in row.Incomplete do
                    w.WriteStringValue reason

                w.WriteEndArray()

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
            | Unclaimed
            | Undetermined -> ()

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

        // #2459 — `claim`'s own #353 collision report, shaped exactly like `renderPathUpdateJson`'s
        // `collisions` array so one consumer can read either document the same way. Advisory: an empty
        // array here is NOT proof of disjointness (a degraded scan reports empty too, best-effort), so
        // unlike `PathUpdateReceipt` there is no derived `verdict` key sitting beside it to overclaim one.
        w.WriteStartArray("collisions")

        for c in receipt.Collisions do
            w.WriteStartObject()
            w.WriteString("ref", c.Ref.Short)
            w.WriteString("repo", $"%s{c.Ref.Owner}/%s{c.Ref.Repo}")
            w.WriteNumber("number", c.Ref.Number)
            w.WriteString("worker", c.Worker)

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

        w.WriteBoolean("converged", receipt.Converged)
        w.WriteEndObject()
        w.Flush()
        Text.Encoding.UTF8.GetString(stream.ToArray())

    /// `take --json`'s EMPTY arm (.github#1525) — a receipt for the outcome where nothing was claimed.
    ///
    /// THE FIRST FIVE KEYS ARE `renderClaimReceiptJson`'S, IN ITS ORDER, and that is deliberate rather
    /// than decorative: `ref`, `repo` and `number` are the identity a consumer reads, and emitting them as
    /// explicit `null` is what lets ONE parse handle both outcomes. `kind` sits where it sits in the
    /// claimed receipt, so the key a caller branches on is in the same place in both documents. It is a
    /// LITERAL here, not a field: `ClaimReceipt.Kind` varies (`claimed`/`adopted`) because two verbs share
    /// that document, and this outcome has exactly one name. A field would be an invitation to mint a
    /// second word for it.
    ///
    /// The shared keys stop there. The claimed receipt's remaining ones are read-backs of a mutation this
    /// command did not make, and inventing `false`/`null` values for them would describe a claim that was
    /// attempted and failed. Nothing was attempted.
    ///
    /// `passedOver` and `repoAdvisory` are the two facts this outcome has that the other does not, so they
    /// come last, after the shared identity — a consumer reading both documents meets the common keys in
    /// the same places and the divergence in one place.
    let renderNoItemJson (receipt: NoItemReceipt) : string =
        use stream = new MemoryStream()
        use w = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = false, SkipValidation = false))
        w.WriteStartObject()
        w.WriteNull "ref"
        w.WriteNull "repo"
        w.WriteNull "number"
        w.WriteString("worker", receipt.Worker)
        w.WriteString("kind", "none")
        w.WriteNumber("passedOver", receipt.PassedOver)
        // #979's advisory, in the document rather than only on stderr — `passedOver:0` is what a MISSPELT
        // `--repo` looks like to a parser, and it is also what an empty board looks like.
        match receipt.RepoAdvisory with
        | Some a -> w.WriteString("repoAdvisory", a)
        | None -> w.WriteNull "repoAdvisory"
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

    /// A JSON-mode failure goes to stderr, preserving stdout as the successful document channel.  The
    /// rate-limit class is data rather than a phrase a board driver must parse (#1892).
    let renderFailureJson (exitCode: int) (message: string) (rateLimit: Errors.RateLimitKind option) : string =
        use stream = new MemoryStream()
        use w = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = false, SkipValidation = false))
        w.WriteStartObject()
        w.WriteString("kind", "error")
        w.WriteNumber("exitCode", exitCode)
        w.WriteString("message", message)
        match rateLimit with
        | Some Errors.Primary -> w.WriteString("rateLimit", "primary")
        | Some Errors.Secondary -> w.WriteString("rateLimit", "secondary")
        | Some Errors.Unknown -> w.WriteString("rateLimit", "unknown")
        | None -> w.WriteNull("rateLimit")
        w.WriteEndObject()
        w.Flush()
        Text.Encoding.UTF8.GetString(stream.ToArray())

    /// `reconcile --json` / `reconcile --apply --json` (.github#1524) — the array of mechanical findings,
    /// and under `--apply` how each repair went.
    ///
    /// **THE FIRST SIX KEYS ARE IN ALPHABETICAL ORDER, AND THAT IS LOAD-BEARING.** This projection used to
    /// be `JsonSerializer.Serialize` over an F# ANONYMOUS RECORD, and the compiler sorts an anonymous
    /// record's fields alphabetically — so the bytes every existing consumer parses are
    /// `id,remedy,rule,size,statement,subject`, NOT the `id,rule,subject,size,remedy,statement` its source
    /// literal read as. Rewriting this as a real `Utf8JsonWriter` in the order a human would naturally
    /// choose would have silently rewritten the wire contract. `ApplicationServiceTests` pins these bytes.
    /// Do not "tidy" this order.
    ///
    /// `includeOutcome` is `--apply`, and it is a DOCUMENT-level decision rather than a per-row one, on
    /// `renderWhoJson`'s `includeWorktree` precedent (#959): the dry-run shape stays byte-identical, and an
    /// apply run cannot emit a ragged array where some rows carry the outcome and others do not.
    ///
    /// The four appended keys are exactly the facts `--apply` ADDS — what was attempted (`field`/`value`)
    /// and how it went (`outcome`/`error`). Identity facts are NOT re-spelled here: `subject` already IS
    /// this row's ref, and a second key carrying the same string would be one fact with two names.
    let renderReconcileJson (includeOutcome: bool) (rows: ReconcileRow list) : string =
        use stream = new MemoryStream()
        use w = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = false, SkipValidation = false))

        w.WriteStartArray()

        for r in rows do
            w.WriteStartObject()

            // Alphabetical — see above. This is the pre-existing wire order, reproduced deliberately.
            w.WriteString("id", r.Id)
            w.WriteString("remedy", r.Remedy)
            w.WriteString("rule", r.Rule)
            w.WriteString("size", r.Size)
            w.WriteString("statement", r.Statement)
            w.WriteString("subject", r.Subject.Short)

            if includeOutcome then
                w.WriteStartArray("writes")
                for field, value in r.Writes do
                    w.WriteStartObject()
                    w.WriteString("field", field)
                    w.WriteString("value", value)
                    w.WriteEndObject()
                w.WriteEndArray()

                w.WriteStartArray("observed")
                for field, value in r.Observed |> Option.defaultValue [] do
                    w.WriteStartObject()
                    w.WriteString("field", field)
                    w.WriteString("value", value)
                    w.WriteEndObject()
                w.WriteEndArray()
                match r.Write with
                | Some(field, value) ->
                    w.WriteString("field", field)
                    w.WriteString("value", value)
                // `STALE-CLAIM` sets no field. Null is the modelled fact (#437's argument), not an omission.
                | None ->
                    w.WriteNull("field")
                    w.WriteNull("value")

                match r.Outcome with
                | Some outcome -> w.WriteString("outcome", reconcileOutcomeName outcome)
                | None -> w.WriteNull("outcome")

                // The reason rides WITH the outcome it belongs to, derived from the same union case, so a
                // consumer never has to pair a failure with an explanation off a second stream — which is
                // the whole defect .github#1524 and .github#1517 are the two halves of.
                //
                // `NotOnBoard` is a FAILURE (it sets the handler's `failed`, so the verb exits non-zero)
                // and it is the one case whose reason is not carried in the union — there is nothing to
                // carry, the case IS the reason. It gets that reason spelled out anyway: the human stream
                // prints a sentence for it, and a document that made a consumer infer "why" from a bare
                // word while stderr said it in full would be this issue's defect in miniature.
                match r.Outcome with
                | Some(NotAttempted reason)
                | Some(Failed reason) -> w.WriteString("error", reason)
                | Some NotOnBoard -> w.WriteString("error", "the item left the board before apply")
                | _ -> w.WriteNull("error")

            w.WriteEndObject()

        w.WriteEndArray()
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
