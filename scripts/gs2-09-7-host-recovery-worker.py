#!/usr/bin/env python3
"""Source-only protected worker for one GS2-09.7 pending revocation.

The worker has no installed scheduler, journal, vault, revoker, or credential.
Every production identity pin remains empty and prevents provider effects.
"""

import importlib.util
import copy
import datetime as dt
import hashlib
import json
import secrets
from pathlib import Path
from typing import Protocol
from urllib.parse import urlsplit


FINALIZER_SOURCE = Path(__file__).with_name("gs2-09-7-host-refusal-finalizer.py")
SPEC = importlib.util.spec_from_file_location("gs2_09_7_finalizer_for_recovery", FINALIZER_SOURCE)
finalizer = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(finalizer)

STORE_SOURCE = Path(__file__).with_name("gs2-09-7-host-recovery-store.py")
STORE_SPEC = importlib.util.spec_from_file_location("gs2_09_7_recovery_store", STORE_SOURCE)
store = importlib.util.module_from_spec(STORE_SPEC)
STORE_SPEC.loader.exec_module(store)

JOINT_SOURCE = Path(__file__).with_name("gs2-09-7-host-joint-seal.py")
JOINT_SPEC = importlib.util.spec_from_file_location("gs2_09_7_joint_for_worker", JOINT_SOURCE)
joint = importlib.util.module_from_spec(JOINT_SPEC)
JOINT_SPEC.loader.exec_module(joint)

WORKER_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-recovery-worker/1"
BINDING_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-recovery-binding/1"
CLAIM_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-recovery-claim/3"
RECEIPT_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-recovery-receipt/1"
SCHEDULER_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-recovery-scheduler/2"
SCHEDULE_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-recovery-schedule/2"
BATCH_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-recovery-batch/2"
CENSUS_SEAL_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-pending-seal/1"
CENSUS_SUBJECT_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-pending-subject/1"

PINNED_RECOVERY_ORIGIN = ""
PINNED_RECOVERY_RESOURCE_ID = ""
PINNED_RECOVERY_ENDPOINT = ""
PINNED_RECOVERY_WORKER_ID = ""
PINNED_SCHEDULER_ORIGIN = ""
PINNED_SCHEDULER_RESOURCE_ID = ""
PINNED_SCHEDULER_ENDPOINT = ""
PINNED_CENSUS_QUEUE_RESOURCE_ID = ""
PINNED_CENSUS_JOURNAL_RESOURCE_ID = ""


class Refused(Exception):
    pass


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise Refused(reason)


class ProtectedRecoveryPort(finalizer.ProtectedFinalizerPort,
                            store.AtomicRecoveryStorePort,
                            joint.ProtectedJointSealPort, Protocol):
    def describe_recovery(self) -> dict: ...
    def describe_scheduler(self) -> dict: ...
    def read_census_seal(self, seal_id: str) -> dict: ...
    def read_census_subject(self, seal_id: str, mint_id: str) -> dict: ...
    def read_binding(self, mint_id: str) -> dict: ...
    def append_recovery_receipt(self, mint_id: str, binding_id: str,
                                token_sha256: str) -> str: ...
    def read_recovery_receipt(self, mint_id: str) -> dict: ...


def check_worker(port: ProtectedRecoveryPort | None) -> None:
    require(all((PINNED_RECOVERY_ORIGIN, PINNED_RECOVERY_RESOURCE_ID,
                 PINNED_RECOVERY_ENDPOINT, PINNED_RECOVERY_WORKER_ID)),
            "recovery-unconfigured")
    methods = ("describe_recovery", "read_binding", "claim_recovery_once",
               "read_recovery_claim", "append_recovery_receipt",
               "read_recovery_receipt")
    require(port is not None and all(callable(getattr(port, name, None)) for name in methods),
            "recovery-unconfigured")
    descriptor = port.describe_recovery()
    require(type(descriptor) is dict and set(descriptor) == {
        "schema", "origin", "resourceId", "endpoint", "workerId", "durable",
        "atomicCas", "nativeReadback", "credentialScope", "candidateCanWrite",
    }, "recovery-descriptor")
    endpoint = descriptor["endpoint"]
    require(type(endpoint) is str, "recovery-endpoint")
    try:
        parsed = urlsplit(endpoint)
        parsed.port
    except ValueError as error:
        raise Refused("recovery-endpoint") from error
    require(parsed.scheme == "https" and bool(parsed.hostname)
            and not parsed.username and not parsed.password
            and not parsed.query and not parsed.fragment
            and endpoint == PINNED_RECOVERY_ENDPOINT
            and f"{parsed.scheme}://{parsed.netloc}" == PINNED_RECOVERY_ORIGIN,
            "recovery-endpoint")
    require(descriptor == {
        "schema": WORKER_SCHEMA, "origin": PINNED_RECOVERY_ORIGIN,
        "resourceId": PINNED_RECOVERY_RESOURCE_ID,
        "endpoint": PINNED_RECOVERY_ENDPOINT,
        "workerId": PINNED_RECOVERY_WORKER_ID,
        "durable": True, "atomicCas": True, "nativeReadback": True,
        "credentialScope": "protected-host-only", "candidateCanWrite": False,
    } and descriptor["durable"] is True
      and descriptor["atomicCas"] is True
      and descriptor["nativeReadback"] is True
      and descriptor["candidateCanWrite"] is False, "recovery-authority")


def check_scheduler(port: ProtectedRecoveryPort | None) -> None:
    require(all((PINNED_SCHEDULER_ORIGIN, PINNED_SCHEDULER_RESOURCE_ID,
                 PINNED_SCHEDULER_ENDPOINT, PINNED_CENSUS_QUEUE_RESOURCE_ID,
                 PINNED_CENSUS_JOURNAL_RESOURCE_ID)), "scheduler-unconfigured")
    require(port is not None
            and callable(getattr(port, "describe_scheduler", None))
            and callable(getattr(port, "read_schedule", None))
            and callable(getattr(port, "read_schedule_batch", None))
            and callable(getattr(port, "withdraw_schedule_batch_once", None))
            and callable(getattr(port, "read_census_seal", None))
            and callable(getattr(port, "read_census_subject", None)),
            "scheduler-unconfigured")
    descriptor = port.describe_scheduler()
    require(type(descriptor) is dict and descriptor == {
        "schema": SCHEDULER_SCHEMA, "origin": PINNED_SCHEDULER_ORIGIN,
        "resourceId": PINNED_SCHEDULER_RESOURCE_ID,
        "endpoint": PINNED_SCHEDULER_ENDPOINT,
        "queueResourceId": PINNED_CENSUS_QUEUE_RESOURCE_ID,
        "journalResourceId": PINNED_CENSUS_JOURNAL_RESOURCE_ID,
        "recoveryResourceId": PINNED_RECOVERY_RESOURCE_ID,
        "durable": True, "atomicCas": True,
        "atomicWithdrawClaim": True, "nativeReadback": True,
        "credentialScope": "protected-host-only", "candidateCanWrite": False,
    } and descriptor["durable"] is True
      and descriptor["atomicCas"] is True
      and descriptor["atomicWithdrawClaim"] is True
      and descriptor["nativeReadback"] is True
      and descriptor["candidateCanWrite"] is False, "scheduler-authority")
    endpoint = descriptor["endpoint"]
    require(type(endpoint) is str, "scheduler-endpoint")
    try:
        parsed = urlsplit(endpoint)
        parsed.port
    except ValueError as error:
        raise Refused("scheduler-endpoint") from error
    require(parsed.scheme == "https" and bool(parsed.hostname)
            and not parsed.username and not parsed.password
            and not parsed.query and not parsed.fragment
            and endpoint == PINNED_SCHEDULER_ENDPOINT
            and f"{parsed.scheme}://{parsed.netloc}" == PINNED_SCHEDULER_ORIGIN,
            "scheduler-endpoint")


def _digest(items: list[dict]) -> str:
    raw = json.dumps(items, sort_keys=True, separators=(",", ":"),
                     ensure_ascii=True).encode("ascii")
    return hashlib.sha256(raw).hexdigest()


def _batch_readback(port: ProtectedRecoveryPort, seal: dict,
                    schedule: dict, generation: int) -> str:
    try:
        batch = copy.deepcopy(port.read_schedule_batch(seal["sealId"]))
        after = copy.deepcopy(port.read_schedule_batch(seal["sealId"]))
    except Exception as error:
        raise Refused("recovery-batch-unknown") from error
    require(type(batch) is dict and type(after) is dict and batch == after
            and set(batch) == {
                "schema", "batchId", "sealId", "jointGeneration",
                "highWater", "pendingCount",
                "pendingSha256", "mintSha256", "schedulerResourceId",
                "queueResourceId", "journalResourceId", "recoveryResourceId",
                "workerId", "jobs", "state",
            }, "recovery-batch")
    require(batch["schema"] == BATCH_SCHEMA
            and type(batch["batchId"]) is str
            and finalizer.release.host.HEX64.fullmatch(batch["batchId"])
            and batch["sealId"] == seal["sealId"]
            and type(batch["jointGeneration"]) is int
            and batch["jointGeneration"] == generation
            and type(batch["highWater"]) is int
            and batch["highWater"] == seal["highWater"]
            and type(batch["pendingCount"]) is int
            and batch["pendingCount"] == seal["pendingCount"]
            and batch["pendingSha256"] == seal["pendingSha256"]
            and batch["mintSha256"] == seal["mintSha256"]
            and batch["schedulerResourceId"] == PINNED_SCHEDULER_RESOURCE_ID
            and batch["queueResourceId"] == PINNED_CENSUS_QUEUE_RESOURCE_ID
            and batch["journalResourceId"] == PINNED_CENSUS_JOURNAL_RESOURCE_ID
            and batch["recoveryResourceId"] == PINNED_RECOVERY_RESOURCE_ID
            and batch["workerId"] == PINNED_RECOVERY_WORKER_ID
            and batch["state"] == "committed"
            and type(batch["jobs"]) is list
            and len(batch["jobs"]) == seal["pendingCount"], "recovery-batch")
    subjects = []
    seen_sequences = set()
    seen_schedules = set()
    seen_mints = set()
    seen_bindings = set()
    for job in batch["jobs"]:
        require(type(job) is dict and set(job) == set(schedule)
                and job["schema"] == SCHEDULE_SCHEMA
                and job["sealId"] == seal["sealId"]
                and type(job["jointGeneration"]) is int
                and job["jointGeneration"] == generation
                and job["highWater"] == seal["highWater"]
                and job["pendingSha256"] == seal["pendingSha256"]
                and job["mintSha256"] == seal["mintSha256"]
                and job["vaultId"] == finalizer.PINNED_TOKEN_VAULT_ID
                and job["finalizerResourceId"] == finalizer.PINNED_FINALIZER_RESOURCE_ID
                and job["recoveryResourceId"] == PINNED_RECOVERY_RESOURCE_ID
                and job["schedulerResourceId"] == PINNED_SCHEDULER_RESOURCE_ID
                and job["queueResourceId"] == PINNED_CENSUS_QUEUE_RESOURCE_ID
                and job["journalResourceId"] == PINNED_CENSUS_JOURNAL_RESOURCE_ID
                and job["censusComplete"] is True and job["state"] == "committed"
                and type(job["sequence"]) is int
                and 1 <= job["sequence"] <= seal["highWater"]
                and all(type(job[key]) is str
                        and finalizer.release.host.HEX64.fullmatch(job[key])
                        for key in ("scheduleId", "mintId", "bindingId",
                                    "tokenSha256", "contextSha256"))
                and type(job["sandboxRepositoryId"]) is int
                and job["sandboxRepositoryId"] == finalizer.release.host.SANDBOX_ID
                and type(job["appId"]) is int
                and job["appId"] == finalizer.release.host.APP_ID
                and job["actor"] == finalizer.release.host.ACTOR
                and type(job["installationId"]) is int
                and job["installationId"] > 0, "recovery-batch")
        require(job["sequence"] not in seen_sequences
                and job["scheduleId"] not in seen_schedules
                and job["mintId"] not in seen_mints
                and job["bindingId"] not in seen_bindings,
                "recovery-batch-duplicate")
        seen_sequences.add(job["sequence"])
        seen_schedules.add(job["scheduleId"])
        seen_mints.add(job["mintId"])
        seen_bindings.add(job["bindingId"])
        subjects.append({
            "schema": CENSUS_SUBJECT_SCHEMA,
            **{key: job[key] for key in (
                "sequence", "mintId", "bindingId", "tokenSha256",
                "contextSha256", "sandboxRepositoryId", "appId", "actor",
                "installationId", "journalResourceId", "finalizerResourceId",
                "vaultId", "recoveryResourceId")},
            "revokeRequired": True,
        })
    require(subjects == sorted(subjects, key=lambda item: item["sequence"])
            and _digest(subjects) == seal["pendingSha256"]
            and schedule in batch["jobs"], "recovery-batch-omission")
    return batch["batchId"]


def _schedule_readback(port: ProtectedRecoveryPort, mint_id: str,
                       binding_id: str, mint: dict, token_sha256: str) -> tuple[str, str, str, int]:
    try:
        schedule = port.read_schedule(mint_id)
    except Exception as error:
        raise Refused("recovery-schedule-unknown") from error
    require(type(schedule) is dict and set(schedule) == {
        "schema", "scheduleId", "sealId", "jointGeneration",
        "highWater", "sequence",
        "pendingSha256", "mintSha256", "mintId", "bindingId",
        "tokenSha256", "contextSha256", "sandboxRepositoryId", "appId",
        "actor", "installationId", "vaultId", "finalizerResourceId",
        "recoveryResourceId", "schedulerResourceId", "queueResourceId",
        "journalResourceId", "censusComplete", "state",
    }, "recovery-schedule")
    require(schedule["schema"] == SCHEDULE_SCHEMA
            and all(type(schedule[name]) is str
                    and finalizer.release.host.HEX64.fullmatch(schedule[name])
                    for name in ("scheduleId", "sealId", "pendingSha256",
                                 "mintSha256"))
            and type(schedule["highWater"]) is int
            and type(schedule["sequence"]) is int
            and 1 <= schedule["sequence"] <= schedule["highWater"] <= 10000
            and schedule["mintId"] == mint_id
            and schedule["bindingId"] == binding_id
            and schedule["tokenSha256"] == token_sha256
            and schedule["contextSha256"] == mint["contextSha256"]
            and type(schedule["sandboxRepositoryId"]) is int
            and schedule["sandboxRepositoryId"] == finalizer.release.host.SANDBOX_ID
            and type(schedule["appId"]) is int
            and schedule["appId"] == finalizer.release.host.APP_ID
            and schedule["actor"] == finalizer.release.host.ACTOR
            and type(schedule["installationId"]) is int
            and schedule["installationId"] == mint["installationId"]
            and schedule["vaultId"] == finalizer.PINNED_TOKEN_VAULT_ID
            and schedule["finalizerResourceId"] == finalizer.PINNED_FINALIZER_RESOURCE_ID
            and schedule["recoveryResourceId"] == PINNED_RECOVERY_RESOURCE_ID
            and schedule["schedulerResourceId"] == PINNED_SCHEDULER_RESOURCE_ID
            and schedule["queueResourceId"] == PINNED_CENSUS_QUEUE_RESOURCE_ID
            and schedule["journalResourceId"] == PINNED_CENSUS_JOURNAL_RESOURCE_ID
            and schedule["censusComplete"] is True
            and schedule["state"] == "committed", "recovery-schedule")
    try:
        seal = port.read_census_seal(schedule["sealId"])
        subject = port.read_census_subject(schedule["sealId"], mint_id)
        seal_after = port.read_census_seal(schedule["sealId"])
    except Exception as error:
        raise Refused("recovery-census-unknown") from error
    require(type(seal) is dict and seal == {
        "schema": CENSUS_SEAL_SCHEMA, "sealId": schedule["sealId"],
        "highWater": schedule["highWater"],
        "pendingCount": seal.get("pendingCount"),
        "pendingSha256": schedule["pendingSha256"],
        "mintCount": schedule["highWater"],
        "mintSha256": schedule["mintSha256"],
        "queueResourceId": PINNED_CENSUS_QUEUE_RESOURCE_ID,
        "journalResourceId": PINNED_CENSUS_JOURNAL_RESOURCE_ID,
        "finalizerResourceId": finalizer.PINNED_FINALIZER_RESOURCE_ID,
        "vaultId": finalizer.PINNED_TOKEN_VAULT_ID,
        "recoveryResourceId": PINNED_RECOVERY_RESOURCE_ID,
        "workerId": PINNED_RECOVERY_WORKER_ID,
        "complete": True, "snapshotIsolation": True,
    } and type(seal["highWater"]) is int
      and type(seal["mintCount"]) is int
      and type(seal["pendingCount"]) is int
      and 1 <= seal["pendingCount"] <= schedule["highWater"]
      and seal["complete"] is True and seal["snapshotIsolation"] is True
      and type(seal_after) is dict and seal_after == seal
      and type(seal_after["highWater"]) is int
      and type(seal_after["mintCount"]) is int
      and type(seal_after["pendingCount"]) is int,
            "recovery-census-seal")
    require(type(subject) is dict and subject == {
        "schema": CENSUS_SUBJECT_SCHEMA,
        "sequence": schedule["sequence"], "mintId": mint_id,
        "bindingId": binding_id, "tokenSha256": token_sha256,
        "contextSha256": mint["contextSha256"],
        "sandboxRepositoryId": finalizer.release.host.SANDBOX_ID,
        "appId": finalizer.release.host.APP_ID,
        "actor": finalizer.release.host.ACTOR,
        "installationId": mint["installationId"],
        "journalResourceId": PINNED_CENSUS_JOURNAL_RESOURCE_ID,
        "finalizerResourceId": finalizer.PINNED_FINALIZER_RESOURCE_ID,
        "vaultId": finalizer.PINNED_TOKEN_VAULT_ID,
        "recoveryResourceId": PINNED_RECOVERY_RESOURCE_ID,
        "revokeRequired": True,
    } and type(subject["sequence"]) is int
      and type(subject["installationId"]) is int
      and subject["revokeRequired"] is True, "recovery-census-subject")
    try:
        joint_record = joint.verify_joint_seal(
            port, seal, dt.datetime.now(dt.timezone.utc))
    except joint.Refused as error:
        raise Refused(str(error)) from error
    generation = joint_record["generation"]
    require(type(schedule["jointGeneration"]) is int
            and schedule["jointGeneration"] == generation,
            "joint-generation")
    batch_id = _batch_readback(port, seal, schedule, generation)
    return schedule["scheduleId"], batch_id, schedule["sealId"], generation


def _binding_record(port: ProtectedRecoveryPort, mint_id: str,
                    binding_id: str, mint: dict, token_sha256: str) -> None:
    record = port.read_binding(mint_id)
    require(type(record) is dict and record == {
        "schema": BINDING_SCHEMA,
        "mintId": mint_id,
        "bindingId": binding_id,
        "tokenSha256": token_sha256,
        "contextSha256": mint["contextSha256"],
        "sandboxRepositoryId": finalizer.release.host.SANDBOX_ID,
        "appId": finalizer.release.host.APP_ID,
        "actor": finalizer.release.host.ACTOR,
        "installationId": mint["installationId"],
        "vaultId": finalizer.PINNED_TOKEN_VAULT_ID,
        "finalizerResourceId": finalizer.PINNED_FINALIZER_RESOURCE_ID,
        "verifiedAtCommit": True,
    } and record["verifiedAtCommit"] is True, "recovery-binding")


def _claim_readback(port: ProtectedRecoveryPort, mint_id: str,
                    binding_id: str, token_sha256: str,
                    schedule_id: str, batch_id: str, seal_id: str,
                    generation: int) -> bool:
    try:
        claim = port.read_recovery_claim(mint_id)
    except Exception:
        return False
    return type(claim) is dict and claim == {
        "schema": CLAIM_SCHEMA, "mintId": mint_id,
        "bindingId": binding_id, "tokenSha256": token_sha256,
        "workerId": PINNED_RECOVERY_WORKER_ID,
        "scheduleId": schedule_id, "batchId": batch_id,
        "sealId": seal_id,
        "jointGeneration": generation,
        "schedulerResourceId": PINNED_SCHEDULER_RESOURCE_ID,
        "recoveryResourceId": PINNED_RECOVERY_RESOURCE_ID,
        "state": "committed",
    }


def _receipt_readback(port: ProtectedRecoveryPort, mint_id: str,
                      binding_id: str, token_sha256: str) -> bool:
    try:
        receipt = port.read_recovery_receipt(mint_id)
    except Exception:
        return False
    return type(receipt) is dict and receipt == {
        "schema": RECEIPT_SCHEMA, "mintId": mint_id,
        "bindingId": binding_id, "tokenSha256": token_sha256,
        "workerId": PINNED_RECOVERY_WORKER_ID,
        "providerState": "revoked",
    }


def _observe(port: ProtectedRecoveryPort, token: str) -> str:
    try:
        state = port.observe(token)
    except Exception:
        return "unknown"
    return state if state in ("active", "revoked") else "unknown"


def recover_one(port: ProtectedRecoveryPort | None, mint_id: str,
                expected_binding_id: str) -> dict:
    """Attempt one native revoke after a fresh protected recovery claim.

    Duplicate or lost claim results permit observation and durable receipt
    only. They never permit another provider mutation or candidate invocation.
    """
    require(type(mint_id) is str and finalizer.release.host.HEX64.fullmatch(mint_id)
            and type(expected_binding_id) is str
            and finalizer.release.host.HEX64.fullmatch(expected_binding_id),
            "recovery-subject")
    check_worker(port)
    check_scheduler(port)
    finalizer.check_port(port)
    finalizer.check_vault(port)
    finalizer.check_revoker(port)
    token = port.recover_token(mint_id)
    token_sha256 = finalizer.digest_token(token)
    mint = finalizer.mint_record(port, mint_id, token_sha256)
    _binding_record(port, mint_id, expected_binding_id, mint, token_sha256)
    require(finalizer._read_state(port, "read_pending", finalizer.PENDING_SCHEMA,
                                  mint_id, token_sha256), "pending-intent")
    schedule_id, batch_id, seal_id, generation = _schedule_readback(
        port, mint_id, expected_binding_id, mint, token_sha256)

    try:
        claim = port.claim_recovery_once(mint_id, expected_binding_id,
                                         schedule_id, batch_id)
    except Exception:
        claim = "unknown"
    claim_state = "fresh" if claim == "committed" else \
                  "duplicate" if claim == "duplicate" else "unknown"
    revocation = "pending"
    claim_readback = _claim_readback(port, mint_id, expected_binding_id,
                                     token_sha256, schedule_id, batch_id,
                                     seal_id, generation)
    if not claim_readback:
        claim_state = "unknown"
    if claim_readback:
        attempt = finalizer.native_attempt_record(
            mint_id, token_sha256, mint["contextSha256"],
            secrets.token_hex(32))
        prior = finalizer._read_native_attempt(port, mint_id, attempt)
        if prior != "unknown":
            state = _observe(port, token)
            if claim_state == "fresh" and state == "active" and prior == "absent":
                try:
                    claimed = port.claim_native_attempt_once(
                        mint_id, token_sha256, attempt["attemptId"])
                except Exception:
                    claimed = "unknown"
                if claimed == "committed" and finalizer._read_native_attempt(
                        port, mint_id, attempt,
                        exact_attempt_id=True) == "exact":
                    try:
                        port.revoke(token)
                    except Exception:
                        pass
                    state = _observe(port, token)
            if state == "revoked":
                try:
                    port.append_recovery_receipt(mint_id, expected_binding_id,
                                                 token_sha256)
                except Exception:
                    pass
                if _receipt_readback(port, mint_id, expected_binding_id,
                                     token_sha256):
                    revocation = "revoked"
    return {"schema": "fsgg.github-substrate-v2.sandbox-host-recovery-verdict/1",
            "mintId": mint_id, "bindingId": expected_binding_id,
            "claim": claim_state, "revocation": revocation,
            "disposition": "pending"}
