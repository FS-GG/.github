#!/usr/bin/env python3
"""Inert common-base source controls; no package/container/network execution."""
import json
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
        self.assertIn("cmp package-inventory.expected /opt/fsgg/package-inventory.txt", self.recipe)

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
        self.assertEqual([command.strip().split()[0] for command in guard.split(";") if command.strip()],
                         ["set", "cd", "test", "test", "test", "test", "sha256sum"])
        with tempfile.TemporaryDirectory() as temporary:
            context = Path(temporary)
            for relative in ("repositories/core/os/x86_64/core.db", "repositories/extra/os/x86_64/extra.db"):
                path = context / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b"inert-database")
            (context / "package-inventory.expected").write_bytes(b"inert 1\n")
            checks = context / "SHA256SUMS"
            checks.write_text("0" * 64 + "  repositories/core/os/x86_64/missing.pkg.tar.zst\n")
            bounded = guard.replace("/opt/fsgg/acquired", str(context))
            failed = subprocess.run(["/bin/sh", "-c", bounded], capture_output=True, timeout=5)
            self.assertNotEqual(failed.returncode, 0)
            self.assertIn(b"missing.pkg.tar.zst", failed.stderr)
            checks.write_bytes(b"")
            empty = subprocess.run(["/bin/sh", "-c", bounded], capture_output=True, timeout=5)
            self.assertNotEqual(empty.returncode, 0)
            self.assertEqual(empty.stderr, b"")

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
        self.assertIn("typescript-todo", self.inputs["consumerPreparation"]["ordinaryProduct"])
        self.assertIn("excluded", self.inputs["consumerPreparation"]["browser"])
        self.assertIn("opt-in", self.inputs["consumerPreparation"]["workspaceSelection"])


if __name__ == "__main__":
    unittest.main()
