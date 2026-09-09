#!/usr/bin/env python3
"""Build and publish the bounded, public FS-GG telemetry dashboard feed."""

from __future__ import annotations

import argparse
import base64
import datetime as dt
import importlib.util
import json
import os
import pathlib
import re
import sqlite3
import stat
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
from typing import Any

MAX_JSON = 1_048_576
MAX_API_JSON = 4 * 1_048_576
HOST_SCHEMA = "fsgg.telemetry.dashboard-host/3"
LEGACY_HOST_SCHEMAS = {"fsgg.telemetry.dashboard-host/1", "fsgg.telemetry.dashboard-host/2"}
DASH_SCHEMA = "fsgg.telemetry.dashboard/2"
DELIVERIES_SCHEMA = "fsgg.telemetry.public-deliveries/1"
ITEMS_SCHEMA = "fsgg.telemetry.completed-items/2"
PROCESS_SCHEMA = "fsgg.telemetry.item-process-detail/1"
LABELS_SCHEMA = "fsgg.telemetry.dashboard-labels/1"
ALLOWED_STATES = {"queued", "in_progress", "completed", "requested", "waiting", "pending"}
ALLOWED_RESULTS = {"success", "failure", "cancelled", "skipped", "timed_out", "action_required", "neutral", "stale", "startup_failure", None}


class RefConflict(RuntimeError):
    pass

class HostSourceError(RuntimeError):
    pass


def now() -> str:
    return dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def dump(value: Any) -> bytes:
    data = (json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True) + "\n").encode()
    if len(data) > MAX_JSON:
        raise ValueError("dashboard payload exceeds 1 MiB")
    return data


def load(path: pathlib.Path, limit: int = MAX_JSON) -> Any:
    if path.is_symlink() or not path.is_file() or path.stat().st_size > limit:
        raise ValueError(f"unsafe JSON input: {path}")
    return json.loads(path.read_text(encoding="utf-8"))


def atomic(path: pathlib.Path, value: Any) -> None:
    data = dump(value)
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, name = tempfile.mkstemp(prefix=path.name + ".", dir=path.parent)
    try:
        with os.fdopen(fd, "wb") as stream:
            stream.write(data); stream.flush(); os.fsync(stream.fileno())
        os.replace(name, path)
    finally:
        if os.path.exists(name): os.unlink(name)


def github(path: str, token: str, method: str = "GET", body: Any = None) -> tuple[Any, dict[str, str]]:
    data = None if body is None else dump(body)
    req = urllib.request.Request("https://api.github.com/" + path.lstrip("/"), data=data, method=method,
        headers={"Accept":"application/vnd.github+json", "Authorization":f"Bearer {token}", "X-GitHub-Api-Version":"2022-11-28", "User-Agent":"fsgg-telemetry-dashboard"})
    try:
        with urllib.request.urlopen(req, timeout=30) as response:
            raw = response.read(MAX_API_JSON + 1)
            if len(raw) > MAX_API_JSON: raise ValueError("GitHub response exceeds 4 MiB")
            return (json.loads(raw) if raw else {}), dict(response.headers)
    except urllib.error.HTTPError as error:
        detail = error.read(4096).decode(errors="replace")
        if error.code in {409, 422}: raise RefConflict(detail) from error
        raise RuntimeError(f"GitHub API {error.code}: {detail}") from error


def parse_time(value: Any) -> dt.datetime | None:
    if not isinstance(value, str): return None
    try:
        parsed=dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
        return parsed if parsed.tzinfo is not None else None
    except ValueError: return None


def collect_actions(repo: str, token: str, cap: int) -> dict[str, Any]:
    if not 1 <= cap <= 1000: raise ValueError("cap must be between 1 and 1000")
    runs: list[dict[str, Any]] = []; seen: set[int] = set(); observation = now()
    total = None
    pages = 0
    for page in range(1, (cap + 99) // 100 + 1):
        created=urllib.parse.quote(f"<={observation}")
        result, _ = github(f"repos/{repo}/actions/runs?per_page=100&page={page}&created={created}", token)
        if not isinstance(result, dict) or not isinstance(result.get("workflow_runs"), list): raise ValueError("malformed Actions response")
        total = result.get("total_count") if isinstance(result.get("total_count"), int) else total
        pages += 1
        for raw in result["workflow_runs"]:
            if len(runs) >= cap: break
            if not isinstance(raw, dict): continue
            run_id=int(raw["id"])
            if run_id in seen: continue
            seen.add(run_id)
            state, conclusion = raw.get("status"), raw.get("conclusion")
            if state not in ALLOWED_STATES or conclusion not in ALLOWED_RESULTS: raise ValueError("unsupported Actions status or conclusion")
            created, started, updated = parse_time(raw.get("created_at")), parse_time(raw.get("run_started_at")), parse_time(raw.get("updated_at"))
            duration = int((updated - started).total_seconds()) if started and updated and updated >= started and state == "completed" else None
            runs.append({"id": run_id, "workflow": str(raw.get("name") or "Unnamed workflow")[:120],
                "event": str(raw.get("event") or "unknown")[:40], "status": state, "conclusion": conclusion,
                "createdAt": created.isoformat().replace("+00:00", "Z") if created else None,
                "startedAt": started.isoformat().replace("+00:00", "Z") if started else None,
                "updatedAt": updated.isoformat().replace("+00:00", "Z") if updated else None,
                "durationSeconds": duration, "attempt": int(raw.get("run_attempt") or 1),
                "url": f"https://github.com/{repo}/actions/runs/{int(raw['id'])}"})
        if len(result["workflow_runs"]) < 100 or len(runs) >= cap: break
    stamps = [r["createdAt"] for r in runs if r["createdAt"]]
    return {"schema":"fsgg.telemetry.public-actions/1", "observedAt":observation, "repository":repo,
        "selection":{"order":"created-descending", "cap":cap, "pagesFetched":pages, "returned":len(runs),
            "repositoryTotalAtObservation":total, "truncated":bool(total is not None and total > len(runs)),
            "newestCreatedAt":max(stamps) if stamps else None, "oldestCreatedAt":min(stamps) if stamps else None,
            "semantics":"bounded multi-page sample, deduplicated by run id; latest observed attempt; not an atomic inventory"}, "runs":runs}


def collect_deliveries(repo: str, token: str, cap: int) -> dict[str, Any]:
    if not 1 <= cap <= 500: raise ValueError("delivery cap must be between 1 and 500")
    rows: list[dict[str, Any]] = []; seen: set[int] = set(); observation = now(); pages = 0; scanned=0
    for page in range(1, (cap + 99) // 100 + 1):
        result, _ = github(f"repos/{repo}/pulls?state=closed&sort=updated&direction=desc&per_page=100&page={page}", token)
        if not isinstance(result, list): raise ValueError("malformed pull request response")
        pages += 1
        for raw in result:
            scanned += 1
            if len(rows) >= cap: break
            if not isinstance(raw, dict) or raw.get("merged_at") is None: continue
            number = checked_int(raw.get("number"), "pull request number")
            if number in seen: continue
            seen.add(number)
            merged = parse_time(raw.get("merged_at")); created = parse_time(raw.get("created_at"))
            title = raw.get("title")
            if merged is None or created is None or not isinstance(title, str): raise ValueError("malformed merged pull request")
            elapsed=int((merged-created).total_seconds()) if merged>=created else None
            rows.append({"number":number,"title":title[:180],"url":f"https://github.com/{repo}/pull/{number}",
                "createdAt":created.isoformat().replace("+00:00","Z"),"mergedAt":merged.isoformat().replace("+00:00","Z"),
                "elapsedSeconds":elapsed})
        if len(result) < 100: break
    return {"schema":DELIVERIES_SCHEMA,"observedAt":observation,"repository":repo,
        "selection":{"order":"closed-updated-descending","cap":cap,"pagesFetched":pages,"closedScanned":scanned,"returned":len(rows),
            "semantics":"merged pull requests found in a bounded updated-ordered closed-PR scan; public delivery evidence, not proof of a whole completed item or effort"},"deliveries":rows}


def config() -> tuple[pathlib.Path, dict[str, str]]:
    helper = pathlib.Path(__file__).resolve().parents[1] / ".claude/skills/work-roadmap/scripts/fsgg_telemetry_defaults.py"
    spec = importlib.util.spec_from_file_location("dashboard_telemetry_defaults", helper)
    if spec is None or spec.loader is None: raise HostSourceError("HOST_CONFIG_HELPER_UNAVAILABLE")
    module = importlib.util.module_from_spec(spec); sys.modules[spec.name] = module; spec.loader.exec_module(module)
    found = module.discover_config()
    if found is None: raise HostSourceError("HOST_NOT_CONFIGURED")
    return found.path, {"storeRoot":str(found.store_root),"engine":found.engine}


def engine_json(engine: str, args: list[str]) -> Any:
    try: done = subprocess.run([engine, *args], capture_output=True, text=True, timeout=45, check=False)
    except (OSError, subprocess.SubprocessError) as error: raise HostSourceError("HOST_ENGINE_UNAVAILABLE") from error
    if done.returncode: raise HostSourceError("HOST_ENGINE_PROJECTION_FAILED")
    if len(done.stdout.encode("utf-8")) > MAX_JSON: raise HostSourceError("HOST_ENGINE_OUTPUT_TOO_LARGE")
    try: return json.loads(done.stdout)
    except json.JSONDecodeError as error: raise HostSourceError("HOST_ENGINE_INVALID_JSON") from error


def load_labels(path: pathlib.Path | None) -> dict[str, Any]:
    if path is None: return {"schema":LABELS_SCHEMA,"items":{},"models":{},"efforts":{},"scopes":{}}
    if path.is_symlink() or not path.is_file() or stat.S_IMODE(path.stat().st_mode) & 0o077:
        raise HostSourceError("HOST_LABELS_UNSAFE")
    value = load(path, 65536)
    exact(value,{"schema","items","models","efforts","scopes"},"label approval")
    if value["schema"] != LABELS_SCHEMA: raise ValueError("invalid label approval schema")
    for category in ("models","efforts","scopes"):
        if not isinstance(value[category],dict) or len(value[category])>64: raise ValueError("invalid approved category map")
        for private,public in value[category].items():
            if not isinstance(private,str) or not isinstance(public,str) or not 1<=len(public)<=48: raise ValueError("invalid approved category")
        if category=="scopes" and len(set(value[category].values()))!=len(value[category]): raise ValueError("public scope aliases must be unique")
    if not isinstance(value["items"],dict) or len(value["items"])>200: raise ValueError("invalid approved item map")
    aliases=set()
    for private,item in value["items"].items():
        exact(item,{"key","label","url","repositories","notes"},"approved item")
        if not isinstance(private,str) or not re.fullmatch(r"[a-z0-9][a-z0-9-]{0,63}",item["key"]): raise ValueError("invalid public item key")
        if item["key"] in aliases: raise ValueError("conflicting public item key")
        aliases.add(item["key"])
        if not isinstance(item["label"],str) or not 1<=len(item["label"])<=120: raise ValueError("invalid public item label")
        if not isinstance(item["url"],str) or not re.fullmatch(r"https://github\.com/FS-GG/[A-Za-z0-9_.-]+(?:/(?:issues|pull)/[1-9][0-9]*)?",item["url"]): raise ValueError("invalid public item URL")
        if not isinstance(item["repositories"],list) or len(item["repositories"])>8 or any(not re.fullmatch(r"FS-GG/[A-Za-z0-9_.-]+",r) for r in item["repositories"]): raise ValueError("invalid approved repository")
        if not isinstance(item["notes"],list) or len(item["notes"])>8: raise ValueError("invalid approved notes")
        for note in item["notes"]:
            exact(note,{"kind","text","evidenceUrl"},"approved note")
            enum(note["kind"],{"repair","complication"},"note kind")
            if not isinstance(note["text"],str) or not 1<=len(note["text"])<=240: raise ValueError("invalid approved note")
            if not isinstance(note["evidenceUrl"],str) or not re.fullmatch(r"https://github\.com/FS-GG/[A-Za-z0-9_.-]+/(?:issues|pull|actions/runs)/[1-9][0-9]*",note["evidenceUrl"]): raise ValueError("invalid note evidence")
    return value


def _latest_rows(connection: sqlite3.Connection, table: str, order: str) -> list[sqlite3.Row]:
    # Table and order are fixed call-site constants, never caller input.
    rows=connection.execute(f"SELECT * FROM {table} ORDER BY {order} LIMIT 10001").fetchall()
    if len(rows)>10000: raise HostSourceError("HOST_QUERY_BOUND_EXCEEDED")
    return rows


ACTIVITY_CATEGORIES={"planning","implementation","review","validation","delivery","repair","operations","other","unclassified"}
ATTRIBUTION_CLASSES={"direct","mixed","unclassified"}
COMPLICATION_TRIGGERS={"test-failure","review-finding","ci-failure","tooling","runtime","dependency","authority","operation","human-change","unknown","other"}
COMPLICATION_CAUSES={"product-defect","test-defect","process-defect","infrastructure","tooling","dependency","requirements","authorization","external","unknown","other"}
REVIEW_COVERAGE={"complete","partial","unknown"}


def validate_private_evidence(value: Any) -> None:
    if not isinstance(value,list) or len(value)>16: raise ValueError("invalid private evidence")
    for row in value:
        exact(row,{"kind","digest"},"private evidence")
        enum(row["kind"],{"runtime","ci","delivery","test","other"},"evidence kind")
        if not isinstance(row["digest"],str) or not re.fullmatch(r"[0-9a-f]{64}",row["digest"]): raise ValueError("invalid evidence digest")


def validate_private_item_detail(value: Any, expected_item: str) -> None:
    """Validate the engine-owned private contract before selecting any public fields."""
    exact(value,{"schema","item","activities","activityTruncated","usageAttributions","attributionTruncated","complications","complicationTruncated","reviews","reviewTruncated","accounting"},"private item detail")
    if value["schema"]!="fsgg.telemetry.item-detail/1" or value["item"]!=expected_item: raise ValueError("invalid private item detail identity")
    limits=(("activities","activityTruncated",256),("usageAttributions","attributionTruncated",256),("complications","complicationTruncated",256),("reviews","reviewTruncated",128))
    for rows,flag,limit in limits:
        if not isinstance(value[rows],list) or len(value[rows])>limit or not isinstance(value[flag],bool): raise ValueError("invalid private item detail bound")
    for row in value["activities"]:
        exact(row,{"activityId","invocationId","attemptId","category","startedAt","endedAt","clockProvenance","evidence","summary","revision"},"private activity")
        for key in ("activityId","invocationId","attemptId"):
            if not isinstance(row[key],str) or not row[key]: raise ValueError("invalid private activity identity")
        enum(row["category"],ACTIVITY_CATEGORIES,"activity category"); enum(row["clockProvenance"],{"host-wall","provider-native","github-native"},"activity clock")
        start=parse_time(row["startedAt"]); end=parse_time(row["endedAt"]) if row["endedAt"] is not None else None
        if start is None or (row["endedAt"] is not None and (end is None or end<start)): raise ValueError("invalid activity interval")
        validate_private_evidence(row["evidence"])
        if row["summary"] is not None and (not isinstance(row["summary"],str) or not 1<=len(row["summary"])<=256): raise ValueError("invalid activity summary")
        checked_int(row["revision"],"activity revision")
    for row in value["usageAttributions"]:
        exact(row,{"usageIdentity","activityId","classification","input","cachedInput","output","reasoning","total","revision"},"private attribution")
        if not isinstance(row["usageIdentity"],str) or not row["usageIdentity"]: raise ValueError("invalid attribution identity")
        enum(row["classification"],ATTRIBUTION_CLASSES,"attribution classification")
        if (row["classification"]=="direct") != isinstance(row["activityId"],str): raise ValueError("invalid attribution activity")
        for key in ("input","cachedInput","output","total","revision"): checked_int(row[key],key)
        if row["cachedInput"]>row["input"] or row["total"]!=row["input"]+row["output"]: raise ValueError("invalid attribution counters")
        if row["reasoning"] is not None:
            checked_int(row["reasoning"],"reasoning")
            if row["reasoning"]>row["output"]: raise ValueError("invalid attribution reasoning")
    for row in value["complications"]:
        exact(row,{"attemptId","activityId","trigger","cause","occurredAt","synopsis","evidence","revision"},"private complication")
        enum(row["trigger"],COMPLICATION_TRIGGERS,"complication trigger"); enum(row["cause"],COMPLICATION_CAUSES,"complication cause")
        if parse_time(row["occurredAt"]) is None: raise ValueError("invalid complication time")
        if not isinstance(row["synopsis"],str) or not 1<=len(row["synopsis"])<=512: raise ValueError("invalid complication synopsis")
        validate_private_evidence(row["evidence"])
        checked_int(row["revision"],"complication revision")
    review_arrays=("wentWell","problems","avoidableDelayOrRework","processObservations","remainingRisks","concreteImprovements")
    for row in value["reviews"]:
        exact(row,{"scope","attemptId","revision","outcomeSynopsis",*review_arrays,"evidence","evidenceCoverage","populationCoverage","confidence","reviewerModel","reviewerEffort","reviewedAt","durationSeconds"},"private review")
        enum(row["scope"],{"attempt","item"},"review scope")
        if (row["scope"]=="attempt") != isinstance(row["attemptId"],str): raise ValueError("invalid review subject")
        checked_int(row["revision"],"review revision"); checked_int(row["durationSeconds"],"review duration")
        if row["revision"]<1 or row["durationSeconds"]>86400 or parse_time(row["reviewedAt"]) is None: raise ValueError("invalid review revision or time")
        enum(row["evidenceCoverage"],REVIEW_COVERAGE,"evidence coverage"); enum(row["populationCoverage"],REVIEW_COVERAGE,"population coverage"); enum(row["confidence"],{"low","medium","high"},"confidence")
        if not isinstance(row["reviewerModel"],str) or not isinstance(row["reviewerEffort"],str): raise ValueError("invalid reviewer categories")
        if not isinstance(row["outcomeSynopsis"],str) or not 1<=len(row["outcomeSynopsis"])<=1024: raise ValueError("invalid review synopsis")
        validate_private_evidence(row["evidence"])
        for key in review_arrays:
            if not isinstance(row[key],list) or len(row[key])>8 or any(not isinstance(entry,str) or not 1<=len(entry)<=512 for entry in row[key]): raise ValueError("invalid private review list")
    accounting=exact(value["accounting"],{"nativeTotal","direct","mixed","unclassified","missingAttribution","allocation"},"private accounting")
    for key in ("nativeTotal","direct","mixed","unclassified","missingAttribution"): checked_int(accounting[key],key)
    if accounting["allocation"]!="native-exact-only" or accounting["direct"]+accounting["mixed"]+accounting["unclassified"]>accounting["nativeTotal"]: raise ValueError("invalid private accounting")


def project_process_detail(details: list[tuple[str,dict[str,Any]]], labels: dict[str,Any]) -> dict[str,Any]:
    base={"schema":PROCESS_SCHEMA,"availability":"unsupported","members":{"requested":0,"available":0},
        "truncated":{"activities":False,"attributions":False,"complications":False,"reviews":False},
        "activities":{"rows":[],"summary":[],"semantics":"activity spans may overlap; summed activity time is not owner effort or an elapsed-time partition"},
        "attribution":{"rows":[],"accounting":{"nativeTotal":0,"direct":0,"mixed":0,"unclassified":0,"missingAttribution":0},"crossRead":"unavailable","semantics":"native and attributed totals are related, not additive; missing attribution counts usage rows; totals can span incompatible private accounting scopes"},
        "complications":{"rows":[]},"reviews":{"rows":[],"semantics":"review counts omit private findings text; confidence and duration do not establish item, effort, or token completeness"},
        "observation":"engine item-detail and store projections are independently read and are not one atomic snapshot"}
    if not details: return base
    base["availability"]="available"; base["members"]={"requested":len(details),"available":len(details)}
    activity_lookup={}; activity_rows=[]; activity_summary={}; attribution={}; complication_rows=[]; review_rows=[]
    accounting={"nativeTotal":0,"direct":0,"mixed":0,"unclassified":0,"missingAttribution":0}
    list_names=("wentWell","problems","avoidableDelayOrRework","processObservations","remainingRisks","concreteImprovements")
    for member,detail in details:
        base["truncated"]["activities"] |= detail["activityTruncated"]
        base["truncated"]["attributions"] |= detail["attributionTruncated"]
        base["truncated"]["complications"] |= detail["complicationTruncated"]
        base["truncated"]["reviews"] |= detail["reviewTruncated"]
        for key in accounting: accounting[key]+=detail["accounting"][key]
        member_sums={classification:sum(row["total"] for row in detail["usageAttributions"] if row["classification"]==classification) for classification in ATTRIBUTION_CLASSES}
        for classification,total in member_sums.items():
            expected=detail["accounting"][classification]
            if total>expected or (not detail["attributionTruncated"] and total!=expected): raise ValueError("item detail attribution accounting mismatch")
        for row in detail["activities"]:
            activity_lookup[(member,row["activityId"])]=row["category"]
            start=parse_time(row["startedAt"]); end=parse_time(row["endedAt"]) if row["endedAt"] is not None else None
            seconds=int((end-start).total_seconds()) if start and end else None
            activity_rows.append({"category":row["category"],"startedAt":row["startedAt"],"endedAt":row["endedAt"],"durationSeconds":seconds})
            bucket=activity_summary.setdefault(row["category"],{"category":row["category"],"spans":0,"open":0,"knownDuration":0,"summedSeconds":0})
            bucket["spans"]+=1; bucket["open"]+=int(end is None); bucket["knownDuration"]+=int(seconds is not None); bucket["summedSeconds"]+=seconds or 0
        for row in detail["usageAttributions"]:
            category=activity_lookup.get((member,row["activityId"])) if row["classification"]=="direct" else None
            if row["classification"]=="direct" and category is None: category="unallocated"
            key=(row["classification"],category)
            bucket=attribution.setdefault(key,{"classification":row["classification"],"activityCategory":category,"records":0,"input":0,"cachedInput":0,"output":0,"reasoning":0,"reasoningKnown":True,"total":0})
            bucket["records"]+=1
            for source in ("input","cachedInput","output","total"): bucket[source]+=row[source]
            if row["reasoning"] is None: bucket["reasoningKnown"]=False
            else: bucket["reasoning"]+=row["reasoning"]
        for row in detail["complications"]:
            complication_rows.append({"trigger":row["trigger"],"cause":row["cause"],"occurredAt":row["occurredAt"],"activityCategory":activity_lookup.get((member,row["activityId"])) if row["activityId"] is not None else None})
        for row in detail["reviews"]:
            counts={key:len(row[key]) for key in list_names}
            review_rows.append({"scope":row["scope"],"revision":row["revision"],"evidenceCoverage":row["evidenceCoverage"],"populationCoverage":row["populationCoverage"],"confidence":row["confidence"],"reviewerModel":labels["models"].get(row["reviewerModel"],"unknown"),"reviewerEffort":labels["efforts"].get(row["reviewerEffort"],"unknown"),"reviewedAt":row["reviewedAt"],"durationSeconds":row["durationSeconds"],"counts":counts})
    attribution_rows=[]
    for row in attribution.values():
        if not row.pop("reasoningKnown"): row["reasoning"]=None
        attribution_rows.append(row)
    base["activities"]={**base["activities"],"rows":sorted(activity_rows,key=lambda r:r["startedAt"]),"summary":sorted(activity_summary.values(),key=lambda r:r["category"])}
    base["attribution"]={**base["attribution"],"rows":sorted(attribution_rows,key=lambda r:(r["classification"],r["activityCategory"] or "")),"accounting":accounting}
    base["complications"]={"rows":sorted(complication_rows,key=lambda r:r["occurredAt"])}
    base["reviews"]={**base["reviews"],"rows":sorted(review_rows,key=lambda r:(r["scope"],-r["revision"]))}
    if len(activity_rows)>512 or len(attribution_rows)>768 or len(complication_rows)>512 or len(review_rows)>256: raise ValueError("grouped item detail exceeds public bound")
    return base


def project_completed_items(store_root: str, status: dict[str, Any], labels: dict[str, Any], ci_by_item: dict[str,dict[str,Any]], budgets_by_item: dict[str,dict[str,Any]], detail_loader: Any = None) -> dict[str, Any]:
    database=pathlib.Path(store_root)/"telemetry.sqlite3"
    if database.is_symlink() or not database.is_file(): raise HostSourceError("HOST_STORE_UNAVAILABLE")
    uri=f"file:{urllib.parse.quote(str(database))}?mode=ro"
    connection=None; selected_details=[]; store_schema=None
    try:
        connection=sqlite3.connect(uri,uri=True,timeout=5); connection.row_factory=sqlite3.Row
        deadline=time.monotonic()+10; progress=[0]
        def bounded_progress() -> int:
            progress[0]+=1
            return int(progress[0]>2000 or time.monotonic()>deadline)
        connection.set_progress_handler(bounded_progress,1000)
        connection.execute("PRAGMA query_only=ON"); connection.execute("BEGIN")
        store_schema=connection.execute("PRAGMA user_version").fetchone()[0]
        if store_schema not in {7,8}: raise HostSourceError("HOST_SCHEMA_INCOMPATIBLE")
        if connection.execute("PRAGMA journal_mode").fetchone()[0].lower() != "wal": raise HostSourceError("HOST_JOURNAL_INCOMPATIBLE")
        native=tuple(int(part) for part in connection.execute("SELECT sqlite_version()").fetchone()[0].split(".")[:3])
        # STRICT tables arrived in 3.37; the read-only queries use no later SQL feature.
        # The writer engine's stronger 3.51.3 minimum is checked separately in build_host.
        if native < (3,37,0): raise HostSourceError("HOST_READER_INCOMPATIBLE")

        dirty={r[0] for r in connection.execute("SELECT item_id FROM budget_dirty_items LIMIT 10001")}
        if len(dirty)>10000: raise HostSourceError("HOST_QUERY_BOUND_EXCEEDED")
        populations={}
        for row in _latest_rows(connection,"budget_population_facts","item_id,CASE WHEN source_ref LIKE 'derived:%' THEN 0 ELSE 1 END,fact_revision DESC,identity DESC"):
            populations.setdefault(row["item_id"],row)
        outcomes={}
        for row in _latest_rows(connection,"native_item_outcomes","item_id,observed_at DESC,fact_revision DESC,identity DESC"):
            outcomes.setdefault(row["item_id"],row)
        groups: dict[str,list[str]]={}
        for item,row in populations.items(): groups.setdefault(row["original_item_id"],[]).append(item)
        approved=labels["items"]; result=[]; eligible=unmapped=dirty_count=incompatible=0
        for original,members in sorted(groups.items()):
            population_rows=[populations[m] for m in members]
            latest=[outcomes.get(m) for m in members]
            if any(m in dirty for m in members): dirty_count+=1; continue
            if any(r["state"]!="completed" for r in population_rows): continue
            if any(r is None or r["outcome"] not in {"delivered","delivered-after-readback"} or r["code_delivery"]!="delivered" for r in latest): continue
            eligible+=1
            approval=approved.get(original)
            if approval is None: unmapped+=1; continue
            try:
                result.append(project_one_item(connection,original,members,latest,approval,labels,ci_by_item,budgets_by_item,status.get("epoch")))
                selected_details.append((len(result)-1,sorted(members)))
            except ValueError: incompatible+=1
        connection.rollback()
    except sqlite3.Error as error:
        raise HostSourceError("HOST_READ_PROJECTION_FAILED") from error
    finally:
        if connection is not None: connection.close()
    # Engine item-detail reads happen only after the bounded SQLite transaction closes.
    # They are intentionally described as independent observations in the public payload.
    if store_schema == 8:
        if detail_loader is None: raise HostSourceError("HOST_ITEM_DETAIL_UNAVAILABLE")
        for index,members in selected_details:
            details=[]
            try:
                for member in members:
                    detail=detail_loader(member)
                    validate_private_item_detail(detail,member)
                    details.append((member,detail))
                process=project_process_detail(details,labels)
                native_observed=sum(row["total"] for row in result[index]["runtime"]["tokens"]["rows"])
                process["attribution"]["crossRead"]="matched" if process["attribution"]["accounting"]["nativeTotal"]==native_observed else "partial"
                result[index]["process"]=process
            except (ValueError,KeyError,TypeError) as error:
                raise HostSourceError("HOST_ITEM_DETAIL_INVALID") from error
    return {"schema":ITEMS_SCHEMA,"coverage":{"eligible":eligible,"published":len(result),"unmapped":unmapped,"dirty":dirty_count,"incompatible":incompatible},"items":result}


def project_one_item(connection: sqlite3.Connection, original: str, members: list[str], outcomes: list[sqlite3.Row], approval: dict[str,Any], labels: dict[str,Any], ci_by_item: dict[str,dict[str,Any]], budgets_by_item: dict[str,dict[str,Any]], epoch: Any) -> dict[str,Any]:
    placeholders=",".join("?" for _ in members)
    terminals=connection.execute(f"SELECT item_id,invocation_id,outcome FROM runtime_terminals WHERE item_id IN ({placeholders}) LIMIT 4097",members).fetchall()
    if len(terminals)>4096: raise ValueError("item projection exceeds runtime bound")
    complete_invocations={r["invocation_id"] for r in terminals}
    lineage={}
    lineage_rows=connection.execute(f"SELECT invocation_id,relation FROM invocation_lineage WHERE item_id IN ({placeholders}) ORDER BY fact_revision DESC,identity DESC LIMIT 4097",members).fetchall()
    if len(lineage_rows)>4096: raise ValueError("item projection exceeds lineage bound")
    for row in lineage_rows:
        if row["invocation_id"] in lineage and lineage[row["invocation_id"]]!=row["relation"]: raise ValueError("ambiguous lineage")
        lineage.setdefault(row["invocation_id"],row["relation"])
    times=connection.execute(f"SELECT invocation_id,event,occurred_at,occurred_clock_provenance FROM operational_event_times WHERE item_id IN ({placeholders}) ORDER BY fact_revision DESC,identity DESC LIMIT 8193",members).fetchall()
    if len(times)>8192: raise ValueError("item projection exceeds timing bound")
    events={}
    for r in times: events.setdefault((r["invocation_id"],r["event"]),r)
    durations=[]
    for invocation in complete_invocations:
        first,last=events.get((invocation,"start")),events.get((invocation,"terminal"))
        if first and last and first["occurred_clock_provenance"] in {"host-wall","provider-native","github-native"} and first["occurred_clock_provenance"]==last["occurred_clock_provenance"]:
            a,b=parse_time(first["occurred_at"]),parse_time(last["occurred_at"])
            if a and b and b>=a: durations.append((lineage.get(invocation,"unknown"),int((b-a).total_seconds())))
    usage=connection.execute(f"SELECT u.* FROM runtime_turn_usage u JOIN runtime_terminals t ON t.item_id=u.item_id AND t.invocation_id=u.invocation_id WHERE u.item_id IN ({placeholders}) ORDER BY u.identity LIMIT 8193",members).fetchall()
    if len(usage)>8192: raise ValueError("item projection exceeds usage bound")
    breakdown={}
    for row in usage:
        role=lineage.get(row["invocation_id"],"unknown"); role=role if role in {"root","child","follow-up"} else "unknown"
        requested_model=labels["models"].get(row["requested_model"]); observed_model=labels["models"].get(row["observed_model"])
        requested_effort=labels["efforts"].get(row["requested_effort"]); observed_effort=labels["efforts"].get(row["observed_effort"])
        scope=labels["scopes"].get(f"{row['provider']}|{row['accounting_scope']}")
        raw_key=(role,row["provider"],row["accounting_scope"],row["requested_model"],row["observed_model"],row["requested_effort"],row["observed_effort"])
        public=(requested_model,observed_model,requested_effort,observed_effort,scope)
        unmapped=any(v is None for v in public)
        requested_model=requested_model or "unknown"; observed_model=observed_model or "unknown"; requested_effort=requested_effort or "unknown"; observed_effort=observed_effort or "unknown"; scope=scope or "unknown"
        key=("mapped",raw_key)
        bucket=breakdown.setdefault(key,{"role":role,"requestedModel":requested_model,"observedModel":observed_model,"requestedEffort":requested_effort,"observedEffort":observed_effort,"scope":scope,"turns":0,"input":0,"cachedInput":0,"output":0,"reasoning":0,"reasoningKnown":True,"total":0,"unmapped":unmapped})
        bucket["turns"]+=1
        for source,target in (("input_count","input"),("cached_input","cachedInput"),("output_count","output"),("total","total")): bucket[target]+=row[source]
        if row["reasoning"] is None: bucket["reasoningKnown"]=False
        else: bucket["reasoning"]+=row["reasoning"]
    token_rows=[]
    unmapped_rows=sum(1 for bucket in breakdown.values() if bucket["unmapped"])
    for bucket in breakdown.values():
        bucket.pop("unmapped")
        if not bucket.pop("reasoningKnown"): bucket["reasoning"]=None
        token_rows.append(bucket)
    token_rows.sort(key=lambda r:(r["role"],r["observedModel"],r["observedEffort"],r["scope"]))
    repos=set(approval["repositories"]); deliveries=[]
    outcome_rows=connection.execute(f"SELECT repository,pr_number,outcome,code_delivery,occurred_at,observed_at FROM native_item_outcomes WHERE item_id IN ({placeholders}) ORDER BY observed_at DESC,fact_revision DESC,identity DESC LIMIT 257",members).fetchall()
    if len(outcome_rows)>256: raise ValueError("item projection exceeds delivery bound")
    seen_deliveries=set()
    for row in outcome_rows:
        identity=(row["repository"],row["pr_number"])
        if identity in seen_deliveries: continue
        seen_deliveries.add(identity)
        if row["repository"] in repos and row["code_delivery"]=="delivered" and row["outcome"] in {"delivered","delivered-after-readback"}:
            deliveries.append({"repository":row["repository"],"number":row["pr_number"],"url":f"https://github.com/{row['repository']}/pull/{row['pr_number']}","mergedAt":row["occurred_at"]})
    ci_counts={k:0 for k in ("runs","attempts","jobs","steps")}; ci_seconds={k:{"knownItems":0,"unknownItems":0,"totalItemSeconds":0} for k in ("runnerSeconds","wallSeconds","queueSeconds","usefulValidationSeconds","administrativeSeconds","necessarySetupSeconds","mixedSeconds","unclassifiedSeconds")}
    for member in members:
        ci=ci_by_item.get(member)
        if not ci:
            for k in ci_seconds: ci_seconds[k]["unknownItems"]+=1
            continue
        for k in ci_counts: ci_counts[k]+=checked_int(ci.get(k),k)
        for k in ci_seconds:
            if ci.get(k) is None: ci_seconds[k]["unknownItems"]+=1
            else: ci_seconds[k]["knownItems"]+=1; ci_seconds[k]["totalItemSeconds"]+=checked_int(ci[k],k)
    terminal_counts={k:0 for k in ("completed","failed","cancelled","launch-failed","other")}
    for row in terminals:
        outcome=row["outcome"] if row["outcome"] in terminal_counts else "other"
        terminal_counts[outcome]+=1
    ci_fail=connection.execute(f"SELECT count(*) FROM ci_runs WHERE item_id IN ({placeholders}) AND conclusion IN ('failure','cancelled','timed_out')",members).fetchone()[0]
    repeated=connection.execute(f"SELECT count(*) FROM (SELECT repository,run_id FROM ci_runs WHERE item_id IN ({placeholders}) GROUP BY repository,run_id HAVING max(attempt)>1)",members).fetchone()[0]
    delivered=max((parse_time(r["occurred_at"]) for r in outcomes if parse_time(r["occurred_at"])),default=None)
    duration_rows=[]
    for role in ("root","child","follow-up","unknown"):
        values=[seconds for item_role,seconds in durations if item_role==role]
        count=sum(1 for invocation in complete_invocations if lineage.get(invocation,"unknown")==role)
        if count: duration_rows.append({"role":role,"invocations":count,"known":len(values),"unknown":count-len(values),"summedSeconds":sum(values)})
    budget=[]
    for member in members:
        for dimension in budgets_by_item.get(member,{}).get("dimensions",[]):
            if isinstance(dimension,dict) and dimension.get("dimension") in {"model-usage","owner-effort","priced-cost","critical-path-delay","ci-runner-administration"} and dimension.get("verdict") in {"unknown","not-applicable","pass","breach"}:
                budget.append({"epoch":"current" if dimension.get("epoch")==epoch else "historical","dimension":dimension["dimension"],"verdict":dimension["verdict"],"numerator":dimension.get("numerator"),"denominator":dimension.get("denominator"),"severe":dimension.get("severe") is True})
    invocations_with_usage=len({row["invocation_id"] for row in usage})
    runtime_gaps=connection.execute(f"SELECT count(*) FROM runtime_gaps WHERE item_id IN ({placeholders})",members).fetchone()[0] if connection.execute("SELECT 1 FROM sqlite_master WHERE type='table' AND name='runtime_gaps'").fetchone() else 0
    process=project_process_detail([],labels)
    return {"key":approval["key"],"label":approval["label"],"url":approval["url"],"state":"settled","deliveredAt":delivered.isoformat().replace("+00:00","Z") if delivered else None,"deliveries":deliveries,
        "runtime":{"invocations":len(complete_invocations),"terminalOutcomes":terminal_counts,"duration":{"rows":duration_rows,"semantics":"same-clock non-reversed invocation spans summed by role; roles and invocations may overlap in wall time"},"tokens":{"scope":"completed native turns; input includes cached input","rows":token_rows,"unmappedRows":unmapped_rows,"coverage":{"invocationsWithUsage":invocations_with_usage,"invocationsWithoutUsage":len(complete_invocations)-invocations_with_usage,"runtimeGaps":runtime_gaps}}},
        "ci":{"counts":ci_counts,"seconds":ci_seconds,"semantics":"runner seconds sum jobs; wall, queue, and category values are per-item unions and may overlap"},
        "budget":{"scope":"canonical reducer assessments; epoch identities removed","assessments":budget},
        "complications":{"observed":{"runtimeNonSuccess":sum(v for k,v in terminal_counts.items() if k!="completed"),"failedOrCancelledCiRuns":ci_fail,"repeatedCiRuns":repeated,"followUpInvocations":sum(1 for v in lineage.values() if v=="follow-up")},"notes":approval["notes"],"semantics":"observed runtime and CI signals plus separately approved public notes; no inferred cause or repair cost"},
        "process":process}


def aggregate_host(public: dict[str, Any], ci: list[dict[str, Any]], budgets: list[dict[str, Any]], status: dict[str, Any], observed: str, reconciliations: list[dict[str,Any]] | None = None, budget_health: list[dict[str,Any]] | None = None, store_status: dict[str,Any] | None = None, completed_items: dict[str,Any] | None = None) -> dict[str, Any]:
    if not isinstance(public, dict) or public.get("schema") != "fsgg.telemetry.public-export/1" or not isinstance(public.get("items"), list): raise ValueError("invalid public export")
    totals = {k:0 for k in ("factCount","usageObservations","deliveryObservations")}
    usage = {k:0 for k in ("input","cachedInput","cacheWriteInput","output","total")}; reasoning: int | None = 0
    launcher = {k:0 for k in ("admitted","started","terminal","usage","missingAdmission","missingStart","missingTerminal","missingUsage")}
    quality = {k:{} for k in ("recordValidity","joinIntegrity","populationCoverage","qualification")}
    quality_allowed={"recordValidity":{"valid","invalid","unknown"},"joinIntegrity":{"matched","mismatch","conflict","unknown"},"populationCoverage":{"unknown","partial","complete","not-evaluated"},"qualification":{"qualified","not-qualified","not-evaluated","unknown"}}
    for item in public["items"]:
        if not isinstance(item, dict) or set(item) - {"schema","item","factCount","usageObservations","deliveryObservations","usage","launcherPopulation","recordValidity","joinIntegrity","populationCoverage","qualification"} or item.get("schema")!="fsgg.telemetry.public-summary/1": raise ValueError("public export contains an unapproved item")
        for key in totals: totals[key] += checked_int(item.get(key), key)
        u = item.get("usage"); lp = item.get("launcherPopulation")
        if not isinstance(u, dict) or not isinstance(lp, dict): raise ValueError("invalid public aggregate")
        for key in usage: usage[key] += checked_int(u.get(key), key)
        rv = u.get("reasoning")
        if rv is None: reasoning = None
        elif reasoning is not None: reasoning += checked_int(rv, "reasoning")
        for key in launcher: launcher[key] += checked_int(lp.get(key), key)
        for key in quality:
            raw=item.get(key); label = raw if raw in quality_allowed[key] else "unknown"; quality[key][label] = quality[key].get(label, 0) + 1
    usage["reasoning"] = reasoning
    ci_totals = {k:0 for k in ("runs","attempts","jobs","steps")}; seconds = {k:0 for k in ("runnerSeconds","wallSeconds","queueSeconds","usefulValidationSeconds","administrativeSeconds","necessarySetupSeconds","mixedSeconds","unclassifiedSeconds")}
    ci_coverage={k:{} for k in ("inventoryCoverage","checkCoverage","attemptCoverage","jobPageCoverage","terminalCoverage","timestampCoverage","lineageCoverage","classificationCoverage","criticalPathCoverage")}
    for item in ci:
        for key in ci_totals: ci_totals[key] += checked_int(item.get(key), key)
        for key in seconds:
            if item.get(key) is not None: seconds[key] += checked_int(item.get(key), key)
        for key in ci_coverage:
            raw=item.get(key); value=raw if raw in {"unknown","partial","complete","not-evaluated"} else "unknown"; ci_coverage[key][value]=ci_coverage[key].get(value,0)+1
    seconds={key:{"knownItems":sum(1 for item in ci if item.get(key) is not None),"unknownItems":sum(1 for item in ci if item.get(key) is None),"totalItemSeconds":value} for key,value in seconds.items()}
    operational={"expected":0,"lineage":{},"timing":{},"usageCoverage":{"not-evaluated":0},"terminalOutcomeCoverage":{"not-evaluated":0}}
    for result in reconciliations or []:
        operational["expected"] += checked_int(result.get("expected"),"expected")
        for source,target,allowed in ((result.get("lineageCoverage"),operational["lineage"],{"matched","unknown","invalid","unsupported","outOfScope"}),(result.get("timingCoverage"),operational["timing"],{"complete","late","missing","invalid","notEvaluated"})):
            if not isinstance(source,dict) or set(source)!=allowed: raise ValueError("invalid reconciliation coverage")
            for key,value in source.items(): target[key]=target.get(key,0)+checked_int(value,key)
        if result.get("usageCoverage")!="not-evaluated" or result.get("terminalOutcomeCoverage")!="not-evaluated": raise ValueError("unsupported reconciliation coverage")
        operational["usageCoverage"]["not-evaluated"] += 1; operational["terminalOutcomeCoverage"]["not-evaluated"] += 1
    budget_health_counts={}
    for health in budget_health or []:
        value=health.get("status"); value=value if value in {"complete","open","pending","missing-outcome"} else "unknown"; budget_health_counts[value]=budget_health_counts.get(value,0)+1
    dims: dict[str, dict[str, int]] = {}; assessments=[]; allowed_dims={"model-usage","owner-effort","priced-cost","critical-path-delay","ci-runner-administration"}; allowed_verdicts={"unknown","not-applicable","pass","breach"}
    severe_items = 0
    current_epoch=status.get("epoch") if isinstance(status.get("epoch"),str) else None
    for summary in budgets:
        item_severe=False
        for dimension in summary.get("dimensions", []):
            if not isinstance(dimension, dict) or current_epoch is None or dimension.get("epoch") != current_epoch: continue
            name = dimension.get("dimension") if dimension.get("dimension") in allowed_dims else "unknown"; verdict = dimension.get("verdict") if dimension.get("verdict") in allowed_verdicts else "unknown"
            dims.setdefault(name, {})[verdict] = dims.setdefault(name, {}).get(verdict, 0) + 1
            item_severe = item_severe or dimension.get("severe") is True
            numerator=dimension.get("numerator"); denominator=dimension.get("denominator")
            if numerator is not None: checked_int(numerator,"numerator")
            if denominator is not None: checked_int(denominator,"denominator")
            assessments.append({"dimension":name,"verdict":verdict,"numerator":numerator,"denominator":denominator,"severe":dimension.get("severe") is True})
        severe_items += int(item_severe)
    return {"schema":HOST_SCHEMA,"observedAt":observed,"source":{"kind":"configured-local-store","publicExportSchema":"fsgg.telemetry.public-export/1"},
        "scope":{"items":len(public["items"]),"identities":"aggregated-or-explicitly-aliased","freeText":"removed-except-approved-notes"},"totals":totals,"usage":usage,"launcherPopulation":launcher,
        "quality":quality,"operational":operational,"store":{"status":enum((store_status or {}).get("status"),{"ready"},"store status"),"schemaVersion":checked_int((store_status or {}).get("schemaVersion"),"schemaVersion"),"journalMode":enum((store_status or {}).get("journalMode"),{"wal"},"journalMode"),"pendingBatches":checked_int((store_status or {}).get("pendingBatches"),"pendingBatches")},
        "localCi":{"counts":ci_totals,"seconds":seconds,"coverage":ci_coverage,"attribution":"repository-owned item attribution only; time values are summed per-item projections"},
        "budget":{"scope":"current-canonical-epoch","dimensions":dims,"assessments":assessments,"health":budget_health_counts,"severeItems":severe_items,"distinctBreaches":checked_int(status.get("distinctBreaches"),"distinctBreaches"),"dirtyItems":checked_int(status.get("dirtyItems")," in dirtyItems"),"intervention":enum(status.get("intervention"), {"none","open","verified"}, "intervention")},
        "completedItems":completed_items or {"schema":ITEMS_SCHEMA,"coverage":{"eligible":0,"published":0,"unmapped":0,"dirty":0,"incompatible":0},"items":[]}}


def checked_int(value: Any, name: str) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or value < 0 or value > 2**63-1: raise ValueError(f"invalid {name}")
    return value


def enum(value: Any, values: set[str], name: str) -> str:
    if value not in values: raise ValueError(f"invalid {name}")
    return value


def build_host(labels_path: pathlib.Path | None = None) -> dict[str, Any]:
    _, cfg = config(); store, engine = cfg["storeRoot"], cfg["engine"]
    with tempfile.TemporaryDirectory(prefix="fsgg-dashboard-") as directory:
        output = pathlib.Path(directory) / "public.json"
        engine_json(engine, ["telemetry","store","export","--public","--output",str(output),"--store-root",store])
        public = load(output, 65536)
    ids = [item.get("item") for item in public.get("items",[]) if isinstance(item,dict) and isinstance(item.get("item"),str)]
    ci = [engine_json(engine,["telemetry","ci","summary","--item",item,"--store-root",store]) for item in ids]
    budgets = [engine_json(engine,["telemetry","budget","summary","--item",item,"--store-root",store]) for item in ids]
    reconciliations=[]; health=[]
    for item in ids:
        reconciliations.append(engine_json(engine,["telemetry","store","reconcile","--item",item,"--store-root",store]))
        health.append(engine_json(engine,["telemetry","budget","health","--item",item,"--store-root",store]))
    status = engine_json(engine,["telemetry","budget","status","--store-root",store])
    store_status=engine_json(engine,["telemetry","store","status","--store-root",store])
    if not isinstance(store_status,dict) or store_status.get("status")!="ready" or store_status.get("schemaVersion") not in {7,8} or store_status.get("journalMode")!="wal": raise HostSourceError("HOST_STORE_INCOMPATIBLE")
    try: engine_version=tuple(int(part) for part in store_status["nativeEngine"].split(".")[:3])
    except (KeyError,AttributeError,ValueError): raise HostSourceError("HOST_STORE_INCOMPATIBLE")
    if engine_version<(3,51,3): raise HostSourceError("HOST_STORE_INCOMPATIBLE")
    labels=load_labels(labels_path)
    detail_loader=(lambda item: engine_json(engine,["telemetry","item-detail","--item",item,"--store-root",store])) if store_status["schemaVersion"]==8 else None
    completed=project_completed_items(store,status,labels,dict(zip(ids,ci)),dict(zip(ids,budgets)),detail_loader)
    return aggregate_host(public,ci,budgets,status,now(),reconciliations,health,store_status,completed)


def publish(repo: str, branch: str, path: str, token: str, snapshot: dict[str, Any]) -> str:
    encoded = urllib.parse.quote(branch, safe="")
    try:
        ref,_ = github(f"repos/{repo}/git/ref/heads/{encoded}",token); parent=ref["object"]["sha"]
        commit,_=github(f"repos/{repo}/git/commits/{parent}",token); base_tree=commit["tree"]["sha"]
    except RuntimeError as error:
        if "404" not in str(error): raise
        default,_=github(f"repos/{repo}",token); parent=None
        base,_=github(f"repos/{repo}/git/ref/heads/{urllib.parse.quote(default['default_branch'],safe='')}",token)
        base_tree=None
    blob,_=github(f"repos/{repo}/git/blobs",token,"POST",{"content":base64.b64encode(dump(snapshot)).decode(),"encoding":"base64"})
    tree_body={"tree":[{"path":path,"mode":"100644","type":"blob","sha":blob["sha"]}]}
    if base_tree: tree_body["base_tree"]=base_tree
    tree,_=github(f"repos/{repo}/git/trees",token,"POST",tree_body)
    body={"message":"Refresh safe telemetry dashboard snapshot","tree":tree["sha"],"parents":[parent] if parent else []}
    created,_=github(f"repos/{repo}/git/commits",token,"POST",body)
    if parent: github(f"repos/{repo}/git/refs/heads/{encoded}",token,"PATCH",{"sha":created["sha"],"force":False})
    else: github(f"repos/{repo}/git/refs",token,"POST",{"ref":f"refs/heads/{branch}","sha":created["sha"]})
    return created["sha"]


def compose(actions: dict[str, Any], deliveries: dict[str,Any], host: dict[str, Any] | None, source_revision: str, host_revision: str | None = None) -> dict[str, Any]:
    validate_actions(actions)
    validate_deliveries(deliveries)
    if host is not None: validate_host(host)
    if host_revision is not None and (len(host_revision)!=40 or any(c not in "0123456789abcdef" for c in host_revision)): raise ValueError("invalid host revision")
    return {"schema":DASH_SCHEMA,"builtAt":now(),"sourceRevision":source_revision,"hostRevision":host_revision,"actions":actions,"deliveries":deliveries,
        "host":host if host is not None else {"schema":"fsgg.telemetry.dashboard-host-unavailable/1","status":"unconfigured","reason":"No approved host snapshot has been published."}}


def exact(obj: Any, fields: set[str], name: str) -> dict[str, Any]:
    if not isinstance(obj,dict) or set(obj)!=fields: raise ValueError(f"invalid {name} shape")
    return obj


def validate_count_map(value: Any, allowed: set[str], name: str) -> None:
    if not isinstance(value,dict) or set(value)-allowed: raise ValueError(f"invalid {name}")
    for count in value.values(): checked_int(count,name)


def validate_actions(value: Any) -> None:
    exact(value,{"schema","observedAt","repository","selection","runs"},"Actions feed")
    if value["schema"]!="fsgg.telemetry.public-actions/1" or value["repository"]!="FS-GG/.github" or parse_time(value["observedAt"]) is None: raise ValueError("invalid Actions identity")
    selection=exact(value["selection"],{"order","cap","pagesFetched","returned","repositoryTotalAtObservation","truncated","newestCreatedAt","oldestCreatedAt","semantics"},"Actions selection")
    for key in ("cap","pagesFetched","returned"): checked_int(selection[key],key)
    if selection["repositoryTotalAtObservation"] is not None: checked_int(selection["repositoryTotalAtObservation"],"repositoryTotalAtObservation")
    if selection["order"]!="created-descending" or selection["semantics"]!="bounded multi-page sample, deduplicated by run id; latest observed attempt; not an atomic inventory" or not isinstance(selection["truncated"],bool) or selection["returned"]!=len(value["runs"]) or selection["returned"]>selection["cap"] or selection["cap"]>1000: raise ValueError("invalid Actions selection")
    if value["runs"] and (parse_time(selection["newestCreatedAt"]) is None or parse_time(selection["oldestCreatedAt"]) is None): raise ValueError("invalid Actions extent")
    for run in value["runs"]:
        exact(run,{"id","workflow","event","status","conclusion","createdAt","startedAt","updatedAt","durationSeconds","attempt","url"},"Actions run")
        checked_int(run["id"],"run id"); checked_int(run["attempt"],"attempt")
        if run["status"] not in ALLOWED_STATES or run["conclusion"] not in ALLOWED_RESULTS: raise ValueError("invalid Actions state")
        if not isinstance(run["workflow"],str) or len(run["workflow"])>120 or not isinstance(run["event"],str) or len(run["event"])>40: raise ValueError("invalid Actions label")
        if run["url"]!=f"https://github.com/FS-GG/.github/actions/runs/{run['id']}": raise ValueError("invalid Actions link")
        for key in ("createdAt","startedAt","updatedAt"):
            if run[key] is not None and parse_time(run[key]) is None: raise ValueError("invalid Actions timestamp")
        if run["durationSeconds"] is not None: checked_int(run["durationSeconds"],"durationSeconds")


def validate_deliveries(value: Any) -> None:
    exact(value,{"schema","observedAt","repository","selection","deliveries"},"delivery feed")
    if value["schema"]!=DELIVERIES_SCHEMA or value["repository"]!="FS-GG/.github" or parse_time(value["observedAt"]) is None: raise ValueError("invalid delivery identity")
    selection=exact(value["selection"],{"order","cap","pagesFetched","closedScanned","returned","semantics"},"delivery selection")
    for key in ("cap","pagesFetched","closedScanned","returned"): checked_int(selection[key],key)
    if selection["order"]!="closed-updated-descending" or selection["cap"]>500 or selection["returned"]!=len(value["deliveries"]) or selection["returned"]>selection["closedScanned"] or selection["semantics"]!="merged pull requests found in a bounded updated-ordered closed-PR scan; public delivery evidence, not proof of a whole completed item or effort": raise ValueError("invalid delivery selection")
    if not isinstance(value["deliveries"],list): raise ValueError("invalid deliveries")
    seen=set()
    for row in value["deliveries"]:
        exact(row,{"number","title","url","createdAt","mergedAt","elapsedSeconds"},"delivery")
        number=checked_int(row["number"],"delivery number")
        if number in seen or not isinstance(row["title"],str) or len(row["title"])>180: raise ValueError("invalid delivery row")
        seen.add(number)
        if row["url"]!=f"https://github.com/FS-GG/.github/pull/{number}" or parse_time(row["createdAt"]) is None or parse_time(row["mergedAt"]) is None: raise ValueError("invalid delivery evidence")
        if row["elapsedSeconds"] is not None: checked_int(row["elapsedSeconds"],"delivery elapsed")


def validate_host(value: Any) -> None:
    schema=value.get("schema") if isinstance(value,dict) else None
    aggregate_only=schema=="fsgg.telemetry.dashboard-host/1"
    fields={"schema","observedAt","source","scope","totals","usage","launcherPopulation","quality","operational","store","localCi","budget"}
    if not aggregate_only: fields.add("completedItems")
    exact(value,fields,"host feed")
    if value["schema"] not in {HOST_SCHEMA,*LEGACY_HOST_SCHEMAS} or parse_time(value["observedAt"]) is None: raise ValueError("invalid host identity")
    exact(value["source"],{"kind","publicExportSchema"},"host source"); exact(value["scope"],{"items","identities","freeText"},"host scope")
    identity="aggregated-and-removed" if aggregate_only else "aggregated-or-explicitly-aliased"; free="removed" if aggregate_only else "removed-except-approved-notes"
    if value["source"]!={"kind":"configured-local-store","publicExportSchema":"fsgg.telemetry.public-export/1"} or value["scope"]["identities"]!=identity or value["scope"]["freeText"]!=free: raise ValueError("invalid host safety declaration")
    checked_int(value["scope"]["items"],"items"); validate_count_map(value["totals"],{"factCount","usageObservations","deliveryObservations"},"totals")
    exact(value["usage"],{"input","cachedInput","cacheWriteInput","output","total","reasoning"},"usage")
    for key,count in value["usage"].items():
        if count is not None: checked_int(count,key)
    validate_count_map(value["launcherPopulation"],{"admitted","started","terminal","usage","missingAdmission","missingStart","missingTerminal","missingUsage"},"launcher")
    quality=exact(value["quality"],{"recordValidity","joinIntegrity","populationCoverage","qualification"},"quality")
    quality_values={"valid","invalid","matched","mismatch","conflict","unknown","partial","complete","not-evaluated","qualified","not-qualified"}
    for key in quality: validate_count_map(quality[key],quality_values,key)
    operational=exact(value["operational"],{"expected","lineage","timing","usageCoverage","terminalOutcomeCoverage"},"operational")
    checked_int(operational["expected"],"expected"); validate_count_map(operational["lineage"],{"matched","unknown","invalid","unsupported","outOfScope"},"lineage"); validate_count_map(operational["timing"],{"complete","late","missing","invalid","notEvaluated"},"timing")
    validate_count_map(operational["usageCoverage"],{"not-evaluated"},"usage coverage"); validate_count_map(operational["terminalOutcomeCoverage"],{"not-evaluated"},"terminal outcome coverage")
    store=exact(value["store"],{"status","schemaVersion","journalMode","pendingBatches"},"store")
    enum(store["status"],{"ready"},"store status"); enum(store["journalMode"],{"wal"},"journalMode"); checked_int(store["schemaVersion"],"schemaVersion"); checked_int(store["pendingBatches"],"pendingBatches")
    local=exact(value["localCi"],{"counts","seconds","coverage","attribution"},"local CI"); validate_count_map(local["counts"],{"runs","attempts","jobs","steps"},"local CI counts")
    coverage=exact(local["coverage"],{"inventoryCoverage","checkCoverage","attemptCoverage","jobPageCoverage","terminalCoverage","timestampCoverage","lineageCoverage","classificationCoverage","criticalPathCoverage"},"local CI coverage")
    for key in coverage: validate_count_map(coverage[key],{"unknown","partial","complete","not-evaluated"},key)
    exact(local["seconds"],{"runnerSeconds","wallSeconds","queueSeconds","usefulValidationSeconds","administrativeSeconds","necessarySetupSeconds","mixedSeconds","unclassifiedSeconds"},"local CI seconds")
    for key,count in local["seconds"].items():
        exact(count,{"knownItems","unknownItems","totalItemSeconds"},key)
        for field in count: checked_int(count[field],field)
    if local["attribution"]!="repository-owned item attribution only; time values are summed per-item projections": raise ValueError("invalid local CI attribution")
    budget=exact(value["budget"],{"scope","dimensions","assessments","health","severeItems","distinctBreaches","dirtyItems","intervention"},"budget")
    if budget["scope"]!="current-canonical-epoch": raise ValueError("invalid budget scope")
    for key in ("severeItems","distinctBreaches","dirtyItems"): checked_int(budget[key],key)
    enum(budget["intervention"],{"none","open","verified"},"intervention")
    validate_count_map(budget["health"],{"complete","open","pending","missing-outcome","unknown"},"budget health")
    if not isinstance(budget["dimensions"],dict) or set(budget["dimensions"])-{"model-usage","owner-effort","priced-cost","critical-path-delay","ci-runner-administration","unknown"}: raise ValueError("invalid dimensions")
    for key,counts in budget["dimensions"].items(): validate_count_map(counts,{"unknown","not-applicable","pass","breach"},key)
    if not isinstance(budget["assessments"],list) or len(budget["assessments"])>1000: raise ValueError("invalid assessments")
    for assessment in budget["assessments"]:
        exact(assessment,{"dimension","verdict","numerator","denominator","severe"},"assessment")
        enum(assessment["dimension"],{"model-usage","owner-effort","priced-cost","critical-path-delay","ci-runner-administration","unknown"},"dimension"); enum(assessment["verdict"],{"unknown","not-applicable","pass","breach"},"verdict")
        if not isinstance(assessment["severe"],bool): raise ValueError("invalid severe")
        for key in ("numerator","denominator"):
            if assessment[key] is not None: checked_int(assessment[key],key)
    if not aggregate_only: validate_completed_items(value["completedItems"])


def public_text(value: Any, maximum: int, name: str) -> str:
    if not isinstance(value,str) or not 1<=len(value)<=maximum: raise ValueError(f"invalid {name}")
    return value


def validate_completed_items(value: Any) -> None:
    exact(value,{"schema","coverage","items"},"completed items")
    legacy=value["schema"]=="fsgg.telemetry.completed-items/1"
    if value["schema"] not in {ITEMS_SCHEMA,"fsgg.telemetry.completed-items/1"} or not isinstance(value["items"],list) or len(value["items"])>200: raise ValueError("invalid completed items")
    validate_count_map(value["coverage"],{"eligible","published","unmapped","dirty","incompatible"},"completed item coverage")
    if value["coverage"]["published"]!=len(value["items"]): raise ValueError("invalid completed item count")
    seen=set()
    for item in value["items"]:
        fields={"key","label","url","state","deliveredAt","deliveries","runtime","ci","budget","complications"}
        if not legacy: fields.add("process")
        exact(item,fields,"completed item")
        key=public_text(item["key"],64,"item key")
        if key in seen or not re.fullmatch(r"[a-z0-9][a-z0-9-]{0,63}",key): raise ValueError("invalid item key")
        seen.add(key); public_text(item["label"],120,"item label"); enum(item["state"],{"settled"},"item state")
        if not re.fullmatch(r"https://github\.com/FS-GG/[A-Za-z0-9_.-]+(?:/(?:issues|pull)/[1-9][0-9]*)?",item["url"]): raise ValueError("invalid item URL")
        if item["deliveredAt"] is not None and parse_time(item["deliveredAt"]) is None: raise ValueError("invalid delivered time")
        if not isinstance(item["deliveries"],list) or len(item["deliveries"])>32: raise ValueError("invalid item deliveries")
        for delivery in item["deliveries"]:
            exact(delivery,{"repository","number","url","mergedAt"},"item delivery"); public_text(delivery["repository"],100,"repository"); number=checked_int(delivery["number"],"PR")
            if not re.fullmatch(r"FS-GG/[A-Za-z0-9_.-]+",delivery["repository"]) or delivery["url"]!=f"https://github.com/{delivery['repository']}/pull/{number}" or (delivery["mergedAt"] is not None and parse_time(delivery["mergedAt"]) is None): raise ValueError("invalid item delivery")
        runtime=exact(item["runtime"],{"invocations","terminalOutcomes","duration","tokens"},"item runtime"); checked_int(runtime["invocations"],"invocations")
        validate_count_map(runtime["terminalOutcomes"],{"completed","failed","cancelled","launch-failed","other"},"terminal outcomes")
        duration_value=exact(runtime["duration"],{"rows","semantics"},"runtime duration")
        if duration_value["semantics"]!="same-clock non-reversed invocation spans summed by role; roles and invocations may overlap in wall time" or not isinstance(duration_value["rows"],list): raise ValueError("invalid runtime duration")
        for row in duration_value["rows"]:
            exact(row,{"role","invocations","known","unknown","summedSeconds"},"duration row"); enum(row["role"],{"root","child","follow-up","unknown"},"role")
            for field in ("invocations","known","unknown","summedSeconds"): checked_int(row[field],field)
        tokens=exact(runtime["tokens"],{"scope","rows","unmappedRows","coverage"},"tokens"); checked_int(tokens["unmappedRows"],"unmapped rows"); validate_count_map(tokens["coverage"],{"invocationsWithUsage","invocationsWithoutUsage","runtimeGaps"},"token coverage")
        if tokens["scope"]!="completed native turns; input includes cached input" or not isinstance(tokens["rows"],list) or len(tokens["rows"])>512: raise ValueError("invalid token rows")
        for row in tokens["rows"]:
            exact(row,{"role","requestedModel","observedModel","requestedEffort","observedEffort","scope","turns","input","cachedInput","output","reasoning","total"},"token row"); enum(row["role"],{"root","child","follow-up","unknown"},"role")
            for name in ("requestedModel","observedModel","requestedEffort","observedEffort","scope"): public_text(row[name],48,name)
            for name in ("turns","input","cachedInput","output","total"): checked_int(row[name],name)
            if row["reasoning"] is not None: checked_int(row["reasoning"],"reasoning")
        ci=exact(item["ci"],{"counts","seconds","semantics"},"item CI"); validate_count_map(ci["counts"],{"runs","attempts","jobs","steps"},"item CI counts")
        exact(ci["seconds"],{"runnerSeconds","wallSeconds","queueSeconds","usefulValidationSeconds","administrativeSeconds","necessarySetupSeconds","mixedSeconds","unclassifiedSeconds"},"item CI seconds")
        for metric in ci["seconds"].values():
            exact(metric,{"knownItems","unknownItems","totalItemSeconds"},"item CI metric")
            for count in metric.values(): checked_int(count,"item CI metric")
        if ci["semantics"]!="runner seconds sum jobs; wall, queue, and category values are per-item unions and may overlap": raise ValueError("invalid item CI semantics")
        item_budget=exact(item["budget"],{"scope","assessments"},"item budget")
        if item_budget["scope"]!="canonical reducer assessments; epoch identities removed" or not isinstance(item_budget["assessments"],list) or len(item_budget["assessments"])>64: raise ValueError("invalid item budget")
        for assessment in item_budget["assessments"]:
            exact(assessment,{"epoch","dimension","verdict","numerator","denominator","severe"},"item assessment"); enum(assessment["epoch"],{"current","historical"},"epoch position"); enum(assessment["dimension"],{"model-usage","owner-effort","priced-cost","critical-path-delay","ci-runner-administration"},"dimension"); enum(assessment["verdict"],{"unknown","not-applicable","pass","breach"},"verdict")
            if not isinstance(assessment["severe"],bool): raise ValueError("invalid severe")
            for name in ("numerator","denominator"):
                if assessment[name] is not None: checked_int(assessment[name],name)
        complications=exact(item["complications"],{"observed","notes","semantics"},"complications"); validate_count_map(complications["observed"],{"runtimeNonSuccess","failedOrCancelledCiRuns","repeatedCiRuns","followUpInvocations"},"observed complications")
        semantics={"observed events and approved notes; no inferred cause or repair cost; development phases and repair attribution unavailable"} if legacy else {"observed runtime and CI signals plus separately approved public notes; no inferred cause or repair cost"}
        if complications["semantics"] not in semantics or not isinstance(complications["notes"],list) or len(complications["notes"])>8: raise ValueError("invalid complications")
        for note in complications["notes"]:
            exact(note,{"kind","text","evidenceUrl"},"note"); enum(note["kind"],{"repair","complication"},"note kind"); public_text(note["text"],240,"note")
            if not re.fullmatch(r"https://github\.com/FS-GG/[A-Za-z0-9_.-]+/(?:issues|pull|actions/runs)/[1-9][0-9]*",note["evidenceUrl"]): raise ValueError("invalid note evidence")
        if not legacy: validate_process_detail(item["process"])


def validate_process_detail(value: Any) -> None:
    exact(value,{"schema","availability","members","truncated","activities","attribution","complications","reviews","observation"},"process detail")
    if value["schema"]!=PROCESS_SCHEMA: raise ValueError("invalid process detail schema")
    enum(value["availability"],{"available","unsupported"},"process availability")
    members=exact(value["members"],{"requested","available"},"process members")
    for count in members.values(): checked_int(count,"process members")
    if members["available"]>members["requested"]: raise ValueError("invalid process coverage")
    trunc=exact(value["truncated"],{"activities","attributions","complications","reviews"},"process truncation")
    if any(not isinstance(flag,bool) for flag in trunc.values()): raise ValueError("invalid process truncation")
    if value["observation"]!="engine item-detail and store projections are independently read and are not one atomic snapshot": raise ValueError("invalid process observation")
    activities=exact(value["activities"],{"rows","summary","semantics"},"activities")
    if not isinstance(activities["rows"],list) or not isinstance(activities["summary"],list) or activities["semantics"]!="activity spans may overlap; summed activity time is not owner effort or an elapsed-time partition" or len(activities["rows"])>512 or len(activities["summary"])>10: raise ValueError("invalid activities")
    for row in activities["rows"]:
        exact(row,{"category","startedAt","endedAt","durationSeconds"},"activity row"); enum(row["category"],ACTIVITY_CATEGORIES,"activity category")
        start=parse_time(row["startedAt"]); end=parse_time(row["endedAt"]) if row["endedAt"] is not None else None
        if start is None or (row["endedAt"] is not None and (end is None or end<start)): raise ValueError("invalid activity interval")
        if row["durationSeconds"] is not None: checked_int(row["durationSeconds"],"activity duration")
    for row in activities["summary"]:
        exact(row,{"category","spans","open","knownDuration","summedSeconds"},"activity summary"); enum(row["category"],ACTIVITY_CATEGORIES,"activity category")
        for key in ("spans","open","knownDuration","summedSeconds"): checked_int(row[key],key)
    attribution=exact(value["attribution"],{"rows","accounting","crossRead","semantics"},"activity attribution")
    if not isinstance(attribution["rows"],list) or attribution["semantics"]!="native and attributed totals are related, not additive; missing attribution counts usage rows; totals can span incompatible private accounting scopes" or len(attribution["rows"])>768: raise ValueError("invalid activity attribution")
    enum(attribution["crossRead"],{"matched","partial","unavailable"},"cross-read consistency")
    for row in attribution["rows"]:
        exact(row,{"classification","activityCategory","records","input","cachedInput","output","reasoning","total"},"attribution row"); enum(row["classification"],ATTRIBUTION_CLASSES,"classification")
        if row["activityCategory"] is not None: enum(row["activityCategory"],ACTIVITY_CATEGORIES|{"unallocated"},"attributed category")
        for key in ("records","input","cachedInput","output","total"): checked_int(row[key],key)
        if row["reasoning"] is not None: checked_int(row["reasoning"],"reasoning")
    accounting=exact(attribution["accounting"],{"nativeTotal","direct","mixed","unclassified","missingAttribution"},"public accounting")
    for count in accounting.values(): checked_int(count,"accounting")
    complications=exact(value["complications"],{"rows"},"recorded complications")
    if not isinstance(complications["rows"],list) or len(complications["rows"])>512: raise ValueError("too many complications")
    for row in complications["rows"]:
        exact(row,{"trigger","cause","occurredAt","activityCategory"},"complication row"); enum(row["trigger"],COMPLICATION_TRIGGERS,"trigger"); enum(row["cause"],COMPLICATION_CAUSES,"cause")
        if parse_time(row["occurredAt"]) is None: raise ValueError("invalid complication time")
        if row["activityCategory"] is not None: enum(row["activityCategory"],ACTIVITY_CATEGORIES,"complication activity")
    reviews=exact(value["reviews"],{"rows","semantics"},"reviews")
    if not isinstance(reviews["rows"],list) or reviews["semantics"]!="review counts omit private findings text; confidence and duration do not establish item, effort, or token completeness" or len(reviews["rows"])>256: raise ValueError("invalid reviews")
    count_keys={"wentWell","problems","avoidableDelayOrRework","processObservations","remainingRisks","concreteImprovements"}
    for row in reviews["rows"]:
        exact(row,{"scope","revision","evidenceCoverage","populationCoverage","confidence","reviewerModel","reviewerEffort","reviewedAt","durationSeconds","counts"},"review row")
        enum(row["scope"],{"attempt","item"},"review scope"); enum(row["evidenceCoverage"],REVIEW_COVERAGE,"review evidence"); enum(row["populationCoverage"],REVIEW_COVERAGE,"review population"); enum(row["confidence"],{"low","medium","high"},"review confidence")
        checked_int(row["revision"],"review revision"); checked_int(row["durationSeconds"],"review duration")
        if row["revision"]<1 or parse_time(row["reviewedAt"]) is None: raise ValueError("invalid review revision")
        public_text(row["reviewerModel"],48,"reviewer model"); public_text(row["reviewerEffort"],48,"reviewer effort"); validate_count_map(row["counts"],count_keys,"review counts")


def main() -> int:
    parser=argparse.ArgumentParser(); subs=parser.add_subparsers(dest="cmd",required=True)
    actions=subs.add_parser("collect-actions"); actions.add_argument("--repo",default="FS-GG/.github"); actions.add_argument("--cap",type=int,default=1000); actions.add_argument("--output",type=pathlib.Path,required=True)
    deliveries=subs.add_parser("collect-deliveries"); deliveries.add_argument("--repo",default="FS-GG/.github"); deliveries.add_argument("--cap",type=int,default=200); deliveries.add_argument("--output",type=pathlib.Path,required=True)
    host=subs.add_parser("host-snapshot"); host.add_argument("--output",type=pathlib.Path,required=True); host.add_argument("--labels",type=pathlib.Path); host.add_argument("--dry-run",action="store_true"); host.add_argument("--repo"); host.add_argument("--branch",default="telemetry-data"); host.add_argument("--path",default="host.json")
    comp=subs.add_parser("compose"); comp.add_argument("--actions",type=pathlib.Path,required=True); comp.add_argument("--deliveries",type=pathlib.Path,required=True); comp.add_argument("--host",type=pathlib.Path); comp.add_argument("--host-revision"); comp.add_argument("--source-revision",required=True); comp.add_argument("--output",type=pathlib.Path,required=True)
    args=parser.parse_args(); token=os.environ.get("GITHUB_TOKEN","")
    if args.cmd=="collect-actions":
        if not token: raise ValueError("GITHUB_TOKEN is required")
        atomic(args.output,collect_actions(args.repo,token,args.cap)); return 0
    if args.cmd=="collect-deliveries":
        if not token: raise ValueError("GITHUB_TOKEN is required")
        atomic(args.output,collect_deliveries(args.repo,token,args.cap)); return 0
    if args.cmd=="host-snapshot":
        labels=args.labels or (pathlib.Path(os.environ["FSGG_TELEMETRY_DASHBOARD_LABELS"]) if os.environ.get("FSGG_TELEMETRY_DASHBOARD_LABELS") else None)
        snap=build_host(labels); atomic(args.output,snap)
        if not args.dry_run:
            if not token or not args.repo: raise ValueError("GITHUB_TOKEN and --repo are required to publish")
            print(publish(args.repo,args.branch,args.path,token,snap))
        return 0
    host_value=load(args.host) if args.host and args.host.exists() else None
    atomic(args.output,compose(load(args.actions),load(args.deliveries),host_value,args.source_revision,args.host_revision)); return 0


if __name__ == "__main__":
    try: raise SystemExit(main())
    except HostSourceError as error:
        print(f"telemetry-dashboard: {error}",file=sys.stderr); raise SystemExit(1)
    except RefConflict:
        print("telemetry-dashboard: PUBLISH_REF_CONFLICT",file=sys.stderr); raise SystemExit(1)
    except (ValueError,RuntimeError,json.JSONDecodeError):
        print("telemetry-dashboard: INVALID_PUBLIC_DATA",file=sys.stderr); raise SystemExit(1)
