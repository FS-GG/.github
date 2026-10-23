#!/usr/bin/env python3
"""Hosted public qualification of the retained knowledge upgrade R3 (FS-GG/.github#4822).

R3: an existing knowledge-bearing Typed SDD console workspace created on an older published producer and
template (SDD.Cli 2.2.0 / Workspace.Template 0.19.0) and in ordinary owner use is upgraded in place to the
current published SDD.Cli 2.4.0 / Workspace.Template 0.19.3. The upgrade must preserve user content,
canonical records and their Git history, keep the 10 MiB canonical boundary, and preserve owner bytes
through each refusal. Public nuget.org feeds only, an isolated HOME, no token in any child.

`--source-controls` runs pure causal controls and launches no child. `--run` is the hosted operation.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import stat
import sys
import tempfile
import types
import unittest
import zipfile

CONSOLE_SHA = 'afc9de40013bc71d430c91bb215a4938ef4ca9bd5e97e389ba53af3007204873'
PROBE_REVISION = 'f5fbd06ed9e6df40f9037c7acddf9eca3c6a14fc'   # frozen probes and the 0.19.0 console descriptor
CURRENT_REVISION = '24f3c30790b401d4bc435a1b1c07c07ecfd30d76'  # current Templates main: the 0.19.3 console descriptor
DESCRIPTORS = {
    'old': (PROBE_REVISION, 'FS.GG.Workspace.Template::0.19.0', '3911afe2d54876f735074c2dc4947a59b1ab6e5f367f40bea7231ea0e606329f'),
    'current': (CURRENT_REVISION, 'FS.GG.Workspace.Template::0.19.3', '4f6dba6a9f3923e11b23b1a090c3cf014da2581a2dd3746de2369e1121e0e38c'),
}
# Literal published nupkg bytes (SHA-256). Version text alone establishes none of these identities.
PACKAGES = {
    'old-producer': ('FS.GG.SDD.Cli', '2.2.0', '46b4b172eac65df00db43f813bd3d3fbafac5c6376da62a6d3c08fb05750cf28'),
    'producer': ('FS.GG.SDD.Cli', '2.4.0', '1df2e74faebd388f90b813ed9d7497f4247f977718469c9247b37111d791921f'),
    'old-template': ('FS.GG.Workspace.Template', '0.19.0', '187c995806bb2f22db67c8a9a67c251f46f339bf6350b8051035935d308b497f'),
    'template': ('FS.GG.Workspace.Template', '0.19.3', '9594ec204a26f238a06e016d1cfe1e796a07fda23c16589eceb19366cd4a72a2'),
    'coord': ('FS.GG.Coord.Cli', '0.94.0', 'e68a2134242c1e59611a41f98aa5b7d26a545fd6411a0de01af610f4862cc332'),
}
PRODUCT = 'RetainedConsole'
CI_FILES = ('.github/workflows/project-knowledge.yml', 'scripts/check-project-knowledge.py')
LIMIT = 10485760
PROBE_FILES = ('candidate.py', 'receiver.py', 'public21.py', 'findings.json')
# Owner work done on the old tuple. Seeded guidance/skills are edited in place; product source is edited;
# new owner files are added. Every byte must survive the upgrade.
OWNER_APPENDS = {
    'CLAUDE.md': '\nOwner rule: this line was authored before the upgrade and must survive it.\n',
    'AGENTS.md': '\nOwner rule: this line was authored before the upgrade and must survive it.\n',
    # Only the Claude copy: the two mirrored roots now differ, which doctor must report as drift on both.
    '.claude/skills/fs-gg-sdd-lifecycle/SKILL.md': '\nOwner-authored lifecycle note.\n',
    '.fsgg/early-stage-guidance.md': '\nOwner-authored early-stage note.\n',
}
OWNER_PREPEND = ('src/RetainedConsole/Program.fs', '// Owner-authored product edit before the upgrade.\n')
OWNER_NEW = {
    'docs/owner-note.md': '# Owner design note\n\nAuthored on the old tuple; the upgrade must not touch it.\n',
    '.github/workflows/owner-ci.yml': 'name: owner-ci\non: workflow_dispatch\njobs: {}\n',
}
EDITED_SKILLS = ('.agents/skills/fs-gg-sdd-lifecycle/SKILL.md', '.claude/skills/fs-gg-sdd-lifecycle/SKILL.md')
LOST_SEED = '.claude/skills/fs-gg-sdd-plan/SKILL.md'   # a seeded artifact the owner lost; upgrade re-seeds only it
UPGRADE_PATHS = {'.config/dotnet-tools.json', '.fsgg/providers.yml'}
# The only canonical records the post-upgrade captures may change (an update) or add (a new finding).
POST_UPGRADE_RECORDS = {'.fsgg/knowledge/records/architecture-decision.json', '.fsgg/knowledge/records/retained-upgrade-lesson.json'}


def sha(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as f:
        for part in iter(lambda: f.read(1024 * 1024), b''):
            h.update(part)
    return h.hexdigest()


def require(ok, message):
    if not ok:
        raise ValueError(message)


def load_console():
    path = Path(__file__).with_name('knowledge-console-public.py').resolve()
    require(sha(path) == CONSOLE_SHA, 'delivered-console-helper-drift')
    module = types.ModuleType('sealed_console_ports')
    module.__file__ = str(path)
    exec(compile(path.read_bytes(), str(path), 'exec'), module.__dict__)
    return module


C = load_console()


def snapshot(root):
    """Every regular file outside .git with its digest and permission bits. Links refuse."""
    result = {}
    for p in sorted(Path(root).rglob('*')):
        parts = p.relative_to(root).parts
        if '.git' in parts:
            continue
        info = p.lstat()
        require(not stat.S_ISLNK(info.st_mode), 'subject-symlink')
        if stat.S_ISREG(info.st_mode):
            result['/'.join(parts)] = [sha(p), stat.S_IMODE(info.st_mode)]
    return result


def changed_paths(before, after):
    return sorted(k for k in set(before) | set(after) if before.get(k) != after.get(k))


def scaffold_argv(sdd, root):
    return [sdd, 'scaffold', '--root', root, '--provider', 'console', '--no-update', '--json',
            '--param', 'productName=' + PRODUCT, '--param', 'lifecycle=typed-sdd']


def check_descriptor(path, key):
    revision, source, digest = DESCRIPTORS[key]
    require(sha(path) == digest, 'descriptor-sha256:' + key)
    lines = [line.strip() for line in Path(path).read_text().splitlines()]
    require(lines.count('source: ' + source) == 1, 'descriptor-source:' + key)
    return dict(revision=revision, source=source, sha256=digest)


def tool_pins(root):
    value = C.read_json(Path(root) / '.config/dotnet-tools.json')
    require(value['version'] == 1 and value['isRoot'] is True, 'tool-manifest-root')
    require(set(value['tools']) == {'fs.gg.sdd.cli', 'fs.gg.coord.cli'}, 'tool-set')
    require('fsgg-sdd' in value['tools']['fs.gg.sdd.cli']['commands'], 'sdd-command')
    return value


def check_manifest_upgrade(before, after):
    """Only the SDD pin moves; every other value is unchanged. The SDK's `dotnet tool update` may add
    `rollForward: false` to an entry; nothing else may appear, disappear or change."""
    require(before['tools']['fs.gg.sdd.cli']['version'] == PACKAGES['old-producer'][1], 'manifest-old-pin')
    require(after['tools']['fs.gg.sdd.cli']['version'] == PACKAGES['producer'][1], 'manifest-new-pin')
    require({k: v for k, v in before.items() if k != 'tools'} == {k: v for k, v in after.items() if k != 'tools'}, 'manifest-root-properties')
    require(set(before['tools']) == set(after['tools']), 'manifest-tool-set')
    added = {}
    for name, old in before['tools'].items():
        new = dict(after['tools'][name])
        if name == 'fs.gg.sdd.cli':
            new['version'] = old['version']
        extra = {k: new.pop(k) for k in list(new) if k not in old}
        require(new == old, 'manifest-entry-changed:' + name)
        require(set(extra) <= {'rollForward'} and all(v is False for v in extra.values()), 'manifest-unexpected-property:' + name)
        if extra:
            added[name] = extra
    return dict(before=before['tools']['fs.gg.sdd.cli']['version'], after=after['tools']['fs.gg.sdd.cli']['version'],
                coord=after['tools']['fs.gg.coord.cli']['version'], sdkAddedProperties=added)


def apply_owner_work(root):
    root = Path(root)
    for relative, text in OWNER_APPENDS.items():
        require((root / relative).is_file(), 'owner-target-missing:' + relative)
        with (root / relative).open('a') as out:
            out.write(text)
    path = root / OWNER_PREPEND[0]
    path.write_text(OWNER_PREPEND[1] + path.read_text())
    for relative, text in OWNER_NEW.items():
        require(not (root / relative).exists(), 'owner-new-exists:' + relative)
        (root / relative).parent.mkdir(parents=True, exist_ok=True)
        (root / relative).write_text(text)
    return sorted(list(OWNER_APPENDS) + [OWNER_PREPEND[0]] + list(OWNER_NEW))


def check_owner_content(root):
    root = Path(root)
    for relative, text in OWNER_APPENDS.items():
        require((root / relative).read_text().endswith(text), 'owner-append-lost:' + relative)
    require((root / OWNER_PREPEND[0]).read_text().startswith(OWNER_PREPEND[1]), 'owner-product-edit-lost')
    for relative, text in OWNER_NEW.items():
        require((root / relative).read_text() == text, 'owner-file-lost:' + relative)
    return True


def check_doctor(report, *, missing, drift):
    d = report['doctor']
    require(d['installedCliVersion'] == PACKAGES['producer'][1] and d['providerName'] == 'console', 'doctor-identity')
    require(sorted(d['missingArtifactPaths']) == sorted(missing), 'doctor-missing-paths')
    require(sorted(d['skillDriftPaths']) == sorted(drift), 'doctor-owner-drift-not-visible')
    steps = {s['stepId']: s['outcome'] for s in d['previewSteps']}
    require(steps == dict(cliSelfUpdate='noTarget', templateRePin='noTarget',
                          artifactReSeed='wouldApply' if missing else 'noTarget'), 'doctor-steps')
    return dict(outcome=report['outcome'], isCoherent=d['isCoherent'], missing=d['missingArtifactPaths'],
                skillDrift=d['skillDriftPaths'], steps=steps)


def check_upgrade(report, reseeded):
    u = report['upgrade']
    changed = [(c['path'], c['operation']) for c in report['changedArtifacts']]
    require(changed == [(p, 'create') for p in reseeded], 'upgrade-changed-artifacts')
    require(u['failedStepIds'] == [] and u['appliedStepIds'] == (['artifactReSeed'] if reseeded else []), 'upgrade-steps')
    return dict(outcome=report['outcome'], applied=u['appliedStepIds'], changed=[p for p, _ in changed], residualDrift=u['residualDrift'])


def check_overlay_refusal(result, before, after):
    """Re-applying the 0.19.3 overlay without --force must refuse and leave every owner byte unchanged."""
    require(result.returncode == 73 and 'Overwrite' in result.stdout + result.stderr, 'overlay-not-refused')
    require(before == after, 'overlay-refusal-changed-bytes')
    return dict(exitCode=result.returncode, ownerBytesUnchanged=True)


def check_final(pre, post, reseeded, records_before):
    """Final tree vs the pre-upgrade owner tree: only the declared upgrade paths, the re-seeded path and
    the knowledge captured after the upgrade may differ; every other byte and mode is the owner's."""
    changes = changed_paths(pre, post)
    other = sorted(set(changes) - UPGRADE_PATHS - POST_UPGRADE_RECORDS - set(reseeded))
    require(not other, 'unexpected-changes:' + ','.join(other))
    for relative in UPGRADE_PATHS:
        require(relative in post, 'upgrade-path-missing:' + relative)
    removed = [p for p in records_before if p not in post]
    require(not removed, 'knowledge-record-removed')
    return dict(changedPaths=changes, preservedFiles=len(pre) - len([p for p in changes if p in pre]))


def tool_package(home, key):
    pid, version, _ = PACKAGES[key]
    name = pid.lower()
    return Path(home) / '.dotnet/tools/.store' / name / version / name / version / f'{name}.{version}.nupkg'


def restored_package(home, key):
    pid, version, _ = PACKAGES[key]
    name = pid.lower()
    return Path(home) / '.nuget/packages' / name / version / f'{name}.{version}.nupkg'


def template_package(home, key):
    meta = json.loads((Path(home) / '.templateengine/packages.json').read_text(encoding='utf-8-sig'))
    rows = [r for r in meta['Packages'] if r.get('Details', {}).get('PackageId', '').lower() == PACKAGES[key][0].lower()]
    require(len(rows) == 1 and rows[0]['Details']['Version'] == PACKAGES[key][1]
            and rows[0]['Details']['NuGetSource'] == 'https://api.nuget.org/v3/index.json', 'sole-template-registration:' + key)
    return Path(rows[0]['MountPointUri'])


def verify_identity(key, observed, name):
    pid, version, expected = PACKAGES[key]
    require(observed == expected, 'package-sha256-mismatch:' + key)
    return dict(package=pid, version=version, file=name, sha256=observed, expectedSha256=expected, match=True)


def identity(home, key, path):
    require(path.is_file() and path.resolve().is_relative_to(Path(home).resolve()), 'package-not-in-isolated-home:' + key)
    return verify_identity(key, sha(path), path.name)


def environment(work, dotnet):
    home = work / 'home'
    for relative in ('tmp', '.nuget/packages'):
        (home / relative).mkdir(parents=True)
    dotnet_dir = Path(dotnet).resolve().parent
    return home, {
        'HOME': str(home), 'DOTNET_CLI_HOME': str(home), 'TMPDIR': str(home / 'tmp'),
        'NUGET_PACKAGES': str(home / '.nuget/packages'),
        'DOTNET_ROOT': os.environ.get('DOTNET_ROOT') or str(dotnet_dir),
        'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'DOTNET_NOLOGO': '1', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE': '1',
        'MSBUILDDISABLENODEREUSE': '1', 'TERM': 'dumb', 'NO_COLOR': '1', 'LANG': 'C.UTF-8', 'LC_ALL': 'C.UTF-8',
        'GIT_CONFIG_NOSYSTEM': '1', 'GIT_CONFIG_GLOBAL': '/dev/null',
        'PATH': os.pathsep.join([str(home / '.dotnet/tools'), str(dotnet_dir), '/usr/local/bin', '/usr/bin', '/bin']),
    }  # No GITHUB_TOKEN/GH_TOKEN/FSGG_PACKAGES_TOKEN: public anonymous feeds only.


def verify_sources(templates, current):
    templates, current = Path(templates).resolve(), Path(current).resolve()
    for name in PROBE_FILES:
        digest = C.SOURCE_HASHES['tests/knowledge-receiver/' + name]
        require(sha(templates / 'tests/knowledge-receiver' / name) == digest, 'probe-source-drift:' + name)
    check_descriptor(templates / 'providers/console.providers.yml', 'old')
    check_descriptor(current / 'providers/console.providers.yml', 'current')
    return templates, current


def commit(runner, root, message):
    runner.git(root, 'add', '.')
    runner.git(root, '-c', 'user.name=Retained owner', '-c', 'user.email=owner@example.invalid', 'commit', '-qm', message)
    return runner.git(root, 'rev-parse', 'HEAD')


def checker(runner, root, version):
    value = json.loads(runner.call([sys.executable, str(Path(root) / CI_FILES[1]), '--root', str(root)], cwd=root).stdout)
    require(value['outcome'] == 'passed' and value['pinnedVersion'] == version and value['sourceCandidateOverride'] is False
            and value['limit'] == LIMIT, 'ordinary-pinned-checker:' + version)
    return value


def knowledge_view(runner, sdd, root, ids):
    """The retrieval surface compared byte for byte across the upgrade."""
    view = dict(search=runner.call([sdd, 'knowledge', 'search', '--root', root]).stdout)
    for record in ids:
        view['get:' + record] = runner.call([sdd, 'knowledge', 'get', '--root', root, '--id', record]).stdout
        view['history:' + record] = runner.call([sdd, 'knowledge', 'history', '--root', root, '--id', record]).stdout
    return view


REFUSALS = {
    'stale': 'Knowledge revision conflict',
    'excluded': 'Unsupported finding kind; copied source/log records are excluded.',
    'restore': 'Restore would overwrite authored knowledge',
    'budget': 'Knowledge byte budget exceeded',
}


def refusal(result, kind):
    """A knowledge refusal must be the producer's blocked diagnostic for this reason, not any non-zero exit."""
    require(result.returncode == 1, 'refusal-exit:' + kind)
    try:
        value = json.loads(result.stderr)
    except ValueError:
        value = {}
    require(value.get('outcome') == 'blocked' and value.get('diagnostic') == 'knowledge.invalid'
            and REFUSALS[kind] in value.get('message', ''), 'refusal-reason:' + kind)
    return value['message']


def write_record(path, base, **changes):
    record = dict(base)
    record.update(changes)
    Path(path).write_text(json.dumps(record))
    return path


def write_budget(runner, sdd, root, scratch, base):
    """Write-path boundary on the retained store: with the store already at 10,485,760 bytes a capture is
    refused (owner bytes unchanged); with exactly the capture's growth of room left, the same capture is
    accepted and lands on 10,485,760 bytes. The one-byte boundary itself is the checker's (ordinary_ci_limits).
    The schema padding and the probe record are removed afterwards."""
    store = Path(root) / '.fsgg/knowledge'
    schema = store / 'schema.json'
    original = schema.read_bytes()
    record = write_record(scratch / 'budget-probe.json', base, Id='budget-probe', Summary='Synthetic write-path budget probe.')
    total = lambda: sum(p.stat().st_size for p in store.rglob('*') if p.is_file())
    try:
        schema.write_bytes(original + b' ' * (LIMIT - total()))
        before = snapshot(root)
        refused = runner.call([sdd, 'knowledge', 'capture', '--root', root, '--record', record], success=False)
        message = refusal(refused, 'budget')
        require(snapshot(root) == before, 'write-over-limit-not-refused')
        growth = int(message.rsplit('growth=', 1)[1].rstrip('.'))
        others = total() - len(schema.read_bytes())
        schema.write_bytes(original + b' ' * (LIMIT - growth - others - len(original)))
        require(total() + growth == LIMIT, 'write-exact-setup')
        accepted = json.loads(runner.call([sdd, 'knowledge', 'capture', '--root', root, '--record', record]).stdout)
        require(accepted['size']['Bytes'] == LIMIT and accepted['size']['Limit'] == LIMIT, 'write-exact-limit-not-accepted')
    finally:
        schema.write_bytes(original)
        (store / 'records/budget-probe.json').unlink(missing_ok=True)
    return dict(overLimitRefused=True, growth=growth, exactLimitAccepted=accepted['size'])


def restore_no_clobber(runner, sdd, root, scratch, record_id):
    """Export, then an owner edit; restoring the older export must refuse and preserve the owner's bytes."""
    archive = scratch / 'retained-export.json'
    runner.call([sdd, 'knowledge', 'export', '--root', root, '--ids', record_id, '--archive', archive])
    current = json.loads(runner.call([sdd, 'knowledge', 'get', '--root', root, '--id', record_id]).stdout)
    edit = write_record(scratch / 'owner-edit.json', current['Record'], Summary=current['Record']['Summary'] + ' Owner clarification after the upgrade.')
    runner.call([sdd, 'knowledge', 'capture', '--root', root, '--record', edit, '--expected', current['Revision']])
    before = snapshot(root)
    refusal(runner.call([sdd, 'knowledge', 'restore', '--root', root, '--archive', archive], success=False), 'restore')
    require(snapshot(root) == before, 'restore-clobbered-owner-edit')
    return dict(restoreRefused=True, ownerEditPreserved=True)


def qualify(templates, current, work):
    work = Path(work)
    require(work.is_absolute() and not work.exists(), 'fresh-output-no-replay')
    templates, current = verify_sources(templates, current)
    dotnet = shutil.which('dotnet')
    require(dotnet and shutil.which('git'), 'missing-host-tools')
    work.mkdir(mode=0o700)
    evidence = work / 'evidence'
    evidence.mkdir()
    home, env = environment(work, dotnet)
    runner = C.Runner(evidence, env, total_seconds=1500)
    report = dict(schema='fsgg.wizard.retained-upgrade-public-acceptance/1', route='R3', state='started', readOnly=True,
                  host=dict(system=platform.platform(), python=platform.python_version()),
                  expected={k: list(v) for k, v in PACKAGES.items()}, descriptors={k: list(v) for k, v in DESCRIPTORS.items()},
                  phases={})

    def save():
        (work / 'report.json').write_text(json.dumps(report, indent=2) + '\n')
    save()
    phases = report['phases']
    try:
        report['host']['dotnet'] = runner.call(['dotnet', '--version']).stdout.strip()
        # Phase A: create on the old tuple and use it as an owner would.
        runner.call(['dotnet', 'tool', 'install', '--global', PACKAGES['old-producer'][0], '--version', PACKAGES['old-producer'][1]])
        sdd = shutil.which('fsgg-sdd', path=env['PATH'])
        require(sdd and Path(sdd).resolve().is_relative_to(home.resolve()), 'selected-tool-not-isolated')
        require(runner.call([sdd, '--version']).stdout.strip() == PACKAGES['old-producer'][1], 'old-producer-version')
        report['packages'] = {'old-producer': identity(home, 'old-producer', tool_package(home, 'old-producer'))}
        case = work / 'retained'
        root = case / 'subject'
        (root / '.fsgg').mkdir(parents=True)
        shutil.copyfile(templates / 'providers/console.providers.yml', root / '.fsgg/providers.yml')
        created = json.loads(runner.call(scaffold_argv(sdd, root), cwd=case, timeout=180).stdout)
        require(created['outcome'] == 'succeeded', 'old-tuple-scaffold')
        report['packages']['old-template'] = identity(home, 'old-template', template_package(home, 'old-template'))
        old_manifest = tool_pins(root)
        with zipfile.ZipFile(template_package(home, 'old-template')) as archive:
            old_ci = {n: archive.read('content/templates/fs-gg-console/' + n) for n in CI_FILES}
        for name in CI_FILES:
            require((root / name).read_bytes() == old_ci[name], 'old-owner-ci-payload')
        require(not runner.git(root, 'rev-list', '--all'), 'unexpected-background-commit')
        initial = commit(runner, root, 'Initial workspace on SDD 2.2.0 / Workspace.Template 0.19.0')
        runner.call(['dotnet', 'tool', 'restore', '--no-cache'], cwd=root)
        report['packages']['coord'] = identity(home, 'coord', restored_package(home, 'coord'))
        old_check = checker(runner, root, PACKAGES['old-producer'][1])
        with C.frozen_probes(templates, runner) as (candidate, public):
            probe = candidate.probe([sdd], root, case / 'probe-old')
        require(probe['findings'] == 4 and probe['historyVersions'] == 2, 'old-tuple-probe')
        owner_files = apply_owner_work(root)
        owner_commit = commit(runner, root, 'Owner work before the upgrade')
        records = sorted(p.relative_to(root).as_posix() for p in (root / '.fsgg/knowledge/records').glob('*.json'))
        ids = sorted(row['Record']['Id'] for row in json.loads(runner.call([sdd, 'knowledge', 'search', '--root', root]).stdout))
        old_view = knowledge_view(runner, sdd, root, ids)
        pre = snapshot(root)
        phases['old'] = dict(scaffold=created['outcome'], initialCommit=initial, checker=old_check, probe=probe,
                             ownerFiles=owner_files, ownerCommit=owner_commit, recordIds=ids, files=len(pre)); save()

        # Phase B: upgrade in place to SDD 2.4.0 / Workspace.Template 0.19.3.
        runner.call(['dotnet', 'tool', 'update', '--global', PACKAGES['producer'][0], '--version', PACKAGES['producer'][1]])
        require(runner.call([sdd, '--version']).stdout.strip() == PACKAGES['producer'][1], 'global-producer-not-2.4.0')
        report['packages']['producer'] = identity(home, 'producer', tool_package(home, 'producer'))
        runner.call(['dotnet', 'tool', 'update', 'fs.gg.sdd.cli', '--version', PACKAGES['producer'][1]], cwd=root)
        manifest = check_manifest_upgrade(old_manifest, tool_pins(root))
        shutil.copyfile(current / 'providers/console.providers.yml', root / '.fsgg/providers.yml')
        descriptor = check_descriptor(root / '.fsgg/providers.yml', 'current')
        runner.call(['dotnet', 'new', 'install', DESCRIPTORS['current'][1]], cwd=case)
        report['packages']['template'] = identity(home, 'template', template_package(home, 'template'))
        with zipfile.ZipFile(template_package(home, 'template')) as archive:
            for name in CI_FILES:
                for member in ('fs-gg-console/', 'fs-gg-project-knowledge/'):
                    require(archive.read('content/templates/' + member + name) == (root / name).read_bytes(), 'retained-ci-not-current')
        doctor = check_doctor(json.loads(runner.call([sdd, 'doctor', '--root', root, '--json'], cwd=root).stdout), missing=[], drift=EDITED_SKILLS)
        before_upgrade = snapshot(root)
        steady = check_upgrade(json.loads(runner.call([sdd, 'upgrade', '--root', root, '--yes', '--json'], cwd=root).stdout), [])
        require(snapshot(root) == before_upgrade, 'upgrade-clobbered-owner-content')
        lost = (root / LOST_SEED).read_bytes()
        (root / LOST_SEED).unlink()
        doctor_lost = check_doctor(json.loads(runner.call([sdd, 'doctor', '--root', root, '--json'], cwd=root).stdout), missing=[LOST_SEED], drift=EDITED_SKILLS + (LOST_SEED,))
        reseed = check_upgrade(json.loads(runner.call([sdd, 'upgrade', '--root', root, '--yes', '--json'], cwd=root).stdout), [LOST_SEED])
        require(changed_paths(before_upgrade, snapshot(root)) == ([] if (root / LOST_SEED).read_bytes() == lost else [LOST_SEED]), 'reseed-touched-other-paths')
        reseed['equalsPredecessorSeed'] = (root / LOST_SEED).read_bytes() == lost
        check_owner_content(root)
        before_overlay = snapshot(root)
        overlay = check_overlay_refusal(runner.call(['dotnet', 'new', 'fs-gg-project-knowledge', '--lifecycle', 'typed-sdd', '--output', '.'],
                                                    cwd=root, success=False), before_overlay, snapshot(root))
        upgrade_commit = commit(runner, root, 'Upgrade to SDD 2.4.0 / Workspace.Template 0.19.3')
        diff = sorted(runner.git(root, 'diff', '--name-only', owner_commit, upgrade_commit).splitlines())
        require(set(diff) <= UPGRADE_PATHS | {LOST_SEED} and UPGRADE_PATHS <= set(diff), 'upgrade-commit-diff')
        phases['upgrade'] = dict(manifest=manifest, descriptor=descriptor, doctor=doctor, steadyUpgrade=steady,
                                 lostSeedDoctor=doctor_lost, reseed=reseed, overlayRefusal=overlay,
                                 upgradeCommit=upgrade_commit, commitDiff=diff); save()

        # Phase C: the current producer on the retained store and its history.
        runner.call(['dotnet', 'tool', 'restore', '--no-cache'], cwd=root)
        report['packages']['restored-producer'] = identity(home, 'producer', restored_package(home, 'producer'))
        new_check = checker(runner, root, PACKAGES['producer'][1])
        new_view = knowledge_view(runner, sdd, root, ids)
        require(new_view == old_view, 'retrieval-changed-across-upgrade')
        old_version = json.loads(runner.call([sdd, 'knowledge', 'get-version', '--root', root, '--id', 'architecture-decision',
                                              '--commit', probe['findingsCommit']]).stdout)
        current_ad = json.loads(runner.call([sdd, 'knowledge', 'get', '--root', root, '--id', 'architecture-decision']).stdout)
        scratch = case / 'probe-new'
        scratch.mkdir()
        updated = write_record(scratch / 'ad.json', current_ad['Record'], Summary=current_ad['Record']['Summary'] + ' Still holds after the upgrade to SDD 2.4.0.')
        runner.call([sdd, 'knowledge', 'capture', '--root', root, '--record', updated, '--expected', current_ad['Revision']])
        lesson = write_record(scratch / 'lesson.json', current_ad['Record'], Id='retained-upgrade-lesson', Title='retained-upgrade-lesson',
                              Kind='diagnostic', Summary='Upgrading the local SDD pin kept every record and its history readable.',
                              Relations=[{'Kind': 'related', 'Target': 'architecture-decision'}])
        runner.call([sdd, 'knowledge', 'capture', '--root', root, '--record', lesson])
        later = commit(runner, root, 'Capture findings after the upgrade')
        tracked = set(runner.git(root, 'ls-files').splitlines())
        require('.fsgg/knowledge/records/retained-upgrade-lesson.json' in tracked, 'later-capture-not-committed')
        history = json.loads(runner.call([sdd, 'knowledge', 'history', '--root', root, '--id', 'architecture-decision']).stdout)
        require(len(history) == 3, 'history-across-upgrade')
        before_refusal = snapshot(root)
        stale = write_record(scratch / 'stale.json', current_ad['Record'], Summary='Divergent stale writer after the upgrade.')
        refusal(runner.call([sdd, 'knowledge', 'capture', '--root', root, '--record', stale, '--expected', current_ad['Revision']], success=False), 'stale')
        require(snapshot(root) == before_refusal, 'stale-writer-changed-bytes')
        fixture = json.loads((templates / 'tests/knowledge-receiver/findings.json').read_text())
        for excluded in fixture['excludedInputs']:
            invalid = write_record(scratch / (excluded['id'] + '.json'), current_ad['Record'], Id=excluded['id'], Kind=excluded['kind'], Summary=excluded['finding'])
            refusal(runner.call([sdd, 'knowledge', 'capture', '--root', root, '--record', invalid], success=False), 'excluded')
            require(snapshot(root) == before_refusal, 'excluded-input-changed-bytes:' + excluded['id'])
        budget = write_budget(runner, sdd, root, scratch, current_ad['Record'])
        require(snapshot(root) == before_refusal, 'write-budget-residue')
        with C.frozen_probes(templates, runner) as (candidate, public):
            boundary = public.ordinary_ci_limits(root)
        require(boundary['exactLimit']['bytes'] == LIMIT and boundary['oneByteOverExit'] != 0, 'checker-boundary')
        require(snapshot(root) == before_refusal, 'checker-boundary-residue')
        after_check = checker(runner, root, PACKAGES['producer'][1])
        no_clobber = restore_no_clobber(runner, sdd, root, scratch, 'retained-upgrade-lesson')
        final_commit = commit(runner, root, 'Owner clarification after the upgrade')
        clone = case / 'clone'
        runner.call(['git', 'clone', '-q', root, clone])
        require(not (clone / '.fsgg/cache').exists(), 'clone-has-cache')
        clone_history = json.loads(runner.call([sdd, 'knowledge', 'history', '--root', clone, '--id', 'architecture-decision']).stdout)
        require(clone_history == history, 'clone-history')
        require(runner.call([sdd, 'knowledge', 'search', '--root', clone]).stdout == runner.call([sdd, 'knowledge', 'search', '--root', root]).stdout, 'clone-search')
        clone_old = json.loads(runner.call([sdd, 'knowledge', 'get-version', '--root', clone, '--id', 'architecture-decision',
                                            '--commit', probe['findingsCommit']]).stdout)
        require(clone_old == old_version and old_version['Record']['Id'] == 'architecture-decision', 'clone-pre-upgrade-version')
        check_owner_content(root)
        final = check_final(pre, snapshot(root), [LOST_SEED] if not reseed['equalsPredecessorSeed'] else [], records)
        phases['current'] = dict(checker=new_check, retrievalUnchanged=True, preUpgradeVersion=old_version['Revision'],
                                 laterCommit=later, historyVersions=len(history), staleWriterRefused=True,
                                 excludedInputsRefused=[e['id'] for e in fixture['excludedInputs']], writeBudget=budget,
                                 checkerBoundary=boundary, checkerAfter=after_check, restoreNoClobber=no_clobber,
                                 finalCommit=final_commit, cloneHistoryVersions=len(clone_history), final=final)
        report.update(state='passed', commands=runner.sequence); save()
        return report
    except BaseException as error:
        report.update(state='failed', failureType=type(error).__name__, failure=str(error), commands=runner.sequence); save()
        raise


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--source-controls', action='store_true')
    parser.add_argument('--run', action='store_true')
    parser.add_argument('--templates', type=Path, help=f'Templates checkout at {PROBE_REVISION}')
    parser.add_argument('--current', type=Path, help=f'Templates checkout at {CURRENT_REVISION}')
    parser.add_argument('--work', type=Path)
    args = parser.parse_args()
    if args.source_controls:
        suite = unittest.defaultTestLoader.loadTestsFromTestCase(Controls)
        return 0 if unittest.TextTestRunner(verbosity=1).run(suite).wasSuccessful() else 1
    require(args.run and args.templates and args.current and args.work, 'operation-required')
    report = qualify(args.templates, args.current, args.work)
    print(json.dumps({k: report[k] for k in ('state', 'commands')}))
    return 0


class Controls(unittest.TestCase):
    """Pure causal controls: each invariant is made to fail by the defect it exists to catch."""

    def tree(self):
        root = Path(tempfile.mkdtemp(dir=os.environ.get('TMPDIR')))
        self.addCleanup(shutil.rmtree, root, True)
        for relative in list(OWNER_APPENDS) + [OWNER_PREPEND[0]]:
            (root / relative).parent.mkdir(parents=True, exist_ok=True)
            (root / relative).write_text('generated\n')
        return root

    def refuses(self, reason, function, *args, **kwargs):
        with self.assertRaises(ValueError) as caught:
            function(*args, **kwargs)
        self.assertEqual(str(caught.exception), reason)

    def manifest(self, version='2.2.0', **extra):
        sdd = dict(version=version, commands=['fsgg-sdd'], **extra)
        return dict(version=1, isRoot=True, tools={'fs.gg.coord.cli': dict(version='0.94.0', commands=['fsgg-coord-engine']), 'fs.gg.sdd.cli': sdd})

    def test_manifest_upgrade_moves_only_the_sdd_pin(self):
        result = check_manifest_upgrade(self.manifest(), self.manifest('2.4.0', rollForward=False))
        self.assertEqual((result['before'], result['after'], result['coord']), ('2.2.0', '2.4.0', '0.94.0'))
        self.refuses('manifest-new-pin', check_manifest_upgrade, self.manifest(), self.manifest('2.3.0'))
        self.refuses('manifest-old-pin', check_manifest_upgrade, self.manifest('2.1.0'), self.manifest('2.4.0'))
        coord = self.manifest('2.4.0'); coord['tools']['fs.gg.coord.cli']['version'] = '0.95.0'
        self.refuses('manifest-entry-changed:fs.gg.coord.cli', check_manifest_upgrade, self.manifest(), coord)
        self.refuses('manifest-unexpected-property:fs.gg.sdd.cli', check_manifest_upgrade, self.manifest(), self.manifest('2.4.0', rollForward=True))
        self.refuses('manifest-unexpected-property:fs.gg.sdd.cli', check_manifest_upgrade, self.manifest(), self.manifest('2.4.0', extra=1))
        dropped = self.manifest('2.4.0'); dropped['tools']['fs.gg.sdd.cli']['commands'] = []
        self.refuses('manifest-entry-changed:fs.gg.sdd.cli', check_manifest_upgrade, self.manifest(), dropped)
        self.refuses('manifest-root-properties', check_manifest_upgrade, self.manifest(), dict(self.manifest('2.4.0'), isRoot=False))

    def test_owner_work_is_detected_when_lost(self):
        root = self.tree()
        self.assertIn('docs/owner-note.md', apply_owner_work(root))
        self.assertTrue(check_owner_content(root))
        (root / 'CLAUDE.md').write_text('generated\n')
        self.refuses('owner-append-lost:CLAUDE.md', check_owner_content, root)
        root = self.tree(); apply_owner_work(root); (root / OWNER_PREPEND[0]).write_text('reverted\n')
        self.refuses('owner-product-edit-lost', check_owner_content, root)
        root = self.tree(); apply_owner_work(root); (root / 'docs/owner-note.md').unlink()
        with self.assertRaises(FileNotFoundError): check_owner_content(root)
        root = self.tree(); apply_owner_work(root)
        self.refuses('owner-new-exists:docs/owner-note.md', apply_owner_work, root)

    def doctor(self, missing=(), drift=EDITED_SKILLS, version='2.4.0', reseed=None):
        return {'outcome': 'noChange', 'doctor': {'installedCliVersion': version, 'providerName': 'console', 'isCoherent': not missing,
                'missingArtifactPaths': list(missing), 'skillDriftPaths': list(drift), 'previewSteps': [
                    {'stepId': 'cliSelfUpdate', 'outcome': 'noTarget'}, {'stepId': 'templateRePin', 'outcome': 'noTarget'},
                    {'stepId': 'artifactReSeed', 'outcome': reseed or ('wouldApply' if missing else 'noTarget')}]}}

    def test_doctor_keeps_owner_drift_visible_and_names_only_the_lost_seed(self):
        self.assertEqual(check_doctor(self.doctor(), missing=[], drift=EDITED_SKILLS)['steps']['artifactReSeed'], 'noTarget')
        self.assertEqual(check_doctor(self.doctor([LOST_SEED]), missing=[LOST_SEED], drift=EDITED_SKILLS)['missing'], [LOST_SEED])
        self.refuses('doctor-owner-drift-not-visible', check_doctor, self.doctor(drift=()), missing=[], drift=EDITED_SKILLS)
        self.refuses('doctor-missing-paths', check_doctor, self.doctor(['other']), missing=[LOST_SEED], drift=EDITED_SKILLS)
        self.refuses('doctor-identity', check_doctor, self.doctor(version='2.2.0'), missing=[], drift=EDITED_SKILLS)
        self.refuses('doctor-steps', check_doctor, self.doctor(reseed='wouldApply'), missing=[], drift=EDITED_SKILLS)

    def test_upgrade_may_create_only_the_lost_seed(self):
        steady = {'outcome': 'noChange', 'changedArtifacts': [], 'upgrade': {'appliedStepIds': [], 'failedStepIds': [], 'residualDrift': True}}
        self.assertEqual(check_upgrade(steady, [])['changed'], [])
        reseed = {'outcome': 'succeeded', 'changedArtifacts': [{'path': LOST_SEED, 'operation': 'create'}],
                  'upgrade': {'appliedStepIds': ['artifactReSeed'], 'failedStepIds': [], 'residualDrift': True}}
        self.assertEqual(check_upgrade(reseed, [LOST_SEED])['changed'], [LOST_SEED])
        overwrite = dict(reseed, changedArtifacts=[{'path': EDITED_SKILLS[1], 'operation': 'update'}])
        self.refuses('upgrade-changed-artifacts', check_upgrade, overwrite, [LOST_SEED])
        self.refuses('upgrade-changed-artifacts', check_upgrade, reseed, [])
        self.refuses('upgrade-steps', check_upgrade, dict(steady, upgrade=dict(steady['upgrade'], failedStepIds=['artifactReSeed'])), [])

    def test_overlay_must_refuse_without_changing_bytes(self):
        ok = types.SimpleNamespace(returncode=73, stdout='Overwrite   scripts/check-project-knowledge.py', stderr='')
        self.assertEqual(check_overlay_refusal(ok, {'a': 1}, {'a': 1})['exitCode'], 73)
        self.refuses('overlay-not-refused', check_overlay_refusal, types.SimpleNamespace(returncode=0, stdout='', stderr=''), {}, {})
        self.refuses('overlay-refusal-changed-bytes', check_overlay_refusal, ok, {'a': 1}, {'a': 2})

    def test_final_tree_admits_only_declared_changes(self):
        pre = {'CLAUDE.md': ['a', 0o644], '.config/dotnet-tools.json': ['m', 0o644], '.fsgg/providers.yml': ['d', 0o644],
               '.fsgg/knowledge/records/architecture-decision.json': ['r', 0o644], 'docs/owner-note.md': ['n', 0o644]}
        post = dict(pre, **{'.config/dotnet-tools.json': ['m2', 0o644], '.fsgg/providers.yml': ['d2', 0o644],
                            '.fsgg/knowledge/records/architecture-decision.json': ['r2', 0o644],
                            '.fsgg/knowledge/records/retained-upgrade-lesson.json': ['l', 0o644]})
        records = ['.fsgg/knowledge/records/architecture-decision.json']
        self.assertEqual(check_final(pre, post, [], records)['preservedFiles'], 2)
        self.refuses('unexpected-changes:CLAUDE.md', check_final, pre, dict(post, **{'CLAUDE.md': ['b', 0o644]}), [], records)
        self.refuses('unexpected-changes:docs/owner-note.md', check_final, pre, dict(post, **{'docs/owner-note.md': ['n', 0o755]}), [], records)
        gone = dict(post); del gone['docs/owner-note.md']
        self.refuses('unexpected-changes:docs/owner-note.md', check_final, pre, gone, [], records)
        dropped = dict(post); del dropped['.fsgg/knowledge/records/architecture-decision.json']
        self.refuses('knowledge-record-removed', check_final, pre, dropped, [], records)
        self.refuses('unexpected-changes:' + LOST_SEED, check_final, pre, dict(post, **{LOST_SEED: ['s', 0o644]}), [], records)
        self.assertIn(LOST_SEED, check_final(pre, dict(post, **{LOST_SEED: ['s', 0o644]}), [LOST_SEED], records)['changedPaths'])
        other_record = '.fsgg/knowledge/records/bug-cause-fix.json'
        self.refuses('unexpected-changes:' + other_record, check_final, dict(pre, **{other_record: ['b', 0o644]}),
                     dict(post, **{other_record: ['b2', 0o644]}), [], records)

    def test_descriptors_and_identities_are_literal(self):
        root = Path(tempfile.mkdtemp(dir=os.environ.get('TMPDIR'))); self.addCleanup(shutil.rmtree, root, True)
        path = root / 'providers.yml'
        path.write_text('    source: FS.GG.Workspace.Template::0.19.3\n')
        self.refuses('descriptor-sha256:current', check_descriptor, path, 'current')
        for key, (pid, version, digest) in PACKAGES.items():
            self.assertRegex(digest, '^[0-9a-f]{64}$'); self.assertTrue(verify_identity(key, digest, 'x')['match'])
            self.refuses('package-sha256-mismatch:' + key, verify_identity, key, '0' * 64, 'x')
        self.assertEqual(DESCRIPTORS['old'][2], C.SOURCE_HASHES['providers/console.providers.yml'])
        self.assertEqual(PACKAGES['old-producer'][2], C.PACKAGES['producer'][3])
        self.assertEqual(PACKAGES['old-template'][2], C.PACKAGES['workspace'][3])
        self.assertEqual(PACKAGES['coord'][2], C.PACKAGES['coord'][3])

    def test_refusals_must_carry_their_own_reason(self):
        def result(code, message, outcome='blocked'):
            return types.SimpleNamespace(returncode=code, stderr=json.dumps(dict(diagnostic='knowledge.invalid', outcome=outcome, message=message)))
        self.assertIn('revision conflict', refusal(result(1, 'Knowledge revision conflict: retrieve and reconcile.'), 'stale'))
        self.assertIn('budget', refusal(result(1, 'Knowledge byte budget exceeded: 10486598 > 10485760; growth=838.'), 'budget'))
        self.refuses('refusal-reason:stale', refusal, result(1, 'Expected unique --option value pairs.'), 'stale')
        self.refuses('refusal-reason:restore', refusal, result(1, 'Unsupported finding kind; copied source/log records are excluded.'), 'restore')
        self.refuses('refusal-reason:excluded', refusal, types.SimpleNamespace(returncode=1, stderr='Unhandled exception'), 'excluded')
        self.refuses('refusal-reason:budget', refusal, result(1, 'Knowledge byte budget exceeded', outcome='succeeded'), 'budget')
        self.refuses('refusal-exit:stale', refusal, result(134, 'Knowledge revision conflict'), 'stale')

    def test_snapshot_tracks_modes_and_refuses_links(self):
        root = Path(tempfile.mkdtemp(dir=os.environ.get('TMPDIR'))); self.addCleanup(shutil.rmtree, root, True)
        (root / 'a').write_text('x'); before = snapshot(root)
        (root / 'a').chmod(0o755); self.assertEqual(changed_paths(before, snapshot(root)), ['a'])
        (root / 'l').symlink_to(root / 'a')
        self.refuses('subject-symlink', snapshot, root)

    def test_inputs_are_sealed_and_a_run_is_never_replayed(self):
        self.assertEqual(sha(Path(__file__).with_name('knowledge-console-public.py')), CONSOLE_SHA)
        root = Path(tempfile.mkdtemp(dir=os.environ.get('TMPDIR'))); self.addCleanup(shutil.rmtree, root, True)
        (root / 'tests/knowledge-receiver').mkdir(parents=True)
        for name in PROBE_FILES: (root / 'tests/knowledge-receiver' / name).write_text('drift')
        self.refuses('probe-source-drift:candidate.py', verify_sources, root, root)
        self.refuses('fresh-output-no-replay', qualify, root, root, root)


if __name__ == '__main__':
    sys.exit(main())
