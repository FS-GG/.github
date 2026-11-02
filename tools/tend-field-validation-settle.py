#!/usr/bin/env python3
"""Checked controller for #5206's three-request incident. Preview by default; --admit appends local evidence only.

Use the separately qualified released engine's runtime directory and exact artifact hashes. This is not an
unpublished-engine installer, a generic custody override, or a provider mutation/retry command.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import stat
import subprocess


def checked(path: Path, sha: str | None = None, private: bool = False) -> bytes:
    info = path.lstat()
    if not path.is_absolute() or not stat.S_ISREG(info.st_mode) or info.st_uid not in ((os.getuid(),) if private else (0, os.getuid())):
        raise ValueError("regular absolute owner-owned file required")
    if private and stat.S_IMODE(info.st_mode) != 0o600:
        raise ValueError("private input mode must be 0600")
    data = path.read_bytes()
    if sha is not None and hashlib.sha256(data).hexdigest() != sha:
        raise ValueError("runtime identity changed")
    return data


def main(argv=None) -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--runtime-directory", type=Path, required=True)
    p.add_argument("--expect-github-sha256", required=True)
    p.add_argument("--expect-core-sha256", required=True)
    p.add_argument("--dotnet", type=Path, required=True)
    p.add_argument("--expect-dotnet-sha256", required=True)
    for flag in ("credentials", "custody-root", "original-path", "request-path", "original-runtime-directory", "output-directory"):
        p.add_argument("--" + flag, type=Path, required=True)
    p.add_argument("--root-admission-reference", required=True)
    p.add_argument("--admit", action="store_true")
    a = p.parse_args(argv)
    try:
        for value in (a.expect_github_sha256, a.expect_core_sha256, a.expect_dotnet_sha256):
            if not re.fullmatch(r"[0-9a-f]{64}", value):
                raise ValueError("exact runtime hashes required")
        core = a.runtime_directory / "FS.GG.Coord.Core.dll"
        github = a.runtime_directory / "FS.GG.Coord.GitHub.dll"
        checked(core, a.expect_core_sha256); checked(github, a.expect_github_sha256)
        checked(a.dotnet, a.expect_dotnet_sha256)
        checked(a.credentials, private=True); checked(a.original_path, private=True); checked(a.request_path)
        if not all(getattr(a, name).is_absolute() for name in ("custody_root", "original_runtime_directory", "output_directory")):
            raise ValueError("absolute paths required")
        if not re.fullmatch(r"https://github\.com/FS-GG/\.github/issues/5206#issuecomment-\d+", a.root_admission_reference):
            raise ValueError("Root admission reference required")
        if not a.admit:
            print("tend field recovery: preview only; no process, provider read or certificate")
            return 0
        runtime = os.environ.get("XDG_RUNTIME_DIR", "")
        if not runtime or not Path(runtime).is_absolute() or not a.output_directory.is_absolute():
            raise ValueError("explicit runtime and output paths required")
        a.output_directory.mkdir(mode=0o700)  # exclusive run; never overwrite/retry an earlier controller
        config = {key: str(getattr(a, name)) for key, name in (
            ("credentials", "credentials"), ("custodyRoot", "custody_root"), ("originalPath", "original_path"),
            ("requestPath", "request_path"), ("originalRuntimeDirectory", "original_runtime_directory"))}
        config["rootAdmissionReference"] = a.root_admission_reference
        source = Path(__file__).with_suffix(".fsx.in").read_text()
        # F# verbatim strings escape quotes by doubling them; no shell or interpolated F# expressions.
        references = "\n".join('#r @"' + str(path).replace('"', '""') + '"' for path in (core, github))
        for name, data in (("input.json", json.dumps(config)), ("controller.fsx", references + "\n" + source)):
            with (a.output_directory / name).open("x") as handle:
                os.chmod(handle.name, 0o600); handle.write(data)
        env = {key: os.environ[key] for key in ("HOME", "PATH", "LANG", "DOTNET_ROOT", "XDG_RUNTIME_DIR") if key in os.environ}
        env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
        with (a.output_directory / "stdout").open("x") as out, (a.output_directory / "stderr").open("x") as err:
            os.chmod(out.name, 0o600); os.chmod(err.name, 0o600)
            child = subprocess.Popen([str(a.dotnet), "fsi", "--exec", str(a.output_directory / "controller.fsx"),
                                      str(a.output_directory / "input.json")], env=env, stdout=out, stderr=err,
                                     start_new_session=True, umask=0o077)
            try:
                code = child.wait(timeout=120)
            except subprocess.TimeoutExpired:
                os.killpg(child.pid, signal.SIGKILL); child.wait(); code = 124
        print("tend field recovery: finished; inspect private evidence; no provider mutation")
        return 0 if code == 0 else 3
    except (OSError, ValueError):
        print("tend field recovery: refused; inputs/evidence preserved")
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
