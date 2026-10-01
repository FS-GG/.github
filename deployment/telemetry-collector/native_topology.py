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
PRODUCER_CONFIG_MOUNT = "/qualification/native/telemetry/roadmap.json"
PRODUCER_SPOOL_MOUNT = "/qualification/native/telemetry/spool"
RECEIVER_ALIAS = "native-receiver"
RECEIVER_PORT = 7443
RECEIVER_ENDPOINT = f"https://{RECEIVER_ALIAS}:{RECEIVER_PORT}/"
PRODUCER_CREDENTIAL_REFERENCE = "native-prospective-v1"
PRODUCER_CREDENTIAL_ENV = "FSGG_TELEMETRY_CREDENTIAL_NATIVE_PROSPECTIVE_V1"
FIXED_ENV = (
    "HOME=/qualification/native",
    "CODEX_HOME=/qualification/native/.codex",
    "HTTPS_PROXY=http://native-egress:3128",
    f"NO_PROXY=localhost,127.0.0.1,[::1],{RECEIVER_ALIAS}",
)
HEX64 = re.compile(r"^[0-9a-f]{64}$")
RECEIVER_MOUNTS = {
    "/qualification/host.json": False,
    "/qualification/host.json.native-collector.json": False,
    NATIVE_MOUNT: False,
    "/qualification/evidence": True,
    "/qualification/store": True,
    "/qualification/tls": False,
    "/qualification/credentials": False,
}
RECEIVER_INSPECTION_REFUSALS = frozenset({
    "container-network-set-refused", "direct-network-route-refused",
    "receiver-runtime-fence-refused", "receiver-resource-fence-refused",
    "receiver-user-refused", "receiver-environment-route-refused",
    "receiver-mount-custody-refused", "receiver-public-route-refused",
    "receiver-command-refused",
})


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


def native_create(
    image: str,
    native_volume: pathlib.Path,
    producer_config: pathlib.Path,
    producer_spool: pathlib.Path,
    run_nonce: str,
) -> list[str]:
    require(re.fullmatch(r"[a-z0-9][a-z0-9-]{7,63}", run_nonce) is not None, "run-nonce-refused")
    command = common_create("fsgg-native-development", image, "2g", "2", NATIVE_NETWORK, "native-development")
    insertion = len(command) - 1
    additions = [
        "--volume", f"{native_volume.resolve()}:{NATIVE_MOUNT}:rw,rprivate",
        "--volume", f"{producer_config.resolve()}:{PRODUCER_CONFIG_MOUNT}:ro,rprivate",
        "--volume", f"{producer_spool.resolve()}:{PRODUCER_SPOOL_MOUNT}:rw,rprivate",
        # Podman inherits this one named value from its own environment.  The value is never
        # rendered into argv and the native driver must expose it only to the telemetry engine.
        "--env", PRODUCER_CREDENTIAL_ENV,
    ]
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
        # Podman 4.9 rejects uid=/gid= in --tmpfs before create.  Its supported U
        # option maps tmpfs ownership to the already fixed container user.
        "--tmpfs", "/qualification/readback-output:rw,noexec,nosuid,nodev,size=8m,mode=0700,U",
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
        (qualification / "host.json.native-collector.json",
         "/qualification/host.json.native-collector.json", "ro"),
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
    command.extend(["serve", "--config", "/qualification/host.json"])
    return command


def receiver_connect_command() -> list[str]:
    return [
        "podman", "network", "connect", "--alias", RECEIVER_ALIAS,
        NATIVE_NETWORK, "fsgg-native-collector",
    ]


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
    credentials = {name for name in environment if name.startswith("FSGG_TELEMETRY_CREDENTIAL_")}
    require(credentials == {PRODUCER_CREDENTIAL_ENV}
            and bool(environment.get(PRODUCER_CREDENTIAL_ENV)), "native-producer-credential-refused")
    mounts = {(row["Destination"], bool(row["RW"])) for row in value.get("Mounts", [])}
    require(mounts == {
        (NATIVE_MOUNT, True),
        (PRODUCER_CONFIG_MOUNT, False),
        (PRODUCER_SPOOL_MOUNT, True),
    }, "native-mount-custody-refused")
    require(not host.get("CapAdd") and set(host.get("CapDrop") or []) in ({"ALL"}, {"CAP_ALL"}),
            "native-capability-fence-refused")
    require(host.get("UsernsMode") == "private" and host.get("PidMode") != "host"
            and host.get("UTSMode") != "host", "native-namespace-fence-refused")
    require(any(item.startswith("no-new-privileges") for item in (host.get("SecurityOpt") or [])),
            "native-privilege-fence-refused")


def inspect_egress(value: dict) -> None:
    inspect_networks(value, {NATIVE_NETWORK, UPLINK_NETWORK})
    require(not value.get("Mounts"), "egress-mount-refused")


def receiver_inspection_projection(value: dict, refusal: str) -> dict:
    """Project only bounded, non-secret receiver guard facts for sealed evidence."""
    host = value.get("HostConfig") if isinstance(value.get("HostConfig"), dict) else {}
    config = value.get("Config") if isinstance(value.get("Config"), dict) else {}
    settings = value.get("NetworkSettings") if isinstance(value.get("NetworkSettings"), dict) else {}
    networks = settings.get("Networks") if isinstance(settings.get("Networks"), dict) else {}
    network_names = set(networks)
    expected_networks = {NATIVE_NETWORK, COLLECTOR_NETWORK}
    mode = host.get("NetworkMode")
    if mode == COLLECTOR_NETWORK:
        mode_category = "expected"
    elif mode in {"host", "default", "bridge", "slirp4netns", "pasta"}:
        mode_category = "direct"
    elif isinstance(mode, str) and mode:
        mode_category = "other"
    else:
        mode_category = "missing"

    environment = config.get("Env") if isinstance(config.get("Env"), list) else []
    environment_names = [item.split("=", 1)[0] for item in environment
                         if isinstance(item, str) and "=" in item]
    mounts = value.get("Mounts") if isinstance(value.get("Mounts"), list) else []
    mount_states = {}
    for destination, expected_writable in RECEIVER_MOUNTS.items():
        rows = [row for row in mounts if isinstance(row, dict) and row.get("Destination") == destination]
        if not rows:
            mount_states[destination] = "missing"
        elif len(rows) != 1 or type(rows[0].get("RW")) is not bool:
            mount_states[destination] = "ambiguous"
        else:
            writable = rows[0]["RW"]
            mount_states[destination] = ("expected" if writable == expected_writable
                                         else "writability-differs")
    unexpected_mounts = sum(1 for row in mounts
                            if not isinstance(row, dict) or row.get("Destination") not in RECEIVER_MOUNTS)

    cap_drop = host.get("CapDrop") if isinstance(host.get("CapDrop"), list) else []
    cap_drop_set = {item for item in cap_drop if isinstance(item, str)}
    if cap_drop_set == {"ALL"}:
        cap_drop_category = "all"
    elif cap_drop_set == {"CAP_ALL"}:
        cap_drop_category = "cap-all"
    elif cap_drop_set and all(re.fullmatch(r"CAP_[A-Z0-9_]+", item) for item in cap_drop_set):
        cap_drop_category = "expanded"
    elif not cap_drop_set:
        cap_drop_category = "empty"
    else:
        cap_drop_category = "other"

    def bounded_integer(candidate, maximum):
        return candidate if type(candidate) is int and -1 <= candidate <= maximum else None

    user = config.get("User")
    if user == "32768:32768":
        user_category = "expected"
    elif isinstance(user, str) and re.fullmatch(r"[0-9]+(?::[0-9]+)?", user):
        user_category = "numeric-other"
    elif user is None:
        user_category = "missing"
    else:
        user_category = "other"
    security = host.get("SecurityOpt") if isinstance(host.get("SecurityOpt"), list) else []
    effective = value.get("EffectiveCaps") if isinstance(value.get("EffectiveCaps"), list) else []
    bounding = value.get("BoundingCaps") if isinstance(value.get("BoundingCaps"), list) else []
    tmpfs = host.get("Tmpfs") if isinstance(host.get("Tmpfs"), dict) else {}
    return {
        "failureCode": (refusal if refusal in RECEIVER_INSPECTION_REFUSALS
                        else "receiver-inspection-refused"),
        "network": {
            "count": min(len(network_names), 64),
            "collectorPresent": COLLECTOR_NETWORK in network_names,
            "nativePresent": NATIVE_NETWORK in network_names,
            "unexpectedCount": min(len(network_names - expected_networks), 64),
            "modeCategory": mode_category,
        },
        "runtime": {
            "readonlyRootfs": host.get("ReadonlyRootfs") if type(host.get("ReadonlyRootfs")) is bool else None,
            "pidsLimit": bounded_integer(host.get("PidsLimit"), 1 << 30),
            "memory": bounded_integer(host.get("Memory"), 1 << 50),
            "nanoCpus": bounded_integer(host.get("NanoCpus"), 1 << 50),
            "userCategory": user_category,
        },
        "environment": {
            "entryCount": min(len(environment), 4096),
            "proxyPresent": any(name.endswith("_PROXY") for name in environment_names),
            "credentialPresent": any(name.startswith("FSGG_TELEMETRY_CREDENTIAL_")
                                     for name in environment_names),
        },
        "storage": {
            "mountCount": min(len(mounts), 4096),
            "unexpectedMountCount": min(unexpected_mounts, 4096),
            "expectedMounts": mount_states,
            "tmpfsCount": min(len(tmpfs), 64),
            "temporaryMountPresent": "/tmp" in tmpfs,
        },
        "privilege": {
            "capAddEmpty": not bool(host.get("CapAdd")),
            "capDropCategory": cap_drop_category,
            "capDropCount": min(len(cap_drop_set), 512),
            "effectiveCapabilityCount": min(len(effective), 512),
            "boundingCapabilityCount": min(len(bounding), 512),
            "noNewPrivileges": any(isinstance(item, str) and item.startswith("no-new-privileges")
                                   for item in security),
            "privileged": host.get("Privileged") if type(host.get("Privileged")) is bool else None,
            "usernsPrivate": host.get("UsernsMode") == "private",
            "pidHost": host.get("PidMode") == "host",
            "utsHost": host.get("UTSMode") == "host",
        },
        "route": {
            "portBindingsPresent": bool(host.get("PortBindings")),
            "commandExact": config.get("Cmd") == ["serve", "--config", "/qualification/host.json"],
        },
    }


def inspect_receiver(value: dict) -> None:
    inspect_networks(value, {NATIVE_NETWORK, COLLECTOR_NETWORK})
    host, config = value["HostConfig"], value["Config"]
    require(host.get("ReadonlyRootfs") is True and host.get("PidsLimit") == 128,
            "receiver-runtime-fence-refused")
    require(host.get("Memory") == 1024 ** 3 and host.get("NanoCpus") == 1_000_000_000,
            "receiver-resource-fence-refused")
    require(config.get("User") == "32768:32768", "receiver-user-refused")
    environment = dict(item.split("=", 1) for item in (config.get("Env") or []) if "=" in item)
    require(not any(name.endswith("_PROXY") or name.startswith("FSGG_TELEMETRY_CREDENTIAL_")
                    for name in environment), "receiver-environment-route-refused")
    mounts = {(row["Destination"], bool(row["RW"])) for row in value.get("Mounts", [])}
    require(mounts == set(RECEIVER_MOUNTS.items()), "receiver-mount-custody-refused")
    require(not host.get("PortBindings") and host.get("NetworkMode") != "host",
            "receiver-public-route-refused")
    require(config.get("Cmd") == ["serve", "--config", "/qualification/host.json"],
            "receiver-command-refused")


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
