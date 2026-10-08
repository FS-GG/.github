#!/usr/bin/env python3
"""One explicit public-package installation; never select or recover telemetry.

The operator owns admission and the available SDK. This command has no latest,
activation, retry, migration, download-SDK or arbitrary executable recipe mode.
"""
import argparse
import base64
import fcntl
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import resource
import selectors
import shutil
import signal
import stat
import subprocess
import sys
import tempfile
import time
import zipfile

VERSION = "0.100.0"
SOURCE = "3ed8ad419a64253ca6f665e9779e5e4d110f50a5"
ARCHIVE_SHA = "86e08d635cdb7fcb3a908b9c373e7417e0635d0ca8b13c9ae6b96f36f9ff95f4"
MANIFEST_SHA = "0b31c416a4779fff83052e49e890d07d65194793dc86327bd239e35b8b18c916"
PAYLOAD_ID = "sha256:e25c9ca654d5aaaa4944daa78c823fb481e9f8ef0e278861e15500f48408b078"
PREFIX = "tools/net10.0/any/"
STORE = ".store/fs.gg.coord.cli/0.100.0/fs.gg.coord.cli/0.100.0/"
SHIM = "fsgg-coord-engine"
MAX_FILE = 128 * 1024 * 1024
MAX_TREE = 256 * 1024 * 1024
MAX_OUTPUT = 32768  # Per stream; 64 KiB combined per fixed command.
WORK_SECONDS = 150
CLEANUP_SECONDS = 5
CONFIG = b'''<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear />
<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
</packageSources></configuration>\n'''


class Refusal(RuntimeError):
    pass


def require(condition, cause):
    if not condition:
        raise Refusal(cause)


def digest(path, maximum=MAX_FILE):
    info = path.lstat()
    require(stat.S_ISREG(info.st_mode) and info.st_size <= maximum, "regular-file-bound:" + str(path))
    flags = os.O_RDONLY | os.O_NOFOLLOW
    with os.fdopen(os.open(path, flags), "rb") as stream:
        before = os.fstat(stream.fileno())
        require((before.st_dev, before.st_ino) == (info.st_dev, info.st_ino), "file-identity-changed")
        result = hashlib.sha256()
        size = 0
        while block := stream.read(65536):
            size += len(block)
            require(size <= maximum, "file-read-bound")
            result.update(block)
        after = os.fstat(stream.fileno())
        require((before.st_size, before.st_mtime_ns, before.st_ctime_ns) ==
                (after.st_size, after.st_mtime_ns, after.st_ctime_ns) and size == before.st_size,
                "file-changed-during-read")
    return result.hexdigest()


def pinned_bytes(path, expected, maximum, cause):
    # Hash the same immutable bytes that the parser consumes, never reopen by name.
    require(stat.S_ISREG(path.lstat().st_mode), cause)
    with os.fdopen(os.open(path, os.O_RDONLY | os.O_NOFOLLOW), "rb") as stream:
        require(os.fstat(stream.fileno()).st_size <= maximum, cause)
        data = stream.read(maximum + 1)
    require(len(data) <= maximum and hashlib.sha256(data).hexdigest() == expected, cause)
    return data


def release(manifest, archive):
    value = json.loads(pinned_bytes(manifest, MANIFEST_SHA, 65536, "release-manifest-pin"))
    descriptor, state = value["descriptor"], value["state"]
    promotion = state["channelPromotion"]
    receipt = promotion["receipt"]
    require(value["schema"] == "fsgg.release-saga/1" and
            descriptor["sourceSha"] == receipt["sourceSha"] == SOURCE and
            descriptor["version"] == receipt["version"] == VERSION and
            descriptor["channel"] == "stable" and promotion["state"] == "promoted" and
            value["contentId"] == receipt["contentId"], "release-promotion")
    packages = descriptor["packages"]
    require(len(packages) == 3 and {(p["id"], p["version"]) for p in packages} ==
            {(name, VERSION) for name in ("FS.GG.Coord.Cli", "FS.GG.Kit", "FS.GG.Drivers")},
            "coherent-package-identities")
    cli = next(p for p in packages if p["id"] == "FS.GG.Coord.Cli")
    require(cli["artifact"]["payloadSha256"] == PAYLOAD_ID, "release-payload-binding")
    for feed in ("github", "nuget"):
        observed = state["feeds"][feed]["packages"]["FS.GG.Coord.Cli"]
        require(observed["state"] == "verified" and observed["externalPayloadSha256"] == PAYLOAD_ID,
                "release-feed-verification")
    require(state["feeds"]["nuget"]["packages"]["FS.GG.Coord.Cli"]["externalSha256"] == ARCHIVE_SHA,
            "public-archive-release-binding")
    archive_bytes = pinned_bytes(archive, ARCHIVE_SHA, 64 * 1024 * 1024, "public-archive-pin")
    members = {}
    total = 0
    with zipfile.ZipFile(io.BytesIO(archive_bytes)) as package:
        require(len(package.infolist()) <= 256, "archive-population-bound")
        for member in package.infolist():
            name = member.filename
            path = PurePosixPath(name)
            require(name not in members and not path.is_absolute() and ".." not in path.parts and
                    str(path) == name and "\\" not in name and not member.is_dir() and
                    not stat.S_ISLNK(member.external_attr >> 16) and member.file_size <= MAX_FILE,
                    "archive-member-shape")
            total += member.file_size
            require(total <= MAX_TREE, "archive-byte-bound")
            sha = hashlib.sha256()
            with package.open(member) as stream:
                while block := stream.read(65536):
                    sha.update(block)
            members[name] = (member.file_size, sha.hexdigest())
    require(any(name.startswith(PREFIX) for name in members), "archive-tools-absent")
    return members


def selector_snapshot(path):
    try:
        info = path.lstat()
    except FileNotFoundError:
        return None
    # Witness equality even for an unmanaged path; install-only never replaces it.
    target = os.readlink(path) if stat.S_ISLNK(info.st_mode) else None
    resolved = str(path.resolve(strict=False)) if target is not None else None
    return (info.st_dev, info.st_ino, info.st_mode, info.st_size, info.st_mtime_ns,
            info.st_ctime_ns, target, resolved)


def managed_directory(path):
    # Existing ancestors must be actual directories; do not traverse a symlink.
    for part in reversed((path, *path.parents)):
        if part.exists() or part.is_symlink():
            require(stat.S_ISDIR(part.lstat().st_mode), "managed-parent-not-directory")
        else:
            part.mkdir(mode=0o700)
    require(path.stat().st_uid == os.getuid() and not path.stat().st_mode & 0o022 and
            os.access(path, os.W_OK), "managed-parent-custody")


def installed(destination, members):
    expected = {STORE + name: row for name, row in members.items() if name.startswith(PREFIX)}
    # NuGet extracts only some package-envelope members. Their presence is optional;
    # when present their bytes still have to match the authenticated archive.
    for name in ("FS.GG.Coord.Cli.nuspec", "README.md", ".signature.p7s"):
        if name in members:
            expected[STORE + name] = members[name]
    generated = {SHIM, ".store/fs.gg.coord.cli/0.100.0/project.assets.json", STORE + ".nupkg.metadata",
                 STORE + "fs.gg.coord.cli.0.100.0.nupkg.sha512"}
    archives = {STORE + "FS.GG.Coord.Cli.nupkg", STORE + "fs.gg.coord.cli.0.100.0.nupkg"}
    seen, observations, total = set(), [], 0
    require(stat.S_ISDIR(destination.lstat().st_mode), "installed-root-shape")
    for current, directories, files in os.walk(destination, followlinks=False):
        require(len(seen) + len(directories) + len(files) <= 384, "installed-population-bound")
        for name in directories:
            require(stat.S_ISDIR((Path(current) / name).lstat().st_mode), "installed-link-or-special")
        for name in files:
            path = Path(current) / name
            relative = path.relative_to(destination).as_posix()
            info = path.lstat()
            require(stat.S_ISREG(info.st_mode), "installed-link-or-special")
            total += info.st_size
            require(total <= MAX_TREE, "installed-byte-bound")
            sha = digest(path)
            seen.add(relative)
            if relative in expected:
                require((info.st_size, sha) == expected[relative], "installed-payload-diff:" + relative)
            elif relative in archives:
                require(sha == ARCHIVE_SHA, "installed-package-diff")
            elif relative in generated:
                if relative != SHIM:
                    require(not info.st_mode & 0o111, "unexpected-executable")
                if relative.endswith(".json") or relative.endswith(".metadata"):
                    require(info.st_size <= 1024 * 1024, "generated-json-bound")
                    require(isinstance(json.loads(path.read_bytes()), dict), "generated-json-shape")
                if relative.endswith(".sha512"):
                    require(info.st_size <= 128, "generated-sha512-bound")
                    base64.b64decode(path.read_bytes(), validate=True)
                observations.append({"path": relative, "bytes": info.st_size, "sha256": sha,
                                     "origin": "observed-sdk-output-not-independent-provenance"})
            else:
                raise Refusal("unexpected-installed-file:" + relative)
    require({STORE + name for name in members if name.startswith(PREFIX)} <= seen,
            "installed-tools-missing")
    require(generated <= seen and bool(archives & seen), "installed-generated-files-missing")
    require(os.access(destination / SHIM, os.X_OK), "installed-shim-not-executable")
    return observations


def child_limits():
    resource.setrlimit(resource.RLIMIT_FSIZE, (MAX_FILE, MAX_FILE))
    resource.setrlimit(resource.RLIMIT_AS, (1024 * 1024 * 1024, 1024 * 1024 * 1024))
    resource.setrlimit(resource.RLIMIT_CPU, (WORK_SECONDS, WORK_SECONDS))


def command(argv, environment, workspace, kind, deadline, outcome):
    record = {"kind": kind, "argv": argv, "exit": None, "terminal": False,
              "cleanup": "not-started", "stdout": b"", "stderr": b"",
              "stdoutEOF": False, "stderrEOF": False}
    outcome["commands"].append(record)
    require(deadline > time.monotonic(), "original-work-deadline")
    process = subprocess.Popen(argv, env=environment, cwd=workspace, stdout=subprocess.PIPE,
                               stderr=subprocess.PIPE, stdin=subprocess.DEVNULL, close_fds=True,
                               start_new_session=True, preexec_fn=child_limits)
    record["pid"] = process.pid
    first = None
    try:
        with selectors.DefaultSelector() as ready:
            for name, stream in (("stdout", process.stdout), ("stderr", process.stderr)):
                os.set_blocking(stream.fileno(), False)
                ready.register(stream, selectors.EVENT_READ, name)
            while ready.get_map():
                remaining = deadline - time.monotonic()
                require(remaining > 0, kind + "-deadline")
                for key, _ in ready.select(min(remaining, 0.1)):
                    block = os.read(key.fileobj.fileno(), 4096)
                    if not block:
                        record[key.data + "EOF"] = True
                        ready.unregister(key.fileobj)
                    else:
                        room = MAX_OUTPUT - len(record[key.data])
                        record[key.data] += block[:room]
                        require(len(block) <= room, kind + "-output-bound")
            remaining = deadline - time.monotonic()
            require(remaining > 0, kind + "-deadline")
            try:
                record["exit"] = process.wait(timeout=remaining)
            except subprocess.TimeoutExpired:
                raise Refusal(kind + "-deadline") from None
            record["terminal"] = True
            record["cleanup"] = "direct-child-reaped"
        require(record["exit"] == 0, kind + "-exit:" + str(record["exit"]))
    except Exception as error:
        first = error
        record["failure"] = str(error)
    finally:
        if process.returncode is None:
            record["cleanup"] = "owned-process-group-kill-requested"
            try:
                os.killpg(process.pid, signal.SIGKILL)
                record["exit"] = process.wait(timeout=CLEANUP_SECONDS)
                record["terminal"] = True
                record["cleanup"] = "direct-child-reaped-group-termination-requested"
            except Exception as error:
                record["cleanup"] = "unknown"
                if first is None:
                    first = error
                else:
                    outcome["additionalErrors"].append("cleanup:" + str(error))
        # Each closer/log write is independent and cannot replace the first cause.
        for name, stream in (("stdout", process.stdout), ("stderr", process.stderr)):
            for action in (stream.close, lambda name=name: (workspace / (kind + "." + name)).write_bytes(record[name])):
                try:
                    action()
                except Exception as error:
                    if first is None:
                        first = error
                    else:
                        outcome["additionalErrors"].append("stream-finalization:" + str(error))
    if first is not None:
        raise first
    return record


def execute(args):
    outcome = {"schema": "fsgg.coord.host-install/1", "version": VERSION, "sourceSha": SOURCE,
               "publicArchiveSha256": ARCHIVE_SHA, "payloadId": PAYLOAD_ID,
               "state": "not-attempted", "firstError": None, "additionalErrors": [],
               "selectorUnchanged": None, "commands": [], "generated": [], "reporting": "pending",
               "nativeAccepted": False, "wholeProcessTreeCleanup": "not-qualified"}
    lock = None
    before = None
    selector = None
    captured = False
    deadline = time.monotonic() + WORK_SECONDS
    try:
        require(os.getuid() != 0, "run-as-developer")
        home = Path(os.environ["HOME"])
        require(home.is_absolute(), "absolute-home-required")
        root = home / ".local/share/fs-gg/tools"
        state = home / ".local/state/fs-gg"
        selector = home / ".local/bin" / SHIM
        managed_directory(root)
        managed_directory(state)
        lock_path = state / "coord-tool-update.lock"
        lock = os.open(lock_path, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
        info = os.fstat(lock)
        require(stat.S_ISREG(info.st_mode) and info.st_uid == os.getuid() and info.st_nlink == 1 and
                stat.S_IMODE(info.st_mode) == 0o600, "update-lock-custody")
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise Refusal("update-lock-busy") from None
        before = selector_snapshot(selector)
        captured = True
        destination = root / ("coord-" + VERSION)
        require(not destination.exists() and not destination.is_symlink(), "existing-install-needs-reconciliation")
        if before is not None and before[7] is not None:
            require(not Path(before[7]).is_relative_to(destination.resolve(strict=False)),
                    "selector-already-targets-destination")
        members = release(args.manifest, args.archive)
        dotnet = shutil.which("dotnet")
        require(dotnet is not None, "available-dotnet-required")
        dotnet = str(Path(dotnet).absolute())
        workspace = Path(tempfile.mkdtemp(prefix="coord-install-0.100.0-", dir=state))
        outcome["workspace"] = str(workspace)
        config = workspace / "NuGet.Config"
        config.write_bytes(CONFIG)
        environment = {"HOME": str(home), "PATH": os.environ.get("PATH", "/usr/bin:/bin"),
                       "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_NOLOGO": "1", "DOTNET_PROCESSOR_COUNT": "1"}
        for key, name in (("DOTNET_CLI_HOME", "dotnet-home"), ("NUGET_PACKAGES", "packages"),
                          ("NUGET_HTTP_CACHE_PATH", "http-cache"), ("NUGET_PLUGINS_CACHE_PATH", "plugins"),
                          ("NUGET_SCRATCH", "scratch"), ("TMPDIR", "tmp")):
            directory = workspace / name
            directory.mkdir(mode=0o700)
            environment[key] = str(directory)
        # Rebind absence under the held lock immediately before the exact installer.
        require(not destination.exists() and not destination.is_symlink(), "existing-install-needs-reconciliation")
        require((lock_path.lstat().st_dev, lock_path.lstat().st_ino) == (info.st_dev, info.st_ino),
                "update-lock-identity-changed")
        require(selector_snapshot(selector) == before, "selector-changed-before-install")
        outcome["state"] = "install-intent"
        command([dotnet, "tool", "install", "FS.GG.Coord.Cli", "--version", VERSION,
                 "--tool-path", str(destination), "--configfile", str(config), "--no-cache"],
                environment, workspace, "install", deadline, outcome)
        outcome["state"] = "installed-unverified-inactive"
        require(selector_snapshot(selector) == before, "selector-changed-during-install-only")
        outcome["generated"] = installed(destination, members)
        outcome["state"] = "payload-verified-inactive"
        reported = command([str(destination / SHIM), "--version"], environment, workspace,
                           "version", deadline, outcome)
        require(reported["stdout"] == b"0.100.0.0\n" and reported["stderr"] == b"", "version-output-diff")
        # Reverify installed bytes after launch before reporting success.
        require(installed(destination, members) == outcome["generated"], "installed-files-changed")
        require(time.monotonic() <= deadline, "original-work-deadline")
        outcome["state"] = "version-verified-inactive"
    except Exception as error:
        outcome["firstError"] = str(error)
    finally:
        if captured:
            try:
                outcome["selectorUnchanged"] = selector_snapshot(selector) == before
                require(outcome["selectorUnchanged"], "selector-changed-during-install-only")
            except Exception as error:
                if outcome["firstError"] is None:
                    outcome["firstError"] = str(error)
                else:
                    outcome["additionalErrors"].append(str(error))
        if lock is not None:
            try:
                os.close(lock)
            except OSError as error:
                if outcome["firstError"] is None:
                    outcome["firstError"] = str(error)
                else:
                    outcome["additionalErrors"].append(str(error))
    return outcome


def emit(outcome, stream):
    # Logs stay in the private workspace. The bounded summary preserves stream pins.
    summary = dict(outcome)
    summary["commands"] = []
    for original in outcome["commands"]:
        record = dict(original)
        for name in ("stdout", "stderr"):
            if isinstance(record[name], bytes):
                value = record[name]
                record[name] = {"bytes": len(value), "sha256": hashlib.sha256(value).hexdigest()}
        summary["commands"].append(record)
    try:
        summary["reporting"] = "emitted"
        stream.write(json.dumps(summary, sort_keys=True) + "\n")
        stream.flush()
        outcome["reporting"] = "emitted"
    except OSError as error:
        outcome["reporting"] = "failed"
        if outcome["firstError"] is None:
            outcome["firstError"] = "reporting-failed:" + str(error)
        else:
            outcome["additionalErrors"].append("reporting-failed:" + str(error))
    return 0 if outcome["firstError"] is None and outcome["reporting"] == "emitted" else 1


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, allow_abbrev=False)
    parser.add_argument("--version", required=True, choices=[VERSION])
    parser.add_argument("--install-only", required=True, action="store_true")
    parser.add_argument("--manifest", required=True, type=Path, help="retained promoted release-manifest.json")
    parser.add_argument("--archive", required=True, type=Path, help="retained public NuGet package")
    arguments = list(sys.argv[1:] if argv is None else argv)
    if any(arguments.count(flag) > 1 for flag in ("--version", "--install-only", "--manifest", "--archive")):
        parser.error("repeated operation arguments")
    args = parser.parse_args(arguments)
    os.umask(0o077)
    return emit(execute(args), sys.stdout)


if __name__ == "__main__":
    sys.exit(main())
