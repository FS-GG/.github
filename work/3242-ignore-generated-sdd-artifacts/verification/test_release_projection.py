from pathlib import Path
import subprocess


ROOT = Path(__file__).resolve().parents[3]


def run(*args: str) -> None:
    subprocess.run(args, cwd=ROOT, check=True)


def test_0832_release_source_is_coherent() -> None:
    props = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
    notes = (ROOT / "src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj").read_text(encoding="utf-8")
    registry = (ROOT / "registry/dependencies.yml").read_text(encoding="utf-8")
    assert "<FsggCoherentSetVersion>0.83.2</FsggCoherentSetVersion>" in props
    assert "<PackageReleaseNotes>0.83.2 " in notes
    assert 'version: "0.83.2"' in registry
    assert 'package-version: "0.83.1"' in registry
    run("python3", "scripts/check-engine-freshness.py")
    run("python3", "scripts/check-engine-release-notes.py")


def test_0832_release_projections_are_current() -> None:
    run("scripts/generate-projections", "--check")
    run("scripts/generate-driver-manifest", "--check")
