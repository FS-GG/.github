#!/usr/bin/env python3
"""Build and publish the bounded, public FS-GG telemetry dashboard feed."""

from __future__ import annotations

import argparse
import base64
import datetime as dt
import gzip
import hashlib
import importlib.util
import io
import json
import os
import pathlib
import re
import shutil
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
MAX_CANONICAL_SNAPSHOT = 4 * MAX_JSON
MAX_API_JSON = 4 * 1_048_576
HOST_SCHEMA = "fsgg.telemetry.dashboard-host/3"
LEGACY_HOST_SCHEMAS = {"fsgg.telemetry.dashboard-host/1","fsgg.telemetry.dashboard-host/2"}
DASH_SCHEMA = "fsgg.telemetry.dashboard/2"
DELIVERIES_SCHEMA = "fsgg.telemetry.public-deliveries/1"
ITEMS_SCHEMA = "fsgg.telemetry.completed-items/2"
PROCESS_SCHEMA = "fsgg.telemetry.item-process-detail/1"
LABELS_SCHEMA = "fsgg.telemetry.dashboard-labels/1"
EVENT_RECEIPT_SCHEMA = "fsgg.telemetry.dashboard-event-activation/1"
EVENT_HEALTH_SCHEMA = "fsgg.telemetry.dashboard-event-health/1"
EVENT_RECEIPT_NAME = "telemetry-dashboard-event-activation.json"
EVENT_HEALTH_NAME = "telemetry-dashboard-event-health.json"
EVENT_LOCK_NAME = "telemetry-dashboard-event.lock"
EVENT_REASON_CODES={
    "EVENT_ACTIVATION_RECEIPT_UNSAFE","EVENT_ACTIVATION_RECEIPT_INVALID","EVENT_LOCK_UNSAFE",
    "EVENT_CONFIG_CHANGED","EVENT_ENGINE_CHANGED","EVENT_LABELS_CHANGED","EVENT_PRIVATE_INPUT_CHANGED",
    "PUBLISHER_CREDENTIAL_SOURCE_UNAVAILABLE","PUBLISHER_TOKEN_UNAVAILABLE","HOST_ENGINE_UNAVAILABLE",
    "HOST_ENGINE_PROJECTION_FAILED","HOST_ENGINE_OUTPUT_TOO_LARGE","HOST_ENGINE_INVALID_JSON",
    "HOST_ENGINE_SNAPSHOT_INCOMPATIBLE","HOST_ENGINE_SNAPSHOT_REVISION_MISMATCH","HOST_ENGINE_SNAPSHOT_INCOMPLETE",
    "HOST_ENGINE_SNAPSHOT_MALFORMED","HOST_STORE_INCOMPATIBLE","HOST_LABELS_UNSAFE",
    "EVENT_PUBLIC_PAYLOAD_INVALID","EVENT_PUBLICATION_VERIFICATION_FAILED","EVENT_REFRESH_FAILED",
}
ALLOWED_STATES = {"queued", "in_progress", "completed", "requested", "waiting", "pending"}
ALLOWED_RESULTS = {"success", "failure", "cancelled", "skipped", "timed_out", "action_required", "neutral", "stale", "startup_failure", None}
ACTIVITY_CATEGORIES={"planning","implementation","review","validation","delivery","repair","operations","other","unclassified"}
ATTRIBUTION_CLASSES={"direct","mixed","unclassified"}
COMPLICATION_TRIGGERS={"test-failure","review-finding","ci-failure","tooling","runtime","dependency","authority","operation","human-change","unknown","other"}
COMPLICATION_CAUSES={"product-defect","test-defect","process-defect","infrastructure","tooling","dependency","requirements","authorization","external","unknown","other"}
REVIEW_COVERAGE={"complete","partial","unknown"}


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


def validate_private_parent(path: pathlib.Path) -> None:
    parent=path.parent.lstat()
    if path.parent.is_symlink() or not stat.S_ISDIR(parent.st_mode) or stat.S_IMODE(parent.st_mode)&0o077:
        raise HostSourceError("EVENT_PRIVATE_FILE_UNSAFE")


def atomic_private(path: pathlib.Path, value: Any) -> None:
    data=dump(value)
    path.parent.mkdir(parents=True,exist_ok=True)
    validate_private_parent(path)
    if path.is_symlink() or (path.exists() and not path.is_file()): raise HostSourceError("EVENT_PRIVATE_FILE_UNSAFE")
    fd,name=tempfile.mkstemp(prefix=path.name+".",dir=path.parent)
    try:
        os.fchmod(fd,0o600)
        with os.fdopen(fd,"wb") as stream: stream.write(data); stream.flush(); os.fsync(stream.fileno())
        os.replace(name,path)
    finally:
        if os.path.exists(name): os.unlink(name)


def read_private_bytes(path: pathlib.Path, maximum: int, error_code: str) -> bytes:
    flags=os.O_RDONLY
    if hasattr(os,"O_NOFOLLOW"): flags|=os.O_NOFOLLOW
    try: fd=os.open(path,flags)
    except OSError as error: raise HostSourceError(error_code) from error
    try:
        info=os.fstat(fd)
        if not stat.S_ISREG(info.st_mode) or stat.S_IMODE(info.st_mode)!=0o600 or info.st_size>maximum: raise HostSourceError(error_code)
        chunks=[]; total=0
        while True:
            chunk=os.read(fd,min(65536,maximum+1-total))
            if not chunk: break
            chunks.append(chunk); total+=len(chunk)
            if total>maximum: raise HostSourceError(error_code)
        return b"".join(chunks)
    finally: os.close(fd)


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


def config(explicit: pathlib.Path | None = None) -> tuple[pathlib.Path, dict[str, str]]:
    helper = pathlib.Path(__file__).resolve().parents[1] / ".claude/skills/work-roadmap/scripts/fsgg_telemetry_defaults.py"
    spec = importlib.util.spec_from_file_location("dashboard_telemetry_defaults", helper)
    if spec is None or spec.loader is None: raise HostSourceError("HOST_CONFIG_HELPER_UNAVAILABLE")
    module = importlib.util.module_from_spec(spec); sys.modules[spec.name] = module; spec.loader.exec_module(module)
    found = module.discover_config(str(explicit) if explicit is not None else None)
    if found is None: raise HostSourceError("HOST_NOT_CONFIGURED")
    return found.path, {"storeRoot":str(found.store_root),"engine":found.engine}


def engine_json(engine: str, args: list[str]) -> Any:
    try: done = subprocess.run([engine, *args], capture_output=True, text=True, timeout=45, check=False)
    except (OSError, subprocess.SubprocessError) as error: raise HostSourceError("HOST_ENGINE_UNAVAILABLE") from error
    if done.returncode: raise HostSourceError("HOST_ENGINE_PROJECTION_FAILED")
    if len(done.stdout.encode("utf-8")) > MAX_JSON: raise HostSourceError("HOST_ENGINE_OUTPUT_TOO_LARGE")
    try: return json.loads(done.stdout)
    except json.JSONDecodeError as error: raise HostSourceError("HOST_ENGINE_INVALID_JSON") from error


def bounded_base64(value: Any, maximum: int, error_code: str) -> bytes:
    encoded_limit=((maximum+2)//3)*4
    # GitHub may line-wrap base64 throughout the response. Permit one ASCII
    # whitespace separator per encoded byte while keeping transport bounded.
    if not isinstance(value,str) or len(value)>encoded_limit*2+4: raise HostSourceError(error_code)
    normalized=re.sub(r"[ \t\r\n]","",value)
    if len(normalized)>encoded_limit or re.search(r"[^A-Za-z0-9+/=]",normalized): raise HostSourceError(error_code)
    try: decoded=base64.b64decode(normalized,validate=True)
    except (ValueError,base64.binascii.Error) as error: raise HostSourceError(error_code) from error
    if len(decoded)>maximum: raise HostSourceError(error_code)
    return decoded


def load_labels(path: pathlib.Path | None) -> dict[str, Any]:
    if path is None: return {"schema":LABELS_SCHEMA,"items":{},"models":{},"efforts":{},"scopes":{}}
    if path.is_symlink() or not path.is_file() or stat.S_IMODE(path.stat().st_mode) != 0o600:
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


def snapshot_rows(snapshot: dict[str, Any], name: str, members: set[str] | None = None) -> list[dict[str, Any]]:
    rows=snapshot.get(name)
    if not isinstance(rows,list) or len(rows)>10000 or any(not isinstance(row,dict) for row in rows): raise HostSourceError("HOST_ENGINE_SNAPSHOT_MALFORMED")
    return rows if members is None else [row for row in rows if row.get("item_id") in members]


def snapshot_ci(snapshot: dict[str,Any], item: str) -> dict[str,Any]:
    members={item}; runs=snapshot_rows(snapshot,"ciRuns",members); jobs=snapshot_rows(snapshot,"ciJobs",members); steps=snapshot_rows(snapshot,"ciSteps",members)
    def seconds(rows: list[dict[str,Any]], start: str, end: str) -> int | None:
        spans=[]
        for row in rows:
            a,b=parse_time(row.get(start)),parse_time(row.get(end))
            if a is not None and b is not None and b>=a: spans.append((a,b))
        if not spans: return None
        spans.sort(); total=0; first,last=spans[0]
        for a,b in spans[1:]:
            if a<=last: last=max(last,b)
            else: total+=int((last-first).total_seconds()); first,last=a,b
        return total+int((last-first).total_seconds())
    latest={}
    for row in snapshot_rows(snapshot,"ciPopulationCoverage",members): latest=row
    legacy={}
    for row in snapshot_rows(snapshot,"ciCoverage",members): legacy=row
    category={name:seconds([r for r in steps if r.get("classification")==classification],"started_at","completed_at") for name,classification in (("usefulValidationSeconds","useful-validation"),("administrativeSeconds","admin"),("necessarySetupSeconds","necessary-setup"),("mixedSeconds","mixed"),("unclassifiedSeconds","unclassified"))}
    return {"runs":len({(r.get("repository"),r.get("run_id")) for r in runs+jobs}),"attempts":len({(r.get("repository"),r.get("run_id"),r.get("attempt")) for r in runs+jobs}),"jobs":len(jobs),"steps":len(steps),"runnerSeconds":sum(int((b-a).total_seconds()) for r in jobs if (a:=parse_time(r.get("started_at"))) and (b:=parse_time(r.get("completed_at"))) and b>=a),"wallSeconds":seconds(jobs,"started_at","completed_at"),"queueSeconds":seconds(jobs,"created_at","started_at"),**category,
        "inventoryCoverage":latest.get("actions",legacy.get("inventory","unknown")),"checkCoverage":latest.get("checks","unknown"),"attemptCoverage":latest.get("attempts",legacy.get("attempts","unknown")),"jobPageCoverage":latest.get("jobs",legacy.get("job_pages","unknown")),"terminalCoverage":latest.get("terminal",legacy.get("terminal","unknown")),"timestampCoverage":latest.get("timestamps",legacy.get("timestamps","unknown")),"lineageCoverage":legacy.get("lineage","unknown"),"classificationCoverage":legacy.get("classification","unknown"),"criticalPathCoverage":legacy.get("critical_path","unknown")}


def snapshot_budget(snapshot: dict[str,Any], item: str) -> dict[str,Any]:
    latest={}
    for row in snapshot_rows(snapshot,"budgetAssessments",{item}):
        key=(row.get("dimension"),row.get("provider"),row.get("accounting_scope"))
        if key not in latest or checked_int(row.get("assessment_revision"),"assessment revision")>checked_int(latest[key].get("assessment_revision"),"assessment revision"): latest[key]=row
    return {"dimensions":[{"dimension":r.get("dimension"),"provider":r.get("provider"),"accountingScope":r.get("accounting_scope"),"verdict":r.get("verdict"),"numerator":r.get("numerator"),"denominator":r.get("denominator"),"severe":r.get("severe")==1,"reason":r.get("reason"),"epoch":r.get("epoch_id")} for r in latest.values()]}


def _json_array(value: Any, name: str) -> list[Any]:
    try: parsed=json.loads(value) if isinstance(value,str) else value
    except json.JSONDecodeError as error: raise ValueError(f"invalid private {name}") from error
    if not isinstance(parsed,list) or len(parsed)>16: raise ValueError(f"invalid private {name}")
    return parsed


def project_process_detail(snapshot: dict[str,Any], members: set[str], labels: dict[str,Any], native_total: int) -> dict[str,Any]:
    activities=snapshot_rows(snapshot,"activities",members); attributions=snapshot_rows(snapshot,"activityUsageAttributions",members); complications=snapshot_rows(snapshot,"complications",members); reviews=snapshot_rows(snapshot,"reviews",members)
    if len(activities)>512 or len(attributions)>768 or len(complications)>512 or len(reviews)>256: raise ValueError("grouped item process detail exceeds public bound")
    lookup={}; activity_rows=[]; summary={}
    for row in activities:
        category=enum(row.get("category"),ACTIVITY_CATEGORIES,"activity category"); start=parse_time(row.get("started_at")); end=parse_time(row.get("ended_at")) if row.get("ended_at") is not None else None
        if start is None or (row.get("ended_at") is not None and (end is None or end<start)): raise ValueError("invalid activity interval")
        lookup[(row.get("item_id"),row.get("activity_id"))]=category; seconds=int((end-start).total_seconds()) if end else None
        activity_rows.append({"category":category,"startedAt":row.get("started_at"),"endedAt":row.get("ended_at"),"durationSeconds":seconds})
        bucket=summary.setdefault(category,{"category":category,"spans":0,"open":0,"knownDuration":0,"summedSeconds":0}); bucket["spans"]+=1; bucket["open"]+=int(end is None); bucket["knownDuration"]+=int(seconds is not None); bucket["summedSeconds"]+=seconds or 0
    attributed={}; accounted={"direct":0,"mixed":0,"unclassified":0}; usage_identities=set()
    for row in attributions:
        classification=enum(row.get("classification"),ATTRIBUTION_CLASSES,"attribution classification"); category=lookup.get((row.get("item_id"),row.get("activity_id"))) if classification=="direct" else None
        if classification=="direct" and category is None: category="unallocated"
        key=(classification,category); bucket=attributed.setdefault(key,{"classification":classification,"activityCategory":category,"records":0,"input":0,"cachedInput":0,"output":0,"reasoning":0,"reasoningKnown":True,"total":0}); bucket["records"]+=1
        for source,target in (("input_count","input"),("cached_input","cachedInput"),("output_count","output"),("total","total")): bucket[target]+=checked_int(row.get(source),source)
        if row.get("reasoning") is None: bucket["reasoningKnown"]=False
        else: bucket["reasoning"]+=checked_int(row.get("reasoning"),"reasoning")
        accounted[classification]+=checked_int(row.get("total"),"total"); usage_identities.add(row.get("usage_identity"))
    attribution_rows=[]
    for row in attributed.values():
        if not row.pop("reasoningKnown"): row["reasoning"]=None
        attribution_rows.append(row)
    complication_rows=[]
    for row in complications:
        complication_rows.append({"trigger":enum(row.get("trigger"),COMPLICATION_TRIGGERS,"complication trigger"),"cause":enum(row.get("cause"),COMPLICATION_CAUSES,"complication cause"),"occurredAt":row.get("occurred_at"),"activityCategory":lookup.get((row.get("item_id"),row.get("activity_id"))) if row.get("activity_id") is not None else None})
        if parse_time(row.get("occurred_at")) is None: raise ValueError("invalid complication time")
    list_columns=(("wentWell","went_well"),("problems","problems"),("avoidableDelayOrRework","avoidable_delay_rework"),("processObservations","process_observations"),("remainingRisks","remaining_risks"),("concreteImprovements","concrete_improvements")); review_rows=[]
    for row in reviews:
        counts={public:len(_json_array(row.get(private),private)) for public,private in list_columns}
        review_rows.append({"scope":enum(row.get("scope"),{"attempt","item"},"review scope"),"revision":checked_int(row.get("fact_revision"),"review revision"),"evidenceCoverage":enum(row.get("evidence_coverage"),REVIEW_COVERAGE,"review evidence"),"populationCoverage":enum(row.get("population_coverage"),REVIEW_COVERAGE,"review population"),"confidence":enum(row.get("confidence"),{"low","medium","high"},"review confidence"),"reviewerModel":labels["models"].get(row.get("reviewer_model"),"unknown"),"reviewerEffort":labels["efforts"].get(row.get("reviewer_effort"),"unknown"),"reviewedAt":row.get("reviewed_at"),"durationSeconds":checked_int(row.get("duration_seconds"),"review duration"),"counts":counts})
    missing=len([row for row in snapshot_rows(snapshot,"usage",members) if row.get("identity") not in usage_identities])
    return {"schema":PROCESS_SCHEMA,"availability":"available","members":{"requested":len(members),"available":len(members)},"truncated":{"activities":False,"attributions":False,"complications":False,"reviews":False},"activities":{"rows":sorted(activity_rows,key=lambda r:r["startedAt"]),"summary":sorted(summary.values(),key=lambda r:r["category"]),"semantics":"activity spans may overlap; summed activity time is not owner effort or an elapsed-time partition"},"attribution":{"rows":sorted(attribution_rows,key=lambda r:(r["classification"],r["activityCategory"] or "")),"accounting":{"nativeTotal":native_total,**accounted,"missingAttribution":missing},"crossRead":"matched","semantics":"native and attributed totals are related, not additive; missing attribution counts usage rows; totals can span incompatible private accounting scopes"},"complications":{"rows":sorted(complication_rows,key=lambda r:r["occurredAt"])},"reviews":{"rows":sorted(review_rows,key=lambda r:(r["scope"],-r["revision"])),"semantics":"review counts omit private findings text; confidence and duration do not establish item, effort, or token completeness"},"observation":"all process and item projections share one engine-owned database snapshot"}


def project_completed_items(snapshot: dict[str,Any], status: dict[str,Any], labels: dict[str,Any], ci_by_item: dict[str,dict[str,Any]], budgets_by_item: dict[str,dict[str,Any]]) -> dict[str, Any]:
    dirty={r.get("item_id") for r in snapshot_rows(snapshot,"dirtyItems")}; populations={}; outcomes={}
    for row in snapshot_rows(snapshot,"populations"):
        current=populations.get(row.get("item_id"))
        if current is None or (not str(row.get("source_ref","")).startswith("derived:"),-int(row.get("fact_revision",0)))<(not str(current.get("source_ref","")).startswith("derived:"),-int(current.get("fact_revision",0))): populations[row["item_id"]]=row
    for row in snapshot_rows(snapshot,"outcomes"):
        outcomes.setdefault(row.get("item_id"),row)
    groups: dict[str,list[str]]={}
    for item,row in populations.items(): groups.setdefault(row["original_item_id"],[]).append(item)
    result=[]; eligible=unmapped=dirty_count=incompatible=0
    for original,members in sorted(groups.items()):
        latest=[outcomes.get(m) for m in members]
        if any(m in dirty for m in members): dirty_count+=1; continue
        if any(populations[m].get("state")!="completed" for m in members): continue
        if any(r is None or r.get("outcome") not in {"delivered","delivered-after-readback"} or r.get("code_delivery")!="delivered" for r in latest): continue
        member_set=set(members); terminals={r.get("invocation_id") for r in snapshot_rows(snapshot,"terminals",member_set)}
        admitted={r.get("invocation_id") for r in snapshot_rows(snapshot,"admissions",member_set)}
        lineage_by_dispatch={r.get("dispatch_id"):r.get("invocation_id") for r in snapshot_rows(snapshot,"lineage",member_set)}
        expected={r.get("dispatch_id") for r in snapshot_rows(snapshot,"expectedDispatches",member_set)}
        if not admitted.issubset(terminals) or any(lineage_by_dispatch.get(dispatch) not in terminals for dispatch in expected): incompatible+=1; continue
        eligible+=1; approval=labels["items"].get(original)
        if approval is None: unmapped+=1; continue
        try: result.append(project_one_item(snapshot,original,members,latest,approval,labels,ci_by_item,budgets_by_item,status.get("epoch")))
        except (ValueError,KeyError,TypeError): incompatible+=1
    return {"schema":ITEMS_SCHEMA,"coverage":{"eligible":eligible,"published":len(result),"unmapped":unmapped,"dirty":dirty_count,"incompatible":incompatible},"items":result}


def project_one_item(snapshot: dict[str,Any], original: str, members: list[str], outcomes: list[dict[str,Any]], approval: dict[str,Any], labels: dict[str,Any], ci_by_item: dict[str,dict[str,Any]], budgets_by_item: dict[str,dict[str,Any]], epoch: Any) -> dict[str,Any]:
    member_set=set(members); terminals=snapshot_rows(snapshot,"terminals",member_set)
    if len(terminals)>4096: raise ValueError("item projection exceeds runtime bound")
    complete_invocations={r["invocation_id"] for r in terminals}
    lineage={}
    lineage_rows=snapshot_rows(snapshot,"lineage",member_set)
    if len(lineage_rows)>4096: raise ValueError("item projection exceeds lineage bound")
    for row in lineage_rows:
        if row["invocation_id"] in lineage and lineage[row["invocation_id"]]!=row["relation"]: raise ValueError("ambiguous lineage")
        lineage.setdefault(row["invocation_id"],row["relation"])
    times=snapshot_rows(snapshot,"times",member_set)
    if len(times)>8192: raise ValueError("item projection exceeds timing bound")
    events={}
    for r in times: events.setdefault((r["invocation_id"],r["event"]),r)
    durations=[]
    for invocation in complete_invocations:
        first,last=events.get((invocation,"start")),events.get((invocation,"terminal"))
        if first and last and first["occurred_clock_provenance"] in {"host-wall","provider-native","github-native"} and first["occurred_clock_provenance"]==last["occurred_clock_provenance"]:
            a,b=parse_time(first["occurred_at"]),parse_time(last["occurred_at"])
            if a and b and b>=a: durations.append((lineage.get(invocation,"unknown"),int((b-a).total_seconds())))
    usage=[r for r in snapshot_rows(snapshot,"usage",member_set) if r.get("invocation_id") in complete_invocations]
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
    expected_rows=snapshot_rows(snapshot,"expectedDispatches",member_set)
    admissions=snapshot_rows(snapshot,"admissions",member_set)
    starts=snapshot_rows(snapshot,"starts",member_set)
    gaps=snapshot_rows(snapshot,"runtimeGaps",member_set)
    expected_by_dispatch={}
    for row in expected_rows:
        dispatch=row.get("dispatch_id")
        if dispatch in expected_by_dispatch: raise ValueError("ambiguous expected dispatch")
        expected_by_dispatch[dispatch]=row
    runtime_lineage=lineage_rows
    lineage_by_dispatch={}
    for row in runtime_lineage: lineage_by_dispatch.setdefault(row.get("dispatch_id"),[]).append(row)
    expected_invocations={rows[0].get("invocation_id") for dispatch,rows in lineage_by_dispatch.items() if dispatch in expected_by_dispatch and len(rows)==1}
    all_admitted_invocations={row.get("invocation_id") for row in admissions}
    all_started_invocations={row.get("invocation_id") for row in starts}
    all_terminal_invocations={row.get("invocation_id") for row in terminals}
    all_usage_invocations={row.get("invocation_id") for row in usage}
    admitted_invocations=expected_invocations & all_admitted_invocations
    started_invocations=expected_invocations & all_started_invocations
    terminal_invocations=expected_invocations & all_terminal_invocations
    usage_invocations=expected_invocations & all_usage_invocations
    lineage_exact=(bool(expected_by_dispatch) and set(lineage_by_dispatch)==set(expected_by_dispatch)
        and len(expected_invocations)==len(expected_by_dispatch)
        and all(len(rows)==1
            and rows[0].get("relation")==expected_by_dispatch[dispatch].get("relation")
            and rows[0].get("runtime")==expected_by_dispatch[dispatch].get("runtime")
            for dispatch,rows in lineage_by_dispatch.items()))
    root_invocations={row.get("invocation_id") for row in runtime_lineage if row.get("relation")=="root"}
    ancestry_exact=(len(root_invocations)==1 and all(
        row.get("root_invocation_id") in root_invocations
        and (row.get("relation")=="root" or row.get("parent_invocation_id") in expected_invocations)
        for row in runtime_lineage))
    population_complete=(lineage_exact and ancestry_exact
        and expected_invocations==all_admitted_invocations==all_started_invocations==all_terminal_invocations)
    usage_complete=population_complete and all_usage_invocations==expected_invocations and not gaps
    coverage_status="complete" if usage_complete else "partial" if usage_invocations else "unknown"
    compatible={}
    for row in usage:
        raw=(row.get("provider"),row.get("accounting_scope"))
        bucket=compatible.setdefault(raw,{"scope":labels["scopes"].get(f"{row.get('provider')}|{row.get('accounting_scope')}","unknown"),"turns":0,"invocations":set(),"input":0,"cachedInput":0,"output":0,"reasoning":0,"reasoningKnown":True,"total":0})
        bucket["turns"]+=1; bucket["invocations"].add(row.get("invocation_id"))
        for source,target in (("input_count","input"),("cached_input","cachedInput"),("output_count","output"),("total","total")): bucket[target]+=checked_int(row.get(source),source)
        if row.get("reasoning") is None: bucket["reasoningKnown"]=False
        else: bucket["reasoning"]+=checked_int(row.get("reasoning"),"reasoning")
    compatible_totals=[]
    for bucket in compatible.values():
        bucket["invocations"]=len(bucket["invocations"])
        if not bucket.pop("reasoningKnown"): bucket["reasoning"]=None
        compatible_totals.append(bucket)
    compatible_totals.sort(key=lambda row:(row["scope"],row["total"],row["turns"]))
    complete_total=usage_complete and len(compatible_totals)==1
    total_source=compatible_totals[0] if complete_total else None
    token_total={"status":"complete" if complete_total else "not-proven","input":total_source["input"] if total_source else None,"cachedInput":total_source["cachedInput"] if total_source else None,"output":total_source["output"] if total_source else None,"reasoning":total_source["reasoning"] if total_source else None,"total":total_source["total"] if total_source else None,"unknownRemainder":not usage_complete,"semantics":"complete only when the exact expected runtime invocation population is linked, admitted, started, terminal, gap-free, usage-covered, and has one compatible accounting basis"}
    repos=set(approval["repositories"]); deliveries=[]
    outcome_rows=snapshot_rows(snapshot,"outcomes",member_set)
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
    ci_runs=snapshot_rows(snapshot,"ciRuns",member_set); ci_fail=sum(1 for r in ci_runs if r.get("conclusion") in {"failure","cancelled","timed_out"})
    repeated=sum(1 for identity in {(r.get("repository"),r.get("run_id")) for r in ci_runs} if max((int(r.get("attempt",1)) for r in ci_runs if (r.get("repository"),r.get("run_id"))==identity),default=1)>1)
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
    invocations_with_usage=len(usage_invocations)
    runtime_gaps=len(gaps)
    process=project_process_detail(snapshot,member_set,labels,sum(row["total"] for row in token_rows))
    return {"key":approval["key"],"label":approval["label"],"url":approval["url"],"state":"settled","deliveredAt":delivered.isoformat().replace("+00:00","Z") if delivered else None,"deliveries":deliveries,
        "runtime":{"invocations":len(complete_invocations),"terminalOutcomes":terminal_counts,"duration":{"rows":duration_rows,"semantics":"same-clock non-reversed invocation spans summed by role; roles and invocations may overlap in wall time"},"tokens":{"scope":"exact native turns grouped by compatible accounting basis; input includes cached input","rows":token_rows,"compatibleTotals":compatible_totals,"total":token_total,"unmappedRows":unmapped_rows,"coverage":{"boundary":"canonical completed member items and all expected runtime dispatches","status":coverage_status,"expectedDispatches":len(expected_by_dispatch),"linkedInvocations":len(expected_invocations),"admittedInvocations":len(admitted_invocations),"startedInvocations":len(started_invocations),"terminalInvocations":len(terminal_invocations),"invocationsWithUsage":invocations_with_usage,"invocationsWithoutUsage":max(0,len(expected_by_dispatch)-invocations_with_usage),"runtimeGaps":runtime_gaps,"accountingCompatibility":"single" if len(compatible_totals)==1 else "none" if not compatible_totals else "multiple"}}},
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
    result={"schema":HOST_SCHEMA,"observedAt":observed,"source":{"kind":"configured-local-store","publicExportSchema":"fsgg.telemetry.public-export/1"},
        "scope":{"items":len(public["items"]),"identities":"aggregated-or-explicitly-aliased","freeText":"removed-except-approved-notes"},"totals":totals,"usage":usage,"launcherPopulation":launcher,
        "quality":quality,"operational":operational,"store":{"status":enum((store_status or {}).get("status"),{"ready"},"store status"),"schemaVersion":checked_int((store_status or {}).get("schemaVersion"),"schemaVersion"),"journalMode":enum((store_status or {}).get("journalMode"),{"wal"},"journalMode"),"pendingBatches":checked_int((store_status or {}).get("pendingBatches"),"pendingBatches")},
        "localCi":{"counts":ci_totals,"seconds":seconds,"coverage":ci_coverage,"attribution":"repository-owned item attribution only; time values are summed per-item projections"},
        "budget":{"scope":"current-canonical-epoch","dimensions":dims,"assessments":assessments,"health":budget_health_counts,"severeItems":severe_items,"distinctBreaches":checked_int(status.get("distinctBreaches"),"distinctBreaches"),"dirtyItems":checked_int(status.get("dirtyItems")," in dirtyItems"),"intervention":enum(status.get("intervention"), {"none","open","verified"}, "intervention")},
        "completedItems":completed_items or {"schema":ITEMS_SCHEMA,"coverage":{"eligible":0,"published":0,"unmapped":0,"dirty":0,"incompatible":0},"items":[]}}
    result["revision"]=hashlib.sha256(json.dumps(result,sort_keys=True,separators=(",",":"),ensure_ascii=True).encode()).hexdigest()
    return result


def checked_int(value: Any, name: str) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or value < 0 or value > 2**63-1: raise ValueError(f"invalid {name}")
    return value


def enum(value: Any, values: set[str], name: str) -> str:
    if value not in values: raise ValueError(f"invalid {name}")
    return value


def build_host(labels_path: pathlib.Path | None = None, config_path: pathlib.Path | None = None, engine_path: str | None = None) -> dict[str, Any]:
    _, cfg = config(config_path); store, engine = cfg["storeRoot"], engine_path or cfg["engine"]
    envelope=engine_json(engine,["telemetry","item-detail","--format-version","2","--all","--store-root",store])
    if not isinstance(envelope,dict) or set(envelope)!={"schema","observedAt","revision","canonicalSnapshotGzip","operational"} or envelope.get("schema")!="fsgg.telemetry.item-detail/2" or not re.fullmatch(r"[0-9a-f]{64}",str(envelope.get("revision"))): raise HostSourceError("HOST_ENGINE_SNAPSHOT_INCOMPATIBLE")
    compressed=bounded_base64(envelope["canonicalSnapshotGzip"],MAX_JSON,"HOST_ENGINE_SNAPSHOT_REVISION_MISMATCH")
    try:
        with gzip.GzipFile(fileobj=io.BytesIO(compressed)) as stream: selected=stream.read(MAX_CANONICAL_SNAPSHOT+1)
    except (OSError,EOFError) as error: raise HostSourceError("HOST_ENGINE_SNAPSHOT_REVISION_MISMATCH") from error
    if len(selected)>MAX_CANONICAL_SNAPSHOT: raise HostSourceError("HOST_ENGINE_SNAPSHOT_REVISION_MISMATCH")
    try: snapshot=json.loads(selected)
    except (UnicodeDecodeError,json.JSONDecodeError) as error: raise HostSourceError("HOST_ENGINE_SNAPSHOT_REVISION_MISMATCH") from error
    if hashlib.sha256(selected).hexdigest()!=envelope["revision"]: raise HostSourceError("HOST_ENGINE_SNAPSHOT_REVISION_MISMATCH")
    if not isinstance(snapshot,dict) or not isinstance(snapshot.get("selection"),dict) or snapshot["selection"].get("mode")!="all" or snapshot["selection"].get("complete") is not True: raise HostSourceError("HOST_ENGINE_SNAPSHOT_INCOMPLETE")
    store_projection=snapshot.get("store")
    if not isinstance(store_projection,dict) or store_projection.get("schemaVersion") not in (8,9) or store_projection.get("journalMode")!="wal": raise HostSourceError("HOST_STORE_INCOMPATIBLE")
    public={"schema":"fsgg.telemetry.public-export/1","items":snapshot.get("summaries")}
    if not isinstance(public["items"],list): raise HostSourceError("HOST_ENGINE_SNAPSHOT_MALFORMED")
    ids=[item.get("item") for item in public["items"] if isinstance(item,dict) and isinstance(item.get("item"),str)]
    ci=[snapshot_ci(snapshot,item) for item in ids]; budgets=[snapshot_budget(snapshot,item) for item in ids]
    ci_by_item=dict(zip(ids,ci)); budgets_by_item=dict(zip(ids,budgets))
    epochs=snapshot_rows(snapshot,"budgetEpochs"); current=next((row.get("epoch_id") for row in epochs if row.get("state")=="open"),None)
    interventions=snapshot_rows(snapshot,"budgetInterventions")
    intervention=next((row.get("state") for row in interventions if row.get("epoch_id")==current),"none")
    status={"epoch":current,"distinctBreaches":len({row.get("item_id") for row in snapshot_rows(snapshot,"budgetBreaches") if row.get("epoch_id")==current}),"dirtyItems":len(snapshot_rows(snapshot,"dirtyItems")),"intervention":intervention}
    health=[]
    for item in ids:
        population=next((r.get("state") for r in snapshot_rows(snapshot,"populations",{item})),"missing")
        dirty=any(r.get("item_id")==item for r in snapshot_rows(snapshot,"dirtyItems"))
        delivered=bool(snapshot_rows(snapshot,"outcomes",{item}))
        health.append({"status":"pending" if dirty else "missing-outcome" if not delivered else "complete" if population=="completed" else "open"})
    operational=envelope.get("operational")
    if not isinstance(operational,dict) or operational.get("consistency")!="observed-outside-database-transaction": raise HostSourceError("HOST_ENGINE_SNAPSHOT_MALFORMED")
    store_status={"status":"ready","schemaVersion":store_projection["schemaVersion"],"journalMode":"wal","pendingBatches":checked_int(operational.get("pendingBatches"),"pending batches")}
    labels=load_labels(labels_path)
    completed=project_completed_items(snapshot,status,labels,ci_by_item,budgets_by_item)
    return aggregate_host(public,ci,budgets,status,envelope["observedAt"],[],health,store_status,completed)


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


def verify_publication(repo: str, branch: str, path: str, token: str, commit: str, snapshot: dict[str,Any]) -> dict[str,Any]:
    if not re.fullmatch(r"[0-9a-f]{40}",commit): raise ValueError("invalid publication commit")
    value,_=github(f"repos/{repo}/contents/{urllib.parse.quote(path,safe='/')}?ref={commit}",token)
    if not isinstance(value,dict) or value.get("type") not in {None,"file"} or value.get("encoding")!="base64" or not isinstance(value.get("content"),str): raise RuntimeError("published file is unavailable")
    try: actual=bounded_base64(value["content"],MAX_JSON,"PUBLISHED_FILE_ENCODING_INVALID")
    except HostSourceError as error: raise RuntimeError("published file encoding is invalid") from error
    immutable=actual==dump(snapshot)
    try: decoded=json.loads(actual)
    except json.JSONDecodeError: decoded=None
    revision=immutable and isinstance(decoded,dict) and decoded.get("revision")==snapshot.get("revision")
    ref,_=github(f"repos/{repo}/git/ref/heads/{urllib.parse.quote(branch,safe='')}",token)
    current=isinstance(ref,dict) and isinstance(ref.get("object"),dict) and ref["object"].get("sha")==commit
    return {"commit":commit,"immutableBytes":immutable,"payloadRevision":revision,"branchCurrent":current,"verified":immutable and revision and current}


def _systemd_quote(value: str) -> str:
    if "\n" in value or "\r" in value or "\0" in value: raise ValueError("unsafe systemd argument")
    return '"'+value.replace("\\","\\\\").replace('"','\\"').replace("%","%%")+'"'


def publisher_units(engine: str, config_path: pathlib.Path, labels: pathlib.Path, repo: str, branch: str, path: str, output: pathlib.Path) -> dict[str,bytes]:
    python=pathlib.Path(sys.executable).resolve(); script=pathlib.Path(__file__).resolve()
    resolved_engine=str(pathlib.Path(engine).resolve(strict=True))
    command=[str(python),str(script),"host-snapshot","--config",str(config_path),"--producer-executable",resolved_engine,"--labels",str(labels),"--credential-source","environment-or-gh-auth","--repo",repo,"--branch",branch,"--path",path,"--output",str(output)]
    service=("# fsgg-telemetry-dashboard-owned/v1\n[Unit]\nDescription=Publish the FS-GG safe telemetry dashboard snapshot\n\n[Service]\nType=oneshot\nExecStart="+" ".join(_systemd_quote(v) for v in command)+"\n").encode()
    timer=b"# fsgg-telemetry-dashboard-owned/v1\n[Unit]\nDescription=Refresh the FS-GG safe telemetry dashboard snapshot\n\n[Timer]\nOnBootSec=3m\nOnUnitActiveSec=15m\nPersistent=true\nUnit=fsgg-telemetry-dashboard.service\n\n[Install]\nWantedBy=timers.target\n"
    return {"fsgg-telemetry-dashboard.service":service,"fsgg-telemetry-dashboard.timer":timer}


def publication_token(source: str = "environment-or-gh-auth") -> str:
    if source!="environment-or-gh-auth": raise HostSourceError("PUBLISHER_CREDENTIAL_SOURCE_UNAVAILABLE")
    token=os.environ.get("GITHUB_TOKEN","")
    if token: return token
    gh=shutil.which("gh")
    if gh is None: raise HostSourceError("PUBLISHER_TOKEN_UNAVAILABLE")
    result=subprocess.run([gh,"auth","token"],capture_output=True,text=True,check=False)
    token=result.stdout.strip()
    if result.returncode!=0 or not token: raise HostSourceError("PUBLISHER_TOKEN_UNAVAILABLE")
    return token


def install_units(directory: pathlib.Path, units: dict[str,bytes]) -> str:
    directory.mkdir(parents=True,exist_ok=True)
    pending=[]
    for name,data in units.items():
        target=directory/name
        if target.is_symlink() or (target.exists() and not target.is_file()): raise HostSourceError("PUBLISHER_UNIT_CONFLICT")
        if target.exists():
            current=target.read_bytes()
            if current==data: continue
            if not current.startswith(b"# fsgg-telemetry-dashboard-owned/v1\n"): raise HostSourceError("PUBLISHER_UNIT_CONFLICT")
            active=subprocess.run(["systemctl","--user","is-active",name],capture_output=True,check=False).returncode==0
            if active: raise HostSourceError("PUBLISHER_UNIT_ACTIVE_CONFLICT")
        pending.append((target,data))
    for target,data in pending:
        name=target.name
        fd,temporary=tempfile.mkstemp(prefix=name+".",dir=directory)
        try:
            with os.fdopen(fd,"wb") as stream: stream.write(data); stream.flush(); os.fsync(stream.fileno())
            os.replace(temporary,target)
        finally:
            if os.path.exists(temporary): os.unlink(temporary)
    return "installed" if pending else "unchanged"


def publisher_setup(args: argparse.Namespace) -> dict[str,Any]:
    config_path,cfg=config(args.config)
    config_bytes=read_private_bytes(config_path,8192,"HOST_CONFIG_UNSAFE")
    engine=shutil.which(cfg["engine"])
    if engine is None or not pathlib.Path(engine).is_absolute(): raise HostSourceError("HOST_ENGINE_UNAVAILABLE")
    if args.labels is None: raise HostSourceError("HOST_LABELS_REQUIRED")
    labels=load_labels(args.labels)
    labels_path=args.labels.resolve(strict=True)
    label_bytes=read_private_bytes(labels_path,65536,"HOST_LABELS_UNSAFE")
    if not re.fullmatch(r"FS-GG/[A-Za-z0-9_.-]+",args.repo) or not re.fullmatch(r"[A-Za-z0-9._/-]{1,200}",args.branch) or args.branch.startswith("/") or ".." in args.branch.split("/"): raise ValueError("invalid publication destination")
    target=pathlib.PurePosixPath(args.path)
    if target.is_absolute() or ".." in target.parts or str(target) in {"","."}: raise ValueError("invalid publication path")
    resolved_engine=str(pathlib.Path(engine).resolve(strict=True))
    snapshot=build_host(labels_path,config_path,resolved_engine)
    if read_private_bytes(labels_path,65536,"HOST_LABELS_CHANGED_DURING_PREVIEW")!=label_bytes: raise HostSourceError("HOST_LABELS_CHANGED_DURING_PREVIEW")
    label_digest=hashlib.sha256(label_bytes).hexdigest()
    config_digest=hashlib.sha256(config_bytes).hexdigest()
    receipt={"schema":EVENT_RECEIPT_SCHEMA,"configDigest":config_digest,"engine":resolved_engine,
        "labelsPath":str(labels_path),"labelsDigest":label_digest,
        "destination":{"repository":args.repo,"branch":args.branch,"path":args.path},
        "credentialSource":"environment-or-gh-auth"}
    receipt_path=config_path.parent/EVENT_RECEIPT_NAME
    if getattr(args,"authorize_event_publication",False): validate_private_parent(receipt_path)
    if getattr(args,"authorize_event_publication",False) and (receipt_path.exists() or receipt_path.is_symlink()):
        if load_event_receipt(receipt_path)!=receipt: raise HostSourceError("EVENT_ACTIVATION_CHANGE_REFUSED")
    if read_private_bytes(config_path,8192,"HOST_CONFIG_CHANGED_DURING_PREVIEW")!=config_bytes: raise HostSourceError("HOST_CONFIG_CHANGED_DURING_PREVIEW")
    units=publisher_units(engine,config_path,labels_path,args.repo,args.branch,args.path,args.output)
    report={"schema":"fsgg.telemetry.publisher-setup/1","mode":"preview","engine":resolved_engine,"config":str(config_path),"labelsDigest":label_digest,"counts":{"approved":len(labels["items"]),"eligible":snapshot["completedItems"]["coverage"]["eligible"],"published":snapshot["completedItems"]["coverage"]["published"]},"revision":snapshot["revision"],"destination":{"repository":args.repo,"branch":args.branch,"path":args.path},"actions":["validate-config","resolve-engine","validate-labels","build-preview"],"effects":[]}
    if args.activate and (args.approve_labels!=label_digest or not args.authorize_recurring_publication):
        raise HostSourceError("PUBLISHER_ACTIVATION_NOT_AUTHORIZED")
    if args.install_only or args.activate:
        report["mode"]="activate" if args.activate else "install-only"
        report["installation"]=install_units(args.systemd_dir,units); report["effects"].append("write-inert-user-units")
    if args.activate:
        token=publication_token()
        commit=publish(args.repo,args.branch,args.path,token,snapshot); report["effects"].append("publish-once")
        try: verification=verify_publication(args.repo,args.branch,args.path,token,commit,snapshot)
        except (RuntimeError,ValueError,RefConflict): verification={"commit":commit,"immutableBytes":False,"payloadRevision":False,"branchCurrent":False,"verified":False}
        report["publication"]=verification
        if not verification["verified"]:
            report["status"]="publication-verification-failed"; report["recurrence"]="inactive"
            return report
        if getattr(args,"authorize_event_publication",False):
            atomic_private(receipt_path,receipt); report["effects"].append("authorize-event-publication")
            report["eventPublication"]="active"
        try:
            subprocess.run(["systemctl","--user","daemon-reload"],check=True)
            subprocess.run(["systemctl","--user","enable","--now","fsgg-telemetry-dashboard.timer"],check=True)
        except (OSError,subprocess.CalledProcessError):
            report["recurrence"]="unavailable"
        else:
            report["effects"].append("enable-recurrence")
            report["recurrence"]="active"
    return report


def load_event_receipt(path: pathlib.Path) -> dict[str,Any]:
    try: value=json.loads(read_private_bytes(path,8192,"EVENT_ACTIVATION_RECEIPT_UNSAFE"))
    except (OSError,UnicodeError,json.JSONDecodeError) as error: raise HostSourceError("EVENT_ACTIVATION_RECEIPT_INVALID") from error
    exact(value,{"schema","configDigest","engine","labelsPath","labelsDigest","destination","credentialSource"},"event activation receipt")
    if value["schema"]!=EVENT_RECEIPT_SCHEMA or not re.fullmatch(r"[0-9a-f]{64}",str(value["configDigest"])) or not re.fullmatch(r"[0-9a-f]{64}",str(value["labelsDigest"])):
        raise HostSourceError("EVENT_ACTIVATION_RECEIPT_INVALID")
    if not isinstance(value["engine"],str) or not pathlib.Path(value["engine"]).is_absolute() or not isinstance(value["labelsPath"],str) or not pathlib.Path(value["labelsPath"]).is_absolute():
        raise HostSourceError("EVENT_ACTIVATION_RECEIPT_INVALID")
    destination=exact(value["destination"],{"repository","branch","path"},"event activation destination")
    target=pathlib.PurePosixPath(destination["path"]) if isinstance(destination["path"],str) else pathlib.PurePosixPath(".")
    if (not isinstance(destination["repository"],str) or not re.fullmatch(r"FS-GG/[A-Za-z0-9_.-]+",destination["repository"])
        or not isinstance(destination["branch"],str) or not re.fullmatch(r"[A-Za-z0-9._/-]{1,200}",destination["branch"])
        or destination["branch"].startswith("/") or ".." in destination["branch"].split("/")
        or target.is_absolute() or ".." in target.parts or str(target) in {"","."}
        or value["credentialSource"]!="environment-or-gh-auth"):
        raise HostSourceError("EVENT_ACTIVATION_RECEIPT_INVALID")
    return value


def event_health(status: str, reason: str, revision: str | None = None, commit: str | None = None) -> dict[str,Any]:
    return {"schema":EVENT_HEALTH_SCHEMA,"status":status,"reason":reason,"observedAt":now(),"publicRevision":revision,"commit":commit}


def write_event_health(config_path: pathlib.Path, value: dict[str,Any]) -> None:
    exact(value,{"schema","status","reason","observedAt","publicRevision","commit"},"event health")
    atomic_private(config_path.parent/EVENT_HEALTH_NAME,value)


def acquire_event_lock(config_path: pathlib.Path) -> int | None:
    import fcntl
    path=config_path.parent/EVENT_LOCK_NAME
    flags=os.O_RDWR|os.O_CREAT
    if hasattr(os,"O_NOFOLLOW"): flags|=os.O_NOFOLLOW
    try: fd=os.open(path,flags,0o600)
    except OSError as error: raise HostSourceError("EVENT_LOCK_UNSAFE") from error
    try:
        info=os.fstat(fd)
        if not stat.S_ISREG(info.st_mode) or stat.S_IMODE(info.st_mode)!=0o600: raise HostSourceError("EVENT_LOCK_UNSAFE")
        for _ in range(5):
            try: fcntl.flock(fd,fcntl.LOCK_EX|fcntl.LOCK_NB); return fd
            except BlockingIOError: time.sleep(0.02)
        os.close(fd); return None
    except Exception:
        os.close(fd); raise


def current_publication(repo: str, branch: str, path: str, token: str) -> tuple[dict[str,Any] | None,str | None]:
    encoded=urllib.parse.quote(branch,safe="")
    try: ref,_=github(f"repos/{repo}/git/ref/heads/{encoded}",token)
    except RuntimeError as error:
        if "404" in str(error): return None,None
        raise
    commit=ref.get("object",{}).get("sha") if isinstance(ref,dict) else None
    if not isinstance(commit,str) or not re.fullmatch(r"[0-9a-f]{40}",commit): raise HostSourceError("EVENT_PUBLIC_PAYLOAD_INVALID")
    value,_=github(f"repos/{repo}/contents/{urllib.parse.quote(path,safe='/')}?ref={commit}",token)
    if not isinstance(value,dict) or value.get("encoding")!="base64": raise HostSourceError("EVENT_PUBLIC_PAYLOAD_INVALID")
    raw=bounded_base64(value.get("content"),MAX_JSON,"EVENT_PUBLIC_PAYLOAD_INVALID")
    try: payload=json.loads(raw)
    except (UnicodeError,json.JSONDecodeError) as error: raise HostSourceError("EVENT_PUBLIC_PAYLOAD_INVALID") from error
    if not isinstance(payload,dict) or payload.get("schema")!=HOST_SCHEMA: raise HostSourceError("EVENT_PUBLIC_PAYLOAD_INVALID")
    try: validate_host(payload)
    except ValueError as error: raise HostSourceError("EVENT_PUBLIC_PAYLOAD_INVALID") from error
    return payload,commit


def unchanged_with_preserved_observation(candidate: dict[str,Any], current: dict[str,Any]) -> bool:
    preserved=dict(candidate); preserved["observedAt"]=current["observedAt"]; preserved.pop("revision",None)
    preserved["revision"]=hashlib.sha256(json.dumps(preserved,sort_keys=True,separators=(",",":"),ensure_ascii=True).encode()).hexdigest()
    return dump(preserved)==dump(current)


def publisher_event(args: argparse.Namespace) -> dict[str,Any]:
    try: config_path,cfg=config(args.config)
    except (HostSourceError,OSError,ValueError,RuntimeError): return event_health("skipped","EVENT_CONFIG_UNAVAILABLE")
    receipt_path=config_path.parent/EVENT_RECEIPT_NAME
    if not receipt_path.exists() and not receipt_path.is_symlink(): return event_health("skipped","EVENT_ACTIVATION_RECEIPT_MISSING")
    lock=None
    try:
        validate_private_parent(receipt_path)
        receipt=load_event_receipt(receipt_path)
        lock=acquire_event_lock(config_path)
        if lock is None:
            result=event_health("skipped","EVENT_LOCK_CONTENDED"); write_event_health(config_path,result); return result
        receipt=load_event_receipt(receipt_path)
        config_bytes=read_private_bytes(config_path,8192,"EVENT_CONFIG_CHANGED")
        if hashlib.sha256(config_bytes).hexdigest()!=receipt["configDigest"]: raise HostSourceError("EVENT_CONFIG_CHANGED")
        engine=shutil.which(cfg["engine"])
        if engine is None or str(pathlib.Path(engine).resolve(strict=True))!=receipt["engine"]: raise HostSourceError("EVENT_ENGINE_CHANGED")
        labels_path=pathlib.Path(receipt["labelsPath"]); label_bytes=read_private_bytes(labels_path,65536,"EVENT_LABELS_CHANGED"); load_labels(labels_path)
        if hashlib.sha256(label_bytes).hexdigest()!=receipt["labelsDigest"]: raise HostSourceError("EVENT_LABELS_CHANGED")
        token=publication_token(receipt["credentialSource"])
        snapshot=build_host(labels_path,config_path,receipt["engine"])
        if read_private_bytes(config_path,8192,"EVENT_PRIVATE_INPUT_CHANGED")!=config_bytes or read_private_bytes(labels_path,65536,"EVENT_PRIVATE_INPUT_CHANGED")!=label_bytes: raise HostSourceError("EVENT_PRIVATE_INPUT_CHANGED")
        destination=receipt["destination"]
        current,commit=current_publication(destination["repository"],destination["branch"],destination["path"],token)
        if current is not None and unchanged_with_preserved_observation(snapshot,current):
            result=event_health("unchanged","SEMANTIC_CONTENT_UNCHANGED",current["revision"],commit)
        else:
            commit=publish(destination["repository"],destination["branch"],destination["path"],token,snapshot)
            verification=verify_publication(destination["repository"],destination["branch"],destination["path"],token,commit,snapshot)
            result=(event_health("published","PUBLICATION_VERIFIED",snapshot["revision"],commit) if verification["verified"]
                else event_health("failed","EVENT_PUBLICATION_VERIFICATION_FAILED",snapshot["revision"],commit))
    except RefConflict:
        result=event_health("failed","PUBLISH_REF_CONFLICT")
    except HostSourceError as error:
        code=str(error); result=event_health("failed",code if code in EVENT_REASON_CODES else "EVENT_REFRESH_FAILED")
    except (OSError,ValueError,RuntimeError,subprocess.SubprocessError):
        result=event_health("failed","EVENT_REFRESH_FAILED")
    try: write_event_health(config_path,result)
    except (OSError,ValueError,HostSourceError):
        return event_health("failed","EVENT_HEALTH_WRITE_FAILED",result.get("publicRevision"),result.get("commit"))
    finally:
        if lock is not None: os.close(lock)
    return result


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
    current=schema==HOST_SCHEMA
    fields={"schema","observedAt","source","scope","totals","usage","launcherPopulation","quality","operational","store","localCi","budget"}
    if not aggregate_only: fields.add("completedItems")
    if current: fields.add("revision")
    exact(value,fields,"host feed")
    if schema not in {HOST_SCHEMA,*LEGACY_HOST_SCHEMAS} or parse_time(value["observedAt"]) is None: raise ValueError("invalid host identity")
    if current:
        revision=value["revision"]
        if not isinstance(revision,str) or not re.fullmatch(r"[0-9a-f]{64}",revision): raise ValueError("invalid public payload revision")
        projected=dict(value); projected.pop("revision")
        if hashlib.sha256(json.dumps(projected,sort_keys=True,separators=(",",":"),ensure_ascii=True).encode()).hexdigest()!=revision: raise ValueError("public payload revision mismatch")
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
        token_fields={"scope","rows","unmappedRows","coverage"} if legacy else {"scope","rows","compatibleTotals","total","unmappedRows","coverage"}
        tokens=exact(runtime["tokens"],token_fields,"tokens"); checked_int(tokens["unmappedRows"],"unmapped rows")
        if legacy:
            validate_count_map(tokens["coverage"],{"invocationsWithUsage","invocationsWithoutUsage","runtimeGaps"},"token coverage")
            valid_scope=tokens["scope"]=="completed native turns; input includes cached input"
        else:
            coverage=exact(tokens["coverage"],{"boundary","status","expectedDispatches","linkedInvocations","admittedInvocations","startedInvocations","terminalInvocations","invocationsWithUsage","invocationsWithoutUsage","runtimeGaps","accountingCompatibility"},"token coverage")
            if coverage["boundary"] not in {"canonical completed member items and their codex-exec expected dispatches","canonical completed member items and all expected runtime dispatches"}: raise ValueError("invalid token boundary")
            enum(coverage["status"],{"complete","partial","unknown","incomplete"},"token coverage status"); enum(coverage["accountingCompatibility"],{"none","single","multiple"},"accounting compatibility")
            for name in ("expectedDispatches","linkedInvocations","admittedInvocations","startedInvocations","terminalInvocations","invocationsWithUsage","invocationsWithoutUsage","runtimeGaps"): checked_int(coverage[name],name)
            current_boundary=coverage["boundary"]=="canonical completed member items and all expected runtime dispatches"
            if current_boundary:
                expected=coverage["expectedDispatches"]
                if any(coverage[name]>expected for name in ("linkedInvocations","admittedInvocations","startedInvocations","terminalInvocations","invocationsWithUsage","invocationsWithoutUsage")): raise ValueError("invalid token population counts")
                if coverage["invocationsWithoutUsage"]!=expected-coverage["invocationsWithUsage"]: raise ValueError("invalid token usage remainder")
                if coverage["status"]=="complete" and (any(coverage[name]!=expected for name in ("linkedInvocations","admittedInvocations","startedInvocations","terminalInvocations","invocationsWithUsage")) or coverage["invocationsWithoutUsage"] or coverage["runtimeGaps"]): raise ValueError("invalid complete token coverage")
                if coverage["status"]=="partial" and not coverage["invocationsWithUsage"]: raise ValueError("invalid partial token coverage")
                if coverage["status"]=="unknown" and coverage["invocationsWithUsage"]: raise ValueError("invalid unknown token coverage")
            if not isinstance(tokens["compatibleTotals"],list) or len(tokens["compatibleTotals"])>512: raise ValueError("invalid compatible totals")
            for aggregate in tokens["compatibleTotals"]:
                exact(aggregate,{"scope","turns","invocations","input","cachedInput","output","reasoning","total"},"compatible total"); public_text(aggregate["scope"],48,"scope")
                for name in ("turns","invocations","input","cachedInput","output","total"): checked_int(aggregate[name],name)
                if aggregate["reasoning"] is not None: checked_int(aggregate["reasoning"],"reasoning")
            total=exact(tokens["total"],{"status","input","cachedInput","output","reasoning","total","unknownRemainder","semantics"},"token total")
            enum(total["status"],{"complete","not-proven"},"token total status")
            if not isinstance(total["unknownRemainder"],bool) or total["semantics"] not in {"complete only when the exact expected native invocation population is linked, admitted, started, terminal, gap-free, usage-covered, and has one compatible accounting basis","complete only when the exact expected runtime invocation population is linked, admitted, started, terminal, gap-free, usage-covered, and has one compatible accounting basis"}: raise ValueError("invalid token total")
            for name in ("input","cachedInput","output","reasoning","total"):
                if total[name] is not None: checked_int(total[name],name)
            if total["status"]=="complete" and (total["total"] is None or coverage["status"]!="complete" or coverage["accountingCompatibility"]!="single" or total["unknownRemainder"]): raise ValueError("unproven complete token total")
            if total["status"]!="complete" and any(total[name] is not None for name in ("input","cachedInput","output","reasoning","total")): raise ValueError("partial token total must remain unknown")
            if current_boundary and total["unknownRemainder"]!=(coverage["status"]!="complete"): raise ValueError("invalid token remainder status")
            valid_scope=tokens["scope"]=="exact native turns grouped by compatible accounting basis; input includes cached input"
        if not valid_scope or not isinstance(tokens["rows"],list) or len(tokens["rows"])>512: raise ValueError("invalid token rows")
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
        semantics="observed events and approved notes; no inferred cause or repair cost; development phases and repair attribution unavailable" if legacy else "observed runtime and CI signals plus separately approved public notes; no inferred cause or repair cost"
        if complications["semantics"]!=semantics or not isinstance(complications["notes"],list) or len(complications["notes"])>8: raise ValueError("invalid complications")
        for note in complications["notes"]:
            exact(note,{"kind","text","evidenceUrl"},"note"); enum(note["kind"],{"repair","complication"},"note kind"); public_text(note["text"],240,"note")
            if not re.fullmatch(r"https://github\.com/FS-GG/[A-Za-z0-9_.-]+/(?:issues|pull|actions/runs)/[1-9][0-9]*",note["evidenceUrl"]): raise ValueError("invalid note evidence")
        if not legacy: validate_process_detail(item["process"])


def validate_process_detail(value: Any) -> None:
    exact(value,{"schema","availability","members","truncated","activities","attribution","complications","reviews","observation"},"process detail")
    if value["schema"]!=PROCESS_SCHEMA or value["availability"] not in {"available","unsupported"}: raise ValueError("invalid process detail")
    members=exact(value["members"],{"requested","available"},"process members")
    for count in members.values(): checked_int(count,"process members")
    if members["available"]>members["requested"]: raise ValueError("invalid process coverage")
    truncated=exact(value["truncated"],{"activities","attributions","complications","reviews"},"process truncation")
    if any(not isinstance(flag,bool) for flag in truncated.values()) or value["observation"]!="all process and item projections share one engine-owned database snapshot": raise ValueError("invalid process observation")
    activities=exact(value["activities"],{"rows","summary","semantics"},"activities")
    if len(activities["rows"])>512 or len(activities["summary"])>10: raise ValueError("invalid activities")
    for row in activities["rows"]:
        exact(row,{"category","startedAt","endedAt","durationSeconds"},"activity row"); enum(row["category"],ACTIVITY_CATEGORIES,"activity category")
        if parse_time(row["startedAt"]) is None or (row["endedAt"] is not None and parse_time(row["endedAt"]) is None): raise ValueError("invalid activity time")
    attribution=exact(value["attribution"],{"rows","accounting","crossRead","semantics"},"attribution")
    enum(attribution["crossRead"],{"matched","partial","unavailable"},"cross-read")
    for row in attribution["rows"]:
        exact(row,{"classification","activityCategory","records","input","cachedInput","output","reasoning","total"},"attribution row"); enum(row["classification"],ATTRIBUTION_CLASSES,"classification")
    accounting=exact(attribution["accounting"],{"nativeTotal","direct","mixed","unclassified","missingAttribution"},"accounting")
    for count in accounting.values(): checked_int(count,"accounting")
    complications=exact(value["complications"],{"rows"},"complications")
    for row in complications["rows"]: exact(row,{"trigger","cause","occurredAt","activityCategory"},"complication row")
    reviews=exact(value["reviews"],{"rows","semantics"},"reviews")
    for row in reviews["rows"]: exact(row,{"scope","revision","evidenceCoverage","populationCoverage","confidence","reviewerModel","reviewerEffort","reviewedAt","durationSeconds","counts"},"review row")


def main() -> int:
    parser=argparse.ArgumentParser(); subs=parser.add_subparsers(dest="cmd",required=True)
    actions=subs.add_parser("collect-actions"); actions.add_argument("--repo",default="FS-GG/.github"); actions.add_argument("--cap",type=int,default=1000); actions.add_argument("--output",type=pathlib.Path,required=True)
    deliveries=subs.add_parser("collect-deliveries"); deliveries.add_argument("--repo",default="FS-GG/.github"); deliveries.add_argument("--cap",type=int,default=200); deliveries.add_argument("--output",type=pathlib.Path,required=True)
    host=subs.add_parser("host-snapshot"); host.add_argument("--output",type=pathlib.Path,required=True); host.add_argument("--config",type=pathlib.Path); host.add_argument("--producer-executable"); host.add_argument("--labels",type=pathlib.Path); host.add_argument("--credential-source",choices=["environment-or-gh-auth"],default="environment-or-gh-auth"); host.add_argument("--dry-run",action="store_true"); host.add_argument("--repo"); host.add_argument("--branch",default="telemetry-data"); host.add_argument("--path",default="host.json")
    setup=subs.add_parser("publisher-setup"); setup.add_argument("--config",type=pathlib.Path); setup.add_argument("--labels",type=pathlib.Path,required=True); setup.add_argument("--repo",default="FS-GG/.github"); setup.add_argument("--branch",default="telemetry-data"); setup.add_argument("--path",default="host.json"); setup.add_argument("--output",type=pathlib.Path,default=pathlib.Path(os.environ.get("XDG_RUNTIME_DIR","/tmp"))/"fsgg-telemetry-dashboard-host.json"); setup.add_argument("--systemd-dir",type=pathlib.Path,default=pathlib.Path.home()/".config/systemd/user"); setup.add_argument("--install-only",action="store_true"); setup.add_argument("--activate",action="store_true"); setup.add_argument("--approve-labels"); setup.add_argument("--authorize-recurring-publication",action="store_true"); setup.add_argument("--authorize-event-publication",action="store_true")
    event=subs.add_parser("publisher-event"); event.add_argument("--config",type=pathlib.Path)
    comp=subs.add_parser("compose"); comp.add_argument("--actions",type=pathlib.Path,required=True); comp.add_argument("--deliveries",type=pathlib.Path,required=True); comp.add_argument("--host",type=pathlib.Path); comp.add_argument("--host-revision"); comp.add_argument("--source-revision",required=True); comp.add_argument("--output",type=pathlib.Path,required=True)
    args=parser.parse_args()
    if args.cmd=="collect-actions":
        token=publication_token()
        atomic(args.output,collect_actions(args.repo,token,args.cap)); return 0
    if args.cmd=="collect-deliveries":
        token=publication_token()
        atomic(args.output,collect_deliveries(args.repo,token,args.cap)); return 0
    if args.cmd=="host-snapshot":
        labels=args.labels or (pathlib.Path(os.environ["FSGG_TELEMETRY_DASHBOARD_LABELS"]) if os.environ.get("FSGG_TELEMETRY_DASHBOARD_LABELS") else None)
        snap=build_host(labels,args.config,args.producer_executable); atomic(args.output,snap)
        if not args.dry_run:
            if not args.repo: raise ValueError("--repo is required to publish")
            token=publication_token(args.credential_source)
            print(publish(args.repo,args.branch,args.path,token,snap))
        return 0
    if args.cmd=="publisher-setup":
        if args.install_only and args.activate: raise ValueError("--install-only and --activate are mutually exclusive")
        print(json.dumps(publisher_setup(args),sort_keys=True,separators=(",",":")))
        return 0
    if args.cmd=="publisher-event":
        print(json.dumps(publisher_event(args),sort_keys=True,separators=(",",":")))
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
