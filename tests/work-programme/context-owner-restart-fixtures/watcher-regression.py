"""Synthetic watcher samples: no native queries, subprocesses, or delivery effects.

A watcher revision is local to one invocation. It is never an owner-return revision.
Run directly with Python; this supplements the existing watcher suite.
"""
import importlib.util
import sys
import unittest
from datetime import datetime, timezone
from pathlib import Path

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location("restart_fixture_delivery", ROOT / "tools/routine-delivery.py")
MODULE = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = MODULE
spec.loader.exec_module(MODULE)
HEAD = "b" * 40


class Clock:
    def __init__(self):
        self.value = 0

    def __call__(self):
        return self.value

    def sleep(self, seconds):
        self.value += seconds


class SyntheticApi:
    def __init__(self, samples):
        self.samples = iter(samples)
        self.query_count = 0

    def get_pr(self, *_args):
        self.query_count += 1
        return {"head": {"sha": HEAD}, "state": "open", "merged": False,
                "draft": False, "updated_at": str(self.query_count)}

    def checks(self, *_args):
        self.query_count += 1
        return next(self.samples)


def check(bucket):
    return {"bucket": bucket, "completedAt": "" if bucket == "pending" else "2026-10-05T00:00:10Z",
            "event": "pull_request", "link": "https://github.test/synthetic/run/1",
            "name": "synthetic-test", "startedAt": "2026-10-05T00:00:00Z",
            "state": "IN_PROGRESS" if bucket == "pending" else "SUCCESS", "workflow": "synthetic-CI"}


def observe(samples):
    clock, api, events = Clock(), SyntheticApi(samples), []
    code = MODULE.watch_checks("FS-GG/Synthetic", 7, HEAD, 12, clock=clock, sleep=clock.sleep,
        utc_now=lambda: datetime(2026, 10, 5, 0, 0, int(clock.value), tzinfo=timezone.utc),
        api_factory=lambda *_a, **_kw: api, emit=events.append)
    return code, events, api.query_count


class RestartWatcherTests(unittest.TestCase):
    def test_unchanged_observations_keep_source_time_and_original_deadline(self):
        code, events, queries = observe([[check("pending")]] * 3)
        self.assertEqual((code, [e["event"] for e in events]), (3, ["initial", "deadline"]))
        self.assertEqual(queries, 9)
        self.assertEqual(events[0]["observedAt"], "2026-10-05T00:00:00+00:00")

    def test_original_watch_emits_material_terminal_change(self):
        code, events, _ = observe([[check("pending")], [check("pass")]])
        self.assertEqual((code, [e["event"] for e in events]), (0, ["initial", "terminal"]))
        self.assertEqual([e["revision"] for e in events], [1, 2])

    def test_dropped_notification_recovers_by_current_read_with_new_watch_local_revision(self):
        # Drop the previous invocation's notification; recovery reads current facts.
        _, original, _ = observe([[check("pending")], [check("pass")]])
        code, recovered, _ = observe([[check("pass")]])
        self.assertEqual((code, recovered[0]["event"]), (0, "terminal"))
        self.assertEqual((original[-1]["revision"], recovered[0]["revision"]), (2, 1))
        self.assertTrue(all(not e["applyAuthorized"] and e["readiness"] == "not-evaluated"
                            for e in original + recovered))


if __name__ == "__main__":
    unittest.main()
