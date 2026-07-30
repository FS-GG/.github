#!/usr/bin/env python3
"""Fleet release-tag anchor detector (.github#2033).

The immutable `<repository commit>` from every served nuspec is compared to the
peeled release tag.  This intentionally reports historical disagreement; it
never repairs tags.  `--fixture` is an offline TSV: repo, namespace, package,
version, artifact-commit, peeled-tag-commit (or `-`), one row per version.
"""
from __future__ import annotations
import argparse
from dataclasses import dataclass

@dataclass(frozen=True)
class Mapping:
    repo: str; namespace: str; package: str | None; grammar: str

# Authoritative roster publishers, including the two Rendering namespaces and
# both SDD packages.  A missing package is emitted UNCOVERED, never omitted.
FLEET: tuple[Mapping, ...] = (
    Mapping("FS-GG/.github", "kit/v", "FS.GG.Kit", "semver"),
    Mapping("FS-GG/.github", "coord-engine/v", "FS.GG.Coord.Cli", "semver"),
    Mapping("FS-GG/FS.GG.Game", "v", "FS.GG.Game.Core", "semver"),
    Mapping("FS-GG/FS.GG.Game", "skills/v", "FS.GG.Game.Skills", "semver"),
    Mapping("FS-GG/FS.GG.Governance", "v", "FS.GG.Governance.ReferenceGateSet", "semver"),
    Mapping("FS-GG/FS.GG.Rendering", "v", "FS.GG.UI", "semver"),
    Mapping("FS-GG/FS.GG.Rendering", "fs-gg-ui/v", "FS.GG.UI", "semver"),
    Mapping("FS-GG/FS.GG.Rendering", "fs-gg-ui-template/v", "FS.GG.UI.Template", "semver"),
    Mapping("FS-GG/FS.GG.Audio", "v", "FS.GG.Audio.Core", "semver"),
    Mapping("FS-GG/FS.GG.Templates", "fs-gg-templates/v", "FS.GG.Templates", "semver"),
    Mapping("FS-GG/FS.GG.Net", "v", "FS.GG.Net.Core", "semver"),
    Mapping("FS-GG/FS.GG.SDD", "v", "FS.GG.SDD.Cli", "semver"),
    Mapping("FS-GG/FS.GG.SDD", "v", "FS.GG.Contracts", "semver"),
)

# Historical dual-publish disagreements are evidence, not exemptions: these two commits must remain
# visible in any fleet report and no detector may "fix" them by moving the cited tags.
HISTORICAL_DUAL_PUBLISH = {
    ("FS-GG/.github", "coord-engine/v", "0.1.0"):
        ("94b044b1e575fc9da0105c32bd063b0f387a5eef", "78c3b5492263a33016e9e3bcac7816a14e9bb237"),
    ("FS-GG/.github", "new-sdd-fullstack/v", "0.1.1-preview.1"):
        ("2e73e5a02099947108663a1edace1214c56647a6", "775a11eec882e2184ea9a18a5f759bb54a9ba143"),
}

def classify(anchor: str, tag: str) -> str:
    if anchor == "-": return "UNRESOLVED"
    if tag == "-": return "MISSING"
    return "AGREE" if anchor == tag else "DISAGREE"

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--fixture"); ap.add_argument("--live", action="store_true")
    args = ap.parse_args()
    if args.live:
        # The live adapter deliberately shells out only for tags; feed/nuspec reads stay in the
        # proven single-repo implementation until its parser is promoted as shared infrastructure.
        # Keeping this entrypoint explicit makes a live invocation read-only and fail closed.
        ap.error("live sweep adapter is unavailable until SourceLink feed reader is promoted")
    if not args.fixture: ap.error("--fixture or --live is required")
    seen: set[tuple[str,str,str]] = set(); bad = False
    with open(args.fixture, encoding="utf-8") as f:
        for raw in f:
            if not raw.strip() or raw.startswith("#"): continue
            repo, ns, package, version, anchor, tag = raw.rstrip("\n").split("\t")
            seen.add((repo, ns, package)); verdict = classify(anchor, tag)
            print(f"{verdict}\t{repo}\t{ns}{version}\t{package}")
            bad |= verdict in {"DISAGREE", "MISSING", "UNRESOLVED"}
    for row in FLEET:
        if row.package and (row.repo, row.namespace, row.package) not in seen:
            print(f"UNCOVERED\t{row.repo}\t{row.namespace}*\t{row.package}"); bad = True
    return 1 if bad else 0
if __name__ == "__main__": raise SystemExit(main())
