#!/usr/bin/env python3
"""Black-box fixtures for fsgg-coord-report's ledger and projections."""
from __future__ import annotations
import json, os, re, subprocess, sys, tempfile, unicodedata
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CLIENT = ROOT / "scripts/fsgg-coord-report"

EVENT_SCHEMA = "fsgg.coord.session-event/1"
CAPACITY_SCHEMA = "fsgg.coord.lane-capacity/1"
LANE_SNAPSHOT_SCHEMA = "fsgg.coord.lane-snapshot/1"
COORDINATION = {"owner":"FS-GG","number":1,"title":"Coordination"}

def scope_for(lanes_path):
    path = Path(lanes_path).with_name("coordination-scope.json")
    path.write_text(json.dumps({"schema":"fsgg.coord.coordination-scope-receipt/1","project":COORDINATION,
        "observedAt":"2026-08-08T00:00:00Z","source":{"kind":"project-scoped-driver-receipt",
        "repository":"FS-GG/.github","receiptId":"who-repo-fsgg-github"}}))
    return str(path)

def run(*args, env=None, check=True):
    args = list(args)
    if "--lanes" in args and "--scope" not in args:
        args.extend(("--scope", scope_for(args[args.index("--lanes") + 1])))
    return subprocess.run([str(CLIENT), *args], text=True, capture_output=True, check=check, env=env)

def scoped(snapshot):
    if "schema" in snapshot:
        return snapshot
    result = {"schema":LANE_SNAPSHOT_SCHEMA,"coordinationProject":COORDINATION,**snapshot}
    for lane in result.get("lanes", []):
        lane.setdefault("coordinationProject", COORDINATION)
        lane.setdefault("board", {"status":lane.get("boardStatus", "unknown"),"observedAt":"2026-08-08T00:00:00Z"})
        observed = {"state":lane.get("workState", "unknown"),"observedAt":"2026-08-08T00:00:00Z"}
        if lane.get("worker") is not None: observed["claimHolder"] = lane["worker"]
        if lane.get("pr") is not None: observed["pr"] = lane["pr"]
        lane.setdefault("observed", observed)
    return result

def write(path, value):
    if path.name == "lanes.json": value = scoped(value)
    path.write_text(json.dumps(value)); return str(path)

def capacity(implementation, review, active, open_slots, *reasons):
    return {"schema":CAPACITY_SCHEMA,"implementationCapacity":implementation,"reviewCapacity":review,
            "activeLanes":active,"openSlots":open_slots,"reasons":list(reasons)}

def reason(kind, detail, source="driver-receipt", freshness="current"):
    return {"kind":kind,"source":source,"freshness":freshness,"detail":detail}

def display_width(text):
    total = 0
    for character in text:
        if unicodedata.combining(character) or unicodedata.category(character) in ("Mn", "Me", "Cf"):
            continue
        total += 2 if unicodedata.east_asian_width(character) in ("W", "F") else 1
    return total

strip_ansi = lambda text: re.sub(r"\x1b\[[0-9;]*m", "", text)
compact = lambda text: re.sub(r"[\s┌┬┐├┼┤└┴┘─│]+", "", strip_ansi(text))
def human_escape(text):
    return "".join("\\\\" if character == "\\" else f"\\u{ord(character):04x}" if ord(character) < 0x20 or 0x7f <= ord(character) <= 0x9f else character for character in str(text))

def human_decode(text):
    result, index = [], 0
    while index < len(text):
        if text[index] != "\\":
            result.append(text[index]); index += 1; continue
        if index + 1 < len(text) and text[index + 1] == "\\":
            result.append("\\"); index += 2; continue
        assert index + 5 < len(text) and text[index + 1] == "u"
        result.append(chr(int(text[index + 2:index + 6], 16))); index += 6
    return "".join(result)

def raw_terminal_controls(text):
    return [character for character in text if character != "\n" and (ord(character) < 0x20 or 0x7f <= ord(character) <= 0x9f)]

# The four routed variants inherit their canonical driver, rather than carrying a
# sixth subtly different reporting recipe.  Assert both runtime roots so projection
# drift cannot make the installed skill omit the reporter.
for runtime in (".claude", ".agents"):
    for canonical, variants in (("drive-board", ("drive-board-normal", "drive-board-best")), ("work-board", ("work-board-normal", "work-board-best"))):
        body = (ROOT / runtime / "skills" / canonical / "SKILL.md").read_text()
        assert "fsgg-coord-report" in body and "already-cached lane snapshot" in body
        assert "typed lane-capacity facts" in body and "indeterminate receipt" in body and "derived cache" in body
        if canonical == "drive-board":
            assert "Coordination project identity" in body and "append-only correction" in body
        for variant in variants:
            inherited = (ROOT / runtime / "skills" / variant / "SKILL.md").read_text()
            assert f"[{canonical}]" in inherited

with tempfile.TemporaryDirectory() as temp:
    state = Path(temp) / "state"; event = Path(temp) / "event.json"; lanes = Path(temp) / "lanes.json"
    base_snapshot = {"freshness":"stale","capacity":capacity(6,2,1,7,reason("no-schedulable-item","cached scheduler found no additional Ready item","batch receipt","stale")),"lanes":[{"item":".github#1","repository":"FS-GG/.github","workState":"merged","boardStatus":"Done","worker":"kite","activity":"stamp","pr":"#2","blocker":None}]}
    write(lanes, base_snapshot)
    run("--state-dir", str(state), "start", "--session", "demo", "--at", "2026-08-08T00:00:00Z")
    kinds = ["created", "boarded", "claimed", "reviewed", "blocked", "merged", "published", "done"]
    for kind in kinds:
        write(event, {"eventSchema":EVENT_SCHEMA,"kind":kind,"eventKey":kind,"item":".github#1", **({"fromStatus":"In progress","toStatus":"In review","activity":"review complete"} if kind == "reviewed" else {})})
        run("--state-dir", str(state), "emit", "--session", "demo", "--event", str(event), "--lanes", str(lanes), "--mode", "json")
    write(event, {"eventSchema":EVENT_SCHEMA,"kind":"reviewed","eventKey":"reviewed","item":".github#1","fromStatus":"In progress","toStatus":"In review","activity":"review complete"})
    # Retry is deduplicated, restart only rereads the durable ledger, and supplied stale facts do not network.
    out = run("--state-dir", str(state), "show", "--session", "demo", "--event", str(event), "--lanes", str(lanes), "--mode", "json").stdout
    doc = json.loads(out); assert doc["schema"] == "fsgg.coord.session-report/2"; assert doc["sessionTotals"]["done"] == 1
    assert doc["snapshot"] == {"freshness":"stale","network":"not-used"}
    trace_env = {**os.environ,"FSGG_COORD_REPORT_CACHE_TRACE":"1"}
    # A fresh process reuses the exact schema/HWM/snapshot cache; derive() is only reached on a miss.
    cached = run("--state-dir",str(state),"show","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","json",env=trace_env)
    assert cached.stderr.strip() == "fsgg-coord-report: cache hit"
    cache_file = state / "sessions" / "demo.derived.json"
    cached_value = json.loads(cache_file.read_text())
    assert set(cached_value["derived"]) == {"session","lanes","laneCapacity","sessionTotals","snapshot","coordinationProject"}

    # A real append changes the high-water mark; retrying the identical heartbeat does not grow it and hits.
    write(event, {"eventSchema":EVENT_SCHEMA,"kind":"heartbeat","eventKey":"cache-hwm","item":".github#1"})
    first_heartbeat = run("--state-dir",str(state),"emit","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","json",env=trace_env)
    assert first_heartbeat.stderr.strip() == "fsgg-coord-report: cache miss"
    ledger_file = state / "sessions" / "demo.jsonl"; before_retry = ledger_file.read_bytes()
    repeated_heartbeat = run("--state-dir",str(state),"emit","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","json",env=trace_env)
    assert repeated_heartbeat.stderr.strip() == "fsgg-coord-report: cache hit" and ledger_file.read_bytes() == before_retry

    # Snapshot reason/freshness are in the digest and invalidate independently of the append-only ledger.
    changed_reason = json.loads(json.dumps(base_snapshot)); changed_reason["capacity"]["reasons"][0]["detail"] = "a different limiting cause"
    write(lanes, changed_reason)
    assert "cache miss" in run("--state-dir",str(state),"show","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","json",env=trace_env).stderr
    changed_freshness = json.loads(json.dumps(changed_reason)); changed_freshness["freshness"] = "current"
    write(lanes, changed_freshness)
    assert "cache miss" in run("--state-dir",str(state),"show","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","json",env=trace_env).stderr

    # A stale schema key and corrupt cache both fail closed to atomic recomputation without ledger loss.
    cache_doc = json.loads(cache_file.read_text()); cache_doc["key"]["reportSchema"] = "fsgg.coord.session-report/0"; cache_file.write_text(json.dumps(cache_doc))
    assert "cache miss" in run("--state-dir",str(state),"show","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","json",env=trace_env).stderr
    ledger_before_corruption = ledger_file.read_bytes(); cache_file.write_text("{corrupt")
    repaired_cache = run("--state-dir",str(state),"show","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","json",env=trace_env)
    assert "cache miss" in repaired_cache.stderr and ledger_file.read_bytes() == ledger_before_corruption
    json.loads(cache_file.read_text())

    # Emit/show serialize on the session lock; every result and the atomically replaced cache remain complete JSON.
    write(event, {"eventSchema":EVENT_SCHEMA,"kind":"heartbeat","eventKey":"cache-concurrent","item":".github#1"})
    concurrent_commands = [
        [str(CLIENT),"--state-dir",str(state),"emit","--session","demo","--event",str(event),"--lanes",str(lanes),"--scope",scope_for(str(lanes)),"--mode","json"],
        *[[str(CLIENT),"--state-dir",str(state),"show","--session","demo","--event",str(event),"--lanes",str(lanes),"--scope",scope_for(str(lanes)),"--mode","json"] for _ in range(3)],
    ]
    concurrent = [subprocess.Popen(command,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True) for command in concurrent_commands]
    for process in concurrent:
        stdout, stderr = process.communicate(); assert process.returncode == 0, stderr; json.loads(stdout)
    json.loads(cache_file.read_text())
    write(lanes, base_snapshot)
    write(event, {"eventSchema":EVENT_SCHEMA,"kind":"reviewed","eventKey":"reviewed","item":".github#1","fromStatus":"In progress","toStatus":"In review","activity":"review complete"})
    plain = run("--state-dir", str(state), "show", "--session", "demo", "--event", str(event), "--lanes", str(lanes), "--mode", "plain", "--width", "40").stdout
    assert "\x1b[" not in plain and "┌" in plain and max(map(len, plain.splitlines())) <= 40
    colored_env = {key: value for key, value in os.environ.items() if key != "NO_COLOR"}
    rich = run("--state-dir", str(state), "show", "--session", "demo", "--event", str(event), "--lanes", str(lanes), "--mode", "rich", "--color", "always", "--width", "256", env=colored_env).stdout
    assert "\x1b[" in rich and "\x1b[32m" in rich  # Done is semantically green.
    assert strip_ansi(rich) == run("--state-dir", str(state), "show", "--session", "demo", "--event", str(event), "--lanes", str(lanes), "--mode", "plain", "--width", "256").stdout
    # JSON is authoritative; both text projections visibly retain every scalar fact.
    authoritative = json.loads(run("--state-dir", str(state), "show", "--session", "demo", "--event", str(event), "--lanes", str(lanes), "--mode", "json").stdout)
    def scalars(value):
        if isinstance(value, dict):
            for key, child in value.items():
                yield from scalars(child)
        elif isinstance(value, list):
            for child in value: yield from scalars(child)
        else: yield str(value).lower() if isinstance(value, bool) else str(value)
    plain_full = run("--state-dir", str(state), "show", "--session", "demo", "--event", str(event), "--lanes", str(lanes), "--mode", "plain").stdout
    wide_plain = run("--state-dir", str(state), "show", "--session", "demo", "--event", str(event), "--lanes", str(lanes), "--mode", "plain", "--width", "256").stdout
    missing_plain = [value for value in scalars(authoritative) if value not in wide_plain]
    missing_rich = [value for value in scalars(authoritative) if value not in strip_ansi(rich)]
    assert not missing_plain, missing_plain
    assert not missing_rich, missing_rich
    assert all(label in plain for label in ("eventKey", "fromStatus", "toStatus", "activity", "lifecycle", "eventsRecorded", "lanes", "sessionTotals", "snapshot", "trigger"))
    no_color = run("--state-dir", str(state), "show", "--session", "demo", "--event", str(event), "--lanes", str(lanes), "--mode", "rich", "--color", "always", env={**os.environ,"NO_COLOR":"1"}).stdout
    assert "\x1b[" not in no_color
    # Parallel repeats share the fcntl-protected append and retain one receipt.
    write(event, {"eventSchema":EVENT_SCHEMA,"kind":"done","eventKey":"parallel","item":".github#1"})
    processes = [subprocess.Popen([str(CLIENT), "--state-dir",str(state),"emit","--session","demo","--event",str(event),"--lanes",str(lanes),"--scope",scope_for(str(lanes)),"--mode","json"], stdout=subprocess.PIPE, text=True) for _ in range(4)]
    [p.communicate() for p in processes]
    assert json.loads(run("--state-dir",str(state),"show","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","json").stdout)["sessionTotals"]["done"] == 2
    ended = json.loads(run("--state-dir",str(state),"end","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","json").stdout)
    assert ended["session"]["lifecycle"] == "ended"

    # Typed capacity facts explain low/full activity without a compensating board read.
    capacity_cases = {
        "full": capacity(6,2,8,0,reason("slot-cap-reached","all implementation and review slots are occupied")),
        "overlap": capacity(6,2,3,5,
            reason("touch-set-overlap","remaining item overlaps an active declared path","overlap receipt"),
            reason("claim-contention","another worker won the claim compare-and-swap","claim receipt"),
            reason("indeterminate-claim-receipt","claim response did not converge","claim receipt","unknown")),
        "no-work": capacity(6,2,1,7,reason("no-schedulable-item","cached scheduler has no Ready candidate","batch receipt")),
        "rest": capacity(6,2,2,6,
            reason("rest-reserve","REST reserve is held for lifecycle completion","budget ledger"),
            reason("rest-backoff","reset gate requires backoff before another claim","budget ledger")),
        "review": capacity(6,2,6,0,
            reason("review-slots-reserved","two slots are reserved for independent critics","driver policy"),
            reason("review-slots-in-use","both critic slots currently review exact heads","review roster")),
        "human": capacity(6,2,1,7,
            reason("human-blocker","acceptance boundary requires human action","issue state"),
            reason("decision-blocker","a typed planning decision is unresolved","planning receipt")),
    }
    observed_kinds = set()
    for label, lane_capacity in capacity_cases.items():
        supplied = {"freshness":"current","capacity":lane_capacity,"lanes":[]}
        write(lanes, supplied)
        json_doc = json.loads(run("--state-dir",str(state),"show","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","json").stdout)
        assert json_doc["laneCapacity"] == lane_capacity
        observed_kinds.update(entry["kind"] for entry in lane_capacity["reasons"])
        plain40 = run("--state-dir",str(state),"show","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","plain","--width","40").stdout
        rich40 = run("--state-dir",str(state),"show","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","rich","--color","always","--width","40",env=colored_env).stdout
        assert strip_ansi(rich40) == plain40
        assert max(map(display_width, plain40.splitlines())) <= 40
        projected = compact(plain40)
        for scalar in scalars(json_doc["laneCapacity"]):
            assert compact(str(scalar)) in projected, (label, scalar)
    assert observed_kinds == {"slot-cap-reached","review-slots-reserved","review-slots-in-use","touch-set-overlap",
        "no-schedulable-item","rest-reserve","rest-backoff","claim-contention","indeterminate-claim-receipt",
        "human-blocker","decision-blocker"}

    # Scope is an explicit, typed project identity: a cross-project lane cannot be rendered into this ledger.
    scoped_snapshot = scoped({"freshness":"current","capacity":capacity(3,2,1,4,reason("no-schedulable-item","fixture")),"lanes":[
        {"item":".github#11","repository":"FS-GG/.github","activity":"aligned","board":{"status":"In progress","observedAt":"2026-08-08T01:00:00Z"},
         "observed":{"state":"in-progress","observedAt":"2026-08-08T01:00:01Z","claimHolder":"wren","worktree":"/tmp/item","branch":"item/11-a","pr":"#12","head":"abc","checkGate":"pending"}},
        {"item":".github#12","repository":"FS-GG/.github","activity":"repair ahead","board":{"status":"In review","observedAt":"2026-08-08T01:00:00Z"},
         "observed":{"state":"local-repair","observedAt":"2026-08-08T01:00:02Z","claimHolder":"wren","worktree":"/tmp/item","branch":"item/12-a","pr":"#13","head":"def","checkGate":"red"}},
        {"item":".github#13","repository":"FS-GG/.github","activity":"orphan","board":{"status":"In progress","observedAt":"2026-08-08T01:00:00Z"},
         "observed":{"state":"in-progress","observedAt":"2026-08-08T01:00:03Z","claimHolder":"orphan"}},
        {"item":".github#14","repository":"FS-GG/.github","activity":"unclaimed repair","board":{"status":"In progress","observedAt":"2026-08-08T01:00:00Z"},
         "observed":{"state":"local-repair","observedAt":"2026-08-08T01:00:04Z","worktree":"/tmp/item","branch":"item/14-a","head":"fed"}},
        {"item":".github#15","repository":"FS-GG/.github","activity":"unknown","board":{"status":"unknown","observedAt":"2026-08-08T01:00:00Z"},
         "observed":{"state":"unknown","observedAt":"2026-08-08T01:00:05Z"}},
    ]})
    write(lanes, scoped_snapshot)
    observed_doc = json.loads(run("--state-dir",str(state),"show","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","json").stdout)
    assert [lane["disagreement"] for lane in observed_doc["lanes"]] == ["aligned","projection-lag","orphan-claim","work-without-claim","unknown"]
    assert observed_doc["coordinationProject"] == {**COORDINATION,"scopeObservedAt":"2026-08-08T00:00:00Z","scopeReceipt":"who-repo-fsgg-github"}
    wrong_scope = json.loads(json.dumps(scoped_snapshot)); wrong_scope["lanes"][0]["coordinationProject"] = {"owner":"EHotwagner","number":75,"title":"Rogue3"}
    write(lanes, wrong_scope)
    rejected_scope = run("--state-dir",str(state),"show","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","json",check=False)
    assert rejected_scope.returncode != 0 and "does not match" in rejected_scope.stderr
    # Subject inversion: a fully self-consistent foreign snapshot must still fail against the driver-supplied scope receipt.
    rogue_snapshot = json.loads(json.dumps(scoped_snapshot)); rogue = {"owner":"EHotwagner","number":75,"title":"Rogue3"}
    rogue_snapshot["coordinationProject"] = rogue
    for lane in rogue_snapshot["lanes"]: lane["coordinationProject"] = rogue
    write(lanes, rogue_snapshot)
    rejected_foreign = run("--state-dir",str(state),"show","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","json",check=False)
    assert rejected_foreign.returncode != 0 and "externally supplied Coordination scope receipt" in rejected_foreign.stderr

    # Corrections are append-only but remove a bad source event from effective session totals.
    write(lanes, scoped_snapshot)
    run("--state-dir",str(state),"start","--session","correction","--at","2026-08-08T02:00:00Z")
    write(event,{"eventSchema":EVENT_SCHEMA,"kind":"reviewed","eventKey":"wrong-project-review","item":"EHotwagner/rogue3#75"})
    run("--state-dir",str(state),"emit","--session","correction","--event",str(event),"--lanes",str(lanes),"--mode","json")
    write(event,{"eventSchema":EVENT_SCHEMA,"kind":"corrected","eventKey":"remove-wrong-project-review","supersedes":"wrong-project-review","item":"EHotwagner/rogue3#75"})
    run("--state-dir",str(state),"emit","--session","correction","--event",str(event),"--lanes",str(lanes),"--mode","json")
    write(event,{"eventSchema":EVENT_SCHEMA,"kind":"repaired","eventKey":"repair","item":".github#12"})
    run("--state-dir",str(state),"emit","--session","correction","--event",str(event),"--lanes",str(lanes),"--mode","json")
    write(event,{"eventSchema":EVENT_SCHEMA,"kind":"reconciled","eventKey":"reconcile","item":".github#12"})
    run("--state-dir",str(state),"emit","--session","correction","--event",str(event),"--lanes",str(lanes),"--mode","json")
    correction_doc = json.loads(run("--state-dir",str(state),"show","--session","correction","--event",str(event),"--lanes",str(lanes),"--mode","json").stdout)
    assert correction_doc["sessionTotals"]["reviewed"] == 0 and correction_doc["sessionTotals"]["corrected"] == 1
    assert correction_doc["sessionTotals"]["repaired"] == correction_doc["sessionTotals"]["reconciled"] == 1

    # Subject mutation for the former len()-based width gate: code points fit while display cells overflow.
    old_gate_mutant = "│" + "界" * 20 + "│"
    assert len(old_gate_mutant) <= 40 and display_width(old_gate_mutant) > 40
    unicode_snapshot = {"freshness":"current","capacity":capacity(6,2,1,7,
        reason("no-schedulable-item","界界 emoji 🚦 and combining e\u0301 remain bounded","unicode fixture")),
        "lanes":[{"item":".github#界","repository":"FS-GG/.github","workState":"review 🚦","boardStatus":"In review",
                  "worker":"e\u0301-worker","activity":"界"*20+" 🚦 e\u0301","pr":"#2","blocker":"unknown"}]}
    write(lanes, unicode_snapshot)
    unicode_doc = json.loads(run("--state-dir",str(state),"show","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","json").stdout)
    unicode_plain = run("--state-dir",str(state),"show","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","plain","--width","40").stdout
    unicode_rich = run("--state-dir",str(state),"show","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","rich","--color","always","--width","40",env=colored_env).stdout
    assert strip_ansi(unicode_rich) == unicode_plain
    assert max(map(display_width, unicode_plain.splitlines())) <= 40
    projected = compact(unicode_plain)
    for scalar in scalars({"lanes":unicode_doc["lanes"],"laneCapacity":unicode_doc["laneCapacity"]}):
        assert compact(str(scalar)) in projected, scalar

    # Caller facts cannot inject terminal control. JSON retains authoritative raw facts; human routes visibly encode them.
    controls = "CSI:\x1b[31mred\x1b[0m OSC:\x1b]0;title\x07 BS:\x08 CR:\r C0:\x00\x01\x1f C1:\x80\x85\x9b DEL:\x7f"
    assert "\x1b" in controls  # subject mutation: the former direct table projection emitted this byte unchanged.
    control_event = {"eventSchema":EVENT_SCHEMA,"kind":"heartbeat","eventKey":"terminal-controls","item":"item\x08",
                     "activity":"trigger "+controls,"blocker":"osc \x1b]8;;bad\x07"}
    control_snapshot = {"freshness":"snap\x85","capacity":capacity(6,2,1,7,
        reason("human-blocker","capacity "+controls,"source\x1b[2J","fresh\x7f")),
        "lanes":[{"item":"item\x08","repository":"repo\rname","workState":"work\x00state","boardStatus":"In review",
                  "worker":"worker\x9b31m","activity":"lane "+controls,"pr":"#2","blocker":"block\x7f",
                  "freshness":"lane\x80"}]}
    write(event, control_event); write(lanes, control_snapshot)
    control_doc = json.loads(run("--state-dir",str(state),"show","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","json").stdout)
    assert control_doc["trigger"]["activity"] == control_event["activity"]
    assert control_doc["lanes"][0]["activity"] == control_snapshot["lanes"][0]["activity"]
    assert control_doc["laneCapacity"]["reasons"][0]["detail"] == control_snapshot["capacity"]["reasons"][0]["detail"]
    control_plain = run("--state-dir",str(state),"show","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","plain","--width","40").stdout
    control_rich = run("--state-dir",str(state),"show","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","rich","--color","always","--width","40",env=colored_env).stdout
    control_no_color = run("--state-dir",str(state),"show","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","rich","--color","always","--width","40",env={**os.environ,"NO_COLOR":"1"}).stdout
    assert not raw_terminal_controls(control_plain) and not raw_terminal_controls(control_no_color)
    assert strip_ansi(control_rich) == control_plain == control_no_color
    assert not raw_terminal_controls(strip_ansi(control_rich))
    assert max(map(display_width, control_plain.splitlines())) <= 40
    projected = compact(control_plain)
    for scalar in scalars({"trigger":control_doc["trigger"],"lanes":control_doc["lanes"],
                           "laneCapacity":control_doc["laneCapacity"],"snapshot":control_doc["snapshot"],
                           "sessionTotals":control_doc["sessionTotals"]}):
        assert compact(human_escape(scalar)) in projected, repr(scalar)
    # Every rich ESC belongs to the renderer's SGR grammar; stripping those leaves none.
    sgrs = re.findall(r"\x1b\[[0-9;]*m", control_rich)
    assert sgrs and control_rich.count("\x1b") == len(sgrs)

    # Visible encoding is injective: a raw control and a caller's literal escape spelling remain distinct.
    control_codes = [*range(0x20), *range(0x7f, 0xa0)]
    raw_escape = "".join(map(chr, control_codes))
    literal_escape = "".join(f"\\u{code:04x}" for code in control_codes)
    assert human_escape(raw_escape) != human_escape(literal_escape)
    assert human_decode(human_escape(raw_escape)) == raw_escape
    assert human_decode(human_escape(literal_escape)) == literal_escape
    for code in control_codes:
        raw, literal = chr(code), f"\\u{code:04x}"
        assert human_escape(raw) != human_escape(literal)
        assert human_decode(human_escape(raw)) == raw and human_decode(human_escape(literal)) == literal
    collision_event = {"eventSchema":EVENT_SCHEMA,"kind":"heartbeat","eventKey":"escape-collision",
                       "item":"raw="+raw_escape,"activity":"raw="+raw_escape+" literal="+literal_escape}
    collision_snapshot = {"freshness":"raw="+raw_escape+" literal="+literal_escape,
        "capacity":capacity(6,2,2,6,reason("human-blocker","literal="+literal_escape,
            "raw="+raw_escape,"raw="+raw_escape+" literal="+literal_escape)),
        "lanes":[
            {"item":"raw="+raw_escape,"repository":"literal="+literal_escape,"workState":"blocked","boardStatus":"In review","worker":"w","activity":"raw="+raw_escape+" literal="+literal_escape,"pr":"#2","blocker":"b"},
            {"item":"literal="+literal_escape,"repository":"raw="+raw_escape,"workState":"blocked","boardStatus":"Blocked","worker":"w","activity":"literal="+literal_escape+" raw="+raw_escape,"pr":"#3","blocker":"b"},
        ]}
    write(event, collision_event); write(lanes, collision_snapshot)
    collision_doc = json.loads(run("--state-dir",str(state),"show","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","json").stdout)
    assert collision_doc["trigger"]["item"] != collision_doc["lanes"][1]["item"]
    assert collision_doc["sessionTotals"]["unresolved"] == ["raw="+raw_escape,"literal="+literal_escape]
    collision_plain = run("--state-dir",str(state),"show","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","plain","--width","40").stdout
    collision_rich = run("--state-dir",str(state),"show","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","rich","--color","always","--width","40",env=colored_env).stdout
    assert strip_ansi(collision_rich) == collision_plain and not raw_terminal_controls(collision_plain)
    assert max(map(display_width, collision_plain.splitlines())) <= 40
    collision_projection = compact(collision_plain)
    assert compact("raw="+human_escape(raw_escape)) in collision_projection
    assert compact("literal="+human_escape(literal_escape)) in collision_projection
    for scalar in scalars({"trigger":collision_doc["trigger"],"lanes":collision_doc["lanes"],
                           "laneCapacity":collision_doc["laneCapacity"],"snapshot":collision_doc["snapshot"],
                           "sessionTotals":collision_doc["sessionTotals"]}):
        assert compact(human_escape(scalar)) in collision_projection, repr(scalar)
    # Invert the schema boundary: caller structure cannot forge a session record or bypass dedupe.
    write(event, {"eventSchema":EVENT_SCHEMA,"kind":"done","eventKey":"reserved","item":"x","record":"session"})
    rejected = run("--state-dir",str(state),"emit","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","json", check=False)
    assert rejected.returncode != 0 and "ledger fields" in rejected.stderr
    write(event, {"eventSchema":EVENT_SCHEMA,"kind":"done","eventKey":"reserved","item":"x"})
    run("--state-dir",str(state),"emit","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","json")
    run("--state-dir",str(state),"emit","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","json")
    final = json.loads(run("--state-dir",str(state),"show","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","json").stdout)
    assert final["sessionTotals"]["done"] == 3 and final["sessionTotals"]["eventsRecorded"] == 14
print("coord-report fixtures: PASS")
