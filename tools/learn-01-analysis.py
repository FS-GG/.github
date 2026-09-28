#!/usr/bin/env python3
"""Validate and summarize the frozen LEARN-01.1 synthetic contract corpus."""
from __future__ import annotations

import argparse
import base64
import gzip
import hashlib
import io
import json
import math
import pathlib
import re
import sys
from collections import Counter, defaultdict
from datetime import datetime, timedelta


class Refusal(ValueError):
    pass


def load(path: pathlib.Path) -> dict:
    with path.open(encoding="utf-8") as handle:
        value = json.load(handle)
    if not isinstance(value, dict):
        raise Refusal(f"{path}: root must be an object")
    return value


def validate_contract(contract: dict) -> None:
    if contract.get("schema") != "fsgg.learn.context-experiment/v1":
        raise Refusal("unsupported contract schema")
    if contract.get("status") != "source-contract-not-enrolled":
        raise Refusal("LEARN-01.1 cannot activate enrollment")
    arms = [arm.get("id") for arm in contract.get("arms", [])]
    if arms != ["current", "focused"]:
        raise Refusal("contract must freeze current and focused arms")
    assignment = contract.get("assignment", {})
    if assignment.get("unit") != "original-item" or assignment.get("descendantsIndependentSamples") is not False:
        raise Refusal("original item must be the assignment and analysis unit")
    if not assignment.get("outcomeDerivedLabelsForbidden"):
        raise Refusal("outcome-derived labels must be forbidden")
    accounting = contract.get("accounting", {})
    if accounting.get("missingUsage") != "unknown-unbounded" or not accounting.get("zeroImputationForbidden"):
        raise Refusal("unbounded missing usage must remain unknown")
    window = contract.get("window", {})
    if window.get("minimumEnrollmentDays") != 28 or window.get("maximumEnrollmentDays") != 84:
        raise Refusal("window cadence drifted")
    budget = window.get("buildAndAnalysisBudget", {})
    if budget != {"maximumEngineeringDays": 5, "scopeCheckDay": 2}:
        raise Refusal("build and analysis budget drifted")
    decision = contract.get("decision", {})
    primary = decision.get("primary", {})
    if (decision.get("confidenceLevel"), decision.get("power"), primary.get("endpoint"),
            primary.get("practicalReduction"), primary.get("minimumIndependentOriginalItemsPerArm")) != (
            0.95, 0.8, "arithmetic-mean-provider-total-tokens-per-assigned-original-item", 0.10, 6908):
        raise Refusal("primary decision or precision plan drifted")
    mapping = contract.get("fieldMapping", [])
    concepts = {row.get("concept") for row in mapping if isinstance(row, dict)}
    required_concepts = {
        "feature and item identity", "invocation lineage", "requested execution profile",
        "observed execution profile and usage", "terminal native outcome",
        "pre-dispatch task rubric snapshot", "context recipe and manifest identity",
        "persisted experiment window and assignment", "late repair link"
    }
    if concepts != required_concepts:
        raise Refusal("conceptual-to-existing field mapping is incomplete")
    analysis = contract.get("analysis", {})
    if analysis.get("version") != "learn-01-synthetic-analysis-v1" or analysis.get("claimBoundary") != "synthetic-fixture-only-no-efficiency-result":
        raise Refusal("analysis version or claim boundary drifted")
    claims = contract.get("claims", {})
    if not claims or any(value is not False for value in claims.values()):
        raise Refusal("LEARN-01.1 cannot claim publication, operation, observation or feature exit")


def decode_private_snapshot(observations: dict) -> tuple[dict, str]:
    """Authenticate the immutable bytes and workspace carried by an item-detail snapshot."""
    encoded = observations.get("canonicalSnapshotGzip")
    workspace = observations.get("workspaceId")
    revision = observations.get("revision")
    if not isinstance(encoded, str):
        raise Refusal("telemetry snapshot lacks canonicalSnapshotGzip")
    if not isinstance(workspace, str) or not workspace:
        raise Refusal("telemetry snapshot lacks retained workspace provenance")
    try:
        compressed = base64.b64decode(encoded, validate=True)
        if len(compressed) > 1024 * 1024:
            raise Refusal("compressed telemetry snapshot exceeds analysis bound")
        maximum = 4 * 1024 * 1024
        raw = bytearray()
        with gzip.GzipFile(fileobj=io.BytesIO(compressed), mode="rb") as stream:
            while len(raw) <= maximum:
                chunk = stream.read(min(64 * 1024, maximum + 1 - len(raw)))
                if not chunk:
                    break
                raw.extend(chunk)
        if len(raw) > maximum:
            raise Refusal("telemetry snapshot exceeds analysis bound")
        digest = hashlib.sha256(raw).hexdigest()
        if revision != digest:
            raise Refusal("telemetry snapshot revision does not bind canonical bytes")
        content = json.loads(raw)
    except Refusal:
        raise
    except (ValueError, TypeError, OSError, EOFError, json.JSONDecodeError) as error:
        raise Refusal("telemetry snapshot is malformed") from error
    if content.get("workspaceId") != workspace:
        raise Refusal("telemetry snapshot workspace provenance disagrees with canonical bytes")
    selection = content.get("selection")
    if not isinstance(selection, dict) or selection.get("complete") is not True:
        raise Refusal("telemetry snapshot selection is incomplete")
    return content, workspace


def validate_observations(corpus: dict, observations: dict) -> dict:
    """Select a stable pre-dispatch fact set from a bounded telemetry snapshot."""
    if observations.get("schema") == "fsgg.telemetry.item-detail/2":
        content, workspace = decode_private_snapshot(observations)
        try:
            rows = content.get("learningObservations", [])
            events = [json.loads(row["canonical"]) for row in rows]
        except (ValueError, KeyError, TypeError, json.JSONDecodeError) as error:
            raise Refusal("telemetry snapshot learning observations are malformed") from error
        observations = {
            "schema": "fsgg.learn.observation-snapshot/v1",
            "workspaceId": workspace,
            "events": events,
        }
    if observations.get("schema") != "fsgg.learn.observation-snapshot/v1":
        raise Refusal("unsupported observation snapshot schema")
    events = observations.get("events")
    workspace = observations.get("workspaceId")
    if not isinstance(workspace, str) or not workspace:
        raise Refusal("observation snapshot requires a workspace identity")
    if not isinstance(events, list) or not events or len(events) > 10000:
        raise Refusal("observation snapshot requires between 1 and 10000 events")

    revisions_by_identity = defaultdict(dict)
    duplicates = 0
    corrections = 0
    for event in events:
        if not isinstance(event, dict):
            raise Refusal("observation events must be objects")
        identity = event.get("identity")
        revision = event.get("revision", 0)
        kind = event.get("kind")
        if event.get("workspaceId", workspace) != workspace:
            raise Refusal("cross-workspace observation join is forbidden")
        if (not isinstance(identity, str) or not identity or not isinstance(revision, int)
                or isinstance(revision, bool) or revision < 0):
            raise Refusal("observation identity and revision are invalid")
        if kind not in {"learn-task-snapshot", "learn-context-manifest", "learn-experiment-assignment"}:
            raise Refusal("observation kind is unsupported")
        prior = revisions_by_identity[identity].get(revision)
        if prior == event:
            duplicates += 1
            continue
        if prior is not None:
            raise Refusal("observation identity has conflicting content at the same revision")
        revisions_by_identity[identity][revision] = event

    by_identity = {}
    for identity, revisions in revisions_by_identity.items():
        versions = list(revisions.values())
        kinds = {event["kind"] for event in versions}
        if len(kinds) != 1:
            raise Refusal("observation identity cannot change kind across revisions")
        if len(versions) > 1:
            raise Refusal("learning pre-dispatch fact is immutable after persistence")
        corrections += len(versions) - 1
        by_identity[identity] = revisions[max(revisions)]

    selected = {}
    roots = {item["itemId"]: item for item in corpus.get("items", []) if item.get("parentItemId") is None}
    for event in by_identity.values():
        item = event.get("itemId")
        key = (item, event["kind"])
        if item not in roots:
            raise Refusal("pre-dispatch observation must belong to an original item")
        if key in selected:
            raise Refusal("each original item requires one stable observation of each kind")
        selected[key] = event

    hex_digest = lambda value: isinstance(value, str) and len(value) == 64 and all(c in "0123456789abcdef" for c in value)
    for item, root in roots.items():
        required = {
            kind: selected.get((item, kind))
            for kind in ("learn-task-snapshot", "learn-context-manifest", "learn-experiment-assignment")
        }
        if any(value is None for value in required.values()):
            raise Refusal(f"{item}: incomplete pre-dispatch observation set")
        snapshot = required["learn-task-snapshot"]
        manifest = required["learn-context-manifest"]
        assignment = required["learn-experiment-assignment"]
        required_ids = [snapshot.get("snapshotId"), snapshot.get("rubricVersion"), manifest.get("recipeId"),
                        manifest.get("manifestId"), assignment.get("windowId"), assignment.get("policyId")]
        if any(not isinstance(value, str) or not value for value in required_ids):
            raise Refusal(f"{item}: observation identity fields must be non-empty strings")
        if not hex_digest(snapshot.get("snapshotDigest")):
            raise Refusal(f"{item}: invalid task snapshot digest")
        if not hex_digest(manifest.get("recipeDigest")) or not hex_digest(manifest.get("manifestDigest")):
            raise Refusal(f"{item}: invalid recipe or manifest digest")
        if assignment.get("arm") != root.get("assignedArm") or assignment.get("policyId") != corpus.get("contractId"):
            raise Refusal(f"{item}: observed assignment disagrees with the frozen issue contract")
        if assignment.get("assignedAt") != root.get("assignedAt"):
            raise Refusal(f"{item}: observed assignment time disagrees with the frozen issue contract")
        try:
            captured = datetime.fromisoformat(snapshot["capturedAt"].replace("Z", "+00:00"))
            assigned = datetime.fromisoformat(assignment["assignedAt"].replace("Z", "+00:00"))
        except (AttributeError, ValueError) as error:
            raise Refusal(f"{item}: observation timestamps must be RFC3339") from error
        if captured.tzinfo is None or assigned.tzinfo is None or captured > assigned:
            raise Refusal(f"{item}: task snapshot must precede assignment")
        deviation = assignment.get("deviation")
        if deviation is not None and (not isinstance(deviation, str) or not deviation or len(deviation) > 512):
            raise Refusal(f"{item}: assignment deviation must be null or a bounded string")

    canonical = json.dumps(sorted(by_identity.values(), key=lambda value: value["identity"]), separators=(",", ":"), sort_keys=True)
    return {
        "observationSchema": observations["schema"],
        "observationDigest": hashlib.sha256(canonical.encode()).hexdigest(),
        "observationFacts": len(by_identity),
        "duplicateFactsIgnored": duplicates,
        "correctedFacts": corrections,
    }


def analyze_private_snapshot(contract: dict, envelope: dict) -> dict:
    """Derive assigned-treatment token coverage only from one retained private snapshot."""
    content, workspace = decode_private_snapshot(envelope)
    learning_rows = content.get("learningObservations", [])
    try:
        events = [json.loads(row["canonical"]) for row in learning_rows]
    except (KeyError, TypeError, json.JSONDecodeError) as error:
        raise Refusal("telemetry snapshot learning observations are malformed") from error

    observation_kinds = {"learn-task-snapshot", "learn-context-manifest", "learn-experiment-assignment"}
    supported_kinds = observation_kinds | {
        "learn-accounting-inventory/1", "runtime-native-inventory/1",
        "runtime-native-inventory-source/1", "learn-shared-cost/1",
        "learn-shared-cost-allocation/1", "learn-shared-cost-authority/1"
    }
    unknown_kinds = {event.get("kind") for event in events} - supported_kinds
    if unknown_kinds:
        raise Refusal("private snapshot contains unsupported learning fact kinds: " + ", ".join(sorted(unknown_kinds)))
    observation_events = [event for event in events if event.get("kind") in observation_kinds]
    assignments = {
        event.get("itemId"): event
        for event in observation_events
        if event.get("kind") == "learn-experiment-assignment"
    }
    if not assignments or None in assignments:
        raise Refusal("private snapshot has no stable experiment assignments")
    roots = [
        {
            "itemId": item,
            "originalItemId": item,
            "parentItemId": None,
            "assignedArm": assignment.get("arm"),
            "assignedAt": assignment.get("assignedAt"),
        }
        for item, assignment in assignments.items()
    ]
    observation_result = validate_observations(
        {"contractId": contract.get("contractId"), "items": roots},
        {"schema": "fsgg.learn.observation-snapshot/v1", "workspaceId": workspace, "events": observation_events},
    )
    snapshot_schema = content.get("learningSnapshotSchema")
    is_v3 = snapshot_schema in {
        "fsgg.telemetry.learn-item-detail/3", "fsgg.telemetry.learn-item-detail/4"
    }
    is_v4 = snapshot_schema == "fsgg.telemetry.learn-item-detail/4"
    ingest_order = {}
    receipt_provenance = {}
    if is_v3:
        seen_orders = set()
        for row, event in zip(learning_rows, events):
            canonical = row.get("canonical")
            digest = row.get("content_digest")
            order = row.get("ingest_order")
            if (not isinstance(canonical, str) or
                    digest != hashlib.sha256(canonical.encode()).hexdigest()):
                raise Refusal("v3 learning fact digest does not bind canonical bytes")
            if not isinstance(event.get("identity"), str) or not event["identity"]:
                raise Refusal("v3 learning fact identity is malformed")
            if order is not None:
                if not isinstance(order, int) or isinstance(order, bool) or order < 1 or order in seen_orders:
                    raise Refusal("v3 learning facts require unique retained ingest order")
                seen_orders.add(order)
            ingest_order[event["identity"]] = order
            if is_v4:
                fields = (
                    row.get("receipt_producer"), row.get("receipt_stream"), row.get("receipt_role"),
                    row.get("receipt_grant_id"), row.get("receipt_grant_generation"),
                    row.get("receipt_key"), row.get("receipt_envelope_digest"),
                )
                if all(value is None for value in fields):
                    receipt_provenance[event["identity"]] = None
                else:
                    producer, stream, role, grant, generation, receipt_key, envelope_digest = fields
                    if (not all(isinstance(value, str) and value for value in
                                (producer, stream, role, receipt_key, envelope_digest)) or
                            role not in {"generic", "native-collector"} or
                            not re.fullmatch(r"[0-9a-f]{64}", receipt_key) or
                            not re.fullmatch(r"[0-9a-f]{64}", envelope_digest) or
                            ((grant is None) != (generation is None)) or
                            (grant is not None and
                             (not isinstance(grant, str) or not grant or
                              not isinstance(generation, int) or isinstance(generation, bool) or generation < 1))):
                        raise Refusal("v4 learning fact receipt provenance is malformed")
                    receipt_provenance[event["identity"]] = fields
            else:
                receipt_provenance[event["identity"]] = None

    original_by_item = {item: item for item in assignments}
    for row in content.get("populations", []):
        item = row.get("item_id")
        original = row.get("original_item_id")
        if isinstance(item, str) and isinstance(original, str):
            if original not in assignments:
                raise Refusal(f"{item}: population references an unassigned original item")
            prior = original_by_item.setdefault(item, original)
            if prior != original:
                raise Refusal(f"{item}: conflicting original-item population")

    incomplete = defaultdict(set)
    totals = defaultdict(int)
    usage_totals = defaultdict(int)
    invocation_providers = defaultdict(set)
    invocations = {}
    for row in content.get("admissions", []):
        invocation = row.get("invocation_id")
        item = row.get("item_id")
        original = original_by_item.get(item)
        if not isinstance(invocation, str) or not invocation or original is None:
            raise Refusal("runtime admission is not bound to an assigned original item")
        if invocation in invocations:
            raise Refusal("duplicate runtime admission invocation")
        invocations[invocation] = (original, row)

    lineage_by_dispatch = {}
    root_by_invocation = {}
    for row in content.get("lineage", []):
        dispatch = row.get("dispatch_id")
        invocation = row.get("invocation_id")
        original = original_by_item.get(row.get("item_id"))
        if isinstance(dispatch, str) and isinstance(invocation, str) and original is not None:
            value = (invocation, original)
            if dispatch in lineage_by_dispatch and lineage_by_dispatch[dispatch] != value:
                raise Refusal("dispatch has conflicting invocation lineage")
            admitted = invocations.get(invocation)
            if admitted is not None and admitted[0] != original:
                raise Refusal("invocation lineage crosses original-item identity")
            lineage_by_dispatch[dispatch] = value
            root = row.get("root_invocation_id")
            if isinstance(root, str) and root:
                prior_root = root_by_invocation.setdefault(invocation, root)
                if prior_root != root:
                    raise Refusal("invocation has conflicting root lineage")
        else:
            raise Refusal("invocation lineage is not bound to an assigned original item")
    dispatch_original = {}
    dispatch_relation = {}
    for row in content.get("expectedDispatches", []):
        dispatch = row.get("dispatch_id")
        item = row.get("item_id")
        original = original_by_item.get(item)
        if original is None or not isinstance(dispatch, str):
            raise Refusal("expected dispatch is not bound to an assigned original item")
        if dispatch in dispatch_original:
            raise Refusal("duplicate expected dispatch identity")
        dispatch_original[dispatch] = original
        dispatch_relation[dispatch] = row.get("relation")
        lineage = lineage_by_dispatch.get(dispatch)
        if lineage is None:
            incomplete[original].add("missing-expected-invocation:" + dispatch)
        else:
            invocation, lineage_original = lineage
            if lineage_original != original:
                raise Refusal("dispatch lineage crosses original-item identity")
        if lineage is not None and invocation not in invocations:
            incomplete[original].add("missing-expected-admission:" + invocation)

    admitted_originals = {original for original, _ in invocations.values()}
    for original in assignments:
        if original not in admitted_originals:
            incomplete[original].add("missing-root-admission")
        if not is_v3:
            incomplete[original].update({
                "expected-dispatch-population-unproven",
                "expected-native-turn-inventory-unavailable",
                "expected-shared-cost-inventory-unavailable",
                "expected-provider-unavailable",
                "usage-support-unproven",
            })

    terminal = set()
    for row in content.get("terminals", []):
        invocation = row.get("invocation_id")
        admitted = invocations.get(invocation)
        if not isinstance(invocation, str) or admitted is None:
            raise Refusal("runtime terminal has no expected admitted invocation")
        if original_by_item.get(row.get("item_id")) != admitted[0]:
            raise Refusal("runtime terminal crosses original-item identity")
        if invocation in terminal:
            raise Refusal("duplicate runtime terminal invocation")
        terminal.add(invocation)
    gaps = defaultdict(set)
    for row in content.get("runtimeGaps", []):
        invocation = row.get("invocation_id")
        code = row.get("code")
        if isinstance(invocation, str):
            gaps[invocation].add(str(code) if code is not None else "unknown")
        else:
            raise Refusal("runtime usage gap lacks invocation identity")

    observed_usage = Counter()
    observed_providers = defaultdict(set)
    observed_turns = defaultdict(set)
    observed_threads = defaultdict(set)
    seen_usage = set()
    for row in content.get("usage", []):
        identity = row.get("identity")
        invocation = row.get("invocation_id")
        if not isinstance(identity, str) or not identity or identity in seen_usage:
            raise Refusal("runtime usage identities must be unique non-empty strings")
        seen_usage.add(identity)
        if invocation not in invocations:
            raise Refusal(f"{identity}: usage has no expected admitted invocation")
        original, admission = invocations[invocation]
        if original_by_item.get(row.get("item_id")) != original:
            raise Refusal(f"{identity}: usage crosses original-item identity")
        observed_usage[invocation] += 1
        turn_id = row.get("turn_id")
        if is_v3:
            if not isinstance(turn_id, str) or not turn_id:
                incomplete[original].add("missing-native-turn-identity:" + identity)
            elif turn_id in observed_turns[invocation]:
                raise Refusal("duplicate native turn identity: " + turn_id)
            else:
                observed_turns[invocation].add(turn_id)
            thread_id = row.get("thread_id")
            if not isinstance(thread_id, str) or not thread_id:
                incomplete[original].add("missing-native-thread-identity:" + identity)
            else:
                observed_threads[invocation].add(thread_id)
        total = row.get("total")
        provider = row.get("provider")
        requested_model = row.get("requested_model")
        observed_model = row.get("observed_model")
        requested_effort = row.get("requested_effort")
        observed_effort = row.get("observed_effort")
        if not isinstance(total, int) or isinstance(total, bool) or total < 0:
            incomplete[original].add("invalid-provider-total:" + identity)
        elif not all(isinstance(value, str) and value for value in
                     (provider, requested_model, observed_model, requested_effort, observed_effort)):
            incomplete[original].add("missing-provider-or-profile:" + identity)
        elif (requested_model != observed_model or requested_effort != observed_effort or
              admission.get("requested_model") != requested_model or
              admission.get("requested_effort") != requested_effort):
            incomplete[original].add("provider-profile-mismatch:" + identity)
        else:
            totals[original] += total
            usage_totals[invocation] += total
            invocation_providers[invocation].add(provider)
            observed_providers[original].add(provider)

    for invocation, (original, _) in invocations.items():
        if invocation not in terminal:
            incomplete[original].add("nonterminal-invocation:" + invocation)
        if observed_usage[invocation] == 0:
            incomplete[original].add("missing-provider-usage:" + invocation)
        for code in gaps[invocation]:
            incomplete[original].add("unsupported-usage:" + code)
    for original, providers in observed_providers.items():
        if len(providers) > 1:
            incomplete[original].add("provider-mismatch")
    for invocation in gaps.keys() - invocations.keys():
        raise Refusal("runtime usage gap has no expected admitted invocation: " + invocation)

    for row in content.get("ciRuns", []):
        original = original_by_item.get(row.get("item_id"))
        if original is None:
            raise Refusal("CI run is not bound to an assigned original item")
        if row.get("status") != "completed":
            incomplete[original].add("incomplete-ci-run:" + str(row.get("run_id")))
    ci_coverage = Counter()
    for row in content.get("ciPopulationCoverage", []):
        original = original_by_item.get(row.get("item_id"))
        if original is None:
            raise Refusal("CI population coverage is not bound to an assigned original item")
        dimensions = ("actions", "checks", "attempts", "jobs", "terminal", "timestamps")
        if any(row.get(name) != "complete" for name in dimensions) or row.get("continuation") != "none":
            incomplete[original].add("incomplete-ci-population")
        ci_coverage[original] += 1

    if is_v3:
        valid_digest = lambda value: (isinstance(value, str) and len(value) == 64 and
                                      value != "0" * 64 and
                                      all(character in "0123456789abcdef" for character in value))
        accounting_by_original = {}
        native_pages = defaultdict(dict)
        native_sources = {}
        shared_by_cost = {}
        shared_allocation_by_cost = {}
        shared_authority_by_cost = {}
        for event in events:
            kind = event.get("kind")
            if kind == "learn-accounting-inventory/1":
                original = event.get("itemId")
                if original not in assignments or original in accounting_by_original:
                    raise Refusal("accounting inventory must be unique per assigned original item")
                if (event.get("scope") != "whole-original-item" or
                        event.get("sourceKind") != "prospective-independent-roster" or
                        not valid_digest(event.get("sourceDigest")) or
                        event.get("policyId") != assignments[original].get("policyId") or
                        event.get("windowId") != assignments[original].get("windowId")):
                    raise Refusal(f"{original}: accounting inventory disagrees with persisted assignment")
                dispatches = event.get("expectedDispatchIds")
                shared = event.get("expectedSharedCostIds")
                if (not isinstance(dispatches, list) or len(dispatches) != len(set(dispatches)) or
                        not all(isinstance(value, str) and value for value in dispatches) or
                        not isinstance(shared, list) or len(shared) != len(set(shared)) or
                        not all(isinstance(value, str) and value for value in shared)):
                    raise Refusal(f"{original}: accounting inventory rosters are malformed")
                ci = event.get("ciApplicability")
                if ci not in ("required", "not-applicable"):
                    raise Refusal(f"{original}: accounting inventory lacks explicit CI applicability")
                try:
                    assigned = datetime.fromisoformat(assignments[original]["assignedAt"].replace("Z", "+00:00"))
                    cutoff = datetime.fromisoformat(event["cutoffAt"].replace("Z", "+00:00"))
                    captured = datetime.fromisoformat(event["capturedAt"].replace("Z", "+00:00"))
                except (KeyError, AttributeError, ValueError) as error:
                    raise Refusal(f"{original}: accounting inventory timestamps are malformed") from error
                if (assigned.tzinfo is None or cutoff.tzinfo is None or captured.tzinfo is None or
                        captured > assigned or cutoff <= assigned):
                    raise Refusal(f"{original}: accounting capture must precede assignment and cutoff must follow it")
                accounting_by_original[original] = event
            elif kind == "runtime-native-inventory/1":
                invocation = event.get("invocationId")
                original = event.get("originalItemId")
                item_original = original_by_item.get(event.get("itemId"))
                if invocation not in invocations or original != item_original or invocations[invocation][0] != original:
                    raise Refusal("native inventory crosses invocation or original-item identity")
                page = event.get("page")
                pages = event.get("pages")
                if (not isinstance(page, int) or isinstance(page, bool) or not isinstance(pages, int) or
                        isinstance(pages, bool) or page < 1 or pages < 1 or page > pages):
                    raise Refusal("native inventory paging is malformed")
                if not valid_digest(event.get("sourceDigest")):
                    raise Refusal("native inventory source digest is malformed")
                if page in native_pages[invocation]:
                    raise Refusal("duplicate native inventory page")
                native_pages[invocation][page] = event
            elif kind == "runtime-native-inventory-source/1":
                invocation = event.get("invocationId")
                original = event.get("originalItemId")
                if (invocation not in invocations or original != original_by_item.get(event.get("itemId")) or
                        invocations[invocation][0] != original or invocation in native_sources or
                        not valid_digest(event.get("sourceDigest"))):
                    raise Refusal("native inventory source crosses identity or is duplicated")
                source_binding = event.get("sourceBinding")
                if (not isinstance(source_binding, dict) or set(source_binding) != {
                        "schema", "producerIdentity", "sha256", "bytesBase64"} or
                        source_binding.get("schema") != "fsgg.telemetry.native-inventory-source-binding/1" or
                        source_binding.get("producerIdentity") != "fsgg-work-roadmap-native-collector/1" or
                        not valid_digest(source_binding.get("sha256"))):
                    raise Refusal("native inventory source binding envelope is malformed")
                try:
                    binding_bytes = base64.b64decode(source_binding["bytesBase64"], validate=True)
                    binding = json.loads(binding_bytes)
                except (TypeError, ValueError, UnicodeError, json.JSONDecodeError) as error:
                    raise Refusal("native inventory source binding bytes are malformed") from error
                fields = {"schema", "producerIdentity", "capturedAt", "hostSource", "rootInvocationId",
                          "invocationId", "parentThreadId", "threadId", "orderedTurnIds", "revision"}
                if (not binding_bytes or len(binding_bytes) > 16384 or not isinstance(binding, dict) or
                        set(binding) != fields or hashlib.sha256(binding_bytes).hexdigest() != source_binding["sha256"] or
                        json.dumps(binding, separators=(",", ":"), sort_keys=True).encode("ascii") != binding_bytes or
                        binding.get("schema") != source_binding["schema"] or
                        binding.get("producerIdentity") != source_binding["producerIdentity"] or
                        binding.get("hostSource") != "codex-app-server:thread/turns/list" or
                        binding.get("invocationId") != invocation or
                        binding.get("revision") != event.get("revision") or
                        not isinstance(binding.get("rootInvocationId"), str) or not binding["rootInvocationId"] or
                        not isinstance(binding.get("parentThreadId"), str) or not binding["parentThreadId"] or
                        not isinstance(binding.get("threadId"), str) or not binding["threadId"] or
                        not isinstance(binding.get("orderedTurnIds"), list) or
                        len(binding["orderedTurnIds"]) != len(set(binding["orderedTurnIds"])) or
                        not all(isinstance(turn, str) and turn for turn in binding["orderedTurnIds"])):
                    raise Refusal("native inventory source binding is not canonical or internally bound")
                native_sources[invocation] = (event, binding)
            elif kind == "learn-shared-cost/1":
                cost = event.get("nativeCostId")
                if not isinstance(cost, str) or not cost or cost in shared_by_cost:
                    raise Refusal("shared native cost identities must be unique")
                if event.get("sourceKind") != "native-shared-cost" or not valid_digest(event.get("sourceDigest")):
                    raise Refusal("shared native cost provenance is malformed")
                shared_by_cost[cost] = event
            elif kind == "learn-shared-cost-authority/1":
                cost = event.get("nativeCostId")
                if not isinstance(cost, str) or not cost or cost in shared_authority_by_cost:
                    raise Refusal("shared cost authority identities must be unique")
                if (event.get("sourceKind") != "retained-native-shared-cost-source" or
                        not all(isinstance(event.get(name), str) and event[name] for name in
                                ("sourceInventoryId", "sourceInvocationId")) or
                        not valid_digest(event.get("sourceDigest"))):
                    raise Refusal("shared cost authority is malformed")
                shared_authority_by_cost[cost] = event
            elif kind == "learn-shared-cost-allocation/1":
                cost = event.get("nativeCostId")
                if not isinstance(cost, str) or not cost or cost in shared_allocation_by_cost:
                    raise Refusal("shared cost allocation identities must be unique")
                roster = event.get("allocationRoster")
                if (event.get("allocationRule") != "equal-largest-remainder-v1" or
                        not isinstance(roster, list) or not roster or roster != sorted(roster) or
                        len(roster) != len(set(roster)) or
                        not all(isinstance(value, str) and value for value in roster) or
                        not all(isinstance(event.get(name), str) and event[name] for name in
                                ("policyId", "windowId", "frozenAt"))):
                    raise Refusal("shared cost allocation is malformed")
                shared_allocation_by_cost[cost] = event

        for original in assignments:
            inventory = accounting_by_original.get(original)
            if inventory is None:
                incomplete[original].add("missing-accounting-inventory")
                continue
            expected_dispatches = set(inventory["expectedDispatchIds"])
            observed_dispatches = {value for value, owner in dispatch_original.items() if owner == original}
            if expected_dispatches != observed_dispatches:
                incomplete[original].add("expected-dispatch-roster-mismatch")
            expected_shared = set(inventory["expectedSharedCostIds"])
            observed_shared = {
                cost for cost, event in shared_by_cost.items()
                if any(allocation.get("originalItemId") == original for allocation in event.get("allocations", []))
            }
            if expected_shared != observed_shared:
                incomplete[original].add("expected-shared-cost-roster-mismatch")
            if inventory["ciApplicability"] == "required" and ci_coverage[original] == 0:
                incomplete[original].add("missing-ci-population-coverage")
            if inventory["ciApplicability"] == "not-applicable" and ci_coverage[original] != 0:
                incomplete[original].add("unexpected-ci-population-coverage")

        expected_invocations = {lineage_by_dispatch[dispatch][0] for dispatch in dispatch_original if dispatch in lineage_by_dispatch}
        if expected_invocations != set(invocations):
            for original in assignments:
                incomplete[original].add("expected-invocation-population-mismatch")
        for invocation, (original, admission) in invocations.items():
            pages = native_pages.get(invocation, {})
            if not pages:
                incomplete[original].add("missing-native-inventory:" + invocation)
                continue
            page_counts = {event.get("pages") for event in pages.values()}
            if len(page_counts) != 1 or set(pages) != set(range(1, next(iter(page_counts)) + 1)):
                incomplete[original].add("incomplete-native-inventory-pages:" + invocation)
                continue
            first = pages[1]
            stable = ("revision", "inventoryId", "originalItemId", "invocationId", "expectedProvider", "requestedModel",
                      "requestedEffort", "support", "followupBaseline", "capturedAt", "sourceKind", "sourceDigest")
            if any(any(event.get(name) != first.get(name) for name in stable) for event in pages.values()):
                raise Refusal("native inventory pages disagree on stable provenance")
            if (first.get("support") != "provider-native-final-turn-counters" or
                    first.get("sourceKind") != "provider-capability-and-dispatch-roster"):
                incomplete[original].add("usage-support-unproven:" + invocation)
            accounting = accounting_by_original.get(original)
            if accounting is None:
                incomplete[original].add("native-inventory-cutoff-unavailable:" + invocation)
                continue
            try:
                captured = datetime.fromisoformat(first["capturedAt"].replace("Z", "+00:00"))
                assigned = datetime.fromisoformat(assignments[original]["assignedAt"].replace("Z", "+00:00"))
                cutoff = datetime.fromisoformat(accounting["cutoffAt"].replace("Z", "+00:00"))
            except (KeyError, AttributeError, ValueError) as error:
                raise Refusal("native inventory capture timestamp is malformed") from error
            if captured.tzinfo is None or captured < assigned or captured > cutoff:
                raise Refusal("native inventory capture must fall between assignment and cutoff")
            expected_turns = []
            for page in sorted(pages):
                turns = pages[page].get("expectedTurnIds")
                if not isinstance(turns, list) or not turns or not all(isinstance(turn, str) and turn for turn in turns):
                    incomplete[original].add("missing-expected-turns:" + invocation)
                    turns = []
                expected_turns.extend(turns)
            if len(expected_turns) != len(set(expected_turns)):
                raise Refusal("native inventory repeats a turn across pages")
            if set(expected_turns) != observed_turns[invocation]:
                incomplete[original].add("expected-native-turn-roster-mismatch:" + invocation)
            expected_provider = first.get("expectedProvider")
            if (not isinstance(expected_provider, str) or not expected_provider or
                    observed_providers[original] != {expected_provider}):
                incomplete[original].add("provider-mismatch:" + invocation)
            if (admission.get("requested_model") != first.get("requestedModel") or
                    admission.get("requested_effort") != first.get("requestedEffort")):
                incomplete[original].add("requested-profile-mismatch:" + invocation)
            followups = sum(1 for dispatch, owner in dispatch_original.items()
                            if owner == original and dispatch_relation.get(dispatch) == "follow-up")
            baseline = first.get("followupBaseline")
            if not isinstance(baseline, int) or isinstance(baseline, bool) or baseline < 0:
                raise Refusal("native inventory follow-up baseline is malformed")
            if baseline != followups:
                incomplete[original].add("followup-baseline-mismatch:" + invocation)
            authority = native_sources.get(invocation)
            if authority is None:
                incomplete[original].add("independent-inventory-source-unavailable")
            else:
                source, binding = authority
                if (source.get("inventoryId") != first.get("inventoryId") or
                        source.get("sourceDigest") != first.get("sourceDigest") or
                        source.get("revision") != first.get("revision") or
                        binding.get("capturedAt") != first.get("capturedAt") or
                        binding.get("orderedTurnIds", [])[baseline:] != expected_turns or
                        root_by_invocation.get(invocation) != binding.get("rootInvocationId")):
                    raise Refusal("native inventory source authority disagrees with inventory or lineage")
                if observed_threads[invocation] != {binding.get("threadId")}:
                    incomplete[original].add("native-source-thread-mismatch:" + invocation)

        for invocation in native_sources.keys() - native_pages.keys():
            raise Refusal("native inventory source has no matching inventory: " + invocation)

        expected_all_shared = set().union(*(
            set(event["expectedSharedCostIds"]) for event in accounting_by_original.values()
        )) if accounting_by_original else set()
        if expected_all_shared != set(shared_by_cost):
            for original in assignments:
                incomplete[original].add("shared-cost-population-mismatch")
        used_shared_invocations = set()
        for cost, event in shared_by_cost.items():
            allocations = event.get("allocations")
            total = event.get("providerTotalTokens")
            provider = event.get("provider")
            if (not isinstance(total, int) or isinstance(total, bool) or total < 0 or
                    not isinstance(allocations, list) or not allocations):
                raise Refusal(cost + ": shared cost is malformed")
            seen_originals = set()
            allocated = 0
            for allocation in allocations:
                original = allocation.get("originalItemId")
                tokens = allocation.get("tokens")
                if (original not in assignments or original in seen_originals or not isinstance(tokens, int) or
                        isinstance(tokens, bool) or tokens < 0):
                    raise Refusal(cost + ": shared allocation is malformed or crosses original-item identity")
                seen_originals.add(original)
                allocated += tokens
            if allocated != total:
                raise Refusal(cost + ": shared allocations do not equal provider total")
            authority = shared_authority_by_cost.get(cost)
            allocation = shared_allocation_by_cost.get(cost)
            if authority is None or allocation is None:
                for original in seen_originals:
                    incomplete[original].add("independent-shared-cost-authority-unavailable")
                continue
            roster = allocation["allocationRoster"]
            if set(roster) != seen_originals:
                raise Refusal(cost + ": frozen roster disagrees with allocations")
            if (event.get("itemId") not in seen_originals or authority.get("itemId") not in seen_originals or
                    allocation.get("itemId") not in seen_originals):
                raise Refusal(cost + ": shared cost evidence is not retained by an allocated original item")
            try:
                frozen = datetime.fromisoformat(allocation["frozenAt"].replace("Z", "+00:00"))
                assigned_times = {
                    original: datetime.fromisoformat(assignments[original]["assignedAt"].replace("Z", "+00:00"))
                    for original in roster
                }
            except (KeyError, AttributeError, ValueError) as error:
                raise Refusal(cost + ": authority freeze time is malformed") from error
            if frozen.tzinfo is None or any(assigned.tzinfo is None or frozen > assigned for assigned in assigned_times.values()):
                raise Refusal(cost + ": allocation authority must be frozen before assignment")
            allocation_order = ingest_order[allocation["identity"]]
            assignment_orders = [ingest_order[assignments[original]["identity"]] for original in roster]
            if allocation_order is None or any(order is None for order in assignment_orders):
                for original in seen_originals:
                    incomplete[original].add("prospective-allocation-order-unavailable")
                continue
            if any(allocation_order >= order for order in assignment_orders):
                raise Refusal(cost + ": retained allocation was not persisted before assignment")
            if any(assignments[original].get("policyId") != allocation["policyId"] or
                   assignments[original].get("windowId") != allocation["windowId"] or
                   cost not in accounting_by_original.get(original, {}).get("expectedSharedCostIds", [])
                   for original in roster):
                raise Refusal(cost + ": allocation authority disagrees with prospective accounting")
            invocation = authority["sourceInvocationId"]
            if invocation in used_shared_invocations:
                raise Refusal("shared native invocation is allocated more than once: " + invocation)
            used_shared_invocations.add(invocation)
            admitted = invocations.get(invocation)
            pages = native_pages.get(invocation, {})
            source = native_sources.get(invocation)
            if admitted is None or admitted[0] not in seen_originals or not pages or source is None:
                for original in seen_originals:
                    incomplete[original].add("independent-shared-cost-authority-unavailable")
                continue
            first = pages.get(1)
            source_event, _ = source
            source_order = ingest_order[source_event["identity"]]
            authority_order = ingest_order[authority["identity"]]
            cost_order = ingest_order[event["identity"]]
            if source_order is None or authority_order is None or cost_order is None:
                for original in seen_originals:
                    incomplete[original].add("independent-shared-cost-authority-unavailable")
                continue
            if not (source_order < authority_order < cost_order):
                raise Refusal(cost + ": retained native source, authority and cost are out of order")
            if (first is None or authority["sourceInventoryId"] != first.get("inventoryId") or
                    authority["sourceDigest"] != first.get("sourceDigest") or
                    authority["sourceDigest"] != source_event.get("sourceDigest") or
                    event.get("sourceDigest") != authority["sourceDigest"]):
                raise Refusal(cost + ": authority does not bind the retained native source")
            source_failures = {
                reason for reason in incomplete[admitted[0]]
                if invocation in reason or reason == "independent-inventory-source-unavailable"
            }
            if (invocation not in terminal or observed_usage[invocation] == 0 or gaps[invocation] or
                    source_failures):
                for original in seen_originals:
                    incomplete[original].add("shared-source-incomplete:" + invocation)
                continue
            if invocation_providers[invocation] != {provider}:
                raise Refusal(cost + ": authority provider disagrees with retained native usage")
            source_total = usage_totals[invocation]
            if source_total != total:
                raise Refusal(cost + ": provider total disagrees with retained native usage")
            quotient, remainder = divmod(total, len(roster))
            expected_allocations = {
                original: quotient + (1 if index < remainder else 0)
                for index, original in enumerate(roster)
            }
            actual_allocations = {row["originalItemId"]: row["tokens"] for row in allocations}
            if actual_allocations != expected_allocations:
                raise Refusal(cost + ": allocations disagree with frozen equal allocation rule")
            source_provenance = receipt_provenance[source_event["identity"]]
            authority_provenance = receipt_provenance[authority["identity"]]
            protected_collector = (
                source_provenance is not None and authority_provenance is not None and
                source_provenance[2] == "native-collector" and
                authority_provenance[2] == "native-collector" and
                source_provenance[3:5] == authority_provenance[3:5]
            )
            for original in seen_originals:
                incomplete[original].add("independent-shared-cost-authority-unavailable")
                incomplete[original].add("snapshot-origin-unverified")
                if protected_collector:
                    incomplete[original].add("native-source-verification-unavailable")
                else:
                    incomplete[original].add("collector-principal-unavailable")

        for cost in shared_authority_by_cost.keys() - shared_by_cost.keys():
            raise Refusal("shared cost authority has no matching cost: " + cost)
        for cost in shared_allocation_by_cost.keys() - shared_by_cost.keys():
            raise Refusal("shared cost allocation has no matching cost: " + cost)

    arms = {item: assignment.get("arm") for item, assignment in assignments.items()}
    arm_totals = {arm: [] for arm in ("current", "focused")}
    for item, arm in arms.items():
        if arm not in arm_totals:
            raise Refusal(f"{item}: invalid persisted assignment arm")
        if not incomplete[item]:
            arm_totals[arm].append(totals[item])
    incomplete_items = sorted(item for item in assignments if incomplete[item])
    return {
        "schema": "fsgg.learn.private-snapshot-summary/v1",
        "contractId": contract["contractId"],
        "workspaceId": workspace,
        "snapshotRevision": envelope["revision"],
        **observation_result,
        "assignedOriginalItemsByArm": {
            arm: sum(1 for value in arms.values() if value == arm) for arm in ("current", "focused")
        },
        "completeTokenItemsByArm": {arm: len(values) for arm, values in arm_totals.items()},
        "providerTotalTokensByArm": {arm: sum(values) for arm, values in arm_totals.items()},
        "providerTotalTokensByOriginalItem": {
            item: totals[item] for item in sorted(assignments) if not incomplete[item]
        },
        "incompleteTokenOriginalItems": incomplete_items,
        "incompleteTokenReasons": {item: sorted(incomplete[item]) for item in incomplete_items},
        "tokenComparisonQualified": not incomplete_items,
        "qualificationPrerequisite": None if not incomplete_items else (
            "complete verified evidence plus retained source bytes and producer identity for independent "
            "whole-item inventories and shared-cost allocation authority"
        ),
        "claim": "private-snapshot-analysis-no-efficiency-result",
    }


def validate_corpus(contract: dict, corpus: dict) -> dict:
    if corpus.get("schema") != "fsgg.learn.synthetic-corpus/v1":
        raise Refusal("unsupported corpus schema")
    if corpus.get("contractId") != contract.get("contractId"):
        raise Refusal("corpus contract identity mismatch")
    if corpus.get("analysisUnit") != "original-item":
        raise Refusal("subissues or invocations cannot be independent successes")

    items = corpus.get("items")
    expected_invocations = corpus.get("expectedInvocations")
    expected_shared_costs = corpus.get("expectedSharedCosts")
    costs = corpus.get("costs")
    if not isinstance(items, list) or not items:
        raise Refusal("corpus requires items")
    if not isinstance(expected_invocations, list) or not expected_invocations:
        raise Refusal("corpus requires an expected invocation inventory")
    if not isinstance(expected_shared_costs, list):
        raise Refusal("corpus requires an expected shared-cost inventory")
    if not isinstance(costs, list) or not costs:
        raise Refusal("corpus requires costs")
    if len(items) > 10000 or len(expected_invocations) > 10000 or len(expected_shared_costs) > 10000 or len(costs) > 20000:
        raise Refusal("corpus exceeds bounded issue-analysis input")

    items_by_id = {}
    for item in items:
        item_id = item.get("itemId")
        if not isinstance(item_id, str) or not item_id or item_id in items_by_id:
            raise Refusal("item identities must be unique non-empty strings")
        items_by_id[item_id] = item

    roots = {}
    for item_id, item in items_by_id.items():
        original = item.get("originalItemId")
        parent = item.get("parentItemId")
        if parent is None:
            if original != item_id:
                raise Refusal(f"{item_id}: an original root must own its canonical identity")
            roots[item_id] = item
        else:
            if (parent not in items_by_id or original == item_id or
                    items_by_id[parent].get("originalItemId") != original):
                raise Refusal(f"{item_id}: child lineage does not identify a distinct canonical original")

    if not roots:
        raise Refusal("corpus requires original roots")

    outcomes = Counter()
    required_scenarios = {"failure", "cancellation", "open", "rescue", "delayed-repair", "shared-cost"}
    scenarios = set()
    arm_by_issue = {}
    fixed_14_day_completions = 0
    repair_mature = 0
    repairs_in_mature_followup = 0
    open_censored = []

    def timestamp(value, field, item_id):
        if not isinstance(value, str):
            raise Refusal(f"{item_id}: {field} must be an RFC3339 timestamp")
        try:
            parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
        except ValueError as error:
            raise Refusal(f"{item_id}: invalid {field}") from error
        if parsed.tzinfo is None:
            raise Refusal(f"{item_id}: {field} must include an offset")
        return parsed

    for item_id, item in items_by_id.items():
        original = item.get("originalItemId")
        root = roots.get(original)
        if root is None:
            raise Refusal(f"{item_id}: canonical original is absent")
        arm = item.get("assignedArm")
        if arm not in {"current", "focused"}:
            raise Refusal(f"{item_id}: invalid assigned arm")
        if arm != root.get("assignedArm"):
            raise Refusal(f"{item_id}: child assignment differs from its canonical original")
        expected_source = "persisted-pre-treatment-assignment" if item_id == original else "inherited-original-assignment"
        if item.get("assignmentSource") != expected_source:
            raise Refusal(f"{item_id}: outcome-derived or redrawn assignment is forbidden")
        if item.get("assignmentOrdinal", 0) >= item.get("firstTreatmentWorkOrdinal", 0):
            raise Refusal(f"{item_id}: assignment must precede treatment work")
        outcome = item.get("outcome")
        if outcome not in {"accepted", "failed", "cancelled", "open"}:
            raise Refusal(f"{item_id}: invalid outcome")
        if item_id != original:
            continue
        scenarios.update(item.get("scenarios", []))
        outcome_scenario = {"failed": "failure", "cancelled": "cancellation", "open": "open"}.get(outcome)
        if outcome_scenario:
            scenarios.add(outcome_scenario)
        outcomes[outcome] += 1
        arm_by_issue[item_id] = arm
        assigned = timestamp(item.get("assignedAt"), "assignedAt", item_id)
        cutoff = timestamp(item.get("cutoffAt"), "cutoffAt", item_id)
        if cutoff < assigned:
            raise Refusal(f"{item_id}: cutoff precedes assignment")
        accepted_value = item.get("acceptedAt")
        accepted = timestamp(accepted_value, "acceptedAt", item_id) if accepted_value is not None else None
        repair_value = item.get("materialRepairAt")
        repair = timestamp(repair_value, "materialRepairAt", item_id) if repair_value is not None else None
        if accepted is not None and not assigned <= accepted <= cutoff:
            raise Refusal(f"{item_id}: acceptance falls outside assignment and cutoff")
        if outcome == "accepted" and accepted is None:
            raise Refusal(f"{item_id}: accepted outcome lacks acceptance time")
        if outcome != "accepted" and accepted is not None:
            raise Refusal(f"{item_id}: non-accepted outcome has an acceptance time")
        if repair is not None and (accepted is None or not accepted < repair <= cutoff):
            raise Refusal(f"{item_id}: repair does not follow acceptance before cutoff")
        if "delayed-repair" in item.get("scenarios", []) and repair is None:
            raise Refusal(f"{item_id}: delayed repair scenario lacks a repair time")
        if accepted is not None and accepted <= assigned + timedelta(days=14):
            fixed_14_day_completions += 1
        if accepted is not None and cutoff >= accepted + timedelta(days=30):
            repair_mature += 1
            if repair is not None and repair <= accepted + timedelta(days=30):
                repairs_in_mature_followup += 1
        if outcome == "open":
            open_censored.append(item_id)

    expected_pairs = {}
    expected_by_invocation = {}
    inventoried_items = set()
    root_invocation_items = set()
    for invocation in expected_invocations:
        invocation_id = invocation.get("invocationId")
        item_id = invocation.get("itemId")
        original = invocation.get("originalItemId")
        turns = invocation.get("expectedTurnIds")
        if not isinstance(invocation_id, str) or not invocation_id or invocation_id in expected_by_invocation:
            raise Refusal("expected invocation identities must be unique non-empty strings")
        if item_id not in items_by_id or original != items_by_id[item_id].get("originalItemId"):
            raise Refusal(f"{invocation_id}: invocation lineage disagrees with item inventory")
        if not isinstance(turns, list) or not turns or len(set(turns)) != len(turns) or not all(isinstance(turn, str) and turn for turn in turns):
            raise Refusal(f"{invocation_id}: expected turn identities must be unique non-empty strings")
        if not isinstance(invocation.get("terminal"), bool):
            raise Refusal(f"{invocation_id}: terminal assertion must be boolean")
        if invocation.get("kind") not in {"root", "child", "retry", "review", "integration", "rescue", "repair"}:
            raise Refusal(f"{invocation_id}: invalid invocation kind")
        if invocation.get("kind") == "root":
            if item_id != original:
                raise Refusal(f"{invocation_id}: root invocation belongs to a child item")
            root_invocation_items.add(item_id)
        expected_by_invocation[invocation_id] = invocation
        inventoried_items.add(item_id)
        for turn in turns:
            expected_pairs[(invocation_id, turn)] = original
    if inventoried_items != set(items_by_id):
        raise Refusal("expected invocation inventory does not cover every root and child item")
    if root_invocation_items != set(roots):
        raise Refusal("expected invocation inventory lacks one root invocation per original")

    expected_shared = {}
    for shared in expected_shared_costs:
        cost_id = shared.get("costId")
        if not isinstance(cost_id, str) or not cost_id or cost_id in expected_shared:
            raise Refusal("expected shared-cost identities must be unique non-empty strings")
        allocations = shared.get("allocations")
        if not isinstance(allocations, list) or len(allocations) < 2:
            raise Refusal(f"{cost_id}: expected shared cost requires multiple allocations")
        identities = [allocation.get("originalItemId") for allocation in allocations]
        fractions = [allocation.get("fraction") for allocation in allocations]
        if (len(set(identities)) != len(identities) or any(identity not in roots for identity in identities) or
                any(not isinstance(fraction, (int, float)) or isinstance(fraction, bool) or fraction <= 0 for fraction in fractions) or
                not math.isclose(sum(float(fraction) for fraction in fractions), 1.0, rel_tol=0.0, abs_tol=1e-9)):
            raise Refusal(f"{cost_id}: invalid expected shared-cost allocation")
        expected_shared[cost_id] = shared
        scenarios.add("shared-cost")

    totals = defaultdict(float)
    incomplete_reasons = defaultdict(set)
    cost_ids = set()
    observed_pairs = set()
    observed_shared = set()
    for cost in costs:
        cost_id = cost.get("costId")
        if not isinstance(cost_id, str) or not cost_id or cost_id in cost_ids:
            raise Refusal("cost identities must be unique non-empty strings")
        cost_ids.add(cost_id)
        completeness = cost.get("completeness")
        if completeness == "imputed-zero":
            raise Refusal(f"{cost_id}: unbounded missing tokens cannot be zero")
        if completeness not in {"complete", "unknown-unbounded"}:
            raise Refusal(f"{cost_id}: invalid completeness")
        amount = cost.get("providerTotalTokens")
        expected_provider = cost.get("expectedProvider")
        observed_provider = cost.get("observedProvider")
        usage_support = cost.get("usageSupport")
        if not isinstance(expected_provider, str) or not expected_provider:
            raise Refusal(f"{cost_id}: expected provider evidence is required")
        if not isinstance(observed_provider, str) or not observed_provider:
            raise Refusal(f"{cost_id}: observed provider evidence is required")
        if not isinstance(usage_support, str) or not usage_support:
            raise Refusal(f"{cost_id}: usage support evidence is required")
        if completeness == "complete" and (not isinstance(amount, int) or isinstance(amount, bool) or amount < 0):
            raise Refusal(f"{cost_id}: complete provider total must be a non-negative integer")
        if completeness == "unknown-unbounded" and amount is not None:
            raise Refusal(f"{cost_id}: unknown usage cannot carry a guessed amount")
        allocations = cost.get("allocations")
        if not isinstance(allocations, list) or not allocations:
            raise Refusal(f"{cost_id}: missing allocation")
        fraction_sum = 0.0
        allocated_items = set()
        for allocation in allocations:
            item = allocation.get("originalItemId")
            fraction = allocation.get("fraction")
            if item not in roots or item in allocated_items:
                raise Refusal(f"{cost_id}: invalid or duplicate original-item allocation")
            if not isinstance(fraction, (int, float)) or isinstance(fraction, bool) or fraction <= 0:
                raise Refusal(f"{cost_id}: invalid allocation fraction")
            allocated_items.add(item)
            fraction_sum += float(fraction)
            if completeness == "complete":
                totals[item] += amount * float(fraction)
            else:
                incomplete_reasons[item].add("unknown-unbounded-usage")
            if observed_provider != expected_provider:
                incomplete_reasons[item].add("provider-mismatch")
            if usage_support != "supported":
                incomplete_reasons[item].add("unsupported-usage:" + str(usage_support))
        if not math.isclose(fraction_sum, 1.0, rel_tol=0.0, abs_tol=1e-9):
            raise Refusal(f"{cost_id}: shared-cost fractions must sum to one")
        if len(allocations) > 1:
            scenarios.add("shared-cost")
        kind = cost.get("kind")
        if kind == "invocation":
            pair = (cost.get("invocationId"), cost.get("turnId"))
            if pair not in expected_pairs:
                raise Refusal(f"{cost_id}: usage does not match an expected invocation turn")
            if pair in observed_pairs:
                raise Refusal(f"{cost_id}: duplicate usage for one invocation turn")
            observed_pairs.add(pair)
            if allocated_items != {expected_pairs[pair]} or not math.isclose(fraction_sum, 1.0):
                raise Refusal(f"{cost_id}: invocation usage must belong wholly to its canonical original")
        elif kind == "shared":
            if cost_id not in expected_shared or cost_id in observed_shared:
                raise Refusal(f"{cost_id}: unexpected or duplicate shared cost")
            observed_shared.add(cost_id)
            expected_allocations = expected_shared[cost_id].get("allocations")
            if allocations != expected_allocations:
                raise Refusal(f"{cost_id}: shared allocation differs from expected inventory")
        else:
            raise Refusal(f"{cost_id}: invalid cost kind")

    for pair, original in expected_pairs.items():
        invocation = expected_by_invocation[pair[0]]
        if pair not in observed_pairs:
            incomplete_reasons[original].add("missing-expected-usage:" + "/".join(pair))
        if not invocation["terminal"]:
            incomplete_reasons[original].add("nonterminal-invocation:" + pair[0])
    for cost_id, shared in expected_shared.items():
        if cost_id not in observed_shared:
            for allocation in shared.get("allocations", []):
                original = allocation.get("originalItemId")
                if original in roots:
                    incomplete_reasons[original].add("missing-expected-shared-cost:" + cost_id)

    missing = required_scenarios - scenarios
    if missing:
        raise Refusal("synthetic scenarios missing: " + ", ".join(sorted(missing)))

    arm_totals = {arm: [] for arm in ("current", "focused")}
    incomplete = []
    for item in sorted(roots):
        if not incomplete_reasons[item]:
            arm_totals[arm_by_issue[item]].append(totals[item])
        else:
            incomplete.append(item)
    return {
        "schema": "fsgg.learn.synthetic-summary/v1",
        "contractId": contract["contractId"],
        "originalItems": len(roots),
        "childItems": len(items_by_id) - len(roots),
        "outcomes": dict(sorted(outcomes.items())),
        "fixed14DayCompletions": fixed_14_day_completions,
        "repairMatureAcceptedItems": repair_mature,
        "materialRepairsInMatureFollowup": repairs_in_mature_followup,
        "openCensoredOriginalItems": sorted(open_censored),
        "completeTokenItemsByArm": {arm: len(values) for arm, values in arm_totals.items()},
        "providerTotalTokensByArm": {arm: int(sum(values)) for arm, values in arm_totals.items()},
        "providerTotalTokensByOriginalItem": {item: int(totals[item]) for item in sorted(roots) if not incomplete_reasons[item]},
        "incompleteTokenOriginalItems": incomplete,
        "incompleteTokenReasons": {item: sorted(incomplete_reasons[item]) for item in incomplete},
        "tokenComparisonQualified": not incomplete,
        "claim": "synthetic-fixture-only-no-efficiency-result"
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("contract", type=pathlib.Path)
    parser.add_argument("corpus", type=pathlib.Path, nargs="?")
    parser.add_argument("--observations", type=pathlib.Path)
    parser.add_argument("--output", type=pathlib.Path)
    args = parser.parse_args(argv)
    try:
        contract = load(args.contract)
        validate_contract(contract)
        observation_input = load(args.observations) if args.observations else None
        if observation_input and observation_input.get("schema") == "fsgg.telemetry.item-detail/2":
            if args.corpus is not None:
                raise Refusal("private snapshot analysis refuses a second unbound corpus input")
            result = analyze_private_snapshot(contract, observation_input)
        else:
            if args.corpus is None:
                raise Refusal("synthetic analysis requires a corpus")
            corpus = load(args.corpus)
            result = validate_corpus(contract, corpus)
            if observation_input:
                result.update(validate_observations(corpus, observation_input))
    except (OSError, json.JSONDecodeError, Refusal) as error:
        print(f"refused: {error}", file=sys.stderr)
        return 2
    rendered = json.dumps(result, indent=2, sort_keys=True) + "\n"
    if len(rendered.encode("utf-8")) > 1024 * 1024:
        print("refused: analysis output exceeds 1048576 bytes", file=sys.stderr)
        return 2
    if args.output:
        args.output.write_text(rendered, encoding="utf-8")
    else:
        print(rendered, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
