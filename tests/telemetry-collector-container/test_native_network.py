#!/usr/bin/env python3
import importlib.util
import asyncio
import hashlib
import json
import pathlib
import sys
import tempfile
import unittest
from unittest import mock


ROOT = pathlib.Path(__file__).resolve().parents[2]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


gate = load("native_egress_gate", ROOT / "deployment/telemetry-collector/native_egress_gate.py")
topology = load("native_topology", ROOT / "deployment/telemetry-collector/native_topology.py")
POLICY_PATH = ROOT / "deployment/telemetry-collector/native-network-policy.json"


class NativeNetworkTests(unittest.TestCase):
    def setUp(self):
        self.policy = gate.load_policy(POLICY_PATH)

    def valid_native_inspection(self):
        return {
            "Config": {"User": "32768:32768", "Env": [
                *topology.FIXED_ENV, topology.PRODUCER_CREDENTIAL_ENV + "=fixture"]},
            "HostConfig": {"NetworkMode": "bridge", "ReadonlyRootfs": True,
                           "PidsLimit": 128, "Memory": 2 * 1024 ** 3,
                           "NanoCpus": 2_000_000_000, "Privileged": False,
                           "CapAdd": [], "CapDrop": ["ALL"], "UsernsMode": "private",
                           "PidMode": "private", "UTSMode": "private",
                           "SecurityOpt": ["no-new-privileges"]},
            "EffectiveCaps": [], "BoundingCaps": [],
            "NetworkSettings": {"Networks": {topology.NATIVE_NETWORK: {}}},
            "Mounts": [{"Destination": path, "RW": writable}
                       for path, writable in topology.NATIVE_MOUNTS.items()],
        }

    def test_policy_accepts_only_exact_connect_authorities(self):
        for host in ("chatgpt.com", "auth.openai.com"):
            request = f"CONNECT {host}:443 HTTP/1.1\r\nHost: {host}:443\r\n\r\n".encode()
            self.assertEqual(host, gate.parse_connect_request(request, self.policy))
        for first_line in (
            "CONNECT api.openai.com:443 HTTP/1.1", "CONNECT chatgpt.com:80 HTTP/1.1",
            "CONNECT 127.0.0.1:443 HTTP/1.1", "CONNECT chatgpt.com.:443 HTTP/1.1",
            "GET chatgpt.com:443 HTTP/1.1", "CONNECT CHATGPT.COM:443 HTTP/1.1",
        ):
            with self.assertRaises(gate.Refusal):
                gate.parse_connect_request((first_line + "\r\n\r\n").encode(), self.policy)

    def test_dns_rebinding_private_metadata_and_mixed_answers_are_refused(self):
        def row(address):
            return (2, 1, 6, "", (address, 443))
        public = row("8.8.8.8")
        self.assertEqual([gate.ipaddress.ip_address("8.8.8.8")],
                         gate.validate_resolved_addresses([public], self.policy))
        for addresses in ([row("127.0.0.1")], [row("169.254.169.254")],
                          [row("10.0.0.1")], [public, row("192.168.1.1")], [row("::1")]):
            with self.assertRaisesRegex(gate.Refusal, "dns-address-refused"):
                gate.validate_resolved_addresses(addresses, self.policy)

    def test_policy_shape_and_bounds_fail_closed(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = pathlib.Path(temporary) / "policy.json"
            value = json.loads(POLICY_PATH.read_text())
            value["allowedHosts"].append("api.openai.com")
            path.write_text(json.dumps(value))
            with self.assertRaisesRegex(gate.Refusal, "policy-hosts-refused"):
                gate.load_policy(path)
            value = json.loads(POLICY_PATH.read_text())
            value["maximumConnectionSeconds"] = 0
            path.write_text(json.dumps(value))
            with self.assertRaisesRegex(gate.Refusal, "policy-bound-refused"):
                gate.load_policy(path)

    def test_topology_gives_only_proxy_route_and_disjoint_mounts(self):
        native = topology.native_create(
            "sha256:" + "a" * 64, pathlib.Path("/private/native"),
            pathlib.Path("/private/producer/roadmap.json"), pathlib.Path("/private/producer/spool"),
            "nonce-001")
        rendered = " ".join(native)
        self.assertIn("fsgg-native-private-v1:alias=native-development", rendered)
        self.assertIn("/private/native:/qualification/native:rw,rprivate", rendered)
        self.assertIn("HTTPS_PROXY=http://native-egress:3128", rendered)
        self.assertIn("NO_PROXY=localhost,127.0.0.1,[::1],native-receiver", rendered)
        self.assertIn("/private/producer/roadmap.json:/qualification/native/telemetry/roadmap.json:ro,rprivate", rendered)
        self.assertIn("/private/producer/spool:/qualification/native/telemetry/spool:rw,rprivate", rendered)
        self.assertIn("--env FSGG_TELEMETRY_CREDENTIAL_NATIVE_PROSPECTIVE_V1", rendered)
        for absent in ("/qualification/store", "/qualification/evidence", "/qualification/host.json",
                       "/qualification/credentials", "/qualification/tls", "api.openai.com", "--network host"):
            self.assertNotIn(absent, rendered)
        create, connect = topology.egress_create("sha256:" + "b" * 64)
        self.assertIn("fsgg-native-private-v1:alias=native-egress", " ".join(create))
        self.assertEqual("fsgg-native-uplink-v1", connect[-2])
        collector = " ".join(topology.collector_create("sha256:" + "c" * 64, pathlib.Path("/private/q")))
        self.assertIn("--memory 1g --cpus 1", collector)
        self.assertIn("/private/q/native:/qualification/native:ro,rprivate", collector)
        self.assertIn("/private/q/host.json.native-collector.json:/qualification/host.json.native-collector.json:ro,rprivate", collector)
        self.assertTrue(collector.endswith("serve --config /qualification/host.json"))
        self.assertEqual(
            ["podman", "network", "connect", "--alias", "native-receiver",
             "fsgg-native-private-v1", "fsgg-native-collector"],
            topology.receiver_connect_command())
        commands = topology.network_create_commands()
        self.assertIn("--internal", commands[0])
        self.assertNotIn("--internal", commands[1])
        self.assertIn("--internal", commands[2])
        probe = " ".join(topology.readonly_probe_create(
            "sha256:" + "d" * 64, pathlib.Path("/private/native"), "readonlynonce1"))
        self.assertIn("--network none", probe)
        self.assertIn("/private/native:/qualification/native:ro,rprivate", probe)
        self.assertIn("/qualification/readback-output:rw,noexec,nosuid,nodev,size=8m,mode=0700,U", probe)
        tmpfs = topology.readonly_probe_create(
            "sha256:" + "d" * 64, pathlib.Path("/private/native"), "readonlynonce1")
        output_options = tmpfs[tmpfs.index("--tmpfs", tmpfs.index("--tmpfs") + 1) + 1]
        self.assertNotIn("uid=", output_options)
        self.assertNotIn("gid=", output_options)
        self.assertIn("--run-nonce readonlynonce1", probe)
        self.assertEqual(1, native.count("--cap-drop=all"))
        self.assertFalse(any(item == "--privileged" or item.startswith("--cap-add")
                             for item in native))

    def test_inspection_refuses_direct_route_extra_mount_and_environment(self):
        value = self.valid_native_inspection()
        topology.inspect_native(value)
        value["NetworkSettings"]["Networks"]["bridge"] = {}
        with self.assertRaisesRegex(topology.Refusal, "network-set"):
            topology.inspect_native(value)
        value["NetworkSettings"]["Networks"].pop("bridge")
        value["Mounts"].append({"Destination": "/qualification/store", "RW": False})
        with self.assertRaisesRegex(topology.Refusal, "mount-custody"):
            topology.inspect_native(value)
        value["Mounts"].pop()
        value["Config"]["Env"].append("FSGG_TELEMETRY_CREDENTIAL_OTHER=bad")
        with self.assertRaisesRegex(topology.Refusal, "producer-credential"):
            topology.inspect_native(value)

    def test_native_capability_metadata_accepts_only_complete_zero_capability_shapes(self):
        accepted = (
            (["CAP_CHOWN", "CAP_SETUID"], None, None),
            (["CAP_NET_RAW"], [], []),
            (["CAP_CHOWN", "CAP_SETUID", "CAP_NET_RAW"], None, []),
            (["ALL"], None, None),
            (["CAP_ALL"], [], []),
        )
        for cap_drop, effective, bounding in accepted:
            with self.subTest(accepted=(cap_drop, effective, bounding)):
                value = self.valid_native_inspection()
                value["HostConfig"]["CapDrop"] = cap_drop
                value["EffectiveCaps"], value["BoundingCaps"] = effective, bounding
                topology.inspect_native(value)

        malformed_zero_sets = (False, 0, "", {}, (), [[]], ["CAP_CHOWN"])
        refused_changes = [
            ("HostConfig", "CapAdd", candidate)
            for candidate in (None, False, 0, "", {}, (), [[]], [1], ["CAP_CHOWN"])
        ] + [
            ("HostConfig", "Privileged", candidate)
            for candidate in (True, None, 0, "false")
        ] + [
            ("root", key, candidate)
            for key in ("EffectiveCaps", "BoundingCaps")
            for candidate in malformed_zero_sets
        ] + [
            ("HostConfig", "CapDrop", candidate) for candidate in (
                None, [], False, 0, "", {}, (), ["CAP_CHOWN", "CAP_CHOWN"],
                ["ALL", "CAP_CHOWN"], ["CAP_ALL", "CAP_CHOWN"], ["CHOWN"],
                ["cap_chown"], ["CAP_"], ["CAP_" + "X" * 61], ["CAP_X"] * 513,
                [{"private": "value"}], [1], [""],
            )
        ]
        for location, key, replacement in refused_changes:
            with self.subTest(refused=(location, key, type(replacement).__name__)):
                value = self.valid_native_inspection()
                target = value if location == "root" else value["HostConfig"]
                target[key] = replacement
                with self.assertRaisesRegex(topology.Refusal, "^native-capability-fence-refused$"):
                    topology.inspect_native(value)

        for location, key in (("HostConfig", "CapAdd"), ("HostConfig", "CapDrop"),
                              ("HostConfig", "Privileged"), ("root", "EffectiveCaps"),
                              ("root", "BoundingCaps")):
            with self.subTest(missing=(location, key)):
                value = self.valid_native_inspection()
                target = value if location == "root" else value["HostConfig"]
                del target[key]
                with self.assertRaisesRegex(topology.Refusal, "^native-capability-fence-refused$"):
                    topology.inspect_native(value)

    def test_receiver_is_private_dual_homed_and_holds_all_receiver_secrets(self):
        value = {
            "Config": {"User": "32768:32768", "Env": [],
                       "Cmd": ["serve", "--config", "/qualification/host.json"]},
            "HostConfig": {"NetworkMode": "bridge", "ReadonlyRootfs": True,
                           "PidsLimit": 128, "Memory": 1024 ** 3, "NanoCpus": 1_000_000_000,
                           "PortBindings": {}},
            "NetworkSettings": {"Networks": {
                topology.COLLECTOR_NETWORK: {}, topology.NATIVE_NETWORK: {}}},
            "Mounts": [{"Destination": path, "RW": path in {"/qualification/evidence", "/qualification/store"}}
                       for path in ("/qualification/host.json", topology.NATIVE_MOUNT,
                                    "/qualification/host.json.native-collector.json",
                                    "/qualification/evidence", "/qualification/store",
                                    "/qualification/tls", "/qualification/credentials")],
        }
        topology.inspect_receiver(value)
        for refused_mode in ("host", "default", "slirp4netns", "pasta",
                             topology.COLLECTOR_NETWORK, ["bridge"], {"mode": "bridge"}):
            with self.subTest(network_mode=refused_mode):
                value["HostConfig"]["NetworkMode"] = refused_mode
                with self.assertRaisesRegex(topology.Refusal, "direct-network-route"):
                    topology.inspect_receiver(value)
        value["HostConfig"]["NetworkMode"] = "bridge"
        value["NetworkSettings"]["Networks"][topology.UPLINK_NETWORK] = {}
        with self.assertRaisesRegex(topology.Refusal, "network-set"):
            topology.inspect_receiver(value)
        value["NetworkSettings"]["Networks"].pop(topology.UPLINK_NETWORK)
        value["Config"]["Env"] = ["HTTPS_PROXY=http://native-egress:3128"]
        with self.assertRaisesRegex(topology.Refusal, "environment-route"):
            topology.inspect_receiver(value)

    def test_receiver_refusal_projection_is_bounded_and_excludes_raw_private_values(self):
        value = {
            "Config": {"User": "32768:32768",
                       "Env": ["FSGG_TELEMETRY_CREDENTIAL_PRIVATE=secret-value"],
                       "Cmd": ["serve", "--config", "/private/secret-host.json"]},
            "HostConfig": {"NetworkMode": "bridge", "ReadonlyRootfs": True,
                           "PidsLimit": 128, "Memory": 1024 ** 3, "NanoCpus": 1_000_000_000,
                           "PortBindings": {}, "CapAdd": [],
                           "CapDrop": ["CAP_CHOWN", "CAP_SETUID"],
                           "SecurityOpt": ["no-new-privileges"], "Privileged": False,
                           "UsernsMode": "private", "PidMode": "private", "UTSMode": "private",
                           "Tmpfs": {"/tmp": "rw,noexec,nosuid,nodev,size=64m"}},
            "EffectiveCaps": [], "BoundingCaps": [],
            "NetworkSettings": {"Networks": {
                topology.COLLECTOR_NETWORK: {}, topology.NATIVE_NETWORK: {}, "private-network-name": {}}},
            "Mounts": [{"Source": "/private/source-path", "Destination": path,
                        "RW": path in {"/qualification/evidence", "/qualification/store"}}
                       for path in topology.RECEIVER_MOUNTS] +
                      [{"Source": "/private/unexpected", "Destination": "/private/destination", "RW": True}],
        }
        projection = topology.receiver_inspection_projection(value, "container-network-set-refused")
        self.assertEqual("container-network-set-refused", projection["failureCode"])
        self.assertEqual((3, 1), (projection["network"]["count"],
                                 projection["network"]["unexpectedCount"]))
        self.assertEqual("expanded", projection["privilege"]["capDropCategory"])
        self.assertEqual("bridge", projection["network"]["modeCategory"])
        self.assertTrue(projection["environment"]["credentialPresent"])
        self.assertFalse(projection["route"]["commandExact"])
        self.assertEqual(1, projection["storage"]["unexpectedMountCount"])
        encoded = json.dumps(projection, sort_keys=True)
        self.assertLess(len(encoded), 8192)
        for private in ("secret-value", "PRIVATE", "private-network-name", "/private/source-path",
                        "/private/destination", "/private/secret-host.json"):
            self.assertNotIn(private, encoded)
        unknown = topology.receiver_inspection_projection(value, "unbounded-private-detail")
        self.assertEqual("receiver-inspection-refused", unknown["failureCode"])

    def test_native_refusal_projection_types_capability_metadata_without_private_values(self):
        value = {
            "Config": {"User": {"malformed": "private-user"},
                       "Env": ["FSGG_TELEMETRY_CREDENTIAL_NATIVE_PROSPECTIVE_V1=secret-value",
                               {"malformed": "private-env"}]},
            "HostConfig": {"NetworkMode": {"malformed": "private-network-mode"},
                           "ReadonlyRootfs": True, "PidsLimit": 128,
                           "Memory": 2 * 1024 ** 3, "NanoCpus": 2_000_000_000,
                           "CapDrop": None, "SecurityOpt": ["no-new-privileges"],
                           "Privileged": False, "UsernsMode": "private",
                           "PidMode": "private", "UTSMode": "private"},
            "EffectiveCaps": {"malformed": "private-effective-capability"},
            "BoundingCaps": [],
            "NetworkSettings": {"Networks": {
                topology.NATIVE_NETWORK: {}, "private-network-name": {}}},
            "Mounts": [{"Source": "/private/source", "Destination": path, "RW": writable}
                       for path, writable in topology.NATIVE_MOUNTS.items()] +
                      [{"Source": "/private/unexpected", "Destination": ["private-destination"],
                        "RW": True}],
        }
        projection = topology.native_inspection_projection(value, "native-capability-fence-refused")
        privilege = projection["privilege"]
        self.assertEqual("native-capability-fence-refused", projection["failureCode"])
        self.assertEqual({"shape": "missing", "count": 0}, privilege["capAdd"])
        self.assertEqual({"shape": "null", "count": 0, "category": "unknown"},
                         privilege["capDrop"])
        self.assertEqual({"shape": "malformed", "count": 0}, privilege["ociEffective"])
        self.assertEqual({"shape": "empty", "count": 0}, privilege["ociBounding"])
        self.assertEqual("malformed", projection["environment"]["shape"])
        self.assertEqual("malformed", projection["storage"]["shape"])
        self.assertEqual("malformed", projection["network"]["modeCategory"])
        encoded = json.dumps(projection, sort_keys=True)
        self.assertLess(len(encoded), 8192)
        for private in ("secret-value", "private-user", "private-env", "private-network-mode",
                        "private-effective-capability", "private-network-name", "/private/source",
                        "/private/unexpected", "private-destination"):
            self.assertNotIn(private, encoded)

        value["HostConfig"].update({"CapAdd": [], "CapDrop": ["CAP_CHOWN", "CAP_SETUID"]})
        value["EffectiveCaps"] = []
        value["BoundingCaps"] = ["CAP_CHOWN"]
        typed = topology.native_inspection_projection(value, "private-refusal-detail")["privilege"]
        self.assertEqual({"shape": "empty", "count": 0}, typed["capAdd"])
        self.assertEqual({"shape": "list", "count": 2, "category": "expanded"}, typed["capDrop"])
        self.assertEqual({"shape": "empty", "count": 0}, typed["ociEffective"])
        self.assertEqual({"shape": "list", "count": 1}, typed["ociBounding"])
        self.assertEqual("native-inspection-refused",
                         topology.native_inspection_projection(value, "private-refusal-detail")["failureCode"])

    def test_original_volume_identity_and_digest_are_preserved(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary) / "native"
            rollout = root / ".codex/sessions"
            rollout.mkdir(parents=True)
            (rollout / "original.jsonl").write_text("original\n")
            before = topology.snapshot_native_volume(root)
            after = topology.snapshot_native_volume(root)
            topology.require_same_original_volume(before, after)
            self.assertEqual(before.digest, after.digest)
            replacement = topology.CustodySnapshot(before.device, before.inode + 1, before.digest, before.files)
            with self.assertRaisesRegex(topology.Refusal, "volume-replaced"):
                topology.require_same_original_volume(before, replacement)
        topology.require_collection_handoff(False, False)
        with self.assertRaisesRegex(topology.Refusal, "writer-still-running"):
            topology.require_collection_handoff(True, False)
        with self.assertRaisesRegex(topology.Refusal, "lock-still-held"):
            topology.require_collection_handoff(False, True)

    def test_only_full_immutable_image_ids_are_admitted(self):
        digest = "a" * 64
        self.assertEqual("sha256:" + digest, topology.normalize_image_id(digest))
        self.assertEqual("sha256:" + digest, topology.normalize_image_id("sha256:" + digest))
        for refused in ("native:latest", "sha256:abc", "0" * 64, ""):
            with self.assertRaisesRegex(topology.Refusal, "image-id-refused"):
                topology.normalize_image_id(refused)
        with self.assertRaisesRegex(topology.Refusal, "image-id-refused"):
            topology.native_create("native:latest", pathlib.Path("/private/native"),
                                   pathlib.Path("/private/producer/config"),
                                   pathlib.Path("/private/producer/spool"), "nonce-001")
        rendered = " ".join(topology.native_create(
            digest, pathlib.Path("/private/native"), pathlib.Path("/private/producer/config"),
            pathlib.Path("/private/producer/spool"), "nonce-001"))
        self.assertIn("sha256:" + digest, rendered)

    def test_native_recipe_has_pins_runtime_checks_and_no_floating_identity(self):
        recipe = (ROOT / "deployment/telemetry-collector/NativeContainerfile").read_text()
        self.assertIn("python@sha256:9bbb8720", recipe)
        self.assertIn("aspnet:10.0.12-noble-amd64@sha256:ed6a2d", recipe)
        self.assertIn("Python 3.14.0", recipe)
        self.assertIn("import hashlib, json, socket, ssl", recipe)
        self.assertIn("codex-cli 0.158.0", recipe)
        self.assertIn("FROM native-development AS native-readonly-source", recipe)
        self.assertIn("v2-host-01.8a-readonly-source-compatibility-v1", recipe)
        self.assertIn("native/native-receiver.crt", recipe)
        self.assertIn("RECEIVER_TRUST_CERT_SHA256", recipe)
        self.assertIn("update-ca-certificates", recipe)
        self.assertIn("install -d -o 32768 -g 32768 -m 0700 /qualification", recipe)
        self.assertIn("find /opt/fsgg/coord -type f -exec chmod 0444", recipe)
        self.assertIn("find /opt/fsgg/telemetry-host -type f -exec chmod 0444", recipe)
        self.assertIn("chmod 0555 /opt/fsgg/telemetry-host/fsgg-telemetry-host", recipe)
        self.assertIn("exec /usr/bin/dotnet /opt/fsgg/telemetry-host/FS.GG.Telemetry.Host.dll", recipe)
        self.assertNotIn(":latest", recipe)
        self.assertNotIn("apt-get", recipe)
        for argument, path in (
            ("EGRESS_GATE_SHA256", ROOT / "deployment/telemetry-collector/native_egress_gate.py"),
            ("NETWORK_POLICY_SHA256", POLICY_PATH),
        ):
            digest = hashlib.sha256(path.read_bytes()).hexdigest()
            self.assertIn(f"ARG {argument}={digest}", recipe)


class NativeGateProcessTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.policy = gate.load_policy(POLICY_PATH)
        self.gate = gate.Gate(self.policy)
        self.server = await asyncio.start_server(self.gate.handle, "127.0.0.1", 0)
        self.port = self.server.sockets[0].getsockname()[1]

    async def asyncTearDown(self):
        self.server.close()
        await self.server.wait_closed()

    async def request(self, authority):
        reader, writer = await asyncio.open_connection("127.0.0.1", self.port)
        writer.write(f"CONNECT {authority} HTTP/1.1\r\n\r\n".encode())
        await writer.drain()
        response = await asyncio.wait_for(reader.readuntil(b"\r\n\r\n"), timeout=2)
        writer.close()
        await writer.wait_closed()
        return response

    async def test_running_gate_refuses_unknown_destination_without_dns(self):
        with mock.patch.object(gate.socket, "getaddrinfo") as resolver:
            response = await self.request("api.openai.com:443")
        self.assertTrue(response.startswith(b"HTTP/1.1 403"))
        resolver.assert_not_called()

    async def test_running_gate_refuses_metadata_rebinding_before_connect(self):
        rows = [(gate.socket.AF_INET, gate.socket.SOCK_STREAM, 6, "", ("169.254.169.254", 443))]
        with mock.patch.object(gate.socket, "getaddrinfo", return_value=rows):
            response = await self.request("chatgpt.com:443")
        self.assertTrue(response.startswith(b"HTTP/1.1 403"))

    async def test_running_gate_refuses_capacity_queue(self):
        for _ in range(self.policy.maximum_concurrent_connections):
            await self.gate.capacity.acquire()
        try:
            response = await self.request("chatgpt.com:443")
        finally:
            for _ in range(self.policy.maximum_concurrent_connections):
                self.gate.capacity.release()
        self.assertTrue(response.startswith(b"HTTP/1.1 503"))


if __name__ == "__main__":
    unittest.main(verbosity=2)
