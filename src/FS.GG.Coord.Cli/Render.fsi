namespace FS.GG.Coord.Cli

/// THE WIRE-JSON RENDERERS — the machine contracts (`ready`, `who`, `claim`/`take`, `inbox`, `lint`),
/// extracted out of `Client` as ADR-0047's second Client.fs decomposition seam. Each hand-writes its
/// array with a real `Utf8JsonWriter`, so a worker id, path, branch, or title carrying a quote cannot
/// forge the shape a consumer parses. The `who`/`lint` presentation DTOs travel WITH their renderers —
/// they are the renderers' input shape and nothing else's — so the wire contract and the type it
/// serialises live behind one `.fsi`. `Client`'s handlers gather the data; these render it.
module Render =

    open FS.GG.Coord
    open FS.GG.Coord.Types
    open FS.GG.Coord.GitHub

    /// A `who` row's lock state. "In flight" is a LOCK fact, not a column fact: an item is in flight when
    /// a marker holds it — LIVE (`Held`) or past its lease (`Stale`, still a lock only `reap` may break) —
    /// or when the board column says In progress with NO marker (`Unclaimed`). The JSON `.state` a
    /// consumer keys on (held/stale/unclaimed) is derived from this.
    type WhoState =
        | Held of Reads.Marker
        | Stale of Reads.Marker
        | Unclaimed

    /// A classified in-flight row: the item, its lock state, the paths it reserves, and — on a STALE row
    /// only — the proof-of-life a human needs before reaping (#581/#697/#1055).
    type WhoRow =
        { Ref: Ref
          State: WhoState
          Paths: string list
          /// The item's own OPEN `item/<n>-*` PR as (number, headRef), when the lease lapsed but the WORK
          /// did not (#581). `Some` only on a Stale row.
          LivePr: (int * string) option
          /// #1055: the lease lapsed, there is NO open PR, but a pushed `item/<n>-*` branch exists — proof
          /// of life during §3. `true` only on a Stale row. Mutually exclusive with `LivePr`.
          BranchPushed: bool
          /// What that PR says (#697), when there is one — is the finished work landable? `Some` exactly
          /// when `LivePr` is `Some`.
          PrState: PrState option
          /// `who --local` — the local git worktree this item is checked out in, if any (#959). Emitted
          /// only when `--local` was asked.
          Worktree: string option }

    /// The fresh postcondition emitted by `claim --json` and `take --json`. The lock and board column are
    /// separate observations; `Converged` is true only when both were read back successfully.
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

    /// A single `lint` finding, in the shape `lint --json` emits.
    type LintFinding =
        { Code: string
          Severity: string
          Id: string
          Short: string
          Status: string
          Url: string
          Detail: string }

    /// A `predicate --json` result — the ADR-0050 oracle verdict, structured (.github#1202). `ownerValue`
    /// and `note` are non-null exactly on `contradicts`; `reason` on `unknown`.
    type PredicateResult =
        { Verdict: string
          Id: string
          Field: string
          Value: string
          OwnerValue: string option
          Note: string option
          Reason: string option }

    /// ONE claim a path update now collides with (.github#1517) — the same three facts the human OVERLAP
    /// branch prints (`OVERLAP — now collides with <ref> (worker <holder>)` / `  <tokens>`), plus whether
    /// the courtesy notice this command posts on that holder's item actually landed. The notify outcome is
    /// part of the receipt because a notice that FAILED still leaves a standing collision: the human form
    /// says so on stderr, so the machine form must not have to infer it from silence.
    type PathCollision =
        { Ref: Ref
          Worker: string
          /// The shared token STEMS. A LIST, rendered as an array — the human form joins them into one
          /// stderr line, and a machine field shaped to that line would be a consumer splitting on ", ".
          SharedTokens: string list
          Notified: bool
          /// Why the notice failed; `None` when it landed.
          NotifyError: string option }

    /// The receipt `widen --json` and `set-paths --json` emit (.github#1517): the ref, the declaration the
    /// update RESULTED IN, and the #353 overlap verdict — all three in ONE object, so a machine consumer
    /// never scrapes `widened <ref> → Paths: a, b` prose or reads the overlap detail off a second stream.
    /// `Kind` is the past-tense verb (`widened`/`set`), mirroring `ClaimReceipt.Kind`'s `claimed`.
    /// `Collisions` is empty exactly when the verdict is `disjoint`.
    type PathUpdateReceipt =
        { Ref: Ref
          Worker: string
          Kind: string
          /// The tokens the item now declares — the resulting touch-set, not the tokens that were asked for.
          Paths: string list
          Collisions: PathCollision list }

    /// How ONE mechanical repair went under `reconcile --apply` (.github#1524). The wire words are
    /// `ClaimReceipt.StatusWrite`'s (`written`/`deferred`/`not-on-board`) rather than the human line's
    /// (`applied`/`queued`), because those are the same cases of the same `Board.WriteOutcome` reported by
    /// the same CLI — one fact, one name. `Deferred` is the QUEUED write nothing replays for you, and it is
    /// a case of a closed union rather than a word in a sentence. `Reaped` is deliberately weaker than
    /// `Written`: `STALE-CLAIM` is delegated to the `reap` verb once per repo, so it reports that the pass
    /// covering this item exited green, not a per-item read-back.
    type ReconcileOutcome =
        | Written
        | Deferred
        | NotOnBoard
        | Reaped
        | NotAttempted of reason: string
        | Failed of reason: string

    /// The wire word for an outcome.
    val reconcileOutcomeName: outcome: ReconcileOutcome -> string

    /// One row of `reconcile --json`: a mechanical finding, plus — under `--apply` — the field write it
    /// attempted and how that went.
    type ReconcileRow =
        { Id: string
          Rule: string
          Subject: Ref
          Size: string
          Remedy: string
          Statement: string
          /// The field and value this repair sets; `None` for `STALE-CLAIM`, which writes no field. One
          /// option over the PAIR, so the two cannot be present independently.
          Write: (string * string) option
          /// `None` on a dry run — nothing was attempted, so nothing is known.
          Outcome: ReconcileOutcome option }

    /// `ready --json` — the machine contract a reconciler reads: a JSON array of the startable rows. A
    /// real JSON writer, so a title or path carrying a quote cannot forge the array.
    val renderReadyJson: rows: Scan.Row list -> string

    /// `who --json` — the machine contract cases 20/25 certify: a JSON array of the in-flight items, each
    /// with `number`, `repo`, `state`, the `worker` (`null` when unclaimed), and the `paths` it reserves;
    /// a STALE row also carries `livePr`/`prState`/`branchPushed`. `includeWorktree` is `who --local`
    /// (#959): the `worktree` field is emitted ONLY when asked, so the no-`--local` shape is byte-identical.
    val renderWhoJson: includeWorktree: bool -> rows: WhoRow list -> string

    /// `claim --json` / `take --json` — one typed mutation receipt, safe to gate worker startup on.
    val renderClaimReceiptJson: receipt: ClaimReceipt -> string

    /// `inbox --json` — a JSON array of the messages addressed to this worker.
    val renderInboxJson: msgs: (string * Reads.Message) list -> string

    /// `lint --json` — a JSON array of the findings, each carrying its code, severity, and the item it is
    /// about. A real JSON writer, so a finding detail carrying a quote cannot forge the array.
    val renderLintJson: findings: LintFinding list -> string

    /// `predicate --json` — a single JSON object: the ADR-0050 verdict and its assertion (.github#1202).
    /// A real JSON writer, so a governing note carrying a quote cannot forge the object.
    val renderPredicateJson: result: PredicateResult -> string

    /// `reconcile --json` / `reconcile --apply --json` — the findings array, and under `--apply` how each
    /// repair went (.github#1524), so a mutating verb's outcome is IN its document rather than printed
    /// past it.
    ///
    /// THE FIRST SIX KEYS ARE ALPHABETICAL (`id,remedy,rule,size,statement,subject`) BY CONTRACT, not by
    /// preference: this projection was an F# anonymous record, whose fields the compiler sorts, so those
    /// are the bytes existing consumers already parse. The tests pin them. Do not reorder.
    ///
    /// `includeOutcome` is `--apply`, decided once per DOCUMENT (`renderWhoJson`'s `includeWorktree`
    /// precedent, #959): the dry-run shape stays byte-identical, and an apply run cannot emit a ragged
    /// array. It appends exactly the facts `--apply` adds — `field`, `value`, `outcome`, `error`.
    val renderReconcileJson: includeOutcome: bool -> rows: ReconcileRow list -> string

    /// `widen --json` / `set-paths --json` — one typed touch-set receipt (.github#1517). `verdict` is
    /// `disjoint` or `overlap`, derived from `Collisions` rather than carried beside it, so the two can
    /// never disagree. A real JSON writer, so a path token or worker id carrying a quote cannot forge the
    /// object.
    val renderPathUpdateJson: receipt: PathUpdateReceipt -> string
