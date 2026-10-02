module PersistentV3Runner {
  type State = { phase: str, current: bool, stores: int, first: str, second: str, qualification: str, cancelled: bool, cleanupFailed: bool, refusal: str }
  var state: State
  val initialState = { phase: "Prepared", current: true, stores: 0, first: "None", second: "None", qualification: "Unknown", cancelled: false, cleanupFailed: false, refusal: "None" }
  action init = state' = initialState
  action acquire = all { state.phase == "Prepared", state' = {...state, phase: "Acquired"} }
  action validate = all { state.phase == "Acquired", state.current, state' = {...state, phase: "Validated"} }
  action createStores = all { state.phase == "Validated", state.current, state' = {...state, phase: "BuildingFirst", stores: 2} }
  action firstBuilt = all { state.phase == "BuildingFirst", state' = {...state, phase: "BuildingSecond", first: "aaa"} }
  action firstLost = all { state.phase == "BuildingFirst", state' = {...state, phase: "Cleanup", first: "Unknown", qualification: "Unknown"} }
  action secondMatches = all { state.phase == "BuildingSecond", state.current, state' = {...state, phase: "Comparing", second: "aaa"} }
  action secondDiffers = all { state.phase == "BuildingSecond", state' = {...state, phase: "Cleanup", second: "bbb", refusal: "BuildMismatch"} }
  action invalidate = all { state.phase != "Complete", state' = {...state, current: false, phase: if (state.stores > 0) "Cleanup" else "Refused", qualification: "Unknown", refusal: "InputChanged"} }
  action qualify = all { state.phase == "Comparing", state.current, state.first == state.second, state' = {...state, phase: "Cleanup", qualification: "Accepted"} }
  action staleResult = all { state.phase == "Comparing", not(state.current), state' = {...state, phase: "Cleanup", qualification: "Unknown", refusal: "StaleResult"} }
  action cancel = all { state.stores > 0, state.phase != "Complete", state' = {...state, phase: "Cleanup", cancelled: true, qualification: "Unknown"} }
  action cleanupOne = all { state.phase == "Cleanup", state.stores > 0, state' = {...state, stores: state.stores - 1} }
  action cleanupFailure = all { state.phase == "Cleanup", state.stores > 0, state' = {...state, cleanupFailed: true, qualification: "Unknown"} }
  action finish = all { state.phase == "Cleanup", state.stores == 0, not(state.cleanupFailed), not(state.cancelled), state.qualification == "Accepted", state.current, state' = {...state, phase: "Complete"} }
  action refuseAfterCleanup = all { state.phase == "Cleanup", state.stores == 0, (state.cleanupFailed or state.cancelled or state.qualification != "Accepted" or not(state.current)), state' = {...state, phase: "Refused"} }
  action step = any { acquire, validate, createStores, firstBuilt, firstLost, secondMatches, secondDiffers, invalidate, qualify, staleResult, cancel, cleanupOne, cleanupFailure, finish, refuseAfterCleanup }
  action successStep = if (state.phase == "Prepared") acquire else if (state.phase == "Acquired") validate else if (state.phase == "Validated") createStores else if (state.phase == "BuildingFirst") firstBuilt else if (state.phase == "BuildingSecond") secondMatches else if (state.phase == "Comparing") qualify else if (state.phase == "Cleanup" and state.stores > 0) cleanupOne else finish
  val successIsClosed = state.phase != "Complete" or (state.current and state.first == state.second and state.qualification == "Accepted" and state.stores == 0 and not(state.cancelled) and not(state.cleanupFailed))
  val unknownNeverSucceeds = state.phase != "Complete" or state.qualification == "Accepted"
  val ownedStoresBounded = state.stores >= 0 and state.stores <= 2
  val allInvariants = all { successIsClosed, unknownNeverSucceeds, ownedStoresBounded }
  val successWitness = state.phase == "Complete"
  val lostWitness = state.phase == "Refused" and state.first == "Unknown"
  val mismatchWitness = state.phase == "Refused" and state.refusal == "BuildMismatch"
  val cancellationWitness = state.phase == "Refused" and state.cancelled and state.qualification == "Unknown"
  val cleanupFailureWitness = state.phase == "Cleanup" and state.cleanupFailed and state.qualification == "Unknown"
  val staleWitness = state.refusal == "StaleResult" and state.qualification == "Unknown"
}
