namespace Fs.Gg.Telemetry.HostAttempt

module AttemptReducer =
    let private firstRefusal reason state = state.Refusal |> Option.defaultValue reason
    let private cleanupActions state =
        [ for role in [ SecretRole.NativeAuth; SecretRole.EffectAdmission ] do
              if state.SecretsMayHaveEffect.Contains role && not (state.SecretAbsenceObserved.Contains role) then
                  yield FixedAction.DeleteSecret role
                  yield FixedAction.ReadSecretAbsence role ]
    let private refuse reason state =
        let phase = if state.SecretsMayHaveEffect.IsEmpty then Phase.Refused else Phase.CleanupPending
        let cleanup = if state.SecretsMayHaveEffect.IsEmpty then state.CleanupDisposition else CleanupDisposition.CleanupPending
        { State={state with Phase=phase;Refusal=Some(firstRefusal reason state);NativeDisposition=NativeDisposition.NativeRefused;CleanupDisposition=cleanup};Actions=(if phase=Phase.CleanupPending then cleanupActions state else []) }
    let private cleanup state = {state with Phase=Phase.CleanupPending;CleanupDisposition=CleanupDisposition.CleanupPending},cleanupActions state
    let private current state = state.CurrentSourceGeneration=state.Prepared.SourceGeneration && state.CurrentBindingSha256=state.Prepared.BindingSha256
    let private effectCheckMatches (state:AttemptState) (evidence:EffectCheckEvidence) =
        evidence.SourceGeneration=state.Prepared.SourceGeneration && evidence.BindingSha256=state.Prepared.BindingSha256 && evidence.ProfileSha256=state.Prepared.ProfileSha256 && evidence.WorkflowSha256=state.Prepared.WorkflowSha256 && evidence.BindingProducerSha256=state.Prepared.BindingProducerSha256 && evidence.RuntimeHostSha256=state.Prepared.RuntimeHostSha256 && evidence.MechanismAdapterSha256=state.Prepared.MechanismAdapterSha256
    let private effectAllowed state = current state && state.EffectCheckFresh && state.ElapsedSeconds<state.BudgetSeconds && state.Refusal.IsNone && state.Phase<>Phase.Finalized && state.Phase<>Phase.Unknown
    let private runIdentityMatches state repository workflow qualificationRef placementSha nonce = repository=AttemptPreparation.Repository && workflow=AttemptPreparation.Workflow && qualificationRef=AttemptPreparation.QualificationRef && placementSha=state.Prepared.PlacementSha && nonce=state.Prepared.Nonce
    let private validRunId (value:string) = value.Length>0 && value.Length<=32
    let private secretAbsenceValid state (evidence:SecretAbsenceEvidence) = evidence.Repository=AttemptPreparation.Repository && evidence.Environment=AttemptPreparation.Environment && evidence.HttpStatus=200 && evidence.ListingComplete && not evidence.SecretPresent && state.SecretsMayHaveEffect.Contains evidence.Role
    let private applyNativeEvidenceWithGuard ignoreCancellation (evidence:NativeEvidence) state =
        let identity=runIdentityMatches state evidence.Repository evidence.Workflow evidence.QualificationRef evidence.PlacementSha evidence.Nonce && evidence.ProfileSha256=state.Prepared.ProfileSha256 && evidence.OperationId=state.Prepared.OperationId && evidence.BindingSha256=state.Prepared.BindingSha256
        let allowed=state.Phase=Phase.Watching && (ignoreCancellation || not state.CancellationMayHaveEffect) && state.ElapsedSeconds<state.BudgetSeconds && current state && state.OwnedRunId=Some evidence.RunId
        if not(identity && allowed && evidence.EvidenceComplete) then let pending,actions=cleanup {state with NativeDisposition=NativeDisposition.NativeUnknown} in {State=pending;Actions=actions}
        else let accepted=evidence.NonceJoined && evidence.ProfileVerified && evidence.ActivityComplete && evidence.CompletionCommunicated && evidence.ChildAck && evidence.ParentAck && evidence.WriterJoinsHealthy in let pending,actions=cleanup {state with NativeDisposition=(if accepted then NativeDisposition.NativeAccepted else NativeDisposition.NativeUnknown)} in {State=pending;Actions=actions}
    let private retirementObservation observation state =
        match observation with
        | Observation.EffectCheckObserved evidence when state.Refusal=Some "mechanism-failure" && effectCheckMatches state evidence && state.ElapsedSeconds<state.BudgetSeconds && state.Phase<>Phase.Finalized && state.Phase<>Phase.Unknown ->
            {State={state with EffectCheckFresh=true;CurrentSourceGeneration=evidence.SourceGeneration;CurrentBindingSha256=evidence.BindingSha256};Actions=[]}
        | Observation.EffectRequested(FixedAction.CancelOwnedRun run) when state.Refusal=Some "mechanism-failure" && current state && state.EffectCheckFresh && state.ElapsedSeconds<state.BudgetSeconds && state.OwnedRunId=Some run && state.Phase=Phase.CleanupPending && state.NativeDisposition=NativeDisposition.NativeUnknown && not state.RunRetirementObserved && not state.CancellationMayHaveEffect ->
            {State={state with EffectCheckFresh=false;CancellationMayHaveEffect=true};Actions=[FixedAction.CancelOwnedRun run]}
        | Observation.EffectAcknowledged(FixedAction.CancelOwnedRun run) when state.Refusal=Some "mechanism-failure" && state.OwnedRunId=Some run && state.CancellationMayHaveEffect ->
            {State={state with Phase=Phase.CleanupPending;NativeDisposition=NativeDisposition.NativeUnknown};Actions=cleanupActions state}
        | Observation.EffectResponseLost(FixedAction.CancelOwnedRun run) when state.Refusal=Some "mechanism-failure" && state.OwnedRunId=Some run && state.CancellationMayHaveEffect ->
            {State={state with Phase=Phase.Unknown;CleanupDisposition=CleanupDisposition.CleanupUnknown;NativeDisposition=NativeDisposition.NativeUnknown};Actions=[]}
        | Observation.EffectRequested(FixedAction.DeleteSecret role) when state.SecretsMayHaveEffect.Contains role -> {State={state with Phase=Phase.CleanupPending;CleanupDisposition=CleanupDisposition.CleanupPending};Actions=[FixedAction.DeleteSecret role]}
        | Observation.EffectAcknowledged(FixedAction.DeleteSecret role) when state.SecretsMayHaveEffect.Contains role -> {State={state with Phase=Phase.CleanupPending;CleanupDisposition=CleanupDisposition.CleanupPending};Actions=[FixedAction.ReadSecretAbsence role]}
        | Observation.SecretAbsenceObserved evidence when secretAbsenceValid state evidence ->
            let absent=state.SecretAbsenceObserved.Add evidence.Role
            let allAbsent=Set.isSubset state.SecretsMayHaveEffect absent
            let finalizable=allAbsent && (not state.DispatchMayHaveEffect || state.RunRetirementObserved)
            {State={state with Phase=(if finalizable then Phase.Finalized else Phase.CleanupPending);SecretAbsenceObserved=absent;CleanupDisposition=(if allAbsent then CleanupDisposition.SecretsAbsent else CleanupDisposition.CleanupPending)};Actions=(if allAbsent then [FixedAction.EmitRootReadback] else [])}
        | Observation.OwnedRunRetired evidence when state.OwnedRunId=Some evidence.RunId && evidence.RootOwnershipObserved && evidence.HttpStatus=200 && evidence.ObservationComplete && not evidence.RunActive && runIdentityMatches state evidence.Repository evidence.Workflow evidence.QualificationRef evidence.PlacementSha evidence.Nonce ->
            {State={state with RunRetirementObserved=true;Phase=(if state.CleanupDisposition=CleanupDisposition.SecretsAbsent then Phase.Finalized else Phase.CleanupPending)};Actions=[]}
        | Observation.CleanupCallFailed -> {State={state with Phase=Phase.Unknown;CleanupDisposition=CleanupDisposition.CleanupUnknown};Actions=[]}
        | Observation.OperationFailed _ -> {State={state with Phase=Phase.CleanupPending;Refusal=Some(firstRefusal "mechanism-failure" state);NativeDisposition=NativeDisposition.NativeUnknown;CleanupDisposition=CleanupDisposition.CleanupUnknown};Actions=cleanupActions state}
        | Observation.TimeAdvanced seconds when seconds>0 && state.ElapsedSeconds+seconds>=state.ElapsedSeconds -> {State={state with ElapsedSeconds=min state.BudgetSeconds (state.ElapsedSeconds+seconds)};Actions=[]}
        | _ -> {State=state;Actions=[]}
    let rec apply observation state =
        if state.Schema<>"fsgg.telemetry.host-attempt-state/2" then raise(AttemptRefusal "state-schema-refused")
        elif state.Refusal.IsSome then retirementObservation observation state
        else
            match observation with
            | Observation.TimeAdvanced seconds ->
                if seconds<=0 || state.ElapsedSeconds+seconds<state.ElapsedSeconds then refuse "time-refused" state else
                let advanced={state with ElapsedSeconds=min state.BudgetSeconds (state.ElapsedSeconds+seconds)}
                if state.ElapsedSeconds+seconds>=state.BudgetSeconds then let pending,actions=cleanup {advanced with Phase=Phase.Unknown;NativeDisposition=NativeDisposition.NativeUnknown} in {State=pending;Actions=actions} else {State=advanced;Actions=[]}
            | Observation.SourceInvalidated ->
                if state.DispatchMayHaveEffect || not state.SecretsMayHaveEffect.IsEmpty then let pending,actions=cleanup {state with Phase=Phase.Unknown;CurrentSourceGeneration=state.CurrentSourceGeneration+1;NativeDisposition=NativeDisposition.NativeUnknown} in {State=pending;Actions=actions}
                else refuse "source-invalidated" {state with CurrentSourceGeneration=state.CurrentSourceGeneration+1}
            | Observation.SourceRevalidated(generation,binding,profile,workflow) ->
                if generation<>state.Prepared.SourceGeneration || binding<>state.Prepared.BindingSha256 || profile<>state.Prepared.ProfileSha256 || workflow<>state.Prepared.WorkflowSha256 then
                    if state.DispatchMayHaveEffect || not state.SecretsMayHaveEffect.IsEmpty then let pending,actions=cleanup {state with Phase=Phase.Unknown;CurrentSourceGeneration=generation;CurrentBindingSha256=binding;NativeDisposition=NativeDisposition.NativeUnknown} in {State=pending;Actions=actions}
                    else refuse "source-revalidation-refused" {state with CurrentSourceGeneration=generation;CurrentBindingSha256=binding}
                else {State={state with CurrentSourceGeneration=generation;CurrentBindingSha256=binding};Actions=[FixedAction.ReadPublicIdentity]}
            | Observation.PlacementObserved ->
                if not(current state) || state.ElapsedSeconds>=state.BudgetSeconds || state.Refusal.IsSome || state.Phase<>Phase.Prepared then refuse "placement-observation-refused" state else {State={state with Phase=Phase.PlacementObserved;EffectCheckFresh=false};Actions=[FixedAction.ReadPublicIdentity;FixedAction.InspectAuthMetadata;FixedAction.InvokeBinding]}
            | Observation.EffectCheckObserved evidence ->
                let matches=effectCheckMatches state evidence
                if not matches || state.ElapsedSeconds>=state.BudgetSeconds || state.Phase=Phase.Finalized || state.Phase=Phase.Unknown then refuse "effect-check-refused" state
                else {State={state with EffectCheckFresh=true;CurrentSourceGeneration=evidence.SourceGeneration;CurrentBindingSha256=evidence.BindingSha256};Actions=[]}
            | Observation.EffectRequested action ->
                match action with
                | FixedAction.DeleteSecret role when state.SecretsMayHaveEffect.Contains role -> retirementObservation observation state
                | _ when not(effectAllowed state) -> refuse "effect-source-refused" state
                | _ ->
                    match action with
                    | FixedAction.TransferSecret SecretRole.NativeAuth when state.Phase=Phase.PlacementObserved && state.SecretIntentions.IsEmpty -> {State={state with Phase=Phase.SecretPlacementPending;EffectCheckFresh=false;SecretIntentions=state.SecretIntentions.Add SecretRole.NativeAuth;SecretsMayHaveEffect=state.SecretsMayHaveEffect.Add SecretRole.NativeAuth};Actions=[action]}
                    | FixedAction.TransferSecret SecretRole.EffectAdmission when state.Phase=Phase.SecretPlacementPending && state.SecretAcknowledgments=Set.singleton SecretRole.NativeAuth && not(state.SecretIntentions.Contains SecretRole.EffectAdmission) -> {State={state with EffectCheckFresh=false;SecretIntentions=state.SecretIntentions.Add SecretRole.EffectAdmission;SecretsMayHaveEffect=state.SecretsMayHaveEffect.Add SecretRole.EffectAdmission};Actions=[action]}
                    | FixedAction.DispatchOnce when state.Phase=Phase.SecretsObserved && state.SecretAcknowledgments.Count=2 && not state.DispatchIntended -> {State={state with Phase=Phase.DispatchPending;EffectCheckFresh=false;DispatchIntended=true;DispatchMayHaveEffect=true};Actions=[action]}
                    | FixedAction.CancelOwnedRun run when state.OwnedRunId=Some run && (state.Phase=Phase.Watching || state.Phase=Phase.CleanupPending) && state.NativeDisposition=NativeDisposition.NativeUnknown && not state.RunRetirementObserved -> {State={state with EffectCheckFresh=false;CancellationMayHaveEffect=true;NativeDisposition=NativeDisposition.NativeUnknown};Actions=[action]}
                    | _ -> refuse "effect-action-refused" state
            | Observation.EffectAcknowledged action ->
                match action with
                | FixedAction.TransferSecret SecretRole.NativeAuth when state.Phase=Phase.SecretPlacementPending && state.SecretIntentions.Contains SecretRole.NativeAuth && state.SecretAcknowledgments.IsEmpty -> {State={state with SecretAcknowledgments=Set.singleton SecretRole.NativeAuth};Actions=[]}
                | FixedAction.TransferSecret SecretRole.EffectAdmission when state.Phase=Phase.SecretPlacementPending && state.SecretAcknowledgments=Set.singleton SecretRole.NativeAuth && state.SecretIntentions.Contains SecretRole.EffectAdmission -> {State={state with Phase=Phase.SecretsObserved;SecretAcknowledgments=state.SecretAcknowledgments.Add SecretRole.EffectAdmission};Actions=[]}
                | FixedAction.DispatchOnce when state.Phase=Phase.DispatchPending && state.DispatchIntended -> {State={state with Phase=Phase.Discovery;DispatchAcknowledged=true};Actions=[FixedAction.ListRuns]}
                | FixedAction.DeleteSecret _ -> retirementObservation observation state
                | FixedAction.CancelOwnedRun run when state.OwnedRunId=Some run && state.CancellationMayHaveEffect -> {State={state with Phase=Phase.CleanupPending;NativeDisposition=NativeDisposition.NativeUnknown};Actions=cleanupActions state}
                | _ -> refuse "effect-acknowledgment-refused" state
            | Observation.EffectResponseLost action ->
                match action with
                | FixedAction.DispatchOnce when state.DispatchIntended -> let pending,actions=cleanup {state with Phase=Phase.Unknown;DispatchMayHaveEffect=true;NativeDisposition=NativeDisposition.NativeUnknown} in {State=pending;Actions=actions}
                | FixedAction.TransferSecret role when state.SecretIntentions.Contains role -> let pending,actions=cleanup {state with Phase=Phase.Unknown;SecretsMayHaveEffect=state.SecretsMayHaveEffect.Add role} in {State=pending;Actions=actions}
                | FixedAction.DeleteSecret _ | FixedAction.CancelOwnedRun _ -> {State={state with Phase=Phase.Unknown;CleanupDisposition=CleanupDisposition.CleanupUnknown;NativeDisposition=NativeDisposition.NativeUnknown};Actions=[]}
                | _ -> refuse "lost-response-refused" state
            | Observation.RunsObserved listing ->
                let allRuns=listing.PriorRunIds@listing.CandidateRunIds
                let valid=listing.ListingComplete && listing.Repository=AttemptPreparation.Repository && listing.Workflow=AttemptPreparation.Workflow && listing.QualificationRef=AttemptPreparation.QualificationRef && listing.PlacementSha=state.Prepared.PlacementSha && allRuns.Length<=22 && allRuns=List.distinct allRuns && List.forall validRunId allRuns && Set.intersect(Set.ofList listing.PriorRunIds)(Set.ofList listing.CandidateRunIds)|>Set.isEmpty
                if not valid || not state.DispatchMayHaveEffect || state.Phase<>Phase.Discovery then refuse "run-discovery-refused" state
                elif listing.CandidateRunIds.Length=1 then let run=listing.CandidateRunIds.Head in {State={state with CandidateRuns=listing.CandidateRunIds;OwnedRunId=None};Actions=[FixedAction.GetRun run]}
                else let pending,actions=cleanup {state with Phase=Phase.Unknown;CandidateRuns=listing.CandidateRunIds;OwnedRunId=None;NativeDisposition=NativeDisposition.NativeUnknown} in {State=pending;Actions=actions}
            | Observation.OwnedRunObserved evidence ->
                if state.Phase<>Phase.Discovery || state.CandidateRuns<>[evidence.RunId] || not evidence.RootOwnershipObserved || not(runIdentityMatches state evidence.Repository evidence.Workflow evidence.QualificationRef evidence.PlacementSha evidence.Nonce) then refuse "owned-run-observation-refused" state
                else {State={state with Phase=Phase.Watching;OwnedRunId=Some evidence.RunId};Actions=[FixedAction.GetRun evidence.RunId]}
            | Observation.NativeEvidenceObserved evidence ->
                applyNativeEvidenceWithGuard false evidence state
            | Observation.SecretAbsenceObserved _ | Observation.OwnedRunRetired _ | Observation.CleanupCallFailed | Observation.OperationFailed _ -> retirementObservation observation state
    type ModelAction = Prepare|Revalidate|Invalidate|CheckEffect|RequestAuth|AckAuth|LoseAuth|RequestAdmission|AckAdmission|RequestDispatch|AckDispatch|LoseDispatch|DiscoverNone|DiscoverOne|DiscoverTwo|OwnRun|AcceptNative|ObserveUnknownNative|FailOperation|RequestCancel|AckCancel|DeleteAuth|DeleteAdmission|ObserveAuthAbsent|ObserveAdmissionAbsent|RetireRun|ReadbackFailure|AdvanceToDeadline
    let canonicalAction observation =
        match observation with
        | Observation.PlacementObserved->Some "place"
        | Observation.SourceRevalidated _->Some "revalidate"
        | Observation.SourceInvalidated->Some "invalidate"
        | Observation.EffectCheckObserved _->Some "checkEffect"
        | Observation.EffectRequested(FixedAction.TransferSecret SecretRole.NativeAuth)->Some "requestAuth"
        | Observation.EffectAcknowledged(FixedAction.TransferSecret SecretRole.NativeAuth)->Some "ackAuth"
        | Observation.EffectResponseLost(FixedAction.TransferSecret SecretRole.NativeAuth)->Some "loseAuth"
        | Observation.EffectRequested(FixedAction.TransferSecret SecretRole.EffectAdmission)->Some "requestAdmission"
        | Observation.EffectAcknowledged(FixedAction.TransferSecret SecretRole.EffectAdmission)->Some "ackAdmission"
        | Observation.EffectRequested FixedAction.DispatchOnce->Some "requestDispatch"
        | Observation.EffectAcknowledged FixedAction.DispatchOnce->Some "ackDispatch"
        | Observation.EffectResponseLost FixedAction.DispatchOnce->Some "loseDispatch"
        | Observation.RunsObserved value->Some(if value.CandidateRunIds.IsEmpty then "discoverNone" elif value.CandidateRunIds.Length=1 then "discoverOne" else "discoverTwo")
        | Observation.OwnedRunObserved _->Some "ownRun"
        | Observation.NativeEvidenceObserved value->Some(if value.EvidenceComplete&&value.NonceJoined&&value.ProfileVerified&&value.ActivityComplete&&value.CompletionCommunicated&&value.ChildAck&&value.ParentAck&&value.WriterJoinsHealthy then "acceptNative" else "observeUnknownNative")
        | Observation.OperationFailed _->Some "failOwnedNative"
        | Observation.EffectRequested(FixedAction.CancelOwnedRun _)->Some "requestCleanupCancel"
        | Observation.EffectAcknowledged(FixedAction.CancelOwnedRun _)->Some "ackCleanupCancel"
        | Observation.SecretAbsenceObserved value->Some(if value.Role=SecretRole.NativeAuth then "observeSecretAbsent" else "observeSecretAbsent")
        | Observation.OwnedRunRetired _->Some "retireRun"
        | Observation.CleanupCallFailed->Some "readbackFailure"
        | Observation.TimeAdvanced _->None
        | Observation.EffectRequested(FixedAction.DeleteSecret _)|Observation.EffectAcknowledged(FixedAction.DeleteSecret _)|Observation.EffectResponseLost(FixedAction.DeleteSecret _)->None
        | _->None
    let private listing state candidates={Repository=AttemptPreparation.Repository;Workflow=AttemptPreparation.Workflow;QualificationRef=AttemptPreparation.QualificationRef;PlacementSha=state.Prepared.PlacementSha;PriorRunIds=["prior-run"];CandidateRunIds=candidates;ListingComplete=true}
    let private ownership state={Repository=AttemptPreparation.Repository;Workflow=AttemptPreparation.Workflow;QualificationRef=AttemptPreparation.QualificationRef;PlacementSha=state.Prepared.PlacementSha;Nonce=state.Prepared.Nonce;RunId="run-1";RootOwnershipObserved=true}
    let private retirement state={Repository=AttemptPreparation.Repository;Workflow=AttemptPreparation.Workflow;QualificationRef=AttemptPreparation.QualificationRef;PlacementSha=state.Prepared.PlacementSha;Nonce=state.Prepared.Nonce;RunId="run-1";RootOwnershipObserved=true;HttpStatus=200;ObservationComplete=true;RunActive=false}
    let private absence role={Role=role;Repository=AttemptPreparation.Repository;Environment=AttemptPreparation.Environment;HttpStatus=200;ListingComplete=true;SecretPresent=false}
    let private native state joined={RunId="run-1";Repository=AttemptPreparation.Repository;Workflow=AttemptPreparation.Workflow;QualificationRef=AttemptPreparation.QualificationRef;PlacementSha=state.Prepared.PlacementSha;Nonce=state.Prepared.Nonce;ProfileSha256=state.Prepared.ProfileSha256;OperationId=state.Prepared.OperationId;BindingSha256=state.Prepared.BindingSha256;EvidenceComplete=true;NonceJoined=joined;ProfileVerified=true;ActivityComplete=true;CompletionCommunicated=true;ChildAck=true;ParentAck=true;WriterJoinsHealthy=true}
    let private effectCheck state={SourceGeneration=state.Prepared.SourceGeneration;BindingSha256=state.Prepared.BindingSha256;ProfileSha256=state.Prepared.ProfileSha256;WorkflowSha256=state.Prepared.WorkflowSha256;BindingProducerSha256=state.Prepared.BindingProducerSha256;RuntimeHostSha256=state.Prepared.RuntimeHostSha256;MechanismAdapterSha256=state.Prepared.MechanismAdapterSha256}
    let internal applyNativeEvidenceMutationForCorrespondence ignoreCancellation state =
        let evidence=native state true
        applyNativeEvidenceWithGuard ignoreCancellation evidence state
    let applyModelAction action state=
        let reduce observation value=(apply observation value).State
        match action with
        | Prepare->reduce Observation.PlacementObserved state | Revalidate->reduce(Observation.SourceRevalidated(state.Prepared.SourceGeneration,state.Prepared.BindingSha256,state.Prepared.ProfileSha256,state.Prepared.WorkflowSha256))state | Invalidate->reduce Observation.SourceInvalidated state | CheckEffect->reduce(Observation.EffectCheckObserved(effectCheck state))state
        | RequestAuth->reduce(Observation.EffectRequested(FixedAction.TransferSecret SecretRole.NativeAuth))state | AckAuth->reduce(Observation.EffectAcknowledged(FixedAction.TransferSecret SecretRole.NativeAuth))state | LoseAuth->reduce(Observation.EffectResponseLost(FixedAction.TransferSecret SecretRole.NativeAuth))state
        | RequestAdmission->reduce(Observation.EffectRequested(FixedAction.TransferSecret SecretRole.EffectAdmission))state | AckAdmission->reduce(Observation.EffectAcknowledged(FixedAction.TransferSecret SecretRole.EffectAdmission))state
        | RequestDispatch->reduce(Observation.EffectRequested FixedAction.DispatchOnce)state | AckDispatch->reduce(Observation.EffectAcknowledged FixedAction.DispatchOnce)state | LoseDispatch->reduce(Observation.EffectResponseLost FixedAction.DispatchOnce)state
        | DiscoverNone->reduce(Observation.RunsObserved(listing state []))state | DiscoverOne->reduce(Observation.RunsObserved(listing state ["run-1"]))state | DiscoverTwo->reduce(Observation.RunsObserved(listing state ["run-1";"run-2"]))state | OwnRun->reduce(Observation.OwnedRunObserved(ownership state))state
        | AcceptNative->reduce(Observation.NativeEvidenceObserved(native state true))state | ObserveUnknownNative->reduce(Observation.NativeEvidenceObserved(native state false))state
        | FailOperation->reduce(Observation.OperationFailed OperationFailure.MechanismFailure)state
        | RequestCancel->reduce(Observation.EffectRequested(FixedAction.CancelOwnedRun "run-1"))state | AckCancel->reduce(Observation.EffectAcknowledged(FixedAction.CancelOwnedRun "run-1"))state
        | DeleteAuth->reduce(Observation.EffectRequested(FixedAction.DeleteSecret SecretRole.NativeAuth))state | DeleteAdmission->reduce(Observation.EffectRequested(FixedAction.DeleteSecret SecretRole.EffectAdmission))state
        | ObserveAuthAbsent->reduce(Observation.SecretAbsenceObserved(absence SecretRole.NativeAuth))state | ObserveAdmissionAbsent->reduce(Observation.SecretAbsenceObserved(absence SecretRole.EffectAdmission))state | RetireRun->reduce(Observation.OwnedRunRetired(retirement state))state
        | ReadbackFailure->reduce Observation.CleanupCallFailed state | AdvanceToDeadline->reduce(Observation.TimeAdvanced 675)state
