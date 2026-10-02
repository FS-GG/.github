module HostAttempt {
 type State={phase:str,current:bool,checkFresh:bool,refusal:str,authIntent:bool,authAck:bool,admissionIntent:bool,admissionAck:bool,dispatchIntentions:int,dispatchAck:bool,dispatchMayHaveEffect:bool,candidateCount:int,ownedRun:int,cancellationMayHaveEffect:bool,native:str,secretsAbsent:int,runRetired:bool,cleanup:str,elapsed:int}
 var state:State
 val initialState={phase:"Prepared",current:true,checkFresh:false,refusal:"None",authIntent:false,authAck:false,admissionIntent:false,admissionAck:false,dispatchIntentions:0,dispatchAck:false,dispatchMayHaveEffect:false,candidateCount:0,ownedRun:0,cancellationMayHaveEffect:false,native:"Unknown",secretsAbsent:0,runRetired:false,cleanup:"Unknown",elapsed:0}
 action init=state'=initialState
 action place=all{state.phase=="Prepared",state.refusal=="None",state'={...state,phase:"PlacementObserved",checkFresh:false}}
 action revalidate=all{state.refusal=="None",state.current,state'=state}
 action checkEffect=all{state.refusal=="None",state.current,state.elapsed<4,state'={...state,checkFresh:true}}
 action invalidate=all{state.refusal=="None",state'=if(state.authIntent or state.dispatchMayHaveEffect){...state,current:false,phase:"CleanupPending",cleanup:"Pending",native:"Unknown"}else{...state,current:false,phase:"Refused",refusal:"SourceInvalid",native:"Refused"}}
 action requestAuth=all{state.phase=="PlacementObserved",state.checkFresh,state.refusal=="None",state'={...state,phase:"SecretPlacementPending",checkFresh:false,authIntent:true}}
 action ackAuth=all{state.phase=="SecretPlacementPending",state.authIntent,not(state.authAck),state.refusal=="None",state'={...state,authAck:true}}
 action loseAuth=all{state.authIntent,state.refusal=="None",state'={...state,phase:"CleanupPending",cleanup:"Pending"}}
 action requestAdmission=all{state.phase=="SecretPlacementPending",state.authAck,state.checkFresh,state.refusal=="None",state'={...state,checkFresh:false,admissionIntent:true}}
 action requestAdmissionEarly=all{not(state.authAck),state.refusal=="None",state'={...state,phase:"CleanupPending",refusal:"EffectAction",native:"Refused",cleanup:if(state.authIntent)"Pending" else state.cleanup}}
 action ackAdmission=all{state.phase=="SecretPlacementPending",state.authAck,state.admissionIntent,state.refusal=="None",state'={...state,phase:"SecretsObserved",admissionAck:true}}
 action duplicateAckAuth=all{state.authAck,state.refusal=="None",state'={...state,phase:"CleanupPending",refusal:"EffectAck",native:"Refused",cleanup:"Pending"}}
 action lateAckAdmission=all{state.refusal!="None",state'=state}
 action lateDispatchRefused=all{state.refusal!="None",state'=state}
 action requestDispatch=all{state.phase=="SecretsObserved",state.authAck,state.admissionAck,state.checkFresh,state.dispatchIntentions==0,state.refusal=="None",state'={...state,phase:"DispatchPending",checkFresh:false,dispatchIntentions:1,dispatchMayHaveEffect:true}}
 action ackDispatch=all{state.phase=="DispatchPending",state.dispatchIntentions==1,state.refusal=="None",state'={...state,phase:"Discovery",dispatchAck:true}}
 action loseDispatch=all{state.dispatchIntentions==1,state.refusal=="None",state'={...state,phase:"CleanupPending",cleanup:"Pending",native:"Unknown"}}
 action retryDispatch=all{state.dispatchIntentions==1,state'=if(state.refusal=="None"){...state,phase:"CleanupPending",refusal:"DispatchRepeat",native:"Refused",cleanup:"Pending"}else state}
 action discoverNone=all{state.phase=="Discovery",state'= {...state,phase:"CleanupPending",candidateCount:0,cleanup:"Pending"}}
 action discoverOne=all{state.phase=="Discovery",state'= {...state,candidateCount:1,ownedRun:0}}
 action discoverTwo=all{state.phase=="Discovery",state'= {...state,phase:"CleanupPending",candidateCount:2,ownedRun:0,cleanup:"Pending"}}
 action ownRun=all{state.phase=="Discovery",state.candidateCount==1,state.refusal=="None",state'={...state,phase:"Watching",ownedRun:1}}
 action cancelWithoutOwnership=all{state.ownedRun==0,state.checkFresh,state.refusal=="None",state'={...state,phase:"CleanupPending",refusal:"EffectAction",native:"Refused",cleanup:"Pending"}}
 action requestCancel=all{state.phase=="Watching",state.ownedRun==1,state.checkFresh,state.refusal=="None",state'={...state,checkFresh:false,cancellationMayHaveEffect:true,native:"Unknown"}}
 action failOwnedNative=all{state.phase=="Watching",state.ownedRun==1,state.refusal=="None",state'={...state,phase:"CleanupPending",refusal:"MechanismFailure",checkFresh:false,native:"Unknown",cleanup:"Unknown"}}
 action checkCleanupEffect=all{state.phase=="CleanupPending",state.ownedRun==1,state.refusal=="MechanismFailure",state.current,state.elapsed<4,state'={...state,checkFresh:true}}
 action requestCleanupCancel=all{state.phase=="CleanupPending",state.ownedRun==1,state.refusal=="MechanismFailure",state.checkFresh,not(state.cancellationMayHaveEffect),not(state.runRetired),state'={...state,checkFresh:false,cancellationMayHaveEffect:true,native:"Unknown"}}
 action ackCleanupCancel=all{state.phase=="CleanupPending",state.ownedRun==1,state.refusal=="MechanismFailure",state.cancellationMayHaveEffect,state'={...state,native:"Unknown"}}
 action acceptNative=all{state.phase=="Watching",state.ownedRun==1,not(state.cancellationMayHaveEffect),state.elapsed<4,state.current,state.refusal=="None",state'={...state,phase:"CleanupPending",native:"Accepted",cleanup:"Pending"}}
 action observeUnknownNative=all{state.phase=="Watching",state.ownedRun==1,state'={...state,phase:"CleanupPending",native:"Unknown",cleanup:"Pending"}}
 action rejectedLateNative=all{state.ownedRun==1,(state.cancellationMayHaveEffect or state.elapsed>=4 or state.refusal!="None"),state'={...state,phase:"CleanupPending",native:"Unknown",cleanup:"Pending"}}
 action observeSecretAbsent=all{state.secretsAbsent<2,state'={...state,secretsAbsent:state.secretsAbsent+1,cleanup:if(state.secretsAbsent==1)"SecretsAbsent" else "Pending",phase:if(state.secretsAbsent==1 and state.runRetired)"Finalized" else "CleanupPending"}}
 action retireRun=all{state.ownedRun==1,state'={...state,runRetired:true,phase:if(state.secretsAbsent==2)"Finalized" else "CleanupPending"}}
 action readbackFailure=all{state.phase=="CleanupPending",state'={...state,phase:"Unknown",cleanup:"Unknown",native:"Unknown"}}
 action advanceTime=all{state.elapsed<4,state'=if(state.elapsed+1>=4){...state,elapsed:4,checkFresh:false,phase:"CleanupPending",native:"Unknown",cleanup:"Pending"}else{...state,elapsed:state.elapsed+1,checkFresh:false}}
 action step=any{place,revalidate,checkEffect,invalidate,requestAuth,ackAuth,loseAuth,requestAdmission,requestAdmissionEarly,ackAdmission,duplicateAckAuth,lateAckAdmission,lateDispatchRefused,requestDispatch,ackDispatch,loseDispatch,retryDispatch,discoverNone,discoverOne,discoverTwo,ownRun,cancelWithoutOwnership,requestCancel,failOwnedNative,checkCleanupEffect,requestCleanupCancel,ackCleanupCancel,acceptNative,observeUnknownNative,rejectedLateNative,observeSecretAbsent,retireRun,readbackFailure,advanceTime}
 action successStep=if(state.phase=="Prepared")place else if(state.phase=="PlacementObserved" and not(state.checkFresh))checkEffect else if(not(state.authIntent))requestAuth else if(not(state.authAck))ackAuth else if(not(state.admissionIntent) and not(state.checkFresh))checkEffect else if(not(state.admissionIntent))requestAdmission else if(not(state.admissionAck))ackAdmission else if(state.phase=="SecretsObserved" and not(state.checkFresh))checkEffect else if(state.phase=="SecretsObserved")requestDispatch else if(state.phase=="DispatchPending")ackDispatch else if(state.candidateCount==0)discoverOne else if(state.ownedRun==0)ownRun else if(state.native=="Unknown" and state.phase=="Watching")acceptNative else if(state.secretsAbsent<2)observeSecretAbsent else retireRun
 action ambiguityStep=if(state.phase=="Prepared")place else if(state.phase=="PlacementObserved" and not(state.checkFresh))checkEffect else if(not(state.authIntent))requestAuth else if(not(state.authAck))ackAuth else if(not(state.admissionIntent) and not(state.checkFresh))checkEffect else if(not(state.admissionIntent))requestAdmission else if(not(state.admissionAck))ackAdmission else if(state.phase=="SecretsObserved" and not(state.checkFresh))checkEffect else if(state.phase=="SecretsObserved")requestDispatch else if(state.phase=="DispatchPending")ackDispatch else discoverTwo
 action lostStep=if(state.phase=="Prepared")place else if(state.phase=="PlacementObserved" and not(state.checkFresh))checkEffect else if(not(state.authIntent))requestAuth else if(not(state.authAck))ackAuth else if(not(state.admissionIntent) and not(state.checkFresh))checkEffect else if(not(state.admissionIntent))requestAdmission else if(not(state.admissionAck))ackAdmission else if(state.phase=="SecretsObserved" and not(state.checkFresh))checkEffect else if(state.phase=="SecretsObserved")requestDispatch else loseDispatch
 action stickyStep=if(state.phase=="Prepared")place else if(state.phase=="PlacementObserved" and not(state.checkFresh))checkEffect else if(not(state.authIntent))requestAuth else if(not(state.authAck))ackAuth else duplicateAckAuth
 action deadlineStep=if(state.phase=="Finalized")advanceTime else successStep
 action refusedOwnedCancelStep=if(state.phase=="Prepared")place else if(state.phase=="PlacementObserved" and not(state.checkFresh))checkEffect else if(not(state.authIntent))requestAuth else if(not(state.authAck))ackAuth else if(not(state.admissionIntent) and not(state.checkFresh))checkEffect else if(not(state.admissionIntent))requestAdmission else if(not(state.admissionAck))ackAdmission else if(state.phase=="SecretsObserved" and not(state.checkFresh))checkEffect else if(state.phase=="SecretsObserved")requestDispatch else if(state.phase=="DispatchPending")ackDispatch else if(state.candidateCount==0)discoverOne else if(state.ownedRun==0)ownRun else if(state.refusal=="None")failOwnedNative else if(state.cancellationMayHaveEffect)ackCleanupCancel else if(not(state.checkFresh))checkCleanupEffect else requestCleanupCancel
 action falseOwnershipStep=if(state.phase=="Prepared")place else if(state.phase=="PlacementObserved" and not(state.checkFresh))checkEffect else if(not(state.authIntent))requestAuth else if(not(state.authAck))ackAuth else if(not(state.admissionIntent) and not(state.checkFresh))checkEffect else if(not(state.admissionIntent))requestAdmission else if(not(state.admissionAck))ackAdmission else if(state.phase=="SecretsObserved" and not(state.checkFresh))checkEffect else if(state.phase=="SecretsObserved")requestDispatch else if(state.phase=="DispatchPending")ackDispatch else if(state.candidateCount==0)discoverOne else if(not(state.checkFresh))checkEffect else cancelWithoutOwnership
 val atMostOneDispatch=state.dispatchIntentions<=1
 val stickyRefusal=state.refusal=="None" or state.native!="Accepted"
 val admissionRequiresAuth=not(state.admissionIntent) or state.authAck
 val ownedRequiresCandidate=state.ownedRun==0 or state.candidateCount==1
 val cancellationRequiresOwnership=not(state.cancellationMayHaveEffect) or state.ownedRun==1
 val acceptedGuard=state.native!="Accepted" or (state.ownedRun==1 and not(state.cancellationMayHaveEffect) and state.elapsed<4 and state.current and state.refusal=="None")
 val secretsDoNotRetireRun=state.cleanup!="SecretsAbsent" or state.secretsAbsent==2
 val finalNeedsBoth=state.phase!="Finalized" or (state.secretsAbsent==2 and state.runRetired)
 val timeBound=state.elapsed>=0 and state.elapsed<=4
 val allInvariants=all{atMostOneDispatch,stickyRefusal,admissionRequiresAuth,ownedRequiresCandidate,cancellationRequiresOwnership,acceptedGuard,secretsDoNotRetireRun,finalNeedsBoth,timeBound}
 val successWitness=state.native=="Accepted" and state.phase=="Finalized"
 val earlyAdmissionWitness=state.refusal=="EffectAction" and not(state.admissionIntent)
 val stickyLateAckWitness=state.refusal=="EffectAck" and state.dispatchIntentions==0
 val lostDispatchWitness=state.dispatchMayHaveEffect and not(state.dispatchAck) and state.cleanup=="Pending"
 val ambiguityWitness=state.candidateCount==2 and state.ownedRun==0
 val candidateNotOwnedWitness=state.candidateCount==1 and state.ownedRun==0
 val cancellationUnknownWitness=state.cancellationMayHaveEffect and state.native=="Unknown"
 val deadlineUnknownWitness=state.elapsed==4 and state.native=="Unknown"
 val secretOnlyCleanupWitness=state.cleanup=="SecretsAbsent" and not(state.runRetired) and state.phase=="CleanupPending"
 val refusedOwnedCancellationWitness=state.refusal=="MechanismFailure" and state.ownedRun==1 and state.cancellationMayHaveEffect and state.native=="Unknown"
 val falseOwnershipRefusalWitness=state.refusal=="EffectAction" and state.ownedRun==0 and not(state.cancellationMayHaveEffect)
}

module OwnedLaunch {
 type LaunchState={phase:str,identity:bool,releaseIntentions:int,releaseMayHaveEffect:bool,cancelled:bool,deadline:bool,failure:bool,directSettled:bool,descendantsSettled:bool,readersSettled:bool}
 var launch:LaunchState
 val launchInitial={phase:"Gated",identity:false,releaseIntentions:0,releaseMayHaveEffect:false,cancelled:false,deadline:false,failure:false,directSettled:false,descendantsSettled:false,readersSettled:false}
 action launchInit=launch'=launchInitial
 action recordIdentity=all{launch.phase=="Gated",not(launch.failure),launch'={...launch,phase:"IdentityRecorded",identity:true}}
 action requestRelease=all{launch.phase=="IdentityRecorded",launch.identity,not(launch.cancelled),not(launch.deadline),not(launch.failure),launch.releaseIntentions==0,launch'={...launch,phase:"ReleaseIntended",releaseIntentions:1,releaseMayHaveEffect:true}}
 action ackRelease=all{launch.phase=="ReleaseIntended",launch.releaseIntentions==1,launch'={...launch,phase:"Released"}}
 action loseRelease=all{launch.releaseIntentions==1,launch'={...launch,phase:"Retiring",failure:true,releaseMayHaveEffect:true}}
 action cancelLaunch=all{launch'={...launch,cancelled:true,failure:true,phase:if(launch.releaseMayHaveEffect)"Retiring" else "Refused"}}
 action expireLaunch=all{launch'={...launch,deadline:true,failure:true,phase:if(launch.releaseMayHaveEffect)"Retiring" else "Refused"}}
 action beginRetirement=all{launch.phase=="Released" or launch.phase=="Retiring",launch'={...launch,phase:"Retiring"}}
 action settleAll=all{launch.phase=="Retiring",launch'={...launch,directSettled:true,descendantsSettled:true,readersSettled:true}}
 action finishLaunch=all{launch.phase=="Retiring",launch.directSettled,launch.descendantsSettled,launch.readersSettled,not(launch.failure),launch'={...launch,phase:"Terminal"}}
 action launchStep=any{recordIdentity,requestRelease,ackRelease,loseRelease,cancelLaunch,expireLaunch,beginRetirement,settleAll,finishLaunch}
 action gatedReleaseStep=if(launch.phase=="Gated")recordIdentity else if(launch.phase=="IdentityRecorded")requestRelease else ackRelease
 action lostReleaseStep=if(launch.phase=="Gated")recordIdentity else if(launch.phase=="IdentityRecorded")requestRelease else loseRelease
 action cancelBeforeStep=cancelLaunch
 action deadlineBeforeStep=expireLaunch
 action settledStep=if(launch.phase=="Gated")recordIdentity else if(launch.phase=="IdentityRecorded")requestRelease else if(launch.phase=="ReleaseIntended")ackRelease else if(launch.phase=="Released")beginRetirement else if(not(launch.directSettled))settleAll else finishLaunch
 val releaseNeedsIdentity=launch.releaseIntentions==0 or launch.identity
 val atMostOneRelease=launch.releaseIntentions<=1
 val terminalNeedsSettlement=launch.phase!="Terminal" or (launch.directSettled and launch.descendantsSettled and launch.readersSettled and not(launch.failure))
 val terminalNeedsOwnership=launch.phase!="Terminal" or (launch.identity and launch.releaseIntentions==1)
 val refusalNeverTerminal=not(launch.failure) or launch.phase!="Terminal"
 val launchInvariants=all{releaseNeedsIdentity,atMostOneRelease,terminalNeedsSettlement,terminalNeedsOwnership,refusalNeverTerminal}
 val releasedWitness=launch.phase=="Released" and launch.identity and launch.releaseIntentions==1
 val lostWitness=launch.phase=="Retiring" and launch.failure and launch.releaseMayHaveEffect
 val cancelledBeforeWitness=launch.phase=="Refused" and launch.cancelled and launch.releaseIntentions==0
 val deadlineBeforeWitness=launch.phase=="Refused" and launch.deadline and launch.releaseIntentions==0
 val settledWitness=launch.phase=="Terminal" and launchInvariants
}
