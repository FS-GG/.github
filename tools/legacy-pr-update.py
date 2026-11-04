"""One existing-owner metadata continuation; called only by pr-lane-admission.

Policy: .github#5269, Root decision issuecomment-6590105190. This does not
adopt reservations or alter admission, source, CI, integration or closure rules.
"""
from __future__ import annotations

import importlib.util
import hashlib
import json
import os
import pathlib
import re
import stat

SCHEMA = "fsgg.legacy-pr-update/1"
AUTH_PREFIX = "<!-- fsgg:legacy-pr-update/v1 "
HEX40 = re.compile(r"[0-9a-f]{40}")
HEX64 = re.compile(r"[0-9a-f]{64}")


def encoded(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def read_plan(lane, path, repo):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise lane.ToolError("duplicate continuation JSON key")
            result[key] = value
        return result
    try:
        if path.is_symlink() or not path.is_file() or path.stat().st_size > 65536:
            raise ValueError("not a bounded regular file")
        value = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique)
    except (OSError, ValueError, UnicodeError) as error:
        raise lane.ToolError("unreadable continuation plan") from error
    def require(condition):
        if not condition:
            raise lane.ToolError("invalid closed continuation plan")
    def positive(item):
        return type(item) is int and item > 0
    def text(item):
        return isinstance(item, str) and bool(item.strip()) and not any(c in item for c in "\r\n")
    def sha(item, pattern):
        return isinstance(item, str) and pattern.fullmatch(item) is not None
    require(isinstance(value, dict) and set(value) == {
        "schema", "repo", "repositoryId", "defaultBranch", "defaultSha", "number",
        "previousHead", "authorization", "successor", "pulls"})
    require(value["schema"] == SCHEMA and value["repo"] == repo and positive(value["repositoryId"])
            and text(value["defaultBranch"]) and sha(value["defaultSha"], HEX40) and positive(value["number"]))
    require(sha(value["previousHead"], HEX40))
    auth = value["authorization"]
    require(isinstance(auth, dict) and set(auth) == {"commentId", "nodeId", "bodySha256"}
            and positive(auth["commentId"]) and text(auth["nodeId"]) and sha(auth["bodySha256"], HEX64))
    successor = value["successor"]
    require(successor is None or (isinstance(successor, dict) and set(successor) == {
        "custodySha256", "intentSha256", "disposition"} and sha(successor["custodySha256"], HEX64)
        and sha(successor["intentSha256"], HEX64) and successor["disposition"] in {"delivered", "not-sent"}))
    rows = value["pulls"]
    require(isinstance(rows, list) and 1 <= len(rows) <= 20)
    fields = {"number", "id", "nodeId", "authorId", "author", "branch", "headSha", "base", "baseSha",
              "bodySha256", "titleSha256", "delivery", "issueId", "issueBodySha256"}
    for row in rows:
        require(isinstance(row, dict) and set(row) == fields)
        require(all(positive(row[k]) for k in ("number", "id", "authorId", "delivery", "issueId")))
        require(all(text(row[k]) for k in ("nodeId", "author", "branch", "base")))
        require(all(sha(row[k], HEX40) for k in ("headSha", "baseSha")))
        require(all(sha(row[k], HEX64) for k in ("bodySha256", "titleSha256", "issueBodySha256")))
    for key in ("number", "id", "nodeId", "branch"):
        require(len({row[key] for row in rows}) == len(rows))
    require([row["number"] for row in rows] == sorted(row["number"] for row in rows))
    require(value["number"] in {row["number"] for row in rows})
    return value


def declaration(spec, row):
    # Closed machine-only comment. No arbitrary Root identities, expiry or prose parsing.
    value = {"repo": spec["repo"], "prId": row["id"], "number": row["number"],
             "delivery": row["delivery"], "branch": row["branch"],
             "previousHead": spec["previousHead"], "currentHead": row["headSha"],
             "operation": "update-body-title", "successor": spec["successor"]}
    return AUTH_PREFIX + encoded(value) + " -->"


def root_rules(lane):
    root = pathlib.Path(__file__).resolve().parents[1]
    alias = root / ".fsgg/legacy-pr-root-rules.py"
    # The selected packaged source wins over an unrelated receiver-local policy.
    path = alias if alias.is_file() else root / "scripts/board-currency-rules.py"
    raw = path.read_bytes()
    policy_path = path.with_name("root-identity-policy.json")
    policy_raw = policy_path.read_bytes()
    spec = importlib.util.spec_from_file_location("legacy_update_root_rules", path)
    module = importlib.util.module_from_spec(spec)
    assert spec and spec.loader
    # Execute the exact bytes whose hash is exposed in this operation's existing proof/custody.
    try:
        exec(compile(raw, str(path), "exec"), module.__dict__)
    except (ValueError, UnicodeError, RecursionError) as error:
        raise lane.ToolError("maintained Root trust is unreadable") from error
    if policy_path.read_bytes() != policy_raw:
        raise lane.ToolError("maintained Root trust changed while loading")
    module.selected_source = {"path": path.relative_to(root).as_posix(),
                              "sha256": hashlib.sha256(raw).hexdigest(),
                              "policy": {"path": policy_path.relative_to(root).as_posix(),
                                         "sha256": hashlib.sha256(policy_raw).hexdigest()}}
    roots = module.PARAMS.get("rootLogins")
    if not isinstance(roots, list) or not roots or any(not isinstance(x, str) or not x.strip() for x in roots):
        raise lane.ToolError("maintained Root trust is unreadable")
    return {x.lower() for x in roots}, module


def guard_sha(lane, guard):
    return lane.text_sha(encoded({key: lane.metadata_fingerprint(value) for key, value in guard.items()}))


def execute(lane, args):
    spec = read_plan(lane, args.recover_legacy_update, args.repo)
    selected = next(row for row in spec["pulls"] if row["number"] == spec["number"])
    try:
        if args.body_file.is_symlink() or args.body_file.stat().st_size > 65536:
            raise ValueError("body must be bounded")
        proposed = args.body_file.read_text(encoding="utf-8")
    except (OSError, ValueError, UnicodeError) as error:
        raise lane.ToolError("unreadable continuation body") from error
    roots, rules = root_rules(lane)
    intent = {"plan": spec, "proposedBodySha256": lane.text_sha(proposed), "titleSha256": lane.text_sha(args.title),
              "rootRulesSource": rules.selected_source}
    intent_sha = lane.text_sha(encoded(intent))
    identity = {"repo": args.repo, "number": selected["number"], "nativeId": selected["id"], "intentSha256": intent_sha,
                "rootRulesSource": rules.selected_source}
    attempted = False
    def refuse(reason, **details):
        raise lane.Refusal("legacy-update-" + reason, **identity, writeAttempted=attempted, **details)
    def get(endpoint):
        return lane.run_gh(args.gh, ["--method", "GET", endpoint])
    def projection(native):
        guard, problems = lane.metadata_projection(native)
        if problems:
            refuse("native-unreadable")
        return guard
    guards, issues = {}, {}
    authorization_before = None
    source_before = None
    desired = None

    def authorization():
        auth = spec["authorization"]
        comment = get(f"/repos/{args.repo}/issues/comments/{auth['commentId']}")
        expected_url = f"https://api.github.com/repos/{args.repo}/issues/{selected['delivery']}"
        if (not isinstance(comment, dict) or comment.get("id") != auth["commentId"]
                or comment.get("node_id") != auth["nodeId"] or comment.get("issue_url") != expected_url
                or not isinstance(comment.get("body"), str)
                or lane.text_sha(comment["body"]) != auth["bodySha256"]
                or comment["body"].strip() != declaration(spec, selected)):
            refuse("authorization-mismatch")
        query = "query($id:ID!){node(id:$id){... on IssueComment{id databaseId body lastEditedAt author{login} editor{login} issue{number repository{nameWithOwner}}}}}"
        response = lane.run_gh(args.gh, ["graphql", "-f", "query=" + query, "-f", "id=" + auth["nodeId"]])
        data = response.get("data") if isinstance(response, dict) else None
        node = data.get("node") if isinstance(data, dict) else None
        if (not isinstance(node, dict) or response.get("errors") or node.get("id") != auth["nodeId"]
                or node.get("databaseId") != auth["commentId"] or node.get("body") != comment["body"]
                or node.get("issue") != {"number": selected["delivery"], "repository": {"nameWithOwner": args.repo}}
                or "lastEditedAt" not in node or "editor" not in node
                or not isinstance(node.get("author"), dict) or not isinstance(node["author"].get("login"), str)
                or node["author"]["login"].lower() not in roots
                or lane.metadata_field(comment, "user.login") != node["author"]["login"]):
            refuse("authorization-identity-unreadable")
        edited = node["lastEditedAt"]
        if edited is not None and (not isinstance(edited, str) or not edited
                or not isinstance(node["editor"], dict) or not isinstance(node["editor"].get("login"), str)
                or node["editor"]["login"].lower() not in roots):
            refuse("authorization-editor-untrusted")
        return {"commentId": auth["commentId"], "nodeId": auth["nodeId"], "bodySha256": auth["bodySha256"],
                "author": node["author"]["login"], "editedAt": edited, "editor": node["editor"]}

    def source():
        old, new = spec["previousHead"], selected["headSha"]
        # A bounded complete comparison, not a caller's local ancestry assertion.
        value = get(f"/repos/{args.repo}/compare/{old}...{new}?per_page=100")
        if (not isinstance(value, dict) or value.get("status") != ("identical" if old == new else "ahead")
                or lane.metadata_field(value, "base_commit.sha") != old
                or lane.metadata_field(value, "merge_base_commit.sha") != old
                or type(value.get("ahead_by")) is not int or not 0 <= value["ahead_by"] <= 100
                or type(value.get("behind_by")) is not int or value["behind_by"] != 0
                or type(value.get("total_commits")) is not int or value["total_commits"] != value["ahead_by"]
                or not isinstance(value.get("commits"), list) or len(value["commits"]) != value["ahead_by"]):
            refuse("source-ancestry-unproved")
        commits = [item.get("sha") if isinstance(item, dict) else None for item in value["commits"]]
        if (any(not isinstance(x, str) or not HEX40.fullmatch(x) for x in commits)
                or len(set(commits)) != len(commits) or (old != new and (not commits or commits[-1] != new))
                or (old == new and commits)):
            refuse("source-comparison-incomplete")
        return {"previousHead": old, "currentHead": new, "commits": commits}

    def population(initial=False):
        nonlocal desired, authorization_before, source_before
        try:
            current_roots, current_rules = root_rules(lane)
        except (OSError, lane.ToolError):
            refuse("root-trust-unreadable")
        if current_roots != roots or current_rules.selected_source != rules.selected_source:
            refuse("root-trust-drift")
        repo = get(f"/repos/{args.repo}")
        if (not isinstance(repo, dict) or repo.get("id") != spec["repositoryId"] or repo.get("full_name") != args.repo
                or repo.get("default_branch") != spec["defaultBranch"] or repo.get("archived") is not False
                or repo.get("disabled") is not False):
            refuse("repository-mismatch")
        if lane.current_head_sha(args.gh, args.repo, spec["defaultBranch"]) != spec["defaultSha"]:
            refuse("default-moved")
        pulls = lane.open_pulls(args.gh, args.repo)
        numbers = [pull.get("number") for pull in pulls]
        if (len(pulls) != len(spec["pulls"]) or any(type(n) is not int for n in numbers)
                or len(set(numbers)) != len(numbers) or set(numbers) != {r["number"] for r in spec["pulls"]}):
            refuse("population-mismatch")
        for row in spec["pulls"]:
            number = row["number"]
            native = get(f"/repos/{args.repo}/pulls/{number}")
            guard = projection(native)
            listed = next(p for p in pulls if p["number"] == number)
            # Actual GitHub list omits these two detail-only fields; all remaining deciding fields join.
            list_fields = set(lane._METADATA_FIELDS) - {"merged", "maintainer_can_modify"}
            if any(lane.metadata_field(listed, field) != guard[field] for field in list_fields):
                refuse("population-disagrees")
            if lane.current_head_sha(args.gh, args.repo, row["branch"]) != row["headSha"]:
                refuse("head-moved")
            wanted = {"number": number, "id": row["id"], "node_id": row["nodeId"], "state": "open", "merged": False,
                      "user.id": row["authorId"], "user.login": row["author"], "head.ref": row["branch"], "head.sha": row["headSha"],
                      "base.ref": row["base"], "base.sha": row["baseSha"], "head.repo.id": spec["repositoryId"],
                      "base.repo.id": spec["repositoryId"], "head.repo.full_name": args.repo, "base.repo.full_name": args.repo,
                      "html_url": f"https://github.com/{args.repo}/pull/{number}"}
            if any(guard[field] != value for field, value in wanted.items()):
                refuse("preimage-identity-mismatch")
            if initial:
                if lane.text_sha(native["body"]) != row["bodySha256"] or lane.text_sha(native["title"]) != row["titleSha256"]:
                    refuse("preimage-text-mismatch")
                guards[number] = guard
            elif guard != guards[number]:
                refuse("preimage-drift")
            issue = get(f"/repos/{args.repo}/issues/{row['delivery']}")
            if (not isinstance(issue, dict) or issue.get("id") != row["issueId"] or issue.get("number") != row["delivery"]
                    or issue.get("state") != "open" or "pull_request" in issue or not isinstance(issue.get("body"), str)
                    or issue.get("repository_url") != f"https://api.github.com/repos/{args.repo}"
                    or lane.text_sha(issue["body"]) != row["issueBodySha256"]
                    or not re.search(r"^Work level: Delivery\b", rules.visible_text(issue["body"]), re.MULTILINE)):
                refuse("ownership-issue-mismatch")
            if f"{args.repo}#{row['delivery']}" not in rules.pr_refs(args.repo, native["body"]):
                refuse("ownership-reference-missing")
            issue_fact = {"id": issue["id"], "number": issue["number"], "bodySha256": row["issueBodySha256"], "state": "open"}
            if initial:
                issues[number] = issue_fact
            elif issues[number] != issue_fact:
                refuse("ownership-drift")
        if initial:
            fresh = guards[selected["number"]]["body"]
            if not selected["branch"].startswith("routine/"):
                refuse("selected-not-routine")
            desired = lane.update_body(fresh, proposed, selected["branch"], selected["headSha"], spec["previousHead"], "", "", retain_legacy=True)
            fields = lane.routine_validator().marker_fields(lane.routine_validator().ROUTINE_MARKER_RE, desired)[0]
            if fields.get("operation") != "source-change":
                refuse("selected-operation-mismatch")
            if f"{args.repo}#{selected['delivery']}" not in rules.pr_refs(args.repo, desired):
                refuse("proposed-owner-reference-missing")
        auth, comparison = authorization(), source()
        if initial:
            authorization_before, source_before = auth, comparison
        elif authorization_before != auth or source_before != comparison:
            refuse("authorization-or-source-drift")

    with lane.repository_lock(args.repo):
        # Same stable PR identity across head/body/filename changes. Prior operations are never erased.
        root = lane.roster_custody_root() / ("legacy-update-" + lane.text_sha(args.repo.lower() + ":" + str(selected["id"])))
        root.mkdir(mode=0o700, exist_ok=True)
        info = root.lstat()
        if not stat.S_ISDIR(info.st_mode) or info.st_uid != os.getuid() or stat.S_IMODE(info.st_mode) & 0o077:
            raise lane.ToolError("unsafe continuation custody directory")
        # Persist newly created directory entries as well as journal contents. A crash
        # must not lose the stable custody directory after a provider effect.
        for directory in (root, *root.parents):
            descriptor = os.open(directory, os.O_RDONLY | os.O_DIRECTORY)
            try:
                os.fsync(descriptor)
            finally:
                os.close(descriptor)
        files = sorted(root.iterdir())
        history = []
        for index, path in enumerate(files, 1):
            info = path.lstat()
            if (path.name != f"{index:06d}.json" or not stat.S_ISREG(info.st_mode)
                    or info.st_uid != os.getuid() or stat.S_IMODE(info.st_mode) & 0o077 or info.st_size > 262144):
                raise lane.ToolError("unsafe continuation custody history")
            try:
                raw = path.read_text(encoding="utf-8")
                old = json.loads(raw)
                if old.get("schema") != SCHEMA or old.get("nativeId") != selected["id"] or old.get("repo") != args.repo:
                    raise ValueError("identity")
                history.append((old, lane.text_sha(raw)))
            except (ValueError, AttributeError, UnicodeError) as error:
                raise lane.ToolError("unreadable prior continuation custody; no resend") from error
        if history:
            prior, prior_sha = history[-1]
            successor = spec["successor"]
            if (not successor or successor["custodySha256"] != prior_sha or successor["intentSha256"] != prior.get("intentSha256")
                    or any(old.get("authorization", {}).get("commentId") == spec["authorization"]["commentId"] for old, _ in history)):
                native = get(f"/repos/{args.repo}/pulls/{selected['number']}")
                refuse("custody-exists-no-resend", priorIntentSha256=prior.get("intentSha256"), phase=prior.get("phase"),
                       observedMetadataSha256=guard_sha(lane, projection(native)))
        elif spec["successor"] is not None:
            refuse("successor-without-history")
        population(initial=True)
        if history:
            prior, _ = history[-1]
            delivered = spec["successor"]["disposition"] == "delivered"
            expected = prior.get("after" if delivered else "before", {})
            current = guards[selected["number"]]
            if (lane.text_sha(current["body"]) != expected.get("bodySha256")
                    or lane.text_sha(current["title"]) != expected.get("titleSha256")
                    or spec["previousHead"] != prior.get("currentHead" if delivered else "previousHead")
                    or (not delivered and prior.get("phase") == "complete")):
                refuse("successor-disposition-unproved")
        population()
        if args.inspect:
            return {"ok": True, "action": "admitted", **identity, "openNativePullRequestCount": len(spec["pulls"])}
        before = guards[selected["number"]]
        after = {**before, "body": desired, "title": args.title}
        def texts(guard):
            return {"bodySha256": lane.text_sha(guard["body"]), "titleSha256": lane.text_sha(guard["title"]),
                    "metadataSha256": guard_sha(lane, guard)}
        custody = {"schema": SCHEMA, **identity, "intent": intent, "authorization": authorization_before,
                   "source": source_before, "previousHead": spec["previousHead"], "currentHead": selected["headSha"],
                   "before": texts(before), "after": texts(after), "phase": "validated",
                   "population": {str(n): guard_sha(lane, g) for n, g in guards.items()},
                   "ownership": {str(n): fact for n, fact in issues.items()}}
        path = root / f"{len(history)+1:06d}.json"
        lane.save_roster_custody(path, custody, create=True)
        population()
        custody["phase"] = "possible-send"
        lane.save_roster_custody(path, custody)
        attempted = True
        try:
            lane.run_gh(args.gh, ["--method", "PATCH", f"/repos/{args.repo}/pulls/{selected['number']}", "--input", "-"],
                        stdin=json.dumps({"body": desired, "title": args.title}, ensure_ascii=False))
        except lane.ToolError:
            refuse("send-uncertain")
        guards[selected["number"]] = after
        try:
            population()
        except (lane.ToolError, lane.Refusal) as error:
            refuse("readback-unresolved", cause=str(error))
        custody["phase"] = "complete"
        custody["readbackPopulation"] = {str(n): guard_sha(lane, g) for n, g in guards.items()}
        lane.save_roster_custody(path, custody)
        return {"ok": True, "action": "updated-legacy-owner", **identity,
                "headSha": selected["headSha"], "custodySha256": lane.text_sha(path.read_text()),
                "openNativePullRequestCount": len(spec["pulls"])}
