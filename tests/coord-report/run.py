#!/usr/bin/env python3
"""Black-box fixtures for fsgg-coord-report's ledger and projections."""
from __future__ import annotations
import json, os, subprocess, sys, tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CLIENT = ROOT / "scripts/fsgg-coord-report"

EVENT_SCHEMA = "fsgg.coord.session-event/1"

def run(*args, env=None, check=True):
    return subprocess.run([str(CLIENT), *args], text=True, capture_output=True, check=check, env=env)

def write(path, value): path.write_text(json.dumps(value)); return str(path)

# The four routed variants inherit their canonical driver, rather than carrying a
# sixth subtly different reporting recipe.  Assert both runtime roots so projection
# drift cannot make the installed skill omit the reporter.
for runtime in (".claude", ".agents"):
    for canonical, variants in (("drive-board", ("drive-board-normal", "drive-board-best")), ("work-board", ("work-board-normal", "work-board-best"))):
        body = (ROOT / runtime / "skills" / canonical / "SKILL.md").read_text()
        assert "fsgg-coord-report" in body and "already-cached lane snapshot" in body
        for variant in variants:
            inherited = (ROOT / runtime / "skills" / variant / "SKILL.md").read_text()
            assert f"[{canonical}]" in inherited

with tempfile.TemporaryDirectory() as temp:
    state = Path(temp) / "state"; event = Path(temp) / "event.json"; lanes = Path(temp) / "lanes.json"
    write(lanes, {"freshness":"stale","lanes":[{"item":".github#1","repository":"FS-GG/.github","workState":"merged","boardStatus":"Done","worker":"kite","activity":"stamp","pr":"#2","blocker":None}]})
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
    strip_ansi = lambda text: __import__("re").sub(r"\x1b\[[0-9;]*m", "", text)
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
