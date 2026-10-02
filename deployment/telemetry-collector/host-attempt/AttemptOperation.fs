namespace Fs.Gg.Telemetry.HostAttempt

open System

[<RequireQualifiedAccess>]
type MechanismOutcome = Returned of exitCode:int | ResponseLost

type OperationWindow = { RemainingSeconds: int; CancellationRequested: bool }

type OperationContext =
    { HostBindingDll: string
      RecipeRoot: string
      ProfilePath: string
      SourcePinsPath: string }

type IAttemptMechanism =
    abstract MonotonicMilliseconds: unit -> int64
    abstract ConsumeInterruption: unit -> bool
    abstract Sleep: seconds:int -> unit
    abstract AcquireEffectCheck: AttemptState * OperationWindow -> EffectCheckEvidence
    abstract AcquireRunBaseline: AttemptState * OperationWindow -> bool
    abstract Execute: action:FixedAction * timeoutSeconds:int * sensitiveInput:string option -> MechanismOutcome
    abstract AcquireRunListing: AttemptState * OperationWindow -> RunListing
    abstract AcquireOwnedRun: AttemptState * runId:string * OperationWindow -> OwnedRunEvidence
    abstract AcquireNativeEvidence: AttemptState * runId:string * OperationWindow -> NativeEvidence option
    abstract AcquireSecretAbsence: AttemptState * role:SecretRole * OperationWindow -> SecretAbsenceEvidence
    abstract AcquireRunRetirement: AttemptState * runId:string * OperationWindow -> RunRetirementEvidence
    abstract PersistReadback: AttemptState * OperationWindow -> unit

module AttemptOperation =
    [<Literal>]
    let AggregateSeconds = 2700
    [<Literal>]
    let DiscoverySeconds = 90
    [<Literal>]
    let DiscoveryPollSeconds = 2
    [<Literal>]
    let WatchPollSeconds = 45

    let private timeoutFor = function
        | FixedAction.InvokeBinding -> 90
        | FixedAction.InspectAuthMetadata -> 45
        | FixedAction.TransferSecret _ | FixedAction.DeleteSecret _ | FixedAction.ReadSecretAbsence _ -> 45
        | FixedAction.DownloadRunArtifacts _ -> 120
        | FixedAction.EmitRootReadback -> 30
        | _ -> 60

    let runObserved observer (context:OperationContext) (initial:AttemptState) (mechanism:IAttemptMechanism) =
        let started=mechanism.MonotonicMilliseconds()
        let initialElapsedMilliseconds=int64 initial.ElapsedSeconds*1000L
        let budgetMilliseconds=int64 (min initial.BudgetSeconds AggregateSeconds)*1000L
        let elapsedMilliseconds () = initialElapsedMilliseconds + max 0L (mechanism.MonotonicMilliseconds()-started)
        let remainingMilliseconds () = max 0L (budgetMilliseconds-elapsedMilliseconds())
        let remainingSeconds cap = min cap (int(remainingMilliseconds()/1000L))
        let mutable state=initial
        let reduce observation =
            let reduction=AttemptReducer.apply observation state
            state<-reduction.State
            observer observation reduction
        let refresh () =
            let observed=min budgetMilliseconds (elapsedMilliseconds())
            let seconds=int((observed+999L)/1000L)
            if seconds>state.ElapsedSeconds then reduce(Observation.TimeAdvanced(seconds-state.ElapsedSeconds))
        let fail () = reduce(Observation.OperationFailed OperationFailure.MechanismFailure)
        let invoke cap cancellation callback =
            refresh()
            let newlyInterrupted=mechanism.ConsumeInterruption()
            if newlyInterrupted then fail();None
            else
                let available=remainingSeconds cap
                if available<=0 then None
                else
                    try
                        let value=callback {RemainingSeconds=available;CancellationRequested=cancellation || state.CancellationMayHaveEffect}
                        refresh()
                        if remainingMilliseconds()<=0L then None else Some value
                    with _ ->
                        mechanism.ConsumeInterruption()|>ignore
                        fail();None
        let executeWithCap cap sensitive action =
            match invoke (min cap (timeoutFor action)) false (fun window->mechanism.Execute(action,window.RemainingSeconds,sensitive)) with
            | Some outcome -> outcome
            | None -> MechanismOutcome.ResponseLost
        let execute sensitive action = executeWithCap (timeoutFor action) sensitive action
        let checkedState () =
            match invoke 45 false (fun window->mechanism.AcquireEffectCheck(state,window)) with
            | Some evidence -> reduce(Observation.EffectCheckObserved evidence);true
            | None -> false
        let effect sensitive action =
            let cleanupAction=match action with FixedAction.DeleteSecret _ -> true | _ -> false
            if (cleanupAction || checkedState()) && remainingMilliseconds()>0L then
                let intended=AttemptReducer.apply(Observation.EffectRequested action)state
                state<-intended.State;observer (Observation.EffectRequested action) intended
                if intended.Actions=[action] then
                    match execute sensitive action with
                    | MechanismOutcome.Returned 0 -> reduce(Observation.EffectAcknowledged action)
                    | _ -> reduce(Observation.EffectResponseLost action)
        let sleep maximumSeconds =
            let seconds=remainingSeconds maximumSeconds
            if seconds>0 then ignore(invoke seconds false (fun _->mechanism.Sleep seconds))
        let cleanup () =
            match state.OwnedRunId with
            | Some run when state.NativeDisposition=NativeDisposition.NativeUnknown && not state.CancellationMayHaveEffect && remainingMilliseconds()>0L -> effect None (FixedAction.CancelOwnedRun run)
            | _ -> ()
            for role in [SecretRole.NativeAuth;SecretRole.EffectAdmission] do
                if state.SecretsMayHaveEffect.Contains role && not(state.SecretAbsenceObserved.Contains role) && remainingMilliseconds()>0L then
                    effect None (FixedAction.DeleteSecret role)
                    match execute None (FixedAction.ReadSecretAbsence role) with
                    | MechanismOutcome.Returned 0 ->
                        match invoke 45 false (fun window->mechanism.AcquireSecretAbsence(state,role,window)) with
                        | Some evidence -> reduce(Observation.SecretAbsenceObserved evidence)
                        | None -> ()
                    | _ -> reduce Observation.CleanupCallFailed
            match state.OwnedRunId with
            | Some run ->
                let mutable observing=not state.RunRetirementObserved
                while observing && remainingMilliseconds()>0L do
                    match execute None (FixedAction.GetRun run) with
                    | MechanismOutcome.Returned 0 ->
                        match invoke 60 false (fun window->mechanism.AcquireRunRetirement(state,run,window)) with
                        | Some evidence ->
                            reduce(Observation.OwnedRunRetired evidence)
                            observing<-not state.RunRetirementObserved
                            if observing then sleep WatchPollSeconds
                        | None -> observing<-false
                    | _ -> reduce Observation.CleanupCallFailed;observing<-false
            | _ -> ()
        reduce Observation.PlacementObserved
        let checks=[FixedAction.ReadPublicIdentity;FixedAction.InspectAuthMetadata;FixedAction.InvokeBinding]
        let mutable checksOk=true
        for action in checks do if checksOk then checksOk <- execute None action = MechanismOutcome.Returned 0
        if not checksOk && state.Refusal.IsNone then
            if state.ElapsedSeconds<state.BudgetSeconds then reduce Observation.SourceInvalidated else fail()
        if checksOk && state.Refusal.IsNone && remainingMilliseconds()>0L then
            let admission=invoke 90 false (fun window->AttemptPreparation.deriveAdmission context.HostBindingDll context.RecipeRoot context.ProfilePath context.SourcePinsPath state.Prepared.Nonce (window.RemainingSeconds*1000))
            match admission with
            | Some value ->
                effect None (FixedAction.TransferSecret SecretRole.NativeAuth)
                if state.Refusal.IsNone && state.SecretAcknowledgments.Contains SecretRole.NativeAuth then effect (Some value) (FixedAction.TransferSecret SecretRole.EffectAdmission)
                if state.Refusal.IsNone && state.SecretAcknowledgments.Count=2 then
                    match invoke 60 false (fun window->mechanism.AcquireRunBaseline(state,window)) with
                    | Some true -> effect None FixedAction.DispatchOnce
                    | _ -> fail()
            | None -> ()
            if state.DispatchAcknowledged && state.Refusal.IsNone then
                let discoveryDeadline=mechanism.MonotonicMilliseconds()+int64 DiscoverySeconds*1000L
                let localRemaining ()=max 0 (int((discoveryDeadline-mechanism.MonotonicMilliseconds())/1000L))
                let mutable searching=true
                while searching && remainingMilliseconds()>0L && localRemaining()>0 do
                    match executeWithCap (localRemaining()) None FixedAction.ListRuns with
                    | MechanismOutcome.Returned 0 ->
                        match invoke (localRemaining()) false (fun window->mechanism.AcquireRunListing(state,window)) with
                        | Some listing when localRemaining()>0 ->
                            if not listing.CandidateRunIds.IsEmpty then reduce(Observation.RunsObserved listing)
                            if state.CandidateRuns.Length=1 then
                                match invoke (localRemaining()) false (fun window->mechanism.AcquireOwnedRun(state,state.CandidateRuns.Head,window)) with
                                | Some owned when localRemaining()>0 -> reduce(Observation.OwnedRunObserved owned);searching<-false
                                | None -> searching<-false
                                | Some _ -> searching<-false
                            elif state.Phase=Phase.CleanupPending || state.Refusal.IsSome || localRemaining()<=0 then searching<-false
                            else sleep (min DiscoveryPollSeconds (localRemaining()))
                        | None -> searching<-false
                        | Some _ -> searching<-false
                    | _ -> searching<-false
                if searching then reduce Observation.CleanupCallFailed
            match state.OwnedRunId with
            | Some run when state.Refusal.IsNone ->
                let mutable watching=true
                while watching && remainingMilliseconds()>0L do
                    match invoke WatchPollSeconds false (fun window->mechanism.AcquireNativeEvidence(state,run,window)) with
                    | Some(Some evidence) -> reduce(Observation.NativeEvidenceObserved evidence);watching<-false
                    | Some None -> sleep WatchPollSeconds
                    | None -> watching<-false
            | _ -> ()
        if remainingMilliseconds()<=0L && state.Refusal.IsNone && state.Phase<>Phase.Finalized then fail()
        cleanup()
        if state.Phase=Phase.Finalized && remainingMilliseconds()>0L then ignore(invoke 30 false (fun window->mechanism.PersistReadback(state,window)))
        refresh()
        state
    let run context initial mechanism=runObserved (fun _ _->()) context initial mechanism
