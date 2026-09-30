#!/usr/bin/env python3
"""Validate and qualify the fixed disposable telemetry collector topology."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import pathlib
import re
import shutil
import stat
import subprocess
import sys
import zipfile

REQUIRED_VERSION = "0.2.1"
CAPTURE_V2 = b"fsgg.telemetry.protected-native-capture/2"
HEX64 = re.compile(r"^[0-9a-f]{64}$")
SHA40 = re.compile(r"^[0-9a-f]{40}$")
RUN_ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")
TOOLS_PREFIX = "tools/net10.0/linux-x64/"
PODMAN_DEFAULT_CAPABILITIES = frozenset({
    "CAP_CHOWN",
    "CAP_DAC_OVERRIDE",
    "CAP_FOWNER",
    "CAP_FSETID",
    "CAP_KILL",
    "CAP_NET_BIND_SERVICE",
    "CAP_SETFCAP",
    "CAP_SETGID",
    "CAP_SETPCAP",
    "CAP_SETUID",
    "CAP_SYS_CHROOT",
})


class Refusal(Exception):
    pass


def require(value: bool, detail: str) -> None:
    if not value:
        raise Refusal(detail)


def sha256(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def regular(path: pathlib.Path, maximum: int) -> None:
    info = path.lstat()
    require(stat.S_ISREG(info.st_mode) and not path.is_symlink(), f"regular file required: {path}")
    require(0 < info.st_size <= maximum, f"file size refused: {path}")


def exact_hex(value: str, label: str, pattern: re.Pattern[str] = HEX64) -> str:
    require(pattern.fullmatch(value) is not None, f"{label} is malformed")
    require(set(value) != {"0"}, f"{label} placeholder refused")
    return value


def load_object(path: pathlib.Path, maximum: int = 1024 * 1024) -> dict:
    regular(path, maximum)
    value = json.loads(path.read_text(encoding="utf-8"))
    require(isinstance(value, dict), f"JSON object required: {path}")
    return value


def validate_selection(version: str, source_sha: str, package_sha: str, manifest_sha: str, journal_sha: str) -> None:
    require(version == REQUIRED_VERSION, f"Host version must be {REQUIRED_VERSION}")
    exact_hex(source_sha, "source SHA", SHA40)
    exact_hex(package_sha, "package SHA-256")
    exact_hex(manifest_sha, "manifest SHA-256")
    exact_hex(journal_sha, "journal SHA-256")
    require(len({package_sha, manifest_sha, journal_sha}) == 3, "release digests must be distinct")


def package_members(package: pathlib.Path) -> list[zipfile.ZipInfo]:
    regular(package, 32 * 1024 * 1024)
    with zipfile.ZipFile(package) as archive:
        members = archive.infolist()
        require(0 < len(members) <= 512, "Host archive population refused")
        selected: list[zipfile.ZipInfo] = []
        names: set[str] = set()
        for member in members:
            name = member.filename
            parts = pathlib.PurePosixPath(name).parts
            require("\\" not in name and not name.startswith("/") and ".." not in parts and "." not in parts,
                    "unsafe Host archive path")
            require(name not in names, "duplicate Host archive path")
            names.add(name)
            require(not stat.S_ISLNK(member.external_attr >> 16), "Host archive symbolic link refused")
            require(member.file_size <= 32 * 1024 * 1024, "Host archive member is oversized")
            if name.startswith(TOOLS_PREFIX) and not name.endswith("/"):
                selected.append(member)
        host_name = TOOLS_PREFIX + "FS.GG.Telemetry.Host.dll"
        runtime_name = TOOLS_PREFIX + "FS.GG.Telemetry.Host.runtimeconfig.json"
        require(any(x.filename == host_name for x in selected), "Host DLL is missing")
        require(any(x.filename == runtime_name for x in selected), "Host runtime config is missing")
        host = next(x for x in selected if x.filename == host_name)
        host_bytes = archive.read(host)
        require(CAPTURE_V2 in host_bytes or CAPTURE_V2.decode().encode("utf-16le") in host_bytes,
                "Host package lacks protected capture /2")
        return selected


def verify_release(repository: pathlib.Path, package: pathlib.Path, manifest: pathlib.Path, journal: pathlib.Path,
                   version: str, source_sha: str, package_sha: str, manifest_sha: str, journal_sha: str) -> dict:
    validate_selection(version, source_sha, package_sha, manifest_sha, journal_sha)
    regular(package, 32 * 1024 * 1024)
    require(sha256(package) == package_sha, "Host package digest differs")
    require(sha256(manifest) == manifest_sha, "Host manifest digest differs")
    require(sha256(journal) == journal_sha, "Host journal digest differs")
    release = load_object(manifest)
    published = load_object(journal)
    expected = {
        "archiveSha256", "createdAt", "dependencyLockSha256", "framework", "packageId",
        "producerPayloadSha256", "runtimePrerequisites", "schema", "sourceSha",
        "supportedStoreSchemaMax", "supportedStoreSchemaMin", "tag", "target",
        "uiAssetTreeSha256", "version",
    }
    require(set(release) == expected, "Host manifest shape differs")
    require(release["schema"] == "fsgg.telemetry.host-release/1", "Host manifest schema differs")
    require(release["version"] == version and release["tag"] == f"telemetry-host/v{version}", "Host identity differs")
    require(release["sourceSha"] == source_sha and release["archiveSha256"] == package_sha, "Host binding differs")
    require(release["packageId"] == "FS.GG.Telemetry.Host", "Host package ID differs")
    require(release["framework"] == "net10.0" and release["target"] == "linux-x64", "Host platform differs")
    require(release["supportedStoreSchemaMax"] == 12, "Host lacks schema-12 support")
    require(published.get("schema") == "fsgg.telemetry-host-release-journal/v1", "Host journal schema differs")
    require(set(published.get("observations", {})) == {"github", "nuget"}, "both-feed proof is incomplete")
    for feed in ("github", "nuget"):
        observation = published["observations"][feed]
        require(observation.get("version") == version and observation.get("packageId") == "FS.GG.Telemetry.Host",
                f"{feed} identity differs")
        require(observation.get("producerPayloadEqual") is True, f"{feed} payload is unverified")
        require(observation.get("payloadSha256") == release["producerPayloadSha256"], f"{feed} payload differs")
    verifier = repository / "scripts/telemetry-host-release.py"
    regular(verifier, 1024 * 1024)
    subprocess.run([sys.executable, str(verifier), "verify", "--manifest", str(manifest), "--package", str(package),
                    "--feed", "prepared"], cwd=repository, check=True,
                   stdout=subprocess.DEVNULL)
    package_members(package)
    return release


def prepare_context(package: pathlib.Path, destination: pathlib.Path, binding: dict) -> None:
    require(not destination.exists(), "build context already exists")
    destination.mkdir(mode=0o700, parents=True)
    host = destination / "host"
    host.mkdir(mode=0o755)
    with zipfile.ZipFile(package) as archive:
        for member in package_members(package):
            relative = pathlib.PurePosixPath(member.filename).relative_to(TOOLS_PREFIX)
            target = host.joinpath(*relative.parts)
            target.parent.mkdir(mode=0o755, parents=True, exist_ok=True)
            with archive.open(member) as source, target.open("xb") as output:
                shutil.copyfileobj(source, output, 1024 * 1024)
            target.chmod(0o444)
    here = pathlib.Path(__file__).resolve().parent
    for name in ("Containerfile", "controlled-development.sh", "controlled-collector.sh"):
        regular(here / name, 1024 * 1024)
        shutil.copy2(here / name, destination / name, follow_symlinks=False)
    (destination / "binding.json").write_text(
        json.dumps(binding, sort_keys=True, separators=(",", ":")) + "\n", encoding="utf-8")


def podman_command(podman: pathlib.Path, root: pathlib.Path, runroot: pathlib.Path, *args: str) -> list[str]:
    return [str(podman), "--storage-driver=vfs", "--root", str(root), "--runroot", str(runroot), *args]


def create_command(podman: pathlib.Path, root: pathlib.Path, runroot: pathlib.Path, name: str, image: str,
                   mounts: list[tuple[pathlib.Path, str, bool]], entrypoint: str | None = None) -> list[str]:
    command = podman_command(
        podman, root, runroot, "create", "--name", name, "--pull=never", "--read-only", "--network=none",
        "--cap-drop=all", "--security-opt=no-new-privileges", "--pids-limit=64", "--memory=256m", "--cpus=1",
        "--http-proxy=false", "--userns=keep-id:uid=32768,gid=32768", "--user=32768:32768",
        "--tmpfs", "/tmp:rw,noexec,nosuid,nodev,size=16m")
    if entrypoint:
        command.extend(["--entrypoint", entrypoint])
    for source, target, writable in mounts:
        command.extend(["--volume", f"{source}:{target}:{'rw' if writable else 'ro'}"])
    command.append(image)
    return command


def run(command: list[str], timeout: int = 30) -> subprocess.CompletedProcess[str]:
    return subprocess.run(command, check=True, text=True, capture_output=True, timeout=timeout)


def capability_names(value: object, label: str) -> list[str]:
    require(isinstance(value, list) and all(isinstance(item, str) for item in value),
            f"{label} capability shape differs")
    require(len(value) == len(set(value)), f"{label} capability duplicates refused")
    return value


def inspect_capability_fence(value: dict) -> None:
    host = value["HostConfig"]
    added = capability_names(host.get("CapAdd"), "added")
    dropped = capability_names(host.get("CapDrop"), "dropped")
    effective_value = value.get("EffectiveCaps")
    effective = [] if effective_value is None else capability_names(effective_value, "effective")
    require(not added, "added capabilities refused")
    require(not effective, "effective capabilities refused")
    observed = set(dropped)
    all_sentinel = observed in ({"ALL"}, {"CAP_ALL"})
    # Podman 4 inspect expands "all" by subtracting the OCI bounding set from
    # these defaults. An exact expansion plus no additions proves it is empty.
    require(all_sentinel or observed == PODMAN_DEFAULT_CAPABILITIES,
            "capability bounding fence differs")


def inspect_container(value: dict, expected_mounts: set[tuple[str, bool]]) -> None:
    config, host = value["Config"], value["HostConfig"]
    observed = {(row["Destination"], bool(row["RW"])) for row in value["Mounts"]}
    require(observed == expected_mounts, "container mount topology differs")
    require(config["User"] == "32768:32768", "container user differs")
    require(host.get("UsernsMode") == "private", "container user namespace differs")
    mappings = host.get("IDMappings") or {}
    require("32768:0:1" in (mappings.get("UidMap") or [])
            and "32768:0:1" in (mappings.get("GidMap") or []), "keep-id mapping differs")
    require(host["NetworkMode"] == "none" and host["ReadonlyRootfs"] is True, "container isolation differs")
    require(host.get("PidMode") != "host" and host.get("UTSMode") != "host", "host namespace refused")
    inspect_capability_fence(value)
    require(any(item.startswith("no-new-privileges") for item in (host.get("SecurityOpt") or [])),
            "privilege fence differs")
    require(host["PidsLimit"] == 64 and host["Memory"] == 256 * 1024 * 1024
            and host["NanoCpus"] == 1_000_000_000, "resource fence differs")
    forbidden = ("NODE_OPTIONS=", "PYTHONPATH=", "LD_PRELOAD=", "HTTP_PROXY=", "HTTPS_PROXY=", "ALL_PROXY=")
    require(not any(item.upper().startswith(forbidden) for item in (config.get("Env") or [])),
            "host environment injection survived")


def qualify(args: argparse.Namespace) -> dict:
    podman = args.podman.resolve()
    regular(podman, 64 * 1024 * 1024)
    require(os.access(podman, os.X_OK), "Podman is not executable")
    require(RUN_ID.fullmatch(args.run_id) is not None, "run ID is malformed")
    context = args.context.resolve()
    binding = load_object(context / "binding.json")
    require(binding.get("schema") == "fsgg.telemetry.collector-build-binding/1", "build binding differs")
    state, evidence = args.state_root.resolve(), args.evidence.resolve()
    require(not state.exists() and not evidence.exists(), "qualification roots already exist")
    state.mkdir(mode=0o700, parents=True)
    evidence.mkdir(mode=0o700, parents=True)
    podman_root, podman_runroot = args.podman_root.resolve(), args.podman_runroot.resolve()
    podman_root.mkdir(mode=0o700, parents=True, exist_ok=True)
    podman_runroot.mkdir(mode=0o700, parents=True, exist_ok=True)
    suffix = args.run_id.lower()
    dev_image = f"localhost/fsgg-telemetry-controlled-development:{suffix}"
    collector_image = f"localhost/fsgg-telemetry-collector:{suffix}"
    dev_name, collector_name = f"fsgg-telemetry-dev-{suffix}", f"fsgg-telemetry-collector-{suffix}"
    directories = {name: state / name for name in ("config", "store")}
    for path in directories.values():
        path.mkdir(mode=0o700)
    native_source = directories["config"] / "native-codex-home"
    native_evidence = directories["config"] / "native-evidence"
    native_executable = directories["config"] / "native-executable"
    for path in (native_source, native_evidence, native_executable):
        path.mkdir(mode=0o700)
    (directories["config"] / "controlled-boundary.json").write_text(
        '{"schema":"fsgg.telemetry.collector-controlled-boundary/1","claim":"topology-only"}\n',
        encoding="utf-8")
    (directories["config"] / "credential-boundary.sentinel").write_text(
        "no credential is used by controlled qualification\n", encoding="utf-8")
    reader = native_executable / "controlled-reader"
    reader.write_text("#!/bin/sh\nexit 2\n", encoding="utf-8")
    reader.chmod(0o500)
    for path in (directories["config"] / "controlled-boundary.json",
                 directories["config"] / "credential-boundary.sentinel"):
        path.chmod(0o600)
    try:
        common = ["build", "--quiet", "--pull=never", "--network=none", "--timestamp=0",
                  "--file", str(context / "Containerfile")]
        run(podman_command(podman, podman_root, podman_runroot, *common, "--target", "controlled-development",
                           "--tag", dev_image, str(context)), timeout=300)
        build = [*common, "--target", "collector", "--tag", collector_image]
        for key, name in (("version", "HOST_VERSION"), ("sourceSha", "HOST_SOURCE_SHA"),
                          ("packageSha256", "HOST_PACKAGE_SHA256"), ("manifestSha256", "HOST_MANIFEST_SHA256"),
                          ("journalSha256", "HOST_JOURNAL_SHA256")):
            build.extend(["--build-arg", f"{name}={binding[key]}"])
        build.append(str(context))
        run(podman_command(podman, podman_root, podman_runroot, *build), timeout=300)
        run(create_command(podman, podman_root, podman_runroot, dev_name, dev_image,
                           [(native_source, "/native-source", True)]))
        dev_inspect = json.loads(run(podman_command(podman, podman_root, podman_runroot,
                                                    "inspect", dev_name)).stdout)[0]
        inspect_container(dev_inspect, {("/native-source", True)})
        run(podman_command(podman, podman_root, podman_runroot, "start", "--attach", dev_name))
        mounts = [(directories["config"], "/collector-config", False),
                  (directories["store"], "/collector-store", True),
                  (native_evidence, "/collector-config/native-evidence", True),
                  (native_source, "/collector-config/native-codex-home", False)]
        run(create_command(podman, podman_root, podman_runroot, collector_name, collector_image, mounts,
                           "/opt/fsgg/controlled-collector"))
        inspected = json.loads(run(podman_command(podman, podman_root, podman_runroot,
                                                  "inspect", collector_name)).stdout)[0]
        inspect_container(inspected, {(target, writable) for _, target, writable in mounts})
        run(podman_command(podman, podman_root, podman_runroot, "start", "--attach", collector_name))
        first = (native_evidence / "controlled-custody.json").read_bytes()
        run(podman_command(podman, podman_root, podman_runroot, "start", "--attach", collector_name))
        second = (native_evidence / "controlled-custody.json").read_bytes()
        require(first == second, "collector restart changed custody receipt")
        custody = json.loads(first)
        require(custody.get("verdict") == "controlled-topology-only", "controlled verdict differs")
        require(custody.get("nativeAccessQualified") is False, "controlled receipt claimed native access")
        image = json.loads(run(podman_command(podman, podman_root, podman_runroot,
                                              "image", "inspect", collector_image)).stdout)[0]
        image_config = image["Config"]
        labels = image_config["Labels"]
        require(image_config["User"] == "32768:32768", "collector image user differs")
        require(image_config["Entrypoint"] == ["/usr/bin/dotnet", "/opt/fsgg/telemetry-host/FS.GG.Telemetry.Host.dll"],
                "collector entrypoint differs")
        require(not image_config.get("Volumes"), "collector image declares volumes")
        require(labels["org.fsgg.telemetry.collector.activation"] == "disabled-before-readiness",
                "collector activation label differs")
        require(labels["org.opencontainers.image.revision"] == binding["sourceSha"], "collector revision label differs")
        (evidence / "controlled-custody.json").write_bytes(first)
        return {
            "schema": "fsgg.telemetry.collector-container-qualification/1",
            "verdict": "controlled-topology-passed",
            "strictRuntimeFences": True,
            "restartReceiptStable": True,
            "developmentMounts": ["/native-source:rw"],
            "collectorMounts": [f"{target}:{'rw' if writable else 'ro'}" for _, target, writable in mounts],
            "hostVersion": binding["version"], "hostSourceSha": binding["sourceSha"],
            "hostPackageSha256": binding["packageSha256"], "hostManifestSha256": binding["manifestSha256"],
            "hostJournalSha256": binding["journalSha256"], "collectorImageId": image["Id"],
            "controlledSourceSha256": custody["sourceSha256"], "nativeAccessQualified": False,
            "modelSupportObserved": False, "captureApplied": False, "activationAuthorized": False,
        }
    finally:
        for name in (collector_name, dev_name):
            subprocess.run(podman_command(podman, podman_root, podman_runroot, "rm", "--force", name),
                           text=True, capture_output=True, timeout=30)
        remaining = run(podman_command(podman, podman_root, podman_runroot, "ps", "--all", "--format", "{{.Names}}"))
        require(not ({collector_name, dev_name} & set(remaining.stdout.splitlines())), "qualified container cleanup failed")
        if state.exists():
            shutil.rmtree(state)


def add_selection(parser: argparse.ArgumentParser) -> None:
    for name in ("version", "source-sha", "package-sha256", "manifest-sha256", "journal-sha256"):
        parser.add_argument("--" + name, required=True)


def cli() -> argparse.ArgumentParser:
    root = argparse.ArgumentParser(description=__doc__)
    commands = root.add_subparsers(dest="command", required=True)
    add_selection(commands.add_parser("selection-preflight"))
    preflight = commands.add_parser("preflight")
    preflight.add_argument("--repository", required=True, type=pathlib.Path)
    preflight.add_argument("--package", required=True, type=pathlib.Path)
    preflight.add_argument("--manifest", required=True, type=pathlib.Path)
    preflight.add_argument("--journal", required=True, type=pathlib.Path)
    preflight.add_argument("--output", required=True, type=pathlib.Path)
    add_selection(preflight)
    context = commands.add_parser("prepare-context")
    context.add_argument("--package", required=True, type=pathlib.Path)
    context.add_argument("--preflight", required=True, type=pathlib.Path)
    context.add_argument("--output", required=True, type=pathlib.Path)
    qualification = commands.add_parser("qualify")
    for name in ("podman", "podman-root", "podman-runroot", "context", "state-root", "evidence", "output"):
        qualification.add_argument("--" + name, required=True, type=pathlib.Path)
    qualification.add_argument("--run-id", required=True)
    return root


def main() -> int:
    args = cli().parse_args()
    try:
        if args.command == "selection-preflight":
            validate_selection(args.version, args.source_sha, args.package_sha256, args.manifest_sha256,
                               args.journal_sha256)
            print('{"schema":"fsgg.telemetry.collector-selection-preflight/1","status":"accepted"}')
        elif args.command == "preflight":
            release = verify_release(args.repository.resolve(), args.package.resolve(), args.manifest.resolve(),
                                     args.journal.resolve(), args.version, args.source_sha, args.package_sha256,
                                     args.manifest_sha256, args.journal_sha256)
            receipt = {"schema": "fsgg.telemetry.collector-build-binding/1", "version": args.version,
                       "sourceSha": args.source_sha, "packageSha256": args.package_sha256,
                       "manifestSha256": args.manifest_sha256, "journalSha256": args.journal_sha256,
                       "producerPayloadSha256": release["producerPayloadSha256"], "captureSchema": 2,
                       "nativeAccessQualified": False, "modelSupportObserved": False}
            args.output.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
            args.output.write_text(json.dumps(receipt, sort_keys=True, separators=(",", ":")) + "\n",
                                   encoding="utf-8")
        elif args.command == "prepare-context":
            binding = load_object(args.preflight.resolve())
            require(binding.get("schema") == "fsgg.telemetry.collector-build-binding/1", "preflight differs")
            prepare_context(args.package.resolve(), args.output.resolve(), binding)
        elif args.command == "qualify":
            result = qualify(args)
            require(not args.state_root.exists(), "qualification state cleanup failed")
            result["stateRemoved"] = True
            result["containersRemoved"] = True
            args.output.write_text(json.dumps(result, sort_keys=True, separators=(",", ":")) + "\n",
                                   encoding="utf-8")
        return 0
    except (Refusal, OSError, ValueError, KeyError, json.JSONDecodeError, zipfile.BadZipFile,
            subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
        print(f"telemetry collector refused: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
