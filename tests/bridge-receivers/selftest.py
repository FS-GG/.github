#!/usr/bin/env python3
"""Independent refusal mutations for the shared receiver proof."""

from __future__ import annotations

import argparse
import copy
import json
import pathlib
import shutil
import subprocess
import tempfile


HERE = pathlib.Path(__file__).resolve().parent
BASE_EVIDENCE = HERE.parents[1] / "docs/reports/gs2-08-8-bridge-adoption.json"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--receiver-root", type=pathlib.Path, required=True)
    parser.add_argument("--receiver-revision", required=True)
    parser.add_argument("--receiver-tree", required=True)
    parser.add_argument("--release-manifest", type=pathlib.Path, required=True)
    parser.add_argument("--packages", type=pathlib.Path, required=True)
    args = parser.parse_args()
    original = json.loads(BASE_EVIDENCE.read_text())

    with tempfile.TemporaryDirectory(prefix="fsgg-bridge-receiver-selftest-") as temporary:
        work = pathlib.Path(temporary)

        def refuses(label: str, mutate, contains: str, package_mutation=None) -> None:
            evidence = copy.deepcopy(original)
            mutate(evidence)
            evidence_path = work / (label.replace(" ", "-") + ".json")
            evidence_path.write_text(json.dumps(evidence))
            package_dir = args.packages
            if package_mutation:
                package_dir = work / (label.replace(" ", "-") + "-packages")
                shutil.copytree(args.packages, package_dir)
                package_mutation(package_dir)
            command = ["python3", str(HERE / "run.py"), "validate", "--receiver-root", str(args.receiver_root),
                       "--receiver-revision", args.receiver_revision, "--receiver-tree", args.receiver_tree,
                       "--evidence", str(evidence_path), "--release-manifest", str(args.release_manifest),
                       "--packages", str(package_dir)]
            result = subprocess.run(command, text=True, capture_output=True)
            output = result.stdout + result.stderr
            if result.returncode == 0 or contains not in output:
                raise SystemExit(f"bridge-receivers-selftest: {label} survived or gave wrong refusal: {output}")
            print(f"PASS  {label}")

        refuses("missing receiver", lambda value: value.pop("receiver"), "incomplete or unsupported schema")
        refuses("unaccounted route", lambda value: value["routes"].pop(), "unaccounted route")

        def old_cli(value):
            next(row for row in value["packages"] if row["id"] == "FS.GG.Coord.Cli")["version"] = "0.89.0"
        refuses("old CLI", old_cli, "wrong coherent identity")

        def substitute(package_dir: pathlib.Path) -> None:
            with (package_dir / "fs.gg.coord.cli.0.90.0.nupkg").open("ab") as stream:
                stream.write(b"substituted")
        refuses("substituted package", lambda value: None, "substituted public package", substitute)

        def false_immutable(value):
            value["callableRoutes"][0]["ref"] = "immutable"
        refuses("mutable dependency falsely immutable", false_immutable, "mutable callable dependency")

    print("bridge-receivers-selftest: PASS (5 refusal controls)")


if __name__ == "__main__":
    main()
