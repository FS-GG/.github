#!/usr/bin/env python3
"""Mutation coverage for the fail-closed v1 writer census."""

from __future__ import annotations

import importlib.util
import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
CENSUS = ROOT / "docs/coordination/v1-writer-census.json"
FIXTURES = Path(__file__).resolve().parent / "fixtures"
passed = 0
failed = 0


def ok(label: str) -> None:
    global passed
    passed += 1
    print(f"PASS  {label}")


def bad(label: str, detail: str = "") -> None:
    global failed
    failed += 1
    print(f"FAIL  {label}{': ' + detail if detail else ''}", file=sys.stderr)


def run(*args: str, root: Path = ROOT) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [sys.executable, str(root / "scripts/check-v1-writer-census.py"), "--root", str(root), *args],
        text=True,
        capture_output=True,
        check=False,
    )


def expect_red(result: subprocess.CompletedProcess[str], needle: str, label: str) -> None:
    if result.returncode != 0 and needle in result.stderr:
        ok(label)
    else:
        bad(label, f"rc={result.returncode}; stderr={result.stderr.strip()!r}")


with tempfile.TemporaryDirectory(prefix="v1-writer-census-") as temporary:
    work = Path(temporary)
    structural = run("--structural")
    if structural.returncode == 0 and "54 command roots: 20 always, 6 conditional, 28 never" in structural.stdout:
        ok("checked-in census covers the tracked executable population")
    else:
        bad("checked-in census covers the tracked executable population", structural.stderr.strip())

    supplied_candidate = os.environ.get("V1_WRITER_CENSUS_CANDIDATE")
    if supplied_candidate:
        engine = ROOT / supplied_candidate
        build_error = "workflow-supplied candidate does not exist"
    else:
        build = subprocess.run(
            [str(ROOT / "scripts/build-gate-engine")],
            text=True,
            capture_output=True,
            check=False,
        )
        engine = Path(build.stdout.strip())
        build_error = build.stderr[-1000:]
    if not engine.is_file():
        bad("candidate build supplies one typed command contract", build_error)
        contract = {"schema": "invalid", "commands": []}
    else:
        emitted = subprocess.run([str(engine), "command-contract"], text=True, capture_output=True, check=False)
        try:
            contract = json.loads(emitted.stdout)
        except json.JSONDecodeError:
            contract = {"schema": "invalid", "commands": []}
        if emitted.returncode == 0 and contract.get("schema") == "fsgg.coord.commands/1":
            ok("candidate build supplies one typed command contract")
        else:
            bad("candidate build supplies one typed command contract", emitted.stderr.strip())

    contract_path = work / "contract.json"
    contract_path.write_text(json.dumps(contract), encoding="utf-8")
    typed = run("--contract", str(contract_path))
    if typed.returncode == 0 and "candidate-built" in typed.stdout:
        ok("candidate-built metadata agrees with the census")
    else:
        bad("candidate-built metadata agrees with the census", typed.stderr.strip())

    base_census = json.loads(CENSUS.read_text(encoding="utf-8"))

    retired_writer = json.loads(json.dumps(base_census))
    next(
        row for row in retired_writer["sources"]
        if row["path"] == ".github/workflows/dispatch-sender.yml"
    )["disposition"] = "conditional-remote-writer"
    retired_writer_path = work / "retired-writer.json"
    retired_writer_path.write_text(json.dumps(retired_writer), encoding="utf-8")
    expect_red(
        run("--census", str(retired_writer_path), "--structural"),
        "must remain classified read-only",
        "retired dispatch sender cannot be relabeled as writable",
    )

    omitted = json.loads(json.dumps(base_census))
    omitted["commandRoots"] = omitted["commandRoots"][1:]
    omitted_path = work / "omitted.json"
    omitted_path.write_text(json.dumps(omitted), encoding="utf-8")
    expect_red(run("--census", str(omitted_path), "--contract", str(contract_path)), "omitted/new", "omitted command root reds")

    extra_contract = json.loads(json.dumps(contract))
    extra_contract["commands"].append(json.loads((FIXTURES / "unknown-command.json").read_text(encoding="utf-8")))
    extra_path = work / "extra-contract.json"
    extra_path.write_text(json.dumps(extra_contract), encoding="utf-8")
    expect_red(run("--contract", str(extra_path)), "omitted/new", "new command root reds")

    wrong = json.loads(json.dumps(base_census))
    for row in wrong["commandRoots"]:
        if row["name"] == "add":
            row["writes"] = "never"
    wrong_path = work / "wrong.json"
    wrong_path.write_text(json.dumps(wrong), encoding="utf-8")
    expect_red(run("--census", str(wrong_path), "--contract", str(contract_path)), "misclassified", "misclassified command root reds")

    missing_source = json.loads(json.dumps(base_census))
    missing_source["sources"] = [row for row in missing_source["sources"] if row["path"] != "tools/routine-delivery.py"]
    missing_source_path = work / "missing-source.json"
    missing_source_path.write_text(json.dumps(missing_source), encoding="utf-8")
    expect_red(run("--census", str(missing_source_path), "--structural"), "unknown or unresolved writer source", "omitted direct REST writer reds")

    wrong_source = json.loads(json.dumps(base_census))
    for row in wrong_source["sources"]:
        if row["path"] == "tools/routine-delivery.py":
            row["disposition"] = "remote-writer"
            row.pop("nonWriterJustification", None)
    wrong_source_path = work / "wrong-source.json"
    wrong_source_path.write_text(json.dumps(wrong_source), encoding="utf-8")
    expect_red(run("--census", str(wrong_source_path), "--structural"), "sealed merge boundary", "sealed routine delivery cannot be relabeled as writable")

    drifted = json.loads(json.dumps(base_census))
    next(row for row in drifted["sources"] if row["path"] == "global.json")["sha256"] = "0" * 64
    drifted_path = work / "drifted.json"
    drifted_path.write_text(json.dumps(drifted), encoding="utf-8")
    expect_red(run("--census", str(drifted_path), "--structural"), "census source identity changed for global.json", "drifted source identity reds")

    stale = json.loads(json.dumps(base_census))
    stale["sources"].append({"path": "scripts/vanished-writer.py", "disposition": "read-only", "sinkKinds": [], "sha256": "0" * 64})
    stale_path = work / "stale.json"
    stale_path.write_text(json.dumps(stale), encoding="utf-8")
    expect_red(run("--census", str(stale_path), "--structural"), "outside the current executable population", "census row for a source that no longer carries a sink reds")

    relabeled = json.loads(json.dumps(base_census))
    next(row for row in relabeled["sources"] if row["path"] == "tools/routine-delivery.py")["disposition"] = "read-only"
    relabeled_path = work / "routine-delivery-read-only.json"
    relabeled_path.write_text(json.dumps(relabeled), encoding="utf-8")
    expect_red(
        run("--census", str(relabeled_path), "--structural"),
        "must remain classified v2-delivery-boundary",
        "sealed routine delivery cannot be relabeled back to read-only",
    )

    for omitted_arm in ('"board-v2" :: _', "TelemetryApplication.tryRun"):
        unlisted_census = json.loads(json.dumps(base_census))
        unlisted_census["outOfCatalogueRoots"] = [row for row in unlisted_census["outOfCatalogueRoots"] if row["arm"] != omitted_arm]
        unlisted_path = work / "unlisted-arm.json"
        unlisted_path.write_text(json.dumps(unlisted_census), encoding="utf-8")
        expect_red(run("--census", str(unlisted_path), "--structural"), omitted_arm, f"pre-handler arm {omitted_arm} left out of the census reds")

    stale_arm = json.loads(json.dumps(base_census))
    stale_arm["outOfCatalogueRoots"].append({"arm": '"retired" :: _', "name": "retired", "writes": "never", "justification": "An arm that main no longer dispatches must not linger in the census."})
    stale_arm_path = work / "stale-arm.json"
    stale_arm_path.write_text(json.dumps(stale_arm), encoding="utf-8")
    expect_red(run("--census", str(stale_arm_path), "--structural"), "no longer dispatches", "census arm that main no longer dispatches reds")

    unjustified = json.loads(json.dumps(base_census))
    unjustified["outOfCatalogueRoots"][0]["justification"] = "ok"
    unjustified_path = work / "unjustified-arm.json"
    unjustified_path.write_text(json.dumps(unjustified), encoding="utf-8")
    expect_red(run("--census", str(unjustified_path), "--structural"), "lacks a specific justification", "pre-handler arm without a justification reds")

    undeclared_kind = json.loads(json.dumps(base_census))
    next(row for row in undeclared_kind["sources"] if row["path"] == "tests/recipe-landable/run.sh")["sinkKinds"].remove("gh-api-write")
    undeclared_kind_path = work / "undeclared-kind.json"
    undeclared_kind_path.write_text(json.dumps(undeclared_kind), encoding="utf-8")
    expect_red(run("--census", str(undeclared_kind_path), "--structural"), "sink discovery changed", "census row that omits the gh-api-write sink reds")

    # The arm guard reads F# text, so every dispatch spelling a contributor might use must either be listed or
    # refuse. Each case edits the real Program.fs in a throwaway root and runs the guard on it.
    checker_spec = importlib.util.spec_from_file_location("v1_writer_census_checker", ROOT / "scripts/check-v1-writer-census.py")
    checker = importlib.util.module_from_spec(checker_spec)
    checker_spec.loader.exec_module(checker)
    real_program = (ROOT / "src/FS.GG.Coord.Cli/Program.fs").read_text(encoding="utf-8")
    board_arm = '            | "board-v2" :: _ ->\n'
    outer_match = "            match arguments with\n"
    skill_dispatch = "                match SkillCommandApplication.tryRun arguments with\n"
    shape_ok = all(real_program.count(anchor_text) == 1 for anchor_text in (board_arm, outer_match, skill_dispatch))
    if not shape_ok:
        bad("arm guard fixtures", "Program.fs dispatch shape moved; update the fixtures")

    def guard(program_text: str) -> str | None:
        root = work / "guard-root"
        (root / "src/FS.GG.Coord.Cli").mkdir(parents=True, exist_ok=True)
        (root / "src/FS.GG.Coord.Cli/Program.fs").write_text(program_text, encoding="utf-8")
        try:
            checker.validate_out_of_catalogue(root, base_census)
        except checker.CensusError as error:
            return str(error)
        return None

    def reindent(program_text: str, extra: int) -> str:
        start = program_text.index("let main argv")
        end = program_text.index("commandHandlers[opts.Command]", start)
        body = "\n".join((" " * extra + line) if line.strip() else line for line in program_text[start:end].split("\n"))
        return program_text[:start] + body + program_text[end:]

    if shape_ok:
        refused = {
            "same-line arm body": (board_arm, '            | "same-line" :: _ -> NewApplication.tryRun arguments |> Option.defaultValue ExitError\n' + board_arm, '"same-line" :: _'),
            "same-line arm body with a guard": (board_arm, '            | "guarded" :: rest when rest.Length > 1 -> ExitError\n' + board_arm, '"guarded" :: rest when rest.Length > 1'),
            "arm with a trailing comment after the arrow": (board_arm, '            | "commented" :: _ -> // looks harmless\n                ExitError\n' + board_arm, '"commented" :: _'),
            "arm at an unexpected indent": (board_arm, '        | "shallow" :: _ ->\n            ExitError\n' + board_arm, "unexpected indent"),
            "stacked or-pattern arm": (board_arm, '            | "stacked-a" :: _\n            | "stacked-b" :: _ ->\n                ExitError\n' + board_arm, '"stacked-a" :: _ | "stacked-b" :: _'),
            "different dispatcher method name": (skill_dispatch, "                match QuietApplication.tryExecute arguments with\n                | Some exitCode -> exitCode\n                | None ->\n" + skill_dispatch, "QuietApplication.tryExecute"),
            "dispatcher named run": (skill_dispatch, "                match QuietApplication.run arguments with\n                | Some exitCode -> exitCode\n                | None ->\n" + skill_dispatch, "QuietApplication.run"),
            "parenthesised dispatcher": (skill_dispatch, "                match (QuietApplication.tryRun) arguments with\n                | Some exitCode -> exitCode\n                | None ->\n" + skill_dispatch, "QuietApplication.tryRun"),
            "piped dispatcher": (skill_dispatch, "                match arguments |> QuietApplication.tryRun with\n                | Some exitCode -> exitCode\n                | None ->\n" + skill_dispatch, "QuietApplication.tryRun"),
            "dispatcher on a different argument name": (skill_dispatch, "                match QuietApplication.tryRun argv with\n                | Some exitCode -> exitCode\n                | None ->\n" + skill_dispatch, "QuietApplication.tryRun"),
            "if-then dispatch before the match": (outer_match, "            if List.contains \"--quiet\" arguments then QuietApplication.run arguments else\n" + outer_match, "QuietApplication.run"),
            "function-keyword dispatch": (outer_match, "            let route = function | [ \"x\" ] -> 1 | _ -> 0\n" + outer_match, "function"),
        }
        wrap = "                | Some exitCode -> exitCode\n                | None ->\n"
        for method in ("Process", "Go", "Start", "Send", "Post", "Apply", "Main", "Operate", "Call", "Perform"):
            refused[f"unlisted Evil.{method} dispatcher"] = (
                skill_dispatch, f"                match Evil.{method} arguments with\n" + wrap + skill_dispatch, f"Evil.{method}")
        refused["renamed match argument (refused until the allow-list is updated)"] = (outer_match, "            match args with\n", "`args`")
        marker = "commandHandlers[opts.Command] opts"
        refused["end marker spelled in a comment before an evil call"] = (
            skill_dispatch, f"                // {marker}\n                match Evil.Go arguments with\n" + wrap + skill_dispatch, "Evil.Go")
        refused["end marker spelled in a block comment before an evil call"] = (
            skill_dispatch, f"                (* {marker} *)\n                match Evil.Go arguments with\n" + wrap + skill_dispatch, "Evil.Go")
        refused["end marker spelled in a string before an evil call"] = (
            skill_dispatch, f'                eprint "{marker}"\n                match Evil.Go arguments with\n' + wrap + skill_dispatch, "Evil.Go")
        refused["commandHandlers is not a safe token"] = (
            skill_dispatch, "                match commandHandlers with\n" + wrap + skill_dispatch, "`commandHandlers`")
        refused["non-ASCII module and method (Δ.Σ)"] = (
            skill_dispatch, "                match Δ.Σ arguments with\n" + wrap + skill_dispatch, "`Δ.Σ`")
        refused["non-ASCII bare call"] = (
            skill_dispatch, "                match Σ arguments with\n" + wrap + skill_dispatch, "`Σ`")
        refused["custom operator |>!"] = (
            skill_dispatch, "                match arguments |>! Some with\n" + wrap + skill_dispatch, "`|>!`")
        refused["custom operator <|>"] = (
            skill_dispatch, "                match arguments <|> Some with\n" + wrap + skill_dispatch, "`<|>`")
        refused["open-module bare call"] = (skill_dispatch, "                match go arguments with\n" + wrap + skill_dispatch, "`go`")
        refused["let-bound call"] = (
            skill_dispatch, "                let routed = arguments\n                match routed arguments with\n" + wrap + skill_dispatch, "`routed`")
        refused["lambda"] = (skill_dispatch, "                match (fun a -> Some 1) arguments with\n" + wrap + skill_dispatch, "`fun`")
        refused["GoAsync(...).Result"] = (
            skill_dispatch, "                match Some(GoAsync(arguments).Result) with\n" + wrap + skill_dispatch, "`GoAsync`")
        refused["nested arm under the wildcard"] = (
            skill_dispatch, skill_dispatch + '                | "evil" :: _ -> ExitError\n', "nested arm under the wildcard")
        refused["property access on a call result"] = (
            skill_dispatch, "                match arguments.Head.Length with\n" + wrap + skill_dispatch, "`arguments.Head.Length`")
        refused["interpolated-string hole"] = (
            outer_match, '            eprint $"{Evil.Go arguments}"\n' + outer_match, "Evil.Go")
        for label, (old, replacement, needle) in refused.items():
            message = guard(real_program.replace(old, replacement, 1))
            if message is not None and needle in message:
                ok(f"arm guard refuses: {label}")
            else:
                bad(f"arm guard refuses: {label}", repr(message))

        accepted = {
            "unchanged Program.fs": real_program,
            "comment-only edits are not dispatch": real_program.replace(board_arm, '            // | "ghost" :: _ -> GhostApplication.tryRun arguments\n            (* QuietApplication.run arguments *)\n' + board_arm, 1),
            "the end marker spelled in a comment inside a named arm": real_program.replace(board_arm, board_arm + "                // commandHandlers[opts.Command] opts\n", 1),
            "the whole dispatch reindented": reindent(real_program, 4),
        }
        for label, program_text in accepted.items():
            message = guard(program_text)
            if message is None:
                ok(f"arm guard accepts: {label}")
            else:
                bad(f"arm guard accepts: {label}", message)
        message = guard(reindent(real_program.replace(board_arm, '            | "late" :: _ -> NewApplication.run arguments\n' + board_arm, 1), 4))
        if message is not None and '"late" :: _' in message:
            ok("arm guard refuses a new arm in a reindented dispatch")
        else:
            bad("arm guard refuses a new arm in a reindented dispatch", repr(message))

    scanner_pin = json.loads(json.dumps(base_census))
    scanner_pin["scannerIdentities"][0]["sha256"] = "0" * 64
    scanner_pin_path = work / "scanner-pin.json"
    scanner_pin_path.write_text(json.dumps(scanner_pin), encoding="utf-8")
    expect_red(run("--census", str(scanner_pin_path), "--structural"), "scanner identity changed", "mispinned gh api scanner reds")

    clone = work / "repo"
    cloned = subprocess.run(["git", "clone", "--quiet", "--shared", str(ROOT), str(clone)], text=True, capture_output=True, check=False)
    if cloned.returncode != 0:
        bad("new dynamic writer reds", cloned.stderr.strip())
    else:
        shutil.copy2(FIXTURES / "new-dynamic-writer.py", clone / "scripts/dynamic-writer.py")
        subprocess.run(["git", "-C", str(clone), "add", "scripts/dynamic-writer.py"], check=True)
        dynamic = run("--structural", root=clone)
        expect_red(dynamic, "scripts/dynamic-writer.py", "new dynamic writer reds")
        (clone / "scripts/dynamic-writer.py").unlink()
        subprocess.run(["git", "-C", str(clone), "rm", "-q", "--cached", "scripts/dynamic-writer.py"], check=True)

        for fixture, label in (
            ("new-gh-api-field-writer.sh", "implicit POST by field flag (no method flag) reds"),
            ("new-gh-api-input-writer.sh", "request body by input flag reds"),
            ("new-gh-api-folded-writer.yml", "field flag on the next line of a folded scalar reds"),
            ("new-gh-api-graphql-query.sh", "graphql query field needs an explicit row and reds"),
        ):
            target = clone / "scripts" / fixture
            shutil.copy2(FIXTURES / fixture, target)
            subprocess.run(["git", "-C", str(clone), "add", f"scripts/{fixture}"], check=True)
            expect_red(run("--structural", root=clone), f"scripts/{fixture}", label)
            subprocess.run(["git", "-C", str(clone), "rm", "-q", "-f", f"scripts/{fixture}"], check=True)

        shutil.copy2(FIXTURES / "read-only-gh-api.sh", clone / "scripts/read-only-gh-api.sh")
        subprocess.run(["git", "-C", str(clone), "add", "scripts/read-only-gh-api.sh"], check=True)
        green = run("--structural", root=clone)
        if green.returncode == 0:
            ok("read-only gh api GET, command substitution and pipe stay green")
        else:
            bad("read-only gh api GET, command substitution and pipe stay green", green.stderr.strip())
        subprocess.run(["git", "-C", str(clone), "rm", "-q", "-f", "scripts/read-only-gh-api.sh"], check=True)

        scanner = clone / "scripts/lib/gh_api_scan.py"
        original_scanner = scanner.read_text(encoding="utf-8")
        scanner.write_text(original_scanner.replace('READ_ONLY_METHODS = {"GET", "HEAD"}', 'READ_ONLY_METHODS = {"GET", "HEAD", "POST"}'), encoding="utf-8")
        expect_red(run("--structural", root=clone), "scanner identity changed", "weakened gh api tokeniser reds")
        scanner.write_text(original_scanner, encoding="utf-8")

        dispatch_scanner = clone / "scripts/lib/fsharp_dispatch_scan.py"
        original_dispatch_scanner = dispatch_scanner.read_text(encoding="utf-8")
        dispatch_scanner.write_text(original_dispatch_scanner.replace("(?:try)?(?:run|execute", "(?:try)?(?:runx|execute"), encoding="utf-8")
        expect_red(run("--structural", root=clone), "scanner identity changed", "weakened arm-dispatch scanner reds")
        dispatch_scanner.write_text(original_dispatch_scanner, encoding="utf-8")

        program = clone / "src/FS.GG.Coord.Cli/Program.fs"
        original_program = program.read_text(encoding="utf-8")
        arm = '            | "board-v2" :: _ ->\n'
        skill = "                match SkillCommandApplication.tryRun arguments with\n"
        if original_program.count(arm) != 1 or original_program.count(skill) != 1:
            bad("new pre-handler arm reds", "Program.fs dispatch shape moved")
        else:
            program.write_text(
                original_program.replace(arm, '            | "new-writer" :: _ ->\n                invoked <- "new-writer"\n                ExitError\n' + arm),
                encoding="utf-8")
            expect_red(run("--structural", root=clone), '"new-writer" :: _', "new pre-handler dispatch arm added unlisted reds")
            program.write_text(
                original_program.replace(skill, "                match QuietApplication.tryRun arguments with\n                | Some exitCode -> exitCode\n                | None ->\n" + skill),
                encoding="utf-8")
            expect_red(run("--structural", root=clone), "QuietApplication.tryRun", "new pre-handler tryRun dispatcher added unlisted reds")
            program.write_text(original_program, encoding="utf-8")
            program.write_text(original_program, encoding="utf-8")

    unreadable = work / "unreadable.json"
    unreadable.write_text("{", encoding="utf-8")
    expect_red(run("--census", str(unreadable), "--structural"), "unreadable or malformed", "malformed census reds")

    release = (ROOT / ".github/workflows/release-coord-engine.yml").read_text(encoding="utf-8")
    wiring = (
        "check-v1-writer-census.py --candidate" in release
        and release.index("check-v1-writer-census.py --candidate") < release.index("dotnet nuget push")
    )
    ok("candidate contract and pre-publish wiring are present") if wiring else bad("candidate contract and pre-publish wiring are present")

print(f"\nv1 writer census fixture: {passed} passed, {failed} failed")
raise SystemExit(0 if failed == 0 else 1)
