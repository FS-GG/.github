#!/usr/bin/env python3
"""Hermetic orchestration tests for the Wizard's provider-catalog route (FS-GG/.github#3010).

The real compiled Wizard runs against a local HTTP server that plays Templates (the ref-pinned catalog)
and the NuGet flat container (the pinned template archive), and a stub `fsgg-sdd` that records every
argument vector and answers with SDD's JSON projections. Nothing is published, fetched from the network
or scaffolded by SDD; what is asserted is the Wizard's own contract:

  * the catalog is fetched at the requested ref and its exact bytes are what SDD is given;
  * the archive is the exact package the provider's template source names, pinned by digest;
  * every typed value is forwarded literally, with no identifier derived from the product name;
  * a fatal preflight (stale ref, bad digest, bad policy, unsupported platform, SDD refusal, occupied
    target) mutates no target and never reaches `scaffold`;
  * offline operation uses only explicitly pinned files; no fallback exists;
  * a provider added only to the catalog is created with no Wizard source change;
  * built-in providers keep their legacy route and never read the catalog.

usage: catalog-route.py <new-sdd-workspace.dll> <scratch-dir>
"""
import hashlib
import io
import zipfile
import http.server
import json
import os
import pathlib
import re
import shutil
import signal
import socketserver
import subprocess
import sys
import threading

import platform
if platform.system() != "Linux" or platform.machine() not in ("x86_64", "AMD64"):
    print("SKIP  the catalog route is exercised on linux-x64 only")
    sys.exit(0)
DLL = pathlib.Path(sys.argv[1]).resolve()
HERE = pathlib.Path(__file__).resolve().parent
SCRATCH = pathlib.Path(sys.argv[2]).resolve()
if SCRATCH.exists():
    sys.exit("scratch directory must be absent")
SCRATCH.mkdir(parents=True)

CATALOG = (HERE / "catalogs" / "polyglot-0.20.0.catalog.json").read_bytes()
REF = "fs-gg-templates/v0.20.0"
def fixture_archive(include_ci=True):
    memory = io.BytesIO()
    with zipfile.ZipFile(memory, "w") as archive:
        for name in ("typescript-cli", "javascript-cli", "rust-cli", "go-cli", "zig-cli"):
            prefix = "content/templates/%s/" % name
            archive.writestr(prefix + ".template.config/template.json", json.dumps({"shortName": "fs-gg-" + name}))
            if include_ci:
                archive.writestr(prefix + ".github/workflows/project-knowledge.yml", "fixture owner workflow")
                archive.writestr(prefix + "scripts/check-project-knowledge.py", "fixture owner checker")
    return memory.getvalue()


ARCHIVE = fixture_archive()
ARCHIVE_PATH = "/fs.gg.workspace.template/0.20.0/fs.gg.workspace.template.0.20.0.nupkg"


def has(output, needle):
    """Spectre wraps long lines at 80 columns when redirected; compare with all whitespace removed."""
    return re.sub(r"\s+", "", needle) in re.sub(r"[\s│]+", "", output)


def digest(data):
    return "sha256:" + hashlib.sha256(data).hexdigest()


results = {"pass": 0, "fail": 0}


def check(name, condition, detail=""):
    if condition:
        results["pass"] += 1
        print("PASS  " + name)
    else:
        results["fail"] += 1
        print("FAIL  " + name)
        if detail:
            for line in str(detail).splitlines()[-25:]:
                print("    | " + line)


# ── A local Templates + NuGet flat container ─────────────────────────────────────────────────────
class Server:
    def __init__(self):
        self.routes = {}
        self.requests = []
        outer = self

        class Handler(http.server.BaseHTTPRequestHandler):
            def do_GET(self):
                outer.requests.append(self.path)
                body = outer.routes.get(self.path)
                if body is None:
                    self.send_response(404)
                    self.end_headers()
                    return
                self.send_response(200)
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

            def log_message(self, *args):
                pass

        self.httpd = socketserver.TCPServer(("127.0.0.1", 0), Handler)
        self.port = self.httpd.server_address[1]
        threading.Thread(target=self.httpd.serve_forever, daemon=True).start()

    def base(self):
        return "http://127.0.0.1:%d" % self.port


server = Server()
server.routes["/%s/providers/polyglot.catalog.json" % REF] = CATALOG
server.routes[ARCHIVE_PATH] = ARCHIVE

# ── The stub SDD ─────────────────────────────────────────────────────────────────────────────────
BIN = SCRATCH / "bin"
BIN.mkdir()
STUB_LOG = SCRATCH / "stub.log"
stub = BIN / "fsgg-sdd"
stub.write_text('''#!/usr/bin/env python3
import json, os, pathlib, sys
args = sys.argv[1:]
with open(os.environ["STUB_LOG"], "a") as log:
    log.write(json.dumps(args) + "\\n")
def option(name):
    return args[args.index(name) + 1] if name in args else None
if args[:1] == ["--version"]:
    print(os.environ.get("STUB_VERSION", "2.5.1"))
elif args[:2] == ["catalog", "inspect"]:
    if os.environ.get("STUB_INSPECT") == "refuse":
        print(json.dumps({"schema": "fsgg.catalog-preview/v1", "status": "refused",
                          "diagnostics": [{"code": "catalog.fixtureRefusal", "path": "$.fixture", "message": "refused by the stub"}]}))
        sys.exit(1)
    print(json.dumps({"schema": "fsgg.catalog-preview/v1", "status": "prepared", "selected": {"providerId": option("--provider")}}))
elif args[:1] == ["scaffold"] and "--catalog" in args:
    if os.environ.get("STUB_SCAFFOLD") == "fail":
        print(json.dumps({"schema": "fsgg.catalog-scaffold/v1", "status": "failed", "diagnostics": [{"code": "catalog.fixtureFailure", "path": "$", "message": "failed by the stub"}]}))
        sys.exit(2)
    if "--dry-run" in args:
        print(json.dumps({"schema": "fsgg.catalog-scaffold/v1", "status": "prepared", "diagnostics": []}))
        sys.exit(0)
    import shutil
    seen = pathlib.Path(os.environ["STUB_LOG"]).parent
    shutil.copy(option("--catalog"), seen / "seen-catalog")
    shutil.copy(option("--template-archive"), seen / "seen-archive")
    shutil.copy(option("--admission-policy"), seen / "seen-policy")
    target = pathlib.Path(option("--root"))
    (target / ".fsgg").mkdir(parents=True)
    (target / ".fsgg" / "scaffold-provenance.json").write_text("{}")
    if os.environ.get("STUB_KNOWLEDGE") != "missing-ci":
        for relative in (".github/workflows/project-knowledge.yml", "scripts/check-project-knowledge.py"):
            path = target / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("fixture owner payload")
    print(json.dumps({"schema": "fsgg.catalog-scaffold/v1", "status": "succeeded", "ownership": "Settled", "diagnostics": []}))
elif args[:1] == ["scaffold"]:
    target = pathlib.Path(option("--root"))
    (target / ".fsgg").mkdir(parents=True, exist_ok=True)
    print("legacy scaffold ok")
elif args[:2] == ["knowledge", "initialize"]:
    if os.environ.get("STUB_KNOWLEDGE") == "initialize-refused":
        sys.exit(1)
    target = pathlib.Path(option("--root"))
    for relative in (".fsgg/knowledge-guide.md", ".fsgg/knowledge/schema.json", ".fsgg/knowledge/records/project-knowledge.json"):
        path = target / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("{}")
    print("initialized")
elif args[:2] == ["knowledge", "check"]:
    sys.exit(1 if os.environ.get("STUB_KNOWLEDGE") == "check-refused" else 0)
elif args[:1] == ["doctor"]:
    print(json.dumps({"schemaVersion": 1, "command": {"name": "doctor"}, "outcome": "succeeded", "coherent": True, "doctor": {"isCoherent": True}}))
else:
    print("unexpected stub invocation: %r" % (args,), file=sys.stderr)
    sys.exit(97)
''')
stub.chmod(0o755)

dotnet_dir = pathlib.Path(subprocess.check_output(["bash", "-c", "command -v dotnet"], text=True).strip()).resolve().parent
dotnet_path = str(dotnet_dir / "dotnet")

# The independently selected policy: budgets plus the archive association for 0.20.0.
policy = SCRATCH / "policy.json"
policy.write_text(json.dumps({
    "schemaVersion": 1,
    "maximumPreflightSeconds": 111,
    "maximumScaffoldSeconds": 222,
    "archives": [{"templateSource": "FS.GG.Workspace.Template::0.20.0", "packageId": "FS.GG.Workspace.Template", "packageVersion": "0.20.0"}],
}))
policy_digest = digest(policy.read_bytes())
wrong_policy = SCRATCH / "policy-other.json"
wrong_policy.write_text(json.dumps({
    "schemaVersion": 1, "maximumPreflightSeconds": 5, "maximumScaffoldSeconds": 5,
    "archives": [{"templateSource": "FS.GG.Workspace.Template::0.19.3", "packageId": "FS.GG.Workspace.Template", "packageVersion": "0.19.3"}],
}))

counter = [0]
SEEN = [None]


def run(args, env_extra=None, bases=True, target=None):
    """Run the Wizard; return (rc, output, target path, recorded stub invocations)."""
    counter[0] += 1
    case = SCRATCH / ("case-%02d" % counter[0])
    case.mkdir()
    tmp = case / "tmp"
    tmp.mkdir()
    target = target or (case / "out")
    log = case / "stub.log"
    env = {
        "PATH": "%s:%s:/usr/bin:/bin" % (BIN, dotnet_dir),
        "HOME": str(case / "home"),
        "DOTNET_CLI_HOME": str(case / "home"),
        "TMPDIR": str(tmp),
        "STUB_LOG": str(log),
        "NO_COLOR": "1",
    }
    if bases:
        env["FSGG_TEMPLATES_RAW_BASE"] = server.base()
        env["FSGG_NUGET_FLAT_BASE"] = server.base()
    env.update(env_extra or {})
    argv = [dotnet_path, str(DLL)] + [a.replace("@T", str(target)).replace("@P", str(policy)) for a in args]
    completed = subprocess.run(argv, env=env, stdin=subprocess.DEVNULL, capture_output=True, text=True, timeout=300)
    calls = [json.loads(line) for line in log.read_text().splitlines()] if log.exists() else []
    leftovers = [p.name for p in tmp.iterdir()]
    SEEN[0] = case
    return completed.returncode, completed.stdout + completed.stderr, target, calls, leftovers


def scaffold_call(calls):
    return next((c for c in calls if c[:1] == ["scaffold"] and "--catalog" in c), None)


def value(call, flag):
    return call[call.index(flag) + 1]


GO = ["--template", "go-cli", "--ref", REF, "--param", "packageIdentity=example.org/explicit/product",
      "--param", "codeIdentifier=explicit_greet", "--admission-policy", "@P", "--pinned", "--no-coordination"]

# ── 1. The online route: ref-pinned catalog, exact archive, literal values ───────────────────────
server.requests.clear()
rc, out, target, calls, leftovers = run(["@T", "Awkward name!? é"] + GO)
check("online catalog creation exits 0", rc == 0, out)
check("the catalog was fetched at the requested ref, before selection",
      server.requests[:1] == ["/%s/providers/polyglot.catalog.json" % REF], server.requests)
check("the pinned archive is the exact package the template source names", ARCHIVE_PATH in server.requests, server.requests)
check("no descriptor of the legacy kind was requested", not any(r.endswith(".providers.yml") for r in server.requests), server.requests)
scaffold = scaffold_call(calls)
check("scaffold was invoked on the catalog route", scaffold is not None, calls)
if scaffold:
    check("SDD is given exactly the catalog bytes the Wizard verified",
          (SEEN[0] / "seen-catalog").read_bytes() == CATALOG and value(scaffold, "--catalog-sha256") == digest(CATALOG))
    check("SDD is given exactly the archive bytes whose digest was pinned",
          (SEEN[0] / "seen-archive").read_bytes() == ARCHIVE and value(scaffold, "--template-sha256") == digest(ARCHIVE))
    check("the policy digest is that of the supplied file", value(scaffold, "--admission-policy-sha256") == policy_digest)
    check("budgets come from the policy", value(scaffold, "--preflight-timeout-seconds") == "111" and value(scaffold, "--scaffold-timeout-seconds") == "222")
    check("the platform is the host's and the transport is a fully qualified dotnet",
          value(scaffold, "--platform") == "linux-x64" and value(scaffold, "--transport-executable") == dotnet_path)
    check("the target is the absolute requested root", value(scaffold, "--root") == str(target))
    check("values are forwarded literally; the display name derives no identifier",
          "rawName=Awkward name!? é" in scaffold and "packageIdentity=example.org/explicit/product" in scaffold and "codeIdentifier=explicit_greet" in scaffold, scaffold)
    check("no legacy productName/lifecycle parameter is sent to a catalog provider", not any(a.startswith(("productName=", "lifecycle=")) for a in scaffold), scaffold)
    check("scaffold carries no template-update flag on the catalog route", "--no-update" not in scaffold)
inspect_index = next((i for i, c in enumerate(calls) if c[:2] == ["catalog", "inspect"]), None)
scaffold_index = next((i for i, c in enumerate(calls) if c[:1] == ["scaffold"]), None)
check("SDD inspects read-only before it scaffolds", inspect_index is not None and scaffold_index is not None and inspect_index < scaffold_index, calls)
check("no `dotnet new` governance overlay is attempted for a catalog provider", has(out, "governance overlay") and has(out, "skipped"), out)
check("the creation reports the workspace and installs the initialization handoff",
      (target / ".fsgg" / "workspace-initialization.json").exists() and has(out, "Initialization required"), out)
check("the private scratch directory is removed afterwards", leftovers == [], leftovers)

server.routes[ARCHIVE_PATH] = fixture_archive(False)
rc, out, no_ci_target, no_ci_calls, _ = run(["@T", "P"] + GO)
check("known-missing knowledge CI refuses before target mutation",
      rc != 0 and has(out, "lacks required project-knowledge asset") and not no_ci_target.exists() and not no_ci_calls, out)
server.routes[ARCHIVE_PATH] = ARCHIVE

# Every catalog route composes the canonical knowledge store before its first-agent handoff.
rc, out, target, calls, _ = run(["@T", "P"] + GO)
check("catalog creation initializes then checks knowledge through the exact producer",
      any(c[:2] == ["knowledge", "initialize"] for c in calls) and any(c[:2] == ["knowledge", "check"] for c in calls), calls)
manifest = json.loads((target / ".config/dotnet-tools.json").read_text())
check("catalog creation pins the actual capable knowledge producer",
      manifest["tools"]["fs.gg.sdd.cli"]["version"] == "2.5.1", manifest)
for knowledge_case, message in (("missing-ci", "Typed initialization is incomplete"),
                                ("initialize-refused", "refused project-knowledge initialization"),
                                ("check-refused", "knowledge check refused")):
    rc, out, failed_target, failed_calls, _ = run(["@T", "P"] + GO, env_extra={"STUB_KNOWLEDGE": knowledge_case})
    check("catalog knowledge %s fails before first-agent handoff" % knowledge_case,
          rc == 1 and has(out, message) and not (failed_target / ".fsgg/workspace-initialization.json").exists(), out)
rc, out, failed_target, failed_calls, _ = run(["@T", "P"] + GO, env_extra={"STUB_VERSION": "2.0.3"})
check("an insufficient knowledge producer refuses before catalog scaffold",
      rc == 1 and not failed_target.exists() and scaffold_call(failed_calls) is None and has(out, "2.1.0 or newer"), out)

# ── 2. A provider that exists only in the catalog ────────────────────────────────────────────────
fifth = json.loads(CATALOG)
extra = json.loads(json.dumps(fifth["providers"][0]))
extra.update(id="zig-cli", displayName="zig-cli", language="Zig", productShape="library", templateId="fs-gg-zig-cli")
extra["parameters"].append({"key": "optimize", "kind": "enum", "required": True, "prompt": "Optimization", "help": "Optimization",
                            "default": "release", "values": ["debug", "release"],
                            "validation": {"nonEmpty": True, "minLength": 1, "maxLength": 16, "allowedValues": []}})
fifth["providers"].append(extra)
fifth_bytes = json.dumps(fifth).encode()
server.routes["/fifth/providers/polyglot.catalog.json"] = fifth_bytes
rc, out, target, calls, _ = run(["@T", "Z", "--template", "zig-cli", "--ref", "fifth", "--param", "packageIdentity=zig.example/z",
                                 "--param", "codeIdentifier=zig_z", "--param", "optimize=debug", "--admission-policy", "@P", "--pinned", "--no-coordination"])
scaffold = scaffold_call(calls)
check("a provider added only to the catalog is created with no Wizard edit", rc == 0 and scaffold is not None and "optimize=debug" in scaffold, out)
rc, out, target, calls, _ = run(["@T", "Z", "--template", "zig-cli", "--ref", "fifth", "--param", "packageIdentity=zig.example/z",
                                 "--param", "codeIdentifier=zig_z", "--param", "optimize=fast", "--admission-policy", "@P", "--pinned"])
check("its enum is validated from the catalog before anything runs", rc == 2 and has(out, "optimize is invalid") and not target.exists() and not calls, out)

# ── 3. Fatal preflights leave no target and never reach scaffold ─────────────────────────────────
def refused(name, args, needle, rc_expected=None, **kwargs):
    rc, out, target, calls, leftovers = run(args, **kwargs)
    ok = rc != 0 and has(out, needle) and not target.exists() and scaffold_call(calls) is None and leftovers == []
    if rc_expected is not None:
        ok = ok and rc == rc_expected
    check(name, ok, "rc=%s target-exists=%s scaffold=%s leftovers=%s\n%s" % (rc, target.exists(), scaffold_call(calls), leftovers, out))
    return rc, out


refused("a stale ref (no catalog) refuses before any step", ["@T", "P"] + GO[:3] + ["stale-ref"] + GO[4:], "provider catalog is unavailable", 2)
refused("a catalog whose digest differs from the expected pin is refused", ["@T", "P"] + GO + ["--catalog-sha256", "sha256:" + "0" * 64], "not the expected", 2)
refused("a template archive whose digest differs from the expected pin is refused", ["@T", "P"] + GO + ["--template-sha256", "sha256:" + "0" * 64], "not the expected")
refused("a policy with no association for this archive is refused", ["@T", "P"] + GO[:-4] + ["--admission-policy", str(wrong_policy), "--pinned", "--no-coordination"], "no archive association")
refused("a policy digest pin mismatch is refused", ["@T", "P"] + GO + ["--admission-policy-sha256", "sha256:" + "1" * 64], "not the expected")
refused("a missing policy is refused at parse time", ["@T", "P", "--template", "go-cli", "--ref", REF, "--param", "packageIdentity=x", "--param", "codeIdentifier=y"], "requires --admission-policy", 2)
refused("a missing required typed value is refused at parse time", ["@T", "P", "--template", "go-cli", "--ref", REF, "--admission-policy", "@P"], "packageIdentity requires a value", 2)
refused("an unknown typed value is refused at parse time", ["@T", "P"] + GO + ["--param", "bogus=1"], "declares no parameter 'bogus'", 2)
refused("a legacy --lifecycle is not forwarded to a provider that declares none", ["@T", "P"] + GO + ["--lifecycle", "typed-sdd"], "declares no lifecycle parameter", 2)
refused("an inspect refusal from SDD stops before scaffold", ["@T", "P"] + GO, "catalog.fixtureRefusal", env_extra={"STUB_INSPECT": "refuse"})

unsupported = json.loads(CATALOG)
for provider in unsupported["providers"]:
    provider["platforms"] = ["plan9-x64"]
server.routes["/odd/providers/polyglot.catalog.json"] = json.dumps(unsupported).encode()
refused("a provider that does not support this host's platform is refused", ["@T", "P"] + GO[:3] + ["odd"] + GO[4:], "this host is linux-x64", 1)

broken = json.loads(CATALOG)
broken["providers"][2]["contractVersion"] = "9.9.9"
server.routes["/broken/providers/polyglot.catalog.json"] = json.dumps(broken).encode()
refused("one unsupported entry refuses the whole catalog, including valid providers", ["@T", "P"] + GO[:3] + ["broken"] + GO[4:], "Only descriptor protocol 3.0.0", 2)

occupied = SCRATCH / "occupied"
occupied.mkdir()
(occupied / "keep.txt").write_text("owner bytes")
server.requests.clear()
rc, out, _, calls, leftovers = run(["@T", "P"] + GO, target=occupied)
check("an occupied target is refused and left byte-identical",
      rc != 0 and has(out, "already exists") and (occupied / "keep.txt").read_text() == "owner bytes" and sorted(p.name for p in occupied.iterdir()) == ["keep.txt"] and scaffold_call(calls) is None and leftovers == [],
      out)
check("an occupied target is refused before the archive is downloaded", ARCHIVE_PATH not in server.requests, server.requests)

rc, out, target, calls, leftovers = run(["@T", "P"] + GO, env_extra={"STUB_SCAFFOLD": "fail"})
check("a failed SDD scaffold fails the creation and runs no later step",
      rc == 1 and has(out, "catalog.fixtureFailure") and not any(c[:1] == ["doctor"] for c in calls) and not (target / ".fsgg" / "workspace-initialization.json").exists(), out)

# ── 4. Offline operation: only explicitly pinned files, never a fallback ─────────────────────────
catalog_file = SCRATCH / "catalog.json"
catalog_file.write_bytes(CATALOG)
archive_file = SCRATCH / "template.nupkg"
archive_file.write_bytes(ARCHIVE)
OFFLINE = ["--template", "go-cli", "--param", "packageIdentity=example.org/explicit/product", "--param", "codeIdentifier=explicit_greet",
           "--admission-policy", "@P", "--pinned", "--no-coordination",
           "--catalog-file", str(catalog_file), "--catalog-sha256", digest(CATALOG),
           "--template-archive", str(archive_file), "--template-sha256", digest(ARCHIVE)]
dead = {"FSGG_TEMPLATES_RAW_BASE": "http://127.0.0.1:1", "FSGG_NUGET_FLAT_BASE": "http://127.0.0.1:1"}
rc, out, target, calls, _ = run(["@T", "P"] + OFFLINE, env_extra=dead)
scaffold = scaffold_call(calls)
check("an explicitly pinned offline creation needs no network", rc == 0 and scaffold is not None, out)
if scaffold:
    check("offline SDD receives the pinned files themselves", value(scaffold, "--catalog") == str(catalog_file) and value(scaffold, "--template-archive") == str(archive_file), scaffold)
refused("an offline catalog without its digest is refused", ["@T", "P"] + [a for a in OFFLINE if a not in ("--catalog-sha256", digest(CATALOG))], "requires --catalog-sha256", 2, env_extra=dead)
refused("an offline archive without its digest is refused", ["@T", "P"] + [a for a in OFFLINE if a not in ("--template-sha256", digest(ARCHIVE))], "requires --template-sha256", 2, env_extra=dead)
refused("an offline catalog that does not match its digest is refused", ["@T", "P"] + [("sha256:" + "2" * 64) if a == digest(CATALOG) else a for a in OFFLINE], "not the expected", env_extra=dead)
refused("an offline archive that does not match its digest is refused", ["@T", "P"] + [("sha256:" + "3" * 64) if a == digest(ARCHIVE) else a for a in OFFLINE], "not the expected", env_extra=dead)
refused("an unreachable catalog never falls back to any cached copy", ["@T", "P"] + GO, "provider catalog is unavailable", 2, env_extra=dead)

# ── 5. Dry run: SDD's own preparation, nothing written ───────────────────────────────────────────
rc, out, target, calls, leftovers = run(["@T", "P"] + GO + ["--dry-run"])
scaffold = scaffold_call(calls)
check("--dry-run asks SDD for a dry run and creates nothing", rc == 0 and scaffold is not None and "--dry-run" in scaffold and not target.exists() and leftovers == [] and not any(c[:1] == ["doctor"] for c in calls), out)

# ── 6. Built-in providers keep their route and never read the catalog ────────────────────────────
legacy_yml = b"source: FS.GG.Workspace.Template::0.19.3\nparameters: []\n"
server.routes["/main/providers/console.providers.yml"] = legacy_yml
server.requests.clear()
rc, out, target, calls, _ = run(["@T", "P", "--template", "console", "--pinned", "--no-coordination", "--no-governance"])
legacy = next((c for c in calls if c[:1] == ["scaffold"] and "--provider" in c), None)
check("a built-in template still scaffolds through its descriptor route",
      rc == 0 and legacy is not None and value(legacy, "--provider") == "console" and "productName=P" in legacy and "lifecycle=sdd" in legacy, out)
check("a built-in template never fetches the catalog", not any("polyglot.catalog.json" in r for r in server.requests), server.requests)
rc, out, _, calls, _ = run(["@T", "P", "--template", "console", "--param", "x=1"])
check("catalog options are refused for a built-in template", rc == 2 and has(out, "apply only to a catalog provider") and not calls, out)

# ── 7. Dynamic help ──────────────────────────────────────────────────────────────────────────────
rc, out, _, _, _ = run(["--list-providers", "--ref", "fifth"])
check("--list-providers prints built-ins and every catalog provider with typed parameters",
      rc == 0 and has(out, "zig-cli") and has(out, "Zig") and has(out, "--param optimize=<debug|release>") and has(out, "go-cli") and has(out, "console"), out)
rc, out, _, _, _ = run(["--list-providers", "--catalog-file", str(catalog_file), "--catalog-sha256", digest(CATALOG)], env_extra=dead)
check("--list-providers works offline from a pinned file", rc == 0 and has(out, "typescript-cli"), out)
rc, out, _, _, _ = run(["--list-providers", "--ref", "stale-ref"])
check("--list-providers names an unavailable catalog and still lists the built-ins", rc == 1 and has(out, "unavailable") and has(out, "console"), out)

# ── 8. The interactive wizard: language, then shape, then the descriptor's own typed prompts ──────
import fcntl
import pty
import select
import struct
import termios
import time

interactive_catalog = json.loads(fifth_bytes)
for provider in interactive_catalog["providers"]:
    if provider["id"] == "go-cli":
        next(p for p in provider["parameters"] if p["key"] == "codeIdentifier")["validation"]["allowedValues"] = ["  greet  "]
server.routes["/main/providers/polyglot.catalog.json"] = json.dumps(interactive_catalog).encode()


def interactive(steps, cwd, prompt_timeout=40):
    """Drive the real wizard in a pseudo-terminal. steps: (text to wait for, keys to send); returns (screen, exit code)."""
    env = {"PATH": "%s:%s:/usr/bin:/bin" % (BIN, dotnet_dir), "HOME": str(cwd / "home"), "TMPDIR": str(cwd / "tmp"),
           "STUB_LOG": str(cwd / "stub.log"), "TERM": "xterm", "NO_COLOR": "1", "FSGG_TEMPLATES_RAW_BASE": server.base(),
           "FSGG_NUGET_FLAT_BASE": server.base()}
    (cwd / "tmp").mkdir()
    pid, fd = pty.fork()
    if pid == 0:
        os.chdir(cwd)
        os.execve(dotnet_path, [dotnet_path, str(DLL)], env)
    fcntl.ioctl(fd, termios.TIOCSWINSZ, struct.pack("HHHH", 60, 200, 0, 0))
    screen = b""

    def pump(seconds):
        nonlocal screen
        end = time.time() + seconds
        while time.time() < end:
            ready, _, _ = select.select([fd], [], [], 0.1)
            if ready:
                try:
                    data = os.read(fd, 65536)
                except OSError:
                    return False
                if not data:
                    return False
                screen += data
        return True

    def text():
        return re.sub(rb"\x1b\[[0-9;?]*[A-Za-z]", b"", screen).decode(errors="replace")

    missing = None
    for wait_for, keys in steps:
        deadline = time.time() + prompt_timeout
        while wait_for not in text() and time.time() < deadline:
            if not pump(0.3):
                break
        if wait_for not in text():
            missing = wait_for
            break
        os.write(fd, keys.encode())
        pump(0.4)
    pump(1)
    code = None
    deadline = time.monotonic() + 5
    while time.monotonic() < deadline:
        child, status = os.waitpid(pid, os.WNOHANG)
        if child:
            code = os.waitstatus_to_exitcode(status)
            break
        pump(0.1)
    if code is None:
        # A missed prompt must fail the fixture, never leave CI waiting on stdin.
        os.kill(pid, signal.SIGKILL)
        _, status = os.waitpid(pid, 0)
        code = os.waitstatus_to_exitcode(status)
        missing = missing or "wizard exit before terminal deadline"
    os.close(fd)
    return text(), code, missing


DOWN, ENTER = "\x1b[B", "\r"
cwd = SCRATCH / "interactive"
cwd.mkdir()
screen, code, missing = interactive([
    ("Product name?", "Greeter" + ENTER),
    ("Target directory?", ENTER),
    ("Which language is the product written in?", DOWN + ENTER),            # built-in, Go, ...
    ("Which product shape for Go?", ENTER),
    ("Explicit language-valid function identifier (codeIdentifier)", "  greet  " + ENTER),
    ("Required explicit Go module path (packageIdentity)", "x" * 257 + ENTER),          # empty: refused by the descriptor's own bounds
    ("at most 256 characters are allowed", "example.org/acme/greeter" + ENTER),
    ("Human display name (rawName)", ENTER),                                # defaults to the product name
    ("policy", str(policy) + ENTER),
    ("Create this scaffold now?", "n" + ENTER),
], cwd)
check("the wizard offers the built-in templates and each catalog language as the first choice",
      "built-in templates" in screen and all(language in screen for language in ("Go", "JavaScript", "Rust", "TypeScript", "Zig")), screen[-1500:])
check("the product shape is a separate prompt", "Which product shape for Go?" in screen and "executable-cli" in screen, screen[-1500:])
check("each descriptor-declared prompt is asked in its own words, validated by its own bounds",
      "Required explicit Go module path" in screen and "at most 256 characters are allowed" in screen, screen[-1500:])
check("interactive descriptor strings preserve surrounding whitespace", "  greet  " in screen, screen[-2500:])
check("the raw-name prompt defaults to the product name", "Greeter" in screen.split("Human display name (rawName)")[-1], screen[-800:])
check("the resolved selection is previewed from the catalog before anything is created",
      "from the provider catalog" in screen and "param packageIdentity" in screen and "example.org/acme/greeter" in screen and "FS.GG.Workspace.Template::0.20.0" in screen, screen[-2500:])
check("declining creates nothing and does not call SDD", missing is None and code == 130 and not (cwd / "Greeter").exists() and not (cwd / "stub.log").exists(), "missing=%s code=%s\n%s" % (missing, code, screen[-800:]))

# Exercise the cleanup path that a missing prompt takes, with a short fixture-owned timeout.
control_cwd = SCRATCH / "interactive-missing-prompt"
control_cwd.mkdir()
_, control_code, control_missing = interactive([("deliberately absent prompt", "")], control_cwd, prompt_timeout=0.3)
check("a missing interactive prompt terminates and reaps the waiting Wizard",
      control_missing == "deliberately absent prompt" and control_code == -signal.SIGKILL,
      "missing=%s code=%s" % (control_missing, control_code))

print("\n%d passed, %d failed" % (results["pass"], results["fail"]))
shutil.rmtree(SCRATCH, ignore_errors=True)
sys.exit(1 if results["fail"] else 0)
