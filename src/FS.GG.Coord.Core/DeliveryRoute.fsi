namespace FS.GG.Coord

/// The explicit, agent-authored decision that routes an item through lightweight or SDD delivery.
///
/// `SddRequired` names a governing SDD work package (`SddWorkId`, `SpecHome`) this decision binds to,
/// but this module never checks whether that package's files exist or are `implementationReady` — that
/// is a filesystem fact the Cli layer reads separately (`Client.sddEvidenceErrors`) and reports rather
/// than enforces. The receipt itself may be recorded, and the item may schedule and be claimed, before
/// the package exists: the CLAIMED WORKER is the actor who authors or completes it (via `fsgg-sdd`,
/// inside their worktree) before touching the item's declared implementation paths (#2298). Requiring
/// the package up front made `sdd-required` unrecordable for any item that did not already carry
/// one — the only actor able to produce it could never get claimed to do so.
module DeliveryRoute =
    [<Literal>]
    val Schema: string = "fsgg.coord.delivery-route/v1"

    type Route = Lightweight | SddRequired

    type Receipt =
        { Schema: string
          Subject: string
          SubjectRevision: string
          Route: Route option
          Agent: string
          Timestamp: string
          ReasonCodes: string list
          Rationale: string
          DeclaredImpacts: string list
          ObservedFacts: string list
          SddWorkId: string option
          SpecHome: string option
          RequiredGates: string list }

    /// The only route decision states a scheduler is allowed to consume.  A missing or
    /// unreadable fact deliberately has no "lightweight by default" arm.
    type Verdict = Current of Receipt | Stale of string list | Unreadable of string list

    /// The package directories the `sdd-required` route ITSELF is guaranteed to produce —
    /// `work/<sddWorkId>` and `readiness/<sddWorkId>` — or the EMPTY LIST for any receipt that does not
    /// cleanly name one (a lightweight route, a missing or malformed binding, a work id that is not
    /// path-safe).
    ///
    /// WHY THIS EXISTS (.github#2324). Those two directories are mandatory output of the route, not
    /// optional scope a worker chooses: `pnext-item` makes the CLAIMED WORKER responsible for authoring
    /// the package (#2298), and nothing in filing, routing, or claiming declares the directories in the
    /// item's `Paths:` up front — the paths cannot be named before the item exists, and nothing revisits
    /// the declaration once they do. So EVERY `sdd-required` item drifted against its own declaration by
    /// construction, and every one of them paid a mid-flight `widen` to say so. Measured on four items
    /// (`.github#2305`, `#2306`, `#2366`, `#2496`); `#2496` widened twice.
    ///
    /// THE REMEDY IS AN EXEMPTION, NOT AN AUTO-DECLARATION, and that was a decision (see
    /// `work/2324-mandatory-sdd-output-enforcement/spec.md`'s `Rejected Alternative`). `widen`/`set-paths`
    /// are the only writers of a `Paths:` line; both require HOLDING the item's claim (#706) and both gate
    /// the PATCH on a live board-wide collision scan (#523/#353). Auto-declaring would therefore spend one
    /// board scan and one issue-body PATCH per `sdd-required` item to reserve two directories derived from
    /// that item's own id — which only that item's claim holder can ever author, so the reservation buys
    /// nothing the claim lock does not already give.
    ///
    /// DECLARING THEM STAYS LEGAL. This is NOT ADR-0044's generated-artifact rule, which additionally
    /// REFUSES such a token at `widen`: that refusal rests on "nobody authors them", and an SDD package is
    /// authored. Four live items already declare theirs and keep working byte-unchanged; this removes the
    /// OBLIGATION to declare, never the ability.
    ///
    /// FAILS CLOSED, ALWAYS. The empty list exempts nothing and leaves every changed file reported exactly
    /// as before; a wrong list would silently excuse a path nobody declared. It is bound to the receipt's
    /// OWN `sddWorkId`, so `work/` and `readiness/` are never roots and another item's package is never
    /// exempted.
    val mandatorySddPaths: receipt: Receipt -> string list

    /// Rejects incomplete, inferred, or malformed decisions. Checklist facts are evidence only.
    val validate: expectedSubject: string -> expectedRevision: string -> Receipt -> Result<Receipt, string list>
    val decide: expectedSubject: string -> expectedRevision: string option -> Receipt option -> Verdict
