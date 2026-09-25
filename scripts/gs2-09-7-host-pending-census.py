#!/usr/bin/env python3
"""Source-only complete protected pending-token census for GS2-09.7.

No protected queue, journal, credential, or scheduler is installed. Empty pins
make every production scan refuse before it can return recovery subjects.
"""

import hashlib
import importlib.util
import json
from pathlib import Path
from typing import Protocol
from urllib.parse import urlsplit


WORKER_SOURCE = Path(__file__).with_name("gs2-09-7-host-recovery-worker.py")
SPEC = importlib.util.spec_from_file_location("gs2_09_7_worker_for_census", WORKER_SOURCE)
worker = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(worker)

QUEUE_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-pending-queue/1"
JOURNAL_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-census-journal/1"
SEAL_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-pending-seal/1"
WATERMARK_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-high-water/1"
PAGE_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-pending-page/1"
SUBJECT_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-pending-subject/1"

PINNED_QUEUE_ORIGIN = ""
PINNED_QUEUE_RESOURCE_ID = ""
PINNED_QUEUE_ENDPOINT = ""
PINNED_JOURNAL_ORIGIN = ""
PINNED_JOURNAL_RESOURCE_ID = ""
PINNED_JOURNAL_ENDPOINT = ""

MAX_PAGES = 128
MAX_SUBJECTS = 10000


class Refused(Exception):
    pass


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise Refused(reason)


class ProtectedCensusPort(Protocol):
    def describe_queue(self) -> dict: ...
    def describe_journal(self) -> dict: ...
    def seal_snapshot(self) -> dict: ...
    def read_seal(self, seal_id: str) -> dict: ...
    def read_high_water(self) -> dict: ...
    def read_page(self, seal_id: str, cursor: str) -> dict: ...
    def read_subject(self, seal_id: str, mint_id: str) -> dict: ...
    def read_binding(self, mint_id: str) -> dict: ...


def _endpoint(value: str, pinned: str, origin: str) -> bool:
    if type(value) is not str:
        return False
    try:
        parsed = urlsplit(value)
        parsed.port
    except ValueError:
        return False
    return (parsed.scheme == "https" and bool(parsed.hostname)
            and not parsed.username and not parsed.password
            and not parsed.query and not parsed.fragment
            and value == pinned
            and f"{parsed.scheme}://{parsed.netloc}" == origin)


def check_port(port: ProtectedCensusPort | None) -> None:
    require(all((PINNED_QUEUE_ORIGIN, PINNED_QUEUE_RESOURCE_ID,
                 PINNED_QUEUE_ENDPOINT, PINNED_JOURNAL_ORIGIN,
                 PINNED_JOURNAL_RESOURCE_ID, PINNED_JOURNAL_ENDPOINT,
                 worker.PINNED_RECOVERY_RESOURCE_ID,
                 worker.PINNED_RECOVERY_WORKER_ID,
                 worker.finalizer.PINNED_FINALIZER_RESOURCE_ID,
                 worker.finalizer.PINNED_TOKEN_VAULT_ID)),
            "census-unconfigured")
    methods = ("describe_queue", "describe_journal", "seal_snapshot",
               "read_seal", "read_high_water", "read_page", "read_subject",
               "read_binding")
    require(port is not None and all(callable(getattr(port, name, None)) for name in methods),
            "census-unconfigured")
    queue = port.describe_queue()
    journal = port.describe_journal()
    require(type(queue) is dict and queue == {
        "schema": QUEUE_SCHEMA,
        "origin": PINNED_QUEUE_ORIGIN,
        "resourceId": PINNED_QUEUE_RESOURCE_ID,
        "endpoint": PINNED_QUEUE_ENDPOINT,
        "journalResourceId": PINNED_JOURNAL_RESOURCE_ID,
        "vaultId": worker.finalizer.PINNED_TOKEN_VAULT_ID,
        "recoveryResourceId": worker.PINNED_RECOVERY_RESOURCE_ID,
        "credentialScope": "protected-host-only",
        "candidateCanWrite": False,
        "snapshotIsolation": True,
    } and queue["candidateCanWrite"] is False
      and queue["snapshotIsolation"] is True
      and _endpoint(queue["endpoint"], PINNED_QUEUE_ENDPOINT,
                    PINNED_QUEUE_ORIGIN), "queue-authority")
    require(type(journal) is dict and journal == {
        "schema": JOURNAL_SCHEMA,
        "origin": PINNED_JOURNAL_ORIGIN,
        "resourceId": PINNED_JOURNAL_RESOURCE_ID,
        "endpoint": PINNED_JOURNAL_ENDPOINT,
        "finalizerResourceId": worker.finalizer.PINNED_FINALIZER_RESOURCE_ID,
        "vaultId": worker.finalizer.PINNED_TOKEN_VAULT_ID,
        "recoveryResourceId": worker.PINNED_RECOVERY_RESOURCE_ID,
        "durable": True,
        "atomicSeal": True,
        "nativeReadback": True,
        "credentialScope": "protected-host-only",
        "candidateCanWrite": False,
    } and journal["durable"] is True
      and journal["atomicSeal"] is True
      and journal["nativeReadback"] is True
      and journal["candidateCanWrite"] is False
      and _endpoint(journal["endpoint"], PINNED_JOURNAL_ENDPOINT,
                    PINNED_JOURNAL_ORIGIN), "journal-authority")


def _seal(value: dict) -> dict:
    require(type(value) is dict and set(value) == {
        "schema", "sealId", "highWater", "pendingCount", "pendingSha256",
        "queueResourceId", "journalResourceId", "finalizerResourceId",
        "vaultId", "recoveryResourceId", "workerId", "complete",
        "snapshotIsolation",
    }, "seal")
    require(value["schema"] == SEAL_SCHEMA
            and type(value["sealId"]) is str
            and worker.finalizer.release.host.HEX64.fullmatch(value["sealId"])
            and type(value["highWater"]) is int and value["highWater"] >= 0
            and type(value["pendingCount"]) is int
            and 0 <= value["pendingCount"] <= min(value["highWater"], MAX_SUBJECTS)
            and type(value["pendingSha256"]) is str
            and worker.finalizer.release.host.HEX64.fullmatch(value["pendingSha256"])
            and value["queueResourceId"] == PINNED_QUEUE_RESOURCE_ID
            and value["journalResourceId"] == PINNED_JOURNAL_RESOURCE_ID
            and value["finalizerResourceId"] ==
                worker.finalizer.PINNED_FINALIZER_RESOURCE_ID
            and value["vaultId"] == worker.finalizer.PINNED_TOKEN_VAULT_ID
            and value["recoveryResourceId"] == worker.PINNED_RECOVERY_RESOURCE_ID
            and value["workerId"] == worker.PINNED_RECOVERY_WORKER_ID
            and value["complete"] is True
            and value["snapshotIsolation"] is True, "seal-identity")
    return value


def _watermark(port: ProtectedCensusPort, high_water: int) -> None:
    value = port.read_high_water()
    require(type(value) is dict and value == {
        "schema": WATERMARK_SCHEMA,
        "journalResourceId": PINNED_JOURNAL_RESOURCE_ID,
        "highWater": high_water,
    } and type(value["highWater"]) is int, "high-water-drift")


def _subject(value: dict, seal: dict) -> dict:
    require(type(value) is dict and set(value) == {
        "schema", "sequence", "mintId", "bindingId", "tokenSha256",
        "contextSha256", "sandboxRepositoryId", "appId", "actor",
        "installationId", "journalResourceId", "finalizerResourceId",
        "vaultId", "recoveryResourceId", "revokeRequired",
    }, "subject")
    require(value["schema"] == SUBJECT_SCHEMA
            and type(value["sequence"]) is int
            and 1 <= value["sequence"] <= seal["highWater"]
            and all(type(value[name]) is str
                    and worker.finalizer.release.host.HEX64.fullmatch(value[name])
                    for name in ("mintId", "bindingId", "tokenSha256",
                                 "contextSha256"))
            and type(value["sandboxRepositoryId"]) is int
            and value["sandboxRepositoryId"] ==
                worker.finalizer.release.host.SANDBOX_ID
            and type(value["appId"]) is int
            and value["appId"] == worker.finalizer.release.host.APP_ID
            and value["actor"] == worker.finalizer.release.host.ACTOR
            and type(value["installationId"]) is int
            and value["installationId"] > 0
            and value["journalResourceId"] == PINNED_JOURNAL_RESOURCE_ID
            and value["finalizerResourceId"] ==
                worker.finalizer.PINNED_FINALIZER_RESOURCE_ID
            and value["vaultId"] == worker.finalizer.PINNED_TOKEN_VAULT_ID
            and value["recoveryResourceId"] == worker.PINNED_RECOVERY_RESOURCE_ID
            and value["revokeRequired"] is True, "subject-identity")
    return value


def _digest(items: list[dict]) -> str:
    raw = json.dumps(items, sort_keys=True, separators=(",", ":"),
                     ensure_ascii=True).encode("ascii")
    return hashlib.sha256(raw).hexdigest()


def _binding_readback(port: ProtectedCensusPort, subject: dict) -> None:
    record = port.read_binding(subject["mintId"])
    require(type(record) is dict and record == {
        "schema": worker.BINDING_SCHEMA,
        "mintId": subject["mintId"],
        "bindingId": subject["bindingId"],
        "tokenSha256": subject["tokenSha256"],
        "contextSha256": subject["contextSha256"],
        "sandboxRepositoryId": subject["sandboxRepositoryId"],
        "appId": subject["appId"],
        "actor": subject["actor"],
        "installationId": subject["installationId"],
        "vaultId": worker.finalizer.PINNED_TOKEN_VAULT_ID,
        "finalizerResourceId": worker.finalizer.PINNED_FINALIZER_RESOURCE_ID,
        "verifiedAtCommit": True,
    } and record["verifiedAtCommit"] is True, "binding-readback")


def census_pending(port: ProtectedCensusPort | None) -> dict:
    """Return subjects only after the entire sealed snapshot is verified."""
    check_port(port)
    try:
        seal = _seal(port.seal_snapshot())
        require(_seal(port.read_seal(seal["sealId"])) == seal, "seal-readback")
        _watermark(port, seal["highWater"])
        cursor = "0"
        items = []
        seen_mints = set()
        seen_bindings = set()
        last_sequence = 0
        for _ in range(MAX_PAGES):
            page = port.read_page(seal["sealId"], cursor)
            require(type(page) is dict and set(page) == {
                "schema", "sealId", "highWater", "cursor", "items", "nextCursor",
            } and page["schema"] == PAGE_SCHEMA
                    and page["sealId"] == seal["sealId"]
                    and type(page["highWater"]) is int
                    and page["highWater"] == seal["highWater"]
                    and page["cursor"] == cursor
                    and type(page["items"]) is list, "page")
            batch = page["items"]
            require(bool(batch) or (not items and seal["pendingCount"] == 0
                                    and page["nextCursor"] is None), "partial-page")
            for raw in batch:
                subject = _subject(raw, seal)
                require(subject["sequence"] > last_sequence
                        and subject["mintId"] not in seen_mints
                        and subject["bindingId"] not in seen_bindings,
                        "duplicate-or-order")
                require(_subject(port.read_subject(seal["sealId"],
                                                   subject["mintId"]), seal)
                        == subject, "subject-readback")
                _binding_readback(port, subject)
                last_sequence = subject["sequence"]
                seen_mints.add(subject["mintId"])
                seen_bindings.add(subject["bindingId"])
                items.append(subject)
                require(len(items) <= seal["pendingCount"], "count-overrun")
            next_cursor = page["nextCursor"]
            if next_cursor is None:
                break
            require(type(next_cursor) is str and bool(batch)
                    and next_cursor == str(last_sequence)
                    and next_cursor != cursor, "cursor")
            cursor = next_cursor
        else:
            raise Refused("page-limit")
        require(len(items) == seal["pendingCount"]
                and _digest(items) == seal["pendingSha256"], "census-omission")
        require(_seal(port.read_seal(seal["sealId"])) == seal, "seal-drift")
        _watermark(port, seal["highWater"])
    except Refused:
        raise
    except Exception as error:
        raise Refused("census-unknown") from error
    return {"schema": "fsgg.github-substrate-v2.sandbox-host-pending-census/1",
            "sealId": seal["sealId"], "highWater": seal["highWater"],
            "subjectCount": len(items),
            "subjects": [{"sequence": item["sequence"],
                          "mintId": item["mintId"],
                          "bindingId": item["bindingId"]} for item in items],
            "disposition": "pending"}
