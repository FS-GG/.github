#!/usr/bin/env python3
"""Hosted public qualification of the retained knowledge adoption R4 (FS-GG/.github#4822).

R4: a Typed SDD console workspace created before project knowledge existed (published SDD.Cli 2.0.3 through
the Workspace.Template 0.13.0 provider descriptor, so no canonical store, capture guide or knowledge CI) and
in ordinary owner use adopts project knowledge on the current published SDD.Cli 2.4.0 / Workspace.Template
0.19.3. The insufficient producer must refuse; adoption must preserve user content and Git history, enter
the normal owner commit, and then meet the full typed knowledge contract, including the 10 MiB boundary and
owner-preserving refusals. Public nuget.org feeds only, an isolated HOME, no token in any child.

Helpers shared with the accepted R3 adapter are loaded from its sealed bytes. `--source-controls` runs pure
causal controls and launches no child. `--run` is the hosted operation.
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import platform
import shutil
import sys
import tempfile
import types
import unittest
import zipfile

R3_SHA = '3269ebd7a8dde638184973f0bee5c3e93db66fcd58de80f639d0f937fcaa160f'  # accepted R3 adapter (#4822, 65a71ad2)
LEGACY_REVISION = '908da309c9a52490cf914c3f2d5ce1eaf1188b2e'   # Templates: the pre-knowledge 0.13.0 console descriptor
LEGACY_DESCRIPTOR = ('FS.GG.Workspace.Template::0.13.0', 'b6ee8c9a3c79c60cbc9f99df970a9bf7beaf8ab6869ee492dee66697800a3cf8')
LEGACY = {
    'legacy-producer': ('FS.GG.SDD.Cli', '2.0.3', 'b950bf4fc46a09554a51b6b31f920830c8811bb6580b2724c500b8317bcfa9d7'),
    'legacy-template': ('FS.GG.Workspace.Template', '0.13.0', 'ab72f74a76d59ad4be6f11f367bbdf1b27f8bdedae7a2e3afecbe0e4b3fac10c'),
}
KNOWLEDGE_FILES = ('.fsgg/knowledge-guide.md', '.fsgg/knowledge/schema.json', '.fsgg/knowledge/records/project-knowledge.json')
# Adoption may change exactly these owner files; every other pre-adoption byte and mode is the owner's.
ADOPTION_CHANGED = ('.config/dotnet-tools.json', '.fsgg/providers.yml', '.gitignore')


def load_r3():
    from hashlib import sha256
    path = Path(__file__).with_name('knowledge-retained-upgrade-public.py').resolve()
    require_local(sha256(path.read_bytes()).hexdigest() == R3_SHA, 'accepted-r3-adapter-drift')
    module = types.ModuleType('sealed_r3_helpers')
    module.__file__ = str(path)
    exec(compile(path.read_bytes(), str(path), 'exec'), module.__dict__)
    return module


def require_local(ok, message):
    if not ok:
        raise ValueError(message)


R = load_r3()
C = R.C
require, sha, snapshot, changed_paths = R.require, R.sha, R.snapshot, R.changed_paths
LIMIT, CI_FILES, PRODUCT = R.LIMIT, R.CI_FILES, R.PRODUCT


def check_legacy_descriptor(path):
    lines = [line.strip() for line in Path(path).read_text().splitlines()]
    require(sha(path) == LEGACY_DESCRIPTOR[1] and lines.count('source: ' + LEGACY_DESCRIPTOR[0]) == 1, 'legacy-descriptor')
    return dict(revision=LEGACY_REVISION, source=LEGACY_DESCRIPTOR[0], sha256=LEGACY_DESCRIPTOR[1])


def check_pre_knowledge(root, provenance):
    """The created workspace is Typed SDD on the insufficient producer and has no knowledge of any kind."""
    root = Path(root)
    effective = {p['key']: p['value'] for p in provenance['effectiveParameters']}
    require(provenance['generator']['version'] == LEGACY['legacy-producer'][1] and effective.get('lifecycle') == 'typed-sdd'
            and provenance['providerName'] == 'console' and provenance['outcome'] == 'providerSucceeded', 'legacy-typed-provenance')
    present = [p for p in KNOWLEDGE_FILES + CI_FILES if (root / p).exists()] + (['.fsgg/knowledge'] if (root / '.fsgg/knowledge').exists() else [])
    require(not present, 'legacy-workspace-already-has-knowledge:' + ','.join(present))
    return dict(lifecycle='typed-sdd', generator=provenance['generator']['version'], knowledgePresent=False)


def check_insufficient(result, before, after):
    """The pre-knowledge producer must refuse the knowledge route as an unknown command and change nothing."""
    require(result.returncode == 1, 'insufficient-producer-exit')
    require(not result.stdout.strip(), 'insufficient-producer-stdout')
    report = json.loads(result.stderr)  # the 2.0.3 CLI writes its blocked report to stderr
    ids = [d['id'] for d in report['diagnostics']]
    require(report['outcome'] == 'blocked' and report['toolVersion'] == LEGACY['legacy-producer'][1] and ids == ['unknownCommand'],
            'insufficient-producer-reason')
    require(before == after, 'insufficient-producer-changed-bytes')
    return dict(exitCode=1, outcome='blocked', diagnostics=ids, toolVersion=report['toolVersion'])


def check_manifest(before, after, old, new):
    """Only the SDD pin moves (old -> new); `dotnet tool update` may add `rollForward: false`, nothing else."""
    require(before['tools']['fs.gg.sdd.cli']['version'] == old and after['tools']['fs.gg.sdd.cli']['version'] == new, 'manifest-pins')
    require({k: v for k, v in before.items() if k != 'tools'} == {k: v for k, v in after.items() if k != 'tools'}, 'manifest-root-properties')
    require(set(before['tools']) == set(after['tools']), 'manifest-tool-set')
    added = {}
    for name, original in before['tools'].items():
        entry = dict(after['tools'][name])
        if name == 'fs.gg.sdd.cli':
            entry['version'] = original['version']
        extra = {k: entry.pop(k) for k in list(entry) if k not in original}
        require(entry == original, 'manifest-entry-changed:' + name)
        require(set(extra) <= {'rollForward'} and all(v is False for v in extra.values()), 'manifest-unexpected-property:' + name)
        if extra:
            added[name] = extra
    return dict(before=old, after=new, sdkAddedProperties=added)


def check_created(before, after, created, payload=None):
    """Exactly `created` paths appear (with `payload` bytes when given); nothing existing changes."""
    require(changed_paths(before, after) == sorted(created) and all(p not in before for p in created), 'unexpected-effect')
    for path, data in (payload or {}).items():
        require(after[path][0] == R.hashlib.sha256(data).hexdigest(), 'payload-mismatch:' + path)
    return sorted(created)


def check_initialize(before, after, gitignore_before, gitignore_after):
    """Knowledge initialization creates the store and guide, appends the ignore block, and touches nothing else."""
    changes = changed_paths(before, after)
    require(set(changes) == set(KNOWLEDGE_FILES) | {'.gitignore'}, 'initialize-effects')
    require(all(p not in before for p in KNOWLEDGE_FILES), 'initialize-overwrote')
    require(gitignore_after.startswith(gitignore_before) and b'!.fsgg/knowledge/records/*.json' in gitignore_after[len(gitignore_before):],
            'initialize-ignore-block')
    return changes


def check_checker_refusal(result, before, after):
    require(result.returncode != 0 and 'Missing .fsgg/knowledge-guide.md' in result.stderr + result.stdout, 'checker-pre-init-reason')
    require(before == after, 'checker-pre-init-changed-bytes')
    return dict(exitCode=result.returncode, reason='missing-knowledge-guide')


def check_final(pre, post, gitignore_pre, gitignore_post):
    """Against the pre-adoption owner tree: only the adoption files may differ or appear, plus canonical records;
    the ignore file keeps its owner prefix; every other owner byte and mode is unchanged."""
    changes = changed_paths(pre, post)
    other = [p for p in changes if p not in ADOPTION_CHANGED and p not in KNOWLEDGE_FILES and p not in CI_FILES
             and not (p.startswith('.fsgg/knowledge/records/') and p.endswith('.json') and p not in pre)
             and not (p.startswith('.fsgg/cache/') and p not in pre)]  # the ignored, untracked optional cache
    require(not other, 'unexpected-changes:' + ','.join(sorted(other)))
    require(all(p in post for p in pre), 'owner-file-removed')
    require(gitignore_post.startswith(gitignore_pre), 'owner-ignore-prefix-lost')
    return dict(changedPaths=changes, preservedFiles=len([p for p in pre if p not in changes]))


def legacy_package(home, key):
    pid, version, _ = LEGACY[key]
    if key == 'legacy-template':
        meta = json.loads((Path(home) / '.templateengine/packages.json').read_text(encoding='utf-8-sig'))
        rows = [r for r in meta['Packages'] if r.get('Details', {}).get('PackageId', '').lower() == pid.lower()]
        require(len(rows) == 1 and rows[0]['Details']['Version'] == version
                and rows[0]['Details']['NuGetSource'] == 'https://api.nuget.org/v3/index.json', 'sole-legacy-template-registration')
        return Path(rows[0]['MountPointUri'])
    name = pid.lower()
    return Path(home) / '.dotnet/tools/.store' / name / version / name / version / f'{name}.{version}.nupkg'


def legacy_identity(home, key):
    path = legacy_package(home, key)
    require(path.is_file() and path.resolve().is_relative_to(Path(home).resolve()), 'package-not-in-isolated-home:' + key)
    observed = sha(path)
    require(observed == LEGACY[key][2], 'package-sha256-mismatch:' + key)
    return dict(package=LEGACY[key][0], version=LEGACY[key][1], file=path.name, sha256=observed, match=True)


def verify_sources(templates, current, legacy):
    templates, current, legacy = R.verify_sources(templates, current) + (Path(legacy).resolve(),)
    check_legacy_descriptor(legacy / 'providers/console.providers.yml')
    return templates, current, legacy


def qualify(templates, current, legacy, work):
    work = Path(work)
    require(work.is_absolute() and not work.exists(), 'fresh-output-no-replay')
    templates, current, legacy = verify_sources(templates, current, legacy)
    dotnet = shutil.which('dotnet')
    require(dotnet and shutil.which('git'), 'missing-host-tools')
    work.mkdir(mode=0o700)
    evidence = work / 'evidence'
    evidence.mkdir()
    home, env = R.environment(work, dotnet)
    runner = C.Runner(evidence, env, total_seconds=1500)
    report = dict(schema='fsgg.wizard.retained-adoption-public-acceptance/1', route='R4', state='started', readOnly=True,
                  host=dict(system=platform.platform(), python=platform.python_version()),
                  expected=dict({k: list(v) for k, v in LEGACY.items()}, **{k: list(R.PACKAGES[k]) for k in ('producer', 'template', 'coord')}),
                  descriptors=dict(legacy=[LEGACY_REVISION, *LEGACY_DESCRIPTOR], current=list(R.DESCRIPTORS['current'])), phases={})

    def save():
        (work / 'report.json').write_text(json.dumps(report, indent=2) + '\n')
    save()
    phases = report['phases']
    checker_argv = lambda root: [sys.executable, str(Path(root) / CI_FILES[1]), '--root', str(root)]
    try:
        report['host']['dotnet'] = runner.call(['dotnet', '--version']).stdout.strip()
        # Phase A: a Typed SDD workspace created before knowledge existed, then used by its owner.
        runner.call(['dotnet', 'tool', 'install', '--global', LEGACY['legacy-producer'][0], '--version', LEGACY['legacy-producer'][1]])
        sdd = shutil.which('fsgg-sdd', path=env['PATH'])
        require(sdd and Path(sdd).resolve().is_relative_to(home.resolve()), 'selected-tool-not-isolated')
        require(runner.call([sdd, '--version']).stdout.strip() == LEGACY['legacy-producer'][1], 'legacy-producer-version')
        report['packages'] = {'legacy-producer': legacy_identity(home, 'legacy-producer')}
        case = work / 'retained'
        root = case / 'subject'
        (root / '.fsgg').mkdir(parents=True)
        shutil.copyfile(legacy / 'providers/console.providers.yml', root / '.fsgg/providers.yml')
        created = json.loads(runner.call(R.scaffold_argv(sdd, root), cwd=case, timeout=180).stdout)
        require(created['outcome'] == 'succeeded' and created['toolVersion'] == LEGACY['legacy-producer'][1], 'legacy-scaffold')
        report['packages']['legacy-template'] = legacy_identity(home, 'legacy-template')
        legacy_state = check_pre_knowledge(root, C.read_json(root / '.fsgg/scaffold-provenance.json'))
        legacy_manifest = R.tool_pins(root)
        require(not runner.git(root, 'rev-list', '--all'), 'unexpected-background-commit')
        initial = R.commit(runner, root, 'Initial workspace on SDD 2.0.3 / Workspace.Template 0.13.0')
        owner_files = R.apply_owner_work(root)
        owner_commit = R.commit(runner, root, 'Owner work before project knowledge')
        pre, gitignore_pre = snapshot(root), (root / '.gitignore').read_bytes()
        insufficient = check_insufficient(runner.call([sdd, 'knowledge', 'initialize', '--root', root], cwd=root, success=False), pre, snapshot(root))
        phases['legacy'] = dict(scaffold=created['outcome'], state=legacy_state, initialCommit=initial, ownerFiles=owner_files,
                                ownerCommit=owner_commit, files=len(pre), insufficientProducer=insufficient); save()

        # Phase B: adopt knowledge on SDD 2.4.0 / Workspace.Template 0.19.3.
        runner.call(['dotnet', 'new', 'install', R.DESCRIPTORS['current'][1]], cwd=case)
        report['packages']['template'] = R.identity(home, 'template', R.template_package(home, 'template'))
        with zipfile.ZipFile(R.template_package(home, 'template')) as archive:
            overlay_payload = {n: archive.read('content/templates/fs-gg-project-knowledge/' + n) for n in CI_FILES}
        collision = case / 'collision'
        runner.call(['git', 'clone', '-q', root, collision])
        (collision / CI_FILES[0]).write_text('name: owner-authored-knowledge\non: workflow_dispatch\njobs: {}\n')
        before = snapshot(collision)
        overlay_collision = R.check_overlay_refusal(runner.call(['dotnet', 'new', 'fs-gg-project-knowledge', '--lifecycle', 'typed-sdd', '--output', '.'],
                                                                cwd=collision, success=False), before, snapshot(collision))
        before = snapshot(root)
        runner.call(['dotnet', 'new', 'fs-gg-project-knowledge', '--lifecycle', 'typed-sdd', '--output', '.'], cwd=root)
        overlay = check_created(before, snapshot(root), list(CI_FILES), overlay_payload)
        before = snapshot(root)
        checker_refusal = check_checker_refusal(runner.call(checker_argv(root), cwd=root, success=False), before, snapshot(root))
        runner.call(['dotnet', 'tool', 'update', '--global', R.PACKAGES['producer'][0], '--version', R.PACKAGES['producer'][1]])
        require(runner.call([sdd, '--version']).stdout.strip() == R.PACKAGES['producer'][1], 'global-producer-not-2.4.0')
        report['packages']['producer'] = R.identity(home, 'producer', R.tool_package(home, 'producer'))
        runner.call(['dotnet', 'tool', 'update', 'fs.gg.sdd.cli', '--version', R.PACKAGES['producer'][1]], cwd=root)
        manifest = check_manifest(legacy_manifest, R.tool_pins(root), LEGACY['legacy-producer'][1], R.PACKAGES['producer'][1])
        shutil.copyfile(current / 'providers/console.providers.yml', root / '.fsgg/providers.yml')
        descriptor = R.check_descriptor(root / '.fsgg/providers.yml', 'current')
        before, gitignore_before = snapshot(root), (root / '.gitignore').read_bytes()
        initialized = json.loads(runner.call([sdd, 'knowledge', 'initialize', '--root', root], cwd=root).stdout)
        require(initialized['Limit'] == LIMIT and 0 < initialized['Bytes'] <= LIMIT, 'initialize-size')
        initialize = check_initialize(before, snapshot(root), gitignore_before, (root / '.gitignore').read_bytes())
        once = snapshot(root)
        runner.call([sdd, 'knowledge', 'initialize', '--root', root], cwd=root)
        require(snapshot(root) == once, 'initialize-not-idempotent')
        runner.call(['dotnet', 'tool', 'restore', '--no-cache'], cwd=root)
        report['packages']['restored-producer'] = R.identity(home, 'producer', R.restored_package(home, 'producer'))
        report['packages']['coord'] = R.identity(home, 'coord', R.restored_package(home, 'coord'))
        checker = R.checker(runner, root, R.PACKAGES['producer'][1])
        doctor = R.check_doctor(json.loads(runner.call([sdd, 'doctor', '--root', root, '--json'], cwd=root).stdout), missing=[], drift=R.EDITED_SKILLS)
        before = snapshot(root)
        steady = R.check_upgrade(json.loads(runner.call([sdd, 'upgrade', '--root', root, '--yes', '--json'], cwd=root).stdout), [])
        require(snapshot(root) == before, 'upgrade-clobbered-owner-content')
        R.check_owner_content(root)
        cache = root / '.fsgg/cache/adoption.json'
        cache.parent.mkdir(parents=True, exist_ok=True)
        cache.write_text('{}')
        adoption_commit = R.commit(runner, root, 'Adopt project knowledge on SDD 2.4.0 / Workspace.Template 0.19.3')
        tracked = set(runner.git(root, 'ls-files').splitlines())
        require(set(KNOWLEDGE_FILES) | set(CI_FILES) <= tracked and '.fsgg/cache/adoption.json' not in tracked, 'adoption-commit-inclusion')
        diff = sorted(runner.git(root, 'diff', '--name-only', owner_commit, adoption_commit).splitlines())
        require(diff == sorted(set(ADOPTION_CHANGED) | set(KNOWLEDGE_FILES) | set(CI_FILES)), 'adoption-commit-diff')
        phases['adoption'] = dict(overlayCollision=overlay_collision, overlay=overlay, checkerBeforeInitialize=checker_refusal,
                                  manifest=manifest, descriptor=descriptor, initialized=initialized, initializeEffects=initialize,
                                  checker=checker, doctor=doctor, steadyUpgrade=steady, adoptionCommit=adoption_commit,
                                  commitDiff=diff); save()

        # Phase C: the full typed knowledge contract on the adopted store.
        scratch = case / 'probe'
        with C.frozen_probes(templates, runner) as (candidate, public):
            probe = candidate.probe([sdd], root, scratch)
            shared = C.post_private_export(runner, sdd, root, scratch)
            before_boundary = snapshot(root)
            boundary = public.ordinary_ci_limits(root)
        require(snapshot(root) == before_boundary, 'checker-boundary-residue')
        require(probe['findings'] == 4 and probe['historyVersions'] == 2 and probe['restoreAuthoredNoClobber'] is True
                and probe['explicitPrivateStoreExcludedFromShared'] is True and probe['size']['Limit'] == LIMIT, 'adopted-probe')
        require(boundary['exactLimit']['bytes'] == LIMIT and boundary['oneByteOverExit'] != 0, 'checker-boundary')
        after_check = R.checker(runner, root, R.PACKAGES['producer'][1])
        extra = case / 'probe-extra'
        extra.mkdir()
        base = json.loads(runner.call([sdd, 'knowledge', 'get', '--root', root, '--id', 'architecture-decision']).stdout)
        older = json.loads(runner.call([sdd, 'knowledge', 'get-version', '--root', root, '--id', 'architecture-decision',
                                        '--commit', probe['findingsCommit']]).stdout)
        require(older['Revision'] != base['Revision'], 'stale-setup')
        before = snapshot(root)
        stale = R.write_record(extra / 'stale.json', base['Record'], Summary='Divergent stale writer on the adopted store.')
        R.refusal(runner.call([sdd, 'knowledge', 'capture', '--root', root, '--record', stale, '--expected', older['Revision']], success=False), 'stale')
        require(snapshot(root) == before, 'stale-writer-changed-bytes')
        budget = R.write_budget(runner, sdd, root, extra, base['Record'])
        require(snapshot(root) == before, 'write-budget-residue')
        no_clobber = R.restore_no_clobber(runner, sdd, root, extra, 'diagnostic-lesson')
        final_commit = R.commit(runner, root, 'Owner clarification on the adopted store')
        clone = case / 'clone'
        runner.call(['git', 'clone', '-q', root, clone])
        require(not (clone / '.fsgg/cache').exists(), 'clone-has-cache')
        search = lambda r: runner.call([sdd, 'knowledge', 'search', '--root', r]).stdout
        require(search(clone) == search(root), 'clone-search')
        history = lambda r: runner.call([sdd, 'knowledge', 'history', '--root', r, '--id', 'architecture-decision']).stdout
        require(history(clone) == history(root) and len(json.loads(history(clone))) == 2, 'clone-history')
        R.check_owner_content(root)
        for name in ('CLAUDE.md', 'AGENTS.md'):
            require(snapshot(root)[name] == pre[name], 'agent-guidance-clobbered:' + name)
        final = check_final(pre, snapshot(root), gitignore_pre, (root / '.gitignore').read_bytes())
        phases['knowledge'] = dict(probe=probe, allShared=shared, checkerBoundary=boundary, checkerAfter=after_check,
                                   staleWriterRefused=True, writeBudget=budget, restoreNoClobber=no_clobber,
                                   finalCommit=final_commit, cloneHistoryVersions=2, final=final)
        report.update(state='passed', commands=runner.sequence); save()
        return report
    except BaseException as error:
        report.update(state='failed', failureType=type(error).__name__, failure=str(error), commands=runner.sequence); save()
        raise


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--source-controls', action='store_true')
    parser.add_argument('--run', action='store_true')
    parser.add_argument('--templates', type=Path, help=f'Templates checkout at {R.PROBE_REVISION}')
    parser.add_argument('--current', type=Path, help=f'Templates checkout at {R.CURRENT_REVISION}')
    parser.add_argument('--legacy', type=Path, help=f'Templates checkout at {LEGACY_REVISION}')
    parser.add_argument('--work', type=Path)
    args = parser.parse_args()
    if args.source_controls:
        suite = unittest.defaultTestLoader.loadTestsFromTestCase(Controls)
        return 0 if unittest.TextTestRunner(verbosity=1).run(suite).wasSuccessful() else 1
    require(args.run and args.templates and args.current and args.legacy and args.work, 'operation-required')
    report = qualify(args.templates, args.current, args.legacy, args.work)
    print(json.dumps({k: report[k] for k in ('state', 'commands')}))
    return 0


class Controls(unittest.TestCase):
    """Pure causal controls: each invariant is made to fail by the defect it exists to catch."""

    def refuses(self, reason, function, *args, **kwargs):
        with self.assertRaises(ValueError) as caught:
            function(*args, **kwargs)
        self.assertEqual(str(caught.exception), reason)

    def tmp(self):
        root = Path(tempfile.mkdtemp(dir=os.environ.get('TMPDIR')))
        self.addCleanup(shutil.rmtree, root, True)
        return root

    def provenance(self, version='2.0.3', lifecycle='typed-sdd'):
        return {'generator': {'version': version}, 'providerName': 'console', 'outcome': 'providerSucceeded',
                'effectiveParameters': [{'key': 'lifecycle', 'value': lifecycle}, {'key': 'productName', 'value': PRODUCT}]}

    def test_legacy_workspace_must_be_typed_and_free_of_knowledge(self):
        root = self.tmp()
        self.assertFalse(check_pre_knowledge(root, self.provenance())['knowledgePresent'])
        self.refuses('legacy-typed-provenance', check_pre_knowledge, root, self.provenance(version='2.1.0'))
        self.refuses('legacy-typed-provenance', check_pre_knowledge, root, self.provenance(lifecycle='sdd'))
        (root / '.fsgg/knowledge').mkdir(parents=True)
        self.refuses('legacy-workspace-already-has-knowledge:.fsgg/knowledge', check_pre_knowledge, root, self.provenance())
        root = self.tmp(); (root / 'scripts').mkdir(); (root / CI_FILES[1]).write_text('x')
        self.refuses('legacy-workspace-already-has-knowledge:' + CI_FILES[1], check_pre_knowledge, root, self.provenance())

    def test_insufficient_producer_must_refuse_as_unknown_command(self):
        def result(code=1, outcome='blocked', ids=('unknownCommand',), version='2.0.3'):
            return types.SimpleNamespace(returncode=code, stderr=json.dumps(dict(outcome=outcome, toolVersion=version,
                                         diagnostics=[{'id': i} for i in ids])), stdout='')
        self.assertEqual(check_insufficient(result(), {'a': 1}, {'a': 1})['diagnostics'], ['unknownCommand'])
        self.refuses('insufficient-producer-exit', check_insufficient, result(code=0), {}, {})
        self.refuses('insufficient-producer-reason', check_insufficient, result(ids=('invalidArgs',)), {}, {})
        self.refuses('insufficient-producer-reason', check_insufficient, result(version='2.4.0'), {}, {})
        self.refuses('insufficient-producer-reason', check_insufficient, result(outcome='succeeded'), {}, {})
        self.refuses('insufficient-producer-changed-bytes', check_insufficient, result(), {'a': 1}, {'a': 2})
        self.refuses('insufficient-producer-stdout', check_insufficient, types.SimpleNamespace(returncode=1, stdout='{}', stderr='{}'), {}, {})

    def manifest(self, version, **extra):
        return dict(version=1, isRoot=True, tools={'fs.gg.coord.cli': dict(version='0.94.0', commands=['fsgg-coord-engine']),
                                                   'fs.gg.sdd.cli': dict(version=version, commands=['fsgg-sdd'], **extra)})

    def test_manifest_moves_only_the_sdd_pin(self):
        self.assertEqual(check_manifest(self.manifest('2.0.3'), self.manifest('2.4.0', rollForward=False), '2.0.3', '2.4.0')['after'], '2.4.0')
        self.refuses('manifest-pins', check_manifest, self.manifest('2.0.3'), self.manifest('2.3.0'), '2.0.3', '2.4.0')
        coord = self.manifest('2.4.0'); coord['tools']['fs.gg.coord.cli']['version'] = '0.112.0'
        self.refuses('manifest-entry-changed:fs.gg.coord.cli', check_manifest, self.manifest('2.0.3'), coord, '2.0.3', '2.4.0')
        self.refuses('manifest-unexpected-property:fs.gg.sdd.cli', check_manifest, self.manifest('2.0.3'), self.manifest('2.4.0', rollForward=True), '2.0.3', '2.4.0')

    def test_overlay_creates_only_its_two_files_with_package_bytes(self):
        before = {'a': ['x', 0o644]}
        payload = {CI_FILES[0]: b'w', CI_FILES[1]: b'c'}
        after = dict(before, **{n: [R.hashlib.sha256(d).hexdigest(), 0o644] for n, d in payload.items()})
        self.assertEqual(check_created(before, after, list(CI_FILES), payload), sorted(CI_FILES))
        self.refuses('unexpected-effect', check_created, before, dict(after, a=['y', 0o644]), list(CI_FILES), payload)
        self.refuses('payload-mismatch:' + CI_FILES[1], check_created, before, after, list(CI_FILES), dict(payload, **{CI_FILES[1]: b'other'}))
        self.refuses('unexpected-effect', check_created, dict(before, **{CI_FILES[0]: ['old', 0o644]}), after, list(CI_FILES), payload)

    def test_initialize_creates_store_and_appends_ignore_only(self):
        before = {'.gitignore': ['g', 0o644], 'CLAUDE.md': ['c', 0o644]}
        after = dict(before, **{'.gitignore': ['g2', 0o644]}, **{p: ['k', 0o644] for p in KNOWLEDGE_FILES})
        block = b'\n!.fsgg/knowledge/records/*.json\n'
        self.assertIn('.gitignore', check_initialize(before, after, b'owner\n', b'owner\n' + block))
        self.refuses('initialize-effects', check_initialize, before, dict(after, **{'CLAUDE.md': ['c2', 0o644]}), b'owner\n', b'owner\n' + block)
        self.refuses('initialize-ignore-block', check_initialize, before, after, b'owner\n', b'rewritten\n' + block)
        self.refuses('initialize-ignore-block', check_initialize, before, after, b'owner\n', b'owner\n')
        self.refuses('initialize-overwrote', check_initialize, dict(before, **{KNOWLEDGE_FILES[0]: ['old', 0o644]}), after, b'owner\n', b'owner\n' + block)

    def test_checker_refuses_before_initialization_for_the_missing_store(self):
        ok = types.SimpleNamespace(returncode=1, stdout='', stderr='project-knowledge: Missing .fsgg/knowledge-guide.md; initialize knowledge')
        self.assertEqual(check_checker_refusal(ok, {}, {})['reason'], 'missing-knowledge-guide')
        self.refuses('checker-pre-init-reason', check_checker_refusal, types.SimpleNamespace(returncode=2, stdout='', stderr='Traceback'), {}, {})
        self.refuses('checker-pre-init-reason', check_checker_refusal, types.SimpleNamespace(returncode=0, stdout='', stderr=ok.stderr), {}, {})
        self.refuses('checker-pre-init-changed-bytes', check_checker_refusal, ok, {'a': 1}, {'a': 2})

    def test_final_tree_admits_only_adoption_changes_and_new_records(self):
        pre = {'CLAUDE.md': ['c', 0o644], '.gitignore': ['g', 0o644], '.config/dotnet-tools.json': ['m', 0o644], 'docs/owner-note.md': ['n', 0o644]}
        post = dict(pre, **{'.gitignore': ['g2', 0o644], '.config/dotnet-tools.json': ['m2', 0o644], CI_FILES[0]: ['w', 0o644],
                            KNOWLEDGE_FILES[1]: ['s', 0o644], '.fsgg/knowledge/records/bug-cause-fix.json': ['b', 0o644]})
        self.assertEqual(check_final(pre, post, b'owner\n', b'owner\nblock\n')['preservedFiles'], 2)
        self.refuses('unexpected-changes:CLAUDE.md', check_final, pre, dict(post, **{'CLAUDE.md': ['x', 0o644]}), b'owner\n', b'owner\n')
        self.refuses('unexpected-changes:docs/owner-note.md', check_final, pre, dict(post, **{'docs/owner-note.md': ['n', 0o755]}), b'owner\n', b'owner\n')
        self.refuses('owner-ignore-prefix-lost', check_final, pre, post, b'owner\n', b'replaced\n')
        self.refuses('unexpected-changes:src/x.fs', check_final, pre, dict(post, **{'src/x.fs': ['y', 0o644]}), b'o', b'o')
        self.assertIn('.fsgg/cache/adoption.json', check_final(pre, dict(post, **{'.fsgg/cache/adoption.json': ['z', 0o644]}), b'o', b'o')['changedPaths'])
        self.refuses('unexpected-changes:.fsgg/cache/owner.json', check_final, dict(pre, **{'.fsgg/cache/owner.json': ['a', 0o644]}),
                     dict(post, **{'.fsgg/cache/owner.json': ['b', 0o644]}), b'o', b'o')
        owned = dict(pre, **{'.fsgg/knowledge/records/owner.json': ['o', 0o644]})
        self.refuses('unexpected-changes:.fsgg/knowledge/records/owner.json', check_final, owned,
                     dict(post, **{'.fsgg/knowledge/records/owner.json': ['o2', 0o644]}), b'owner\n', b'owner\n')

    def test_inputs_are_sealed_and_a_run_is_never_replayed(self):
        self.assertEqual(sha(Path(__file__).with_name('knowledge-retained-upgrade-public.py')), R3_SHA)
        creation = (Path(__file__).with_name('knowledge-creation.py')).read_text()
        self.assertIn(f"INSUFFICIENT_ARCHIVE_SHA256 = '{LEGACY['legacy-producer'][2]}'", creation)  # the 2.0.3 archive bound in #4708
        root = self.tmp(); (root / 'providers').mkdir(); (root / 'providers/console.providers.yml').write_text('    source: FS.GG.Workspace.Template::0.13.0\n')
        self.refuses('legacy-descriptor', check_legacy_descriptor, root / 'providers/console.providers.yml')
        for key, (pid, version, digest) in LEGACY.items():
            self.assertRegex(digest, '^[0-9a-f]{64}$')
        self.refuses('fresh-output-no-replay', qualify, root, root, root, root)


if __name__ == '__main__':
    sys.exit(main())
