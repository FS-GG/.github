#!/usr/bin/env python3
"""Process regression for the production dashboard publisher-event CLI hook."""
import os
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[3]
ENGINE = ROOT / "src/FS.GG.Coord.Cli/bin/Debug/net10.0/fsgg-coord-engine.dll"
HEALTH = (b'{"schema":"fsgg.telemetry.dashboard-event-health/1","status":"ready",'
          b'"reason":null,"observedAt":"2026-09-27T00:00:00Z","publicRevision":null,"commit":null}\n')


def run(config, env):
    return subprocess.run(["dotnet", str(ENGINE), "telemetry", "dashboard", "publisher-event",
                           "--config", str(config)], cwd=ROOT, env=env, capture_output=True, check=False,
                          timeout=10)


def main():
    assert ENGINE.is_file(), f"build the production CLI first: {ENGINE}"
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        config = root / "telemetry.json"
        config.write_text("{}\n", encoding="utf-8")
        script = root / "dashboard.py"
        script.write_text(
            "import os,sys\n"
            "assert sys.argv == ['" + str(script) + "', 'publisher-event', '--config', '" + str(config) + "']\n"
            "if os.environ.get('SKILL_FS_01_DASHBOARD_FAIL') == '1': sys.exit(3)\n"
            "print('{\"schema\":\"fsgg.telemetry.dashboard-event-health/1\",\"status\":\"ready\",\"reason\":null,\"observedAt\":\"2026-09-27T00:00:00Z\",\"publicRevision\":null,\"commit\":null}')\n",
            encoding="utf-8")
        env = dict(os.environ, FSGG_TELEMETRY_DASHBOARD_SCRIPT=str(script))
        ready = run(config, env)
        assert (ready.returncode, ready.stdout, ready.stderr) == (0, HEALTH, b""), ready
        env["SKILL_FS_01_DASHBOARD_FAIL"] = "1"
        failed = run(config, env)
        assert failed.returncode == 1 and b"publisher-event failed" in failed.stderr, failed
        env["FSGG_TELEMETRY_DASHBOARD_SCRIPT"] = str(root / "missing.py")
        unavailable = run(config, env)
        assert unavailable.returncode == 1 and b"script unavailable" in unavailable.stderr, unavailable
    print("production dashboard publisher-event hook: PASS")


if __name__ == "__main__":
    main()
