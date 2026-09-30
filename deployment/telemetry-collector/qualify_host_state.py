#!/usr/bin/env python3
"""Qualify the published Telemetry Host's controlled durable state boundary."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import pathlib
import secrets
import shutil
import ssl
import stat
import subprocess
import sys
import time
import urllib.error
import urllib.request
import zipfile

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import telemetry_collector as release_tools  # noqa: E402


# Protocol identity for the generated empty controlled request; no raw ingest is retained.
INGEST_SCHEMA = "fsgg.telemetry.ingest/1"


class Refusal(Exception):
    pass


def require(condition: bool, message: str) -> None:
    if not condition:
        raise Refusal(message)


def regular(path: pathlib.Path, maximum: int, executable: bool = False) -> None:
    info = path.lstat()
    require(stat.S_ISREG(info.st_mode) and not path.is_symlink(), f"regular file required: {path}")
    require(0 < info.st_size <= maximum, f"file size refused: {path}")
    require(not executable or os.access(path, os.X_OK), f"executable required: {path}")


def lexical_absolute(path: pathlib.Path) -> None:
    require(path.is_absolute() and path == pathlib.Path(os.path.normpath(path)),
            f"absolute normalized path required: {path}")
    current = pathlib.Path(path.anchor)
    for part in path.parts[1:]:
        current /= part
        if current.is_symlink():
            raise Refusal(f"symbolic-link path component refused: {current}")
        if not current.exists():
            break


def existing_directory(path: pathlib.Path, label: str) -> None:
    info = path.lstat()
    require(stat.S_ISDIR(info.st_mode) and not path.is_symlink(), f"{label} directory required: {path}")


def remove_owned_directory(path: pathlib.Path, identity: tuple[int, int]) -> None:
    info = path.lstat()
    require(stat.S_ISDIR(info.st_mode) and not path.is_symlink()
            and (info.st_dev, info.st_ino) == identity, "owned state identity changed")
    shutil.rmtree(path)


def sha256(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def write_private(path: pathlib.Path, value: bytes) -> None:
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(descriptor, "wb") as stream:
        stream.write(value)
        stream.flush()
        os.fsync(stream.fileno())


def open_private_output(path: pathlib.Path):
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    return os.fdopen(descriptor, "wb")


def startup_output_summary(process: subprocess.Popen[bytes], stdout: pathlib.Path,
                           stderr: pathlib.Path) -> str:
    fields = []
    for label, path in (("stdout", stdout), ("stderr", stderr)):
        info = path.lstat()
        require(stat.S_ISREG(info.st_mode) and not path.is_symlink(),
                f"Host startup {label} is not a regular file")
        require(info.st_size <= 256 * 1024, f"Host startup {label} exceeded its custody bound")
        fields.append(f"{label}Bytes={info.st_size} {label}Sha256={sha256(path)}")
    return f"exitCode={process.returncode} " + " ".join(fields)


def copy_private(source: pathlib.Path, destination: pathlib.Path, maximum: int) -> None:
    regular(source, maximum)
    write_private(destination, source.read_bytes())


def run(command: list[str], environment: dict[str, str], timeout: int = 30,
        expected: int = 0) -> subprocess.CompletedProcess[str]:
    completed = subprocess.run(command, text=True, capture_output=True, env=environment,
                               timeout=timeout, check=False)
    require(len(completed.stdout.encode()) <= 256 * 1024 and len(completed.stderr.encode()) <= 256 * 1024,
            "Host command output exceeded its custody bound")
    require(completed.returncode == expected,
            f"Host command returned {completed.returncode}, expected {expected}")
    return completed


def request(url: str, token: str, context: ssl.SSLContext, method: str = "GET",
            body: bytes | None = None) -> tuple[int, bytes]:
    headers = {"Authorization": f"Bearer {token}"}
    if body is not None:
        headers["Content-Type"] = "application/json"
    candidate = urllib.request.Request(url, data=body, headers=headers, method=method)
    try:
        with urllib.request.urlopen(candidate, context=context, timeout=5) as response:
            payload = response.read(128 * 1024 + 1)
            require(len(payload) <= 128 * 1024, "Host response exceeded its custody bound")
            return response.status, payload
    except urllib.error.HTTPError as error:
        payload = error.read(128 * 1024 + 1)
        require(len(payload) <= 128 * 1024, "Host refusal exceeded its custody bound")
        return error.code, payload


def receipt(payload: bytes, batch: str, expected_status: str, expected_digest: str) -> dict:
    value = json.loads(payload)
    require(set(value) == {"schema", "workspaceId", "producerId", "streamId", "batchId",
                           "digest", "status", "code"}, "receipt shape differs")
    require(value["schema"] == "fsgg.telemetry.receipt/1", "receipt schema differs")
    require(value["workspaceId"] == "controlled-state" and value["producerId"] == "controlled-active"
            and value["streamId"] == "runtime", "receipt scope differs")
    require(value["batchId"] == batch and value["status"] == expected_status
            and value["digest"] == expected_digest, "receipt state differs")
    return value


def host_config(listen_url: str, certificate: pathlib.Path, password: pathlib.Path,
                lock: pathlib.Path, store: pathlib.Path, secret: pathlib.Path,
                revoked_secret: pathlib.Path, browser_key_hash: pathlib.Path,
                generation: int = 1) -> dict:
    def credential(reference: str, secret_file: pathlib.Path, producer: str, revoked: bool) -> dict:
        return {
            "Reference": reference, "SecretFile": str(secret_file), "WorkspaceId": "controlled-state",
            "ProducerId": producer, "StreamId": "runtime", "Role": "native-collector",
            "GrantId": f"grant-{producer}", "GrantGeneration": generation, "Revoked": revoked,
        }
    return {
        "Schema": "fsgg.telemetry.host-config/2", "ListenUrl": listen_url,
        "CertificatePath": str(certificate), "CertificatePasswordFile": str(password),
        "ServiceLockPath": str(lock),
        "Stores": [{"WorkspaceId": "controlled-state", "Root": str(store)}],
        "Credentials": [credential("active", secret, "controlled-active", False),
                        credential("revoked", revoked_secret, "controlled-revoked", True)],
        "BrowserPrincipals": [{"PrincipalId": "controlled-browser-preflight",
                               "KeyHashFile": str(browser_key_hash),
                               "WorkspaceIds": ["controlled-state"], "Revoked": False}],
        "BrowserSession": {"IdleSeconds": 300, "AbsoluteSeconds": 3600, "MaximumSessions": 16,
                           "LoginAttemptsPerMinute": 8, "LoginAdmission": 2,
                           "QueryAdmission": 2, "QueryTimeoutSeconds": 10},
    }


def controlled_envelope() -> bytes:
    # An empty, valid ingest batch tests admission, durability, replay and recovery without
    # manufacturing a native observation or a learning fact.
    value = {
        "schema": "fsgg.telemetry.envelope/1", "workspaceId": "controlled-state",
        "producerId": "controlled-active", "streamId": "runtime", "batchId": "controlled-empty-1",
        "payload": {"schema": INGEST_SCHEMA, "ingestId": "controlled-empty-1",
                    "sourceIdentity": "controlled-state", "generation": "g1", "cursor": "1",
                    "eventCount": 0, "events": []},
    }
    return json.dumps(value, sort_keys=True, separators=(",", ":")).encode()


def extract_host(package: pathlib.Path, destination: pathlib.Path) -> pathlib.Path:
    destination.mkdir(mode=0o700)
    with zipfile.ZipFile(package) as archive:
        for member in release_tools.package_members(package):
            relative = pathlib.PurePosixPath(member.filename).relative_to(release_tools.TOOLS_PREFIX)
            target = destination.joinpath(*relative.parts)
            target.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
            with archive.open(member) as source, target.open("xb") as output:
                shutil.copyfileobj(source, output, 1024 * 1024)
            target.chmod(0o500 if target.name == "FS.GG.Telemetry.Host" else 0o400)
    dll = destination / "FS.GG.Telemetry.Host.dll"
    regular(dll, 32 * 1024 * 1024)
    return dll


def qualify(args: argparse.Namespace) -> dict:
    supplied_paths = (args.repository, args.package, args.manifest, args.journal, args.dotnet,
                      args.state_root, args.evidence, args.certificate,
                      args.certificate_password_file, args.ca_certificate)
    for path in supplied_paths:
        lexical_absolute(path)
    repository = args.repository
    package, manifest, journal = args.package, args.manifest, args.journal
    dotnet, state, evidence = args.dotnet, args.state_root, args.evidence
    existing_directory(repository, "repository")
    existing_directory(state.parent, "state parent")
    existing_directory(evidence.parent, "evidence parent")
    for path, maximum in ((package, 32 * 1024 * 1024), (manifest, 1024 * 1024),
                          (journal, 1024 * 1024), (args.certificate, 1024 * 1024),
                          (args.certificate_password_file, 4096), (args.ca_certificate, 1024 * 1024)):
        regular(path, maximum)
    regular(dotnet, 128 * 1024 * 1024, executable=True)
    require(not state.exists() and not state.is_symlink(), "state output must not exist")
    require(not evidence.exists() and not evidence.is_symlink(), "evidence output must not exist")
    require(args.listen_url.startswith("https://localhost:"), "loopback HTTPS listen URL required")
    release = release_tools.verify_release(repository, package, manifest, journal, args.version,
                                           args.source_sha, args.package_sha256,
                                           args.manifest_sha256, args.journal_sha256)
    state.mkdir(mode=0o700, parents=False)
    state_info = state.lstat()
    state_identity = (state_info.st_dev, state_info.st_ino)
    try:
        config_root, store, home = state / "config", state / "store", state / "home"
        for directory in (config_root, home):
            directory.mkdir(mode=0o700)
        certificate, password, ca = config_root / "server.pfx", config_root / "password", config_root / "ca.pem"
        active_secret, revoked_secret = config_root / "active.secret", config_root / "revoked.secret"
        browser_key_hash = config_root / "browser-key-hash.json"
        copy_private(args.certificate, certificate, 1024 * 1024)
        copy_private(args.certificate_password_file, password, 4096)
        copy_private(args.ca_certificate, ca, 1024 * 1024)
        active_token, revoked_token, invalid_token = secrets.token_urlsafe(48), secrets.token_urlsafe(48), secrets.token_urlsafe(48)
        write_private(active_secret, (active_token + "\n").encode())
        write_private(revoked_secret, (revoked_token + "\n").encode())
        write_private(browser_key_hash, (json.dumps({
            "schema": "fsgg.telemetry.browser-key/1", "algorithm": "sha256",
            "keyHash": hashlib.sha256(secrets.token_bytes(32)).hexdigest(),
        }, separators=(",", ":")) + "\n").encode())
        dll = extract_host(package, state / "host")
        command = [str(dotnet), str(dll)]
        environment = {"HOME": str(home), "DOTNET_CLI_HOME": str(home), "DOTNET_NOLOGO": "1",
                       "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1", "PATH": "/usr/bin:/bin", "LANG": "C.UTF-8"}
        config_path, invalid_path = config_root / "host.json", config_root / "invalid.json"
        config = host_config(args.listen_url, certificate, password, state / "service.lock", store,
                             active_secret, revoked_secret, browser_key_hash)
        invalid = host_config(args.listen_url, certificate, password, state / "invalid.lock", store,
                              active_secret, revoked_secret, browser_key_hash, generation=0)
        write_private(config_path, (json.dumps(config, separators=(",", ":")) + "\n").encode())
        write_private(invalid_path, (json.dumps(invalid, separators=(",", ":")) + "\n").encode())
        run(command + ["status", "--config", str(invalid_path)], environment, expected=2)
        require(not store.exists(), "invalid configuration wrote store state")
        run(command + ["init", "--root", str(store), "--workspace", "controlled-state"], environment)
        for reference, secret_file, producer, revoked in (
            ("active", active_secret, "controlled-active", False),
            ("revoked", revoked_secret, "controlled-revoked", True),
        ):
            enroll = command + ["enroll-producer", "--config", str(config_path), "--reference", reference,
                                "--secret-file", str(secret_file), "--workspace", "controlled-state",
                                "--producer", producer, "--stream", "runtime"]
            if revoked:
                enroll.append("--revoked")
            run(enroll, environment)
        ready = json.loads(run(command + ["status", "--config", str(config_path)], environment).stdout)
        require(len(ready.get("stores") or []) == 1 and ready["stores"][0].get("status") == "ready",
                "initialized Host store is not ready")

        context = ssl.create_default_context(cafile=str(ca))
        context.check_hostname = True
        envelope = controlled_envelope()
        envelope_digest = hashlib.sha256(envelope).hexdigest()
        url = args.listen_url.rstrip("/")

        serve_attempt = 0

        def start_and_wait() -> subprocess.Popen[bytes]:
            nonlocal serve_attempt
            serve_attempt += 1
            stdout_path = state / f"serve-{serve_attempt}.stdout"
            stderr_path = state / f"serve-{serve_attempt}.stderr"
            with open_private_output(stdout_path) as stdout, open_private_output(stderr_path) as stderr:
                process = subprocess.Popen(command + ["serve", "--config", str(config_path)], env=environment,
                                           stdin=subprocess.DEVNULL, stdout=stdout, stderr=stderr)
            try:
                deadline = time.monotonic() + 20
                while time.monotonic() < deadline:
                    if process.poll() is not None:
                        raise Refusal("Host exited before becoming ready; "
                                      + startup_output_summary(process, stdout_path, stderr_path))
                    for output in (stdout_path, stderr_path):
                        require(output.stat().st_size <= 256 * 1024,
                                "Host startup output exceeded its custody bound")
                    try:
                        if request(url + "/private/health", active_token, context)[0] == 200:
                            return process
                    except (OSError, TimeoutError):
                        pass
                    time.sleep(0.1)
                raise Refusal("Host readiness deadline elapsed")
            except BaseException:
                if process.poll() is None:
                    process.terminate()
                    try:
                        process.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait(timeout=5)
                raise

        def stop(process: subprocess.Popen[bytes]) -> None:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)

        process = start_and_wait()
        try:
            require(request(url + "/private/health", invalid_token, context)[0] == 401,
                    "undeclared principal was accepted")
            require(request(url + "/private/health", revoked_token, context)[0] == 401,
                    "revoked principal was accepted")
            status, first_bytes = request(url + "/v1/batches", active_token, context, "POST", envelope)
            require(status == 202, "new controlled batch was not durably admitted")
            first = receipt(first_bytes, "controlled-empty-1", "durably-received", envelope_digest)
            deadline = time.monotonic() + 20
            applied_bytes = b""
            while time.monotonic() < deadline:
                code, candidate = request(url + "/v1/receipts/controlled-empty-1", active_token, context)
                if code == 200 and json.loads(candidate).get("status") == "applied":
                    applied_bytes = candidate
                    break
                time.sleep(0.1)
            require(bool(applied_bytes), "controlled receipt did not reach applied")
            applied = receipt(applied_bytes, "controlled-empty-1", "applied", envelope_digest)
            require(first["digest"] == applied["digest"], "receipt digest changed while applying")
            code, duplicate_bytes = request(url + "/v1/batches", active_token, context, "POST", envelope)
            require(code == 200 and duplicate_bytes == applied_bytes, "live exact replay acknowledgement differs")
        finally:
            stop(process)

        before = json.loads(run(command + ["status", "--config", str(config_path)], environment).stdout)
        process = start_and_wait()
        try:
            code, recovered_bytes = request(url + "/v1/receipts/controlled-empty-1", active_token, context)
            require(code == 200 and recovered_bytes == applied_bytes, "restart receipt history differs")
            code, replay_bytes = request(url + "/v1/batches", active_token, context, "POST", envelope)
            require(code == 200 and replay_bytes == applied_bytes, "restart exact replay acknowledgement differs")
        finally:
            stop(process)
        after = json.loads(run(command + ["status", "--config", str(config_path)], environment).stdout)
        for observed in (before, after):
            stores = observed.get("stores") or []
            require(len(stores) == 1 and stores[0].get("lifetimeReceipts") == 1
                    and stores[0].get("pendingReceipts") == 0, "retained Host receipt history differs")

        result = {
            "schema": "fsgg.telemetry.collector-host-state-qualification/1",
            "verdict": "controlled-production-state-passed", "hostVersion": release["version"],
            "hostSourceSha": release["sourceSha"], "hostPackageSha256": args.package_sha256,
            "hostDllSha256": sha256(dll), "configSchema": "fsgg.telemetry.host-config/2",
            "principalRole": "native-collector", "grantId": "grant-controlled-active", "grantGeneration": 1,
            "receiptDigest": applied["digest"], "receiptStatus": applied["status"], "lifetimeReceipts": 1,
            "invalidPrincipalRefused": True, "revokedPrincipalRefused": True,
            "exactReplayStable": True, "restartReceiptStable": True, "retainedStateReopened": True,
            "nativeAccessQualified": False, "modelSupportObserved": False,
            "captureApplied": False, "activationAuthorized": False,
            "controlledPayloadFacts": 0, "stateRemoved": True,
        }
        remove_owned_directory(state, state_identity)
        write_private(evidence, (json.dumps(result, sort_keys=True, separators=(",", ":")) + "\n").encode())
        return result
    except BaseException:
        try:
            remove_owned_directory(state, state_identity)
        except (FileNotFoundError, Refusal):
            pass
        raise


def parser() -> argparse.ArgumentParser:
    value = argparse.ArgumentParser()
    for name in ("repository", "package", "manifest", "journal", "dotnet", "state-root", "evidence",
                 "certificate", "certificate-password-file", "ca-certificate"):
        value.add_argument("--" + name, type=pathlib.Path, required=True)
    value.add_argument("--version", required=True)
    value.add_argument("--source-sha", required=True)
    value.add_argument("--package-sha256", required=True)
    value.add_argument("--manifest-sha256", required=True)
    value.add_argument("--journal-sha256", required=True)
    value.add_argument("--listen-url", required=True)
    return value


def main() -> int:
    args = parser().parse_args()
    try:
        result = qualify(args)
        print(json.dumps(result, sort_keys=True, separators=(",", ":")))
        return 0
    except (Refusal, release_tools.Refusal, OSError, ValueError, json.JSONDecodeError,
            subprocess.SubprocessError, zipfile.BadZipFile) as error:
        print(f"collector-host-state-refused: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
