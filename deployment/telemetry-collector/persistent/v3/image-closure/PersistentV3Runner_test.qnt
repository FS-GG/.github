module PersistentV3Runner_test {
  import PersistentV3Runner.* from "./PersistentV3Runner"
  run success = init.then(acquire).then(validate).then(createStores).then(firstBuilt).then(secondMatches).then(qualify).then(cleanupOne).then(cleanupOne).then(finish).expect(allInvariants and successWitness)
  run lost = init.then(acquire).then(validate).then(createStores).then(firstLost).then(cleanupOne).then(cleanupOne).then(refuseAfterCleanup).expect(allInvariants and lostWitness)
  run mismatch = init.then(acquire).then(validate).then(createStores).then(firstBuilt).then(secondDiffers).then(cleanupOne).then(cleanupOne).then(refuseAfterCleanup).expect(allInvariants and mismatchWitness)
  run cancellation = init.then(acquire).then(validate).then(createStores).then(cancel).then(cleanupOne).then(cleanupOne).then(refuseAfterCleanup).expect(allInvariants and cancellationWitness)
  run cleanupFailureCase = init.then(acquire).then(validate).then(createStores).then(cancel).then(cleanupFailure).expect(allInvariants and cleanupFailureWitness)
  run stale = init.then(acquire).then(validate).then(createStores).then(firstBuilt).then(invalidate).then(staleResult).expect(allInvariants and staleWitness)
}
