namespace FS.GG.Coord

/// Pure recommendations for an existing owner. No execution, clock, storage or authority minting.
module OwnerContinuation =
    open System

    type Knowledge<'a> = Known of 'a | Unknown of string | Conflict of string list
    type Attestation =
        { Supplier: string; Role: string; ActualModel: string option; ActualEffort: string option
          Method: string; Contributors: string list; ContributorsComplete: bool; Limitations: string list }
    type Support = NativeObservation of adapter: string * digest: string | AgentAttestation of Attestation | Reported of string
    type Identity = { Subject: string; Owner: string; Slice: string; Attempt: string; Revision: string; ScopeDigest: string }
    type Predicate = ScopeCurrent | EffectClear | Isolated | ResourceShare | NativeRollup | IndependentReview | SourceReady | VerificationPassed | RoutinePreflight | EnvelopeCurrent | OriginalSucceeded | OriginalFailed | SourceDelivered | ChangesRequired | ImmutableScope | SubjectClosed | NativePullRequest | DraftPreflight | ScopeChanged | BlockingPrerequisite | UnregisteredOwnerRoute
    type Fact = { Id: string; Subject: Identity; Predicate: Predicate; Value: Knowledge<bool>; Support: Support; ObservedAt: DateTimeOffset; ValidUntil: DateTimeOffset; Evidence: string list; Supersedes: string list; ArtifactRevision: string option }
    type NextAction = Next | VerifyIsolated | Implement | Review | OpenDraft | Deliver
    type EnvelopeReference = { NodeId: string; UpdatedAt: string; BodySha256: string }
    type Request = { Envelope: EnvelopeReference option; Subject: Identity; Action: NextAction; Original: string option; Facts: Fact list }
    type Recommendation = ContinueOwner of NextAction | ObserveOriginal of string | Await of string | RequestJudgment of string
    type Decision = { Schema: string; PolicyRevision: string; Id: string; Subject: Identity; Recommendation: Recommendation; InputDigest: string; Reasons: string list; DeterminingFacts: string list; NextEvent: string; GrantReference: string option; RequiredRefresh: string list; ArtifactRevision: string option; OriginalReference: string option }

    /// SHA-256 of the UTF-8 input, encoded as lowercase hexadecimal.
    val digest: text: string -> string

    /// Closed wire name of a supported owner action.
    val actionName: NextAction -> string

    /// Closed wire name of a supported fact predicate.
    val predicateName: Predicate -> string

    /// Bind the policy, evaluation clock, exact request and ordered facts for traceability.
    val inputDigest: now: 'a -> request: Request -> string

    /// Native identity joins are mandatory; agent CI attestations never satisfy NativeRollup.
    /// Any retained original is observed through its existing handler, regardless of expiry.
    val recommend: now: DateTimeOffset -> request: Request -> Decision

    /// Stable bounded projection; the decision is advice, never an executable grant.
    val encode: decisions: Decision list -> string
