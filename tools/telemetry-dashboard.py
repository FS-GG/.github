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
import stat
import subprocess
import sys
import tempfile
import urllib.error
import urllib.parse
import urllib.request
from typing import Any

MAX_JSON = 1_048_576
MAX_API_JSON = 4 * 1_048_576
HOST_SCHEMA = "fsgg.telemetry.dashboard-host/1"
DASH_SCHEMA = "fsgg.telemetry.dashboard/1"
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
    try: return dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
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
    try: return json.loads(done.stdout)
    except json.JSONDecodeError as error: raise HostSourceError("HOST_ENGINE_INVALID_JSON") from error


def aggregate_host(public: dict[str, Any], ci: list[dict[str, Any]], budgets: list[dict[str, Any]], status: dict[str, Any], observed: str, reconciliations: list[dict[str,Any]] | None = None, budget_health: list[dict[str,Any]] | None = None, store_status: dict[str,Any] | None = None) -> dict[str, Any]:
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
    ci_totals = {k:0 for k in ("runs","attempts","jobs","steps")}; seconds = {k:0 for k in ("runnerSeconds","wallSeconds","queueSeconds","usefulValidationSeconds","administrativeSeconds","necessarySetupSeconds","mixedSeconds","unclassifiedSeconds")}; sec_known = {k:True for k in seconds}
    ci_coverage={k:{} for k in ("inventoryCoverage","checkCoverage","attemptCoverage","jobPageCoverage","terminalCoverage","timestampCoverage","lineageCoverage","classificationCoverage","criticalPathCoverage")}
    for item in ci:
        for key in ci_totals: ci_totals[key] += checked_int(item.get(key), key)
        for key in seconds:
            if item.get(key) is None: sec_known[key] = False
            elif sec_known[key]: seconds[key] += checked_int(item.get(key), key)
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
        "scope":{"items":len(public["items"]),"identities":"aggregated-and-removed","freeText":"removed"},"totals":totals,"usage":usage,"launcherPopulation":launcher,
        "quality":quality,"operational":operational,"store":{"status":enum((store_status or {}).get("status"),{"ready"},"store status"),"schemaVersion":checked_int((store_status or {}).get("schemaVersion"),"schemaVersion"),"journalMode":enum((store_status or {}).get("journalMode"),{"wal"},"journalMode"),"pendingBatches":checked_int((store_status or {}).get("pendingBatches"),"pendingBatches")},
        "localCi":{"counts":ci_totals,"seconds":seconds,"coverage":ci_coverage,"attribution":"repository-owned item attribution only; time values are summed per-item projections"},
        "budget":{"scope":"current-canonical-epoch","dimensions":dims,"assessments":assessments,"health":budget_health_counts,"severeItems":severe_items,"distinctBreaches":checked_int(status.get("distinctBreaches"),"distinctBreaches"),"dirtyItems":checked_int(status.get("dirtyItems")," in dirtyItems"),"intervention":enum(status.get("intervention"), {"none","open","verified"}, "intervention")}}


def checked_int(value: Any, name: str) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or value < 0 or value > 2**63-1: raise ValueError(f"invalid {name}")
    return value


def enum(value: Any, values: set[str], name: str) -> str:
    if value not in values: raise ValueError(f"invalid {name}")
    return value


def build_host() -> dict[str, Any]:
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
    return aggregate_host(public,ci,budgets,status,now(),reconciliations,health,store_status)


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


def compose(actions: dict[str, Any], host: dict[str, Any] | None, source_revision: str, host_revision: str | None = None) -> dict[str, Any]:
    validate_actions(actions)
    if host is not None: validate_host(host)
    if host_revision is not None and (len(host_revision)!=40 or any(c not in "0123456789abcdef" for c in host_revision)): raise ValueError("invalid host revision")
    return {"schema":DASH_SCHEMA,"builtAt":now(),"sourceRevision":source_revision,"hostRevision":host_revision,"actions":actions,
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


def validate_host(value: Any) -> None:
    exact(value,{"schema","observedAt","source","scope","totals","usage","launcherPopulation","quality","operational","store","localCi","budget"},"host feed")
    if value["schema"]!=HOST_SCHEMA or parse_time(value["observedAt"]) is None: raise ValueError("invalid host identity")
    exact(value["source"],{"kind","publicExportSchema"},"host source"); exact(value["scope"],{"items","identities","freeText"},"host scope")
    if value["source"]!={"kind":"configured-local-store","publicExportSchema":"fsgg.telemetry.public-export/1"} or value["scope"]["identities"]!="aggregated-and-removed" or value["scope"]["freeText"]!="removed": raise ValueError("invalid host safety declaration")
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


def main() -> int:
    parser=argparse.ArgumentParser(); subs=parser.add_subparsers(dest="cmd",required=True)
    actions=subs.add_parser("collect-actions"); actions.add_argument("--repo",default="FS-GG/.github"); actions.add_argument("--cap",type=int,default=1000); actions.add_argument("--output",type=pathlib.Path,required=True)
    host=subs.add_parser("host-snapshot"); host.add_argument("--output",type=pathlib.Path,required=True); host.add_argument("--dry-run",action="store_true"); host.add_argument("--repo"); host.add_argument("--branch",default="telemetry-data"); host.add_argument("--path",default="host.json")
    comp=subs.add_parser("compose"); comp.add_argument("--actions",type=pathlib.Path,required=True); comp.add_argument("--host",type=pathlib.Path); comp.add_argument("--host-revision"); comp.add_argument("--source-revision",required=True); comp.add_argument("--output",type=pathlib.Path,required=True)
    args=parser.parse_args(); token=os.environ.get("GITHUB_TOKEN","")
    if args.cmd=="collect-actions":
        if not token: raise ValueError("GITHUB_TOKEN is required")
        atomic(args.output,collect_actions(args.repo,token,args.cap)); return 0
    if args.cmd=="host-snapshot":
        snap=build_host(); atomic(args.output,snap)
        if not args.dry_run:
            if not token or not args.repo: raise ValueError("GITHUB_TOKEN and --repo are required to publish")
            print(publish(args.repo,args.branch,args.path,token,snap))
        return 0
    host_value=load(args.host) if args.host and args.host.exists() else None
    atomic(args.output,compose(load(args.actions),host_value,args.source_revision,args.host_revision)); return 0


if __name__ == "__main__":
    try: raise SystemExit(main())
    except HostSourceError as error:
        print(f"telemetry-dashboard: {error}",file=sys.stderr); raise SystemExit(1)
    except RefConflict:
        print("telemetry-dashboard: PUBLISH_REF_CONFLICT",file=sys.stderr); raise SystemExit(1)
    except (ValueError,RuntimeError,json.JSONDecodeError):
        print("telemetry-dashboard: INVALID_PUBLIC_DATA",file=sys.stderr); raise SystemExit(1)
