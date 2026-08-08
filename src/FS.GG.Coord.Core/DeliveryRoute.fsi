namespace FS.GG.Coord

/// The explicit, agent-authored decision that routes an item through lightweight or SDD delivery.
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
