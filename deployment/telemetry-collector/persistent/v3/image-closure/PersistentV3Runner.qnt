module PersistentV3Runner {
 type State={phase:str,inputId:str,current:bool,owned:str,running:str,storeUnknown:bool,unknownBuild:bool,identityOk:bool,first:str,second:str,qualification:str,cancelled:bool,cleanupFailed:bool,refusal:str,pending:str,lastEffect:str}
 var state:State
 val initialState={phase:"Prepared",inputId:"input",current:true,owned:"None",running:"None",storeUnknown:false,unknownBuild:false,identityOk:true,first:"None",second:"None",qualification:"Unknown",cancelled:false,cleanupFailed:false,refusal:"None",pending:"None",lastEffect:"None"}
 action init=state'=initialState
 action requestAcquire=all{state.phase=="Prepared",state.pending=="None",state'={...state,pending:"Acquire",lastEffect:"Acquire"}}
 action observeAcquire=all{state.pending=="Acquire",state'={...state,phase:"Acquired",pending:"None"}}
 action requestValidate=all{(state.phase=="Acquired" or state.phase=="Revalidating"),state.pending=="None",state'={...state,pending:"Validate",lastEffect:"Validate"}}
 action observeValid=all{state.pending=="Validate",state.current,state'={...state,phase:if(state.phase=="Acquired")"Validated" else "Qualifying",pending:"None"}}
 action observeStale=all{state.pending=="Validate",state'={...state,phase:if(state.owned=="None" and state.running=="None")"Refused" else "Cleanup",current:false,qualification:"Unknown",refusal:"InputChanged",pending:"None"}}
 action requestStoreA=all{state.phase=="Validated",state.pending=="None",state'={...state,pending:"CreateA",lastEffect:"CreateA"}}
 action observeStoreA=all{state.pending=="CreateA",state'={...state,phase:"CreatingStores",owned:"A",pending:"None"}}
 action requestStoreB=all{state.phase=="CreatingStores",state.owned=="A",state.pending=="None",state'={...state,pending:"CreateB",lastEffect:"CreateB"}}
 action observeStoreB=all{state.pending=="CreateB",state'={...state,owned:"AB",pending:"None"}}
 action observeDuplicateStore=all{state.pending=="CreateB",state'={...state,phase:"Cleanup",storeUnknown:true,identityOk:false,qualification:"Unknown",refusal:"DuplicateResourceIdentity",pending:"None"}}
 action requestStartA=all{state.phase=="CreatingStores",state.owned=="AB",state.pending=="None",state'={...state,pending:"StartA",lastEffect:"StartA"}}
 action observeStartedA=all{state.pending=="StartA",state'={...state,phase:"BuildingFirst",running:"A",pending:"None"}}
 action observeDuplicateProcess=all{state.pending=="StartA",state'={...state,phase:"Cleanup",unknownBuild:true,identityOk:false,qualification:"Unknown",refusal:"DuplicateProcessIdentity",pending:"None"}}
 action loseBuildA=all{state.pending=="StartA",state'={...state,phase:"Cleanup",unknownBuild:true,first:"Unknown",qualification:"Unknown",refusal:"BuildAckLost",pending:"None"}}
 action requestAwaitA=all{state.phase=="BuildingFirst",state.running=="A",state.pending=="None",state'={...state,pending:"AwaitA",lastEffect:"AwaitA"}}
 action observeBuildA=all{state.pending=="AwaitA",state'={...state,phase:"BuildingSecond",running:"None",first:"aaa",pending:"None"}}
 action requestStartB=all{state.phase=="BuildingSecond",state.pending=="None",state'={...state,pending:"StartB",lastEffect:"StartB"}}
 action observeStartedB=all{state.pending=="StartB",state'={...state,running:"B",pending:"None"}}
 action requestAwaitB=all{state.phase=="BuildingSecond",state.running=="B",state.pending=="None",state'={...state,pending:"AwaitB",lastEffect:"AwaitB"}}
 action observeBuildB=all{state.pending=="AwaitB",state'={...state,phase:"Comparing",running:"None",second:"aaa",pending:"None"}}
 action observeBuildMismatch=all{state.pending=="AwaitB",state'={...state,phase:"Comparing",running:"None",second:"bbb",pending:"None"}}
 action requestCompare=all{state.phase=="Comparing",state.pending=="None",state'={...state,pending:"Compare",lastEffect:"Compare"}}
 action observeMatch=all{state.pending=="Compare",state.first==state.second,state'={...state,phase:"Revalidating",pending:"None"}}
 action observeMismatch=all{state.pending=="Compare",state.first!=state.second,state'={...state,phase:"Cleanup",qualification:"Unknown",refusal:"BuildMismatch",pending:"None"}}
 action requestQualify=all{state.phase=="Qualifying",state.pending=="None",state'={...state,pending:"Qualify",lastEffect:"Qualify"}}
 action observeQualified=all{state.pending=="Qualify",state.current,state'={...state,phase:"Cleanup",qualification:"Accepted",pending:"None"}}
 action cancel=all{state.owned!="None",state'={...state,phase:"Cleanup",cancelled:true,qualification:"Unknown",pending:"None"}}
 action requestCancel=all{state.phase=="Cleanup",state.running!="None",state.pending=="None",state'={...state,pending:"Cancel",lastEffect:"Cancel"}}
 action observeCancelled=all{state.pending=="Cancel",state'={...state,running:"None",pending:"None"}}
 action requestRemove=all{state.phase=="Cleanup",state.running=="None",state.owned!="None",state.pending=="None",state'={...state,pending:"Remove",lastEffect:"Remove"}}
 action observeRemoved=all{state.pending=="Remove",state'={...state,owned:if(state.owned=="AB")"B" else "None",pending:"None",phase:if(state.owned!="AB" and (state.unknownBuild or state.storeUnknown))"Unknown" else if(state.owned!="AB" and state.qualification=="Accepted" and state.current and not(state.cancelled))"Complete" else if(state.owned!="AB")"Refused" else "Cleanup"}}
 action cleanupFailure=all{(state.pending=="Remove" or state.pending=="Cancel"),state'={...state,cleanupFailed:true,qualification:"Unknown",pending:"None"}}
 action step=any{requestAcquire,observeAcquire,requestValidate,observeValid,observeStale,requestStoreA,observeStoreA,requestStoreB,observeStoreB,observeDuplicateStore,requestStartA,observeStartedA,observeDuplicateProcess,loseBuildA,requestAwaitA,observeBuildA,requestStartB,observeStartedB,requestAwaitB,observeBuildB,observeBuildMismatch,requestCompare,observeMatch,observeMismatch,requestQualify,observeQualified,cancel,requestCancel,observeCancelled,requestRemove,observeRemoved,cleanupFailure}
 val successIsClosed=state.phase!="Complete" or (state.current and state.owned=="None" and state.running=="None" and not(state.storeUnknown) and not(state.unknownBuild) and state.identityOk and state.qualification=="Accepted" and not(state.cancelled) and not(state.cleanupFailed))
 val unknownNeverSucceeds=(not(state.storeUnknown) and not(state.unknownBuild)) or state.phase!="Complete"
 val effectsOrdered=state.pending=="None" or state.pending==state.lastEffect
 val allInvariants=all{successIsClosed,unknownNeverSucceeds,effectsOrdered}
 val successWitness=state.phase=="Complete"
 val lostWitness=state.phase=="Unknown" and state.unknownBuild and state.first=="Unknown"
 val mismatchWitness=state.phase=="Refused" and state.refusal=="BuildMismatch"
 val cancellationWitness=state.phase=="Refused" and state.cancelled and state.running=="None" and state.qualification=="Unknown"
 val cleanupFailureWitness=state.cleanupFailed and state.owned!="None" and state.qualification=="Unknown"
 val staleWitness=state.phase=="Refused" and not(state.current) and state.refusal=="InputChanged"
 val duplicateStoreWitness=state.phase=="Unknown" and state.storeUnknown and not(state.identityOk)
 val duplicateProcessWitness=state.phase=="Unknown" and state.unknownBuild and not(state.identityOk)
}
