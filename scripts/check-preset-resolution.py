#!/usr/bin/env python3
"""Resolve the org Renovate preset with Renovate itself.

Schema validation cannot show whether the preset and its transitive `extends`
entries actually resolve, or whether the resolved configuration retains the
facts the organization depends on.
"""

import argparse
import json
import os
import subprocess
import sys
import tempfile


def classify_resolution_failure(stderr: str) -> int:
    """Return 2 for an indeterminate network failure, otherwise a red verdict."""
    network_markers = (
        "ECONNREFUSED",
        "ECONNRESET",
        "EAI_AGAIN",
        "ENETUNREACH",
        "ENOTFOUND",
        "ETIMEDOUT",
        "fetch failed",
    )
    return 2 if any(marker in stderr for marker in network_markers) else 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--file", default="default.json")
    parser.add_argument(
        "--simulate-network-failure",
        action="store_true",
        help=argparse.SUPPRESS,
    )
    args = parser.parse_args()

    # This controlled seam keeps the fixture deterministic: CI can prove that
    # callers receive an indeterminate exit code without disconnecting a runner.
    if args.simulate_network_failure:
        print("preset resolution: simulated network failure (no verdict)", file=sys.stderr)
        return 2

    try:
        with open(args.file, encoding="utf8") as preset_file:
            json.load(preset_file)
    except (OSError, json.JSONDecodeError) as error:
        print(f"preset resolution: cannot read {args.file}: {error}", file=sys.stderr)
        return 3

    driver = '''
import fs from "node:fs";
import { resolveConfigPresets } from "renovate/dist/config/presets/index.js";

const raw = JSON.parse(fs.readFileSync(process.argv[2], "utf8"));
const { config, visitedPresets } = await resolveConfigPresets(raw);
console.log(JSON.stringify({ config, visitedPresets }));
'''
    driver_path = None
    try:
        # Place the temporary module below the checkout so Node resolves its
        # installed renovate package from this repository's node_modules.
        with tempfile.NamedTemporaryFile(
            "w", suffix=".mjs", dir=os.getcwd(), delete=False
        ) as driver_file:
            driver_file.write(driver)
            driver_path = driver_file.name
        result = subprocess.run(
            ["node", driver_path, args.file],
            text=True,
            capture_output=True,
            timeout=60,
            check=False,
        )
    except subprocess.TimeoutExpired:
        print("preset resolution: Renovate timed out (no verdict)", file=sys.stderr)
        return 2
    except OSError as error:
        print(f"preset resolution: cannot start Renovate: {error}", file=sys.stderr)
        return 3
    finally:
        if driver_path:
            try:
                os.unlink(driver_path)
            except FileNotFoundError:
                pass

    if result.returncode:
        outcome = classify_resolution_failure(result.stderr)
        status = "no verdict" if outcome == 2 else "invalid preset"
        print(
            f"preset resolution: {status}: {result.stderr.strip()}",
            file=sys.stderr,
        )
        return outcome

    try:
        resolved = json.loads(result.stdout)["config"]
    except (json.JSONDecodeError, KeyError, TypeError):
        print("preset resolution: Renovate returned no readable resolved config", file=sys.stderr)
        return 3

    if not resolved.get("gitIgnoredAuthors"):
        print("preset resolution: gitIgnoredAuthors vanished after resolution", file=sys.stderr)
        return 1
    package_rules = resolved.get("packageRules") or []
    if not package_rules:
        print("preset resolution: no resolved packageRules (no verdict)", file=sys.stderr)
        return 3
    if not any(
        rule.get("enabled") is False and rule.get("matchRepositories")
        for rule in package_rules
    ):
        print(
            "preset resolution: no disabled matchRepositories build-config rule "
            "survived resolution",
            file=sys.stderr,
        )
        return 1

    print("ok: Renovate resolved preset and required resolved facts survive")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
