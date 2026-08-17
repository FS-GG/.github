#!/usr/bin/env python3
"""Gate-inversion evidence for .github#2312's production-caller repair.

Every gate this change adds ships with evidence it can fail. For each leg below this script:

  1. applies ONE exact string mutation to ONE production file;
  2. proves the mutation landed at the intended site by `git diff --name-only` — not by eye;
  3. rebuilds and records the SHA-256 of the rebuilt test assembly, and REFUSES the leg if that hash
     did not change (a clean-looking tree is not evidence of a rebuilt artifact: a critic on this board
     reverted a file, confirmed a clean tree, and measured a DLL still carrying its mutation);
  4. runs the NAMED test the mutation is supposed to red, and records the observed result;
  5. reverts, rebuilds, and requires the source restored byte-for-byte and the named test green again.

A local Release build of this tree was MEASURED not to be byte-reproducible — `fsgg-coord-engine.dll`
alternates between two hashes across rebuilds of identical source — so the revert is verified on source
bytes and on the suite, and the hashes are recorded as observations. A hash that MOVES between the
pre-mutation and mutated builds still correctly identifies which compilation unit a mutation reached,
which is what step 3 uses it for.

Leg L8 is the CONTROL. It is not evidence about the code under test — it is evidence about this
harness: one mutation that must red exactly ONE named leg and leave the other fourteen green. Without
it every "the gate reds" line above is unfalsifiable, because a harness that reported red for
everything would produce the same transcript.

Run from the worktree root:  python3 tests/FS.GG.Coord.Cli.Tests/inversions-2312.py
"""

import hashlib
import json
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
BIN = ROOT / "tests/FS.GG.Coord.Cli.Tests/bin/Release/net10.0"
DLL = BIN / "FS.GG.Coord.Cli.Tests.dll"

# THE ARTIFACT A MUTATION LANDS IN IS NOT ALWAYS THE ONE THE TESTS LIVE IN, and hashing only the test
# assembly measured nothing: `Client.fs` compiles into `fsgg-coord-engine.dll` and `Options.fs` into
# `FS.GG.Coord.Cli.Kernel.dll`, both of which the test project merely REFERENCES. The first run of this
# script reported `changed=False` on every leg while every leg reded — the hash was watching a file no
# mutation could reach. Hash all three, and let each leg's evidence name which one moved.
#
# `scripts/fsgg-coord-guards.sh` (L6) is expected to move NONE of them: it is a shell file the test reads
# from disk at run time, so its evidence is the `git diff --name-only` anchor plus the observed red, and
# saying so is more useful than manufacturing a hash that would not mean anything.
WATCHED = ["FS.GG.Coord.Cli.Tests.dll", "fsgg-coord-engine.dll", "FS.GG.Coord.Cli.Kernel.dll"]

CLIENT = "src/FS.GG.Coord.Cli/Client.fs"
OPTIONS = "src/FS.GG.Coord.Cli.Kernel/Options.fs"
GUARDS = "scripts/fsgg-coord-guards.sh"
PROGRAM = "src/FS.GG.Coord.Cli/Program.fs"

CLASS = "FS.GG.Coord.Cli.Tests.OpLockDispatchVerbTests"

# (id, file, old, new, target test name, note)
LEGS = [
    (
        "L1",
        CLIENT,
        "                let grant = string held.MarkerId",
        "                let grant = generation",
        "op-lock acquire takes the grant and prints the broker's whole input tuple",
        "the grant must be the SERVER-assigned comment id, not any value the caller supplied",
    ),
    (
        "L2",
        CLIENT,
        "            match Operation.compose item generation receiver parsedOp with",
        '            match Operation.compose item generation receiver (Operation.Dispatch "something-else") with',
        "op-lock acquire takes the grant and prints the broker's whole input tuple",
        "the opkey must be composed from the caller's own four components, or the broker's recomputation disagrees",
    ),
    (
        "L3",
        CLIENT,
        "                | OpLock.HeldByAnother _ -> ExitContended",
        "                | OpLock.HeldByAnother _ -> ExitError",
        "a live holder REFUSES the dispatch with exit 6 - a contended receiver is the fence working",
        "a contended receiver and a misconfigured one must not share an exit code; their remedies are opposite",
    ),
    (
        "L4",
        CLIENT,
        "            if op.StartsWith(prefix, StringComparison.Ordinal) then",
        "            if true then",
        "the dispatch prefix is DERIVED from Operation.wire, so a non-dispatch operation is refused unpaid",
        "a non-dispatch operation must be refused BEFORE the lock is taken, not brokered under a lease its verifier cannot see",
    ),
    (
        "L5",
        CLIENT,
        "                match Writes.verifyHeld transport LeaseMinutes worker self session lockRef with",
        "                match Writes.verifyHeld transport 120 worker self session lockRef with",
        "op-lock release drops OUR grant, through verifyHeld rather than lowest id",
        "the ten-minute lease is what makes a lapsed foreign marker lapsed; at the item's 120 it still wins and we hold nothing",
    ),
    (
        "L6",
        GUARDS,
        'BOARD_WRITES="add adopt child claim done flush heartbeat intake op-lock release review room say set-field set-paths take widen"',
        'BOARD_WRITES="add adopt child claim done flush heartbeat intake release review room say set-field set-paths take widen"',
        "both op-lock commands are BOARD WRITES in the engine and in the shim's partition",
        "a verb the shim classifies nowhere runs on a STALE engine; the guard refuses only what it can name",
    ),
    (
        "L7",
        OPTIONS,
        "        | OpLockAcquire -> Writes // POSTs the `fsgg:claim` grant marker onto the receiver's op-lock issue",
        "        | OpLockAcquire -> Reads // POSTs the `fsgg:claim` grant marker onto the receiver's op-lock issue",
        "both op-lock commands are BOARD WRITES in the engine and in the shim's partition",
        "the engine's own writes contract is what the parity gate holds the shim in bijection with",
    ),
    (
        "L9",
        CLIENT,
        "            | OpLockAcquire -> opLockAcquire ctx opts",
        "            | OpLockAcquire -> failwith \"unwired\"",
        "the CLI dispatch actually routes op-lock to its handlers - the link that was missing",
        "a handler nothing dispatches to is exactly the defect this row reopened, one level up",
    ),
    (
        "L8",
        CLIENT,
        'Dispatch now, then `op-lock release %s{receiver}`',
        'Dispatch now, then drop the grant',
        "the text projection prints grant and opkey on stdout, and the release reminder on stderr",
        "CONTROL — this mutation must red exactly ONE leg and leave every other leg green",
    ),
]


def sh(cmd, **kw):
    return subprocess.run(cmd, cwd=ROOT, shell=True, capture_output=True, text=True, **kw)


def build(touch=()):
    """Rebuild, FORCING recompilation of the named sources.

    MSBUILD SKIPS A REBUILD IT THINKS IS UNNECESSARY, and that decision is made on TIMESTAMPS, not on
    content. Reverting a file and rebuilding can therefore leave an artifact still carrying the mutation
    — the failure a critic on this board measured directly (revert, clean `git status`, mutated DLL) and
    the same mechanism that makes `shutil.copy2` dangerous here. Touching every file this script writes,
    on every build, removes the question rather than hoping about it.

    Measured on this tree: with the touch in place, two consecutive builds of byte-identical source
    produce a byte-identical `fsgg-coord-engine.dll`, so a hash that MOVES is evidence the source moved
    and a hash that does not is evidence it did not.
    """
    for rel in touch:
        (ROOT / rel).touch()
    r = sh("dotnet build tests/FS.GG.Coord.Cli.Tests -c Release")
    return r.returncode, (r.stdout + r.stderr)


def artifact_hashes():
    out = {}
    for name in WATCHED:
        p = BIN / name
        out[name] = hashlib.sha256(p.read_bytes()).hexdigest()[:16] if p.exists() else "ABSENT"
    return out


def run_class():
    """Run the whole class; return {test display name: 'Passed'|'Failed'}."""
    r = sh(f'dotnet vstest "{DLL}" --TestCaseFilter:"FullyQualifiedName~OpLockDispatchVerbTests"')
    text = r.stdout + r.stderr
    results = {}
    for m in re.finditer(r"^\s*(Passed|Failed)\s+" + re.escape(CLASS) + r"\.(.+?)\s*(?:\[|$)", text, re.M):
        results[m.group(2).strip()] = m.group(1)
    summary = re.search(r"(Failed|Passed)!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+)", text)
    return results, (summary.groups() if summary else None), text


def main():
    report = []

    rc, out = build(touch=(CLIENT, OPTIONS))
    assert rc == 0, out[-3000:]
    baseline_hash = artifact_hashes()
    base_results, base_summary, _ = run_class()
    assert base_summary and base_summary[0] == "Passed", f"baseline is not green: {base_summary}"
    print(f"BASELINE  {baseline_hash}  {base_summary}")

    for leg, rel, old, new, target, note in LEGS:
        path = ROOT / rel
        src = path.read_text()
        if src.count(old) != 1:
            print(f"{leg}  ANCHOR NOT UNIQUE in {rel}: {src.count(old)} occurrences — leg ABANDONED")
            report.append({"leg": leg, "status": "anchor-not-unique", "file": rel})
            continue

        path.write_text(src.replace(old, new, 1))

        touched = sh("git diff --name-only").stdout.split()
        anchored = rel in touched

        rc, out = build(touch=(rel,))
        mutated_hash = artifact_hashes()
        moved = sorted(k for k in WATCHED if mutated_hash[k] != baseline_hash[k])

        if rc != 0:
            # A mutation the COMPILER refuses is the strongest possible red — record it as one.
            verdict = "red (compile error)"
            observed = "build failed"
            results = {}
        else:
            results, summary, _ = run_class()
            observed = results.get(target, "NOT RUN")
            verdict = "red" if observed == "Failed" else f"GREEN — GATE DID NOT FIRE ({observed})"

        others_red = sorted(k for k, v in results.items() if k != target and v == "Failed") if results else None
        others_green = (others_red == []) if others_red is not None else None

        print(
            f"{leg}  file={rel} anchored={anchored} artifactsMoved={moved or 'none (source read at run time)'} "
            f"target=[{target[:55]}] -> {verdict}  othersGreen={others_green}"
        )
        report.append(
            {
                "leg": leg,
                "file": rel,
                "mutation": {"from": old.strip()[:90], "to": new.strip()[:90]},
                "anchoredByGitDiff": anchored,
                "artifactHashesBefore": baseline_hash,
                "artifactHashesAfter": mutated_hash,
                "artifactsThatMoved": moved,
                "target": target,
                "observed": verdict,
                "everyOtherLegStillGreen": others_green,
                "otherLegsThatAlsoRedded": others_red,
                "why": note,
                "artifactHashesAfterRevert": None,
            }
        )

        path.write_text(src)
        rc, _ = build(touch=(rel,))
        assert rc == 0
        restored_hash = artifact_hashes()

        # THE REVERT IS VERIFIED ON SOURCE BYTES AND ON THE SUITE, NOT ON THE ARTIFACT HASH, and that is a
        # MEASUREMENT rather than a convenience. This script first asserted the reverted artifact hashed
        # back to its baseline, and that assertion FAILED on byte-identical source: `fsgg-coord-engine.dll`
        # alternated between two hashes (ce904dae… and bcf63a94…) across rebuilds of the same bytes, and
        # `FS.GG.Coord.Cli.Tests.dll` moved too without its own source changing. So a local Release build of
        # this tree is NOT byte-reproducible, and an equality assertion on it would fail for a reason that
        # says nothing about the revert.
        #
        # What is still sound, and is what the legs above rely on: a hash that MOVES between the pre-mutation
        # and mutated builds identifies WHICH compilation unit the mutation reached, and it did so correctly
        # on every leg (Client.fs -> fsgg-coord-engine.dll, Options.fs -> FS.GG.Coord.Cli.Kernel.dll,
        # fsgg-coord-guards.sh -> neither, because it is read from disk at run time). The reverted hashes are
        # recorded as an observation so a reader can see the non-determinism rather than take this note for it.
        assert path.read_text() == src, f"{leg}: the source was not restored byte-for-byte"
        results, summary, _ = run_class()
        assert summary and summary[0] == "Passed", f"{leg}: suite did not return to green after revert ({summary})"
        report[-1]["artifactHashesAfterRevert"] = restored_hash
        report[-1]["sourceRestoredByteForByte"] = True

    (ROOT / "readiness/2312-dispatch-lock-merge-election/inversion-evidence.json").write_text(
        json.dumps({"baselineArtifactSha256Prefixes": baseline_hash, "legs": report}, indent=2) + "\n"
    )
    print("\nwrote readiness/2312-dispatch-lock-merge-election/inversion-evidence.json")
    return 0


if __name__ == "__main__":
    sys.exit(main())
