#!/usr/bin/env python3
"""Independent source and package-boundary refusal controls for FSC-02."""

import importlib.util
import json
import shutil
import tempfile
import warnings
import zipfile
from pathlib import Path

root = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("fsc02", root / "scripts/check-fsc02-telemetry-closure.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

with tempfile.TemporaryDirectory() as folder:
    fixture = Path(folder)
    for name in module.PROJECTS:
        shutil.copytree(root / "src" / name, fixture / "src" / name,
                        ignore=shutil.ignore_patterns("bin", "obj"))
    cli = fixture / "src/FS.GG.Coord.Cli"
    cli.mkdir()
    for suffix in ("fs", "fsi"):
        name = f"TelemetryStoreApplication.{suffix}"
        shutil.copy2(root / "src/FS.GG.Coord.Cli" / name, cli / name)
    allow = fixture / "tests/standalone-telemetry-host-package/allowed-files.txt"
    allow.parent.mkdir(parents=True)
    shutil.copy2(root / "tests/standalone-telemetry-host-package/allowed-files.txt", allow)

    clean = module.inspect(fixture)
    assert clean["issues"] == [], clean["issues"]
    assert "FS.GG.Coord.Core" in clean["hostProjectClosure"]
    assert "FS.GG.Coord.Cli" not in clean["hostProjectClosure"]
    assert "CanonicalJson" in clean["coreMixedTelemetryModules"]
    assert "LifecycleTelemetry" in clean["coreMixedTelemetryModules"]

    host_project = fixture / "src/FS.GG.Telemetry.Host/FS.GG.Telemetry.Host.fsproj"
    original = host_project.read_text()
    host_project.write_text(original.replace(
        '<ProjectReference Include="../FS.GG.Telemetry.Contracts/FS.GG.Telemetry.Contracts.fsproj" />',
        '<ProjectReference Include="../FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj" />\n'
        '    <ProjectReference Include="../FS.GG.Telemetry.Contracts/FS.GG.Telemetry.Contracts.fsproj" />'))
    assert any("Coord.Cli assembly" in issue for issue in module.inspect(fixture)["issues"])
    host_project.write_text(original)

    host_source = fixture / "src/FS.GG.Telemetry.Host/HostRuntime.fs"
    original = host_source.read_text()
    host_source.write_text(original + "\nopen FS.GG.Coord.LifecycleTelemetry\n")
    assert any("direct legacy lifecycle" in issue for issue in module.inspect(fixture)["issues"])
    host_source.write_text(original)

    dashboard_source = fixture / "src/FS.GG.Telemetry.Dashboard/DashboardProjection.fs"
    original_dashboard = dashboard_source.read_text()
    dashboard_source.write_text(original_dashboard + "\nopen FS.GG.Coord.LifecycleTelemetry\n")
    assert any("direct legacy lifecycle" in issue for issue in module.inspect(fixture)["issues"])
    dashboard_source.write_text(original_dashboard)

    original_project = host_project.read_text()
    host_project.write_text(original_project.replace(
        '<ProjectReference Include="../FS.GG.Telemetry.Contracts/FS.GG.Telemetry.Contracts.fsproj" />',
        '<ProjectReference Include="..\\FS.GG.Coord.Cli\\FS.GG.Coord.Cli.fsproj" />\n'
        '    <ProjectReference Include="../FS.GG.Telemetry.Contracts/FS.GG.Telemetry.Contracts.fsproj" />'))
    assert any("Coord.Cli assembly" in issue for issue in module.inspect(fixture)["issues"])
    host_project.write_text(original_project)

    store_project = fixture / "src/FS.GG.Telemetry.Store/FS.GG.Telemetry.Store.fsproj"
    original_store = store_project.read_text()
    store_project.write_text(original_store.replace(
        '    <ProjectReference Include="../FS.GG.Coord.Core/FS.GG.Coord.Core.fsproj" />', ""))
    assert any("Contracts and Store must each reference Core" in issue for issue in module.inspect(fixture)["issues"])
    store_project.write_text(original_store)
    store_project.write_text(original_store.replace(
        '    <Compile Include="../FS.GG.Coord.Cli/TelemetryStoreApplication.fsi" Link="TelemetryStoreApplication.fsi" />', ""))
    assert any("historical CLI-path source links changed" in issue for issue in module.inspect(fixture)["issues"])
    store_project.write_text(original_store)

    original = allow.read_text()
    allow.write_text(original + "\ntools/net10.0/linux-x64/FS.GG.Coord.Cli.dll\n")
    assert any("allowlist includes Coord.Cli" in issue for issue in module.inspect(fixture)["issues"])
    allow.write_text(original)

    package = fixture / "test.nupkg"
    required = {f"{name}.dll" for name in (module.HOST, module.CORE,
                module.CONTRACTS, module.STORE, module.DASHBOARD)}
    def deps(libraries):
        return json.dumps({
            "runtimeTarget": {"name": ".NETCoreApp,Version=v10.0/linux-x64"},
            "libraries": {name: {} for name in libraries},
            "targets": {".NETCoreApp,Version=v10.0/linux-x64":
                        {name: {} for name in libraries}},
        })
    with zipfile.ZipFile(package, "w") as archive:
        for name in required:
            archive.writestr(f"tools/net10.0/linux-x64/{name}", b"fixture")
        archive.writestr("tools/net10.0/linux-x64/FS.GG.Telemetry.Host.deps.json",
                         deps(["FS.GG.Coord.Core/0.0.0"]))
    assert module.inspect(fixture, package)["issues"] == []
    with zipfile.ZipFile(package, "w") as archive:
        for name in required - {"FS.GG.Coord.Core.dll"}:
            archive.writestr(f"tools/net10.0/linux-x64/{name}", b"fixture")
        archive.writestr("tools/net10.0/linux-x64/FS.GG.Telemetry.Host.deps.json",
                         deps(["FS.GG.Coord.Core/0.0.0"]))
    assert any("runtime package misses" in issue for issue in module.inspect(fixture, package)["issues"])
    with zipfile.ZipFile(package, "w") as archive:
        for name in required:
            archive.writestr(f"tools/net10.0/linux-x64/{name}", b"fixture")
        archive.writestr("tools/net10.0/linux-x64/FS.GG.Telemetry.Host.deps.json",
                         deps(["FS.GG.Coord.Core/0.0.0", "FS.GG.Coord.Cli/0.0.0"]))
    assert any("dependency manifest includes Coord.Cli" in issue for issue in module.inspect(fixture, package)["issues"])

    # An assembly basename in an inert archive path is not a tool runtime member.
    with zipfile.ZipFile(package, "w") as archive:
        for name in required - {"FS.GG.Coord.Core.dll"}:
            archive.writestr(f"tools/net10.0/linux-x64/{name}", b"fixture")
        archive.writestr("content/FS.GG.Coord.Core.dll", b"decoy")
        archive.writestr("tools/net10.0/linux-x64/FS.GG.Telemetry.Host.deps.json",
                         deps(["FS.GG.Coord.Core/0.0.0"]))
    assert any("runtime package misses" in issue for issue in module.inspect(fixture, package)["issues"])

    # A dependency document elsewhere in the archive cannot speak for the tool.
    with zipfile.ZipFile(package, "w") as archive:
        for name in required:
            archive.writestr(f"tools/net10.0/linux-x64/{name}", b"fixture")
        archive.writestr("content/FS.GG.Telemetry.Host.deps.json",
                         deps(["FS.GG.Coord.Core/0.0.0"]))
    assert any("no unique runtime Host .deps.json" in issue for issue in module.inspect(fixture, package)["issues"])

    # Runtime-target entries can name a loaded legacy assembly independently of libraries.
    with zipfile.ZipFile(package, "w") as archive:
        for name in required:
            archive.writestr(f"tools/net10.0/linux-x64/{name}", b"fixture")
        archive.writestr("tools/net10.0/linux-x64/FS.GG.Telemetry.Host.deps.json",
                         json.dumps({"runtimeTarget": {"name": ".NETCoreApp,Version=v10.0/linux-x64"},
                                     "libraries": {"FS.GG.Coord.Core/0.0.0": {}},
                                     "targets": {".NETCoreApp,Version=v10.0/linux-x64":
                                                 {"FS.GG.Coord.Core/0.0.0": {}, "FS.GG.Coord.Cli/0.0.0": {}}}}))
    assert any("dependency runtime targets include Coord.Cli" in issue for issue in module.inspect(fixture, package)["issues"])

    with zipfile.ZipFile(package, "w") as archive:
        for name in required:
            archive.writestr(f"tools/net10.0/linux-x64/{name}", b"fixture")
        archive.writestr("content/FS.GG.Coord.Core.dll", b"extra")
        archive.writestr("tools/net10.0/linux-x64/FS.GG.Telemetry.Host.deps.json",
                         deps(["FS.GG.Coord.Core/0.0.0"]))
    assert any("extra Core assembly path" in issue for issue in module.inspect(fixture, package)["issues"])

    with warnings.catch_warnings():
        warnings.simplefilter("ignore", UserWarning)
        with zipfile.ZipFile(package, "w") as archive:
            for name in required:
                archive.writestr(f"tools/net10.0/linux-x64/{name}", b"fixture")
            archive.writestr("tools/net10.0/linux-x64/FS.GG.Coord.Core.dll", b"shadow")
            archive.writestr("tools/net10.0/linux-x64/FS.GG.Telemetry.Host.deps.json",
                             deps(["FS.GG.Coord.Core/0.0.0"]))
    assert any("duplicate archive members" in issue for issue in module.inspect(fixture, package)["issues"])

    with zipfile.ZipFile(package, "w") as archive:
        for name in required:
            archive.writestr(f"tools/net10.0/linux-x64/{name}", b"fixture")
        archive.writestr("tools/net10.0/linux-x64/FS.GG.Telemetry.Host.deps.json", "{}")
    assert any("lacks libraries, targets or runtimeTarget" in issue for issue in module.inspect(fixture, package)["issues"])

    with zipfile.ZipFile(package, "w") as archive:
        for name in required:
            archive.writestr(f"tools/net10.0/linux-x64/{name}", b"fixture")
        archive.writestr("tools/net10.0/linux-x64/FS.GG.Telemetry.Host.deps.json",
                         deps(["FS.GG.Coord.Core/0.0.0"]).replace("v10.0/linux-x64", "v10.0/win-x64"))
    assert any("runtime target differs" in issue for issue in module.inspect(fixture, package)["issues"])

    original_allow = allow.read_text()
    allow.write_text(original_allow.replace(
        "tools/net10.0/linux-x64/FS.GG.Coord.Core.dll",
        "content/FS.GG.Coord.Core.dll"))
    assert any("allowlist misses runtime members" in issue for issue in module.inspect(fixture)["issues"])
    allow.write_text(original_allow)

print("fsc02 telemetry closure: 23 independent assertions passed")
