#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
python3 - "$ROOT" <<'PY'
from datetime import datetime, timedelta, timezone
import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile

root = Path(sys.argv[1])
def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    return module

collector = load("collector", root / "scripts/coordination-health-collector.py")
readiness = load("readiness", root / "scripts/coordination-retirement-readiness.py")
try:
    collector.exact_windows(datetime(2026, 9, 6, 23, 59, 59, tzinfo=timezone.utc))
    raise AssertionError("future week accepted")
except ValueError as error:
    assert "2026-09-07T00:00:00Z" in str(error)
windows = collector.exact_windows(datetime(2026, 9, 7, tzinfo=timezone.utc))
assert [(collector.iso(a), collector.iso(b)) for a, b in windows] == [
    ("2026-08-17T00:00:00Z", "2026-08-24T00:00:00Z"),
    ("2026-08-24T00:00:00Z", "2026-08-31T00:00:00Z"),
    ("2026-08-31T00:00:00Z", "2026-09-07T00:00:00Z")]
assert collector.SUCCESSOR_QUERIES == readiness.CANONICAL_SUCCESSOR_QUERIES
original_gh_json = collector.gh_json
collector.gh_json = lambda *_: [{"total_count": 1500, "incomplete_results": False,
                                 "items": [{"html_url": f"https://example.invalid/{n}"} for n in range(1000)]}]
try:
    collector.search("fixture", root)
    raise AssertionError("capped search accepted")
except ValueError:
    pass
collector.gh_json = lambda *_: [{"total_count": 0, "incomplete_results": True, "items": []}]
try:
    collector.search("fixture", root)
    raise AssertionError("incomplete search accepted")
except ValueError:
    pass
collector.gh_json = original_gh_json
identity, coherent = collector.classify_release_manifest(
    {"contentId":"sha256:x","descriptor":{"version":"9.9.9","releaseId":"github:9.9.9",
      "policyVersion":"release-saga/1","sourceSha":"a"*40,"packages":[]},
     "state":{"channelPromotion":{"state":"promoted","receipt":{"contentId":"sha256:x"}},"feeds":{}}},
    {"version":"9.9.9","sourceSha":"a"*40,"contentId":"sha256:x"}, {"coord-engine"}, "9.9.9")
assert not identity and not coherent
ids = ["FS.GG.Coord.Cli","FS.GG.Kit","FS.GG.Drivers"]
packages = [{"id":name,"artifact":{"payloadSha256":"sha256:" + str(index)}} for index,name in enumerate(ids)]
feeds = {feed:{name:{"state":"verified","externalPayloadSha256":"sha256:" + str(index)} for index,name in enumerate(ids)} for feed in ("github","nuget")}
identity, coherent = collector.classify_release_manifest(
    {"contentId":"sha256:x","descriptor":{"version":"9.9.9","releaseId":"github:9.9.9",
      "policyVersion":"release-saga/1","sourceSha":"a"*40,"packages":packages},
     "state":{"channelPromotion":{"state":"promoted","receipt":{"contentId":"sha256:x"}},"feeds":feeds}},
    {"version":"9.9.9","sourceSha":"a"*40,"contentId":"sha256:x"}, {"coord-engine","kit","drivers"}, "9.9.9")
assert identity and coherent

with tempfile.TemporaryDirectory() as work:
    repo = Path(work)
    def git(*args):
        return subprocess.check_output(["git", *args], cwd=repo, text=True).strip()
    subprocess.run(["git", "init", "-q"], cwd=repo, check=True)
    subprocess.run(["git", "config", "user.email", "fixture@example.invalid"], cwd=repo, check=True)
    subprocess.run(["git", "config", "user.name", "fixture"], cwd=repo, check=True)
    (repo / "docs").mkdir(); (repo / "docs/a.md").write_text("before\n")
    subprocess.run(["git", "add", "."], cwd=repo, check=True); subprocess.run(["git", "commit", "-qm", "base"], cwd=repo, check=True)
    start = git("rev-parse", "HEAD")
    (repo / "docs/a.md").write_text("after\n")
    subprocess.run(["git", "commit", "-qam", "arbitrary squash subject"], cwd=repo, check=True)
    docs_commit = git("rev-parse", "HEAD")
    (repo / "scripts").mkdir(); (repo / "scripts/gate.py").write_text("print('ok')\n")
    subprocess.run(["git", "add", "."], cwd=repo, check=True); subprocess.run(["git", "commit", "-qm", "fix: repair behavior"], cwd=repo, check=True)
    behavior_commit = git("rev-parse", "HEAD")
    (repo / "reviews/roadmap").mkdir(parents=True)
    (repo / "reviews/roadmap/fixture.json").write_text(__import__('json').dumps({"schema_version":3,"repair_rounds":2,"reviewed_commits":[start,docs_commit,behavior_commit]}))
    subprocess.run(["git", "add", "."], cwd=repo, check=True); subprocess.run(["git", "commit", "-qm", "review evidence"], cwd=repo, check=True)
    end_sha = git("rev-parse", "HEAD")
    now = datetime.now(timezone.utc)
    def fake_commit_api(args, _root):
        endpoint = next(value for value in args if value.startswith("repos/"))
        if "/commits/" in endpoint:
            return {"commit":{"committer":{"date":collector.iso(now)},"message":"arbitrary reviewed repair"}}
        previous, commit = endpoint.rsplit("/",1)[-1].split("...")
        path = "docs/a.md" if commit == docs_commit else "scripts/gate.py"
        return {"merge_base_commit":{"sha":previous},"files":[{"filename":path,"patch":"@@\n-old\n+new"}]}
    collector.gh_json = fake_commit_api
    repairs, statements, rows = collector.commit_measure(repo, now-timedelta(days=1), now+timedelta(days=1), end_sha)
    collector.gh_json = original_gh_json
    assert (repairs, statements) == (2, 1)
    assert [row["statement_only"] for row in rows] == [True, False]

parser_text = (root / "scripts/coordination-health-collector.py").read_text()
assert 'parser.add_argument("--period' not in parser_text
assert 'parser.add_argument("--verdict' not in parser_text
assert 'source_sha != remote_sha' in parser_text
workflow_text = (root / ".github/workflows/coord-board-reconcile.yml").read_text()
assert "FSGG_COORD_LIFECYCLE_SHADOW_REPORT:" in workflow_text
assert "coordination-health-${{ github.run_id }}" in workflow_text
assert readiness.ACCEPTANCE_ENABLED is False
print("coordination health collector: 16 passed, 0 failed")
PY
