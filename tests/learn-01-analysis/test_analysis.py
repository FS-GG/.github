import copy
import base64
import contextlib
import gzip
import hashlib
import importlib.util
import io
import json
import pathlib
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("learn01", ROOT / "tools/learn-01-analysis.py")
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)
CONTRACT = json.loads((ROOT / "policy/learn-01-current-focused-v1.json").read_text())
CORPUS = json.loads((ROOT / "tests/learn-01-analysis/fixtures/synthetic.json").read_text())
OBSERVATIONS = json.loads((ROOT / "tests/learn-01-analysis/fixtures/observations.json").read_text())


def private_envelope(content, workspace="workspace-a", version=2):
    body = copy.deepcopy(content)
    body["workspaceId"] = workspace
    body.setdefault("selection", {"mode": "all", "complete": True})
    if version in {3, 4}:
        body["learningSnapshotSchema"] = f"fsgg.telemetry.learn-item-detail/{version}"
        for index, row in enumerate(body.get("learningObservations", []), 100):
            row.setdefault("content_digest", hashlib.sha256(row["canonical"].encode()).hexdigest())
            row.setdefault("ingest_order", index)
            if version == 4:
                for field in (
                        "receipt_producer", "receipt_stream", "receipt_role", "receipt_grant_id",
                        "receipt_grant_generation", "receipt_key", "receipt_envelope_digest"):
                    row.setdefault(field, None)
    canonical = json.dumps(body, separators=(",", ":"), sort_keys=True).encode()
    return {
        "schema": "fsgg.telemetry.item-detail/2",
        "workspaceId": workspace,
        "revision": hashlib.sha256(canonical).hexdigest(),
        "canonicalSnapshotGzip": base64.b64encode(gzip.compress(canonical)).decode(),
    }


class Learn01ContractTests(unittest.TestCase):
    def native_source_event(self, item="I-001"):
        binding = {
            "schema": "fsgg.telemetry.native-inventory-source-binding/1",
            "producerIdentity": "fsgg-work-roadmap-native-collector/1",
            "capturedAt": "2026-01-01T00:00:01Z",
            "hostSource": "codex-app-server:thread/turns/list",
            "rootInvocationId": f"inv-{item}",
            "invocationId": f"inv-{item}",
            "parentThreadId": f"parent-thread-{item}",
            "threadId": f"thread-{item}",
            "orderedTurnIds": [f"turn-{item}"],
            "revision": 1,
        }
        raw = json.dumps(binding, separators=(",", ":"), sort_keys=True).encode("ascii")
        return {
            "kind": "runtime-native-inventory-source/1", "identity": f"native-source-{item}",
            "itemId": item, "revision": 1, "inventoryId": f"native-{item}",
            "originalItemId": item, "invocationId": f"inv-{item}", "sourceDigest": "b" * 64,
            "sourceBinding": {
                "schema": binding["schema"], "producerIdentity": binding["producerIdentity"],
                "sha256": hashlib.sha256(raw).hexdigest(),
                "bytesBase64": base64.b64encode(raw).decode("ascii"),
            },
        }

    def shared_authority_event(self):
        return {
            "kind": "learn-shared-cost-authority/1", "identity": "shared-authority-I-001",
            "itemId": "I-001", "revision": 1, "nativeCostId": "shared-I-001",
            "sourceInventoryId": "native-I-001", "sourceInvocationId": "inv-I-001",
            "sourceKind": "retained-native-shared-cost-source", "sourceDigest": "b" * 64,
        }

    def shared_allocation_event(self):
        return {
            "kind": "learn-shared-cost-allocation/1", "identity": "shared-allocation-I-001",
            "itemId": "I-001", "revision": 1, "nativeCostId": "shared-I-001",
            "policyId": "learn-01-current-focused-v1", "windowId": "window-2026-01",
            "frozenAt": "2025-12-31T23:59:45Z", "allocationRule": "equal-largest-remainder-v1",
            "allocationRoster": ["I-001"],
        }

    def event_for_item(self, event, item):
        return json.loads(json.dumps(event).replace("I-001", item))

    def complete_v3_content(self):
        events = [copy.deepcopy(event) for event in OBSERVATIONS["events"] if event.get("itemId") == "I-001"]
        events.extend([
            {
                "kind": "learn-accounting-inventory/1", "identity": "accounting-I-001", "itemId": "I-001",
                "revision": 1, "inventoryId": "accounting-v1", "windowId": "window-2026-01",
                "policyId": "learn-01-current-focused-v1", "scope": "whole-original-item",
                "cutoffAt": "2026-02-02T00:00:00Z", "capturedAt": "2025-12-31T23:59:30Z",
                "ciApplicability": "not-applicable", "expectedDispatchIds": ["dispatch-I-001"],
                "expectedSharedCostIds": ["shared-I-001"], "sourceKind": "prospective-independent-roster",
                "sourceDigest": "a" * 64,
            },
            {
                "kind": "runtime-native-inventory/1", "identity": "native-I-001-1", "itemId": "I-001",
                "revision": 1, "inventoryId": "native-I-001", "originalItemId": "I-001",
                "invocationId": "inv-I-001", "page": 1, "pages": 1,
                "expectedTurnIds": ["turn-I-001"], "expectedProvider": "openai",
                "requestedModel": "gpt-fixed", "requestedEffort": "medium",
                "support": "provider-native-final-turn-counters", "followupBaseline": 0,
                "capturedAt": "2026-01-01T00:00:01Z",
                "sourceKind": "provider-capability-and-dispatch-roster", "sourceDigest": "b" * 64,
            },
            {
                "kind": "learn-shared-cost/1", "identity": "shared-I-001-fact", "itemId": "I-001",
                "revision": 1, "nativeCostId": "shared-I-001", "provider": "openai",
                "providerTotalTokens": 100, "allocations": [{"originalItemId": "I-001", "tokens": 100}],
                "sourceKind": "native-shared-cost", "sourceDigest": "b" * 64,
            },
        ])
        return {
            "learningObservations": [
                {"canonical": json.dumps(event, separators=(",", ":"), sort_keys=True)} for event in events
            ],
            "populations": [],
            "admissions": [{
                "identity": "admission-I-001", "item_id": "I-001", "invocation_id": "inv-I-001",
                "requested_model": "gpt-fixed", "requested_effort": "medium", "backend": "codex",
            }],
            "expectedDispatches": [{
                "item_id": "I-001", "dispatch_id": "dispatch-I-001", "relation": "root",
            }],
            "lineage": [{
                "item_id": "I-001", "dispatch_id": "dispatch-I-001", "invocation_id": "inv-I-001",
                "root_invocation_id": "inv-I-001",
            }],
            "usage": [{
                "identity": "usage-I-001", "item_id": "I-001", "invocation_id": "inv-I-001",
                "turn_id": "turn-I-001", "provider": "openai", "requested_model": "gpt-fixed",
                "thread_id": "thread-I-001",
                "observed_model": "gpt-fixed", "requested_effort": "medium", "observed_effort": "medium",
                "total": 100,
            }],
            "terminals": [{"item_id": "I-001", "invocation_id": "inv-I-001"}],
            "runtimeGaps": [], "ciRuns": [], "ciPopulationCoverage": [],
        }

    def protected_v4_fixture(self):
        content = self.complete_v3_content()
        source = self.native_source_event()
        events = [json.loads(row["canonical"]) for row in content["learningObservations"]]
        events.extend([self.shared_allocation_event(), source, self.shared_authority_event()])
        content["learningObservations"] = [
            {"canonical": json.dumps(event, separators=(",", ":"), sort_keys=True)}
            for event in events
        ]
        content["usage"][0]["turn_sequence"] = 1
        envelope = private_envelope(content, version=4)
        decoded = json.loads(gzip.decompress(base64.b64decode(envelope["canonicalSnapshotGzip"])))
        for row in decoded["learningObservations"]:
            event = json.loads(row["canonical"])
            if event["kind"] == "learn-shared-cost-allocation/1":
                row["ingest_order"] = 1
            elif event["kind"] == "runtime-native-inventory-source/1":
                row["ingest_order"] = 300
            elif event["kind"] == "learn-shared-cost-authority/1":
                row["ingest_order"] = 301
            elif event["kind"] == "learn-shared-cost/1":
                row["ingest_order"] = 302
            if event["kind"] in {"runtime-native-inventory-source/1", "learn-shared-cost-authority/1"}:
                row.update({
                    "receipt_producer": "collector", "receipt_stream": "native-inventory",
                    "receipt_role": "native-collector", "receipt_grant_id": "collector-grant",
                    "receipt_grant_generation": 1, "receipt_key": "c" * 64,
                    "receipt_envelope_digest": "d" * 64,
                })
        envelope = private_envelope(decoded, version=4)
        capture = {
            "receiptKey": "c" * 64, "envelopeDigest": "d" * 64,
            "producer": "collector", "stream": "native-inventory", "grantId": "collector-grant",
            "grantGeneration": 1, "events": [source], "turns": [{
                "threadId": "thread-I-001", "turnId": "turn-I-001", "sequence": 1,
                "provider": "openai", "model": "gpt-fixed", "effort": "medium",
                "input": 90, "cachedInput": 0, "output": 10, "reasoning": 0, "total": 100,
            }],
        }
        return envelope, capture

    def run_pre_admission_export(self, exported, root, *, original="I-001",
                                 window="window-2026-01", repository="FS-GG/.github"):
        config = root / "host.json"
        config.write_text("{}")
        config.chmod(0o600)
        host = root / "installed-host-fixture"
        host.write_text("#!/usr/bin/python3\nimport json,sys\n"
                        "assert sys.argv[1:3] == ['export-learning', '--config']\n"
                        + "print(" + repr(json.dumps(exported)) + ")\n")
        host.chmod(0o700)
        pin = hashlib.sha256(host.read_bytes()).hexdigest()
        stdout, stderr = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
            result = MODULE.main([
                str(ROOT / "policy/learn-01-current-focused-v1.json"),
                "--protected-host-executable", str(host), "--protected-host-sha256", pin,
                "--protected-host-config", str(config), "--assess-pre-admission",
                "--original-item", original, "--window", window,
                "--repository", repository,
            ])
        return result, stdout.getvalue(), stderr.getvalue()

    def test_pre_admission_assessment_is_honest_stable_and_non_circular(self):
        empty = {
            "learningObservations": [], "populations": [], "outcomes": [], "admissions": [],
            "expectedDispatches": [], "lineage": [], "usage": [], "terminals": [],
            "runtimeGaps": [], "ciRuns": [], "ciPopulationCoverage": [],
        }
        first = MODULE.assess_pre_admission_owner_evidence(
            CONTRACT, private_envelope(empty, version=4), [], original_item="NEW-001",
            window_id="window-2026-01", repository="FS-GG/.github")
        self.assertEqual("unassigned", first["assignmentState"])
        self.assertFalse(first["operationalReady"])
        self.assertIsNone(first["predicates"]["completeNativeUsageSupport"]["value"])
        self.assertIn("operational-window-authority-unavailable", first["missingOwnerInputs"])
        self.assertIn(
            "prospective-root-and-descendant-native-capability-certification-unavailable",
            first["missingOwnerInputs"],
        )

        unrelated = copy.deepcopy(empty)
        unrelated["learningObservations"] = [{
            "canonical": json.dumps(OBSERVATIONS["events"][3], separators=(",", ":"), sort_keys=True)
        }]
        second = MODULE.assess_pre_admission_owner_evidence(
            CONTRACT, private_envelope(unrelated, version=4), [], original_item="NEW-001",
            window_id="window-2026-01", repository="FS-GG/.github")
        self.assertEqual(first["evidenceDigest"], second["evidenceDigest"])

        changed = copy.deepcopy(empty)
        changed["outcomes"] = [{
            "identity": "outcome-new", "item_id": "NEW-001", "repository": "FS-GG/.github",
            "outcome": "delivered", "head_sha": "a" * 40,
        }]
        third = MODULE.assess_pre_admission_owner_evidence(
            CONTRACT, private_envelope(changed, version=4), [], original_item="NEW-001",
            window_id="window-2026-01", repository="FS-GG/.github")
        self.assertNotEqual(first["evidenceDigest"], third["evidenceDigest"])

    def test_pre_admission_reconciles_capture_but_does_not_promote_candidate_records(self):
        envelope, capture = self.protected_v4_fixture()
        result = MODULE.assess_pre_admission_owner_evidence(
            CONTRACT, envelope, [capture], original_item="I-001",
            window_id="window-2026-01", repository="FS-GG/.github")
        self.assertEqual(1, result["selectedEvidence"]["reconciledProtectedNativeCaptures"])
        self.assertEqual(
            {"status": "scope-unknown", "verifiedInvocations": [],
             "scopeUnknownInvocations": ["inv-I-001"]},
            result["predicates"]["postOutcomeProtectedNativeCapture"],
        )
        self.assertEqual(1, result["predicates"]["independentProspectiveDispatchCensus"]["retainedCandidateRecords"])
        self.assertEqual(1, result["predicates"]["independentProspectiveSharedAllocation"]["retainedCandidateRecords"])
        self.assertEqual("missing", result["predicates"]["independentProspectiveDispatchCensus"]["status"])
        self.assertEqual("missing", result["predicates"]["independentProspectiveSharedAllocation"]["status"])
        self.assertFalse(result["operationalReady"])

        with tempfile.TemporaryDirectory(prefix="learn-owner-capture-") as directory:
            root = pathlib.Path(directory)
            config = root / "host.json"
            config.write_text("{}")
            config.chmod(0o600)
            exported = {"schema": "fsgg.telemetry.protected-learning-export/1",
                        "snapshot": envelope, "captures": [capture]}
            host = root / "installed-host-fixture"
            host.write_text("#!/usr/bin/python3\nimport json,sys\n"
                            "assert sys.argv[1:3] == ['export-learning', '--config']\n"
                            + "print(" + repr(json.dumps(exported)) + ")\n")
            host.chmod(0o700)
            pin = hashlib.sha256(host.read_bytes()).hexdigest()
            output = root / "assessment.json"
            self.assertEqual(0, MODULE.main([
                str(ROOT / "policy/learn-01-current-focused-v1.json"),
                "--protected-host-executable", str(host), "--protected-host-sha256", pin,
                "--protected-host-config", str(config), "--assess-pre-admission",
                "--original-item", "I-001", "--window", "window-2026-01",
                "--repository", "FS-GG/.github", "--output", str(output),
            ]))
            self.assertEqual(
                1, json.loads(output.read_text())["selectedEvidence"]["reconciledProtectedNativeCaptures"])

        foreign_envelope = json.loads(gzip.decompress(base64.b64decode(
            envelope["canonicalSnapshotGzip"])))
        foreign_capture = copy.deepcopy(capture)
        foreign_source = copy.deepcopy(foreign_capture["events"][0])
        foreign_source.update({"windowId": "foreign-window", "repository": "FS-GG/foreign"})
        foreign_capture["events"][0] = foreign_source
        for row in foreign_envelope["learningObservations"]:
            if json.loads(row["canonical"])["kind"] == "runtime-native-inventory-source/1":
                row["canonical"] = json.dumps(foreign_source, separators=(",", ":"), sort_keys=True)
                row["content_digest"] = hashlib.sha256(row["canonical"].encode()).hexdigest()
        foreign = MODULE.assess_pre_admission_owner_evidence(
            CONTRACT, private_envelope(foreign_envelope, version=4), [foreign_capture],
            original_item="I-001", window_id="window-2026-01", repository="FS-GG/.github")
        self.assertEqual(0, foreign["selectedEvidence"]["reconciledProtectedNativeCaptures"])
        self.assertEqual("missing", foreign["predicates"]["postOutcomeProtectedNativeCapture"]["status"])

        stale = copy.deepcopy(capture)
        stale["grantGeneration"] = 2
        with self.assertRaisesRegex(MODULE.Refusal, "first authenticated source admission"):
            MODULE.assess_pre_admission_owner_evidence(
                CONTRACT, envelope, [stale], original_item="I-001",
                window_id="window-2026-01", repository="FS-GG/.github")

        truncated = json.loads(gzip.decompress(base64.b64decode(envelope["canonicalSnapshotGzip"])))
        truncated["selection"]["complete"] = False
        with self.assertRaisesRegex(MODULE.Refusal, "selection is incomplete"):
            MODULE.assess_pre_admission_owner_evidence(
                CONTRACT, private_envelope(truncated, version=4), [capture], original_item="I-001",
                window_id="window-2026-01", repository="FS-GG/.github")

    def test_native_delivery_readback_is_verified_without_becoming_readiness_authority(self):
        envelope, native_capture = self.protected_v4_fixture()
        content, _ = MODULE.decode_private_snapshot(envelope)
        candidate = {
            "kind": "native-item-outcome", "identity": "candidate-I-001", "itemId": "I-001",
            "revision": 1, "repository": "FS-GG/.github", "prNumber": 7,
            "baseRef": "main", "baseSha": "d" * 40, "head": "a" * 40,
            "outcome": "delivered", "codeDelivery": "delivered", "mergeCommit": "b" * 40,
            "occurredAt": "2026-01-01T00:00:00Z", "observedAt": "2026-01-01T00:00:01Z",
            "sourceKind": "routine-delivery", "sourceRef": "routine-delivery:I-001",
        }
        candidate_canonical = json.dumps(candidate, separators=(",", ":"), sort_keys=True)
        binding = {
            "schema": "fsgg.telemetry.native-delivery-candidate-binding/1",
            "canonicalFact": candidate_canonical,
            "factDigest": hashlib.sha256(candidate_canonical.encode()).hexdigest(),
            "receiptRole": "generic", "receiptGrantId": None, "receiptGrantGeneration": None,
            "receiptKey": "e" * 64, "receiptEnvelopeDigest": "f" * 64,
        }
        binding_digest = hashlib.sha256(
            json.dumps(binding, separators=(",", ":"), sort_keys=True).encode()).hexdigest()
        response = {
            "number": 7, "state": "closed", "merged": True,
            "head": {"sha": "a" * 40},
            "base": {"ref": "main", "sha": "d" * 40,
                     "repo": {"full_name": "FS-GG/.github"}},
            "merge_commit_sha": "b" * 40, "merged_at": "2026-01-01T00:00:00Z",
        }
        response_body = json.dumps(response, separators=(",", ":"))
        source_digest = hashlib.sha256(response_body.encode()).hexdigest()
        event = {
            "kind": "learn-native-delivery-source/1", "identity": "native-delivery-I-001",
            "itemId": "I-001", "revision": 0, "candidateIdentity": candidate["identity"],
            "candidateSourceRef": candidate["sourceRef"], "candidateDigest": binding_digest,
            "repository": candidate["repository"], "pullRequest": 7,
            "expectedHead": "a" * 40, "observedHead": "a" * 40,
            "baseRef": "main", "baseSha": "d" * 40, "state": "merged",
            "mergeCommit": "b" * 40, "mergedAt": "2026-01-01T00:00:00Z",
            "sourceKind": "github-pull-request-readback", "sourceDigest": source_digest,
            "originalWindowBinding": "unverified",
        }
        canonical = json.dumps(event, separators=(",", ":"), sort_keys=True)
        content["learningObservations"].append({
            "canonical": canonical, "content_digest": hashlib.sha256(canonical.encode()).hexdigest(),
            "ingest_order": 400, "receipt_producer": "collector",
            "receipt_stream": "native-inventory", "receipt_role": "native-collector",
            "receipt_grant_id": "collector-grant", "receipt_grant_generation": 1,
            "receipt_key": "1" * 64, "receipt_envelope_digest": "2" * 64,
        })
        delivery_capture = {
            "receiptKey": "1" * 64, "envelopeDigest": "2" * 64,
            "producer": "collector", "stream": "native-inventory",
            "grantId": "collector-grant", "grantGeneration": 1,
            "candidateBinding": binding, "candidateDigest": binding_digest,
            "responseBody": response_body, "sourceDigest": source_digest, "events": [event],
        }
        result = MODULE.assess_pre_admission_owner_evidence(
            CONTRACT, private_envelope(content, version=4), [native_capture],
            original_item="I-001", window_id="window-2026-01", repository="FS-GG/.github",
            delivery_captures=[delivery_capture])
        self.assertEqual("native-state-verified/original-window-binding-unverified",
                         result["predicates"]["nativeDeliveryIdentity"]["status"])
        self.assertEqual(["merged"], result["predicates"]["nativeDeliveryIdentity"]["states"])
        self.assertFalse(result["operationalReady"])
        self.assertIn("native-delivery-original-window-binding-unavailable",
                      result["missingOwnerInputs"])

        def assess_unmerged(raw_state):
            unmerged_response = copy.deepcopy(response)
            unmerged_response.update({
                "state": raw_state, "merged": False,
                "merge_commit_sha": "e" * 40, "merged_at": None,
            })
            unmerged_body = json.dumps(unmerged_response, separators=(",", ":"))
            unmerged_event = copy.deepcopy(event)
            unmerged_event.update({
                "state": "closed-unmerged" if raw_state == "closed" else "open",
                "mergeCommit": None, "mergedAt": None,
                "sourceDigest": hashlib.sha256(unmerged_body.encode()).hexdigest(),
            })
            unmerged_canonical = json.dumps(unmerged_event, separators=(",", ":"), sort_keys=True)
            unmerged_content = copy.deepcopy(content)
            row = next(row for row in unmerged_content["learningObservations"]
                       if json.loads(row["canonical"]).get("identity") == event["identity"])
            row["canonical"] = unmerged_canonical
            row["content_digest"] = hashlib.sha256(unmerged_canonical.encode()).hexdigest()
            unmerged_capture = copy.deepcopy(delivery_capture)
            unmerged_capture.update({
                "responseBody": unmerged_body,
                "sourceDigest": unmerged_event["sourceDigest"], "events": [unmerged_event],
            })
            return MODULE.assess_pre_admission_owner_evidence(
                CONTRACT, private_envelope(unmerged_content, version=4), [native_capture],
                original_item="I-001", window_id="window-2026-01",
                repository="FS-GG/.github", delivery_captures=[unmerged_capture])

        self.assertEqual(["open"], assess_unmerged("open")["predicates"]["nativeDeliveryIdentity"]["states"])
        self.assertEqual(["closed-unmerged"],
                         assess_unmerged("closed")["predicates"]["nativeDeliveryIdentity"]["states"])

        with tempfile.TemporaryDirectory(prefix="learn-native-delivery-export-") as directory:
            root = pathlib.Path(directory)
            config = root / "host.json"
            config.write_text("{}")
            config.chmod(0o600)
            exported = {
                "schema": "fsgg.telemetry.protected-learning-export/2",
                "snapshot": private_envelope(content, version=4),
                "captures": [native_capture], "deliveryCaptures": [delivery_capture],
            }
            host = root / "installed-host-fixture"
            host.write_text(
                "#!/usr/bin/python3\nimport json,sys\n"
                "assert sys.argv[1] == 'export-learning'\n"
                "assert sys.argv[2] == '--config'\n"
                "assert sys.argv[4:] == ['--include-native-delivery']\n"
                + "print(" + repr(json.dumps(exported)) + ")\n")
            host.chmod(0o700)
            pin = hashlib.sha256(host.read_bytes()).hexdigest()
            output = root / "assessment.json"
            self.assertEqual(0, MODULE.main([
                str(ROOT / "policy/learn-01-current-focused-v1.json"),
                "--protected-host-executable", str(host), "--protected-host-sha256", pin,
                "--protected-host-config", str(config), "--assess-pre-admission",
                "--include-native-delivery", "--original-item", "I-001",
                "--window", "window-2026-01", "--repository", "FS-GG/.github",
                "--output", str(output),
            ]))
            self.assertEqual(
                "native-state-verified/original-window-binding-unverified",
                json.loads(output.read_text())["predicates"]["nativeDeliveryIdentity"]["status"])

        for label, mutate in (
                ("candidate", lambda value: value["candidateBinding"].update({"factDigest": "0" * 64})),
                ("response", lambda value: value.update({"responseBody": "{}"})),
                ("provenance", lambda value: value.update({"grantGeneration": 2}))):
            changed = copy.deepcopy(delivery_capture)
            mutate(changed)
            with self.subTest(label=label), self.assertRaises(MODULE.Refusal):
                MODULE.assess_pre_admission_owner_evidence(
                    CONTRACT, private_envelope(content, version=4), [native_capture],
                    original_item="I-001", window_id="window-2026-01",
                    repository="FS-GG/.github", delivery_captures=[changed])

    def test_pre_admission_cli_requires_direct_pinned_host_export(self):
        empty = {
            "learningObservations": [], "populations": [], "outcomes": [], "admissions": [],
            "expectedDispatches": [], "lineage": [], "usage": [], "terminals": [],
            "runtimeGaps": [], "ciRuns": [], "ciPopulationCoverage": [],
        }
        exported = {"schema": "fsgg.telemetry.protected-learning-export/1",
                    "snapshot": private_envelope(empty, version=4), "captures": []}
        with tempfile.TemporaryDirectory(prefix="learn-pre-admission-") as directory:
            root = pathlib.Path(directory)
            config = root / "host.json"
            config.write_text("{}")
            config.chmod(0o600)
            host = root / "installed-host-fixture"
            host.write_text("#!/usr/bin/python3\nimport json,sys\n"
                            "assert sys.argv[1:3] == ['export-learning', '--config']\n"
                            + "print(" + repr(json.dumps(exported)) + ")\n")
            host.chmod(0o700)
            pin = hashlib.sha256(host.read_bytes()).hexdigest()
            output = root / "assessment.json"
            args = [str(ROOT / "policy/learn-01-current-focused-v1.json"),
                    "--protected-host-executable", str(host), "--protected-host-sha256", pin,
                    "--protected-host-config", str(config), "--assess-pre-admission",
                    "--original-item", "NEW-001", "--window", "window-2026-01",
                    "--repository", "FS-GG/.github", "--output", str(output)]
            self.assertEqual(0, MODULE.main(args))
            self.assertFalse(json.loads(output.read_text())["operationalReady"])
            drifted = list(args)
            drifted[drifted.index(pin)] = "0" * 64
            self.assertEqual(2, MODULE.main(drifted))

        imported = tempfile.NamedTemporaryFile(mode="w", suffix=".json", delete=False)
        try:
            json.dump(exported["snapshot"], imported)
            imported.close()
            self.assertEqual(2, MODULE.main([
                str(ROOT / "policy/learn-01-current-focused-v1.json"),
                "--observations", imported.name, "--assess-pre-admission",
                "--original-item", "NEW-001", "--window", "window-2026-01",
                "--repository", "FS-GG/.github",
            ]))
        finally:
            pathlib.Path(imported.name).unlink(missing_ok=True)

    def test_pre_admission_cli_refuses_malformed_retained_structures_cleanly(self):
        empty = {
            "learningObservations": [], "populations": [], "outcomes": [], "admissions": [],
            "expectedDispatches": [], "lineage": [], "usage": [], "terminals": [],
            "runtimeGaps": [], "ciRuns": [], "ciPopulationCoverage": [],
        }
        cases = []
        for label, canonical in (
                ("canonical-list", "[]"), ("canonical-scalar", "1"),
                ("canonical-nested-shape", '{"identity":"bad","allocations":1}'),
                ("canonical-unhashable-id", '{"identity":"bad","itemId":[]}')):
            content = copy.deepcopy(empty)
            content["learningObservations"] = [{"canonical": canonical}]
            cases.append((label, private_envelope(content, version=4), []))
        for label, relation, value in (
                ("relation-array", "populations", {}),
                ("relation-row", "admissions", [42]),
                ("relation-unhashable-id", "populations",
                 [{"item_id": [], "original_item_id": "I-001"}]),
                ("dispatch-array", "expectedDispatches", "not-a-list")):
            content = copy.deepcopy(empty)
            content[relation] = value
            cases.append((label, private_envelope(content, version=4), []))
        cases.extend([
            ("capture-event", private_envelope(empty, version=4), [{"events": [[]]}]),
            ("capture-nested-id", private_envelope(empty, version=4), [{"events": [{
                "identity": "capture-bad", "itemId": "I-001", "allocationRoster": [[]],
            }]}]),
        ])

        with tempfile.TemporaryDirectory(prefix="learn-malformed-owner-export-") as directory:
            root = pathlib.Path(directory)
            for label, snapshot, captures in cases:
                with self.subTest(label=label):
                    exported = {"schema": "fsgg.telemetry.protected-learning-export/1",
                                "snapshot": snapshot, "captures": captures}
                    result, stdout, stderr = self.run_pre_admission_export(exported, root)
                    self.assertEqual(2, result)
                    self.assertEqual("", stdout)
                    self.assertTrue(stderr.startswith("refused: "), stderr)
                    self.assertNotIn("Traceback", stderr)

            foreign_assignment = {
                "kind": "learn-experiment-assignment", "identity": "assignment-I-001",
                "itemId": "I-001", "revision": 1, "policyId": "learn-01-current-focused-v1",
                "windowId": "window-2026-01", "arm": "current",
                "assignedAt": "2026-01-01T00:00:00Z", "repository": "FS-GG/foreign",
            }
            content = copy.deepcopy(empty)
            content["learningObservations"] = [{
                "canonical": json.dumps(foreign_assignment, separators=(",", ":"), sort_keys=True)
            }]
            exported = {"schema": "fsgg.telemetry.protected-learning-export/1",
                        "snapshot": private_envelope(content, version=4), "captures": []}
            result, stdout, stderr = self.run_pre_admission_export(exported, root)
            self.assertEqual(0, result, stderr)
            assessment = json.loads(stdout)
            self.assertEqual("conflict", assessment["assignmentState"])
            self.assertFalse(assessment["operationalReady"])
            self.assertIn("selected-scope-conflicts-with-retained-assignment",
                          assessment["missingOwnerInputs"])

    def test_complete_v3_structure_remains_unqualified_without_independent_sources(self):
        content = self.complete_v3_content()
        result = MODULE.analyze_private_snapshot(CONTRACT, private_envelope(content, version=3))
        self.assertFalse(result["tokenComparisonQualified"])
        self.assertEqual(
            ["independent-inventory-source-unavailable", "independent-shared-cost-authority-unavailable"],
            result["incompleteTokenReasons"]["I-001"],
        )
        self.assertEqual({"current": 0, "focused": 0}, result["providerTotalTokensByArm"])

        missing = copy.deepcopy(content)
        missing["learningObservations"] = [
            row for row in missing["learningObservations"]
            if json.loads(row["canonical"])["kind"] != "runtime-native-inventory/1"
        ]
        incomplete = MODULE.analyze_private_snapshot(CONTRACT, private_envelope(missing, version=3))
        self.assertIn("missing-native-inventory:inv-I-001", incomplete["incompleteTokenReasons"]["I-001"])

        duplicate_turn = copy.deepcopy(content)
        extra = copy.deepcopy(duplicate_turn["usage"][0])
        extra["identity"] = "usage-I-001-copy"
        duplicate_turn["usage"].append(extra)
        with self.assertRaisesRegex(MODULE.Refusal, "duplicate native turn identity"):
            MODULE.analyze_private_snapshot(CONTRACT, private_envelope(duplicate_turn, version=3))

        missing_turn = copy.deepcopy(content)
        missing_turn["usage"] = []
        missing_result = MODULE.analyze_private_snapshot(CONTRACT, private_envelope(missing_turn, version=3))
        self.assertIn("expected-native-turn-roster-mismatch:inv-I-001", missing_result["incompleteTokenReasons"]["I-001"])

        truncated_pages = copy.deepcopy(content)
        native = next(json.loads(row["canonical"]) for row in truncated_pages["learningObservations"]
                      if json.loads(row["canonical"])["kind"] == "runtime-native-inventory/1")
        native["pages"] = 2
        truncated_pages["learningObservations"] = [
            {"canonical": json.dumps(native, separators=(",", ":"), sort_keys=True)}
            if json.loads(row["canonical"])["kind"] == "runtime-native-inventory/1" else row
            for row in truncated_pages["learningObservations"]
        ]
        truncated_result = MODULE.analyze_private_snapshot(CONTRACT, private_envelope(truncated_pages, version=3))
        self.assertIn("incomplete-native-inventory-pages:inv-I-001", truncated_result["incompleteTokenReasons"]["I-001"])

        cross_item_turn = copy.deepcopy(content)
        cross_item_turn["usage"][0]["item_id"] = "foreign-original"
        with self.assertRaisesRegex(MODULE.Refusal, "usage crosses original-item identity"):
            MODULE.analyze_private_snapshot(CONTRACT, private_envelope(cross_item_turn, version=3))

        unsupported = copy.deepcopy(content)
        native = next(json.loads(row["canonical"]) for row in unsupported["learningObservations"]
                      if json.loads(row["canonical"])["kind"] == "runtime-native-inventory/1")
        native["support"] = "unknown"
        unsupported["learningObservations"] = [
            {"canonical": json.dumps(native, separators=(",", ":"), sort_keys=True)}
            if json.loads(row["canonical"])["kind"] == "runtime-native-inventory/1" else row
            for row in unsupported["learningObservations"]
        ]
        unsupported_result = MODULE.analyze_private_snapshot(CONTRACT, private_envelope(unsupported, version=3))
        self.assertIn("usage-support-unproven:inv-I-001", unsupported_result["incompleteTokenReasons"]["I-001"])

        bad_shared = copy.deepcopy(content)
        shared = next(json.loads(row["canonical"]) for row in bad_shared["learningObservations"]
                      if json.loads(row["canonical"])["kind"] == "learn-shared-cost/1")
        shared["allocations"][0]["tokens"] = 19
        bad_shared["learningObservations"] = [
            {"canonical": json.dumps(shared, separators=(",", ":"), sort_keys=True)}
            if json.loads(row["canonical"])["kind"] == "learn-shared-cost/1" else row
            for row in bad_shared["learningObservations"]
        ]
        with self.assertRaisesRegex(MODULE.Refusal, "shared allocations do not equal provider total"):
            MODULE.analyze_private_snapshot(CONTRACT, private_envelope(bad_shared, version=3))

        reopened = copy.deepcopy(content)
        reopened["expectedDispatches"].append({
            "item_id": "I-001", "dispatch_id": "dispatch-reopened", "relation": "follow-up",
        })
        reopened_result = MODULE.analyze_private_snapshot(CONTRACT, private_envelope(reopened, version=3))
        self.assertIn("expected-dispatch-roster-mismatch", reopened_result["incompleteTokenReasons"]["I-001"])

    def test_native_and_frozen_shared_cost_evidence_remains_unqualified_without_authenticated_producer(self):
        content = self.complete_v3_content()
        source = self.native_source_event()
        content["learningObservations"].append({
            "canonical": json.dumps(source, separators=(",", ":"), sort_keys=True), "ingest_order": 300,
        })
        result = MODULE.analyze_private_snapshot(CONTRACT, private_envelope(content, version=3))
        self.assertFalse(result["tokenComparisonQualified"])
        self.assertEqual(["independent-shared-cost-authority-unavailable"],
                         result["incompleteTokenReasons"]["I-001"])

        allocation = self.shared_allocation_event()
        content["learningObservations"].append({
            "canonical": json.dumps(allocation, separators=(",", ":"), sort_keys=True), "ingest_order": 1,
        })
        authority = self.shared_authority_event()
        content["learningObservations"].append({
            "canonical": json.dumps(authority, separators=(",", ":"), sort_keys=True), "ingest_order": 301,
        })
        for row in content["learningObservations"]:
            if json.loads(row["canonical"])["kind"] == "learn-shared-cost/1":
                row["ingest_order"] = 302
        unqualified = MODULE.analyze_private_snapshot(CONTRACT, private_envelope(content, version=3))
        self.assertFalse(unqualified["tokenComparisonQualified"])
        self.assertEqual(
            {"collector-principal-unavailable", "independent-shared-cost-authority-unavailable",
             "snapshot-origin-unverified"},
            set(unqualified["incompleteTokenReasons"]["I-001"]),
        )
        self.assertEqual({}, unqualified["providerTotalTokensByOriginalItem"])

        generic = copy.deepcopy(content)
        for row in generic["learningObservations"]:
            row.update({
                "receipt_producer": "producer-v3", "receipt_stream": "runtime",
                "receipt_role": "generic", "receipt_grant_id": "generic-grant",
                "receipt_grant_generation": 1, "receipt_key": "c" * 64,
                "receipt_envelope_digest": "d" * 64,
            })
        generic_report = MODULE.analyze_private_snapshot(CONTRACT, private_envelope(generic, version=4))
        self.assertIn("collector-principal-unavailable", generic_report["incompleteTokenReasons"]["I-001"])
        self.assertIn("snapshot-origin-unverified", generic_report["incompleteTokenReasons"]["I-001"])

        partial = copy.deepcopy(content)
        partial["learningObservations"][0]["receipt_role"] = "native-collector"
        with self.assertRaisesRegex(MODULE.Refusal, "receipt provenance is malformed"):
            MODULE.analyze_private_snapshot(CONTRACT, private_envelope(partial, version=4))

        protected = copy.deepcopy(generic)
        for row in protected["learningObservations"]:
            if json.loads(row["canonical"])["kind"] in {
                    "runtime-native-inventory-source/1", "learn-shared-cost-authority/1"}:
                row.update({
                    "receipt_producer": "collector", "receipt_stream": "native-inventory",
                    "receipt_role": "native-collector", "receipt_grant_id": "collector-grant",
                    "receipt_grant_generation": 1,
                })
        protected_report = MODULE.analyze_private_snapshot(CONTRACT, private_envelope(protected, version=4))
        self.assertNotIn("collector-principal-unavailable", protected_report["incompleteTokenReasons"]["I-001"])
        self.assertIn("native-source-verification-unavailable", protected_report["incompleteTokenReasons"]["I-001"])
        self.assertIn("snapshot-origin-unverified", protected_report["incompleteTokenReasons"]["I-001"])
        self.assertFalse(protected_report["tokenComparisonQualified"])

        mismatched_grant = copy.deepcopy(protected)
        for row in mismatched_grant["learningObservations"]:
            if json.loads(row["canonical"])["kind"] == "learn-shared-cost-authority/1":
                row["receipt_grant_generation"] = 2
        with self.assertRaisesRegex(MODULE.Refusal, "collector principal or grant mismatch"):
            MODULE.analyze_private_snapshot(CONTRACT, private_envelope(mismatched_grant, version=4))

        mismatched_principal = copy.deepcopy(protected)
        for row in mismatched_principal["learningObservations"]:
            if json.loads(row["canonical"])["kind"] == "learn-shared-cost-authority/1":
                row["receipt_producer"] = "foreign-collector"
        with self.assertRaisesRegex(MODULE.Refusal, "collector principal or grant mismatch"):
            MODULE.analyze_private_snapshot(CONTRACT, private_envelope(mismatched_principal, version=4))

        for field, changed in (("rootInvocationId", "foreign-root"),
                               ("orderedTurnIds", ["foreign-turn"]),
                               ("revision", 2)):
            tampered = copy.deepcopy(content)
            event = copy.deepcopy(source)
            binding = json.loads(base64.b64decode(event["sourceBinding"]["bytesBase64"]))
            binding[field] = changed
            raw = json.dumps(binding, separators=(",", ":"), sort_keys=True).encode("ascii")
            event["sourceBinding"]["sha256"] = hashlib.sha256(raw).hexdigest()
            event["sourceBinding"]["bytesBase64"] = base64.b64encode(raw).decode("ascii")
            tampered["learningObservations"] = [
                {"canonical": json.dumps(event, separators=(",", ":"), sort_keys=True)}
                if json.loads(row["canonical"])["kind"] == "runtime-native-inventory-source/1" else row
                for row in tampered["learningObservations"]
            ]
            with self.subTest(field=field), self.assertRaises(MODULE.Refusal):
                MODULE.analyze_private_snapshot(CONTRACT, private_envelope(tampered, version=3))

        for field, changed, message in (
            ("sourceDigest", "d" * 64, "retained native source"),
            ("sourceInvocationId", "forged-invocation", "independent-shared-cost-authority-unavailable"),
        ):
            tampered = copy.deepcopy(content)
            event = copy.deepcopy(authority)
            event[field] = changed
            tampered["learningObservations"][-1] = {
                "canonical": json.dumps(event, separators=(",", ":"), sort_keys=True), "ingest_order": 301,
            }
            with self.subTest(authority_field=field):
                if field == "sourceInvocationId":
                    refused = MODULE.analyze_private_snapshot(CONTRACT, private_envelope(tampered, version=3))
                    self.assertFalse(refused["tokenComparisonQualified"])
                    self.assertIn(message, refused["incompleteTokenReasons"]["I-001"])
                else:
                    with self.assertRaisesRegex(MODULE.Refusal, message):
                        MODULE.analyze_private_snapshot(CONTRACT, private_envelope(tampered, version=3))

        late_allocation = copy.deepcopy(content)
        for row in late_allocation["learningObservations"]:
            if json.loads(row["canonical"])["kind"] == "learn-shared-cost-allocation/1":
                row["ingest_order"] = 250
        with self.assertRaisesRegex(MODULE.Refusal, "not persisted before assignment"):
            MODULE.analyze_private_snapshot(CONTRACT, private_envelope(late_allocation, version=3))

        backdated = copy.deepcopy(content)
        for row in backdated["learningObservations"]:
            event = json.loads(row["canonical"])
            if event["kind"] == "learn-shared-cost-allocation/1":
                event["frozenAt"] = "2025-01-01T00:00:00Z"
                row["canonical"] = json.dumps(event, separators=(",", ":"), sort_keys=True)
                row["ingest_order"] = 250
                row.pop("content_digest", None)
        with self.assertRaisesRegex(MODULE.Refusal, "not persisted before assignment"):
            MODULE.analyze_private_snapshot(CONTRACT, private_envelope(backdated, version=3))

        historical = copy.deepcopy(content)
        for row in historical["learningObservations"]:
            if json.loads(row["canonical"])["kind"] == "learn-shared-cost-allocation/1":
                row["ingest_order"] = None
        unknown = MODULE.analyze_private_snapshot(CONTRACT, private_envelope(historical, version=3))
        self.assertFalse(unknown["tokenComparisonQualified"])
        self.assertIn("prospective-allocation-order-unavailable", unknown["incompleteTokenReasons"]["I-001"])

        forged = copy.deepcopy(content)
        forged_cost = next(json.loads(row["canonical"]) for row in forged["learningObservations"]
                           if json.loads(row["canonical"])["kind"] == "learn-shared-cost/1")
        forged_cost["providerTotalTokens"] = 99
        forged_cost["allocations"][0]["tokens"] = 99
        forged["learningObservations"] = [
            {**row, "canonical": json.dumps(forged_cost, separators=(",", ":"), sort_keys=True)}
            if json.loads(row["canonical"])["kind"] == "learn-shared-cost/1" else row
            for row in forged["learningObservations"]
        ]
        with self.assertRaisesRegex(MODULE.Refusal, "provider total disagrees with retained native usage"):
            MODULE.analyze_private_snapshot(CONTRACT, private_envelope(forged, version=3))

        self_hashed = copy.deepcopy(content)
        rewritten = []
        for row in self_hashed["learningObservations"]:
            event = json.loads(row["canonical"])
            if event["kind"] in {"learn-shared-cost/1", "learn-shared-cost-authority/1"}:
                event["sourceDigest"] = "d" * 64
                row = {**row, "canonical": json.dumps(event, separators=(",", ":"), sort_keys=True)}
            rewritten.append(row)
        self_hashed["learningObservations"] = rewritten
        with self.assertRaisesRegex(MODULE.Refusal, "authority does not bind the retained native source"):
            MODULE.analyze_private_snapshot(CONTRACT, private_envelope(self_hashed, version=3))

    def test_two_item_shared_cost_is_reconciled_once_by_frozen_rule(self):
        content = self.complete_v3_content()
        events = [json.loads(row["canonical"]) for row in content["learningObservations"]]
        shared = next(event for event in events if event["kind"] == "learn-shared-cost/1")
        shared["allocations"] = [
            {"originalItemId": "I-001", "tokens": 50},
            {"originalItemId": "I-002", "tokens": 50},
        ]
        accounting = next(event for event in events if event["kind"] == "learn-accounting-inventory/1")
        native = next(event for event in events if event["kind"] == "runtime-native-inventory/1")
        events.extend(copy.deepcopy(event) for event in OBSERVATIONS["events"] if event.get("itemId") == "I-002")
        accounting_two = self.event_for_item(accounting, "I-002")
        accounting_two["expectedDispatchIds"] = ["dispatch-I-002"]
        accounting_two["expectedSharedCostIds"] = ["shared-I-001"]
        events.extend([accounting_two, self.event_for_item(native, "I-002")])
        allocation = self.shared_allocation_event()
        allocation["allocationRoster"] = ["I-001", "I-002"]
        authority = self.shared_authority_event()
        events.extend([allocation, self.native_source_event(), self.native_source_event("I-002"), authority])
        content["learningObservations"] = [
            {"canonical": json.dumps(event, separators=(",", ":"), sort_keys=True)} for event in events
        ]
        for row in content["learningObservations"]:
            kind = json.loads(row["canonical"])["kind"]
            if kind == "learn-shared-cost-allocation/1": row["ingest_order"] = 1
            elif kind == "runtime-native-inventory-source/1": row["ingest_order"] = 300 if "I-001" in row["canonical"] else 303
            elif kind == "learn-shared-cost-authority/1": row["ingest_order"] = 301
            elif kind == "learn-shared-cost/1": row["ingest_order"] = 302
        content["admissions"].append({
            "identity": "admission-I-002", "item_id": "I-002", "invocation_id": "inv-I-002",
            "requested_model": "gpt-fixed", "requested_effort": "medium", "backend": "codex",
        })
        content["expectedDispatches"].append({
            "item_id": "I-002", "dispatch_id": "dispatch-I-002", "relation": "root",
        })
        content["lineage"].append({
            "item_id": "I-002", "dispatch_id": "dispatch-I-002", "invocation_id": "inv-I-002",
            "root_invocation_id": "inv-I-002",
        })
        content["usage"].append({
            "identity": "usage-I-002", "item_id": "I-002", "invocation_id": "inv-I-002",
            "turn_id": "turn-I-002", "provider": "openai", "requested_model": "gpt-fixed",
            "thread_id": "thread-I-002", "observed_model": "gpt-fixed", "requested_effort": "medium",
            "observed_effort": "medium", "total": 50,
        })
        content["terminals"].append({"item_id": "I-002", "invocation_id": "inv-I-002"})

        report = MODULE.analyze_private_snapshot(CONTRACT, private_envelope(content, version=3))
        self.assertFalse(report["tokenComparisonQualified"])
        self.assertEqual({}, report["providerTotalTokensByOriginalItem"])
        self.assertEqual({"current": 0, "focused": 0}, report["providerTotalTokensByArm"])
        for original in ("I-001", "I-002"):
            self.assertIn(
                "independent-shared-cost-authority-unavailable",
                report["incompleteTokenReasons"][original],
            )

        protected = copy.deepcopy(content)
        protected["usage"][0]["turn_sequence"] = 1
        source = self.native_source_event()
        for row in protected["learningObservations"]:
            if json.loads(row["canonical"])["kind"] in {
                    "runtime-native-inventory-source/1", "learn-shared-cost-authority/1"}:
                row.update({"receipt_producer": "collector", "receipt_stream": "native-inventory",
                            "receipt_role": "native-collector", "receipt_grant_id": "collector-grant",
                            "receipt_grant_generation": 1, "receipt_key": "c" * 64,
                            "receipt_envelope_digest": "d" * 64})
        capture = {"receiptKey": "c" * 64, "envelopeDigest": "d" * 64,
                   "producer": "collector", "stream": "native-inventory", "grantId": "collector-grant",
                   "grantGeneration": 1, "events": [source], "turns": [{
                       "threadId": "thread-I-001", "turnId": "turn-I-001", "sequence": 1,
                       "provider": "openai", "model": "gpt-fixed", "effort": "medium",
                       "input": 90, "cachedInput": 0, "output": 10, "reasoning": 0, "total": 100}]}
        qualified = MODULE.analyze_private_snapshot(
            CONTRACT, private_envelope(protected, version=4), _protected_captures=[capture])
        self.assertTrue(qualified["tokenComparisonQualified"])
        self.assertEqual({"I-001": 50, "I-002": 100}, qualified["providerTotalTokensByOriginalItem"])
        self.assertEqual(150, sum(qualified["providerTotalTokensByArm"].values()))
        with tempfile.TemporaryDirectory(prefix="learn-protected-export-") as directory:
            root = pathlib.Path(directory)
            config = root / "host.json"
            config.write_text("{}")
            config.chmod(0o600)
            exported = {"schema": "fsgg.telemetry.protected-learning-export/1",
                        "snapshot": private_envelope(protected, version=4), "captures": [capture]}
            host = root / "installed-host-fixture"
            host.write_text("#!/usr/bin/python3\nimport json,sys\n"
                            "assert sys.argv[1:3] == ['export-learning', '--config']\n"
                            + "print(" + repr(json.dumps(exported)) + ")\n")
            host.chmod(0o700)
            pin = hashlib.sha256(host.read_bytes()).hexdigest()
            output = root / "analysis.json"
            self.assertEqual(0, MODULE.main([
                str(ROOT / "policy/learn-01-current-focused-v1.json"),
                "--protected-host-executable", str(host), "--protected-host-sha256", pin,
                "--protected-host-config", str(config), "--output", str(output)]))
            self.assertEqual({"I-001": 50, "I-002": 100}, json.loads(output.read_text())["providerTotalTokensByOriginalItem"])
            with self.assertRaisesRegex(MODULE.Refusal, "operator pin"):
                MODULE.acquire_protected_export(host, config, "0" * 64)
            config.chmod(0o644)
            with self.assertRaisesRegex(MODULE.Refusal, "custody"):
                MODULE.acquire_protected_export(host, config, pin)
        imported = MODULE.analyze_private_snapshot(CONTRACT, private_envelope(protected, version=4))
        self.assertFalse(imported["tokenComparisonQualified"])
        self.assertIn("snapshot-origin-unverified", imported["incompleteTokenReasons"]["I-001"])
        generic = copy.deepcopy(protected)
        for row in generic["learningObservations"]:
            if json.loads(row["canonical"])["identity"] == source["identity"]:
                row["receipt_role"] = "generic"
        with self.assertRaisesRegex(MODULE.Refusal, "first authenticated source admission"):
            MODULE.analyze_private_snapshot(CONTRACT, private_envelope(generic, version=4), _protected_captures=[capture])
        substituted = copy.deepcopy(capture)
        substituted["turns"][0].update({"input": 190, "total": 200})
        mismatch = MODULE.analyze_private_snapshot(
            CONTRACT, private_envelope(protected, version=4), _protected_captures=[substituted])
        self.assertFalse(mismatch["tokenComparisonQualified"])
        self.assertIn("native-source-verification-unavailable", mismatch["incompleteTokenReasons"]["I-001"])

        missing_shared_turn = copy.deepcopy(content)
        missing_shared_turn["usage"] = [
            row for row in missing_shared_turn["usage"] if row["invocation_id"] != "inv-I-001"
        ]
        incomplete = MODULE.analyze_private_snapshot(
            CONTRACT, private_envelope(missing_shared_turn, version=3)
        )
        self.assertFalse(incomplete["tokenComparisonQualified"])
        for original in ("I-001", "I-002"):
            self.assertIn(
                "shared-source-incomplete:inv-I-001", incomplete["incompleteTokenReasons"][original]
            )

    def test_v3_refuses_foreign_terminal_future_capture_and_zero_digest(self):
        foreign = self.complete_v3_content()
        foreign["terminals"][0]["item_id"] = "foreign"
        with self.assertRaisesRegex(MODULE.Refusal, "terminal crosses original-item identity"):
            MODULE.analyze_private_snapshot(CONTRACT, private_envelope(foreign, version=3))

        future = self.complete_v3_content()
        native = next(json.loads(row["canonical"]) for row in future["learningObservations"]
                      if json.loads(row["canonical"])["kind"] == "runtime-native-inventory/1")
        native["capturedAt"] = "2027-01-01T00:00:00Z"
        future["learningObservations"] = [
            {"canonical": json.dumps(native, separators=(",", ":"), sort_keys=True)}
            if json.loads(row["canonical"])["kind"] == "runtime-native-inventory/1" else row
            for row in future["learningObservations"]
        ]
        with self.assertRaisesRegex(MODULE.Refusal, "capture must fall between assignment and cutoff"):
            MODULE.analyze_private_snapshot(CONTRACT, private_envelope(future, version=3))

        zero = self.complete_v3_content()
        accounting = next(json.loads(row["canonical"]) for row in zero["learningObservations"]
                          if json.loads(row["canonical"])["kind"] == "learn-accounting-inventory/1")
        accounting["sourceDigest"] = "0" * 64
        zero["learningObservations"] = [
            {"canonical": json.dumps(accounting, separators=(",", ":"), sort_keys=True)}
            if json.loads(row["canonical"])["kind"] == "learn-accounting-inventory/1" else row
            for row in zero["learningObservations"]
        ]
        with self.assertRaisesRegex(MODULE.Refusal, "disagrees with persisted assignment"):
            MODULE.analyze_private_snapshot(CONTRACT, private_envelope(zero, version=3))

    def test_v3_requires_explicit_ci_coverage_when_applicable(self):
        content = self.complete_v3_content()
        accounting = next(json.loads(row["canonical"]) for row in content["learningObservations"]
                          if json.loads(row["canonical"])["kind"] == "learn-accounting-inventory/1")
        accounting["ciApplicability"] = "required"
        content["learningObservations"] = [
            {"canonical": json.dumps(accounting, separators=(",", ":"), sort_keys=True)}
            if json.loads(row["canonical"])["kind"] == "learn-accounting-inventory/1" else row
            for row in content["learningObservations"]
        ]
        result = MODULE.analyze_private_snapshot(CONTRACT, private_envelope(content, version=3))
        self.assertIn("missing-ci-population-coverage", result["incompleteTokenReasons"]["I-001"])

    def test_observation_snapshot_is_order_independent_and_bounded(self):
        first = MODULE.validate_observations(CORPUS, OBSERVATIONS)
        reordered = copy.deepcopy(OBSERVATIONS)
        reordered["events"].reverse()
        second = MODULE.validate_observations(CORPUS, reordered)
        self.assertEqual(first["observationDigest"], second["observationDigest"])
        self.assertEqual(18, first["observationFacts"])

    def test_existing_bounded_dashboard_snapshot_is_a_reproducible_input(self):
        rows = [{"canonical": json.dumps(event, separators=(",", ":"), sort_keys=True)} for event in OBSERVATIONS["events"]]
        envelope = private_envelope({"learningObservations": rows})
        result = MODULE.validate_observations(CORPUS, envelope)
        self.assertEqual(18, result["observationFacts"])

    def test_private_snapshot_itself_drives_assignment_coverage_and_totals(self):
        rows = [{"canonical": json.dumps(event, separators=(",", ":"), sort_keys=True)} for event in OBSERVATIONS["events"]]
        admissions = []
        usage = []
        terminals = []
        for index in range(1, 7):
            item = f"I-{index:03d}"
            invocation = f"private-{index}"
            admissions.append({
                "identity": f"admission-{index}", "item_id": item, "invocation_id": invocation,
                "requested_model": "gpt-fixed", "requested_effort": "medium", "backend": "codex",
            })
            usage.append({
                "identity": f"usage-{index}", "item_id": item, "invocation_id": invocation,
                "provider": "openai", "requested_model": "gpt-fixed", "observed_model": "gpt-fixed",
                "requested_effort": "medium", "observed_effort": "medium", "total": index * 100,
            })
            terminals.append({"identity": f"terminal-{index}", "item_id": item, "invocation_id": invocation})
        envelope = private_envelope({
            "learningObservations": rows, "populations": [], "admissions": admissions,
            "expectedDispatches": [], "lineage": [], "usage": usage, "terminals": terminals,
            "runtimeGaps": [],
        })
        result = MODULE.analyze_private_snapshot(CONTRACT, envelope)
        self.assertEqual({"current": 3, "focused": 3}, result["assignedOriginalItemsByArm"])
        self.assertEqual({"current": 0, "focused": 0}, result["providerTotalTokensByArm"])
        self.assertFalse(result["tokenComparisonQualified"])
        for reason in (
            "expected-dispatch-population-unproven",
            "expected-native-turn-inventory-unavailable",
            "expected-shared-cost-inventory-unavailable",
            "expected-provider-unavailable",
            "usage-support-unproven",
        ):
            self.assertIn(reason, result["incompleteTokenReasons"]["I-001"])

        fabricated = json.loads(gzip.decompress(base64.b64decode(envelope["canonicalSnapshotGzip"])))
        fabricated.update({
            "expectedTurnInventoryComplete": True,
            "expectedSharedCostInventoryComplete": True,
            "usageSupport": "supported",
            "expectedProvider": "openai",
        })
        still_incomplete = MODULE.analyze_private_snapshot(CONTRACT, private_envelope(fabricated))
        self.assertFalse(still_incomplete["tokenComparisonQualified"])

        provider_drift = json.loads(gzip.decompress(base64.b64decode(envelope["canonicalSnapshotGzip"])))
        second_turn = copy.deepcopy(provider_drift["usage"][0])
        second_turn.update({"identity": "usage-provider-drift", "provider": "foreign"})
        provider_drift["usage"].append(second_turn)
        drift_result = MODULE.analyze_private_snapshot(CONTRACT, private_envelope(provider_drift))
        self.assertIn("provider-mismatch", drift_result["incompleteTokenReasons"]["I-001"])

        changed = copy.deepcopy(envelope)
        changed["workspaceId"] = "workspace-b"
        with self.assertRaisesRegex(MODULE.Refusal, "workspace provenance disagrees"):
            MODULE.analyze_private_snapshot(CONTRACT, changed)

        content = json.loads(gzip.decompress(base64.b64decode(envelope["canonicalSnapshotGzip"])))
        content["admissions"] = [row for row in content["admissions"] if row["item_id"] != "I-001"]
        content["usage"] = [row for row in content["usage"] if row["item_id"] != "I-001"]
        content["terminals"] = [row for row in content["terminals"] if row["item_id"] != "I-001"]
        missing_root = MODULE.analyze_private_snapshot(CONTRACT, private_envelope(content))
        self.assertIn("missing-root-admission", missing_root["incompleteTokenReasons"]["I-001"])

        content["ciRuns"] = [{"item_id": "I-002", "run_id": 42, "status": "in_progress"}]
        incomplete_ci = MODULE.analyze_private_snapshot(CONTRACT, private_envelope(content))
        self.assertIn("incomplete-ci-run:42", incomplete_ci["incompleteTokenReasons"]["I-002"])

    def test_private_snapshot_refuses_cross_original_dispatch_lineage(self):
        rows = [{"canonical": json.dumps(event, separators=(",", ":"), sort_keys=True)} for event in OBSERVATIONS["events"]]
        admissions = [
            {"identity": "admission-1", "item_id": "I-001", "invocation_id": "inv-1",
             "requested_model": "gpt-fixed", "requested_effort": "medium"},
            {"identity": "admission-2", "item_id": "I-002", "invocation_id": "inv-2",
             "requested_model": "gpt-fixed", "requested_effort": "medium"},
        ]
        envelope = private_envelope({
            "learningObservations": rows,
            "populations": [],
            "admissions": admissions,
            "expectedDispatches": [{"item_id": "I-001", "dispatch_id": "dispatch-1"}],
            "lineage": [{"item_id": "I-002", "dispatch_id": "dispatch-1", "invocation_id": "inv-2"}],
            "usage": [], "terminals": [], "runtimeGaps": [],
        })
        with self.assertRaisesRegex(MODULE.Refusal, "dispatch lineage crosses original-item"):
            MODULE.analyze_private_snapshot(CONTRACT, envelope)

    def test_private_snapshot_refuses_missing_provider_evidence(self):
        rows = [{"canonical": json.dumps(event, separators=(",", ":"), sort_keys=True)} for event in OBSERVATIONS["events"]]
        admissions = [{
            "identity": f"admission-{index}", "item_id": f"I-{index:03d}", "invocation_id": f"private-{index}",
            "requested_model": "gpt-fixed", "requested_effort": "medium", "backend": "codex",
        } for index in range(1, 7)]
        usage = [{
            "identity": f"usage-{index}", "item_id": f"I-{index:03d}", "invocation_id": f"private-{index}",
            "provider": None, "requested_model": "gpt-fixed", "observed_model": "gpt-fixed",
            "requested_effort": "medium", "observed_effort": "medium", "total": 100,
        } for index in range(1, 7)]
        terminals = [{"item_id": f"I-{index:03d}", "invocation_id": f"private-{index}"} for index in range(1, 7)]
        result = MODULE.analyze_private_snapshot(CONTRACT, private_envelope({
            "learningObservations": rows, "populations": [], "admissions": admissions,
            "expectedDispatches": [], "lineage": [], "usage": usage, "terminals": terminals,
            "runtimeGaps": [],
        }))
        self.assertEqual(6, len(result["incompleteTokenOriginalItems"]))
        self.assertFalse(result["tokenComparisonQualified"])

    def test_dashboard_gzip_expansion_is_stopped_at_output_bound(self):
        envelope = {
            "schema": "fsgg.telemetry.item-detail/2",
            "workspaceId": "workspace-a",
            "revision": "0" * 64,
            "canonicalSnapshotGzip": base64.b64encode(gzip.compress(b"x" * (4 * 1024 * 1024 + 1))).decode(),
        }
        with self.assertRaisesRegex(MODULE.Refusal, "exceeds analysis bound"):
            MODULE.validate_observations(CORPUS, envelope)

    def test_exact_duplicate_is_idempotent(self):
        observations = copy.deepcopy(OBSERVATIONS)
        observations["events"].append(copy.deepcopy(observations["events"][0]))
        result = MODULE.validate_observations(CORPUS, observations)
        self.assertEqual(1, result["duplicateFactsIgnored"])
        self.assertEqual(18, result["observationFacts"])

    def test_non_assignment_correction_is_refused(self):
        observations = copy.deepcopy(OBSERVATIONS)
        correction = copy.deepcopy(next(event for event in observations["events"] if event["identity"] == "snapshot-001"))
        correction.update({"revision": 2, "snapshotDigest": "9" * 64})
        observations["events"].append(correction)
        with self.assertRaisesRegex(MODULE.Refusal, "immutable after persistence"):
            MODULE.validate_observations(CORPUS, observations)

    def test_non_assignment_correction_refusal_is_independent_of_revision_order(self):
        forward = copy.deepcopy(OBSERVATIONS)
        original = next(event for event in forward["events"] if event["identity"] == "snapshot-001")
        correction = copy.deepcopy(original)
        correction.update({"revision": 2, "snapshotDigest": "9" * 64})
        forward["events"].append(correction)
        reversed_order = copy.deepcopy(OBSERVATIONS)
        original_index = next(index for index, event in enumerate(reversed_order["events"]) if event["identity"] == "snapshot-001")
        reversed_order["events"].insert(original_index, copy.deepcopy(correction))
        for observations in (forward, reversed_order):
            with self.assertRaisesRegex(MODULE.Refusal, "immutable after persistence"):
                MODULE.validate_observations(CORPUS, observations)

    def test_boolean_revision_is_rejected(self):
        observations = copy.deepcopy(OBSERVATIONS)
        observations["events"][0]["revision"] = True
        with self.assertRaisesRegex(MODULE.Refusal, "identity and revision are invalid"):
            MODULE.validate_observations(CORPUS, observations)

    def test_retry_cannot_redraw_assignment(self):
        observations = copy.deepcopy(OBSERVATIONS)
        redraw = copy.deepcopy(next(event for event in observations["events"] if event["identity"] == "assignment-001"))
        redraw.update({"revision": 2, "arm": "focused"})
        observations["events"].append(redraw)
        with self.assertRaisesRegex(MODULE.Refusal, "immutable after persistence"):
            MODULE.validate_observations(CORPUS, observations)

    def test_cross_item_or_missing_observation_is_rejected(self):
        observations = copy.deepcopy(OBSERVATIONS)
        observations["events"] = [event for event in observations["events"] if event["identity"] != "manifest-006"]
        with self.assertRaisesRegex(MODULE.Refusal, "incomplete pre-dispatch"):
            MODULE.validate_observations(CORPUS, observations)

    def test_cross_workspace_join_is_rejected(self):
        observations = copy.deepcopy(OBSERVATIONS)
        observations["events"][0]["workspaceId"] = "foreign-workspace"
        with self.assertRaisesRegex(MODULE.Refusal, "cross-workspace"):
            MODULE.validate_observations(CORPUS, observations)

    def test_frozen_contract_and_fixture_are_reviewable(self):
        MODULE.validate_contract(CONTRACT)
        result = MODULE.validate_corpus(CONTRACT, CORPUS)
        self.assertEqual(6, result["originalItems"])
        self.assertEqual(1, result["childItems"])
        self.assertEqual({"accepted": 3, "cancelled": 1, "failed": 1, "open": 1}, result["outcomes"])
        self.assertEqual(3, result["fixed14DayCompletions"])
        self.assertEqual(3, result["repairMatureAcceptedItems"])
        self.assertEqual(1, result["materialRepairsInMatureFollowup"])
        self.assertEqual(["I-004"], result["openCensoredOriginalItems"])
        self.assertEqual({"current": 3, "focused": 2}, result["completeTokenItemsByArm"])
        self.assertEqual({"current": 2700, "focused": 1800}, result["providerTotalTokensByArm"])
        self.assertEqual(["I-004"], result["incompleteTokenOriginalItems"])
        self.assertIn("nonterminal-invocation:inv-root-004", result["incompleteTokenReasons"]["I-004"])
        self.assertFalse(result["tokenComparisonQualified"])
        self.assertEqual("synthetic-fixture-only-no-efficiency-result", result["claim"])

    def test_outcome_derived_baseline_label_is_rejected(self):
        corpus = copy.deepcopy(CORPUS)
        corpus["items"][1]["assignmentSource"] = "successful-outcome-reclassified-as-current"
        with self.assertRaisesRegex(MODULE.Refusal, "outcome-derived"):
            MODULE.validate_corpus(CONTRACT, corpus)

    def test_analysis_unit_cannot_be_invocation(self):
        corpus = copy.deepcopy(CORPUS)
        corpus["analysisUnit"] = "invocation"
        with self.assertRaisesRegex(MODULE.Refusal, "independent successes"):
            MODULE.validate_corpus(CONTRACT, corpus)

    def test_actual_child_is_grouped_under_canonical_original(self):
        result = MODULE.validate_corpus(CONTRACT, CORPUS)
        self.assertEqual(6, result["originalItems"])
        self.assertEqual(1, result["childItems"])
        self.assertEqual(1200, result["providerTotalTokensByOriginalItem"]["I-005"])
        self.assertEqual(2700, result["providerTotalTokensByArm"]["current"])

    def test_child_cannot_mint_a_new_original_success(self):
        corpus = copy.deepcopy(CORPUS)
        child = copy.deepcopy(corpus["items"][0])
        child.update({"itemId": "I-child", "originalItemId": "I-child", "parentItemId": "I-001"})
        corpus["items"].append(child)
        with self.assertRaisesRegex(MODULE.Refusal, "child lineage"):
            MODULE.validate_corpus(CONTRACT, corpus)

    def test_missing_expected_root_usage_stays_incomplete(self):
        corpus = copy.deepcopy(CORPUS)
        corpus["costs"] = [cost for cost in corpus["costs"] if cost["costId"] != "usage-root-001"]
        result = MODULE.validate_corpus(CONTRACT, corpus)
        self.assertFalse(result["tokenComparisonQualified"])
        self.assertIn("I-001", result["incompleteTokenOriginalItems"])
        self.assertIn("missing-expected-usage:inv-root-001/turn-root-001", result["incompleteTokenReasons"]["I-001"])

    def test_missing_expected_child_usage_stays_incomplete(self):
        corpus = copy.deepcopy(CORPUS)
        corpus["costs"] = [cost for cost in corpus["costs"] if cost["costId"] != "usage-rescue-005"]
        result = MODULE.validate_corpus(CONTRACT, corpus)
        self.assertFalse(result["tokenComparisonQualified"])
        self.assertIn("I-005", result["incompleteTokenOriginalItems"])
        self.assertIn("missing-expected-usage:inv-rescue-005/turn-rescue-005", result["incompleteTokenReasons"]["I-005"])

    def test_missing_expected_shared_cost_stays_incomplete(self):
        corpus = copy.deepcopy(CORPUS)
        corpus["costs"] = [cost for cost in corpus["costs"] if cost["costId"] != "shared-plan-001-002"]
        result = MODULE.validate_corpus(CONTRACT, corpus)
        self.assertFalse(result["tokenComparisonQualified"])
        self.assertIn("I-001", result["incompleteTokenOriginalItems"])
        self.assertIn("I-002", result["incompleteTokenOriginalItems"])

    def test_duplicate_native_turn_usage_is_rejected(self):
        corpus = copy.deepcopy(CORPUS)
        duplicate = copy.deepcopy(corpus["costs"][0])
        duplicate["costId"] = "usage-root-001-copy"
        corpus["costs"].append(duplicate)
        with self.assertRaisesRegex(MODULE.Refusal, "duplicate usage"):
            MODULE.validate_corpus(CONTRACT, corpus)

    def test_unbounded_missing_tokens_cannot_be_zero(self):
        corpus = copy.deepcopy(CORPUS)
        missing = next(cost for cost in corpus["costs"] if cost["costId"] == "usage-root-004")
        missing["completeness"] = "imputed-zero"
        missing["providerTotalTokens"] = 0
        with self.assertRaisesRegex(MODULE.Refusal, "cannot be zero"):
            MODULE.validate_corpus(CONTRACT, corpus)

    def test_legal_zero_total_is_complete_for_arithmetic_primary(self):
        corpus = copy.deepcopy(CORPUS)
        cost = next(cost for cost in corpus["costs"] if cost["costId"] == "usage-root-002")
        cost["providerTotalTokens"] = 0
        result = MODULE.validate_corpus(CONTRACT, corpus)
        self.assertEqual(1000, result["providerTotalTokensByArm"]["focused"])
        self.assertNotIn("I-002", result["incompleteTokenOriginalItems"])

    def test_cache_and_reasoning_cannot_change_provider_total(self):
        corpus = copy.deepcopy(CORPUS)
        corpus["costs"][0]["cacheTokens"] = 900
        corpus["costs"][0]["reasoningTokens"] = 300
        result = MODULE.validate_corpus(CONTRACT, corpus)
        self.assertEqual(2700, result["providerTotalTokensByArm"]["current"])

    def test_provider_mismatch_and_unsupported_usage_stay_incomplete(self):
        corpus = copy.deepcopy(CORPUS)
        cost = next(cost for cost in corpus["costs"] if cost["costId"] == "usage-root-001")
        cost.update({"expectedProvider": "openai", "observedProvider": "foreign", "usageSupport": "native-join-unsupported"})
        result = MODULE.validate_corpus(CONTRACT, corpus)
        self.assertIn("provider-mismatch", result["incompleteTokenReasons"]["I-001"])
        self.assertIn("unsupported-usage:native-join-unsupported", result["incompleteTokenReasons"]["I-001"])

    def test_provider_and_usage_support_evidence_cannot_default_to_complete(self):
        corpus = copy.deepcopy(CORPUS)
        corpus["costs"][0].pop("expectedProvider")
        with self.assertRaisesRegex(MODULE.Refusal, "expected provider evidence is required"):
            MODULE.validate_corpus(CONTRACT, corpus)

    def test_late_usage_changes_only_coverage_when_reanalyzed(self):
        corpus = copy.deepcopy(CORPUS)
        corpus["costs"] = [cost for cost in corpus["costs"] if cost["costId"] != "usage-root-001"]
        before = MODULE.validate_corpus(CONTRACT, corpus)
        corpus["costs"].append(copy.deepcopy(next(cost for cost in CORPUS["costs"] if cost["costId"] == "usage-root-001")))
        after = MODULE.validate_corpus(CONTRACT, corpus)
        self.assertIn("I-001", before["incompleteTokenOriginalItems"])
        self.assertNotIn("I-001", after["incompleteTokenOriginalItems"])

    def test_reopened_root_attempt_is_counted_once_under_the_same_issue(self):
        corpus = copy.deepcopy(CORPUS)
        corpus["expectedInvocations"].append({
            "invocationId": "inv-reopen-001", "itemId": "I-001", "originalItemId": "I-001",
            "kind": "root", "expectedTurnIds": ["turn-reopen-001"], "terminal": True,
        })
        corpus["costs"].append({
            "costId": "usage-reopen-001", "kind": "invocation", "invocationId": "inv-reopen-001",
            "turnId": "turn-reopen-001", "completeness": "complete", "providerTotalTokens": 50,
            "expectedProvider": "openai", "observedProvider": "openai", "usageSupport": "supported",
            "allocations": [{"originalItemId": "I-001", "fraction": 1.0}],
        })
        result = MODULE.validate_corpus(CONTRACT, corpus)
        self.assertEqual(6, result["originalItems"])
        self.assertEqual(1150, result["providerTotalTokensByOriginalItem"]["I-001"])

    def test_incomplete_ci_invocation_keeps_whole_issue_incomplete(self):
        corpus = copy.deepcopy(CORPUS)
        corpus["expectedInvocations"].append({
            "invocationId": "inv-ci-002", "itemId": "I-002", "originalItemId": "I-002",
            "kind": "integration", "expectedTurnIds": ["turn-ci-002"], "terminal": False,
        })
        result = MODULE.validate_corpus(CONTRACT, corpus)
        self.assertIn("nonterminal-invocation:inv-ci-002", result["incompleteTokenReasons"]["I-002"])
        self.assertIn("missing-expected-usage:inv-ci-002/turn-ci-002", result["incompleteTokenReasons"]["I-002"])

    def test_shared_cost_is_allocated_once(self):
        corpus = copy.deepcopy(CORPUS)
        corpus["costs"][-1]["allocations"][1]["fraction"] = 1.0
        with self.assertRaisesRegex(MODULE.Refusal, "fractions must sum to one"):
            MODULE.validate_corpus(CONTRACT, corpus)

    def test_delayed_repair_must_follow_acceptance(self):
        corpus = copy.deepcopy(CORPUS)
        item = next(item for item in corpus["items"] if item["itemId"] == "I-006")
        item["materialRepairAt"] = "2026-01-01T12:00:00Z"
        with self.assertRaisesRegex(MODULE.Refusal, "repair does not follow acceptance"):
            MODULE.validate_corpus(CONTRACT, corpus)


if __name__ == "__main__":
    unittest.main()
