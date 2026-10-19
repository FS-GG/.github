"""Read-only interpretation of existing board CLI/native qualification results.

No command dispatch, write, retry, enrollment or acceptance authority lives here.
Preservation covers the complete reported snapshot, including opaque system-field
identities; it cannot establish hidden contents the native collector did not read.
"""
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import stat

MAX_BYTES = 4 * 1024 * 1024


class Unknown(ValueError):
    """Original result is unusable; reconcile its original identity without resend."""


def require(condition, reason):
    if not condition:
        raise Unknown(reason)


def closed(value, keys):
    require(isinstance(value, dict) and set(value) == set(keys), "unsupported-result-shape")
    return value


def text(value):
    require(isinstance(value, str) and bool(value), "missing-native-text")
    return value


def timestamp(value):
    require(isinstance(value, str) and re.fullmatch(r"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|\+00:00)", value) is not None, "invalid-native-timestamp")
    try:
        datetime.datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as error:
        raise Unknown("invalid-native-timestamp") from error
    return value


def _pairs(pairs):
    require(len({key for key, _ in pairs}) == len(pairs), "duplicate-json-property")
    return dict(pairs)


def loads(raw):
    require(isinstance(raw, (bytes, str)) and len(raw if isinstance(raw, bytes) else raw.encode()) <= MAX_BYTES, "result-byte-bound")
    try:
        if isinstance(raw, bytes): raw = raw.decode("utf-8", errors="strict")
        require(not raw.startswith("\ufeff"), "json-bom-is-unsupported")
        value = json.loads(raw, object_pairs_hook=_pairs, parse_constant=lambda _: (_ for _ in ()).throw(Unknown("nonfinite-json-number")))
    except (ValueError, UnicodeError, RecursionError) as error:
        raise Unknown("invalid-original-json") from error
    def bounded(node, depth=0):
        require(depth <= 16, "result-depth-bound")
        if isinstance(node, dict):
            for child in node.values(): bounded(child, depth + 1)
        elif isinstance(node, list):
            for child in node: bounded(child, depth + 1)
    bounded(value)
    return value


def read_original(path, expected_sha256=None):
    """Read one bounded original on a non-symlink opened handle, without rewriting it."""
    path = Path(path)
    require(path.is_absolute() and str(path) == os.path.normpath(str(path)) and all(not parent.is_symlink() for parent in (path, *path.parents)), "original-path-not-canonical")
    try:
        with os.fdopen(os.open(path, os.O_RDONLY | os.O_NOFOLLOW), "rb") as stream:
            before = os.fstat(stream.fileno())
            require(stat.S_ISREG(before.st_mode) and before.st_size <= MAX_BYTES, "original-file-bound")
            raw = stream.read(MAX_BYTES + 1)
            after = os.fstat(stream.fileno())
            require((before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns) ==
                    (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns) and len(raw) == before.st_size, "original-file-changed")
    except OSError as error:
        raise Unknown("original-file-unavailable") from error
    digest = hashlib.sha256(raw).hexdigest()
    require(expected_sha256 is None or digest == expected_sha256, "original-byte-digest-differs")
    return loads(raw), {"path": str(path), "sha256": digest}


SCALAR_KEYS = {"ProjectV2ItemFieldTextValue": ("text",),
               "ProjectV2ItemFieldSingleSelectValue": ("optionId", "name")}
VALUE_KEYS = {**SCALAR_KEYS,
              "ProjectV2ItemFieldDateValue": ("id", "updatedAt", "date"),
              "ProjectV2ItemFieldNumberValue": ("id", "updatedAt", "number"),
              "ProjectV2ItemFieldIterationValue": ("id", "updatedAt", "iterationId", "title", "startDate", "duration"),
              "ProjectV2ItemFieldMultiSelectValue": ("id", "updatedAt")}
for _kind in ("Repository", "Label", "Milestone", "PullRequest", "Reviewer", "User"):
    VALUE_KEYS["ProjectV2ItemField" + _kind + "Value"] = ()
VALUE_KEYS["ProjectV2ItemIssueFieldValue"] = ()


def decode_field_value(value, expected_field_id):
    """Decode the actual text/select union; absence stays absent, never another union."""
    if value is None:
        return None
    require(isinstance(value, dict), "native-field-shape")
    kind = value.get("__typename")
    require(kind in SCALAR_KEYS, "unsupported-native-field-union")
    identity = {"id", "updatedAt"} if "id" in value or "updatedAt" in value else set()
    closed(value, {"__typename", "field", *SCALAR_KEYS[kind]} | identity)
    closed(value["field"], {"id"})
    require(value["field"]["id"] == expected_field_id, "native-field-identity-differs")
    for name in identity | set(SCALAR_KEYS[kind]):
        if name == "text": require(isinstance(value[name], str), "native-text-union-value-shape")
        else: text(value[name])
    if identity: timestamp(value["updatedAt"])
    return {"kind": kind, "fieldId": expected_field_id, **{name: value[name] for name in SCALAR_KEYS[kind]}}


def _snapshot_value(tokens):
    identity, field_id, kind, canonical = tokens
    require(identity == field_id and kind in VALUE_KEYS, "unsupported-snapshot-field-identity")
    pairs = loads(canonical)
    require(isinstance(pairs, list) and len(pairs) % 2 == 0 and all(isinstance(value, str) for value in pairs), "snapshot-value-pairs")
    properties = _pairs([(pairs[index], loads(pairs[index + 1])) for index in range(0, len(pairs), 2)])
    keys = set(VALUE_KEYS[kind]) | ({"id", "updatedAt"} if kind in SCALAR_KEYS else set())
    closed(properties, keys)
    if kind in SCALAR_KEYS:
        decode_field_value({"__typename": kind, "field": {"id": field_id}, **properties}, field_id)
    return {"tokens": tokens, "properties": properties}


def decode_snapshot(tokens):
    """Decode the existing complete flat snapshot layout, retaining every reported token."""
    require(isinstance(tokens, list) and len(tokens) <= 250000 and all(isinstance(value, str) for value in tokens), "snapshot-token-bound")
    position = 0
    def take(count):
        nonlocal position
        require(0 <= count and position + count <= len(tokens), "partial-snapshot")
        result = tokens[position:position + count]
        position += count
        return result
    def count(maximum=5000):
        value = take(1)[0]
        require(re.fullmatch(r"0|[1-9][0-9]*", value) is not None and int(value) <= maximum, "snapshot-count-bound")
        return int(value)
    header = take(9)
    require(header[5].isdigit() and header[6] in ("True", "False") and header[7] in ("True", "False"), "snapshot-project-header")
    timestamp(header[8])
    fields = {}
    for _ in range(count()):
        field = take(3)
        options = [take(4) for _ in range(count())]
        require(field[0] not in fields and len({option[0] for option in options}) == len(options), "duplicate-snapshot-field")
        fields[field[0]] = {"tokens": field, "options": options}
    items = {}
    for _ in range(count()):
        item = take(7)
        require(item[0] not in items and item[2] in ("Issue", "PullRequest", "DraftIssue") and item[6] in ("True", "False"), "snapshot-item-identity")
        timestamp(item[5])
        values = [take(4) for _ in range(count())]
        require(len({value[1] for value in values}) == len(values) and all(value[1] in fields for value in values), "snapshot-value-field-identity")
        items[item[0]] = {"header": item, "values": {value[1]: _snapshot_value(value) for value in values}}
    require(len({item["header"][1] for item in items.values()}) == len(items), "duplicate-snapshot-content")
    views = []
    for _ in range(count()):
        view = take(5)
        views.append({"header": view, "visibleFields": take(count())})
    issues = [take(5) for _ in range(count())]
    require(position == len(tokens) and len({view["header"][0] for view in views}) == len(views)
            and len({issue[0] for issue in issues}) == len(issues), "snapshot-trailing-or-duplicate-identities")
    for issue in issues: timestamp(issue[3])
    return {"header": header, "fields": fields, "items": items, "views": views, "issues": issues}


INSPECTION_COMMON = set("schema bindingVersion organizationId owner projectNumber projectId projectTitle schemaVersion ownerKind repositories status roadmapFieldId track observation selectedIssues recipeRevision artifactSha256 populationRevision importRecipeRevision importArtifactSha256 protectedInputs selected attempted verified mutationAttempts populationGap cleanup items candidates".split())
INSPECTION_PROGRAMME = set("enrollmentCommitmentSha256 membershipGap memberships nativeIntegrator integratorObservation selectionCoverage selection".split())
INSPECTION_ITEM = set("issue expectedNodeId native dependencyObservations dependencyReadComplete planObservation sourceCurrentness planning planningGap discrepancies health delivery publication nativeAcceptance reads mutationAttempts gap lastVerified".split())


def decode_inspection(report, expected_binding):
    """Keep product/organization shapes distinct; decoded facts do not grant authority."""
    binding = report.get("bindingVersion") if isinstance(report, dict) else None
    require(binding in (3, 4, 5), "unsupported-inspection-binding")
    schema, version = {3: (1, 1), 4: (2, 2), 5: (3, 3)}[binding]
    closed(report, INSPECTION_COMMON | (INSPECTION_PROGRAMME if binding != 3 else set()))
    require(report["schema"] == f"fsgg.coord.board-v2-inspection/{schema}" and report["schemaVersion"] == version, "inspection-schema-binding-differs")
    require(isinstance(expected_binding, dict) and all(report.get(key) == value for key, value in expected_binding.items())
            and {"bindingVersion", "schemaVersion", "projectId", "selectedIssues"} <= set(expected_binding), "inspection-selected-binding-differs")
    require(report["mutationAttempts"] == 0 and report["populationGap"] is None and report["selected"] == report["attempted"] == len(report["items"]), "inspection-coverage-unavailable")
    require(report["selected"] == len(report["selectedIssues"]), "inspection-cohort-count-differs")
    def issue_shape(issue):
        closed(issue, {"owner", "repository", "number"})
        text(issue["owner"]); text(issue["repository"])
        require(type(issue["number"]) is int and issue["number"] > 0, "inspection-issue-number")
    def native_shape(native):
        if native is None: return
        closed(native, {"issue", "nodeId", "updatedAt", "state", "url", "bodySha256"})
        issue_shape(native["issue"]); timestamp(native["updatedAt"]); text(native["nodeId"])
        require(native["state"] in ("OPEN", "CLOSED") and re.fullmatch(r"[0-9a-f]{64}", native["bodySha256"]) is not None, "inspection-native-observation-shape")
    seen = set()
    for item in report["items"]:
        closed(item, INSPECTION_ITEM | ({"sourceRevision", "accountableOwner", "currentDeliverable", "nativeIntake"} if binding != 3 else set()) | ({"workItem"} if binding == 5 else set()))
        issue_shape(item["issue"])
        native_shape(item["native"]); native_shape(item["planObservation"])
        require(isinstance(item["dependencyObservations"], list) and type(item["dependencyReadComplete"]) is bool
                and item["sourceCurrentness"] in ("Current", "Stale", "Unknown"), "inspection-source-facts-shape")
        for dependency in item["dependencyObservations"]: native_shape(dependency)
        if binding != 3:
            if item["currentDeliverable"] is not None:
                closed(item["currentDeliverable"], {"provenance", "declaration", "references", "gap", "reconciliationRequired"})
            if item["nativeIntake"] is not None:
                intake = item["nativeIntake"]
                closed(intake, {"provenance", "observedAt", "admission", "allowed", "gap"})
                require(intake["allowed"] is None or type(intake["allowed"]) is bool, "inspection-intake-shape")
                if intake["admission"] is not None:
                    closed(intake["admission"], {"RepositoryId", "IssueId", "UpdatedAt", "AuthorId", "AuthorLogin", "Permission"})
        if binding == 5 and item["workItem"] is not None:
            work = item["workItem"]
            closed(work, {"workLevel", "workLevelOptionId", "hierarchy", "closure"})
            closed(work["hierarchy"], {"parent", "children", "complete", "gap", "observedAt", "sha256"})
            closed(work["closure"], {"stateReason", "closedAt", "doneWhen", "blockedOn", "acceptanceEvidence", "acceptance", "gaps"})
            require(work["closure"]["acceptance"] == "Unverified" and type(work["hierarchy"]["complete"]) is bool
                    and isinstance(work["hierarchy"]["children"], list) and len(work["hierarchy"]["children"]) <= 500, "inspection-work-item-shape")
            if work["hierarchy"]["parent"] is not None:
                closed(work["hierarchy"]["parent"], {"issue", "nodeId"})
                issue_shape(work["hierarchy"]["parent"]["issue"])
            for child in work["hierarchy"]["children"]:
                closed(child, {"issue", "nodeId", "state", "stateReason", "closedAt"})
                issue_shape(child["issue"])
        issue = item["issue"]
        identity = f"{issue['owner']}/{issue['repository']}#{issue['number']}"
        node = item["expectedNodeId"]
        require(node not in seen and report["selectedIssues"].get(node) == identity and item["mutationAttempts"] == 0, "inspection-native-cohort-differs")
        seen.add(node)
        if item["planning"] is not None:
            closed(item["planning"], {"itemId", "status", "roadmap", "track", "observation", "membershipPages"})
    require(seen == set(report["selectedIssues"]), "inspection-cohort-incomplete")
    if binding != 3:
        require(report["membershipGap"] is None and isinstance(report["memberships"], list), "inspection-membership-unavailable")
        memberships = report["memberships"]
        for member in memberships:
            closed(member, {"itemId", "contentId", "kind", "issue", "enrolled", "discrepancy"})
        require(len({member["itemId"] for member in memberships}) == len(memberships)
                and len({member["contentId"] for member in memberships}) == len(memberships)
                and all(any(member["contentId"] == node and member["enrolled"] is True for member in memberships) for node in seen), "inspection-membership-identity-differs")
    return {"family": "product3" if binding == 3 else "organization" + str(binding), "report": report}


def decode_prepared(prepared, prepared_path):
    closed(prepared, {"schema", "preparedPath", "observedAt", "scope", "snapshot", "changes", "ownerActions", "digest"})
    scope_keys = set("sourceRevision artifactSha256 cliArtifactSha256 principalId principalLogin selectedIssues statusFieldId roadmapFieldId trackFieldId observationFieldId inReviewColor inReviewDescription views initialPlanning".split())
    scope = prepared["scope"]
    closed(scope, scope_keys | ({"workLevel"} if "workLevel" in scope else set()))
    require(re.fullmatch(r"[0-9a-f]{40}", scope["sourceRevision"]) is not None
            and all(re.fullmatch(r"[0-9a-f]{64}", scope[key]) is not None for key in ("artifactSha256", "cliArtifactSha256"))
            and re.fullmatch(r"[0-9a-f]{64}", prepared["digest"]) is not None, "migration-source-identity-shape")
    for name, maximum, keys in (("views", 10, {"id", "name", "layout", "filter", "visibleFieldIds"}),
                                ("initialPlanning", 100, {"issueId", "track", "roadmap", "status"})):
        require(isinstance(scope[name], list) and len(scope[name]) <= maximum, "migration-scope-collection-bound")
        for declaration in scope[name]:
            closed(declaration, keys)
            if name == "initialPlanning":
                closed(declaration["track"], {"id", "name"})
                if declaration["status"] is not None: closed(declaration["status"], {"id", "name"})
    if "workLevel" in scope:
        closed(scope["workLevel"], {"schema", "fieldId", "assignments"})
        require(scope["workLevel"]["schema"] == "fs.gg.board-v2.work-level-migration/1"
                and isinstance(scope["workLevel"]["assignments"], list) and len(scope["workLevel"]["assignments"]) <= 100, "migration-work-level-shape")
        for assignment in scope["workLevel"]["assignments"]: closed(assignment, {"issueId", "name"})
    timestamp(prepared["observedAt"])
    version = 2 if "workLevel" in scope else 1
    require(prepared["schema"] == f"fs.gg.board-v2.migration-prepare/{version}"
            and prepared["preparedPath"] == str(prepared_path), "migration-original-prepared-identity-differs")
    snapshot = decode_snapshot(prepared["snapshot"])
    require(snapshot["header"][:2] == [scope["principalId"], scope["principalLogin"]], "migration-native-principal-differs")
    require(isinstance(scope["selectedIssues"], list) and 1 <= len(scope["selectedIssues"]) <= 100, "migration-selected-scope-bound")
    selected = set()
    for issue in scope["selectedIssues"]:
        closed(issue, {"id", "repository", "number", "slice"})
        require(issue["id"] not in selected and any(row[:3] == [issue["id"], issue["repository"], str(issue["number"])] for row in snapshot["issues"]), "migration-native-selected-identity-differs")
        selected.add(issue["id"])
    require(selected == {row[0] for row in snapshot["issues"]}, "migration-native-selected-cohort-differs")
    require(isinstance(prepared["changes"], list) and isinstance(prepared["ownerActions"], list) and all(isinstance(value, str) for value in prepared["ownerActions"]), "migration-original-intent-shape")
    return snapshot


def migration_readback(report, prepared, prepared_path):
    closed(report, {"schema", "observedAt", "scope", "snapshot", "snapshotDigest", "admission"})
    before = decode_prepared(prepared, prepared_path)
    version = 2 if "workLevel" in prepared["scope"] else 1
    timestamp(report["observedAt"])
    require(report["schema"] == f"fs.gg.board-v2.migration-readback/{version}", "migration-result-version-differs")
    require(report["scope"] == prepared["scope"], "migration-original-scope-differs")
    require(report["admission"] == "readback-only; original outcome and uncertainty retained"
            and re.fullmatch(r"[0-9a-f]{64}", report["snapshotDigest"]) is not None, "migration-readback-admission-differs")
    return {"before": before, "after": decode_snapshot(report["snapshot"]), "report": report, "original": prepared}


def require_preimage(observed, expected):
    require(observed == expected, "selected-preimage-changed")


def verify_field_transition(before, after, item_id, field_id, expected_before, expected_after):
    """Verify one cooperative transition and every other reported field/identity.

    This is post-send interpretation, not a server CAS or permission to send/retry.
    """
    require(before["header"][:8] == after["header"][:8] and before["fields"] == after["fields"]
            and before["views"] == after["views"] and before["issues"] == after["issues"]
            and set(before["items"]) == set(after["items"]), "unrelated-native-snapshot-changed")
    for identity, previous in before["items"].items():
        current = after["items"][identity]
        if identity != item_id:
            require(previous == current, "unrelated-native-item-changed")
        else:
            require(previous["header"][:5] + previous["header"][6:] == current["header"][:5] + current["header"][6:], "selected-native-item-identity-changed")
            require({key: value for key, value in previous["values"].items() if key != field_id} ==
                    {key: value for key, value in current["values"].items() if key != field_id}, "unrelated-native-field-changed")
    require(item_id in before["items"], "selected-item-unavailable")
    def selected(snapshot):
        value = snapshot["items"][item_id]["values"].get(field_id)
        if value is None: return None
        require(value["tokens"][2] in SCALAR_KEYS, "unsupported-selected-transition-field")
        return {"kind": value["tokens"][2], "fieldId": field_id,
                **{key: value["properties"][key] for key in SCALAR_KEYS.get(value["tokens"][2], ())}}
    require_preimage(selected(before), expected_before)
    require(selected(after) == expected_after, "selected-transition-unverified")
    return {"fields": len(before["fields"]), "views": len(before["views"]), "memberships": len(before["items"]),
            "coverage": "all reported values and identities; opaque system-field contents and hidden native fields remain unproved"}


def qualify(record, *, kind, source_path=None, prepared_path=None, expected_binding=None, expected_sha256=None,
            expected_source_revision=None, expected_scope=None):
    """Interpret one original terminal command. Unknown never licenses a resend."""
    references = [{"path": str(source_path), "sha256": None}] if source_path is not None else []
    def retain(reference):
        references[:] = [previous for previous in references if previous["path"] != reference["path"]]
        references.append(reference)
    try:
        require(record.get("terminal") is True and record.get("exit") == 0 and record.get("stdoutEOF") is True
                and record.get("stderrEOF") is True and not record.get("failure")
                and record.get("cleanup") == "direct-child-reaped", "original-command-incomplete-or-failed")
        if kind == "migration-readback":
            require(record.get("stdout") == b"" and prepared_path is not None and source_path is not None, "migration-readback-is-custody-file-not-stdout")
            prepared_path, source_path = Path(prepared_path), Path(source_path)
            require(source_path.parent == Path(str(prepared_path) + ".invocation") and re.fullmatch(r"readback-[0-9a-f]{32}\.json", source_path.name) is not None, "migration-readback-original-path-differs")
            prepared, ref = read_original(source_path.parent / "prepared.json")
            retain(ref)
            report, ref = read_original(source_path, expected_sha256)
            retain(ref)
            value = migration_readback(report, prepared, prepared_path)
        elif kind == "migration-prepare":
            require(record.get("stdout") == b"" and source_path is not None, "migration-prepare-is-report-file-not-stdout")
            report, ref = read_original(source_path, expected_sha256)
            retain(ref)
            snapshot = decode_prepared(report, Path(source_path))
            require(isinstance(expected_source_revision, str) and re.fullmatch(r"[0-9a-f]{40}", expected_source_revision) is not None
                    and report["scope"]["sourceRevision"] == expected_source_revision, "migration-selected-source-differs")
            require(expected_scope is not None and report["scope"] == expected_scope, "migration-selected-scope-differs")
            require(report["changes"] == [], "migration-prepare-not-an-unchanged-readonly-snapshot")
            value = {"snapshot": snapshot, "report": report}
        elif kind == "inspection":
            require(source_path is not None, "inspection-report-file-required")
            report, ref = read_original(source_path, expected_sha256)
            retain(ref)
            value = decode_inspection(report, expected_binding)
        else:
            raise Unknown("unsupported-result-kind")
        return {"state": "Observed", "value": value, "originals": references, "gap": None}
    except (Unknown, KeyError, TypeError, ValueError, AttributeError) as error:
        reason = str(error) if isinstance(error, Unknown) else "invalid-original-result-shape"
        return {"state": "Unknown", "value": None, "originals": references, "gap": reason}
