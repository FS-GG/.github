#!/usr/bin/env python3
"""No-effect contract for an atomic, complete GS2-09.7 recovery batch.

The protected scheduler, durable store, native ports and identity pins are not
installed. This module only exercises a supplied port; it has no CLI entry.
"""

import hashlib
import importlib.util
import json
import copy
from pathlib import Path
from typing import Protocol


CENSUS_SOURCE = Path(__file__).with_name("gs2-09-7-host-pending-census.py")
SPEC = importlib.util.spec_from_file_location("gs2_09_7_census_for_scheduler", CENSUS_SOURCE)
census = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(census)
worker = census.worker


class Refused(Exception):
    pass


class ProtectedSchedulerPort(census.ProtectedCensusPort,
                             worker.store.AtomicRecoveryStorePort, Protocol):
    def describe_scheduler(self) -> dict: ...


def _id(value: object) -> str:
    raw = json.dumps(value, sort_keys=True, separators=(",", ":"),
                     ensure_ascii=True).encode("ascii")
    return hashlib.sha256(raw).hexdigest()


def schedule_pending(port: ProtectedSchedulerPort | None) -> dict:
    """Append all verified pending jobs as one CAS unit; never enqueue a subset.

    An unknown append result is pending and must not trigger a second append.
    The installed store must atomically compare the seal/high-water and bind
    the entire batch to every later claim, including batch withdrawal.
    """
    try:
        worker.check_scheduler(port)
        required = ("append_schedule_batch_once", "read_schedule_batch")
        if port is None or not all(callable(getattr(port, name, None)) for name in required):
            raise Refused("scheduler-unconfigured")
        result = census.census_pending(port)
        seal = census._seal(port.read_seal(result["sealId"]))
        if seal["highWater"] != result["highWater"] or \
                seal["pendingCount"] != result["subjectCount"]:
            raise Refused("schedule-seal-drift")
        jobs = []
        for item in result["subjects"]:
            subject = census._subject(
                port.read_subject(seal["sealId"], item["mintId"]), seal)
            if {key: subject[key] for key in ("sequence", "mintId", "bindingId")} != item:
                raise Refused("schedule-subject-drift")
            job = {
                "schema": worker.SCHEDULE_SCHEMA,
                "scheduleId": _id([seal["sealId"], result["jointGeneration"], item]),
                "sealId": seal["sealId"],
                "jointGeneration": result["jointGeneration"],
                "highWater": seal["highWater"],
                "sequence": subject["sequence"],
                "pendingSha256": seal["pendingSha256"],
                "mintSha256": seal["mintSha256"],
                **{key: subject[key] for key in (
                    "mintId", "bindingId", "tokenSha256", "contextSha256",
                    "sandboxRepositoryId", "appId", "actor", "installationId",
                    "vaultId", "finalizerResourceId", "recoveryResourceId",
                    "journalResourceId")},
                "schedulerResourceId": worker.PINNED_SCHEDULER_RESOURCE_ID,
                "queueResourceId": worker.PINNED_CENSUS_QUEUE_RESOURCE_ID,
                "censusComplete": True, "state": "committed",
            }
            jobs.append(job)
        if census._digest([port.read_subject(seal["sealId"], job["mintId"])
                           for job in jobs]) != seal["pendingSha256"]:
            raise Refused("schedule-census-drift")
        try:
            current = census.joint.verify_joint_seal(
                port, seal, census.dt.datetime.now(census.dt.timezone.utc))
        except census.joint.Refused as error:
            raise Refused(str(error)) from error
        if current["generation"] != result["jointGeneration"]:
            raise Refused("schedule-joint-generation-drift")
        batch = {
            "schema": worker.BATCH_SCHEMA,
            "batchId": _id([seal["sealId"], result["jointGeneration"],
                            seal["pendingSha256"],
                            seal["mintSha256"], worker.PINNED_SCHEDULER_RESOURCE_ID]),
            "sealId": seal["sealId"],
            "jointGeneration": result["jointGeneration"],
            "highWater": seal["highWater"],
            "pendingCount": seal["pendingCount"],
            "pendingSha256": seal["pendingSha256"],
            "mintSha256": seal["mintSha256"],
            "schedulerResourceId": worker.PINNED_SCHEDULER_RESOURCE_ID,
            "queueResourceId": worker.PINNED_CENSUS_QUEUE_RESOURCE_ID,
            "journalResourceId": worker.PINNED_CENSUS_JOURNAL_RESOURCE_ID,
            "recoveryResourceId": worker.PINNED_RECOVERY_RESOURCE_ID,
            "workerId": worker.PINNED_RECOVERY_WORKER_ID,
            "jobs": jobs, "state": "committed",
        }
        expected = copy.deepcopy(batch)
        if port.append_schedule_batch_once(batch, seal["sealId"],
                                           seal["highWater"]) != "committed":
            raise Refused("schedule-append-unknown")
        if port.read_schedule_batch(seal["sealId"]) != expected or \
                port.read_schedule_batch(seal["sealId"]) != expected:
            raise Refused("schedule-batch-readback")
        for job in jobs:
            if port.read_schedule(job["mintId"]) != job:
                raise Refused("schedule-job-readback")
        if port.read_schedule_batch(seal["sealId"]) != expected:
            raise Refused("schedule-batch-readback")
        return {"schema": "fsgg.github-substrate-v2.sandbox-host-schedule-verdict/2",
                "batchId": expected["batchId"], "sealId": seal["sealId"],
                "jointGeneration": result["jointGeneration"],
                "jobCount": len(jobs), "disposition": "pending"}
    except (Refused, worker.Refused, census.Refused):
        raise
    except Exception as error:
        raise Refused("schedule-unknown") from error
