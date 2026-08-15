#!/usr/bin/env python3
"""Fail-closed validation and authenticated replay of the M6 intent seed."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys

SHA = re.compile(r"^[0-9a-f]{40}$")
HASH = re.compile(r"^[0-9a-f]{64}$")
COMMENT = re.compile(
    r"^https://github\.com/(?P<owner>[^/]+)/(?P<repo>[^/]+)/issues/(?P<number>[0-9]+)"
    r"#issuecomment-(?P<comment>[0-9]+)$"
)


def sha256(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def load(path: Path) -> dict:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"{path}: root must be an object")
    return value


def validate(seed: dict, replay: dict, implementation_sha: str) -> list[str]:
    failures: list[str] = []
    if not SHA.fullmatch(implementation_sha):
        failures.append("implementation SHA must be exact lowercase 40-hex")
    if seed.get("implementation_sha") != implementation_sha:
        failures.append("seed does not bind the requested implementation SHA")
    if replay.get("implementation_sha") != implementation_sha:
        failures.append("replay does not bind the requested implementation SHA")
    rows = seed.get("rows")
    observations = replay.get("comments")
    if not isinstance(rows, list) or len(rows) != 24:
        failures.append("seed must contain exactly 24 rows")
        rows = []
    if not isinstance(observations, list) or len(observations) != 24:
        failures.append("replay must contain exactly 24 comment observations")
        observations = []
    by_url = {
        row.get("comment_url"): row for row in observations
        if isinstance(row, dict) and isinstance(row.get("comment_url"), str)
    }
    if len(by_url) != len(observations):
        failures.append("replay comment URLs must be unique")
    refs: set[str] = set()
    ids: set[int] = set()
    for index, row in enumerate(rows):
        if not isinstance(row, dict):
            failures.append(f"rows[{index}] must be an object")
            continue
        ref = row.get("ref")
        marker = row.get("marker")
        url = row.get("comment_url")
        if not isinstance(ref, str) or ref in refs:
            failures.append(f"rows[{index}] ref is missing or duplicated")
        else:
            refs.add(ref)
        if not isinstance(marker, str) or "fsgg:lifecycle-watermark v=2" not in marker:
            failures.append(f"rows[{index}] is not a v2 watermark")
            continue
        match = COMMENT.fullmatch(str(url))
        if match is None:
            failures.append(f"rows[{index}] comment URL is not canonical")
            continue
        comment_id = int(match.group("comment"))
        if comment_id in ids:
            failures.append(f"rows[{index}] comment id is duplicated")
        ids.add(comment_id)
        expected_ref = f"{match.group('owner')}/{match.group('repo')}#{match.group('number')}"
        if ref != expected_ref:
            failures.append(f"rows[{index}] ref does not match its comment URL")
        observation = by_url.get(url)
        if observation is None:
            failures.append(f"rows[{index}] has no authenticated replay observation")
            continue
        if observation.get("comment_id") != comment_id:
            failures.append(f"rows[{index}] replay comment id mismatch")
        if observation.get("marker_sha256") != sha256(marker):
            failures.append(f"rows[{index}] replay marker digest mismatch")
        if not observation.get("created_at") or observation.get("created_at") != observation.get("updated_at"):
            failures.append(f"rows[{index}] replay lacks immutable creation identity")
        if not observation.get("actor"):
            failures.append(f"rows[{index}] replay lacks actor identity")
    board = replay.get("board", {})
    if board.get("pagination_complete") is not True or board.get("rows") != 108 or board.get("unique_refs") != 108:
        failures.append("replay does not bind the complete 108-row board read")
    second = replay.get("second_pass", {})
    if second.get("would_post") != 0 or second.get("exact_existing") != 24 or second.get("conflicts") != 0:
        failures.append("replay does not prove an exact idempotent second pass")
    reproduce = replay.get("reproduce")
    if not isinstance(reproduce, list) or not reproduce or "--live-github" not in reproduce:
        failures.append("replay does not bind the authenticated reproduction argv")
    if not HASH.fullmatch(str(replay.get("raw_comments_sha256", ""))) or not HASH.fullmatch(str(board.get("stdout_sha256", ""))):
        failures.append("replay raw observation hashes are missing")
    return failures


def api_json(argv: list[str]) -> object:
    result = subprocess.run(argv, check=True, text=True, stdout=subprocess.PIPE)
    return json.loads(result.stdout)


def replay_live(seed: dict, replay: dict, coord_bin: str) -> list[str]:
    failures: list[str] = []
    board_result = subprocess.run(
        [coord_bin, "ready", "--all", "--json"], check=True, text=True, stdout=subprocess.PIPE
    )
    board = json.loads(board_result.stdout)
    if not isinstance(board, list):
        return ["live ready --all did not return an array"]
    board_refs = {f"{row.get('repo')}#{row.get('number')}" for row in board if isinstance(row, dict)}
    expected_board = replay["board"]
    if len(board) != expected_board["rows"] or len(board_refs) != expected_board["unique_refs"]:
        failures.append("live complete board population differs from replay")
    if hashlib.sha256(board_result.stdout.encode("utf-8")).hexdigest() != expected_board["stdout_sha256"]:
        failures.append("live complete board bytes differ from replay")
    observed_by_url = {row["comment_url"]: row for row in replay["comments"]}
    raw: list[dict] = []
    for row in seed["rows"]:
        match = COMMENT.fullmatch(row["comment_url"])
        assert match is not None
        response = api_json([
            "gh", "api",
            f"repos/{match.group('owner')}/{match.group('repo')}/issues/comments/{match.group('comment')}",
        ])
        if not isinstance(response, dict):
            failures.append(f"{row['ref']}: comment response is not an object")
            continue
        raw.append(response)
        expected = observed_by_url[row["comment_url"]]
        if response.get("html_url") != row["comment_url"] or response.get("id") != expected["comment_id"]:
            failures.append(f"{row['ref']}: live comment identity mismatch")
        if response.get("body") != row["marker"] or sha256(str(response.get("body", ""))) != expected["marker_sha256"]:
            failures.append(f"{row['ref']}: live marker bytes differ")
        if response.get("created_at") != expected["created_at"] or response.get("updated_at") != expected["updated_at"]:
            failures.append(f"{row['ref']}: live comment timestamps differ")
        if (response.get("user") or {}).get("login") != expected["actor"]:
            failures.append(f"{row['ref']}: live comment actor differs")
        if row["ref"] not in board_refs:
            failures.append(f"{row['ref']}: seed subject is absent from the complete board read")
    raw.sort(key=lambda row: str(row.get("html_url", "")))
    canonical_raw = json.dumps(raw, indent=2, sort_keys=False) + "\n"
    if hashlib.sha256(canonical_raw.encode("utf-8")).hexdigest() != replay["raw_comments_sha256"]:
        failures.append("live raw comment envelope digest differs from replay")
    return failures


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("seed")
    parser.add_argument("replay")
    parser.add_argument("--implementation-sha", required=True)
    parser.add_argument("--live-github", action="store_true")
    parser.add_argument("--coord-bin", default="scripts/fsgg-coord")
    args = parser.parse_args()
    try:
        seed = load(Path(args.seed))
        replay = load(Path(args.replay))
        failures = validate(seed, replay, args.implementation_sha)
        if args.live_github and not failures:
            failures.extend(replay_live(seed, replay, args.coord_bin))
    except (OSError, ValueError, json.JSONDecodeError, subprocess.CalledProcessError) as error:
        failures = [str(error)]
    if failures:
        print("M6 live intent seed: FAIL", file=sys.stderr)
        for failure in failures:
            print(f"- {failure}", file=sys.stderr)
        return 1
    mode = "authenticated live replay" if args.live_github else "offline binding"
    print(f"M6 live intent seed: PASS — 24 exact v2 comments, complete board, idempotent second pass ({mode})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
