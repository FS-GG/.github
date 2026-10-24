#!/usr/bin/env python3
"""Read native usage through the maintained strict readers, once per retained transcript.

The reader is the source-built `fsgg-coord-engine` CLI (see build-reader.sh). It runs read-only; the
receipt store must lie outside the current directory, every repository and the system temporary
directory (the reader refuses otherwise). No receiver, telemetry store or UTEL database is opened.
Writes data/usage/<runtime>/<id>.csv and data/usage/index.tsv (runtime, id, exit code, path, stderr).
"""
import hashlib
import os
import subprocess
import sys

from census_common import DATA, cfg

C = cfg()
reader = os.path.expanduser(os.environ.get("CENSUS_READER") or C["reader"])
receipts = os.path.expanduser(C["receipt_store"])
out = os.path.join(DATA, "usage")
for sub in ("claude", "codex"):
    os.makedirs(os.path.join(out, sub), exist_ok=True)
os.makedirs(receipts, exist_ok=True)


def transcripts(root, runtime):
    root = os.path.expanduser(root)
    for base, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if d != "memory"]
        for name in files:
            if name.endswith(".jsonl"):
                yield runtime, os.path.join(base, name)


files = sorted(list(transcripts(C["claude_home"], "claude")) + list(transcripts(C["codex_home"], "codex")), key=lambda t: (t[0] != "claude", t[1]))
with open(os.path.join(out, "index.tsv"), "w", encoding="utf-8") as index:
    for runtime, path in files:
        ident = hashlib.sha256(path.encode()).hexdigest()[:16]
        proc = subprocess.run([reader, "telemetry", "usage", "collect", runtime, "--session-file", path, "--task", C["task"], "--all-responses",
                               "--since", C["lo"], "--until", C["hi"], "--coord-version", C["coord_version"], "--sdd-version", "unknown",
                               "--contracts-version", "unknown", "--receipt-store", receipts, "--format", "csv",
                               "--output", os.path.join(out, runtime, ident + ".csv")], capture_output=True, text=True)
        err = " ".join((proc.stdout + proc.stderr).split())
        index.write(f"{runtime}\t{ident}\t{proc.returncode}\t{path}\t{err}\n")
print(len(files), "transcripts", file=sys.stderr)
