#!/usr/bin/env python3
"""Is THIS `FS.GG.Kit` bump PR mechanical? Decide it from the diff's SHAPE, derived from the package.

.github#1693, correcting one premise of .github#1587. #1587 makes automerge safe by asserting a class
rather than trusting an author: a bump PR may touch only (a) the kit pin and (b) what the materializer
itself produces. This script is that assertion, and it exists as its own file because #1587's clause (b)
was measured against a world that ended on 2026-07-27.

WHAT CHANGED, AND WHY A LITERAL READING OF #1587 WOULD REFUSE EVERY BUMP
  #1587 measured four landed bumps and found each was "exactly two files — a one-line version bump plus
  `scripts/fsgg-coord` as materialized output". A guard that derives (b) from `FsggKitSkillRoots` alone
  is correct for that world and WRONG for every bump after it:

    * `FS.GG.Kit` 0.14.0 (ADR-0067 §5, #1636) RETIRED `.codex/skills`. Materializing it writes skills
      into two roots instead of three AND DELETES the receiver's whole `.codex/skills` tree — the
      materializer doing it, precisely because ADR-0065's transport contract forbids a receiver
      hand-deleting a mirror.
    * `FS.GG.Kit` 0.15.0 (#1696) added a THIRD root kind, `FsggKitViewSkillRoots`, whose
      previously-materialized copies are swept the same way.

  So the bump carries the pin change PLUS deletions under roots that are not in `FsggKitSkillRoots`.
  A guard reading only that property sees them as contamination and refuses — on all seven receivers,
  blocking the one bump that carries the retirement, and implying the one repair ("hand-delete
  `.codex/skills` and re-run") that ADR-0065 §Retiring a root forbids in as many words.

  MEASURED, not argued (#1693) — three receiver COPIES materialized against the published 0.15.0, one
  for each of the three places #1587 AC 4 says a receiver pins, with the real checkouts untouched:

    FS.GG.Net        0.8.0  -> 0.15.0   35 paths = 1 pin (Directory.Packages.props)       + 8 M + 3 A + 23 D
    FS.GG.SDD        0.10.0 -> 0.15.0   34 paths = 1 pin (Directory.Packages.local.props) + 7 M + 3 A + 23 D
    FS.GG.Templates  0.8.0  -> 0.15.0   35 paths = 1 pin (.config/kit/…receiver.proj)     + 8 M + 3 A + 23 D

  Every one of those 23 deletions is under `.codex/skills`. Not one of them is inside
  `FsggKitSkillRoots`.

THE CLASS THIS ADMITS, STATED SO A HUMAN CAN CHECK IT BY HAND
  Everything below is read from the TARGET version's package — the version the PR is bumping TO — and
  from the receiver's own effective MSBuild evaluation of it. Nothing here is a list.

    SKILLS  = the first path segment of every `kind: skill` destination in `kit/kit-manifest.tsv`
    FLAT    = the destination of every `kind: client` / `kind: config` row, plus the `kind: build-config`
              rows if and only if the receiver evaluates `FsggKitMaterializeBuildConfig` true
    LIVE    = FsggKitSkillRoots           (the roots this package materializes INTO)
    RETIRED = FsggKitRetiredSkillRoots    (roots that LEFT the runtime contract; swept)
    VIEW    = FsggKitViewSkillRoots       (roots still IN the contract whose content is GENERATED; swept)

  A changed path is admissible iff it is exactly one of:

    1. THE PIN — the single file whose changed lines are all `FS.GG.Kit` version declarations, every
       added one naming the target version. Modify only. The pin's LOCATION is derived from the diff,
       never from a list of the three places receivers pin (#1587 AC 4).
    2. A FLAT destination, added or modified. Exact path equality — not a prefix.
    3. `<root>/<dest>` for root in LIVE and `<dest>` a skill row's destination — added or modified.
    4. `<root>/<skill>/...` for root in LIVE and skill in SKILLS — DELETED. This is the materializer's
       managed-skill-directory sweep: "a skill directory is a closed transport unit", so a reference
       file dropped between versions is removed.
    5. `<root>/<skill>/...` for root in RETIRED or VIEW and skill in SKILLS — DELETED, AND NOTHING ELSE.
       A retired or view root admits deletions only.

  Anything else is a FINDING. In particular a deletion under a retired root of a directory that is NOT
  one of the kit's own skills is refused — the materializer leaves a receiver's own skill alone, so a
  bump PR that removes one is not a bump PR.

WHY VIEW ROOTS ARE ADMITTED, AND WHY THEY ARE STILL READ SEPARATELY
  Their DIFF disposition is identical to a retired root's — `FS.GG.Kit.targets` sweeps both by deleting
  the kit's own skill directories, and a view root's generated content (a symlink, or a copy carrying a
  `.skill-view` receipt) is never committed, because `scripts/skill-view` refuses to generate over a root
  git tracks. So a view root only ever loses files from a receiver's tree.

  They are nonetheless read from their OWN property rather than folded into the retired set, because the
  two differ where it matters elsewhere: a view root STAYS in the runtime contract (`agentSkillRoots`,
  `.agent-skill-roots`, `coordination-sync`, `KitDigest` keep counting it) and a retired root leaves it.
  Spelling them the same here would be a second copy waiting to disagree, which is the class #1693 was
  filed to avoid. `FsggKitViewSkillRoots` is EMPTY by default, so no receiver has one today; this admits
  the first one that does, with no edit here.

THE MIDDLE CLASS, AND WHY A TWO-VALUED ANSWER IS NOT WORTH REPORTING (.github#1726, #1713)
  #1587's premise — "the class of change is provably mechanical" — was measured on 2026-07-28 against
  the seven receivers being brought current in one morning. THREE OF THE SEVEN needed genuine
  receiver-side changes, so measured coverage of the two-valued rule is about 4 of 7, and the cases it
  excludes are exactly the ones a human would most want automated.

  `FS.GG.Rendering#1088` is the decisive one. 36 files: the pin, 29 materialized paths, 5 kit-owned
  clients/configs — and `scripts/materialize-skill-roots.sh`, a file RENDERING WROTE, whose stale
  three-root expectation 0.15.0 invalidated. That repair cannot land before the bump (at the old pin
  the expectation is correct) or after it (the bump is red until it lands). It has to ride the bump,
  and merging it was right.

  So the answer this script owes a reader is not pass/fail. It is WHICH OF THESE THREE:

    mechanical         the pin and what the materializer produces, and nothing else.
    mechanical+repair  all of that, PLUS receiver-authored files a human wrote. The kit's own
                       territory is exactly as the materializer left it; the reading a human owes
                       this PR is the listed receiver files and nothing more.
    not mechanical     anything else — the kit's OWN territory does not match what the materializer
                       would have produced, so nothing in this diff can be taken on trust.

  That distinction is the whole value: it turns "seven PRs a human must read" into "three PRs a human
  must read", and it does it without gating anything.

  A FINDING IS A REPAIR ONLY IF IT IS BOTH:
    1. OUTSIDE KIT TERRITORY — not a destination this package declares in `kit-manifest.tsv` (any
       kind, INCLUDING build-config rows a receiver has not opted into: content appearing at a
       destination the receiver does not receive is the kit's territory going wrong, not a repair),
       and not under any declared root, live or retired or view; and
    2. AN ADD OR A MODIFY. A deletion is never a repair. Deleting is the materializer's own
       vocabulary — the managed-skill-directory sweep, the retired-root sweep, the view-root sweep —
       and a deletion this script cannot attribute to one of them is precisely the thing it exists to
       catch. A human who genuinely must delete one of their own files alongside a bump gets
       `not mechanical` and a read, which is the safe direction.

  A single kit-territory finding makes the whole PR `not mechanical`, however many repairs sit beside
  it. The milder class is not a union; it is a statement about the kit's half being clean.

FAIL CLOSED (epic #266). "I could not decide" is never spelled like "I decided, and it's fine":

    exit 0  MECHANICAL     — the diff is exactly the admissible class. Safe to automerge.
    exit 1  NOT-MECHANICAL — a kit bump whose KIT territory carries something the materializer would
                             not have produced, or which deletes a file no sweep accounts for.
                             Automerge must not fire.
    exit 2  NOT-A-KIT-BUMP — no `FS.GG.Kit` pin change in this diff. The guard ABSTAINS; abstention is
                             not a pass, and #1587's automerge must treat it as "do not merge".
    exit 3  REFUSED        — the inputs cannot support a verdict (no manifest, no skill rows, a root
                             declared in two of the three properties, an unreadable diff, a rename,
                             or a pin change with no package/properties to judge it against).
                             Never guessed.
    exit 4  MECHANICAL+REPAIR — mechanical in kit territory, plus receiver-authored files a human
                             wrote. NOT a pass: automerge fires on 0 ALONE. It is a REPORT that the
                             reading this PR needs is bounded, and names its bounds.

  Exit 4 rather than a sub-case of 0 is the #266 rule applied to the new class: an automerge rule, a
  shell `if`, and a human skimming a check list must all be able to tell "no human need read this"
  from "read these three files", and they must not be able to confuse either with a refusal.

HOW A CALLER PRODUCES THE INPUTS — all three are derivations, none is a restatement:

    dotnet build .config/kit/FS.GG.Kit.receiver.proj -t:FsggKitMaterialize      # so the pin is restored
    dotnet msbuild .config/kit/FS.GG.Kit.receiver.proj \\
        -getProperty:FsggKitSkillRoots,FsggKitRetiredSkillRoots,FsggKitViewSkillRoots,FsggKitMaterializeBuildConfig \\
        > /tmp/kit-props.json
    python3 check-kit-bump-shape.py --repo . --base "$BASE" --head "$HEAD" \\
        --kit-dir ~/.nuget/packages/fs.gg.kit/<target> --properties /tmp/kit-props.json

  `-getProperty` is the receiver's OWN evaluation of the target package's `build/FS.GG.Kit.props`, so a
  receiver that overrides a root set is judged by what it actually declares, not by the package default.
  The `--kit-dir` version is read from the package's nuspec and asserted equal to the pin the diff moves
  to, so pointing this at the wrong package is a refusal rather than a wrong verdict.

  `--kit-dir` AND `--properties` ARE OPTIONAL, AND ONLY BECAUSE OF WHAT THAT BUYS A RECEIVER-SIDE
  CALLER. Whether a diff moves the pin at all is decided from the diff alone, before any package is
  needed — so a caller may run this with three arguments to get the abstention (exit 2) for free, and
  pay for a .NET SDK, a restore and an MSBuild evaluation ONLY on the pull requests that actually move
  the pin. That is what makes the receiver-side reporter cheap enough to run UNGATED on every pull
  request, which #1508 requires of anything a branch might one day require. Omitting them on a diff
  that DOES move the pin is a REFUSAL (exit 3), never a pass and never an abstention.

WHICH COPY OF THIS FILE A RECEIVER RUNS (.github#1772, #1584, ADR-0067 §2)
  This file is canonical HERE and is copied nowhere. The receiver-side reporter — the `bump-shape` job
  in `.github/workflows/kit-materialize.yml` — fetches it at the commit tagged `kit/v<the FS.GG.Kit
  version the receiver's own restore resolved>`, so a receiver's verdict is a pure function of the
  receiver's tree and an immutable ref. Editing this file therefore does NOT change any receiver's
  report until a kit release carries the edit and that receiver's bump PR targets it.

  That is the point, and it is the difference between a report and a gate. #1713 shipped the reporter
  reading `FS-GG/.github@main`, which made a receiver's answer a function of WHEN it ran — the measured
  #1584 defect (`FS.GG.SDD#724`: green on `0376309` at 08:15Z, red on byte-identical content at 08:21Z).
  A required context with that property is not a gate; it is a clock.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

MECHANICAL, NOT_MECHANICAL, NOT_A_KIT_BUMP, REFUSED, MECHANICAL_PLUS_REPAIR = 0, 1, 2, 3, 4

PACKAGE = "FS.GG.Kit"

# The pin, in every location a receiver uses (#1587 AC 4 names three: `Directory.Packages.props`,
# `Directory.Packages.local.props`, and an inline `Version=` on the `PackageReference` in
# `.config/kit/FS.GG.Kit.receiver.proj`). This matches the DECLARATION, not the file — which is why
# the three locations need no list: whichever file carries the declaration is the pin file.
PIN_INCLUDE = re.compile(r'Include\s*=\s*"FS\.GG\.Kit"')
PIN_VERSION = re.compile(r'Version\s*=\s*"([^"]+)"')

PROPERTY_NAMES = (
    "FsggKitSkillRoots",
    "FsggKitRetiredSkillRoots",
    "FsggKitViewSkillRoots",
    "FsggKitMaterializeBuildConfig",
)


class Refused(Exception):
    """The inputs cannot support a verdict. Exit 3, never a guess."""


def git(repo: Path, *args: str) -> str:
    proc = subprocess.run(
        ["git", "-C", str(repo), *args],
        capture_output=True,
        text=True,
        check=False,
    )
    if proc.returncode != 0:
        raise Refused(f"git {' '.join(args)} failed: {proc.stderr.strip()}")
    return proc.stdout


def read_package_version(kit_dir: Path) -> str:
    """The target package's own version, from its nuspec. Refuses rather than trusting the path."""
    nuspecs = sorted(kit_dir.glob("*.nuspec"))
    if len(nuspecs) != 1:
        raise Refused(
            f"{kit_dir} holds {len(nuspecs)} .nuspec file(s); a restored package holds exactly one. "
            "Point --kit-dir at the extracted package root (e.g. ~/.nuget/packages/fs.gg.kit/<version>)."
        )
    text = nuspecs[0].read_text(encoding="utf-8")
    found = re.search(r"<version>\s*([^<\s]+)\s*</version>", text, re.IGNORECASE)
    if not found:
        raise Refused(f"{nuspecs[0]} declares no <version>.")
    return found.group(1)


def read_manifest(kit_dir: Path) -> list[tuple[str, str]]:
    """(kind, receiver destination) for every row of the target package's kit-manifest.tsv."""
    manifest = kit_dir / "kit" / "kit-manifest.tsv"
    if not manifest.is_file():
        raise Refused(
            f"{manifest} is missing — this package carries no kit manifest, so there is nothing to "
            "derive the admissible set FROM. Refusing rather than admitting an empty set."
        )
    rows: list[tuple[str, str]] = []
    for number, line in enumerate(manifest.read_text(encoding="utf-8").splitlines(), start=1):
        if not line.strip():
            continue
        fields = line.split("\t")
        if len(fields) < 4:
            raise Refused(f"{manifest}:{number}: malformed row (expected >=4 tab-separated fields).")
        dest = fields[2].replace("\\", "/").strip()
        # Validated BEFORE any normalization: an absolute or escaping destination must be a refusal,
        # never something quietly rewritten into a plausible relative path. The materializer refuses
        # the same shapes for the same reason — there its consequence is a recursive delete outside
        # the root it was told to sweep.
        if not dest or dest.startswith("/") or dest.startswith("../") or "/../" in dest or dest == "..":
            raise Refused(f"{manifest}:{number}: destination {dest!r} is absolute or escapes the receiver root.")
        rows.append((fields[0], dest.rstrip("/")))
    if not rows:
        raise Refused(f"{manifest} names no members.")
    return rows


def read_properties(path: Path) -> dict[str, str]:
    """The receiver's own MSBuild evaluation, as `dotnet msbuild -getProperty:` emits it."""
    try:
        raw = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise Refused(f"cannot read {path}: {error}") from error
    if not isinstance(raw, dict):
        raise Refused(f"{path}: expected an object.")
    props = raw.get("Properties", raw)
    if not isinstance(props, dict):
        raise Refused(f"{path}: 'Properties' is not an object.")
    missing = [name for name in PROPERTY_NAMES if name not in props]
    if missing:
        raise Refused(
            f"{path}: missing evaluated propert(y|ies): {', '.join(missing)}. Produce it with "
            f"`dotnet msbuild <receiver.proj> -getProperty:{','.join(PROPERTY_NAMES)}` so the roots "
            "are the receiver's OWN evaluation of the target package, not a default typed here."
        )
    return {name: str(props[name] or "") for name in PROPERTY_NAMES}


def split_roots(value: str) -> list[str]:
    return [part.strip().strip("/") for part in value.split(";") if part.strip()]


def is_truthy(value: str) -> bool:
    return value.strip().lower() == "true"


class Shape:
    """The admissible class, derived from the target package and the receiver's evaluation."""

    def __init__(self, kit_dir: Path, properties: dict[str, str]) -> None:
        self.version = read_package_version(kit_dir)
        rows = read_manifest(kit_dir)

        self.live = split_roots(properties["FsggKitSkillRoots"])
        self.retired = split_roots(properties["FsggKitRetiredSkillRoots"])
        self.view = split_roots(properties["FsggKitViewSkillRoots"])
        self.build_config = is_truthy(properties["FsggKitMaterializeBuildConfig"])

        # The materializer REFUSES a root declared in more than one property rather than picking a
        # disposition (FS.GG.Kit.targets). A guard that resolved the ambiguity differently would
        # certify a tree the materializer will not produce, so it refuses in the same place.
        for a_name, a, b_name, b in (
            ("FsggKitSkillRoots", self.live, "FsggKitRetiredSkillRoots", self.retired),
            ("FsggKitSkillRoots", self.live, "FsggKitViewSkillRoots", self.view),
            ("FsggKitRetiredSkillRoots", self.retired, "FsggKitViewSkillRoots", self.view),
        ):
            both = sorted(set(a) & set(b))
            if both:
                raise Refused(
                    f"root(s) {', '.join(both)} declared in BOTH {a_name} and {b_name}. The "
                    "materializer refuses this rather than choosing a disposition; so does this guard."
                )
        if not self.live:
            raise Refused(
                "FsggKitSkillRoots is empty — the target package materializes into no root. "
                "Refusing: an empty live set would make every materialized path a finding."
            )

        # SKILLS: the top-level directory name of each skill destination. This, not the full
        # destination, is the unit the sweeps delete — `FS.GG.Kit.targets` removes
        # `<root>/<skillName>` recursively — so it is the unit the delete rules are keyed on.
        self.skills: set[str] = set()
        self.skill_dests: set[str] = set()
        self.flat: set[str] = set()
        # EVERY non-skill destination the package declares, whether or not THIS receiver opts into it.
        # `flat` is what the materializer may WRITE here; this is what belongs to the KIT here. They
        # differ by exactly the build-config rows of a receiver that does not receive build-config,
        # and that difference is load-bearing: a `Directory.Build.props` appearing in such a
        # receiver's bump is the kit's own territory being written by something that is not the
        # materializer, which is `not mechanical` — never a receiver-authored repair.
        self.declared_dests: set[str] = set()
        for kind, dest in rows:
            if kind == "skill":
                self.skills.add(dest.split("/", 1)[0])
                self.skill_dests.add(dest)
            elif kind in ("client", "config"):
                self.flat.add(dest)
                self.declared_dests.add(dest)
            elif kind == "build-config":
                self.declared_dests.add(dest)
                # Opt-in, per receiver. A receiver that does not receive build-config never has these
                # written, so admitting them unconditionally would widen the class for four repos that
                # cannot produce them.
                if self.build_config:
                    self.flat.add(dest)
            else:
                raise Refused(f"unknown manifest kind {kind!r} — refusing to classify what it produces.")

        if not self.skills:
            raise Refused(
                "the target package's manifest names no skill rows — refusing to certify a bump "
                "against an empty skill set (epic #266: an empty subject must not satisfy itself)."
            )

    def writable(self) -> set[str]:
        """Paths the materializer WRITES: exact destinations only, never a prefix."""
        paths = set(self.flat)
        for root in self.live:
            for dest in self.skill_dests:
                paths.add(f"{root}/{dest}")
        return paths

    def deletable_prefixes(self) -> list[tuple[str, str]]:
        """(prefix, why) for every managed skill directory any sweep may delete from."""
        out: list[tuple[str, str]] = []
        for root in self.live:
            for skill in sorted(self.skills):
                out.append((f"{root}/{skill}/", f"managed-skill-directory sweep under live root {root}"))
        for root in self.retired:
            for skill in sorted(self.skills):
                out.append((f"{root}/{skill}/", f"retired-root sweep under {root} (deletions only)"))
        for root in self.view:
            for skill in sorted(self.skills):
                out.append((f"{root}/{skill}/", f"view-root sweep under {root} (deletions only)"))
        return out

    def delete_only_roots(self) -> list[str]:
        return self.retired + self.view

    def territory(self, path: str) -> str:
        """"kit" if this path belongs to the package's own surface here, else "receiver".

        This is a WIDER question than `writable()`, and deliberately so. `writable()` asks "would the
        materializer have produced exactly this?"; this asks "is this the kit's ground at all?" —
        which is what decides whether a finding is a receiver-authored repair (readable, bounded) or
        the kit's own output being wrong (not readable at all without checking the whole diff).

        Kit ground is every destination the TARGET package declares, plus everything under every root
        it declares in ANY of the three dispositions. Nothing here is a list; both come from the
        package and the receiver's own evaluation of it, exactly as the admissible class does.
        """
        if path in self.declared_dests:
            return "kit"
        for root in self.live + self.retired + self.view:
            if path == root or path.startswith(root + "/"):
                return "kit"
        return "receiver"


def changed_paths(repo: Path, base: str, head: str) -> list[tuple[str, str]]:
    """(status, path) for the PR's diff, as add/modify/delete and nothing else.

    `--no-renames` is deliberate and is the whole reason only three statuses appear: a rename arrives
    as a delete plus an add, and each half is then judged on its own merits against the class. That is
    the STRICTER reading — a rename out of a materialized path is a deletion the materializer did not
    make, and rename detection would have hidden it behind an `R`. Anything git still reports (a
    typechange `T`, an unmerged `U`) is refused rather than mapped onto one of the three.
    """
    raw = git(repo, "diff", "--no-renames", "--name-status", "-z", f"{base}...{head}")
    fields = [f for f in raw.split("\0") if f]
    if len(fields) % 2 != 0:
        raise Refused("git diff --name-status -z produced an odd number of fields.")
    out: list[tuple[str, str]] = []
    for status, path in zip(fields[0::2], fields[1::2]):
        code = status[0]
        if code not in ("A", "M", "D"):
            raise Refused(
                f"diff status {status!r} on {path} is not add/modify/delete. The materializer produces "
                "only those three; anything else is outside this guard's competence, so it refuses."
            )
        out.append((code, path.replace("\\", "/")))
    return out


def pin_files(repo: Path, base: str, head: str, modified: list[str]) -> dict[str, dict]:
    """Find the pin BY ITS CONTENT. Returns {path: {"to": …, "from": …, "clean": bool}}."""
    found: dict[str, dict] = {}
    for path in modified:
        patch = git(repo, "diff", "-U0", "--no-renames", f"{base}...{head}", "--", path)
        added = [ln[1:] for ln in patch.splitlines() if ln.startswith("+") and not ln.startswith("+++")]
        removed = [ln[1:] for ln in patch.splitlines() if ln.startswith("-") and not ln.startswith("---")]
        if not any(PIN_INCLUDE.search(ln) for ln in added + removed):
            continue
        # Every changed line in this file must BE a pin declaration. A bump PR that also edits a
        # neighbouring line of the same file is not a one-line version bump, and #1587's whole claim
        # is that the class is provably mechanical.
        stray = [ln for ln in added + removed if not PIN_INCLUDE.search(ln)]
        versions_to = [PIN_VERSION.search(ln) for ln in added]
        versions_from = [PIN_VERSION.search(ln) for ln in removed]
        found[path] = {
            "clean": not stray and all(versions_to) and all(versions_from) and bool(added),
            "removes_only": bool(removed) and not added,
            "stray": stray,
            "to": sorted({m.group(1) for m in versions_to if m}),
            "from": sorted({m.group(1) for m in versions_from if m}),
        }
    return found


def classify(shape: Shape, entries: list[tuple[str, str]], pin: str) -> list[tuple[str, str, str]]:
    """(status, path, reason-or-'') — reason non-empty means the path is a finding."""
    writable = shape.writable()
    prefixes = shape.deletable_prefixes()
    delete_only = set(shape.delete_only_roots())
    verdicts: list[tuple[str, str, str]] = []
    for status, path in entries:
        if path == pin:
            verdicts.append((status, path, ""))
            continue
        if status in ("A", "M"):
            if path in writable:
                verdicts.append((status, path, ""))
                continue
            root = next((r for r in delete_only if path == r or path.startswith(r + "/")), None)
            if root is not None:
                verdicts.append((
                    status,
                    path,
                    f"{'added' if status == 'A' else 'modified'} under {root}, which the target "
                    "version declares RETIRED or VIEW — such a root admits DELETIONS ONLY. The "
                    "materializer never writes there, so this content came from somewhere else.",
                ))
                continue
            verdicts.append((
                status,
                path,
                "not a destination the target package materializes. The admissible write set is the "
                "manifest's client/config destinations plus <live root>/<skill destination>.",
            ))
            continue
        # status == "D"
        why = next((why for prefix, why in prefixes if path.startswith(prefix)), None)
        if why is not None:
            verdicts.append((status, path, ""))
            continue
        verdicts.append((
            status,
            path,
            "deleted outside every managed skill directory the target version declares. Deletions are "
            f"admissible only under <root>/<skill>/ for root in {shape.live + shape.retired + shape.view} "
            "and skill in the target manifest's own skill set.",
        ))
    return verdicts


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(
        description="Decide whether an FS.GG.Kit bump PR's diff is the mechanical, automergeable shape."
    )
    parser.add_argument("--repo", required=True, type=Path, help="receiver checkout to read the diff from")
    parser.add_argument("--base", required=True, help="the PR's base ref")
    parser.add_argument("--head", required=True, help="the PR's head ref")
    # Optional so a caller can buy the abstention (exit 2) for the price of a git diff — see the
    # module docstring. A pin change with these missing is a REFUSAL, never a pass.
    parser.add_argument("--kit-dir", type=Path, help="the TARGET version's extracted package")
    parser.add_argument("--properties", type=Path, help="`dotnet msbuild -getProperty:` JSON")
    parser.add_argument("--json", action="store_true", help="emit the verdict as one object")
    args = parser.parse_args(argv)

    try:
        entries = changed_paths(args.repo, args.base, args.head)
        modified = [path for status, path in entries if status == "M"]
        pins = pin_files(args.repo, args.base, args.head, modified)

        if not pins:
            verdict = {
                "verdict": "not-a-kit-bump",
                "reason": f"no {PACKAGE} version declaration changes in {args.base}...{args.head}",
                "changed": len(entries),
            }
            code = NOT_A_KIT_BUMP
        else:
            missing = [
                flag
                for flag, value in (("--kit-dir", args.kit_dir), ("--properties", args.properties))
                if value is None
            ]
            if missing:
                raise Refused(
                    f"this diff MOVES the {PACKAGE} pin, so a shape verdict needs {' and '.join(missing)}. "
                    "Run again with the target version's extracted package and the receiver's evaluated "
                    "properties. A caller that omits them gets a refusal and never an abstention: "
                    "'no package to judge against' must not be spelled like 'this is not a kit bump'."
                )
            shape = Shape(args.kit_dir, read_properties(args.properties))
            if len(pins) != 1:
                raise Refused(
                    f"{len(pins)} files change a {PACKAGE} pin ({', '.join(sorted(pins))}). A receiver "
                    "pins in exactly one place; two is a receiver defect, not a mechanical bump."
                )
            pin_path, pin_info = next(iter(pins.items()))
            if pin_path in shape.writable():
                raise Refused(
                    f"{pin_path} is BOTH the pin and a destination the target package materializes. "
                    "Re-materializing would overwrite the pin; refusing rather than certifying it."
                )
            if pin_info["removes_only"]:
                raise Refused(
                    f"{pin_path} REMOVES a {PACKAGE} pin declaration and adds none. That is an "
                    "offboarding, not a bump; this guard has no mechanical class for it."
                )
            if not pin_info["clean"]:
                raise Refused(
                    f"{pin_path} changes lines that are not {PACKAGE} version declarations "
                    f"({len(pin_info['stray'])} of them) or a declaration whose version cannot be read."
                )
            if pin_info["to"] != [shape.version]:
                raise Refused(
                    f"the diff moves the pin to {pin_info['to']} but --kit-dir is {PACKAGE} "
                    f"{shape.version}. Restore the version this PR bumps TO, then re-run."
                )

            verdicts = classify(shape, entries, pin_path)
            findings = [(s, p, why, shape.territory(p)) for s, p, why in verdicts if why]
            # A REPAIR: outside kit territory, and written rather than removed. Both halves are
            # argued in the module docstring; neither is a heuristic over path names.
            repairs = [f for f in findings if f[3] == "receiver" and f[0] in ("A", "M")]
            blocking = [f for f in findings if f not in repairs]
            # The pin is counted as the pin and NOT as a materialized output. Folding it into the
            # modified count makes the summary say "9 materialized outputs" for a diff carrying 8,
            # and a summary a human is meant to check by hand must not be off by one.
            counts = {"A": 0, "M": 0, "D": 0}
            for status, path in entries:
                if path != pin_path:
                    counts[status] += 1
            if not findings:
                name, code = "mechanical", MECHANICAL
            elif not blocking:
                name, code = "mechanical+repair", MECHANICAL_PLUS_REPAIR
            else:
                name, code = "not-mechanical", NOT_MECHANICAL
            verdict = {
                "verdict": name,
                "target": shape.version,
                "pin": {"path": pin_path, "from": pin_info["from"], "to": pin_info["to"]},
                "roots": {
                    "live": shape.live,
                    "retired": shape.retired,
                    "view": shape.view,
                },
                "buildConfig": shape.build_config,
                "skills": sorted(shape.skills),
                "changed": {"added": counts["A"], "modified": counts["M"], "deleted": counts["D"]},
                "findings": [
                    {"status": s, "path": p, "why": why, "territory": t} for s, p, why, t in findings
                ],
                "repairs": [p for _, p, _, _ in repairs],
                "blocking": [p for _, p, _, _ in blocking],
            }
    except Refused as error:
        if args.json:
            print(json.dumps({"verdict": "refused", "reason": str(error)}, indent=2))
        else:
            print(f"::error::check-kit-bump-shape: REFUSED — {error}", file=sys.stderr)
        return REFUSED

    if args.json:
        print(json.dumps(verdict, indent=2))
        return code

    if code == NOT_A_KIT_BUMP:
        print(
            f"check-kit-bump-shape: ABSTAINS — {verdict['reason']}. This is NOT a pass: an automerge "
            "rule must treat an abstention as 'do not merge'."
        )
        return code
    if code == MECHANICAL:
        changed = verdict["changed"]
        print(
            f"ok: mechanical {PACKAGE} bump to {verdict['target']} — pin {verdict['pin']['path']} "
            f"{verdict['pin']['from']} -> {verdict['pin']['to']}, plus {changed['added']} added / "
            f"{changed['modified']} modified materialized output(s) and {changed['deleted']} "
            f"deletion(s) confined to swept skill directories under "
            f"{verdict['roots']['live'] + verdict['roots']['retired'] + verdict['roots']['view']}. "
            "Every path is derived from the target package's kit-manifest.tsv and the receiver's own "
            "evaluation of its root declarations."
        )
        return code

    if code == MECHANICAL_PLUS_REPAIR:
        changed = verdict["changed"]
        repaired = "\n".join(
            f"    {f['status']}  {f['path']}" for f in verdict["findings"] if f["territory"] == "receiver"
        )
        # stdout, and NOT `::error::`. This class is a REPORT, not a refusal: FS.GG.Rendering#1088 is
        # the measured instance, and merging it was right. Naming the class in words is #1726 AC 3 —
        # a reader must not have to deduce "a receiver-authored repair rode this bump" from a path list.
        print(
            f"report: MECHANICAL + RECEIVER-SIDE REPAIR — {PACKAGE} bump to {verdict['target']}, pin "
            f"{verdict['pin']['path']} {verdict['pin']['from']} -> {verdict['pin']['to']}. Kit "
            f"territory is exactly what the materializer produces ({changed['added']} added / "
            f"{changed['modified']} modified output(s), {changed['deleted']} swept deletion(s)), and "
            f"{len(verdict['repairs'])} receiver-authored file(s) ride along:\n{repaired}\n"
            "A repair that can only ride its bump is an expected class, not a contamination "
            "(.github#1726: FS.GG.Rendering#1088's `scripts/materialize-skill-roots.sh` could not land "
            "before the bump, because at the old pin its expectation was correct, nor after it, "
            "because the bump was red until it landed). THIS IS NOT A PASS — automerge fires on the "
            "`mechanical` verdict ALONE. What it says is that the reading this pull request needs is "
            "bounded, and these are its bounds."
        )
        return code

    lines = "\n".join(
        f"    {f['status']}  {f['path']}  [{f['territory']} territory]\n        {f['why']}"
        for f in verdict["findings"]
    )
    repaired = verdict["repairs"]
    aside = (
        f"\n{len(repaired)} receiver-authored change(s) ride along too ({', '.join(repaired)}); they "
        "are not what makes this verdict — the kit-territory finding(s) above are."
        if repaired
        else ""
    )
    print(
        f"::error::check-kit-bump-shape: this {PACKAGE} bump to {verdict['target']} is NOT MECHANICAL "
        f"— {len(verdict['blocking'])} change(s) the materializer would not have produced, so it must "
        f"not automerge (.github#1587 AC 2):\n{lines}{aside}\n"
        f"Admissible: the pin; the target manifest's client/config destinations; "
        f"<root>/<skill destination> for root in {verdict['roots']['live']}; and DELETIONS ONLY under "
        f"<root>/<skill>/ for root in {verdict['roots']['live'] + verdict['roots']['retired'] + verdict['roots']['view']}. "
        "A receiver-authored file ADDED or MODIFIED beside all of that is the milder `mechanical+repair` "
        "class (exit 4); a DELETION, or anything inside kit territory, is this one.",
        file=sys.stderr,
    )
    return code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
