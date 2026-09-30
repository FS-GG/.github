#!/usr/bin/env python3
"""Construct and inspect the fixed .8b container topology without running it."""

from __future__ import annotations

import hashlib
import pathlib
import re
import stat
from dataclasses import dataclass


NATIVE_NETWORK = "fsgg-native-private-v1"
UPLINK_NETWORK = "fsgg-native-uplink-v1"
COLLECTOR_NETWORK = "fsgg-collector-private-v1"
NATIVE_MOUNT = "/qualification/native"
FIXED_ENV = (
    "HOME=/qualification/native",
    "CODEX_HOME=/qualification/native/.codex",
    "HTTPS_PROXY=http://native-egress:3128",
    "NO_PROXY=localhost,127.0.0.1,[::1]",
)
HEX64 = re.compile(r"^[0-9a-f]{64}$")


class Refusal(Exception):
    pass


def require(condition: bool, detail: str) -> None:
    if not condition:
        raise Refusal(detail)


def common_create(name: str, image: str, memory: str, cpus: str, network: str, alias: str) -> list[str]:
    image = normalize_image_id(image)
    return [
        "podman", "create", "--name", name, "--pull=never", "--read-only",
        "--cap-drop=all", "--security-opt=no-new-privileges", "--pids-limit=128",
        "--memory", memory, "--cpus", cpus, "--userns=keep-id:uid=32768,gid=32768",
        "--user=32768:32768", "--http-proxy=false", "--network", f"{network}:alias={alias}",
        "--tmpfs", "/tmp:rw,noexec,nosuid,nodev,size=64m", image,
    ]


def network_create_commands() -> list[list[str]]:
    return [
        ["podman", "network", "create", "--internal", NATIVE_NETWORK],
        ["podman", "network", "create", UPLINK_NETWORK],
        ["podman", "network", "create", "--internal", COLLECTOR_NETWORK],
    ]


def normalize_image_id(value: str) -> str:
    normalized = value.removeprefix("sha256:")
    require(HEX64.fullmatch(normalized) is not None and set(normalized) != {"0"}, "image-id-refused")
    return "sha256:" + normalized


def native_create(image: str, native_volume: pathlib.Path, run_nonce: str) -> list[str]:
    require(re.fullmatch(r"[a-z0-9][a-z0-9-]{7,63}", run_nonce) is not None, "run-nonce-refused")
    command = common_create("fsgg-native-development", image, "2g", "2", NATIVE_NETWORK, "native-development")
    insertion = len(command) - 1
    additions = ["--volume", f"{native_volume.resolve()}:{NATIVE_MOUNT}:rw,rprivate"]
    for item in FIXED_ENV:
        additions.extend(["--env", item])
    command[insertion:insertion] = additions
    command.extend(["--run-nonce", run_nonce])
    return command


def readonly_probe_create(image: str, native_volume: pathlib.Path, run_nonce: str) -> list[str]:
    require(re.fullmatch(r"[a-z0-9][a-z0-9-]{7,63}", run_nonce) is not None, "run-nonce-refused")
    command = common_create("fsgg-native-readonly-probe", image, "1g", "1", "none", "unused")
    network_index = command.index("--network")
    command[network_index:network_index + 2] = ["--network", "none"]
    insertion = len(command) - 1
    command[insertion:insertion] = [
        "--volume", f"{native_volume.resolve()}:{NATIVE_MOUNT}:ro,rprivate",
        "--env", "HOME=/qualification/native",
        "--env", "CODEX_HOME=/qualification/native/.codex",
        "--tmpfs", "/qualification/readback-output:rw,noexec,nosuid,nodev,size=8m,mode=0700",
    ]
    command.extend(["--run-nonce", run_nonce])
    return command


def egress_create(image: str) -> tuple[list[str], list[str]]:
    create = common_create("fsgg-native-egress", image, "256m", "1", NATIVE_NETWORK, "native-egress")
    connect = ["podman", "network", "connect", "--alias", "native-egress-uplink", UPLINK_NETWORK,
               "fsgg-native-egress"]
    return create, connect


def collector_create(image: str, qualification: pathlib.Path) -> list[str]:
    command = common_create("fsgg-native-collector", image, "1g", "1", COLLECTOR_NETWORK, "native-collector")
    insertion = len(command) - 1
    mounts = [
        (qualification / "host.json", "/qualification/host.json", "ro"),
        (qualification / "native", NATIVE_MOUNT, "ro"),
        (qualification / "evidence", "/qualification/evidence", "rw"),
        (qualification / "store", "/qualification/store", "rw"),
        (qualification / "tls", "/qualification/tls", "ro"),
        (qualification / "credentials", "/qualification/credentials", "ro"),
    ]
    arguments: list[str] = []
    for source, target, mode in mounts:
        arguments.extend(["--volume", f"{source.resolve()}:{target}:{mode},rprivate"])
    command[insertion:insertion] = arguments
    return command


def inspect_networks(value: dict, expected: set[str]) -> None:
    networks = set((value.get("NetworkSettings", {}).get("Networks") or {}).keys())
    require(networks == expected, "container-network-set-refused")
    host = value["HostConfig"]
    require(host.get("NetworkMode") not in {"host", "default", "bridge", "slirp4netns", "pasta"},
            "direct-network-route-refused")


def inspect_native(value: dict) -> None:
    inspect_networks(value, {NATIVE_NETWORK})
    host, config = value["HostConfig"], value["Config"]
    require(host.get("ReadonlyRootfs") is True and host.get("PidsLimit") == 128, "native-runtime-fence-refused")
    require(host.get("Memory") == 2 * 1024 ** 3 and host.get("NanoCpus") == 2_000_000_000,
            "native-resource-fence-refused")
    require(config.get("User") == "32768:32768", "native-user-refused")
    environment = dict(item.split("=", 1) for item in (config.get("Env") or []) if "=" in item)
    require(all(environment.get(item.split("=", 1)[0]) == item.split("=", 1)[1] for item in FIXED_ENV),
            "native-environment-refused")
    forbidden = {"HTTP_PROXY", "ALL_PROXY", "FSGG_TELEMETRY_CONFIG", "FSGG_TELEMETRY_STORE",
                 "FSGG_TELEMETRY_NATIVE_COLLECTOR_CONFIG", "GITHUB_TOKEN", "GH_TOKEN"}
    require(not (forbidden & set(environment)), "native-sensitive-environment-refused")
    mounts = {(row["Destination"], bool(row["RW"])) for row in value.get("Mounts", [])}
    require(mounts == {(NATIVE_MOUNT, True)}, "native-mount-custody-refused")
    require(not host.get("CapAdd") and set(host.get("CapDrop") or []) in ({"ALL"}, {"CAP_ALL"}),
            "native-capability-fence-refused")
    require(host.get("UsernsMode") == "private" and host.get("PidMode") != "host"
            and host.get("UTSMode") != "host", "native-namespace-fence-refused")
    require(any(item.startswith("no-new-privileges") for item in (host.get("SecurityOpt") or [])),
            "native-privilege-fence-refused")


def inspect_egress(value: dict) -> None:
    inspect_networks(value, {NATIVE_NETWORK, UPLINK_NETWORK})
    require(not value.get("Mounts"), "egress-mount-refused")


@dataclass(frozen=True)
class CustodySnapshot:
    device: int
    inode: int
    digest: str
    files: int


def snapshot_native_volume(root: pathlib.Path) -> CustodySnapshot:
    info = root.lstat()
    require(stat.S_ISDIR(info.st_mode) and not root.is_symlink(), "native-volume-root-refused")
    digest = hashlib.sha256()
    count = 0
    for path in sorted(root.rglob("*")):
        relative = path.relative_to(root).as_posix()
        entry = path.lstat()
        require(not path.is_symlink(), "native-volume-symlink-refused")
        if path.is_dir():
            continue
        require(stat.S_ISREG(entry.st_mode), "native-volume-entry-refused")
        count += 1
        require(count <= 10000 and entry.st_size <= 64 * 1024 * 1024, "native-volume-bound-refused")
        digest.update(relative.encode("utf-8") + b"\0")
        with path.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(chunk)
    return CustodySnapshot(info.st_dev, info.st_ino, digest.hexdigest(), count)


def require_same_original_volume(before: CustodySnapshot, after: CustodySnapshot) -> None:
    require((before.device, before.inode) == (after.device, after.inode), "native-volume-replaced")


def require_collection_handoff(development_running: bool, receiver_running: bool) -> None:
    require(not development_running, "development-writer-still-running")
    require(not receiver_running, "receiver-lock-still-held")
