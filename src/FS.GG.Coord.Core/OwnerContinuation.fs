namespace FS.GG.Coord

/// Pure recommendations for an existing owner. No execution, clock, storage or authority minting.
module OwnerContinuation =
    open System
    open System.Security.Cryptography
    open System.Text
    open System.Text.Json

    type Knowledge<'a> = Known of 'a | Unknown of string | Conflict of string list
    type Attestation =
        { Supplier: string; Role: string; ActualModel: string option; ActualEffort: string option
          Method: string; Contributors: string list; ContributorsComplete: bool; Limitations: string list }
    type Support = NativeObservation of adapter: string * digest: string | AgentAttestation of Attestation | Reported of string
    type Identity = { Subject: string; Owner: string; Slice: string; Attempt: string; Revision: string; ScopeDigest: string }
    type Predicate = ScopeCurrent | EffectClear | Isolated | ResourceShare | NativeRollup | IndependentReview | SourceReady | VerificationPassed | RoutinePreflight | EnvelopeCurrent | OriginalSucceeded | OriginalFailed | SourceDelivered | ChangesRequired | ImmutableScope | SubjectClosed | NativePullRequest | DraftPreflight | ScopeChanged
    type Fact = { Id: string; Subject: Identity; Predicate: Predicate; Value: Knowledge<bool>; Support: Support; ObservedAt: DateTimeOffset; ValidUntil: DateTimeOffset; Evidence: string list; Supersedes: string list }
    type NextAction = Next | VerifyIsolated | Implement | Review | OpenDraft | Deliver
    type EnvelopeReference = { NodeId: string; UpdatedAt: string; BodySha256: string }
    type Request = { Envelope: EnvelopeReference option; Subject: Identity; Action: NextAction; Original: string option; Facts: Fact list }
    type Recommendation = ContinueOwner of NextAction | ObserveOriginal of string | Await of string | RequestJudgment of string
    type Decision = { Schema: string; PolicyRevision: string; Id: string; Subject: Identity; Recommendation: Recommendation; InputDigest: string; Reasons: string list; DeterminingFacts: string list; NextEvent: string; GrantReference: string option; RequiredRefresh: string list; ArtifactRevision: string option }

    let digest (text: string) = SHA256.HashData(Encoding.UTF8.GetBytes text) |> Convert.ToHexStringLower
    let actionName = function Next -> "next" | VerifyIsolated -> "verify-isolated" | Implement -> "implement" | Review -> "review" | OpenDraft -> "open-draft" | Deliver -> "deliver"
    let predicateName = function ScopeCurrent -> "scope-current" | EffectClear -> "effect-clear" | Isolated -> "isolated" | ResourceShare -> "resource-share" | NativeRollup -> "native-rollup" | IndependentReview -> "independent-review" | SourceReady -> "source-ready" | VerificationPassed -> "verification-passed" | RoutinePreflight -> "routine-preflight" | EnvelopeCurrent -> "envelope-current" | OriginalSucceeded -> "original-succeeded" | OriginalFailed -> "original-failed" | SourceDelivered -> "source-delivered" | ChangesRequired -> "changes-required" | ImmutableScope -> "immutable-scope" | SubjectClosed -> "subject-closed" | NativePullRequest -> "native-pull-request" | DraftPreflight -> "draft-preflight" | ScopeChanged -> "scope-changed"
    let private identityWire (s: Identity) = [s.Subject; s.Owner; s.Slice; s.Attempt; s.Revision; s.ScopeDigest]
    let private supportWire = function NativeObservation(a,d) -> ["native";a;d] | AgentAttestation a -> ["attestation";a.Supplier;a.Role;JsonSerializer.Serialize a.ActualModel;JsonSerializer.Serialize a.ActualEffort; a.Method; JsonSerializer.Serialize(List.sort a.Contributors); string a.ContributorsComplete; JsonSerializer.Serialize(List.sort a.Limitations)] | Reported s -> ["reported";s]
    let private factWire (f: Fact) =
        let value = match f.Value with Known b -> ["known"; string b] | Unknown r -> ["unknown";r] | Conflict ids -> "conflict" :: List.sort ids
        {| id = f.Id; subject = identityWire f.Subject; predicate = predicateName f.Predicate; value = value; support = supportWire f.Support
           observedAt = f.ObservedAt.ToUniversalTime().ToString("O"); validUntil = f.ValidUntil.ToUniversalTime().ToString("O"); evidence = List.sort f.Evidence; supersedes = List.sort f.Supersedes |}
    let private policy = "owner-continuation/1"
    let inputDigest now (request: Request) =
        JsonSerializer.Serialize {| policy = policy; now = now; subject = identityWire request.Subject; action = actionName request.Action;
                                   original = request.Original; envelope = request.Envelope; facts = request.Facts |> List.sortBy _.Id |> List.map factWire |} |> digest

    /// Native identity joins are mandatory; agent CI attestations never satisfy NativeRollup.
    /// Any retained original is observed through its existing handler, regardless of expiry.
    let recommend (now: DateTimeOffset) (request: Request) =
        let allFacts = request.Facts |> List.sortBy _.Id
        let valid (f: Fact) = f.Subject = request.Subject && not f.Evidence.IsEmpty && f.ObservedAt <> DateTimeOffset.MinValue && f.ObservedAt <= now && now <= f.ValidUntil && now - f.ObservedAt <= TimeSpan.FromMinutes 5.0
        let sameIssuer left right =
            match left, right with
            | NativeObservation(a, _), NativeObservation(b, _) -> a = b
            | AgentAttestation a, AgentAttestation b -> a.Supplier = b.Supplier && a.Role = b.Role
            | Reported a, Reported b -> a = b
            | _ -> false
        let byId id = allFacts |> List.tryFind (fun f -> f.Id = id)
        let invalidLinks =
            allFacts |> List.exists (fun f -> f.Supersedes |> List.exists (fun id ->
                match byId id with
                | Some old -> id = f.Id || old.Subject <> f.Subject || old.Predicate <> f.Predicate || not (sameIssuer old.Support f.Support) || old.ObservedAt > f.ObservedAt
                | None -> true))
        let cycle =
            if allFacts.Length > 64 then true
            else
                let rec visit path id =
                    if Set.contains id path then true
                    else byId id |> Option.exists (fun f -> f.Supersedes |> List.exists (visit (Set.add id path)))
                allFacts |> List.exists (fun f -> visit Set.empty f.Id)
        let superseded = allFacts |> List.filter valid |> List.collect _.Supersedes |> Set.ofList
        let facts = allFacts |> List.filter (fun f -> not (superseded.Contains f.Id))
        let native = function NativeObservation _ -> true | _ -> false
        let attested = function AgentAttestation a -> not (String.IsNullOrWhiteSpace a.Method) | NativeObservation _ -> true | _ -> false
        let known predicate support =
            let applicable = facts |> List.filter (fun f -> f.Predicate = predicate)
            not applicable.IsEmpty && applicable |> List.forall (fun f -> valid f && f.Value = Known true && support f.Support)
        let status predicate support =
            let applicable = facts |> List.filter (fun f -> f.Predicate = predicate)
            match applicable with
            | [] -> None
            | first :: _ when applicable |> List.forall (fun f -> valid f && support f.Support && f.Value = first.Value) ->
                match first.Value with Known value -> Some value | _ -> None
            | _ -> None
        let reviewer = function AgentAttestation a -> a.Supplier <> request.Subject.Owner && a.Role = "reviewer" && a.ContributorsComplete && not (List.contains a.Supplier a.Contributors) && not (String.IsNullOrWhiteSpace a.Method) | NativeObservation _ -> true | _ -> false
        let reviewConflict = facts |> List.exists (fun f -> f.Predicate = IndependentReview && match f.Value with Conflict _ -> true | _ -> false)
        let repairRequired = facts |> List.exists (fun f -> f.Predicate = ChangesRequired && valid f && f.Value = Known true && reviewer f.Support)
        let changesGap = facts |> List.exists (fun f -> f.Predicate = ChangesRequired && match f.Value with Unknown _ | Conflict _ -> true | _ -> false)
        let inferred =
            if request.Action <> Next then Some request.Action
            elif repairRequired then Some Implement
            else
                match status SourceReady attested, status VerificationPassed attested with
                | Some false, _ -> Some Implement
                | Some true, Some false -> Some VerifyIsolated
                | Some true, Some true when status NativePullRequest native = Some false -> Some OpenDraft
                | Some true, Some true when status NativePullRequest native <> Some true -> None
                | Some true, Some true when not changesGap && not reviewConflict && known IndependentReview reviewer -> Some Deliver
                | Some true, Some true when not changesGap && not reviewConflict -> Some Review
                | _ -> None
        let originalSucceeded original =
            let applicable = facts |> List.filter (fun f -> f.Predicate = OriginalSucceeded)
            known OriginalSucceeded native && applicable |> List.forall (fun f -> List.contains original f.Evidence)
        let originalFailed original =
            let applicable = facts |> List.filter (fun f -> f.Predicate = OriginalFailed)
            known OriginalFailed native && applicable |> List.forall (fun f -> List.contains original f.Evidence)
        let duplicate = allFacts |> List.map _.Id |> Set.ofList |> Set.count <> allFacts.Length
        let recommendation, reasons, event =
            match request.Original with
            | Some original when originalFailed original -> RequestJudgment "original-failed-owner-repair", ["original-terminal-failure"], "owner-repair-or-scoped-judgment"
            | Some original when not (originalSucceeded original) -> ObserveOriginal original, ["original-custody-retained"], "original-terminal-readback"
            | _ when invalidLinks || cycle -> RequestJudgment "invalid-fact-supersession", ["invalid-supersession-or-input-bound"], "corrected-facts"
            | _ when duplicate -> RequestJudgment "duplicate-fact-identities", ["invalid-fact-set"], "corrected-facts"
            | _ when known ScopeChanged native -> RequestJudgment "reconcile-changed-owner-scope", ["owner-scope-changed"], "accountable-scope-judgment"
            | _ when not (known ScopeCurrent native) -> Await "native-scope-refresh", ["scope-unknown-stale-or-changed"], "native-scope-refresh"
            | _ when known SourceDelivered native -> Await "delivery-acceptance-readback", ["source-merged-acceptance-distinct"], "native-acceptance-evidence"
            | _ when known SubjectClosed native -> RequestJudgment "reconcile-closed-owned-subject", ["native-subject-closed"], "native-acceptance-or-authorized-reopening"
            | _ when request.Original.IsNone && not (known EffectClear attested) -> RequestJudgment "locate-original-before-advancement", ["possible-write-unknown"], "original-custody-reference"
            | _ ->
                match inferred with
                | None when status SourceReady attested = Some true && status VerificationPassed attested = Some true && status NativePullRequest native |> Option.isNone -> Await "native-pr-association-refresh", ["native-pr-association-unknown"], "native-pr-association-readback"
                | None -> RequestJudgment "supply-current-owner-stage-state", ["stage-state-unknown-or-conflicting"], "owner-stage-evidence"
                | Some Next -> RequestJudgment "unsupported-stage", ["invalid-stage"], "corrected-facts"
                | Some VerifyIsolated when known Isolated attested && known ResourceShare attested -> ContinueOwner VerifyIsolated, ["owned-work";"adr0096-ordinary-verification"], "verification-result"
                | Some VerifyIsolated -> Await "isolated-output-and-resource-share-evidence", ["isolation-or-resource-unknown"], "isolation-resource-refresh"
                | Some (Implement | Review as action) when known Isolated attested && known ResourceShare attested -> ContinueOwner action, ["owned-work";"owned-isolated-source"], "owner-result"
                | Some (Implement | Review) -> Await "isolated-output-and-resource-share-evidence", ["isolation-or-resource-unknown"], "isolation-resource-refresh"
                | Some OpenDraft when known DraftPreflight native -> ContinueOwner OpenDraft, ["owned-work";"registered-open-draft-preflight";"qualified-pr-helper-draft-at-create"], "native-draft-readback"
                | Some OpenDraft -> RequestJudgment "registered-open-draft-preflight-required", ["draft-route-support-unobserved"], "registered-open-draft-preflight"
                | Some Deliver when not (known NativePullRequest native) -> Await "native-draft-creation", ["native-pr-head-required-before-review-reuse-or-merge"], "native-draft-readback"
                | Some Deliver when repairRequired -> Await "owner-source-repair", ["current-review-requires-changes"], "owner-repair-result"
                | Some Deliver when changesGap || reviewConflict -> RequestJudgment "resolve-current-review-conflict", ["review-conflict-or-gap"], "review-supplier-resolution"
                | Some Deliver when request.Envelope.IsSome && not (known EnvelopeCurrent native) -> RequestJudgment "refresh-current-finite-envelope", ["envelope-unknown-expired-or-changed"], "native-envelope-refresh"
                | Some Deliver when known SourceDelivered native -> Await "delivery-acceptance-readback", ["source-merged-acceptance-distinct"], "native-acceptance-evidence"
                | Some Deliver when not (known NativeRollup native) -> Await "native-check-qualification", ["observed-rollup-unknown-or-unsuccessful"], "existing-required-check-qualification"
                | Some Deliver when not (known IndependentReview reviewer) -> Await "independent-source-review", ["review-unknown-or-not-independent"], "independent-review-result"
                | Some Deliver when known RoutinePreflight native && (request.Envelope.IsNone || known EnvelopeCurrent native) -> ContinueOwner Deliver, ["owned-work";"registered-route-support-only";"executor-revalidates-native-merge-boundary"], "native-delivery-readback"
                | Some Deliver -> RequestJudgment "qualified-protected-envelope-and-native-checks-required", ["registered-route-or-envelope-evidence-required"], "qualified-route-evidence"
        let input = inputDigest (now.ToUniversalTime().ToString("O")) request
        let recommendationWire =
            match recommendation with ContinueOwner a -> ["continue-owner";actionName a] | ObserveOriginal o -> ["observe-original";o] | Await e -> ["await";e] | RequestJudgment q -> ["request-judgment";q]
        let artifactRevision = facts |> List.tryPick (fun f ->
            if valid f && f.Value = Known true && native f.Support && (f.Predicate = DraftPreflight || f.Predicate = NativePullRequest) then
                f.Evidence |> List.tryPick (fun value -> if value.StartsWith("source-head:", StringComparison.Ordinal) then Some(value.Substring 12) else None)
            else None)
        let immutableScope =
            facts
            |> List.tryPick (fun f ->
                if f.Predicate = ImmutableScope && valid f && f.Value = Known true then
                    match f.Support with NativeObservation(_, hash) -> Some hash | _ -> None
                else None)
            |> Option.defaultValue (if known ScopeCurrent native then request.Subject.ScopeDigest else "unknown-native-source")
        let semantic = digest (JsonSerializer.Serialize [policy; request.Subject.Subject; request.Subject.Owner; request.Subject.Slice; request.Subject.Attempt
                                                         immutableScope; Option.defaultValue "unbound-source-artifact" artifactRevision
                                                         JsonSerializer.Serialize recommendationWire; JsonSerializer.Serialize reasons])
        { Schema = "fsgg.coord.owner-continuation/1"; PolicyRevision = policy; Id = semantic
          Subject = request.Subject
          Recommendation = recommendation; InputDigest = input; Reasons = reasons
          DeterminingFacts = allFacts |> List.map _.Id |> List.distinct; NextEvent = event
          GrantReference =
            let applicable = facts |> List.filter (fun f -> f.Predicate = EnvelopeCurrent)
            let originalMatches = request.Original |> Option.exists (fun original -> not applicable.IsEmpty && applicable |> List.forall (fun f -> List.contains original f.Evidence))
            if known EnvelopeCurrent native && originalMatches then request.Envelope |> Option.map (fun e -> e.NodeId + "@" + e.UpdatedAt + ":" + e.BodySha256) else None
          RequiredRefresh =
            match recommendation with
            | ContinueOwner OpenDraft -> ["executor-exact-source-branch-head-body"; "executor-draft-true-at-create"; "executor-no-duplicate-pr"; "executor-current-intake-linkage"]
            | ContinueOwner Deliver -> ["executor-current-r1-linkage"; "executor-delivery-marker"; "executor-native-required-check-population"; "executor-current-authority-custody-resources"]
            | _ -> []
          ArtifactRevision = artifactRevision }

    /// Stable bounded projection; the decision is advice, never an executable grant.
    let encode (decisions: Decision list) =
        decisions |> List.map (fun d ->
            let kind, action, original, condition =
                match d.Recommendation with
                | ContinueOwner a -> "continue-owner", Some(actionName a), None, None
                | ObserveOriginal o -> "observe-original", None, Some o, None
                | Await e -> "await", None, None, Some e
                | RequestJudgment q -> "request-judgment", None, None, Some q
            {| schema = d.Schema; policyRevision = d.PolicyRevision; id = d.Id; subject = d.Subject.Subject; owner = d.Subject.Owner
               sliceId = d.Subject.Slice; attempt = d.Subject.Attempt; revision = d.Subject.Revision; scopeDigest = d.Subject.ScopeDigest
               kind = kind; action = action; original = original; condition = condition; inputDigest = d.InputDigest
               semanticDigest = d.Id; reasonCodes = d.Reasons; determiningFacts = d.DeterminingFacts; nextEvent = d.NextEvent; grantRef = d.GrantReference; requiredRefresh = d.RequiredRefresh; artifactRevision = d.ArtifactRevision; dispatchAuthority = false |}) |> JsonSerializer.Serialize
