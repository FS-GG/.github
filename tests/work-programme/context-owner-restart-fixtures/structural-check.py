"""Check retained synthetic input custody/shape; never substitute for F# execution."""
import json
from pathlib import Path

ROOT = Path(__file__).parent


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        assert key not in result, f"duplicate JSON key: {key}"
        result[key] = value
    return result


def read(path):
    assert not path.is_symlink()
    assert path.stat().st_size <= 262144
    return json.loads(path.read_text(), object_pairs_hook=unique_object)


index = read(ROOT / "fixture-index.json")
assert index["synthetic"] is True
assert len(index["fixtures"]) == len(set(index["fixtures"])) == 6
for name in index["fixtures"]:
    x = read(ROOT / (name + ".delta-input.json"))
    expected = read(ROOT / (name + ".expectations.json"))
    assert expected["synthetic"] is True
    assert x["schema"] == "fsgg.programme.delta-input/1"
    s = x["snapshot"]
    assert s["schema"] == "fsgg.programme.snapshot/1"
    assert s["campaign"] == "synthetic-context-restart"
    lanes = {lane["id"]: lane for lane in s["lanes"]}
    assert len(lanes) == len(s["lanes"])
    assert sorted(lane["id"] for lane in lanes.values() if lane["inFlight"]) == expected["expectedProperties"]["reservationIds"]
    for row in x["baseReturns"] + x["currentReturns"]:
        assert row["schema"] == "fsgg.programme.lane-return/1"
        assert row["campaign"] == s["campaign"]
        assert row["revision"] > row["supersedes"] >= 0
        assert "SYNTHETIC" in row["narrative"]
        assert len(row["narrative"].encode()) <= 2048
        assert isinstance(row["exception"], str) and isinstance(row["continuation"], str)
        assert len(row["inputPacketSha256"]) == 64
        assert set(row["boundaries"]) == {"source", "validation", "publication", "installed", "native", "projection"}
    if name == "interrupted-dispatch":
        assert x["baseRevision"] == "" and not x["baseReturns"] and not x["currentReturns"]
        assert lanes["A"]["inFlight"] and not lanes["A"]["readable"]
    elif name == "missed-owner-revision":
        assert x["baseReturns"][0]["revision"] == 1 and x["currentReturns"][0]["supersedes"] == 2
    elif name == "absent-owner":
        assert not x["currentReturns"] and not lanes["B"]["readable"] and lanes["B"]["inFlight"]
    elif name == "full-reservation":
        assert s["capacity"] == 2 and not lanes["C"]["inFlight"] and lanes["C"]["state"] == "ready"
        assert lanes["C"]["native"] == "not-required"
    elif name == "one-integrator-same-owner-repair":
        assert s["capacity"] == 1 and len(lanes) == 1 and lanes["A"]["native"] == "not-required"
        assert lanes["A"]["state"] == "repair"
        old, new = x["baseReturns"][0], x["currentReturns"][0]
        assert all(old[k] == new[k] for k in ["owner", "attempt", "originalAttempt", "inputPacketSha256"])
    elif name == "superseded-owner-return":
        assert [row["revision"] for row in x["currentReturns"]] == [99, 2]
        assert x["currentReturns"][0]["owner"] != lanes["A"]["owner"]
print("PASS six synthetic input structural controls; actual F# execution remains separate")
