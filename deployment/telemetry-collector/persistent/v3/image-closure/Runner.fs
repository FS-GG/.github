namespace FSGG.Telemetry.PersistentV3.ImageClosure

open System
open System.IO

type Phase = Prepared | Acquired | Validated | CreatingStores | BuildingFirst | BuildingSecond | Comparing | Revalidating | Qualifying | Cleanup | Complete | Refused | Indeterminate
type BuildResult = NoResult | Unknown | Digest of string
type Qualification = QualificationUnknown | Accepted
type Effect = AcquireInputs of string | ValidateInputs of string | CreateStore of string | StartBuild of string*string | AwaitBuild of string*string*string | CancelBuild of string | CompareBuilds of string*string | QualifyInactive of string | RemoveStore of string
type Observation = InputsAcquired of string | InputsValidated of string | StoreCreated of string*string | StoreMayHaveEffect of string | BuildStarted of string*string | BuildCompleted of string*string*string*string | BuildMayHaveEffect of string | BuildCancelled of string | BuildsCompared of bool | QualificationObserved of bool*string | StoreAbsent of string | EffectFailed of string
type State = { Phase:Phase; ExpectedInput:string; Current:bool; Owned:Map<string,string>; MayHaveEffect:Set<string>; Running:Map<string,string>; UnknownBuilds:Set<string>; First:BuildResult; Second:BuildResult; Qualification:Qualification; Cancelled:bool; CleanupFailed:bool; Refusal:string option; Pending:Effect option; LastEffect:string }

module Runner =
    let initial expected={Phase=Prepared;ExpectedInput=expected;Current=true;Owned=Map.empty;MayHaveEffect=Set.empty;Running=Map.empty;UnknownBuilds=Set.empty;First=NoResult;Second=NoResult;Qualification=QualificationUnknown;Cancelled=false;CleanupFailed=false;Refusal=None;Pending=None;LastEffect="None"}
    let private effectName=function AcquireInputs _->"Acquire"|ValidateInputs _->"Validate"|CreateStore "a"->"CreateA"|CreateStore _->"CreateB"|StartBuild("a",_)->"StartA"|StartBuild _->"StartB"|AwaitBuild("a",_,_)->"AwaitA"|AwaitBuild _->"AwaitB"|CancelBuild _->"Cancel"|CompareBuilds _->"Compare"|QualifyInactive _->"Qualify"|RemoveStore _->"Remove"
    let private refuse reason state={state with Phase=(if state.Owned.IsEmpty&&state.MayHaveEffect.IsEmpty&&state.Running.IsEmpty then Refused else Cleanup);Qualification=QualificationUnknown;Refusal=Some reason;Pending=None}
    let nextEffect state =
        if state.Pending.IsSome||state.Phase=Complete||state.Phase=Refused||state.CleanupFailed then state,None else
        let effect=
          match state.Phase with
          | Prepared->Some(AcquireInputs state.ExpectedInput)
          | Acquired->Some(ValidateInputs state.ExpectedInput)
          | Validated->Some(CreateStore "a")
          | CreatingStores when not(state.Owned.ContainsKey "a")->Some(CreateStore "a")
          | CreatingStores when not(state.Owned.ContainsKey "b")->Some(CreateStore "b")
          | CreatingStores->Some(StartBuild("a",state.ExpectedInput))
          | BuildingFirst when state.Running.ContainsKey "a"->Some(AwaitBuild("a",state.Running["a"],state.ExpectedInput))
          | BuildingFirst->Some(StartBuild("a",state.ExpectedInput))
          | BuildingSecond when state.Running.ContainsKey "b"->Some(AwaitBuild("b",state.Running["b"],state.ExpectedInput))
          | BuildingSecond->Some(StartBuild("b",state.ExpectedInput))
          | Comparing->match state.First,state.Second with Digest a,Digest b->Some(CompareBuilds(a,b))|_->None
          | Revalidating->Some(ValidateInputs state.ExpectedInput)
          | Qualifying->match state.First with Digest digest->Some(QualifyInactive digest)|_->None
          | Cleanup when not state.Running.IsEmpty->state.Running|>Map.toSeq|>Seq.head|>snd|>CancelBuild|>Some
          | Cleanup when not state.Owned.IsEmpty->state.Owned|>Map.toSeq|>Seq.head|>snd|>RemoveStore|>Some
          | _->None
        match effect with
        | Some value->
            {state with Pending=Some value;LastEffect=effectName value},Some value
        | None->state,None
    let observe observation state =
      match state.Pending,observation with
      | Some(AcquireInputs expected),InputsAcquired actual when expected=actual->{state with Phase=Acquired;Pending=None}
      | Some(ValidateInputs expected),InputsValidated actual when expected=actual&&state.Phase=Acquired->{state with Phase=Validated;Pending=None;Current=true}
      | Some(ValidateInputs expected),InputsValidated actual when expected=actual&&state.Phase=Revalidating->{state with Phase=Qualifying;Pending=None;Current=true}
      | Some(ValidateInputs _),InputsValidated _->
#if PERSISTENT_V3_REMOVE_INPUT_REVALIDATION_GUARD
          {state with Phase=Qualifying;Pending=None;Current=true}
#else
          refuse "InputChanged" {state with Current=false}
#endif
      | Some(CreateStore logical),StoreCreated(observed,resource) when logical=observed && not(state.Owned.ContainsKey logical)->{state with Phase=CreatingStores;Owned=state.Owned.Add(logical,resource);Pending=None}
      | Some(CreateStore logical),StoreMayHaveEffect observed when logical=observed->{state with Phase=Cleanup;MayHaveEffect=state.MayHaveEffect.Add logical;Pending=None;Qualification=QualificationUnknown;Refusal=Some "StoreAckLost"}
      | Some(StartBuild(logical,_)),BuildStarted(observed,resource) when logical=observed->{state with Phase=(if logical="a" then BuildingFirst else BuildingSecond);Running=state.Running.Add(logical,resource);Pending=None}
      | Some(StartBuild(logical,_)),BuildMayHaveEffect observed when logical=observed->{state with Phase=Cleanup;UnknownBuilds=state.UnknownBuilds.Add logical;First=(if logical="a" then Unknown else state.First);Second=(if logical="b" then Unknown else state.Second);Pending=None;Qualification=QualificationUnknown;Refusal=Some "BuildAckLost"}
      | Some(AwaitBuild(logical,resource,expected)),BuildCompleted(observed,processId,digest,input) when logical=observed&&resource=processId&&input=expected&&logical="a"->{state with Phase=BuildingSecond;Running=state.Running.Remove logical;First=Digest digest;Pending=None}
      | Some(AwaitBuild(logical,resource,expected)),BuildCompleted(observed,processId,digest,input) when logical=observed&&resource=processId&&input=expected&&logical="b"->{state with Phase=Comparing;Running=state.Running.Remove logical;Second=Digest digest;Pending=None}
      | Some(AwaitBuild _),BuildCompleted _->refuse "StaleResult" {state with Current=false}
      | Some(CancelBuild resource),BuildCancelled observed when resource=observed->{state with Running=state.Running|>Map.filter(fun _ value->value<>resource);Pending=None}
      | Some(CompareBuilds(a,b)),BuildsCompared true when a=b->{state with Phase=Revalidating;Pending=None}
      | Some(CompareBuilds _),BuildsCompared _->{state with Phase=Cleanup;Pending=None;Refusal=Some "BuildMismatch";Qualification=QualificationUnknown}
      | Some(QualifyInactive _),QualificationObserved(true,input) when input=state.ExpectedInput&&state.Current->{state with Phase=Cleanup;Pending=None;Qualification=Accepted}
      | Some(QualifyInactive _),QualificationObserved _->refuse "StaleResult" {state with Current=false}
      | Some(RemoveStore resource),StoreAbsent observed when resource=observed->
          let owned=state.Owned|>Map.filter(fun _ value->value<>resource)
          if owned.IsEmpty&&state.MayHaveEffect.IsEmpty then {state with Phase=(if not state.UnknownBuilds.IsEmpty then Indeterminate elif state.Qualification=Accepted&&state.Current&&not state.Cancelled then Complete else Refused);Owned=owned;Pending=None} else {state with Owned=owned;Pending=None}
      | Some(RemoveStore _),EffectFailed _->{state with CleanupFailed=true;Qualification=QualificationUnknown;Pending=None}
      | Some _,EffectFailed reason->refuse reason state
      | _->refuse "ObservationMismatch" state
    let cancel state=if state.Owned.IsEmpty&&state.MayHaveEffect.IsEmpty&&state.Running.IsEmpty then refuse "Cancelled" state else {state with Phase=Cleanup;Cancelled=true;Qualification=QualificationUnknown;Pending=None}
    let qualificationAccepted state=state.Phase=Complete&&state.Owned.IsEmpty&&state.MayHaveEffect.IsEmpty&&state.Running.IsEmpty&&state.UnknownBuilds.IsEmpty&&state.Current&&state.Qualification=Accepted&&not state.Cancelled&&not state.CleanupFailed

type IRunnerMechanism = abstract Execute:Effect->Observation
module RunnerExecution =
    let run maximumSteps (mechanism:IRunnerMechanism) state =
      let rec loop remaining current trace =
        if remaining=0||current.Phase=Complete||current.Phase=Refused||current.CleanupFailed then current,List.rev trace else
        let requested,effect=Runner.nextEffect current
        match effect with None->requested,List.rev trace|Some command->let observation=mechanism.Execute command in loop (remaining-1) (Runner.observe observation requested) ((command,observation)::trace)
      loop maximumSteps state []
    type DirectoryFixture(root:string,input:string,first:string,second:string)=
      interface IRunnerMechanism with
        member _.Execute effect=
          match effect with
          | AcquireInputs _->InputsAcquired input
          | ValidateInputs _->InputsValidated input
          | CreateStore logical->
              let path=Path.Combine(root,"store-"+logical)
              Directory.CreateDirectory path|>ignore
              StoreCreated(logical,path)
          | StartBuild(logical,_)->BuildStarted(logical,"process-"+logical)
          | AwaitBuild(logical,resource,_)->BuildCompleted(logical,resource,(if logical="a" then first else second),input)
          | CancelBuild resource->BuildCancelled resource
          | CompareBuilds(a,b)->BuildsCompared(a=b)
          | QualifyInactive _->QualificationObserved(true,input)
          | RemoveStore resource->
              if Directory.Exists resource then Directory.Delete resource
              StoreAbsent resource
