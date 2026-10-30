#!/usr/bin/env python3
"""Controls for the pinned coordination kit (FS-GG/.github#4964) against the compiled Wizard.

The pin file is checked against git, then a local server serves exactly the pinned revision's files
(from `git show`) and the real `retrofit` command and the production creation-time step
(`wireLegacyCoordination`, through coordination-wiring.fsx) run against it. FSGG_COORDINATION_KIT_RAW_BASE
only relocates the host; the pinned revision path and sha256 anchors are still enforced, which is what
the tampered-server cases prove: the Wizard refuses and writes nothing.

usage: coordination-kit-pin.py --dll PATH/new-sdd-workspace.dll
"""
from __future__ import annotations

import argparse
import hashlib
import http.server
import json
import os
import pathlib
import shutil
import subprocess
import sys
import tempfile
import threading

ROOT = pathlib.Path(__file__).resolve().parents[2]
HERE = pathlib.Path(__file__).resolve().parent
PIN = json.loads((ROOT / "scripts/NewSddWorkspace/coordination-kit-pin.json").read_text())
REVISION = PIN["revision"]
failures: list[str] = []


def check(name: str, ok: bool, detail: str = "") -> None:
    print(("PASS  " if ok else "FAIL  ") + name)
    if not ok:
        failures.append(name)
        if detail:
            print("    | " + detail[-1500:].replace("\n", "\n    | "))


def blob(path: str) -> bytes:
    return subprocess.check_output(["git", "-C", str(ROOT), "show", f"{REVISION}:{path}"])


def kit_paths() -> list[str]:
    manifest = json.loads(blob("registry/coordination-kit-skill-manifest.json"))
    paths = ["registry/coordination-kit-skill-manifest.json", "scripts/fsgg-coord", "dist/dotnet/.config/dotnet-tools.json"]
    for skill in manifest["skills"]:
        paths += [f".claude/skills/{skill['id']}/{row['path']}" for row in skill["files"]]
    return paths


class Server:
    def __init__(self, overrides: dict[str, bytes | int]):
        files = {f"/FS-GG/.github/{REVISION}/{p}": blob(p) for p in kit_paths()}
        self.requests: list[str] = []
        owner = self

        class Handler(http.server.BaseHTTPRequestHandler):
            def do_GET(self):  # noqa: N802
                owner.requests.append(self.path)
                prefix = f"/FS-GG/.github/{REVISION}/"
                relative = self.path[len(prefix):] if self.path.startswith(prefix) else None
                override = overrides.get(relative) if relative is not None else None
                if isinstance(override, int):
                    self.send_response(override)
                    self.end_headers()
                    return
                body = override if isinstance(override, bytes) else files.get(self.path)
                if body is None:
                    self.send_response(404)
                    self.end_headers()
                    return
                self.send_response(200)
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

            def log_message(self, *_):
                pass

        self.httpd = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=self.httpd.serve_forever, daemon=True)

    def __enter__(self):
        self.thread.start()
        self.base = f"http://127.0.0.1:{self.httpd.server_address[1]}"
        return self

    def __exit__(self, *_):
        self.httpd.shutdown()
        self.httpd.server_close()


def env_for(work: pathlib.Path, base: str, **extra: str) -> dict[str, str]:
    home = work / "home"
    home.mkdir(exist_ok=True)
    env = {"PATH": os.environ["PATH"], "HOME": str(home), "DOTNET_CLI_HOME": str(home), "DOTNET_NOLOGO": "1",
           "NO_COLOR": "1", "DOTNET_ROOT": os.environ.get("DOTNET_ROOT", "/usr/share/dotnet"), "TMPDIR": str(work),
           "FSGG_COORDINATION_KIT_RAW_BASE": base}
    env.update(extra)
    return env


def retrofit(dll: str, work: pathlib.Path, base: str, *extra: str) -> subprocess.CompletedProcess:
    target = work / "ws"
    target.mkdir(exist_ok=True)
    (target / ".fsgg").mkdir(exist_ok=True)
    return subprocess.run(["dotnet", dll, "retrofit", str(target), *extra], env=env_for(work, base),
                          capture_output=True, text=True, timeout=300)


def wiring(dll: str, fsx: pathlib.Path, work: pathlib.Path, base: str) -> subprocess.CompletedProcess:
    target = work / "created"
    target.mkdir(exist_ok=True)
    return subprocess.run(["dotnet", "fsi", f"--reference:{dll}", str(fsx)], env=env_for(work, base, KIT_TARGET=str(target)),
                          capture_output=True, text=True, timeout=600)


def written(root: pathlib.Path) -> list[str]:
    return sorted(str(p.relative_to(root)) for p in root.rglob("*") if p.is_file())


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--dll", required=True)
    args = parser.parse_args()
    dll = args.dll
    wiring_fsx = HERE / "coordination-wiring.fsx"

    pin = subprocess.run([sys.executable, "-B", str(ROOT / "scripts/new-sdd-workspace-kit-pin.py"), "check"], capture_output=True, text=True)
    check("the pin names a full commit and its anchors equal the blobs at that commit", pin.returncode == 0 and len(REVISION) == 40, pin.stdout + pin.stderr)
    check("the pinned revision is not a moving ref", REVISION not in ("main", "master") and all(c in "0123456789abcdef" for c in REVISION))

    skill_paths = [p for p in kit_paths() if p.startswith(".claude/skills/")]
    victim = skill_paths[0]

    with tempfile.TemporaryDirectory(prefix="kit-pin-") as tmp:
        tmp_path = pathlib.Path(tmp)

        # ── retrofit against the faithful pinned server ───────────────────────────────────────────────
        work = tmp_path / "good"; work.mkdir()
        with Server({}) as server:
            run = retrofit(dll, work, server.base)
            ws = work / "ws"
            check("retrofit from the pinned revision exits 0", run.returncode == 0, run.stdout + run.stderr)
            check("every request used the pinned revision path, never a moving ref",
                  server.requests and all(r.startswith(f"/FS-GG/.github/{REVISION}/") for r in server.requests), "\n".join(server.requests[:5]))
            equal = all((ws / root / rel.split(".claude/skills/", 1)[1]).read_bytes() == blob(rel)
                        for rel in skill_paths for root in (".claude/skills", ".agents/skills"))
            check("every skill file is byte-identical to the pinned revision in both agent roots", equal)
            shim = ws / "scripts/fsgg-coord"
            check("the shim equals the pinned revision and is executable", shim.read_bytes() == blob("scripts/fsgg-coord") and os.access(shim, os.X_OK))
            tools = json.loads((ws / ".config/dotnet-tools.json").read_text())
            pinned_tools = json.loads(blob("dist/dotnet/.config/dotnet-tools.json"))
            check("the coordination tool entry equals the pinned revision", tools["tools"]["fs.gg.coord.cli"] == pinned_tools["tools"]["fs.gg.coord.cli"])
            log = ws / ".fsgg/coordination-retrofits.json"
            check("the retrofit is recorded in the Wizard-owned log", log.exists() and json.loads(log.read_text())["retrofits"][0]["ref"] == REVISION)
            check("SDD-owned provenance was not created or written", not (ws / ".fsgg/scaffold-provenance.json").exists())
            rerun = retrofit(dll, work, server.base)
            check("a repeat retrofit is a clean no-op", rerun.returncode == 0 and "already wired" in rerun.stdout.lower(), rerun.stdout + rerun.stderr)

        # ── a legacy `retrofits` member is migrated by the next retrofit ──────────────────────────────
        work = tmp_path / "legacy"; work.mkdir()
        legacy = work / "ws/.fsgg"; legacy.mkdir(parents=True)
        provenance = {"schemaVersion": 3, "generator": {"id": "FS.GG.SDD.Artifacts", "version": "2.5.3"},
                      "retrofits": [{"kind": "coordination", "ref": "main", "materialized": [], "reMaterializedDrift": []}]}
        (legacy / "scaffold-provenance.json").write_text(json.dumps(provenance))
        with Server({}) as server:
            run = retrofit(dll, work, server.base)
            moved = json.loads((legacy / "scaffold-provenance.json").read_text())
            entries = json.loads((legacy / "coordination-retrofits.json").read_text())["retrofits"]
            check("retrofit moves a legacy retrofits member out of SDD provenance", run.returncode == 0 and "retrofits" not in moved
                  and moved["generator"] == provenance["generator"] and [e["ref"] for e in entries] == ["main", REVISION], run.stdout + run.stderr)

        # ── mismatch: a tampered skill file is refused and nothing is written ───────────────────────
        for label, overrides in (("a tampered skill file", {victim: b"tampered\n"}),
                                 ("a tampered manifest", {"registry/coordination-kit-skill-manifest.json": b"{}"}),
                                 ("a tampered shim", {"scripts/fsgg-coord": b"#!/bin/sh\nexit 0\n"}),
                                 ("a tampered tool manifest", {"dist/dotnet/.config/dotnet-tools.json": b"{}"})):
            work = tmp_path / ("refuse-" + label.replace(" ", "-")); work.mkdir()
            with Server(overrides) as server:
                run = retrofit(dll, work, server.base)
                ws = work / "ws"
                check(f"retrofit refuses {label} with a nonzero exit", run.returncode == 1 and "failed pinned verification" in run.stdout, run.stdout + run.stderr)
                check(f"retrofit writes nothing for {label}", written(ws) == [], str(written(ws)))
            work = tmp_path / ("create-" + label.replace(" ", "-")); work.mkdir()
            with Server(overrides) as server:
                run = wiring(dll, wiring_fsx, work, server.base)
                created = work / "created"
                check(f"the creation step is Failed for {label}", "OUTCOME Failed" in run.stdout and "differs from the pinned" in run.stdout, run.stdout + run.stderr)
                check(f"the creation step writes nothing, not even the board env, for {label}", written(created) == [], str(written(created)))

        # ── an unpinned --ref is refused before any byte is fetched ─────────────────────────────────
        work = tmp_path / "ref"; work.mkdir()
        with Server({}) as server:
            run = retrofit(dll, work, server.base, "--ref", "main")
            check("retrofit --ref main is refused without fetching", run.returncode == 1 and "cannot be verified" in run.stdout and server.requests == [], run.stdout + run.stderr + str(server.requests))
            run = retrofit(dll, work, server.base, "--ref", REVISION)
            check("retrofit --ref <pinned revision> is accepted", run.returncode == 0, run.stdout + run.stderr)

        # ── unavailable (as opposed to mismatched) stays a warning ────────────────────────────────────
        work = tmp_path / "unavailable"; work.mkdir()
        with Server({victim: 404}) as server:
            run = wiring(dll, wiring_fsx, work, server.base)
            created = work / "created"
            check("an unfetchable kit file is still a warning: the env lands and verified files are vendored",
                  "OUTCOME Warned" in run.stdout and (created / ".claude/settings.json").exists() and (created / "scripts/fsgg-coord").exists(), run.stdout + run.stderr)
            check("the unfetchable file itself is not written", not (created / ".claude/skills" / victim.split(".claude/skills/", 1)[1]).exists())

        # ── the faithful creation step succeeds and vendors the verified bytes ──────────────────────
        work = tmp_path / "wired"; work.mkdir()
        with Server({}) as server:
            run = wiring(dll, wiring_fsx, work, server.base)
            created = work / "created"
            check("the creation step succeeds against the pinned kit", "OUTCOME Succeeded" in run.stdout, run.stdout + run.stderr)
            check("the creation step vendors byte-identical skill files",
                  all((created / ".claude/skills" / rel.split(".claude/skills/", 1)[1]).read_bytes() == blob(rel) for rel in skill_paths))

    print(f"\n{len(failures)} failure(s)")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
