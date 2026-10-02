module HostAttempt_test {
 import HostAttempt.* from "./HostAttempt"
 run success = init.then(place).then(checkEffect).then(requestAuth).then(ackAuth).then(checkEffect).then(requestAdmission).then(ackAdmission).then(checkEffect).then(requestDispatch).then(ackDispatch).then(discoverOne).then(ownRun).then(acceptNative).then(observeSecretAbsent).then(observeSecretAbsent).then(retireRun).expect(successWitness)
 run earlyAdmission = init.then(place).then(checkEffect).then(requestAdmissionEarly).expect(earlyAdmissionWitness)
 run stickyLateAck = init.then(place).then(checkEffect).then(requestAuth).then(ackAuth).then(duplicateAckAuth).then(lateAckAdmission).then(lateDispatchRefused).expect(stickyLateAckWitness)
 run lostDispatch = init.then(place).then(checkEffect).then(requestAuth).then(ackAuth).then(checkEffect).then(requestAdmission).then(ackAdmission).then(checkEffect).then(requestDispatch).then(loseDispatch).expect(lostDispatchWitness)
 run ambiguity = init.then(place).then(checkEffect).then(requestAuth).then(ackAuth).then(checkEffect).then(requestAdmission).then(ackAdmission).then(checkEffect).then(requestDispatch).then(ackDispatch).then(discoverTwo).expect(ambiguityWitness)
 run candidateNotOwned = init.then(place).then(checkEffect).then(requestAuth).then(ackAuth).then(checkEffect).then(requestAdmission).then(ackAdmission).then(checkEffect).then(requestDispatch).then(ackDispatch).then(discoverOne).expect(candidateNotOwnedWitness)
 run cancellationUnknown = init.then(place).then(checkEffect).then(requestAuth).then(ackAuth).then(checkEffect).then(requestAdmission).then(ackAdmission).then(checkEffect).then(requestDispatch).then(ackDispatch).then(discoverOne).then(ownRun).then(checkEffect).then(requestCancel).then(rejectedLateNative).expect(cancellationUnknownWitness)
 run deadlineUnknown = init.then(place).then(checkEffect).then(requestAuth).then(ackAuth).then(checkEffect).then(requestAdmission).then(ackAdmission).then(checkEffect).then(requestDispatch).then(ackDispatch).then(discoverOne).then(ownRun).then(advanceTime).then(advanceTime).then(advanceTime).then(advanceTime).then(rejectedLateNative).expect(deadlineUnknownWitness)
 run secretOnlyCleanup = init.then(place).then(checkEffect).then(requestAuth).then(ackAuth).then(checkEffect).then(requestAdmission).then(ackAdmission).then(checkEffect).then(requestDispatch).then(loseDispatch).then(observeSecretAbsent).then(observeSecretAbsent).expect(secretOnlyCleanupWitness)
 run successThenDeadline = init.then(place).then(checkEffect).then(requestAuth).then(ackAuth).then(checkEffect).then(requestAdmission).then(ackAdmission).then(checkEffect).then(requestDispatch).then(ackDispatch).then(discoverOne).then(ownRun).then(acceptNative).then(observeSecretAbsent).then(observeSecretAbsent).then(retireRun).then(advanceTime).then(advanceTime).then(advanceTime).then(advanceTime).expect(allInvariants and deadlineUnknownWitness)
 run refusedOwnedCancellation = init.then(place).then(checkEffect).then(requestAuth).then(ackAuth).then(checkEffect).then(requestAdmission).then(ackAdmission).then(checkEffect).then(requestDispatch).then(ackDispatch).then(discoverOne).then(ownRun).then(failOwnedNative).then(checkCleanupEffect).then(requestCleanupCancel).then(ackCleanupCancel).expect(allInvariants and refusedOwnedCancellationWitness)
 run falseOwnershipRefusal = init.then(place).then(checkEffect).then(requestAuth).then(ackAuth).then(checkEffect).then(requestAdmission).then(ackAdmission).then(checkEffect).then(requestDispatch).then(ackDispatch).then(discoverOne).then(checkEffect).then(cancelWithoutOwnership).expect(allInvariants and falseOwnershipRefusalWitness)
}

module OwnedLaunch_test {
 import OwnedLaunch.* from "./HostAttempt"
 run gatedRelease = launchInit.then(recordIdentity).then(requestRelease).then(ackRelease).expect(launchInvariants and releasedWitness)
 run lostRelease = launchInit.then(recordIdentity).then(requestRelease).then(loseRelease).expect(launchInvariants and lostWitness)
 run cancelBefore = launchInit.then(cancelLaunch).expect(launchInvariants and cancelledBeforeWitness)
 run deadlineBefore = launchInit.then(expireLaunch).expect(launchInvariants and deadlineBeforeWitness)
 run identityFailure = launchInit.then(failIdentity).expect(launchInvariants and identityFailureWitness)
 run cancelAfter = launchInit.then(recordIdentity).then(requestRelease).then(ackRelease).then(cancelLaunch).expect(launchInvariants and cancelledAfterWitness)
 run settled = launchInit.then(recordIdentity).then(requestRelease).then(ackRelease).then(beginRetirement).then(settleAll).then(finishLaunch).expect(launchInvariants and settledWitness)
}
