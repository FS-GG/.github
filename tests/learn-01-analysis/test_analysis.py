import copy
import base64
import gzip
import hashlib
import importlib.util
import json
import pathlib
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
    if version == 3:
        body["learningSnapshotSchema"] = "fsgg.telemetry.learn-item-detail/3"
        for index, row in enumerate(body.get("learningObservations", []), 100):
            row.setdefault("content_digest", hashlib.sha256(row["canonical"].encode()).hexdigest())
            row.setdefault("ingest_order", index)
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
            ["independent-shared-cost-authority-unavailable"],
            unqualified["incompleteTokenReasons"]["I-001"],
        )
        self.assertEqual({}, unqualified["providerTotalTokensByOriginalItem"])

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
