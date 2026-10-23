#!/usr/bin/env python3
"""Publish one exact, independently verified candidate through protected effects."""

from __future__ import annotations

import argparse
import base64
import release_successor_journal as continuation_policy
import datetime
import fcntl
import hashlib
import http.client
import importlib.util
import io
import json
import os
import pathlib
import re
import stat
import subprocess
import sys
import time
import zipfile

from release_successor_admission import SingleOperatorAdmission, publisher_context_authorized
from release_successor_execution import Refused, advance, ordered_effects, full_transition
from release_successor_journal import (ProtectedReleaseJournal, REF, REPOSITORY as JOURNAL_REPOSITORY, canonical,
    ReleaseSuspension, ALLOWED_SUCCESSOR, CURRENT_RELEASE, SUSPENDED_REF, SUSPENSION_PATH, strict_suspension,
    SUSPENSION_SCHEMA, SUSPENDED_ORIGINAL, SUSPENSION_DECISION)
from release_successor_provider import (GitHubAPI, MetadataRequestBudget, INSTALL_METADATA_REQUEST_CEILING, LiveProvider, NotFound, sanitized_dispatch_diagnostic,
                                       VerifiedOriginalTagTransport, approved_original_tag_transport,
                                       AUTH_PROTOCOL, AUTH_PROFILE, decode_auth_frame, validate_auth_binding, validate_auth_payload)

REPOSITORY = "FS-GG/.github"
SUCCESSOR_VERSION = CURRENT_RELEASE["version"]
SUCCESSOR_PREDECESSOR = CURRENT_RELEASE["predecessor"]
SUCCESSOR_REF = CURRENT_RELEASE["logicalRef"]
ORIGINAL_SUCCESSOR_VERSION = "0.102.0"
ORIGINAL_SUCCESSOR_PREDECESSOR = "0.101.0"
ORIGINAL_SUCCESSOR_REF = "refs/heads/fsgg/v2/journal/release/utel-rel-20"


CONTINUATION_PROFILE = "utel-rel-22/original-0104-checkpoint/1"


def continuation_checkpoint(raw):
    def pairs(items):
        value = {}
        for key, item in items:
            require(key not in value, "duplicate continuation checkpoint field")
            value[key] = item
        return value
    require(type(raw) is str and len(raw.encode()) <= 1024, "continuation checkpoint text exceeds bound")
    value = json.loads(raw, object_pairs_hook=pairs)
    require(type(value) is dict and value == continuation_policy.CONTINUATION_CHECKPOINT,
            "original .104 continuation checkpoint differs")
    return value


def continuation_window():
    window = continuation_policy.CONTINUATION_WINDOW
    require(type(window) is dict and set(window) == {"authorityEntries", "seconds", "visibilityProbes", "quotaFloor"},
            ".104 continuation finite allocation has not been selected")
    require(all(type(value) is int and value > 0 for value in window.values()), "continuation window bounds differ")
    return dict(window)


class ContinuationPublisherAPI:
    """New invocation's application entries, separate from original exhaustion."""
    def __init__(self, api, deadline, window, clock=time.monotonic):
        self.api, self.deadline, self.window, self.clock = api, deadline, window, clock
        self.attempted, self.succeeded = {}, {}

    def __getattr__(self, name):
        require(name in {"get", "post", "patch", "download_asset", "upload_asset", "download_continuation_result", "download_continuation_candidate"},
                "continuation publisher method is outside remainder")
        def call(*args):
            require(self.clock() < self.deadline, "continuation publisher original deadline exhausted")
            self.attempted[name] = self.attempted.get(name, 0) + 1
            value = getattr(self.api, name)(*args)
            require(self.clock() < self.deadline, "continuation publisher deadline expired during acquisition")
            self.succeeded[name] = self.succeeded.get(name, 0) + 1
            return value
        return call

    def report(self):
        return {"attempted": dict(self.attempted), "succeeded": dict(self.succeeded),
                "scope": "new publisher entries under original deadline/existing transport limits; no new broad provider cap; feed subprocess/downloads and redirects are separate"}


def continuation_phase(report, label, ledger_api, publisher_api=None):
    # Bounded snapshots in the existing result, not a second authority ledger.
    rows = report.setdefault("continuationPhases", [])
    require(len(rows) < 16 and len(label) <= 64, "continuation diagnostic phase bound exceeded")
    row = {"phase": label, "authorityEntries": sum(ledger_api.attempted.values()),
           "authorityByMethod": dict(ledger_api.attempted)}
    if isinstance(publisher_api, ContinuationPublisherAPI):
        row["publisherEntries"] = sum(publisher_api.attempted.values())
        row["publisherByMethod"] = dict(publisher_api.attempted)
    rows.append(row)


def _continuation_original_run(api):
    fixed = continuation_policy.CONTINUATION_PROOF
    run = api.get(f"repos/{REPOSITORY}/actions/runs/{fixed['runId']}")
    require(type(run.get("id")) is int and run["id"] == fixed["runId"] and run.get("workflow_id") == 362181999
            and type(run.get("run_attempt")) is int and run["run_attempt"] == 1
            and run.get("head_sha") == fixed["sourceSha"] and run.get("status") == "completed" and run.get("conclusion") == "success"
            and run.get("path") == ".github/workflows/release-successor-publish.yml" and run.get("head_branch") == "main"
            and run.get("event") == "workflow_dispatch" and run.get("actor", {}).get("login") == "EHotwagner"
            and run.get("repository", {}).get("id") == 1269292704 and run.get("repository", {}).get("full_name") == REPOSITORY,
            "original .104 terminal native run differs")
    return run


def _continuation_source(api, path, revision, digest):
    value = api.get(f"repos/{REPOSITORY}/contents/{path}?ref={revision}")
    require(value.get("encoding") == "base64" and isinstance(value.get("content"), str)
            and hashlib.sha256(base64.b64decode(value["content"], validate=False)).hexdigest() == digest,
            "immutable continuation source/control-flow binding differs")


_CONTINUATION_PROOF_SEAL = object()


class VerifiedContinuationProof(dict):
    def __init__(self, seal, value):
        require(seal is _CONTINUATION_PROOF_SEAL, "continuation proof requires original authenticated evidence")
        super().__init__(value)


def authenticate_continuation_proof(api):
    fixed = continuation_policy.CONTINUATION_PROOF
    _continuation_original_run(api)
    artifact = api.get(f"repos/{REPOSITORY}/actions/artifacts/{fixed['artifactId']}")
    require(artifact.get("id") == fixed["artifactId"] and artifact.get("name") == "release-successor-result"
            and artifact.get("expired") is False and artifact.get("size_in_bytes") == fixed["archiveBytes"]
            and artifact.get("digest") == "sha256:" + fixed["archiveSha256"]
            and artifact.get("workflow_run", {}).get("id") == fixed["runId"]
            and artifact["workflow_run"].get("head_sha") == fixed["sourceSha"], "original .104 result artifact differs")
    expiry = datetime.datetime.fromisoformat(artifact["expires_at"].replace("Z", "+00:00"))
    require(expiry.tzinfo is not None and expiry > datetime.datetime.now(datetime.timezone.utc), "original .104 proof expired")
    raw = api.download_continuation_result()
    require(len(raw) == fixed["archiveBytes"] and hashlib.sha256(raw).hexdigest() == fixed["archiveSha256"], "original .104 proof ZIP differs")
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        entries = archive.infolist()
        require(len(entries) == 1 and entries[0].filename == "release-successor-result.json"
                and not entries[0].flag_bits & 1 and not entries[0].is_dir()
                and stat.S_IFMT(entries[0].external_attr >> 16) in (0, stat.S_IFREG)
                and entries[0].file_size == fixed["memberBytes"], "original .104 proof sole member differs")
        body = archive.read(entries[0])
    require(hashlib.sha256(body).hexdigest() == fixed["memberSha256"], "original .104 proof result differs")
    value = json.loads(body)
    checkpoint = continuation_policy.CONTINUATION_CHECKPOINT
    last, authority = value.get("lastObservedJournal", {}), value.get("authorityRequests", {})
    require(value.get("schema") == "fsgg.release-successor-result/1" and value.get("profile") == ALLOWED_SUCCESSOR["profile"]
            and value.get("execution") == {"sourceSha": fixed["sourceSha"], "runId": fixed["runId"], "runAttempt": "1", "operator": "EHotwagner"}
            and value.get("candidate") == {**continuation_policy.CONTINUATION_CANDIDATE, "version": "0.104.0"}
            and value.get("journalRef") == ALLOWED_SUCCESSOR["logicalRef"]
            and value.get("disposition") == "checkpoint" and value.get("publicationComplete") is False and value.get("failure") is None
            and {key:last.get(key) for key in checkpoint} == checkpoint
            and last.get("effects") == continuation_policy.continuation_effects(28)
            and value.get("lastDispatch") == {"effect": "qualification-asset:runtime", "state": "applied", "attempted": True}
            and authority.get("ceiling") == 1500 and authority.get("attemptedByMethod") == {"get": 1276, "post": 196, "patch": 28}
            and authority.get("blockedBeforeSend") == {"method": "get", "path": f"repos/{JOURNAL_REPOSITORY}/rulesets/24802698"},
            "original .104 NoSend control-flow result differs")
    # Immutable producer/import closure binds the admitted point BEFORE ReportingProvider.
    for path, digest in CONTINUATION_SOURCE_PINS.items():
        _continuation_source(api, path, fixed["sourceSha"], digest)
    decision = continuation_policy.CONTINUATION_DECISION
    _continuation_source(api, decision["path"], decision["revision"], decision["sha256"])
    return VerifiedContinuationProof(_CONTINUATION_PROOF_SEAL, {"originalProof": dict(fixed), "channelProviderEntryNotReached": True})


def continuation_native_census(api, ledger_api, execution):
    require(publisher_context_authorized(api, execution["sourceSha"], execution["runId"], hosted_install=True),
            "continuation native current source/run/actor/attempt differs")
    _continuation_original_run(api)
    for status in ("in_progress", "queued"):
        page = api.get(f"repos/{REPOSITORY}/actions/workflows/release-successor-publish.yml/runs?status={status}&per_page=100")
        require(type(page) is dict and type(page.get("total_count")) is int and 0 <= page["total_count"] < 100
                and isinstance(page.get("workflow_runs"), list) and len(page["workflow_runs"]) == page["total_count"],
                "continuation competing native entry census incomplete")
        require([row.get("id") for row in page["workflow_runs"]] == ([execution["runId"]] if status == "in_progress" else []),
                "another publisher entry is active or queued")
    scope = ledger_api.get("installation/repositories?per_page=100")
    require(scope.get("total_count") == 1 and isinstance(scope.get("repositories"), list)
            and [(row.get("id"), row.get("full_name")) for row in scope["repositories"]] == [(1351660651, JOURNAL_REPOSITORY)],
            "continuation Authority writer repository custody differs")


def run_0104_continuation(manifest, journal, admission, provider, ledger_api, publisher_api, report, deadline,
                          execution, proof, candidate_check, receipt_path, *, clock=time.monotonic, sleep=time.sleep):
    window, effects = continuation_window(), ordered_effects(manifest)
    require(type(proof) is VerifiedContinuationProof and proof.get("channelProviderEntryNotReached") is True and proof.get("originalProof") == continuation_policy.CONTINUATION_PROOF,
            "authenticated original .104 NoSend proof is required")
    require(manifest["descriptor"].get("sourceSha") == continuation_policy.CONTINUATION_CANDIDATE["sourceSha"]
            and manifest["contentId"] == continuation_policy.CONTINUATION_CANDIDATE["contentId"], "original continuation candidate differs")
    report.update(originalAuthorityEntries=1500, continuationWindow=dict(window),
                  cumulativeAuthorityCeiling=1500 + window["authorityEntries"])
    target = provider.provider if isinstance(provider, ReportingProvider) else provider
    last = [None]
    def guard(effect, action, *, before_claim=False, absent_payload=None):
        require(clock() < deadline, "continuation original finite deadline expired")
        candidate_check()
        # The known state selects observations only; it carries no authorization.
        # Fresh journal ownership is checked after all potentially slow reads.
        expected_effects = continuation_policy.continuation_effects(28) if before_claim else last[0]["effects"]
        for item in effects:
            if expected_effects.get(item.identity) == "verified":
                observed = provider.observe(item)
                require(observed.state == "matched" and observed.observed_digest == item.target_digest, "continuation verified prefix drifted")
        witness = target.continuation_release_witness()
        if absent_payload is not None:
            require((effect.identity == "promote" and witness["draft"] is True)
                    or (effect.identity != "promote" and absent_payload["name"] not in witness["names"]),
                    "continuation fresh presend absence changed")
        current = journal.read()
        if last[0] is not None:
            require(journal._observed.state == last[0], "continuation checkpoint changed outside current action")
        if before_claim:
            checkpoint = continuation_policy.CONTINUATION_CHECKPOINT
            require(journal._observed.head == checkpoint["logicalHead"]
                    and hashlib.sha256(canonical(journal._observed.state)).hexdigest() == checkpoint["stateSHA256"],
                    "original .104 checkpoint moved before claim")
        else:
            require(journal._observed.state.get("continuation") == permit.record, "continuation ownership marker changed")
        # Native source/run/custody/protection checks follow receipt/prefix reads,
        # rather than carrying their conclusion across those potentially slow I/O.
        continuation_native_census(publisher_api, ledger_api, execution)
        require(admission.authorize(manifest["contentId"], effect.identity, action, effect.request_digest), "continuation fresh action admission denied")
        require(clock() < deadline, "continuation deadline expired during full verification")
        return current
    guard(effects[13], "dispatch", before_claim=True)
    continuation_phase(report, "claimPrerequisites", ledger_api, publisher_api)
    record = continuation_policy.continuation_record(execution)
    report["journalMutationMayHaveOccurred"] = True
    permit = journal.claim_continuation(journal.read(), record)
    if isinstance(journal, ReportingJournal): journal.capture()
    last[0] = json.loads(canonical(journal._observed.state))
    target._continuation_permit = permit
    report["continuationClaim"] = {"logicalHead": permit.head, "execution": dict(execution)}
    continuation_phase(report, "claimReadback", ledger_api, publisher_api)
    probes = 0
    for effect in effects[13:]:
        current = guard(effect, "dispatch")
        observed = provider.observe(effect)
        require(observed.state in {"absent", "matched"} and (observed.state != "matched" or observed.observed_digest == effect.target_digest),
                "continuation remainder observation mismatched or unknown")
        state = current.effects.get(effect.identity)
        if observed.state == "absent":
            if state is None:
                current = guard(effect, "intent")
                require(journal.compare_and_swap(current, effect.identity, "intent"), "continuation intent CAS conflict")
                last[0] = json.loads(canonical(journal._observed.state))
            else: require(effect.identity == "channel-asset" and state == "intent", "continuation cannot resend an existing intent")
            payload = target.prepare_continuation_asset(effect) if effect.identity != "promote" else {"releaseId": 416097241, "draft": False, "make_latest": "true"}
            witness = target.continuation_release_witness()
            require((effect.identity == "promote" and witness["draft"] is True)
                    or (effect.identity != "promote" and payload["name"] not in witness["names"]), "continuation presend absence changed")
            receipt = {"schema": "fsgg.release-successor-presend/1", "effect": effect.identity, "execution": execution,
                       "originalProof": proof, "claimHead": permit.head, "payload": payload, "absence": witness,
                       "request": {"method": "PATCH" if effect.identity == "promote" else "POST", "releaseId": 416097241}}
            path = receipt_path.with_name(receipt_path.name + "-" + effect.identity + ".json")
            raw = canonical(receipt);require(len(raw) <= 16384, "continuation presend receipt exceeds bound")
            with path.open("xb") as stream:
                stream.write(raw)
                stream.flush()
                os.fsync(stream.fileno())
            directory_fd = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
            try:
                os.fsync(directory_fd)
            finally:
                os.close(directory_fd)
            require(path.read_bytes() == raw, "continuation presend receipt independent readback differs")
            report["continuationPresend"] = {"effect": effect.identity, "sha256": hashlib.sha256(raw).hexdigest(), "payload": payload}
            permit.consume(effect.identity)
            try:
                guard(effect, "dispatch", absent_payload=payload)
                if effect.identity != "promote": target._continuation_asset_path(effect)
                outcome = provider.dispatch(effect)
            finally: permit.finish_dispatch()
            require(outcome.state == "applied", "continuation send response unconfirmed; preserve original intent without retry")
            continuation_phase(report, effect.identity + ":dispatch", ledger_api, publisher_api)
            while True:
                require(clock() < deadline and probes < window["visibilityProbes"], "continuation finite visibility window exhausted")
                probes += 1;visible = target.continuation_visibility(effect)
                require(visible in {"absent", "potential"}, "continuation visibility is unknown")
                report["continuationVisibilityProbes"] = probes
                if visible == "potential": break
                if deadline - clock() <= 5 or probes >= window["visibilityProbes"]:
                    raise Refused("continuation finite absence observation exhausted; original intent retained")
                sleep(5)
        else:
            require(state == "intent", "preexisting continuation effect is outside retained intent")
            # Matching readback settles without a send, and still retires this
            # local permission so it cannot be used after that settlement.
            permit.consume(effect.identity)
            permit.finish_dispatch()
        current = guard(effect, "settle");observed = provider.observe(effect)
        require(observed.state == "matched" and observed.observed_digest == effect.target_digest, "continuation independent semantic readback failed")
        require(journal.compare_and_swap(current, effect.identity, "verified"), "continuation settlement CAS conflict")
        last[0] = json.loads(canonical(journal._observed.state))
        continuation_phase(report, effect.identity + ":settled", ledger_api, publisher_api)
    guard(effects[-1], "settle")
    require(journal._observed.state["generation"] == 34 and journal._observed.state["effects"] == {item.identity:"verified" for item in effects},
            "continuation final generation or full effect readback differs")
    continuation_phase(report, "finalReadback", ledger_api, publisher_api)
    report["cumulativeAuthorityEntries"] = 1500 + sum(ledger_api.attempted.values())
    return "complete"


CONTINUATION_SOURCE_PINS = {'scripts/authority-state-import.py': 'ccf87a351daa6e83fe313a09bde0854c88abae54146eb11e24c0903c014f5197', 'scripts/fsgg_feed.py': 'f7650429432417af72168c288cdee347b338328848870cb4f2f18d6b62d0733f', 'scripts/release-successor-publish.py': '41173b36b38787f3662623942cfc405fddafeede34b7211f795d728766082fe3', 'scripts/release_successor_admission.py': '17073aa84dcec104742bc507af5b8f82ba08e1e07ba24aa05149238f60a490f6', 'scripts/release_successor_execution.py': '0666ff5e3c3cf8064f274899184568942eeb8c7debe818e5d290f36fd4bd1677', 'scripts/release_successor_journal.py': 'bef65fbeac8a439a2cdad5d7c3f942a66b5fed11ced58f8e955d7e4ca5370242', 'scripts/release_successor_provider.py': '85fecfc4e47bf5158c73b3e573479ebf9dc76a619037caba8fdfc6f027c3d134'}


def selected_release_binding(args):
    """New ordinary source cut; explicit historical profiles keep original bindings."""
    if getattr(args, "suspended_0103_successor", False) or getattr(args, "continue_original_0104_checkpoint", False):
        require(not any(getattr(args, flag, False) for flag in ("original_0103_candidate",
            "recover_original_0103_tag_intent", "recover_original_successor_checkpoint", "recovery_checkpoint",
            "repeat_original_nuget_kit_intent", "recover_original_runtime_intent")), "literal .104 profile conflicts")
        return (ALLOWED_SUCCESSOR["version"], ALLOWED_SUCCESSOR["predecessor"], ALLOWED_SUCCESSOR["logicalRef"])
    if getattr(args, "original_0103_candidate", False) or getattr(args, "recover_original_0103_tag_intent", False):
        return ("0.103.0", "0.102.0", SUSPENDED_REF)
    if getattr(args, "recover_original_successor_checkpoint", False):
        return (ORIGINAL_SUCCESSOR_VERSION, ORIGINAL_SUCCESSOR_PREDECESSOR, ORIGINAL_SUCCESSOR_REF)
    historical = any(getattr(args, flag, False) for flag in
                     ("recovery_checkpoint", "repeat_original_nuget_kit_intent", "recover_original_runtime_intent"))
    return ("0.101.0", "0.100.0", REF) if historical else (SUCCESSOR_VERSION, SUCCESSOR_PREDECESSOR, SUCCESSOR_REF)


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise Refused(reason)


# Fixed original bytes; these pins grant compatibility, never effect authority.
ORIGINAL_0103_PROFILE = "utel-rel-21/original-candidate/1"
ORIGINAL_0103_TAG_PROFILE = "utel-rel-21/original-tag-intent/1"
ORIGINAL_0103_TAG_CHECKPOINT = {
    "logicalHead": "80bf2bace917316c79032542660a5c00519a828c",
    "stateSHA256": "67018515b0e6b49f96986d7844729461099e950058d20d0a9fa0b8ba3f3864d4",
    "originalPhysicalHead": "9691196bfd169fef57d93389764fdea77e7be2d8",
    "runId": 38580684077, "publisher": "6c2747248b9d757316b93d7efe8aaf797a080549",
    "artifactId": 11902876694, "archiveBytes": 1511,
    "archiveSha256": "edff2d42ad2bde96317073e066ac1e813a90023b4dacda0d2e12d3c198c30ea9",
    "memberSha256": "9be16e4e8f96ae1990ce2a4f52acc4274089d2c2f87942c0aa80e71626728620"}
ORIGINAL_0103_CANDIDATE = {
    "sourceSha": "4d136d5450a7032d0b87c4b8a37f69f020144792",
    "runId": 38290966724,
    "attempt": 1,
    "artifactId": 11766710761,
    "archiveBytes": 32744064,
    "archiveSha256": "d8b22728b9e36932bf8d31e1ab486b312f2d1aedddf96660c872fc69f97f25ba",
    "contentId": "sha256:0615d0d776f06b824b98193233c749f0ae6983e811d900cc5a166ede5e7e5007",
    "version": "0.103.0",
    "predecessor": "0.102.0",
    "predecessorContentId": "sha256:fc16321e011ea616648ccc1dafda00bfc06fa34210fd49ddb008bb56c9ceecf5",
    "manifestSha256": "0b9236c6fd6d3893361d86f67e2d639c02d4e72ce3044b9b6150eeae2259d18f"
}
ORIGINAL_0103_PACKAGES = {
    "FS.GG.Coord.Cli": {
        "archiveSha256": "ba23b95624ec9423708f42dd6abf39933fc5e006e5047e6ab7ba160f9d16ff68",
        "archiveBytes": 32398808,
        "payloadSha256": "sha256:dd7cfd60abbcaf31f633f7d09d82116ecef2aac6302bd11b23e7e27b611e3461"
    },
    "FS.GG.Drivers": {
        "archiveSha256": "e40b3016d528de783d945bc09d2a3c1cf8f9879e93383158dc4b94ae590709f0",
        "archiveBytes": 166742,
        "payloadSha256": "sha256:acbad9f8ac79c84a99aa8ef34f8e8d6b600b1571a8aab7ef18c5a1ebd95623fa"
    },
    "FS.GG.Kit": {
        "archiveSha256": "acdcadd17d002bb92b8b9cc4db766f7a9121c2c6201b09205636a3a54f24aad0",
        "archiveBytes": 266524,
        "payloadSha256": "sha256:5a1b4ed3c016225eaca9bf19ac3cbad3cba40e1ed9fb6aebee71ed6c80d88b7f"
    }
}
ORIGINAL_0103_DEPENDENCIES = {
    "scripts/release_successor_admission.py": {
        "bytes": 2530,
        "sha256": "42392e4275db815337eeee3fe41022e784dea8666e9c0aa6c817f9aeb7c60281"
    },
    "scripts/release_successor_execution.py": {
        "bytes": 8257,
        "sha256": "ea1fb156eca3d63ab4d7ae0c60ecfd6fd5d2c5053c25019252451a855387375e"
    },
    "scripts/release_successor_journal.py": {
        "bytes": 24133,
        "sha256": "14104ca72087c79412d7fe3828a197bb2f0ea6e70e3675a9e2d70d98c5e9202c"
    },
    "scripts/release_successor_provider.py": {
        "bytes": 43721,
        "sha256": "b5e9dbeccc18539b0a7bb0153651fd9205cfeb297cbe5e789941d1cb487744f1"
    },
    "scripts/release-successor-artifact.py": {
        "bytes": 7264,
        "sha256": "1c2ce68c0f5b80d142be7776ffd50319278d9816f5aaa108abc7be862180f431"
    },
    "scripts/release-saga.py": {
        "bytes": 61127,
        "sha256": "24bdfa2a4c1491ecc1ddf24f797a072372fdce14881d413d9301b739dc77501c"
    },
    "scripts/check-release-candidate-uniqueness.py": {
        "bytes": 2177,
        "sha256": "496732557948fa43a8a8a099ce0583235c21aa6bba01223ad31398ff12351424"
    },
    "scripts/fsgg_feed.py": {
        "bytes": 22427,
        "sha256": "4be8af2a1784a7ac2b1ab32bd548e944f3d3feb7253bf602662b81d18cf8f744"
    },
    "scripts/authority-state-import.py": {
        "bytes": 107728,
        "sha256": "ccf87a351daa6e83fe313a09bde0854c88abae54146eb11e24c0903c014f5197"
    }
}
# Explicit descendant compatibility, not a rewrite of the nine historical pins.
# Reviewed source-only closure: .github PR4544 at 2fb32c51a630f972c593b331f9ff46471796d237
# (same seven blobs as semantic/security review3330abb7). The provider adds inert
# proof/custody paths and retains the unavailable production factory; feed_versions
# refuses authenticated index redirects, preserving direct reads and nupkg stripping.
# The first unchanged baseline refused "original .103 dependency size differs"
# (8 failures/3 errors); neither that failure nor attempted-unknown tag is erased.
# This exact WHOLE descendant closure admits no arbitrary future drift, new candidate,
# signing key, native custody, credential effect or historical non-dispatch claim.
ORIGINAL_0103_DESCENDANT_PROFILE = "utel-rel-21/descendant-source-compatibility/1"
ORIGINAL_0103_DESCENDANT_DEPENDENCIES = {
    "scripts/release_successor_provider.py": {
        "bytes": 85901,
        "sha256": "b20239f394379e2c42dba023d43065bdebe4e6a620ab8adba77be77c4689bd17"
    },
    "scripts/fsgg_feed.py": {
        "bytes": 22991,
        "sha256": "f7650429432417af72168c288cdee347b338328848870cb4f2f18d6b62d0733f"
    }
}
_ORIGINAL_0103_SEAL = object()


class VerifiedOriginal0103(tuple):
    """Immutable seam input created by the complete compatibility check."""

    __slots__ = ()

    def __new__(cls, seal, publisher_sha):
        require(seal is _ORIGINAL_0103_SEAL, "original .103 selection is not verified")
        return super().__new__(cls, (publisher_sha,))

    @property
    def publisher_sha(self):
        return self[0]


def validate_original_0103_request(args):
    tag_recovery = bool(getattr(args, "recover_original_0103_tag_intent", False))
    selected = bool(getattr(args, "original_0103_candidate", False)) or tag_recovery
    if selected:
        require(not (tag_recovery and getattr(args, "original_0103_candidate", False)),
                "original tag selector conflicts with ordinary original candidate selector")
        require(not any(getattr(args, name, False) for name in
                        ("recovery_checkpoint", "repeat_original_nuget_kit_intent",
                         "recover_original_runtime_intent", "recover_original_successor_checkpoint")),
                "original .103 selection conflicts with historical profile")
        fixed = ORIGINAL_0103_CANDIDATE
        require((args.candidate_run_id, args.candidate_artifact_id, args.candidate_archive_sha256) ==
                (fixed["runId"], fixed["artifactId"], fixed["archiveSha256"]),
                "original .103 requested candidate differs")
        require(selected_release_binding(args) == (fixed["version"], fixed["predecessor"], SUSPENDED_REF)
                and SUSPENDED_REF == "refs/heads/fsgg/v2/journal/release/utel-rel-21",
                "original .103 release binding differs")
    return selected


def validate_original_0103_metadata(run, artifact):
    fixed = ORIGINAL_0103_CANDIDATE
    require(run.get("id") == fixed["runId"] and run.get("run_attempt") == fixed["attempt"]
            and run.get("head_sha") == fixed["sourceSha"] and run.get("status") == "completed"
            and run.get("repository", {}).get("id") == 1269292704,
            "original .103 native run identity differs")
    require(artifact.get("id") == fixed["artifactId"] and artifact.get("expired") is False
            and artifact.get("size_in_bytes") == fixed["archiveBytes"]
            and artifact.get("digest") == "sha256:" + fixed["archiveSha256"],
            "original .103 native artifact identity differs")


def validate_original_0103_manifest(manifest):
    fixed = ORIGINAL_0103_CANDIDATE
    descriptor = manifest.get("descriptor", {})
    descriptor_bytes = json.dumps(descriptor, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()
    require(manifest.get("contentId") == fixed["contentId"]
            and "sha256:" + hashlib.sha256(descriptor_bytes).hexdigest() == fixed["contentId"]
            and descriptor.get("sourceSha") == fixed["sourceSha"]
            and descriptor.get("version") == fixed["version"]
            and descriptor.get("policyVersion") == "release-successor/1"
            and descriptor.get("previousStableVersion") == fixed["predecessor"]
            and descriptor.get("previousStableContentId") == fixed["predecessorContentId"],
            "original .103 manifest binding differs")
    rows = descriptor.get("packages", [])
    require(isinstance(rows, list) and len(rows) == 3
            and {row.get("id") for row in rows} == set(ORIGINAL_0103_PACKAGES),
            "original .103 package population differs")
    for row in rows:
        package = ORIGINAL_0103_PACKAGES[row["id"]]
        require(row.get("version") == fixed["version"] and row.get("artifact") == {
            "path": f"{row['id']}.{fixed['version']}.nupkg", "sha256": package["archiveSha256"],
            "size": package["archiveBytes"], "payloadSha256": package["payloadSha256"]},
            "original .103 package binding differs")


def verify_original_0103_dependencies(root=None):
    """Accept one complete historical OR explicitly reviewed descendant closure.

    Per-file alternatives only bound the read. The final whole-set comparison
    refuses unreviewed historical/descendant hybrids as well as future bytes.
    Original candidate/archive checks and fresh effect admission stay separate.
    """
    root = root or pathlib.Path(__file__).resolve().parents[1]
    observed = {}
    for name, pin in ORIGINAL_0103_DEPENDENCIES.items():
        allowed = [pin]
        if name in ORIGINAL_0103_DESCENDANT_DEPENDENCIES:
            allowed.append(ORIGINAL_0103_DESCENDANT_DEPENDENCIES[name])
        path = root / name
        require(not any(part.is_symlink() for part in [path, *path.parents]),
                "original .103 dependency is linked")
        with path.open("rb") as stream:
            before = os.fstat(stream.fileno())
            require(stat.S_ISREG(before.st_mode) and before.st_size in {row["bytes"] for row in allowed},
                    "original .103 dependency size differs")
            digest = hashlib.file_digest(stream, "sha256").hexdigest()
            after = os.fstat(stream.fileno())
        identity = lambda value: (value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns, value.st_ctime_ns)
        value = {"bytes": before.st_size, "sha256": digest}
        require(identity(before) == identity(after) == identity(path.stat())
                and value in allowed, "original .103 dependency bytes differ")
        observed[name] = value
    if observed == ORIGINAL_0103_DEPENDENCIES:
        return "utel-rel-21/frozen-source-compatibility/1"
    descendant = {**ORIGINAL_0103_DEPENDENCIES, **ORIGINAL_0103_DESCENDANT_DEPENDENCIES}
    require(observed == descendant, "original .103 dependency closure mixes unreviewed revisions")
    return ORIGINAL_0103_DESCENDANT_PROFILE


def verify_original_0103(args, run, artifact, manifest_bytes, archive, candidate, publisher_sha, api):
    require(validate_original_0103_request(args), "original .103 selection was not requested")
    validate_original_0103_metadata(run, artifact)
    fixed = ORIGINAL_0103_CANDIDATE
    require(hashlib.sha256(manifest_bytes).hexdigest() == fixed["manifestSha256"],
            "original .103 manifest bytes differ")
    manifest = json.loads(manifest_bytes)
    validate_original_0103_manifest(manifest)
    with archive.open("rb") as stream:
        require(os.fstat(stream.fileno()).st_size == fixed["archiveBytes"]
                and hashlib.file_digest(stream, "sha256").hexdigest() == fixed["archiveSha256"],
                "original .103 archive bytes differ")
    for name, package in ORIGINAL_0103_PACKAGES.items():
        with (candidate / f"{name}.{fixed['version']}.nupkg").open("rb") as stream:
            require(os.fstat(stream.fileno()).st_size == package["archiveBytes"]
                    and hashlib.file_digest(stream, "sha256").hexdigest() == package["archiveSha256"],
                    "original .103 retained package bytes differ")
    verify_original_0103_dependencies()
    require(isinstance(publisher_sha, str) and re.fullmatch(r"[0-9a-f]{40}", publisher_sha),
            "original .103 publisher identity differs")
    if publisher_sha != fixed["sourceSha"]:
        comparison = api.get(f"repos/{REPOSITORY}/compare/{fixed['sourceSha']}...{publisher_sha}")
        require(comparison.get("status") == "ahead"
                and type(comparison.get("ahead_by")) is int and comparison["ahead_by"] > 0
                and type(comparison.get("behind_by")) is int and comparison["behind_by"] == 0
                and comparison.get("base_commit", {}).get("sha") == fixed["sourceSha"]
                and comparison.get("merge_base_commit", {}).get("sha") == fixed["sourceSha"],
                "original .103 publisher ancestry differs")
        head = api.get(f"repos/{REPOSITORY}/commits/{publisher_sha}")
        require(head.get("sha") == publisher_sha, "original .103 publisher commit identity differs")
    return VerifiedOriginal0103(_ORIGINAL_0103_SEAL, publisher_sha)


def validate_original_0103_seam(selection, manifest, intent, candidate_source, publisher_sha, journal_ref):
    fixed = ORIGINAL_0103_CANDIDATE
    require(type(selection) is VerifiedOriginal0103 and selection.publisher_sha == publisher_sha,
            "original .103 journal selection is not verified")
    validate_original_0103_manifest(manifest)
    require(candidate_source == fixed["sourceSha"] and journal_ref == SUSPENDED_REF
            and journal_ref == "refs/heads/fsgg/v2/journal/release/utel-rel-21"
            and intent == {"contentId": fixed["contentId"], "sourceSha": fixed["sourceSha"],
                           "version": fixed["version"], "candidateArchiveSha256": fixed["archiveSha256"],
                           "operator": "EHotwagner"}, "original .103 journal intent differs")


def authority_protection():
    """Load only the existing read-only predicate and reviewed rule binding."""
    path = pathlib.Path(__file__).with_name("authority-state-import.py")
    spec = importlib.util.spec_from_file_location("successor_authority_protection", path)
    require(spec is not None and spec.loader is not None, "Authority protection source unavailable")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    binding = module.ADMITTED_IMPORT
    require(isinstance(binding, dict), "reviewed Authority protection binding unavailable")
    return module.validate_native_protection, {
        "rulesets": binding["rulesets"], "protectionAcceptedAt": binding["protectionAcceptedAt"],
    }


# Admission is GET-only: suspension, publisher identity/source and Authority protection
# reads. A transient read failure (an exception, never an explicit False) is retried a
# bounded number of times with a short backoff before it denies (#4811, #4753). Writes
# are not part of admission and are never retried here; a persistent failure still
# denies. This does not cover the journal helper's own post-PATCH readback.
ADMISSION_READ_ATTEMPTS = 3
ADMISSION_READ_BACKOFF_SECONDS = (0.5, 1.5)


class ProtectedPublisherAdmission:
    """Compose exact publisher admission with fresh main/retirement protections."""

    def __init__(self, publisher, ledger_api, logical_ref, *, suspension=None, sleep=time.sleep):
        self.sleep = sleep
        self.read_failures = 0
        self.publisher = publisher
        self.ledger_api = ledger_api
        self.logical_ref = logical_ref
        require(suspension is None or (isinstance(suspension, ReleaseSuspension) and suspension.api is ledger_api
                and logical_ref == ALLOWED_SUCCESSOR["logicalRef"]), "shared publisher suspension reader binding differs")
        self.suspension = suspension or (ReleaseSuspension(ledger_api) if logical_ref in {SUSPENDED_REF, ALLOWED_SUCCESSOR["logicalRef"]} else None)
        self.protection, self.binding = authority_protection()

    def authorize(self, content_id, effect, action, request_digest):
        for attempt in range(ADMISSION_READ_ATTEMPTS):
            try:
                if self.suspension is not None:
                    self.suspension.authorize(self.logical_ref)
                if not self.publisher.authorize(content_id, effect, action, request_digest):
                    return False  # An explicit denial is final; only failed reads are retried.
                self.protection(self.ledger_api, self.binding, [self.logical_ref])
                return True
            except AuthorityQuotaWaiting:
                raise  # Selected pre-send budget waiting is an incomplete checkpoint.
            except (KeyError, TypeError, ValueError, OSError, RuntimeError, http.client.HTTPException):
                self.read_failures += 1
                if attempt + 1 == ADMISSION_READ_ATTEMPTS:
                    return False
                self.sleep(ADMISSION_READ_BACKOFF_SECONDS[attempt])
        return False


def authority_main(api):
    value = api.get(f"repos/{JOURNAL_REPOSITORY}/git/ref/heads/main")
    require(isinstance(value, dict) and isinstance(value.get("object"), dict),
            "Authority main response is malformed")
    obj = value.get("object", {})
    require(obj.get("type") == "commit" and isinstance(obj.get("sha"), str)
            and re.fullmatch(r"[0-9a-f]{40}", obj["sha"]), "Authority main has no exact commit head")
    return obj["sha"]


def classify_journal_destination(api, logical_ref):
    """Return (fresh, physical head); nested read failures never mean absence."""
    prefix = "refs/heads/fsgg/v2/journal/release/"
    require(isinstance(logical_ref, str) and logical_ref.startswith(prefix)
            and re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]*", logical_ref.removeprefix(prefix)),
            "selected release journal identity differs")
    repository = api.get(f"repos/{JOURNAL_REPOSITORY}")
    require(isinstance(repository, dict) and repository.get("id") == 1351660651
            and repository.get("full_name") == JOURNAL_REPOSITORY,
            "Authority repository identity differs")
    physical = authority_main(api)
    commit = api.get(f"repos/{JOURNAL_REPOSITORY}/git/commits/{physical}")
    require(isinstance(commit, dict) and commit.get("sha") == physical
            and isinstance(commit.get("tree"), dict) and isinstance(commit.get("parents"), list)
            and len(commit["parents"]) <= 2
            and all(isinstance(parent, dict) and isinstance(parent.get("sha"), str)
                    and re.fullmatch(r"[0-9a-f]{40}", parent["sha"]) for parent in commit["parents"]),
            "Authority main commit identity differs")
    tree = commit.get("tree", {}).get("sha")
    present = True
    selected = logical_ref.removeprefix(prefix)
    for depth, segment in enumerate(("state", "releases", selected)):
        require(isinstance(tree, str) and re.fullmatch(r"[0-9a-f]{40}", tree),
                "Authority tree identity is malformed")
        value = api.get(f"repos/{JOURNAL_REPOSITORY}/git/trees/{tree}")
        require(isinstance(value, dict), "Authority destination tree response is malformed")
        entries = value.get("tree")
        require(value.get("sha") == tree and value.get("truncated") is False
                and isinstance(entries, list) and len(entries) <= 1000,
                "Authority destination tree is incomplete")
        require(all(isinstance(entry, dict) and isinstance(entry.get("path"), str)
                    and entry["path"] and entry["path"] not in {".", ".."} and "/" not in entry["path"]
                    and entry.get("type") in {"tree", "blob", "commit"}
                    and entry.get("mode") in {"040000", "100644", "100755", "120000", "160000"}
                    and isinstance(entry.get("sha"), str)
                    and re.fullmatch(r"[0-9a-f]{40}", entry["sha"]) for entry in entries)
                and len({entry["path"] for entry in entries}) == len(entries),
                "Authority destination entries are malformed or ambiguous")
        matches = [entry for entry in entries if entry["path"] == segment]
        if not matches:
            require(depth == 2, "Authority required parent directory is missing")
            present = False
            break
        require(matches[0].get("type") == "tree" and matches[0].get("mode") == "040000",
                "Authority journal destination is not a directory")
        tree = matches[0]["sha"]
    try:
        api.get(f"repos/{JOURNAL_REPOSITORY}/git/ref/{logical_ref.removeprefix('refs/')}")
    except NotFound:
        pass
    else:
        raise Refused("selected legacy release journal still exists; separate disposition required")
    require(authority_main(api) == physical, "Authority main moved during destination classification")
    return not present, physical


def prepare_release_journal(journal, ledger_api, admission, provider, api, manifest, intent,
                            candidate_source, publisher_sha, preflight_only, uniqueness, *,
                            recovery_checkpoint=False, original_0103_selection=None, original_tag_recovery=False):
    """Qualify the physical route before genesis or original-intent recovery."""
    if journal.ref in {SUSPENDED_REF, ALLOWED_SUCCESSOR["logicalRef"]}:
        ReleaseSuspension(ledger_api).authorize(journal.ref, manifest["descriptor"]["version"])
    if original_0103_selection is not None:
        require(not recovery_checkpoint, "original .103 selection conflicts with recovery checkpoint")
        validate_original_0103_seam(original_0103_selection, manifest, intent, candidate_source, publisher_sha, journal.ref)
    fresh, physical = classify_journal_destination(ledger_api, journal.ref)
    require(not original_tag_recovery or (original_0103_selection is not None and not fresh),
            "original tag recovery requires the existing original journal; no genesis")
    if fresh:
        require(not recovery_checkpoint, "recovery checkpoint requires the existing original journal")
        require(candidate_source == publisher_sha or original_0103_selection is not None,
                "fresh publication requires candidate and publisher source to match")
        try:
            api.get(f"repos/FS-GG/.github/git/ref/tags/coherent-set/v{manifest['descriptor']['version']}")
        except NotFound:
            pass
        else:
            raise Refused("successor tag already exists outside the protected journal")
        require(provider._release() is None, "successor release already exists outside the protected journal")
        uniqueness()
        require(authority_main(ledger_api) == physical, "Authority main moved before journal preparation")
        require(admission.authorize(manifest["contentId"], "journal", "intent", manifest["contentId"]),
                "release journal initialization admission denied")
        if preflight_only:
            return True
        journal.initialize(intent)
    else:
        require(authority_main(ledger_api) == physical, "Authority main moved before journal recovery")
        journal.read()
        require(authority_main(ledger_api) == physical, "Authority main moved during journal recovery")
        require(not preflight_only or original_tag_recovery,
                "preflight requires an unused release journal and version")
        if candidate_source != publisher_sha:
            comparison = api.get(f"repos/{REPOSITORY}/compare/{candidate_source}...{publisher_sha}")
            require(comparison.get("status") == "ahead", "recovery publisher does not descend from candidate source")
    journal.validate_intent(intent)
    return False


# This profile recovers only the retained journal19 candidate. These are identity
# bindings, not credentials or an authority receipt for a new invocation.
RECOVERY_PROFILE = "utel-rel-19/four-advance/1"
KIT_REPEAT_PROFILE = "utel-rel-19/original-nuget-kit-repeat/1"
RUNTIME_RECOVERY_PROFILE = "utel-rel-19/original-runtime-intent-recovery/1"
RUNTIME_EFFECT = "qualification-asset:runtime"
RUNTIME_NAME = "standalone-telemetry-runtime-evidence.json"
ORIGINAL_RUNTIME_HEAD = "e25521bff7f24dc0c454327c854f0d912fc6fa2c"
ORIGINAL_RUNTIME_STATE_SHA256 = "8b7b4995a328d9fc300f9db579aeeb7703d1d03c80834f95df55b43ec1d93829"
RUNTIME_REFUSAL_SOURCE = "a988d25c68263ce44a1d0f48c3a1e72f2428fa82"
RUNTIME_REFUSAL_ZIP_SHA256 = "7f50582d26f171d2d446ad5eed10268fe169b07e321881b97f41a59fe9232107"
RUNTIME_REFUSAL_MEMBER_SHA256 = "76dcf6db0ed2f138d22fb19678ec16cdaa09d7a6aabb537c75e8d5c11295dffb"
KIT_EFFECT = "nuget:FS.GG.Kit"
KIT_ENDPOINT = "https://api.nuget.org/v3/index.json"
ORIGINAL_KIT_JOURNAL_HEAD = "30edfbd8aee914492ce359f4dd34454701a41d5a"
ORIGINAL_KIT_STATE_SHA256 = "494c772deaadd71f635a50237fc4dee0077c71c71968ee4c3ff6ef31a25e1dce"
AUTHORITY_CALL_CEILING = 4200
AUTHORITY_QUOTA_FLOOR = 4500
RECOVERY_ADVANCES = 4
ORDINARY_AUTHORITY_CALL_CEILING = 1500
ORDINARY_AUTHORITY_QUOTA_FLOOR = 1800
ORDINARY_VISIBILITY_PROBES = 135
SUCCESSOR_CHECKPOINT_PROFILE = "utel-rel-20/original-bounded-forward/1"
SUCCESSOR_CHECKPOINT_CANDIDATE = {
    "runId": 38128101680, "artifactId": 11698335858,
    "archiveSha256": "3ecc266e3c3b2d521ed4143af68555483034f399177e078d213bc719d7908ebb",
    "sourceSha": "6e9be17bacd288ece103b67ecd5842d37a9c8e50",
    "contentId": "sha256:fc16321e011ea616648ccc1dafda00bfc06fa34210fd49ddb008bb56c9ceecf5",
}
RECOVERY_CANDIDATE = {
    "runId": 38026714312,
    "artifactId": 11660242226,
    "archiveSha256": "d5d7d68ab0f73ce020e36f6ae10321b632eb177162d925cd9faabc071891bd64",
    "sourceSha": "79051e56b025dadfc6fcaabd58b79e33f2854928",
    "contentId": "sha256:e8ed439047f663dfcb34aba1152ffd4c6966a0c3a27be0ebb48eebcf47e26996",
}


def error_chain(error):
    """Bound reporting while retaining the primary and explicitly omitted causes."""
    rows, seen = [], set()
    while error is not None and id(error) not in seen and len(rows) < 8:
        seen.add(id(error))
        message = str(error)
        rows.append({"type": type(error).__name__, "message": message[:512],
                     "messageTruncated": len(message) > 512})
        error = error.__cause__ if error.__cause__ is not None else error.__context__
    return {"chain": rows, "chainIncomplete": error is not None}


class AuthorityQuotaWaiting(Refused):
    """Ordinary bounded checkpoint: no new time or permission to resend."""


class CountedAuthorityAPI:
    """One counter for application calls through the original installation token.

    This does not count transport redirects, token provisioning, or provider calls.
    Its fixed ceiling cannot be increased by a CLI input or by token replacement.
    """
    def __init__(self, api, *, ceiling=AUTHORITY_CALL_CEILING, quota_floor=AUTHORITY_QUOTA_FLOOR):
        require(type(ceiling) is int and 0 < ceiling <= AUTHORITY_CALL_CEILING
                and type(quota_floor) is int and ceiling < quota_floor <= AUTHORITY_QUOTA_FLOOR,
                "Authority request budget differs")
        self.api, self.ceiling, self.quota_floor = api, ceiling, quota_floor
        self.attempted = {method: 0 for method in ("get", "post", "patch")}
        self.succeeded = {method: 0 for method in self.attempted}
        self.last_success = None
        self.last_failure = None
        self.blocked = None
        self.quota = None

    def _call(self, method, path, *args):
        if sum(self.attempted.values()) >= self.ceiling:
            self.blocked = {"method": method, "path": path[:256]}
            error_type = Refused if self.ceiling == AUTHORITY_CALL_CEILING else AuthorityQuotaWaiting
            error = error_type("Authority API ceiling exhausted; reconcile original operation")
            self.last_failure = {"method": method, "path": path[:256], "error": error_chain(error)}
            raise error
        self.attempted[method] += 1
        try:
            result = getattr(self.api, method)(path, *args)
        except Exception as error:
            self.last_failure = {"method": method, "path": path[:256], "error": error_chain(error)}
            raise
        self.succeeded[method] += 1
        self.last_success = {"method": method, "path": path[:256]}
        return result

    def get(self, path):
        return self._call("get", path)

    def post(self, path, body):
        return self._call("post", path, body)

    def patch(self, path, body):
        return self._call("patch", path, body)

    def admit_quota(self, now=None):
        value = self.get("rate_limit")  # Counted before journal construction.
        require(isinstance(value, dict) and isinstance(value.get("resources"), dict),
                "Authority quota resources are malformed")
        core = value["resources"].get("core")
        require(isinstance(core, dict), "Authority core quota is unavailable")
        require(all(type(core.get(key)) is int for key in ("limit", "remaining", "reset", "used")),
                "Authority core quota fields are malformed")
        limit, remaining, reset, used = (core[key] for key in ("limit", "remaining", "reset", "used"))
        require(0 < limit <= 1000000000 and 0 <= remaining <= limit and 0 <= used <= limit
                and used + remaining == limit and 0 < reset < 2**63,
                "Authority core quota values are inconsistent")
        observed = time.time() if now is None else now
        require(reset > observed, "Authority core quota reset is not in the future")
        self.quota = {"resource": "core", "limit": limit, "remaining": remaining,
                      "reset": reset, "used": used, "observedUnix": observed}
        if remaining < self.quota_floor:
            if self.quota_floor == AUTHORITY_QUOTA_FLOOR:
                raise Refused("fixed recovery requires at least 4500 observed Authority core requests")
            raise AuthorityQuotaWaiting("ordinary Authority quota is below its admitted request budget")
        return self.quota

    def report(self):
        return {"ceiling": self.ceiling, "attemptedByMethod": dict(self.attempted),
                "succeededByMethod": dict(self.succeeded), "lastSuccessfulEntry": self.last_success,
                "lastFailedEntry": self.last_failure, "blockedBeforeSend": self.blocked,
                "quota": self.quota, "scope": "application API entries; no reservation or wire-request claim"}


class RuntimePublisherAPI:
    """Attribute bounded publisher calls separately from the Authority counter."""
    def __init__(self, api):
        self.api, self.attempted, self.succeeded = api, {}, {}

    def __getattr__(self, name):
        if name not in {"get", "runtime_recovery_json", "download_asset", "download_runtime_refusal_artifact",
                        "download_runtime_recovery_asset", "upload_runtime_recovery_asset"}:
            raise Refused("runtime recovery API method is not selected")
        def call(*args):
            require(sum(self.attempted.values()) < 256, "runtime recovery publisher API bound exhausted")
            self.attempted[name] = self.attempted.get(name, 0) + 1
            result = getattr(self.api, name)(*args)
            self.succeeded[name] = self.succeeded.get(name, 0) + 1
            return result
        return call

    def report(self):
        return {"attempted": self.attempted, "succeeded": self.succeeded, "ceiling": 256,
                "scope": "publisher API application entries; candidate gh ZIP, feed downloads/subprocesses and redirects are separate"}


class OriginalTagPublisherAPI:
    """Finite read-only publisher entry budget plus the separately capped tag send."""
    def __init__(self, api, deadline, clock=time.monotonic):
        self.api, self.deadline, self.clock = api, deadline, clock
        self.attempted, self.succeeded = {}, {}

    def claim(self, name):
        require(self.clock() < self.deadline and sum(self.attempted.values()) < 256,
                "original tag publisher deadline or256entry budget exhausted")
        self.attempted[name] = self.attempted.get(name, 0) + 1

    def get(self, path):
        require(path == f"repos/{REPOSITORY}" or path.startswith(f"repos/{REPOSITORY}/"),
                "original tag reader repository differs")
        self.claim("get")
        value = self.api.get(path)
        require(self.clock() < self.deadline, "original tag publisher deadline expired during read")
        self.succeeded["get"] = self.succeeded.get("get", 0) + 1
        return value

    def download_original_tag_result(self):
        self.claim("originalResultArchive")
        value = self.api.download_original_tag_result()
        require(self.clock() < self.deadline, "original tag result download expired")
        self.succeeded["originalResultArchive"] = 1
        return value

    def report(self):
        return {"attempted": self.attempted, "succeeded": self.succeeded, "ceiling": 256,
                "scope": "publisher application entries including candidate gh ZIP and one tag POST; redirects are not wire-count claims"}


def authenticate_original_tag_result(api):
    """Use only the pinned genuine original run/artifact/member, never a supplied receipt."""
    fixed = ORIGINAL_0103_TAG_CHECKPOINT
    run = api.get(f"repos/{REPOSITORY}/actions/runs/{fixed['runId']}")
    require(run.get("id") == fixed["runId"] and run.get("workflow_id") == 362181999
            and run.get("run_attempt") == 1 and run.get("head_sha") == fixed["publisher"]
            and run.get("path") == ".github/workflows/release-successor-publish.yml"
            and run.get("head_branch") == "main" and run.get("event") == "workflow_dispatch"
            and run.get("actor", {}).get("login") == "EHotwagner"
            and run.get("repository", {}).get("id") == 1269292704
            and run.get("repository", {}).get("full_name") == REPOSITORY
            and run.get("status") == "completed" and run.get("conclusion") == "success",
            "original tag publisher native identity differs")
    artifact = api.get(f"repos/{REPOSITORY}/actions/artifacts/{fixed['artifactId']}")
    require(artifact.get("id") == fixed["artifactId"] and artifact.get("name") == "release-successor-result"
            and artifact.get("size_in_bytes") == fixed["archiveBytes"] and artifact.get("expired") is False
            and artifact.get("digest") == "sha256:" + fixed["archiveSha256"]
            and artifact.get("workflow_run", {}).get("id") == fixed["runId"]
            and artifact.get("workflow_run", {}).get("head_sha") == fixed["publisher"],
            "original tag result artifact identity differs")
    expiry = datetime.datetime.fromisoformat(artifact["expires_at"].replace("Z", "+00:00"))
    require(expiry.tzinfo is not None and expiry > datetime.datetime.now(datetime.timezone.utc),
            "original tag result artifact expired")
    raw = api.download_original_tag_result()
    require(len(raw) == fixed["archiveBytes"] and hashlib.sha256(raw).hexdigest() == fixed["archiveSha256"],
            "original tag result archive differs")
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        entries = archive.infolist()
        require(len(entries) == 1 and entries[0].filename == "release-successor-result.json"
                and not entries[0].flag_bits & 1 and not entries[0].is_dir()
                and stat.S_IFMT(entries[0].external_attr >> 16) in (0, stat.S_IFREG)
                and 0 < entries[0].file_size <= 16384, "original tag result member differs")
        body = archive.read(entries[0])
    require(hashlib.sha256(body).hexdigest() == fixed["memberSha256"], "original tag result member bytes differ")
    value = json.loads(body)
    require(value.get("execution") == {"operator": "EHotwagner", "runAttempt": "1", "runId": fixed["runId"],
                                       "sourceSha": fixed["publisher"]}
            and value.get("schema") == "fsgg.release-successor-result/1" and value.get("profile") == ORIGINAL_0103_PROFILE
            and value.get("candidate") == {"runId": ORIGINAL_0103_CANDIDATE["runId"],
                "artifactId": ORIGINAL_0103_CANDIDATE["artifactId"], "archiveSha256": ORIGINAL_0103_CANDIDATE["archiveSha256"],
                "contentId": ORIGINAL_0103_CANDIDATE["contentId"], "sourceSha": ORIGINAL_0103_CANDIDATE["sourceSha"],
                "version": "0.103.0"}
            and value.get("disposition") == "checkpoint" and value.get("publicationComplete") is False
            and value.get("failure") is None and value.get("journalRef") == SUSPENDED_REF
            and value.get("lastDispatch") == {"attempted": True, "effect": "tag", "state": "unknown"}
            and value.get("lastObservedJournal", {}).get("generation") == 2
            and value["lastObservedJournal"].get("effects") == {"tag": "intent"}
            and value["lastObservedJournal"].get("logicalHead") == fixed["logicalHead"]
            and value["lastObservedJournal"].get("stateSHA256") == fixed["stateSHA256"],
            "original tag uncertainty result binding differs")
    return {"runId": fixed["runId"], "attempt": 1, "publisher": fixed["publisher"],
            "artifactId": fixed["artifactId"], "archiveSha256": fixed["archiveSha256"],
            "memberSha256": fixed["memberSha256"], "historicalPhysicalHead": fixed["originalPhysicalHead"],
            "historicalNonDispatchProved": False}


def validate_original_tag_state(manifest, journal):
    validate_original_0103_manifest(manifest)
    observed = journal._observed
    fixed = ORIGINAL_0103_TAG_CHECKPOINT
    require(journal.ref == SUSPENDED_REF and journal.main_directory and observed is not None
            and observed.head == fixed["logicalHead"] and observed.state["generation"] == 2
            and observed.state["effects"] == {"tag": "intent"}
            and observed.state["contentId"] == manifest["contentId"]
            and hashlib.sha256(canonical(observed.state)).hexdigest() == fixed["stateSHA256"],
            "original tag requires exact retained generation2/logical state")
    return ordered_effects(manifest)[0]


def exact_original_tag_observation(api):
    repository = api.get(f"repos/{REPOSITORY}")
    require(repository.get("id") == 1269292704 and repository.get("full_name") == REPOSITORY,
            "original tag observation repository differs")
    try:
        reference = api.get(f"repos/{REPOSITORY}/git/ref/tags/coherent-set/v0.103.0")
    except NotFound:
        return "absent"
    require(isinstance(reference, dict) and reference.get("ref") == "refs/tags/coherent-set/v0.103.0"
            and isinstance(reference.get("object"), dict) and reference["object"].get("type") == "commit"
            and reference["object"].get("sha") == ORIGINAL_0103_CANDIDATE["sourceSha"],
            "original tag reference is mismatched, malformed, or annotated")
    return "matched"


def verify_original_tag_candidate(manifest, provider, manifest_bytes, archive):
    validate_original_0103_manifest(manifest)
    require(provider.manifest_path.read_bytes() == manifest_bytes and provider.manifest == manifest
            and provider.version == "0.103.0" and provider.source == ORIGINAL_0103_CANDIDATE["sourceSha"]
            and not archive.is_symlink() and stat.S_ISREG(archive.stat().st_mode),
            "original tag candidate/provider archive binding moved")
    require(hashlib.sha256(manifest_bytes).hexdigest() == ORIGINAL_0103_CANDIDATE["manifestSha256"]
            and archive.stat().st_size == ORIGINAL_0103_CANDIDATE["archiveBytes"]
            and hashlib.sha256(archive.read_bytes()).hexdigest() == ORIGINAL_0103_CANDIDATE["archiveSha256"],
            "original tag retained archive/manifest bytes moved")
    for name, pin in ORIGINAL_0103_PACKAGES.items():
        path = provider.root / f"{name}.0.103.0.nupkg"
        require(not path.is_symlink() and stat.S_ISREG(path.stat().st_mode) and path.stat().st_size == pin["archiveBytes"]
                and hashlib.sha256(path.read_bytes()).hexdigest() == pin["archiveSha256"],
                "original tag retained package bytes moved")
    return {"contentId": manifest["contentId"], "archiveSha256": ORIGINAL_0103_CANDIDATE["archiveSha256"],
            "manifestSha256": ORIGINAL_0103_CANDIDATE["manifestSha256"]}


def run_original_tag_recovery(manifest, journal, admission, provider, ledger_api, publisher_api,
                              report, deadline, verify_candidate, attempt_path, *, preflight=False,
                              factory=approved_original_tag_transport, clock=time.monotonic):
    """One matched settlement OR one explicit original create, never a later effect."""
    ReleaseSuspension(ledger_api).authorize(SUSPENDED_REF, "0.103.0")
    effect = validate_original_tag_state(manifest, journal)
    original = authenticate_original_tag_result(publisher_api)
    observed, physical = journal._observed, journal._physical_head
    guarded = UnchangedKitJournal(journal, observed, physical)
    report["originalTagRecovery"] = {"profile": ORIGINAL_0103_TAG_PROFILE, "original": original,
        "maxDispatchesThisInvocation": 1, "historicalNonDispatchProved": False, "globalOnceClaim": False,
        "observation": None, "branch": "independent-observation", "attemptRecord": None,
        "productionCapability": "unavailable; no approved credential factory"}
    require(clock() < deadline, "original tag deadline expired")
    observation = exact_original_tag_observation(publisher_api)
    report["originalTagRecovery"]["observation"] = observation
    guarded.read()
    require(clock() < deadline, "original tag observation expired")
    require(admission.authorize(manifest["contentId"], "tag", "intent", effect.request_digest),
            "original tag current publisher observation admission denied")
    if preflight:
        report["originalTagRecovery"]["branch"] = "non-writing-existing-intent-preflight"
        return "preflight"
    if observation == "matched":
        require(exact_original_tag_observation(publisher_api) == "matched",
                "original tag match moved before settlement")
        current = guarded.read()
        require(admission.authorize(manifest["contentId"], "tag", "settle", effect.request_digest),
                "original tag matched settlement denied")
        require(guarded.compare_and_swap(current, "tag", "verified"), "original tag settlement CAS conflict")
        report["originalTagRecovery"]["branch"] = "matched-one-settlement-no-send"
        return "checkpoint"
    # Refuse original-byte/admission changes BEFORE the credential factory is entered.
    candidate = verify_candidate()
    require(candidate == {key: ORIGINAL_0103_CANDIDATE[key]
                          for key in ('contentId', 'archiveSha256', 'manifestSha256')},
            'original tag pre-credential candidate binding changed')
    guarded.read()
    require(clock() < deadline and admission.authorize(manifest['contentId'], 'tag', 'dispatch', effect.request_digest),
            'original tag pre-credential admission denied')
    ledger_api.admit_quota()
    require(authority_main(ledger_api) == physical and clock() < deadline
            and sum(ledger_api.attempted.values()) < ORDINARY_AUTHORITY_CALL_CEILING
            and ledger_api.quota is not None and ledger_api.quota['reset'] > time.time(),
            'original tag pre-credential physical head/quota/deadline changed')
    # Factory acquisition AND all later candidate/quota/record/send/report paths
    # are inside one lexical owner; no returned live token outlives this scope.
    lease = factory()  # Native factory still refuses before acquisition.
    require(callable(getattr(lease, '__enter__', None)) and callable(getattr(lease, '__exit__', None)),
            'original tag factory requires an owned credential lease')
    with lease as transport:
        require(type(transport) is VerifiedOriginalTagTransport, "original tag verified transport unavailable")
        capability = transport.identity()
        candidate = verify_candidate()
        require(candidate == {"contentId": ORIGINAL_0103_CANDIDATE["contentId"],
                              "archiveSha256": ORIGINAL_0103_CANDIDATE["archiveSha256"],
                              "manifestSha256": ORIGINAL_0103_CANDIDATE["manifestSha256"]},
                "original tag candidate binding changed")
        require(admission.authorize(manifest["contentId"], "tag", "dispatch", effect.request_digest),
                "original tag uncertainty dispatch admission denied")
        ledger_api.admit_quota()
        require(authority_main(ledger_api) == physical and clock() < deadline
                and sum(ledger_api.attempted.values()) < ORDINARY_AUTHORITY_CALL_CEILING
                and ledger_api.quota is not None and ledger_api.quota["reset"] > time.time(),
                "original tag current physical head/quota/deadline changed")
        record = {"schema": "fsgg.original-tag-intent-attempt/1", "profile": ORIGINAL_0103_TAG_PROFILE,
                  "execution": report.get("execution"), "original": original, "candidate": candidate,
                  "journal": {"logicalHead": observed.head, "physicalHead": physical, "generation": 2,
                              "stateSHA256": ORIGINAL_0103_TAG_CHECKPOINT["stateSHA256"]},
                  "request": {"method": "POST", "path": f"repos/{REPOSITORY}/git/refs",
                              "ref": "refs/tags/coherent-set/v0.103.0", "sha": ORIGINAL_0103_CANDIDATE["sourceSha"],
                              "requestDigest": effect.request_digest}, "capabilityIdentity": capability,
                  "historicalNonDispatchProved": False, "globalOnceClaim": False, "maxDispatchesThisInvocation": 1}
        write_kit_repeat_attempt(attempt_path, record)
        report["originalTagRecovery"]["attemptRecord"] = {"path": str(attempt_path),
            "sha256": hashlib.sha256(attempt_path.read_bytes()).hexdigest()}
        guarded.read()
        require(verify_candidate() == candidate and transport.identity() == capability,
                "original tag candidate/capability moved before send")
        require(exact_original_tag_observation(publisher_api) == "absent",
                "original tag appeared before the explicit create")
        require(admission.authorize(manifest["contentId"], "tag", "dispatch", effect.request_digest)
                and authority_main(ledger_api) == physical and clock() < deadline
                and sum(ledger_api.attempted.values()) < ORDINARY_AUTHORITY_CALL_CEILING
                and ledger_api.quota is not None and ledger_api.quota["reset"] > time.time(),
                "original tag final source/physical/deadline admission denied")
        publisher_api.claim("tagPost")
        report["originalTagRecovery"]["branch"] = "one-explicit-create-no-settlement"
        report["lastDispatch"] = {"effect": "tag", "attempted": True, "state": "unknown"}
        outcome = transport.dispatch(effect, deadline, clock)
        report["lastDispatch"]["state"] = outcome.state
        try:
            diagnostic = sanitized_dispatch_diagnostic({"category": "protocol-failure", "request": transport.last_dispatch_diagnostic})
        except BaseException:
            diagnostic = {"diagnosticUnavailable": True}
        if diagnostic is not None:
            report["lastDispatch"]["diagnostic"] = diagnostic
        return "checkpoint"  # No journal mutation or second effect after the sole send, including201.


class ReportingJournal:
    """Observe existing successful reads without extra reads or altered CAS policy."""
    def __init__(self, journal, report, *, detailed=False):
        self.journal = journal
        self.report = report
        self.detailed = detailed

    def __getattr__(self, name):
        return getattr(self.journal, name)

    def capture(self):
        observed = self.journal._observed
        if observed is not None:
            state = observed.state
            value = {"generation": state["generation"], "logicalHead": observed.head,
                     "physicalHead": self.journal._physical_head,
                     "effects": dict(state["effects"])}
            if self.detailed:
                value["stateSHA256"] = hashlib.sha256(canonical(state)).hexdigest()
                value["intent"] = {key: state[key] for key in
                                   ("contentId", "sourceSha", "version", "candidateArchiveSha256", "operator")}
            if self.report["startingJournal"] is None:
                self.report["startingJournal"] = value
            self.report["lastObservedJournal"] = value

    def read(self):
        state = self.journal.read()
        self.capture()
        return state

    def initialize(self, intent):
        self.report["journalMutationMayHaveOccurred"] = True
        try:
            return self.journal.initialize(intent)
        finally:
            self.capture()

    def compare_and_swap(self, expected, effect, state):
        self.report["journalMutationMayHaveOccurred"] = True
        try:
            result = self.journal.compare_and_swap(expected, effect, state)
            return result
        finally:
            # A failed PATCH/readback never becomes a committed-CAS assertion.
            self.capture()


class ReportingProvider:
    """Retain dispatch uncertainty without retrying or changing provider behavior."""
    def __init__(self, provider, report):
        self.provider, self.report = provider, report

    def __getattr__(self, name):
        return getattr(self.provider, name)

    def observe(self, effect):
        return self.provider.observe(effect)

    def dispatch(self, effect):
        row = {"effect": effect.identity, "state": "unknown", "attempted": True}
        self.report["lastDispatch"] = row
        def retain(error=None):
            try:
                value = getattr(self.provider, "last_dispatch_diagnostic", None)
                if error is not None and value is None:
                    value = {"category": "uncaught-provider-failure", "request": getattr(error, "diagnostic", None)}
                diagnostic = sanitized_dispatch_diagnostic(value)
                if diagnostic is not None:
                    row["diagnostic"] = diagnostic
            except BaseException:
                row["diagnosticUnavailable"] = True  # Never change the provider's first outcome/cause.
        try:
            result = self.provider.dispatch(effect)
        except BaseException as error:
            retain(error)
            raise
        retain()
        row["state"] = result.state
        return result


def validate_checkpoint_state(manifest, journal):
    effects = ordered_effects(manifest)
    require(journal.ref == "refs/heads/fsgg/v2/journal/release/utel-rel-19"
            and journal.main_directory and len(effects) == 16,
            "fixed recovery journal or effect set differs")
    observed = journal._observed
    require(observed is not None, "fixed recovery has no successfully observed journal")
    current = observed.state
    require(type(current["generation"]) is int and 14 <= current["generation"] <= 33,
            "fixed recovery requires starting generation 14 through 33")
    known = {effect.identity for effect in effects}
    require(not set(current["effects"]) - known
            and all(value in {"intent", "verified"} for value in current["effects"].values()),
            "fixed recovery has an unknown effect or state")
    opened = False
    verified = intents = 0
    for effect in effects:
        state = current["effects"].get(effect.identity, "pending")
        require(not (state == "verified" and opened), "fixed recovery verified prefix is incoherent")
        if state == "verified":
            verified += 1
        else:
            require(not (opened and state != "pending"), "fixed recovery has multiple in-flight effects")
            opened = True
            intents += state == "intent"
    require(current["generation"] == 1 + 2 * verified + intents,
            "fixed recovery generation disagrees with the retained effect prefix")


def run_recovery_checkpoint(manifest, journal, admission, provider, report, deadline,
                            clock=time.monotonic, sleep=time.sleep):
    validate_checkpoint_state(manifest, journal)
    for index in range(RECOVERY_ADVANCES):
        require(clock() < deadline, "publication observation deadline expired; reconcile before another run")
        report["iterationsAttempted"] = index + 1
        report["stage"] = "advance"
        result = advance(manifest, journal, admission, provider)
        report["lastAdvanceResult"] = result
        report["lastSuccessfulStage"] = "advance"
        require(result in {"verified", "waiting", "complete"}, "unexpected recovery advance result")
        if result == "complete":
            return "complete"
        require(clock() < deadline, "publication observation deadline expired; reconcile before another run")
        if index + 1 < RECOVERY_ADVANCES:
            sleep(5)
    return "checkpoint"



def validate_successor_checkpoint(manifest, journal, expected):
    require(journal.ref == ORIGINAL_SUCCESSOR_REF and journal.main_directory
            and manifest["descriptor"]["version"] == ORIGINAL_SUCCESSOR_VERSION
            and manifest["descriptor"]["sourceSha"] == SUCCESSOR_CHECKPOINT_CANDIDATE["sourceSha"]
            and manifest["contentId"] == SUCCESSOR_CHECKPOINT_CANDIDATE["contentId"],
            "original successor checkpoint binding differs")
    require(type(expected) is dict and set(expected) == {"generation", "logicalHead", "stateSHA256"}
            and type(expected["generation"]) is int and 1 <= expected["generation"] <= 33
            and isinstance(expected["logicalHead"], str) and re.fullmatch(r"[0-9a-f]{40}", expected["logicalHead"])
            and isinstance(expected["stateSHA256"], str) and re.fullmatch(r"[0-9a-f]{64}", expected["stateSHA256"]),
            "expected successor checkpoint is malformed")
    observed = journal._observed
    require(observed is not None and observed.head == expected["logicalHead"]
            and observed.state["generation"] == expected["generation"]
            and hashlib.sha256(canonical(observed.state)).hexdigest() == expected["stateSHA256"],
            "original successor checkpoint changed")
    effects = ordered_effects(manifest)
    require(len(effects) == 16 and not set(observed.state["effects"]) - {e.identity for e in effects},
            "original successor effect set differs")
    verified = intents = 0
    opened = False
    for effect in effects:
        state = observed.state["effects"].get(effect.identity, "pending")
        require(state in {"pending", "intent", "verified"}, "original successor effect state differs")
        if state == "verified":
            require(not opened, "original successor verified prefix differs")
            verified += 1
        else:
            require(not (opened and state != "pending"), "original successor has multiple in-flight effects")
            opened = True
            intents += state == "intent"
    require(observed.state["generation"] == 1 + 2 * verified + intents,
            "original successor checkpoint generation differs")


def run_forward(manifest, journal, admission, provider, report, deadline, *, maximum=RECOVERY_ADVANCES,
                clock=time.monotonic, sleep=time.sleep, ordinary=False, candidate_check=lambda: None):
    """Full transitions separated from finite pending-only observations.

    `ordinary` is selected by the maintained current caller, never workflow input.
    Historical recovery callers retain their original stop-at-wait semantics.
    """
    require(type(ordinary) is bool, "ordinary observation selection differs")
    require(type(maximum) is int and 0 < maximum <= 152, "forward advance bound differs")
    require(clock() < deadline, "publication observation deadline expired; reconcile before another run")
    admission = RuntimeDeadlineAdmission(admission, deadline, clock,
                                         candidate_check=candidate_check if ordinary else None)
    counted = journal.journal.api
    require(isinstance(counted, CountedAuthorityAPI), "forward Authority counter is unavailable")
    counted.api = RuntimeDeadlineAuthority(counted.api, deadline, clock)
    effects = ordered_effects(manifest)
    if ordinary:
        require(journal.ref == CURRENT_RELEASE["logicalRef"]
                and manifest["descriptor"].get("version") == CURRENT_RELEASE["version"]
                and manifest["descriptor"].get("previousStableVersion") == CURRENT_RELEASE["predecessor"],
                "current ordinary release binding differs")
        target = provider.provider if isinstance(provider, ReportingProvider) else provider
        target._ordinary_mode = True
        target._ordinary_journal = journal.journal
        target._ordinary_deadline_check = lambda: require(clock() < deadline, "ordinary deadline expired before provider send")
    def prepare_ordinary(effect):
        observed = journal.journal._observed
        require(observed is not None and observed.state["contentId"] == manifest["contentId"]
                and observed.state["effects"].get(effect.identity) == "intent",
                "ordinary dispatch has no independently read-back committed intent")
        target._ordinary_expected_journal = (observed.head, canonical(observed.state))
        provider.prepare_ordinary_dispatch(effect)
    probes = 0
    for index in range(maximum):
        require(clock() < deadline, "publication observation deadline expired; reconcile before another run")
        report["iterationsAttempted"] = index + 1
        report["stage"] = "advance"
        if ordinary:
            result = full_transition(manifest["contentId"], effects, journal, admission, provider,
                                     prepare_dispatch=prepare_ordinary)
            pending = result.pending
            result = result.state
        else:
            result = advance(manifest, journal, admission, provider)
            pending = None
        report["lastAdvanceResult"] = result
        report["lastSuccessfulStage"] = "advance"
        require(result in {"verified", "waiting", "complete", "unknown"}, "unexpected forward advance result")
        require(clock() < deadline, "publication observation deadline expired; reconcile before another run")
        if result in {"verified", "complete"}:
            report.pop("pendingReason", None)
        if result == "complete":
            if ordinary:
                # Provider prefix verification may be slow. Bind final journal
                # and candidate again, then finish with fresh native admission.
                current = journal.read()
                require(current.content_id == manifest["contentId"] and
                        current.effects == {effect.identity: "verified" for effect in effects},
                        "final journal no longer binds the complete candidate")
                require(admission.authorize(manifest["contentId"], effects[-1].identity, "settle", effects[-1].request_digest),
                        "final publication admission denied")
            return "complete"
        if result == "unknown":
            report["pendingReason"] = "sender outcome Unknown; retained intent requires independent reconciliation"
            return "checkpoint"
        if result == "waiting":
            report["pendingReason"] = "exact retained intent awaits independent provider observation"
            if not ordinary:
                return "checkpoint"
            require(pending is not None, "pending effect observation context is unavailable")
            report["pendingEffect"] = {"identity": pending.effect.identity, "generation": pending.journal.generation}
            if index + 1 >= maximum:
                report["pendingReason"] = "original finite transition window exhausted; retained intent remains incomplete"
                return "checkpoint"
            while True:
                if probes >= ORDINARY_VISIBILITY_PROBES:
                    report["pendingReason"] = "finite pending visibility probe window exhausted"
                    return "checkpoint"
                require(clock() < deadline, "publication observation deadline expired; reconcile before another run")
                probes += 1
                report["visibilityProbes"] = probes
                report["stage"] = "pending-observation"
                visible = provider.ordinary_visibility(pending.effect)
                require(clock() < deadline, "publication observation deadline expired during pending observation")
                require(visible in {"absent", "potential"}, "pending observation is not a validated visibility witness")
                if visible == "potential":
                    break  # No authority retained: the next full transition rereads everything.
                if probes >= ORDINARY_VISIBILITY_PROBES:
                    report["pendingReason"] = "finite pending visibility probe window exhausted"
                    return "checkpoint"
                if deadline - clock() <= 5:
                    report["pendingReason"] = "original deadline has no next five-second observation interval"
                    return "checkpoint"
                sleep(5)
    report["pendingReason"] = "admitted finite transition window exhausted"
    return "checkpoint"

def write_result(path, report):
    if report.get("authenticationSession") is not None:
        return finalize_authentication_result(path, report)
    body = (json.dumps(report, sort_keys=True, separators=(",", ":")) + "\n").encode()
    require(len(body) <= 16384, "publisher result exceeds its 16KiB bound")
    with path.open("xb") as stream:
        stream.write(body)


def finalize_authentication_result(path, report):
    """Extend the existing retained projection under its original destination lock."""
    directory = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
    try:
        fcntl.flock(directory, fcntl.LOCK_EX)
        require(path.exists() and not path.is_symlink() and stat.S_ISREG(path.stat().st_mode),
                'authentication finalization requires original durable result')
        with path.open('rb') as stream:
            prior_bytes = stream.read(16385)
        require(len(prior_bytes) <= 16384, 'authentication finalization prior exceeds bound')
        prior = json.loads(prior_bytes)
        require(type(prior) is dict and prior.get('authenticationSession') is not None
                and json.dumps(prior['authenticationSession'], sort_keys=True, separators=(',', ':'))
                    == json.dumps(report['authenticationSession'], sort_keys=True, separators=(',', ':'))
                and (json.dumps(prior, sort_keys=True, separators=(',', ':')) + '\n').encode('ascii') == prior_bytes,
                'authentication finalization projection differs; reconcile original evidence')
        body = (json.dumps(report, sort_keys=True, separators=(',', ':')) + '\n').encode('ascii')
        require(len(body) <= 16384, 'authentication finalized result exceeds bound')
        temporary = path.with_name(path.name + '.authentication-finalizing')
        with temporary.open('xb') as stream:
            os.chmod(temporary, 0o600)
            stream.write(body); stream.flush(); os.fsync(stream.fileno())
        os.replace(temporary, path)
        os.fsync(directory)
        require(not path.is_symlink() and stat.S_ISREG(path.stat().st_mode),
                'authentication finalized readback is not regular')
        with path.open('rb') as stream:
            actual = stream.read(16385)
        require(actual == body, 'authentication finalized readback differs')
    finally:
        os.close(directory)


def persist_authentication_snapshot(path, report, binding, frame):
    """Retain the optional operation projection, then independently read it back.

    Called outside the producer custody loop. This synchronous local sink is not
    a hard deadline mechanism; its task and cleanup belong to that producer.
    No ACK is created here, and an uncertain write/readback cannot permit an ACK.
    """
    # Lock the stable existing directory, not the result inode replaced below.
    # This serializes late callbacks and independent writers using this sink;
    # waiting/fsync remain producer-accounted tasks outside the custody loop.
    directory = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
    try:
        fcntl.flock(directory, fcntl.LOCK_EX)
        return _persist_authentication_snapshot_locked(path, report, binding, frame)
    finally:
        os.close(directory)


def _persist_authentication_snapshot_locked(path, report, binding, frame):
    row = decode_auth_frame(frame)
    require(set(row) == {'v', 'b', 'n', 'k', 'p'} and row['v'] == AUTH_PROTOCOL
            and row['b'] == validate_auth_binding(binding) and type(row['n']) is int,
            'authentication snapshot binding differs')
    prior = report.get('authenticationSession')
    if prior is None:
        require(not path.exists() and not path.is_symlink(),
                'authentication destination already exists; reconcile retained evidence')
    else:
        require(path.exists() and not path.is_symlink() and stat.S_ISREG(path.stat().st_mode),
                'authentication durable prior unavailable')
        with path.open('rb') as stream:
            previous_bytes = stream.read(16385)
        require(len(previous_bytes) <= 16384, 'authentication durable prior exceeds bound')
        durable = json.loads(previous_bytes)
        require(type(durable) is dict and durable.get('authenticationSession') == prior
                and json.dumps(durable.get('authenticationSession'), sort_keys=True, separators=(',', ':'))
                    == json.dumps(prior, sort_keys=True, separators=(',', ':'))
                and (json.dumps(durable, sort_keys=True, separators=(',', ':')) + '\n').encode('ascii') == previous_bytes,
                'authentication durable prior differs; reconcile original evidence')
        require(type(prior) is dict and set(prior) == {'binding', 'ordinal', 'frameDigest', 'frame'}
                and prior['binding'] == row['b'] and type(prior['ordinal']) is int and 1 <= prior['ordinal'] <= 6,
                'authentication prior identity differs')
        previous_row = prior['frame']
        require(type(previous_row) is dict and set(previous_row) == {'v', 'b', 'n', 'k', 'p'}
                and previous_row['v'] == AUTH_PROTOCOL and previous_row['b'] == row['b']
                and type(previous_row['n']) is int and previous_row['n'] == prior['ordinal'],
                'authentication prior frame binding differs')
        validate_auth_payload(previous_row['k'], previous_row['p'], binding)
        canonical = json.dumps(previous_row, sort_keys=True, separators=(',', ':')).encode('ascii')
        require(hashlib.sha256(len(canonical).to_bytes(4, 'big') + canonical).hexdigest() == prior['frameDigest'],
                'authentication prior frame digest differs')
    previous = prior['frame']['p'].get('facts') if prior is not None else None
    allowed = {None: {'ready'}, 'ready': {'issuance-possible', 'terminal'},
               'issuance-possible': {'token-owned', 'terminal'}, 'token-owned': {'revocation-possible'},
               'revocation-possible': {'revoked', 'terminal'}, 'revoked': {'terminal'}, 'terminal': set()}
    require(row['n'] == (prior['ordinal'] + 1 if prior else 1)
            and row['k'] in allowed[prior['frame']['k'] if prior else None], 'authentication snapshot order differs')
    validate_auth_payload(row['k'], row['p'], binding, previous)
    projection = {'binding': row['b'], 'ordinal': row['n'],
                  'frameDigest': hashlib.sha256(frame).hexdigest(), 'frame': row}
    retained = dict(report, authenticationSession=projection)
    body = (json.dumps(retained, sort_keys=True, separators=(',', ':')) + '\n').encode('ascii')
    require(len(body) <= 16384, 'authentication result exceeds existing result bound')
    temporary = path.with_name(path.name + '.authentication-pending')
    if path.exists() or path.is_symlink():
        require(not path.is_symlink() and stat.S_ISREG(path.stat().st_mode), 'authentication result is not regular')
    with temporary.open('xb') as stream:
        os.chmod(temporary, 0o600)
        stream.write(body); stream.flush(); os.fsync(stream.fileno())
    os.replace(temporary, path)
    directory = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(directory)
    finally:
        os.close(directory)
    require(not path.is_symlink() and stat.S_ISREG(path.stat().st_mode), 'authentication readback is not regular')
    with path.open('rb') as stream:
        actual = stream.read(16385)
    require(actual == body, 'authentication retained readback differs')
    observed = json.loads(actual)['authenticationSession']
    require(observed == projection, 'authentication snapshot readback differs')
    report['authenticationSession'] = projection
    return {name: projection[name] for name in ('binding', 'ordinal', 'frameDigest')}


class KitObservationProvider(ReportingProvider):
    """Record ordinary observations; only the explicit repeat seam enables one send."""
    def __init__(self, provider, report, effect):
        super().__init__(provider, report)
        self.effect = effect
        self.observations = []
        self.repeat_enabled = False
        self.repeat_attempted = False

    def observe(self, effect):
        value = self.provider.observe(effect)
        self.observations.append({"effect": effect.identity, "state": value.state,
                                  "observedDigest": value.observed_digest})
        self.report["kitRepeat"]["observations"] = list(self.observations)
        return value

    def dispatch(self, effect):
        require(self.repeat_enabled and not self.repeat_attempted and effect == self.effect
                and effect.identity == KIT_EFFECT, "original Kit repeat send is not admitted")
        self.repeat_attempted = True  # Exceptions still consume this invocation's send.
        return super().dispatch(effect)


class UnchangedKitJournal:
    """Fence ordinary pre-send reads to the exact retained generation14 binding."""
    def __init__(self, journal, observed, physical):
        self.journal, self.observed, self.physical = journal, observed, physical

    def read(self):
        value = self.journal.read()
        require(self.journal._observed == self.observed
                and self.journal._physical_head == self.physical,
                "original Kit journal moved before the repeat boundary")
        return value

    def compare_and_swap(self, *args):
        return self.journal.compare_and_swap(*args)


def validate_original_kit_state(manifest, journal):
    validate_checkpoint_state(manifest, journal)
    effects = ordered_effects(manifest)
    expected = {effect.identity: "verified" for effect in effects[:6]}
    expected[KIT_EFFECT] = "intent"
    observed = journal._observed
    require(observed.head == ORIGINAL_KIT_JOURNAL_HEAD
            and hashlib.sha256(canonical(observed.state)).hexdigest() == ORIGINAL_KIT_STATE_SHA256
            and observed.state["generation"] == 14 and observed.state["effects"] == expected
            and observed.state["contentId"] == manifest["contentId"]
            and effects[6].identity == KIT_EFFECT,
            "original Kit repeat requires the exact retained head and generation14 prefix")
    return effects[6]


def verify_kit_repeat_candidate(manifest, provider, manifest_bytes):
    """Recheck original manifest/archive/payload locally using existing pure semantics."""
    require(provider.manifest_path.read_bytes() == manifest_bytes
            and provider.manifest == manifest and provider.content_id == manifest["contentId"]
            and provider.version == "0.101.0" and provider.source == manifest["descriptor"]["sourceSha"],
            "original Kit repeat manifest or provider binding changed")
    effect = next(effect for effect in ordered_effects(manifest) if effect.identity == KIT_EFFECT)
    package = next(row for row in manifest["descriptor"]["packages"] if row["id"] == "FS.GG.Kit")
    path = provider.root / "FS.GG.Kit.0.101.0.nupkg"
    require(package["artifact"]["path"] == path.name and not path.is_symlink()
            and stat.S_ISREG(path.stat().st_mode) and path.resolve().parent == provider.root.resolve(),
            "original Kit repeat archive is not the owned exact candidate file")
    saga_path = pathlib.Path(__file__).with_name("release-saga.py")
    spec = importlib.util.spec_from_file_location("kit_repeat_payload", saga_path)
    saga = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(saga)
    require(saga.sha256(path) == effect.request_digest
            and saga.payload_id(path) == effect.target_digest
            and saga.nuspec(path)[:2] == ("FS.GG.Kit", "0.101.0"),
            "original Kit repeat archive, normalized payload or package identity differs")
    require(provider._feed_url("nuget", "FS.GG.Kit") ==
            "https://api.nuget.org/v3-flatcontainer/fs.gg.kit/0.101.0/fs.gg.kit.0.101.0.nupkg",
            "original Kit repeat observation endpoint differs")
    return {"effect": KIT_EFFECT, "packageId": "FS.GG.Kit", "version": "0.101.0", "feed": "nuget.org",
            "archiveSha256": effect.request_digest, "payloadSha256": effect.target_digest,
            "argv": ["dotnet", "nuget", "push", str(path), "--source", KIT_ENDPOINT,
                     "--api-key", "<credential omitted>"],
            "localFile": {"device": path.stat().st_dev, "inode": path.stat().st_ino,
                          "size": path.stat().st_size, "mtimeNs": path.stat().st_mtime_ns}}


def write_kit_repeat_attempt(path, record):
    """One exclusive fsynced local invocation record, never a global retry lease."""
    body = (json.dumps(record, sort_keys=True, separators=(",", ":")) + "\n").encode()
    require(len(body) <= 16384, "original Kit repeat attempt record exceeds16KiB")
    with path.open("xb") as stream:
        stream.write(body)
        stream.flush()
        os.fsync(stream.fileno())
    directory = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(directory)
    finally:
        os.close(directory)


def run_original_kit_repeat(manifest, journal, admission, provider, ledger_api, report, deadline,
                            verify_candidate, attempt_path, clock=time.monotonic):
    effect = validate_original_kit_state(manifest, journal)
    observed = journal._observed
    physical = journal._physical_head
    report["kitRepeat"] = {"profile": KIT_REPEAT_PROFILE, "effect": KIT_EFFECT,
                           "maxDispatchesThisInvocation": 1, "historicalNonDispatchProved": False,
                           "globalOnceClaim": False, "observations": [], "attemptRecord": None,
                           "branch": "ordinary-observation"}
    guarded = UnchangedKitJournal(journal, observed, physical)
    recorded = KitObservationProvider(provider, report, effect)
    require(clock() < deadline, "original Kit repeat observation deadline expired")
    report["iterationsAttempted"] = 1
    result = advance(manifest, guarded, admission, recorded)
    report["lastAdvanceResult"] = result
    report["lastSuccessfulStage"] = "advance"
    require(clock() < deadline, "original Kit repeat observation deadline expired")
    if result == "verified":
        report["kitRepeat"]["branch"] = "matched-settlement-no-send"
        return "checkpoint"  # Ordinary matched CAS14→15; no second advance/send.
    require(result == "waiting" and recorded.observations[-1] ==
            {"effect": KIT_EFFECT, "state": "absent", "observedDigest": None},
            "original Kit repeat lacks the targeted fresh absent observation")
    require(clock() < deadline, "original Kit repeat observation deadline expired")
    report["stage"] = "repeat-unchanged-journal"
    guarded.read()  # One extra read only, with all ordinary canonical/history checks.
    candidate = verify_candidate()
    require(candidate["effect"] == KIT_EFFECT and candidate["feed"] == "nuget.org"
            and candidate["packageId"] == "FS.GG.Kit" and candidate["version"] == "0.101.0"
            and candidate["archiveSha256"] == effect.request_digest
            and candidate["payloadSha256"] == effect.target_digest
            and candidate["argv"][0:3] == ["dotnet", "nuget", "push"]
            and candidate["argv"][4:] == ["--source", KIT_ENDPOINT, "--api-key", "<credential omitted>"],
            "original Kit repeat candidate or push route differs")
    require(admission.authorize(manifest["contentId"], effect.identity, "dispatch", effect.request_digest),
            "original Kit repeat fresh dispatch admission denied")
    require(sum(ledger_api.attempted.values()) < AUTHORITY_CALL_CEILING,
            "original Kit repeat Authority ceiling exhausted before send")
    require(clock() < deadline, "original Kit repeat observation deadline expired")
    record = {"schema": "fsgg.original-kit-repeat-attempt/1", "profile": KIT_REPEAT_PROFILE,
              "execution": report.get("execution"), "originalPublisher": report.get("originalPublisher"),
              "candidate": report.get("candidate"), "effect": candidate,
              "journal": {"logicalHead": observed.head, "physicalHead": physical, "generation": 14,
                          "canonicalStateSha256": hashlib.sha256(canonical(observed.state)).hexdigest()},
              "absentObservation": recorded.observations[-1], "originalIntentRetained": True,
              "historicalNonDispatchProved": False, "globalOnceClaim": False,
              "permissionScope": "one dispatch in this explicitly selected invocation only",
              "providerOutcome": "not yet delegated; any prior send remains uncertain"}
    report["stage"] = "repeat-presend-record"
    write_kit_repeat_attempt(attempt_path, record)
    report["kitRepeat"]["attemptRecord"] = {"path": str(attempt_path),
                                             "sha256": hashlib.sha256(attempt_path.read_bytes()).hexdigest()}
    require(verify_candidate() == candidate, "original Kit repeat local candidate changed before send")
    require(sum(ledger_api.attempted.values()) < AUTHORITY_CALL_CEILING and clock() < deadline,
            "original Kit repeat budget or deadline exhausted before delegation")
    report["kitRepeat"]["branch"] = "one-explicit-repeat"
    report["stage"] = "repeat-provider-dispatch"
    recorded.repeat_enabled = True
    outcome = recorded.dispatch(effect)
    require(outcome.state == "applied", "original Kit repeat outcome unconfirmed; reconcile without retry")
    report["lastSuccessfulStage"] = "repeat-provider-dispatch"
    return "checkpoint"  # No settlement, final reread, other effect or second send.


def authenticate_runtime_refusal(api):
    """Authenticate the fixed original refusal, never caller-provided JSON."""
    run = api.runtime_recovery_json(f"repos/{REPOSITORY}/actions/runs/38063768172")
    require(run.get("id") == 38063768172 and run.get("workflow_id") == 362181999
            and run.get("run_attempt") == 1 and run.get("head_sha") == RUNTIME_REFUSAL_SOURCE
            and run.get("path") == ".github/workflows/release-successor-publish.yml"
            and run.get("head_branch") == "main" and run.get("event") == "workflow_dispatch"
            and run.get("actor", {}).get("login") == "EHotwagner"
            and run.get("repository", {}).get("id") == 1269292704
            and run.get("repository", {}).get("full_name") == REPOSITORY
            and run.get("status") == "completed" and run.get("conclusion") == "failure",
            "original runtime refusal run identity differs")
    artifact = api.runtime_recovery_json(f"repos/{REPOSITORY}/actions/artifacts/11675095562")
    require(artifact.get("id") == 11675095562 and artifact.get("name") == "release-successor-result"
            and artifact.get("size_in_bytes") == 1413 and artifact.get("expired") is False
            and artifact.get("digest") == "sha256:" + RUNTIME_REFUSAL_ZIP_SHA256
            and artifact.get("workflow_run", {}).get("id") == 38063768172
            and artifact.get("workflow_run", {}).get("head_sha") == RUNTIME_REFUSAL_SOURCE,
            "original runtime refusal artifact identity differs")
    expiry = datetime.datetime.fromisoformat(artifact["expires_at"].replace("Z", "+00:00"))
    require(expiry.tzinfo is not None and expiry > datetime.datetime.now(datetime.timezone.utc),
            "original runtime refusal artifact expiry passed")
    raw = api.download_runtime_refusal_artifact()
    require(len(raw) == 1413 and hashlib.sha256(raw).hexdigest() == RUNTIME_REFUSAL_ZIP_SHA256,
            "original runtime refusal archive differs")
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        entries = archive.infolist()
        require(len(entries) == 1 and entries[0].filename == "release-successor-result.json"
                and not entries[0].flag_bits & 1 and not entries[0].is_dir()
                and stat.S_IFMT(entries[0].external_attr >> 16) in (0, stat.S_IFREG)
                and 0 < entries[0].file_size <= 16384, "original runtime refusal member differs")
        body = archive.read(entries[0])
    require(len(body) <= 16384 and hashlib.sha256(body).hexdigest() == RUNTIME_REFUSAL_MEMBER_SHA256,
            "original runtime refusal member bytes differ")
    def unique(items):
        value = {}
        for key, item in items:
            require(key not in value, "original runtime refusal duplicate JSON key")
            value[key] = item
        return value
    value = json.loads(body, object_pairs_hook=unique,
                       parse_constant=lambda _: (_ for _ in ()).throw(Refused("nonfinite runtime refusal JSON")))
    require(value.get("schema") == "fsgg.release-successor-result/1" and value.get("profile") == RECOVERY_PROFILE
            and value.get("execution") == {"operator": "EHotwagner", "runAttempt": "1", "runId": 38063768172,
                                           "sourceSha": RUNTIME_REFUSAL_SOURCE}
            and value.get("candidate") == {**RECOVERY_CANDIDATE, "version": "0.101.0"}
            and value.get("originalPublisher") == {"runId": 38029395937, "runAttempt": 1, "jobId": 114146971214}
            and value.get("disposition") == "failed-or-unknown" and value.get("publicationComplete") is False
            and value.get("stage") == "advance" and value.get("iterationsAttempted") == 3
            and value.get("journalRef") == REF and value.get("journalMutationMayHaveOccurred") is True
            and value.get("lastDispatch") == {"attempted": True, "effect": "qualification-asset:evidence", "state": "applied"}
            and value.get("failure") == {"chain": [{"message": "qualification-asset:runtime: dispatch admission denied",
                                                       "messageTruncated": False, "type": "Refused"}], "chainIncomplete": False}
            and value.get("lastObservedJournal", {}).get("generation") == 26
            and value["lastObservedJournal"].get("logicalHead") == ORIGINAL_RUNTIME_HEAD,
            "original runtime refusal semantic binding differs")
    return {"runId": 38063768172, "runAttempt": 1, "sourceSha": RUNTIME_REFUSAL_SOURCE,
            "artifactId": 11675095562, "archiveSha256": RUNTIME_REFUSAL_ZIP_SHA256,
            "memberSha256": RUNTIME_REFUSAL_MEMBER_SHA256, "thisInvocationRuntimeDispatchEntered": False,
            "historicalNonDispatchProved": False, "globalOnceClaim": False}


def validate_original_runtime_state(manifest, journal):
    validate_checkpoint_state(manifest, journal)
    effects = ordered_effects(manifest)
    expected = {effect.identity: "verified" for effect in effects[:12]}
    expected[RUNTIME_EFFECT] = "intent"
    observed = journal._observed
    require(observed.head == ORIGINAL_RUNTIME_HEAD and observed.state["generation"] == 26
            and hashlib.sha256(canonical(observed.state)).hexdigest() == ORIGINAL_RUNTIME_STATE_SHA256
            and observed.state["effects"] == expected and effects[12].identity == RUNTIME_EFFECT
            and observed.state["contentId"] == manifest["contentId"],
            "runtime recovery requires the exact original generation26 intent")
    return effects[12]


def verify_runtime_candidate(manifest, provider, manifest_bytes, archive):
    require(provider.manifest_path.read_bytes() == manifest_bytes and provider.manifest == manifest
            and provider.content_id == RECOVERY_CANDIDATE["contentId"]
            and provider.source == RECOVERY_CANDIDATE["sourceSha"] and provider.version == "0.101.0"
            and not archive.is_symlink() and stat.S_ISREG(archive.stat().st_mode)
            and hashlib.sha256(archive.read_bytes()).hexdigest() == RECOVERY_CANDIDATE["archiveSha256"],
            "runtime recovery original candidate drift")
    path = provider.root / RUNTIME_NAME
    qualification = manifest["descriptor"]["standaloneTelemetry"]
    require(qualification["qualificationPath"] == RUNTIME_NAME and not path.is_symlink()
            and path.resolve().parent == provider.root.resolve() and stat.S_ISREG(path.stat().st_mode)
            and 0 < path.stat().st_size <= 1048576
            and hashlib.sha256(path.read_bytes()).hexdigest() == qualification["qualificationSha256"],
            "runtime recovery original payload drift")
    info = path.stat()
    return {"effect": RUNTIME_EFFECT, "name": RUNTIME_NAME, "payloadSha256": qualification["qualificationSha256"],
            "localFile": {"device": info.st_dev, "inode": info.st_ino, "size": info.st_size, "mtimeNs": info.st_mtime_ns}}


class RuntimeRecoveryProvider(ReportingProvider):
    def __init__(self, provider, report, effect):
        super().__init__(provider, report)
        self.effect, self.witness = effect, None
        self.send_enabled = self.send_consumed = False

    def observe(self, effect):
        if effect.identity != RUNTIME_EFFECT:
            return self.provider.observe(effect)
        value, self.witness = self.provider.observe_runtime_recovery(effect)
        self.report["runtimeRecovery"]["targetObservation"] = {"state": value.state, "witness": self.witness}
        return value

    def dispatch(self, effect):
        require(self.send_enabled and not self.send_consumed and effect == self.effect,
                "runtime recovery send is not admitted")
        self.send_consumed = True  # Any exception consumes this invocation's permission.
        self.report["lastDispatch"] = {"effect": effect.identity, "attempted": True, "state": "unknown"}
        result = self.provider.dispatch_runtime_recovery(effect, self.witness)
        self.report["lastDispatch"]["state"] = result.state
        return result


class RuntimeDeadlineAdmission:
    """Keep composite admission and refuse expiry both sides of its reads."""
    def __init__(self, admission, deadline, clock, *, candidate_check=None):
        self.admission, self.deadline, self.clock = admission, deadline, clock
        self.candidate_check = candidate_check

    def authorize(self, *args):
        require(self.clock() < self.deadline, "runtime recovery deadline expired before admission")
        if self.candidate_check is not None:
            self.candidate_check()
        result = self.admission.authorize(*args)
        require(self.clock() < self.deadline, "runtime recovery deadline expired during admission")
        return result


class RuntimeDeadlineAuthority:
    """Selected counter still owns calls; fence every underlying mutation."""
    def __init__(self, api, deadline, clock):
        self.api, self.deadline, self.clock = api, deadline, clock

    def get(self, *args):
        return self.api.get(*args)

    def post(self, *args):
        require(self.clock() < self.deadline, "runtime recovery deadline expired before Authority POST")
        return self.api.post(*args)

    def patch(self, *args):
        require(self.clock() < self.deadline, "runtime recovery deadline expired before Authority PATCH")
        return self.api.patch(*args)


def run_original_runtime_recovery(manifest, journal, admission, provider, ledger_api, report, deadline,
                                  proof, verify_candidate, attempt_path, clock=time.monotonic):
    effect = validate_original_runtime_state(manifest, journal)
    admission = RuntimeDeadlineAdmission(admission, deadline, clock)
    ledger_api.api = RuntimeDeadlineAuthority(ledger_api.api, deadline, clock)
    observed, physical = journal._observed, journal._physical_head
    require(proof == {"runId": 38063768172, "runAttempt": 1, "sourceSha": RUNTIME_REFUSAL_SOURCE,
                      "artifactId": 11675095562, "archiveSha256": RUNTIME_REFUSAL_ZIP_SHA256,
                      "memberSha256": RUNTIME_REFUSAL_MEMBER_SHA256, "thisInvocationRuntimeDispatchEntered": False,
                      "historicalNonDispatchProved": False, "globalOnceClaim": False},
            "runtime recovery authenticated refusal proof differs")
    report["runtimeRecovery"] = {"profile": RUNTIME_RECOVERY_PROFILE, "originalRefusal": proof,
                                 "maxDispatchesThisInvocation": 1, "globalOnceClaim": False,
                                 "historicalNonDispatchProved": False, "branch": "ordinary-observation"}
    guarded = UnchangedKitJournal(journal, observed, physical)  # Same exact logical/physical read fence.
    recorded = RuntimeRecoveryProvider(provider, report, effect)
    candidate = verify_candidate()  # Required even for the zero-send matched branch.
    require(clock() < deadline, "runtime recovery deadline expired")
    report["iterationsAttempted"] = 1
    result = advance(manifest, guarded, admission, recorded)
    report["lastAdvanceResult"] = result
    report["lastSuccessfulStage"] = "advance"
    require(clock() < deadline, "runtime recovery deadline expired")
    if result == "verified":
        report["runtimeRecovery"]["branch"] = "matched-settlement-no-send"
        return "checkpoint"  # Normal CAS26->27, no other effect.
    require(result == "waiting" and report["runtimeRecovery"]["targetObservation"]["state"] == "absent",
            "runtime recovery lacks a complete fresh absence witness")
    witness = recorded.witness
    guarded.read()  # Full original history, exact selected state and paired physical fence.
    validate_original_runtime_state(manifest, journal)
    require(verify_candidate() == candidate, "runtime recovery payload changed during observation")
    require(recorded.observe(effect).state == "absent" and recorded.witness == witness,
            "runtime recovery release or asset population moved")
    require(clock() < deadline, "runtime recovery deadline expired")
    record = {"schema": "fsgg.original-runtime-recovery-attempt/1", "profile": RUNTIME_RECOVERY_PROFILE,
              "execution": report.get("execution"), "originalPublisher": report.get("originalPublisher"),
              "originalRefusal": proof, "candidate": report.get("candidate"), "effect": candidate,
              "journal": {"logicalHead": observed.head, "physicalHead": physical, "generation": 26,
                          "canonicalStateSha256": ORIGINAL_RUNTIME_STATE_SHA256},
              "absenceWitness": witness, "originalIntentRetained": True, "globalOnceClaim": False,
              "permissionScope": "one fixed runtime upload in this explicitly admitted invocation only",
              "providerOutcome": "not delegated; prior operations require root reconciliation"}
    report["stage"] = "runtime-presend-record"
    write_kit_repeat_attempt(attempt_path, record)  # Existing exclusive fsynced record mechanism.
    report["runtimeRecovery"]["attemptRecord"] = {"path": str(attempt_path),
                                                 "sha256": hashlib.sha256(attempt_path.read_bytes()).hexdigest()}
    require(admission.authorize(manifest["contentId"], effect.identity, "dispatch", effect.request_digest),
            "runtime recovery fresh dispatch admission denied")
    require(authority_main(ledger_api) == physical, "runtime recovery physical head moved before upload")
    require(verify_candidate() == candidate, "runtime recovery payload changed before upload")
    require(sum(ledger_api.attempted.values()) < AUTHORITY_CALL_CEILING and clock() < deadline
            and ledger_api.quota is not None and ledger_api.quota["reset"] > time.time(),
            "runtime recovery quota, counter or deadline expired before upload")
    report["runtimeRecovery"]["branch"] = "one-explicit-runtime-upload"
    report["stage"] = "runtime-provider-dispatch"
    recorded.send_enabled = True
    outcome = recorded.dispatch(effect)
    require(outcome.state == "applied", "runtime recovery upload unconfirmed; reconcile without retry")
    report["lastSuccessfulStage"] = "runtime-provider-dispatch"
    return "checkpoint"  # Still26/intent: no final read, settlement or later effect.


def execute_publisher(args, report):
    version, predecessor, selected_ref = selected_release_binding(args)
    continuation = bool(getattr(args, "continue_original_0104_checkpoint", False))
    window = continuation_window() if continuation else None
    if continuation:
        report["cumulativeAuthorityCeiling"] = continuation_policy.CONTINUATION_PROOF["originalAuthorityEntries"] + window["authorityEntries"]
    forward_deadline = time.monotonic() + (window["seconds"] if continuation else 45 * 60)
    original_0103 = validate_original_0103_request(args)
    original_tag_recovery = bool(getattr(args, "recover_original_0103_tag_intent", False))
    runtime_recovery = getattr(args, "recover_original_runtime_intent", False)
    runtime_deadline = time.monotonic() + 45 * 60 if runtime_recovery else None
    report["stage"] = "publisher-context"
    publisher_sha = os.environ["GITHUB_SHA"]
    operator = os.environ["GITHUB_ACTOR"]
    run_id = int(os.environ["GITHUB_RUN_ID"])
    require(os.environ.get("GITHUB_EVENT_NAME") == "workflow_dispatch", "publisher event differs")
    require(os.environ.get("GITHUB_REPOSITORY") == REPOSITORY, "publisher repository differs")
    require(os.environ.get("GITHUB_REF") == "refs/heads/main", "publisher branch differs")
    require(os.environ.get("GITHUB_RUN_ATTEMPT") == "1", "publisher rerun is not admitted")
    successor_checkpoint = bool(getattr(args, "recover_original_successor_checkpoint", False))
    expected_checkpoint = getattr(args, "expected_checkpoint", None)
    require(not successor_checkpoint or args.publish, "original successor checkpoint requires publish")
    require((successor_checkpoint or continuation) == bool(expected_checkpoint), "expected checkpoint requires the selected exact recovery")
    if continuation:
        require(args.publish and not args.preflight_only, ".104 continuation requires explicit publish")
        require(continuation_checkpoint(expected_checkpoint) == continuation_policy.CONTINUATION_CHECKPOINT
                and (args.candidate_run_id, args.candidate_artifact_id, args.candidate_archive_sha256) ==
                tuple(continuation_policy.CONTINUATION_CANDIDATE[key] for key in ("runId", "artifactId", "archiveSha256")),
                "original .104 continuation checkpoint/candidate tuple differs")
    if successor_checkpoint:
        require((args.candidate_run_id, args.candidate_artifact_id, args.candidate_archive_sha256) ==
                (SUCCESSOR_CHECKPOINT_CANDIDATE["runId"], SUCCESSOR_CHECKPOINT_CANDIDATE["artifactId"], SUCCESSOR_CHECKPOINT_CANDIDATE["archiveSha256"]),
                "original successor candidate tuple differs")
    require(operator == "EHotwagner", "publisher operator differs")
    require(len(args.candidate_archive_sha256) == 64, "candidate archive digest is malformed")
    if args.recovery_checkpoint or args.repeat_original_nuget_kit_intent or runtime_recovery:
        require(args.publish and not args.preflight_only, "recovery checkpoint requires publish, never preflight")
        require((args.candidate_run_id, args.candidate_artifact_id, args.candidate_archive_sha256) ==
                (RECOVERY_CANDIDATE["runId"], RECOVERY_CANDIDATE["artifactId"], RECOVERY_CANDIDATE["archiveSha256"]),
                "fixed recovery original candidate tuple differs")
    report["execution"] = {"sourceSha": publisher_sha, "runId": run_id,
                           "runAttempt": os.environ["GITHUB_RUN_ATTEMPT"], "operator": operator}
    report["lastSuccessfulStage"] = "publisher-context"
    continuation_ledger = None
    if continuation:
        # This execute process counts startup, cold history and every later edge
        # together. The hosted pre-mint reader is a separate process, not a reset
        # or a silently shared counter. No credential acquisition happens here.
        continuation_ledger = CountedAuthorityAPI(
            RuntimeDeadlineAuthority(GitHubAPI(os.environ["ORDINARY_LEDGER_TOKEN"]), forward_deadline, time.monotonic),
            ceiling=window["authorityEntries"], quota_floor=window["quotaFloor"])
        report["authorityCounter"] = continuation_ledger
        continuation_ledger.admit_quota()
        precredential_release_check(args, continuation_ledger)
        continuation_phase(report, "startup", continuation_ledger)
    elif selected_ref == SUSPENDED_REF:
        # Preserve original/default refusal before reading publication credentials.
        precredential_release_check(args, GitHubAPI(os.environ["GH_TOKEN"]))
    elif not any((args.recovery_checkpoint, args.repeat_original_nuget_kit_intent, runtime_recovery, successor_checkpoint)):
        continuation_ledger = CountedAuthorityAPI(
            RuntimeDeadlineAuthority(GitHubAPI(os.environ["ORDINARY_LEDGER_TOKEN"]), forward_deadline, time.monotonic),
            ceiling=ORDINARY_AUTHORITY_CALL_CEILING, quota_floor=ORDINARY_AUTHORITY_QUOTA_FLOOR)
        report["authorityCounter"] = continuation_ledger
        continuation_ledger.admit_quota()
        if selected_ref in {SUSPENDED_REF, ALLOWED_SUCCESSOR["logicalRef"]}:
            precredential_release_check(args, continuation_ledger)
    require(not args.workdir.exists(), "candidate workdir already exists")
    args.workdir.mkdir(parents=True)

    github_token = os.environ["GH_TOKEN"]
    ledger_token = os.environ["ORDINARY_LEDGER_TOKEN"]
    nuget_key = "" if continuation else os.environ["NUGET_API_KEY"]
    api = GitHubAPI(github_token)
    if continuation:
        api = ContinuationPublisherAPI(api, forward_deadline, window)
        report["publisherCounter"] = api
    if original_tag_recovery:
        api = OriginalTagPublisherAPI(api, forward_deadline)
        report["publisherCounter"] = api
    if runtime_recovery:
        api = RuntimePublisherAPI(api)
        report["publisherCounter"] = api
    report["stage"] = "candidate-authentication"
    artifact = api.get(f"repos/{REPOSITORY}/actions/artifacts/{args.candidate_artifact_id}")
    run = api.get(f"repos/{REPOSITORY}/actions/runs/{args.candidate_run_id}")
    if original_0103:
        validate_original_0103_metadata(run, artifact)
    candidate_source = run.get("head_sha")
    require(isinstance(candidate_source, str) and len(candidate_source) == 40, "candidate source is malformed")
    require(artifact.get("digest") == "sha256:" + args.candidate_archive_sha256, "candidate artifact digest differs")
    artifact_json = args.workdir / "artifact.json"
    run_json = args.workdir / "run.json"
    archive = args.workdir / "candidate.zip"
    artifact_json.write_text(json.dumps(artifact))
    run_json.write_text(json.dumps(run))
    with archive.open("xb") as stream:
        if original_tag_recovery:
            api.claim("candidateArchive")
        if continuation:
            raw = api.download_continuation_candidate()
            require(len(raw) == 33326837 and hashlib.sha256(raw).hexdigest() == continuation_policy.CONTINUATION_CANDIDATE["archiveSha256"],
                    "original .104 candidate ZIP differs")
            stream.write(raw)
        else:
            subprocess.run(
                ["gh", "api", f"repos/{REPOSITORY}/actions/artifacts/{args.candidate_artifact_id}/zip"],
                stdout=stream, check=True,
            )
    candidate = args.workdir / "candidate"
    verifier = pathlib.Path(__file__).with_name("release-successor-artifact.py")
    subprocess.run(
        [sys.executable, str(verifier), "--artifact-json", str(artifact_json), "--run-json", str(run_json),
         "--archive", str(archive), "--source-sha", candidate_source, "--output", str(candidate)]
        + (["--suspended-0103-successor"] if getattr(args, "suspended_0103_successor", False) or continuation else []),
        check=True,
    )
    manifest_path = candidate / "release-manifest.json"
    manifest_bytes = manifest_path.read_bytes()
    manifest = json.loads(manifest_bytes)
    require(manifest["descriptor"]["version"] == version, "publisher version differs")
    if selected_ref == CURRENT_RELEASE["logicalRef"]:
        require(manifest["descriptor"].get("previousStableVersion") == predecessor, "publisher predecessor differs")
        previous = json.loads((candidate / "previous-stable-channel.json").read_bytes())
        require(previous.get("version") == CURRENT_RELEASE["predecessor"]
                and previous.get("sourceSha") == CURRENT_RELEASE["predecessorSource"]
                and previous.get("contentId") == CURRENT_RELEASE["predecessorContentId"]
                and manifest["descriptor"].get("previousStableContentId") == CURRENT_RELEASE["predecessorContentId"],
                "current accepted predecessor identity differs")
    if args.recovery_checkpoint or args.repeat_original_nuget_kit_intent or runtime_recovery:
        require(candidate_source == RECOVERY_CANDIDATE["sourceSha"]
                and manifest["contentId"] == RECOVERY_CANDIDATE["contentId"],
                "fixed recovery original source or content differs")
    original_selection = None
    if original_0103:
        report["stage"] = "original-0103-compatibility"
        report["originalCandidateCompatibility"] = {"profile": ORIGINAL_0103_PROFILE,
            "candidateSourceSha": candidate_source, "publisherSha": publisher_sha,
            "supportingDependencies": len(ORIGINAL_0103_DEPENDENCIES), "verified": False}
        original_selection = verify_original_0103(args, run, artifact, manifest_bytes, archive, candidate, publisher_sha, api)
        report["originalCandidateCompatibility"]["verified"] = True
    report["candidate"].update(sourceSha=candidate_source, contentId=manifest["contentId"], version=version)
    report["lastSuccessfulStage"] = "candidate-authentication"
    publisher_admission = SingleOperatorAdmission(api, manifest, publisher_sha, run_id, operator, "refs/heads/main")
    provider = LiveProvider(api, manifest_path, github_token, nuget_key)
    if selected_ref == CURRENT_RELEASE["logicalRef"]:
        provider._ordinary_mode = True
    runtime_proof = None
    if runtime_recovery:
        report["stage"] = "runtime-original-refusal-authentication"
        runtime_proof = authenticate_runtime_refusal(api)
        report["runtimeOriginalRefusal"] = runtime_proof
        require(time.monotonic() < runtime_deadline, "runtime recovery deadline expired")
    intent = {
        "contentId": manifest["contentId"],
        "sourceSha": candidate_source,
        "version": version,
        "candidateArchiveSha256": args.candidate_archive_sha256,
        "operator": operator,
    }
    ledger_api = continuation_ledger if continuation_ledger is not None else GitHubAPI(ledger_token)
    historical_recovery = args.recovery_checkpoint or args.repeat_original_nuget_kit_intent or runtime_recovery
    # Ordinary budget is independently calibrated; it was not enforced by old runs.
    if not historical_recovery and not continuation and continuation_ledger is None:
        ledger_api = CountedAuthorityAPI(ledger_api, ceiling=ORDINARY_AUTHORITY_CALL_CEILING,
                                         quota_floor=ORDINARY_AUTHORITY_QUOTA_FLOOR)
        report["authorityCounter"] = ledger_api
        report["stage"] = "Authority-quota"
        ledger_api.admit_quota()
        report["lastSuccessfulStage"] = "Authority-quota"
    if historical_recovery:
        ledger_api = CountedAuthorityAPI(ledger_api)
        report["authorityCounter"] = ledger_api
        report["stage"] = "Authority-quota"
        ledger_api.admit_quota()
        report["lastSuccessfulStage"] = "Authority-quota"
    if not historical_recovery and not continuation:
        # The one original forward clock already began before authentication.
        # Guard dependent genesis mutations as well as later advances.
        ledger_api.api = RuntimeDeadlineAuthority(ledger_api.api, forward_deadline, time.monotonic)
    suspension = ReleaseSuspension(ledger_api) if selected_ref in {SUSPENDED_REF, ALLOWED_SUCCESSOR["logicalRef"]} else None
    if suspension is not None:
        suspension.authorize(selected_ref, version)
    provider.suspension = suspension
    provider.logical_ref = selected_ref
    execution = {"runId": run_id, "attempt": 1, "sourceSha": publisher_sha, "operator": operator}
    journal = ProtectedReleaseJournal(ledger_api, ref=selected_ref, main_directory=True, suspension=suspension,
                                      continuation_owner=execution if continuation else None,
                                      ordinary_forward=not continuation and not historical_recovery and not successor_checkpoint
                                          and selected_ref == CURRENT_RELEASE["logicalRef"])
    journal = ReportingJournal(journal, report, detailed=not historical_recovery)
    provider = ReportingProvider(provider, report)
    admission = ProtectedPublisherAdmission(publisher_admission, ledger_api, selected_ref, suspension=suspension)
    if not historical_recovery:
        admission = RuntimeDeadlineAdmission(admission, forward_deadline, time.monotonic)
    def uniqueness():
        subprocess.run(
            [sys.executable, str(pathlib.Path(__file__).with_name("check-release-candidate-uniqueness.py")),
             "--version", version, "--predecessor", predecessor],
            check=True, env={**os.environ, "GITHUB_TOKEN": github_token},
        )
    report["stage"] = "journal-preparation"
    if continuation:
        continuation_phase(report, "candidateAuthenticated", ledger_api, api)
        proof = authenticate_continuation_proof(api)
        report["originalProofAuthenticated"] = True
        continuation_phase(report, "proofAuthenticated", ledger_api, api)
        candidate_pin = continuation_policy.CONTINUATION_CANDIDATE
        require(candidate_source == candidate_pin["sourceSha"] and manifest["contentId"] == candidate_pin["contentId"],
                "original .104 candidate authentication differs")
        comparison = api.get(f"repos/{REPOSITORY}/compare/{candidate_source}...{publisher_sha}")
        require(comparison.get("status") == "ahead" and comparison.get("merge_base_commit", {}).get("sha") == candidate_source,
                "continuation executor does not descend from immutable candidate source")
        require_clean_installer_source(publisher_sha)
        journal.read();journal.validate_intent(intent)
        continuation_phase(report, "coldJournalRead", ledger_api, api)
        expected_files = {}
        with zipfile.ZipFile(archive) as original:
            for entry in original.infolist():
                if entry.filename != "release-manifest.json":expected_files[entry.filename] = hashlib.sha256(original.read(entry)).hexdigest()
        def candidate_check():
            require(hashlib.sha256(archive.read_bytes()).hexdigest() == candidate_pin["archiveSha256"], "original candidate archive changed")
            current = json.loads(manifest_path.read_bytes())
            require(current.get("descriptor") == manifest["descriptor"] and current.get("contentId") == manifest["contentId"], "original descriptor changed")
            require(all(not (candidate/name).is_symlink() and hashlib.sha256((candidate/name).read_bytes()).hexdigest() == digest
                        for name,digest in expected_files.items()), "original candidate members changed")
        return run_0104_continuation(manifest, journal, admission, provider, ledger_api, api, report, forward_deadline,
                    execution, proof, candidate_check, args.result_path.with_name(args.result_path.stem + "-continuation"))
    if prepare_release_journal(journal, ledger_api, admission, provider, api, manifest, intent,
                               candidate_source, publisher_sha, args.preflight_only, uniqueness,
                               recovery_checkpoint=args.recovery_checkpoint or args.repeat_original_nuget_kit_intent or runtime_recovery or successor_checkpoint,
                               original_0103_selection=original_selection, original_tag_recovery=original_tag_recovery):
        print("Release successor preflight passed; no journal, tag, feed or release was changed.")
        return "preflight"
    journal.capture()
    report["lastSuccessfulStage"] = "journal-preparation"
    if original_tag_recovery:
        return run_original_tag_recovery(
            manifest, journal, admission, provider, ledger_api, api, report, forward_deadline,
            lambda: verify_original_tag_candidate(manifest, provider, manifest_bytes, archive),
            args.result_path.with_name(args.result_path.stem + "-original-tag-attempt.json"),
            preflight=args.preflight_only)
    if successor_checkpoint:
        validate_successor_checkpoint(manifest, journal, json.loads(expected_checkpoint))
    deadline = time.monotonic() + 45 * 60
    if runtime_recovery:
        return run_original_runtime_recovery(
            manifest, journal, admission, provider, ledger_api, report, runtime_deadline, runtime_proof,
            lambda: verify_runtime_candidate(manifest, provider, manifest_bytes, archive),
            args.result_path.with_name(args.result_path.stem + "-runtime-recovery-attempt.json"))
    if args.repeat_original_nuget_kit_intent:
        return run_original_kit_repeat(
            manifest, journal, admission, provider, ledger_api, report, deadline,
            lambda: verify_kit_repeat_candidate(manifest, provider, manifest_bytes),
            args.result_path.with_name(args.result_path.stem + "-kit-repeat-attempt.json"))
    if args.recovery_checkpoint:
        return run_recovery_checkpoint(manifest, journal, admission, provider, report, deadline)
    with zipfile.ZipFile(archive) as original:
        member_digests = {entry.filename: hashlib.sha256(original.read(entry)).hexdigest()
                          for entry in original.infolist() if entry.filename != "release-manifest.json"}
    def candidate_check():
        require(hashlib.sha256(archive.read_bytes()).hexdigest() == args.candidate_archive_sha256,
                "authenticated ordinary candidate archive changed")
        current = json.loads(manifest_path.read_bytes())
        require(current.get("descriptor") == manifest["descriptor"] and current.get("contentId") == manifest["contentId"],
                "authenticated ordinary candidate descriptor changed")
        require(all(not (candidate/name).is_symlink() and hashlib.sha256((candidate/name).read_bytes()).hexdigest() == digest
                    for name, digest in member_digests.items()), "authenticated ordinary candidate members changed")
    return run_forward(manifest, journal, admission, provider, report, forward_deadline,
                       maximum=RECOVERY_ADVANCES if successor_checkpoint else len(ordered_effects(manifest)) * 2 + 120,
                       ordinary=selected_ref == CURRENT_RELEASE["logicalRef"], candidate_check=candidate_check)


def diagnostic(report, message, phase):
    """Diagnostics are additional evidence, never a replacement first cause."""
    try:
        print(message, file=sys.stderr)
    except BaseException as error:
        rows = report.setdefault("additionalReportingFailures", [])
        if len(rows) < 16:
            rows.append({"phase": phase, "type": type(error).__name__})


def authentication_only_main():
    """Select before candidate, credential, journal or publication preparation.

    Native activation requires the accountable App owner, authenticated grants,
    reviewed issuance custody and separate protected-operation admission. Source
    qualification alone does not select that owner or admit credential effects.
    """
    parser = argparse.ArgumentParser(description="Inactive original-tag authentication-only qualification")
    selection = parser.add_mutually_exclusive_group(required=True)
    selection.add_argument("--qualify-original-tag-authentication-only", action="store_true")
    selection.add_argument("--refuse-original-tag-factory-inactive", action="store_true")
    parser.add_argument("--result-path", type=pathlib.Path, required=True)
    args = parser.parse_args()
    report = {"schema": "fsgg.release-successor-result/1",
              "profile": AUTH_PROFILE,
              "disposition": "blocked-authentication-route-inactive", "qualified": False,
              "publicationComplete": False, "credentialAcquisitionAttempted": False,
              "publicationEffects": {"tag": 0, "journal": 0, "feed": 0, "release": 0, "asset": 0, "channel": 0},
              "cleanup": "not-issued", "failure": "accountable-owner-and-native-custody-qualification-required"}
    if args.refuse_original_tag_factory_inactive:
        report.update(profile=ORIGINAL_0103_TAG_PROFILE,
            disposition='blocked-original-tag-factory-native-inactive',
            failure='native-factory-ingress-custody-and-effect-admission-required')
    write_result(args.result_path, report)
    return 1


def precredential_release_check(args, api):
    if getattr(args, "continue_original_0104_checkpoint", False):
        continuation_window()
    version, _, logical_ref = selected_release_binding(args)
    if logical_ref in {SUSPENDED_REF, ALLOWED_SUCCESSOR["logicalRef"]}:
        protection, binding = authority_protection()
        protection(api, binding, [logical_ref])
        ReleaseSuspension(api).authorize(logical_ref, version)


def suspension_check_main():
    parser = argparse.ArgumentParser(description="Read installed release suspension before credential exchange")
    parser.add_argument("--check-release-suspension", action="store_true", required=True)
    profile = parser.add_mutually_exclusive_group()
    for name in ("suspended-0103-successor", "continue-original-0104-checkpoint", "original-0103-candidate", "recover-original-0103-tag-intent",
                 "recovery-checkpoint", "repeat-original-nuget-kit-intent", "recover-original-runtime-intent",
                 "recover-original-successor-checkpoint"):
        profile.add_argument("--" + name, action="store_true")
    args = parser.parse_args()
    try:
        precredential_release_check(args, GitHubAPI(os.environ["GH_TOKEN"]))
    except (RuntimeError, ValueError, KeyError, OSError) as error:
        print(f"release suspension refused before credential exchange: {error}", file=sys.stderr)
        return 1
    return 0




_INSTALL_FLAGS = ("publish", "continue_original_0104_checkpoint", "qualify_original_tag_authentication_only", "suspended_0103_successor",
    "original_0103_candidate", "recover_original_0103_tag_intent", "recovery_checkpoint",
    "repeat_original_nuget_kit_intent", "recover_original_runtime_intent", "recover_original_successor_checkpoint")
_INSTALL_COORDINATES = ("candidate_run_id", "candidate_artifact_id", "candidate_archive_sha256")
_INSTALL_PROFILE = "utel-rel-21/install-suspension/1"
# Two source-selected hosted processes (prepare, apply), each sharing one reader/
# writer budget: at most512 metadata opens in composition, never one cross-process
# counter or a JSON grant. Standalone uses one256 budget. The ordinary1500 counter,
# HTTP60-second timeout and hosted15-minute whole-job deadline remain distinct.
_INSTALL_METADATA_OPEN_LIMITS = {"perProcess": INSTALL_METADATA_REQUEST_CEILING,
                                 "hostedProcesses": 2, "hostedComposed": 2 * INSTALL_METADATA_REQUEST_CEILING}


def validate_workflow_inputs(value):
    keys = set(_INSTALL_FLAGS) | set(_INSTALL_COORDINATES) | {"install_release_suspension", "expected_authority_main", "expected_checkpoint"}
    require(type(value) is dict and set(value) == keys, "workflow input fields differ")
    require(all(type(value[key]) is bool for key in (*_INSTALL_FLAGS, "install_release_suspension")), "workflow boolean inputs differ")
    require(all(type(value[key]) is str for key in (*_INSTALL_COORDINATES, "expected_authority_main", "expected_checkpoint")), "workflow string inputs differ")
    if value["install_release_suspension"]:
        require(not any(value[key] for key in (*_INSTALL_FLAGS, *_INSTALL_COORDINATES, "expected_checkpoint")),
                "install-only selection conflicts with publication inputs")
        require(bool(re.fullmatch(r"[0-9a-f]{40}", value["expected_authority_main"])), "install-only expected Authority parent differs")
        return "install-only"
    require(value["expected_authority_main"] == "", "Authority parent requires install-only selection")
    require(all(re.fullmatch(r"[1-9][0-9]{0,19}", value[key]) for key in _INSTALL_COORDINATES[:2])
            and bool(re.fullmatch(r"[0-9a-f]{64}", value["candidate_archive_sha256"])), "publication candidate coordinates are required")
    require(sum(value[key] for key in _INSTALL_FLAGS[1:]) <= 1, "publication selectors conflict")
    continuation = value["continue_original_0104_checkpoint"]
    require(bool(value["expected_checkpoint"]) == (value["recover_original_successor_checkpoint"] or continuation), "expected checkpoint selection differs")
    if continuation:
        continuation_window()
        require(value["publish"] and continuation_checkpoint(value["expected_checkpoint"]) == continuation_policy.CONTINUATION_CHECKPOINT
                and tuple(value[key] for key in _INSTALL_COORDINATES) == tuple(str(continuation_policy.CONTINUATION_CANDIDATE[key]) for key in ("runId", "artifactId", "archiveSha256")),
                "original .104 continuation inputs differ")
    if value["expected_checkpoint"]:
        expected = json.loads(value["expected_checkpoint"])
        require(type(expected) is dict and set(expected) == {"generation", "logicalHead", "stateSHA256"}
                and type(expected["generation"]) is int and 1 <= expected["generation"] <= 33
                and isinstance(expected["logicalHead"], str) and bool(re.fullmatch(r"[0-9a-f]{40}", expected["logicalHead"]))
                and isinstance(expected["stateSHA256"], str) and bool(re.fullmatch(r"[0-9a-f]{64}", expected["stateSHA256"])),
                "expected checkpoint input is malformed")
    if value["qualify_original_tag_authentication_only"]:
        require(not value["publish"], "inactive authentication selection cannot publish")
    return "publication"


def workflow_inputs():
    raw = os.environ["FSGG_RELEASE_INPUTS"]
    require(len(raw.encode()) <= 16384, "workflow inputs exceed bound")
    def pairs(items):
        value = {}
        for key, item in items:
            require(key not in value, "duplicate workflow input")
            value[key] = item
        return value
    value = json.loads(raw, object_pairs_hook=pairs)
    # Native Actions may omit declared optional strings whose default is empty.
    # Restore only that wire omission; the closed semantic validator and digest
    # still receive all sixteen fields with their original exact types/values.
    if type(value) is dict:
        for key in (*_INSTALL_COORDINATES, "expected_checkpoint", "expected_authority_main"):
            value.setdefault(key, "")
    validate_workflow_inputs(value)
    return value


def require_clean_installer_source(source):
    """Normal source cleanliness, including untracked siblings in the import root."""
    root = pathlib.Path(__file__).resolve().parents[1]
    local = subprocess.run(["git", "rev-parse", "HEAD"], cwd=root, check=True, capture_output=True, text=True, timeout=30).stdout.strip()
    require(local == source, "suspension installer checkout differs")
    subprocess.run(["git", "diff", "--quiet", "HEAD"], cwd=root, check=True, capture_output=True, timeout=30)
    status = subprocess.run(["git", "status", "--porcelain=v1", "--untracked-files=all", "--", "scripts"],
                            cwd=root, check=True, capture_output=True, text=True, timeout=30).stdout
    require(len(status.encode()) <= 16384 and status == "", "suspension installer import-root source is not clean")


def fixed_suspension_record():
    raw = canonical({"schema": SUSPENSION_SCHEMA, "disposition": "suspended", "original": SUSPENDED_ORIGINAL,
                     "allowedSuccessor": ALLOWED_SUCCESSOR, "decision": SUSPENSION_DECISION})
    strict_suspension(raw)
    return raw


def suspension_install_preconditions(api, expected_parent):
    require(bool(re.fullmatch(r"[0-9a-f]{40}", expected_parent)), "suspension expected parent differs")
    protection, binding = authority_protection()
    protection(api, binding, [SUSPENDED_REF, ALLOWED_SUCCESSOR["logicalRef"]])
    reader = ReleaseSuspension(api)
    require(reader.main() == expected_parent, "Authority main moved before suspension installation")
    require(reader.file(expected_parent, SUSPENSION_PATH) is None, "suspension already exists; no replacement or resume")
    reader.original(expected_parent)
    reader.verify_decision()
    require(reader.main() == expected_parent, "Authority main moved during suspension preparation")
    return reader


def hosted_install_context(api):
    source = os.environ["GITHUB_SHA"]
    run = os.environ["GITHUB_RUN_ID"]
    require(bool(re.fullmatch(r"[0-9a-f]{40}", source)) and bool(re.fullmatch(r"[1-9][0-9]{0,19}", run)), "hosted install source/run differs")
    require(os.environ.get("GITHUB_REPOSITORY") == REPOSITORY and os.environ.get("GITHUB_REF") == "refs/heads/main"
            and os.environ.get("GITHUB_EVENT_NAME") == "workflow_dispatch" and os.environ.get("GITHUB_RUN_ATTEMPT") == "1"
            and os.environ.get("GITHUB_ACTOR") == "EHotwagner" and os.environ.get("GITHUB_TRIGGERING_ACTOR") == "EHotwagner",
            "hosted install invocation differs")
    require(publisher_context_authorized(api, source, int(run), hosted_install=True), "hosted install native run/source admission denied")
    require_clean_installer_source(source)
    return {"sourceSha": source, "runId": int(run), "attempt": 1, "operator": "EHotwagner"}


def install_paths():
    root = pathlib.Path(os.environ["RUNNER_TEMP"])
    require(root.is_dir() and not root.is_symlink(), "install invocation directory differs")
    return root / "release-successor-suspension-prepared.json", root / "release-successor-result.json"


def read_install_file(path):
    require(path.is_file() and not path.is_symlink(), "install prepared/result input is not a regular file")
    with path.open("rb") as stream:
        raw = stream.read(16385)
    require(len(raw) <= 16384, "install prepared/result input exceeds bound")
    value = json.loads(raw)
    require(type(value) is dict and raw == canonical(value), "install prepared/result input is not canonical")
    return raw, value


def retain_install_result(path, report):
    body = canonical(report)
    require(len(body) <= 16384, "install-only result exceeds bound")
    temporary = path.with_name(path.name + ".install-updating")
    with temporary.open("xb") as stream:
        os.chmod(temporary, 0o600)
        stream.write(body); stream.flush(); os.fsync(stream.fileno())
    os.replace(temporary, path)
    require(path.read_bytes() == body, "install-only result readback differs")


def hosted_suspension_main():
    parser = argparse.ArgumentParser(description="Selected install-only preparation or protected write")
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--prepare-hosted-release-suspension", action="store_true")
    mode.add_argument("--install-hosted-release-suspension", action="store_true")
    parser.add_argument("--prepared-sha256")
    args = parser.parse_args()
    report = {"schema": "fsgg.release-successor-result/1", "profile": _INSTALL_PROFILE, "publicationComplete": False,
              "disposition": "failed-before-installation", "failure": None, "attemptedRequests": [],
              "metadataOpenLimits": dict(_INSTALL_METADATA_OPEN_LIMITS),
              "credentialCleanup": "existing App action post-step; outcome not independently observed",
              "reportingOutcome": "pending"}
    result_path = None
    result_owned = False
    metadata_budget = MetadataRequestBudget()
    try:
        prepared_path, result_path = install_paths()
        inputs = workflow_inputs()
        require(validate_workflow_inputs(inputs) == "install-only", "hosted install requires explicit install-only selection")
        require(os.environ.get("ORDINARY_LEDGER_APP_ID") == "4882140", "ordinary writer App identity differs")
        require(args.install_hosted_release_suspension == bool(args.prepared_sha256), "prepared digest requires exactly the install stage")
        owner_api = GitHubAPI(os.environ["GH_TOKEN"], metadata_budget=metadata_budget)
        context = hosted_install_context(owner_api)
        raw = fixed_suspension_record()
        prepared = {"schema": "fsgg.release-suspension-prepared/1", "execution": context,
                    "expectedParent": inputs["expected_authority_main"], "record": json.loads(raw),
                    "recordSHA256": hashlib.sha256(raw).hexdigest(), "inputSHA256": hashlib.sha256(canonical(inputs)).hexdigest()}
        prepared_raw = canonical(prepared)
        digest = hashlib.sha256(prepared_raw).hexdigest()
        report.update(execution=context, expectedParent=prepared["expectedParent"], recordSHA256=prepared["recordSHA256"], preparedSHA256=digest)
        if args.prepare_hosted_release_suspension:
            require(not prepared_path.exists() and not result_path.exists(), "install preparation already exists; no renewed invocation")
            suspension_install_preconditions(owner_api, prepared["expectedParent"])
            with prepared_path.open("xb") as stream:
                os.chmod(prepared_path, 0o600); stream.write(prepared_raw); stream.flush(); os.fsync(stream.fileno())
            require(read_install_file(prepared_path)[0] == prepared_raw, "install preparation readback differs")
            report["metadataAcquisition"] = metadata_budget.report()
            report["disposition"] = "install-only-prepared"
            report["reportingOutcome"] = "retained"
            write_result(result_path, report)
            result_owned = True
            with open(os.environ["GITHUB_OUTPUT"], "a") as stream:
                stream.write(f"prepared_sha256={digest}\n")
            return 0
        observed_raw, observed = read_install_file(prepared_path)
        require(bool(re.fullmatch(r"[0-9a-f]{64}", args.prepared_sha256)) and args.prepared_sha256 == digest
                and observed_raw == prepared_raw and observed == prepared, "install prepared source/input/digest binding changed")
        _, prior = read_install_file(result_path)
        prepared_counts = prior.get("metadataAcquisition")
        require(isinstance(prepared_counts, dict) and prepared_counts.get("ceiling") == INSTALL_METADATA_REQUEST_CEILING
                and isinstance(prepared_counts.get("attempted"), dict)
                and set(prepared_counts["attempted"]) == {"GET", "POST", "PATCH"}
                and all(type(value) is int and 0 <= value <= INSTALL_METADATA_REQUEST_CEILING for value in prepared_counts["attempted"].values())
                and sum(prepared_counts["attempted"].values()) <= INSTALL_METADATA_REQUEST_CEILING
                and prepared_counts == {**metadata_budget.report(), "attempted": prepared_counts["attempted"]},
                "install preparation acquisition counts differ")
        require(prior == {**report, "disposition": "install-only-prepared", "reportingOutcome": "retained", "metadataAcquisition": prepared_counts},
                "install original result changed or consumed; no retry")
        result_owned = True
        require(os.environ.get("ACTUAL_INSTALLATION_ID") == "160261608"
                and os.environ.get("EXPECTED_INSTALLATION_ID") == "160261608", "ordinary writer installation identity differs")
        writer = GitHubAPI(os.environ["ORDINARY_LEDGER_TOKEN"], metadata_budget=metadata_budget)
        scope = writer.get("installation/repositories?per_page=100")
        require(isinstance(scope, dict) and scope.get("total_count") == 1 and isinstance(scope.get("repositories"), list)
                and [(row.get("id"), row.get("full_name")) for row in scope["repositories"]]
                    == [(1351660651, JOURNAL_REPOSITORY)], "ordinary writer repository scope differs")
        report["preparationMetadataAcquisition"] = prepared_counts
        report["disposition"] = "install-only-in-progress"
        def guard():
            require(hosted_install_context(owner_api) == context, "hosted install execution changed")
            require(canonical(workflow_inputs()) == canonical(inputs), "hosted install mode changed")
            require(read_install_file(prepared_path)[0] == prepared_raw, "hosted install prepared input changed")
            suspension_install_preconditions(owner_api, prepared["expectedParent"])
        def retain():
            report["metadataAcquisition"] = metadata_budget.report()
            report["reportingOutcome"] = "retained"
            retain_install_result(result_path, report)
        retain()  # Consume this invocation before its first possible mutation.
        installed = install_release_suspension(writer, raw, prepared["expectedParent"], guard=guard, report=report, retain=retain, read_api=owner_api)
        report.update(disposition="install-only-complete", installedAuthorityMain=installed)
        retain()
        return 0
    except (RuntimeError, ValueError, KeyError, TypeError, AttributeError, OSError, subprocess.SubprocessError) as error:
        report["metadataAcquisition"] = metadata_budget.report()
        report["failure"] = error_chain(error)
        report["disposition"] = "install-only-failed-or-unknown" if report["attemptedRequests"] else "failed-before-installation"
        if result_path is not None and (result_owned or not result_path.exists()):
            try:
                report["reportingOutcome"] = "retained"
                retain_install_result(result_path, report)
            except BaseException as reporting:
                print(f"install-only result retention failed: {type(reporting).__name__}", file=sys.stderr)
        print(f"release suspension install-only refused; preserve original operation: {type(error).__name__}: {error}", file=sys.stderr)
        return 1


def workflow_inputs_main():
    try:
        validate_workflow_inputs(workflow_inputs())
        return 0
    except (ValueError, RuntimeError, KeyError, TypeError) as error:
        print(f"release workflow inputs refused before credentials: {error}", file=sys.stderr)
        try:
            _, result_path = install_paths()
            if not result_path.exists():
                write_result(result_path, {"schema": "fsgg.release-successor-result/1", "profile": "workflow-input-validation/1",
                    "publicationComplete": False, "disposition": "failed-before-credentials", "credentialAcquisitionAttempted": False,
                    "failure": error_chain(error)})
        except (OSError, RuntimeError, ValueError, KeyError):
            print("workflow input refusal result unavailable; original diagnostic retained", file=sys.stderr)
        return 1

def suspension_install_main():
    """Separately admitted owner invocation using already held credentials only."""
    parser = argparse.ArgumentParser(description="Install the one source-pinned release suspension")
    parser.add_argument("--install-release-suspension", action="store_true", required=True)
    parser.add_argument("--suspension-file", type=pathlib.Path, required=True)
    parser.add_argument("--expected-authority-main", required=True)
    parser.add_argument("--publisher-source-sha", required=True)
    args = parser.parse_args()
    metadata_budget = MetadataRequestBudget()
    try:
        require(bool(re.fullmatch(r"[0-9a-f]{40}", args.publisher_source_sha)), "installer source SHA differs")
        path = args.suspension_file
        require(path.is_file() and not path.is_symlink(), "suspension input must be a regular file")
        with path.open("rb") as stream:
            raw = stream.read(16385)
        strict_suspension(raw)
        # No credential acquisition occurs here. The operation owner must separately
        # admit custody of the authenticated owner token and ordinary writer token.
        owner_api = GitHubAPI(os.environ["GH_TOKEN"], metadata_budget=metadata_budget)
        owner = owner_api.get("user")
        require(isinstance(owner, dict) and owner.get("login") == "EHotwagner"
                and owner.get("type") == "User", "suspension installer accountable owner differs")
        repository = owner_api.get(f"repos/{REPOSITORY}")
        require(repository.get("id") == 1269292704 and repository.get("full_name") == REPOSITORY,
                "suspension installer source repository differs")
        head = owner_api.get(f"repos/{REPOSITORY}/git/ref/heads/main")
        require(head.get("object", {}).get("type") == "commit"
                and head.get("object", {}).get("sha") == args.publisher_source_sha,
                "suspension installer requires exact current protected source")
        require_clean_installer_source(args.publisher_source_sha)
        writer = GitHubAPI(os.environ["ORDINARY_LEDGER_TOKEN"], metadata_budget=metadata_budget)
        # Refresh the authenticated source beside the protected expected-parent write.
        require(owner_api.get(f"repos/{REPOSITORY}/git/ref/heads/main") == head,
                "suspension installer source moved before installation")
        def guard():
            require(owner_api.get("user") == owner, "suspension installer accountable owner changed")
            require(owner_api.get(f"repos/{REPOSITORY}/git/ref/heads/main") == head, "suspension installer source changed")
            require_clean_installer_source(args.publisher_source_sha)
            suspension_install_preconditions(owner_api, args.expected_authority_main)
        installed = install_release_suspension(writer, raw, args.expected_authority_main, guard=guard, read_api=owner_api)
        print(f"Release-specific suspension installed at Authority main {installed}; original Unknown retained.")
        return 0
    except (RuntimeError, ValueError, KeyError, OSError, subprocess.SubprocessError) as error:
        print(f"release suspension installation refused or uncertain; reconcile without retry: {error}", file=sys.stderr)
        return 1

def install_release_suspension(api, raw, expected_parent, *, guard=None, report=None, retain=None, read_api=None):
    """Existing ordinary protected writer; source preparation grants no installation authority.

    Create exactly one sibling object using an expected-parent, nonforced main update.
    No old state, logical ancestry or unrelated tree entry is edited.
    """
    import base64
    strict_suspension(raw)
    require(bool(re.fullmatch(r"[0-9a-f]{40}", expected_parent)), "suspension expected parent differs")
    # Reuse the authenticated reader qualified before issuance; the scoped writer
    # needs only Authority access, never a new cross-repository permission.
    reader = suspension_install_preconditions(read_api or api, expected_parent)
    def mutate(method, path, body):
        if guard is not None:
            guard()
        if report is not None:
            attempt = {"method": method, "path": path, "bodySHA256": hashlib.sha256(canonical(body)).hexdigest(), "outcome": "unknown"}
            if method == "patch":
                attempt["intendedRefUpdate"] = {"ref": "refs/heads/main", "expectedParent": expected_parent, **body}
            report["attemptedRequests"].append(attempt)
            retain()  # Retain original identity before every possible send.
        response = getattr(api, method)(path, body)
        require(isinstance(response, dict), "suspension writer response is malformed")
        oid = response.get("sha") if method == "post" else response.get("object", {}).get("sha")
        require(isinstance(oid, str) and bool(re.fullmatch(r"[0-9a-f]{40}", oid)), "suspension writer response identity differs")
        if report is not None:
            attempt.update(returnedSHA=oid, outcome="response-received")
            retain()
        return response
    parent = reader.objects._main_commit(expected_parent)
    blob = mutate("post", f"repos/{JOURNAL_REPOSITORY}/git/blobs", {"content": base64.b64encode(raw).decode(), "encoding": "base64"})
    tree = mutate("post", f"repos/{JOURNAL_REPOSITORY}/git/trees", {"base_tree": parent["tree"]["sha"], "tree": [
        {"path": SUSPENSION_PATH, "mode": "100644", "type": "blob", "sha": blob["sha"]}]})
    commit = mutate("post", f"repos/{JOURNAL_REPOSITORY}/git/commits", {"tree": tree["sha"], "parents": [expected_parent],
        "message": "Prospective release-specific suspension; original Unknown retained"})
    require(reader.main() == expected_parent, "Authority main moved before suspension CAS")
    mutate("patch", f"repos/{JOURNAL_REPOSITORY}/git/refs/heads/main", {"sha": commit["sha"], "force": False})
    require(reader.main() == commit["sha"], "suspension installation response uncertain; reconcile without retry")
    reader.read()
    return commit["sha"]


def main() -> int:
    if "--check-workflow-inputs" in sys.argv[1:]:
        require(sys.argv[1:] == ["--check-workflow-inputs"], "workflow input check arguments differ")
        return workflow_inputs_main()
    if any(flag in sys.argv[1:] for flag in ("--prepare-hosted-release-suspension", "--install-hosted-release-suspension")):
        return hosted_suspension_main()
    if "--install-release-suspension" in sys.argv[1:]:
        return suspension_install_main()
    if "--check-release-suspension" in sys.argv[1:]:
        return suspension_check_main()
    if any(flag in sys.argv[1:] for flag in ("--qualify-original-tag-authentication-only",
                                             "--refuse-original-tag-factory-inactive")):
        return authentication_only_main()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidate-run-id", required=True, type=int)
    parser.add_argument("--candidate-artifact-id", required=True, type=int)
    parser.add_argument("--candidate-archive-sha256", required=True)
    parser.add_argument("--workdir", required=True, type=pathlib.Path)
    parser.add_argument("--result-path", type=pathlib.Path)
    profile = parser.add_mutually_exclusive_group()
    profile.add_argument("--suspended-0103-successor", action="store_true",
                         help="Select only literal .104/utel-rel-22 under the installed .103 suspension")
    profile.add_argument("--recover-original-0103-tag-intent", action="store_true",
                         help="Fixed original generation2 tag intent; one matched settlement or approved create, never a later effect")
    profile.add_argument("--original-0103-candidate", action="store_true",
                         help="Select only the accepted original .103 bytes on a compatible publisher")
    profile.add_argument("--recovery-checkpoint", action="store_true",
                        help="Recover only the retained journal19 candidate, with at most four advances")
    profile.add_argument("--repeat-original-nuget-kit-intent", action="store_true",
                         help="Explicitly permit one same-original nuget.org Kit send under retained intent uncertainty")
    profile.add_argument("--recover-original-runtime-intent", action="store_true",
                         help="Recover only the authenticated original generation26 runtime intent; one upload or matched settlement")
    profile.add_argument("--recover-original-successor-checkpoint", action="store_true",
                         help="Continue the exact original .102 journal20 candidate, at most four advances and stop on waiting")
    profile.add_argument("--continue-original-0104-checkpoint", action="store_true", help="One claimed original .104 remainder; no package push")
    parser.add_argument("--expected-checkpoint", help="Exact admitted generation/logicalHead/stateSHA256 JSON for the selected exact continuation")
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--preflight-only", action="store_true")
    mode.add_argument("--publish", action="store_true")
    args = parser.parse_args()
    result_path = args.result_path or args.workdir.with_name(args.workdir.name + "-result.json")
    args.result_path = result_path
    report = {"schema": "fsgg.release-successor-result/1",
              "profile": CONTINUATION_PROFILE if args.continue_original_0104_checkpoint else ALLOWED_SUCCESSOR["profile"] if args.suspended_0103_successor else ORIGINAL_0103_TAG_PROFILE if args.recover_original_0103_tag_intent else ORIGINAL_0103_PROFILE if args.original_0103_candidate else SUCCESSOR_CHECKPOINT_PROFILE if args.recover_original_successor_checkpoint else RUNTIME_RECOVERY_PROFILE if args.recover_original_runtime_intent else KIT_REPEAT_PROFILE if args.repeat_original_nuget_kit_intent else RECOVERY_PROFILE if args.recovery_checkpoint else "ordinary/1",
              "candidate": {"runId": args.candidate_run_id, "artifactId": args.candidate_artifact_id,
                            "archiveSha256": args.candidate_archive_sha256},
              "execution": None, "originalPublisher": {"runId": continuation_policy.CONTINUATION_PROOF["runId"], "runAttempt": 1} if args.continue_original_0104_checkpoint else {"runId": ORIGINAL_0103_TAG_CHECKPOINT["runId"], "runAttempt": 1} if args.recover_original_0103_tag_intent else {"runId": 38133108945, "runAttempt": 1, "jobId": 114451349714} if args.recover_original_successor_checkpoint else {"runId": 38029395937, "runAttempt": 1, "jobId": 114146971214} if args.recovery_checkpoint or args.repeat_original_nuget_kit_intent or args.recover_original_runtime_intent else None,
              "journalRef": selected_release_binding(args)[2], "startingJournal": None, "lastObservedJournal": None,
              "iterationsAttempted": 0, "lastAdvanceResult": None, "lastDispatch": None,
              "journalMutationMayHaveOccurred": False, "stage": "preparation",
              "lastSuccessfulStage": None, "disposition": "failed-or-unknown",
              "publicationComplete": False, "failure": None}
    if args.continue_original_0104_checkpoint:
        report["originalAuthorityEntries"] = continuation_policy.CONTINUATION_PROOF["originalAuthorityEntries"]
        report["originalProofAuthenticated"] = False
        report["cumulativeAuthorityCeiling"] = None
    exit_code = 1
    try:
        require(not result_path.exists() and not result_path.is_symlink(), "publisher result path already exists")
        disposition = execute_publisher(args, report)
        report["disposition"] = disposition
        report["publicationComplete"] = disposition == "complete"
        exit_code = 0
    except AuthorityQuotaWaiting as error:
        report["disposition"] = "checkpoint"
        report["pendingReason"] = ("continuation finite request window unavailable; no permission renewal or automatic resume"
                                   if args.continue_original_0104_checkpoint else
                                   "Authority quota or finite request budget unavailable; fresh bounded observation required")
        report["quotaWaiting"] = error_chain(error)
        exit_code = 0
    except BaseException as error:
        report["failure"] = error_chain(error)
        diagnostic(report, f"release successor refused: {error}", "primary diagnostic")
    counter = report.pop("authorityCounter", None)
    report["authorityRequests"] = counter.report() if counter is not None else None
    if args.continue_original_0104_checkpoint:
        report["continuationAuthorityEntries"] = sum(counter.attempted.values()) if counter is not None else 0
        report["cumulativeAuthorityEntries"] = report["originalAuthorityEntries"] + report["continuationAuthorityEntries"]
    publisher_counter = report.pop("publisherCounter", None)
    if publisher_counter is not None:
        report["publisherRequests"] = publisher_counter.report()
    try:
        write_result(result_path, report)
    except BaseException as reporting_error:
        # Reporting cannot replace the primary cause or normalize a failed effect.
        diagnostic(report, f"release successor result reporting failed: {reporting_error}", "result reporting diagnostic")
        return 1
    output = os.environ.get("GITHUB_OUTPUT")
    if output:
        try:
            with open(output, "a") as stream:
                stream.write(f"disposition={report['disposition']}\n")
                stream.write(f"publication_complete={str(report['publicationComplete']).lower()}\n")
        except BaseException as reporting_error:
            diagnostic(report, f"release successor output reporting failed: {reporting_error}", "output reporting diagnostic")
            return 1
    try:
        print(f"Release successor result: {report['disposition']}; publicationComplete={report['publicationComplete']}")
    except BaseException as reporting_error:
        diagnostic(report, f"release successor summary reporting failed: {reporting_error}", "summary reporting diagnostic")
        return 1
    return exit_code


if __name__ == "__main__":
    raise SystemExit(main())
