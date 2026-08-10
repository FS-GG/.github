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

    /// Rejects incomplete, inferred, or malformed decisions. Checklist facts are evidence only.
    val validate: expectedSubject: string -> expectedRevision: string -> Receipt -> Result<Receipt, string list>
    val decide: expectedSubject: string -> expectedRevision: string option -> Receipt option -> Verdict
