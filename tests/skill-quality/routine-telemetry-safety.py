#!/usr/bin/env python3
"""Exercise bounded public I/O and candidate-index-only Git telemetry safety."""
import importlib.util, json, subprocess, tempfile
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("routine_telemetry_safety", ROOT / "tools/routine-telemetry-safety.py")
module = importlib.util.module_from_spec(SPEC); assert SPEC.loader; SPEC.loader.exec_module(module)
GUARD = ROOT / "scripts/check-telemetry-git-safety.py"
with tempfile.TemporaryDirectory(prefix="fsgg-telemetry-safety-") as scratch:
    root = Path(scratch); source, output = root / "aggregate.json", root / "output.json"
    source.write_text('{"schema":"synthetic/1","attempts":1}')
    assert module.read_public_object(str(source)) == ({"schema":"synthetic/1","attempts":1}, None)
    assert module.write_public_report(str(output), {"attempts":1}, [str(source)]) is None
    output.write_text("preserve")
    assert module.write_public_report(str(source), {"count":2}, [str(source)]) == "input-output-alias"
    assert source.read_text() == '{"schema":"synthetic/1","attempts":1}'
    link = root / "link.json"; link.symlink_to(output)
    assert module.write_public_report(str(link), {}, []) == "output-symlink-refused"
    assert module.read_public_object(str(root / ".codex/sessions/private.json"))[1] == "unsafe-input-path"
    large = root / "large.json"; large.write_bytes(b" " * (1024 * 1024 + 1))
    assert module.read_public_object(str(large))[1] == "input-too-large"
    assert module.write_public_report(str(output), {"x":"x" * (64 * 1024)}, []) == "output-too-large"
    assert output.read_text() == "preserve"
    repo = root / "repo"; repo.mkdir(); subprocess.run(["git", "init", "-q", str(repo)], check=True)
    subprocess.run(["git", "-C", str(repo), "config", "user.email", "fixture@example.invalid"], check=True)
    subprocess.run(["git", "-C", str(repo), "config", "user.name", "Fixture"], check=True)
    (repo / "aggregate.json").write_text(json.dumps({"schema":"fsgg.routine-unit-economics/2", "count":1}))
    subprocess.run(["git", "-C", str(repo), "add", "aggregate.json"], check=True)
    assert subprocess.run([str(GUARD), "--repo", str(repo)], capture_output=True).returncode == 0
    (repo / "session.jsonl").write_text('{"type":"response_' + 'item"}\n')
    subprocess.run(["git", "-C", str(repo), "add", "-f", "session.jsonl"], check=True)
    assert subprocess.run([str(GUARD), "--repo", str(repo)], capture_output=True).returncode == 1
    subprocess.run(["git", "-C", str(repo), "reset", "-q", "session.jsonl"], check=True)
    (repo / "aggregate.json").write_text('{"session_' + 'id":"private"}')
    subprocess.run(["git", "-C", str(repo), "add", "-f", "aggregate.json"], check=True)
    assert subprocess.run([str(GUARD), "--repo", str(repo)], capture_output=True).returncode == 1
    subprocess.run(["git", "-C", str(repo), "reset", "-q", "aggregate.json"], check=True)
    (repo / "telemetry-evidence.json").write_text(' ' * (64 * 1024 + 1))
    subprocess.run(["git", "-C", str(repo), "add", "-f", "telemetry-evidence.json"], check=True)
    assert subprocess.run([str(GUARD), "--repo", str(repo)], capture_output=True).returncode == 1
    subprocess.run(["git", "-C", str(repo), "reset", "-q", "telemetry-evidence.json"], check=True)
    # Binary fixtures exist only in this temporary repo. Extension and renamed-signature detection are
    # independent: neither a renamed database nor a private immutable spool batch may be forced in.
    (repo / "innocent.bin").write_bytes(b"SQLite format " + b"3\x00" + b"synthetic")
    subprocess.run(["git", "-C", str(repo), "add", "-f", "innocent.bin"], check=True)
    assert subprocess.run([str(GUARD), "--repo", str(repo)], capture_output=True).returncode == 1
    subprocess.run(["git", "-C", str(repo), "reset", "-q", "innocent.bin"], check=True)
    (repo / "worker.ready").write_text('{"schema":"fsgg.telemetry.' + 'ingest/1"}')
    subprocess.run(["git", "-C", str(repo), "add", "-f", "worker.ready"], check=True)
    assert subprocess.run([str(GUARD), "--repo", str(repo)], capture_output=True).returncode == 1
print("routine-telemetry-safety: bounded I/O and forced-index rejection pass")
