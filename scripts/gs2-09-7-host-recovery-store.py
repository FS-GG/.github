#!/usr/bin/env python3
"""Typed no-effect boundary for the protected GS2-09.7 recovery store.

There is no installed adapter or endpoint. An installer must implement these
operations in one candidate-inaccessible durable authority domain.
"""

from typing import Literal, Protocol, TypedDict


StoreOutcome = Literal["committed", "duplicate", "refused"]
WithdrawalReason = Literal["seal-revoked", "operator-hold", "candidate-invalid"]


class RecoveryJob(TypedDict):
    schema: str
    scheduleId: str
    sealId: str
    jointGeneration: int
    highWater: int
    sequence: int
    pendingSha256: str
    mintSha256: str
    mintId: str
    bindingId: str
    tokenSha256: str
    contextSha256: str
    sandboxRepositoryId: int
    appId: int
    actor: str
    installationId: int
    vaultId: str
    finalizerResourceId: str
    recoveryResourceId: str
    schedulerResourceId: str
    queueResourceId: str
    journalResourceId: str
    censusComplete: bool
    state: str


class RecoveryBatch(TypedDict):
    schema: str
    batchId: str
    sealId: str
    jointGeneration: int
    highWater: int
    pendingCount: int
    pendingSha256: str
    mintSha256: str
    schedulerResourceId: str
    queueResourceId: str
    journalResourceId: str
    recoveryResourceId: str
    workerId: str
    jobs: list[RecoveryJob]
    state: str


class RecoveryClaimRecord(TypedDict):
    schema: str
    mintId: str
    bindingId: str
    tokenSha256: str
    workerId: str
    scheduleId: str
    batchId: str
    sealId: str
    jointGeneration: int
    schedulerResourceId: str
    recoveryResourceId: str
    state: str


class AtomicRecoveryStorePort(Protocol):
    """One linearizable store; reads alone never authorize a fresh claim.

    `append_schedule_batch_once` compares the authentic sealed mint/pending
    high-water and signed head generation, then commits all jobs together or
    none. `withdraw` and `claim`
    share the same linearization point. A prior committed claim survives a
    later withdrawal for native observation and finalization. An exception or
    unknown response cannot be retried as a fresh mutation.
    """

    def append_schedule_batch_once(self, batch: RecoveryBatch, seal_id: str,
                                   high_water: int) -> StoreOutcome: ...
    def read_schedule_batch(self, seal_id: str) -> RecoveryBatch | None: ...
    def read_schedule(self, mint_id: str) -> RecoveryJob | None: ...
    def withdraw_schedule_batch_once(self, batch_id: str, seal_id: str,
                                     reason: WithdrawalReason) -> StoreOutcome: ...
    def claim_recovery_once(self, mint_id: str, binding_id: str,
                            schedule_id: str, batch_id: str) -> StoreOutcome: ...
    def read_recovery_claim(self, mint_id: str) -> RecoveryClaimRecord | None: ...
