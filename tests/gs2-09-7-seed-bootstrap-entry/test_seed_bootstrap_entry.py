import datetime as dt
from contextlib import nullcontext
import importlib.util
import json
import os
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/github-substrate-v2-sandbox-qualification.yml"
SPEC = importlib.util.spec_from_file_location(
    "seed_bootstrap_entry", ROOT / "scripts/gs2-09-7-seed-bootstrap-entry.py")
entry = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(entry)


class EntryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.host = self.root / "host"
        self.candidate = self.root / "coordination"
        self.runtime = self.root / "private-runtime"
        for path in (self.host, self.candidate, self.runtime):
            path.mkdir()
        self.source_path = self.candidate / entry.SOURCE_MANIFEST
        self.source_path.parent.mkdir(parents=True)
        self.plan = b'{"seed":"plan"}\n'
        self.corpus = b'{"seed":"corpus"}\n'
        for name, raw in (("seed-plan.json", self.plan), ("corpus.json", self.corpus)):
            (self.source_path.parent / name).write_bytes(raw)
        candidate = "b" * 40
        nonce = "12345-2-" + candidate
        self.approved = entry.approved_source(candidate, entry.digest(self.plan),
                                              entry.digest(self.corpus))
        self.source = {
            "schema": entry.SOURCE_SCHEMA, "status": "source-only-no-authority",
            "candidateSha": candidate,
            "approvedArtifactSourceSha256": self.approved,
            "seedPlan": {"path": "seed-plan.json", "byteLength": len(self.plan),
                         "sha256": entry.digest(self.plan)},
            "corpus": {"path": "corpus.json", "byteLength": len(self.corpus),
                       "sha256": entry.digest(self.corpus)},
            "postMintSealRequired": True, "bootstrapAuthority": False,
            "providerEffectsAuthorized": False,
        }
        self.source_raw = (json.dumps(self.source, sort_keys=True, separators=(",", ":")) + "\n").encode()
        self.env = {"GITHUB_ACTIONS": "true", "GITHUB_EVENT_NAME": "workflow_dispatch",
                    "GITHUB_REPOSITORY": entry.HOST, "GITHUB_REF": "refs/heads/main",
                    "GITHUB_WORKFLOW_REF": f"{entry.HOST}/{entry.WORKFLOW}@refs/heads/main",
                    "GITHUB_WORKFLOW_SHA": "a" * 40, "GITHUB_SHA": "a" * 40,
                    "FSGG_PROTECTED_SHA": "a" * 40, "FSGG_CANDIDATE_SHA": candidate,
                    "GITHUB_RUN_ID": "12345", "GITHUB_RUN_ATTEMPT": "2",
                    "FSGG_SEED_SOURCE_MANIFEST_SHA256": entry.digest(self.source_raw)}
        self.derived_files = {name: (name + "\n").encode() for name in entry.FILES}
        self.derived_files["seed-plan.json"] = self.plan
        self.derived_files["corpus.json"] = self.corpus
        self.derived = {
            "schema": entry.DERIVED_SCHEMA, "status": "source-only-no-authority",
            "candidateSha": candidate, "workflowRunId": 12345,
            "workflowRunAttempt": 2, "workflowSha": "a" * 40,
            "runNonce": nonce,
            "refName": f"refs/heads/gs2-09-7/{nonce}/seed-journal",
            "journalGeneration": 0, "stateGeneration": 0, "expectedParent": None,
            "stateSha256": entry.digest(self.derived_files["journal/state.json"]),
            "blobOid": "1" * 40, "treeOid": "2" * 40, "commitOid": "3" * 40,
            "bootstrapAuthority": False, "providerEffectsAuthorized": False,
            "files": [{"path": name, "byteLength": len(self.derived_files[name]),
                       "sha256": entry.digest(self.derived_files[name])} for name in entry.FILES],
        }

    def write_source(self):
        self.source_path.write_bytes(self.source_raw)

    def write_derived(self):
        (self.runtime / entry.DERIVED_MANIFEST).write_text(
            json.dumps(self.derived, sort_keys=True, separators=(",", ":")) + "\n")
        for name, raw in self.derived_files.items():
            path = self.runtime / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(raw)

    def test_source_only_refuses_before_mint(self):
        with self.assertRaisesRegex(entry.Refused, "bootstrap-source-uninstalled"):
            entry.preflight(self.host, self.candidate, self.env)
        self.assertFalse(self.source_path.exists())

    def test_static_source_digest_files_and_admission_refuse(self):
        with mock.patch.object(entry, "INSTALLATION_STATUS", "installed-protected-bootstrap"):
            with self.assertRaisesRegex(entry.Refused, "source-producer-uninstalled"):
                entry.preflight(self.host, self.candidate, self.env)
            self.write_source()
            changed = {**self.env, "FSGG_SEED_SOURCE_MANIFEST_SHA256": "f" * 64}
            with self.assertRaisesRegex(entry.Refused, "source-manifest-digest"):
                entry.preflight(self.host, self.candidate, changed)
            with self.assertRaisesRegex(entry.Refused, "prepare-admission-unconfigured"):
                entry.preflight(self.host, self.candidate, self.env)
            (self.source_path.parent / "seed-plan.json").write_bytes(b"drift")
            with self.assertRaisesRegex(entry.Refused, "source-file-drift"):
                entry.preflight(self.host, self.candidate, self.env)

    def test_wrong_protected_context_and_symlink_refuse(self):
        for name, changed in (("GITHUB_REF", "refs/heads/feature"),
                              ("GITHUB_WORKFLOW_SHA", "c" * 40),
                              ("FSGG_CANDIDATE_SHA", "C" * 40)):
            with self.subTest(name=name), self.assertRaises(entry.Refused):
                entry.preflight(self.host, self.candidate, {**self.env, name: changed})
        self.write_source()
        path = self.source_path.parent / "seed-plan.json"
        path.unlink()
        path.symlink_to(self.source_path)
        with mock.patch.object(entry, "INSTALLATION_STATUS", "installed-protected-bootstrap"):
            with self.assertRaisesRegex(entry.Refused, "source-file-unavailable"):
                entry.preflight(self.host, self.candidate, self.env)

    def test_postmint_artifacts_are_distinct_and_raw_bound(self):
        self.write_derived()
        manifest, retained = entry.validate_artifacts(self.runtime, entry.context(self.env))
        self.assertEqual(self.plan, retained["seed-plan.json"])
        self.assertEqual(self.derived["commitOid"], manifest["commitOid"])
        (self.runtime / "journal/tree.raw").write_bytes(b"changed")
        with self.assertRaisesRegex(entry.Refused, "manifest-file-drift"):
            entry.validate_artifacts(self.runtime, entry.context(self.env))

    def test_workflow_routes_static_request_before_secret_and_run(self):
        seed = WORKFLOW.read_text().split("  seed-bootstrap:\n", 1)[1]
        self.assertIn("if: inputs.seed_source_manifest_sha256 != ''", seed)
        self.assertIn("environment: github-substrate-v2-sandbox", seed)
        self.assertIn("gs2-09-7-seed-bootstrap-entry.py preflight", seed)
        self.assertIn("gs2-09-7-seed-bootstrap-entry.py run", seed)
        self.assertLess(seed.index("gs2-09-7-seed-bootstrap-entry.py preflight"),
                        seed.index("secrets.FSGG_DISPATCH_APP_PRIVATE_KEY"))
        self.assertIn("native-readback.json", seed)
        self.assertNotIn("gs2-09-7-mint-sandbox-token.py", seed)

    def test_prestate_capture_requires_exact_nonce_and_fresh_native_source(self):
        now = dt.datetime.now(dt.timezone.utc)
        raw = json.dumps({"schema": entry.PRESTATE_SCHEMA, "complete": True,
                          "repositoryId": 1353050537,
                          "projectNodeId": "PVT_kwDOEYAWY84BiESo",
                          "nonceIssueCount": 0, "nonceProjectItemCount": 0,
                          "snapshotSha256": "c" * 64}).encode()
        capture = {"runNonce": entry.context(self.env)["runNonce"], "raw": raw,
                   "captureId": "d" * 64,
                   "observedAt": now.isoformat().replace("+00:00", "Z"),
                   "source": "fresh-native-issue-and-project-pages"}
        with mock.patch.object(entry, "PRESTATE_PRODUCER_PORT",
                               SimpleNamespace(produce=lambda *_: capture)):
            self.assertEqual(raw, entry.produce_prestate("token", entry.context(self.env)))
            capture["runNonce"] = "foreign"
            with self.assertRaisesRegex(entry.Refused, "capture"):
                entry.produce_prestate("token", entry.context(self.env))

    def test_run_refuses_before_mint_while_installation_is_unavailable(self):
        self.write_source()
        mint = mock.Mock()
        with mock.patch.object(entry, "mint_private", mint):
            with self.assertRaisesRegex(entry.Refused, "bootstrap-source-uninstalled"):
                entry.run_protected(self.host, self.candidate, self.env)
        mint.assert_not_called()

    def test_coordination_seal_uses_exact_flags_and_excludes_credentials(self):
        self.write_source()
        facts = entry.context(self.env)
        output = self.runtime / "sealed"
        completed = SimpleNamespace(returncode=0,
                                    stdout=str(output / entry.DERIVED_MANIFEST).encode() + b"\n")
        with mock.patch.dict(os.environ, {
            "FSGG_DISPATCH_APP_PRIVATE_KEY": "private-key",
            "FSGG_SANDBOX_TOKEN": "installation-token",
            "ACTIONS_RUNTIME_TOKEN": "actions-token"}), \
             mock.patch.object(entry.subprocess, "run", return_value=completed) as run:
            self.assertEqual(output, entry.seal_with_coordination(
                self.candidate, facts, self.runtime, self.source,
                self.runtime / "s2-declaration.json", self.runtime / "mint-grants.json",
                self.runtime / "prestate.json"))
        command = run.call_args.args[0]
        self.assertEqual(command[:5], ["dotnet", "run", "--project",
                                       entry.COORDINATION_CLI_PROJECT, "--"])
        self.assertEqual(command[5:7], ["seed-bootstrap-artifacts", "seal"])
        self.assertIn("--prestate", command)
        self.assertIn("--source-manifest", command)
        self.assertNotIn("FSGG_SANDBOX_TOKEN", run.call_args.kwargs["env"])
        self.assertNotIn("FSGG_DISPATCH_APP_PRIVATE_KEY", run.call_args.kwargs["env"])
        self.assertNotIn("ACTIONS_RUNTIME_TOKEN", run.call_args.kwargs["env"])

    def test_private_mint_retains_only_sanitized_proof_and_revokes_on_failure(self):
        token = "t" * 30
        minted_raw = json.dumps({"token": token}).encode()
        responses = [
            ({"id": 4166418, "slug": "fs-gg-cross-repo-dispatch"}, b"{}"),
            ({"id": 143110413, "app_id": 4166418,
              "app_slug": "fs-gg-cross-repo-dispatch",
              "account": {"login": "FS-GG"}}, b"{}"),
            ({"token": token}, minted_raw),
            ({"data": {"viewer": {"login": "fs-gg-cross-repo-dispatch[bot]",
                                  "databaseId": 297630107}}}, b'{"viewer":"raw"}'),
        ]
        module = SimpleNamespace(
            APP_SLUG="fs-gg-cross-repo-dispatch", OWNER="FS-GG",
            REPOSITORY="FS.GG.GitHub.Substrate.Sandbox",
            PERMISSIONS={"contents": "write"},
            APP_ACTOR="fs-gg-cross-repo-dispatch[bot]", APP_ACTOR_ID=297630107,
            app_jwt=lambda *_: "jwt", request_json=mock.Mock(side_effect=responses),
            validate_mint_response=lambda _: {"tokenSha256": entry.digest(token.encode())},
            revoke_token=mock.Mock())
        environment = {"FSGG_DISPATCH_APP_ID": "4166418",
                       "FSGG_DISPATCH_APP_PRIVATE_KEY": "PRIVATE KEY"}
        returned, proof = entry.mint_private(module, environment)
        self.assertEqual(token, returned)
        self.assertNotIn(token.encode(), proof)
        self.assertEqual(entry.digest(minted_raw), json.loads(proof)["mintResponseSha256"])
        module.revoke_token.assert_not_called()
        responses[-1] = ({"data": {"viewer": {"login": "foreign", "databaseId": 9}}}, b"{}")
        module.request_json = mock.Mock(side_effect=responses)
        with self.assertRaisesRegex(entry.Refused, "mint-actor"):
            entry.mint_private(module, environment)
        module.revoke_token.assert_called_once_with(token)

    def test_pending_native_readback_is_sanitized_and_token_is_revoked(self):
        facts = entry.context(self.env)
        environment = {**self.env,
                       "FSGG_SEED_SANITIZED_EVIDENCE_DIR": str(self.root / "sanitized")}
        source = {"approvedArtifactSourceSha256": self.approved}
        order = []
        mint = SimpleNamespace(
            request_json=mock.Mock(side_effect=[
                ({"id": 1353050537}, b'{"id":1353050537}'),
                ({"data": {"organization": {"projectV2": {"id": "PVT_kwDOEYAWY84BiESo"}}}}, b"{}")]),
            revoke_token=mock.Mock(side_effect=lambda _: order.append("revoke")))
        binding = SimpleNamespace(build_document=lambda: (order.append("s2"), b"{}\n")[1])
        cas = SimpleNamespace(authenticated_port=lambda _: nullcontext(object()),
                              canonical=lambda report: (json.dumps(report) + "\n").encode())
        report = {"complete": False, "status": "pending"}
        with mock.patch.object(entry, "preflight", return_value=facts), \
             mock.patch.object(entry, "require_postmint_ready", return_value=(binding, cas, object())), \
             mock.patch.object(entry, "validate_source_artifacts",
                               return_value=(source, {"seed-plan.json": self.plan,
                                                     "corpus.json": self.corpus})), \
             mock.patch.object(entry, "load_sibling", return_value=mint), \
             mock.patch.object(entry, "mint_private",
                               side_effect=lambda *_: (order.append("mint"), ("t" * 30, b"{}\n"))[1]), \
             mock.patch.object(entry, "produce_prestate",
                               side_effect=lambda *_: (order.append("prestate"), b"{}\n")[1]), \
             mock.patch.object(entry, "seal_with_coordination",
                               side_effect=lambda *_: (order.append("seal"), self.runtime)[1]), \
             mock.patch.object(entry, "execute_source",
                               side_effect=lambda *_: (order.append("cas"), report)[1]):
            with self.assertRaisesRegex(entry.Refused, "native-readback-pending"):
                entry.run_protected(self.host, self.candidate, environment)
        self.assertEqual(["mint", "prestate", "s2", "seal", "cas", "revoke"], order)
        self.assertEqual(report, json.loads((self.root / "sanitized/native-readback.json").read_bytes()))

    def test_composed_source_never_calls_git_without_admission(self):
        self.write_source()
        self.write_derived()
        ref = self.derived["refName"]
        native_apply = mock.Mock()
        cas = SimpleNamespace(inspect_proposal=lambda _: {"ref": ref},
                              verify_declaration=lambda *_: None,
                              strict_json=lambda _: {"source": {"approvedArtifactSourceSha256": self.approved}},
                              apply=native_apply)
        binding = SimpleNamespace(validate_mint=lambda *_: {"appId": 4166418,
                                                            "installationId": 143110413,
                                                            "proofSha256": "9" * 64,
                                                            "tokenSha256": "8" * 64})
        admission = SimpleNamespace(require_admitted=mock.Mock(side_effect=RuntimeError("no authority")))
        modules = {"gs2_seed_native_cas": cas, "gs2_seed_execution_binding": binding,
                   "gs2_seed_bootstrap_admission": admission}
        repository = b'{"id":1353050537,"node_id":"R_kgDOUKXpqQ","full_name":"FS-GG/FS.GG.GitHub.Substrate.Sandbox","private":true,"fork":false}'
        project = b'{"id":"PVT_kwDOEYAWY84BiESo","title":"fsgg-sandbox-gs2-04-9","closed":false,"public":false}'
        with mock.patch.object(entry, "INSTALLATION_STATUS", "installed-protected-bootstrap"), \
             mock.patch.object(entry, "load_sibling", side_effect=lambda name, _: modules[name]):
            with self.assertRaisesRegex(RuntimeError, "no authority"):
                entry.execute_source(self.env, self.candidate, self.runtime, b"mint", "token",
                                     repository, project, object(), object(),
                                     dt.datetime.now(dt.timezone.utc))
        native_apply.assert_not_called()
        admission.require_admitted.assert_called_once()


if __name__ == "__main__":
    unittest.main()
