module PersistentV3Runner_test {
 import PersistentV3Runner.* from "./PersistentV3Runner"
 action acquireStores=init.then(requestAcquire).then(observeAcquire).then(requestValidate).then(observeValid).then(requestStoreA).then(observeStoreA).then(requestStoreB).then(observeStoreB)
 action startedA=acquireStores.then(requestStartA).then(observeStartedA)
 action compared=startedA.then(requestAwaitA).then(observeBuildA).then(requestStartB).then(observeStartedB).then(requestAwaitB).then(observeBuildB).then(requestCompare).then(observeMatch)
 run success=compared.then(requestValidate).then(observeValid).then(requestQualify).then(observeQualified).then(requestRemove).then(observeRemoved).then(requestRemove).then(observeRemoved).expect(allInvariants and successWitness)
 run lost=acquireStores.then(requestStartA).then(loseBuildA).then(requestRemove).then(observeRemoved).then(requestRemove).then(observeRemoved).expect(allInvariants and lostWitness)
 run mismatch=startedA.then(requestAwaitA).then(observeBuildA).then(requestStartB).then(observeStartedB).then(requestAwaitB).then(observeBuildMismatch).then(requestCompare).then(observeMismatch).then(requestRemove).then(observeRemoved).then(requestRemove).then(observeRemoved).expect(allInvariants and mismatchWitness)
 run cancellation=startedA.then(cancel).then(requestCancel).then(observeCancelled).then(requestRemove).then(observeRemoved).then(requestRemove).then(observeRemoved).expect(allInvariants and cancellationWitness)
 run cleanupFailureCase=startedA.then(cancel).then(requestCancel).then(observeCancelled).then(requestRemove).then(cleanupFailure).expect(allInvariants and cleanupFailureWitness)
 run stale=compared.then(requestValidate).then(observeStale).then(requestRemove).then(observeRemoved).then(requestRemove).then(observeRemoved).expect(allInvariants and staleWitness)
}
