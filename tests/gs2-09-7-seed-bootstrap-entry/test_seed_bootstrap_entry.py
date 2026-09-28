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
        self.source = {
            "schema": entry.SOURCE_SCHEMA, "status": "source-only-no-authority",
            "candidateSha": candidate,
            "seedPlan": {"path": "seed-plan.json", "byteLength": len(self.plan),
                         "sha256": entry.digest(self.plan)},
            "corpus": {"path": "corpus.json", "byteLength": len(self.corpus),
                       "sha256": entry.digest(self.corpus)},
            "postMintSealRequired": True, "bootstrapAuthority": False,
            "providerEffectsAuthorized": False,
        }
        self.source_raw = json.dumps(self.source, separators=(",", ":")).encode()
        self.approved = entry.digest(self.source_raw)
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

    def test_raw_manifest_digest_is_approved_source_and_noncanonical_bytes_refuse(self):
        self.write_source()
        facts = entry.context(self.env)
        manifest, _ = entry.validate_source_artifacts(self.candidate, facts)
        self.assertEqual(entry.digest(self.source_raw),
                         manifest["approvedArtifactSourceSha256"])
        self.assertNotIn(b"approvedArtifactSourceSha256", self.source_raw)
        changed = self.source_raw + b"\n"
        self.source_path.write_bytes(changed)
        with self.assertRaisesRegex(entry.Refused, "source-manifest-noncanonical"):
            entry.validate_source_artifacts(
                self.candidate, {**facts, "manifestSha256": entry.digest(changed)})

    def test_final_admission_binds_private_prestate_evidence_and_ref_absence(self):
        facts = entry.context(self.env)
        root = self.root / "private"
        root.mkdir(mode=0o700)
        summary = {"schema": entry.PRESTATE_SCHEMA, "complete": True,
                   "repositoryId": 1353050537,
                   "projectNodeId": "PVT_kwDOEYAWY84BiESo",
                   "nonceIssueCount": 0, "nonceProjectItemCount": 0,
                   "snapshotSha256": "c" * 64}
        summary_raw = json.dumps(summary, separators=(",", ":")).encode()
        entry.write_private(root / "prestate.json", summary_raw)
        entry.write_private(self.runtime / "prestate.json", summary_raw)
        ref = f'refs/heads/gs2-09-7/{facts["runNonce"]}/seed-journal'
        snapshot = {"ref": {"expectedAbsent": True}}
        pass_record = {"snapshot": snapshot,
                       "requests": [{"status": 200}] * 5 + [{"status": 404}]}
        evidence = {"schema": "fsgg.gs2-09-7.sandbox-seed-prestate-evidence/1",
                    "runNonce": facts["runNonce"], "refName": ref,
                    "repositoryId": 1353050537,
                    "projectNodeId": "PVT_kwDOEYAWY84BiESo",
                    "expectedRefAbsent": True,
                    "snapshotSha256": summary["snapshotSha256"],
                    "summarySha256": entry.digest(summary_raw),
                    "observedAt": "2026-09-28T08:00:00Z",
                    "passes": [pass_record, pass_record], "complete": True}
        evidence_raw = json.dumps(evidence, separators=(",", ":")).encode()
        entry.write_private(root / "prestate-evidence.private.json", evidence_raw)
        subject = {"refName": ref,
                   "approvedArtifactSourceSha256": facts["manifestSha256"]}
        bound = entry.attach_final_prestate(subject, root, self.runtime, facts)
        self.assertEqual(entry.digest(evidence_raw), bound["prestateEvidenceSha256"])
        self.assertEqual(entry.digest(summary_raw), bound["prestateSha256"])
        self.assertTrue(bound["expectedRefAbsent"])
        evidence["expectedRefAbsent"] = False
        (root / "prestate-evidence.private.json").write_bytes(
            json.dumps(evidence, separators=(",", ":")).encode())
        with self.assertRaisesRegex(entry.Refused, "final-prestate-evidence"):
            entry.attach_final_prestate(subject, root, self.runtime, facts)

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

    def test_workflow_stages_two_native_decisions_before_effect(self):
        seed = WORKFLOW.read_text().split("  seed-bootstrap:\n", 1)[1]
        self.assertIn("if: inputs.seed_source_manifest_sha256 != ''", seed)
        self.assertIn("environment: github-substrate-v2-sandbox", seed)
        self.assertIn("timeout-minutes: 40", seed)
        self.assertIn("actions: read", seed)
        self.assertIn("gs2-09-7-seed-bootstrap-entry.py prepare-request", seed)
        self.assertIn("gs2-09-7-seed-bootstrap-entry.py seal-input", seed)
        self.assertIn("gs2-09-7-seed-bootstrap-entry.py consume-seal-and-request-final", seed)
        self.assertIn("  seed-seal:", seed)
        self.assertIn("gs2-09-7-seed-seal-exchange.py run-seal", seed)
        self.assertNotIn("needs: seed-bootstrap", seed)
        self.assertIn("gs2-09-7-seed-bootstrap-entry.py final-apply", seed)
        self.assertLess(seed.index("gs2-09-7-seed-bootstrap-entry.py prepare-request"),
                        seed.index("secrets.FSGG_DISPATCH_APP_PRIVATE_KEY"))
        self.assertLess(seed.index("seed-admission-request-prepare-"),
                        seed.index("seed-admission-request-final-"))
        self.assertLess(seed.index("seed-admission-request-final-"),
                        seed.index("gs2-09-7-seed-bootstrap-entry.py final-apply"))
        self.assertIn("gs2-09-7-seed-bootstrap-entry.py revoke-private", seed)
        self.assertIn("FSGG_SEED_SANITIZED_EVIDENCE_DIR", seed)
        self.assertNotIn("gs2-09-7-mint-sandbox-token.py", seed)

    def test_prestate_capture_requires_exact_nonce_and_fresh_native_source(self):
        now = dt.datetime.now(dt.timezone.utc)
        raw = json.dumps({"schema": entry.PRESTATE_SCHEMA, "complete": True,
                          "repositoryId": 1353050537,
                          "projectNodeId": "PVT_kwDOEYAWY84BiESo",
                          "nonceIssueCount": 0, "nonceProjectItemCount": 0,
                          "snapshotSha256": "c" * 64}).encode()
        facts = entry.context(self.env)
        snapshot = {"ref": {"expectedAbsent": True}}
        record = {"snapshot": snapshot, "requests": [{"status": 200}] * 5 + [{"status": 404}]}
        evidence = {"schema": "fsgg.gs2-09-7.sandbox-seed-prestate-evidence/1",
                    "runNonce": facts["runNonce"],
                    "refName": f'refs/heads/gs2-09-7/{facts["runNonce"]}/seed-journal',
                    "repositoryId": 1353050537, "projectNodeId": "PVT_kwDOEYAWY84BiESo",
                    "expectedRefAbsent": True, "snapshotSha256": "c" * 64,
                    "summarySha256": entry.digest(raw),
                    "observedAt": now.isoformat().replace("+00:00", "Z"),
                    "passes": [record, record], "complete": True}
        evidence_raw = json.dumps(evidence).encode()
        capture = {"runNonce": entry.context(self.env)["runNonce"], "raw": raw,
                   "captureId": entry.digest(evidence_raw), "evidenceRaw": evidence_raw,
                   "observedAt": now.isoformat().replace("+00:00", "Z"),
                   "source": "fresh-native-issue-project-and-ref-pages"}
        with mock.patch.object(entry, "PRESTATE_PRODUCER_PORT",
                               SimpleNamespace(produce=lambda *_: capture)):
            self.assertEqual(raw, entry.produce_prestate("token", entry.context(self.env)))
            capture["runNonce"] = "foreign"
            with self.assertRaisesRegex(entry.Refused, "capture"):
                entry.produce_prestate("token", entry.context(self.env))

    def test_staged_prepare_refuses_before_request_and_mint_when_uninstalled(self):
        self.write_source()
        environment = {**self.env, "FSGG_SEED_PRIVATE_DIR": str(self.root / "private")}
        mint = mock.Mock()
        with mock.patch.object(entry, "mint_private", mint):
            with self.assertRaisesRegex(entry.Refused, "bootstrap-source-uninstalled"):
                entry.stage_prepare_request(self.host, self.candidate, environment)
        self.assertFalse((self.root / "private").exists())
        mint.assert_not_called()

    def test_private_request_refuses_changed_bytes(self):
        self.write_source()
        environment = {**self.env, "FSGG_SEED_PRIVATE_DIR": str(self.root / "private"),
                       "GH_TOKEN": "g" * 30}
        with mock.patch.object(entry, "INSTALLATION_STATUS", "installed-protected-bootstrap"), \
             mock.patch.object(entry, "native_host_start",
                               return_value=(7, dt.datetime.now(dt.timezone.utc))):
            subject = entry.stage_prepare_request(self.host, self.candidate, environment)
        root = self.root / "private"
        self.assertEqual(subject, entry.read_request(root, "prepare"))
        path = entry.request_file(root, "prepare")
        path.write_bytes(path.read_bytes().replace(b"prepare-only-no-effect", b"foreign-operation"))
        with self.assertRaises(ValueError):
            entry.read_request(root, "prepare")

    def test_altered_returned_s2_is_rejected_before_final_request(self):
        files = {"s2-declaration.json": b"original",
                 "seed-plan.json": self.plan, "corpus.json": self.corpus}
        retained = {"seed-plan.json": self.plan, "corpus.json": self.corpus}
        entry.require_returned_seal_inputs(files, retained, b"original")
        with self.assertRaisesRegex(entry.Refused, "seal-returned-input-drift"):
            entry.require_returned_seal_inputs({**files, "s2-declaration.json": b"altered"},
                                               retained, b"original")

    def test_revoke_failure_preserves_private_token_and_pending_readback(self):
        root = self.root / "private"
        root.mkdir(mode=0o700)
        token_path = root / "installation-token.private"
        entry.write_private(token_path, b"t" * 30)
        entry.write_private(root / "cas-readback.private.json", b'{"complete":false}\n')
        sanitized = self.root / "sanitized"
        entry.write_revocation_pending(
            root, {"complete": False},
            {"FSGG_SEED_SANITIZED_EVIDENCE_DIR": str(sanitized)})
        revoke = mock.Mock(side_effect=ValueError("response-unknown"))
        with mock.patch.object(entry, "load_sibling", return_value=SimpleNamespace(revoke_token=revoke)), \
             mock.patch.dict(os.environ, {"FSGG_SEED_SANITIZED_EVIDENCE_DIR": str(sanitized)}):
            with self.assertRaisesRegex(ValueError, "response-unknown"):
                entry.revoke_private_token(root)
            self.assertTrue(token_path.exists())
            pending = json.loads((sanitized / "revocation-pending.json").read_bytes())
            self.assertEqual("revocation-pending", pending["status"])
            self.assertFalse(pending["activation"])
            self.assertFalse((sanitized / "native-readback.json").exists())
            revoke.side_effect = None
            self.assertTrue(entry.revoke_private_token(root))
        self.assertFalse(token_path.exists())
        self.assertFalse((sanitized / "revocation-pending.json").exists())
        self.assertEqual(b'{"complete":false}\n',
                         (sanitized / "native-readback.json").read_bytes())

    def test_native_host_start_and_effect_reserve_are_exact_run_bound(self):
        facts = entry.context(self.env)
        now = dt.datetime.now(dt.timezone.utc).replace(microsecond=0)
        job = {"id": 77, "name": "gs2-09-7-seed-bootstrap-admission",
               "run_id": facts["runId"], "head_sha": facts["workflowSha"],
               "status": "in_progress", "conclusion": None,
               "started_at": now.isoformat().replace("+00:00", "Z")}
        bridge = SimpleNamespace(REPO="repos/FS-GG/.github",
                                 get_json=lambda *_: {"total_count": 1, "jobs": [job]})
        with mock.patch.object(entry, "bridge_module", return_value=bridge):
            job_id, started = entry.native_host_start(object(), facts)
            self.assertEqual(77, job_id)
            self.assertEqual(now, started)
            job["head_sha"] = "f" * 40
            with self.assertRaisesRegex(entry.Refused, "host-job-binding"):
                entry.native_host_start(object(), facts)
        root = self.root / "deadline"
        root.mkdir(mode=0o700)
        record = {"schema": "fsgg.gs2-09-7.seed-host-deadline/1",
                  "jobId": 77, "runId": facts["runId"],
                  "runAttempt": facts["runAttempt"],
                  "workflowSha": facts["workflowSha"],
                  "startedAt": now.isoformat().replace("+00:00", "Z"),
                  "deadlineAt": (now + dt.timedelta(minutes=35)).isoformat().replace("+00:00", "Z")}
        entry.write_private(root / "deadline.json", json.dumps(record).encode())
        self.assertGreater(entry.deadline_remaining(root, facts, reserve_seconds=300,
                                                     now=now + dt.timedelta(minutes=10)), 0)
        with self.assertRaisesRegex(entry.Refused, "host-deadline-expired"):
            entry.deadline_remaining(root, facts, reserve_seconds=300,
                                     now=now + dt.timedelta(minutes=31))

    def test_final_nonce_prestate_drift_refuses_before_cas(self):
        root = self.root / "private"
        root.mkdir(mode=0o700)
        entry.write_private(root / "installation-token.private", b"t" * 30)
        env = {**self.env, "FSGG_SEED_PRIVATE_DIR": str(root), "GH_TOKEN": "g" * 30}
        subject = {"runId": 12345}
        bridge = SimpleNamespace(
            GitHubReadPort=lambda _: object(),
            require_protected_main=lambda *_: None,
            wait_decision=lambda *_args, **_kwargs: object())
        execute = mock.Mock()
        with mock.patch.object(entry, "INSTALLATION_STATUS", "installed-protected-bootstrap"), \
             mock.patch.object(entry, "read_request", return_value=subject), \
             mock.patch.object(entry, "bridge_module", return_value=bridge), \
             mock.patch.object(entry, "deadline_remaining", return_value=30), \
             mock.patch.object(entry, "require_postmint_ready", return_value=(object(), object(), object())), \
             mock.patch.object(entry, "load_sibling", return_value=object()), \
             mock.patch.object(entry, "produce_prestate_capture", side_effect=entry.Refused("nonce-nonzero")), \
             mock.patch.object(entry, "revoke_private_token") as revoke, \
             mock.patch.object(entry, "execute_source", execute):
            with self.assertRaisesRegex(entry.Refused, "nonce-nonzero"):
                entry.stage_final_apply(self.host, self.candidate, env)
        execute.assert_not_called()
        revoke.assert_called_once_with(root)

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
             mock.patch.object(entry, "private_root", return_value=self.root), \
             mock.patch.object(entry, "attach_final_prestate", side_effect=lambda subject, *_: subject), \
             mock.patch.object(entry, "load_sibling", side_effect=lambda name, _: modules[name]):
            with self.assertRaisesRegex(RuntimeError, "no authority"):
                entry.execute_source(self.env, self.candidate, self.runtime, b"mint", "token",
                                     repository, project, object(), object(),
                                     dt.datetime.now(dt.timezone.utc))
        native_apply.assert_not_called()
        admission.require_admitted.assert_called_once()


if __name__ == "__main__":
    unittest.main()
