#!/usr/bin/env python3
"""Black-box fixtures for fsgg-coord-report's ledger and projections."""
from __future__ import annotations
import json, os, re, subprocess, sys, tempfile, unicodedata
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CLIENT = ROOT / "scripts/fsgg-coord-report"

EVENT_SCHEMA = "fsgg.coord.session-event/1"
CAPACITY_SCHEMA = "fsgg.coord.lane-capacity/1"

def run(*args, env=None, check=True):
    return subprocess.run([str(CLIENT), *args], text=True, capture_output=True, check=check, env=env)

def write(path, value): path.write_text(json.dumps(value)); return str(path)

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

# The four routed variants inherit their canonical driver, rather than carrying a
# sixth subtly different reporting recipe.  Assert both runtime roots so projection
# drift cannot make the installed skill omit the reporter.
for runtime in (".claude", ".agents"):
    for canonical, variants in (("drive-board", ("drive-board-normal", "drive-board-best")), ("work-board", ("work-board-normal", "work-board-best"))):
        body = (ROOT / runtime / "skills" / canonical / "SKILL.md").read_text()
        assert "fsgg-coord-report" in body and "already-cached lane snapshot" in body
        assert "typed lane-capacity facts" in body and "indeterminate receipt" in body
        for variant in variants:
            inherited = (ROOT / runtime / "skills" / variant / "SKILL.md").read_text()
            assert f"[{canonical}]" in inherited

with tempfile.TemporaryDirectory() as temp:
    state = Path(temp) / "state"; event = Path(temp) / "event.json"; lanes = Path(temp) / "lanes.json"
    write(lanes, {"freshness":"stale","capacity":capacity(6,2,1,7,reason("no-schedulable-item","cached scheduler found no additional Ready item","batch receipt","stale")),"lanes":[{"item":".github#1","repository":"FS-GG/.github","workState":"merged","boardStatus":"Done","worker":"kite","activity":"stamp","pr":"#2","blocker":None}]})
    run("--state-dir", str(state), "start", "--session", "demo", "--at", "2026-08-08T00:00:00Z")
    kinds = ["created", "boarded", "claimed", "reviewed", "blocked", "merged", "published", "done"]
    for kind in kinds:
        write(event, {"eventSchema":EVENT_SCHEMA,"kind":kind,"eventKey":kind,"item":".github#1", **({"fromStatus":"In progress","toStatus":"In review","activity":"review complete"} if kind == "reviewed" else {})})
        run("--state-dir", str(state), "emit", "--session", "demo", "--event", str(event), "--lanes", str(lanes), "--mode", "json")
    write(event, {"eventSchema":EVENT_SCHEMA,"kind":"reviewed","eventKey":"reviewed","item":".github#1","fromStatus":"In progress","toStatus":"In review","activity":"review complete"})
    # Retry is deduplicated, restart only rereads the durable ledger, and supplied stale facts do not network.
    out = run("--state-dir", str(state), "show", "--session", "demo", "--event", str(event), "--lanes", str(lanes), "--mode", "json").stdout
    doc = json.loads(out); assert doc["schema"] == "fsgg.coord.session-report/1"; assert doc["sessionTotals"]["done"] == 1
    assert doc["snapshot"] == {"freshness":"stale","network":"not-used"}
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
                yield key
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
    processes = [subprocess.Popen([str(CLIENT), "--state-dir",str(state),"emit","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","json"], stdout=subprocess.PIPE, text=True) for _ in range(4)]
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
    # Invert the schema boundary: caller structure cannot forge a session record or bypass dedupe.
    write(event, {"eventSchema":EVENT_SCHEMA,"kind":"done","eventKey":"reserved","item":"x","record":"session"})
    rejected = run("--state-dir",str(state),"emit","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","json", check=False)
    assert rejected.returncode != 0 and "ledger fields" in rejected.stderr
    write(event, {"eventSchema":EVENT_SCHEMA,"kind":"done","eventKey":"reserved","item":"x"})
    run("--state-dir",str(state),"emit","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","json")
    run("--state-dir",str(state),"emit","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","json")
    final = json.loads(run("--state-dir",str(state),"show","--session","demo","--event",str(event),"--lanes",str(lanes),"--mode","json").stdout)
    assert final["sessionTotals"]["done"] == 3 and final["sessionTotals"]["eventsRecorded"] == 12
print("coord-report fixtures: PASS")
