import json
import argparse
import copy
import hashlib
from unittest import mock
import importlib.util
import os
import pathlib
import subprocess
import sys
import tempfile
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[2]
TOOL = ROOT / "tools/pr-lane-admission.py"
FAKE_GH = ROOT / "tests/pr-lane-admission/fake_gh.py"
EXPECTED_SHA = "a" * 40


def managed_body(campaign: str, chain: str) -> str:
    return (
        "Existing PR body\n\n"
        f"<!-- fsgg-pr-lane-admission:v1 campaign={campaign} chain={chain} -->\n"
    )


class PullRequestLaneAdmissionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        def inert(event, args):
            if event == "socket.connect":
                raise RuntimeError("offline qualification refuses network")
            if event == "subprocess.Popen":
                argv = args[1]
                executable = pathlib.Path(argv[0]).name
                if executable != "git" and not (argv[0] == sys.executable and str(TOOL) in argv) and argv[0] != str(FAKE_GH):
                    raise RuntimeError("offline qualification refuses nonfixture child")
        sys.addaudithook(inert)

    def invoke(self, state, body="First line\nSecond line\n", branch="feature/combined", head=EXPECTED_SHA, extra=(), repo="FS-GG/example"):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        directory = pathlib.Path(temporary.name)
        state_path = directory / "state.json"
        log_path = directory / "calls.jsonl"
        body_path = directory / "body.md"
        state_path.write_text(json.dumps(state), encoding="utf-8")
        body_path.write_text(body, encoding="utf-8")
        environment = dict(os.environ)
        environment.update(FAKE_GH_STATE=str(state_path), FAKE_GH_LOG=str(log_path))
        completed = subprocess.run(
            [
                sys.executable,
                "-I", "-S", "-B",
                str(TOOL),
                "--repo",
                repo,
                "--campaign-id",
                "campaign-7",
                "--dependency-chain-id",
                "chain-3",
                "--head",
                branch,
                "--head-sha",
                head,
                "--base",
                "main",
                "--title",
                "Deliver coherent feature",
                "--body-file",
                str(body_path),
                "--gh",
                str(FAKE_GH),
                *extra,
            ],
            env=environment,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
        )
        calls = []
        if log_path.exists():
            calls = [json.loads(line) for line in log_path.read_text().splitlines()]
        return completed, json.loads(completed.stdout), calls

    def update_state(self, body=None):
        # Real GET identity/authority shape; guard coverage cannot rely on omitted fields/defaults.
        owner = {"id": 7, "node_id": "O_owner", "login": "FS-GG", "type": "Organization"}
        author = {"id": 8, "node_id": "U_author", "login": "fixture", "type": "User"}
        repository = {"id": 9, "node_id": "R_repository", "full_name": "FS-GG/example", "owner": owner,
                      "private": True, "archived": False, "disabled": False, "visibility": "private",
                      "permissions": {"admin": False, "maintain": True, "push": True, "triage": True, "pull": True}}
        value = {"head_sha": EXPECTED_SHA, "pull": {
            "number": 42, "id": 1042, "node_id": "PR_fixture", "state": "open", "merged": False,
            "draft": False, "locked": False, "maintainer_can_modify": True, "author_association": "MEMBER",
            "user": author, "html_url": "https://github.com/FS-GG/example/pull/42", "title": "Deliver coherent feature",
            "head": {"ref": "routine/fixture", "sha": EXPECTED_SHA, "repo": repository, "user": owner},
            "base": {"ref": "main", "sha": "c" * 40, "repo": repository, "user": owner},
            "mergeable": None, "rebaseable": None, "mergeable_state": "unknown", "merge_commit_sha": "d" * 40,
            "body": body if body is not None else managed_body("campaign-7", "chain-3") +
                "<!-- fsgg:routine-development/v1 head=" + "b" * 40 + " operation=source-change extra=value -->\n"}}
        return json.loads(json.dumps(value))

    def update(self, state, body="Revised prose\n", repo="FS-GG/example", extra=()):
        return self.invoke(state, body, "routine/fixture", extra=(
            "--update-pr", "42", "--previous-head-sha", "b" * 40, *extra), repo=repo)

    def legacy_state(self):
        state = self.update_state()
        state["pull"]["body"] = state["pull"]["body"].replace(
            managed_body("campaign-7", "chain-3").split("\n\n")[1], "")
        state["pull"]["body"] += "<!-- fsgg:future/v8 opaque=preserve-me -->\n"
        state["open_pages"] = [[copy.deepcopy(state["pull"])]]
        return state

    def test_legacy_adoption_is_explicit_preserves_metadata_and_uses_independent_readback(self):
        state = self.legacy_state()
        completed, result, calls = self.update(state)
        self.assertEqual(3, completed.returncode)
        self.assertEqual("update-metadata-conflict", result["reason"])
        self.assertFalse(any(call["arguments"][2] == "PATCH" for call in calls))
        completed, result, calls = self.update(state, extra=("--adopt-legacy-pr",))
        self.assertEqual(0, completed.returncode, completed.stdout)
        self.assertEqual(("updated", 42, EXPECTED_SHA), (result["action"], result["number"], result["headSha"]))
        patches = [call for call in calls if call["arguments"][2] == "PATCH"]
        self.assertEqual(1, len(patches))
        payload = json.loads(patches[0]["stdin"])
        self.assertEqual({"body", "title"}, set(payload))
        self.assertEqual(1, payload["body"].count("fsgg-pr-lane-admission"))
        self.assertIn("campaign=campaign-7 chain=chain-3", payload["body"])
        self.assertIn("head=" + EXPECTED_SHA + " operation=source-change extra=value", payload["body"])
        self.assertIn("<!-- fsgg:future/v8 opaque=preserve-me -->", payload["body"])
        self.assertEqual(2, sum("--paginate" in call["arguments"] for call in calls))
        self.assertEqual(["GET", "/repos/FS-GG/example/pulls/42"], calls[-1]["arguments"][2:])
        state["pull"].update(payload)
        completed, result, calls = self.update(state, extra=("--adopt-legacy-pr",))
        self.assertEqual(0, completed.returncode, completed.stdout)
        self.assertEqual("unchanged", result["action"])
        self.assertFalse(any(call["arguments"][2] == "PATCH" for call in calls))

    def test_legacy_adoption_flag_requires_update_and_inspect_never_writes(self):
        completed, _, calls = self.invoke({}, extra=("--adopt-legacy-pr",))
        self.assertNotEqual(0, completed.returncode)
        self.assertEqual([], calls)
        completed, result, calls = self.update(self.legacy_state(), extra=("--adopt-legacy-pr", "--inspect"))
        self.assertEqual(0, completed.returncode, completed.stdout)
        self.assertEqual("admitted", result["action"])
        self.assertFalse(any(call["arguments"][2] in {"PATCH", "POST"} for call in calls))

    def test_legacy_adoption_refuses_damaged_conflicting_or_invented_metadata(self):
        footprints = ["<!-- fsgg-pr-lane-admission", managed_body("other", "chain-3"),
                      managed_body("campaign-7", "chain-3") * 2,
                      "<!-- fsgg-pr-lane-admission:v2 campaign=campaign-7 chain=chain-3 -->"]
        for footprint in footprints:
            state = self.legacy_state()
            state["pull"]["body"] += footprint
            completed, result, calls = self.update(state, extra=("--adopt-legacy-pr",))
            self.assertEqual(3, completed.returncode, completed.stdout)
            self.assertFalse(result["writeAttempted"])
            self.assertFalse(any(call["arguments"][2] == "PATCH" for call in calls))
        for body in ["No routine marker", self.legacy_state()["pull"]["body"].replace("b" * 40, "c" * 40),
                     self.legacy_state()["pull"]["body"].replace("source-change", "publish")]:
            state = self.legacy_state()
            state["pull"]["body"] = body
            completed, _, calls = self.update(state, extra=("--adopt-legacy-pr",))
            self.assertEqual(3, completed.returncode, completed.stdout)
            self.assertFalse(any(call["arguments"][2] == "PATCH" for call in calls))
        for proposed in ["<!-- fsgg:future/v8 replaced=yes -->", managed_body("other", "other")]:
            completed, _, calls = self.update(self.legacy_state(), proposed, extra=("--adopt-legacy-pr",))
            self.assertEqual(3, completed.returncode, completed.stdout)
            self.assertFalse(any(call["arguments"][2] == "PATCH" for call in calls))

    def test_legacy_adoption_requires_current_complete_noncompeting_population(self):
        for stage in (0, 1):
            for kind in ("same-chain", "capacity", "unknown", "damaged", "missing", "duplicate", "changed"):
                with self.subTest(stage=stage, kind=kind):
                    state = self.legacy_state()
                    roster = copy.deepcopy(state["open_pages"])
                    if kind == "missing":
                        roster = [[]]
                    elif kind == "duplicate":
                        roster[0].append(copy.deepcopy(roster[0][0]))
                    elif kind == "changed":
                        roster[0][0]["body"] += "Concurrent edit"
                    else:
                        other = {"number": 43, "body": managed_body("campaign-7", "chain-3") if kind == "same-chain"
                                 else managed_body("other", "other"), "head": {"ref": "routine/other", "sha": EXPECTED_SHA}}
                        if kind == "unknown":
                            other["body"] = "No admission identity"
                        if kind == "damaged":
                            other["body"] += "<!-- fsgg-pr-lane-admission broken -->"
                        roster[0].append(other)
                        if kind == "capacity":
                            roster[0].append({**other, "number": 44})
                    state["open_read_sequence"] = [roster] if stage == 0 else [state["open_pages"], roster]
                    completed, result, calls = self.update(state, extra=("--adopt-legacy-pr",))
                    self.assertEqual(3, completed.returncode, completed.stdout)
                    self.assertFalse(result["writeAttempted"])
                    self.assertFalse(any(call["arguments"][2] == "PATCH" for call in calls))
        state = self.legacy_state()
        state["open_pages"][0].append({"number": 43, "body": managed_body("other", "other")})
        completed, _, _ = self.update(state, extra=("--adopt-legacy-pr",))
        self.assertEqual(0, completed.returncode)

    def test_legacy_adoption_retains_exact_candidate_race_and_uncertain_effect_guards(self):
        for kind in ("wrong-base", "wrong-repository", "moved-ref", "prewrite-body", "prewrite-permissions",
                     "postwrite-body", "patch_fail_before", "patch_fail_after", "fail_read_after_patch"):
            with self.subTest(kind=kind):
                state = self.legacy_state()
                if kind == "wrong-base":
                    state["pull"]["base"]["ref"] = "other"
                elif kind == "wrong-repository":
                    state["pull"]["head"]["repo"]["full_name"] = "other/repo"
                elif kind == "moved-ref":
                    state["head_sha"] = "c" * 40
                elif kind.startswith(("prewrite-", "postwrite-")):
                    changed = copy.deepcopy(state["pull"])
                    if kind.endswith("permissions"):
                        changed["head"]["repo"]["permissions"]["push"] = False
                    else:
                        changed["body"] += "Concurrent edit"
                    state["read_sequence"] = [state["pull"]] * (2 if kind.startswith("postwrite-") else 1) + [changed]
                else:
                    state[kind] = True
                completed, result, calls = self.update(state, extra=("--adopt-legacy-pr",))
                self.assertEqual(3, completed.returncode, completed.stdout)
                writes = sum(call["arguments"][2] == "PATCH" for call in calls)
                self.assertEqual(1 if kind in {"postwrite-body", "patch_fail_before", "patch_fail_after", "fail_read_after_patch"} else 0, writes)
                self.assertEqual(bool(writes), result["writeAttempted"])
                self.assertFalse(any(call["arguments"][2] == "POST" for call in calls))

    def test_update_preserves_fresh_metadata_and_rebinds_only_routine_head(self):
        state = self.update_state()
        state["pull"]["body"] += "<!-- fsgg:future/v8 opaque=preserve-me -->\n"
        completed, result, calls = self.update(state)
        self.assertEqual(0, completed.returncode, completed.stdout)
        self.assertEqual("updated", result["action"])
        patches = [c for c in calls if c["arguments"][2] == "PATCH"]
        self.assertEqual(1, len(patches))
        body = json.loads(patches[0]["stdin"])["body"]
        self.assertIn(managed_body("campaign-7", "chain-3").split("\n\n")[1].strip(), body)
        self.assertIn("head=" + EXPECTED_SHA + " operation=source-change extra=value", body)
        self.assertIn("<!-- fsgg:future/v8 opaque=preserve-me -->", body)
        self.assertTrue(body.startswith("Revised prose\n"))
        self.assertEqual("GET", calls[-1]["arguments"][2])
        old_body = state["pull"]["body"]
        state["pull"]["body"] = body
        completed, result, calls = self.update(state, "Revised prose\n" + old_body[old_body.index("<!--"):])
        self.assertEqual(0, completed.returncode, completed.stdout)
        self.assertEqual("unchanged", result["action"])
        self.assertFalse(any(c["arguments"][2] == "PATCH" for c in calls))

    def test_update_refuses_metadata_conflicts_and_missing_ownership(self):
        cases = [self.update_state("Prose without admission marker"),
                 self.update_state(self.update_state()["pull"]["body"].replace("campaign-7", "other")),
                 self.update_state(self.update_state()["pull"]["body"].replace("b" * 40, "c" * 40))]
        for state in cases:
            completed, _, calls = self.update(state)
            self.assertNotEqual(0, completed.returncode)
            self.assertFalse(any(c["arguments"][2] == "PATCH" for c in calls))
        for proposed in ["<!-- fsgg:future/v8 replace=unknown -->", self.update_state()["pull"]["body"].replace("source-change", "internal-docs")]:
            completed, _, calls = self.update(self.update_state(), proposed)
            self.assertNotEqual(0, completed.returncode)
            self.assertFalse(any(c["arguments"][2] == "PATCH" for c in calls))

    def test_update_conflicts_refuse_before_write_and_readback_detects_race(self):
        import copy
        state = self.update_state()
        changed = copy.deepcopy(state["pull"])
        changed["body"] += "Concurrent edit\n"
        state["read_sequence"] = [state["pull"], changed]
        completed, result, calls = self.update(state)
        self.assertEqual(3, completed.returncode, completed.stdout)
        self.assertEqual("update-conflict", result["reason"])
        self.assertFalse(any(c["arguments"][2] == "PATCH" for c in calls))
        state = self.update_state()
        state["read_sequence"] = [state["pull"], state["pull"], changed]
        completed, result, calls = self.update(state)
        self.assertEqual(3, completed.returncode, completed.stdout)
        self.assertEqual("update-readback-mismatch", result["reason"])
        self.assertEqual(1, sum(c["arguments"][2] == "PATCH" for c in calls))

    def test_update_uncertain_write_and_readback_failures_never_succeed_or_retry(self):
        for failure in ["patch_fail_before", "patch_fail_after", "fail_read_after_patch"]:
            with self.subTest(failure=failure):
                state = self.update_state()
                state[failure] = True
                completed, result, calls = self.update(state)
                self.assertEqual(3, completed.returncode, completed.stdout)
                self.assertFalse(result["ok"])
                expected = ("post-write-readback-unresolved" if failure == "fail_read_after_patch"
                            else "update-write-unresolved")
                self.assertEqual(expected, result["reason"])
                self.assertEqual("FS-GG/example", result["repo"])
                self.assertEqual(42, result["number"])
                self.assertEqual(EXPECTED_SHA, result["headSha"])
                self.assertEqual(1, sum(c["arguments"][2] == "PATCH" for c in calls))
                self.assertFalse(any(c["arguments"][2] == "POST" for c in calls))

    def test_update_refuses_wrong_candidate_or_moved_ref(self):
        for field in ["sha", "ref", "repo"]:
            state = self.update_state()
            state["pull"]["head"][field] = {"full_name": "other/repo"} if field == "repo" else "wrong"
            completed, result, calls = self.update(state)
            self.assertEqual(3, completed.returncode, completed.stdout)
            self.assertEqual("update-candidate-mismatch", result["reason"])
            self.assertFalse(any(c["arguments"][2] == "PATCH" for c in calls))
        state = self.update_state()
        state["head_sha"] = "c" * 40
        completed, result, calls = self.update(state)
        self.assertEqual(3, completed.returncode, completed.stdout)
        self.assertEqual("head-moved", result["reason"])
        self.assertFalse(any(c["arguments"][2] == "PATCH" for c in calls))

    @staticmethod
    def set_field(value, path, item, *, remove=False):
        parts = path.split(".")
        for part in parts[:-1]:
            value = value[part]
        if remove:
            del value[parts[-1]]
        else:
            value[parts[-1]] = item

    def test_update_ignores_only_action_irrelevant_native_observation_churn(self):
        volatile = {"mergeable": True, "rebaseable": True, "mergeable_state": "unstable", "merge_commit_sha": "e" * 40}
        for changes in [{field: value} for field, value in volatile.items()] + [volatile]:
            with self.subTest(changes=tuple(changes)):
                state = self.update_state()
                changed = copy.deepcopy(state["pull"])
                changed.update(changes)
                state["read_sequence"] = [state["pull"], changed]
                completed, result, calls = self.update(state)
                self.assertEqual(0, completed.returncode, completed.stdout)
                self.assertEqual("updated", result["action"])
                self.assertEqual(1, sum(call["arguments"][2] == "PATCH" for call in calls))
                self.assertEqual(3, sum(any(argument.endswith("/pulls/42") for argument in call["arguments"]) and call["arguments"][2] == "GET" for call in calls))

    def test_update_repo_argument_case_preserves_exact_native_github_url_contract(self):
        for repo in ("fs-gg/example", "fS-gG/eXaMpLe"):
            with self.subTest(repo=repo):
                completed, result, calls = self.update(self.update_state(), repo=repo)
                self.assertEqual(0, completed.returncode, completed.stdout)
                self.assertEqual("updated", result["action"])
                self.assertEqual("https://github.com/FS-GG/example/pull/42", result["url"])
                self.assertEqual(1, sum(call["arguments"][2] == "PATCH" for call in calls))
        for url in ("https://other.example/FS-GG/example/pull/42", "http://github.com/FS-GG/example/pull/42",
                    "https://github.com/FS-GG/example/issues/42", "https://github.com/FS-GG/example/pull/43",
                    "https://github.com@other.example/FS-GG/example/pull/42"):
            for stage in ("initial-candidate", "prewrite-comparison"):
                with self.subTest(url=url, stage=stage):
                    state = self.update_state()
                    changed = copy.deepcopy(state["pull"])
                    changed["html_url"] = url
                    if stage == "initial-candidate":
                        state["pull"] = changed
                    else:
                        state["read_sequence"] = [state["pull"], changed]
                    completed, result, calls = self.update(state, repo="fs-gg/example")
                    self.assertEqual(3, completed.returncode, completed.stdout)
                    self.assertEqual(stage, result["stage"])
                    self.assertFalse(result["writeAttempted"])
                    self.assertIn("html_url", [item["field"] for item in result["diagnostic"]["fields"]])
                    self.assertFalse(any(call["arguments"][2] == "PATCH" for call in calls))

    def test_every_metadata_identity_authority_and_prose_change_refuses_before_patch(self):
        changes = {"number": 43, "id": 1043, "node_id": "PR_other", "state": "closed", "merged": True,
                   "draft": True, "locked": True, "maintainer_can_modify": False, "author_association": "NONE",
                   "html_url": "https://github.com/FS-GG/example/pull/43", "title": "Concurrent private title",
                   "body": self.update_state()["pull"]["body"] + "Concurrent private prose\n",
                   "head.ref": "routine/other", "head.sha": "e" * 40, "base.ref": "other", "base.sha": "e" * 40}
        for prefix in ("user", "head.user", "base.user", "head.repo.owner", "base.repo.owner"):
            for field, value in (("id", 999), ("node_id", "U_other"), ("login", "other"), ("type", "Bot")):
                changes[prefix + "." + field] = value
        for prefix in ("head.repo", "base.repo"):
            changes.update({prefix + ".id": 999, prefix + ".node_id": "R_other", prefix + ".full_name": "Other/repository",
                            prefix + ".private": False, prefix + ".archived": True, prefix + ".disabled": True,
                            prefix + ".visibility": "public", prefix + ".permissions": {"push": False}})
        for field, value in changes.items():
            with self.subTest(field=field):
                state = self.update_state()
                changed = copy.deepcopy(state["pull"])
                self.set_field(changed, field, value)
                state["read_sequence"] = [state["pull"], changed]
                completed, result, calls = self.update(state)
                self.assertEqual(3, completed.returncode, completed.stdout)
                self.assertEqual("prewrite-comparison", result["stage"])
                self.assertFalse(result["writeAttempted"])
                self.assertIn(field, [entry["field"] for entry in result["diagnostic"]["fields"]])
                self.assertFalse(any(call["arguments"][2] == "PATCH" for call in calls))
                self.assertNotIn("Concurrent private", completed.stdout)

    def test_required_metadata_fields_missing_and_malformed_are_distinct_and_never_defaulted(self):
        required = ["number", "id", "node_id", "state", "merged", "draft", "locked", "maintainer_can_modify",
                    "author_association", "html_url", "title", "body", "head.ref", "head.sha", "base.ref", "base.sha"]
        for prefix in ("user", "head.user", "base.user", "head.repo.owner", "base.repo.owner"):
            required += [prefix + "." + field for field in ("id", "node_id", "login", "type")]
        for prefix in ("head.repo", "base.repo"):
            required += [prefix + "." + field for field in ("id", "node_id", "full_name")]
        for field in required:
            for remove in (True, False):
                with self.subTest(field=field, remove=remove):
                    state = self.update_state()
                    self.set_field(state["pull"], field, None, remove=remove)
                    completed, result, calls = self.update(state)
                    self.assertEqual(3, completed.returncode, completed.stdout)
                    self.assertEqual("initial-candidate", result["stage"])
                    self.assertFalse(result["writeAttempted"])
                    problem = next(item for item in result["diagnostic"]["fields"] if item["field"] == field)
                    self.assertEqual("missing" if remove else "malformed", problem["status"])
                    self.assertFalse(any(call["arguments"][2] == "PATCH" for call in calls))
        for value in (None, [], {"body": {"credential-secret": "raw native blob"}}):
            state = self.update_state()
            state["read_sequence"] = [value]
            completed, result, calls = self.update(state)
            self.assertEqual(3, completed.returncode)
            self.assertEqual("initial-candidate", result["stage"])
            self.assertNotIn("credential-secret", completed.stdout)
            self.assertNotIn("raw native blob", completed.stdout)
            self.assertFalse(result["writeAttempted"])

    def test_optional_capability_presence_and_malformed_types_remain_guarded(self):
        for field in ("head.repo.private", "base.repo.archived", "head.repo.disabled", "base.repo.visibility", "head.repo.permissions"):
            for missing_before in (True, False):
                with self.subTest(field=field, missing_before=missing_before):
                    state = self.update_state()
                    changed = copy.deepcopy(state["pull"])
                    self.set_field(state["pull"] if missing_before else changed, field, None, remove=True)
                    state["read_sequence"] = [state["pull"], changed]
                    completed, result, calls = self.update(state)
                    self.assertEqual(3, completed.returncode)
                    self.assertEqual("update-conflict", result["reason"])
                    difference = next(item for item in result["diagnostic"]["fields"] if item["field"] == field)
                    self.assertEqual("missing", difference["expected" if missing_before else "observed"]["kind"])
                    self.assertFalse(any(call["arguments"][2] == "PATCH" for call in calls))
            state = self.update_state()
            self.set_field(state["pull"], field, ["private-secret"])
            completed, result, calls = self.update(state)
            self.assertEqual(3, completed.returncode)
            self.assertEqual("malformed", result["diagnostic"]["fields"][0]["status"])
            self.assertNotIn("private-secret", completed.stdout)

    def test_metadata_marker_policy_and_branch_ref_failures_have_safe_actual_stage(self):
        for field, replacement in (("campaign-7", "other"), ("source-change", "deploy"), ("b" * 40, "f" * 40)):
            state = self.update_state()
            state["pull"]["body"] = state["pull"]["body"].replace(field, replacement)
            completed, result, calls = self.update(state)
            self.assertEqual(3, completed.returncode)
            self.assertEqual("metadata-preparation", result["stage"])
            self.assertFalse(result["writeAttempted"])
            self.assertFalse(any(call["arguments"][2] == "PATCH" for call in calls))
        for head in ("f" * 40, None, 123, "malformed-private-secret"):
            state = self.update_state()
            state["head_sha"] = head
            completed, result, calls = self.update(state)
            self.assertEqual(3, completed.returncode)
            self.assertEqual("branch-ref", result["stage"])
            self.assertEqual("branch.sha", result["diagnostic"]["fields"][0]["field"])
            self.assertFalse(result["writeAttempted"])
            self.assertNotIn("malformed-private-secret", completed.stdout)
            self.assertFalse(any(call["arguments"][2] == "PATCH" for call in calls))

    def test_diagnostic_fingerprints_bind_deciding_read_without_later_substitute(self):
        state = self.update_state()
        changed = copy.deepcopy(state["pull"])
        changed["body"] += "private deciding response\n"
        state["read_sequence"] = [state["pull"], changed, state["pull"]]
        completed, result, calls = self.update(state)
        self.assertEqual(3, completed.returncode)
        self.assertEqual("prewrite-comparison", result["stage"])
        difference = next(item for item in result["diagnostic"]["fields"] if item["field"] == "body")
        encoded = json.dumps(changed["body"], ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode()
        self.assertEqual(hashlib.sha256(encoded).hexdigest(), difference["observed"]["sha256"])
        self.assertEqual(len(encoded), difference["observed"]["bytes"])
        self.assertEqual(2, sum(any(argument.endswith("/pulls/42") for argument in call["arguments"]) for call in calls))
        self.assertNotIn("private deciding response", completed.stdout)
        self.assertLess(len(completed.stdout), 2000)

    def test_stale_then_current_native_head_are_separate_attempts(self):
        stale = self.update_state()
        stale["pull"]["head"]["sha"] = "b" * 40
        completed, refused, calls = self.update(stale)
        self.assertEqual(3, completed.returncode)
        self.assertEqual("initial-candidate", refused["stage"])
        self.assertFalse(refused["writeAttempted"])
        self.assertEqual(1, len(calls))
        completed, accepted, current_calls = self.update(self.update_state())
        self.assertEqual(0, completed.returncode)
        self.assertEqual("updated", accepted["action"])
        self.assertEqual(1, sum(call["arguments"][2] == "PATCH" for call in current_calls))
        self.assertEqual("update-candidate-mismatch", refused["reason"])

    def test_postwrite_base_drift_remains_unresolved_with_exact_updated_prose_and_no_retry(self):
        state = self.update_state()
        completed, _, calls = self.update(state)
        self.assertEqual(0, completed.returncode)
        patch = next(call for call in calls if call["arguments"][2] == "PATCH")
        after = copy.deepcopy(state["pull"])
        after.update(json.loads(patch["stdin"]))
        after["base"]["sha"] = "e" * 40
        state["read_sequence"] = [state["pull"], state["pull"], after]
        completed, result, calls = self.update(state)
        self.assertEqual(3, completed.returncode)
        self.assertEqual("update-readback-mismatch", result["reason"])
        self.assertEqual("postwrite-readback", result["stage"])
        self.assertTrue(result["writeAttempted"])
        self.assertEqual(["base.sha"], [item["field"] for item in result["diagnostic"]["fields"]])
        self.assertEqual(1, sum(call["arguments"][2] == "PATCH" for call in calls))
        self.assertEqual("GET", calls[-1]["arguments"][2])

    def test_update_read_errors_have_actual_stage_and_never_emit_exception_secrets_or_retry(self):
        spec = importlib.util.spec_from_file_location("lane_read_errors", TOOL)
        lane = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(lane)
        args = lane.parser().parse_args(["--repo", "FS-GG/example", "--campaign-id", "campaign-7", "--dependency-chain-id", "chain-3",
            "--head", "routine/fixture", "--head-sha", EXPECTED_SHA, "--base", "main", "--title", "Deliver coherent feature",
            "--body-file", "unused", "--update-pr", "42", "--previous-head-sha", "b" * 40])
        pull = self.update_state()["pull"]
        branch = {"object": {"sha": EXPECTED_SHA}}
        for stage, sequence in (("initial-candidate", []), ("branch-ref", [pull]),
                                ("prewrite-comparison", [pull, branch]), ("postwrite-readback", [pull, branch, pull, {}])):
            with self.subTest(stage=stage), mock.patch.object(lane, "run_gh", side_effect=sequence + [lane.ToolError("credential-secret raw native stderr")]) as transport:
                with self.assertRaises(lane.Refusal) as failure:
                    lane.update_pull(args, "Revised prose\n", {"repo": "FS-GG/example"})
                result = failure.exception.details
                self.assertEqual(stage, result["stage"])
                self.assertEqual(stage == "postwrite-readback", result["writeAttempted"])
                self.assertEqual("read-error", result["diagnostic"]["status"])
                self.assertEqual(len(sequence) + 1, transport.call_count)
                self.assertNotIn("credential-secret", json.dumps(result))
                self.assertLessEqual(sum(call.args[1][1] == "PATCH" for call in transport.call_args_list), 1)

    def test_refuses_when_same_dependency_chain_is_open_across_paginated_results(self):
        state = {
            "head_sha": EXPECTED_SHA,
            "open_pages": [
                [{"number": 1, "html_url": "https://example.test/pull/1", "body": "unmanaged"}],
                [{"number": 2, "html_url": "https://example.test/pull/2", "body": managed_body("campaign-7", "chain-3")}],
            ],
        }
        completed, result, calls = self.invoke(state)
        self.assertEqual(3, completed.returncode, completed.stderr)
        self.assertEqual("same-chain-open", result["reason"])
        self.assertEqual([2], [pull["number"] for pull in result["matchingPullRequests"]])
        self.assertEqual(1, len(calls))
        self.assertIn("--paginate", calls[0]["arguments"])

    def test_refuses_third_managed_pr_across_different_campaigns_in_repo(self):
        state = {
            "head_sha": EXPECTED_SHA,
            "open_pages": [[
                {"number": 8, "html_url": "https://example.test/pull/8", "body": managed_body("campaign-1", "chain-1")},
                {"number": 9, "html_url": "https://example.test/pull/9", "body": managed_body("campaign-2", "chain-2")},
            ]],
        }
        completed, result, calls = self.invoke(state)
        self.assertEqual(3, completed.returncode, completed.stderr)
        self.assertEqual("repository-open-pr-cap", result["reason"])
        self.assertEqual(2, result["openManagedPullRequestCount"])
        self.assertEqual(1, len(calls))

    def test_same_chain_id_in_a_different_campaign_does_not_collide(self):
        state = {
            "head_sha": EXPECTED_SHA,
            "open_pages": [[
                {"number": 10, "html_url": "https://example.test/pull/10", "body": managed_body("campaign-6", "chain-3")},
            ]],
        }
        completed, result, calls = self.invoke(state)
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual("created", result["action"])
        self.assertEqual(1, result["openManagedPullRequestCount"])
        self.assertEqual("POST", calls[-1]["arguments"][2])

    def test_stale_routine_head_does_not_release_campaign_chain_reservation(self):
        body = managed_body('campaign-7', 'chain-3') + (
            '<!-- fsgg:routine-development/v1 head=' + 'b' * 40 + ' operation=source-change -->\n')
        state = {'head_sha': EXPECTED_SHA, 'open_pages': [[{'number': 11, 'body': body,
            'head': {'ref': 'routine/source', 'sha': EXPECTED_SHA}}]]}
        completed, result, calls = self.invoke(state)
        self.assertEqual(3, completed.returncode)
        self.assertEqual('same-chain-open', result['reason'])
        self.assertEqual(1, result['openManagedPullRequestCount'])
        self.assertEqual(1, result['openNativePullRequestCount'])
        self.assertEqual(EXPECTED_SHA, result['nativeOpenPullRequests'][0]['headSha'])
        self.assertEqual(1, len(calls))

    def test_erased_lane_marker_on_routine_branch_cannot_manufacture_third_slot(self):
        routine = '<!-- fsgg:routine-development/v1 head=' + EXPECTED_SHA + ' operation=source-change -->'
        state = {'head_sha': EXPECTED_SHA, 'open_pages': [[{'number': 11, 'body': routine,
            'head': {'ref': 'routine/source', 'sha': EXPECTED_SHA}},
            {'number': 12, 'body': managed_body('other', 'other')}]]}
        completed, result, calls = self.invoke(state)
        self.assertEqual(3, completed.returncode)
        self.assertEqual('repository-open-pr-cap', result['reason'])
        self.assertEqual(2, result['openManagedPullRequestCount'])
        self.assertEqual(2, result['openNativePullRequestCount'])
        self.assertEqual([11, 12], [p['number'] for p in result['matchingPullRequests']])
        self.assertEqual(1, len(calls))

    def test_missing_or_edited_routine_identity_blocks_without_guessing_chain(self):
        bodies = [None, '', 'Rewritten prose', '<!-- fsgg-pr-lane-admission:v1 campaign=campaign-7 -->',
                  managed_body('campaign-7', 'chain-3') * 2,
                  managed_body('campaign-7', 'chain-3') + managed_body('other', 'other')]
        for body in bodies:
            state = {'head_sha': EXPECTED_SHA, 'open_pages': [[{'number': 11, 'body': body,
                'head': {'ref': 'routine/source', 'sha': EXPECTED_SHA}}]]}
            with self.subTest(body=body):
                completed, result, calls = self.invoke(state)
                self.assertEqual(3, completed.returncode)
                self.assertEqual('managed-reservation-identity-unknown', result['reason'])
                self.assertEqual(1, result['openManagedPullRequestCount'])
                self.assertEqual(1, result['openNativePullRequestCount'])
                self.assertEqual(1, len(calls))

    def test_damaged_lane_footprint_blocks_even_on_generic_feature_branch(self):
        for body in ('<!-- fsgg-pr-lane-admission', '<!-- fsgg-pr-lane-admission edited metadata -->',
                     managed_body('one', 'one') + managed_body('two', 'two')):
            state = {'head_sha': EXPECTED_SHA, 'open_pages': [[{'number': 11, 'body': body,
                'head': {'ref': 'feature/source', 'sha': EXPECTED_SHA}}]]}
            with self.subTest(body=body):
                completed, result, calls = self.invoke(state)
                self.assertEqual(3, completed.returncode)
                self.assertEqual('managed-reservation-identity-unknown', result['reason'])
                self.assertEqual(1, len(calls))

    def test_native_population_reports_genuine_unmanaged_prs_without_expanding_cap(self):
        state = {'head_sha': EXPECTED_SHA, 'open_pages': [[{'number': 11, 'body': '',
            'head': {'ref': 'feature/unmanaged', 'sha': EXPECTED_SHA}}],
            [{'number': 12, 'body': 'Discuss fsgg-pr-lane-admission without a reservation marker',
              'head': {'ref': 'feature/other', 'sha': 'b' * 40}}]]}
        completed, result, calls = self.invoke(state)
        self.assertEqual(0, completed.returncode)
        self.assertEqual(0, result['openManagedPullRequestCount'])
        self.assertEqual(2, result['openNativePullRequestCount'])
        self.assertEqual([11, 12], [p['number'] for p in result['nativeOpenPullRequests']])
        self.assertEqual('POST', calls[-1]['arguments'][2])

    def test_valid_lane_identity_remains_reserved_when_routine_body_is_malformed(self):
        state = {'head_sha': EXPECTED_SHA, 'open_pages': [[{'number': 11,
            'body': managed_body('campaign-7', 'chain-3') + '<!-- fsgg:routine-development/v1 broken',
            'head': {'ref': 'routine/source', 'sha': 'b' * 40}}]]}
        completed, result, calls = self.invoke(state)
        self.assertEqual(3, completed.returncode)
        self.assertEqual('same-chain-open', result['reason'])
        self.assertEqual(1, len(calls))

    def test_refuses_when_head_moved_and_does_not_post(self):
        state = {"head_sha": "b" * 40, "open_pages": [[]]}
        completed, result, calls = self.invoke(state)
        self.assertEqual(3, completed.returncode, completed.stderr)
        self.assertEqual("head-moved", result["reason"])
        self.assertEqual("b" * 40, result["observedHeadSha"])
        self.assertEqual(["GET", "GET"], [call["arguments"][2] for call in calls])

    def test_creates_with_json_input_and_preserves_body_before_marker(self):
        original_body = "Heading\n\nParagraph with `code` and ünicode.\n"
        state = {"head_sha": EXPECTED_SHA, "open_pages": [[], []]}
        completed, result, calls = self.invoke(state, body=original_body)
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual("created", result["action"])
        self.assertEqual(42, result["number"])
        self.assertEqual(3, len(calls))
        post = calls[-1]
        self.assertEqual("POST", post["arguments"][2])
        payload = json.loads(post["stdin"])
        self.assertTrue(payload["body"].startswith(original_body))
        self.assertEqual(
            original_body
            + "\n<!-- fsgg-pr-lane-admission:v1 campaign=campaign-7 chain=chain-3 -->\n",
            payload["body"],
        )
        self.assertEqual("feature/combined", payload["head"])

    def test_routine_creation_real_gate_and_idempotent_preparation(self):
        spec = importlib.util.spec_from_file_location("canonical_lane", TOOL)
        lane = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(lane)
        with tempfile.TemporaryDirectory() as directory:
            def git(*args):
                return subprocess.check_output(["git", "-C", directory, *args], text=True).strip()
            git("init", "-q")
            git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.test", "commit", "--allow-empty", "-qm", "base")
            base = git("rev-parse", "HEAD")
            pathlib.Path(directory, "fixture.txt").write_text("nonempty source delta\n")
            git("add", "fixture.txt")
            git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.test", "commit", "-qm", "candidate")
            head = git("rev-parse", "HEAD")
            completed, result, calls = self.invoke({"head_sha": head, "open_pages": [[]]}, branch="routine/fixture", head=head)
            self.assertEqual(0, completed.returncode, completed.stderr)
            body = json.loads(calls[-1]["stdin"])["body"]
            self.assertEqual(1, body.count("fsgg:routine-development/v1"))
            self.assertEqual(body, lane.prepare_body(body, "routine/fixture", head, "campaign-7", "chain-3"))
            gate = lane.routine_validator()
            args = argparse.Namespace(base_sha=base, head_sha=head, head_ref="routine/fixture",
                routine_policy=str(ROOT / ".fsgg/routine-development.json"), routine_policy_ref=None)
            previous = os.getcwd()
            try:
                os.chdir(directory)
                self.assertIsNone(gate.evaluate_routine(args, body)[0])
                self.assertEqual("routine-missing", gate.evaluate_routine(args, "Original missing marker")[0])
                self.assertEqual("routine-changed-head", gate.evaluate_routine(args, body.replace(head, "f" * 40))[0])
                args.base_sha = head
                self.assertEqual("routine-empty", gate.evaluate_routine(args, body)[0])
            finally:
                os.chdir(previous)

    def test_routine_invalid_metadata_refuses_without_any_remote_call(self):
        valid = f"<!-- fsgg:routine-development/v1 head={EXPECTED_SHA} operation=source-change -->"
        cases = [(valid + valid, "routine/fixture", EXPECTED_SHA),
                 (valid.replace("operation=source-change", "operation=publish"), "routine/fixture", EXPECTED_SHA),
                 (valid.replace("head=", "head=" + "b" * 40 + " head="), "routine/fixture", EXPECTED_SHA),
                 (valid.replace(EXPECTED_SHA, "b" * 40), "routine/fixture", EXPECTED_SHA),
                 ("<!-- fsgg:routine-development/v1", "routine/fixture", EXPECTED_SHA),
                 ("", "routine/INVALID", EXPECTED_SHA), ("", "routine/fixture", "a" * 64)]
        for body, branch, head in cases:
            with self.subTest(body=body, branch=branch):
                completed, result, calls = self.invoke({"head_sha": head, "open_pages": [[]]}, body, branch, head)
                self.assertNotEqual(0, completed.returncode)
                self.assertEqual([], calls)

    def test_existing_docs_marker_is_preserved_and_lane_conflicts_refuse(self):
        body = f"Prose\n<!-- fsgg:routine-development/v1 head={EXPECTED_SHA} operation=internal-docs extra=value -->\n"
        completed, _, calls = self.invoke({"head_sha": EXPECTED_SHA, "open_pages": [[]]}, body, "routine/docs")
        self.assertEqual(0, completed.returncode)
        self.assertTrue(json.loads(calls[-1]["stdin"])["body"].startswith(body))
        conflict = body + managed_body("other", "chain")
        completed, _, calls = self.invoke({"head_sha": EXPECTED_SHA, "open_pages": [[]]}, conflict, "routine/docs")
        self.assertNotEqual(0, completed.returncode)
        self.assertEqual([], calls)
        completed, _, calls = self.invoke({'head_sha':'a' * 64,'open_pages':[[]]}, 'Generic prose\n', 'feature/generic', 'a' * 64)
        self.assertEqual(0, completed.returncode)
        self.assertNotIn('fsgg:routine-development',json.loads(calls[-1]['stdin'])['body'])

    def predecessor(self, *, change=None, source_error=None, ordinary=False, drift=False, uncertain=False, operation="source-change", churn=False, malformed=False, malformed_files=False):
        spec=importlib.util.spec_from_file_location("sc2_lane_fixture",TOOL)
        lane=importlib.util.module_from_spec(spec);spec.loader.exec_module(lane)
        repo='FS-GG/FS.GG.SC2.Client'
        pulls=[dict(number=n,state='open',head=dict(ref=values[0],sha=values[1]),
                    body=managed_body(values[2],values[3])) for n,values in lane.SC2_PRESERVED.items()]
        native={p['number']:dict(copy.deepcopy(p),changed_files=1,base=dict(ref='main',repo=dict(id=1390567199))) for p in pulls}
        for row in native.values():row['head']['repo']=dict(id=1390567199)
        if change:change(pulls,native)
        calls=[];list_reads=0
        def read(gh,args,stdin=None):
            nonlocal list_reads
            calls.append((args,stdin));endpoint=args[-1] if args[-1]!='-' else args[-3]
            if endpoint.endswith('pulls?state=open&per_page=100'):
                list_reads+=1;value=copy.deepcopy(pulls)
                if drift and list_reads==2:value[0]['head']['sha']='c'*40
                if churn and list_reads==2:value[0].update(updated_at='later',mergeable=None)
                return value
            if '/files?' in endpoint:
                if malformed_files:return [[None]]
                return [[dict(filename='src/product-'+endpoint.split('/pulls/')[1].split('/')[0]+'.fs')]]
            if '/git/ref/heads/' in endpoint:return dict(object=dict(sha=EXPECTED_SHA))
            if endpoint.endswith('/pulls'):
                if uncertain:raise lane.ToolError('lost response')
                if malformed:return dict(number=True)
                return dict(number=45,html_url='https://github.com/'+repo+'/pull/45')
            return copy.deepcopy(native[int(endpoint.rsplit('/',1)[1])])
        api=mock.Mock();api.installation_read.side_effect=lambda endpoint:(dict(id=1390567199,private=True) if endpoint.endswith(repo) else dict(object=dict(sha='b'*40)))
        api.installation_source.side_effect=source_error
        producer=argparse.Namespace(PRODUCER_PATHS=('.fsgg/routine-development.json','.github/workflows/routine-eligibility.yml',
            'scripts/check-claim-generation.py','scripts/check-routine-eligibility-envelope.py','scripts/lib/gate.py'))
        with tempfile.TemporaryDirectory() as directory:
            body=pathlib.Path(directory,'body.md');body.write_text('Exact reviewed canonical producer installation.\n'+f'<!-- fsgg:routine-development/v1 head={EXPECTED_SHA} operation={operation} -->\n')
            args=lane.parser().parse_args(['--repo',repo,'--campaign-id','outcome-led-programme-20261016',
                '--dependency-chain-id',lane.SC2_PREDECESSOR_CHAIN,'--head','routine/sc2-producer-install',
                '--head-sha',EXPECTED_SHA,'--base','main','--title','Install canonical producer','--body-file',str(body),
                *([] if ordinary else ['--sc2-producer-predecessor-source','d'*40])])
            with mock.patch.object(lane,'run_gh',side_effect=read),mock.patch.object(lane,'installation_api',return_value=(producer,api)):
                try:result=lane.execute(args)
                except lane.Refusal as error:result=dict(ok=False,reason=error.reason,**error.details)
        return result,calls,api

    def test_exact_sc2_predecessor_creates_once_after_two_source_and_roster_checks(self):
        result,calls,api=self.predecessor()
        self.assertEqual((result['action'],result['number'],result['openManagedPullRequestCount']),('created',45,2))
        self.assertEqual(result['selectedException'],'sc2-exact-five-file-predecessor')
        self.assertEqual(api.installation_source.call_count,2)
        self.assertEqual(api.installation_source.call_args.args,('FS-GG/FS.GG.SC2.Client',EXPECTED_SHA,'main','b'*40,'d'*40))
        writes=[call for call in calls if call[0][1]=='POST'];self.assertEqual(len(writes),1)
        body=json.loads(writes[0][1])['body'];self.assertIn('preserved PR43/44',body)
        self.assertFalse(any('/check-runs' in arg for call,_ in calls for arg in call))

    def test_predecessor_does_not_change_normal_cap_or_admit_changed_reservations(self):
        changes=[lambda p,n:p.append(dict(number=46,body=managed_body('other','third'),head=dict(ref='routine/third',sha=EXPECTED_SHA))),
                 lambda p,n:p[0]['head'].update(sha='c'*40),lambda p,n:p[0].update(body='unknown reservation'),
                 lambda p,n:n[43]['head']['repo'].update(id=1),lambda p,n:n[44].update(changed_files=2),
                 lambda p,n:n[43]['base'].update(ref='other')]
        for options in [dict(ordinary=True),dict(operation='internal-docs'),*[dict(change=change) for change in changes],dict(source_error=RuntimeError('partial/noncanonical payload'))]:
            with self.subTest(options=options):
                result,calls,_=self.predecessor(**options)
                self.assertFalse(result['ok']);self.assertFalse(any(call[0][1]=='POST' for call in calls))

    def test_predecessor_rechecks_before_create_and_fences_unknown_response(self):
        result,calls,_=self.predecessor(drift=True)
        self.assertEqual(result['reason'],'sc2-predecessor-reservation-mismatch')
        self.assertFalse(any(call[0][1]=='POST' for call in calls))
        self.assertTrue(self.predecessor(churn=True)[0]['ok'])
        result,calls,_=self.predecessor(malformed_files=True,change=lambda p,n:n[43].update(changed_files=0))
        self.assertEqual(result['reason'],'sc2-predecessor-preserved-files-incomplete')
        self.assertFalse(any(call[0][1]=='POST' for call in calls))
        for options in [dict(uncertain=True),dict(malformed=True)]:
            result,calls,_=self.predecessor(**options)
            self.assertEqual(result['reason'],'sc2-predecessor-create-unresolved');self.assertTrue(result['writeAttempted'])
            self.assertEqual(sum(call[0][1]=='POST' for call in calls),1)


if __name__ == "__main__":
    unittest.main()
