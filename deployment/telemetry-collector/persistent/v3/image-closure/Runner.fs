namespace FSGG.Telemetry.PersistentV3.ImageClosure

type Phase = Prepared | Acquired | Validated | BuildingFirst | BuildingSecond | Comparing | Cleanup | Complete | Refused
type BuildResult = NoResult | Unknown | Digest of string
type Qualification = QualificationUnknown | Accepted
type State = { Phase: Phase; Current: bool; Stores: Set<string>; First: BuildResult; Second: BuildResult; Qualification: Qualification; Cancelled: bool; CleanupFailed: bool; Refusal: string option }
type Action = Acquire | Validate | CreateStores | FirstBuilt of string | FirstLost | SecondBuilt of string | Invalidate | Qualify | ObserveStaleResult | Cancel | RemoveStore of string | CleanupFailed | Finish

module Runner =
    let initial = { Phase=Prepared; Current=true; Stores=Set.empty; First=NoResult; Second=NoResult; Qualification=QualificationUnknown; Cancelled=false; CleanupFailed=false; Refusal=None }
    let private refuse reason state = { state with Phase=(if state.Stores.IsEmpty then Refused else Cleanup); Qualification=QualificationUnknown; Refusal=Some reason }
    let apply action state =
        match action, state with
        | Acquire, { Phase=Prepared } -> { state with Phase=Acquired }
        | Validate, { Phase=Acquired; Current=true } -> { state with Phase=Validated }
        | CreateStores, { Phase=Validated; Current=true } -> { state with Phase=BuildingFirst; Stores=set ["build-a"; "build-b"] }
        | FirstBuilt digest, { Phase=BuildingFirst } -> { state with Phase=BuildingSecond; First=Digest digest }
        | FirstLost, { Phase=BuildingFirst } -> { state with Phase=Cleanup; First=Unknown; Qualification=QualificationUnknown }
        | SecondBuilt digest, ({ Phase=BuildingSecond; Current=true; First=Digest first } as current) when first= digest -> { current with Phase=Comparing; Second=Digest digest }
        | SecondBuilt digest, ({ Phase=BuildingSecond } as current) -> { current with Phase=Cleanup; Second=Digest digest; Qualification=QualificationUnknown; Refusal=Some "BuildMismatch" }
        | Invalidate, current -> refuse "InputChanged" { current with Current=false }
        | Qualify, ({ Phase=Comparing; Current=true; First=Digest first; Second=Digest second } as current) when first=second -> { current with Phase=Cleanup; Qualification=Accepted }
        | ObserveStaleResult, ({ Current=false } as current) -> refuse "StaleResult" current
        | Cancel, current when not current.Stores.IsEmpty && current.Phase<>Complete -> { current with Phase=Cleanup; Cancelled=true; Qualification=QualificationUnknown }
        | RemoveStore name, ({ Phase=Cleanup } as current) when current.Stores.Contains name ->
            let next={ current with Stores=current.Stores.Remove name }
            if next.Stores.IsEmpty && (next.Qualification<>Accepted || not next.Current || next.Cancelled || next.CleanupFailed) then { next with Phase=Refused } else next
        | CleanupFailed, ({ Phase=Cleanup } as current) -> { current with CleanupFailed=true; Qualification=QualificationUnknown }
        | Finish, ({ Phase=Cleanup; Stores=stores; Qualification=Accepted; Current=true; Cancelled=false; CleanupFailed=false } as current) when stores.IsEmpty -> { current with Phase=Complete }
        | _ -> refuse "InvalidTransition" state

    let qualificationAccepted state = state.Phase=Complete && state.Stores.IsEmpty && state.Current && state.Qualification=Accepted && not state.Cancelled && not state.CleanupFailed

    let applyStaleResultMutationForCorrespondence removeGuard state =
#if PERSISTENT_V3_REMOVE_STALE_RESULT_GUARD
        let removeGuard = true
#endif
        if removeGuard then { state with Phase=Cleanup; Qualification=Accepted } else apply ObserveStaleResult state
