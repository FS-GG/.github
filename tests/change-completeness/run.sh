#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
pass=0
fail=0
ok() { pass=$((pass + 1)); printf 'PASS  %s\n' "$1"; }
bad() { fail=$((fail + 1)); printf 'FAIL  %s\n' "$1" >&2; }

static_only=false
case "${1:-}" in
  "") ;;
  --static-only) static_only=true ;;
  *) echo "usage: $0 [--static-only]" >&2; exit 2 ;;
esac
[ "$#" -le 1 ] || { echo "unexpected arguments" >&2; exit 2; }

# Exercise the actual gate in a metadata-only checkout with a closed fake dotnet.
# These controls prove ordering/refusal/impact; they never claim compiler qualification.
if python3 - "$ROOT" "$WORK" <<'PY_CALLER'
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

root, work = map(Path, sys.argv[1:])
fixture = work / "caller & source"
for name in ("scripts/change-completeness", "scripts/read-trx-count.py",
             "scripts/check-ship-verdict-provenance.py", "scripts/lib/gate.py"):
    target = fixture / name
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(root / name, target)
for folder in ("src", "tests"):
    for project in (root / folder).rglob("*.fsproj"):
        target = fixture / project.relative_to(root)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(project, target)
verdict = fixture / "readiness/fixture/ship-verdict.json"
verdict.parent.mkdir(parents=True)
verdict.write_text("{}")
bin_dir = work / "fake-bin"
bin_dir.mkdir()
fake = bin_dir / "dotnet"
fake.write_text(r'''#!/usr/bin/env python3
import json
import os
from pathlib import Path
import sys
import xml.etree.ElementTree as ET
args = sys.argv[1:]
with open(os.environ["MOCK_LOG"], "a") as log:
    log.write(json.dumps(args) + "\n")
mode = os.environ.get("MOCK_MODE", "good")
if args == ["--version"]:
    if mode == "missing-tool": sys.exit(127)
    if mode == "malformed-sdk": print("not-an-sdk")
    elif mode != "empty-sdk": print("10.0.403")
    sys.exit(0)
verb, project = args[:2]
if verb == "msbuild":
    assert len(args) == 4 and args[2] in ("-target:RestoreRoots", "-target:BuildRoots") and args[3] == "-maxCpuCount:1", args
    plan = ET.parse(project).getroot()
    assert plan.tag == "Project" and not plan.attrib
    assert [item.tag for item in plan] == ["ItemGroup", "Target", "Target"]
    items, restore, build = list(plan)
    assert not items.attrib
    roots = [item.attrib["Include"] for item in items]
    assert roots == json.loads(os.environ["MOCK_ROOTS"])
    assert all(item.tag == "CompilerRoot" and set(item.attrib) == {"Include"} and len(item) == 0 and Path(item.attrib["Include"]).is_absolute() for item in items)
    for phase, name, target, properties in ((restore, "RestoreRoots", "Restore", "RestoreLockedMode=true"),
                                            (build, "BuildRoots", "Build", "Configuration=Release")):
        assert phase.attrib == {"Name": name} and len(phase) == 1
        task = phase[0]
        assert task.tag == "MSBuild" and len(task) == 0 and task.attrib == {
            "Projects": "@(CompilerRoot)", "Targets": target, "Properties": properties,
            "BuildInParallel": "false", "StopOnFirstFailure": "false",
            "SkipNonexistentProjects": "false", "SkipNonexistentTargets": "false"}, task.attrib
    assert "&amp;" in Path(project).read_text(), "fixture must exercise XML path escaping"
    phase = "restore-root" if args[2] == "-target:RestoreRoots" else "build-root"
    failures = 0
    for item in roots:
        with open(os.environ["MOCK_LOG"], "a") as log:
            log.write(json.dumps([phase, item]) + "\n")
        if phase == "build-root" and mode == "timeout": sys.exit(124)
        if phase == "build-root" and mode == "signal": sys.exit(130)
        fails = ((mode == "restore-error" and phase == "restore-root")
                 or (mode == "build-error" and phase == "build-root")
                 or (mode == "missing-target" and phase == "build-root")) and ("Cli.Tests/" in item or "cli-join/" in item)
        if fails:
            failures += 1
            print("fixture MSBuild root failed: " + item, file=sys.stderr)
    sys.exit(1 if failures else 0)
is_v2 = "/tests/coord-board-v2-" in project
if verb == "test":
    if "--results-directory" in args:
        output = Path(args[args.index("--results-directory") + 1])
        output.mkdir(parents=True, exist_ok=True)
        logger = args[args.index("--logger") + 1]
        filename = logger.split("LogFileName=", 1)[1]
        if not (is_v2 and mode == "missing-trx"):
            if is_v2 and mode == "malformed-trx": text = "<broken"
            else:
                passed = 0 if (is_v2 and mode in ("zero-trx", "skipped-trx")) or mode == "native-zero-control" else 1
                failed = 1 if is_v2 and mode == "failed-trx" else 0
                text = f'<TestRun><ResultSummary><Counters total="1" executed="{passed + failed}" passed="{passed}" failed="{failed}" error="0"/></ResultSummary></TestRun>'
            (output / filename).write_text(text)
    if is_v2 and mode == "test-error": sys.exit(1)
sys.exit(0)
''')
fake.chmod(0o755)
source = (root / "scripts/change-completeness").read_text()
match = re.search(r"^COMPILE_PROJECTS=\(\n(.*?)^\)", source, re.M | re.S)
assert match, "missing maintained compiler root inventory"
projects = match.group(1).split()
assert len(projects) == 12 and len(set(projects)) == 12

# Compare maintained roots with the actual literal native workflow commands. Unknown
# command operands are drift, not an empty inventory or a successful restore-only gate.
def inventory(script, workflows):
    match = re.search(r"^COMPILE_PROJECTS=\(\n(.*?)^\)", script, re.M | re.S)
    assert match, "missing inventory"
    declared = match.group(1).split()
    assert declared and len(set(declared)) == len(declared)
    assert all(re.fullmatch(r"(?:src|tests)/[A-Za-z0-9._/-]+\.fsproj", item) for item in declared)
    observed = set()
    for workflow in workflows:
        for line in workflow.splitlines():
            if line.lstrip().startswith("#"): continue
            # Scan every command occurrence, including a second inline command. Command
            # heads must be literal on this line; existing continued flags remain valid.
            # Exclude path/setup-dotnet tokens rather than treating them as shell commands.
            assert not re.search(r"""(?<![\w./-])(["'])dotnet\1(?=\s|$)""", line), "unsupported quoted bare dotnet command head"
            for command in re.finditer(r"(?<![\w./-])dotnet(?=\s|$)", line):
                head = re.match(r"\s+([A-Za-z-]+)\s+([^\s]+)", line[command.end():])
                assert head, "unsupported/dynamic/continued dotnet command head"
                verb, operand = head.groups()
                if verb == "tool":
                    assert operand == "install", "unsupported dotnet tool verb"
                    continue
                assert verb in ("restore", "build", "test", "pack"), "unsupported dotnet verb"
                assert re.fullmatch(r"(?:src|tests)/[A-Za-z0-9._/-]+\.fsproj", operand), "nonliteral workflow root"
                observed.add(operand)
    assert observed and set(declared) == observed, "workflow/compiler inventory drift"
    assert all((root / item).is_file() for item in declared), "missing compiler root"
    engine = workflows[0].split("  engine:\n", 1)[1]
    job_header = engine.split("    steps:", 1)[0]
    assert "needs: change-completeness" in job_header
    assert "if: github.event_name == 'workflow_dispatch' || needs.change-completeness.outputs.engine_changed == 'true'" in job_header
    assert "always()" not in job_header
    assert "continue-on-error:" not in workflows[0], "predecessor/engine must fail closed"
    assert "timeout-minutes: 10" in workflows[0].split("  engine:\n", 1)[0]

workflows = [(root / f".github/workflows/coord-{name}.yml").read_text() for name in ("engine", "github")]
inventory(source, workflows)
# Existing literal roots may share a physical line; current multiline flag continuations
# in the native workflows remain covered by the same inventory comparison.
literal_command = "dotnet build " + projects[0] + " --no-restore -c Release"
for separator in ("; ", " && "):
    inventory(source, [workflows[0] + "\n          " + literal_command + separator + "dotnet test " + projects[1] + " --no-build\n", workflows[1]])
for removed in projects:
    try: inventory(source.replace("  " + removed + "\n", ""), workflows)
    except AssertionError: pass
    else: raise AssertionError(f"missing-root mutation survived: {removed}")
for mutation in (
    workflows[0] + "\n          dotnet build tests/new/new.fsproj\n",
    workflows[0] + "\n          dotnet build $PROJECT\n",
    workflows[0] + "\n          " + literal_command + "; dotnet build tests/new/new.fsproj\n",
    workflows[0] + "\n          " + literal_command + "; dotnet build $PROJECT\n",
    workflows[0] + "\n          " + literal_command + '; "dotnet" build tests/new/new.fsproj\n',
    workflows[0] + "\n          " + literal_command + "; 'dotnet' build tests/new/new.fsproj\n",
    workflows[0] + "\n          " + literal_command + "; dotnet $VERB " + projects[0] + "\n",
    workflows[0] + "\n          dotnet \\\n            build " + projects[0] + "\n",
    workflows[0] + "\n          dotnet build \\\n            " + projects[0] + "\n",
    workflows[0] + "\n          dotnet publish " + projects[0] + "\n",
    workflows[0].replace("needs: change-completeness", "needs: unrelated"),
    workflows[0].replace("if: github.event_name == 'workflow_dispatch' || needs.change-completeness.outputs.engine_changed == 'true'", "if: always()"),
    workflows[0].replace("  engine:\n", "  engine:\n    continue-on-error: true\n"),
    workflows[0].replace("timeout-minutes: 10", "timeout-minutes: 25"),
):
    try: inventory(source, [mutation, workflows[1]])
    except AssertionError: pass
    else: raise AssertionError("workflow drift/bypass mutation survived")
print("PASS  static compiler inventory and workflow drift/bypass mutations")

# The attribution file is an operand of UTEL-CI-01 (FS.GG.Coord.Cli.Tests): every workflow it names must start
# the engine gate (push paths) and count as an engine change (classifier). Derived from the file, never restated,
# so a newly attributed workflow cannot be added without wiring it (#4994 renamed a step and ran no engine job).
attribution = json.loads((root / ".fsgg/telemetry-ci-attribution.json").read_text())
named = sorted({rule["workflow"] for rule in attribution["rules"]} | {".fsgg/telemetry-ci-attribution.json"})
classifier = (root / "scripts/change-completeness").read_text()
pattern = re.search(r"grep -Eq '(\^\(src/FS.*?)' \"\$PATHS\"", classifier).group(1)
def unwired(classifier_pattern, workflow_text):
    push = workflow_text.split("  push:\n", 1)[1].split("  workflow_dispatch:", 1)[0]
    return [path for path in named if not re.search(classifier_pattern, path) or f'- "{path}"' not in push]
assert not unwired(pattern, workflows[0]), unwired(pattern, workflows[0])
for path in named:
    stem = path.rsplit("/", 1)[-1].rsplit(".", 1)[0]
    assert stem in pattern, f"classifier does not name {path}"
    assert unwired(pattern.replace(stem, "unrelated-literal"), workflows[0]), f"classifier mutation survived: {path}"
    listing = f'      - "{path}"\n'
    if listing in workflows[0]:
        assert unwired(pattern, workflows[0].replace(listing, "")), f"push-path mutation survived: {path}"
print("PASS  attribution operands start the engine gate and count as engine changes")

count = 0
def invoke(paths, mode="good", expected=0, engine=False, extra_env=None, revisions=None):
    global count
    count += 1
    paths_file, output, log = [work / f"{count}.{suffix}" for suffix in ("paths", "out", "calls")]
    paths_file.write_text("\n".join(paths) + "\n")
    log.write_text("")
    env = dict(os.environ, PATH=str(bin_dir) + os.pathsep + os.environ["PATH"], MOCK_LOG=str(log), MOCK_MODE=mode,
               MOCK_ROOTS=json.dumps([str(fixture / project) for project in projects]))
    env.update(extra_env or {})
    subject = ["--base", revisions[0], "--head", revisions[1]] if revisions else ["--paths-file", str(paths_file)]
    result = subprocess.run(["bash", str(fixture / "scripts/change-completeness"), *subject, "--github-output", str(output)],
                            env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=20)
    assert (result.returncode == 0) == (expected == 0), (paths, mode, result.returncode, result.stdout)
    assert ("change-completeness: PASS" in result.stdout) == (expected == 0)
    if output.exists():
        assert output.read_text() == f"engine_changed={str(engine).lower()}\n", result.stdout
    calls = [json.loads(line) for line in log.read_text().splitlines()]
    return calls, result.stdout

def tests(calls): return [call for call in calls if call[0] == "test"]
def v2_tests(calls): return [call for call in tests(calls) if "/tests/coord-board-v2-" in call[1]]

def compiled(calls):
    assert calls and calls[0] == ["--version"]
    phases = [call for call in calls if call[0] == "msbuild"]
    restores = [call for call in calls if call[0] == "restore-root"]
    builds = [call for call in calls if call[0] == "build-root"]
    assert len(phases) == 2 and phases[0][1] == phases[1][1]
    assert [call[2:] for call in phases] == [["-target:RestoreRoots", "-maxCpuCount:1"], ["-target:BuildRoots", "-maxCpuCount:1"]]
    assert [Path(call[1]).relative_to(fixture).as_posix() for call in restores] == projects
    assert [Path(call[1]).relative_to(fixture).as_posix() for call in builds] == projects
    assert not any(call[0] in {"restore", "build"} for call in calls), "no repeated per-root process/evaluation"
    verbs = [call[0] for call in calls]
    assert max(i for i, v in enumerate(verbs) if v == "restore-root") < min(i for i, v in enumerate(verbs) if v == "build-root")
    assert max(i for i, v in enumerate(verbs) if v == "build-root") < min(i for i, v in enumerate(verbs) if v == "test")
    assert all("--no-build" in call and "--no-restore" in call and "Release" in call for call in tests(calls))
    assert len(v2_tests(calls)) == 2
    assert not any("FS.GG.Coord.GitHub.Tests/" in call[1] for call in tests(calls)), "broad GitHub tests remain independent"

for path in ("docs/readme.md", "scripts/ci-gate-impact.py", "tests/ci-runtime-optimization/run.sh"):
    calls, _ = invoke([path])
    assert not calls, path
for path, engine in (
    ("tests/coord-board-v2-projection/V2ProjectionTests.fs", False),
    ("tests/coord-board-v2-cli-join/BoardV2ApplicationTests.fs", False),
    ("tests/coord-board-v2-projection/ProgrammeFixture.fs", False),
    ("src/FS.GG.Coord.Core/Review.fs", True),
    ("src/FS.GG.Telemetry.Contracts/Contracts.fs", True),
    ("Directory.Build.props", True),
    ("Directory.Packages.props", True),
    ("global.json", True),
    (".github/workflows/coord-github.yml", False),
):
    calls, _ = invoke([path], engine=engine)
    compiled(calls)
    assert len(tests(calls)) == (5 if engine else 2), path
# UTEL-CI-01 reads the attribution file and the workflows it names (#4994 drift): an engine decision
# boundary with no V2 subject, so the engine selector runs its three focused suites and not the V2 ones.
for path in (".fsgg/telemetry-ci-attribution.json", ".github/workflows/routine-eligibility.yml"):
    calls, _ = invoke([path], engine=True)
    assert calls and calls[0] == ["--version"] and len(tests(calls)) == 3 and not v2_tests(calls), path
calls, _ = invoke(["scripts/ci-gate-impact.py", "src/FS.GG.Coord.Core/Review.fs"], engine=True)
compiled(calls)
for mode in ("restore-error", "build-error", "missing-target", "timeout", "signal", "missing-tool", "empty-sdk", "malformed-sdk"):
    calls, diagnostic = invoke(["src/FS.GG.Coord.Core/Review.fs"], mode, expected=1, engine=True)
    assert not tests(calls), mode
    if mode == "restore-error":
        assert len([c for c in calls if c[0] == "restore-root"]) == 12 and not any(c[0] == "build-root" for c in calls)
        assert len([c for c in calls if c[0] == "msbuild"]) == 1
        assert diagnostic.count("fixture MSBuild root failed:") == 2
    if mode in ("build-error", "missing-target"):
        assert len([c for c in calls if c[0] == "build-root"]) == 12
        assert diagnostic.count("fixture MSBuild root failed:") == 2, diagnostic
    if mode in ("timeout", "signal"): assert len([c for c in calls if c[0] == "build-root"]) == 1
# The real generated XML/commands, rather than a duplicate planned traversal, are
# checked at each fake transport entry. Mutating those contracts blocks tests.
gate = fixture / "scripts/change-completeness"
original_gate = gate.read_text()
for old,new in (
    ('BuildInParallel="false"','BuildInParallel="true"'),
    ('StopOnFirstFailure="false"','StopOnFirstFailure="true"'),
    ('SkipNonexistentProjects="false"','SkipNonexistentProjects="true"'),
    ('SkipNonexistentTargets="false"','SkipNonexistentTargets="true"'),
    ('"RestoreLockedMode=true"','"RestoreLockedMode=false"'),
    ('"Configuration=Release"','"Configuration=Debug"'),
    ('for project in projects:\n        ET.SubElement(items','for project in projects[:-1]:\n        ET.SubElement(items'),
    ('-target:BuildRoots -maxCpuCount:1','-target:BuildRoots -maxCpuCount:1 -restore'),
    ('-maxCpuCount:1','-maxCpuCount:2'),
):
    assert old in original_gate, old
    gate.write_text(original_gate.replace(old,new))
    calls,_ = invoke(["src/FS.GG.Coord.Core/Review.fs"],expected=1,engine=True)
    assert not tests(calls), (old,new)
gate.write_text(original_gate)
print("PASS  actual generated traversal/phase/cache-property mutations block dependent tests")
# Reach the real elapsed-budget refusal without sleeping or invoking a compiler.
budget_env = work / "elapsed-budget.sh"
budget_env.write_text("SECONDS=420\n")
calls, diagnostic = invoke(["src/FS.GG.Coord.Core/Review.fs"], expected=1, engine=True,
                           extra_env={"BASH_ENV": str(budget_env)})
assert not calls and "budget exhausted; coverage incomplete" in diagnostic
# Remove the executable from a closed PATH; there is no fallback to a host SDK.
missing_bin = work / "no-dotnet-bin"
missing_bin.mkdir()
for tool in ("bash", "dirname", "mktemp", "cp", "python3", "cat", "grep", "rm", "timeout"):
    executable = shutil.which(tool)
    assert executable, tool
    (missing_bin / tool).symlink_to(executable)
calls, diagnostic = invoke(["src/FS.GG.Coord.Core/Review.fs"], expected=1, engine=True,
                           extra_env={"PATH": str(missing_bin)})
assert not calls and "required compiler/timeout tool unavailable" in diagnostic
for mode in ("test-error", "missing-trx", "malformed-trx", "zero-trx", "failed-trx", "skipped-trx"):
    calls, _ = invoke(["tests/coord-board-v2-cli-join/BoardV2ApplicationTests.fs"], mode, expected=1)
    assert len(v2_tests(calls)) == 2, "independent V2 suites must both be attempted"
for path in ('"src/quoted.fs"', "../outside", ""):
    calls, _ = invoke([path], expected=1)
    assert not calls
project = fixture / projects[1]
project.rename(project.with_suffix(".missing"))
calls, _ = invoke(["src/FS.GG.Coord.Core/Review.fs"], expected=1)
assert not calls
project.with_suffix(".missing").rename(project)
project = fixture / "tests/coord-board-v2-cli-join/coord-board-v2-cli-join.fsproj"
original = project.read_text()
for malformed in ("<Project", original.replace("ProjectReference Include=", 'ProjectReference Include="$UNKNOWN/" Unused=')):
    project.write_text(malformed)
    calls, _ = invoke(["tests/coord-board-v2-cli-join/BoardV2ApplicationTests.fs"], expected=1)
    assert not calls
project.write_text(original)
# Real Git rename discovery must preserve the old linked Compile item's impact even
# when its new path is outside every V2 dependency folder. Only the compiler is fake.
def git(*args):
    result = subprocess.run(["git", "-c", "core.hooksPath=/dev/null", "-c", "user.name=Preflight fixture",
                             "-c", "user.email=preflight@example.invalid", "-C", str(fixture), *args],
                            check=True, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=10)
    return result.stdout.strip()

linked = "tests/coord-board-v2-projection/ProgrammeFixture.fs"
renamed = "docs/renamed-programme-fixture.fs"
(fixture / linked).write_text("// isolated rename fixture\n")
git("init", "--quiet", "--initial-branch=fixture")
git("add", ".")
git("commit", "--quiet", "-m", "fixture base")
base = git("rev-parse", "HEAD")
(fixture / renamed).parent.mkdir(parents=True, exist_ok=True)
git("mv", linked, renamed)
git("commit", "--quiet", "-m", "move linked source outside V2 folders")
head = git("rev-parse", "HEAD")
assert git("diff", "--name-only", "--find-renames", base, head).splitlines() == [renamed]
assert set(git("diff", "--name-only", "--no-renames", base, head).splitlines()) == {linked, renamed}
calls, _ = invoke([], revisions=(base, head))
compiled(calls)
assert len(tests(calls)) == 2
# Reintroducing rename detection must break that exact caller oracle.
gate = fixture / "scripts/change-completeness"
original_gate = gate.read_text()
assert "--no-renames" in original_gate
gate.write_text(original_gate.replace("--no-renames", "--find-renames"))
mutant_calls, _ = invoke([], revisions=(base, head))
try: compiled(mutant_calls)
except AssertionError: pass
else: raise AssertionError("rename-impact regression mutation survived")
gate.write_text(original_gate)
print("PASS  real Git linked-source rename caller and old-path-loss mutation")
# Exercise the actual default native-control branch with the same closed fake tool.
# Assert its own locked prerequisites, so cold docs-only callers need no earlier build.
fixture_script = (root / "tests/change-completeness/run.sh").read_text()
start = fixture_script.index('\nif [ "$static_only" = false ]; then')
end = fixture_script.index("# The production runner", start)
branch = fixture_script[start:end]
zero_log = work / "default-zero.calls"
zero_log.write_text("")
env = dict(os.environ, PATH=str(bin_dir) + os.pathsep + os.environ["PATH"], MOCK_LOG=str(zero_log), MOCK_MODE="native-zero-control")
prelude = 'set -euo pipefail; ROOT="$1"; WORK="$2"; static_only=false; ok() { :; }; bad() { exit 1; };\n'
zero = subprocess.run(["bash", "-c", prelude + branch, "fixture", str(fixture), str(work / "default-zero")],
                      env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=10)
assert zero.returncode == 0, zero.stdout
zero_calls = [json.loads(line) for line in zero_log.read_text().splitlines()]
assert len(zero_calls) == 2 and [call[0] for call in zero_calls] == ["restore", "test"]
assert zero_calls[0][1] == zero_calls[1][1] == str(fixture / "tests/FS.GG.Coord.Cli.Lifecycle.Tests/FS.GG.Coord.Cli.Lifecycle.Tests.fsproj")
assert "--locked-mode" in zero_calls[0] and "--no-restore" in zero_calls[1]
assert "FullyQualifiedName~DefinitelyNoLifecycleTestMatches" in zero_calls[1]
print("PASS  cold default zero-match branch prerequisite ordering (fake tool only)")
print(f"PASS  {count} isolated actual-gate fake-tool scenarios (native compiler/runtime NOT RUN)")
PY_CALLER
then
  ok 'compiler barrier, impact, incomplete coverage, and independent test failure controls'
else
  bad 'compiler barrier caller controls failed'
fi

selector="$(grep -F 'if grep -Eq ' "$ROOT/scripts/change-completeness")"
for required_pattern in \
  'src/FS\.GG\.(Coord|Telemetry)\.' \
  '\.github/workflows/coord-engine\.yml' \
  '\.github/workflows/routine-eligibility\.yml' \
  '\.fsgg/telemetry-ci-attribution\.json' \
  'Directory\.(Build|Packages)' \
  'dist/dotnet/Directory\.Build\.props' \
  'global\.json'; do
  [[ "$selector" == *"$required_pattern"* ]] \
    && ok "engine selector retains $required_pattern" \
    || bad "engine selector lost $required_pattern"
done

if grep -Fq 'FS.GG.Coord.Cli.Lifecycle.Tests.fsproj' "$ROOT/scripts/change-completeness" \
  && ! grep -Fq 'dotnet test "$ROOT/tests/FS.GG.Coord.Cli.Tests/FS.GG.Coord.Cli.Tests.fsproj" -c Release --no-restore' "$ROOT/scripts/change-completeness"; then
  ok 'focused lifecycle filters execute in the owning Lifecycle assembly'
else
  bad 'focused lifecycle filters still target the residual CLI assembly'
fi
grep -Fq 'read-trx-count.py" "$WORK/lifecycle-focus/lifecycle-focus.trx"' "$ROOT/scripts/change-completeness" \
  && grep -Fq -- '--minimum 1 --label "change-completeness (Lifecycle focused)"' "$ROOT/scripts/change-completeness" \
  && ok 'focused lifecycle selection is guarded against zero-match success' \
  || bad 'focused lifecycle selection can pass vacuously with zero matches'

if [ "$static_only" = false ]; then
  # Keep the original native zero-match mutation in hosted qualification. Static-only
  # runs above use the maintained TRX reader over fake results, not a native PASS claim.
  # This fixture may run alone on a cold docs-only checkout. Bound both commands
  # by the same five-minute ceiling, including fixture elapsed time. In CI the
  # outer ten-minute JOB cap remains authoritative across all steps.
  remaining=$((300 - SECONDS))
  [ "$remaining" -gt 0 ] || { echo "change-completeness fixture: native prerequisite budget exhausted" >&2; exit 124; }
  timeout --signal=TERM --kill-after=5s "${remaining}s" dotnet restore \
    "$ROOT/tests/FS.GG.Coord.Cli.Lifecycle.Tests/FS.GG.Coord.Cli.Lifecycle.Tests.fsproj" --locked-mode >/dev/null
  remaining=$((300 - SECONDS))
  [ "$remaining" -gt 0 ] || { echo "change-completeness fixture: native zero-match budget exhausted" >&2; exit 124; }
  timeout --signal=TERM --kill-after=5s "${remaining}s" dotnet test "$ROOT/tests/FS.GG.Coord.Cli.Lifecycle.Tests/FS.GG.Coord.Cli.Lifecycle.Tests.fsproj" \
    -c Release --no-restore --filter FullyQualifiedName~DefinitelyNoLifecycleTestMatches \
    --logger "trx;LogFileName=zero.trx" --results-directory "$WORK/zero" >/dev/null
  if python3 "$ROOT/scripts/read-trx-count.py" "$WORK/zero/zero.trx" \
    --minimum 1 --label "change-completeness zero-match mutation" >/dev/null 2>&1; then
    bad 'zero-match mutation passed the non-vacuity guard'
  else
    ok 'zero-match mutation reds the non-vacuity guard'
  fi
else
  printf 'SKIP  native Lifecycle zero-match mutation (--static-only); native compiler/runtime NOT RUN\n'
fi

# The production runner must name every active structural family. These are observable diagnostics, not prose:
# deleting a stage makes this fixture red before a PR can silently stop running that family.
for label in \
  'closing-keyword and commit-message contract' \
  'SDD ship-verdict provenance' \
  'command catalogue, parser, render, write-ness, contract, and help closure' \
  'handler ownership and production registration' \
  'delivery, review, declared-path, and focused production-route parity'; do
  grep -Fq "$label" "$ROOT/scripts/change-completeness" && ok "named stage: $label" || bad "missing named stage: $label"
done

release_census_line="$(awk '/check-v1-writer-census.py --candidate/ { print NR; exit }' "$ROOT/.github/workflows/release-coord-engine.yml")"
release_push_line="$(awk '/dotnet nuget push/ { print NR; exit }' "$ROOT/.github/workflows/release-coord-engine.yml")"
if [ -n "$release_census_line" ] && [ -n "$release_push_line" ] && [ "$release_census_line" -lt "$release_push_line" ]; then
  ok 'release census gates pack and publish'
else
  bad 'release can publish before writer census'
fi
release_receiver_line="$(awk '/check-v1-receiver-census.py/ { print NR; exit }' "$ROOT/.github/workflows/release-coord-engine.yml")"
if [ -n "$release_receiver_line" ] && [ -n "$release_push_line" ] && [ "$release_receiver_line" -lt "$release_push_line" ]; then
  ok 'release receiver source census gates publish'
else
  bad 'release can publish before receiver source census'
fi

grep -Fq 'needs: change-completeness' "$ROOT/.github/workflows/coord-engine.yml" \
  && ok 'expensive engine job depends on change-completeness' \
  || bad 'engine job can start before change-completeness'
pull_request_trigger="$(sed -n '/^  pull_request:/,/^  push:/p' "$ROOT/.github/workflows/coord-engine.yml")"
if grep -q 'paths:' <<<"$pull_request_trigger"; then
  bad 'required change-completeness context is path-filtered'
else
  ok 'required change-completeness context reports on every pull-request head'
fi
predecessor_job="$(sed -n '/^  change-completeness:/,/^  engine:/p' "$ROOT/.github/workflows/coord-engine.yml" | grep -v '^[[:space:]]*#')"
grep -Fq 'timeout-minutes: 10' <<<"$predecessor_job" \
  && grep -Fq 'deadline=420' "$ROOT/scripts/change-completeness" \
  && ok 'workflow encodes the ten-minute job cap above the 420 s script budget' \
  || bad 'job cap and script budget are not encoded together'
cache_saved_on_failure() {
  grep -Fq 'actions/cache/restore@v6' <<<"$1" \
    && grep -Fq 'actions/cache/save@v6' <<<"$1" \
    && grep -Fq "always() && steps.nuget-cache.outputs.cache-hit != 'true'" <<<"$1" \
    && ! grep -Fq 'cache: true' <<<"$1"
}
if cache_saved_on_failure "$predecessor_job"; then
  ok 'package cache is saved even when a gate fails, so a rerun starts warm'
else
  bad 'package cache is not saved on failure'
fi
# Inversions: each way of going back to a cache that only a green job saves must be refused.
for mutation in \
  "${predecessor_job//always() && /}" \
  "${predecessor_job//actions\/cache\/save@v6/actions\/cache@v6}" \
  "${predecessor_job//global-json-file: global.json/global-json-file: global.json
          cache: true}"; do
  if cache_saved_on_failure "$mutation"; then
    bad 'a cache that only a green job saves was accepted'
  else
    ok 'inversion refused: package cache saved only on success'
  fi
done

focused_fixture_calls="$(awk '
  /^  change-completeness:/ { inside=1; next }
  /^  engine:/ { inside=0 }
  inside && /run: bash tests\/ci-runtime-optimization\/run.sh/ { count++ }
  END { print count+0 }
' "$ROOT/.github/workflows/coord-engine.yml")"
if [ "$focused_fixture_calls" = 1 ]; then
  ok 'required predecessor directly owns exactly one focused CI selector fixture invocation'
else
  bad "required predecessor has $focused_fixture_calls focused CI selector fixture invocations"
fi
full_engine_fixture_calls="$(awk '
  /^  engine:/ { inside=1; next }
  inside && /^  [A-Za-z][A-Za-z0-9_-]*:/ { inside=0 }
  inside && /run: bash tests\/ci-runtime-optimization\/run.sh/ { count++ }
  END { print count+0 }
' "$ROOT/.github/workflows/coord-engine.yml")"
if [ "$full_engine_fixture_calls" = 0 ]; then
  ok 'full engine job does not repeat the focused CI selector fixture'
else
  bad 'full engine job still repeats the focused CI selector fixture'
fi
grep -Fq "if: github.event_name == 'workflow_dispatch' || needs.change-completeness.outputs.engine_changed == 'true'" \
  "$ROOT/.github/workflows/coord-engine.yml" \
  && ok 'workflow uses the bounded impact decision for pull requests and pushes' \
  || bad 'workflow can bypass the bounded impact decision outside manual dispatch'
grep -Fq -- '- "scripts/ci-gate-impact.py"' "$ROOT/.github/workflows/coord-engine.yml" \
  && ok 'push trigger reaches the bounded selector check when its implementation changes' \
  || bad 'push trigger omits the CI selector implementation'

# This unfiltered required context independently proves the Q0 job is reachable. A checker
# that only runs inside the checked job cannot detect its own job/trigger being disabled.
if python3 "$ROOT/work/2953-gh-modernization-m0-invariants/verify_q0_workflow.py" \
  "$ROOT/.github/workflows/github-substrate-q0.yml" --self-test; then
  ok 'independent gate proves Q0 live acceptance is trigger-, job-, and step-reachable'
else
  bad 'Q0 live acceptance can be skipped outside its own job'
fi

printf '\nchange-completeness fixture: %d passed, %d failed\n' "$pass" "$fail"
[ "$fail" -eq 0 ]
