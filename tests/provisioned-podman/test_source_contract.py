#!/usr/bin/env python3
"""Inert common-base source controls; no package/container/network execution."""
import json
import hashlib
import os
from pathlib import Path
import re
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "deployment/provisioned-podman"


class CommonBaseSource(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.recipe = (SOURCE / "Containerfile").read_text()
        cls.config = (SOURCE / "pacman.conf").read_text()
        cls.inputs = json.loads((SOURCE / "inputs.json").read_text())
        cls.readme = (SOURCE / "README.md").read_text()

    def test_exact_platform_manifest_not_mutable_tag(self):
        self.assertEqual(self.inputs["platform"], "linux/amd64")
        self.assertEqual(self.recipe.splitlines()[0], "FROM " + self.inputs["donor"]["reference"])
        self.assertRegex(self.inputs["donor"]["reference"], r"^docker.io/library/archlinux@sha256:[0-9a-f]{64}$")
        self.assertNotIn(self.inputs["donor"]["tag"], self.recipe)
        self.assertEqual(self.inputs["donor"]["reference"].split(":")[-1],
                         "363129b67b2bc6c5a634f8fb4961a7e2eba213a2ec075d159127b1260290a722")
        self.assertEqual(len(self.inputs["donor"]["layers"]), 2)
        for layer in self.inputs["donor"]["layers"]:
            self.assertRegex(layer["sha256"], r"^[0-9a-f]{64}$")
            self.assertGreater(layer["bytes"], 0)

    def test_full_system_transaction_uses_selected_targets(self):
        transaction = self.recipe.split("pacman --config", 1)[1].split(";", 1)[0].replace("\\\n", " ")
        self.assertIn("--noconfirm -Syu --needed", transaction)
        actual = transaction.split("--needed", 1)[1].split()
        self.assertEqual(actual, self.inputs["targets"])
        self.assertEqual(actual, ["podman", "crun", "conmon", "containers-common", "shadow", "passt",
                                  "fuse-overlayfs", "git", "ca-certificates", "python"])
        self.assertNotRegex(self.recipe, r"pacman\s.*(?:-U\b|--nodeps|--overwrite|--assume-installed)")
        self.assertIn("LC_ALL=C pacman -Q | LC_ALL=C sort", self.recipe)
        self.assertIn('test "$expected_inventory_sha256" = "$actual_inventory_sha256"', self.recipe)

    def test_hash_comparisons_propagate_read_and_hash_failures(self):
        # Execute both literal comparison blocks, independently of package commands.
        for name in ("paths", "inventory"):
            block = self.recipe.split(f"expected_{name}_sha256=", 1)[1].split(";", 3)
            template = "set -eu; expected_" + name + "_sha256=" + ";".join(block[:3])
            paths = re.findall(r"sha256sum < ([^);]+)", template)
            self.assertEqual(len(paths), 2)
            for case in ("equal", "different", "missing-first", "missing-second", "hash-first", "hash-second"):
                with self.subTest(comparison=name, case=case), tempfile.TemporaryDirectory() as temporary:
                    directory = Path(temporary)
                    guard = template
                    for index, original in enumerate(paths):
                        path = directory / str(index)
                        path.write_bytes(b"same\n" if index == 0 or case != "different" else b"different\n")
                        if case == ("missing-first" if index == 0 else "missing-second"):
                            path.unlink()
                        guard_path = str(path)
                        guard = guard.replace(original, guard_path)
                    environment = os.environ.copy()
                    if case.startswith("hash-"):
                        # A failed hash may emit plausible output; its exit must still refuse.
                        wrapper = directory / "sha256sum"
                        wrapper.write_text("#!/bin/sh\n"
                                           'count=0; test ! -f "$HASH_COUNT" || read -r count < "$HASH_COUNT"\n'
                                           'count=$((count + 1)); printf "%s\\n" "$count" > "$HASH_COUNT"\n'
                                           'printf "same hash\\n"\n'
                                           'test "$count" != "$HASH_FAIL_AT"\n')
                        wrapper.chmod(0o700)
                        environment.update(PATH=str(directory), HASH_COUNT=str(directory / "count"),
                                           HASH_FAIL_AT="1" if case == "hash-first" else "2")
                    result = subprocess.run(["/bin/sh", "-c", guard], env=environment,
                                            capture_output=True, timeout=5)
                    if case == "equal":
                        self.assertEqual(result.returncode, 0, result.stderr.decode())
                    else:
                        self.assertNotEqual(result.returncode, 0)

    def test_preinstall_tools_do_not_require_diffutils(self):
        preinstall = self.recipe.split("pacman --config", 1)[0]
        self.assertNotRegex(self.recipe, r"\b(?:cmp|diff)\b")
        # Shell builtins plus the donor's existing coreutils, grep, findutils and pacman.
        for tool in ("grep", "find", "cut", "sort", "sha256sum", "rm", "pacman-key"):
            self.assertRegex(preinstall, r"\b" + tool + r"\b")

    def test_local_repositories_have_no_fetch_fallback(self):
        self.assertEqual(self.inputs["snapshot"]["date"], "2026-10-12")
        self.assertEqual(self.inputs["snapshot"]["repositories"], ["core", "extra"])
        servers = re.findall(r"^Server = (.+)$", self.config, re.M)
        self.assertEqual(servers, ["file:///opt/fsgg/acquired/repositories/core/os/x86_64",
                                   "file:///opt/fsgg/acquired/repositories/extra/os/x86_64"])
        self.assertNotRegex(self.config, r"(?m)^Include\s*=")
        self.assertNotRegex(self.recipe, r"https?://(?!github.com/FS-GG/)")
        self.assertIn("--pull=never --network=none --timestamp=0 --platform=linux/amd64 --format=oci", self.readme)

    def test_candidate_trust_is_pending_and_no_bypass(self):
        self.assertEqual(self.inputs["trust"]["donorPolicyReconciliation"], "pending")
        self.assertEqual(self.inputs["trust"]["archiveSignatureVerification"], "pending")
        self.assertIn("SigLevel = Required DatabaseOptional", self.config)
        self.assertIn("LocalFileSigLevel = Required", self.config)
        self.assertNotRegex(self.config, r"\b(?:Never|TrustAll)\b")
        self.assertIn("stricter donor policy must not", self.readme)
        self.assertIn("not authenticate a supplied checksum list", " ".join(self.readme.split()))

    def test_missing_acquired_closure_refuses_before_transaction(self):
        # Dockerfile COPY semantics require these genuine external inputs before RUN.
        required = ["acquired/SHA256SUMS", "acquired/package-inventory.expected", "acquired/repositories/"]
        copies = re.findall(r"(?m)^COPY (\S+) \S+$", self.recipe)
        for relative in required:
            self.assertIn(relative, copies)
            self.assertFalse((SOURCE / relative).exists(), "source must not fabricate acquired inputs")
            self.assertLess(self.recipe.index("COPY " + relative), self.recipe.index("RUN "))
        transaction = self.recipe.index("pacman --config")
        for prerequisite in ("test -s SHA256SUMS", "test -s package-inventory.expected",
                             "test -s repositories/core/os/x86_64/core.db",
                             "test -s repositories/extra/os/x86_64/extra.db",
                             "sha256sum --strict --check SHA256SUMS"):
            self.assertLess(self.recipe.index(prerequisite), transaction)
        self.assertIsNone(self.inputs["acquiredInputs"])
        self.assertEqual(self.inputs["classification"], "source-specification-not-acquired-input-lock")
        self.assertGreater(len(self.inputs["unresolved"]), 0)
        self.assertNotIn('"ready":true', json.dumps(self.inputs, separators=(",", ":")))

    def test_actual_shell_prerequisites_refuse_empty_or_missing_bytes(self):
        # Exercise only the recipe's checks before keyring/package commands.
        guard = self.recipe.split("RUN ", 1)[1].split("pacman-key --init", 1)[0].replace("\\\n", " ")
        self.assertNotRegex(guard, r"\b(?:podman|pacman|gpg|curl|wget)\b")
        with tempfile.TemporaryDirectory() as temporary:
            context = Path(temporary) / "acquired"
            context.mkdir()
            for relative in ("repositories/core/os/x86_64/core.db", "repositories/extra/os/x86_64/extra.db"):
                path = context / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b"inert-database")
            (context / "package-inventory.expected").write_bytes(b"inert 1\n")
            checks = context / "SHA256SUMS"
            checks.write_text("0" * 64 + "  repositories/core/os/x86_64/missing.pkg.tar.zst\n")
            bounded = guard.replace("/opt/fsgg/acquired", str(context)).replace("/opt/fsgg", str(context.parent))
            failed = subprocess.run(["/bin/sh", "-c", bounded], capture_output=True, timeout=5)
            self.assertNotEqual(failed.returncode, 0)
            checks.write_bytes(b"")
            empty = subprocess.run(["/bin/sh", "-c", bounded], capture_output=True, timeout=5)
            self.assertNotEqual(empty.returncode, 0)
            self.assertEqual(empty.stderr, b"")

    def test_actual_complete_population_guard_and_adversarial_controls(self):
        guard = self.recipe.split("RUN ", 1)[1].split("pacman-key --init", 1)[0].replace("\\\n", " ")
        population = {"package-inventory.expected": b"inert 1\n",
                      "repositories/core/os/x86_64/core.db": b"core",
                      "repositories/extra/os/x86_64/extra.db": b"extra",
                      "repositories/core/os/x86_64/inert-1.pkg.tar.zst": b"package",
                      "repositories/core/os/x86_64/inert-1.pkg.tar.zst.sig": b"signature"}
        cases = ["valid", "valid-epoch", "epoch-parent-escape", "epoch-control-character", "missing-db-row", "missing-package-row", "missing-signature-row",
                 "missing-inventory-row", "extra-file", "duplicate-row", "parent-escape",
                 "absolute-path", "symlink", "byte-drift"]
        for case in cases:
            with self.subTest(case=case), tempfile.TemporaryDirectory() as temporary:
                context = Path(temporary) / "acquired"
                context.mkdir()
                fixture = dict(population)
                if case.startswith("epoch-") or case == "valid-epoch":
                    fixture["repositories/core/os/x86_64/conmon-1:2.2.1-1-x86_64.pkg.tar.zst"] = fixture.pop("repositories/core/os/x86_64/inert-1.pkg.tar.zst")
                    fixture["repositories/core/os/x86_64/conmon-1:2.2.1-1-x86_64.pkg.tar.zst.sig"] = fixture.pop("repositories/core/os/x86_64/inert-1.pkg.tar.zst.sig")
                for relative, content in fixture.items():
                    path = context / relative
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_bytes(content)
                rows = [hashlib.sha256(content).hexdigest() + "  " + relative for relative, content in fixture.items()]
                omitted = {"missing-db-row": "core.db", "missing-package-row": "inert-1.pkg.tar.zst",
                           "missing-signature-row": "inert-1.pkg.tar.zst.sig",
                           "missing-inventory-row": "package-inventory.expected"}
                if case in omitted:
                    rows = [row for row in rows if not row.endswith("  " + omitted[case]) and not row.endswith("/" + omitted[case])]
                if case == "extra-file":
                    (context / "repositories/core/os/x86_64/unlisted.pkg.tar.zst").write_bytes(b"unlisted")
                if case == "duplicate-row":
                    rows.append(rows[0])
                if case == "parent-escape":
                    rows.append("0" * 64 + "  ../outside")
                if case == "epoch-parent-escape":
                    rows.append("0" * 64 + "  repositories/core/os/x86_64/../conmon-1:2.2.1-1-x86_64.pkg.tar.zst")
                if case == "epoch-control-character":
                    rows.append("0" * 64 + "  repositories/core/os/x86_64/conmon-1:2.2.1-1\tx86_64.pkg.tar.zst")
                if case == "absolute-path":
                    rows.append("0" * 64 + "  /outside")
                if case == "symlink":
                    path = context / "repositories/core/os/x86_64/inert-1.pkg.tar.zst"
                    path.unlink()
                    path.symlink_to(context / "package-inventory.expected")
                if case == "byte-drift":
                    (context / "package-inventory.expected").write_bytes(b"drift\n")
                (context / "SHA256SUMS").write_text("\n".join(rows) + "\n")
                bounded = guard.replace("/opt/fsgg/acquired", str(context)).replace("/opt/fsgg", str(context.parent))
                result = subprocess.run(["/bin/sh", "-c", bounded], capture_output=True, timeout=5)
                if case in ("valid", "valid-epoch"):
                    self.assertEqual(result.returncode, 0, result.stderr.decode())
                else:
                    self.assertNotEqual(result.returncode, 0, "unbound population reached transaction boundary")

    def test_ordinary_user_cold_store_and_no_implicit_capabilities(self):
        for line in ("USER 32768:32768", "WORKDIR /workspace", "ENTRYPOINT []", "CMD []", "ENV HOME=/home/fsgg"):
            self.assertIn(line, self.recipe.splitlines())
        self.assertIn("useradd --uid 32768 --gid 32768 --create-home", self.recipe)
        self.assertIn("--key SUB_UID_COUNT=0 --key SUB_GID_COUNT=0", self.recipe)
        self.assertIn("test ! -e /var/lib/containers/storage", self.recipe)
        self.assertIn("test ! -e /run/containers/storage", self.recipe)
        self.assertIn("test ! -e /home/fsgg/.local/share/containers", self.recipe)
        self.assertNotRegex(self.recipe, r"(?m)^(?:VOLUME|EXPOSE|HEALTHCHECK)\b")
        for forbidden in ("--privileged", "podman.sock", "docker.sock", "/dev/fuse", "systemctl", "subuid", "subgid"):
            self.assertNotIn(forbidden, self.recipe)
        self.assertEqual(self.inputs["coldDefault"]["assemblyNetwork"], "none")
        self.assertEqual(self.inputs["coldDefault"]["receiverNetwork"], "none")

    def test_build_inputs_and_private_keys_removed_licenses_retained(self):
        for path in ("/opt/fsgg/acquired", "/var/cache/pacman/pkg", "/etc/pacman.d/gnupg/private-keys-v1.d",
                     "/etc/pacman.d/gnupg/secring.gpg"):
            self.assertIn(path, self.recipe.split("rm -rf", 1)[1])
        self.assertNotIn("/usr/share/licenses", self.recipe)

    def test_language_browser_and_receiver_selection_remain_separate(self):
        for excluded in ("dotnet", "nodejs", "typescript", "rust", "go", "base-devel", "playwright"):
            self.assertNotIn(excluded, self.inputs["targets"])
        self.assertIn("separately", self.inputs["consumerPreparation"]["framework"])
        self.assertIn("10.0.400", self.inputs["consumerPreparation"]["framework"])
        self.assertIn("global.json rollForward=disable", self.inputs["consumerPreparation"]["framework"])
        self.assertNotIn("10.0.401", self.inputs["consumerPreparation"]["framework"])
        self.assertIn("`rollForward: disable`", self.readme)
        self.assertIn("typescript-todo", self.inputs["consumerPreparation"]["ordinaryProduct"])
        self.assertIn("excluded", self.inputs["consumerPreparation"]["browser"])
        self.assertIn("opt-in", self.inputs["consumerPreparation"]["workspaceSelection"])


if __name__ == "__main__":
    unittest.main()
