#!/usr/local/bin/python3
"""Fixed HTTPS CONNECT gate for the private native qualification network."""

from __future__ import annotations

import asyncio
import ipaddress
import json
import pathlib
import socket
from dataclasses import dataclass


POLICY_PATH = pathlib.Path("/opt/fsgg/native-network-policy.json")
POLICY_KEYS = {
    "schema", "policyId", "connectPort", "allowedHosts", "listen",
    "maximumHeaderBytes", "maximumConcurrentConnections", "dnsResultLimit",
    "connectTimeoutSeconds", "idleTimeoutSeconds", "maximumConnectionSeconds",
    "deniedNetworks",
}


class Refusal(Exception):
    pass


@dataclass(frozen=True)
class Policy:
    allowed_hosts: frozenset[str]
    denied_networks: tuple[ipaddress.IPv4Network | ipaddress.IPv6Network, ...]
    maximum_header_bytes: int
    maximum_concurrent_connections: int
    dns_result_limit: int
    connect_timeout_seconds: int
    idle_timeout_seconds: int
    maximum_connection_seconds: int


def load_policy(path: pathlib.Path) -> Policy:
    if path.is_symlink() or not path.is_file() or path.stat().st_size > 64 * 1024:
        raise Refusal("policy-file-refused")
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict) or set(value) != POLICY_KEYS:
        raise Refusal("policy-shape-refused")
    if value["schema"] != "fsgg.telemetry.native-network-policy/1":
        raise Refusal("policy-schema-refused")
    if value["policyId"] != "fsgg-native-egress-v1" or value["connectPort"] != 443:
        raise Refusal("policy-identity-refused")
    if value["listen"] != "0.0.0.0:3128":
        raise Refusal("policy-listener-refused")
    hosts = value["allowedHosts"]
    if hosts != ["auth.openai.com", "chatgpt.com"]:
        raise Refusal("policy-hosts-refused")
    bounded = {
        "maximumHeaderBytes": (1024, 8192),
        "maximumConcurrentConnections": (1, 8),
        "dnsResultLimit": (1, 16),
        "connectTimeoutSeconds": (1, 5),
        "idleTimeoutSeconds": (1, 60),
        "maximumConnectionSeconds": (1, 600),
    }
    for name, (minimum, maximum) in bounded.items():
        if type(value[name]) is not int or not minimum <= value[name] <= maximum:
            raise Refusal(f"policy-bound-refused:{name}")
    networks = tuple(ipaddress.ip_network(item, strict=True) for item in value["deniedNetworks"])
    if len(networks) != len(value["deniedNetworks"]) or len(networks) < 10:
        raise Refusal("policy-networks-refused")
    return Policy(
        frozenset(hosts), networks, value["maximumHeaderBytes"],
        value["maximumConcurrentConnections"], value["dnsResultLimit"],
        value["connectTimeoutSeconds"], value["idleTimeoutSeconds"],
        value["maximumConnectionSeconds"],
    )


def parse_connect_request(header: bytes, policy: Policy) -> str:
    if len(header) > policy.maximum_header_bytes or b"\x00" in header:
        raise Refusal("request-size-refused")
    try:
        lines = header.decode("ascii").split("\r\n")
    except UnicodeDecodeError as error:
        raise Refusal("request-encoding-refused") from error
    if not lines or len(lines[0].split(" ")) != 3:
        raise Refusal("request-line-refused")
    method, authority, version = lines[0].split(" ")
    if method != "CONNECT" or version != "HTTP/1.1":
        raise Refusal("request-method-refused")
    if authority.count(":") != 1 or "@" in authority or authority.endswith("."):
        raise Refusal("request-authority-refused")
    host, port = authority.rsplit(":", 1)
    if port != "443" or host != host.lower() or host not in policy.allowed_hosts:
        raise Refusal("request-destination-refused")
    try:
        ipaddress.ip_address(host)
    except ValueError:
        return host
    raise Refusal("request-ip-literal-refused")


def validate_resolved_addresses(rows: list[tuple], policy: Policy) -> list[ipaddress.IPv4Address | ipaddress.IPv6Address]:
    addresses: list[ipaddress.IPv4Address | ipaddress.IPv6Address] = []
    for row in rows:
        address = ipaddress.ip_address(row[4][0])
        if isinstance(address, ipaddress.IPv6Address) and address.ipv4_mapped is not None:
            address = address.ipv4_mapped
        if address not in addresses:
            addresses.append(address)
    if not addresses or len(addresses) > policy.dns_result_limit:
        raise Refusal("dns-population-refused")
    for address in addresses:
        if not address.is_global or any(address in network for network in policy.denied_networks
                                        if address.version == network.version):
            raise Refusal("dns-address-refused")
    return addresses


async def copy_bounded(reader: asyncio.StreamReader, writer: asyncio.StreamWriter, idle: int) -> None:
    while True:
        data = await asyncio.wait_for(reader.read(64 * 1024), timeout=idle)
        if not data:
            return
        writer.write(data)
        await asyncio.wait_for(writer.drain(), timeout=idle)


class Gate:
    def __init__(self, policy: Policy):
        self.policy = policy
        self.capacity = asyncio.Semaphore(policy.maximum_concurrent_connections)

    async def handle(self, reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
        if self.capacity.locked():
            writer.write(b"HTTP/1.1 503 Service Unavailable\r\nConnection: close\r\nContent-Length: 0\r\n\r\n")
            try:
                await writer.drain()
            except (ConnectionError, asyncio.TimeoutError):
                pass
            await close_writer(writer)
            return
        await self.capacity.acquire()
        try:
            await self.handle_admitted(reader, writer)
        finally:
            self.capacity.release()

    async def handle_admitted(self, reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
        upstream_writer: asyncio.StreamWriter | None = None
        try:
            header = await asyncio.wait_for(
                reader.readuntil(b"\r\n\r\n"), timeout=self.policy.connect_timeout_seconds)
            host = parse_connect_request(header, self.policy)
            loop = asyncio.get_running_loop()
            rows = await asyncio.wait_for(
                loop.getaddrinfo(host, 443, type=socket.SOCK_STREAM),
                timeout=self.policy.connect_timeout_seconds)
            addresses = validate_resolved_addresses(rows, self.policy)
            upstream_reader = None
            for address in addresses:
                try:
                    upstream_reader, upstream_writer = await asyncio.wait_for(
                        asyncio.open_connection(str(address), 443),
                        timeout=self.policy.connect_timeout_seconds)
                    break
                except (OSError, asyncio.TimeoutError):
                    continue
            if upstream_reader is None or upstream_writer is None:
                raise Refusal("upstream-connect-refused")
            writer.write(b"HTTP/1.1 200 Connection Established\r\n\r\n")
            await writer.drain()
            async with asyncio.timeout(self.policy.maximum_connection_seconds):
                await asyncio.gather(
                    copy_bounded(reader, upstream_writer, self.policy.idle_timeout_seconds),
                    copy_bounded(upstream_reader, writer, self.policy.idle_timeout_seconds),
                )
        except (Refusal, asyncio.IncompleteReadError, asyncio.LimitOverrunError,
                asyncio.TimeoutError, OSError, ValueError, json.JSONDecodeError):
            if not writer.is_closing():
                writer.write(b"HTTP/1.1 403 Forbidden\r\nConnection: close\r\nContent-Length: 0\r\n\r\n")
                try:
                    await writer.drain()
                except (ConnectionError, asyncio.TimeoutError):
                    pass
        finally:
            if upstream_writer is not None:
                await close_writer(upstream_writer)
            await close_writer(writer)


async def close_writer(writer: asyncio.StreamWriter) -> None:
    writer.close()
    try:
        await writer.wait_closed()
    except (ConnectionError, asyncio.TimeoutError, OSError):
        pass


async def main() -> None:
    policy = load_policy(POLICY_PATH)
    gate = Gate(policy)
    server = await asyncio.start_server(gate.handle, "0.0.0.0", 3128, limit=policy.maximum_header_bytes)
    async with server:
        await server.serve_forever()


if __name__ == "__main__":
    asyncio.run(main())
